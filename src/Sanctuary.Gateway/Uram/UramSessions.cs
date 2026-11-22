using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Sanctuary.Gateway.Uram;

public static class UramSessions
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private static readonly ConcurrentDictionary<string, (GatewayConnection Connection, DateTimeOffset Created)> Sessions = new();

    public static string Create(GatewayConnection connection)
    {
        foreach (var (token, session) in Sessions)
        {
            if (DateTimeOffset.UtcNow - session.Created > Lifetime || ReferenceEquals(session.Connection, connection))
                Sessions.TryRemove(token, out _);
        }

        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        Sessions[id] = (connection, DateTimeOffset.UtcNow);
        return id;
    }

    public static GatewayConnection? Find(string? id)
    {
        if (string.IsNullOrEmpty(id) || !Sessions.TryGetValue(id, out var session))
            return null;

        if (DateTimeOffset.UtcNow - session.Created > Lifetime)
        {
            Sessions.TryRemove(id, out _);
            return null;
        }

        return session.Connection;
    }
}
