using System;
using System.Collections.Generic;

using Sanctuary.Game.Resources.Definitions.QuestGoals;

namespace Sanctuary.Game.Resources.Definitions;

public sealed class ProgressiveQuestDefinition
{
    public int Id { get; set; }

    public int NameId { get; set; }
    public int IconId { get; set; }

    public List<int> ObjectiveStringIds { get; set; } = [];

    public int ButtonOpenId { get; set; }
    public int ButtonOpenedId { get; set; }
    public int ButtonBuyId { get; set; }

    public int NpcId { get; set; }
    public int IntroductionButtonId { get; set; }
    public int IntroductionIconId { get; set; }
    public List<QuestDialogueLine> Introduction { get; set; } = [];

    public int StartMonth { get; set; }
    public int StartDay { get; set; }
    public int OpenMonth { get; set; }
    public int OpenDay { get; set; }
    public int EndMonth { get; set; }
    public int EndDay { get; set; }

    public int LockedIconId { get; set; }
    public int LockedKeyIconId { get; set; }
    public int OpenedIconId { get; set; }

    public List<ProgressiveQuestSlotDefinition> Slots { get; set; } = [];
    public List<ProgressiveQuestPrizeDefinition> Prizes { get; set; } = [];
    public List<ProgressiveQuestRewardDefinition> SlotRewards { get; set; } = [];

    public DateTime SeasonStart(DateTime now)
    {
        var year = (now.Month, now.Day).CompareTo((StartMonth, 1)) >= 0 ? now.Year : now.Year - 1;
        return new DateTime(year, StartMonth, StartDay);
    }

    public DateTime OpenDate(DateTime seasonStart)
        => new(OpenMonth >= StartMonth ? seasonStart.Year : seasonStart.Year + 1, OpenMonth, OpenDay);

    public DateTime EndDate(DateTime seasonStart)
        => new(EndMonth >= StartMonth ? seasonStart.Year : seasonStart.Year + 1, EndMonth, EndDay);
}

public sealed class ProgressiveQuestSlotDefinition
{
    public int QuestId { get; set; }
    public int NameId { get; set; }
    public int IconId { get; set; }
    public int KeyIconId { get; set; }
    public int LockedTooltipId { get; set; }
    public int SlotItemId { get; set; }
    public int KeyItemId { get; set; }
    public int KeyBundleId { get; set; }
}

public sealed class ProgressiveQuestPrizeDefinition
{
    public int NameId { get; set; }
    public int IconId { get; set; }
    public int TooltipId { get; set; }
    public int RequiredSlots { get; set; }
    public ProgressiveQuestRewardDefinition Reward { get; set; } = new();
}

public sealed class ProgressiveQuestRewardDefinition
{
    public int TitleId { get; set; }
    public int BodyId { get; set; }
    public int IconId { get; set; }
    public int Coins { get; set; }
    public List<ProgressiveQuestRewardItem> Items { get; set; } = [];
}

public sealed class ProgressiveQuestRewardItem
{
    public int ItemDefinitionId { get; set; }
    public int Quantity { get; set; } = 1;
}
