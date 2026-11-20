using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game;

public static class CharacterDeleter
{
    public static bool Delete(DatabaseContext dbContext, DbCharacter character, ILogger logger)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return strategy.Execute(() =>
        {
            using var transaction = dbContext.Database.BeginTransaction();

            try
            {
                var guildMember = dbContext.GuildMembers
                    .AsNoTracking()
                    .SingleOrDefault(x => x.Id == character.Id);

                if (guildMember is not null)
                {
                    character.GuildMemberId = null;

                    var deletedGuildMember = dbContext.GuildMembers
                        .Where(x => x.Id == character.Id)
                        .ExecuteDelete();

                    if (deletedGuildMember <= 0)
                        return false;

                    var hasMembers = dbContext.GuildMembers.Any(x => x.GuildId == guildMember.GuildId);
                    if (!hasMembers)
                    {
                        dbContext.Guilds
                            .Where(x => x.Id == guildMember.GuildId)
                            .ExecuteDelete();
                    }
                    else if (guildMember.Role == GuildRole.Leader.Id
                             && !dbContext.GuildMembers.Any(x => x.GuildId == guildMember.GuildId && x.Role == GuildRole.Leader.Id))
                    {
                        var newLeader = dbContext.GuildMembers
                            .Where(x => x.GuildId == guildMember.GuildId)
                            .OrderBy(x => x.Role)
                            .ThenBy(x => x.Joined)
                            .ThenBy(x => x.Id)
                            .FirstOrDefault();

                        if (newLeader is not null)
                            newLeader.Role = GuildRole.Leader.Id;
                    }
                }

                // Delete references first to avoid foreign key constraint violations.
                var characterId = character.Id;

                var friends = dbContext.Friends.Where(f =>
                    f.CharacterId == characterId || f.FriendCharacterId == characterId);

                var ignores = dbContext.Ignores.Where(i =>
                    i.CharacterId == characterId || i.IgnoreCharacterId == characterId);

                dbContext.Friends.RemoveRange(friends);
                dbContext.Ignores.RemoveRange(ignores);
                dbContext.Remove(character);

                if (dbContext.SaveChanges() == 0)
                    return false;
            }
            catch (DbUpdateException ex)
            {
                logger.LogError(ex, "Failed to delete character {CharacterId}.", character.Id);

                return false;
            }

            transaction.Commit();
            return true;
        });
    }
}
