using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;

namespace Sanctuary.Gateway.Uram;

public sealed class MembershipSyncService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    private readonly ILogger<MembershipSyncService> _logger;
    private readonly GatewayServer _gatewayServer;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;

    public MembershipSyncService(ILogger<MembershipSyncService> logger, GatewayServer gatewayServer, IDbContextFactory<DatabaseContext> dbContextFactory)
    {
        _logger = logger;
        _gatewayServer = gatewayServer;
        _dbContextFactory = dbContextFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SyncAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Failed to check online players' membership.");
            }
        }
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        var online = _gatewayServer.OnlinePlayers()
            .Select(x => (Connection: x, CharacterId: GuidHelper.GetPlayerId(x.Player.Guid)))
            .ToList();

        if (online.Count == 0)
            return;

        var characterIds = online.Select(x => x.CharacterId).Distinct().ToList();

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var owners = await dbContext.Characters
            .Where(x => characterIds.Contains(x.Id))
            .Select(x => new { x.Id, x.UserId })
            .ToListAsync(cancellationToken);

        var userIds = owners.Select(x => x.UserId).Distinct().ToList();

        var users = await dbContext.Users
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var changedUsers = false;
        foreach (var user in users.Values)
            changedUsers |= Membership.ExpireIfDue(dbContext, user);

        if (changedUsers)
            await dbContext.SaveChangesAsync(cancellationToken);

        var ownerOf = owners.ToDictionary(x => x.Id, x => x.UserId);

        var pendingByUser = (await dbContext.PendingRewards
            .Where(x => userIds.Contains(x.UserId) && x.ClaimedAt == null)
            .Select(x => new { x.UserId, x.Id })
            .ToListAsync(cancellationToken))
            .GroupBy(x => x.UserId)
            .ToDictionary(x => x.Key, x => x.Count() * 1_000_003L + x.Max(r => r.Id));

        foreach (var (connection, characterId) in online)
        {
            if (!connection.Player.Visible || !ownerOf.TryGetValue(characterId, out var owner))
                continue;

            var signature = pendingByUser.TryGetValue(owner, out var found) ? found : 0;

            if (signature != connection.PendingRewardsSignature)
                connection.QueueWork(() => Handlers.PacketBaseClaimSystemHandler.SendList(connection));
        }

        foreach (var (connection, characterId) in online)
        {
            if (!ownerOf.TryGetValue(characterId, out var userId) || !users.TryGetValue(userId, out var user))
                continue;

            var status = Membership.StatusFor(user);
            var lifetime = status != Membership.FreeStatus && Membership.IsLifetime(user);

            if (connection.Player.MembershipStatus == status && connection.Player.IsLifetimeMember == lifetime)
                continue;

            _logger.LogInformation("{username} is now {state} in game.", user.Username, status == Membership.FreeStatus ? "a free player" : lifetime ? "a lifetime member" : "a member");
            MembershipLive.Apply(connection, status, lifetime);
        }
    }
}
