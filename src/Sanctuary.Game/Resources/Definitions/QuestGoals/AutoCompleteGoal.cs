using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Resources.Definitions.QuestGoals;

public sealed class AutoCompleteGoal : QuestGoal
{
    public int DelayMs { get; set; } = 6000;

    public override bool Matches(QuestDefinition quest, QuestEvent questEvent)
        => questEvent is GoalTimerEvent { ObjectiveId: var objectiveId } && objectiveId == ClientObjectiveId;
}
