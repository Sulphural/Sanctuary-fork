using System.Collections.Generic;

using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Resources.Definitions.QuestGoals;

public sealed class UseItemOnNpcGoal : QuestGoal
{
    public int ItemDefinitionId { get; set; }

    public List<int> TargetNpcIds { get; set; } = [];

    public float UseRange { get; set; } = 12f;

    public int AnimationId { get; set; }

    public int TargetEffectId { get; set; }

    public float TargetEffectSeconds { get; set; } = 3f;

    public override GoalProgress CreateProgress() => new TalkProgress();

    public bool CanUseOn(int npcId, GoalProgress progress)
        => TargetNpcIds.Contains(npcId) && !(progress is TalkProgress talkProgress && talkProgress.HasTalkedTo(npcId));

    public override bool Matches(QuestDefinition quest, QuestEvent questEvent)
        => ItemDefinitionId != 0
            && questEvent is ItemUsedOnNpcEvent { Npc: var npc, ItemDefinitionId: var itemDefinitionId }
            && itemDefinitionId == ItemDefinitionId
            && TargetNpcIds.Contains(npc.DefinitionId);

    public override QuestGoalResult Progress(QuestDefinition quest, QuestEvent questEvent, GoalProgress progress)
    {
        if (!Matches(quest, questEvent))
            return QuestGoalResult.None;

        if (progress is not TalkProgress talkProgress || !talkProgress.TryAdd(((ItemUsedOnNpcEvent)questEvent).Npc.DefinitionId))
            return QuestGoalResult.None;

        return talkProgress.Count >= RequiredCount ? QuestGoalResult.Completed : QuestGoalResult.Progressed;
    }
}
