using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketInGamePurchaseWalletInfoRequestHandler
{
    private static ILogger _logger = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketBaseInGamePurchaseHandler));

        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    public static bool HandlePacket(GatewayConnection connection)
    {
        _logger.LogTrace("Received {name} packet.", nameof(PacketInGamePurchaseWalletInfoRequest));

        using (var dbContext = _dbContextFactory.CreateDbContext())
        {
            var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);
            var saved = dbContext.Characters
                .Where(x => x.Id == characterId)
                .Select(x => (int?)x.StationCash)
                .FirstOrDefault();

            if (saved is not null)
                connection.Player.StationCash = saved.Value;
        }

        var packetInGamePurchaseWalletInfoResponse = new PacketInGamePurchaseWalletInfoResponse
        {
            ErrorCode = 1,
            WalletInfo =
            {
                Unknown = true,
                StationCash = connection.Player.StationCash,
                CreditCardId = 69420,
                CurrencyCode = "SOE",
                International = true
            }
        };

        connection.SendTunneled(packetInGamePurchaseWalletInfoResponse);

        return true;
    }
}
