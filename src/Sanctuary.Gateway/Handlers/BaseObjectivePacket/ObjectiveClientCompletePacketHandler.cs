using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Quests;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class ObjectiveClientCompletePacketHandler
{
    private static ILogger _logger = null!;
    private static IQuestManager _questManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ObjectiveClientCompletePacketHandler));
        _questManager = serviceProvider.GetRequiredService<IQuestManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ObjectiveClientCompletePacket.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(ObjectiveClientCompletePacket));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(ObjectiveClientCompletePacket), packet);

        if (connection.Player.Zone is CombatZone combatZone)
            combatZone.OnObjectiveComplete(connection.Player, packet.ObjectiveId);

        _questManager.OnEvent(connection.Player, new ClientObjectiveCompletedEvent(packet.ObjectiveId));

        return true;
    }
}
