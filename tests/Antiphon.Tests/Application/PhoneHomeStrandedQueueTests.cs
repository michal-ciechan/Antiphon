using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0679 D-10. A phone-home session has no in-process adapter and is not in the local runner's
// List, so every caller of AgentSessionRuntime.ListLiveSessions() used to read it as dead: the
// stranded-queue sweep skipped its brief, a WhenIdle reply stayed Pending, and send-now answered
// 409 "is not live". The runtime here is the production shape: its runner client routes through
// the real phone-home directory, the recovery pump runs a real catch-up over a real WebSocket, and
// the scripted runner records each typed prompt the way a real session's transcript would.
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class PhoneHomeStrandedQueueTests
{
    [Test]
    public async Task Stranded_delegation_brief_on_a_phone_home_session_is_delivered()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        peer.Sessions.Add(RunningOnRunner(h.SessionId));
        EchoSubmittedPromptsToTranscript(peer, h);
        const string brief = "CARD-0679 stranded delegation brief for a phone-home session";

        // Queued while the runner is still recovering: nothing is live yet, so it waits.
        await h.Queue.EnqueueAsync(h.SessionId, brief, MessageSendMode.WhenIdle, CancellationToken.None,
            origin: QueuedMessageOrigin.Delegation);
        (await ReadRowAsync(schema.ConnectionString, h.SessionId, brief)).Status.ShouldBe(QueuedMessageStatus.Pending);
        using var stop = new CancellationTokenSource();
        (await Pump(host, h).RunCycleAsync(stop.Token)).ShouldBeTrue("the catch-up List ran and recovered the runner");

        var flushed = await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        flushed.ShouldBe(1, "the stranded-queue watchdog serves a live phone-home session like a local one");
        TypedCount(peer, brief).ShouldBe(1);
        var row = await ReadRowAsync(schema.ConnectionString, h.SessionId, brief);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        stop.Cancel();
    }

    [Test]
    public async Task ListLiveSessions_follows_the_runner_inventory_without_an_rpc()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        var other = Guid.NewGuid();
        peer.Sessions.Add(RunningOnRunner(h.SessionId));
        peer.Sessions.Add(RunningOnRunner(other));
        var live = await host.WaitLiveAsync();
        using var stop = new CancellationTokenSource();
        (await Pump(host, h).RunCycleAsync(stop.Token)).ShouldBeTrue();
        var lists = peer.RequestCount(PhoneHomeOperation.List);

        var afterCatchUp = h.Runtime.ListLiveSessions();
        afterCatchUp.ShouldContain(h.SessionId, "the catch-up List named it Running");
        afterCatchUp.ShouldContain(other);

        await peer.EmitAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Event, live.Epoch, Guid.Empty,
            EventName: SessionRunnerEventNames.SessionExited,
            Payload: JsonSerializer.SerializeToElement(
                new RunnerSessionExitedEvent(other, 0, nameof(AgentExitReason.Unknown), 0), PhoneHomeFraming.Json)));
        await WaitUntilAsync(() => !h.Runtime.ListLiveSessions().Contains(other));
        h.Runtime.ListLiveSessions().ShouldContain(h.SessionId, "only the exited session left the inventory");
        h.Runtime.ListLiveOrUnknownSessions().ShouldNotContain(other, "an exit is a confirmed absence");

        peer.Socket.Abort();
        await WaitUntilAsync(() => !live.SocketOpen);
        h.Runtime.ListLiveSessions().ShouldNotContain(h.SessionId, "a closed connection vouches for nothing");
        // Review 87af1bf6: nor does it say the session is gone; that waits for the next catch-up List.
        h.Runtime.ListUnknownSessions().ShouldContain(h.SessionId);
        h.Runtime.ListUnknownSessions().ShouldNotContain(other);

        peer.RequestCount(PhoneHomeOperation.List).ShouldBe(lists, "ListLiveSessions never asks the runner");
        stop.Cancel();
    }

    [Test]
    public async Task Launch_ack_adds_the_session_before_the_next_refresh()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        using var stop = new CancellationTokenSource();
        (await Pump(host, h).RunCycleAsync(stop.Token)).ShouldBeTrue();
        var lists = peer.RequestCount(PhoneHomeOperation.List);
        var launched = Guid.NewGuid();
        h.Runtime.ListLiveSessions().ShouldNotContain(launched);

        var client = host.Directory.Resolve(host.AllowedRunnerId);
        var started = await client.StartAsync(launched, new AgentLaunchSpec(
            DefinitionName: "raw",
            Kind: AgentKind.Raw,
            Exe: "/bin/sh",
            Args: [],
            Env: new Dictionary<string, string>(),
            Cwd: "/work",
            Cols: 120,
            Rows: 30), CancellationToken.None);

        started.Status.ShouldBe("Running");
        h.Runtime.ListLiveSessions().ShouldContain(launched, "the launch ack vouches for the session at once");
        peer.RequestCount(PhoneHomeOperation.List).ShouldBe(lists, "no refresh List was needed");
        stop.Cancel();
    }

    [Test]
    public async Task Inventory_refresh_learns_a_session_no_ack_or_event_announced()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(clock, schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        var pump = Pump(host, h);
        using var stop = new CancellationTokenSource();
        (await pump.RunCycleAsync(stop.Token)).ShouldBeTrue();
        // Adopted by a restarted runner, say: no launch ack on this connection, no event.
        var adopted = Guid.NewGuid();
        peer.Sessions.Add(RunningOnRunner(adopted));

        (await pump.RunCycleAsync(stop.Token)).ShouldBeFalse();
        peer.RequestCount(PhoneHomeOperation.List).ShouldBe(1, "the refresh waits InventoryRefreshSeconds");
        h.Runtime.ListLiveSessions().ShouldNotContain(adopted);

        clock.Advance(TimeSpan.FromSeconds(new PhoneHomeRunnerSettings().InventoryRefreshSeconds));
        (await pump.RunCycleAsync(stop.Token)).ShouldBeFalse();

        await WaitUntilAsync(() => h.Runtime.ListLiveSessions().Contains(adopted));
        peer.RequestCount(PhoneHomeOperation.List).ShouldBe(2);
        stop.Cancel();
    }

    // The 2026-09-24 evening shape: a reply to an idle server2 session sat Pending forever.
    [Test]
    public async Task WhenIdle_reply_to_an_idle_phone_home_session_is_delivered()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        peer.Sessions.Add(RunningOnRunner(h.SessionId));
        EchoSubmittedPromptsToTranscript(peer, h);
        using var stop = new CancellationTokenSource();
        (await Pump(host, h).RunCycleAsync(stop.Token)).ShouldBeTrue();
        const string reply = "CARD-0679 WhenIdle reply to an idle phone-home session";

        await h.Queue.EnqueueAsync(h.SessionId, reply, MessageSendMode.WhenIdle, CancellationToken.None);

        var row = await ReadRowAsync(schema.ConnectionString, h.SessionId, reply);
        row.Status.ShouldBe(QueuedMessageStatus.Sent, "an idle live session takes a WhenIdle message at once");
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        TypedCount(peer, reply).ShouldBe(1);
        stop.Cancel();
    }

    // The same evening: send-now on that Pending message answered 409 "... is not live".
    [Test]
    public async Task Send_now_to_an_idle_phone_home_session_is_not_a_409()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        peer.Sessions.Add(RunningOnRunner(h.SessionId));
        EchoSubmittedPromptsToTranscript(peer, h);
        const string reply = "CARD-0679 send-now to an idle phone-home session";
        await h.Queue.EnqueueAsync(h.SessionId, reply, MessageSendMode.WhenIdle, CancellationToken.None);
        var pending = await ReadRowAsync(schema.ConnectionString, h.SessionId, reply);
        pending.Status.ShouldBe(QueuedMessageStatus.Pending);
        using var stop = new CancellationTokenSource();
        (await Pump(host, h).RunCycleAsync(stop.Token)).ShouldBeTrue();

        ConflictException? refused = null;
        try
        {
            await h.Queue.SendNowAsync(h.SessionId, pending.Id, CancellationToken.None);
        }
        catch (ConflictException ex)
        {
            refused = ex;
        }

        refused.ShouldBeNull($"send-now refused: {refused?.Message}");
        var row = await ReadRowAsync(schema.ConnectionString, h.SessionId, reply);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        TypedCount(peer, reply).ShouldBe(1);
        stop.Cancel();
    }

    // Review 57fa2e6a finding 1. The runner can write a session's exit event before the launch
    // ack's continuation runs on the desktop: the pump records the exit, then the ack marked the
    // dead session live again, and every ListLiveSessions() caller typed into it. Held here so the
    // order is exact: the Launch is answered only after the pump has released the exit.
    [Test]
    public async Task Exit_seen_before_the_launch_ack_keeps_that_generation_dead()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(clock, schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        var pump = Pump(host, h);
        using var stop = new CancellationTokenSource();
        (await pump.RunCycleAsync(stop.Token)).ShouldBeTrue();
        var launched = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        peer.HeldLaunches = 1;

        var start = host.Directory.Resolve(host.AllowedRunnerId)
            .StartAsync(launched, RawSpec(generation), CancellationToken.None);
        await peer.WaitForAsync(PhoneHomeOperation.Launch);
        var received = live.LiveBufferEvents;
        await peer.EmitAsync(Event(live, SessionRunnerEventNames.SessionExited,
            new RunnerSessionExitedEvent(launched, 1, nameof(AgentExitReason.Unknown), 0, generation)));
        await WaitUntilAsync(() => live.LiveBufferEvents == received + 1 && live.PendingEvents == 0);
        peer.ReleaseHeld();
        var started = await start;

        started.Status.ShouldBe("Running", "the ack itself still reads Running: it was written before the exit");
        h.Runtime.ListLiveSessions().ShouldNotContain(launched,
            "an ack for a generation whose exit was already seen does not bring it back");

        // A List of a runner row still naming that generation Running is held to the same guard;
        // the marker session shows the refresh's answer was applied.
        var marker = Guid.NewGuid();
        peer.Sessions.Add(RunningOnRunner(marker));
        await RefreshAsync(clock, peer, pump, stop.Token);
        await WaitUntilAsync(() => h.Runtime.ListLiveSessions().Contains(marker));
        h.Runtime.ListLiveSessions().ShouldNotContain(launched, "the List may not resurrect an exited generation");

        // The tombstone is per generation: a relaunch of the same session is live again.
        peer.Sessions.RemoveAll(s => s.SessionId == launched);
        peer.Sessions.Add(new RunnerSessionDto(launched, 2, DateTime.UtcNow, "Running", null, "", 0,
            AcceptedStartedAt: SessionGeneration.Next(generation, DateTime.UtcNow)));
        await RefreshAsync(clock, peer, pump, stop.Token);
        await WaitUntilAsync(() => h.Runtime.ListLiveSessions().Contains(launched));
        stop.Cancel();
    }

    // Review 57fa2e6a finding 2. Heartbeats kept the lease valid while every refresh List failed,
    // so a session the runner had long since lost stayed "live" to every caller, forever.
    [Test]
    public async Task Inventory_entries_stop_counting_as_live_when_refreshes_keep_failing()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(clock, schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        var quiet = Guid.NewGuid();
        var talking = Guid.NewGuid();
        peer.Sessions.Add(RunningOnRunner(quiet));
        peer.Sessions.Add(RunningOnRunner(talking));
        using var logs = LoggerFactory.Create(b => b.AddProvider(host.Logs));
        var pump = Pump(host, h, logs.CreateLogger<PhoneHomeRecoveryPump>());
        using var stop = new CancellationTokenSource();
        (await pump.RunCycleAsync(stop.Token)).ShouldBeTrue();
        h.Runtime.ListLiveSessions().ShouldContain(quiet);
        peer.Reply = frame => frame.Operation == PhoneHomeOperation.List
            ? new PhoneHomeFrame(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                ErrorCode: "runner_internal_error", ErrorDetail: "List failed", StatusCode: 500)
            : null;
        var interval = TimeSpan.FromSeconds(new PhoneHomeRunnerSettings().InventoryRefreshSeconds);

        for (var failed = 1; failed <= 4; failed++)
        {
            clock.Advance(interval);
            await HeartbeatAsync(peer, live, clock);
            if (failed >= 2)
            {
                // Output is the runner vouching for the session between Lists.
                var received = live.LiveBufferEvents;
                await peer.EmitAsync(Event(live, SessionRunnerEventNames.SessionOutput,
                    new RunnerOutputEvent(talking, failed, "still here")));
                await WaitUntilAsync(() => live.LiveBufferEvents == received + 1 && live.PendingEvents == 0);
            }

            (await pump.RunCycleAsync(stop.Token)).ShouldBeFalse();
            var expected = failed;
            await WaitUntilAsync(() => RefreshFailureLogs(host) == expected);
            if (failed == 2)
                h.Runtime.ListLiveSessions().ShouldContain(quiet, "two missed refreshes are inside the bound");
        }

        host.Directory.DeclaredCapacity(host.AllowedRunnerId).ShouldNotBeNull("heartbeats kept the lease valid");
        var listed = h.Runtime.ListLiveSessions();
        listed.ShouldNotContain(quiet, "unconfirmed for four refresh intervals: past the stale bound");
        listed.ShouldContain(talking, "its output confirmed it this interval");
        // Review 87af1bf6: not live is not gone. Past the bound it is unknown, not dead.
        h.Runtime.ListUnknownSessions().ShouldContain(quiet);
        h.Runtime.ListUnknownSessions().ShouldNotContain(talking);
        host.Logs.Entries.Any(e => e.Level == LogLevel.Warning && e["ConsecutiveFailures"] is int n && n >= 3)
            .ShouldBeTrue("repeated refresh failures are called out, not just logged one by one");

        // One successful List confirms it again.
        peer.Reply = null;
        clock.Advance(interval);
        await HeartbeatAsync(peer, live, clock);
        (await pump.RunCycleAsync(stop.Token)).ShouldBeFalse();
        await WaitUntilAsync(() => h.Runtime.ListLiveSessions().Contains(quiet));
        h.Runtime.ListUnknownSessions().ShouldNotContain(quiet);
        stop.Cancel();
    }

    // Review 87af1bf6 finding 1. A session no List, ack or event confirmed for the stale bound is
    // not known to be live, but nothing says it is gone either: the runner may simply be slow to
    // answer a List while it keeps the session running. Card reconciliation read "not listed" as
    // "not found" and failed the session, canceled its attempt and cleared the card's claim, with
    // the runner process still running.
    [Test]
    public async Task Quiet_session_past_the_stale_bound_keeps_its_card_claim()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(clock, schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        var (cardId, attemptId) = await ClaimCardAsync(h, schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        peer.Sessions.Add(RunningOnRunner(h.SessionId));
        var live = await host.WaitLiveAsync();
        var pump = Pump(host, h);
        using var stop = new CancellationTokenSource();
        (await pump.RunCycleAsync(stop.Token)).ShouldBeTrue();
        h.Runtime.ListLiveSessions().ShouldContain(h.SessionId);

        await FailRefreshesPastTheStaleBoundAsync(clock, host, peer, live, pump, stop.Token);
        h.Runtime.ListLiveSessions().ShouldNotContain(h.SessionId, "unconfirmed past the stale bound: not known live");

        await ReconcileCardsAsync(h);

        await AssertClaimSurvivesAsync(schema.ConnectionString, h.SessionId, cardId, attemptId,
            "a session the runner has not been heard to lose is not missing");
        stop.Cancel();
    }

    [Test]
    public async Task Quiet_session_past_the_stale_bound_still_gets_its_stranded_prompt()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(clock, schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        peer.Sessions.Add(RunningOnRunner(h.SessionId));
        EchoSubmittedPromptsToTranscript(peer, h);
        var live = await host.WaitLiveAsync();
        const string brief = "CARD-0679 stranded brief for a quiet phone-home session";
        await h.Queue.EnqueueAsync(h.SessionId, brief, MessageSendMode.WhenIdle, CancellationToken.None,
            origin: QueuedMessageOrigin.Delegation);
        (await ReadRowAsync(schema.ConnectionString, h.SessionId, brief)).Status.ShouldBe(QueuedMessageStatus.Pending);
        var pump = Pump(host, h);
        using var stop = new CancellationTokenSource();
        (await pump.RunCycleAsync(stop.Token)).ShouldBeTrue();

        await FailRefreshesPastTheStaleBoundAsync(clock, host, peer, live, pump, stop.Token);
        h.Runtime.ListLiveSessions().ShouldNotContain(h.SessionId, "unconfirmed past the stale bound: not known live");

        var flushed = await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        flushed.ShouldBe(1, "a session not known to be gone keeps its queued work moving");
        TypedCount(peer, brief).ShouldBe(1);
        var row = await ReadRowAsync(schema.ConnectionString, h.SessionId, brief);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        stop.Cancel();
    }

    // The same rule across a reconnect: between a connection's end and the next connection's
    // catch-up List nothing confirms a session either way. Only that List, from a connected
    // runner, is the confirmed absence reconciliation may act on.
    [Test]
    public async Task A_reconnect_gap_is_not_a_confirmed_absence_but_the_next_catch_up_list_is()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        var (cardId, attemptId) = await ClaimCardAsync(h, schema.ConnectionString);
        await using var peerA = await host.ConnectPeerAsync();
        peerA.Sessions.Add(RunningOnRunner(h.SessionId));
        var liveA = await host.WaitLiveAsync();
        var pump = Pump(host, h);
        using var stop = new CancellationTokenSource();
        (await pump.RunCycleAsync(stop.Token)).ShouldBeTrue();
        h.Runtime.ListLiveSessions().ShouldContain(h.SessionId);

        peerA.Socket.Abort();
        await WaitUntilAsync(() => !liveA.SocketOpen);
        h.Runtime.ListLiveSessions().ShouldNotContain(h.SessionId, "a closed connection vouches for nothing");

        await ReconcileCardsAsync(h);
        await AssertClaimSurvivesAsync(schema.ConnectionString, h.SessionId, cardId, attemptId,
            "a dropped connection is not the runner saying the session is gone");

        // The runner comes back without it: that List is authoritative.
        await using var peerB = await host.ConnectPeerAsync();
        var liveB = await host.WaitLiveAsync();
        liveB.ShouldNotBeSameAs(liveA);
        (await pump.RunCycleAsync(stop.Token)).ShouldBeTrue("the catch-up List ran on the new connection");

        await ReconcileCardsAsync(h);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == h.SessionId)).Status
            .ShouldBe(SessionStatus.Failed, "absent from a connected runner's List: confirmed gone");
        (await db.Cards.AsNoTracking().SingleAsync(c => c.Id == cardId)).OwnerSessionId.ShouldBeNull();
        stop.Cancel();
    }

    // A desktop restart: no connection has answered for the runner yet, so the directory knows
    // nothing either way about the sessions bound to it. Their liveness is unknown until the
    // runner's first catch-up List.
    [Test]
    public async Task Before_the_runner_first_recovers_its_bound_sessions_are_not_missing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        var (cardId, attemptId) = await ClaimCardAsync(h, schema.ConnectionString);
        h.Runtime.ListLiveSessions().ShouldNotContain(h.SessionId);

        await ReconcileCardsAsync(h);
        await AssertClaimSurvivesAsync(schema.ConnectionString, h.SessionId, cardId, attemptId,
            "no runner has answered since start: nothing confirms the session gone");

        await using var peer = await host.ConnectPeerAsync();
        using var stop = new CancellationTokenSource();
        (await Pump(host, h).RunCycleAsync(stop.Token)).ShouldBeTrue("the first catch-up List answered");

        await ReconcileCardsAsync(h);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == h.SessionId)).Status
            .ShouldBe(SessionStatus.Failed, "absent from a connected runner's List: confirmed gone");
        (await db.Cards.AsNoTracking().SingleAsync(c => c.Id == cardId)).OwnerSessionId.ShouldBeNull();
        stop.Cancel();
    }

    // Review f87b49a7. In that same window IsLiveOrUnknown saw the session but
    // ListLiveOrUnknownSessions() had no id for it: there is no inventory yet. A WhenTargetDown=Skip
    // fire tested the list, read "down" and was skipped for good, with nothing having said the
    // session was gone. The desktop's own binding record is what names it until the runner lists.
    [Test]
    public async Task A_desktop_restart_before_the_first_List_does_not_skip_a_scheduled_fire()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        var ended = await InsertRunnerBoundSessionAsync(schema.ConnectionString, host, SessionStatus.Failed);
        const string prompt = "CARD-0679 scheduled prompt across a desktop restart";
        var scheduleId = await SeedSkipWhenDownPromptAsync(schema.ConnectionString, h.AgentId, prompt);
        h.Runtime.ListLiveSessions().ShouldNotContain(h.SessionId);

        await FireNowAsync(h, scheduleId);

        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var fire = await db.ScheduleFires.AsNoTracking().SingleAsync(f => f.ScheduleId == scheduleId);
            fire.Outcome.ShouldBe(ScheduleFireOutcome.Enqueued, $"fire detail: {fire.Detail}");
            fire.QueuedMessageId.ShouldNotBeNull();
        }
        var queued = await ReadRowContainingAsync(schema.ConnectionString, h.SessionId, prompt);
        queued.Status.ShouldBe(QueuedMessageStatus.Pending, "nothing can reach the runner yet, so it waits");
        h.Runtime.ListLiveOrUnknownSessions().ShouldContain(h.SessionId,
            "bound to the runner, not seen to end, and no runner has listed it either way");
        h.Runtime.ListLiveOrUnknownSessions().ShouldNotContain(ended, "the desktop already saw this one end");

        await using var peer = await host.ConnectPeerAsync();
        peer.Sessions.Add(RunningOnRunner(h.SessionId));
        EchoSubmittedPromptsToTranscript(peer, h);
        using var stop = new CancellationTokenSource();
        (await Pump(host, h).RunCycleAsync(stop.Token)).ShouldBeTrue("the first catch-up List answered");

        (await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None)).ShouldBe(1);

        TypedCount(peer, prompt).ShouldBe(1, "the fire reached the session once the runner was back");
        (await ReadRowContainingAsync(schema.ConnectionString, h.SessionId, prompt)).Status
            .ShouldBe(QueuedMessageStatus.Sent);
        stop.Cancel();
    }

    // Review f87b49a7, channel targets: both the direct send and the @mention lookup read the
    // list, so in the window the session did not exist (NotFound; "No live target matched").
    // Explicit channel input remains immediate and retryable; mentions are accepted durably.
    [Test]
    public async Task A_desktop_restart_before_the_first_List_does_not_lose_a_channel_target()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        var (cardId, _) = await ClaimCardAsync(h, schema.ConnectionString);
        var source = await InsertMentionSourceAsync(schema.ConnectionString, cardId);
        const string direct = "CARD-0679 channel message across a desktop restart";
        const string mentioned = "CARD-0679 mention across a desktop restart";
        var mention = new AgentMention("fake", mentioned);

        await using (var scope = h.Provider.CreateAsyncScope())
        {
            var channel = scope.ServiceProvider.GetRequiredService<AgentChannelService>();
            var sendRefused = await CaptureAsync(() => channel.SendToSessionAsync(null, h.SessionId, direct, CancellationToken.None));
            sendRefused.ShouldBeOfType<ServiceUnavailableException>(
                $"the target exists; only its runner is not back yet: {sendRefused}")
                .Code.ShouldBe(PhoneHomeProblemTypes.Unavailable);
            (await channel.RouteMentionAsync(source, mention, CancellationToken.None)).ShouldBeTrue();
            (await ReadRowContainingAsync(schema.ConnectionString, h.SessionId, mentioned)).Status.ShouldBe(QueuedMessageStatus.Pending);
        }

        await using var peer = await host.ConnectPeerAsync();
        peer.Sessions.Add(RunningOnRunner(h.SessionId));
        EchoSubmittedPromptsToTranscript(peer, h);
        using var stop = new CancellationTokenSource();
        (await Pump(host, h).RunCycleAsync(stop.Token)).ShouldBeTrue("the first catch-up List answered");

        await using (var scope = h.Provider.CreateAsyncScope())
        {
            var channel = scope.ServiceProvider.GetRequiredService<AgentChannelService>();
            await channel.SendToSessionAsync(null, h.SessionId, direct, CancellationToken.None);
            await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        }

        TypedCount(peer, direct).ShouldBe(1);
        TypedCount(peer, mentioned).ShouldBe(1);
        stop.Cancel();
    }

    // Review f87b49a7, send-now: the same list answered 409 "is not live" in the window, a
    // refusal that says the session is gone. The message stays queued and the refusal says why.
    [Test]
    public async Task A_desktop_restart_before_the_first_List_does_not_409_a_send_now()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        const string reply = "CARD-0679 send-now across a desktop restart";
        await h.Queue.EnqueueAsync(h.SessionId, reply, MessageSendMode.WhenIdle, CancellationToken.None);
        var pending = await ReadRowAsync(schema.ConnectionString, h.SessionId, reply);
        pending.Status.ShouldBe(QueuedMessageStatus.Pending);

        var refused = await CaptureAsync(() => h.Queue.SendNowAsync(h.SessionId, pending.Id, CancellationToken.None));

        refused.ShouldNotBeOfType<ConflictException>($"send-now answered 409: {refused?.Message}");
        refused.ShouldBeOfType<ServiceUnavailableException>($"{refused}").Code.ShouldBe(PhoneHomeProblemTypes.Unavailable);
        var held = await ReadRowAsync(schema.ConnectionString, h.SessionId, reply);
        held.Status.ShouldBe(QueuedMessageStatus.Pending, "the message stays queued");
        held.DeliveryAttempts.ShouldBe(pending.DeliveryAttempts, "nothing left the desktop, so no attempt is charged");

        await using var peer = await host.ConnectPeerAsync();
        peer.Sessions.Add(RunningOnRunner(h.SessionId));
        EchoSubmittedPromptsToTranscript(peer, h);
        using var stop = new CancellationTokenSource();
        (await Pump(host, h).RunCycleAsync(stop.Token)).ShouldBeTrue("the first catch-up List answered");

        await h.Queue.SendNowAsync(h.SessionId, pending.Id, CancellationToken.None);

        var row = await ReadRowAsync(schema.ConnectionString, h.SessionId, reply);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        TypedCount(peer, reply).ShouldBe(1);
        stop.Cancel();
    }

    // Review 87af1bf6 finding 2. The guard against an exited generation coming back was a
    // tombstone kept for ten minutes. A reply already received (its request stamped before the
    // exit) whose continuation runs after that, under thread-pool starvation say, found no
    // tombstone and made the dead session live again. The continuation is modelled here by the
    // exact calls PhoneHomeRunnerClient.StartAsync and the refresh List make with that stamp.
    [Test]
    public async Task A_late_reply_continuation_cannot_revive_an_exited_generation_however_late()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(clock, schema.ConnectionString);
        await using var h = await CreateRunnerBoundHarnessAsync(host, schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        var pump = Pump(host, h);
        using var stop = new CancellationTokenSource();
        (await pump.RunCycleAsync(stop.Token)).ShouldBeTrue();
        var launched = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);

        // The Launch (and a List) went out, then the runner wrote the session's exit.
        var sent = live.BeginInventoryRead();
        var received = live.LiveBufferEvents;
        await peer.EmitAsync(Event(live, SessionRunnerEventNames.SessionExited,
            new RunnerSessionExitedEvent(launched, 1, nameof(AgentExitReason.Unknown), 0, generation)));
        await WaitUntilAsync(() => live.LiveBufferEvents == received + 1 && live.PendingEvents == 0);

        // An hour on, with the connection kept up and its inventory refreshed in between.
        clock.Advance(TimeSpan.FromHours(1));
        await HeartbeatAsync(peer, live, clock);
        var marker = Guid.NewGuid();
        peer.Sessions.Add(RunningOnRunner(marker));
        await RefreshAsync(clock, peer, pump, stop.Token);
        await WaitUntilAsync(() => h.Runtime.ListLiveSessions().Contains(marker));

        // Now the replies' continuations run.
        live.NoteSessionLive(launched, generation, sent)
            .ShouldBeFalse("the ack names a generation whose exit was already seen");
        live.ReplaceKnownLiveSessions([(marker, (DateTime?)DateTime.UtcNow), (launched, generation)], sent);

        var listed = h.Runtime.ListLiveSessions();
        listed.ShouldNotContain(launched, "no reply sent before the exit may make that generation live again");
        listed.ShouldContain(marker);
        stop.Cancel();
    }

    private static AgentLaunchSpec RawSpec(DateTime? acceptedStartedAt = null) => new(
        DefinitionName: "raw",
        Kind: AgentKind.Raw,
        Exe: "/bin/sh",
        Args: [],
        Env: new Dictionary<string, string>(),
        Cwd: "/work",
        Cols: 120,
        Rows: 30,
        AcceptedStartedAt: acceptedStartedAt);

    private static PhoneHomeFrame Event(PhoneHomeLiveConnection live, string name, object payload) => new(
        PhoneHomeFrameKind.Event, live.Epoch, Guid.Empty, EventName: name,
        Payload: JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));

    /// <summary>One refresh interval later: the next cycle sends a refresh List.</summary>
    private static async Task RefreshAsync(
        FakeTimeProvider clock, PhoneHomeScriptedPeer peer, PhoneHomeRecoveryPump pump, CancellationToken ct)
    {
        var lists = peer.RequestCount(PhoneHomeOperation.List);
        clock.Advance(TimeSpan.FromSeconds(new PhoneHomeRunnerSettings().InventoryRefreshSeconds));
        (await pump.RunCycleAsync(ct)).ShouldBeFalse();
        await WaitUntilAsync(() => peer.RequestCount(PhoneHomeOperation.List) == lists + 1);
    }

    private static async Task HeartbeatAsync(PhoneHomeScriptedPeer peer, PhoneHomeLiveConnection live, FakeTimeProvider clock)
    {
        await peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Heartbeat, live.Epoch, Guid.NewGuid()));
        var now = clock.GetUtcNow();
        await WaitUntilAsync(() => live.LastHeartbeatUtc == now);
    }

    /// <summary>
    /// Every refresh List fails (heartbeats keep the lease) for one interval more than the stale
    /// bound, so nothing confirms the cached inventory; every other request keeps its reply.
    /// </summary>
    private static async Task FailRefreshesPastTheStaleBoundAsync(
        FakeTimeProvider clock, PhoneHomeTestHost host, PhoneHomeScriptedPeer peer, PhoneHomeLiveConnection live,
        PhoneHomeRecoveryPump pump, CancellationToken ct)
    {
        var settings = new PhoneHomeRunnerSettings();
        var replies = peer.Reply;
        peer.Reply = frame => frame.Operation == PhoneHomeOperation.List
            ? new PhoneHomeFrame(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                ErrorCode: "runner_internal_error", ErrorDetail: "List failed", StatusCode: 500)
            : replies?.Invoke(frame);
        for (var failed = 1; failed <= settings.InventoryStaleAfterRefreshes + 1; failed++)
        {
            var lists = peer.RequestCount(PhoneHomeOperation.List);
            clock.Advance(TimeSpan.FromSeconds(settings.InventoryRefreshSeconds));
            await HeartbeatAsync(peer, live, clock);
            (await pump.RunCycleAsync(ct)).ShouldBeFalse();
            await WaitUntilAsync(() => peer.RequestCount(PhoneHomeOperation.List) == lists + 1);
        }

        host.Directory.DeclaredCapacity(host.AllowedRunnerId).ShouldNotBeNull("heartbeats kept the lease valid");
    }

    /// <summary>
    /// The harness session owns a card mid-turn, the shape card reconciliation probes: a Running
    /// session, its streaming attempt, and the card's claim on it.
    /// </summary>
    internal static async Task<(Guid CardId, Guid AttemptId)> ClaimCardAsync(BridgeQueueHarness h, string connectionString)
    {
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = $"c679-{cardId:N}",
            GitRepositoryUrl = "https://example.invalid/repo.git",
            BaseBranch = "master",
            // The harness's orchestrator reconciles internal boards under its temp root only.
            LocalRepositoryPath = h.TempRoot,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = $"board-{cardId:N}", CreatedAt = now, UpdatedAt = now });
        db.BoardColumns.Add(new BoardColumn
        {
            Id = columnId,
            BoardId = boardId,
            StateKey = "in-progress",
            Name = "In Progress",
            ColumnOrder = 1,
            CardStatus = CardStatus.InProgress,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Cards.Add(new Card
        {
            Id = cardId,
            BoardId = boardId,
            BoardColumnId = columnId,
            Identifier = $"C679-{cardId.ToString("N")[..8]}",
            Title = "phone-home owned card",
            Description = "seeded",
            Status = CardStatus.InProgress,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        await db.AgentSessions.Where(s => s.Id == h.SessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.CardId, cardId));
        db.RunAttempts.Add(new RunAttempt
        {
            Id = attemptId,
            CardId = cardId,
            AgentSessionId = h.SessionId,
            AttemptNumber = 1,
            Phase = RunPhase.StreamingTurn,
            CreatedAt = now,
            StartedAt = now,
            LastEventAt = now,
            PhaseStartedAt = now,
            Prompt = "phone-home turn in flight",
        });
        await db.SaveChangesAsync();
        await db.Cards.Where(c => c.Id == cardId).ExecuteUpdateAsync(u => u
            .SetProperty(c => c.OwnerSessionId, h.SessionId)
            .SetProperty(c => c.ConcurrencyToken, Guid.NewGuid()));
        return (cardId, attemptId);
    }

    /// <summary>One orchestrator tick: its reconcile step probes every claimed card's session.</summary>
    private static async Task ReconcileCardsAsync(BridgeQueueHarness h)
    {
        await using var scope = h.Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OrchestratorService>().PollTickAsync(CancellationToken.None);
    }

    private static async Task AssertClaimSurvivesAsync(
        string connectionString, Guid sessionId, Guid cardId, Guid attemptId, string because)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        session.Status.ShouldBe(SessionStatus.Running, because);
        session.FailureReason.ShouldBeNull(because);
        (await db.Cards.AsNoTracking().SingleAsync(c => c.Id == cardId)).OwnerSessionId.ShouldBe(sessionId, because);
        (await db.RunAttempts.AsNoTracking().SingleAsync(a => a.Id == attemptId)).Phase.ShouldBe(RunPhase.StreamingTurn, because);
    }

    private static int RefreshFailureLogs(PhoneHomeTestHost host) => host.Logs.Entries.Count(e =>
        e.Level == LogLevel.Warning
        && e.Message.StartsWith("Phone-home inventory refresh List", StringComparison.Ordinal));

    /// <summary>
    /// The harness's session, re-bound to the phone-home runner and stripped of its in-process
    /// adapter (which the runtime always reports live): its liveness is the runner's alone, and
    /// every byte typed into it crosses the real transport.
    /// </summary>
    private static async Task<BridgeQueueHarness> CreateRunnerBoundHarnessAsync(PhoneHomeTestHost host, string connectionString)
    {
        var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = connectionString,
            ConfigureServices = s =>
            {
                s.AddSingleton<ISessionRunnerDirectory>(host.Directory);
                s.AddSingleton<ISessionRunnerClient>(new RoutingSessionRunnerClient(host.Directory));
                // The list-based gates of review f87b49a7: schedule fires and channel targets.
                s.AddSingleton(Options.Create(new ScheduleSettings()));
                s.AddSingleton(Options.Create(new DigestSettings { TimeZone = "Europe/London" }));
                s.AddSingleton<ScheduleFireQueue>();
                s.AddSingleton<IScheduledCardActions>(new UnusedScheduledCardActions());
                s.AddScoped<ScheduleService>();
                s.AddScoped<AgentChannelService>();
            },
        });
        h.Runtime.TryRemove(h.SessionId, out _).ShouldBeTrue();
        await h.InsertTurnAsync("prior-observable-turn", "prior-answer");
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.RunnerId, host.AllowedRunnerId)
            .SetProperty(s => s.RunnerStoreId, host.StoreId)
            .SetProperty(s => s.RunnerCwd, "/work"));
        return h;
    }

    internal static PhoneHomeRecoveryPump Pump(
        PhoneHomeTestHost host, BridgeQueueHarness h, ILogger<PhoneHomeRecoveryPump>? logger = null) => new(
        host.Directory,
        Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true,
            AllowedRunnerId = host.AllowedRunnerId,
            StandingAgentId = Guid.NewGuid(),
            HostWorkspaceRoot = @"C:\work",
            SharedSecret = host.Secret,
        }),
        h.Provider.GetRequiredService<IServiceScopeFactory>(),
        logger ?? NullLogger<PhoneHomeRecoveryPump>.Instance);

    internal static RunnerSessionDto RunningOnRunner(Guid sessionId) =>
        new(sessionId, 1, DateTime.UtcNow, "Running", null, "", 0, AcceptedStartedAt: DateTime.UtcNow);

    /// <summary>
    /// The runner side of a real session's terminal: typed text shows in the composer on the
    /// snapshot, every input advances the output sequence, and text typed before an Enter becomes
    /// that turn's UserPrompt (and an immediate TurnEnd): the evidence the queue's delivery
    /// verification reads, all of it over the phone-home connection.
    /// </summary>
    internal static void EchoSubmittedPromptsToTranscript(
        PhoneHomeScriptedPeer peer, BridgeQueueHarness h, Guid? sessionId = null)
    {
        var id = sessionId ?? h.SessionId;
        var composer = new StringBuilder();
        var sequence = 0L;
        var generation = DateTime.UtcNow;
        peer.Reply = frame =>
        {
            switch (frame.Operation)
            {
                case PhoneHomeOperation.Input:
                    sequence++;
                    var input = ReadInput(frame);
                    if (!input.EndsWith('\r'))
                    {
                        composer.Append(input);
                        return null;
                    }

                    composer.Append(input[..^1]);
                    var prompt = Visible(composer);
                    composer.Clear();
                    if (prompt.Length == 0)
                        return null;
                    h.InsertTranscriptEntryAsync(
                            TranscriptKinds.UserPrompt, prompt, sessionId: id, timestamp: DateTime.UtcNow)
                        .GetAwaiter().GetResult();
                    h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn", sessionId: id)
                        .GetAwaiter().GetResult();
                    return null;
                case PhoneHomeOperation.Snapshot:
                    return Result(frame, new RunnerSnapshotDto(
                        id, Visible(composer), "> " + Visible(composer), sequence, generation));
                case PhoneHomeOperation.Buffer:
                    return Result(frame, new RunnerBufferDto(id, Visible(composer), sequence));
                case PhoneHomeOperation.Get:
                    return Result(frame, new RunnerSessionDto(
                        id, 1, generation, "Running", null, "", sequence, AcceptedStartedAt: generation));
                default:
                    return null;
            }
        };

        static string Visible(StringBuilder typed) =>
            typed.ToString().Replace("\u001b[200~", "").Replace("\u001b[201~", "");

        static PhoneHomeFrame Result(PhoneHomeFrame request, object payload) => new(
            PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));
    }

    private static string ReadInput(PhoneHomeFrame frame) =>
        frame.Payload is { } payload && payload.TryGetProperty("input", out var input)
            ? input.GetString() ?? ""
            : "";

    private static int TypedCount(PhoneHomeScriptedPeer peer, string body) =>
        peer.Inputs.ToArray().Count(f => ReadInput(f).Contains(body, StringComparison.Ordinal));

    private static async Task<SessionQueuedMessage> ReadRowAsync(string connectionString, Guid sessionId, string body)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        return await db.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(m => m.AgentSessionId == sessionId && m.Body == body);
    }

    private static async Task<SessionQueuedMessage> ReadRowContainingAsync(string connectionString, Guid sessionId, string text)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        return await db.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(m => m.AgentSessionId == sessionId && m.Body.Contains(text));
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>Another session the desktop bound to the runner, in <paramref name="status"/>.</summary>
    private static async Task<Guid> InsertRunnerBoundSessionAsync(
        string connectionString, PhoneHomeTestHost host, SessionStatus status)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        db.AgentSessions.Add(new AgentSession
        {
            Id = id,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = status,
            Cwd = "/work",
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            RunnerId = host.AllowedRunnerId,
            RunnerStoreId = host.StoreId,
            RunnerCwd = "/work",
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>A local session on the target's card: the source of an @mention.</summary>
    internal static async Task<Guid> InsertMentionSourceAsync(string connectionString, Guid cardId)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        db.AgentSessions.Add(new AgentSession
        {
            Id = id,
            CardId = cardId,
            DefinitionName = "c679-source",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = "/work",
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>A due one-shot prompt to the harness agent whose policy skips a down target.</summary>
    internal static async Task<Guid> SeedSkipWhenDownPromptAsync(string connectionString, Guid agentId, string prompt)
    {
        var now = DateTime.UtcNow;
        var dueAt = now.AddMinutes(-1);
        var schedule = new Schedule
        {
            Id = Guid.NewGuid(),
            Name = "Restart window",
            Kind = ScheduleKind.Prompt,
            Repeat = ScheduleRepeat.Once,
            TimeZoneId = "Europe/London",
            NextFireAt = dueAt,
            Enabled = true,
            MissedGraceMinutes = null,
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyToken = Guid.NewGuid(),
            AgentId = agentId,
            PromptText = prompt,
            WhenTargetDown = ScheduleWhenTargetDown.Skip,
            FireAt = dueAt,
            DaysOfWeek = 0,
        };
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        db.Schedules.Add(schedule);
        await db.SaveChangesAsync();
        return schedule.Id;
    }

    internal static async Task FireNowAsync(BridgeQueueHarness h, Guid scheduleId)
    {
        await using var scope = h.Provider.CreateAsyncScope();
        var schedules = scope.ServiceProvider.GetRequiredService<ScheduleService>();
        await schedules.FireNowAsync(scheduleId, CancellationToken.None);
        var queue = h.Provider.GetRequiredService<ScheduleFireQueue>();
        queue.TryDequeue(out var claim).ShouldBeTrue("the manual fire was claimed");
        await schedules.FireAsync(claim, CancellationToken.None);
    }

    internal sealed class UnusedScheduledCardActions : IScheduledCardActions
    {
        public Task<bool> ApplyAutomatedMoveAsync(
            Guid cardId, CardStatus target, string reason, string movedBy, CancellationToken ct) =>
            throw new InvalidOperationException("A prompt schedule fired a card action.");

        public Task<bool> ReleaseAutoDispatchHoldAsync(Guid cardId, string reason, string actor, CancellationToken ct) =>
            throw new InvalidOperationException("A prompt schedule fired a card action.");

        public Task<SpawnCardResult> SpawnAsync(Guid cardId, SpawnCardRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("A prompt schedule fired a card action.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        condition().ShouldBeTrue();
    }
}
