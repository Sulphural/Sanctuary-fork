using System.Collections.Generic;

using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Resources.Definitions.QuestGoals;

public sealed class UseItemOnNpcGoal : QuestGoal
{
    public int ItemDefinitionId { get; set; }

    public List<int> TargetNpcIds { get; set; } = [];

    public float UseRange { get; set; } = 12f;

    public int AnimationId { get; set; }

    public int AnimationMs { get; set; } = 1500;

    public int TargetEffectId { get; set; }

    public int PlayerEffectId { get; set; }

    public float TargetEffectSeconds { get; set; } = 3f;

    public bool HideUsedTargets { get; set; }

    public NpcAbilityDefinition? Ability { get; set; }

    public int SourceId => ItemDefinitionId != 0 ? ItemDefinitionId : Ability?.Definition.Id ?? 0;

    public override GoalProgress CreateProgress() => new TalkProgress();

    public bool CanUseOn(int npcId, GoalProgress progress)
        => TargetNpcIds.Contains(npcId) && !(progress is TalkProgress talkProgress && talkProgress.HasTalkedTo(npcId));

    public override bool Matches(QuestDefinition quest, QuestEvent questEvent)
        => SourceId != 0
            && questEvent is ItemUsedOnNpcEvent { Npc: var npc, ItemDefinitionId: var itemDefinitionId }
            && itemDefinitionId == SourceId
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
