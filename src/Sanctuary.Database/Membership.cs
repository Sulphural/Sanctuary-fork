using System;
using System.Collections.Generic;
using System.Linq;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database;

public sealed record MembershipPlan(string Sku, string Name, string Description, string? MarketingDescription, int Months, int PriceStationCash)
{
    public bool IsLifetime => Months == 0;
}

public static class Membership
{
    public const int MemberStatus = 2;
    public const int FreeStatus = 0;
    public const int FreeCharacterLimit = 1;

    public static readonly IReadOnlyList<MembershipPlan> Plans =
    [
        new("FRREL-SU-IR0309-MEMBER-01MON", "1 Month", "Free Realms Membership", null, 1, 499),
        new("FRREL-SU-IR0309-MEMBER-03MON", "3 Month", "Free Realms Membership", "10% Savings", 3, 1299),
        new("FRREL-SU-IR0309-MEMBER-06MON", "6 Month", "Free Realms Membership", null, 6, 2450),
        new("FRREL-SU-IR0309-MEMBER-12MON", "12 Month", "Free Realms Membership", null, 12, 3950),
        new("FRREL-SU-IR0810-NEWMBR-LIFE", "Lifetime", "Best Value!", "Best Value!", 0, 3499)
    ];

    public static MembershipPlan? FindPlan(string? sku)
    {
        return Plans.FirstOrDefault(x => x.Sku == sku);
    }

    public static int CharacterLimit(DbUser user)
    {
        return StatusFor(user) == MemberStatus ? user.MaxCharacters : Math.Min(user.MaxCharacters, FreeCharacterLimit);
    }

    public static bool HasExpired(DbUser user)
    {
        return user.IsMember && user.MemberUntil is not null && user.MemberUntil <= DateTimeOffset.UtcNow;
    }

    public static bool IsLifetime(DbUser user)
    {
        return user.IsMember && user.MemberUntil is null;
    }

    public static void Set(DatabaseContext dbContext, DbUser user, bool member, DateTimeOffset? until)
    {
        user.IsMember = member;
        user.MemberUntil = member ? until : null;

        var status = member ? MemberStatus : FreeStatus;

        foreach (var character in dbContext.Characters.Where(x => x.UserId == user.Id))
            character.MembershipStatus = status;
    }

    public static void Extend(DatabaseContext dbContext, DbUser user, MembershipPlan plan)
    {
        ExpireIfDue(dbContext, user);

        if (plan.IsLifetime)
        {
            Set(dbContext, user, true, null);
            return;
        }

        var from = user.IsMember && user.MemberUntil is not null ? user.MemberUntil.Value : DateTimeOffset.UtcNow;

        Set(dbContext, user, true, from.AddMonths(plan.Months));
    }

    public static bool ExpireIfDue(DatabaseContext dbContext, DbUser user)
    {
        if (!HasExpired(user))
            return false;

        Set(dbContext, user, false, null);

        return true;
    }

    public static int StatusFor(DbUser user)
    {
        return user.IsMember && !HasExpired(user) ? MemberStatus : FreeStatus;
    }
}
