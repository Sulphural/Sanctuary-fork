using System.Numerics;
using System.Text.Json.Serialization;

using Sanctuary.Core.Extensions;
using Sanctuary.Core.IO;
using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Resources.Definitions.QuestGoals;

public sealed class EmoteGoal : QuestGoal
{
    public int QuickChatId { get; set; }

    [JsonConverter(typeof(Vector3XzJsonConverter))]
    public Vector3 EmotePosition { get; set; }

    public float EmoteRadius { get; set; } = 12f;

    public int ZoneDefinitionId { get; set; } = ReachLocationGoal.FabledRealmsZoneDefinitionId;

    public override bool Matches(QuestDefinition quest, QuestEvent questEvent)
        => QuickChatId != 0
            && questEvent is EmoteUsedEvent { QuickChatId: var quickChatId, Position: var position, ZoneDefinitionId: var zoneDefinitionId }
            && quickChatId == QuickChatId
            && zoneDefinitionId == ZoneDefinitionId
            && (EmotePosition == Vector3.Zero || position.IsInCircle(EmotePosition, EmoteRadius));
}
