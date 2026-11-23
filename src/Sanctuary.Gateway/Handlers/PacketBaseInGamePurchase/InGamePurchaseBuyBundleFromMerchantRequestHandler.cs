using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class InGamePurchaseBuyBundleFromMerchantRequestHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IRewardManager _rewardManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(InGamePurchaseBuyBundleFromMerchantRequestHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _rewardManager = serviceProvider.GetRequiredService<IRewardManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!InGamePurchaseBuyBundleFromMerchantRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(InGamePurchaseBuyBundleFromMerchantRequest));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(InGamePurchaseBuyBundleFromMerchantRequest), packet);

        if (!_resourceManager.Stores.TryGetValue(packet.StoreId, out var store)
            || !store.Bundles.TryGetValue(packet.BundleId, out var bundle)
            || bundle.Entries.Any(entry => !_resourceManager.ClientItemDefinitions.ContainsKey(entry.MarketingItemId)))
            return true;

        var quantity = Math.Max(1, packet.Quantity);
        var price = connection.Player.MembershipStatus == 0 ? bundle.Price : bundle.MemberDiscount;

        if (!_rewardManager.TrySpendStationCash(connection.Player, price * quantity))
            return true;

        foreach (var entry in bundle.Entries)
            _rewardManager.TryGrantItem(connection.Player, entry.MarketingItemId, 0, entry.Quantity * quantity);

        return PacketInGamePurchaseWalletInfoRequestHandler.HandlePacket(connection);
    }
}
