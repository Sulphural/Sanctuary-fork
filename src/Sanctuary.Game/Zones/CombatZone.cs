using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Extensions;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Interactions;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources.Definitions.QuestGoals;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Zones;

public sealed class CombatZone : BaseZone
{
    private const int RemoveNpcDelayMs = 1000;
    private const int ArrivalSpeakerLifetimeMs = 10000;
    private const int EffectFadeMs = 3000;
    private const int VisibleCheckMs = 100;
    private const int VisibleTimeoutMs = 5000;

    private readonly CombatZoneDefinition _zoneDefinition;
    private readonly IQuestManager _questManager;

    private Player? _player;

    private int _lookAtStep = -1;
    private Npc? _lookAtNpc;
    private int _triggerStep = -1;

    private readonly HashSet<Npc> _assetPreloadNpcs = [];
    private readonly Dictionary<ZoneIntroSpawn, Npc> _preloadedSpawns = [];
    private readonly HashSet<ZoneQuestScene> _playedScenes = [];
    private readonly Dictionary<int, Npc> _sceneNpcs = [];
    private readonly Dictionary<Npc, int> _sceneEffectTags = [];
    private readonly List<Npc> _waveNpcs = [];
    private bool _waveActive;

    public CombatZone(CombatZoneDefinition zoneDefinition, IServiceProvider serviceProvider)
        : base(zoneDefinition, serviceProvider)
    {
        _zoneDefinition = zoneDefinition;
        _questManager = serviceProvider.GetRequiredService<IQuestManager>();
    }

    public override IReadOnlyList<NpcInteractionOption> GetInteractionOptions(Player player, Npc npc)
        => _questManager.IsQuestNpc(npc.DefinitionId) ? _questManager.GetInteractionOptions(player, npc) : [];

    public override NotificationInfo? GetQuestBadge(Player player, Npc npc)
        => _questManager.IsQuestNpc(npc.DefinitionId) ? _questManager.GetQuestBadge(player, npc) : null;

    public override bool IsHiddenByQuest(Player player, Npc npc) => _questManager.IsHiddenByQuest(player, npc);

    public override int GetQuestEffectId(Player player, Npc npc) => _questManager.GetQuestEffectId(player, npc);

    public override void OnClientIsReady(Player player)
    {
        base.OnClientIsReady(player);

        foreach (var npc in Npcs)
        {
            if (!_questManager.IsQuestNpc(npc.DefinitionId))
                continue;

            if (_resourceManager.Quests.TryGetNpcCursorId(npc.DefinitionId, out var cursorId))
                npc.CursorId = cursorId;

            if (_resourceManager.Quests.TryGetNpcInteractRange(npc.DefinitionId, out var interactRange))
                npc.QuestInteractRange = interactRange;

            _questManager.RefreshQuestNotification(player, npc.Guid);
        }

        if (!_zoneDefinition.Tutorial)
            return;

        player.SendTunneled(new PacketSendZoneDetails
        {
            Name = Name,
            Sky = Sky,
            Tutorial = true,
            Id = Id
        });

        RunAfter(0, () => StartTutorial(player));
    }

    public void OnObjectiveComplete(Player player, int objectiveId)
        => RunAfter(0, () => CompleteLookAt(player, objectiveId));

    protected override void UpdateEveryTick()
    {
        if (_player is not { } player || !HasPlayer(player))
            return;

        UpdateTriggers(player);
        UpdateScenes(player);
        UpdateWaves(player);
    }

    private void StartTutorial(Player player)
    {
        if (!HasPlayer(player))
            return;

        _player = player;

        if (_zoneDefinition.Intro is not { } intro)
            return;

        var passedTriggers = intro.PositionTriggers.FindLastIndex(trigger => trigger.QuestId != 0 && player.Quests.Contains(trigger.QuestId)) + 1;

        if (passedTriggers == 0)
        {
            PreloadAssets(player);
            PreloadSpawns(intro);
            PlayIntro(player, intro);
            return;
        }

        foreach (var spawn in intro.FinishSpawns)
        {
            if (SpawnNpc(spawn.NpcId, spawn.Position, spawn.Heading, spawn.Scale, spawn.BaseAnimationId) is { } npc)
                RunWhenVisible(player, npc, () => PlayAnimations(player, npc, spawn.BaseAnimationId, 0));
        }

        foreach (var trigger in intro.PositionTriggers.Take(passedTriggers))
        {
            if (FindNpc(trigger.NpcId) is { } npc)
                RunWhenVisible(player, npc, () => AnimateNpc(player, npc, trigger.BaseAnimationId, 0));
        }

        _triggerStep = passedTriggers;
    }

    private void PreloadAssets(Player player)
    {
        foreach (var preload in _zoneDefinition.AssetPreloads)
        {
            if (!_resourceManager.Npcs.TryGetValue(preload.NpcId, out var definition) || !TryCreateNpc(null, definition, out var npc))
                continue;

            _assetPreloadNpcs.Add(npc);

            npc.HideNamePlate = true;
            npc.IsInteractable = false;
            npc.SetCollidable(false);
            npc.UpdatePosition(_zoneDefinition.AssetPreloadPosition with { W = 1f }, Quaternion.Identity);

            RunWhenVisible(player, npc, () =>
            {
                foreach (var animationId in preload.AnimationIds)
                    player.SendTunneled(new PlayerUpdatePacketSetAnimation { Guid = npc.Guid, AnimationId = animationId });
            });
        }
    }

    private void RemoveAssetPreloads()
    {
        foreach (var npc in _assetPreloadNpcs)
        {
            npc.Remove(0);
            RunAfter(RemoveNpcDelayMs, npc.Dispose);
        }

        _assetPreloadNpcs.Clear();
    }

    private void PreloadSpawns(ZoneIntroDefinition intro)
    {
        foreach (var spawn in intro.FinishSpawns.Where(spawn => spawn.Preload))
        {
            if (SpawnNpc(spawn.NpcId, spawn.Position, spawn.Heading, spawn.Scale) is { } npc)
                _preloadedSpawns[spawn] = npc;
        }
    }

    private void PlayIntro(Player player, ZoneIntroDefinition intro)
    {
        RunScript(player, "HUD.hideMain");

        SendCinematic(player, intro);

        if (intro.ProfileId != 0 && player.ActiveProfileId != intro.ProfileId)
            player.ActivateProfile(intro.ProfileId);

        RunAfter(intro.DialogueDelayMs, () =>
        {
            if (!HasPlayer(player) || FindNpc(intro.NpcId) is not { } npc)
                return;

            QuestDialogue.Begin(player,
            [
                new QuestDialogueLine
                {
                    TextId = intro.DialogueTextId,
                    ResponseTextId = intro.ResponseTextId,
                    SoundId = intro.DialogueSoundId,
                    AnimationId = intro.DialogueAnimationId,
                    Closable = false
                }
            ], npc.Guid, () => RunAfter(0, () => OnIntroDialogueFinished(player, intro, npc)));
        });
    }

    private void OnIntroDialogueFinished(Player player, ZoneIntroDefinition intro, Npc npc)
    {
        if (!HasPlayer(player))
            return;

        SendCinematic(player, intro);

        PlayNpcCast(player, intro, npc);

        if (intro.LookAtSteps.Count == 0)
            return;

        RunAfter(intro.LookAtDelayMs, () =>
        {
            if (!HasPlayer(player))
                return;

            RunScript(player, intro.StartScript);

            ShowLookAtStep(player, intro, 0);

            RunAfter(intro.CinematicEndDelayMs, () =>
            {
                if (!HasPlayer(player))
                    return;

                RunScript(player, "Cinematic.End");

                SendLookAt(player, intro, Math.Max(0, intro.LookAtArmDelayMs - intro.CinematicEndDelayMs));
            });
        });
    }

    private void ShowLookAtStep(Player player, ZoneIntroDefinition intro, int step)
    {
        _lookAtStep = step;

        var lookAtStep = intro.LookAtSteps[step];

        if (TrySpawnNpc(intro.LookAtNpcId, null, lookAtStep.Position.X, lookAtStep.Position.Y, lookAtStep.Position.Z, 0f, 0f, 0f, out var spawned)
            && spawned is Npc lookAtNpc)
        {
            _lookAtNpc = lookAtNpc;

            if (intro.LookAtSpawnEffectId != 0)
                RunAfter(intro.LookAtDelayMs / 2, () => lookAtNpc.PlayEffect(intro.LookAtSpawnEffectId));
        }

        RunScript(player, lookAtStep.Script);
    }

    private void SendLookAt(Player player, ZoneIntroDefinition intro, int delayMs)
    {
        if (_lookAtNpc is null)
            return;

        player.SendTunneledDelayed(new ObjectiveLookAtPacket
        {
            ObjectiveId = _lookAtStep + 1,
            TargetGuid = _lookAtNpc.Guid,
            ConeAngle = intro.LookAtConeAngle
        }, delayMs);
    }

    private void CompleteLookAt(Player player, int objectiveId)
    {
        if (_zoneDefinition.Intro is not { } intro || !HasPlayer(player) || _lookAtStep < 0 || objectiveId != _lookAtStep + 1)
            return;

        RemoveLookAtNpc(intro);

        PlayLookAtComplete(player, intro);

        if (_lookAtStep + 1 < intro.LookAtSteps.Count)
        {
            ShowLookAtStep(player, intro, _lookAtStep + 1);
            SendLookAt(player, intro, intro.LookAtArmDelayMs);
            return;
        }

        _lookAtStep = -1;

        RemoveAssetPreloads();

        RunScript(player, intro.FinishScript);

        foreach (var spawn in intro.FinishSpawns)
            RaiseSpawn(player, spawn);

        _triggerStep = 0;
    }

    private void RemoveLookAtNpc(ZoneIntroDefinition intro)
    {
        if (_lookAtNpc is not { } lookAtNpc)
            return;

        _lookAtNpc = null;

        lookAtNpc.Remove(intro.LookAtRemoveEffectId);

        RunAfter(RemoveNpcDelayMs, lookAtNpc.Dispose);
    }

    private void PlayLookAtComplete(Player player, ZoneIntroDefinition intro)
    {
        if (intro.LookAtCompleteSoundId != 0)
            player.SendTunneled(new ExecuteScriptPacket { Script = "UiSoundHandler.PlaySound", Params = [intro.LookAtCompleteSoundId] });

        if (FindNpc(intro.NpcId) is { } npc)
            PlayNpcCast(player, intro, npc);
    }

    private static void PlayNpcCast(Player player, ZoneIntroDefinition intro, Npc npc)
    {
        if (intro.NpcAnimationId != 0)
            player.SendTunneled(new PlayerUpdatePacketSetAnimation { Guid = npc.Guid, AnimationId = intro.NpcAnimationId });

        if (intro.NpcEffectId != 0)
            npc.PlayEffect(intro.NpcEffectId);
    }

    private void RaiseSpawn(Player player, ZoneIntroSpawn spawn)
    {
        if (!_preloadedSpawns.Remove(spawn, out var npc))
            npc = SpawnNpc(spawn.NpcId, spawn.Position, spawn.Heading, spawn.Scale);

        if (npc is null)
            return;

        RunWhenVisible(player, npc, () => RunAfter(spawn.DelayMs, () =>
        {
            if (HasPlayer(player))
                AnimateNpc(player, npc, spawn.BaseAnimationId, spawn.AnimationId);
        }));
    }

    private void UpdateTriggers(Player player)
    {
        if (_zoneDefinition.Intro is not { } intro || _triggerStep < 0 || _triggerStep >= intro.PositionTriggers.Count)
            return;

        var trigger = intro.PositionTriggers[_triggerStep];

        if (Vector2.Distance(new Vector2(player.Position.X, player.Position.Z), new Vector2(trigger.Position.X, trigger.Position.Z)) > trigger.Radius)
            return;

        _triggerStep++;

        if (trigger.QuestId != 0)
            _questManager.AcceptQuest(player, trigger.QuestId);

        foreach (var script in trigger.Scripts)
            RunScript(player, script);

        if (FindNpc(trigger.NpcId) is { } npc)
            AnimateNpc(player, npc, trigger.BaseAnimationId, trigger.AnimationId);
    }

    private void UpdateScenes(Player player)
    {
        foreach (var scene in _zoneDefinition.QuestScenes)
        {
            if (_playedScenes.Contains(scene) || !IsGoalOpen(player, scene.QuestId, scene.GoalIndex))
                continue;

            _playedScenes.Add(scene);

            foreach (var step in scene.Steps)
            {
                RunAfter(step.DelayMs, () =>
                {
                    if (HasPlayer(player))
                        RunSceneStep(player, step);
                });
            }
        }
    }

    private void RunSceneStep(Player player, ZoneSceneStep step)
    {
        var npc = FindSceneNpc(step.NpcId);

        switch (step.Type)
        {
            case ZoneSceneStepType.Move:
                npc?.MoveTo(new Vector3(step.Position.X, step.Position.Y, step.Position.Z), true);
                break;

            case ZoneSceneStepType.Spawn:
                if (SpawnNpc(step.NpcId, step.Position, step.Heading, step.Scale) is { } spawned)
                {
                    _sceneNpcs[step.NpcId] = spawned;
                    RunWhenVisible(player, spawned, () =>
                    {
                        AnimateNpc(player, spawned, step.BaseAnimationId, step.AnimationId);

                        if (step.EffectId != 0)
                            _sceneEffectTags[spawned] = spawned.AddEffectTag(step.EffectId);
                    });
                }
                break;

            case ZoneSceneStepType.Despawn:
                if (npc is not null)
                {
                    _sceneNpcs.Remove(step.NpcId);

                    var removeDelayMs = 0;

                    if (_sceneEffectTags.Remove(npc, out var tagId))
                    {
                        npc.RemoveEffectTag(tagId);
                        removeDelayMs = EffectFadeMs;
                    }

                    RunAfter(removeDelayMs, () =>
                    {
                        npc.Remove(0);
                        RunAfter(RemoveNpcDelayMs, npc.Dispose);
                    });
                }
                break;

            case ZoneSceneStepType.Animate:
                if (npc is not null)
                    AnimateNpc(player, npc, step.BaseAnimationId, step.AnimationId);
                break;

            case ZoneSceneStepType.Say:
                if (npc is not null)
                    SendChat(player, npc.Guid, step.StringId, step.ColorId, step.Emote);
                break;

            case ZoneSceneStepType.Effect:
                if (npc is not null && FindSceneNpc(step.TargetNpcId) is { } target)
                {
                    player.SendTunneled(new PlayerUpdatePacketAddEffectTagCompositeEffect
                    {
                        Guid = target.Guid,
                        TagId = EffectTagIdGenerator.Next(),
                        CompositeEffectId = step.EffectId,
                        SourceGuid = npc.Guid
                    });
                }
                break;

            case ZoneSceneStepType.Script:
                RunScript(player, step.Script);
                break;

            case ZoneSceneStepType.Waves:
                SpawnWave();
                break;

            case ZoneSceneStepType.Dialogue:
                if (npc is not null)
                {
                    QuestDialogue.Begin(player,
                    [
                        new QuestDialogueLine
                        {
                            TextId = step.StringId,
                            ResponseTextId = step.ResponseTextId,
                            SoundId = step.SoundId,
                            AnimationId = step.AnimationId,
                            Closable = false
                        }
                    ], npc.Guid, () => TeleportOut(player, step));
                }
                break;
        }
    }

    private void TeleportOut(Player player, ZoneSceneStep step)
    {
        if (step.ZoneDefinitionId == 0 || !HasPlayer(player))
            return;

        RunScript(player, step.Script);

        if (!player.TeleportToZone(step.ZoneDefinitionId, null, step.TeleportPosition, QuaternionExtensions.FromHeading(step.TeleportHeading)))
            return;

        player.RunWhenZoneLoaded(() =>
        {
            foreach (var script in step.ArrivalScripts)
                RunScript(player, script);

            var zone = player.Zone;

            if (step.ArrivalStringId == 0
                || !zone.TrySpawnNpc(step.ArrivalSpeakerNpcId, null, player.Position.X, player.Position.Y, player.Position.Z, 0f, 0f, 0f, out var spawned)
                || spawned is not Npc speaker)
                return;

            zone.RunAfter(step.ArrivalDelayMs, () =>
            {
                if (zone.HasPlayer(player))
                    SendChat(player, speaker.Guid, step.ArrivalStringId, step.ArrivalColorId, step.ArrivalEmote);
            });

            zone.RunAfter(step.ArrivalDelayMs + ArrivalSpeakerLifetimeMs, () =>
            {
                speaker.Remove(0);
                zone.RunAfter(RemoveNpcDelayMs, speaker.Dispose);
            });
        });
    }

    private void SpawnWave()
    {
        if (_zoneDefinition.Waves is not { } waves)
            return;

        foreach (var spawn in waves.Spawns)
        {
            if (SpawnNpc(waves.NpcId, spawn.Position, spawn.Heading, spawn.Scale) is { } npc)
                _waveNpcs.Add(npc);
        }

        _waveActive = _waveNpcs.Count > 0;

        if (!_waveActive)
            Logger.LogWarning("{name} ({id}) - No wave NPCs could be spawned from NPC definition {npcId}", Name, Id, waves.NpcId);
    }

    private void UpdateWaves(Player player)
    {
        if (!_waveActive || _zoneDefinition.Waves is not { } waves)
            return;

        if (!IsGoalOpen(player, waves.QuestId, waves.GoalIndex))
        {
            _waveActive = false;

            foreach (var npc in _waveNpcs)
                Defeat(player, npc, waves);

            _waveNpcs.Clear();
            return;
        }

        var reached = _waveNpcs.Where(npc => Vector2.Distance(new Vector2(player.Position.X, player.Position.Z), new Vector2(npc.Position.X, npc.Position.Z)) <= waves.Radius).ToList();

        foreach (var npc in reached)
        {
            _waveNpcs.Remove(npc);
            Defeat(player, npc, waves);
        }

        if (_waveNpcs.Count > 0)
            return;

        _waveActive = false;

        _questManager.OnEvent(player, new ZoneQuestEvent(waves.EventName));

        if (!IsGoalOpen(player, waves.QuestId, waves.GoalIndex))
            return;

        RunAfter(waves.DelayBetweenMs, () =>
        {
            if (HasPlayer(player) && IsGoalOpen(player, waves.QuestId, waves.GoalIndex))
                SpawnWave();
        });
    }

    private void Defeat(Player player, Npc npc, ZoneWaveDefinition waves)
    {
        AnimateNpc(player, npc, waves.DeathBaseAnimationId, waves.DeathAnimationId);

        RunAfter(waves.RemoveDelayMs, () =>
        {
            npc.Remove(0);
            RunAfter(RemoveNpcDelayMs, npc.Dispose);
        });
    }

    private void RunWhenVisible(Player player, Npc npc, Action action, int waitedMs = 0)
    {
        if (!HasPlayer(player))
            return;

        if (npc.VisiblePlayers.ContainsKey(player.Guid) || waitedMs >= VisibleTimeoutMs)
        {
            action();
            return;
        }

        RunAfter(VisibleCheckMs, () => RunWhenVisible(player, npc, action, waitedMs + VisibleCheckMs));
    }

    private Npc? SpawnNpc(int npcId, Vector4 position, float heading, float scale, int animationId = 0)
    {
        if (!_resourceManager.Npcs.TryGetValue(npcId, out var definition) || !TryCreateNpc(null, definition, out var npc))
            return null;

        if (scale > 0f)
            npc.Scale = scale;

        if (animationId != 0)
            npc.Animation = animationId;

        npc.UpdatePosition(new Vector4(position.X, position.Y, position.Z, 1f), QuaternionExtensions.FromHeading(heading));

        return npc;
    }

    private Npc? FindNpc(int npcId)
        => npcId == 0 ? null : Npcs.FirstOrDefault(npc => npc.DefinitionId == npcId && !_assetPreloadNpcs.Contains(npc));

    private Npc? FindSceneNpc(int npcId)
        => _sceneNpcs.TryGetValue(npcId, out var npc) ? npc : FindNpc(npcId);

    private static bool IsGoalOpen(Player player, int questId, int goalIndex)
        => player.Quests.TryGetInProgress(questId, out var progress) && progress.OpenGoals.ContainsKey(goalIndex);

    private static void AnimateNpc(Player player, Npc npc, int baseAnimationId, int animationId)
    {
        if (baseAnimationId != 0)
            npc.Animation = baseAnimationId;

        PlayAnimations(player, npc, baseAnimationId, animationId);
    }

    private static void PlayAnimations(Player player, Npc npc, int baseAnimationId, int animationId)
    {
        if (baseAnimationId != 0)
            player.SendTunneled(new PlayerUpdatePacketSetAnimation { Guid = npc.Guid, AnimationId = baseAnimationId, Flags = 1 });

        if (animationId != 0)
            player.SendTunneled(new PlayerUpdatePacketSetAnimation { Guid = npc.Guid, AnimationId = animationId });
    }

    private static void SendChat(Player player, ulong speakerGuid, int stringId, int colorId, bool emote)
    {
        player.SendTunneled(new ChatPacketFromStringId
        {
            SpeakerGuid = speakerGuid,
            StringId = stringId,
            IsEmote = emote,
            HasColor = colorId != 0,
            ColorId = colorId,
            TargetGuid = player.Guid
        });
    }

    private static void RunScript(Player player, string? script)
    {
        if (!string.IsNullOrEmpty(script))
            player.SendTunneled(new ExecuteScriptPacket { Script = script });
    }

    private static void SendCinematic(Player player, ZoneIntroDefinition intro)
    {
        player.SendTunneled(new CinematicStartLookAtPacket
        {
            Eye = new CinematicTarget
            {
                Type = CinematicTarget.Location,
                Position = intro.CameraPosition
            },
            LookAt = new CinematicTarget
            {
                Type = CinematicTarget.Location,
                Position = intro.CameraLookAt
            },
            PullBack = intro.CameraPullBack,
            CinematicMode = true
        });
    }
}
