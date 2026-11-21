using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;
using Sanctuary.Database.Entities;

namespace Sanctuary.WebAPI.Website;

public static class StaffAccess
{
    public const int Player = 0;
    public const int Moderator = 1;
    public const int Admin = 2;

    public static int RoleOf(DbUser user) => user.IsAdmin ? Admin : user.IsMod ? Moderator : Player;

    public static string RoleName(DbUser user) => user.IsAdmin ? "admin" : user.IsMod ? "moderator" : "player";

    public static bool CanActOn(DbUser staff, DbUser target) => RoleOf(staff) > RoleOf(target);

    public static async Task<DbUser?> GetStaffAsync(DatabaseContext dbContext, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return null;

        return await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && (x.IsAdmin || x.IsMod), cancellationToken);
    }

    public static void Log(DatabaseContext dbContext, DbUser staff, string action, ulong? targetUserId, ulong? targetCharacterId, string? targetName, string? details)
    {
        dbContext.StaffActions.Add(new DbStaffAction
        {
            Created = DateTimeOffset.UtcNow,
            StaffUserId = staff.Id,
            StaffUsername = staff.Username,
            Action = action,
            TargetUserId = targetUserId,
            TargetCharacterId = targetCharacterId,
            TargetName = Truncate(targetName, 64),
            Details = Truncate(details, 500)
        });
    }

    private static string? Truncate(string? value, int length) => value is null || value.Length <= length ? value : value[..length];
}
