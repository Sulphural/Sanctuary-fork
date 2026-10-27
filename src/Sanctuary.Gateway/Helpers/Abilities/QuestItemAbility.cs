using System.Collections.Generic;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources.Definitions.QuestGoals;
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

        UseOn(player, npc, goal, questManager);

        player.StartItemCooldown(itemDefinition.Id, MinCooldownMs);

        FinishActivation(player, clientItem, itemDefinition, slot, MinCooldownMs, IconTintId(clientItem, itemDefinition.Icon.TintId));

        return true;
    }

    internal static void UseOn(Player player, Npc npc, UseItemOnNpcGoal goal, IQuestManager questManager)
    {
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
            }, goal.AnimationMs, sendToSelf: true);
        }

        if (goal.PlayerEffectId != 0)
        {
            var tagId = NextEffectTagId();

            var addEffect = new PlayerUpdatePacketAddEffectTagCompositeEffect
            {
                Guid = player.Guid,
                TagId = tagId,
                CompositeEffectId = goal.PlayerEffectId,
                SourceGuid = player.Guid,
            };

            foreach (var recipient in recipients)
                recipient.SendTunneled(addEffect);

            player.SendTunneledToVisibleDelayed(
                new PlayerUpdatePacketRemoveEffectTagCompositeEffect { Guid = player.Guid, TagId = tagId },
                goal.AnimationMs, sendToSelf: true);
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

        questManager.OnEvent(player, new ItemUsedOnNpcEvent(npc, goal.SourceId));

        if (goal.HideUsedTargets && player.VisibleNpcs.ContainsKey(npc.Guid) && questManager.IsHiddenByQuest(player, npc))
        {
            player.SendTunneled(EntityHelper.GetRemovePlayerGracefullyPacket(npc.Guid, animate: goal.HideEffectId != 0,
                delay: goal.HideDelayMs, effectDelay: goal.HideDelayMs, compositeEffectId: goal.HideEffectId));
            player.VisibleNpcs.TryRemove(npc.Guid, out _);
        }
    }

    public static bool HandleQuestAbility(Player player, AbilityPacketClientRequestStartAbility packet, IQuestManager questManager)
    {
        if (!questManager.TryFindQuestAbilityTarget(player, packet.Data.Id, packet.Data.Slot, packet.Guid, out var isQuestAbility, out var npc, out var goal))
        {
            if (isQuestAbility)
                SendFailure(player);

            return isQuestAbility;
        }

        UseOn(player, npc, goal, questManager);
        return true;
    }
}
