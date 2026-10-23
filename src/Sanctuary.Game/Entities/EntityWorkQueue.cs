using System;
using System.Collections.Concurrent;
using System.Threading;

using Microsoft.Extensions.Logging;

using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Entities;

public sealed class EntityWorkQueue
{
    private readonly Func<IZone> _zone;
    private readonly ConcurrentQueue<Action> _work = new();
    private int _scheduled;

    public EntityWorkQueue(Func<IZone> zone)
    {
        _zone = zone;
    }

    public void Post(Action work)
    {
        _work.Enqueue(work);

        if (Interlocked.Exchange(ref _scheduled, 1) == 0)
            Schedule();
    }

    private void Schedule()
    {
        while (true)
        {
            var zone = _zone();

            if (zone.TryPost(Run))
                return;

            if (_zone() == zone)
            {
                _work.Clear();
                Volatile.Write(ref _scheduled, 0);
                return;
            }
        }
    }

    private void Run()
    {
        var zone = _zone();

        if (!zone.IsTickThread)
        {
            Schedule();
            return;
        }

        var count = _work.Count;

        while (count-- > 0 && _work.TryDequeue(out var work))
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                zone.Logger.LogError(ex, "{name} ({id}) - Queued entity work failed.", zone.Name, zone.Id);
            }

            if (_zone() != zone)
            {
                Schedule();
                return;
            }
        }

        Volatile.Write(ref _scheduled, 0);

        if (!_work.IsEmpty && Interlocked.Exchange(ref _scheduled, 1) == 0)
            Schedule();
    }
}
