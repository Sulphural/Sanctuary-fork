using System;
using System.Linq;
using System.Numerics;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Packet;

namespace Sanctuary.Game.ChatCommands;

public class QuestChatCommand : IChatCommand
{
    private readonly IChatCommandManager _chatCommandManager;
    private readonly IResourceManager _resourceManager;
    private readonly IQuestManager _questManager;

    public string KeyWord => "quest";

    public string Usage => "list | giver <questId> | goto [questId] | skip [questId] | done <questId> | reset <questId>";

    public string Description => "Quest testing tools: list quests and your state, teleport to a quest's giver or current objective, skip the current goal, mark a quest done without rewards, or wipe a quest so it can be taken again.";

    public ChatCommandRole RequiredRole => ChatCommandRole.Admin;

    public QuestChatCommand(IChatCommandManager chatCommandManager, IResourceManager resourceManager, IQuestManager questManager)
    {
        _chatCommandManager = chatCommandManager;
        _resourceManager = resourceManager;
        _questManager = questManager;
    }

    public bool Handle(Player invoker, string[] args)
    {
        if (args.Length < 1)
            return false;

        switch (args[0].ToLowerInvariant())
        {
            case "list":
                return List(invoker, args[1..]);
            case "giver":
                return Giver(invoker, args[1..]);
            case "goto":
                return Goto(invoker, args[1..]);
            case "skip":
                return Skip(invoker, args[1..]);
            case "done":
                return Done(invoker, args[1..]);
            case "reset":
                return Reset(invoker, args[1..]);
            default:
                return false;
        }
    }

    private bool List(Player invoker, string[] args)
    {
        if (args.Length != 0)
            return false;

        foreach (var quest in _resourceManager.Quests.Quests.Values.OrderBy(q => q.QuestId))
            ChatHelper.SendSystemMessage(invoker, $"{quest.QuestId}: giver {NpcName(quest.GiverNpcId)} - {StateOf(invoker, quest)}");

        return true;
    }

    private bool Giver(Player invoker, string[] args)
    {
        if (args.Length != 1)
            return false;

        if (!TryGetQuest(invoker, args[0], out var quest))
            return true;

        Npc? giver = null;
        var best = float.MaxValue;

        foreach (var npc in invoker.Zone.Npcs)
        {
            if (npc.DefinitionId != quest.GiverNpcId)
                continue;

            var dx = npc.Position.X - invoker.Position.X;
            var dz = npc.Position.Z - invoker.Position.Z;
            var distance = dx * dx + dz * dz;
            if (distance < best)
            {
                best = distance;
                giver = npc;
            }
        }

        if (giver is null)
        {
            ChatHelper.SendSystemMessage(invoker, $"Quest {quest.QuestId}'s giver {NpcName(quest.GiverNpcId)} isn't spawned in this zone.");
            return true;
        }

        TeleportTo(invoker, new Vector3(giver.Position.X + 2f, giver.Position.Y, giver.Position.Z + 2f));

        _chatCommandManager.LogAction(this, invoker, "Teleport to quest giver", $"#{quest.QuestId}", $"npc={giver.Guid}");
        ChatHelper.SendSystemMessage(invoker, $"Teleported to {NpcName(quest.GiverNpcId)} (quest {quest.QuestId}, {StateOf(invoker, quest)}).");
        return true;
    }

    private bool Goto(Player invoker, string[] args)
    {
        if (args.Length > 1)
            return false;

        if (args.Length == 1)
        {
            if (!TryGetQuest(invoker, args[0], out var quest))
                return true;

            if (!(invoker.Quests.TryGetValue(quest.QuestId, out var completed) && !completed))
            {
                ChatHelper.SendSystemMessage(invoker, $"Quest {quest.QuestId} isn't active ({StateOf(invoker, quest)}). Use giver to go pick it up.");
                return true;
            }

            _questManager.SetActiveQuest(invoker, quest.QuestId);
        }

        if (!_questManager.TryGetActiveObjectiveTarget(invoker, out var target))
        {
            ChatHelper.SendSystemMessage(invoker, "No tracked objective with a location in this zone.");
            return true;
        }

        TeleportTo(invoker, target);

        _chatCommandManager.LogAction(this, invoker, "Teleport to quest objective", $"#{invoker.ActiveQuestId}", $"position={target}");
        ChatHelper.SendSystemMessage(invoker, $"Teleported to the objective of quest {invoker.ActiveQuestId}.");
        return true;
    }

    private bool Skip(Player invoker, string[] args)
    {
        if (args.Length > 1)
            return false;

        int questId;
        if (args.Length == 1)
        {
            if (!TryGetQuest(invoker, args[0], out var quest))
                return true;

            questId = quest.QuestId;
        }
        else
        {
            questId = invoker.ActiveQuestId;
        }

        if (!_questManager.SkipGoal(invoker, questId))
        {
            ChatHelper.SendSystemMessage(invoker, questId == 0
                ? "No tracked quest. Pass a quest id."
                : $"Quest {questId} has no goal left to skip.");
            return true;
        }

        _chatCommandManager.LogAction(this, invoker, "Skip quest goal", $"#{questId}", null);
        ChatHelper.SendSystemMessage(invoker, $"Skipped the current goal of quest {questId}.");
        return true;
    }

    private bool Done(Player invoker, string[] args)
    {
        if (args.Length != 1)
            return false;

        if (!TryGetQuest(invoker, args[0], out var quest))
            return true;

        if (!_questManager.ForceCompleteQuest(invoker, quest.QuestId))
        {
            ChatHelper.SendSystemMessage(invoker, $"Quest {quest.QuestId} is already completed.");
            return true;
        }

        _chatCommandManager.LogAction(this, invoker, "Force complete quest", $"#{quest.QuestId}", null);
        ChatHelper.SendSystemMessage(invoker, $"Marked quest {quest.QuestId} completed (no rewards granted).");
        return true;
    }

    private bool Reset(Player invoker, string[] args)
    {
        if (args.Length != 1)
            return false;

        if (!TryGetQuest(invoker, args[0], out var quest))
            return true;

        if (!_questManager.ResetQuest(invoker, quest.QuestId))
        {
            ChatHelper.SendSystemMessage(invoker, $"You have no progress on quest {quest.QuestId}.");
            return true;
        }

        _chatCommandManager.LogAction(this, invoker, "Reset quest", $"#{quest.QuestId}", null);
        ChatHelper.SendSystemMessage(invoker, $"Reset quest {quest.QuestId}; it's now {StateOf(invoker, quest)}.");
        return true;
    }

    private bool TryGetQuest(Player invoker, string arg, out QuestDefinition quest)
    {
        if (int.TryParse(arg, out var questId) && _resourceManager.Quests.TryGet(questId, out quest))
            return true;

        ChatHelper.SendSystemMessage(invoker, $"No quest with id {arg}. Use !quest list.");
        quest = null!;
        return false;
    }

    private string NpcName(int npcId)
        => _resourceManager.Npcs.TryGetValue(npcId, out var definition) ? $"{definition.Name} (#{npcId})" : $"#{npcId}";

    private static string StateOf(Player player, QuestDefinition quest)
    {
        if (player.Quests.TryGetValue(quest.QuestId, out var completed))
        {
            if (completed)
                return "completed";

            var done = player.QuestGoalProgress.TryGetValue(quest.QuestId, out var progress) ? progress : 0;
            return done >= quest.Goals.Count
                ? "ready to turn in"
                : $"active, goal {done + 1}/{quest.Goals.Count}";
        }

        return quest.IsOfferableFor(player.Quests) ? "available" : "locked";
    }

    private static void TeleportTo(Player invoker, Vector3 target)
    {
        var position = new Vector4(target, invoker.Position.W);

        invoker.UpdatePosition(position, invoker.Rotation, false);

        invoker.SendTunneled(new ClientUpdatePacketUpdateLocation
        {
            Position = position,
            Rotation = invoker.Rotation,
            Teleport = true
        });
    }
}
