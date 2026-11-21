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

    private static string Html(string title, string body)
    {
        return "<div style=\"font-family:Tahoma,Arial,sans-serif;font-size:14px;color:#1d3a63;max-width:520px\">"
            + "<h2 style=\"color:#e8780c;margin:0 0 12px\">" + WebUtility.HtmlEncode(title) + "</h2>"
            + body
            + "<p style=\"color:#7a8aa0;font-size:12px;margin-top:24px\">If you didn't ask for this, you can ignore this e-mail.</p></div>";
    }

    private static string Button(string url, string label)
    {
        return "<p><a href=\"" + WebUtility.HtmlEncode(url) + "\" style=\"display:inline-block;padding:10px 22px;background:#4a9a1c;color:#ffffff;font-weight:bold;text-decoration:none;border-radius:10px\">"
            + WebUtility.HtmlEncode(label) + "</a></p><p style=\"font-size:12px;color:#7a8aa0\">Or copy this link into your browser:<br>" + WebUtility.HtmlEncode(url) + "</p>";
    }

    public static async Task<bool> SendVerificationEmailAsync(DatabaseContext dbContext, DbUser user, string origin, EmailSender emailSender, CancellationToken cancellationToken)
    {
        if (!IsValidEmail(user.Email))
            return false;

        var token = await CreateTokenAsync(dbContext, user.Id, VerifyPurpose, user.Email, VerifyLifetime, cancellationToken);
        var url = origin + "/verifyEmail.html?token=" + Uri.EscapeDataString(token);

        var text = $"Hi {user.Username},\n\nPlease confirm this is your e-mail address for your Free Realms account by opening this link:\n\n{url}\n\nThe link works for 3 days.\n\nIf you didn't make a Free Realms account, you can ignore this e-mail.";
        var html = Html("Confirm your e-mail", "<p>Hi " + WebUtility.HtmlEncode(user.Username) + ",</p><p>Please confirm this is your e-mail address for your <i>Free Realms</i> account.</p>" + Button(url, "Confirm My E-mail") + "<p>The link works for 3 days.</p>");

        return await emailSender.SendAsync(user.Email!, "Confirm your Free Realms e-mail", text, html, cancellationToken);
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
        var usernames = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.Email != null && x.Email.ToLower() == lower)
            .Select(x => x.Username)
            .ToListAsync(cancellationToken);

        if (usernames.Count > 0)
        {
            var login = Origin(httpContext, pluginOptions) + "/login.html";
            var list = string.Join("\n", usernames.Select(x => "  - " + x));
            var text = $"Hi!\n\nYou asked for the Free Realms username{(usernames.Count == 1 ? "" : "s")} for this e-mail address:\n\n{list}\n\nLog in here: {login}";
            var html = Html("Your Free Realms username", "<p>You asked for the <i>Free Realms</i> username" + (usernames.Count == 1 ? "" : "s") + " for this e-mail address:</p><ul>"
                + string.Concat(usernames.Select(x => "<li><b>" + WebUtility.HtmlEncode(x) + "</b></li>")) + "</ul>" + Button(login, "Log In"));

            await emailSender.SendAsync(email, "Your Free Realms username", text, html, cancellationToken);
        }

        _logger.LogInformation("Username reminder asked for; {Count} accounts matched.", usernames.Count);

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

        foreach (var user in users.Where(x => IsValidEmail(x.Email)))
        {
            var token = await CreateTokenAsync(dbContext, user.Id, ResetPurpose, user.Email, ResetLifetime, cancellationToken);
            var url = origin + "/resetPassword.html?token=" + Uri.EscapeDataString(token);

            var text = $"Hi {user.Username},\n\nSomeone asked to reset the password for your Free Realms account. To choose a new password, open this link:\n\n{url}\n\nThe link works for 1 hour. If you didn't ask for this, you can ignore this e-mail and your password stays the same.";
            var html = Html("Reset your password", "<p>Hi " + WebUtility.HtmlEncode(user.Username) + ",</p><p>Someone asked to reset the password for your <i>Free Realms</i> account.</p>"
                + Button(url, "Choose a New Password") + "<p>The link works for 1 hour. If you didn't ask for this, your password stays the same.</p>");

            await emailSender.SendAsync(user.Email!, "Reset your Free Realms password", text, html, cancellationToken);
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
