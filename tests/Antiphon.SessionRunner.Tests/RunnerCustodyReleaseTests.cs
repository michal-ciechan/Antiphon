using Antiphon.Agents.Pty;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1614 portable release witnesses. Clock, recovery and poll-stop.
/// No cgroup and no installed helper. A kill is counted only on the injected boundary.
/// </summary>
[Category("Unit")]
public sealed class RunnerCustodyReleaseTests
{
    [Test]
    public async Task C1614_Draining_age_reports_without_releasing()
    {
        await using var world = await ReleaseWorld.Open();
        world.Session!.CustodyObserveOverride = (_, _) => Task.FromResult(world.Draining());
        var first = await world.Runtime.ReadCustodyAsync(world.Binding, seal: false, CancellationToken.None);
        first.State.ShouldBe(VerificationCustodyState.Draining);
        first.Receipt.ShouldBeNull();
        world.Logger.Text.Contains("custody-unresolved", StringComparison.Ordinal).ShouldBeFalse();

        world.Clock.Advance(TimeSpan.FromMinutes(29));
        await world.Runtime.ReadCustodyAsync(world.Binding, seal: false, CancellationToken.None);
        world.Logger.Text.Contains("custody-unresolved", StringComparison.Ordinal).ShouldBeFalse();

        world.Clock.Advance(TimeSpan.FromMinutes(2));
        await world.Runtime.ReadCustodyAsync(world.Binding, seal: false, CancellationToken.None);
        world.Logger.Text.Contains("custody-unresolved", StringComparison.Ordinal).ShouldBeTrue();
        world.Logger.Text.Contains(world.Binding.ExecutionId.ToString("D"), StringComparison.Ordinal).ShouldBeTrue();
        world.Logger.Text.Contains(world.ContainerId.ToString("D"), StringComparison.Ordinal).ShouldBeTrue();
        world.Recovery.Kills.ShouldBeEmpty();
        File.Exists(world.Store.PathFor(world.Binding.ExecutionId, "runner-seal.json")).ShouldBeFalse();
        File.Exists(world.Store.PathFor(world.Binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
    }

    [Test]
    public async Task C1614_Recovery_refuses_unbound_or_live_host()
    {
        await using (var ordinary = await ReleaseWorld.Open(adopt: false))
        {
            var status = await ordinary.Runtime.ReadCustodyAsync(ordinary.Binding, seal: false, CancellationToken.None);
            ordinary.Recovery.Kills.ShouldBeEmpty();
            status.Receipt.ShouldBeNull();
            File.Exists(ordinary.Store.PathFor(ordinary.Binding.ExecutionId, "runner-seal.json")).ShouldBeFalse();
        }

        await using (var container = await ReleaseWorld.Open(adopt: false, namespaceContainer: Guid.NewGuid()))
        {
            await container.Runtime.ReadCustodyAsync(container.Binding, seal: true, CancellationToken.None);
            container.Recovery.Kills.ShouldBeEmpty();
        }

        await using (var ns = await ReleaseWorld.Open(adopt: false))
        {
            ns.Recovery.NamespaceId = Guid.NewGuid();
            await ns.Runtime.ReadCustodyAsync(ns.Binding, seal: true, CancellationToken.None);
            ns.Recovery.Kills.ShouldBeEmpty();
        }

        await using (var store = await ReleaseWorld.Open(adopt: false))
        {
            var foreign = store.Binding with { RunnerStoreId = Guid.NewGuid() };
            await Should.ThrowAsync<VerificationCustodyException>(() =>
                store.Runtime.ReadCustodyAsync(foreign, seal: true, CancellationToken.None));
            store.Recovery.Kills.ShouldBeEmpty();
        }

        await using (var live = await ReleaseWorld.Open(adopt: false))
        {
            live.Recovery.Gone = false;
            var status = await live.Runtime.ReadCustodyAsync(live.Binding, seal: true, CancellationToken.None);
            live.Recovery.Kills.ShouldBeEmpty();
            status.State.ShouldBe(VerificationCustodyState.Unknown);
            status.Receipt.ShouldBeNull();
        }

        await using var dead = await ReleaseWorld.Open(adopt: false);
        dead.Recovery.Pids = [];
        var released = await dead.Runtime.ReadCustodyAsync(dead.Binding, seal: true, CancellationToken.None);
        dead.Recovery.Kills.ShouldBe([dead.ContainerId]);
        released.State.ShouldBe(VerificationCustodyState.Unknown);
        released.Reason.ShouldBe("released_unknown");
        released.Receipt.ShouldBeNull();
        File.Exists(dead.Store.PathFor(dead.Binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
        File.Exists(dead.Store.PathFor(dead.Binding.ExecutionId, "accepted-receipt.json")).ShouldBeFalse();

        await using var still = await ReleaseWorld.Open(adopt: false);
        still.Recovery.Gone = false;
        var present = await still.Runtime.ReadCustodyAsync(still.Binding, seal: true, CancellationToken.None);
        still.Recovery.Kills.ShouldBeEmpty();
        present.Reason.ShouldBe("custody_host_still_present");
    }

    [Test]
    public async Task C1614_Dead_host_release_is_exact_and_idempotent()
    {
        await using var world = await ReleaseWorld.Open(adopt: false);
        world.Recovery.Pids = [];
        var first = await world.Runtime.ReadCustodyAsync(world.Binding, seal: true, CancellationToken.None);
        var second = await world.Runtime.ReadCustodyAsync(world.Binding, seal: true, CancellationToken.None);
        world.Recovery.Kills.ShouldBe([world.ContainerId, world.ContainerId]);
        first.State.ShouldBe(VerificationCustodyState.Unknown);
        second.State.ShouldBe(VerificationCustodyState.Unknown);
        first.Receipt.ShouldBeNull();
        second.Receipt.ShouldBeNull();
        File.Exists(world.Store.PathFor(world.Binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
    }

    [Test]
    public async Task C1614_Released_unknown_stops_polling()
    {
        await using var absent = await ReleaseWorld.Open();
        absent.Session!.CustodyObserveOverride = (_, _) => Task.FromResult(absent.Draining());
        absent.Store.WriteRecord(absent.Binding, "runner-seal.json", new CustodyStamp(3, DateTime.UtcNow));
        var absentLoop = absent.Session!.StartCustodyObserverForTests();
        await Task.Delay(300);
        absentLoop.IsCompleted.ShouldBeFalse();

        await using var mismatch = await ReleaseWorld.Open();
        mismatch.Session!.CustodyObserveOverride = (_, _) => Task.FromResult(mismatch.Draining());
        mismatch.WriteDisposition(Guid.NewGuid());
        var mismatchLoop = mismatch.Session!.StartCustodyObserverForTests();
        await Task.Delay(300);
        mismatchLoop.IsCompleted.ShouldBeFalse();

        await using var match = await ReleaseWorld.Open();
        match.Session!.CustodyObserveOverride = (_, _) => Task.FromResult(match.Draining());
        match.WriteDisposition(match.ContainerId);
        var matchLoop = match.Session!.StartCustodyObserverForTests();
        var finished = await Task.WhenAny(matchLoop, Task.Delay(1000));
        finished.ShouldBe(matchLoop);
        File.Exists(match.Store.PathFor(match.Binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
        File.Exists(match.Store.PathFor(match.Binding.ExecutionId, "accepted-receipt.json")).ShouldBeFalse();
        absent.Recovery.Kills.ShouldBeEmpty();
        mismatch.Recovery.Kills.ShouldBeEmpty();
        match.Recovery.Kills.ShouldBeEmpty();
    }

    /// <summary>
    /// CARD-1614 round 2. An adopted session with no client and a prior runner seal is the
    /// state after a runner restart. Ordinary reads and the observer must not call the kill
    /// helper. One explicit seal request does, once.
    /// </summary>
    [Test]
    public async Task C1614_Prior_seal_does_not_recover_on_ordinary_read_or_observer()
    {
        await using var world = await ReleaseWorld.Open(adopt: true);
        world.Store.WriteRecord(world.Binding, "runner-seal.json", new CustodyStamp(3, DateTime.UtcNow));
        world.Recovery.Pids = [];

        for (var i = 0; i < 3; i++)
        {
            var ordinary = await world.Runtime.ReadCustodyAsync(world.Binding, seal: false, CancellationToken.None);
            ordinary.Receipt.ShouldBeNull();
            ordinary.State.ShouldBe(VerificationCustodyState.Unknown);
        }

        world.Recovery.Kills.ShouldBeEmpty();
        var loop = world.Session!.StartCustodyObserverForTests();
        await Task.Delay(TimeSpan.FromSeconds(5));
        world.Recovery.Kills.ShouldBeEmpty();
        loop.IsCompleted.ShouldBeTrue();

        var released = await world.Runtime.ReadCustodyAsync(world.Binding, seal: true, CancellationToken.None);
        world.Recovery.Kills.ShouldBe([world.ContainerId]);
        released.State.ShouldBe(VerificationCustodyState.Unknown);
        released.Reason.ShouldBe("released_unknown");
        released.Receipt.ShouldBeNull();
        File.Exists(world.Store.PathFor(world.Binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
        File.Exists(world.Store.PathFor(world.Binding.ExecutionId, "accepted-receipt.json")).ShouldBeFalse();
    }

    /// <summary>
    /// A live-client formula must not treat root exit as a Linux seal. Green on the current
    /// tip; red if Linux starts using HasExited the way Windows does.
    /// </summary>
    [Test]
    public async Task C1614_Root_exit_read_does_not_pass_exited_as_seal()
    {
        await using var world = await ReleaseWorld.Open(adopt: true);
        world.Session!.SetTerminalBackendStateForTest("Exited");
        bool? seen = null;
        world.Session.CustodySealProbeForTests = (seal, _) =>
        {
            seen = seal;
            return Task.FromResult(world.Draining());
        };

        var status = await world.Runtime.ReadCustodyAsync(world.Binding, seal: false, CancellationToken.None);
        seen.ShouldBe(false);
        status.State.ShouldBe(VerificationCustodyState.Draining);
        status.Receipt.ShouldBeNull();
        world.Recovery.Kills.ShouldBeEmpty();
        File.Exists(world.Store.PathFor(world.Binding.ExecutionId, "runner-seal.json")).ShouldBeFalse();
        File.Exists(world.Store.PathFor(world.Binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
    }

    /// <summary>
    /// A host-pipe failure on an ordinary read must not call the recovery kill, even when a
    /// prior runner seal exists. One explicit seal request does, once.
    /// </summary>
    [Test]
    public async Task C1614_Pipe_failure_on_ordinary_read_does_not_recover()
    {
        await using var world = await ReleaseWorld.Open(adopt: true);
        world.Store.WriteRecord(world.Binding, "runner-seal.json", new CustodyStamp(3, DateTime.UtcNow));
        world.Recovery.Pids = [];
        world.Session!.CustodySealProbeForTests = (_, _) =>
            throw new IOException("verification_custody_pipe_failed");

        var ordinary = await world.Runtime.ReadCustodyAsync(world.Binding, seal: false, CancellationToken.None);
        ordinary.State.ShouldBe(VerificationCustodyState.Unknown);
        ordinary.Receipt.ShouldBeNull();
        world.Recovery.Kills.ShouldBeEmpty();

        var released = await world.Runtime.ReadCustodyAsync(world.Binding, seal: true, CancellationToken.None);
        world.Recovery.Kills.ShouldBe([world.ContainerId]);
        released.State.ShouldBe(VerificationCustodyState.Unknown);
        released.Reason.ShouldBe("released_unknown");
        released.Receipt.ShouldBeNull();
    }

    internal sealed class RecordingRecovery : ILinuxCustodyRecovery
    {
        public List<Guid> Kills { get; } = [];
        public Action? BeforeKill { get; set; }
        public PtyTerminationObservation Result { get; set; } = new(true, 0);
        public bool Gone { get; set; } = true;
        public Guid NamespaceId { get; set; }
        public IReadOnlyList<int> Pids { get; set; } = [];
        public bool HostGone(int pid, DateTime startUtc) => Gone;
        public Guid ReadNamespace() => NamespaceId;
        public PtyTerminationObservation Kill(Guid containerId)
        {
            BeforeKill?.Invoke();
            Kills.Add(containerId);
            return Result;
        }
        public IReadOnlyList<int> ReadTreePids(Guid containerId) => Pids;
    }

    internal sealed class RecordingLogger : ILogger<SessionRunnerRuntime>
    {
        public string Text { get; private set; } = "";
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning) return;
            Text += formatter(state, exception) + "\n";
        }
    }

    internal sealed class ReleaseWorld : IAsyncDisposable
    {
        public required SessionRunnerRuntime Runtime { get; init; }

        public required VerificationCustodyStore Store { get; init; }
        public required VerificationExecutionBinding Binding { get; init; }
        public required RecordingRecovery Recovery { get; init; }
        public required RecordingLogger Logger { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required Guid ContainerId { get; init; }
        public required Guid NamespaceId { get; init; }
        public required VerificationHostIdentity Host { get; init; }
        public required string Root { get; init; }
        public required SessionRunnerRuntime.RunnerSession? Session { get; init; }

        public static Task<ReleaseWorld> Open(bool adopt = true, Guid? namespaceContainer = null)
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "c1614-release-" + Guid.NewGuid().ToString("N")));
            var logs = Path.Combine(root, "logs");
            var snapshot = Path.Combine(root, "snapshot");
            Directory.CreateDirectory(snapshot);
            var settings = new SessionRunnerSettings { SessionLogPath = logs, PtyBackend = "inbox" };
            var logger = new RecordingLogger();
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero));
            var recovery = new RecordingRecovery { NamespaceId = Guid.NewGuid() };
            var runtime = new SessionRunnerRuntime(Options.Create(settings), logger, timeProvider: clock,
                linuxRecovery: recovery);
            var now = DateTime.UtcNow;
            var binding = new VerificationExecutionBinding(Guid.NewGuid(),
                new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
                new(Guid.NewGuid(), new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)),
                new(root, Path.Combine(root, ".git"), snapshot,
                    Path.Combine(root, ".git", "worktrees", "snapshot"), "feat/c1614", Guid.NewGuid()),
                VerificationCustodyBackends.LinuxCgroup, runtime.RunnerStoreId);
            var store = runtime.CustodyForTests.Store;
            store.Reserve(binding);
            var container = Guid.NewGuid();
            var hostId = Guid.NewGuid();
            var started = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
            var host = new VerificationHostIdentity(store.StoreId, hostId, container, 424242, started);
            store.WriteRecord(binding, "host-identity.json", host);
            store.WriteRecord(binding, "runner-host.json", new RunnerHostIdentity(hostId, host.HostPid, host.HostStartTimeUtc));
            store.WriteRecord(binding, "linux-namespace.json",
                new LinuxNamespaceRecord(1, recovery.NamespaceId, namespaceContainer ?? container));
            store.WriteRecord(binding, "native-start-intent.json", new CustodyStamp(1, started));
            host = store.ReadRecord<VerificationHostIdentity>(binding, "host-identity.json")!;
            SessionRunnerRuntime.RunnerSession? session = null;
            if (adopt)
            {
                session = new SessionRunnerRuntime.RunnerSession(binding.Generation.SessionId, settings,
                    new SessionRunnerEventHub(), logger);
                session.SetCustody(runtime.CustodyForTests, binding);
                session.MarkLaunchFinishedForTests();
                runtime.AdoptSessionForTests(session);
            }
            return Task.FromResult(new ReleaseWorld
            {
                Runtime = runtime, Session = session, Store = store, Binding = binding,
                Recovery = recovery, Logger = logger, Clock = clock,
                ContainerId = container, NamespaceId = recovery.NamespaceId, Host = host, Root = root,
            });
        }

        public VerificationCustodyStatus Draining() =>
            new(Binding, VerificationCustodyState.Draining, "descendants_running", Host);

        public void WriteDisposition(Guid containerId)
        {
            Store.WriteRecord(Binding, "runner-seal.json", new CustodyStamp(3, DateTime.UtcNow));
            Store.WriteRecord(Binding, "linux-release-disposition.json", new LinuxReleaseDisposition(
                1, Store.StoreId, Binding.ExecutionId, NamespaceId, Host.HostInstanceId, containerId, DateTime.UtcNow));
        }

        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
