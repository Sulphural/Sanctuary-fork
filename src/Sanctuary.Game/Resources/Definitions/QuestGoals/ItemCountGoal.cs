using System;

using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Resources.Definitions.QuestGoals;

public sealed class ItemCountGoal : QuestGoal
{
    public int ItemDefinitionId { get; set; }

    public override GoalProgress CreateProgress() => new ItemCountProgress();

    public override bool Matches(QuestDefinition quest, QuestEvent questEvent)
        => RequiredCount > 0
            && questEvent is ItemCountChangedEvent { ItemDefinitionId: var itemDefinitionId }
            && itemDefinitionId == ItemDefinitionId;

    public override QuestGoalResult Progress(QuestDefinition quest, QuestEvent questEvent, GoalProgress progress)
    {
        if (!Matches(quest, questEvent) || questEvent is not ItemCountChangedEvent { Count: var count })
            return QuestGoalResult.None;

        if (progress is not ItemCountProgress itemCountProgress || !itemCountProgress.TrySet(Math.Min(count, RequiredCount)))
            return QuestGoalResult.None;

        return progress.Count >= RequiredCount ? QuestGoalResult.Completed : QuestGoalResult.Progressed;
    }
}
