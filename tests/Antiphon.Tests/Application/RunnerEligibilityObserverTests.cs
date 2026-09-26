using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0726 V-2, V-3, V-21, V-22, V-23. The real directory tells its observer on the four
/// eligibility edges and reports one snapshot row per configured remote.
/// </summary>
[Category("Integration")]
public sealed class RunnerEligibilityObserverTests
{
    [Test]
    public async Task disconnect_recovery_and_supersede_notify_the_observer_with_the_runner_id()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        var recorder = new IdRecorder();
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB), observer: recorder);

        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", storeId: Guid.NewGuid(), secret: secretA);
        recorder.Ids.ShouldBeEmpty();

        var liveA = host.Directory.SnapshotLive("runner-a");
        liveA.ShouldNotBeNull();
        host.Directory.MarkRecovered(liveA);
        recorder.Ids.ShouldBe(["runner-a"]);
        host.Directory.MarkRecovered(liveA);
        recorder.Ids.ShouldBe(["runner-a"]);

        await using var replacement = await host.ConnectPeerAsync(runnerId: "runner-a", storeId: liveA.RunnerStoreId, secret: secretA);
        recorder.Ids.ShouldBe(["runner-a", "runner-a"]);
        var next = host.Directory.SnapshotLive("runner-a");
        next.ShouldNotBeNull();
        host.Directory.MarkRecovered(next);
        recorder.Ids.ShouldBe(["runner-a", "runner-a", "runner-a"]);

        replacement.Socket.Abort();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (host.Directory.Status("runner-a").Available && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        host.Directory.Status("runner-a").Available.ShouldBeFalse();
        recorder.Ids.ShouldBe(["runner-a", "runner-a", "runner-a", "runner-a"]);
        recorder.Ids.ShouldNotContain("runner-b");
    }

    [Test]
    public async Task lease_expiry_seen_by_a_snapshot_notifies_once()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var recorder = new IdRecorder();
        await using var host = await PhoneHomeTestHost.StartAsync(
            clock, configured: Pair(secretA, "secret-b-" + Guid.NewGuid().ToString("N")), observer: recorder);
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", storeId: Guid.NewGuid(), secret: secretA);
        var liveA = host.Directory.SnapshotLive("runner-a");
        liveA.ShouldNotBeNull();
        host.Directory.MarkRecovered(liveA);
        recorder.Ids.ShouldBe(["runner-a"]);

        clock.Advance(TimeSpan.FromSeconds(91));
        host.Directory.SnapshotLive("runner-a").ShouldNotBeNull();
        host.Directory.SnapshotLive("runner-a").ShouldNotBeNull();
        host.Directory.Status("runner-a").DispatchEligible.ShouldBeFalse();
        recorder.Ids.ShouldBe(["runner-a", "runner-a"]);
        _ = peerA;
    }

    [Test]
    public async Task snapshots_carry_one_row_per_configured_remote_with_the_dispatch_predicate()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        var secretC = "secret-c-" + Guid.NewGuid().ToString("N");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await PhoneHomeTestHost.StartAsync(clock, configured: Trio(secretA, secretB, secretC));
        var source = (IRunnerEligibilitySnapshotSource)host.Directory;

        var before = source.Snapshots();
        before.Select(row => row.RunnerId).ShouldBe(["runner-a", "runner-b", "runner-c"], ignoreOrder: true);
        before.ShouldNotContain(row => row.RunnerId == "desktop");
        before.Single(row => row.RunnerId == "runner-c").Enabled.ShouldBeFalse();

        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", storeId: Guid.NewGuid(), secret: secretA);
        source.Snapshots().Single(row => row.RunnerId == "runner-a").Eligible.ShouldBeFalse();

        host.Directory.MarkRecovered(host.Directory.SnapshotLive("runner-a"));
        var recovered = source.Snapshots().Single(row => row.RunnerId == "runner-a");
        recovered.Eligible.ShouldBeTrue();
        recovered.Reconnects.ShouldBe(1);

        clock.Advance(TimeSpan.FromSeconds(91));
        var expired = source.Snapshots().Single(row => row.RunnerId == "runner-a");
        expired.Eligible.ShouldBeFalse();
        expired.DisconnectReason.ShouldBe("lease_expired");
        _ = peerA;
    }

    [Test]
    public async Task a_throwing_observer_is_contained_and_logged()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(
            configured: Pair(secretA, "secret-b-" + Guid.NewGuid().ToString("N")),
            observer: new ThrowingObserver());
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", storeId: Guid.NewGuid(), secret: secretA);
        var liveA = host.Directory.SnapshotLive("runner-a");
        liveA.ShouldNotBeNull();

        Should.NotThrow(() => host.Directory.MarkRecovered(liveA));
        host.Directory.Status("runner-a").DispatchEligible.ShouldBeTrue();
        Should.NotThrow(() => host.Directory.Disconnect(liveA, "socket_closed"));
        host.Directory.Status("runner-a").Available.ShouldBeFalse();
        host.Logs.Entries.Count(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("runner-a", StringComparison.Ordinal)
            && entry.Message.Contains("eligibility observer", StringComparison.Ordinal))
            .ShouldBe(2);
    }

    [Test]
    public async Task the_observer_runs_outside_the_directory_gate()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        PhoneHomeRunnerDirectory? directory = null;
        var observer = new GateObserver(() => directory!);
        await using var host = await PhoneHomeTestHost.StartAsync(
            configured: Pair(secretA, "secret-b-" + Guid.NewGuid().ToString("N")),
            observer: observer);
        directory = host.Directory;
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", storeId: Guid.NewGuid(), secret: secretA);
        var liveA = host.Directory.SnapshotLive("runner-a");
        liveA.ShouldNotBeNull();

        host.Directory.MarkRecovered(liveA);
        peerA.Socket.Abort();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (observer.Results.Count < 2 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        observer.Results.ShouldBe([true, true]);
    }

    private static PhoneHomeRunnerSettings Pair(string secretA, string secretB) => new()
    {
        Enabled = true,
        Runners = new Dictionary<string, PhoneHomeRunnerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["runner-a"] = Entry("Runner A", secretA),
            ["runner-b"] = Entry("Runner B", secretB),
        },
    };

    private static PhoneHomeRunnerSettings Trio(string secretA, string secretB, string secretC)
    {
        var settings = Pair(secretA, secretB);
        var disabled = Entry("Runner C", secretC);
        disabled.Enabled = false;
        settings.Runners["runner-c"] = disabled;
        return settings;
    }

    private static PhoneHomeRunnerEntry Entry(string display, string secret) => new()
    {
        Enabled = true,
        DisplayName = display,
        AllowDelegatedTasks = true,
        HostWorkspaceRoot = @"C:\work",
        RunnerWorkspace = "/work",
        RunnerRepository = "/work/repos/antiphon",
        CallbackOrigin = "https://antiphon.test",
        SharedSecret = secret,
        MaxCapacity = 4,
        ChildGrokHome = "/state/grok",
        ChildClaudeHome = "/state/claude",
        ChildCodexHome = "/state/codex",
    };

    private sealed class IdRecorder : IRunnerEligibilityObserver
    {
        public List<string> Ids { get; } = [];

        public void Changed(string runnerId) => Ids.Add(runnerId);
    }

    private sealed class ThrowingObserver : IRunnerEligibilityObserver
    {
        public void Changed(string runnerId) => throw new InvalidOperationException("observer failed");
    }

    private sealed class GateObserver(Func<PhoneHomeRunnerDirectory> directory) : IRunnerEligibilityObserver
    {
        public List<bool> Results { get; } = [];

        public void Changed(string runnerId)
        {
            var completed = Task.Run(() => directory().Status(runnerId)).Wait(TimeSpan.FromSeconds(2));
            Results.Add(completed);
        }
    }
}
