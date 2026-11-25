using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.WebAPI.Website;

namespace Sanctuary.WebAPI.Endpoints;

public static class StaffEndpoints
{
    private static ILogger _logger = null!;

    public static void MapStaffEndpoints(this RouteGroupBuilder group, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger(nameof(StaffEndpoints));

        group.MapPost("/characters/{id}/messages/{messageId}/report", ReportMessageAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);

        group.MapGet("/staff/reports", GetReportsAsync).RequireAuthorization();
        group.MapPost("/staff/reports/{messageId}/dismiss", DismissReportsAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/staff/messages/{messageId}/delete", DeleteMessageAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);

        group.MapGet("/staff/names", GetNamesAsync).RequireAuthorization();
        group.MapPost("/staff/names/{characterId}/approve", ApproveNameAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/staff/names/{characterId}/reject", RejectNameAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out ulong userId)
    {
        return ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    private static Task<DbUser?> GetStaffAsync(DatabaseContext dbContext, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        return StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken);
    }

    private static async Task<IResult> ReportMessageAsync(
        ulong id,
        ulong messageId,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var message = await dbContext.ProfileMessages
            .AsNoTracking()
            .Where(x => x.Id == messageId && x.CharacterId == id)
            .Select(x => new { WallOwnerUserId = x.Character.UserId, AuthorUserId = x.AuthorCharacter.UserId })
            .SingleOrDefaultAsync(cancellationToken);

        if (message is null)
            return Results.NotFound();

        var access = await PrivacyEndpoints.GetAccessAsync(dbContext, id, message.WallOwnerUserId, userId, cancellationToken);

        if (!access.Messages || message.AuthorUserId == userId)
            return Results.NotFound();

        if (!await dbContext.MessageReports.AnyAsync(x => x.MessageId == messageId && x.ReporterUserId == userId, cancellationToken))
        {
            dbContext.MessageReports.Add(new DbMessageReport { MessageId = messageId, ReporterUserId = userId, Created = DateTimeOffset.UtcNow });

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(new { reported = true });
    }

    private static async Task<IResult> GetReportsAsync(
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await GetStaffAsync(dbContext, principal, cancellationToken) is null)
            return Results.Forbid();

        var reports = await dbContext.MessageReports
            .AsNoTracking()
            .Select(x => new
            {
                x.MessageId,
                x.Created,
                Reporter = x.ReporterUser.Username,
                x.Message.Text,
                MessageCreated = x.Message.Created,
                WallId = x.Message.CharacterId,
                WallFirstName = x.Message.Character.FirstName,
                WallLastName = x.Message.Character.LastName,
                AuthorId = x.Message.AuthorCharacterId,
                AuthorFirstName = x.Message.AuthorCharacter.FirstName,
                AuthorLastName = x.Message.AuthorCharacter.LastName,
                AuthorUsername = x.Message.AuthorCharacter.User.Username
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(reports
            .GroupBy(x => x.MessageId)
            .Select(g =>
            {
                var first = g.First();

                return new
                {
                    messageId = g.Key,
                    text = first.Text,
                    created = first.MessageCreated,
                    wall = new { id = first.WallId, firstName = first.WallFirstName, lastName = first.WallLastName },
                    author = new { id = first.AuthorId, firstName = first.AuthorFirstName, lastName = first.AuthorLastName, username = first.AuthorUsername },
                    reporters = g.Select(x => x.Reporter).OrderBy(x => x).ToList(),
                    lastReported = g.Max(x => x.Created)
                };
            })
            .OrderByDescending(x => x.reporters.Count)
            .ThenByDescending(x => x.lastReported));
    }

    private static async Task<IResult> DismissReportsAsync(
        ulong messageId,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var staff = await GetStaffAsync(dbContext, principal, cancellationToken);

        if (staff is null)
            return Results.Forbid();

        var message = await dbContext.ProfileMessages
            .AsNoTracking()
            .Where(x => x.Id == messageId)
            .Select(x => new { x.Text, x.AuthorCharacterId, AuthorUserId = x.AuthorCharacter.UserId, x.AuthorCharacter.FullName })
            .SingleOrDefaultAsync(cancellationToken);

        var dismissed = await dbContext.MessageReports.Where(x => x.MessageId == messageId).ExecuteDeleteAsync(cancellationToken);

        StaffAccess.Log(dbContext, staff, "dismissMessageReports", message?.AuthorUserId, message?.AuthorCharacterId, message?.FullName, message?.Text);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Staff {Username} dismissed {Count} reports on message {MessageId}.", staff.Username, dismissed, messageId);

        return Results.Ok(new { dismissed });
    }

    private static async Task<IResult> DeleteMessageAsync(
        ulong messageId,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var staff = await GetStaffAsync(dbContext, principal, cancellationToken);

        if (staff is null)
            return Results.Forbid();

        var message = await dbContext.ProfileMessages
            .AsNoTracking()
            .Where(x => x.Id == messageId)
            .Select(x => new { x.Text, x.AuthorCharacterId, AuthorUserId = x.AuthorCharacter.UserId, x.AuthorCharacter.FullName })
            .SingleOrDefaultAsync(cancellationToken);

        if (message is not null && !await StaffAccess.CanActOnUserAsync(dbContext, staff, message.AuthorUserId, cancellationToken))
            return Results.Json(new { error = "notAllowed" }, statusCode: StatusCodes.Status403Forbidden);

        var deleted = await dbContext.ProfileMessages.Where(x => x.Id == messageId).ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            StaffAccess.Log(dbContext, staff, "deleteMessage", message?.AuthorUserId, message?.AuthorCharacterId, message?.FullName, message?.Text);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Staff {Username} deleted profile message {MessageId}.", staff.Username, messageId);

        return deleted > 0 ? Results.Ok(new { deleted = messageId }) : Results.NotFound();
    }

    private static async Task<IResult> GetNamesAsync(
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await GetStaffAsync(dbContext, principal, cancellationToken) is null)
            return Results.Forbid();

        var names = await dbContext.NameReviews
            .AsNoTracking()
            .Select(x => new
            {
                characterId = x.CharacterId,
                requested = new { firstName = x.RequestedFirstName, lastName = x.RequestedLastName },
                current = new { firstName = x.Character.FirstName, lastName = x.Character.LastName },
                username = x.Character.User.Username,
                created = x.Created
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(names.OrderBy(x => x.created));
    }

    private static async Task<IResult> ApproveNameAsync(
        ulong characterId,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var staff = await GetStaffAsync(dbContext, principal, cancellationToken);

        if (staff is null)
            return Results.Forbid();

        var review = await dbContext.NameReviews.SingleOrDefaultAsync(x => x.CharacterId == characterId, cancellationToken);
        var character = await dbContext.Characters.SingleOrDefaultAsync(x => x.Id == characterId, cancellationToken);

        if (review is null || character is null)
            return Results.NotFound();

        if (await PendingNames.IsTakenAsync(dbContext, review.RequestedFirstName, review.RequestedLastName, characterId, cancellationToken))
            return Results.Json(new { error = "taken" }, statusCode: StatusCodes.Status409Conflict);

        var oldName = FullName(character.FirstName, character.LastName);

        character.FirstName = review.RequestedFirstName;
        character.LastName = review.RequestedLastName;
        dbContext.NameReviews.Remove(review);

        StaffAccess.Log(dbContext, staff, "approveName", character.UserId, characterId, FullName(character.FirstName, character.LastName), "Was " + oldName);

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Staff {Username} approved the name {FirstName} {LastName} for character {CharacterId}.", staff.Username, character.FirstName, character.LastName, characterId);

        return Results.Ok(new { characterId, firstName = character.FirstName, lastName = character.LastName });
    }

    private static async Task<IResult> RejectNameAsync(
        ulong characterId,
        ClaimsPrincipal principal,
        CharacterNames names,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var staff = await GetStaffAsync(dbContext, principal, cancellationToken);

        if (staff is null)
            return Results.Forbid();

        var review = await dbContext.NameReviews.SingleOrDefaultAsync(x => x.CharacterId == characterId, cancellationToken);
        var character = await dbContext.Characters.SingleOrDefaultAsync(x => x.Id == characterId, cancellationToken);

        if (review is null || character is null)
            return Results.NotFound();

        var stillCustom = character.FirstName == review.RequestedFirstName && (character.LastName ?? string.Empty) == review.RequestedLastName;

        if (stillCustom)
        {
            var randomName = await PendingNames.PickFreeReelNameAsync(dbContext, names, character.Gender == 1, cancellationToken);

            if (randomName is null)
                return Results.Json(new { error = "noFreeName" }, statusCode: StatusCodes.Status409Conflict);

            character.FirstName = randomName.Value.FirstName;
            character.LastName = randomName.Value.LastName;
        }

        dbContext.NameReviews.Remove(review);

        StaffAccess.Log(dbContext, staff, "rejectName", character.UserId, characterId, FullName(character.FirstName, character.LastName), "Requested " + FullName(review.RequestedFirstName, review.RequestedLastName));

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Staff {Username} rejected the name {RequestedFirstName} {RequestedLastName} for character {CharacterId}, which keeps the name {FirstName} {LastName}.", staff.Username, review.RequestedFirstName, review.RequestedLastName, characterId, character.FirstName, character.LastName);

        return Results.Ok(new { characterId, firstName = character.FirstName, lastName = character.LastName });
    }

    public static string FullName(string firstName, string? lastName) => string.IsNullOrEmpty(lastName) ? firstName : $"{firstName} {lastName}";
}
