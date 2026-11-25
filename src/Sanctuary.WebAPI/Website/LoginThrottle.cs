using System;
using System.Collections.Concurrent;

namespace Sanctuary.WebAPI.Website;

public static class LoginThrottle
{
    private const int MaxFailures = 5;
    private const int MaxEntries = 50_000;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset WindowStart;
        public DateTimeOffset? BlockedUntil;
    }

    public static bool IsBlocked(string username)
    {
        if (!Entries.TryGetValue(Key(username), out var entry))
            return false;

        lock (entry)
            return entry.BlockedUntil is { } until && until > DateTimeOffset.UtcNow;
    }

    public static void RecordFailure(string username)
    {
        var now = DateTimeOffset.UtcNow;

        if (Entries.Count >= MaxEntries)
            Prune(now);

        var entry = Entries.GetOrAdd(Key(username), _ => new Entry { WindowStart = now });

        lock (entry)
        {
            if (now - entry.WindowStart > Window)
            {
                entry.Failures = 0;
                entry.WindowStart = now;
            }

            entry.Failures++;

            if (entry.Failures >= MaxFailures)
                entry.BlockedUntil = now + Window;
        }
    }

    public static void RecordSuccess(string username)
    {
        Entries.TryRemove(Key(username), out _);
    }

    private static string Key(string username) => username.Trim();

    private static void Prune(DateTimeOffset now)
    {
        foreach (var (key, entry) in Entries)
        {
            lock (entry)
            {
                if (now - entry.WindowStart > Window && (entry.BlockedUntil is null || entry.BlockedUntil < now))
                    Entries.TryRemove(key, out _);
            }
        }
    }
}
