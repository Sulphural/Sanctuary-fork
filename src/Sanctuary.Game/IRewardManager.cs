using System;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions.Rewards;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game;

public interface IRewardManager
{
    event Action<Player, int>? ItemCountChanged;

    bool TryRollReward(string rewardTableKey, out RewardDropDefinition? drop);

    bool TryRollRewardUnowned(Player player, string rewardTableKey, out RewardDropDefinition? drop, out int tint);

    bool TryGrantReward(Player player, RewardDropDefinition drop, ulong sourceGuid = 0);

    bool TryGrantItem(Player player, int itemDefinitionId, int tint, int quantity = 1, ulong sourceGuid = 0);

    bool TryGrantCurrency(Player player, CurrencyType currencyType, int amount);

    bool TrySpendCoins(Player player, int amount);

    bool TrySpendStationCash(Player player, int amount);

    int CountItems(Player player, int itemDefinitionId);

    bool TryTakeItems(Player player, int itemDefinitionId, int quantity);

    bool TryTakeItem(Player player, ClientItem clientItem, int quantity = 1);
}
