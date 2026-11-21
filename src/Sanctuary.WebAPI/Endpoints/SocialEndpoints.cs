using System;
using System.Collections.Generic;
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
using Sanctuary.WebAPI.Website;

namespace Sanctuary.WebAPI.Endpoints;

public static class SocialEndpoints
{
    private const int MaxMessageLength = 500;
    private const int MessagePageSize = 30;

    public sealed record ActingCharacterRequest(ulong As);
    public sealed record PostMessageRequest(ulong As, string? Text);

    public static void MapSocialEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/characters/{id}/social", GetSocialAsync);

        group.MapPost("/characters/{id}/friends/request", SendFriendRequestAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/characters/{id}/friends/{otherId}/accept", AcceptFriendRequestAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/characters/{id}/friends/{otherId}/decline", DeclineFriendRequestAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/characters/{id}/friends/{otherId}/remove", RemoveFriendAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);

        group.MapGet("/characters/{id}/messages", GetMessagesAsync);
        group.MapPost("/characters/{id}/messages", PostMessageAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/characters/{id}/messages/{messageId}/delete", DeleteMessageAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);

        group.MapPrivacyEndpoints();
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out ulong userId)
    {
        return ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    private static Task<bool> OwnsAsync(DatabaseContext dbContext, ulong userId, ulong characterId, CancellationToken cancellationToken)
    {
        return dbContext.Characters.AnyAsync(x => x.Id == characterId && x.UserId == userId, cancellationToken);
    }

    private static Task<bool> AreFriendsAsync(DatabaseContext dbContext, ulong characterId, ulong otherId, CancellationToken cancellationToken)
    {
        return dbContext.Friends.AnyAsync(x => x.CharacterId == characterId && x.FriendCharacterId == otherId, cancellationToken);
    }

    private static async Task<string> RelationAsync(DatabaseContext dbContext, ulong viewerId, ulong targetId, CancellationToken cancellationToken)
    {
        if (viewerId == targetId)
            return "self";

        if (await AreFriendsAsync(dbContext, viewerId, targetId, cancellationToken))
            return "friends";

        if (await dbContext.FriendRequests.AnyAsync(x => x.CharacterId == viewerId && x.TargetCharacterId == targetId, cancellationToken))
            return "requestSent";

        if (await dbContext.FriendRequests.AnyAsync(x => x.CharacterId == targetId && x.TargetCharacterId == viewerId, cancellationToken))
            return "requestReceived";

        return "none";
    }

    private static async Task<IResult> GetSocialAsync(
        ulong id,
        ulong? @as,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        var userId = TryGetUserId(principal, out var parsedUserId) ? parsedUserId : 0;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var target = await dbContext.Characters.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.UserId }).SingleOrDefaultAsync(cancellationToken);

        if (target is null)
            return Results.NotFound();

        var isOwner = userId != 0 && target.UserId == userId;
        var viewerId = isOwner ? id : @as ?? 0;

        if (viewerId != 0 && (userId == 0 || !await OwnsAsync(dbContext, userId, viewerId, cancellationToken)))
            viewerId = 0;

        var access = await PrivacyEndpoints.GetAccessAsync(dbContext, id, target.UserId, userId, cancellationToken);

        object? friends = null;
        object? characters = null;

        if (!isOwner && access.Friends)
        {
            friends = await dbContext.Friends
                .AsNoTracking()
                .Where(x => x.CharacterId == id)
                .OrderBy(x => x.FriendCharacter.FirstName)
                .Select(x => new { id = x.FriendCharacter.Id, firstName = x.FriendCharacter.FirstName, lastName = x.FriendCharacter.LastName, lastLogin = x.FriendCharacter.LastLogin })
                .ToListAsync(cancellationToken);
        }

        if (!isOwner && access.Characters)
        {
            characters = await dbContext.Characters
                .AsNoTracking()
                .Where(x => x.UserId == target.UserId)
                .OrderBy(x => x.FirstName)
                .Select(x => new { id = x.Id, firstName = x.FirstName, lastName = x.LastName, model = x.Model, gender = x.Gender, lastLogin = x.LastLogin })
                .ToListAsync(cancellationToken);
        }

        var relation = viewerId == 0 ? "none" : await RelationAsync(dbContext, viewerId, id, cancellationToken);

        object? incoming = null;
        object? outgoing = null;

        if (isOwner)
        {
            incoming = await dbContext.FriendRequests
                .AsNoTracking()
                .Where(x => x.TargetCharacterId == id)
                .OrderBy(x => x.Character.FirstName)
                .Select(x => new { id = x.Character.Id, firstName = x.Character.FirstName, lastName = x.Character.LastName })
                .ToListAsync(cancellationToken);

            outgoing = await dbContext.FriendRequests
                .AsNoTracking()
                .Where(x => x.CharacterId == id)
                .OrderBy(x => x.TargetCharacter.FirstName)
                .Select(x => new { id = x.TargetCharacter.Id, firstName = x.TargetCharacter.FirstName, lastName = x.TargetCharacter.LastName })
                .ToListAsync(cancellationToken);
        }

        return Results.Ok(new
        {
            viewerId,
            relation,
            canMessage = relation is "self" or "friends",
            incoming,
            outgoing,
            access = new { messages = access.Messages, gallery = access.Gallery, characters = access.Characters, friends = access.Friends, news = access.News },
            friends,
            characters
        });
    }

    private static async Task<IResult> SendFriendRequestAsync(
        ulong id,
        ActingCharacterRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (request.As == id || !await OwnsAsync(dbContext, userId, request.As, cancellationToken))
            return Results.BadRequest();

        if (!await dbContext.Characters.AnyAsync(x => x.Id == id, cancellationToken))
            return Results.NotFound();

        var relation = await RelationAsync(dbContext, request.As, id, cancellationToken);

        if (relation is "friends" or "requestSent")
            return Results.Ok(new { relation });

        if (relation == "requestReceived")
            return await AcceptAsync(dbContext, request.As, id, cancellationToken);

        var ignored = await dbContext.Ignores.AnyAsync(x => x.CharacterId == id && x.IgnoreCharacterId == request.As, cancellationToken);

        if (!ignored)
        {
            dbContext.FriendRequests.Add(new DbFriendRequest
            {
                CharacterId = request.As,
                TargetCharacterId = id,
                Created = DateTimeOffset.UtcNow
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(new { relation = "requestSent" });
    }

    private static async Task<IResult> AcceptFriendRequestAsync(
        ulong id,
        ulong otherId,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (!await OwnsAsync(dbContext, userId, id, cancellationToken))
            return Results.NotFound();

        if (!await dbContext.FriendRequests.AnyAsync(x => x.CharacterId == otherId && x.TargetCharacterId == id, cancellationToken))
            return Results.NotFound();

        return await AcceptAsync(dbContext, id, otherId, cancellationToken);
    }

    private static async Task<IResult> AcceptAsync(DatabaseContext dbContext, ulong characterId, ulong otherId, CancellationToken cancellationToken)
    {
        await dbContext.FriendRequests
            .Where(x => (x.CharacterId == characterId && x.TargetCharacterId == otherId) || (x.CharacterId == otherId && x.TargetCharacterId == characterId))
            .ExecuteDeleteAsync(cancellationToken);

        if (!await AreFriendsAsync(dbContext, characterId, otherId, cancellationToken))
            dbContext.Friends.Add(new DbFriend { CharacterId = characterId, FriendCharacterId = otherId, Created = DateTimeOffset.UtcNow });

        if (!await AreFriendsAsync(dbContext, otherId, characterId, cancellationToken))
            dbContext.Friends.Add(new DbFriend { CharacterId = otherId, FriendCharacterId = characterId, Created = DateTimeOffset.UtcNow });

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { relation = "friends" });
    }

    private static async Task<IResult> DeclineFriendRequestAsync(
        ulong id,
        ulong otherId,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (!await OwnsAsync(dbContext, userId, id, cancellationToken))
            return Results.NotFound();

        await dbContext.FriendRequests
            .Where(x => (x.CharacterId == otherId && x.TargetCharacterId == id) || (x.CharacterId == id && x.TargetCharacterId == otherId))
            .ExecuteDeleteAsync(cancellationToken);

        return Results.Ok(new { relation = "none" });
    }

    private static async Task<IResult> RemoveFriendAsync(
        ulong id,
        ulong otherId,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (!await OwnsAsync(dbContext, userId, id, cancellationToken))
            return Results.NotFound();

        await dbContext.Friends
            .Where(x => (x.CharacterId == id && x.FriendCharacterId == otherId) || (x.CharacterId == otherId && x.FriendCharacterId == id))
            .ExecuteDeleteAsync(cancellationToken);

        return Results.Ok(new { relation = "none" });
    }

    private static async Task<IResult> GetMessagesAsync(
        ulong id,
        ulong? @as,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var wallOwner = await dbContext.Characters.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.UserId }).SingleOrDefaultAsync(cancellationToken);

        if (wallOwner is null)
            return Results.NotFound();

        var userId = TryGetUserId(principal, out var parsed) ? parsed : 0;
        var isOwner = userId != 0 && wallOwner.UserId == userId;

        var access = await PrivacyEndpoints.GetAccessAsync(dbContext, id, wallOwner.UserId, userId, cancellationToken);

        if (!access.Messages)
            return Results.Json(new { error = "private" }, statusCode: StatusCodes.Status403Forbidden);

        List<ulong> ownCharacterIds = userId == 0
            ? []
            : await dbContext.Characters.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.Id).ToListAsync(cancellationToken);

        var messages = await dbContext.ProfileMessages
            .AsNoTracking()
            .Where(x => x.CharacterId == id)
            .OrderByDescending(x => (long)x.Id)
            .Take(MessagePageSize)
            .Select(x => new
            {
                x.Id,
                x.Text,
                x.Created,
                x.AuthorCharacterId,
                AuthorFirstName = x.AuthorCharacter.FirstName,
                AuthorLastName = x.AuthorCharacter.LastName
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(messages.Select(x => new
        {
            id = x.Id,
            text = x.Text,
            created = x.Created,
            author = new { id = x.AuthorCharacterId, firstName = x.AuthorFirstName, lastName = x.AuthorLastName },
            canDelete = isOwner || ownCharacterIds.Contains(x.AuthorCharacterId)
        }));
    }

    private static async Task<IResult> PostMessageAsync(
        ulong id,
        PostMessageRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        var text = request.Text?.Trim() ?? string.Empty;

        if (text.Length == 0 || text.Length > MaxMessageLength)
            return Results.Json(new { error = "length" }, statusCode: StatusCodes.Status400BadRequest);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (!await OwnsAsync(dbContext, userId, request.As, cancellationToken))
            return Results.BadRequest();

        if (!await dbContext.Characters.AnyAsync(x => x.Id == id, cancellationToken))
            return Results.NotFound();

        if (request.As != id && !await AreFriendsAsync(dbContext, request.As, id, cancellationToken))
            return Results.Json(new { error = "notFriends" }, statusCode: StatusCodes.Status403Forbidden);

        var message = new DbProfileMessage
        {
            CharacterId = id,
            AuthorCharacterId = request.As,
            Text = text,
            Created = DateTimeOffset.UtcNow
        };

        dbContext.ProfileMessages.Add(message);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { id = message.Id });
    }

    private static async Task<IResult> DeleteMessageAsync(
        ulong id,
        ulong messageId,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var deleted = await dbContext.ProfileMessages
            .Where(x => x.Id == messageId && x.CharacterId == id
                && (x.Character.UserId == userId || x.AuthorCharacter.UserId == userId))
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0 ? Results.Ok(new { deleted = messageId }) : Results.NotFound();
    }
}
