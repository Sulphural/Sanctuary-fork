using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Gateway.Uram;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class OneTimeSessionRequestPacketHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(OneTimeSessionRequestPacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, Span<byte> data)
    {
        if (!OneTimeSessionRequestPacket.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(OneTimeSessionRequestPacket));
            return false;
        }

        _logger.LogInformation("{connection} asked for a commerce session ( Type: {type} ).", connection, packet.Type);

        var response = new OneTimeSessionResponsePacket
        {
            Success = true,
            SessionId = UramSessions.Create(connection),
            Type = packet.Type
        };

        connection.SendTunneled(response);

        return true;
    }
}
