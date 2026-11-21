using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NLog.Extensions.Logging;

using Sanctuary.Core.Extensions;
using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.WebAPI.Endpoints;
using Sanctuary.WebAPI.Options;
using Sanctuary.WebAPI.Website;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls();

// Proxy Server / Load Balancer
var forwardedHeaderSection = builder.Configuration.GetSection("ForwardedHeadersOptions");

if (forwardedHeaderSection is not null)
    builder.Services.Configure<ForwardedHeadersOptions>(forwardedHeaderSection);

// Options
builder.Services.AddOptionsWithValidateOnStart<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.Section)
    .ValidateOnStart();

builder.Services.AddOptionsWithValidateOnStart<WebAPIOptions>()
    .BindConfiguration(WebAPIOptions.Section)
    .ValidateOnStart();

builder.Services.AddOptions<PluginOptions>()
    .BindConfiguration(PluginOptions.Section);

builder.Services.AddOptions<CharacterCreationSettings>()
    .BindConfiguration("CharacterCreation");

// Database
builder.Services.AddDatabase(builder.Configuration);

// Game Resources
builder.Services.AddSingleton<IResourceManager, ResourceManager>();
builder.Services.AddSingleton<CharacterNames>();
builder.Services.AddSingleton<CreatorCatalog>();
builder.Services.AddSingleton<ItemNames>();

builder.Services.AddOptions<BackupOptions>()
    .BindConfiguration(BackupOptions.Section);
builder.Services.AddSingleton<DatabaseBackupService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<DatabaseBackupService>());

// Website Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "sanctuary_website";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = WebsiteSecurity.ValidatePrincipalAsync;
    });

builder.Services.AddAuthorization();
builder.Services.AddWebsiteRateLimits();

// Logging
builder.Logging.ClearProviders();

#if DEBUG

builder.Logging.SetMinimumLevel(LogLevel.Debug);

builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = HttpLoggingFields.All;
});

#endif

var nlogConfigFile = builder.Environment.IsDevelopment()
    ? "NLog.Development.config"
    : "NLog.config";

builder.Logging.AddNLog(nlogConfigFile);

var app = builder.Build();

#if DEBUG

app.UseHttpLogging();

#endif

if (!app.Services.GetRequiredService<IResourceManager>().Load())
{
    app.Logger.LogCritical("Failed to load game resources.");
    return;
}

_ = app.Services.GetRequiredService<CharacterNames>();

// Configure the HTTP request pipeline.

app.UseWebsiteSecurityHeaders();

var websiteRoot = app.Services.GetRequiredService<IOptions<WebAPIOptions>>().Value.WebsiteRoot;

if (!string.IsNullOrWhiteSpace(websiteRoot))
{
    if (Directory.Exists(websiteRoot))
    {
        var websiteFiles = new PhysicalFileProvider(Path.GetFullPath(websiteRoot));

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = websiteFiles });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = websiteFiles,
            OnPrepareResponse = context =>
            {
                var extension = Path.GetExtension(context.File.Name);

                if (extension is ".html" or ".js" or ".css" or ".json")
                    context.Context.Response.Headers.CacheControl = "no-cache";
            }
        });
    }
    else
    {
        app.Logger.LogWarning("Website root {WebsiteRoot} does not exist, the website will not be served.", websiteRoot);
    }
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapAuthEndpoints();
app.MapPortraitEndpoints();
app.MapWebsiteEndpoints();
app.MapPluginEndpoints();

app.Run();