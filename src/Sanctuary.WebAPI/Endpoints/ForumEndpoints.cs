using System;
using System.Collections.Generic;
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

public static class ForumEndpoints
{
    private const int ThreadsPerPage = 20;
    private const int PostsPerPage = 20;
    private const int MaxTitleLength = 100;
    private const int MinBodyLength = 2;
    private const int MaxBodyLength = 10000;

    public sealed record NewThreadRequest(string? Title, string? Body, ulong? As);
    public sealed record ReplyRequest(string? Body, ulong? As);
    public sealed record EditRequest(string? Body);
    public sealed record FlagRequest(bool Value);

    private sealed record Poster(DbUser User, DbCharacter Character, bool IsStaff, bool IsMember);

    public static void MapForumEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/forums", ListForumsAsync);
        group.MapGet("/forums/recent", RecentThreadsAsync);
        group.MapGet("/forums/search", SearchThreadsAsync).RequireRateLimiting(WebsiteSecurity.LookupPolicy);
        group.MapGet("/forums/{id:int}", GetForumAsync);
        group.MapGet("/forums/threads/{id}", GetThreadAsync);

        group.MapPost("/forums/{id:int}/threads", CreateThreadAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/forums/threads/{id}/reply", ReplyAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/forums/posts/{id}/edit", EditPostAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/forums/posts/{id}/delete", DeletePostAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/forums/posts/{id}/report", ReportPostAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);

        group.MapPost("/staff/forums/threads/{id}/sticky", StickyAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/staff/forums/threads/{id}/lock", LockAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapPost("/staff/forums/threads/{id}/delete", DeleteThreadAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
        group.MapGet("/staff/forum-reports", GetReportsAsync).RequireAuthorization();
        group.MapPost("/staff/forum-reports/{postId}/dismiss", DismissReportsAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.WritePolicy);
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out ulong userId)
    {
        return ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    private static string Name(string? firstName, string? lastName, string stored)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            return stored;

        return string.IsNullOrWhiteSpace(lastName) ? firstName : firstName + " " + lastName;
    }

    private static string CharacterName(DbCharacter character) => Name(character.FirstName, character.LastName, character.FirstName);

    private static IResult Error(string code, int status = StatusCodes.Status400BadRequest)
    {
        return Results.Json(new { error = code }, statusCode: status);
    }

    private static async Task<DbUser?> GetViewerAsync(DatabaseContext dbContext, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return null;

        return await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
    }

    private static bool IsStaff(DbUser? user) => user is not null && (user.IsAdmin || user.IsMod);

    private static bool IsMember(DbUser? user) => user is not null && user.IsMember && !Membership.HasExpired(user);

    private static bool CanPostIn(DbForum forum, DbUser? user, bool newThread)
    {
        if (user is null)
            return false;

        if (IsStaff(user))
            return true;

        if (newThread && forum.StaffThreadsOnly)
            return false;

        return !forum.MembersOnly || IsMember(user);
    }

    private static async Task<(Poster? Poster, IResult? Error)> GetPosterAsync(DatabaseContext dbContext, ClaimsPrincipal principal, ulong? characterId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return (null, Results.Unauthorized());

        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
            return (null, Results.Unauthorized());

        var now = DateTimeOffset.UtcNow;

        if (user.LockedUntil is not null && user.LockedUntil > now)
            return (null, Error("banned", StatusCodes.Status403Forbidden));

        if (user.MutedUntil is not null && user.MutedUntil > now)
            return (null, Error("muted", StatusCodes.Status403Forbidden));

        var characters = dbContext.Characters.AsNoTracking().Where(x => x.UserId == userId);

        var character = characterId is null
            ? await characters.OrderBy(x => (long)x.Id).FirstOrDefaultAsync(cancellationToken)
            : await characters.SingleOrDefaultAsync(x => x.Id == characterId.Value, cancellationToken);

        if (character is null)
            return (null, Error("noCharacter"));

        return (new Poster(user, character, IsStaff(user), IsMember(user)), null);
    }

    private static bool TryCleanBody(string? raw, out string body)
    {
        body = raw?.Trim().Replace("\r\n", "\n") ?? string.Empty;
        return body.Length >= MinBodyLength && body.Length <= MaxBodyLength;
    }

    private static int PageCount(int items, int perPage) => Math.Max(1, (items + perPage - 1) / perPage);

    private static object Author(ulong? characterId, string? firstName, string? lastName, string stored)
    {
        return new { characterId, name = Name(firstName, lastName, stored) };
    }

    private static async Task<IResult> ListForumsAsync(
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var forums = await dbContext.Forums.AsNoTracking().ToListAsync(cancellationToken);

        var counts = await dbContext.ForumThreads
            .AsNoTracking()
            .GroupBy(x => x.ForumId)
            .Select(g => new { ForumId = g.Key, Threads = g.Count(), Posts = g.Sum(x => x.ReplyCount + 1), Latest = g.Max(x => (long)x.LastPostId) })
            .ToListAsync(cancellationToken);

        var latestIds = counts.Select(x => (ulong)x.Latest).ToList();

        var latest = await dbContext.ForumThreads
            .AsNoTracking()
            .Where(x => latestIds.Contains(x.LastPostId))
            .Select(x => new { x.Id, x.ForumId, x.Title, x.LastPostAt, x.LastPostCharacterId, x.LastPostName, x.LastPostId })
            .ToListAsync(cancellationToken);

        var totalThreads = counts.Sum(x => x.Threads);
        var totalPosts = counts.Sum(x => x.Posts);
        var players = await dbContext.Users.CountAsync(cancellationToken);

        var categories = forums
            .GroupBy(x => new { x.Category, x.CategoryOrder })
            .OrderBy(g => g.Key.CategoryOrder)
            .Select(g => new
            {
                title = g.Key.Category,
                forums = g.OrderBy(x => x.DisplayOrder).Select(f =>
                {
                    var count = counts.FirstOrDefault(c => c.ForumId == f.Id);
                    var last = count is null ? null : latest.FirstOrDefault(t => t.ForumId == f.Id && (long)t.LastPostId == count.Latest);

                    return new
                    {
                        id = f.Id,
                        title = f.Title,
                        description = f.Description,
                        membersOnly = f.MembersOnly,
                        staffThreadsOnly = f.StaffThreadsOnly,
                        threads = count?.Threads ?? 0,
                        posts = count?.Posts ?? 0,
                        latest = last is null ? null : new
                        {
                            threadId = last.Id,
                            title = last.Title,
                            at = last.LastPostAt,
                            author = new { characterId = last.LastPostCharacterId, name = last.LastPostName }
                        }
                    };
                })
            });

        return Results.Ok(new { categories, stats = new { threads = totalThreads, posts = totalPosts, players } });
    }

    private static async Task<IResult> GetForumAsync(
        int id,
        int? page,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var forum = await dbContext.Forums.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (forum is null)
            return Results.NotFound();

        var viewer = await GetViewerAsync(dbContext, principal, cancellationToken);

        var total = await dbContext.ForumThreads.CountAsync(x => x.ForumId == id, cancellationToken);
        var pages = PageCount(total, ThreadsPerPage);
        var current = Math.Clamp(page ?? 1, 1, pages);

        var threads = await dbContext.ForumThreads
            .AsNoTracking()
            .Where(x => x.ForumId == id)
            .OrderByDescending(x => x.IsSticky)
            .ThenByDescending(x => (long)x.LastPostId)
            .Skip((current - 1) * ThreadsPerPage)
            .Take(ThreadsPerPage)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Created,
                x.ReplyCount,
                x.ViewCount,
                x.IsSticky,
                x.IsLocked,
                x.AuthorCharacterId,
                AuthorFirstName = x.AuthorCharacter == null ? null : x.AuthorCharacter.FirstName,
                AuthorLastName = x.AuthorCharacter == null ? null : x.AuthorCharacter.LastName,
                x.AuthorName,
                x.LastPostAt,
                x.LastPostCharacterId,
                x.LastPostName
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            forum = new { id = forum.Id, title = forum.Title, description = forum.Description, category = forum.Category, membersOnly = forum.MembersOnly, staffThreadsOnly = forum.StaffThreadsOnly },
            page = current,
            pages,
            canPost = CanPostIn(forum, viewer, true),
            loggedIn = viewer is not null,
            threads = threads.Select(x => new
            {
                id = x.Id,
                title = x.Title,
                created = x.Created,
                replies = x.ReplyCount,
                views = x.ViewCount,
                sticky = x.IsSticky,
                locked = x.IsLocked,
                author = Author(x.AuthorCharacterId, x.AuthorFirstName, x.AuthorLastName, x.AuthorName),
                lastPost = new { at = x.LastPostAt, author = new { characterId = x.LastPostCharacterId, name = x.LastPostName } }
            })
        });
    }

    private static async Task<IResult> GetThreadAsync(
        ulong id,
        int? page,
        ulong? post,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var thread = await dbContext.ForumThreads
            .AsNoTracking()
            .Include(x => x.Forum)
            .Include(x => x.AuthorCharacter)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (thread is null)
            return Results.NotFound();

        await dbContext.ForumThreads.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ViewCount, x => x.ViewCount + 1), cancellationToken);

        var viewer = await GetViewerAsync(dbContext, principal, cancellationToken);
        var viewerIsStaff = IsStaff(viewer);

        var total = await dbContext.ForumPosts.CountAsync(x => x.ThreadId == id, cancellationToken);
        var pages = PageCount(total, PostsPerPage);

        if (post is not null)
        {
            var before = await dbContext.ForumPosts.CountAsync(x => x.ThreadId == id && (long)x.Id < (long)post.Value, cancellationToken);
            page = before / PostsPerPage + 1;
        }

        var current = Math.Clamp(page ?? 1, 1, pages);

        var firstPostId = await dbContext.ForumPosts
            .Where(x => x.ThreadId == id)
            .OrderBy(x => (long)x.Id)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var posts = await dbContext.ForumPosts
            .AsNoTracking()
            .Where(x => x.ThreadId == id)
            .OrderBy(x => (long)x.Id)
            .Skip((current - 1) * PostsPerPage)
            .Take(PostsPerPage)
            .Select(x => new
            {
                x.Id,
                x.Body,
                x.Created,
                x.Edited,
                x.EditedBy,
                x.AuthorUserId,
                x.AuthorCharacterId,
                AuthorFirstName = x.AuthorCharacter == null ? null : x.AuthorCharacter.FirstName,
                AuthorLastName = x.AuthorCharacter == null ? null : x.AuthorCharacter.LastName,
                x.AuthorName,
                AuthorIsAdmin = x.AuthorUser != null && x.AuthorUser.IsAdmin,
                AuthorIsMod = x.AuthorUser != null && x.AuthorUser.IsMod
            })
            .ToListAsync(cancellationToken);

        var characterIds = posts.Where(x => x.AuthorCharacterId is not null).Select(x => x.AuthorCharacterId!.Value).Distinct().ToList();

        var postCounts = await dbContext.ForumPosts
            .AsNoTracking()
            .Where(x => x.AuthorCharacterId != null && characterIds.Contains(x.AuthorCharacterId.Value))
            .GroupBy(x => x.AuthorCharacterId)
            .Select(g => new { CharacterId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CharacterId!.Value, x => x.Count, cancellationToken);

        var reported = viewer is null
            ? []
            : await dbContext.ForumPostReports
                .AsNoTracking()
                .Where(x => x.ReporterUserId == viewer.Id && posts.Select(p => p.Id).Contains(x.PostId))
                .Select(x => x.PostId)
                .ToListAsync(cancellationToken);

        var number = (current - 1) * PostsPerPage;

        return Results.Ok(new
        {
            thread = new
            {
                id = thread.Id,
                title = thread.Title,
                created = thread.Created,
                replies = thread.ReplyCount,
                views = thread.ViewCount + 1,
                sticky = thread.IsSticky,
                locked = thread.IsLocked,
                author = thread.AuthorCharacter is null
                    ? Author(null, null, null, thread.AuthorName)
                    : Author(thread.AuthorCharacterId, thread.AuthorCharacter.FirstName, thread.AuthorCharacter.LastName, thread.AuthorName),
                forum = new { id = thread.Forum.Id, title = thread.Forum.Title, category = thread.Forum.Category }
            },
            page = current,
            pages,
            loggedIn = viewer is not null,
            isStaff = viewerIsStaff,
            canReply = CanPostIn(thread.Forum, viewer, false) && (!thread.IsLocked || viewerIsStaff),
            posts = posts.Select(x =>
            {
                var own = viewer is not null && x.AuthorUserId == viewer.Id;
                var count = x.AuthorCharacterId is not null && postCounts.TryGetValue(x.AuthorCharacterId.Value, out var c) ? c : 0;
                var role = x.AuthorIsAdmin ? "admin" : x.AuthorIsMod ? "moderator" : "player";

                return new
                {
                    id = x.Id,
                    number = ++number,
                    body = x.Body,
                    created = x.Created,
                    edited = x.Edited,
                    editedBy = x.EditedBy,
                    first = x.Id == firstPostId,
                    author = new
                    {
                        characterId = x.AuthorCharacterId,
                        name = Name(x.AuthorFirstName, x.AuthorLastName, x.AuthorName),
                        role,
                        posts = count
                    },
                    canEdit = viewerIsStaff || (own && !thread.IsLocked),
                    canDelete = viewerIsStaff || (own && x.Id != firstPostId && !thread.IsLocked),
                    canReport = viewer is not null && !own,
                    reported = reported.Contains(x.Id)
                };
            })
        });
    }

    private static async Task<IResult> RecentThreadsAsync(
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var threads = await dbContext.ForumThreads
            .AsNoTracking()
            .OrderByDescending(x => (long)x.LastPostId)
            .Take(25)
            .Select(ThreadRow)
            .ToListAsync(cancellationToken);

        return Results.Ok(threads);
    }

    private static async Task<IResult> SearchThreadsAsync(
        string? q,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        var query = q?.Trim() ?? string.Empty;

        if (query.Length < 2 || query.Length > 50)
            return Results.Ok(Array.Empty<object>());

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var pattern = "%" + query.Replace("\\", "").Replace("%", "").Replace("_", "") + "%";

        var threads = await dbContext.ForumThreads
            .AsNoTracking()
            .Where(x => EF.Functions.Like(x.Title, pattern))
            .OrderByDescending(x => (long)x.LastPostId)
            .Take(25)
            .Select(ThreadRow)
            .ToListAsync(cancellationToken);

        return Results.Ok(threads);
    }

    private static readonly System.Linq.Expressions.Expression<Func<DbForumThread, object>> ThreadRow = x => new
    {
        id = x.Id,
        title = x.Title,
        created = x.Created,
        replies = x.ReplyCount,
        views = x.ViewCount,
        sticky = x.IsSticky,
        locked = x.IsLocked,
        forum = new { id = x.Forum.Id, title = x.Forum.Title },
        author = new { characterId = x.AuthorCharacterId, name = x.AuthorName },
        lastPost = new { at = x.LastPostAt, author = new { characterId = x.LastPostCharacterId, name = x.LastPostName } }
    };

    private static async Task<IResult> CreateThreadAsync(
        int id,
        NewThreadRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim() ?? string.Empty;

        if (title.Length < 3 || title.Length > MaxTitleLength)
            return Error("title");

        if (!TryCleanBody(request.Body, out var body))
            return Error("body");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var forum = await dbContext.Forums.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (forum is null)
            return Results.NotFound();

        var (poster, error) = await GetPosterAsync(dbContext, principal, request.As, cancellationToken);

        if (poster is null)
            return error!;

        if (!CanPostIn(forum, poster.User, true))
            return Error(forum.MembersOnly && !poster.IsMember ? "membersOnly" : "staffOnly", StatusCodes.Status403Forbidden);

        var name = CharacterName(poster.Character);
        var now = DateTimeOffset.UtcNow;

        var thread = new DbForumThread
        {
            ForumId = forum.Id,
            Title = title,
            AuthorUserId = poster.User.Id,
            AuthorCharacterId = poster.Character.Id,
            AuthorName = name,
            Created = now,
            LastPostAt = now,
            LastPostCharacterId = poster.Character.Id,
            LastPostName = name
        };

        dbContext.ForumThreads.Add(thread);
        await dbContext.SaveChangesAsync(cancellationToken);

        var post = new DbForumPost
        {
            ThreadId = thread.Id,
            AuthorUserId = poster.User.Id,
            AuthorCharacterId = poster.Character.Id,
            AuthorName = name,
            Created = now,
            Body = body
        };

        dbContext.ForumPosts.Add(post);
        await dbContext.SaveChangesAsync(cancellationToken);

        thread.LastPostId = post.Id;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { threadId = thread.Id, postId = post.Id });
    }

    private static async Task<IResult> ReplyAsync(
        ulong id,
        ReplyRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryCleanBody(request.Body, out var body))
            return Error("body");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var thread = await dbContext.ForumThreads.Include(x => x.Forum).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (thread is null)
            return Results.NotFound();

        var (poster, error) = await GetPosterAsync(dbContext, principal, request.As, cancellationToken);

        if (poster is null)
            return error!;

        if (!CanPostIn(thread.Forum, poster.User, false))
            return Error("membersOnly", StatusCodes.Status403Forbidden);

        if (thread.IsLocked && !poster.IsStaff)
            return Error("locked", StatusCodes.Status403Forbidden);

        var name = CharacterName(poster.Character);
        var now = DateTimeOffset.UtcNow;

        var post = new DbForumPost
        {
            ThreadId = thread.Id,
            AuthorUserId = poster.User.Id,
            AuthorCharacterId = poster.Character.Id,
            AuthorName = name,
            Created = now,
            Body = body
        };

        dbContext.ForumPosts.Add(post);
        await dbContext.SaveChangesAsync(cancellationToken);

        thread.ReplyCount += 1;
        thread.LastPostAt = now;
        thread.LastPostId = post.Id;
        thread.LastPostCharacterId = poster.Character.Id;
        thread.LastPostName = name;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { threadId = thread.Id, postId = post.Id });
    }

    private static async Task<IResult> EditPostAsync(
        ulong id,
        EditRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryCleanBody(request.Body, out var body))
            return Error("body");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var viewer = await GetViewerAsync(dbContext, principal, cancellationToken);

        if (viewer is null)
            return Results.Unauthorized();

        var post = await dbContext.ForumPosts.Include(x => x.Thread).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (post is null)
            return Results.NotFound();

        var staff = IsStaff(viewer);
        var own = post.AuthorUserId == viewer.Id;

        if (!staff && (!own || post.Thread.IsLocked))
            return Results.Forbid();

        if (staff && !own && !await StaffAccess.CanActOnUserAsync(dbContext, viewer, post.AuthorUserId, cancellationToken))
            return Error("notAllowed", StatusCodes.Status403Forbidden);

        if (!staff && viewer.MutedUntil is not null && viewer.MutedUntil > DateTimeOffset.UtcNow)
            return Error("muted", StatusCodes.Status403Forbidden);

        post.Body = body;
        post.Edited = DateTimeOffset.UtcNow;
        post.EditedBy = own ? null : "Staff";

        if (staff && !own)
            StaffAccess.Log(dbContext, viewer, "editForumPost", post.AuthorUserId, post.AuthorCharacterId, post.AuthorName, post.Thread.Title);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { id = post.Id });
    }

    private static async Task RefreshLastPostAsync(DatabaseContext dbContext, DbForumThread thread, CancellationToken cancellationToken)
    {
        var last = await dbContext.ForumPosts
            .Where(x => x.ThreadId == thread.Id)
            .OrderByDescending(x => (long)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (last is null)
            return;

        thread.LastPostId = last.Id;
        thread.LastPostAt = last.Created;
        thread.LastPostCharacterId = last.AuthorCharacterId;
        thread.LastPostName = last.AuthorName;
    }

    private static async Task<IResult> DeletePostAsync(
        ulong id,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var viewer = await GetViewerAsync(dbContext, principal, cancellationToken);

        if (viewer is null)
            return Results.Unauthorized();

        var post = await dbContext.ForumPosts.Include(x => x.Thread).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (post is null)
            return Results.NotFound();

        var thread = post.Thread;
        var staff = IsStaff(viewer);
        var own = post.AuthorUserId == viewer.Id;

        if (staff && !own && !await StaffAccess.CanActOnUserAsync(dbContext, viewer, post.AuthorUserId, cancellationToken))
            return Error("notAllowed", StatusCodes.Status403Forbidden);

        var firstPostId = await dbContext.ForumPosts
            .Where(x => x.ThreadId == thread.Id)
            .OrderBy(x => (long)x.Id)
            .Select(x => x.Id)
            .FirstAsync(cancellationToken);

        if (post.Id == firstPostId)
        {
            if (!staff)
                return Results.Forbid();

            dbContext.ForumThreads.Remove(thread);
            StaffAccess.Log(dbContext, viewer, "deleteForumThread", post.AuthorUserId, post.AuthorCharacterId, post.AuthorName, thread.Title);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(new { deletedThread = thread.Id, forumId = thread.ForumId });
        }

        if (!staff && (!own || thread.IsLocked))
            return Results.Forbid();

        dbContext.ForumPosts.Remove(post);
        thread.ReplyCount = Math.Max(0, thread.ReplyCount - 1);

        if (staff && !own)
            StaffAccess.Log(dbContext, viewer, "deleteForumPost", post.AuthorUserId, post.AuthorCharacterId, post.AuthorName, Truncate(post.Body, 300));

        await dbContext.SaveChangesAsync(cancellationToken);

        if (thread.LastPostId == post.Id)
        {
            await RefreshLastPostAsync(dbContext, thread, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(new { deleted = post.Id, threadId = thread.Id });
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private static async Task<IResult> ReportPostAsync(
        ulong id,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var post = await dbContext.ForumPosts.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.AuthorUserId }).SingleOrDefaultAsync(cancellationToken);

        if (post is null)
            return Results.NotFound();

        if (post.AuthorUserId == userId)
            return Results.BadRequest();

        if (!await dbContext.ForumPostReports.AnyAsync(x => x.PostId == id && x.ReporterUserId == userId, cancellationToken))
        {
            dbContext.ForumPostReports.Add(new DbForumPostReport { PostId = id, ReporterUserId = userId, Created = DateTimeOffset.UtcNow });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(new { reported = id });
    }

    private static async Task<(DbUser? Staff, DbForumThread? Thread, IResult? Error)> StaffThreadAsync(DatabaseContext dbContext, ClaimsPrincipal principal, ulong id, CancellationToken cancellationToken)
    {
        var staff = await StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken);

        if (staff is null)
            return (null, null, Results.Forbid());

        var thread = await dbContext.ForumThreads.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return thread is null ? (staff, null, Results.NotFound()) : (staff, thread, null);
    }

    private static async Task<IResult> StickyAsync(
        ulong id,
        FlagRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var (staff, thread, error) = await StaffThreadAsync(dbContext, principal, id, cancellationToken);

        if (error is not null)
            return error;

        thread!.IsSticky = request.Value;
        StaffAccess.Log(dbContext, staff!, request.Value ? "stickForumThread" : "unstickForumThread", thread.AuthorUserId, thread.AuthorCharacterId, thread.AuthorName, thread.Title);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { sticky = thread.IsSticky });
    }

    private static async Task<IResult> LockAsync(
        ulong id,
        FlagRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var (staff, thread, error) = await StaffThreadAsync(dbContext, principal, id, cancellationToken);

        if (error is not null)
            return error;

        thread!.IsLocked = request.Value;
        StaffAccess.Log(dbContext, staff!, request.Value ? "lockForumThread" : "unlockForumThread", thread.AuthorUserId, thread.AuthorCharacterId, thread.AuthorName, thread.Title);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { locked = thread.IsLocked });
    }

    private static async Task<IResult> DeleteThreadAsync(
        ulong id,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var (staff, thread, error) = await StaffThreadAsync(dbContext, principal, id, cancellationToken);

        if (error is not null)
            return error;

        if (!await StaffAccess.CanActOnUserAsync(dbContext, staff!, thread!.AuthorUserId, cancellationToken))
            return Error("notAllowed", StatusCodes.Status403Forbidden);

        dbContext.ForumThreads.Remove(thread!);
        StaffAccess.Log(dbContext, staff!, "deleteForumThread", thread!.AuthorUserId, thread.AuthorCharacterId, thread.AuthorName, thread.Title);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { deletedThread = thread.Id, forumId = thread.ForumId });
    }

    private static async Task<IResult> GetReportsAsync(
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var staff = await StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken);

        if (staff is null)
            return Results.Forbid();

        var reports = await dbContext.ForumPostReports
            .AsNoTracking()
            .Select(x => new
            {
                x.PostId,
                x.Created,
                Reporter = x.ReporterUser.Username,
                x.Post.Body,
                PostCreated = x.Post.Created,
                x.Post.ThreadId,
                ThreadTitle = x.Post.Thread.Title,
                x.Post.AuthorName,
                x.Post.AuthorUserId,
                x.Post.AuthorCharacterId,
                AuthorFirstName = x.Post.AuthorCharacter == null ? null : x.Post.AuthorCharacter.FirstName,
                AuthorLastName = x.Post.AuthorCharacter == null ? null : x.Post.AuthorCharacter.LastName,
                AuthorUsername = x.Post.AuthorUser == null ? null : x.Post.AuthorUser.Username
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(reports
            .GroupBy(x => x.PostId)
            .Select(g =>
            {
                var first = g.First();

                return new
                {
                    postId = g.Key,
                    threadId = first.ThreadId,
                    threadTitle = first.ThreadTitle,
                    body = first.Body,
                    created = first.PostCreated,
                    author = new
                    {
                        id = first.AuthorCharacterId,
                        userId = first.AuthorUserId,
                        firstName = first.AuthorFirstName ?? first.AuthorName,
                        lastName = first.AuthorFirstName is null ? null : first.AuthorLastName,
                        username = first.AuthorUsername
                    },
                    reporters = g.Select(x => x.Reporter).OrderBy(x => x).ToList(),
                    lastReported = g.Max(x => x.Created)
                };
            })
            .OrderByDescending(x => x.reporters.Count)
            .ThenByDescending(x => x.lastReported));
    }

    private static async Task<IResult> DismissReportsAsync(
        ulong postId,
        ClaimsPrincipal principal,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var staff = await StaffAccess.GetStaffAsync(dbContext, principal, cancellationToken);

        if (staff is null)
            return Results.Forbid();

        var post = await dbContext.ForumPosts.AsNoTracking().Where(x => x.Id == postId).Select(x => new { x.AuthorUserId, x.AuthorCharacterId, x.AuthorName, x.Body }).SingleOrDefaultAsync(cancellationToken);

        await dbContext.ForumPostReports.Where(x => x.PostId == postId).ExecuteDeleteAsync(cancellationToken);

        StaffAccess.Log(dbContext, staff, "dismissForumReports", post?.AuthorUserId, post?.AuthorCharacterId, post?.AuthorName, post is null ? null : Truncate(post.Body, 300));
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { dismissed = postId });
    }
}
