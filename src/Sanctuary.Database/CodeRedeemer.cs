using System;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database;

public sealed record RedeemOutcome(string? Error, DbRedeemCode? Code, DbPendingReward? PendingReward)
{
    public bool Success => Error is null;
}

public static partial class CodeRedeemer
{
    public const string Invalid = "invalid";
    public const string Expired = "expired";
    public const string AlreadyRedeemed = "alreadyRedeemed";
    public const string UsedUp = "usedUp";

    [GeneratedRegex("^[A-Z0-9-]{4,32}$")]
    private static partial Regex CodePattern();

    public static string Normalize(string? code) => (code ?? string.Empty).Trim().Replace(" ", string.Empty).ToUpperInvariant();

    public static RedeemOutcome Redeem(DatabaseContext dbContext, ulong userId, ulong characterId, string? typedCode)
    {
        var codeText = Normalize(typedCode);

        if (!CodePattern().IsMatch(codeText))
            return new RedeemOutcome(Invalid, null, null);

        using var transaction = dbContext.Database.BeginTransaction();

        var code = dbContext.RedeemCodes.AsNoTracking().SingleOrDefault(x => x.Code == codeText);

        if (code is null)
            return new RedeemOutcome(Invalid, null, null);

        if (code.ExpiresAt is not null && code.ExpiresAt <= DateTimeOffset.UtcNow)
            return new RedeemOutcome(Expired, code, null);

        if (dbContext.CodeRedemptions.Any(x => x.Code == codeText && x.UserId == userId))
            return new RedeemOutcome(AlreadyRedeemed, code, null);

        var claimed = dbContext.RedeemCodes
            .Where(x => x.Code == codeText && (x.MaxUses == 0 || x.Uses < x.MaxUses))
            .ExecuteUpdate(x => x.SetProperty(c => c.Uses, c => c.Uses + 1));

        if (claimed == 0)
            return new RedeemOutcome(UsedUp, code, null);

        dbContext.CodeRedemptions.Add(new DbCodeRedemption { Code = codeText, UserId = userId, CharacterId = characterId, Created = DateTimeOffset.UtcNow });

        DbPendingReward? pending = null;

        if (code.ItemDefinition is not null && code.ItemCount > 0)
        {
            pending = new DbPendingReward
            {
                UserId = userId,
                ItemDefinition = code.ItemDefinition.Value,
                Quantity = code.ItemCount,
                Source = string.IsNullOrWhiteSpace(code.Description) ? "Redeemed code" : code.Description,
                Code = codeText,
                Created = DateTimeOffset.UtcNow
            };

            dbContext.PendingRewards.Add(pending);
        }

        dbContext.SaveChanges();
        transaction.Commit();

        return new RedeemOutcome(null, code, pending);
    }

    public static IQueryable<DbPendingReward> Unclaimed(DatabaseContext dbContext, ulong userId)
    {
        return dbContext.PendingRewards.Where(x => x.UserId == userId && x.ClaimedAt == null);
    }
}
