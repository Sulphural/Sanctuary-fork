using System.Numerics;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Quests;

public abstract record QuestEvent
{
    public virtual bool CreditsOneQuest => false;
}

public sealed record NpcTalkedEvent(Npc Npc) : QuestEvent;

public sealed record GoalTimerEvent(int ObjectiveId) : QuestEvent;

public sealed record ClientObjectiveCompletedEvent(int ObjectiveId) : QuestEvent;

public sealed record ZoneQuestEvent(string Name) : QuestEvent;

public sealed record NodeGatheredEvent(CollectionNode Node) : QuestEvent
{
    public override bool CreditsOneQuest => true;
}

public sealed record ItemUsedOnNpcEvent(Npc Npc, int ItemDefinitionId) : QuestEvent;

public sealed record PlayerMovedEvent(Vector4 Position, int ZoneDefinitionId, int ZoneAreaId = 0) : QuestEvent;
