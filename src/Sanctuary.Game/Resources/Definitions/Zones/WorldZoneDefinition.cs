using System.Collections.Generic;

namespace Sanctuary.Game.Resources.Definitions.Zones;

public sealed class WorldZoneDefinition : BaseZoneDefinition
{
    public List<ZoneAreaDefinition> AreaDefinitions { get; set; } = [];

    public List<int> EnabledClientAreaIds { get; set; } = [];

    public List<SeasonalClientAreaDefinition> SeasonalClientAreas { get; set; } = [];

    public int TutorialZoneDefinitionId { get; set; }

    public List<int> TutorialQuestIds { get; set; } = [];

    public Dictionary<int, int> TutorialQuestGoalEnds { get; set; } = [];
}