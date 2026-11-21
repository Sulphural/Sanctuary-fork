using System;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sanctuary.Database;
using Sanctuary.Database.Entities;

namespace Sanctuary.WebAPI.Website;

public static class WebsiteSecurity
{
    public const string AuthPolicy = "auth";
    public const string WritePolicy = "write";
    public const string LookupPolicy = "lookup";

    public const string StampClaim = "sanctuary:stamp";

    public static IServiceCollection AddWebsiteRateLimits(this IServiceCollection services)
    {
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                return new ValueTask(context.HttpContext.Response.WriteAsync("{\"error\":\"tooManyRequests\"}", cancellationToken));
            };

            options.AddPolicy(AuthPolicy, httpContext => Window(httpContext, AuthPolicy, 10, TimeSpan.FromMinutes(1)));
            options.AddPolicy(WritePolicy, httpContext => Window(httpContext, WritePolicy, 30, TimeSpan.FromMinutes(1)));
            options.AddPolicy(LookupPolicy, httpContext => Window(httpContext, LookupPolicy, 120, TimeSpan.FromMinutes(1)));
        });
    }

    private static RateLimitPartition<string> Window(HttpContext httpContext, string policy, int permits, TimeSpan window)
    {
        return RateLimitPartition.GetFixedWindowLimiter(policy + ":" + ClientAddress(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0
        });
    }

    public static string ClientAddress(HttpContext httpContext)
    {
        var remote = httpContext.Connection.RemoteIpAddress;

        if (remote is not null && IPAddress.IsLoopback(remote)
            && httpContext.Request.Headers.TryGetValue("CF-Connecting-IP", out var forwarded)
            && IPAddress.TryParse(forwarded.ToString(), out var client))
            return client.ToString();

        return remote?.ToString() ?? "unknown";
    }

    public static string Stamp(DbUser dbUser)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dbUser.Password)))[..16];
    }

    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;

        if (principal is null || !ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            await RejectAsync(context);
            return;
        }

        var stamp = principal.FindFirstValue(StampClaim);
        var dbContextFactory = context.HttpContext.RequestServices.GetRequiredService<IDbContextFactory<DatabaseContext>>();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(context.HttpContext.RequestAborted);

        var dbUser = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, context.HttpContext.RequestAborted);

        if (dbUser is null || stamp is null || stamp != Stamp(dbUser)
            || (dbUser.LockedUntil is not null && dbUser.LockedUntil > DateTimeOffset.UtcNow))
            await RejectAsync(context);
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public static IApplicationBuilder UseWebsiteSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use((httpContext, next) =>
        {
            var headers = httpContext.Response.Headers;

            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "SAMEORIGIN";
            headers["Referrer-Policy"] = "same-origin";

            return next(httpContext);
        });
    }
}
