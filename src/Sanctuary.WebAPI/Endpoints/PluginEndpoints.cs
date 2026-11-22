using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

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

    private static readonly Version PurchaseCertificatePluginVersion = new(1, 1, 0);

    private const string PurchaseCertificateFileName = "sanctuary-uram-ca.pem";

    private static IResult GetConfig(string? plugin, HttpContext httpContext, IOptionsSnapshot<PluginOptions> pluginOptions, IOptionsSnapshot<WebAPIOptions> webAPIOptions)
    {
        var options = pluginOptions.Value;
        var arguments = new List<string>((webAPIOptions.Value.LaunchArguments ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var local = IsSameNetwork(httpContext, options);

        string? purchaseCertificate = null;
        var purchaseAddress = local && !string.IsNullOrWhiteSpace(options.LocalPurchaseAddress) ? options.LocalPurchaseAddress : options.PurchaseAddress;

        if (!string.IsNullOrWhiteSpace(purchaseAddress)
            && Version.TryParse(plugin, out var pluginVersion) && pluginVersion >= PurchaseCertificatePluginVersion
            && TryReadPurchaseCertificate(options, out purchaseCertificate))
        {
            arguments.Add($"INGAMEPURCHASE:UramPlatform={purchaseAddress}/");
            arguments.Add($"INGAMEPURCHASE:UramCert={PurchaseCertificateFileName}");
        }

        return Results.Ok(new
        {
            gateway = local && !string.IsNullOrWhiteSpace(options.LocalGatewayAddress) ? options.LocalGatewayAddress : options.GatewayAddress,
            login = local && !string.IsNullOrWhiteSpace(options.LocalLoginAddress) ? options.LocalLoginAddress : options.LoginAddress,
            arguments,
            clientManifestUrl = options.ClientManifestUrl,
            clientBaseUrl = options.ClientBaseUrl,
            purchaseCertificate
        });
    }

    private static bool TryReadPurchaseCertificate(PluginOptions options, out string? certificate)
    {
        certificate = null;

        if (string.IsNullOrWhiteSpace(options.PurchaseCertificateFile) || !File.Exists(options.PurchaseCertificateFile))
            return false;

        var text = File.ReadAllText(options.PurchaseCertificateFile);

        if (!text.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal) || text.Contains("PRIVATE KEY", StringComparison.Ordinal))
            return false;

        certificate = text;
        return true;
    }

    private static bool IsSameNetwork(HttpContext httpContext, PluginOptions options)
    {
        var remote = httpContext.Connection.RemoteIpAddress;

        if (remote is null)
            return false;

        if (remote.IsIPv4MappedToIPv6)
            remote = remote.MapToIPv4();

        if (IPAddress.IsLoopback(remote) && httpContext.Request.Headers.TryGetValue("CF-Connecting-IP", out var forwarded))
        {
            if (!IPAddress.TryParse(forwarded.ToString(), out var client))
                return false;

            remote = client.IsIPv4MappedToIPv6 ? client.MapToIPv4() : client;
        }

        if (IsPrivate(remote))
            return true;

        if (remote.AddressFamily == AddressFamily.InterNetworkV6)
            return SharesLocalIPv6Prefix(remote);

        var publicHost = options.GatewayAddress.Split(':')[0];

        return IPAddress.TryParse(publicHost, out var publicAddress) && publicAddress.Equals(remote);
    }

    private static bool SharesLocalIPv6Prefix(IPAddress remote)
    {
        var remoteBytes = remote.GetAddressBytes();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
                continue;

            foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;

                if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.IsIPv6LinkLocal || IPAddress.IsLoopback(address))
                    continue;

                if (address.GetAddressBytes().AsSpan(0, 8).SequenceEqual(remoteBytes.AsSpan(0, 8)))
                    return true;
            }
        }

        return false;
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal;

        var bytes = address.GetAddressBytes();

        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
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
