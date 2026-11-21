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
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.WebAPI.Website;

namespace Sanctuary.WebAPI.Endpoints;

public static class StaffPlayerEndpoints
{
    private const int MaxReasonLength = 500;

    private static ILogger _logger = null!;

    public sealed record DurationRequest(int? Minutes, string? Reason);
    public sealed record RoleRequest(string? Role);
    public sealed record ReportCharacterRequest(string? Reason);

    public static void MapStaffPlayerEndpoints(this RouteGroupBuilder group, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger(nameof(StaffPlayerEndpoints));

        group.MapPost("/characters/{id}/report", ReportCharacterAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);

        group.MapGet("/staff/players", SearchPlayersAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.LookupPolicy);
        group.MapGet("/staff/players/{userId}", GetPlayerAsync).RequireAuthorization();
        group.MapPost("/staff/players/{userId}/ban", BanAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/staff/players/{userId}/unban", UnbanAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/staff/players/{userId}/mute", MuteAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/staff/players/{userId}/unmute", UnmuteAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/staff/players/{userId}/role", SetRoleAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);

        group.MapGet("/staff/player-reports", GetPlayerReportsAsync).RequireAuthorization();
        group.MapPost("/staff/player-reports/{characterId}/dismiss", DismissPlayerReportsAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);

        group.MapGet("/staff/log", GetLogAsync).RequireAuthorization();
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out ulong userId)
    {
        return ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    private static bool IsActive(DateTimeOffset? until) => until is not null && until > DateTimeOffset.UtcNow;

    private static DateTimeOffset? ActiveOrNull(DateTimeOffset? until) => IsActive(until) ? until : null;

    private static string? CleanReason(string? reason)
    {
        var trimmed = reason?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > MaxReasonLength ? trimmed[..MaxReasonLength] : trimmed;
    }

    private static string DurationText(int? minutes)
    {
        if (minutes is null)
            return "permanently";

        if (minutes % 1440 == 0)
            return minutes / 1440 == 1 ? "for 1 day" : $"for {minutes / 1440} days";

        if (minutes % 60 == 0)
            return minutes / 60 == 1 ? "for 1 hour" : $"for {minutes / 60} hours";

        return minutes == 1 ? "for 1 minute" : $"for {minutes} minutes";
    }

    private static async Task<IResult> ReportCharacterAsync(
        ulong id,
        ReportCharacterRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        var reason = CleanReason(request.Reason);

        if (reason is null)
            return Results.Json(new { error = "reason" }, statusCode: StatusCodes.Status400BadRequest);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var character = await dbContext.Characters.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.UserId }).SingleOrDefaultAsync(cancellationToken);

        if (character is null || character.UserId == userId)
            return Results.NotFound();

        var report = await dbContext.PlayerReports.SingleOrDefaultAsync(x => x.CharacterId == id && x.ReporterUserId == userId, cancellationToken);

        if (report is null)
        {
            dbContext.PlayerReports.Add(new DbPlayerReport { CharacterId = id, ReporterUserId = userId, Reason = reason, Created = DateTimeOffset.UtcNow });
        }
        else
        {
            report.Reason = reason;
            report.Created = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { reported = true });
    }

    private static async Task<IResult> SearchPlayersAsync(
        string? q,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken) is null)
            return Results.Forbid();

        var query = q?.Trim();

        if (string.IsNullOrEmpty(query) || query.Length < 2 || query.Length > 64)
            return Results.Ok(Array.Empty<object>());

        var pattern = "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

        var users = await dbContext.Users
            .AsNoTracking()
            .Where(u => EF.Functions.Like(u.Username, pattern, "\\")
                || u.Characters.Any(c => EF.Functions.Like(c.FirstName, pattern, "\\")
                    || (c.LastName != null && EF.Functions.Like(c.LastName, pattern, "\\"))
                    || (c.FullName != null && EF.Functions.Like(c.FullName, pattern, "\\"))))
            .OrderBy(u => u.Username)
            .Take(25)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.IsAdmin,
                u.IsMod,
                u.LockedUntil,
                u.MutedUntil,
                Characters = u.Characters.Select(c => new { c.Id, c.FirstName, c.LastName }).ToList()
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(users.Select(u => new
        {
            userId = u.Id,
            username = u.Username,
            role = u.IsAdmin ? "admin" : u.IsMod ? "moderator" : "player",
            bannedUntil = ActiveOrNull(u.LockedUntil),
            mutedUntil = ActiveOrNull(u.MutedUntil),
            characters = u.Characters.OrderBy(c => c.Id).Select(c => new { id = c.Id, firstName = c.FirstName, lastName = c.LastName })
        }));
    }

    private static async Task<IResult> GetPlayerAsync(
        ulong userId,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var staff = await StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken);

        if (staff is null)
            return Results.Forbid();

        var user = await dbContext.Users.AsNoTracking().Include(x => x.Characters).SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
            return Results.NotFound();

        var characterIds = user.Characters.Select(x => x.Id).ToList();

        var pendingNames = await dbContext.NameReviews
            .AsNoTracking()
            .Where(x => characterIds.Contains(x.CharacterId))
            .ToDictionaryAsync(x => x.CharacterId, cancellationToken);

        var playerReports = await dbContext.PlayerReports
            .AsNoTracking()
            .Where(x => characterIds.Contains(x.CharacterId))
            .Select(x => new { x.CharacterId, x.Character.FirstName, x.Character.LastName, Reporter = x.ReporterUser.Username, x.Reason, x.Created })
            .ToListAsync(cancellationToken);

        var reportedMessages = await dbContext.MessageReports
            .AsNoTracking()
            .Where(x => characterIds.Contains(x.Message.AuthorCharacterId))
            .Select(x => x.MessageId)
            .Distinct()
            .CountAsync(cancellationToken);

        var history = await dbContext.StaffActions
            .AsNoTracking()
            .Where(x => x.TargetUserId == userId)
            .OrderByDescending(x => (long)x.Id)
            .Take(30)
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            userId = user.Id,
            username = user.Username,
            role = StaffAccess.RoleName(user),
            canModerate = StaffAccess.CanActOn(staff, user),
            canChangeRole = staff.IsAdmin && !user.IsAdmin && user.Id != staff.Id,
            email = staff.IsAdmin ? user.Email : null,
            country = user.Country,
            isMember = user.IsMember,
            created = user.Created,
            lastLogin = user.LastLogin,
            bannedUntil = ActiveOrNull(user.LockedUntil),
            mutedUntil = ActiveOrNull(user.MutedUntil),
            characters = user.Characters.OrderBy(x => x.Id).Select(x => new
            {
                id = x.Id,
                firstName = x.FirstName,
                lastName = x.LastName,
                model = x.Model,
                gender = x.Gender,
                created = x.Created,
                lastLogin = x.LastLogin,
                pendingName = pendingNames.TryGetValue(x.Id, out var review)
                    ? new { firstName = review.RequestedFirstName, lastName = review.RequestedLastName }
                    : null
            }),
            playerReports = playerReports
                .OrderByDescending(x => x.Created)
                .Select(x => new { characterId = x.CharacterId, character = StaffEndpoints.FullName(x.FirstName, x.LastName), reporter = x.Reporter, reason = x.Reason, created = x.Created }),
            reportedMessages,
            history = history.Select(LogEntry)
        });
    }

    private static async Task<(DbUser? Staff, DbUser? Target, IResult? Error)> LoadTargetAsync(DatabaseContext dbContext, ClaimsPrincipal principal, ulong userId, CancellationToken cancellationToken)
    {
        var staff = await StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken);

        if (staff is null)
            return (null, null, Results.Forbid());

        var target = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (target is null)
            return (staff, null, Results.NotFound());

        if (!StaffAccess.CanActOn(staff, target))
            return (staff, target, Results.Json(new { error = "notAllowed" }, statusCode: StatusCodes.Status403Forbidden));

        return (staff, target, null);
    }

    private static async Task<IResult> BanAsync(
        ulong userId,
        DurationRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (request.Minutes is <= 0)
            return Results.Json(new { error = "duration" }, statusCode: StatusCodes.Status400BadRequest);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var (staff, target, error) = await LoadTargetAsync(dbContext, principal, userId, cancellationToken);

        if (error is not null)
            return error;

        target!.LockedUntil = request.Minutes is null ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddMinutes(request.Minutes.Value);
        target.Session = null;
        target.SessionCreated = null;

        var reason = CleanReason(request.Reason);
        StaffAccess.Log(dbContext, staff!, "ban", target.Id, null, target.Username, "Banned " + DurationText(request.Minutes) + (reason is null ? "." : ": " + reason));

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Staff {Staff} banned {Username} {Duration}.", staff!.Username, target.Username, DurationText(request.Minutes));

        return Results.Ok(new { bannedUntil = target.LockedUntil });
    }

    private static async Task<IResult> UnbanAsync(
        ulong userId,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var (staff, target, error) = await LoadTargetAsync(dbContext, principal, userId, cancellationToken);

        if (error is not null)
            return error;

        target!.LockedUntil = null;
        StaffAccess.Log(dbContext, staff!, "unban", target.Id, null, target.Username, null);

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Staff {Staff} unbanned {Username}.", staff!.Username, target.Username);

        return Results.Ok(new { bannedUntil = (DateTimeOffset?)null });
    }

    private static async Task<IResult> MuteAsync(
        ulong userId,
        DurationRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (request.Minutes is null or <= 0)
            return Results.Json(new { error = "duration" }, statusCode: StatusCodes.Status400BadRequest);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var (staff, target, error) = await LoadTargetAsync(dbContext, principal, userId, cancellationToken);

        if (error is not null)
            return error;

        target!.MutedUntil = DateTimeOffset.UtcNow.AddMinutes(request.Minutes.Value);

        var reason = CleanReason(request.Reason);
        StaffAccess.Log(dbContext, staff!, "mute", target.Id, null, target.Username, "Muted " + DurationText(request.Minutes) + (reason is null ? "." : ": " + reason));

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Staff {Staff} muted {Username} {Duration}.", staff!.Username, target.Username, DurationText(request.Minutes));

        return Results.Ok(new { mutedUntil = target.MutedUntil });
    }

    private static async Task<IResult> UnmuteAsync(
        ulong userId,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var (staff, target, error) = await LoadTargetAsync(dbContext, principal, userId, cancellationToken);

        if (error is not null)
            return error;

        target!.MutedUntil = null;
        StaffAccess.Log(dbContext, staff!, "unmute", target.Id, null, target.Username, null);

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Staff {Staff} unmuted {Username}.", staff!.Username, target.Username);

        return Results.Ok(new { mutedUntil = (DateTimeOffset?)null });
    }

    private static async Task<IResult> SetRoleAsync(
        ulong userId,
        RoleRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (request.Role is not ("moderator" or "player"))
            return Results.BadRequest();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var staff = await StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken);

        if (staff is null || !staff.IsAdmin)
            return Results.Forbid();

        var target = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (target is null)
            return Results.NotFound();

        if (target.IsAdmin || target.Id == staff.Id)
            return Results.Json(new { error = "notAllowed" }, statusCode: StatusCodes.Status403Forbidden);

        var makeModerator = request.Role == "moderator";

        if (target.IsMod != makeModerator)
        {
            target.IsMod = makeModerator;
            StaffAccess.Log(dbContext, staff, makeModerator ? "promote" : "demote", target.Id, null, target.Username, makeModerator ? "Made a moderator." : "No longer a moderator.");

            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Staff {Staff} {Change} {Username}.", staff.Username, makeModerator ? "promoted" : "demoted", target.Username);
        }

        return Results.Ok(new { role = StaffAccess.RoleName(target) });
    }

    private static async Task<IResult> GetPlayerReportsAsync(
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken) is null)
            return Results.Forbid();

        var reports = await dbContext.PlayerReports
            .AsNoTracking()
            .Select(x => new
            {
                x.CharacterId,
                x.Character.FirstName,
                x.Character.LastName,
                x.Character.UserId,
                OwnerUsername = x.Character.User.Username,
                Reporter = x.ReporterUser.Username,
                x.Reason,
                x.Created
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(reports
            .GroupBy(x => x.CharacterId)
            .Select(g =>
            {
                var first = g.First();

                return new
                {
                    characterId = g.Key,
                    firstName = first.FirstName,
                    lastName = first.LastName,
                    userId = first.UserId,
                    username = first.OwnerUsername,
                    reports = g.OrderByDescending(x => x.Created).Select(x => new { reporter = x.Reporter, reason = x.Reason, created = x.Created }).ToList(),
                    lastReported = g.Max(x => x.Created)
                };
            })
            .OrderByDescending(x => x.reports.Count)
            .ThenByDescending(x => x.lastReported));
    }

    private static async Task<IResult> DismissPlayerReportsAsync(
        ulong characterId,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var staff = await StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken);

        if (staff is null)
            return Results.Forbid();

        var character = await dbContext.Characters.AsNoTracking().Where(x => x.Id == characterId).Select(x => new { x.UserId, x.FullName }).SingleOrDefaultAsync(cancellationToken);
        var dismissed = await dbContext.PlayerReports.Where(x => x.CharacterId == characterId).ExecuteDeleteAsync(cancellationToken);

        if (dismissed > 0)
        {
            StaffAccess.Log(dbContext, staff, "dismissPlayerReports", character?.UserId, characterId, character?.FullName, dismissed == 1 ? "1 report" : $"{dismissed} reports");
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(new { dismissed });
    }

    private static async Task<IResult> GetLogAsync(
        ulong? before,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken) is null)
            return Results.Forbid();

        var query = dbContext.StaffActions.AsNoTracking();

        if (before is not null)
            query = query.Where(x => x.Id < before.Value);

        var entries = await query.OrderByDescending(x => (long)x.Id).Take(50).ToListAsync(cancellationToken);

        return Results.Ok(entries.Select(LogEntry));
    }

    private static object LogEntry(DbStaffAction x) => new
    {
        id = x.Id,
        created = x.Created,
        staff = x.StaffUsername,
        action = x.Action,
        targetUserId = x.TargetUserId,
        targetCharacterId = x.TargetCharacterId,
        targetName = x.TargetName,
        details = x.Details
    };
}
