using System;
using System.Collections.Generic;
using System.Linq;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Quests;

namespace Sanctuary.Game.ChatCommands;

public class QuestChatCommand : IChatCommand
{
    private readonly IChatCommandManager _chatCommandManager;
    private readonly IQuestManager _questManager;
    private readonly IZoneManager _zoneManager;

    private static readonly Dictionary<string, int[]> HeroJournalRegions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cobblestone"] = [2521, 2522, 2523, 2524, 2525, 2526, 2527, 2528, 2529, 2530, 2566, 2563, 2564, 2565, 2572, 2573, 2587],
        ["southernwilds"] = [2538, 2489, 2490, 2491, 2492, 2493, 2531, 2494, 2495, 2496, 2497, 2498, 2499, 2500, 2501, 2502, 2503, 2532, 2504, 2505, 2506, 2507, 2508, 2533, 2568, 2569, 2570, 2571, 2574, 2575],
        ["wugachug"] = [2588, 2589, 2590, 2591, 2592, 2593, 2594, 2595, 2596, 2597, 2598, 2599],
        ["seaside"] = [2576, 2577, 2578, 2579, 2580, 2581, 2582, 2583, 2584, 2585, 2600, 2601, 2602, 2603, 2604, 2605, 2606]
    };

    public string KeyWord => "quest";

    public string Usage => "reset <questId> [player] | back [questId] [player] | finish <questId> [player] | region <cobblestone|southernwilds|wugachug|seaside> [player]";

    public string Description => "Resets, rewinds or finishes quests, or finishes every Hero's Journal quest in a region.";

    public ChatCommandRole RequiredRole => ChatCommandRole.Admin;

    public QuestChatCommand(IChatCommandManager chatCommandManager, IQuestManager questManager, IZoneManager zoneManager)
    {
        _chatCommandManager = chatCommandManager;
        _questManager = questManager;
        _zoneManager = zoneManager;
    }

    public bool Handle(Player invoker, string[] args)
    {
        if (args.Length < 1)
            return false;

        return args[0].ToLowerInvariant() switch
        {
            "reset" => HandleReset(invoker, args),
            "back" => HandleBack(invoker, args),
            "finish" => HandleFinish(invoker, args),
            "region" => HandleRegion(invoker, args),
            _ => false
        };
    }

    private bool HandleReset(Player invoker, string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var questId))
            return false;

        if (!TryGetTarget(invoker, args, 2, out var target, out var targetName))
            return true;

        if (!_questManager.ResetQuest(target, questId))
        {
            ChatHelper.SendSystemMessage(invoker, $"Quest {questId} is not in that player's log.");
            return true;
        }

        _chatCommandManager.LogAction(this, invoker, "Reset quest", targetName, questId.ToString());
        ChatHelper.SendSystemMessage(invoker, $"Reset quest {questId}.");
        return true;
    }

    private bool HandleBack(Player invoker, string[] args)
    {
        var hasQuestId = args.Length > 1 && int.TryParse(args[1], out _);

        if (!TryGetTarget(invoker, args, hasQuestId ? 2 : 1, out var target, out var targetName))
            return true;

        var questId = hasQuestId ? int.Parse(args[1]) : target.Quests.ActiveQuestId;

        if (questId == 0)
        {
            ChatHelper.SendSystemMessage(invoker, "That player has no tracked quest. Pass a quest id.");
            return true;
        }

        if (!_questManager.RewindGoal(target, questId))
        {
            ChatHelper.SendSystemMessage(invoker, $"Quest {questId} is not in progress or is already on its first goal.");
            return true;
        }

        _chatCommandManager.LogAction(this, invoker, "Rewound quest goal", targetName, questId.ToString());
        ChatHelper.SendSystemMessage(invoker, $"Moved quest {questId} back a goal.");
        return true;
    }

    private bool HandleFinish(Player invoker, string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out var questId))
            return false;

        if (!TryGetTarget(invoker, args, 2, out var target, out var targetName))
            return true;

        if (_questManager.FinishQuests(target, [questId]) == 0)
        {
            ChatHelper.SendSystemMessage(invoker, $"Quest {questId} does not exist or is already completed.");
            return true;
        }

        _chatCommandManager.LogAction(this, invoker, "Finished quest", targetName, questId.ToString());
        ChatHelper.SendSystemMessage(invoker, $"Finished quest {questId}.");
        return true;
    }

    private bool HandleRegion(Player invoker, string[] args)
    {
        if (args.Length < 2)
            return false;

        if (!HeroJournalRegions.TryGetValue(args[1], out var questIds))
        {
            ChatHelper.SendSystemMessage(invoker, $"Unknown region. Use one of: {string.Join(", ", HeroJournalRegions.Keys)}.");
            return true;
        }

        if (!TryGetTarget(invoker, args, 2, out var target, out var targetName))
            return true;

        var finished = _questManager.FinishQuests(target, questIds);

        _chatCommandManager.LogAction(this, invoker, "Finished Hero's Journal region", targetName, args[1]);
        ChatHelper.SendSystemMessage(invoker, $"Finished {finished} of {questIds.Length} {args[1].ToLowerInvariant()} quests.");
        return true;
    }

    private bool TryGetTarget(Player invoker, string[] args, int nameIndex, out Player target, out string? targetName)
    {
        targetName = args.Length > nameIndex ? string.Join(' ', args[nameIndex..]) : null;
        target = invoker;

        if (targetName is null || _zoneManager.TryGetPlayer(targetName, out target!))
            return true;

        ChatHelper.SendSystemMessage(invoker, $"No online player named \"{targetName}\" was found.");
        return false;
    }
}
