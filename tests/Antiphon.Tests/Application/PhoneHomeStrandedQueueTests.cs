using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
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

        peer.Socket.Abort();
        await WaitUntilAsync(() => !live.SocketOpen);
        h.Runtime.ListLiveSessions().ShouldNotContain(h.SessionId, "a closed connection vouches for nothing");

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
        host.Logs.Entries.Any(e => e.Level == LogLevel.Warning && e["ConsecutiveFailures"] is int n && n >= 3)
            .ShouldBeTrue("repeated refresh failures are called out, not just logged one by one");

        // One successful List confirms it again.
        peer.Reply = null;
        clock.Advance(interval);
        await HeartbeatAsync(peer, live, clock);
        (await pump.RunCycleAsync(stop.Token)).ShouldBeFalse();
        await WaitUntilAsync(() => h.Runtime.ListLiveSessions().Contains(quiet));
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

    private static PhoneHomeRecoveryPump Pump(
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

    private static RunnerSessionDto RunningOnRunner(Guid sessionId) =>
        new(sessionId, 1, DateTime.UtcNow, "Running", null, "", 0, AcceptedStartedAt: DateTime.UtcNow);

    /// <summary>
    /// The runner side of a real session's terminal: typed text shows in the composer on the
    /// snapshot, every input advances the output sequence, and text typed before an Enter becomes
    /// that turn's UserPrompt (and an immediate TurnEnd): the evidence the queue's delivery
    /// verification reads, all of it over the phone-home connection.
    /// </summary>
    private static void EchoSubmittedPromptsToTranscript(PhoneHomeScriptedPeer peer, BridgeQueueHarness h)
    {
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
                    h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt, timestamp: DateTime.UtcNow)
                        .GetAwaiter().GetResult();
                    h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn").GetAwaiter().GetResult();
                    return null;
                case PhoneHomeOperation.Snapshot:
                    return Result(frame, new RunnerSnapshotDto(
                        h.SessionId, Visible(composer), "> " + Visible(composer), sequence, generation));
                case PhoneHomeOperation.Buffer:
                    return Result(frame, new RunnerBufferDto(h.SessionId, Visible(composer), sequence));
                case PhoneHomeOperation.Get:
                    return Result(frame, new RunnerSessionDto(
                        h.SessionId, 1, generation, "Running", null, "", sequence, AcceptedStartedAt: generation));
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        condition().ShouldBeTrue();
    }
}
