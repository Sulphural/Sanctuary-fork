using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Sanctuary.WebAPI.Endpoints;

public static partial class ServerUpdatesEndpoints
{
    private const string Organization = "Open-Source-Free-Realms";
    private const string Repository = Organization + "/Sanctuary";
    private const int MaxUpdates = 15;
    private const int MaxSummaryLength = 220;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RetryDuration = TimeSpan.FromMinutes(2);
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static readonly HttpClient Http = CreateClient();

    private static readonly TimeSpan ContributorCacheDuration = TimeSpan.FromHours(1);
    private static readonly SemaphoreSlim ContributorLock = new(1, 1);

    private static List<ServerUpdate> _cached = [];
    private static DateTimeOffset _expires;

    private static List<Contributor> _contributors = [];
    private static DateTimeOffset _contributorsExpire;

    private sealed record ServerUpdate(int Number, string Title, string Summary, string Author, string Avatar, DateTimeOffset Merged, string Url);

    private sealed record Contributor(string Login, string Avatar, string Url, int Contributions);

    public static void MapServerUpdatesEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/server/updates", GetUpdatesAsync);
        group.MapGet("/server/contributors", GetContributorsAsync);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OSFR-Website", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        return client;
    }

    private static async Task<IResult> GetUpdatesAsync(HttpContext httpContext, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var updates = await GetCachedUpdatesAsync(loggerFactory.CreateLogger(nameof(ServerUpdatesEndpoints)), cancellationToken);

        httpContext.Response.Headers.CacheControl = "public, max-age=300";

        return Results.Json(new
        {
            repository = "https://github.com/" + Repository,
            updates = updates.Select(x => new
            {
                number = x.Number,
                title = x.Title,
                summary = x.Summary,
                author = x.Author,
                avatar = x.Avatar,
                merged = x.Merged,
                url = x.Url
            })
        });
    }

    private static async Task<IResult> GetContributorsAsync(HttpContext httpContext, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var contributors = await GetCachedContributorsAsync(loggerFactory.CreateLogger(nameof(ServerUpdatesEndpoints)), cancellationToken);

        httpContext.Response.Headers.CacheControl = "public, max-age=900";

        return Results.Json(new
        {
            organization = "https://github.com/" + Organization,
            contributors = contributors.Select(x => new
            {
                login = x.Login,
                avatar = x.Avatar,
                url = x.Url,
                contributions = x.Contributions
            })
        });
    }

    private static async Task<List<Contributor>> GetCachedContributorsAsync(ILogger logger, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _contributorsExpire)
            return _contributors;

        await ContributorLock.WaitAsync(cancellationToken);

        try
        {
            if (DateTimeOffset.UtcNow < _contributorsExpire)
                return _contributors;

            try
            {
                _contributors = await FetchContributorsAsync(cancellationToken);
                _contributorsExpire = DateTimeOffset.UtcNow + ContributorCacheDuration;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Could not load contributors from GitHub: {Message}", ex.Message);
                _contributorsExpire = DateTimeOffset.UtcNow + RetryDuration;
            }

            return _contributors;
        }
        finally
        {
            ContributorLock.Release();
        }
    }

    private static async Task<List<Contributor>> FetchContributorsAsync(CancellationToken cancellationToken)
    {
        var repositories = new List<string>();

        using (var response = await Http.GetAsync($"orgs/{Organization}/repos?per_page=100", cancellationToken))
        {
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            foreach (var repository in document.RootElement.EnumerateArray())
            {
                if (repository.TryGetProperty("fork", out var fork) && fork.ValueKind == JsonValueKind.True)
                    continue;

                if (repository.GetProperty("name").GetString() is { Length: > 0 } name)
                    repositories.Add(name);
            }
        }

        var totals = new Dictionary<string, Contributor>(StringComparer.OrdinalIgnoreCase);

        foreach (var repository in repositories)
        {
            using var response = await Http.GetAsync($"repos/{Organization}/{repository}/contributors?per_page=100", cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                continue;

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var user in document.RootElement.EnumerateArray())
            {
                if (!user.TryGetProperty("type", out var type) || type.GetString() != "User")
                    continue;

                var login = user.GetProperty("login").GetString();

                if (string.IsNullOrEmpty(login))
                    continue;

                var contributions = user.TryGetProperty("contributions", out var count) && count.TryGetInt32(out var value) ? value : 0;

                totals[login] = totals.TryGetValue(login, out var existing)
                    ? existing with { Contributions = existing.Contributions + contributions }
                    : new Contributor(login, AvatarUrl(user), "https://github.com/" + login, contributions);
            }
        }

        return totals.Values.OrderByDescending(x => x.Contributions).ThenBy(x => x.Login, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<List<ServerUpdate>> GetCachedUpdatesAsync(ILogger logger, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _expires)
            return _cached;

        await Lock.WaitAsync(cancellationToken);

        try
        {
            if (DateTimeOffset.UtcNow < _expires)
                return _cached;

            try
            {
                _cached = await FetchAsync(cancellationToken);
                _expires = DateTimeOffset.UtcNow + CacheDuration;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Could not load server updates from GitHub: {Message}", ex.Message);
                _expires = DateTimeOffset.UtcNow + RetryDuration;
            }

            return _cached;
        }
        finally
        {
            Lock.Release();
        }
    }

    private static async Task<List<ServerUpdate>> FetchAsync(CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync($"repos/{Repository}/pulls?state=closed&sort=updated&direction=desc&per_page=60", cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var updates = new List<ServerUpdate>();

        foreach (var pull in document.RootElement.EnumerateArray())
        {
            if (!pull.TryGetProperty("merged_at", out var mergedAt) || mergedAt.ValueKind != JsonValueKind.String)
                continue;

            var hasUser = pull.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object;

            updates.Add(new ServerUpdate(
                pull.GetProperty("number").GetInt32(),
                pull.GetProperty("title").GetString()?.Trim() ?? string.Empty,
                Summarize(pull.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() : null),
                hasUser ? user.GetProperty("login").GetString() ?? string.Empty : string.Empty,
                hasUser ? AvatarUrl(user) : string.Empty,
                mergedAt.GetDateTimeOffset(),
                pull.GetProperty("html_url").GetString() ?? string.Empty));
        }

        return updates.OrderByDescending(x => x.Merged).Take(MaxUpdates).ToList();
    }

    private static string AvatarUrl(JsonElement user)
    {
        if (!user.TryGetProperty("id", out var id) || !id.TryGetInt64(out var userId))
            return string.Empty;

        return $"https://avatars.githubusercontent.com/u/{userId}?s=96&v=4";
    }

    private static string Summarize(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var text = HtmlComment().Replace(body.Replace("\r\n", "\n"), string.Empty);

        foreach (var block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var lines = block.Split('\n', StringSplitOptions.TrimEntries)
                .Where(x => x.Length > 0 && !x.StartsWith('#') && !x.StartsWith('|') && !x.StartsWith("```") && !x.StartsWith("**") && !x.StartsWith('-') && !x.StartsWith('*') && !x.StartsWith('!'))
                .ToList();

            if (lines.Count == 0)
                continue;

            var paragraph = string.Join(' ', lines);

            paragraph = MarkdownLink().Replace(paragraph, "$1");
            paragraph = BareUrl().Replace(paragraph, string.Empty);
            paragraph = paragraph.Replace("**", string.Empty).Replace("`", string.Empty).Replace("__", string.Empty);
            paragraph = Whitespace().Replace(paragraph, " ").Trim();

            if (paragraph.Length < 20)
                continue;

            if (paragraph.Length <= MaxSummaryLength)
                return paragraph;

            var cut = paragraph.LastIndexOf(' ', MaxSummaryLength);

            return paragraph[..(cut > 0 ? cut : MaxSummaryLength)].TrimEnd(',', '.', ';', ':') + "...";
        }

        return string.Empty;
    }

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlComment();

    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex BareUrl();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
