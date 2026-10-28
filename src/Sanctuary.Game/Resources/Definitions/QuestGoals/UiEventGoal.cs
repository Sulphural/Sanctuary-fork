using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Resources.Definitions.QuestGoals;

public sealed class UiEventGoal : QuestGoal
{
    public string EventName { get; set; } = string.Empty;

    public override bool Matches(QuestDefinition quest, QuestEvent questEvent)
        => questEvent is ClientObjectiveCompletedEvent { ObjectiveId: var objectiveId } && objectiveId == ClientObjectiveId;
}
