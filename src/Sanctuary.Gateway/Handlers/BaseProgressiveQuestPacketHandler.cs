using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game.Quests;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class BaseProgressiveQuestPacketHandler
{
    private static ILogger _logger = null!;
    private static IProgressiveQuestManager _progressiveQuestManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseProgressiveQuestPacketHandler));

        _progressiveQuestManager = serviceProvider.GetRequiredService<IProgressiveQuestManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!ProgressiveQuestRequestPacket.TryDeserialize(reader.Span, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}. ( Data: {data} )", nameof(ProgressiveQuestRequestPacket), Convert.ToHexString(reader.Span));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(ProgressiveQuestRequestPacket), packet);

        switch (packet.SubOpCode)
        {
            case ProgressiveQuestRequestPacket.StartQuestOpCode:
                _progressiveQuestManager.StartQuest(connection.Player, packet.QuestId, packet.SlotId);
                return true;

            case ProgressiveQuestRequestPacket.ClaimSlotOpCode:
                _progressiveQuestManager.ClaimSlot(connection.Player, packet.QuestId, packet.SlotId);
                return true;

            case ProgressiveQuestRequestPacket.ClaimPrizeOpCode:
                _progressiveQuestManager.ClaimPrize(connection.Player, packet.QuestId, packet.SlotId);
                return true;

            default:
                return false;
        }
    }
}
