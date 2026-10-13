using System.Text.Json;
using Antiphon.Server.Application.Dtos;
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
/// CARD-1219: remote (phone-home) transcript-fault and transcript-bound frames through PhoneHomeRecoveryPump.
/// </summary>
[Category("Integration")]
public class PhoneHomeTranscriptIncidentTests
{
    private const string FaultDetail = "no prompt in it matches input delivered to this session";
    private const string FaultPath = "/home/a/.claude/projects/x/37512455.jsonl";
    private const string BoundPath = "/home/a/.codex/sessions/2026/10/13/rollout-abc.jsonl";

    private const string FaultMessage =
        "No transcript is bound to this session. AdoptionRefused: no prompt in it matches input delivered "
        + "to this session (candidate /home/a/.claude/projects/x/37512455.jsonl)";

    private const string BoundMessage =
        "Transcript bound by discovery: /home/a/.codex/sessions/2026/10/13/rollout-abc.jsonl";

    private static readonly DateTimeOffset Epoch0 = DateTimeOffset.Parse("2026-10-13T12:00:00Z");

    [Test]
    public async Task Remote_fault_frame_records_the_local_incident_row()
    {
        await using var f = await Fixture.StartAsync();
        var remote = await f.SeedRemoteAsync("grok-linux", f.Host.StoreId);
        await using var peer = await f.Host.ConnectPeerAsync();
        var live = await f.Host.WaitLiveAsync();
        using var cts = new CancellationTokenSource();
        var pumping = f.Pump.PumpEventsAsync(live, cts.Token);

        await EmitAsync(peer, live.Epoch, SessionRunnerEventNames.SessionTranscriptFault,
            new RunnerTranscriptFaultEvent(remote, "AdoptionRefused", FaultDetail, FaultPath, 184.5, 3));
        await WaitUntilAsync("fault row recorded",
            async () => await f.FaultRowsAsync(remote) >= 1 && live.PendingEvents == 0);

        (await f.FaultRowsAsync(remote)).ShouldBe(1);
        await using var db = f.Db();
        var row = await db.AgentIncidents.AsNoTracking()
            .SingleAsync(i => i.SessionId == remote && i.Kind == AgentIncidentKind.TranscriptBindFailed);
        row.AgentId.ShouldBe(f.H.AgentId);
        row.Severity.ShouldBe(AlertSeverity.Warning);
        row.FailureReason.ShouldBe("AdoptionRefused");
        row.ExitCode.ShouldBeNull();
        row.Message.ShouldBe(FaultMessage);
        var alert = await db.Alerts.AsNoTracking().SingleAsync(a => a.SessionId == remote);
        alert.DedupKey.ShouldBe($"supervisor:TranscriptBindFailed:{f.H.AgentId:D}");
        alert.Severity.ShouldBe(AlertSeverity.Warning);
        alert.AgentId.ShouldBe(f.H.AgentId);
        (await db.AgentIncidents.AsNoTracking()
            .CountAsync(i => i.SessionId == remote && i.Kind == AgentIncidentKind.TranscriptBindStuck)).ShouldBe(0);
        f.Pump.EventFailures(live).ShouldBe(0);
        f.Refused().ShouldBe(0);
        await StopAsync(cts, pumping);
    }

    [Test]
    public async Task Remote_bound_frame_records_an_info_incident_without_alert()
    {
        await using var f = await Fixture.StartAsync();
        var remote = await f.SeedRemoteAsync("grok-linux", f.Host.StoreId);
        await using var peer = await f.Host.ConnectPeerAsync();
        var live = await f.Host.WaitLiveAsync();
        using var cts = new CancellationTokenSource();
        var pumping = f.Pump.PumpEventsAsync(live, cts.Token);

        await EmitAsync(peer, live.Epoch, SessionRunnerEventNames.SessionTranscriptBound,
            new RunnerTranscriptBoundEvent(remote, BoundPath, "discovery"));
        await WaitUntilAsync("bound row recorded",
            async () => await f.BoundRowsAsync(remote) >= 1 && live.PendingEvents == 0);

        (await f.BoundRowsAsync(remote)).ShouldBe(1);
        await using var db = f.Db();
        var row = await db.AgentIncidents.AsNoTracking()
            .SingleAsync(i => i.SessionId == remote && i.Kind == AgentIncidentKind.TranscriptBoundByDiscovery);
        row.Severity.ShouldBe(AlertSeverity.Info);
        row.Message.ShouldBe(BoundMessage);
        row.FailureReason.ShouldBeNull();
        row.AgentId.ShouldBe(f.H.AgentId);
        (await f.AlertsForAsync(remote)).ShouldBe(0);
        f.Pump.EventFailures(live).ShouldBe(0);
        await StopAsync(cts, pumping);
    }

    [Test]
    public async Task Replayed_frames_across_a_reconnect_record_once()
    {
        await using var f = await Fixture.StartAsync();
        var remote = await f.SeedRemoteAsync("grok-linux", f.Host.StoreId);
        var f1 = new RunnerTranscriptFaultEvent(remote, "AdoptionRefused", FaultDetail, FaultPath, 0, 1);
        var b = new RunnerTranscriptBoundEvent(remote, BoundPath, "discovery");
        using var cts = new CancellationTokenSource();

        // (a) first delivery on connection A.
        var peer = await f.Host.ConnectPeerAsync();
        var live = await f.Host.WaitLiveAsync();
        var pumpingA = f.Pump.PumpEventsAsync(live, cts.Token);
        await EmitAsync(peer, live.Epoch, SessionRunnerEventNames.SessionTranscriptFault, f1);
        await EmitAsync(peer, live.Epoch, SessionRunnerEventNames.SessionTranscriptBound, b);
        await WaitUntilAsync("first fault and bound recorded", async () =>
            await f.FaultRowsAsync(remote) >= 1 && await f.BoundRowsAsync(remote) >= 1 && live.PendingEvents == 0);
        (await f.FaultRowsAsync(remote)).ShouldBe(1);
        (await f.BoundRowsAsync(remote)).ShouldBe(1);

        // (b) the socket drops; the runner reconnects on a new connection and epoch.
        peer.Socket.Abort();
        await WaitUntilAsync("connection A closed", () => Task.FromResult(!live.SocketOpen));
        await peer.DisposeAsync();
        await using var peerB = await f.Host.ConnectPeerAsync();
        await WaitUntilAsync("connection B live", () => Task.FromResult(
            f.Host.Directory.SnapshotLive() is { } l && !ReferenceEquals(l, live)));
        var liveB = f.Host.Directory.SnapshotLive()!;
        var pumpingB = f.Pump.PumpEventsAsync(liveB, cts.Token);

        // (c) the same two frames again on the new epoch.
        await EmitAsync(peerB, liveB.Epoch, SessionRunnerEventNames.SessionTranscriptFault, f1);
        await EmitAsync(peerB, liveB.Epoch, SessionRunnerEventNames.SessionTranscriptBound, b);
        await WaitUntilAsync("both replays refused",
            () => Task.FromResult(f.Refused() >= 2 && liveB.PendingEvents == 0));
        (await f.FaultRowsAsync(remote)).ShouldBe(1);
        (await f.BoundRowsAsync(remote)).ShouldBe(1);
        f.Refused().ShouldBe(2);

        // (d) the same frame twice more on the same epoch.
        await EmitAsync(peerB, liveB.Epoch, SessionRunnerEventNames.SessionTranscriptFault, f1);
        await EmitAsync(peerB, liveB.Epoch, SessionRunnerEventNames.SessionTranscriptFault, f1);
        await WaitUntilAsync("two more replays refused",
            () => Task.FromResult(f.Refused() >= 4 && liveB.PendingEvents == 0));
        (await f.FaultRowsAsync(remote)).ShouldBe(1);

        // (e) the fault with Repeat and UnboundSeconds advanced.
        await EmitAsync(peerB, liveB.Epoch, SessionRunnerEventNames.SessionTranscriptFault,
            f1 with { Repeat = 2, UnboundSeconds = 300 });
        await WaitUntilAsync("genuine repeat recorded",
            async () => await f.FaultRowsAsync(remote) >= 2 && liveB.PendingEvents == 0);

        (await f.FaultRowsAsync(remote)).ShouldBe(2);
        (await f.BoundRowsAsync(remote)).ShouldBe(1);
        await using var db = f.Db();
        (await db.Alerts.AsNoTracking()
            .CountAsync(a => a.DedupKey == $"supervisor:TranscriptBindFailed:{f.H.AgentId:D}")).ShouldBe(2);
        f.Refused().ShouldBe(4);
        f.Pump.EventFailures(liveB).ShouldBe(0);
        await StopAsync(cts, pumpingA, pumpingB);
    }

    [Test]
    public async Task Foreign_owner_transcript_events_never_record()
    {
        await using var f = await Fixture.StartAsync();
        var remote = await f.SeedRemoteAsync("grok-linux", f.Host.StoreId);
        // Two seeded foreign sessions and one unknown session.
        var foreignRunner = await f.SeedRemoteAsync("other-runner", Guid.NewGuid());
        var foreignStore = await f.SeedRemoteAsync("grok-linux", Guid.NewGuid());
        var unknown = Guid.NewGuid();
        await using var peer = await f.Host.ConnectPeerAsync();
        var live = await f.Host.WaitLiveAsync();
        using var cts = new CancellationTokenSource();
        var pumping = f.Pump.PumpEventsAsync(live, cts.Token);

        foreach (var sid in new[] { foreignRunner, foreignStore, unknown })
        {
            await EmitAsync(peer, live.Epoch, SessionRunnerEventNames.SessionTranscriptFault,
                new RunnerTranscriptFaultEvent(sid, "AdoptionRefused", FaultDetail, FaultPath, 184.5, 3));
            await EmitAsync(peer, live.Epoch, SessionRunnerEventNames.SessionTranscriptBound,
                new RunnerTranscriptBoundEvent(sid, BoundPath, "discovery"));
        }

        // Ordering sentinel: an output frame emitted after the six frames.
        await EmitOutputAsync(peer, remote, 1, live.Epoch);
        await WaitUntilAsync("sentinel observed",
            () => Task.FromResult(f.Deltas(remote) >= 1 && live.PendingEvents == 0));

        await using var db = f.Db();
        var sessions = new[] { foreignRunner, foreignStore, unknown };
        (await db.AgentIncidents.AsNoTracking()
            .CountAsync(i => i.SessionId != null && sessions.Contains(i.SessionId.Value))).ShouldBe(0);
        foreach (var sid in sessions)
            (await f.AlertsForAsync(sid)).ShouldBe(0, $"alerts for {sid}");
        f.Pump.EventFailures(live).ShouldBe(0);
        f.Refused().ShouldBe(0);
        await StopAsync(cts, pumping);
    }

    [Test]
    public async Task Incident_text_matches_the_local_lane()
    {
        await using var f = await Fixture.StartAsync();
        var a = await f.SeedRemoteAsync("grok-linux", f.Host.StoreId);
        var bSess = await f.SeedRemoteAsync("grok-linux", f.Host.StoreId);
        await using var peer = await f.Host.ConnectPeerAsync();
        var live = await f.Host.WaitLiveAsync();
        using var cts = new CancellationTokenSource();
        var pumping = f.Pump.PumpEventsAsync(live, cts.Token);

        // Remote lane, through the pump, for session A.
        await EmitAsync(peer, live.Epoch, SessionRunnerEventNames.SessionTranscriptFault,
            new RunnerTranscriptFaultEvent(a, "AdoptionRefused", FaultDetail, FaultPath, 184.5, 3));
        await EmitAsync(peer, live.Epoch, SessionRunnerEventNames.SessionTranscriptBound,
            new RunnerTranscriptBoundEvent(a, BoundPath, "discovery"));
        await WaitUntilAsync("remote rows recorded", async () =>
            await f.FaultRowsAsync(a) >= 1 && await f.BoundRowsAsync(a) >= 1 && live.PendingEvents == 0);

        // Local lane: direct service calls for session B, with matching diagnostic fields.
        var service = f.H.Provider.GetRequiredService<TranscriptBindingIncidentService>();
        await service.OnTranscriptFaultAsync(
            new SessionRunnerTranscriptFaultEvent(bSess, "AdoptionRefused", FaultDetail, FaultPath, 184.5, 3),
            CancellationToken.None);
        await service.OnHeuristicBindAsync(
            new SessionRunnerTranscriptBoundEvent(bSess, BoundPath, "discovery"), CancellationToken.None);

        await using var db = f.Db();
        foreach (var kind in new[] { AgentIncidentKind.TranscriptBindFailed, AgentIncidentKind.TranscriptBoundByDiscovery })
        {
            var remoteRow = await db.AgentIncidents.AsNoTracking().SingleAsync(i => i.SessionId == a && i.Kind == kind);
            var localRow = await db.AgentIncidents.AsNoTracking().SingleAsync(i => i.SessionId == bSess && i.Kind == kind);
            remoteRow.Kind.ShouldBe(localRow.Kind, $"{kind} Kind");
            remoteRow.Severity.ShouldBe(localRow.Severity, $"{kind} Severity");
            remoteRow.Message.ShouldBe(localRow.Message, $"{kind} Message");
            remoteRow.FailureReason.ShouldBe(localRow.FailureReason, $"{kind} FailureReason");
            remoteRow.ExitCode.ShouldBe(localRow.ExitCode, $"{kind} ExitCode");
        }

        (await db.AgentIncidents.AsNoTracking()
            .SingleAsync(i => i.SessionId == a && i.Kind == AgentIncidentKind.TranscriptBindFailed))
            .Message.ShouldBe(FaultMessage);
        (await db.AgentIncidents.AsNoTracking()
            .SingleAsync(i => i.SessionId == a && i.Kind == AgentIncidentKind.TranscriptBoundByDiscovery))
            .Message.ShouldBe(BoundMessage);
        var remoteAlert = await db.Alerts.AsNoTracking().SingleAsync(x => x.SessionId == a);
        var localAlert = await db.Alerts.AsNoTracking().SingleAsync(x => x.SessionId == bSess);
        remoteAlert.Title.ShouldBe("TranscriptBindFailed: agent supervision");
        localAlert.Title.ShouldBe("TranscriptBindFailed: agent supervision");
        remoteAlert.Severity.ShouldBe(localAlert.Severity);
        await StopAsync(cts, pumping);
    }

    private static Task EmitAsync<T>(PhoneHomeScriptedPeer peer, long epoch, string eventName, T record) =>
        // Default options: the runner hub's own (PascalCase) spelling.
        peer.EmitAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Event, epoch, Guid.Empty,
            EventName: eventName,
            Payload: JsonSerializer.SerializeToElement(record)));

    private static Task EmitOutputAsync(PhoneHomeScriptedPeer peer, Guid sessionId, long sequence, long epoch) =>
        peer.EmitAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Event, epoch, Guid.Empty,
            EventName: SessionRunnerEventNames.SessionOutput,
            Payload: JsonSerializer.SerializeToElement(
                new RunnerOutputEvent(sessionId, sequence, $"chunk-{sequence}"), PhoneHomeFraming.Json)));

    private static async Task WaitUntilAsync(string what, Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!await condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Timed out after 5 s waiting for: {what}");
            await Task.Delay(20);
        }
    }

    private static async Task StopAsync(CancellationTokenSource cts, params Task[] pumps)
    {
        cts.Cancel();
        foreach (var pump in pumps)
        {
            try { await pump; }
            catch (OperationCanceledException) { /* expected */ }
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public required BridgeQueueHarness H { get; init; }
        public required PhoneHomeTestHost Host { get; init; }
        public required CapturingLoggerProvider Logs { get; init; }
        public required ILoggerFactory Loggers { get; init; }
        public required PhoneHomeRecoveryPump Pump { get; init; }

        public static async Task<Fixture> StartAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var h = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = schema.ConnectionString,
                AlwaysOn = false,
                ConfigureServices = s => s.AddSingleton<TranscriptBindingIncidentService>(),
            });
            var clock = new FakeTimeProvider(Epoch0);
            var host = await PhoneHomeTestHost.StartAsync(clock, schema.ConnectionString);
            var logs = new CapturingLoggerProvider();
            var loggers = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
            // The replay window stays at its default (900 s).
            var pump = new PhoneHomeRecoveryPump(
                host.Directory,
                Options.Create(new PhoneHomeRunnerSettings
                {
                    Enabled = true, AllowedRunnerId = "grok-linux", StandingAgentId = Guid.NewGuid(),
                    HostWorkspaceRoot = @"C:\work", SharedSecret = host.Secret,
                }),
                h.Provider.GetRequiredService<IServiceScopeFactory>(),
                loggers.CreateLogger<PhoneHomeRecoveryPump>());
            return new Fixture { Schema = schema, H = h, Host = host, Logs = logs, Loggers = loggers, Pump = pump };
        }

        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

        /// <summary>A remote session for <paramref name="runnerId"/>/<paramref name="storeId"/>, owned by the harness agent through a task row.</summary>
        public async Task<Guid> SeedRemoteAsync(string runnerId, Guid storeId)
        {
            await using var db = Db();
            var now = DateTime.UtcNow;
            var session = new AgentSession
            {
                Id = Guid.NewGuid(),
                DefinitionName = "grok",
                AgentKind = AgentKind.Grok,
                Status = SessionStatus.Running,
                Cwd = @"C:\work",
                Cols = 80,
                Rows = 24,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                RunnerId = runnerId,
                RunnerStoreId = storeId,
                RunnerCwd = "/work",
            };
            var taskId = Guid.NewGuid();
            db.AgentSessions.Add(session);
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "remote transcript incident",
                Goal = "remote transcript incident",
                WorkingDirectory = @"C:\work",
                AgentId = H.AgentId,
                AgentSessionId = session.Id,
                AgentName = "task-delegate",
                Status = AgentTaskStatus.Dispatched,
                Ephemeral = true,
                CreatedAt = now,
                DispatchedAt = now,
            });
            await db.SaveChangesAsync();
            return session.Id;
        }

        public async Task<int> FaultRowsAsync(Guid sid)
        {
            await using var db = Db();
            return await db.AgentIncidents.AsNoTracking()
                .CountAsync(i => i.SessionId == sid && i.Kind == AgentIncidentKind.TranscriptBindFailed);
        }

        public async Task<int> BoundRowsAsync(Guid sid)
        {
            await using var db = Db();
            return await db.AgentIncidents.AsNoTracking()
                .CountAsync(i => i.SessionId == sid && i.Kind == AgentIncidentKind.TranscriptBoundByDiscovery);
        }

        public async Task<int> AlertsForAsync(Guid sid)
        {
            await using var db = Db();
            return await db.Alerts.AsNoTracking().CountAsync(a => a.SessionId == sid);
        }

        public int Refused() => Logs.Entries.Count(e =>
            e.Level == LogLevel.Debug && e.Message.Contains("is a replay inside", StringComparison.Ordinal));

        public int Deltas(Guid sid) => H.EventBus.PublishedEvents.Count(e =>
            e.EventName == "AgentTextDelta" && e.Group == AgentSessionGroups.Session(sid));

        public async ValueTask DisposeAsync()
        {
            Loggers.Dispose();
            await Host.DisposeAsync();
            await H.DisposeAsync();
            await Schema.DisposeAsync();
        }
    }
}
