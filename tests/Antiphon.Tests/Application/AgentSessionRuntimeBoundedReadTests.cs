using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1362 S1 witnesses. V-1: a runner that never answers the List does not hold the only
/// continuation thread while a watchdog scan awaits it. V-2a/V-2b: the runtime's live-read
/// deadline runs on the injected clock and keeps each read's outcome class (the list family
/// throws, <c>TryGet*</c> reads as unavailable, caller cancellation rethrows). No store, no
/// process: the <see cref="AppDbContext"/> below is never opened because an empty live list
/// returns before the first query.
/// </summary>
[Category("Unit")]
public sealed class AgentSessionRuntimeBoundedReadTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task C1362_Blocked_runner_list_does_not_hold_the_pump_during_a_watchdog_scan()
    {
        var runner = new BlockingSessionRunnerClient();
        var time = new FakeTimeProvider(Start);
        var bus = new MockEventBus();
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-c1362-{Guid.NewGuid():N}");
        var runtime = new AgentSessionRuntime(
            runner, bus,
            Options.Create(new AgentSessionSettings { SessionLogPath = Path.Combine(tempRoot, "session-logs") }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            time, NullLogger<AgentSessionRuntime>.Instance);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=c1362_never_opened;Username=c1362;Password=c1362").Options);
        var service = new WatchdogService(
            db, runtime, new WatchdogMatcher(), new WatchdogCooldownStore(), bus,
            Options.Create(new WatchdogSettings()), time, NullLogger<WatchdogService>.Instance);

        await using var pump = new SingleThreadContinuationPump();
        var scan = pump.InvokeAsync(() => service.ScanAsync(CancellationToken.None));
        try
        {
            await WaitUntilAsync(() => Volatile.Read(ref runner.ListCalls) == 1);
            var probe = pump.PostProbeAsync();
            var first = await Task.WhenAny(probe, Task.Delay(TimeSpan.FromSeconds(2)));
            first.ShouldBe(probe, "an unrelated continuation must run on the pump while the runner list is still unanswered");
        }
        finally
        {
            runner.ReleaseList([]);
        }
        (await scan.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe(0);
    }

    [Test]
    public async Task C1362_List_deadline_throws_on_the_injected_clock()
    {
        var runner = new BlockingSessionRunnerClient();
        var time = new FakeTimeProvider(Start);
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var runtime = NewRuntime(runner, provider, time);
        using var cts = new CancellationTokenSource();

        var task = runtime.ListLiveSessionsAsync(cts.Token);
        await WaitUntilAsync(() => Volatile.Read(ref runner.ListCalls) == 1);

        time.Advance(TimeSpan.FromMilliseconds(4999));
        task.IsCompleted.ShouldBeFalse("the deadline is 5 s on the injected clock; 4999 ms must not fire it");

        time.Advance(TimeSpan.FromMilliseconds(1));
        await Should.ThrowAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
        cts.IsCancellationRequested.ShouldBeFalse("the deadline must not cancel the caller's token");
        runner.LastListToken.IsCancellationRequested.ShouldBeTrue("the token handed to the runner carries the deadline");
    }

    [Test]
    public async Task C1362_Snapshot_deadline_reads_as_unavailable()
    {
        var sessionId = Guid.NewGuid();

        // Part one: the deadline alone reads as "unavailable" (null), never as a thrown error.
        var runner = new BlockingSessionRunnerClient();
        var time = new FakeTimeProvider(Start);
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var runtime = NewRuntime(runner, provider, time);

        var task = runtime.TryGetLiveSnapshotAsync(sessionId, CancellationToken.None);
        await WaitUntilAsync(() => Volatile.Read(ref runner.SnapshotCalls) == 1);
        time.Advance(TimeSpan.FromMilliseconds(5000));
        (await task.WaitAsync(TimeSpan.FromSeconds(2))).ShouldBeNull();
        runner.LastSnapshotToken.IsCancellationRequested.ShouldBeTrue("the token handed to the runner carries the deadline");

        // Part two: a cancelled caller token rethrows instead of reading as unavailable.
        var runner2 = new BlockingSessionRunnerClient();
        var time2 = new FakeTimeProvider(Start);
        await using var provider2 = new ServiceCollection().BuildServiceProvider();
        var runtime2 = NewRuntime(runner2, provider2, time2);
        using var cts = new CancellationTokenSource();

        var t2 = runtime2.TryGetLiveSnapshotAsync(sessionId, cts.Token);
        await WaitUntilAsync(() => Volatile.Read(ref runner2.SnapshotCalls) == 1);
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => t2.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private static AgentSessionRuntime NewRuntime(
        BlockingSessionRunnerClient runner, ServiceProvider provider, FakeTimeProvider time)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-c1362-{Guid.NewGuid():N}");
        return new AgentSessionRuntime(
            runner, new MockEventBus(),
            Options.Create(new AgentSessionSettings { SessionLogPath = Path.Combine(tempRoot, "session-logs") }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            time, NullLogger<AgentSessionRuntime>.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!predicate())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("condition not met within 10 s");
            await Task.Delay(10);
        }
    }
}
