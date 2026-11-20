using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sanctuary.Plugin;

public sealed class StatusServer : IDisposable
{
    public const int Port = 20055;

    private readonly string _allowedOrigin;
    private readonly PluginStatus _status = new();
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cancellation = new();
    private TcpListener? _listener;

    public StatusServer(string allowedOrigin)
    {
        _allowedOrigin = allowedOrigin;
    }

    public bool TryStart()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            _ = AcceptLoopAsync(_cancellation.Token);
            return true;
        }
        catch (SocketException)
        {
            _listener = null;
            return false;
        }
    }

    public void Set(string state, string message, double progress = 0)
    {
        lock (_lock)
        {
            _status.State = state;
            _status.Message = message;
            _status.Progress = Math.Clamp(progress, 0, 1);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            _ = HandleAsync(client, cancellationToken);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;

        try
        {
            client.ReceiveTimeout = 5000;
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);

            var requestLine = await reader.ReadLineAsync(cancellationToken) ?? "";
            string? origin = null;
            string? line;

            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellationToken)))
            {
                if (line.StartsWith("Origin:", StringComparison.OrdinalIgnoreCase))
                    origin = line["Origin:".Length..].Trim();
            }

            var parts = requestLine.Split(' ');
            var method = parts.Length > 0 ? parts[0] : "";
            var path = parts.Length > 1 ? parts[1] : "";
            var originAllowed = origin is null || string.Equals(origin, _allowedOrigin, StringComparison.OrdinalIgnoreCase);

            string status;
            string body;

            if (!originAllowed)
            {
                status = "403 Forbidden";
                body = "";
            }
            else if (method == "OPTIONS")
            {
                status = "204 No Content";
                body = "";
            }
            else if (method == "GET" && (path == "/status" || path.StartsWith("/status?", StringComparison.Ordinal)))
            {
                status = "200 OK";
                lock (_lock)
                    body = JsonSerializer.Serialize(_status, PluginJsonContext.Default.PluginStatus);
            }
            else
            {
                status = "404 Not Found";
                body = "";
            }

            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var headers = new StringBuilder()
                .Append($"HTTP/1.1 {status}\r\n")
                .Append("Content-Type: application/json\r\n")
                .Append("Cache-Control: no-store\r\n")
                .Append($"Content-Length: {bodyBytes.Length}\r\n")
                .Append("Connection: close\r\n");

            if (originAllowed && origin is not null)
            {
                headers.Append($"Access-Control-Allow-Origin: {origin}\r\n")
                    .Append("Vary: Origin\r\n")
                    .Append("Access-Control-Allow-Methods: GET, OPTIONS\r\n")
                    .Append("Access-Control-Allow-Private-Network: true\r\n");
            }

            headers.Append("\r\n");

            await stream.WriteAsync(Encoding.ASCII.GetBytes(headers.ToString()), cancellationToken);
            await stream.WriteAsync(bodyBytes, cancellationToken);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _listener?.Stop();
        _cancellation.Dispose();
    }
}
