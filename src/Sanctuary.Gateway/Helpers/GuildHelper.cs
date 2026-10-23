using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Gateway.Helpers;

public enum GuildNameValidationResult
{
    Valid,
    IncorrectLength,
    IllegalCharacters
}

/// <summary>
/// Shared guild logic used by every guild rename path (the guild-packet handlers
/// and the name-change handlers): name validation plus the database and broadcast operations.
/// </summary>
public static class GuildHelper
{
    public const int MinNameLength = 3;
    public const int MaxNameLength = 32;

    public static GuildData CreateGuildData(DbGuild dbGuild, IZoneManager zoneManager)
    {
        var guildData = new GuildData
        {
            Guid = dbGuild.Id,

            Name = dbGuild.Name,

            CanRenameGuild = true,

            MaxMembers = dbGuild.MaxMembers
        };

        foreach (var dbGuildMember in dbGuild.Members)
        {
            var memberGuid = GuidHelper.GetPlayerGuid(dbGuildMember.Id);

            var guildMember = new GuildMember
            {
                Guid = memberGuid,

                Role = dbGuildMember.Role,

                Name =
                {
                    FirstName = dbGuildMember.Character.FirstName,
                    LastName = dbGuildMember.Character.LastName ?? string.Empty
                }
            };

            if (zoneManager.TryGetPlayer(memberGuid, out var memberPlayer))
            {
                guildMember.Online = true;

                guildMember.WorldId = memberPlayer.Zone.Id;

                guildMember.ProfileId = memberPlayer.ActiveProfileId;
                guildMember.ProfileRank = memberPlayer.ActiveProfile.Rank;
            }

            guildData.Members.Add(memberGuid, guildMember);
        }

        return guildData;
    }

    public static string NormalizeName(string? name)
    {
        return string.Join(' ', (name ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static GuildNameValidationResult ValidateName(string name)
    {
        if (name.Length is < MinNameLength or > MaxNameLength)
            return GuildNameValidationResult.IncorrectLength;

        if (!name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '\'' or '-'))
            return GuildNameValidationResult.IllegalCharacters;

        return GuildNameValidationResult.Valid;
    }

    public static bool IsValidName(string name) => ValidateName(name) == GuildNameValidationResult.Valid;

    public static bool IsProfane(string name, IEnumerable<string> nameFilter)
    {
        return nameFilter.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    public static int? GetMemberRole(DatabaseContext dbContext, ulong guildGuid, ulong playerGuid)
    {
        var characterId = GuidHelper.GetPlayerId(playerGuid);

        return dbContext.GuildMembers
            .AsNoTracking()
            .Where(x => x.GuildId == guildGuid && x.Id == characterId)
            .Select(x => (int?)x.Role)
            .SingleOrDefault();
    }

    public static bool IsLeaderRole(int? role) => role == GuildRole.Leader.Id;

    public static bool IsNameTaken(DatabaseContext dbContext, ulong guildGuid, string normalizedName)
    {
        var loweredName = normalizedName.ToLower();

        return dbContext.Guilds.Any(x => x.Id != guildGuid && x.Name.ToLower() == loweredName);
    }

    /// <summary>
    /// Persists the new guild name, updates the in-memory guild data for every online
    /// member and notifies them. Returns false when the database update did not apply.
    /// </summary>
    public static bool ApplyRename(
        IZoneManager zoneManager,
        DatabaseContext dbContext,
        GatewayConnection connection,
        ulong guildGuid,
        string normalizedName,
        out int notifiedPlayers)
    {
        notifiedPlayers = 0;

        var guildData = connection.Player.GuildData;
        if (guildData is null)
            return false;

        var updated = dbContext.Guilds
            .Where(x => x.Id == guildGuid)
            .ExecuteUpdate(x => x.SetProperty(g => g.Name, normalizedName));

        if (updated <= 0)
            return false;

        guildData.Name = normalizedName;

        var guildNameUpdatePacket = new GuildNameUpdatePacket
        {
            Guid = guildGuid,
            Name = normalizedName
        };

        foreach (var member in guildData.Members.Values)
        {
            if (!zoneManager.TryGetPlayer(member.Guid, out var player))
                continue;

            player.Post(() =>
            {
                if (player.GuildData is null || player.GuildData.Guid != guildGuid)
                    return;

                player.GuildData.Name = normalizedName;
                player.SendTunneled(guildNameUpdatePacket);
            });
            notifiedPlayers++;
        }

        return true;
    }
}
