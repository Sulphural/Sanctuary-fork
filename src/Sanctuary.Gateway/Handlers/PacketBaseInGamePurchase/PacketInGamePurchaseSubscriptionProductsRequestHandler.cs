using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;
using Sanctuary.Packet.Common.GameCommerce;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketInGamePurchaseSubscriptionProductsRequestHandler
{
    private const string LifetimeLegalText = "Upgrade my account to a Lifetime Membership. Your Station Cash is spent right away and your membership never runs out.";
    private const string LifetimePreviewText = "Lifetime Membership never runs out.";

    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketBaseInGamePurchaseHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!PacketInGamePurchaseSubscriptionProductsRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PacketInGamePurchaseSubscriptionProductsRequest));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PacketInGamePurchaseSubscriptionProductsRequest), packet);

        var packetInGamePurchaseSubscriptionProductsResponse = new PacketInGamePurchaseSubscriptionProductsResponse
        {
            ErrorCode = 1,
            Locale = packet.Locale
        };

        packetInGamePurchaseSubscriptionProductsResponse.MembershipSkuList.AddRange(Membership.Plans.Select(plan => new MembershipSkuData
        {
            Sku = plan.Sku,
            Name = plan.Name,
            Description = plan.Description,
            MarketingDescription = plan.MarketingDescription,
            ProductLegalText = plan.IsLifetime ? LifetimeLegalText : null,
            PreviewLegalText = plan.IsLifetime ? LifetimePreviewText : null,
            PriceLocalCurrency = plan.PriceStationCash,
            PriceStationCash = plan.PriceStationCash,
            IsVatTaxable = true
        }));

        connection.SendTunneled(packetInGamePurchaseSubscriptionProductsResponse);

        return true;
    }
}
