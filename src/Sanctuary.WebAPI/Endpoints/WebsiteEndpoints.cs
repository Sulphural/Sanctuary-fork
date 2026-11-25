using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

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

        group.MapPost("/login", LoginAsync).RequireRateLimiting(WebsiteSecurity.AuthPolicy);
        group.MapPost("/signup", SignupAsync).RequireRateLimiting(WebsiteSecurity.SignupPolicy);
        group.MapPost("/logout", LogoutAsync);

        group.MapGet("/names", GetNames);
        group.MapGet("/names/check", CheckNameAsync).RequireRateLimiting(WebsiteSecurity.LookupPolicy);
        group.MapGet("/username/check", CheckUsernameAsync).RequireRateLimiting(WebsiteSecurity.LookupPolicy);

        group.MapGet("/characters/search", SearchCharactersAsync).RequireRateLimiting(WebsiteSecurity.LookupPolicy);
        group.MapGet("/characters/{id}/profile", GetCharacterProfileAsync);
        group.MapPost("/account/optin", SetEmailOptInAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapGet("/characters/{id}/portrait", GetPortrait);
        group.MapGet("/characters/{id}/headshot", GetHeadshot);
        group.MapGet("/characters/{id}/appearance", GetAppearanceAsync);
        group.MapPost("/characters/{id}/portrait", UploadPortraitAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);

        group.MapGet("/account", GetAccountAsync).RequireAuthorization();
        group.MapPost("/characters", CreateCharacterAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/characters/{id}/play", PlayAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/characters/{id}/delete", DeleteCharacterAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/customize", CustomizeAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);

        group.MapSocialEndpoints();
        group.MapStaffEndpoints(loggerFactory);
        group.MapStaffPlayerEndpoints(loggerFactory);
        group.MapNewsEndpoints();
        group.MapForumEndpoints();
        group.MapServerStatusEndpoints();
        group.MapServerUpdatesEndpoints();
        group.MapRedeemEndpoints(loggerFactory);
        group.MapMembershipEndpoints(loggerFactory);
        group.MapAccountRecoveryEndpoints(loggerFactory);
    }

    public sealed record CreateCharacterRequest(int Preset, string FirstName, string LastNamePrefix, string LastNameSuffix, CharacterCustomizationModel? Customization = null, string? CustomName = null);

    private static async Task<IResult> LoginAsync(
        LoginRequestModel request,
        HttpContext httpContext,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!MiniValidator.TryValidate(request, out var errors))
            return Results.ValidationProblem(errors);

        if (LoginThrottle.IsBlocked(request.Username))
            return Results.Json(new { error = "tooManyAttempts" }, statusCode: StatusCodes.Status429TooManyRequests);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var dbUser = await dbContext.Users.FirstOrDefaultAsync(x => x.Username == request.Username, cancellationToken);

        if (dbUser is null || !BC.Verify(request.Password, dbUser.Password))
        {
            _logger.LogWarning("Website login failed for username: {Username}", request.Username);
            LoginThrottle.RecordFailure(request.Username);

            return Results.Json(new { error = "invalidLogin" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        LoginThrottle.RecordSuccess(request.Username);

        if (IsLocked(dbUser))
            return Results.Json(new { error = "locked", until = dbUser.LockedUntil }, statusCode: StatusCodes.Status403Forbidden);

        await SignInAsync(httpContext, dbUser);

        return Results.Ok(new { username = dbUser.Username });
    }

    private static async Task<IResult> SignupAsync(
        SignupRequestModel request,
        HttpContext httpContext,
        CharacterNames names,
        CreatorCatalog catalog,
        IResourceManager resourceManager,
        IOptions<WebAPIOptions> webAPIOptions,
        IOptionsSnapshot<CharacterCreationSettings> settings,
        IOptionsSnapshot<PluginOptions> pluginOptions,
        EmailSender emailSender,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!MiniValidator.TryValidate(request, out var errors))
            return Results.ValidationProblem(errors);

        if (!request.AcceptTerms)
            return Results.Json(new { error = "termsRequired" }, statusCode: StatusCodes.Status400BadRequest);

        if (!string.IsNullOrEmpty(request.Website))
        {
            _logger.LogWarning("Signup for {Username} filled in the hidden bot field.", request.Username);

            return Results.Json(new { error = "invalid" }, statusCode: StatusCodes.Status400BadRequest);
        }

        CharacterData? characterData = null;

        if (request.Character is not null)
        {
            if (!TryBuildCharacterData(request.Character.Preset, request.Character.Customization, request.Character.FirstName, request.Character.LastNamePrefix, request.Character.LastNameSuffix, request.Character.CustomName, names, catalog, resourceManager, out characterData, out var characterError))
                return characterError;
        }

        if (Usernames.IsBlocked(request.Username, resourceManager))
            return Results.Json(new { error = "usernameBlocked" }, statusCode: StatusCodes.Status400BadRequest);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await dbContext.Users.AnyAsync(x => x.Username == request.Username, cancellationToken))
            return Results.Json(new { error = "usernameTaken" }, statusCode: StatusCodes.Status409Conflict);

        (string FirstName, string LastName)? requestedName = null;

        if (characterData is not null)
        {
            var (holdError, held) = await HoldCustomNameAsync(dbContext, names, resourceManager, request.Character!.CustomName, request.Character.FirstName, request.Character.LastNamePrefix, request.Character.LastNameSuffix, characterData, cancellationToken);

            if (holdError is not null)
                return holdError;

            requestedName = held;
        }

        if (!WebsiteSecurity.TryAcquireSignup())
        {
            _logger.LogWarning("Signup for {Username} refused because too many accounts were made in the last hour.", request.Username);

            return Results.Json(new { error = "tooManyRequests" }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var dbUser = new DbUser
        {
            Username = request.Username,
            Password = BC.HashPassword(request.Password, BC.GenerateSalt()),
            IsMember = webAPIOptions.Value.MemberByDefault ?? false,
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

            await AddNameReviewAsync(dbContext, createdId, requestedName, cancellationToken);

            characterId = createdId;
        }

        await transaction.CommitAsync(cancellationToken);

        await SignInAsync(httpContext, dbUser);

        try
        {
            await AccountRecoveryEndpoints.SendVerificationEmailAsync(dbContext, dbUser, PluginEndpoints.SiteOrigin(httpContext, pluginOptions.Value), emailSender, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not send the verification e-mail for new user {Username}.", dbUser.Username);
        }

        return Results.Ok(new { username = dbUser.Username, characterId, pendingName = PendingNameResponse(requestedName) });
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
            return Results.Json(new { error = "locked", until = dbUser.LockedUntil }, statusCode: StatusCodes.Status403Forbidden);

        dbUser.Session = Guid.NewGuid().ToString("N");
        dbUser.SessionCreated = DateTimeOffset.UtcNow;

        if (await dbContext.SaveChangesAsync(cancellationToken) <= 0)
            return Results.InternalServerError();

        var site = Uri.EscapeDataString(PluginEndpoints.SiteOrigin(httpContext, pluginOptions.Value));

        return Results.Ok(new { url = $"freerealms://login/?site={site}&session={dbUser.Session}" });
    }

    private static async Task LogoutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
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

        var characterIds = dbUser.Characters.Select(x => x.Id).ToList();
        var pendingNames = await dbContext.NameReviews
            .AsNoTracking()
            .Where(x => characterIds.Contains(x.CharacterId))
            .ToDictionaryAsync(x => x.CharacterId, cancellationToken);

        return Results.Ok(new
        {
            username = dbUser.Username,
            maxCharacters = Membership.CharacterLimit(dbUser),
            emailOptIn = dbUser.EmailOptIn,
            email = dbUser.Email,
            emailVerified = dbUser.EmailVerified,
            isMember = dbUser.IsMember && !Membership.HasExpired(dbUser),
            memberUntil = dbUser.IsMember && !Membership.HasExpired(dbUser) ? dbUser.MemberUntil : null,
            isStaff = dbUser.IsAdmin || dbUser.IsMod,
            isAdmin = dbUser.IsAdmin,
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
                    hasHeadshot = HasPortraitFile(x.Id, "headshot.png"),
                    portraitStamp = PortraitStamp(x.Id),
                    pendingName = pendingNames.TryGetValue(x.Id, out var review)
                        ? new { firstName = review.RequestedFirstName, lastName = review.RequestedLastName }
                        : null
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
                x.StationCash,
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
            stationCash = isOwner ? character.StationCash : (int?)null,
            isMember = character.MembershipStatus != 0,
            level = character.Level,
            collections = character.Collections,
            titles = character.Titles,
            mounts = character.Mounts,
            pets = character.Pets,
            hasPortrait = HasPortraitFile(character.Id, "portrait.png"),
            portraitStamp = PortraitStamp(character.Id),
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

        var escaped = query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var anywhere = $"%{escaped}%";
        var prefix = $"{escaped}%";

        var results = await dbContext.Characters
            .AsNoTracking()
            .Where(x => EF.Functions.Like(x.FirstName, anywhere, "\\")
                || (x.LastName != null && EF.Functions.Like(x.LastName, anywhere, "\\"))
                || (x.FullName != null && EF.Functions.Like(x.FullName, anywhere, "\\")))
            .OrderByDescending(x => EF.Functions.Like(x.FirstName, prefix, "\\"))
            .ThenBy(x => x.FirstName)
            .ThenBy(x => x.LastName)
            .Take(15)
            .Select(x => new { id = x.Id, firstName = x.FirstName, lastName = x.LastName })
            .ToListAsync(cancellationToken);

        return Results.Ok(results);
    }

    private static IResult GetPortrait(ulong id, PortraitCutouts cutouts) => PortraitFile(id, "portrait", cutouts);

    private static IResult GetHeadshot(ulong id, PortraitCutouts cutouts) => PortraitFile(id, "headshot", cutouts);

    private static IResult PortraitFile(ulong id, string kind, PortraitCutouts cutouts)
    {
        var path = Path.GetFullPath(Path.Combine(PortraitStorage.GetCharacterDirectory(GuidHelper.GetPlayerGuid(id)), kind + ".png"));
        var png = cutouts.GetCutout(path, kind);

        return png is null ? Results.NotFound() : Results.File(png, "image/png");
    }

    private static bool HasPortraitFile(ulong id, string fileName)
    {
        return File.Exists(Path.Combine(PortraitStorage.GetCharacterDirectory(GuidHelper.GetPlayerGuid(id)), fileName));
    }

    private static long PortraitStamp(ulong id)
    {
        var path = Path.Combine(PortraitStorage.GetCharacterDirectory(GuidHelper.GetPlayerGuid(id)), "portrait.png");

        return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks / TimeSpan.TicksPerSecond : 0;
    }

    private const string PortraitRenderVersion = "web5";

    private static async Task<(int CharacterType, Dictionary<string, int> Choices, string Fingerprint)?> DescribeAppearanceAsync(
        ulong id,
        CreatorCatalog catalog,
        DatabaseContext dbContext,
        CancellationToken cancellationToken)
    {
        var character = await dbContext.Characters
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Model,
                x.HeadId,
                x.SkinToneId,
                x.HairId,
                x.HairColor,
                x.EyeColor,
                x.FacePaintId,
                x.ModelCustomizationId,
                Equipped = x.Profiles.Where(p => p.Id == x.ActiveProfileId).SelectMany(p => p.Items).Select(i => new { i.Definition, i.Tint }).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (character is null)
            return null;

        var equipped = character.Equipped.Select(x => (x.Definition, x.Tint)).ToList();

        if (!catalog.TryDescribe(
                character.Model,
                character.HeadId,
                character.SkinToneId,
                character.HairId,
                character.HairColor,
                character.EyeColor,
                character.FacePaintId ?? 0,
                character.ModelCustomizationId ?? 0,
                equipped,
                out var characterType,
                out var choices) || choices is null)
            return null;

        var key = PortraitRenderVersion + ":" + characterType + ":" + string.Join(",", choices.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value));
        var fingerprint = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();

        return (characterType, choices, fingerprint);
    }

    private static string? ReadPortraitFingerprint(ulong id)
    {
        var path = Path.Combine(PortraitStorage.GetCharacterDirectory(GuidHelper.GetPlayerGuid(id)), "portrait.appearance");

        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private static async Task<IResult> GetAppearanceAsync(
        ulong id,
        CreatorCatalog catalog,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var appearance = await DescribeAppearanceAsync(id, catalog, dbContext, cancellationToken);

        if (appearance is null)
            return Results.NotFound();

        var stored = ReadPortraitFingerprint(id);

        return Results.Ok(new
        {
            characterType = appearance.Value.CharacterType,
            choices = appearance.Value.Choices,
            fingerprint = appearance.Value.Fingerprint,
            current = stored == appearance.Value.Fingerprint && HasPortraitFile(id, "portrait.png") && HasPortraitFile(id, "headshot.png")
        });
    }

    private static async Task<IResult> UploadPortraitAsync(
        ulong id,
        HttpContext httpContext,
        ClaimsPrincipal principal,
        CreatorCatalog catalog,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        if (!httpContext.Request.HasFormContentType)
            return Results.BadRequest();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (!await dbContext.Characters.AnyAsync(x => x.Id == id && x.UserId == userId, cancellationToken))
            return Results.NotFound();

        var appearance = await DescribeAppearanceAsync(id, catalog, dbContext, cancellationToken);

        if (appearance is null)
            return Results.NotFound();

        var form = await httpContext.Request.ReadFormAsync(cancellationToken);

        if (form["fingerprint"] != appearance.Value.Fingerprint)
            return Results.Json(new { error = "stale" }, statusCode: StatusCodes.Status409Conflict);

        var portrait = form.Files.GetFile("portrait");
        var headshot = form.Files.GetFile("headshot");

        if (portrait is null || headshot is null || portrait.Length > 1_000_000 || headshot.Length > 200_000)
            return Results.BadRequest();

        using var portraitImage = await TryLoadPngAsync(portrait, 180, 330, cancellationToken);
        using var headshotImage = await TryLoadPngAsync(headshot, 70, 70, cancellationToken);

        if (portraitImage is null || headshotImage is null)
            return Results.BadRequest();

        var directory = PortraitStorage.GetCharacterDirectory(GuidHelper.GetPlayerGuid(id));

        Directory.CreateDirectory(directory);

        await headshotImage.SaveAsPngAsync(Path.Combine(directory, "headshot.png"), cancellationToken);
        await portraitImage.SaveAsPngAsync(Path.Combine(directory, "portrait.png"), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "portrait.appearance"), appearance.Value.Fingerprint, cancellationToken);

        return Results.Ok(new { portraitStamp = PortraitStamp(id) });
    }

    private static async Task<Image<Rgba32>?> TryLoadPngAsync(IFormFile file, int width, int height, CancellationToken cancellationToken)
    {
        try
        {
            var options = new PngDecoderOptions { GeneralOptions = new DecoderOptions { SkipMetadata = true } };

            await using (var header = file.OpenReadStream())
            {
                var info = await PngDecoder.Instance.IdentifyAsync(options.GeneralOptions, header, cancellationToken);

                if (info.Width != width || info.Height != height)
                    return null;
            }

            await using var stream = file.OpenReadStream();

            return await PngDecoder.Instance.DecodeAsync<Rgba32>(options, stream, cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<IResult> CheckNameAsync(
        string? firstName,
        string? lastName,
        string? custom,
        string? gender,
        string? prefix,
        string? suffix,
        IResourceManager resourceManager,
        CharacterNames names,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (custom is null && firstName is not null && prefix is not null && suffix is not null)
            lastName = CharacterNames.ComposeLastName(prefix, suffix);

        if (custom is not null)
        {
            var nameError = CustomNames.Validate(custom, resourceManager, names, out var customFirstName, out var customLastName);

            if (nameError is not null)
                return Results.Ok(new { available = false, error = nameError });

            firstName = customFirstName;
            lastName = customLastName;
        }

        firstName ??= string.Empty;
        lastName ??= string.Empty;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var taken = await PendingNames.IsTakenAsync(dbContext, firstName, lastName, 0, cancellationToken);
        var review = !taken && custom is not null && PendingNames.NeedsReview(names, firstName, lastName);
        var male = gender == "male";
        var knownGender = gender is "male" or "female";

        object? temporary = null;

        if (review && knownGender)
        {
            var picked = await PendingNames.PickFreeReelPartsAsync(dbContext, names, male, cancellationToken);

            if (picked is not null)
                temporary = ReelNameResponse(picked.Value);
        }

        object[]? suggestions = null;

        if (taken && custom is null && knownGender && prefix is not null && suffix is not null
            && names.IsValidFirstName(gender!, firstName) && names.IsValidPrefix(prefix) && names.IsValidSuffix(suffix))
        {
            var found = await PendingNames.SuggestReelNamesAsync(dbContext, names, male, firstName, prefix, suffix, cancellationToken);
            suggestions = found.Select(ReelNameResponse).ToArray();
        }

        return Results.Ok(new { available = !taken, error = taken ? "taken" : null, review, temporary, suggestions });
    }

    private static object ReelNameResponse((string FirstName, string Prefix, string Suffix) name)
    {
        return new { firstName = name.FirstName, lastNamePrefix = name.Prefix, lastNameSuffix = name.Suffix, lastName = CharacterNames.ComposeLastName(name.Prefix, name.Suffix) };
    }

    private static async Task<IResult> CheckUsernameAsync(
        string username,
        IResourceManager resourceManager,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var blocked = Usernames.IsBlocked(username, resourceManager);
        var taken = !blocked && await dbContext.Users.AnyAsync(x => x.Username == username, cancellationToken);

        if (!blocked && !taken)
            return Results.Ok(new { available = true });

        var suggestions = await Usernames.SuggestAsync(dbContext, username, resourceManager, cancellationToken);

        return Results.Ok(new { available = false, reason = blocked ? "blocked" : "taken", suggestions });
    }

    private static async Task<IResult> CreateCharacterAsync(
        CreateCharacterRequest request,
        ClaimsPrincipal principal,
        CharacterNames names,
        CreatorCatalog catalog,
        IResourceManager resourceManager,
        IOptionsSnapshot<CharacterCreationSettings> settings,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        if (!TryBuildCharacterData(request.Preset, request.Customization, request.FirstName, request.LastNamePrefix, request.LastNameSuffix, request.CustomName, names, catalog, resourceManager, out var characterData, out var characterError))
            return characterError;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var (holdError, requestedName) = await HoldCustomNameAsync(dbContext, names, resourceManager, request.CustomName, request.FirstName, request.LastNamePrefix, request.LastNameSuffix, characterData, cancellationToken);

        if (holdError is not null)
            return holdError;

        var result = CharacterCreator.Create(resourceManager, dbContext, settings.Value, userId, characterData, _logger, out var characterId);

        if (result != CharacterCreateResult.Success)
            return CharacterCreateError(result);

        await AddNameReviewAsync(dbContext, characterId, requestedName, cancellationToken);

        return Results.Ok(new { id = characterId, firstName = characterData.FirstName, lastName = characterData.LastName, pendingName = PendingNameResponse(requestedName) });
    }

    private static async Task<(IResult? Error, (string FirstName, string LastName)? Requested)> HoldCustomNameAsync(
        DatabaseContext dbContext,
        CharacterNames names,
        IResourceManager resourceManager,
        string? customName,
        string? reelFirstName,
        string? reelPrefix,
        string? reelSuffix,
        CharacterData characterData,
        CancellationToken cancellationToken)
    {
        var firstName = characterData.FirstName ?? string.Empty;
        var lastName = characterData.LastName ?? string.Empty;

        if (string.IsNullOrWhiteSpace(customName) || !PendingNames.NeedsReview(names, firstName, lastName))
            return (null, null);

        if (await PendingNames.IsTakenAsync(dbContext, firstName, lastName, 0, cancellationToken))
            return (CharacterCreateError(CharacterCreateResult.NameTaken), null);

        var male = resourceManager.Models.TryGetValue(characterData.Model, out var model) && model.Gender == 1;
        var temporaryName = await PendingNames.IsFreeReelNameAsync(dbContext, names, male, reelFirstName, reelPrefix, reelSuffix, cancellationToken)
            ? (reelFirstName!, CharacterNames.ComposeLastName(reelPrefix!, reelSuffix!))
            : await PendingNames.PickFreeReelNameAsync(dbContext, names, male, cancellationToken);

        if (temporaryName is null)
            return (Results.Json(new { error = "failed" }, statusCode: StatusCodes.Status500InternalServerError), null);

        characterData.FirstName = temporaryName.Value.FirstName;
        characterData.LastName = temporaryName.Value.LastName;

        return (null, (firstName, lastName));
    }

    private static async Task AddNameReviewAsync(DatabaseContext dbContext, ulong characterId, (string FirstName, string LastName)? requestedName, CancellationToken cancellationToken)
    {
        if (requestedName is null)
            return;

        dbContext.NameReviews.Add(new DbNameReview
        {
            CharacterId = characterId,
            RequestedFirstName = requestedName.Value.FirstName,
            RequestedLastName = requestedName.Value.LastName,
            Created = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static object? PendingNameResponse((string FirstName, string LastName)? requestedName)
    {
        return requestedName is null ? null : new { firstName = requestedName.Value.FirstName, lastName = requestedName.Value.LastName };
    }

    private static bool TryBuildCharacterData(
        int presetId,
        CharacterCustomizationModel? customization,
        string firstName,
        string lastNamePrefix,
        string lastNameSuffix,
        string? customName,
        CharacterNames names,
        CreatorCatalog catalog,
        IResourceManager resourceManager,
        [NotNullWhen(true)] out CharacterData? characterData,
        [NotNullWhen(false)] out IResult? error)
    {
        characterData = null;
        error = null;

        var useCustomName = !string.IsNullOrWhiteSpace(customName);
        var customFirstName = string.Empty;
        var customLastName = string.Empty;

        if (useCustomName)
        {
            var nameError = CustomNames.Validate(customName, resourceManager, names, out customFirstName, out customLastName);

            if (nameError is not null)
            {
                error = Results.Json(new { error = "invalidName", reason = nameError }, statusCode: StatusCodes.Status400BadRequest);
                return false;
            }
        }

        if (customization is not null)
        {
            if (!catalog.TryBuild(customization, out var custom) || custom is null)
            {
                error = Results.Json(new { error = "invalidCustomization" }, statusCode: StatusCodes.Status400BadRequest);
                return false;
            }

            if (!useCustomName
                && (!names.IsValidFirstName(custom.Gender, firstName)
                    || !names.IsValidPrefix(lastNamePrefix)
                    || !names.IsValidSuffix(lastNameSuffix)))
            {
                error = Results.Json(new { error = "invalidName" }, statusCode: StatusCodes.Status400BadRequest);
                return false;
            }

            characterData = new CharacterData
            {
                FirstName = useCustomName ? customFirstName : firstName,
                LastName = useCustomName ? customLastName : CharacterNames.ComposeLastName(lastNamePrefix, lastNameSuffix),
                Model = custom.Model,
                Head = custom.Head,
                SkinTone = custom.SkinTone,
                Hair = custom.Hair,
                HairColor = custom.HairColor,
                EyeColor = custom.EyeColor,
                ModelCustomization = custom.ModelCustomization,
                FacePaint = custom.FacePaint,
                ItemChest = custom.ItemChest,
                ItemLegs = custom.ItemLegs,
                ItemFeet = custom.ItemFeet,
                TintChest = custom.TintChest,
                TintLegs = custom.TintLegs,
                TintFeet = custom.TintFeet
            };

            return true;
        }

        if (!CharacterPresets.All.TryGetValue(presetId, out var preset))
        {
            error = Results.Json(new { error = "invalidPreset" }, statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        if (!useCustomName
            && (!names.IsValidFirstName(preset.Gender, firstName)
                || !names.IsValidPrefix(lastNamePrefix)
                || !names.IsValidSuffix(lastNameSuffix)))
        {
            error = Results.Json(new { error = "invalidName" }, statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        characterData = CharacterPresets.ToCharacterData(
            preset,
            useCustomName ? customFirstName : firstName,
            useCustomName ? customLastName : CharacterNames.ComposeLastName(lastNamePrefix, lastNameSuffix),
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

    private static async Task<IResult> DeleteCharacterAsync(
        ulong id,
        ClaimsPrincipal principal,
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
            return Results.Json(new { error = "locked", until = dbUser.LockedUntil }, statusCode: StatusCodes.Status403Forbidden);

        var character = await dbContext.Characters.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);

        if (character is null)
            return Results.NotFound();

        if (!CharacterDeleter.Delete(dbContext, character, _logger))
            return Results.Json(new { error = "failed" }, statusCode: StatusCodes.Status500InternalServerError);

        _logger.LogInformation("User {userId} deleted character {characterId} from the website.", userId, id);

        var directory = PortraitStorage.GetCharacterDirectory(GuidHelper.GetPlayerGuid(id));

        foreach (var fileName in new[] { "portrait.png", "headshot.png", "portrait.appearance" })
        {
            try
            {
                File.Delete(Path.Combine(directory, fileName));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return Results.Ok(new { deleted = id });
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
            return Results.Json(new { error = "locked", until = dbUser.LockedUntil }, statusCode: StatusCodes.Status403Forbidden);

        var sessionTicket = Guid.NewGuid();

        var tagged = await dbContext.Characters
            .Where(x => x.Id == id && x.UserId == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.Ticket, (Guid?)sessionTicket), cancellationToken);

        if (tagged <= 0)
            return Results.NotFound();

        var session = sessionTicket.ToString("N");
        DateTimeOffset? sessionCreated = DateTimeOffset.UtcNow;

        var updated = await dbContext.Users
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(u => u.Session, session)
                .SetProperty(u => u.SessionCreated, sessionCreated), cancellationToken);

        if (updated <= 0)
            return Results.NotFound();

        var site = Uri.EscapeDataString(PluginEndpoints.SiteOrigin(httpContext, pluginOptions.Value));

        return Results.Ok(new { url = $"freerealms://login/?site={site}&session={session}" });
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

    public static Task SignInAsync(HttpContext httpContext, DbUser dbUser)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, dbUser.Id.ToString()),
            new Claim(ClaimTypes.Name, dbUser.Username),
            new Claim(WebsiteSecurity.StampClaim, WebsiteSecurity.Stamp(dbUser))
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        return httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }
}
