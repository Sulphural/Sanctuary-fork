using System;
using System.Globalization;
using System.Linq;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Quests;

namespace Sanctuary.Game.ChatCommands;

public class PresentsChatCommand : IChatCommand
{
    private readonly IChatCommandManager _chatCommandManager;
    private readonly IProgressiveQuestManager _progressiveQuestManager;
    private readonly IQuestManager _questManager;
    private readonly IRewardManager _rewardManager;
    private readonly IResourceManager _resourceManager;

    public string KeyWord => "presents";
    public string Usage => "[date <yyyy-mm-dd>|off] [reset]";
    public string Description => "Opens the 12 Days of Presents panel with no arguments, or fakes the date or resets your presents, for testing.";
    public ChatCommandRole RequiredRole => ChatCommandRole.Admin;

    public PresentsChatCommand(IChatCommandManager chatCommandManager, IProgressiveQuestManager progressiveQuestManager,
        IQuestManager questManager, IRewardManager rewardManager, IResourceManager resourceManager)
    {
        _chatCommandManager = chatCommandManager;
        _progressiveQuestManager = progressiveQuestManager;
        _questManager = questManager;
        _rewardManager = rewardManager;
        _resourceManager = resourceManager;
    }

    public bool Handle(Player invoker, string[] args)
    {
        var definition = _resourceManager.ProgressiveQuests.Values.FirstOrDefault();

        if (definition is null)
        {
            ChatHelper.SendSystemMessage(invoker, "No progressive quests are loaded.");
            return true;
        }

        if (args.Length == 2 && args[0].Equals("date", StringComparison.OrdinalIgnoreCase))
        {
            if (args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                _progressiveQuestManager.DateOverride = null;
            else if (DateTime.TryParseExact(args[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                _progressiveQuestManager.DateOverride = date.Add(DateTime.Now.TimeOfDay);
            else
                return false;

            _chatCommandManager.LogAction(this, invoker, "Presents date", null, args[1]);
            ChatHelper.SendSystemMessage(invoker, $"12 Days date is now {_progressiveQuestManager.Now:yyyy-MM-dd}.");
        }
        else if (args.Length == 1 && args[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            _progressiveQuestManager.ResetClaims(invoker);

            foreach (var slot in definition.Slots)
            {
                _questManager.ResetQuest(invoker, slot.QuestId);
                _rewardManager.TryTakeItems(invoker, slot.SlotItemId, _rewardManager.CountItems(invoker, slot.SlotItemId));
                _rewardManager.TryTakeItems(invoker, slot.KeyItemId, _rewardManager.CountItems(invoker, slot.KeyItemId));
            }

            _chatCommandManager.LogAction(this, invoker, "Presents reset", null, null);
            ChatHelper.SendSystemMessage(invoker, "Reset your 12 Days quests, presents, bows and opened presents.");
        }
        else if (args.Length == 0)
        {
            _progressiveQuestManager.Show(invoker, definition.Id);
        }
        else
        {
            return false;
        }

        return true;
    }
}
