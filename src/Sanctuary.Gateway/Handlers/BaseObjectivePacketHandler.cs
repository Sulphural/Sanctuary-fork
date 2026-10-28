using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class BaseObjectivePacketHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseObjectivePacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        var fullBuffer = reader.Span;

        if (!reader.TryRead(out byte subOpCode))
        {
            _logger.LogError("Failed to read sub-opcode from BaseObjectivePacket. ( Data: {data} )", Convert.ToHexString(reader.Span));
            return false;
        }

        switch (subOpCode)
        {
            case ObjectiveClientCompletePacket.OpCode:
                return ObjectiveClientCompletePacketHandler.HandlePacket(connection, fullBuffer);
            default:
                _logger.LogInformation("Sub-opcode {sub} from {name}. Payload: {data}", subOpCode, connection.Player.Name, Convert.ToHexString(reader.RemainingSpan));
                return false;
        }
    }
}
