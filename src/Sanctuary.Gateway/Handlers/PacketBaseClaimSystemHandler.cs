using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Gateway.Uram;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketBaseClaimSystemHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IRewardManager _rewardManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketBaseClaimSystemHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _rewardManager = serviceProvider.GetRequiredService<IRewardManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out short opCode))
        {
            _logger.LogError("Failed to read opcode from packet. ( Data: {data} )", Convert.ToHexString(reader.Span));
            return false;
        }

        return opCode switch
        {
            GetAllAvailableClaimItemsRequest.OpCode => HandleGetAll(connection, reader.Span),
            ClaimItemsRequest.OpCode => HandleClaim(connection, reader.Span),
            _ => false
        };
    }

    public static void SendList(GatewayConnection connection)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        var userId = PendingRewards.UserIdOf(dbContext, connection);
        if (userId is null)
            return;

        var (items, signature) = PendingRewards.Build(dbContext, _resourceManager, userId.Value);

        PendingRewards.SendDefinitions(connection, _resourceManager, items);
        connection.SendTunneled(new GetAllAvailableClaimItemsResponse { Items = items });
        connection.PendingRewardsSignature = signature;
    }

    private static bool HandleGetAll(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!GetAllAvailableClaimItemsRequest.TryDeserialize(data, out _))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(GetAllAvailableClaimItemsRequest));
            return false;
        }

        SendList(connection);
        return true;
    }

    private static bool HandleClaim(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ClaimItemsRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(ClaimItemsRequest));
            return false;
        }

        using var dbContext = _dbContextFactory.CreateDbContext();

        var userId = PendingRewards.UserIdOf(dbContext, connection);
        var characterId = Core.Helpers.GuidHelper.GetPlayerId(connection.Player.Guid);
        var success = userId is not null && packet.ClaimInstanceIds.Count > 0;

        if (userId is not null)
        {
            foreach (var claimId in packet.ClaimInstanceIds.Distinct())
            {
                var rewardId = (long)claimId;
                var now = DateTimeOffset.UtcNow;

                var reward = CodeRedeemer.Unclaimed(dbContext, userId.Value).AsNoTracking().SingleOrDefault(x => x.Id == rewardId);
                if (reward is null)
                {
                    success = false;
                    continue;
                }

                var taken = dbContext.PendingRewards
                    .Where(x => x.Id == rewardId && x.ClaimedAt == null)
                    .ExecuteUpdate(x => x
                        .SetProperty(r => r.ClaimedAt, now)
                        .SetProperty(r => r.ClaimedByCharacterId, characterId));

                if (taken == 0)
                {
                    success = false;
                    continue;
                }

                var tint = _resourceManager.ClientItemDefinitions.TryGetValue(reward.ItemDefinition, out var definition)
                    && definition.Type is ItemDefinitionHelper.MountItemType or ItemDefinitionHelper.PetItemType
                    ? definition.Icon.TintId
                    : 0;

                if (!_rewardManager.TryGrantItem(connection.Player, reward.ItemDefinition, tint, reward.Quantity))
                {
                    dbContext.PendingRewards
                        .Where(x => x.Id == rewardId)
                        .ExecuteUpdate(x => x
                            .SetProperty(r => r.ClaimedAt, (DateTimeOffset?)null)
                            .SetProperty(r => r.ClaimedByCharacterId, (ulong?)null));

                    success = false;
                    continue;
                }

                _logger.LogInformation("{connection} claimed pending reward {id} (item {item} x{quantity}).", connection, rewardId, reward.ItemDefinition, reward.Quantity);
            }
        }

        (System.Collections.Generic.List<ClaimItem> Items, long Signature) remaining = userId is null
            ? ([], 0)
            : PendingRewards.Build(dbContext, _resourceManager, userId.Value);

        connection.SendTunneled(new ClaimItemsResponse
        {
            RequestId = packet.RequestId,
            Success = success,
            Items = remaining.Items
        });

        connection.PendingRewardsSignature = remaining.Signature;

        return true;
    }
}
