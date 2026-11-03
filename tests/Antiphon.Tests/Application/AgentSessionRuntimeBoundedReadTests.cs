using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
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

    /// <summary>
    /// CARD-1679 D-2 (W1): a session bound to another runner reads live or unknown from that runner's
    /// phone-home inventory alone. The local runner's List cannot name it, so a local List that is
    /// canceled, refused or past its deadline must neither be called nor fail the answer.
    /// </summary>
    [Test]
    [Arguments("canceled")]
    [Arguments("http")]
    [Arguments("deadline")]
    public async Task C1679_Remote_bound_liveness_ignores_a_failing_local_list(string fault)
    {
        var live = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        var absent = Guid.NewGuid();
        var runner = new BlockingSessionRunnerClient();
        Fault(runner, fault);
        var time = new FakeTimeProvider(Start);
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var runtime = NewRuntime(runner, provider, time, new RemoteInventoryDirectory([live], [unknown]));

        (await AnswerAsync(() => runtime.IsLiveOrUnknownAsync(Bound(live, RemoteRunner), CancellationToken.None), runner, time))
            .ShouldBeTrue("a live server2 session reads live from its inventory");
        (await AnswerAsync(() => runtime.IsLiveOrUnknownAsync(unknown, RemoteRunner, CancellationToken.None), runner, time))
            .ShouldBeTrue("an unknown server2 session is never read as gone");
        (await AnswerAsync(() => runtime.IsLiveOrUnknownAsync(Bound(absent, RemoteRunner), CancellationToken.None), runner, time))
            .ShouldBeFalse("a server2 session its inventory does not name is not live");
        Volatile.Read(ref runner.ListCalls).ShouldBe(0, "no local List for a session bound to another runner");
    }

    /// <summary>CARD-1679 control (CARD-1362 D-3): a locally bound session still fails on a failed local List.</summary>
    [Test]
    [Arguments("canceled", null)]
    [Arguments("http", null)]
    [Arguments("deadline", null)]
    [Arguments("canceled", PhoneHomeProtocol.LocalRunnerId)]
    [Arguments("deadline", PhoneHomeProtocol.LocalRunnerId)]
    public async Task C1679_Local_bound_liveness_still_propagates_a_list_failure(string fault, string? runnerId)
    {
        var live = Guid.NewGuid();
        var runner = new BlockingSessionRunnerClient();
        Fault(runner, fault);
        var time = new FakeTimeProvider(Start);
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var runtime = NewRuntime(runner, provider, time, new RemoteInventoryDirectory([live], []));

        var failure = await Should.ThrowAsync<Exception>(
            () => AnswerAsync(() => runtime.IsLiveOrUnknownAsync(Bound(live, runnerId), CancellationToken.None), runner, time));
        if (fault == "http")
            failure.ShouldBeOfType<HttpRequestException>();
        else
            failure.ShouldBeAssignableTo<OperationCanceledException>();
        Volatile.Read(ref runner.ListCalls).ShouldBe(1);
    }

    private const string RemoteRunner = "server2";

    private static AgentSession Bound(Guid id, string? runnerId) =>
        new() { Id = id, RunnerId = runnerId, Status = SessionStatus.Running };

    private static void Fault(BlockingSessionRunnerClient runner, string fault)
    {
        if (fault == "canceled")
            runner.FaultAll(new TaskCanceledException("C1679 local list canceled"));
        else if (fault == "http")
            runner.FaultAll(new HttpRequestException("C1679 local runner refused"));
        else if (fault != "deadline")
            throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
    }

    /// <summary>A blocked local List is released by advancing the injected clock past the 5 s deadline.</summary>
    private static async Task<bool> AnswerAsync(Func<Task<bool>> ask, BlockingSessionRunnerClient runner, FakeTimeProvider time)
    {
        var calls = Volatile.Read(ref runner.ListCalls);
        var answer = ask();
        await WaitUntilAsync(() => answer.IsCompleted || Volatile.Read(ref runner.ListCalls) > calls);
        if (!answer.IsCompleted)
            time.Advance(TimeSpan.FromMilliseconds(5000));
        return await answer.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class RemoteInventoryDirectory(Guid[] live, Guid[] unknown) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => throw new NotSupportedException();
        public ISessionRunnerClient Resolve(string? runnerId) => throw new NotSupportedException();
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            throw new NotSupportedException();
        public IReadOnlyList<string> KnownRunnerIds { get; } = [PhoneHomeProtocol.LocalRunnerId, RemoteRunner];
        public Guid? GetLiveStoreId(string? runnerId) => null;
        public IReadOnlyCollection<Guid> LiveRemoteSessionIds() => live;
        public IReadOnlyCollection<Guid> UnknownRemoteSessionIds() => unknown;
        public bool RemoteInventoryPending(string? runnerId) => false;
    }

    private static AgentSessionRuntime NewRuntime(
        BlockingSessionRunnerClient runner, ServiceProvider provider, FakeTimeProvider time,
        ISessionRunnerDirectory? directory = null)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-c1362-{Guid.NewGuid():N}");
        return new AgentSessionRuntime(
            runner, new MockEventBus(),
            Options.Create(new AgentSessionSettings { SessionLogPath = Path.Combine(tempRoot, "session-logs") }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            time, NullLogger<AgentSessionRuntime>.Instance, directory: directory);
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
