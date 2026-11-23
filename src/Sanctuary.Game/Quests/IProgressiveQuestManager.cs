using System;
using System.Collections.Generic;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Interactions;

namespace Sanctuary.Game.Quests;

public interface IProgressiveQuestManager
{
    DateTime? DateOverride { get; set; }

    DateTime Now { get; }

    List<NpcInteractionOption> GetInteractionOptions(Player player, Npc npc);

    bool Show(Player player, int progressiveQuestId);

    void StartQuest(Player player, int progressiveQuestId, int slotId);

    void ClaimSlot(Player player, int progressiveQuestId, int slotId);

    void ClaimPrize(Player player, int progressiveQuestId, int prizeSlotId);

    void ResetClaims(Player player);
}
