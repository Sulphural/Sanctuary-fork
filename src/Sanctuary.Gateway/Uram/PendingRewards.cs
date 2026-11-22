using System.Collections.Generic;
using System.Linq;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet;

namespace Sanctuary.Gateway.Uram;

public static class PendingRewards
{
    public static ulong? UserIdOf(DatabaseContext dbContext, GatewayConnection connection)
    {
        if (connection.Player is null)
            return null;

        var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);

        return dbContext.Characters.Where(x => x.Id == characterId).Select(x => (ulong?)x.UserId).SingleOrDefault();
    }

    public static (List<ClaimItem> Items, long Signature) Build(DatabaseContext dbContext, IResourceManager resourceManager, ulong userId)
    {
        var rewards = CodeRedeemer.Unclaimed(dbContext, userId).OrderBy(x => x.Id).ToList();
        var items = new List<ClaimItem>();

        foreach (var reward in rewards)
        {
            resourceManager.ClientItemDefinitions.TryGetValue(reward.ItemDefinition, out var definition);

            items.Add(new ClaimItem
            {
                ClaimInstanceIds = [(ulong)reward.Id],
                NameId = definition?.NameId ?? 0,
                DescriptionId = definition?.DescriptionId ?? 0,
                ImageSetId = definition?.Icon.Id ?? 0,
                TintId = definition?.Icon.TintId ?? 0,
                Contents = [new ClaimItemContent { ItemDefinitionId = reward.ItemDefinition, Quantity = reward.Quantity }]
            });
        }

        var signature = rewards.Count == 0 ? 0 : rewards.Count * 1_000_003L + rewards[^1].Id;

        return (items, signature);
    }

    public static void SendDefinitions(GatewayConnection connection, IResourceManager resourceManager, IEnumerable<ClaimItem> items)
    {
        var definitions = items
            .SelectMany(x => x.Contents)
            .Select(x => resourceManager.ClientItemDefinitions.TryGetValue(x.ItemDefinitionId, out var definition) ? definition : null)
            .Where(x => x is not null)
            .Distinct()
            .ToList();

        if (definitions.Count > 0)
            ItemDefinitionHelper.SendItemDefinitions(connection.Player, resourceManager, definitions!);
    }
}
