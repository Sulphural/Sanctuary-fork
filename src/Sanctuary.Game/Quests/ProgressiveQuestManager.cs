using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Interactions;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.Rewards;
using Sanctuary.Game.Routines;
using Sanctuary.Packet;

namespace Sanctuary.Game.Quests;

public sealed class ProgressiveQuestManager : IProgressiveQuestManager
{
    private const int ShowAfterDialogueDelayMs = 400;
    private const double MillisecondsPerSecond = 1000d;
    private const int ProgressPercentMax = 100;

    private readonly IResourceManager _resourceManager;
    private readonly IRewardManager _rewardManager;
    private readonly IQuestManager _questManager;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;
    private readonly ILogger _logger;

    public DateTime? DateOverride { get; set; }

    public DateTime Now => DateOverride ?? DateTime.Now;

    public ProgressiveQuestManager(IResourceManager resourceManager, IRewardManager rewardManager, IQuestManager questManager,
        IDbContextFactory<DatabaseContext> dbContextFactory, ILogger<ProgressiveQuestManager> logger)
    {
        _resourceManager = resourceManager;
        _rewardManager = rewardManager;
        _questManager = questManager;
        _dbContextFactory = dbContextFactory;
        _logger = logger;

        _questManager.QuestCompleted += OnQuestCompleted;
        _rewardManager.ItemCountChanged += OnItemCountChanged;
    }

    private void OnItemCountChanged(Player player, int itemDefinitionId)
    {
        foreach (var definition in _resourceManager.ProgressiveQuests.Values)
        {
            if (definition.Slots.Any(slot => slot.SlotItemId == itemDefinitionId || slot.KeyItemId == itemDefinitionId))
                Refresh(player, definition);
        }
    }

    private readonly record struct Season(DateTime Start, DateTime Open, DateTime End, DateTime Today)
    {
        public bool IsOpen => Today >= Open;
        public bool IsActive => Today >= Start && Today <= End;
        public int Year => Start.Year;
    }

    private Season SeasonOf(ProgressiveQuestDefinition definition)
    {
        var start = definition.SeasonStart(Now);
        return new Season(start, definition.OpenDate(start), definition.EndDate(start), Now.Date);
    }

    private static int UnlockedSlots(ProgressiveQuestDefinition definition, Season season)
        => Math.Clamp((season.Today - season.Start).Days + 1, 0, definition.Slots.Count);

    public List<NpcInteractionOption> GetInteractionOptions(Player player, Npc npc)
    {
        var options = new List<NpcInteractionOption>();

        foreach (var definition in _resourceManager.ProgressiveQuests.Values)
        {
            if (definition.NpcId != npc.DefinitionId)
                continue;

            var progressiveQuestId = definition.Id;

            options.Add(new NpcInteractionOption
            {
                IconId = definition.IntroductionIconId,
                ButtonTextId = definition.IntroductionButtonId,
                Invoke = interactingPlayer => QuestDialogue.Begin(interactingPlayer, definition.Introduction, npc.Guid,
                    () => ShowAfterDialogue(interactingPlayer, progressiveQuestId))
            });
        }

        return options;
    }

    private void ShowAfterDialogue(Player player, int progressiveQuestId)
        => player.Routines.SetRoutine($"progressive-quest-show-{progressiveQuestId}",
            new DelayedRoutine(new InstantRoutine(() => Show(player, progressiveQuestId)), ShowAfterDialogueDelayMs / MillisecondsPerSecond));

    public bool Show(Player player, int progressiveQuestId)
    {
        if (!_resourceManager.ProgressiveQuests.TryGetValue(progressiveQuestId, out var definition))
            return false;

        player.SendTunneled(BuildClientData(player, definition));
        player.SendTunneled(new ProgressiveQuestShowWindowPacket());
        return true;
    }

    private void Refresh(Player player, ProgressiveQuestDefinition definition)
        => player.SendTunneled(BuildClientData(player, definition));

    private ProgressiveQuestClientDataPacket BuildClientData(Player player, ProgressiveQuestDefinition definition)
    {
        var season = SeasonOf(definition);
        var unlocked = UnlockedSlots(definition, season);
        var claims = LoadClaims(player, definition.Id, season.Year);

        var packet = new ProgressiveQuestClientDataPacket
        {
            QuestId = definition.Id,
            NameId = definition.NameId,
            SecondsRemaining = season.IsOpen ? 0 : (int)Math.Max(0, (season.Open - Now).TotalSeconds),
            ActiveSlotId = Math.Max(0, unlocked - 1),
            IconId = definition.IconId,
            ObjectiveStringId1 = definition.ObjectiveStringIds.ElementAtOrDefault(0),
            ObjectiveStringId2 = definition.ObjectiveStringIds.ElementAtOrDefault(1),
            ObjectiveStringId3 = definition.ObjectiveStringIds.ElementAtOrDefault(2),
            ButtonStringIdOpen = definition.ButtonOpenId,
            ButtonStringIdOpened = definition.ButtonOpenedId,
            ButtonStringIdBuy = definition.ButtonBuyId
        };

        for (var slotId = 0; slotId < definition.Slots.Count; slotId++)
        {
            var slot = definition.Slots[slotId];
            var claimed = claims.Any(claim => !claim.IsPrize && claim.SlotId == slotId);
            var hasPresent = claimed || _rewardManager.CountItems(player, slot.SlotItemId) > 0;
            var hasBow = claimed || _rewardManager.CountItems(player, slot.KeyItemId) > 0;
            var isUnlocked = slotId < unlocked;

            packet.Slots.Add(new ProgressiveQuestClientDataPacket.Slot
            {
                QuestId = definition.Id,
                SlotId = slotId,
                IconId = SlotIconId(definition, slot, claimed, hasPresent, hasBow),
                HasSlotItem = hasPresent,
                HasKeyItem = hasBow,
                CanDoQuest = isUnlocked && season.IsActive && CanStartQuest(player, slot),
                CanPurchaseKeyItem = isUnlocked && season.IsActive && !hasBow,
                KeyBundleId = slot.KeyBundleId,
                KeyBundlePrice = KeyBundlePrice(slot.KeyBundleId),
                CanClaimSlotItem = !claimed && hasPresent && hasBow && season.IsOpen,
                TooltipId = hasPresent ? slot.NameId : slot.LockedTooltipId,
                NameId = slot.NameId
            });
        }

        var earned = EarnedSlots(player, definition);

        for (var prizeSlotId = 0; prizeSlotId < definition.Prizes.Count; prizeSlotId++)
        {
            var prize = definition.Prizes[prizeSlotId];
            var claimed = claims.Any(claim => claim.IsPrize && claim.SlotId == prizeSlotId);

            packet.PrizeSlots.Add(new ProgressiveQuestClientDataPacket.PrizeSlot
            {
                QuestId = definition.Id,
                PrizeSlotId = prizeSlotId,
                NameId = prize.NameId,
                IconId = prize.IconId,
                TooltipId = prize.TooltipId,
                ProgressPercent = prize.RequiredSlots > 0 ? Math.Min(ProgressPercentMax, earned * ProgressPercentMax / prize.RequiredSlots) : ProgressPercentMax,
                CanClaimPrizeItem = !claimed && earned >= prize.RequiredSlots && season.IsOpen
            });
        }

        return packet;
    }

    private static int SlotIconId(ProgressiveQuestDefinition definition, ProgressiveQuestSlotDefinition slot, bool claimed, bool hasPresent, bool hasBow)
    {
        if (claimed)
            return definition.OpenedIconId;

        if (hasPresent)
            return hasBow ? slot.KeyIconId : slot.IconId;

        return hasBow ? definition.LockedKeyIconId : definition.LockedIconId;
    }

    private bool CanStartQuest(Player player, ProgressiveQuestSlotDefinition slot)
        => _resourceManager.Quests.TryGet(slot.QuestId, out var quest) && quest.IsOfferableFor(player.Quests);

    private int EarnedSlots(Player player, ProgressiveQuestDefinition definition)
        => definition.Slots.Count(slot => player.Quests.IsCompleted(slot.QuestId));

    private int KeyBundlePrice(int bundleId)
    {
        foreach (var store in _resourceManager.Stores.Values)
        {
            if (store.Bundles.TryGetValue(bundleId, out var bundle))
                return bundle.Price;
        }

        return 0;
    }

    public void StartQuest(Player player, int progressiveQuestId, int slotId)
    {
        if (!_resourceManager.ProgressiveQuests.TryGetValue(progressiveQuestId, out var definition)
            || slotId < 0 || slotId >= definition.Slots.Count)
            return;

        var season = SeasonOf(definition);

        if (season.IsActive && slotId < UnlockedSlots(definition, season))
            _questManager.OfferQuest(player, definition.Slots[slotId].QuestId, EntityHelper.NearestNpcGuid(player, definition.NpcId));

        Refresh(player, definition);
    }

    public void ClaimSlot(Player player, int progressiveQuestId, int slotId)
    {
        if (!_resourceManager.ProgressiveQuests.TryGetValue(progressiveQuestId, out var definition)
            || slotId < 0 || slotId >= definition.Slots.Count)
            return;

        var season = SeasonOf(definition);
        var slot = definition.Slots[slotId];

        if (season.IsOpen
            && _rewardManager.CountItems(player, slot.SlotItemId) > 0
            && _rewardManager.CountItems(player, slot.KeyItemId) > 0)
        {
            var claims = LoadClaims(player, definition.Id, season.Year);
            var received = claims.Where(claim => !claim.IsPrize).Select(claim => claim.RewardIndex).ToHashSet();
            var remaining = Enumerable.Range(0, definition.SlotRewards.Count).Where(index => !received.Contains(index)).ToList();
            var rewardIndex = remaining.Count > 0
                ? remaining[Random.Shared.Next(remaining.Count)]
                : Random.Shared.Next(definition.SlotRewards.Count);

            if (definition.SlotRewards.Count > 0
                && !claims.Any(claim => !claim.IsPrize && claim.SlotId == slotId)
                && TrySaveClaim(player, definition.Id, season.Year, slotId, false, rewardIndex)
                && _rewardManager.TryTakeItems(player, slot.SlotItemId, 1)
                && _rewardManager.TryTakeItems(player, slot.KeyItemId, 1))
            {
                GrantReward(player, definition.SlotRewards[rewardIndex]);
            }
        }

        Refresh(player, definition);
    }

    public void ClaimPrize(Player player, int progressiveQuestId, int prizeSlotId)
    {
        if (!_resourceManager.ProgressiveQuests.TryGetValue(progressiveQuestId, out var definition)
            || prizeSlotId < 0 || prizeSlotId >= definition.Prizes.Count)
            return;

        var season = SeasonOf(definition);
        var prize = definition.Prizes[prizeSlotId];

        if (season.IsOpen
            && EarnedSlots(player, definition) >= prize.RequiredSlots
            && !LoadClaims(player, definition.Id, season.Year).Any(claim => claim.IsPrize && claim.SlotId == prizeSlotId)
            && TrySaveClaim(player, definition.Id, season.Year, prizeSlotId, true, prizeSlotId))
        {
            GrantReward(player, prize.Reward);
        }

        Refresh(player, definition);
    }

    private void GrantReward(Player player, ProgressiveQuestRewardDefinition reward)
    {
        if (reward.Coins > 0)
            _rewardManager.TryGrantCurrency(player, CurrencyType.Coins, reward.Coins);

        foreach (var item in reward.Items)
        {
            if (!_resourceManager.ClientItemDefinitions.TryGetValue(item.ItemDefinitionId, out var itemDefinition))
                continue;

            var tint = itemDefinition.Type == ItemDefinitionHelper.MountItemType && _resourceManager.Mounts.TryGetValue(itemDefinition.Param1, out var mountDefinition)
                ? mountDefinition.TintId
                : itemDefinition.IsTintable ? 0 : itemDefinition.Icon.TintId;

            _rewardManager.TryGrantItem(player, item.ItemDefinitionId, tint, item.Quantity);
        }

        player.SendTunneled(new ProgressiveQuestNotifyRewardItemPacket
        {
            IconId = reward.IconId,
            TitleId = reward.TitleId,
            BodyId = reward.BodyId
        });
    }

    private void OnQuestCompleted(Player player, QuestDefinition quest)
    {
        foreach (var definition in _resourceManager.ProgressiveQuests.Values)
        {
            var slotId = definition.Slots.FindIndex(slot => slot.QuestId == quest.QuestId);

            if (slotId < 0)
                continue;

            var season = SeasonOf(definition);

            if (season.Today == season.Start.AddDays(slotId))
                _rewardManager.TryGrantItem(player, definition.Slots[slotId].KeyItemId, 0, 1);
        }
    }

    private List<DbProgressiveQuestClaim> LoadClaims(Player player, int progressiveQuestId, int year)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        return dbContext.ProgressiveQuestClaims
            .AsNoTracking()
            .Where(claim => claim.CharacterId == player.CharacterId && claim.ProgressiveQuestId == progressiveQuestId && claim.Year == year)
            .ToList();
    }

    private bool TrySaveClaim(Player player, int progressiveQuestId, int year, int slotId, bool isPrize, int rewardIndex)
    {
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();

            dbContext.ProgressiveQuestClaims.Add(new DbProgressiveQuestClaim
            {
                CharacterId = player.CharacterId,
                ProgressiveQuestId = progressiveQuestId,
                Year = year,
                SlotId = slotId,
                IsPrize = isPrize,
                RewardIndex = rewardIndex
            });

            return dbContext.SaveChanges() > 0;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Failed to save progressive quest {id} claim {slotId} for {characterId}.", progressiveQuestId, slotId, player.CharacterId);
            return false;
        }
    }

    public void ResetClaims(Player player)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        dbContext.ProgressiveQuestClaims
            .Where(claim => claim.CharacterId == player.CharacterId)
            .ExecuteDelete();
    }
}
