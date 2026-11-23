using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Collections;
using Sanctuary.Core.Extensions;
using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Interactions;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.QuestGoals;
using Sanctuary.Game.Resources.Definitions.Rewards;
using Sanctuary.Game.Routines;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Quests;

public sealed class QuestManager : IQuestManager
{
    public const int AllGoalsDoneGoalId = 0;

    public const int GoalDoneCount = -1;

    public event Action<Player, QuestDefinition>? QuestCompleted;

    private readonly IResourceManager _resourceManager;
    private readonly IRewardManager _rewardManager;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;
    private readonly ILogger<QuestManager> _logger;

    public QuestManager(IResourceManager resourceManager, IRewardManager rewardManager, IDbContextFactory<DatabaseContext> dbContextFactory, ILogger<QuestManager> logger)
    {
        _resourceManager = resourceManager;
        _rewardManager = rewardManager;
        _dbContextFactory = dbContextFactory;
        _logger = logger;

        _rewardManager.ItemCountChanged += (player, itemDefinitionId) =>
            OnEvent(player, new ItemCountChangedEvent(itemDefinitionId, _rewardManager.CountItems(player, itemDefinitionId)));
    }

    public bool IsQuestNpc(int npcId)
        => _resourceManager.Quests.ByGiver.ContainsKey(npcId) || _resourceManager.Quests.ByTarget.ContainsKey(npcId);

    private IEnumerable<(QuestDefinition Quest, QuestProgress Progress, int GoalIndex, QuestGoal Goal)> ActiveGoals(Player player)
    {
        foreach (var progress in player.Quests.InProgress)
        {
            if (!_resourceManager.Quests.TryGet(progress.QuestId, out var quest))
                continue;

            foreach (var goalIndex in progress.OpenGoals.Keys.Order())
                yield return (quest, progress, goalIndex, quest.Goals[goalIndex]);
        }
    }

    public List<NpcInteractionOption> GetInteractionOptions(Player player, Npc npc)
    {
        var options = new List<NpcInteractionOption>();
        var quests = _resourceManager.Quests;

        foreach (var progress in player.Quests.InProgress)
        {
            if (!quests.TryGet(progress.QuestId, out var activeQuest))
                continue;

            if (!AdvancesHere(activeQuest, progress, npc) && !TurnsInHere(activeQuest, progress, npc) && AppearanceGoalHere(activeQuest, progress, npc) < 0)
                continue;

            var quest = activeQuest;

            options.Add(new NpcInteractionOption
            {
                IconId = ContextIcons.QuestTurnIn,
                ButtonTextId = quest.TitleId,
                Invoke = interactingPlayer => AdvanceAtNpc(interactingPlayer, quest, npc)
            });
        }

        foreach (var finished in player.Quests.All.Where(quest => quest.Completed))
        {
            if (!quests.TryGet(finished.QuestId, out var finishedQuest))
                continue;

            foreach (var goal in finishedQuest.Goals.Where(goal => goal.CompletionAppearanceModelIds.Count > 0 && goal.Matches(finishedQuest, new NpcTalkedEvent(npc))))
            {
                options.Add(new NpcInteractionOption
                {
                    IconId = ContextIcons.QuestTurnIn,
                    ButtonTextId = goal.NameId,
                    Invoke = interactingPlayer => UseAppearanceGoal(interactingPlayer, goal, npc)
                });
            }
        }

        if (quests.ByGiver.TryGetValue(npc.DefinitionId, out var giverQuestIds))
        {
            foreach (var questId in giverQuestIds)
            {
                if (!quests.TryGet(questId, out var offerableQuest) || !IsOfferable(player, offerableQuest))
                    continue;

                var quest = offerableQuest;

                options.Add(new NpcInteractionOption
                {
                    IconId = quest.OfferIconId,
                    ButtonTextId = quest.TitleId,
                    Invoke = interactingPlayer => Offer(interactingPlayer, quest)
                });
            }
        }

        return options;
    }

    private static bool IsOfferable(Player player, QuestDefinition quest)
        => quest.IsOfferableFor(player.Quests) && (quest.Season is null || player.Zone.IsSeasonActive(quest.Season));

    private static bool AdvancesHere(QuestDefinition quest, QuestProgress progress, Npc npc)
        => TalkGoalHere(quest, progress, npc) >= 0;

    private static int TalkGoalHere(QuestDefinition quest, QuestProgress progress, Npc npc)
    {
        foreach (var goalIndex in progress.OpenGoals.Keys.Order())
        {
            if (quest.Goals[goalIndex].Matches(quest, new NpcTalkedEvent(npc)))
                return goalIndex;
        }

        return -1;
    }

    private static int AppearanceGoalHere(QuestDefinition quest, QuestProgress progress, Npc npc)
    {
        for (var goalIndex = 0; goalIndex < progress.GoalIndex && goalIndex < quest.Goals.Count; goalIndex++)
        {
            var goal = quest.Goals[goalIndex];

            if (goal.CompletionAppearanceModelIds.Count > 0 && goal.Matches(quest, new NpcTalkedEvent(npc)))
                return goalIndex;
        }

        return -1;
    }

    private bool TryPayForGoal(Player player, QuestGoal goal, Npc npc)
    {
        if (goal.TakeItemCount > 0 && _rewardManager.CountItems(player, goal.TakeItemDefinitionId) < goal.TakeItemCount)
        {
            if (goal.NotEnoughItemsTextId != 0)
                QuestDialogue.Begin(player, [new QuestDialogueLine { TextId = goal.NotEnoughItemsTextId }], npc.Guid);

            return false;
        }

        if (!_rewardManager.TrySpendCoins(player, goal.CoinCost))
        {
            if (goal.NotEnoughCoinsTextId != 0)
                player.SendTunneled(new ChatPacketFromStringId { SpeakerGuid = npc.Guid, StringId = goal.NotEnoughCoinsTextId });

            return false;
        }

        if (!_rewardManager.TryTakeItems(player, goal.TakeItemDefinitionId, goal.TakeItemCount))
            return false;

        if (goal.NpcUseAnimationId != 0)
            player.SendTunneledToVisible(new PlayerUpdatePacketSetAnimation { Guid = npc.Guid, AnimationId = goal.NpcUseAnimationId }, true);

        return true;
    }

    private void RecountItemGoals(Player player, QuestDefinition quest)
    {
        foreach (var itemDefinitionId in quest.Goals.OfType<ItemCountGoal>().Select(goal => goal.ItemDefinitionId).Distinct().ToList())
            OnEvent(player, new ItemCountChangedEvent(itemDefinitionId, _rewardManager.CountItems(player, itemDefinitionId)));

        CheckEquippedItems(player, quest);
    }

    private void CheckEquippedItems(Player player, QuestDefinition quest)
    {
        var equippedItemDefinitionIds = player.ActiveProfile.Items.Values
            .Select(profileItem => player.Items.FirstOrDefault(item => item.Id == profileItem.Id)?.Definition ?? 0)
            .ToHashSet();

        foreach (var itemDefinitionId in quest.Goals.OfType<EquipItemGoal>().Select(goal => goal.ItemDefinitionId).Where(equippedItemDefinitionIds.Contains).Distinct().ToList())
            OnEvent(player, new ItemEquippedEvent(itemDefinitionId));
    }

    private void GrantAcceptItems(Player player, QuestDefinition quest)
    {
        foreach (var group in quest.AcceptItems.GroupBy(itemDefinitionId => itemDefinitionId))
        {
            if (_resourceManager.ClientItemDefinitions.TryGetValue(group.Key, out var itemDefinition))
                _rewardManager.TryGrantItem(player, group.Key, itemDefinition.IsTintable ? 0 : itemDefinition.Icon.TintId, group.Count());
        }
    }

    private static bool TurnsInHere(QuestDefinition quest, QuestProgress progress, Npc npc)
        => progress.GoalIndex >= quest.Goals.Count && quest.GoalTargetNpcId(quest.Goals.Count - 1) == npc.DefinitionId;

    private void AdvanceAtNpc(Player player, QuestDefinition quest, Npc npc)
    {
        if (!player.Quests.TryGetInProgress(quest.QuestId, out var progress))
            return;

        if (TurnsInHere(quest, progress, npc))
        {
            TurnIn(player, quest);
            return;
        }

        var goalIndex = TalkGoalHere(quest, progress, npc);
        if (goalIndex < 0)
        {
            var appearanceGoalIndex = AppearanceGoalHere(quest, progress, npc);

            if (appearanceGoalIndex >= 0)
                UseAppearanceGoal(player, quest.Goals[appearanceGoalIndex], npc);

            return;
        }

        ConfirmPayment(player, quest.Goals[goalIndex], npc, () => AdvanceTalkGoal(player, quest, goalIndex, npc));
    }

    private static void ConfirmPayment(Player player, QuestGoal goal, Npc npc, Action onConfirmed)
    {
        if (goal.PayPromptTextId == 0)
        {
            onConfirmed();
            return;
        }

        QuestDialogue.Begin(player,
        [
            new QuestDialogueLine
            {
                TextId = goal.PayPromptTextId,
                Choices =
                [
                    new QuestDialogueChoice { ResponseTextId = goal.PayResponseTextId },
                    new QuestDialogueChoice { ResponseTextId = goal.DeclinePayResponseTextId, Cancels = true }
                ]
            }
        ], npc.Guid, onConfirmed);
    }

    private void UseAppearanceGoal(Player player, QuestGoal goal, Npc npc)
    {
        ConfirmPayment(player, goal, npc, () =>
        {
            if (TryPayForGoal(player, goal, npc))
                ApplyCompletionAppearance(player, goal);
        });
    }

    private void AdvanceTalkGoal(Player player, QuestDefinition quest, int goalIndex, Npc npc)
    {
        if (!player.Quests.TryGetInProgress(quest.QuestId, out var progress) || !progress.OpenGoals.ContainsKey(goalIndex))
            return;

        var goal = quest.Goals[goalIndex];

        if (!TryPayForGoal(player, goal, npc))
            return;

        if (goal is TalkToNpcGoal { CompleteAfterDialogue: true })
        {
            QuestDialogue.Begin(player, goal.ConversationFor(npc.DefinitionId), npc.Guid, () =>
            {
                if (player.Quests.TryGetInProgress(quest.QuestId, out var current) && current.OpenGoals.ContainsKey(goalIndex))
                    CompleteGoal(player, quest, current, goalIndex, npc, playDialogue: false);
            });
            return;
        }

        var result = goal.Progress(quest, new NpcTalkedEvent(npc), progress.ProgressFor(goalIndex));

        ApplyProgress(player, quest, progress, goalIndex, result, npc);

        if (result != QuestGoalResult.Completed && goal is TalkToNpcGoal { IsCounted: true })
            QuestDialogue.Begin(player, goal.ConversationFor(npc.DefinitionId), npc.Guid);
    }

    public void OnEvent(Player player, QuestEvent questEvent)
    {
        foreach (var (quest, progress, goalIndex, goal) in ActiveGoals(player).ToList())
        {
            if (!progress.OpenGoals.TryGetValue(goalIndex, out var goalProgress))
                continue;

            var result = goal.Progress(quest, questEvent, goalProgress);

            if (result == QuestGoalResult.None)
                continue;

            ApplyProgress(player, quest, progress, goalIndex, result);

            if (questEvent.CreditsOneQuest)
                return;
        }
    }

    private void ApplyProgress(Player player, QuestDefinition quest, QuestProgress progress, int goalIndex, QuestGoalResult result, Npc? npc = null)
    {
        if (result == QuestGoalResult.Completed)
        {
            CompleteGoal(player, quest, progress, goalIndex, npc);
            return;
        }

        if (result == QuestGoalResult.Progressed)
        {
            var goal = quest.Goals[goalIndex];
            var goalProgress = progress.ProgressFor(goalIndex);

            player.SendTunneled(new QuestObjectiveUpdatePacket
            {
                QuestId = quest.QuestId,
                ObjectiveId = goal.NameId,
                CurrentCount = goalProgress.Count,
                CompletedPercentage = QuestCompletion(quest, progress.GoalsDone),
                Unknown6 = RetailObjectiveUpdateUnknown6
            });

            SaveGoalProgress(player, quest.QuestId, goal.NameId, goalProgress);

            if (npc is not null)
                RefreshQuestNotification(player, npc.Guid);
        }

        RefreshObjectiveTarget(player);
    }

    private void SaveQuestRow(Player player, int questId, Action<DbCharacterQuest> update)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var dbQuest = db.CharacterQuests.FirstOrDefault(x => x.QuestId == questId && x.CharacterId == player.CharacterId);
        if (dbQuest is null)
            return;

        update(dbQuest);
        db.SaveChanges();
    }

    private void SaveGoalProgress(Player player, int questId, int goalId, GoalProgress progress)
    {
        using var db = _dbContextFactory.CreateDbContext();

        db.CharacterQuestProgress
            .Where(p => p.CharacterId == player.CharacterId && p.QuestId == questId && p.GoalId == goalId)
            .ExecuteDelete();

        foreach (var (targetId, count) in progress.Save())
        {
            db.CharacterQuestProgress.Add(new DbCharacterQuestProgress
            {
                CharacterId = player.CharacterId,
                QuestId = questId,
                GoalId = goalId,
                TargetId = targetId,
                Count = count
            });
        }

        db.SaveChanges();
    }

    private void SaveGoalDone(Player player, int questId, int goalId)
    {
        using var db = _dbContextFactory.CreateDbContext();

        db.CharacterQuestProgress
            .Where(p => p.CharacterId == player.CharacterId && p.QuestId == questId && p.GoalId == goalId)
            .ExecuteDelete();

        db.CharacterQuestProgress.Add(new DbCharacterQuestProgress
        {
            CharacterId = player.CharacterId,
            QuestId = questId,
            GoalId = goalId,
            TargetId = 0,
            Count = GoalDoneCount
        });

        db.SaveChanges();
    }

    private void DeleteSavedGoalProgress(Player player, int questId)
    {
        using var db = _dbContextFactory.CreateDbContext();

        db.CharacterQuestProgress
            .Where(p => p.CharacterId == player.CharacterId && p.QuestId == questId)
            .ExecuteDelete();
    }

    private void SaveActiveQuestId(Player player, int questId)
    {
        using var db = _dbContextFactory.CreateDbContext();

        var dbCharacter = db.Characters.FirstOrDefault(c => c.Id == player.CharacterId);
        if (dbCharacter is null)
            return;

        dbCharacter.ActiveQuestId = questId != 0 ? questId : null;
        db.SaveChanges();
    }

    public void AcceptQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest) || !IsOfferable(player, quest))
            return;

        var isRepeat = player.Quests.Remove(questId);

        var progress = new QuestProgress(questId);
        progress.StartStep(quest, 0);

        player.Quests.Add(progress);
        player.Quests.ActiveQuestId = questId;
        player.Quests.LastAcceptedAt = DateTime.UtcNow;

        using (var db = _dbContextFactory.CreateDbContext())
        {
            if (isRepeat && db.CharacterQuests.FirstOrDefault(x => x.QuestId == questId && x.CharacterId == player.CharacterId) is { } previous)
            {
                db.CharacterQuests.Remove(previous);
                db.SaveChanges();
            }

            db.CharacterQuests.Add(new DbCharacterQuest
            {
                QuestId = questId,
                CharacterId = player.CharacterId,
                Completed = false,
                CurrentGoalId = quest.Goals[0].NameId,
                Started = progress.Started
            });

            var dbCharacter = db.Characters.FirstOrDefault(c => c.Id == player.CharacterId);
            if (dbCharacter is not null)
                dbCharacter.ActiveQuestId = questId;

            db.SaveChanges();
        }

        SendActiveState(player, quest);

        RefreshQuestNotifications(player, quest);

        RefreshQuestNodes(player, quest);

        RefreshActiveQuestNpcs(player, quest);

        player.SendTunneled(new CommandPacketQuestDialogComplete());

        GrantAcceptItems(player, quest);

        RecountItemGoals(player, quest);
    }

    public bool CompleteQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest))
            return false;

        if (!player.Quests.TryGetInProgress(questId, out var progress) || progress.GoalIndex < quest.Goals.Count)
            return false;

        player.Quests.Complete(questId);
        SaveQuestRow(player, questId, q =>
        {
            q.Completed = true;
            q.CompletedAt = progress.CompletedAt;
        });

        if (player.Quests.ActiveQuestId == questId)
        {
            player.Quests.ActiveQuestId = 0;
            SaveActiveQuestId(player, 0);
        }

        player.SendTunneled(new QuestCompletePacket { QuestId = questId });

        player.SendTunneled(new CompletedQuestCountUpdatePacket
        {
            Count = player.Quests.CompletedCount
        });

        SendJournalQuestStates(player);

        GrantReward(player, quest);

        if (quest.CompletionScene is { } scene)
            QuestScene.Play(player, scene, $"quest-scene-{quest.QuestId}", _logger);

        QuestCompleted?.Invoke(player, quest);

        RefreshActiveQuestNpcs(player, quest);

        RefreshQuestNotifications(player, quest);

        if (quest.NextQuestId is { } nextQuestId && _resourceManager.Quests.TryGet(nextQuestId, out var next))
            RefreshQuestNotifications(player, next.GiverNpcId);

        RefreshObjectiveTarget(player);
        return true;
    }

    public bool OfferNextQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest)
            || quest.NextQuestId is not { } nextQuestId
            || !_resourceManager.Quests.TryGet(nextQuestId, out var next)
            || next.GiverNpcId != quest.GoalTargetNpcId(quest.Goals.Count - 1)
            || !IsOfferable(player, next))
            return false;

        Offer(player, next, NextOfferDelayMs);
        return true;
    }

    private const int NextOfferDelayMs = 750;

    public bool OfferQuest(Player player, int questId, ulong npcGuid)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest) || !IsOfferable(player, quest))
            return false;

        Offer(player, quest, npcGuid: npcGuid);
        return true;
    }

    private static readonly TimeSpan AbandonBlockedAfterAccept = TimeSpan.FromSeconds(3);

    public void AbandonQuest(Player player, int questId)
    {
        if (DateTime.UtcNow - player.Quests.LastAcceptedAt < AbandonBlockedAfterAccept)
            return;

        if (!player.Quests.TryGetInProgress(questId, out _))
            return;

        if (!_resourceManager.Quests.TryGet(questId, out var quest))
            return;

        RemoveQuest(player, quest);
    }

    public bool ResetQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest) || !player.Quests.Contains(questId))
            return false;

        var wasCompleted = player.Quests.IsCompleted(questId);

        RemoveQuest(player, quest);

        if (wasCompleted)
            player.SendTunneled(new CompletedQuestCountUpdatePacket { Count = player.Quests.CompletedCount });

        return true;
    }

    private void RemoveQuest(Player player, QuestDefinition quest)
    {
        var questId = quest.QuestId;

        player.Quests.Remove(questId);
        QuestDialogue.Clear(player);

        if (player.Quests.PendingTurnInQuestId == questId)
            player.Quests.PendingTurnInQuestId = 0;

        var wasActive = player.Quests.ActiveQuestId == questId;
        if (wasActive)
            player.Quests.ActiveQuestId = 0;

        using (var db = _dbContextFactory.CreateDbContext())
        {
            var dbQuest = db.CharacterQuests.FirstOrDefault(x => x.QuestId == questId && x.CharacterId == player.CharacterId);
            if (dbQuest is not null)
                db.CharacterQuests.Remove(dbQuest);

            if (wasActive)
            {
                var dbCharacter = db.Characters.FirstOrDefault(c => c.Id == player.CharacterId);
                if (dbCharacter is not null)
                    dbCharacter.ActiveQuestId = null;
            }

            db.SaveChanges();
        }

        player.SendTunneled(new QuestAbandonedPacket { QuestId = questId });

        RefreshQuestNodes(player, quest);

        RefreshActiveQuestNpcs(player, quest);

        RefreshQuestNotifications(player, quest);

        RefreshObjectiveTarget(player);
    }

    public void SetActiveQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest))
            return;

        if (player.Quests.TryGetInProgress(questId, out var progress))
        {
            player.Quests.ActiveQuestId = questId;
            SaveActiveQuestId(player, questId);

            foreach (var goalIndex in progress.OpenGoals.Keys.Order())
                SendObjectiveActivated(player, questId, quest.Goals[goalIndex]);

            SendObjectiveForGoal(player, quest, progress.FirstOpenGoalIndex);
        }
    }

    public ClientQuestData GetClientQuestData(Player player)
    {
        var questData = new ClientQuestData
        {
            CompletedCount = player.Quests.CompletedCount,
            ActiveQuestId = player.Quests.TryGetInProgress(player.Quests.ActiveQuestId, out _) ? player.Quests.ActiveQuestId : 0
        };

        foreach (var progress in player.Quests.InProgress)
        {
            if (_resourceManager.Quests.TryGet(progress.QuestId, out var quest))
                questData.Quests.Add(ClientQuestFor(player, quest, progress));
        }

        return questData;
    }

    public void SendActiveObjectives(Player player)
    {
        foreach (var (quest, _, _, goal) in ActiveGoals(player))
            SendObjectiveActivated(player, quest.QuestId, goal);
    }

    public void RestoreJournal(Player player)
    {
        RefreshObjectiveTarget(player);

        SendJournalQuestStates(player);
    }

    private void SendJournalQuestStates(Player player)
    {
        var states = new Dictionary<int, int>();
        foreach (var progress in player.Quests.All)
            if (progress.Completed)
                states[progress.QuestId] = 1;

        if (states.Count > 0)
            player.SendTunneled(new AdventurersJournalQuestUpdatePacket { QuestStates = states });
    }

    private void RefreshQuestNotifications(Player player, QuestDefinition quest)
    {
        var npcIds = new HashSet<int> { quest.GiverNpcId, quest.TargetNpcId };

        foreach (var goal in quest.Goals.OfType<TalkToNpcGoal>())
            npcIds.UnionWith(goal.AllTalkTargetNpcIds());

        npcIds.UnionWith(quest.StateNpcIds());

        foreach (var excludedId in quest.ExcludesQuestIds)
        {
            if (!_resourceManager.Quests.TryGet(excludedId, out var excludedQuest))
                continue;

            npcIds.Add(excludedQuest.GiverNpcId);
            npcIds.Add(excludedQuest.TargetNpcId);
        }

        foreach (var npcId in npcIds)
            RefreshQuestNotifications(player, npcId);
    }

    private static void ApplyCompletionAppearance(Player player, QuestGoal goal)
    {
        if (goal.CompletionAppearanceModelIds.Count == 0)
            return;

        var modelId = goal.CompletionAppearanceModelIds[Random.Shared.Next(goal.CompletionAppearanceModelIds.Count)];

        player.ApplyTemporaryAppearance(modelId, goal.CompletionAppearanceMs, goal.CompletionAppearanceEffectId, goal.CompletionAppearanceNameId);
    }

    private void RefreshActiveQuestNpcs(Player player, QuestDefinition quest)
    {
        if (quest.NpcIdsWhileActive.Count == 0)
            return;

        foreach (var npc in player.Zone.Npcs.Where(npc => quest.NpcIdsWhileActive.Contains(npc.DefinitionId)).ToList())
        {
            var hidden = IsHiddenByQuest(player, npc);
            var shown = player.VisibleNpcs.ContainsKey(npc.Guid);

            if (hidden && shown)
                player.OnRemoveVisibleNpcGracefully(npc, animate: false);
            else if (!hidden && !shown && npc.VisiblePlayers.ContainsKey(player.Guid))
                player.OnAddVisibleNpcs(npc);
        }
    }

    private static void RemoveHiddenNpcs(Player player, QuestGoal goal)
    {
        foreach (var npcId in goal.HideNpcIdsWhenDone)
        {
            foreach (var npc in player.VisibleNpcs.Values.Where(npc => npc.DefinitionId == npcId).ToList())
            {
                player.SendTunneled(EntityHelper.GetRemovePlayerGracefullyPacket(npc.Guid, animate: goal.HideEffectId != 0,
                    delay: goal.HideDelayMs, effectDelay: goal.HideDelayMs, compositeEffectId: goal.HideEffectId));
                player.VisibleNpcs.TryRemove(npc.Guid, out _);
            }
        }
    }

    private void RefreshQuestNotifications(Player player, int npcId)
    {
        if (npcId == 0)
            return;

        foreach (var npc in player.Zone.Npcs.Where(npc => npc.DefinitionId == npcId).ToList())
            RefreshQuestNotification(player, npc.Guid);
    }

    public void RefreshQuestNotification(Player player, ulong npcGuid)
    {
        if (!player.VisibleNpcs.TryGetValue(npcGuid, out var npc))
            return;

        var removeNotifications = new PlayerUpdatePacketRemoveNotifications();

        foreach (var (iconState, effectId) in QuestBadgeIconStates(npc))
            removeNotifications.Notifications.Add(new RemoveNotificationInfo { Guid = npc.Guid, ThoughtBubbleIconId = iconState, CompositeEffectId = effectId });

        if (npc.Notification is { } notification)
        {
            removeNotifications.Notifications.Add(new RemoveNotificationInfo
            {
                Guid = npc.Guid,
                ThoughtBubbleIconId = notification.IconState,
                CompositeEffectId = notification.Unknown2
            });
        }

        if (removeNotifications.Notifications.Count > 0)
            player.SendTunneled(removeNotifications);

        var questBadge = GetQuestBadge(player, npc);

        var cursorEntry = questBadge is not null && !npc.IsOwnNotification(questBadge)
            ? InteractionMenuHelper.GetQuestCursorEntry(npc.Guid)
            : InteractionMenuHelper.GetOwnCursorEntry(npc);
        player.SendTunneled(new PlayerUpdatePacketNpcRelevance { Entries = { cursorEntry } });

        if ((questBadge ?? npc.Notification) is { } badge && !npc.IsListedNotification(badge))
            player.SendTunneled(new PlayerUpdatePacketAddNotifications { Notifications = { badge } });
    }

    public const int QuestBadgeNotificationType = 2;
    private const int AvailableBadgeIconId = 2;

    public NotificationInfo? GetQuestBadge(Player player, Npc npc)
    {
        var (imageId, waiting) = QuestBadgeImageId(player, npc);
        if (imageId == 0)
            return null;

        if (waiting && npc.OwnNotification is { } ownNotification)
            return ownNotification;

        return new NotificationInfo
        {
            Guid = npc.Guid,
            IsCompact = false,
            NotificationType = QuestBadgeNotificationType,
            IconId = waiting ? 0 : AvailableBadgeIconId,
            IconState = imageId,
            NameId = npc.NameId,
            ReferenceId = npc.SubTextNameId,
            Enabled = true
        };
    }

    private (int ImageId, bool Waiting) QuestBadgeImageId(Player player, Npc npc)
    {
        var quests = _resourceManager.Quests;

        if (quests.ByGiver.TryGetValue(npc.DefinitionId, out var giverQuestIds))
        {
            foreach (var questId in giverQuestIds)
            {
                if (quests.TryGet(questId, out var quest) && IsOfferable(player, quest))
                    return (quest.NotificationAvailable, false);
            }
        }

        if (quests.ByTarget.TryGetValue(npc.DefinitionId, out var targetQuestIds))
        {
            foreach (var questId in targetQuestIds)
            {
                if (player.Quests.TryGetInProgress(questId, out var progress) && quests.TryGet(questId, out var quest)
                    && quest.IsWaitingOn(progress, npc))
                    return (quest.NotificationActive, true);
            }
        }

        return (0, false);
    }

    public bool CanGatherQuestNode(Player player, CollectionNode node)
        => ActiveGoals(player).Any(active => active.Goal is CollectGoal collectGoal
            && collectGoal.CanGather(node, active.Progress.OpenGoals[active.GoalIndex]));

    private void RefreshQuestNodes(Player player, QuestDefinition quest)
    {
        var nodeTypes = quest.Goals.OfType<CollectGoal>().Select(goal => goal.CollectNodeType).ToHashSet();
        if (nodeTypes.Count == 0)
            return;

        foreach (var node in player.Zone.Npcs.OfType<CollectionNode>()
            .Where(node => node.PoolDefinition.PlayerSpecific && nodeTypes.Contains(node.TypeDefinition.Key)).ToList())
        {
            var hidden = !CanGatherQuestNode(player, node);
            var shown = player.VisibleNpcs.ContainsKey(node.Guid);

            if (hidden && shown)
                player.OnRemoveVisibleNpcGracefully(node, animate: true);
            else if (!hidden && !shown && node.VisiblePlayers.ContainsKey(player.Guid))
                player.OnAddVisibleNpcs(node);
        }
    }

    public bool IsHiddenByQuest(Player player, Npc npc)
    {
        if (npc is CollectionNode { PoolDefinition.PlayerSpecific: true } questNode)
            return !CanGatherQuestNode(player, questNode);

        var quests = _resourceManager.Quests;

        if (!quests.ByStateNpc.TryGetValue(npc.DefinitionId, out var questIds))
            return false;

        foreach (var questId in questIds)
        {
            if (quests.TryGet(questId, out var quest) && quest.HidesNpcFor(player.Quests, npc.DefinitionId))
                return true;
        }

        return false;
    }

    public bool IsNeededByActiveQuest(Player player, Npc npc)
        => player.Quests.InProgress.Any(progress => _resourceManager.Quests.TryGet(progress.QuestId, out var quest)
            && (quest.TargetNpcId == npc.DefinitionId || quest.Goals.Any(goal => goal.Matches(quest, new NpcTalkedEvent(npc)))));

    public int GetQuestEffectId(Player player, Npc npc)
    {
        var quests = _resourceManager.Quests;

        if (!quests.ByStateNpc.TryGetValue(npc.DefinitionId, out var questIds))
            return 0;

        foreach (var questId in questIds)
        {
            if (quests.TryGet(questId, out var quest) && quest.NpcEffectIdFor(player.Quests, npc.DefinitionId) is var effectId and not 0)
                return effectId;
        }

        return 0;
    }

    private static void RefreshNpcEffects(Player player, QuestGoal goal)
    {
        foreach (var effect in goal.NpcEffectsUntilDone)
        {
            foreach (var npc in player.VisibleNpcs.Values.Where(npc => npc.DefinitionId == effect.NpcId).ToList())
            {
                player.OnRemoveVisibleNpcs(npc);
                player.OnAddVisibleNpcs(npc);
            }
        }
    }

    private HashSet<(int IconState, int EffectId)> QuestBadgeIconStates(Npc npc)
    {
        var quests = _resourceManager.Quests;
        var states = new HashSet<(int IconState, int EffectId)>();

        if (quests.ByGiver.TryGetValue(npc.DefinitionId, out var giverQuestIds))
        {
            foreach (var questId in giverQuestIds)
            {
                if (quests.TryGet(questId, out var quest) && quest.NotificationAvailable != 0)
                    states.Add((quest.NotificationAvailable, 0));
            }
        }

        if (quests.ByTarget.TryGetValue(npc.DefinitionId, out var targetQuestIds))
        {
            foreach (var questId in targetQuestIds)
            {
                if (quests.TryGet(questId, out var quest) && quest.NotificationActive != 0)
                    states.Add((quest.NotificationActive, 0));
            }
        }

        return states;
    }

    private const float DefaultOfferCameraDistance = 0f;

    private void Offer(Player player, QuestDefinition quest, int delayMs = 0, ulong npcGuid = 0)
    {
        var offer = new QuestInfoPacket
        {
            QuestId = quest.QuestId,
            NpcDialogId = quest.GiverDialogueId,
            NpcAnimationId = QuestDialogue.TalkAnimationId,
            NameId = quest.TitleId,
            DescriptionId = quest.DescriptionId,
            MembersOnly = false,
            NpcGuid = npcGuid != 0 ? npcGuid : EntityHelper.NearestNpcGuid(player, quest.GiverNpcId),
            CameraDistance = quest.CameraDistance ?? DefaultOfferCameraDistance,
            Unknown11 = false,
            AcceptOnly = false
        };

        FillRewardBundle(offer.RewardBundle, player, quest);

        if (delayMs > 0)
            player.SendTunneledDelayed(offer, delayMs);
        else
            player.SendTunneled(offer);
    }

    private bool FitsPlayerGender(Player player, int itemDefinitionId) =>
        !_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition)
        || itemDefinition.GenderUsage == 0
        || itemDefinition.GenderUsage == player.Gender;

    private void FillRewardBundle(RewardBundleBase bundle, Player player, QuestDefinition quest)
    {
        bundle.Coins = quest.RewardCoins;
        bundle.Experience = quest.RewardExperience;
        bundle.Unknown3 = 0;
        bundle.Multiplier = 1f;
        bundle.IconId = -1;
        bundle.NameId = -1;

        foreach (var entry in BuildRewardEntries(player, quest))
            bundle.Entries.Add(entry);

        bundle.Success = bundle.Entries.Any(entry => entry is RewardBundleEntryCollectionAdd);
    }

    private List<RewardBundleEntryBase> BuildRewardEntries(Player player, QuestDefinition quest)
    {
        var entries = new List<RewardBundleEntryBase>();
        var shownItemIds = quest.RandomRewardDisplayItemId != 0
            ? quest.RewardItems.Append(quest.RandomRewardDisplayItemId)
            : quest.RewardItems;

        foreach (var group in shownItemIds.Where(id => FitsPlayerGender(player, id)).GroupBy(id => id))
        {
            var definitionId = group.Key;

            if (_resourceManager.ClientItemDefinitions.TryGetValue(definitionId, out var itemDef))
            {
                entries.Add(new RewardBundleEntryItem
                {
                    IconId = itemDef.Icon.Id,
                    TintId = itemDef.Icon.TintId,
                    NameId = itemDef.NameId,
                    DefinitionId = definitionId,
                    Quantity = group.Count()
                });
            }
        }

        if (quest.RewardCollectionId != 0 &&
            _resourceManager.Collections.TryGetValue(quest.RewardCollectionId, out var collectionDef))
        {
            entries.Add(new RewardBundleEntryCollectionAdd
            {
                IconId = collectionDef.IconId,
                TintId = collectionDef.IconTintId,
                NameId = collectionDef.NameId,
                CollectionId = collectionDef.Id
            });
        }

        return entries;
    }

    private static ulong NearestUntalkedTarget(Player player, TalkToNpcGoal goal, GoalProgress goalProgress)
    {
        var untalkedNpcIds = goal.AllTalkTargetNpcIds()
            .Where(npcId => goalProgress is not TalkProgress talkProgress || !talkProgress.HasTalkedTo(npcId))
            .ToHashSet();

        return EntityHelper.NearestNpc(player, npc => untalkedNpcIds.Contains(npc.DefinitionId))?.Guid ?? 0;
    }

    private void CompleteGoal(Player player, QuestDefinition quest, QuestProgress progress, int goalIndex, Npc? spokenBy = null, bool playDialogue = true)
    {
        var goals = quest.Goals;
        var completedGoal = goals[goalIndex];

        var stepDone = progress.FinishGoal(quest, goalIndex);
        bool isFinalGoal = progress.GoalIndex >= goals.Count;

        player.SendTunneled(new QuestObjectiveCompletePacket
        {
            QuestId = quest.QuestId,
            ObjectiveId = completedGoal.NameId,
            Percent = QuestCompletion(quest, progress.GoalsDone),
            Silent = isFinalGoal
        });

        if (stepDone)
        {
            SaveQuestRow(player, quest.QuestId, q => q.CurrentGoalId = isFinalGoal ? AllGoalsDoneGoalId : goals[progress.GoalIndex].NameId);
            DeleteSavedGoalProgress(player, quest.QuestId);
        }
        else
        {
            SaveGoalDone(player, quest.QuestId, completedGoal.NameId);
        }

        ApplyCompletionAppearance(player, completedGoal);
        RemoveHiddenNpcs(player, completedGoal);
        RefreshNpcEffects(player, completedGoal);

        if (completedGoal.CompletionScene is { } goalScene)
            QuestScene.Play(player, goalScene, $"quest-scene-{quest.QuestId}-{completedGoal.NameId}", _logger);
        RefreshQuestNodes(player, quest);
        RefreshQuestNotifications(player, quest);

        if (isFinalGoal)
        {
            TurnIn(player, quest);
            return;
        }

        if (stepDone)
        {
            foreach (var openGoalIndex in progress.OpenGoals.Keys.Order())
            {
                player.SendTunneled(new QuestObjectiveAddedPacket
                {
                    QuestId = quest.QuestId,
                    Objective = ObjectiveFor(goals[openGoalIndex], ObjectiveActive)
                });
                SendObjectiveActivated(player, quest.QuestId, goals[openGoalIndex], restored: false);
            }
        }

        SendObjectiveForGoal(player, quest, progress.FirstOpenGoalIndex);

        if (playDialogue && completedGoal is TalkToNpcGoal)
        {
            var speakerNpcId = spokenBy?.DefinitionId ?? quest.GoalTargetNpcId(goalIndex);
            var speakerGuid = spokenBy?.Guid ?? EntityHelper.NearestNpcGuid(player, speakerNpcId);

            QuestDialogue.Begin(player, completedGoal.ConversationFor(speakerNpcId), speakerGuid);
        }

        if (stepDone)
            RecountItemGoals(player, quest);
    }

    private const int YouGotItTextId = 103085;

    private const int GreenCheckImageId = 300;

    private const int GreenButtonImageSet = 17;

    private void TurnIn(Player player, QuestDefinition quest)
    {
        if (player.Quests.PendingTurnInQuestId == quest.QuestId && DateTime.UtcNow - player.Quests.PendingTurnInAt < PendingTurnInTimeout)
            return;

        var end = new QuestEndPacket
        {
            NpcGuid = EntityHelper.NearestNpcGuid(player, quest.GoalTargetNpcId(quest.Goals.Count - 1)),
            QuestId = quest.QuestId,
            EndDialogTextId = quest.TurnInDialogueId,
            NpcAnimationId = QuestDialogue.TalkAnimationId,
            CameraDistance = quest.CameraDistance ?? DefaultOfferCameraDistance
        };

        FillRewardBundle(end.RewardBundle, player, quest);

        player.SendTunneled(end);

        player.Quests.PendingTurnInQuestId = quest.QuestId;
        player.Quests.PendingTurnInAt = DateTime.UtcNow;
    }

    private static readonly TimeSpan PendingTurnInTimeout = TimeSpan.FromSeconds(30);

    private const int QuestListRewardUnknown3 = 15;
    private const int CoinsIconId = 102;
    private const int CoinsTextId = 6513;
    private const int ExperienceIconId = 101;

    private RewardBundleBase QuestListRewardBundle(Player player, QuestDefinition quest)
    {
        var bundle = new RewardBundleBase
        {
            Coins = quest.RewardCoins,
            Experience = quest.RewardExperience,
            Unknown3 = QuestListRewardUnknown3,
            PlayerGuid = player.Guid
        };

        bundle.Entries.AddRange(BuildRewardEntries(player, quest));

        if (bundle.Entries.Count > 0)
        {
            bundle.IconId = bundle.Entries[0].IconId;
            bundle.NameId = bundle.Entries[0].NameId;
        }
        else if (quest.RewardCoins > 0)
        {
            bundle.IconId = CoinsIconId;
            bundle.NameId = CoinsTextId;
        }
        else if (quest.RewardExperience > 0)
        {
            bundle.IconId = ExperienceIconId;
        }

        return bundle;
    }

    private ClientQuest NewClientQuest(Player player, QuestDefinition quest, QuestProgress progress, bool suppressStartBanner) => new()
    {
        QuestId = quest.QuestId,
        TitleId = quest.TitleId,
        DescriptionId = quest.DescriptionId,
        HelperTextId = quest.ObjectiveDescriptionId,
        MembersOnly = false,
        TimeStarted = progress.Started.ToUnixTimeSeconds(),
        ProfileId = 0,
        CompletedPercentage = QuestCompletion(quest, progress.GoalsDone),
        SystemQuest = false,
        SuppressStartBanner = suppressStartBanner,
        RewardBundle = QuestListRewardBundle(player, quest)
    };

    private ClientQuest ClientQuestFor(Player player, QuestDefinition quest, QuestProgress progress)
    {
        var clientQuest = NewClientQuest(player, quest, progress, suppressStartBanner: true);
        var goals = quest.Goals;

        for (int i = 0; i < progress.StepEnd; i++)
        {
            clientQuest.Objectives[i + 1] = progress.IsGoalDone(i)
                ? ObjectiveFor(goals[i], ObjectiveComplete)
                : ObjectiveFor(goals[i], ObjectiveInactive, progress.ProgressFor(i).Count);
        }

        return clientQuest;
    }

    private void SendActiveState(Player player, QuestDefinition quest)
    {
        if (!player.Quests.TryGetInProgress(quest.QuestId, out var progress))
            return;

        player.SendTunneled(new QuestAddPacket
        {
            Quest = NewClientQuest(player, quest, progress, suppressStartBanner: false),
            ShowInTracker = quest.QuestId == player.Quests.ActiveQuestId
        });

        var goals = quest.Goals;

        for (int i = 0; i < progress.StepEnd; i++)
        {
            player.SendTunneled(new QuestObjectiveAddedPacket
            {
                QuestId = quest.QuestId,
                Objective = progress.IsGoalDone(i)
                    ? ObjectiveFor(goals[i], ObjectiveComplete)
                    : ObjectiveFor(goals[i], ObjectiveActive, progress.ProgressFor(i).Count)
            });
        }

        foreach (var goalIndex in progress.OpenGoals.Keys.Order())
            SendObjectiveActivated(player, quest.QuestId, goals[goalIndex], restored: false);

        SendObjectiveForGoal(player, quest, progress.FirstOpenGoalIndex);
    }

    private const int ObjectiveInactive = 0;
    private const int ObjectiveActive = 1;
    private const int ObjectiveComplete = 2;

    private static QuestObjectiveInfo ObjectiveFor(QuestGoal goal, int state, int currentCount = 0) => new()
    {
        ObjectiveId = goal.NameId,
        NameId = goal.NameId,
        DescriptionId = goal.DescriptionId != 0 ? goal.DescriptionId : goal.NameId,
        State = state,
        CurrentCount = state == ObjectiveComplete ? goal.RequiredCount : currentCount,
        RequiredCount = goal.RequiredCount
    };

    private static void SendObjectiveActivated(Player player, int questId, QuestGoal goal, bool restored = true)
    {
        player.SendTunneled(new QuestObjectiveActivatedPacket
        {
            QuestId = questId,
            ObjectiveId = goal.NameId,
            RequiredCount = (restored || goal.RequiredCount > 1) ? goal.RequiredCount : 0,
            Unknown2 = restored
        });
    }

    private const int RetailObjectiveUpdateUnknown6 = 82;

    private static float QuestCompletion(QuestDefinition quest, int goalsDone) => (float)goalsDone / quest.Goals.Count;

    private ulong ResolveGoalTargetGuid(Player player, QuestDefinition quest, int goalIndex)
    {
        var goals = quest.Goals;

        if (goalIndex >= 0 && goalIndex < goals.Count && goals[goalIndex] is TalkToNpcGoal { IsCounted: true } countedGoal
            && player.Quests.TryGetInProgress(quest.QuestId, out var progress))
        {
            var untalked = NearestUntalkedTarget(player, countedGoal, progress.ProgressFor(goalIndex));
            if (untalked != 0)
                return untalked;
        }

        return EntityHelper.NearestNpcGuid(player, quest.GoalTargetNpcId(goalIndex));
    }

    private const ulong LocationTargetGuidBase = 0x7000000000000000;

    private void SendObjectiveForGoal(Player player, QuestDefinition quest, int goalIndex)
    {
        var goals = quest.Goals;

        if (quest.HideObjectiveTarget || (goalIndex >= 0 && goalIndex < goals.Count && goals[goalIndex] is CollectGoal))
        {
            player.SendTunneled(new ObjectiveTargetUpdatePacket { Active = false });
            return;
        }

        var goalSpot = GoalSpot(quest, goalIndex);

        if (goalSpot.Position != Vector3.Zero)
        {
            if (goalSpot.ZoneDefinitionId != player.Zone.DefinitionId)
            {
                player.SendTunneled(new ObjectiveTargetUpdatePacket { Active = false });
                return;
            }

            var reachPos = new Vector4(goalSpot.Position, 1f);

            player.SendTunneled(new ObjectiveTargetUpdatePacket
            {
                Active = true,
                TaskId = player.ObjectiveTaskId,
                Guid = GuidHelper.GetLocationTargetGuid(goals[goalIndex].NameId),
                NameId = goals[goalIndex].NameId,
                PositionX = reachPos.X,
                PositionY = reachPos.Y,
                PositionZ = reachPos.Z,
                PositionW = 1f
            });
            return;
        }

        SendObjectiveTarget(player, ResolveGoalTargetGuid(player, quest, goalIndex));
    }

    private void SendObjectiveTarget(Player player, ulong targetGuid)
    {
        if (targetGuid == 0 || !player.Zone.TryGetNpc(targetGuid, out var target))
            return;

        var pos = InteractionMenuHelper.GetApproachPosition(target.Position, player.Position);

        player.SendTunneled(new ObjectiveTargetUpdatePacket
        {
            Active = true,
            TaskId = player.ObjectiveTaskId,
            Guid = targetGuid,
            NameId = target.NameId,
            PositionX = pos.X,
            PositionY = pos.Y,
            PositionZ = pos.Z,
            PositionW = 1f
        });
    }

    public void RefreshObjectiveTarget(Player player)
    {
        if (TryGetTrackedGoal(player, out var quest, out var goalIndex))
            SendObjectiveForGoal(player, quest, goalIndex);
        else
            player.SendTunneled(new ObjectiveTargetUpdatePacket { Active = false });
    }

    private bool TryGetTrackedGoal(Player player, out QuestDefinition quest, out int goalIndex)
    {
        var activeQuestId = player.Quests.ActiveQuestId;

        if (activeQuestId != 0 && TryGetTrackableGoal(player, activeQuestId, out quest, out goalIndex))
            return true;

        quest = null!;
        goalIndex = -1;
        return false;
    }

    private static (Vector3 Position, int ZoneDefinitionId) GoalSpot(QuestDefinition quest, int goalIndex)
    {
        if (goalIndex < 0 || goalIndex >= quest.Goals.Count)
            return (Vector3.Zero, 0);

        return quest.Goals[goalIndex] switch
        {
            ReachLocationGoal reachGoal => (reachGoal.ReachPosition, reachGoal.ZoneDefinitionId),
            EmoteGoal emoteGoal => (emoteGoal.EmotePosition, emoteGoal.ZoneDefinitionId),
            UseItemGoal useItemGoal => (useItemGoal.TargetPosition, useItemGoal.ZoneDefinitionId),
            _ => (Vector3.Zero, 0)
        };
    }

    private bool TryGetTrackableGoal(Player player, int questId, out QuestDefinition quest, out int goalIndex)
    {
        quest = null!;
        goalIndex = -1;
        if (!_resourceManager.Quests.TryGet(questId, out var q) || !player.Quests.TryGetInProgress(questId, out var progress))
            return false;

        int currentGoalIndex = progress.FirstOpenGoalIndex;
        var goals = q.Goals;

        if (q.HideObjectiveTarget || (currentGoalIndex >= 0 && currentGoalIndex < goals.Count && goals[currentGoalIndex] is CollectGoal))
            return false;

        var goalSpot = GoalSpot(q, currentGoalIndex);

        if (goalSpot.Position != Vector3.Zero)
        {
            if (goalSpot.ZoneDefinitionId != player.Zone.DefinitionId)
                return false;

            quest = q;
            goalIndex = currentGoalIndex;
            return true;
        }

        ulong guid = ResolveGoalTargetGuid(player, q, currentGoalIndex);
        if (guid != 0 && player.Zone.TryGetNpc(guid, out _))
        {
            quest = q;
            goalIndex = currentGoalIndex;
            return true;
        }
        return false;
    }

    private void GrantReward(Player player, QuestDefinition quest)
    {
        var coins = quest.RewardCoins;
        if (coins > 0)
            _rewardManager.TryGrantCurrency(player, CurrencyType.Coins, coins);

        var experience = quest.RewardExperience;

        var grantedCollection = quest.RewardCollectionId != 0 &&
            _resourceManager.Collections.TryGetValue(quest.RewardCollectionId, out var rewardedCollection)
                ? rewardedCollection
                : null;

        if (coins > 0 || experience > 0 || grantedCollection is not null)
        {
            var celebration = new RewardBundlePacket();

            // Success also gates whether each entry's tail is written; only the actual
            // collection id needs one here, so only flip it when there is a collection to report.
            celebration.RewardBundle.Success = grantedCollection is not null;
            celebration.RewardBundle.Coins = coins;
            celebration.RewardBundle.Experience = experience;
            celebration.RewardBundle.Unknown3 = 0;
            celebration.RewardBundle.Multiplier = 1f;
            celebration.RewardBundle.IconId = -1;
            celebration.RewardBundle.NameId = -1;

            if (grantedCollection is not null)
            {
                celebration.RewardBundle.Entries.Add(new RewardBundleEntryCollectionAdd
                {
                    IconId = grantedCollection.IconId,
                    TintId = grantedCollection.IconTintId,
                    NameId = grantedCollection.NameId,
                    CollectionId = grantedCollection.Id
                });
            }

            player.SendTunneled(celebration);
        }

        foreach (var group in quest.RewardItems.Where(id => FitsPlayerGender(player, id)).GroupBy(id => id))
        {
            if (_resourceManager.ClientItemDefinitions.TryGetValue(group.Key, out var itemDefinition))
                _rewardManager.TryGrantItem(player, group.Key, itemDefinition.IsTintable ? 0 : itemDefinition.Icon.TintId, group.Count());
        }

        GrantRandomReward(player, quest);
    }

    private void GrantRandomReward(Player player, QuestDefinition quest)
    {
        if (string.IsNullOrWhiteSpace(quest.RandomRewardTable))
            return;

        if (!_resourceManager.RewardTables.TryGetValue(quest.RandomRewardTable.Trim().ToLowerInvariant(), out var table))
        {
            _logger.LogError("Quest {questId} references unknown reward table {key}.", quest.QuestId, quest.RandomRewardTable);
            return;
        }

        var drops = table.DropTable
            .Where(drop => drop is not ItemRewardDropDefinition item || FitsPlayerGender(player, item.ItemDefinitionId))
            .ToList();

        if (drops.Count > 0)
            _rewardManager.TryGrantReward(player, new WeightedDropTable<RewardDropDefinition>(drops).SelectRandom());
    }
}
