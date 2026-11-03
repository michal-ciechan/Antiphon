using System.Collections.Concurrent;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>The background clone pool: fills to capacity, refills on take, stops cleanly. No database.</summary>
[Category("Unit")]
public sealed class TestDbClonePoolTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Bound;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("condition not reached");
            await Task.Delay(10);
        }
    }

    [Test]
    public async Task Fills_to_capacity_and_refills_what_is_taken()
    {
        var cloned = new ConcurrentBag<string>();
        var pool = TestDbClonePool.Start(3, name => { cloned.Add(name); return Task.CompletedTask; });

        await WaitUntil(() => pool.Depth == 3);
        await Task.Delay(50);
        cloned.Count.ShouldBe(3, "no clone beyond capacity");

        pool.TryTake(out var first).ShouldBeTrue();
        cloned.ShouldContain(first);
        await WaitUntil(() => pool.Filled == 4 && pool.Depth == 3);

        await pool.StopAsync();
    }

    [Test]
    public async Task Workers_fill_in_parallel_without_overshooting_capacity()
    {
        var running = 0;
        var peak = 0;
        var cloned = 0;
        var pool = TestDbClonePool.Start(6, async _ =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            await Task.Delay(50);
            Interlocked.Decrement(ref running);
            Interlocked.Increment(ref cloned);
        }, workers: 3);

        await WaitUntil(() => pool.Depth == 6);
        await Task.Delay(100);

        Volatile.Read(ref cloned).ShouldBe(6, "in-flight clones count toward capacity");
        Volatile.Read(ref peak).ShouldBe(3);

        for (var i = 0; i < 3; i++)
            pool.TryTake(out _).ShouldBeTrue();
        await WaitUntil(() => pool.Depth == 6);
        Volatile.Read(ref cloned).ShouldBe(9);

        await pool.StopAsync();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target))
               && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    [Test]
    public async Task Hands_out_each_database_once()
    {
        var pool = TestDbClonePool.Start(4, _ => Task.CompletedTask);
        await WaitUntil(() => pool.Depth == 4);

        var taken = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            pool.TryTake(out var name).ShouldBeTrue();
            taken.Add(name);
        }

        taken.Distinct().Count().ShouldBe(4);
        await pool.StopAsync();
    }

    [Test]
    public async Task Empty_pool_is_a_miss_not_a_wait()
    {
        var gate = new TaskCompletionSource();
        var pool = TestDbClonePool.Start(2, _ => gate.Task);

        pool.TryTake(out _).ShouldBeFalse();

        gate.SetResult();
        await pool.StopAsync();
    }

    [Test]
    public async Task Failing_clones_do_not_spin_and_stop_returns()
    {
        var attempts = 0;
        var pool = TestDbClonePool.Start(2, _ =>
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("clone broken");
        });

        await Task.Delay(300);
        await pool.StopAsync().WaitAsync(Bound);

        Volatile.Read(ref attempts).ShouldBeLessThanOrEqualTo(2, "a failure backs off for a second");
        pool.Depth.ShouldBe(0);
    }

    [Test]
    public void Capacity_comes_from_the_environment()
    {
        var prior = Environment.GetEnvironmentVariable(TestDbClonePool.EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(TestDbClonePool.EnvVar, null);
            TestDbClonePool.CapacityFromEnvironment().ShouldBe(TestDbClonePool.DefaultCapacity);
            Environment.SetEnvironmentVariable(TestDbClonePool.EnvVar, "off");
            TestDbClonePool.CapacityFromEnvironment().ShouldBe(0);
            Environment.SetEnvironmentVariable(TestDbClonePool.EnvVar, "4");
            TestDbClonePool.CapacityFromEnvironment().ShouldBe(4);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestDbClonePool.EnvVar, prior);
        }
    }

    [Test]
    public async Task Lifecycle_serves_isolated_databases_from_the_pool()
    {
        var ops = new ControlledTestDbOperations();
        var lifecycle = new TestDbFixtureLifecycle(ops, poolCapacity: 2);

        await lifecycle.EnsureReadyAsync().WaitAsync(Bound);
        await WaitUntil(() => lifecycle.Pool!.Depth == 2);

        await using (await lifecycle.CreateIsolatedSchemaAsync())
        {
        }
        await using (await lifecycle.CreateIsolatedSchemaAsync())
        {
        }

        lifecycle.PoolHit.ShouldBe(2);
        lifecycle.PoolMiss.ShouldBe(0);
        await lifecycle.DisposeAsync().WaitAsync(Bound);
    }

    [Test]
    public async Task Injected_operations_run_without_a_pool_by_default()
    {
        var lifecycle = new TestDbFixtureLifecycle(new ControlledTestDbOperations());

        await lifecycle.EnsureReadyAsync().WaitAsync(Bound);
        await using (await lifecycle.CreateIsolatedSchemaAsync())
        {
        }

        lifecycle.Pool.ShouldBeNull();
        lifecycle.PoolHit.ShouldBe(0);
        await lifecycle.DisposeAsync().WaitAsync(Bound);
    }
}
