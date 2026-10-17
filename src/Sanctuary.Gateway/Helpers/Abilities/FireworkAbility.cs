using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Gateway.Helpers.Abilities;

public sealed class FireworkAbility(AbilityServices services) : ConsumableAbility(services)
{
    public override bool Matches(ClientItemDefinition itemDefinition) =>
        _resourceManager.Consumables.Fireworks.ContainsKey(itemDefinition.Id);

    public override bool HandleAbility(Player player, AbilityPacketClientRequestStartAbility packet, int slot, ClientItem clientItem, ClientItemDefinition itemDefinition)
    {
        _resourceManager.Consumables.Fireworks.TryGetValue(itemDefinition.Id, out var firework);

        if (player.IsItemOnCooldown(itemDefinition.Id))
            return SendFailure(player);

        PlayEffect(player, firework!.EffectId);

        var cooldownMs = ClampCooldown(firework.CooldownMs);

        player.StartItemCooldown(itemDefinition.Id, cooldownMs);

        FinishActivation(player, clientItem, itemDefinition, slot, cooldownMs, IconTintId(clientItem, itemDefinition.Icon.TintId));

        return true;
    }
}
