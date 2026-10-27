using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Resources.Definitions.QuestGoals;

public sealed class EnterAreaGoal : QuestGoal
{
    public int ZoneAreaId { get; set; }

    public override bool Matches(QuestDefinition quest, QuestEvent questEvent)
        => ZoneAreaId != 0
            && questEvent is PlayerMovedEvent { ZoneAreaId: var zoneAreaId }
            && zoneAreaId == ZoneAreaId;
}
