using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Resources.Definitions.QuestGoals;

public sealed class ZoneEventGoal : QuestGoal
{
    public string EventName { get; set; } = string.Empty;

    public override bool Matches(QuestDefinition quest, QuestEvent questEvent)
        => questEvent is ZoneQuestEvent { Name: var name } && name == EventName;

    public override QuestGoalResult Progress(QuestDefinition quest, QuestEvent questEvent, GoalProgress progress)
    {
        if (!Matches(quest, questEvent))
            return QuestGoalResult.None;

        progress.Increment();

        return progress.Count >= RequiredCount ? QuestGoalResult.Completed : QuestGoalResult.Progressed;
    }
}
