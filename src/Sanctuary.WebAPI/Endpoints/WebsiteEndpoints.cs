using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MiniValidation;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game;
using Sanctuary.Packet.Common;
using Sanctuary.WebAPI.Models;
using Sanctuary.WebAPI.Options;
using Sanctuary.WebAPI.Website;

using BC = BCrypt.Net.BCrypt;

namespace Sanctuary.WebAPI.Endpoints;

public static class WebsiteEndpoints
{
    private static ILogger _logger = null!;

    public static void MapWebsiteEndpoints(this WebApplication app)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

        _logger = loggerFactory.CreateLogger(nameof(WebsiteEndpoints));

        var group = app.MapGroup("/website");

        group.MapPost("/login", LoginAsync);
        group.MapPost("/register", RegisterAsync);
        group.MapPost("/signup", SignupAsync);
        group.MapPost("/logout", LogoutAsync);

        group.MapGet("/names", GetNames);
        group.MapGet("/names/check", CheckNameAsync);
        group.MapGet("/username/check", CheckUsernameAsync);

        group.MapGet("/characters/search", SearchCharactersAsync);
        group.MapGet("/characters/{id}/profile", GetCharacterProfileAsync);
        group.MapPost("/account/optin", SetEmailOptInAsync).RequireAuthorization();
        group.MapGet("/characters/{id}/portrait", GetPortrait);
        group.MapGet("/characters/{id}/headshot", GetHeadshot);

        group.MapGet("/account", GetAccountAsync).RequireAuthorization();
        group.MapPost("/characters", CreateCharacterAsync).RequireAuthorization();
        group.MapPost("/characters/{id}/play", PlayAsync).RequireAuthorization();
        group.MapPost("/customize", CustomizeAsync).RequireAuthorization();
    }

    public sealed record CreateCharacterRequest(int Preset, string FirstName, string LastNamePrefix, string LastNameSuffix);

    private static async Task<IResult> LoginAsync(
        LoginRequestModel request,
        HttpContext httpContext,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!MiniValidator.TryValidate(request, out var errors))
            return Results.ValidationProblem(errors);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var dbUser = await dbContext.Users.FirstOrDefaultAsync(x => x.Username == request.Username, cancellationToken);

        if (dbUser is null || !BC.Verify(request.Password, dbUser.Password))
        {
            _logger.LogWarning("Website login failed for username: {Username}", request.Username);

            return Results.Json(new { error = "invalidLogin" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (IsLocked(dbUser))
            return Results.Json(new { error = "locked" }, statusCode: StatusCodes.Status403Forbidden);

        await SignInAsync(httpContext, dbUser);

        return Results.Ok(new { username = dbUser.Username });
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequestModel request,
        HttpContext httpContext,
        IOptions<WebAPIOptions> webAPIOptions,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!MiniValidator.TryValidate(request, out var errors))
            return Results.ValidationProblem(errors);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await dbContext.Users.AnyAsync(x => x.Username == request.Username, cancellationToken))
            return Results.Json(new { error = "usernameTaken" }, statusCode: StatusCodes.Status409Conflict);

        var dbUser = new DbUser
        {
            Username = request.Username,
            Password = BC.HashPassword(request.Password, BC.GenerateSalt()),
            IsMember = webAPIOptions.Value.MemberByDefault ?? true,
        };

        await dbContext.Users.AddAsync(dbUser, cancellationToken);

        if (await dbContext.SaveChangesAsync(cancellationToken) <= 0)
        {
            _logger.LogError("Website registration failed to add user: {Username}", request.Username);

            return Results.InternalServerError();
        }

        await SignInAsync(httpContext, dbUser);

        return Results.Ok(new { username = dbUser.Username });
    }

    private static async Task<IResult> SignupAsync(
        SignupRequestModel request,
        HttpContext httpContext,
        CharacterNames names,
        IResourceManager resourceManager,
        IOptions<WebAPIOptions> webAPIOptions,
        IOptionsSnapshot<CharacterCreationSettings> settings,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!MiniValidator.TryValidate(request, out var errors))
            return Results.ValidationProblem(errors);

        if (!request.AcceptTerms)
            return Results.Json(new { error = "termsRequired" }, statusCode: StatusCodes.Status400BadRequest);

        CharacterData? characterData = null;

        if (request.Character is not null)
        {
            if (!TryBuildCharacterData(request.Character.Preset, request.Character.FirstName, request.Character.LastNamePrefix, request.Character.LastNameSuffix, names, resourceManager, out characterData, out var characterError))
                return characterError;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await dbContext.Users.AnyAsync(x => x.Username == request.Username, cancellationToken))
            return Results.Json(new { error = "usernameTaken" }, statusCode: StatusCodes.Status409Conflict);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var dbUser = new DbUser
        {
            Username = request.Username,
            Password = BC.HashPassword(request.Password, BC.GenerateSalt()),
            IsMember = webAPIOptions.Value.MemberByDefault ?? true,
            Email = request.Email.Trim(),
            EmailOptIn = request.EmailOptIn,
            Country = request.Country.Trim(),
            Age = request.Age,
            TermsAccepted = DateTimeOffset.UtcNow
        };

        await dbContext.Users.AddAsync(dbUser, cancellationToken);

        if (await dbContext.SaveChangesAsync(cancellationToken) <= 0)
        {
            _logger.LogError("Website signup failed to add user: {Username}", request.Username);

            return Results.InternalServerError();
        }

        ulong? characterId = null;

        if (characterData is not null)
        {
            var result = CharacterCreator.Create(resourceManager, dbContext, settings.Value, dbUser.Id, characterData, _logger, out var createdId);

            if (result != CharacterCreateResult.Success)
            {
                await transaction.RollbackAsync(cancellationToken);

                return CharacterCreateError(result);
            }

            characterId = createdId;
        }

        await transaction.CommitAsync(cancellationToken);

        await SignInAsync(httpContext, dbUser);

        return Results.Ok(new { username = dbUser.Username, characterId });
    }

    private static async Task<IResult> CustomizeAsync(
        ClaimsPrincipal principal,
        HttpContext httpContext,
        IOptionsSnapshot<PluginOptions> pluginOptions,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var dbUser = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (dbUser is null)
            return Results.Unauthorized();

        if (IsLocked(dbUser))
            return Results.Json(new { error = "locked" }, statusCode: StatusCodes.Status403Forbidden);

        dbUser.Session = Guid.NewGuid().ToString("N");
        dbUser.SessionCreated = DateTimeOffset.UtcNow;

        if (await dbContext.SaveChangesAsync(cancellationToken) <= 0)
            return Results.InternalServerError();

        var site = Uri.EscapeDataString(PluginEndpoints.SiteOrigin(httpContext, pluginOptions.Value));

        return Results.Ok(new { url = $"freerealms://login/?site={site}&session={dbUser.Session}" });
    }

    private static async Task<IResult> LogoutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Results.Ok();
    }

    private static IResult GetNames(string? gender, CharacterNames names)
    {
        var normalizedGender = gender == "male" ? "male" : "female";

        return Results.Ok(new
        {
            firstNames = names.FirstNamesFor(normalizedGender),
            lastNamePrefixes = names.LastNamePrefixes,
            lastNameSuffixes = names.LastNameSuffixes
        });
    }

    private static async Task<IResult> GetAccountAsync(
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var dbUser = await dbContext.Users
            .AsNoTracking()
            .Include(x => x.Characters)
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (dbUser is null)
            return Results.Unauthorized();

        return Results.Ok(new
        {
            username = dbUser.Username,
            maxCharacters = dbUser.MaxCharacters,
            emailOptIn = dbUser.EmailOptIn,
            isMember = dbUser.IsMember,
            characters = dbUser.Characters
                .OrderBy(x => x.Id)
                .Select(x => new
                {
                    id = x.Id,
                    firstName = x.FirstName,
                    lastName = x.LastName,
                    model = x.Model,
                    gender = x.Gender,
                    lastLogin = x.LastLogin,
                    hasPortrait = HasPortraitFile(x.Id, "portrait.png"),
                    hasHeadshot = HasPortraitFile(x.Id, "headshot.png")
                })
        });
    }

    private static async Task<IResult> GetCharacterProfileAsync(
        ulong id,
        ClaimsPrincipal principal,
        IResourceManager resourceManager,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var character = await dbContext.Characters
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.FirstName,
                x.LastName,
                x.Model,
                x.Gender,
                x.Created,
                x.LastLogin,
                x.PlayTime,
                x.Coins,
                x.MembershipStatus,
                Level = x.Profiles.Where(p => p.Id == x.ActiveProfileId).Select(p => p.Level).FirstOrDefault(),
                Collections = x.CollectionEntries.Count,
                Titles = x.Titles.Count,
                Mounts = x.Mounts.Count,
                Pets = x.Pets.Count,
                Friends = x.Friends.Select(f => new { f.FriendCharacter.Id, f.FriendCharacter.FirstName, f.FriendCharacter.LastName, f.FriendCharacter.LastLogin }).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (character is null)
            return Results.NotFound();

        var isOwner = TryGetUserId(principal, out var userId) && userId == character.UserId;
        var race = resourceManager.Models.TryGetValue(character.Model, out var model) && model.RaceId == 2 ? "Pixie" : "Human";

        return Results.Ok(new
        {
            id = character.Id,
            firstName = character.FirstName,
            lastName = character.LastName,
            model = character.Model,
            race,
            gender = character.Gender == 1 ? "Male" : "Female",
            created = character.Created,
            lastLogin = character.LastLogin,
            playTimeMinutes = character.PlayTime,
            coins = character.Coins,
            isMember = character.MembershipStatus != 0,
            level = character.Level,
            collections = character.Collections,
            titles = character.Titles,
            mounts = character.Mounts,
            pets = character.Pets,
            hasPortrait = HasPortraitFile(character.Id, "portrait.png"),
            isOwner,
            friends = isOwner ? character.Friends.Select(f => new { id = f.Id, firstName = f.FirstName, lastName = f.LastName, lastLogin = f.LastLogin }) : null
        });
    }

    public sealed record EmailOptInRequest(bool OptIn);

    private static async Task<IResult> SetEmailOptInAsync(
        EmailOptInRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var updated = await dbContext.Users
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.EmailOptIn, request.OptIn), cancellationToken);

        return updated > 0 ? Results.Ok(new { emailOptIn = request.OptIn }) : Results.NotFound();
    }

    private static async Task<IResult> SearchCharactersAsync(
        string? name,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        var query = name?.Trim();

        if (string.IsNullOrEmpty(query) || query.Length < 2 || query.Length > 64)
            return Results.Ok(Array.Empty<object>());

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var results = await dbContext.Characters
            .AsNoTracking()
            .Where(x => x.FirstName.StartsWith(query) || (x.FullName != null && x.FullName.StartsWith(query)))
            .OrderBy(x => x.FirstName)
            .ThenBy(x => x.LastName)
            .Take(10)
            .Select(x => new { id = x.Id, firstName = x.FirstName, lastName = x.LastName })
            .ToListAsync(cancellationToken);

        return Results.Ok(results);
    }

    private static IResult GetPortrait(ulong id) => PortraitFile(id, "portrait.png");

    private static IResult GetHeadshot(ulong id) => PortraitFile(id, "headshot.png");

    private static IResult PortraitFile(ulong id, string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(PortraitStorage.GetCharacterDirectory(GuidHelper.GetPlayerGuid(id)), fileName));

        return File.Exists(path) ? Results.File(path, "image/png") : Results.NotFound();
    }

    private static bool HasPortraitFile(ulong id, string fileName)
    {
        return File.Exists(Path.Combine(PortraitStorage.GetCharacterDirectory(GuidHelper.GetPlayerGuid(id)), fileName));
    }

    private static async Task<IResult> CheckNameAsync(
        string firstName,
        string lastName,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var taken = await dbContext.Characters.AnyAsync(x => x.FirstName == firstName && x.LastName == lastName, cancellationToken);

        return Results.Ok(new { available = !taken });
    }

    private static async Task<IResult> CheckUsernameAsync(
        string username,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var taken = await dbContext.Users.AnyAsync(x => x.Username == username, cancellationToken);

        return Results.Ok(new { available = !taken });
    }

    private static async Task<IResult> CreateCharacterAsync(
        CreateCharacterRequest request,
        ClaimsPrincipal principal,
        CharacterNames names,
        IResourceManager resourceManager,
        IOptionsSnapshot<CharacterCreationSettings> settings,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        if (!TryBuildCharacterData(request.Preset, request.FirstName, request.LastNamePrefix, request.LastNameSuffix, names, resourceManager, out var characterData, out var characterError))
            return characterError;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var result = CharacterCreator.Create(resourceManager, dbContext, settings.Value, userId, characterData, _logger, out var characterId);

        if (result != CharacterCreateResult.Success)
            return CharacterCreateError(result);

        return Results.Ok(new { id = characterId, firstName = characterData.FirstName, lastName = characterData.LastName });
    }

    private static bool TryBuildCharacterData(
        int presetId,
        string firstName,
        string lastNamePrefix,
        string lastNameSuffix,
        CharacterNames names,
        IResourceManager resourceManager,
        [NotNullWhen(true)] out CharacterData? characterData,
        [NotNullWhen(false)] out IResult? error)
    {
        characterData = null;
        error = null;

        if (!CharacterPresets.All.TryGetValue(presetId, out var preset))
        {
            error = Results.Json(new { error = "invalidPreset" }, statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        if (!names.IsValidFirstName(preset.Gender, firstName)
            || !names.IsValidPrefix(lastNamePrefix)
            || !names.IsValidSuffix(lastNameSuffix))
        {
            error = Results.Json(new { error = "invalidName" }, statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        characterData = CharacterPresets.ToCharacterData(
            preset,
            firstName,
            CharacterNames.ComposeLastName(lastNamePrefix, lastNameSuffix),
            DefaultTint(resourceManager, preset.ItemChest),
            DefaultTint(resourceManager, preset.ItemLegs),
            DefaultTint(resourceManager, preset.ItemFeet));

        return true;
    }

    private static IResult CharacterCreateError(CharacterCreateResult result)
    {
        return result switch
        {
            CharacterCreateResult.NameTaken => Results.Json(new { error = "nameTaken" }, statusCode: StatusCodes.Status409Conflict),
            CharacterCreateResult.MaxCharacters => Results.Json(new { error = "maxCharacters" }, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(new { error = "failed" }, statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> PlayAsync(
        ulong id,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        IOptionsSnapshot<PluginOptions> pluginOptions,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var dbUser = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (dbUser is null)
            return Results.Unauthorized();

        if (IsLocked(dbUser))
            return Results.Json(new { error = "locked" }, statusCode: StatusCodes.Status403Forbidden);

        var ticket = Guid.NewGuid();

        var updated = await dbContext.Characters
            .Where(x => x.Id == id && x.UserId == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.Ticket, (Guid?)ticket), cancellationToken);

        if (updated <= 0)
            return Results.NotFound();

        var guid = GuidHelper.GetPlayerGuid(id);

        var site = Uri.EscapeDataString(PluginEndpoints.SiteOrigin(httpContext, pluginOptions.Value));

        return Results.Ok(new { url = $"freerealms://play/?site={site}&guid={guid}&ticket={ticket:N}" });
    }

    private static int DefaultTint(IResourceManager resourceManager, int itemId)
    {
        return resourceManager.ClientItemDefinitions.TryGetValue(itemId, out var definition) ? definition.Icon.TintId : 0;
    }

    private static bool IsLocked(DbUser dbUser) => dbUser.LockedUntil is not null && dbUser.LockedUntil > DateTimeOffset.UtcNow;

    private static bool TryGetUserId(ClaimsPrincipal principal, out ulong userId)
    {
        return ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    private static Task SignInAsync(HttpContext httpContext, DbUser dbUser)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, dbUser.Id.ToString()),
            new Claim(ClaimTypes.Name, dbUser.Username)
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        return httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }
}
