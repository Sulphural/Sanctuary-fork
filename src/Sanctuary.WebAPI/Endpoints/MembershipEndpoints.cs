using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.WebAPI.Website;

namespace Sanctuary.WebAPI.Endpoints;

public static class MembershipEndpoints
{
    private static ILogger _logger = null!;

    public sealed record BuyMembershipRequest(string? Sku, ulong CharacterId);

    public static void MapMembershipEndpoints(this RouteGroupBuilder group, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger(nameof(MembershipEndpoints));

        group.MapGet("/membership", GetMembershipAsync).RequireAuthorization();
        group.MapPost("/membership/buy", BuyMembershipAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.AuthPolicy);
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out ulong userId)
    {
        return ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    private static object PlanResponse(MembershipPlan plan)
    {
        return new { sku = plan.Sku, name = plan.Name, months = plan.Months, lifetime = plan.IsLifetime, price = plan.PriceStationCash, marketing = plan.MarketingDescription };
    }

    private static async Task<IResult> GetMembershipAsync(
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
            return Results.Unauthorized();

        var characters = await dbContext.Characters
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => (long)x.Id)
            .Select(x => new { id = x.Id, firstName = x.FirstName, lastName = x.LastName, stationCash = x.StationCash })
            .ToListAsync(cancellationToken);

        var member = Membership.StatusFor(user) == Membership.MemberStatus;

        return Results.Ok(new
        {
            isMember = member,
            memberUntil = member ? user.MemberUntil : null,
            lifetime = member && user.MemberUntil is null,
            plans = Membership.Plans.Select(PlanResponse),
            characters
        });
    }

    private static async Task<IResult> BuyMembershipAsync(
        BuyMembershipRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        var plan = Membership.FindPlan(request.Sku);

        if (plan is null)
            return Results.Json(new { error = "plan" }, statusCode: StatusCodes.Status400BadRequest);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
            return Results.Unauthorized();

        Membership.ExpireIfDue(dbContext, user);

        if (Membership.IsLifetime(user))
            return Results.Json(new { error = "lifetime" }, statusCode: StatusCodes.Status409Conflict);

        var character = await dbContext.Characters
            .AsNoTracking()
            .Where(x => x.Id == request.CharacterId && x.UserId == userId)
            .Select(x => new { x.Id, x.FirstName, x.LastName })
            .SingleOrDefaultAsync(cancellationToken);

        if (character is null)
            return Results.NotFound();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var price = plan.PriceStationCash;
        var paid = await dbContext.Characters
            .Where(x => x.Id == character.Id && x.UserId == userId && x.StationCash >= price)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.StationCash, c => c.StationCash - price), cancellationToken);

        if (paid == 0)
            return Results.Json(new { error = "notEnough" }, statusCode: StatusCodes.Status400BadRequest);

        Membership.Extend(dbContext, user, plan);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var balance = await dbContext.Characters
            .AsNoTracking()
            .Where(x => x.Id == character.Id)
            .Select(x => x.StationCash)
            .SingleAsync(cancellationToken);

        _logger.LogInformation("{Username} bought {Plan} membership for {Price} Station Cash from {Character}.", user.Username, plan.Name, price, StaffEndpoints.FullName(character.FirstName, character.LastName));

        return Results.Ok(new
        {
            plan = PlanResponse(plan),
            memberUntil = user.MemberUntil,
            lifetime = user.MemberUntil is null,
            paidBy = StaffEndpoints.FullName(character.FirstName, character.LastName),
            stationCash = balance
        });
    }
}
