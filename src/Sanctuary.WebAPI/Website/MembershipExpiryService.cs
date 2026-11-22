using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;

namespace Sanctuary.WebAPI.Website;

public sealed class MembershipExpiryService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly ILogger<MembershipExpiryService> _logger;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;

    public MembershipExpiryService(ILogger<MembershipExpiryService> logger, IDbContextFactory<DatabaseContext> dbContextFactory)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await ExpireAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Failed to check for memberships that ran out.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExpireAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var timed = await dbContext.Users
            .Where(x => x.IsMember && x.MemberUntil != null)
            .ToListAsync(cancellationToken);

        var expired = timed.Where(Membership.HasExpired).ToList();

        if (expired.Count == 0)
            return;

        foreach (var user in expired)
        {
            Membership.Set(dbContext, user, false, null);
            _logger.LogInformation("Membership of {Username} has run out.", user.Username);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
