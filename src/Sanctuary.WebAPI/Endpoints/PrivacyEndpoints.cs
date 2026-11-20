using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;
using Sanctuary.Database.Entities;

using BC = BCrypt.Net.BCrypt;

namespace Sanctuary.WebAPI.Endpoints;

public static class PrivacyEndpoints
{
    public const int Everyone = 0;
    public const int FriendsOnly = 1;
    public const int OnlyMe = 2;

    public sealed record PrivacyRequest(int Messages, int Gallery, int Characters, int Friends, int News);
    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    public sealed record ProfileAccess(bool IsOwner, bool Messages, bool Gallery, bool Characters, bool Friends, bool News);

    public static void MapPrivacyEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/characters/{id}/privacy", GetPrivacyAsync).RequireAuthorization();
        group.MapPost("/characters/{id}/privacy", SetPrivacyAsync).RequireAuthorization();
        group.MapPost("/account/password", ChangePasswordAsync).RequireAuthorization();
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out ulong userId)
    {
        return ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    public static async Task<DbProfilePrivacy> LoadAsync(DatabaseContext dbContext, ulong characterId, CancellationToken cancellationToken)
    {
        return await dbContext.ProfilePrivacy.AsNoTracking().SingleOrDefaultAsync(x => x.CharacterId == characterId, cancellationToken)
            ?? new DbProfilePrivacy { CharacterId = characterId };
    }

    public static async Task<ProfileAccess> GetAccessAsync(DatabaseContext dbContext, ulong characterId, ulong ownerUserId, ulong viewerUserId, CancellationToken cancellationToken)
    {
        if (viewerUserId != 0 && viewerUserId == ownerUserId)
            return new ProfileAccess(true, true, true, true, true, true);

        var privacy = await LoadAsync(dbContext, characterId, cancellationToken);

        var isFriend = viewerUserId != 0 && await dbContext.Friends.AnyAsync(
            x => x.FriendCharacterId == characterId && x.Character.UserId == viewerUserId, cancellationToken);

        bool Allowed(int level) => level == Everyone || (level == FriendsOnly && isFriend);

        return new ProfileAccess(false, Allowed(privacy.Messages), Allowed(privacy.Gallery), Allowed(privacy.Characters), Allowed(privacy.Friends), Allowed(privacy.News));
    }

    private static async Task<IResult> GetPrivacyAsync(
        ulong id,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (!await dbContext.Characters.AnyAsync(x => x.Id == id && x.UserId == userId, cancellationToken))
            return Results.NotFound();

        var privacy = await LoadAsync(dbContext, id, cancellationToken);

        return Results.Ok(new
        {
            messages = privacy.Messages,
            gallery = privacy.Gallery,
            characters = privacy.Characters,
            friends = privacy.Friends,
            news = privacy.News
        });
    }

    private static async Task<IResult> SetPrivacyAsync(
        ulong id,
        PrivacyRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        int[] levels = [request.Messages, request.Gallery, request.Characters, request.Friends, request.News];

        if (levels.Any(x => x < Everyone || x > OnlyMe))
            return Results.BadRequest();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (!await dbContext.Characters.AnyAsync(x => x.Id == id && x.UserId == userId, cancellationToken))
            return Results.NotFound();

        var privacy = await dbContext.ProfilePrivacy.SingleOrDefaultAsync(x => x.CharacterId == id, cancellationToken);

        if (privacy is null)
        {
            privacy = new DbProfilePrivacy { CharacterId = id };
            dbContext.ProfilePrivacy.Add(privacy);
        }

        privacy.Messages = request.Messages;
        privacy.Gallery = request.Gallery;
        privacy.Characters = request.Characters;
        privacy.Friends = request.Friends;
        privacy.News = request.News;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { saved = true });
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        var newPassword = request.NewPassword ?? string.Empty;

        if (newPassword.Length < 6 || newPassword.Length > 100 || newPassword.Any(c => c > 0x7F))
            return Results.Json(new { error = "newPassword" }, statusCode: StatusCodes.Status400BadRequest);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var dbUser = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (dbUser is null)
            return Results.Unauthorized();

        if (string.IsNullOrEmpty(request.CurrentPassword) || !BC.Verify(request.CurrentPassword, dbUser.Password))
            return Results.Json(new { error = "currentPassword" }, statusCode: StatusCodes.Status400BadRequest);

        dbUser.Password = BC.HashPassword(newPassword, BC.GenerateSalt());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { changed = true });
    }
}
