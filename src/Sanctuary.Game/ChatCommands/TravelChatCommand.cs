using System;
using System.Linq;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources.Definitions.Zones;

namespace Sanctuary.Game.ChatCommands;

public class TravelChatCommand : IChatCommand
{
    private readonly IZoneManager _zoneManager;
    private readonly IResourceManager _resourceManager;

    public string KeyWord => "travel";
    public string Usage => "<zone name|id> | home";
    public string Description => "Moves you into a zone at its spawn point, or back to the starting zone.";
    public ChatCommandRole RequiredRole => ChatCommandRole.Mod;

    public TravelChatCommand(IZoneManager zoneManager, IResourceManager resourceManager)
    {
        _zoneManager = zoneManager;
        _resourceManager = resourceManager;
    }

    public bool Handle(Player invoker, string[] args)
    {
        if (args.Length != 1)
            return false;

        if (args[0].Equals("home", StringComparison.OrdinalIgnoreCase))
        {
            var startingZone = _zoneManager.StartingZone;

            if (!invoker.TeleportToZone(startingZone, invoker.StartingZonePosition, invoker.StartingZoneRotation))
                ChatHelper.SendSystemMessage(invoker, "Failed to return to the starting zone.");

            return true;
        }

        var zoneDefinition = int.TryParse(args[0], out var zoneDefinitionId)
            ? _resourceManager.Zones.Values.FirstOrDefault(zone => zone.Id == zoneDefinitionId)
            : _resourceManager.Zones.Values.FirstOrDefault(zone => zone.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase));

        if (zoneDefinition is null)
        {
            ChatHelper.SendSystemMessage(invoker, $"Unknown zone \"{args[0]}\".");
            return true;
        }

        if (zoneDefinition is HousingZoneDefinition)
        {
            ChatHelper.SendSystemMessage(invoker, $"\"{zoneDefinition.Name}\" is a housing zone and can't be traveled to.");
            return true;
        }

        ulong? ownerId = zoneDefinition is WorldZoneDefinition ? null : invoker.Guid;

        if (!invoker.TeleportToZone(zoneDefinition.Id, ownerId, zoneDefinition.SpawnPosition, zoneDefinition.SpawnRotation))
            ChatHelper.SendSystemMessage(invoker, $"Failed to enter \"{zoneDefinition.Name}\".");

        return true;
    }
}
