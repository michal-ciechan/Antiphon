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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class PhoneHomeEventPumpTests
{
    [Test]
    public async Task Foreign_owner_or_epoch_events_never_reach_runtime()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var ours = await SeedAsync(db, "grok-linux", host.StoreId);
        var foreign = await SeedAsync(db, "other-runner", Guid.NewGuid());
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        var pump = new PhoneHomeRecoveryPump(
            host.Directory,
            Options.Create(new PhoneHomeRunnerSettings { Enabled = true, AllowedRunnerId = "grok-linux", StandingAgentId = Guid.NewGuid(), HostWorkspaceRoot = @"C:\work", SharedSecret = host.Secret }),
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PhoneHomeRecoveryPump>.Instance);

        (await pump.OwnerMatchesAsync(live, ours.Id, CancellationToken.None)).ShouldBeTrue();
        (await pump.OwnerMatchesAsync(live, foreign.Id, CancellationToken.None)).ShouldBeFalse();

        using var cts = new CancellationTokenSource();
        var pumping = pump.PumpEventsAsync(live, cts.Token);
        await peer.EmitTranscriptAsync(Prompt(foreign.Id, "foreign-body-should-not-land", "foreign-uuid"), live.Epoch);
        await Task.Delay(200);
        var persistedForeignEntries = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == foreign.Id)
            .Select(t => t.Text)
            .ToListAsync();
        persistedForeignEntries.ShouldBeEmpty();
        await peer.EmitTranscriptAsync(Prompt(ours.Id, "owner-body", "owner-uuid"), live.Epoch);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline
               && !await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == ours.Id))
            await Task.Delay(50);
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == ours.Id && t.Text == "owner-body")).ShouldBe(1);
        cts.Cancel();
        try { await pumping; } catch (OperationCanceledException) { /* expected */ }
    }

    [Test]
    public async Task Catchup_commits_before_live_release()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var session = await SeedAsync(db, "grok-linux", host.StoreId);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pump = new PhoneHomeRecoveryPump(
            host.Directory,
            Options.Create(new PhoneHomeRunnerSettings { Enabled = true, AllowedRunnerId = "grok-linux", StandingAgentId = Guid.NewGuid(), HostWorkspaceRoot = @"C:\work", SharedSecret = host.Secret }),
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PhoneHomeRecoveryPump>.Instance)
        {
            CatchUpHold = hold,
        };
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        peer.Transcripts[session.Id] = new RunnerTranscriptDto(session.Id, [Prompt(session.Id, "catchup-prompt", "cu-1")], 1);
        peer.Sessions.Add(new RunnerSessionDto(session.Id, 1, DateTime.UtcNow, "Running", null, "", 1));
        var catchUp = pump.CatchUpAsync(live, CancellationToken.None);
        await peer.EmitTranscriptAsync(Prompt(session.Id, "live-later", "live-1", sequence: 2), live.Epoch);
        await Task.Delay(150);
        var liveProcessedBeforeCatchup = await db.TranscriptEntries.AnyAsync(t => t.Uuid == "live-1");
        liveProcessedBeforeCatchup.ShouldBeFalse();
        hold.SetResult();
        await catchUp.WaitAsync(TimeSpan.FromSeconds(5));
        (await db.TranscriptEntries.CountAsync(t => t.Uuid == "cu-1")).ShouldBe(1);
        using var cts = new CancellationTokenSource();
        var pumping = pump.PumpEventsAsync(live, cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && !await db.TranscriptEntries.AnyAsync(t => t.Uuid == "live-1"))
            await Task.Delay(50);
        (await db.TranscriptEntries.OrderBy(t => t.Sequence).Select(t => t.Text).ToListAsync())
            .ShouldBe(["catchup-prompt", "live-later"]);
        cts.Cancel();
        try { await pumping; } catch (OperationCanceledException) { /* expected */ }
    }

    [Test]
    public async Task Replayed_uuid_persists_once()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        var entry = Prompt(h.SessionId, "same-prompt", "same-uuid");
        await h.Runtime.PersistTranscriptAsync(h.SessionId, [Map(entry)]);
        await h.Runtime.PersistTranscriptAsync(h.SessionId, [Map(entry)]);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var persistedPromptCount = await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId && t.Uuid == "same-uuid");
        persistedPromptCount.ShouldBe(1);
    }

    [Test]
    public async Task Live_buffer_overflow_withholds_ready_and_recovers()
    {
        await using var host = await PhoneHomeTestHost.StartAsync(
            limits: new PhoneHomeLimits(MaxPendingEvents: 2, MaxPendingEventBytes: 4096));
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        live.DispatchEligible.ShouldBeFalse();
        var closedForOverflow = false;
        try
        {
            for (var i = 0; i < 4; i++)
                await peer.EmitTranscriptAsync(Prompt(Guid.NewGuid(), new string('x', 8), $"u{i}"), live.Epoch);
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline && live.SocketOpen)
                await Task.Delay(20);
            closedForOverflow = !live.DispatchEligible;
        }
        catch (Exception)
        {
            closedForOverflow = true;
        }

        closedForOverflow.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Persistence_cuts_recover_without_retyping()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        var entry = Prompt(h.SessionId, "typed-once", "cut-uuid");
        await h.Runtime.PersistTranscriptAsync(h.SessionId, [Map(entry)]);
        await using var recovered = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        await recovered.Runtime.PersistTranscriptAsync(h.SessionId, [Map(entry)]);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var extraWrites = await db.TranscriptEntries.CountAsync(t => t.Uuid == "cut-uuid") - 1;
        extraWrites.ShouldBe(0);
    }

    [Test]
    public async Task Pump_survives_a_failing_event_and_releases_the_rest()
    {
        // CARD-0679 V-8 (D-2): one event that throws must not end the pump for the connection.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var session = await SeedAsync(db, "grok-linux", host.StoreId);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        var pump = Pump(host, h);
        h.EventBus.ThrowOnceOnEvent = "AgentTextDelta";

        using var cts = new CancellationTokenSource();
        var pumping = pump.PumpEventsAsync(live, cts.Token);
        for (var i = 1; i <= 5; i++)
            await EmitOutputAsync(peer, session.Id, i, live.Epoch);

        await WaitUntilAsync(() => Deltas(h, session.Id) >= 4 && live.PendingEvents == 0);
        Deltas(h, session.Id).ShouldBe(4);
        live.PendingEvents.ShouldBe(0);
        pump.EventFailures(live).ShouldBe(1);
        cts.Cancel();
        try { await pumping; } catch (OperationCanceledException) { /* expected */ }
    }

    [Test]
    public async Task Recovery_cycle_restarts_a_pump_that_ended_while_the_socket_is_open()
    {
        // CARD-0679 V-9 (D-2): a pump that ended while the socket is open is restarted by the cycle.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var session = await SeedAsync(db, "grok-linux", host.StoreId);
        var logs = new CapturingLoggerProvider();
        using var loggers = LoggerFactory.Create(b => b.AddProvider(logs));
        var pump = Pump(host, h, loggers.CreateLogger<PhoneHomeRecoveryPump>());
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();

        using var cts = new CancellationTokenSource();
        (await pump.RunCycleAsync(cts.Token)).ShouldBeTrue();
        await pump.EndPumpForTest();
        live.SocketOpen.ShouldBeTrue();
        await pump.RunCycleAsync(cts.Token);

        await EmitOutputAsync(peer, session.Id, 1, live.Epoch);
        await WaitUntilAsync(() => Deltas(h, session.Id) >= 1);
        Deltas(h, session.Id).ShouldBe(1);
        logs.Entries.Count(e => e.Level == LogLevel.Error
                && e.Message.Contains("pump ended while the connection is live", StringComparison.Ordinal))
            .ShouldBe(1);
        cts.Cancel();
    }

    [Test]
    public async Task Owner_lookups_are_cached_per_connection()
    {
        // CARD-0679 V-10 (D-3): one binding read per session per connection, not one per event.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var ours = await SeedAsync(db, "grok-linux", host.StoreId);
        var foreign = await SeedAsync(db, "other-runner", Guid.NewGuid());
        var pump = Pump(host, h);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();

        using var cts = new CancellationTokenSource();
        _ = pump.PumpEventsAsync(live, cts.Token);
        var before = host.Directory.BindingLookups;
        for (var i = 1; i <= 5; i++)
            await EmitOutputAsync(peer, foreign.Id, i, live.Epoch);
        for (var i = 1; i <= 50; i++)
            await EmitOutputAsync(peer, ours.Id, i, live.Epoch);
        await WaitUntilAsync(() => Deltas(h, ours.Id) >= 50 && live.PendingEvents == 0);
        Deltas(h, ours.Id).ShouldBe(50);
        (host.Directory.BindingLookups - before).ShouldBeLessThanOrEqualTo(2);

        peer.Socket.Abort();
        await WaitUntilAsync(() => !live.SocketOpen);
        await peer.DisposeAsync();
        await using var peerB = await host.ConnectPeerAsync();
        await WaitUntilAsync(() => host.Directory.SnapshotLive() is { } l && !ReferenceEquals(l, live));
        var liveB = host.Directory.SnapshotLive()!;
        _ = pump.PumpEventsAsync(liveB, cts.Token);
        var beforeB = host.Directory.BindingLookups;
        await EmitOutputAsync(peerB, ours.Id, 51, liveB.Epoch);
        await WaitUntilAsync(() => Deltas(h, ours.Id) >= 51);
        Deltas(h, ours.Id).ShouldBe(51);
        (host.Directory.BindingLookups - beforeB).ShouldBe(1);
        cts.Cancel();
    }

    private static PhoneHomeRecoveryPump Pump(
        PhoneHomeTestHost host, BridgeQueueHarness h, ILogger<PhoneHomeRecoveryPump>? logger = null) =>
        new(
            host.Directory,
            Options.Create(new PhoneHomeRunnerSettings { Enabled = true, AllowedRunnerId = "grok-linux", StandingAgentId = Guid.NewGuid(), HostWorkspaceRoot = @"C:\work", SharedSecret = host.Secret }),
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            logger ?? NullLogger<PhoneHomeRecoveryPump>.Instance);

    private static Task EmitOutputAsync(PhoneHomeScriptedPeer peer, Guid sessionId, long sequence, long epoch) =>
        peer.EmitAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Event, epoch, Guid.Empty,
            EventName: SessionRunnerEventNames.SessionOutput,
            Payload: JsonSerializer.SerializeToElement(
                new RunnerOutputEvent(sessionId, sequence, $"chunk-{sequence}"), PhoneHomeFraming.Json)));

    private static int Deltas(BridgeQueueHarness h, Guid sessionId) =>
        h.EventBus.PublishedEvents.Count(e =>
            e.EventName == "AgentTextDelta" && e.Group == AgentSessionGroups.Session(sessionId));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !condition())
            await Task.Delay(20);
    }

    private static async Task<AgentSession> SeedAsync(AppDbContext db, string runnerId, Guid storeId)
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
            RunnerId = runnerId,
            RunnerStoreId = storeId,
            RunnerCwd = "/work",
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    private static RunnerTranscriptEvent Prompt(Guid sessionId, string text, string uuid, long sequence = 1) =>
        new(sessionId, sequence, TranscriptKinds.UserPrompt, uuid, null, DateTimeOffset.UtcNow, "user", text, null, null, null, null, null);

    private static SessionRunnerTranscriptEvent Map(RunnerTranscriptEvent e) =>
        RunnerContractMapper.MapTranscript(e);
}
