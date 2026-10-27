using System.Collections.Generic;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Quests;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Gateway.Helpers.Abilities;

public sealed class QuestItemAbility(AbilityServices services, IQuestManager questManager) : ConsumableAbility(services)
{
    public override bool Matches(ClientItemDefinition itemDefinition) => questManager.IsQuestItem(itemDefinition.Id);

    public override bool HandleAbility(Player player, AbilityPacketClientRequestStartAbility packet, int slot, ClientItem clientItem, ClientItemDefinition itemDefinition)
    {
        if (player.IsItemOnCooldown(itemDefinition.Id))
            return SendFailure(player);

        if (!questManager.TryFindItemTarget(player, itemDefinition.Id, packet.Guid, out var npc, out var goal))
            return SendFailure(player);

        var recipients = new HashSet<Player> { player };
        foreach (var visiblePlayer in player.VisiblePlayers.Values)
            recipients.Add(visiblePlayer);

        if (goal.AnimationId != 0)
        {
            var animations = new PlayerUpdatePacketSetSynchronizedAnimations();
            animations.Animations.Add(new PlayerUpdatePacketSetSynchronizedAnimations.Animation { Guid = player.Guid, AnimationId = goal.AnimationId });

            foreach (var recipient in recipients)
                recipient.SendTunneled(animations);

            player.SendTunneledToVisibleDelayed(new PlayerUpdatePacketSetAnimation
            {
                Guid = player.Guid,
                AnimationId = IdleAnimationId,
                Flags = 1
            }, 1500, sendToSelf: true);
        }

        if (goal.TargetEffectId != 0)
        {
            var tagId = NextEffectTagId();

            var addEffect = new PlayerUpdatePacketAddEffectTagCompositeEffect
            {
                Guid = npc.Guid,
                TagId = tagId,
                CompositeEffectId = goal.TargetEffectId,
                SourceGuid = player.Guid,
            };

            foreach (var recipient in recipients)
                recipient.SendTunneled(addEffect);

            player.SendTunneledToVisibleDelayed(
                new PlayerUpdatePacketRemoveEffectTagCompositeEffect { Guid = npc.Guid, TagId = tagId },
                (int)(goal.TargetEffectSeconds * 1000), sendToSelf: true);
        }

        questManager.OnEvent(player, new ItemUsedOnNpcEvent(npc, itemDefinition.Id));

        player.StartItemCooldown(itemDefinition.Id, MinCooldownMs);

        FinishActivation(player, clientItem, itemDefinition, slot, MinCooldownMs, IconTintId(clientItem, itemDefinition.Icon.TintId));

        return true;
    }
}
