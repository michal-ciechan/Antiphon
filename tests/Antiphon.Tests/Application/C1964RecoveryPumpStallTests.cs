using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1964 slice 1: every recovery step of the phone-home pump has a deadline on the
/// connection's clock, lease re-recovery needs only the List, refusals and lease expiries are
/// logged, and a per-connection watchdog restarts a recovery cycle that has gone quiet. The
/// 2026-11-17 incident left server2 undispatchable for 45 minutes with no log line at all.
/// </summary>
[Category("Integration")]
public class C1964RecoveryPumpStallTests
{
    private const string L1 = "Phone-home runner {RunnerId} epoch {Epoch} lease expired";
    private const string L2 = "Phone-home catch-up step {Step} for session {SessionId}";
    private const string L3 = "Phone-home catch-up for runner {RunnerId} epoch {Epoch} exhausted";
    private const string L4 = "Phone-home lease re-recovery for runner {RunnerId} epoch {Epoch} refused";
    private const string L5 = "Phone-home runner {RunnerId} epoch {Epoch} re-recovered its lease";
    private const string L6 = "Phone-home recovery watchdog: runner {RunnerId}";
    private const string CatchUpList = "Phone-home catch-up List for runner {RunnerId} failed";
    private const string RefreshList = "Phone-home inventory refresh List for runner {RunnerId} epoch {Epoch} failed";
    private const string TranscriptText = "c1964-transcript-text";

    [Test]
    [Timeout(30_000)]
    public async Task Lease_rerecovery_requires_only_the_list_while_a_session_gate_is_held()
    {
        await using var world = await StallWorld.StartAsync(database: true);
        var session = await world.SeedAsync();
        world.Listed(session.Id, "s-1");
        (await world.CycleAsync()).ShouldBeTrue();
        world.Live.DispatchEligible.ShouldBeTrue();
        var transcripts = world.Peer.RequestCount(PhoneHomeOperation.Transcript);
        transcripts.ShouldBe(1);

        using var held = await world.Store.BeginWriteAsync(session.Id, CancellationToken.None);
        world.Expire();
        await world.HeartbeatAsync();

        var cycle = world.Pump.RunCycleAsync(world.Stop.Token);
        (await cycle.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue(
            "lease re-recovery must not wait on a session gate: it needs only the List");
        world.Live.DispatchEligible.ShouldBeTrue();
        world.Peer.RequestCount(PhoneHomeOperation.Transcript).ShouldBe(
            transcripts, "a live socket delivered every transcript frame already; re-recovery must not re-fetch");
        world.PumpLogs(L5).Count.ShouldBe(1);
        world.ShouldLogNoSecrets();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Connect_catch_up_skips_a_session_whose_gate_never_opens()
    {
        await using var world = await StallWorld.StartAsync(database: true);
        var blocked = await world.SeedAsync();
        var good = await world.SeedAsync();
        world.Listed(blocked.Id, "b-1");
        world.Listed(good.Id, "g-1");

        using var held = await world.Store.BeginWriteAsync(blocked.Id, CancellationToken.None);
        var gateWait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Store.NextGateWait = gateWait;
        var cycle = world.Pump.RunCycleAsync(world.Stop.Token);
        await gateWait.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        world.Clock.Advance(TimeSpan.FromSeconds(world.Settings.CatchUpSessionTimeoutSeconds + 1));

        (await cycle.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue(
            "a session whose gate never opens is skipped; the completed List carries eligibility");
        world.Live.DispatchEligible.ShouldBeTrue();
        (await world.PersistedAsync(good.Id, "g-1")).ShouldBe(1);
        var skipped = world.PumpLogs(L2).ShouldHaveSingleItem();
        skipped.State["SessionId"].ShouldBe(blocked.Id);
        skipped.State["Step"].ShouldBe("transcript.persist");
        world.Pump.LastCatchUpUnverifiedSessions.ShouldBe(1);
        world.Live.KnownLiveSessions().ShouldContain(blocked.Id, "a skipped session keeps its seat");
        world.ShouldLogNoSecrets();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Connect_catch_up_bounds_a_transcript_fetch_that_never_answers()
    {
        await using var world = await StallWorld.StartAsync(database: true);
        var blocked = await world.SeedAsync();
        var good = await world.SeedAsync();
        world.Listed(blocked.Id, "b-1");
        world.Listed(good.Id, "g-1");
        world.Peer.SilentWhen(frame => IsTranscriptFor(frame, blocked.Id));

        var cycle = world.Pump.RunCycleAsync(world.Stop.Token);
        await WaitAsync(() => world.Peer.RequestCount(PhoneHomeOperation.Transcript) >= 1, "the first Transcript request");
        await Task.Delay(100);
        world.Clock.Advance(TimeSpan.FromSeconds(world.Settings.CatchUpSessionTimeoutSeconds + 1));

        (await cycle.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue(
            "the session budget (30 s) must end a silent fetch before its 60 s reply timer");
        world.Live.DispatchEligible.ShouldBeTrue();
        (await world.PersistedAsync(good.Id, "g-1")).ShouldBe(1);
        var skipped = world.PumpLogs(L2).ShouldHaveSingleItem();
        skipped.State["SessionId"].ShouldBe(blocked.Id);
        skipped.State["Step"].ShouldBe("transcript.fetch");
        world.ShouldLogNoSecrets();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Silent_list_trips_the_watchdog_once_per_window_and_recovery_follows_the_answer()
    {
        await using var world = await StallWorld.StartAsync();
        (await world.CycleAsync()).ShouldBeTrue();
        var t0 = world.Clock.GetUtcNow();

        // (1) The lease expires; the re-recovery List is silent and ends at its 60 s reply timer.
        world.Expire();
        await world.HeartbeatAsync();
        world.Peer.SilentFor(PhoneHomeOperation.List);
        var cycle1 = world.Pump.RunCycleAsync(world.Stop.Token);
        await WaitAsync(() => world.Peer.RequestCount(PhoneHomeOperation.List) == 2, "the first re-recovery List");
        await Task.Delay(100);
        await world.AdvanceAsync(61);
        (await cycle1.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();
        world.PumpLogs(CatchUpList).Last().State["Code"].ShouldBe(PhoneHomeProblemTypes.RequestTimeout);

        // (2) The window (180 s) has passed with no List: one Error, and the second poll is Pending.
        await world.AdvanceToAsync(t0 + TimeSpan.FromSeconds(181));
        world.Pump.RunWatchdogOnce();
        world.Pump.RunWatchdogOnce();
        var first = world.PumpLogs(L6).ShouldHaveSingleItem("a quiet runner gets one watchdog Error per window");
        first.Level.ShouldBe(LogLevel.Error);
        first.State["Trigger"].ShouldBe("no_list");
        Convert.ToDouble(first.State["LimitSeconds"]).ShouldBe(180);

        // (3) The next cycle applies the restart and sends at once; its List keeps its full budget.
        var restartedAt = world.Clock.GetUtcNow();
        var cycle2 = world.Pump.RunCycleAsync(world.Stop.Token);
        await WaitAsync(() => world.Peer.RequestCount(PhoneHomeOperation.List) == 3, "the replacement List");
        await Task.Delay(100);
        await world.AdvanceAsync(30);
        world.Pump.RunWatchdogOnce();
        world.PumpLogs(L6).Count.ShouldBe(1, "a restart opens a new window; the replacement is not cancelled");
        await Task.Delay(200);
        cycle2.IsCompleted.ShouldBeFalse("the replacement attempt still owns its List");
        await world.AdvanceAsync(31);
        (await cycle2.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();
        world.PumpLogs(CatchUpList).Last().State["Code"].ShouldBe(
            PhoneHomeProblemTypes.RequestTimeout, "the replacement ran to its reply timer, not a watchdog cancel");

        // (4) The new window passes too: exactly one more Error.
        await world.AdvanceToAsync(restartedAt + TimeSpan.FromSeconds(181));
        world.Pump.RunWatchdogOnce();
        world.Pump.RunWatchdogOnce();
        var fires = world.PumpLogs(L6);
        fires.Count.ShouldBe(2, "one watchdog Error per window");
        fires[1].State["Trigger"].ShouldBe("no_list");
        Convert.ToDouble(fires[1].State["SinceListSeconds"]).ShouldBeGreaterThanOrEqualTo(361);

        // (5) The runner answers: the lease re-recovers on the same connection.
        world.Peer.Speak(PhoneHomeOperation.List);
        (await world.CycleAsync()).ShouldBeTrue();
        world.Live.DispatchEligible.ShouldBeTrue();
        world.PumpLogs(L5).Count.ShouldBe(1);
        world.Peer.RequestCount(PhoneHomeOperation.List).ShouldBe(4, "initial recovery plus three re-recovery Lists");
        world.ShouldLogNoSecrets();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Lease_expiry_logs_once_per_flip()
    {
        await using var world = await StallWorld.StartAsync();
        (await world.CycleAsync()).ShouldBeTrue();
        var directory = world.Host.Directory;
        var runnerId = world.Host.AllowedRunnerId;

        world.Clock.Advance(TimeSpan.FromSeconds(91));
        directory.GetLiveStoreId(runnerId);
        for (var i = 0; i < 3; i++)
            directory.SnapshotLive(runnerId);
        directory.Status(runnerId);
        var expired = world.DirectoryLogs(L1).ShouldHaveSingleItem("one line per eligible-to-ineligible flip");
        expired.Level.ShouldBe(LogLevel.Warning);
        Convert.ToInt32(expired["LeaseSeconds"]).ShouldBe(90);
        expired["RunnerId"].ShouldBe(runnerId);
        Convert.ToInt64(expired["Epoch"]).ShouldBe(world.Live.Epoch);

        await world.HeartbeatAsync();
        (await world.CycleAsync()).ShouldBeTrue();
        world.Clock.Advance(TimeSpan.FromSeconds(91));
        directory.SnapshotLive(runnerId);
        directory.GetLiveStoreId(runnerId);
        directory.Status(runnerId);
        world.DirectoryLogs(L1).Count.ShouldBe(2);
        world.ShouldLogNoSecrets();
    }

    [Test]
    [Arguments("before")]
    [Arguments("after")]
    [Timeout(30_000)]
    public async Task Lease_recovery_refusal_logs_its_reason(string stage)
    {
        await using var world = await StallWorld.StartAsync();
        (await world.CycleAsync()).ShouldBeTrue();
        world.Expire();
        await world.HeartbeatAsync();

        if (stage == "before")
        {
            await world.Pump.EndPumpForTest();
            await world.Peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Event, world.Live.Epoch,
                Guid.Empty, EventName: "held-event", Payload: JsonSerializer.SerializeToElement(new { })));
            var held = await world.Live.Events.ReadAsync(world.Stop.Token);
            try
            {
                world.Live.PendingEvents.ShouldBe(1);
                for (var i = 0; i < 3; i++)
                    (await world.CycleAsync()).ShouldBeFalse();
                var refused = world.PumpLogs(L4).ShouldHaveSingleItem("the first refusal of a streak logs; repeats wait");
                refused.State["Stage"].ShouldBe("before_catch_up");
                refused.State["Reason"].ShouldBe("pending_events");
                Convert.ToInt32(refused.State["Refusals"]).ShouldBe(1);

                await world.AdvanceAsync(61);
                (await world.CycleAsync()).ShouldBeFalse();
                var refusals = world.PumpLogs(L4);
                refusals.Count.ShouldBe(2, "a streak logs again after RecoveryRefusalLogSeconds");
                Convert.ToInt32(refusals[1].State["Refusals"]).ShouldBe(4, "every refusal is counted in the line");
            }
            finally
            {
                held.Release();
            }

            (await world.CycleAsync()).ShouldBeTrue();
            var recovered = world.PumpLogs(L5).ShouldHaveSingleItem();
            Convert.ToInt32(recovered.State["Refusals"]).ShouldBe(4);
        }
        else
        {
            world.Peer.SilentFor(PhoneHomeOperation.List);
            var cycle = world.Pump.RunCycleAsync(world.Stop.Token);
            await WaitAsync(() => world.Peer.RequestCount(PhoneHomeOperation.List) == 2, "the re-recovery List");
            var request = world.Peer.Incoming.ToArray().Last(f => f.Operation == PhoneHomeOperation.List);
            world.Host.Directory.ApplyState(world.Host.AllowedRunnerId,
                new RunnerState(true, world.Clock.GetUtcNow(), "maintenance", null, false, null, null, "maintenance"));
            await world.Peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Result, request.Epoch,
                request.RequestId, request.Operation, JsonSerializer.SerializeToElement(Array.Empty<RunnerSessionDto>())));

            (await cycle.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();
            var refused = world.PumpLogs(L4).ShouldHaveSingleItem();
            refused.State["Stage"].ShouldBe("after_catch_up");
            refused.State["Reason"].ShouldBe("draining");
            world.Live.DispatchEligible.ShouldBeFalse();
        }

        world.ShouldLogNoSecrets();
    }

    [Test]
    [Arguments("connect")]
    [Arguments("lease")]
    [Timeout(30_000)]
    public async Task Timed_out_list_never_makes_the_runner_eligible(string path)
    {
        await using var world = await StallWorld.StartAsync();
        if (path == "lease")
        {
            (await world.CycleAsync()).ShouldBeTrue();
            world.Expire();
            await world.HeartbeatAsync();
        }

        var expected = path == "lease" ? 2 : 1;
        world.Peer.SilentFor(PhoneHomeOperation.List);
        var cycle = world.Pump.RunCycleAsync(world.Stop.Token);
        await WaitAsync(() => world.Peer.RequestCount(PhoneHomeOperation.List) == expected, "the catch-up List");
        await Task.Delay(100);
        world.Clock.Advance(TimeSpan.FromSeconds(world.Settings.CatchUpListTimeoutSeconds + 1));
        await world.HeartbeatAsync();
        (await cycle.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();

        // The runner answers late. The waiter is gone, so the reply must change nothing.
        var request = world.Peer.Incoming.ToArray().Last(f => f.Operation == PhoneHomeOperation.List);
        await world.Peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Result, request.Epoch,
            request.RequestId, request.Operation, JsonSerializer.SerializeToElement(Array.Empty<RunnerSessionDto>())));
        // A same-epoch reply fences receipt of the late frame before it.
        await world.Live.RequestAsync(PhoneHomeOperation.Health, null, world.Stop.Token);

        world.Live.DispatchEligible.ShouldBeFalse();
        Should.Throw<ServiceUnavailableException>(() => world.Host.Directory.Resolve(world.Host.AllowedRunnerId))
            .Message.ShouldContain("has not completed recovery");
        (await world.CycleAsync()).ShouldBeFalse("the retry delay still applies after a timed-out List");
        world.Peer.RequestCount(PhoneHomeOperation.List).ShouldBe(expected);
        world.ShouldLogNoSecrets();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Catch_up_total_budget_skips_the_remaining_sessions()
    {
        await using var world = await StallWorld.StartAsync(database: true, configure: s => s.CatchUpTimeoutSeconds = 45);
        var first = await world.SeedAsync();
        var second = await world.SeedAsync();
        var good = await world.SeedAsync();
        world.Listed(first.Id, "b1-1");
        world.Listed(second.Id, "b2-1");
        world.Listed(good.Id, "g-1");
        world.Peer.SilentWhen(frame => IsTranscriptFor(frame, first.Id) || IsTranscriptFor(frame, second.Id));

        var cycle = world.Pump.RunCycleAsync(world.Stop.Token);
        await WaitAsync(() => world.Peer.RequestCount(PhoneHomeOperation.Transcript) >= 1, "the first Transcript request");
        await Task.Delay(100);
        world.Clock.Advance(TimeSpan.FromSeconds(31));
        await WaitAsync(() => world.Peer.RequestCount(PhoneHomeOperation.Transcript) >= 2,
            "the second session's Transcript request after the first session's budget");
        await Task.Delay(100);
        world.Clock.Advance(TimeSpan.FromSeconds(15));

        (await cycle.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue("the completed List carries eligibility");
        world.Live.DispatchEligible.ShouldBeTrue();
        var exhausted = world.PumpLogs(L3).ShouldHaveSingleItem();
        Convert.ToInt32(exhausted.State["Completed"]).ShouldBe(2);
        Convert.ToInt32(exhausted.State["Listed"]).ShouldBe(3);
        Convert.ToInt32(exhausted.State["Unverified"]).ShouldBe(3);
        world.PumpLogs(L2).Count.ShouldBe(1, "only the first session ran out of its own budget");
        world.Peer.RequestCount(PhoneHomeOperation.Transcript).ShouldBe(2, "the last session is never requested");
        world.Pump.LastCatchUpUnverifiedSessions.ShouldBe(3);
        world.ShouldLogNoSecrets();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Watchdog_cancels_an_attempt_that_outlives_its_budgets()
    {
        await using var world = await StallWorld.StartAsync();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Pump.HoldCatchUp(world.Host.AllowedRunnerId, hold);
        try
        {
            var cycle = world.Pump.RunCycleAsync(world.Stop.Token);
            await Task.Delay(100);
            world.Clock.Advance(TimeSpan.FromSeconds(391));
            await world.HeartbeatAsync();
            world.Pump.RunWatchdogOnce();

            var fired = world.PumpLogs(L6).ShouldHaveSingleItem();
            fired.State["Trigger"].ShouldBe("attempt_overrun");
            fired.State["AttemptStep"].ShouldBe("hold");
            (await cycle.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse(
                "the watchdog cancel must hand the loop back even while a step ignores every budget");
            world.Live.DispatchEligible.ShouldBeFalse();
        }
        finally
        {
            hold.TrySetResult();
        }

        await world.HeartbeatAsync();
        (await world.CycleAsync()).ShouldBeTrue();
        world.ShouldLogNoSecrets();
    }

    [Test]
    [Arguments("catch_up")]
    [Arguments("refresh")]
    [Timeout(30_000)]
    public async Task Stalled_send_gate_ends_the_list_at_its_budget(string path)
    {
        await using var world = await StallWorld.StartAsync(
            configure: s => s.InventoryRefreshSeconds = path == "refresh" ? 30 : 0);
        if (path == "refresh")
        {
            (await world.CycleAsync()).ShouldBeTrue();
            world.Peer.RequestCount(PhoneHomeOperation.List).ShouldBe(1);
        }

        // Hold the connection's send gate: a request parked in beforeWrite writes nothing, so the
        // 60 s reply timer is never armed for anything queued behind it.
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = world.Live.RequestAsync(PhoneHomeOperation.List, null, beforeWrite: _ =>
        {
            entered.TrySetResult();
            return new ValueTask<string?>(release.Task);
        }, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (path == "catch_up")
            {
                var cycle = world.Pump.RunCycleAsync(world.Stop.Token);
                await Task.Delay(100);
                await world.AdvanceAsync(45);
                await world.AdvanceAsync(46);
                (await cycle.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();
                world.Live.DispatchEligible.ShouldBeFalse();
                world.PumpLogs(CatchUpList).ShouldHaveSingleItem().State["Code"].ShouldBe("catch_up_list_budget");
                world.Peer.RequestCount(PhoneHomeOperation.List).ShouldBe(0, "the stall was in the send");

                release.SetResult("test release");
                await Should.ThrowAsync<PhoneHomeRequestRefusedException>(holder);
                await world.AdvanceAsync(world.Settings.CatchUpRetrySeconds);
                (await world.CycleAsync()).ShouldBeTrue();
                world.Live.DispatchEligible.ShouldBeTrue();
                world.Peer.RequestCount(PhoneHomeOperation.List).ShouldBe(1);
            }
            else
            {
                await world.AdvanceAsync(31);
                (await world.CycleAsync()).ShouldBeFalse("an eligible runner's cycle only starts the refresh");
                await Task.Delay(100);
                await world.AdvanceAsync(45);
                await world.AdvanceAsync(46);
                await WaitAsync(() => world.PumpLogs(RefreshList).Count > 0, "the refresh List Warning");
                world.PumpLogs(RefreshList).ShouldHaveSingleItem().State["Code"].ShouldBe("refresh_list_budget");
                world.Live.DispatchEligible.ShouldBeTrue();
                world.Peer.RequestCount(PhoneHomeOperation.List).ShouldBe(1);
            }
        }
        finally
        {
            release.TrySetResult("test release");
        }

        world.ShouldLogNoSecrets();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Watchdog_stays_quiet_for_an_eligible_runner_without_refresh()
    {
        await using var world = await StallWorld.StartAsync();
        (await world.CycleAsync()).ShouldBeTrue();
        world.Peer.RequestCount(PhoneHomeOperation.List).ShouldBe(1);

        for (var i = 0; i < 5; i++)
        {
            await world.AdvanceAsync(80);
            world.Pump.RunWatchdogOnce();
        }

        world.PumpLogs(L6).ShouldBeEmpty("an eligible runner with refresh off is expected to make no List");
        world.Live.DispatchEligible.ShouldBeTrue();
        world.Peer.RequestCount(PhoneHomeOperation.List).ShouldBe(1);
        world.ShouldLogNoSecrets();
    }

    private static bool IsTranscriptFor(PhoneHomeFrame frame, Guid sessionId) =>
        frame.Operation == PhoneHomeOperation.Transcript
        && frame.Payload is { ValueKind: JsonValueKind.Object } payload
        && payload.TryGetProperty("sessionId", out var id)
        && id.GetGuid() == sessionId;

    private static async Task WaitAsync(Func<bool> done, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!done())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Timed out waiting for {what}.");
            await Task.Delay(10);
        }
    }

    // Each test owns one socket and one fake clock, as LeaseWorld does. The database tests use the
    // shared assembly Postgres with an isolated schema each.
    private sealed class StallWorld : IAsyncDisposable
    {
        private IsolatedTestSchema? _schema;
        private BridgeQueueHarness? _harness;
        private AppDbContext? _db;

        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
        public PhoneHomeTestHost Host { get; private set; } = null!;
        public PhoneHomeScriptedPeer Peer { get; private set; } = null!;
        public PhoneHomeLiveConnection Live { get; private set; } = null!;
        public PhoneHomeRecoveryPump Pump { get; private set; } = null!;
        public PhoneHomeRunnerSettings Settings { get; private set; } = null!;
        public RecordingLogger<PhoneHomeRecoveryPump> Logger { get; } = new();
        public CancellationTokenSource Stop { get; } = new(TimeSpan.FromSeconds(25));
        public SessionStateStore Store => _harness!.Provider.GetRequiredService<SessionStateStore>();

        public static async Task<StallWorld> StartAsync(bool database = false, Action<PhoneHomeRunnerSettings>? configure = null)
        {
            var world = new StallWorld();
            if (database)
            {
                world._schema = await TestDbFixture.CreateIsolatedSchemaAsync();
                world._harness = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = world._schema.ConnectionString });
                world._db = new AppDbContext(TestDbFixture.CreateDbContextOptions(world._schema.ConnectionString));
            }

            world.Host = await PhoneHomeTestHost.StartAsync(world.Clock, connectionString: world._schema?.ConnectionString);
            world.Peer = await world.Host.ConnectPeerAsync();
            world.Live = await world.Host.WaitLiveAsync();
            world.Settings = new PhoneHomeRunnerSettings
            {
                Enabled = true,
                AllowedRunnerId = world.Host.AllowedRunnerId,
                StandingAgentId = Guid.NewGuid(),
                HostWorkspaceRoot = @"C:\work",
                SharedSecret = world.Host.Secret,
                // Expiry moves the clock past the periodic refresh too; keep every List awaited.
                InventoryRefreshSeconds = 0,
            };
            configure?.Invoke(world.Settings);
            var scopes = world._harness?.Provider.GetRequiredService<IServiceScopeFactory>()
                ?? world.Host.App.Services.GetRequiredService<IServiceScopeFactory>();
            world.Pump = new PhoneHomeRecoveryPump(world.Host.Directory, Options.Create(world.Settings), scopes, world.Logger);
            return world;
        }

        public Task<bool> CycleAsync() => Pump.RunCycleAsync(Stop.Token).WaitAsync(TimeSpan.FromSeconds(5));

        public void Expire()
        {
            Clock.Advance(TimeSpan.FromSeconds(91));
            Host.Directory.Status(Host.AllowedRunnerId).DispatchEligible.ShouldBeFalse();
            Live.SocketOpen.ShouldBeTrue();
        }

        public async Task HeartbeatAsync()
        {
            await Peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Heartbeat, Live.Epoch, Guid.Empty));
            await WaitAsync(() => Live.LastHeartbeatUtc == Clock.GetUtcNow(), "the heartbeat");
        }

        /// <summary>Moves the clock in steps under the 90 s lease, with a heartbeat after each.</summary>
        public async Task AdvanceAsync(int seconds)
        {
            while (seconds > 0)
            {
                var step = Math.Min(seconds, 60);
                Clock.Advance(TimeSpan.FromSeconds(step));
                await HeartbeatAsync();
                seconds -= step;
            }
        }

        public Task AdvanceToAsync(DateTimeOffset at) =>
            AdvanceAsync((int)Math.Ceiling((at - Clock.GetUtcNow()).TotalSeconds));

        public async Task<AgentSession> SeedAsync()
        {
            var session = new AgentSession
            {
                Id = Guid.NewGuid(),
                DefinitionName = "grok",
                AgentKind = AgentKind.Grok,
                Status = SessionStatus.Running,
                Cwd = @"C:\work",
                Cols = 80,
                Rows = 24,
                CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                RunnerId = Host.AllowedRunnerId,
                RunnerStoreId = Host.StoreId,
                RunnerCwd = "/work",
            };
            _db!.AgentSessions.Add(session);
            await _db.SaveChangesAsync();
            return session;
        }

        public void Listed(Guid sessionId, string uuid)
        {
            Peer.Sessions.Add(new RunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Running", null, "", 1));
            Peer.Transcripts[sessionId] = new RunnerTranscriptDto(sessionId,
                [new RunnerTranscriptEvent(sessionId, 1, TranscriptKinds.UserPrompt, uuid, null, DateTimeOffset.UtcNow,
                    "user", TranscriptText, null, null, null, null, null)], 1);
        }

        public Task<int> PersistedAsync(Guid sessionId, string uuid) =>
            _db!.TranscriptEntries.CountAsync(t => t.AgentSessionId == sessionId && t.Uuid == uuid);

        public List<RecordingLogEntry> PumpLogs(string templatePrefix)
        {
            lock (Logger.Entries)
                return Logger.Entries.Where(e => Template(e.State).StartsWith(templatePrefix, StringComparison.Ordinal)).ToList();
        }

        public List<CapturedLog> DirectoryLogs(string templatePrefix) =>
            Host.Logs.Entries
                .Where(e => e.Category.EndsWith(nameof(PhoneHomeRunnerDirectory), StringComparison.Ordinal)
                    && Template(e.Properties).StartsWith(templatePrefix, StringComparison.Ordinal))
                .ToList();

        public void ShouldLogNoSecrets()
        {
            List<string> messages;
            lock (Logger.Entries)
                messages = Logger.Entries.Select(e => e.Message).ToList();
            messages.AddRange(Host.Logs.Entries.Select(e => e.Message));
            messages.ShouldNotContain(m => m.Contains(Host.Secret, StringComparison.Ordinal));
            messages.ShouldNotContain(m => m.Contains(TranscriptText, StringComparison.Ordinal));
        }

        private static string Template(IReadOnlyDictionary<string, object?> state) =>
            state.TryGetValue("{OriginalFormat}", out var format) && format is string text ? text : "";

        public async ValueTask DisposeAsync()
        {
            Stop.Cancel();
            await Pump.EndPumpForTest();
            Pump.Dispose();
            await Peer.DisposeAsync();
            await Host.DisposeAsync();
            if (_db is not null) await _db.DisposeAsync();
            if (_harness is not null) await _harness.DisposeAsync();
            if (_schema is not null) await _schema.DisposeAsync();
            Stop.Dispose();
        }
    }
}
