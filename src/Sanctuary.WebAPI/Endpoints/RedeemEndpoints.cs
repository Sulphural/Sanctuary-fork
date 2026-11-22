using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game;
using Sanctuary.WebAPI.Website;

namespace Sanctuary.WebAPI.Endpoints;

public static partial class RedeemEndpoints
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static ILogger _logger = null!;

    public sealed record RedeemRequest(string? Code);
    public sealed record CreateCodeRequest(string? Code, string? Description, int Coins, int StationCash, int? ItemDefinition, int ItemCount, int MaxUses, int? ExpiresInDays);

    [GeneratedRegex("^[A-Z0-9-]{4,32}$")]
    private static partial Regex CodePattern();

    public static void MapRedeemEndpoints(this RouteGroupBuilder group, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger(nameof(RedeemEndpoints));

        group.MapPost("/characters/{id}/redeem", RedeemAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.AuthPolicy);
        group.MapGet("/rewards/pending", PendingRewardsAsync).RequireAuthorization();
        group.MapGet("/items/{id}/icon", ItemIcon).RequireRateLimiting(WebsiteSecurity.LookupPolicy);

        group.MapGet("/staff/codes", ListCodesAsync).RequireAuthorization();
        group.MapPost("/staff/codes", CreateCodeAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/staff/codes/{code}/delete", DeleteCodeAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapGet("/staff/items", SearchItemsAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.LookupPolicy);

        group.MapGet("/staff/backups", ListBackupsAsync).RequireAuthorization();
        group.MapPost("/staff/backups", BackupNowAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out ulong userId)
    {
        return ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    private static string Normalize(string? code) => (code ?? string.Empty).Trim().Replace(" ", string.Empty).ToUpperInvariant();

    private static string GenerateCode()
    {
        var chars = new char[14];

        for (var i = 0; i < chars.Length; i++)
            chars[i] = i is 4 or 9 ? '-' : CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];

        return new string(chars);
    }

    private static async Task<DbUser?> GetAdminAsync(DatabaseContext dbContext, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var staff = await StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken);

        return staff is not null && staff.IsAdmin ? staff : null;
    }

    private static List<string> RewardParts(DbRedeemCode code, ItemNames itemNames)
    {
        var parts = new List<string>();

        if (code.ItemDefinition is not null && code.ItemCount > 0)
            parts.Add(code.ItemCount == 1 ? itemNames.NameOf(code.ItemDefinition.Value) : $"{code.ItemCount} x {itemNames.NameOf(code.ItemDefinition.Value)}");

        if (code.Coins > 0)
            parts.Add($"{code.Coins:N0} coins");

        if (code.StationCash > 0)
            parts.Add($"{code.StationCash:N0} Station Cash");

        return parts;
    }

    private static async Task<IResult> RedeemAsync(
        ulong id,
        RedeemRequest request,
        ClaimsPrincipal principal,
        ItemNames itemNames,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (!await dbContext.Characters.AnyAsync(x => x.Id == id && x.UserId == userId, cancellationToken))
            return Results.NotFound();

        var outcome = CodeRedeemer.Redeem(dbContext, userId, id, request.Code);

        if (!outcome.Success)
            return Results.Json(new { error = outcome.Error }, statusCode: StatusCodes.Status400BadRequest);

        var code = outcome.Code!;

        if (code.Coins > 0)
        {
            var coins = code.Coins;
            await dbContext.Characters
                .Where(x => x.Id == id)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.Coins, c => c.Coins > int.MaxValue - coins ? int.MaxValue : c.Coins + coins), cancellationToken);
        }

        if (code.StationCash > 0)
        {
            var stationCash = code.StationCash;
            await dbContext.Characters
                .Where(x => x.Id == id)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.StationCash, c => c.StationCash > int.MaxValue - stationCash ? int.MaxValue : c.StationCash + stationCash), cancellationToken);
        }

        _logger.LogInformation("User {UserId} redeemed code {Code} on character {CharacterId}.", userId, code.Code, id);

        return Results.Ok(new
        {
            description = code.Description,
            rewards = RewardParts(code, itemNames),
            waitingInGame = outcome.PendingReward is null ? null : itemNames.NameOf(outcome.PendingReward.ItemDefinition),
            item = outcome.PendingReward is null ? null : new { id = outcome.PendingReward.ItemDefinition, name = itemNames.NameOf(outcome.PendingReward.ItemDefinition), quantity = outcome.PendingReward.Quantity },
            coins = code.Coins,
            stationCash = code.StationCash
        });
    }

    private static IResult ItemIcon(int id, HttpContext httpContext, ItemIcons itemIcons)
    {
        var png = itemIcons.GetIconPng(id);

        if (png is null)
            return Results.NotFound();

        httpContext.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.File(png, "image/png");
    }

    private static async Task<IResult> PendingRewardsAsync(
        ClaimsPrincipal principal,
        ItemNames itemNames,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var rewards = await CodeRedeemer.Unclaimed(dbContext, userId)
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return Results.Ok(rewards.Select(x => new
        {
            id = x.Id,
            itemDefinition = x.ItemDefinition,
            item = itemNames.NameOf(x.ItemDefinition),
            quantity = x.Quantity,
            source = x.Source,
            created = x.Created
        }));
    }

    private static async Task<IResult> ListCodesAsync(
        ClaimsPrincipal principal,
        ItemNames itemNames,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await GetAdminAsync(dbContext, principal, cancellationToken) is null)
            return Results.Forbid();

        var codes = await dbContext.RedeemCodes.AsNoTracking().ToListAsync(cancellationToken);

        return Results.Ok(codes
            .OrderByDescending(x => x.Created)
            .Select(x => new
            {
                code = x.Code,
                description = x.Description,
                rewards = RewardParts(x, itemNames),
                uses = x.Uses,
                maxUses = x.MaxUses,
                expiresAt = x.ExpiresAt,
                expired = x.ExpiresAt is not null && x.ExpiresAt <= DateTimeOffset.UtcNow,
                createdBy = x.CreatedBy,
                created = x.Created
            }));
    }

    private static async Task<IResult> CreateCodeAsync(
        CreateCodeRequest request,
        ClaimsPrincipal principal,
        ItemNames itemNames,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var admin = await GetAdminAsync(dbContext, principal, cancellationToken);

        if (admin is null)
            return Results.Forbid();

        var description = request.Description?.Trim() ?? string.Empty;

        if (description.Length == 0 || description.Length > 100)
            return Results.Json(new { error = "description" }, statusCode: StatusCodes.Status400BadRequest);

        if (request.Coins < 0 || request.StationCash < 0 || request.ItemCount < 0 || request.MaxUses < 0 || request.ExpiresInDays is < 1)
            return Results.Json(new { error = "numbers" }, statusCode: StatusCodes.Status400BadRequest);

        var hasItem = request.ItemDefinition is not null && request.ItemCount > 0;

        if (hasItem && !itemNames.IsGrantable(request.ItemDefinition!.Value))
            return Results.Json(new { error = "item" }, statusCode: StatusCodes.Status400BadRequest);

        if (!hasItem && request.Coins == 0 && request.StationCash == 0)
            return Results.Json(new { error = "reward" }, statusCode: StatusCodes.Status400BadRequest);

        var codeText = string.IsNullOrWhiteSpace(request.Code) ? GenerateCode() : Normalize(request.Code);

        if (!CodePattern().IsMatch(codeText))
            return Results.Json(new { error = "code" }, statusCode: StatusCodes.Status400BadRequest);

        if (await dbContext.RedeemCodes.AnyAsync(x => x.Code == codeText, cancellationToken))
            return Results.Json(new { error = "codeTaken" }, statusCode: StatusCodes.Status409Conflict);

        var code = new DbRedeemCode
        {
            Code = codeText,
            Description = description,
            Coins = request.Coins,
            StationCash = request.StationCash,
            ItemDefinition = hasItem ? request.ItemDefinition : null,
            ItemCount = hasItem ? request.ItemCount : 0,
            MaxUses = request.MaxUses,
            ExpiresAt = request.ExpiresInDays is null ? null : DateTimeOffset.UtcNow.AddDays(request.ExpiresInDays.Value),
            CreatedBy = admin.Username,
            Created = DateTimeOffset.UtcNow
        };

        dbContext.RedeemCodes.Add(code);
        StaffAccess.Log(dbContext, admin, "createCode", null, null, codeText, description + ": " + string.Join(", ", RewardParts(code, itemNames)));

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { code = codeText });
    }

    private static async Task<IResult> DeleteCodeAsync(
        string code,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var admin = await GetAdminAsync(dbContext, principal, cancellationToken);

        if (admin is null)
            return Results.Forbid();

        var codeText = Normalize(code);
        var existing = await dbContext.RedeemCodes.SingleOrDefaultAsync(x => x.Code == codeText, cancellationToken);

        if (existing is null)
            return Results.NotFound();

        dbContext.RedeemCodes.Remove(existing);
        StaffAccess.Log(dbContext, admin, "deleteCode", null, null, codeText, existing.Description);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { deleted = codeText });
    }

    private static async Task<IResult> SearchItemsAsync(
        string? q,
        ClaimsPrincipal principal,
        ItemNames itemNames,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await GetAdminAsync(dbContext, principal, cancellationToken) is null)
            return Results.Forbid();

        var query = q?.Trim() ?? string.Empty;

        if (query.Length < 2)
            return Results.Ok(Array.Empty<object>());

        return Results.Ok(itemNames.Search(query, 25).Select(x => new { id = x.Id, name = x.Name }));
    }

    private static async Task<IResult> ListBackupsAsync(
        ClaimsPrincipal principal,
        DatabaseBackupService backups,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await GetAdminAsync(dbContext, principal, cancellationToken) is null)
            return Results.Forbid();

        return Results.Ok(new
        {
            enabled = backups.Enabled,
            backups = backups.List().Select(x => new { name = x.Name, size = x.Size, created = x.Created })
        });
    }

    private static async Task<IResult> BackupNowAsync(
        ClaimsPrincipal principal,
        DatabaseBackupService backups,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var admin = await GetAdminAsync(dbContext, principal, cancellationToken);

        if (admin is null)
            return Results.Forbid();

        if (!backups.Enabled)
            return Results.Json(new { error = "disabled" }, statusCode: StatusCodes.Status409Conflict);

        var backup = await backups.BackupNowAsync(cancellationToken);

        StaffAccess.Log(dbContext, admin, "backup", null, null, backup?.Name, null);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { name = backup?.Name, size = backup?.Size, created = backup?.Created });
    }
}
