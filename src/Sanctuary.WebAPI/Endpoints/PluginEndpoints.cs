using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Sanctuary.WebAPI.Options;

namespace Sanctuary.WebAPI.Endpoints;

public static class PluginEndpoints
{
    private static readonly Dictionary<string, (string FileName, string ContentType)> Downloads = new(StringComparer.OrdinalIgnoreCase)
    {
        ["windows"] = ("FreeRealmsPlugin-windows.exe", "application/vnd.microsoft.portable-executable"),
        ["linux"] = ("FreeRealmsPlugin-linux", "application/octet-stream"),
        ["macos"] = ("FreeRealmsPlugin-macos.zip", "application/zip"),
    };

    public static void MapPluginEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/website/plugin");

        group.MapGet("/config", GetConfig);
        group.MapGet("/downloads", GetDownloads);
        group.MapGet("/download/{platform}", Download);
    }

    public static string SiteOrigin(HttpContext httpContext, PluginOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PublicSiteUrl) && Uri.TryCreate(options.PublicSiteUrl, UriKind.Absolute, out var configured))
            return configured.GetLeftPart(UriPartial.Authority);

        return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    }

    private static IResult GetConfig(IOptionsSnapshot<PluginOptions> pluginOptions, IOptionsSnapshot<WebAPIOptions> webAPIOptions)
    {
        var options = pluginOptions.Value;
        var arguments = (webAPIOptions.Value.LaunchArguments ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return Results.Ok(new
        {
            gateway = options.GatewayAddress,
            login = options.LoginAddress,
            arguments,
            clientManifestUrl = options.ClientManifestUrl,
            clientBaseUrl = options.ClientBaseUrl
        });
    }

    private static IResult GetDownloads(IOptionsSnapshot<PluginOptions> pluginOptions)
    {
        var available = new Dictionary<string, bool>();

        foreach (var (platform, download) in Downloads)
            available[platform] = TryGetDownload(pluginOptions.Value, download.FileName, out _);

        return Results.Ok(available);
    }

    private static IResult Download(string platform, IOptionsSnapshot<PluginOptions> pluginOptions)
    {
        if (!Downloads.TryGetValue(platform, out var download) || !TryGetDownload(pluginOptions.Value, download.FileName, out var path))
            return Results.NotFound();

        return Results.File(path, download.ContentType, download.FileName);
    }

    private static bool TryGetDownload(PluginOptions options, string fileName, out string path)
    {
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(options.DownloadsPath))
            return false;

        path = Path.GetFullPath(Path.Combine(options.DownloadsPath, fileName));

        return File.Exists(path);
    }
}
