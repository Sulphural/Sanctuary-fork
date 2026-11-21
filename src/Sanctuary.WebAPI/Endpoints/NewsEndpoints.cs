using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.WebAPI.Website;

namespace Sanctuary.WebAPI.Endpoints;

public static class NewsEndpoints
{
    private const int MaxTitleLength = 100;
    private const int MaxSummaryLength = 300;
    private const int MaxBodyLength = 8000;

    public sealed record NewsRequest(string? Title, string? Summary, string? Body);

    public static void MapNewsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/news", ListNewsAsync);
        group.MapGet("/news/{id}", GetNewsAsync);

        group.MapPost("/staff/news", CreateNewsAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/staff/news/{id}/update", UpdateNewsAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/staff/news/{id}/delete", DeleteNewsAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
    }

    private static async Task<IResult> ListNewsAsync(
        int? take,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var posts = await dbContext.NewsPosts
            .AsNoTracking()
            .OrderByDescending(x => (long)x.Id)
            .Take(Math.Clamp(take ?? 20, 1, 50))
            .Select(x => new { id = x.Id, title = x.Title, summary = x.Summary, created = x.Created, author = x.AuthorUsername })
            .ToListAsync(cancellationToken);

        return Results.Ok(posts);
    }

    private static async Task<IResult> GetNewsAsync(
        ulong id,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var post = await dbContext.NewsPosts
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { id = x.Id, title = x.Title, summary = x.Summary, body = x.Body, created = x.Created, author = x.AuthorUsername })
            .SingleOrDefaultAsync(cancellationToken);

        return post is null ? Results.NotFound() : Results.Ok(post);
    }

    private static bool TryClean(NewsRequest request, out string title, out string summary, out string body, out IResult? error)
    {
        title = request.Title?.Trim() ?? string.Empty;
        summary = request.Summary?.Trim() ?? string.Empty;
        body = request.Body?.Trim().Replace("\r\n", "\n") ?? string.Empty;
        error = null;

        if (title.Length == 0 || title.Length > MaxTitleLength)
            error = Results.Json(new { error = "title" }, statusCode: StatusCodes.Status400BadRequest);
        else if (summary.Length == 0 || summary.Length > MaxSummaryLength)
            error = Results.Json(new { error = "summary" }, statusCode: StatusCodes.Status400BadRequest);
        else if (body.Length == 0 || body.Length > MaxBodyLength)
            error = Results.Json(new { error = "body" }, statusCode: StatusCodes.Status400BadRequest);

        return error is null;
    }

    private static async Task<IResult> CreateNewsAsync(
        NewsRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryClean(request, out var title, out var summary, out var body, out var error))
            return error!;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var staff = await StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken);

        if (staff is null)
            return Results.Forbid();

        var post = new DbNewsPost { Title = title, Summary = summary, Body = body, AuthorUsername = staff.Username, Created = DateTimeOffset.UtcNow };

        dbContext.NewsPosts.Add(post);
        await dbContext.SaveChangesAsync(cancellationToken);

        StaffAccess.Log(dbContext, staff, "postNews", null, null, title, null);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { id = post.Id });
    }

    private static async Task<IResult> UpdateNewsAsync(
        ulong id,
        NewsRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryClean(request, out var title, out var summary, out var body, out var error))
            return error!;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var staff = await StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken);

        if (staff is null)
            return Results.Forbid();

        var post = await dbContext.NewsPosts.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (post is null)
            return Results.NotFound();

        post.Title = title;
        post.Summary = summary;
        post.Body = body;

        StaffAccess.Log(dbContext, staff, "editNews", null, null, title, null);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { id = post.Id });
    }

    private static async Task<IResult> DeleteNewsAsync(
        ulong id,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var staff = await StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken);

        if (staff is null)
            return Results.Forbid();

        var post = await dbContext.NewsPosts.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (post is null)
            return Results.NotFound();

        dbContext.NewsPosts.Remove(post);
        StaffAccess.Log(dbContext, staff, "deleteNews", null, null, post.Title, null);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { deleted = id });
    }
}
