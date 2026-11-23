using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using Sanctuary.WebAPI.Options;

namespace Sanctuary.WebAPI.Endpoints;

public static class ServerStatusEndpoints
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(15);
    private static readonly SemaphoreSlim Lock = new(1, 1);

    private static ServerStatus? _cached;
    private static DateTimeOffset _cachedAt;

    private sealed record ServerStatus(bool Online, bool Locked, int Players);

    public static void MapServerStatusEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/server/status", GetStatusAsync);
    }

    private static async Task<IResult> GetStatusAsync(IOptionsMonitor<PluginOptions> options, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var status = await GetCachedStatusAsync(options.CurrentValue, cancellationToken);

        httpContext.Response.Headers.CacheControl = "public, max-age=15";

        return Results.Json(new { online = status.Online, locked = status.Locked, players = status.Players });
    }

    private static async Task<ServerStatus> GetCachedStatusAsync(PluginOptions options, CancellationToken cancellationToken)
    {
        if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
            return _cached;

        await Lock.WaitAsync(cancellationToken);

        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
                return _cached;

            var address = !string.IsNullOrWhiteSpace(options.LocalLoginAddress) ? options.LocalLoginAddress : options.LoginAddress;

            _cached = await QueryAsync(address, cancellationToken);
            _cachedAt = DateTimeOffset.UtcNow;

            return _cached;
        }
        finally
        {
            Lock.Release();
        }
    }

    private static async Task<ServerStatus> QueryAsync(string address, CancellationToken cancellationToken)
    {
        var offline = new ServerStatus(false, false, 0);

        var separator = address.LastIndexOf(':');

        if (separator <= 0 || !int.TryParse(address.AsSpan(separator + 1), out var port))
            return offline;

        var host = address[..separator];

        try
        {
            if (!IPAddress.TryParse(host, out var ip))
            {
                var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken);

                if (addresses.Length == 0)
                    return offline;

                ip = addresses[0];
            }

            using var udp = new UdpClient(ip.AddressFamily);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            timeout.CancelAfter(TimeSpan.FromSeconds(2));

            var endPoint = new IPEndPoint(ip, port);

            await udp.SendAsync(new byte[] { 0, 32 }, endPoint, timeout.Token);

            var result = await udp.ReceiveAsync(timeout.Token);
            var buffer = result.Buffer;

            if (buffer.Length < 6)
                return offline;

            return new ServerStatus(buffer[0] != 0, buffer[1] != 0, Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(2))));
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return offline;
        }
    }
}
