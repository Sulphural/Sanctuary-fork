using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class EntityWorkQueueTests
{
    private const int Producers = 8;
    private const int ItemsPerProducer = 5_000;

    [TestMethod]
    public void Post_RunsEveryItemInOrderOneAtATimeOnTheEntitysZone_WhileItMovesBetweenZones()
    {
        var zones = new[] { new TestZone(), new TestZone() };
        var entity = new TestEntity(zones[0]);

        var result = Run(entity, item =>
        {
            if (item % 97 == 0)
                entity.Zone = entity.Zone == zones[0].Proxy ? zones[1].Proxy : zones[0].Proxy;
        });

        foreach (var zone in zones)
            zone.Close();

        result.AssertValid();
    }

    [TestMethod]
    public void Post_ForwardsQueuedWork_WhenTheOldZoneClosesAfterTheEntityLeaves()
    {
        var zones = new ConcurrentBag<TestZone>();
        var first = new TestZone();
        zones.Add(first);

        var entity = new TestEntity(first);

        var result = Run(entity, item =>
        {
            if (item % 251 != 0)
                return;

            var oldZone = TestZone.Current!;
            var newZone = new TestZone();
            zones.Add(newZone);

            entity.Zone = newZone.Proxy;
            oldZone.Close();
        });

        foreach (var zone in zones)
            zone.Close();

        result.AssertValid();
    }

    private static RunResult Run(TestEntity entity, Action<int> onItem)
    {
        var result = new RunResult();
        var queue = new EntityWorkQueue(() => entity.Zone);
        var running = 0;
        var nextItem = 0;
        var lastSeen = new int[Producers];

        Array.Fill(lastSeen, -1);

        var producers = Enumerable.Range(0, Producers).Select(producer => new Thread(() =>
        {
            for (var sequence = 0; sequence < ItemsPerProducer; sequence++)
            {
                var itemSequence = sequence;

                queue.Post(() =>
                {
                    if (Interlocked.Increment(ref running) != 1)
                        result.Fail("Two items ran at the same time.");

                    if (!entity.Zone.IsTickThread)
                        result.Fail("An item ran off the entity's zone loop.");

                    if (itemSequence <= lastSeen[producer])
                        result.Fail($"Producer {producer} ran item {itemSequence} after {lastSeen[producer]}.");

                    lastSeen[producer] = itemSequence;

                    if (Random.Shared.Next(8) == 0)
                        Thread.Yield();

                    onItem(Interlocked.Increment(ref nextItem));

                    Interlocked.Decrement(ref running);
                    Interlocked.Increment(ref result.Completed);
                });

                if (itemSequence % 64 == 0)
                    Thread.Yield();
            }
        })).ToList();

        producers.ForEach(x => x.Start());
        producers.ForEach(x => x.Join());

        SpinWait.SpinUntil(() => Volatile.Read(ref result.Completed) == Producers * ItemsPerProducer, TimeSpan.FromSeconds(30));

        return result;
    }

    private sealed class RunResult
    {
        public int Completed;

        private readonly ConcurrentQueue<string> _failures = new();

        public void Fail(string message) => _failures.Enqueue(message);

        public void AssertValid()
        {
            Assert.AreEqual(0, _failures.Count, string.Join(Environment.NewLine, _failures.Take(10)));
            Assert.AreEqual(Producers * ItemsPerProducer, Volatile.Read(ref Completed));
        }
    }

    private sealed class TestZone
    {
        [ThreadStatic]
        private static TestZone? _current;

        private readonly BlockingCollection<Action> _work = new();
        private readonly object _closeLock = new();
        private bool _closed;

        public TestZone()
        {
            Proxy = ZoneProxy.Create(this);

            new Thread(() =>
            {
                _current = this;

                foreach (var work in _work.GetConsumingEnumerable())
                    work();
            })
            {
                IsBackground = true
            }.Start();
        }

        public static TestZone? Current => _current;

        public IZone Proxy { get; }

        public bool IsTickThread => _current == this;

        public bool TryPost(Action work)
        {
            if (IsTickThread)
            {
                work();
                return true;
            }

            lock (_closeLock)
            {
                if (_closed)
                    return false;

                _work.Add(work);
                return true;
            }
        }

        public void Close()
        {
            lock (_closeLock)
            {
                if (_closed)
                    return;

                _closed = true;
                _work.CompleteAdding();
            }
        }
    }

    private sealed class TestEntity
    {
        private IZone _zone;

        public TestEntity(TestZone zone)
        {
            _zone = zone.Proxy;
        }

        public IZone Zone
        {
            get => Volatile.Read(ref _zone);
            set => Volatile.Write(ref _zone, value);
        }
    }

    public class ZoneProxy : DispatchProxy
    {
        private object _zone = null!;

        internal static IZone Create(object zone)
        {
            var proxy = DispatchProxy.Create<IZone, ZoneProxy>();
            ((ZoneProxy)(object)proxy)._zone = zone;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var zone = (TestZone)_zone;

            if (targetMethod!.Name == "Post")
            {
                zone.TryPost((Action)args![0]!);
                return null;
            }

            return targetMethod.Name switch
            {
                "get_IsTickThread" => zone.IsTickThread,
                "TryPost" => zone.TryPost((Action)args![0]!),
                "get_Logger" => NullLogger.Instance,
                "get_Name" => nameof(TestZone),
                "get_Id" => 0,
                _ => throw new NotSupportedException(targetMethod.Name)
            };
        }
    }
}
