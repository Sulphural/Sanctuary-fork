using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.WebAPI.Options;
using Sanctuary.WebAPI.Website;

using BC = BCrypt.Net.BCrypt;

namespace Sanctuary.WebAPI.Endpoints;

public static class AccountRecoveryEndpoints
{
    private const string ResetPurpose = "reset";
    private const string VerifyPurpose = "verify";

    private static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan VerifyLifetime = TimeSpan.FromDays(3);

    private static ILogger _logger = null!;

    public sealed record EmailRequest(string? Email);
    public sealed record NameRequest(string? Name);
    public sealed record ResetRequest(string? Token, string? Password);
    public sealed record TokenRequest(string? Token);
    public sealed record ChangeEmailRequest(string? Email, string? Password);

    public static void MapAccountRecoveryEndpoints(this RouteGroupBuilder group, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger(nameof(AccountRecoveryEndpoints));

        group.MapPost("/recover/username", RecoverUsernameAsync).RequireRateLimiting(WebsiteSecurity.AuthPolicy);
        group.MapPost("/recover/password", RecoverPasswordAsync).RequireRateLimiting(WebsiteSecurity.AuthPolicy);
        group.MapGet("/recover/password", CheckResetTokenAsync).RequireRateLimiting(WebsiteSecurity.LookupPolicy);
        group.MapPost("/recover/password/reset", ResetPasswordAsync).RequireRateLimiting(WebsiteSecurity.AuthPolicy);

        group.MapPost("/verify-email", VerifyEmailAsync).RequireRateLimiting(WebsiteSecurity.AuthPolicy);
        group.MapPost("/account/email/verify", SendVerificationAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.AuthPolicy);
        group.MapPost("/account/email", ChangeEmailAsync).RequireAuthorization().RequireRateLimiting(WebsiteSecurity.AuthPolicy);
    }

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
            return false;

        try
        {
            var address = new MailAddress(email.Trim());
            return address.Address == email.Trim() && address.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static async Task<string> CreateTokenAsync(DatabaseContext dbContext, ulong userId, string purpose, string? email, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        await dbContext.AccountTokens.Where(x => x.UserId == userId && x.Purpose == purpose).ExecuteDeleteAsync(cancellationToken);

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        dbContext.AccountTokens.Add(new DbAccountToken
        {
            TokenHash = Hash(token),
            UserId = userId,
            Purpose = purpose,
            Email = email,
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime),
            Created = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return token;
    }

    private static async Task<DbAccountToken?> FindTokenAsync(DatabaseContext dbContext, string? token, string purpose, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
            return null;

        var hash = Hash(token.Trim());
        var stored = await dbContext.AccountTokens.Include(x => x.User).SingleOrDefaultAsync(x => x.TokenHash == hash && x.Purpose == purpose, cancellationToken);

        return stored is not null && stored.ExpiresAt > DateTimeOffset.UtcNow ? stored : null;
    }

    private static string Origin(HttpContext httpContext, IOptionsSnapshot<PluginOptions> pluginOptions) => PluginEndpoints.SiteOrigin(httpContext, pluginOptions.Value);

    private const string UsernamePurpose = "username";
    private const string ResetDonePurpose = "resetdone";
    private static readonly TimeSpan RetailLimit = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResendWait = TimeSpan.FromMinutes(5);

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string RetailHtml(string heading, string origin, IEnumerable<string> paragraphs)
    {
        var body = new StringBuilder();

        body.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#333333;max-width:560px\">");
        body.Append("<div style=\"font-size:18px;font-weight:bold;color:#1a75bc;border-bottom:1px solid #666666;padding-bottom:4px;margin-bottom:12px\">").Append(Encode(heading)).Append("</div>");
        body.Append("<p style=\"margin:0 0 14px\">Dear Customer:</p>");

        foreach (var paragraph in paragraphs)
            body.Append("<p style=\"margin:0 0 14px\">").Append(paragraph).Append("</p>");

        body.Append("<p style=\"margin:0 0 14px\">Feel free to <a href=\"").Append(Encode(origin)).Append("/\" style=\"color:#1a75bc\">contact us</a> if you have concerns over the security of your account.</p>");
        body.Append("<p style=\"margin:0 0 14px\">Thank you.</p>");
        body.Append("<p style=\"margin:0 0 40px\">(This is an automated e-mail. Please do not reply.)</p>");
        body.Append("<p style=\"margin:0\">Open Source Free Realms<br><a href=\"").Append(Encode(origin)).Append("\" style=\"color:#333333\">").Append(Encode(origin)).Append("</a></p>");
        body.Append("</div>");

        return body.ToString();
    }

    private static string RetailText(string heading, string origin, IEnumerable<string> paragraphs)
    {
        return heading + "\n\nDear Customer:\n\n" + string.Join("\n\n", paragraphs)
            + "\n\nFeel free to contact us (" + origin + "/) if you have concerns over the security of your account.\n\nThank you.\n\n(This is an automated e-mail. Please do not reply.)\n\n\nOpen Source Free Realms\n" + origin;
    }

    private static string Link(string url, string label) => "<a href=\"" + Encode(url) + "\" style=\"color:#1a75bc\">" + Encode(label) + "</a>";

    public static async Task<bool> SendVerificationEmailAsync(DatabaseContext dbContext, DbUser user, string origin, EmailSender emailSender, CancellationToken cancellationToken)
    {
        if (!IsValidEmail(user.Email))
            return false;

        var token = await CreateTokenAsync(dbContext, user.Id, VerifyPurpose, user.Email, VerifyLifetime, cancellationToken);
        var url = origin + "/verifyEmail.html?token=" + Uri.EscapeDataString(token);

        const string heading = "VERIFY YOUR E-MAIL ADDRESS";
        var intro = "We are sending this e-mail because this e-mail address was entered for your OSFR Account {0}. If you did not initiate this request, you may simply disregard this e-mail.";

        var html = RetailHtml(heading, origin,
        [
            string.Format(intro, "<b>" + Encode(user.Username) + "</b>"),
            "Click on the following link to verify your e-mail address.",
            Link(url, "Verify your E-mail Address"),
            "This link may only be used once, within 3 days."
        ]);

        var text = RetailText(heading, origin,
        [
            string.Format(intro, user.Username),
            "Click on the following link to verify your e-mail address.",
            url,
            "This link may only be used once, within 3 days."
        ]);

        return await emailSender.SendAsync(user.Email!, "Open Source Free Realms - Verify Your E-mail Address", text, html, cancellationToken);
    }

    private static async Task<IResult> RecoverUsernameAsync(
        EmailRequest request,
        HttpContext httpContext,
        EmailSender emailSender,
        IOptionsSnapshot<PluginOptions> pluginOptions,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim() ?? string.Empty;

        if (!IsValidEmail(email))
            return Results.Json(new { error = "email" }, statusCode: StatusCodes.Status400BadRequest);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var lower = email.ToLowerInvariant();
        var users = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.Email != null && x.Email.ToLower() == lower)
            .Select(x => new { x.Id, x.Username })
            .ToListAsync(cancellationToken);

        if (users.Count == 0)
            return Results.Ok(new { sent = true });

        var userIds = users.Select(x => x.Id).ToList();
        var recent = await dbContext.AccountTokens
            .Where(x => userIds.Contains(x.UserId) && x.Purpose == UsernamePurpose)
            .ToListAsync(cancellationToken);

        if (recent.Any(x => x.ExpiresAt > DateTimeOffset.UtcNow))
        {
            _logger.LogInformation("Username reminder skipped; one was already sent in the last 24 hours.");
            return Results.Ok(new { sent = true });
        }

        await CreateTokenAsync(dbContext, users[0].Id, UsernamePurpose, email, RetailLimit, cancellationToken);

        var origin = Origin(httpContext, pluginOptions);
        const string heading = "USERNAME(S) RETRIEVED";
        const string intro = "We are sending this e-mail because we have received a request to retrieve the OSFR Account usernames associated with this e-mail address. If you did not initiate this request, you may simply disregard this e-mail.";
        const string limit = "Username retrieval may only be done once every 24 hours.";

        var html = RetailHtml(heading, origin,
        [
            intro,
            "Usernames associated with <b>" + Encode(email) + "</b>:<br>" + string.Concat(users.Select(x => "<br>&nbsp;&nbsp;&nbsp;&nbsp;<b>" + Encode(x.Username) + "</b>")),
            limit
        ]);

        var text = RetailText(heading, origin,
        [
            intro,
            "Usernames associated with " + email + ":\n\n" + string.Join("\n", users.Select(x => "    " + x.Username)),
            limit
        ]);

        await emailSender.SendAsync(email, "Open Source Free Realms - Username(s) Retrieved", text, html, cancellationToken);

        _logger.LogInformation("Username reminder sent for {Count} accounts.", users.Count);

        return Results.Ok(new { sent = true });
    }

    private static async Task<IResult> RecoverPasswordAsync(
        NameRequest request,
        HttpContext httpContext,
        EmailSender emailSender,
        IOptionsSnapshot<PluginOptions> pluginOptions,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (name.Length == 0 || name.Length > 254)
            return Results.Json(new { error = "name" }, statusCode: StatusCodes.Status400BadRequest);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        List<DbUser> users;

        if (name.Contains('@'))
        {
            var lower = name.ToLowerInvariant();
            users = await dbContext.Users.AsNoTracking().Where(x => x.Email != null && x.Email.ToLower() == lower).ToListAsync(cancellationToken);
        }
        else
        {
            users = await dbContext.Users.AsNoTracking().Where(x => x.Username == name).ToListAsync(cancellationToken);
        }

        var origin = Origin(httpContext, pluginOptions);
        var now = DateTimeOffset.UtcNow;
        const string heading = "PASSWORD RESET";
        const string intro = "We are sending this e-mail because we have received a request to reset the password for your OSFR Account {0}. If you did not initiate this request, you may simply disregard this e-mail.";
        const string limit = "Password reset may only be done once every 24 hours.";

        foreach (var user in users.Where(x => IsValidEmail(x.Email)))
        {
            var tokens = await dbContext.AccountTokens
                .Where(x => x.UserId == user.Id && (x.Purpose == ResetDonePurpose || x.Purpose == ResetPurpose))
                .ToListAsync(cancellationToken);

            if (tokens.Any(x => x.Purpose == ResetDonePurpose && x.ExpiresAt > now)
                || tokens.Any(x => x.Purpose == ResetPurpose && x.Created > now - ResendWait))
            {
                _logger.LogInformation("Password reset e-mail for user {UserId} skipped by the reset limits.", user.Id);
                continue;
            }

            var token = await CreateTokenAsync(dbContext, user.Id, ResetPurpose, user.Email, ResetLifetime, cancellationToken);
            var url = origin + "/resetPassword.html?token=" + Uri.EscapeDataString(token);

            var html = RetailHtml(heading, origin,
            [
                string.Format(intro, "<b>" + Encode(user.Username) + "</b>"),
                "Click on the following link to create a new password.",
                Link(url, "Create a new Password"),
                limit
            ]);

            var text = RetailText(heading, origin,
            [
                string.Format(intro, user.Username),
                "Click on the following link to create a new password.",
                url,
                limit
            ]);

            await emailSender.SendAsync(user.Email!, "Open Source Free Realms - Password Reset", text, html, cancellationToken);
        }

        _logger.LogInformation("Password reset asked for; {Count} accounts matched.", users.Count);

        return Results.Ok(new { sent = true });
    }

    private static async Task<IResult> CheckResetTokenAsync(
        string? token,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var stored = await FindTokenAsync(dbContext, token, ResetPurpose, cancellationToken);

        return Results.Ok(new { valid = stored is not null, username = stored?.User.Username });
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetRequest request,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        var password = request.Password ?? string.Empty;

        if (password.Length < 6 || password.Length > 100 || password.Any(c => c > 0x7F))
            return Results.Json(new { error = "password" }, statusCode: StatusCodes.Status400BadRequest);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var stored = await FindTokenAsync(dbContext, request.Token, ResetPurpose, cancellationToken);

        if (stored is null)
            return Results.Json(new { error = "token" }, statusCode: StatusCodes.Status400BadRequest);

        var user = stored.User;

        user.Password = BC.HashPassword(password, BC.GenerateSalt());
        user.Session = null;
        user.SessionCreated = null;

        if (string.Equals(user.Email, stored.Email, StringComparison.OrdinalIgnoreCase))
            user.EmailVerified = true;

        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.AccountTokens.Where(x => x.UserId == user.Id && x.Purpose == ResetPurpose).ExecuteDeleteAsync(cancellationToken);
        await CreateTokenAsync(dbContext, user.Id, ResetDonePurpose, null, RetailLimit, cancellationToken);

        _logger.LogInformation("User {UserId} reset their password with an e-mailed link.", user.Id);

        return Results.Ok(new { username = user.Username });
    }

    private static async Task<IResult> VerifyEmailAsync(
        TokenRequest request,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var stored = await FindTokenAsync(dbContext, request.Token, VerifyPurpose, cancellationToken);

        if (stored is null || !string.Equals(stored.User.Email, stored.Email, StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { error = "token" }, statusCode: StatusCodes.Status400BadRequest);

        stored.User.EmailVerified = true;
        dbContext.AccountTokens.Remove(stored);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { username = stored.User.Username, email = stored.Email });
    }

    private static async Task<IResult> SendVerificationAsync(
        ClaimsPrincipal principal,
        HttpContext httpContext,
        EmailSender emailSender,
        IOptionsSnapshot<PluginOptions> pluginOptions,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
            return Results.Unauthorized();

        if (!IsValidEmail(user.Email))
            return Results.Json(new { error = "noEmail" }, statusCode: StatusCodes.Status400BadRequest);

        if (user.EmailVerified)
            return Results.Json(new { error = "alreadyVerified" }, statusCode: StatusCodes.Status400BadRequest);

        var sent = await SendVerificationEmailAsync(dbContext, user, Origin(httpContext, pluginOptions), emailSender, cancellationToken);

        return sent ? Results.Ok(new { sent = true }) : Results.Json(new { error = "sendFailed" }, statusCode: StatusCodes.Status502BadGateway);
    }

    private static async Task<IResult> ChangeEmailAsync(
        ChangeEmailRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        EmailSender emailSender,
        IOptionsSnapshot<PluginOptions> pluginOptions,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        var email = request.Email?.Trim() ?? string.Empty;

        if (!IsValidEmail(email))
            return Results.Json(new { error = "email" }, statusCode: StatusCodes.Status400BadRequest);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
            return Results.Unauthorized();

        if (string.IsNullOrEmpty(request.Password) || !BC.Verify(request.Password, user.Password))
            return Results.Json(new { error = "password" }, statusCode: StatusCodes.Status400BadRequest);

        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = email;
            user.EmailVerified = false;
            await dbContext.SaveChangesAsync(cancellationToken);
            await dbContext.AccountTokens.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        }

        var sent = !user.EmailVerified && await SendVerificationEmailAsync(dbContext, user, Origin(httpContext, pluginOptions), emailSender, cancellationToken);

        return Results.Ok(new { email = user.Email, emailVerified = user.EmailVerified, sent });
    }
}
