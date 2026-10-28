using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;

using Sanctuary.Core.IO;

namespace Sanctuary.Game.Resources.Definitions.Zones;

public sealed class CombatZoneDefinition : BaseZoneDefinition
{
    public bool Tutorial { get; set; }

    public ZoneIntroDefinition? Intro { get; set; }

    public List<ZoneQuestScene> QuestScenes { get; set; } = [];

    public ZoneWaveDefinition? Waves { get; set; }

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 AssetPreloadPosition { get; set; }

    public List<ZoneAssetPreload> AssetPreloads { get; set; } = [];
}

public sealed class ZoneAssetPreload
{
    public int NpcId { get; set; }

    public List<int> AnimationIds { get; set; } = [];
}

public sealed class ZoneQuestScene
{
    public int QuestId { get; set; }

    public int GoalIndex { get; set; }

    public List<ZoneSceneStep> Steps { get; set; } = [];
}

public sealed class ZoneSceneStep
{
    public int DelayMs { get; set; }

    public ZoneSceneStepType Type { get; set; }

    public int NpcId { get; set; }

    public int TargetNpcId { get; set; }

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 Position { get; set; }

    public float Heading { get; set; }

    public float Scale { get; set; }

    public int AnimationId { get; set; }

    public int BaseAnimationId { get; set; }

    public int StringId { get; set; }

    public int ColorId { get; set; }

    public bool Emote { get; set; }

    public int EffectId { get; set; }

    public int ResponseTextId { get; set; }

    public int SoundId { get; set; }

    public string? Script { get; set; }

    public int ZoneDefinitionId { get; set; }

    public List<string> ArrivalScripts { get; set; } = [];

    public int ArrivalSpeakerNpcId { get; set; }

    public int ArrivalStringId { get; set; }

    public int ArrivalColorId { get; set; }

    public bool ArrivalEmote { get; set; }

    public int ArrivalDelayMs { get; set; }

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 TeleportPosition { get; set; }

    public float TeleportHeading { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ZoneSceneStepType
{
    Move,
    Spawn,
    Despawn,
    Animate,
    Say,
    Effect,
    Script,
    Waves,
    Dialogue
}

public sealed class ZoneWaveDefinition
{
    public int QuestId { get; set; }

    public int GoalIndex { get; set; }

    public string EventName { get; set; } = string.Empty;

    public int NpcId { get; set; }

    public int DelayBetweenMs { get; set; }

    public float Radius { get; set; }

    public int DeathAnimationId { get; set; }

    public int DeathBaseAnimationId { get; set; }

    public int RemoveDelayMs { get; set; }

    public List<ZoneIntroSpawn> Spawns { get; set; } = [];
}

public sealed class ZoneIntroDefinition
{
    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 CameraPosition { get; set; }

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 CameraLookAt { get; set; }

    public float CameraPullBack { get; set; }

    public int ProfileId { get; set; }

    public int NpcId { get; set; }

    public int DialogueTextId { get; set; }

    public int ResponseTextId { get; set; }

    public int DialogueSoundId { get; set; }

    public int DialogueAnimationId { get; set; }

    public int DialogueDelayMs { get; set; }

    public int NpcAnimationId { get; set; }

    public int NpcEffectId { get; set; }

    public int LookAtDelayMs { get; set; }

    public int CinematicEndDelayMs { get; set; }

    public string? StartScript { get; set; }

    public int LookAtNpcId { get; set; }

    public int LookAtSpawnEffectId { get; set; }

    public int LookAtRemoveEffectId { get; set; }

    public int LookAtConeAngle { get; set; }

    public int LookAtArmDelayMs { get; set; }

    public int LookAtCompleteSoundId { get; set; }

    public List<ZoneIntroLookAtStep> LookAtSteps { get; set; } = [];

    public string? FinishScript { get; set; }

    public List<ZoneIntroSpawn> FinishSpawns { get; set; } = [];

    public List<ZoneIntroTrigger> PositionTriggers { get; set; } = [];
}

public sealed class ZoneIntroTrigger
{
    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 Position { get; set; }

    public float Radius { get; set; }

    public int QuestId { get; set; }

    public List<string> Scripts { get; set; } = [];

    public int NpcId { get; set; }

    public int BaseAnimationId { get; set; }

    public int AnimationId { get; set; }
}

public sealed class ZoneIntroSpawn
{
    public int NpcId { get; set; }

    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 Position { get; set; }

    public float Heading { get; set; }

    public float Scale { get; set; }

    public int DelayMs { get; set; }

    public int BaseAnimationId { get; set; }

    public int AnimationId { get; set; }

    public bool Preload { get; set; }
}

public sealed class ZoneIntroLookAtStep
{
    [JsonConverter(typeof(Vector4JsonConverter))]
    public Vector4 Position { get; set; }

    public string Script { get; set; } = string.Empty;
}
