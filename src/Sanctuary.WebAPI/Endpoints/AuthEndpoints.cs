using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MiniValidation;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.WebAPI.Models;
using Sanctuary.WebAPI.Options;
using Sanctuary.WebAPI.Website;

using BC = BCrypt.Net.BCrypt;

namespace Sanctuary.WebAPI.Endpoints;

public static class AuthEndpoints
{
    private static ILogger _logger = null!;

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

        _logger = loggerFactory.CreateLogger(nameof(AuthEndpoints));

        app.MapPost("/login", LoginHandlerAsync).RequireRateLimiting(WebsiteSecurity.AuthPolicy);
    }

    private static async Task<IResult> LoginHandlerAsync(
        LoginRequestModel request,
        CancellationToken cancellationToken,
        IOptionsSnapshot<WebAPIOptions> webAPIOptions,
        IDbContextFactory<DatabaseContext> dbContextFactory)
    {
        if (!MiniValidator.TryValidate(request, out var errors))
            return Results.ValidationProblem(errors);

        var dbContext = await dbContextFactory.CreateDbContextAsync();

        if (LoginThrottle.IsBlocked(request.Username))
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);

        var dbUser = await dbContext.Users.FirstOrDefaultAsync(x => x.Username == request.Username, cancellationToken);

        if (dbUser is null)
        {
            _logger.LogWarning("Login failed, user not found for username: {Username}", request.Username);
            LoginThrottle.RecordFailure(request.Username);

            return Results.Unauthorized();
        }

        if (!BC.Verify(request.Password, dbUser.Password))
        {
            _logger.LogWarning("Login failed, invalid password for username: {Username}", request.Username);
            LoginThrottle.RecordFailure(request.Username);

            return Results.Unauthorized();
        }

        LoginThrottle.RecordSuccess(request.Username);

        if (dbUser.LockedUntil is not null && dbUser.LockedUntil > DateTimeOffset.UtcNow)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        dbUser.Session = Guid.NewGuid().ToString("N");
        dbUser.SessionCreated = DateTimeOffset.UtcNow;

        if (await dbContext.SaveChangesAsync(cancellationToken) <= 0)
        {
            _logger.LogError("Failed to update session info for username: {Username}", dbUser.Username);

            return Results.InternalServerError();
        }

        return Results.Ok(new LoginResponseModel
        {
            SessionId = dbUser.Session,
            LaunchArguments = webAPIOptions.Value.LaunchArguments
        });
    }

}