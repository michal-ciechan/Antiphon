using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0710 V-7. Recovery, inventory and disconnect stay on the runner that owns them.
/// </summary>
[Category("Integration")]
public sealed class MultiRunnerRecoveryTests
{
    [Test]
    public async Task Slow_A_does_not_block_B_recovery()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB));
        host.Capacity = 2;
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", secret: secretB);
        var pump = Pump(host);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pump.HoldCatchUp("runner-a", hold);
        var cycle = pump.RunCycleAsync(CancellationToken.None);

        var ready = await WaitUntilAsync(() => host.Directory.SnapshotLive("runner-b") is { DispatchEligible: true });
        ready.ShouldBeTrue("runner B becomes dispatch-eligible while runner A is still held");
        host.Directory.SnapshotLive("runner-a")!.DispatchEligible.ShouldBeFalse();
        hold.TrySetResult();
        (await cycle).ShouldBeTrue();
        host.Directory.SnapshotLive("runner-a")!.DispatchEligible.ShouldBeTrue();
        peerA.Epoch.ShouldBeGreaterThan(0);
        peerB.Epoch.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Disconnect_A_preserves_B_events()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB));
        host.Capacity = 2;
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", secret: secretB);
        var pump = Pump(host);
        (await pump.RunCycleAsync(CancellationToken.None)).ShouldBeTrue();
        var liveB = host.Directory.SnapshotLive("runner-b");
        liveB.ShouldNotBeNull();
        peerA.Socket.Abort();
        await WaitUntilAsync(() => host.Directory.SnapshotLive("runner-a") is null || !host.Directory.SnapshotLive("runner-a")!.SocketOpen);
        host.Directory.SnapshotLive("runner-b").ShouldBeSameAs(liveB);
        liveB.DispatchEligible.ShouldBeTrue();
        var before = peerB.RequestCount(PhoneHomeOperation.List);
        await host.Directory.GetInventoryAsync("runner-b", CancellationToken.None);
        peerB.RequestCount(PhoneHomeOperation.List).ShouldBeGreaterThan(before);
        peerA.RequestCount(PhoneHomeOperation.List).ShouldBeLessThanOrEqualTo(peerB.RequestCount(PhoneHomeOperation.List));
    }

    [Test]
    public async Task Pending_inventory_and_failures_are_per_runner()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString, configured: Pair(secretA, secretB));
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.AgentSessions.Add(Session(sessionA, "runner-a", now));
            db.AgentSessions.Add(Session(sessionB, "runner-b", now));
            await db.SaveChangesAsync();
        }

        var inventory = new PendingRunnerSessionInventory(
            host.App.Services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger.Instance);
        inventory.Read("runner-a").ShouldBe([sessionA]);
        inventory.Read("runner-b").ShouldBe([sessionB]);
    }

    [Test]
    public async Task Reconnect_replays_only_owned_transcripts()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(
            connectionString: schema.ConnectionString, configured: Pair(secretA, secretB));
        host.Capacity = 2;
        await using var runtime = TranscriptRuntime(schema.ConnectionString);
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.AgentSessions.Add(Session(sessionA, "runner-a", now, storeA));
            db.AgentSessions.Add(Session(sessionB, "runner-b", now, storeB));
            await db.SaveChangesAsync();
        }

        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA, storeId: storeA);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", secret: secretB, storeId: storeB);
        peerA.Sessions.Add(new RunnerSessionDto(sessionA, 1, now, "Running", null, "", 1));
        peerB.Sessions.Add(new RunnerSessionDto(sessionB, 1, now, "Running", null, "", 1));
        peerA.Transcripts[sessionA] = Transcript(sessionA);
        peerB.Transcripts[sessionB] = Transcript(sessionB);
        var pump = new PhoneHomeRecoveryPump(
            host.Directory,
            Options.Create(new PhoneHomeRunnerSettings { Enabled = true, CatchUpRetrySeconds = 30 }),
            runtime.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PhoneHomeRecoveryPump>.Instance);
        (await pump.RunCycleAsync(CancellationToken.None)).ShouldBeTrue();
        peerA.Socket.Abort();
        await WaitUntilAsync(() =>
            host.Directory.SnapshotLive("runner-a") is null || !host.Directory.SnapshotLive("runner-a")!.SocketOpen);
        await using var nextA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA, storeId: storeA);
        nextA.Sessions.Add(new RunnerSessionDto(sessionA, 1, now, "Running", null, "", 1));
        nextA.Transcripts[sessionA] = Transcript(sessionA);
        (await pump.RunCycleAsync(CancellationToken.None)).ShouldBeTrue();
        nextA.RequestCount(PhoneHomeOperation.Transcript).ShouldBeGreaterThan(0);
        nextA.Incoming.Any(frame =>
            frame.Operation == PhoneHomeOperation.Transcript
            && frame.Payload?.ToString().Contains(sessionB.ToString(), StringComparison.Ordinal) == true)
            .ShouldBeFalse("runner A's reconnect does not ask for runner B's session");
        peerB.Incoming.Any(frame =>
            frame.Operation == PhoneHomeOperation.Transcript
            && frame.Payload?.ToString().Contains(sessionA.ToString(), StringComparison.Ordinal) == true)
            .ShouldBeFalse();
    }

    [Test]
    public async Task Deferred_kill_and_slot_release_stay_on_owner()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB));
        host.Capacity = 2;
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", secret: secretB);
        host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: "runner-a"));
        host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: "runner-b"));
        var session = Guid.NewGuid();
        await host.Directory.Resolve("runner-a").KillAsync(session, CancellationToken.None);
        peerA.RequestCount(PhoneHomeOperation.ReleaseSlot).ShouldBeGreaterThan(0);
        peerB.RequestCount(PhoneHomeOperation.ReleaseSlot).ShouldBe(0);
    }

    [Test]
    public async Task Queued_delivery_survives_one_runner_restart()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB));
        host.Capacity = 2;
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA, autoReply: false);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", secret: secretB);
        host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: "runner-b"));
        var prompt = "CARD-0710 unique prompt " + Guid.NewGuid().ToString("N");
        var sessionB = Guid.NewGuid();
        await host.Directory.Resolve("runner-b").SendInputAsync(sessionB, prompt, CancellationToken.None);
        peerB.Inputs.Any(frame => frame.Payload?.ToString().Contains(prompt, StringComparison.Ordinal) == true).ShouldBeTrue();
        peerA.Inputs.ShouldBeEmpty();
        peerA.Socket.Abort();
        await using var nextA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA);
        host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: "runner-a"));
        nextA.Inputs.Any(frame => frame.Payload?.ToString().Contains(prompt, StringComparison.Ordinal) == true).ShouldBeFalse();
        peerB.Inputs.Count(frame => frame.Payload?.ToString().Contains(prompt, StringComparison.Ordinal) == true).ShouldBe(1);
    }

    [Test]
    [NotInParallel("MessageQueue")]
    public async Task Queued_user_prompt_reaches_each_runner_once()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(
            connectionString: schema.ConnectionString, configured: Pair(secretA, secretB));
        host.Capacity = 2;
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA, storeId: storeA);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", secret: secretB, storeId: storeB);
        await using var harness = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = services =>
            {
                services.AddSingleton<ISessionRunnerDirectory>(host.Directory);
                services.AddSingleton<ISessionRunnerClient>(new RoutingSessionRunnerClient(host.Directory));
            },
            ConfigureDeliveryVerification = verification =>
            {
                verification.TranscriptConfirmTimeoutSeconds = 2;
                verification.PostFailureConfirmGraceSeconds = 0;
                verification.PollIntervalMs = 40;
            },
        });
        harness.Runtime.TryRemove(harness.SessionId, out _).ShouldBeTrue();
        var sessionB = harness.SessionId;
        var sessionA = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            await db.AgentSessions.Where(s => s.Id == sessionB).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.RunnerId, "runner-b")
                .SetProperty(s => s.RunnerStoreId, storeB)
                .SetProperty(s => s.RunnerCwd, "/work/b"));
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionA,
                DefinitionName = "claude",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = "/work",
                Cols = 80,
                Rows = 24,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                RunnerId = "runner-a",
                RunnerStoreId = storeA,
                RunnerCwd = "/work/a",
            });
            await db.SaveChangesAsync();
        }

        peerA.Sessions.Add(Running(sessionA));
        peerB.Sessions.Add(Running(sessionB));
        EchoPrompts(peerA, harness, sessionA);
        EchoPrompts(peerB, harness, sessionB);
        await harness.InsertTurnAsync("prior-b", "done", sessionB);
        await harness.MarkWorkingAsync(sessionA);
        var promptA = "CARD-0710-A-" + Guid.NewGuid().ToString("N");
        var promptB = "CARD-0710-B-" + Guid.NewGuid().ToString("N");
        await harness.Queue.EnqueueAsync(sessionA, promptA, MessageSendMode.WhenIdle, CancellationToken.None);
        await harness.Queue.EnqueueAsync(sessionB, promptB, MessageSendMode.WhenIdle, CancellationToken.None);
        (await Pump(host, harness).RunCycleAsync(CancellationToken.None)).ShouldBeTrue();

        await harness.Queue.FlushIfIdleAsync(sessionA, CancellationToken.None);
        await harness.Queue.FlushIfIdleAsync(sessionB, CancellationToken.None);

        (await QueueStatusAsync(schema.ConnectionString, sessionA, promptA)).ShouldBe(QueuedMessageStatus.Pending);
        (await UserPromptCountAsync(schema.ConnectionString, sessionA, promptA)).ShouldBe(0);
        (await QueueStatusAsync(schema.ConnectionString, sessionB, promptB)).ShouldBe(QueuedMessageStatus.Sent);
        (await UserPromptCountAsync(schema.ConnectionString, sessionB, promptB)).ShouldBe(1);
        (await UserPromptCountAsync(schema.ConnectionString, sessionA, promptB)).ShouldBe(0);
        (await UserPromptCountAsync(schema.ConnectionString, sessionB, promptA)).ShouldBe(0);
        peerA.Inputs.ShouldBeEmpty();

        peerA.Socket.Abort();
        await using var nextA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA, storeId: storeA);
        nextA.Sessions.Add(Running(sessionA));
        EchoPrompts(nextA, harness, sessionA);
        host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: "runner-a"));
        (await Pump(host, harness).RunCycleAsync(CancellationToken.None)).ShouldBeTrue();
        (await QueueStatusAsync(schema.ConnectionString, sessionA, promptA)).ShouldBe(QueuedMessageStatus.Pending);
        (await UserPromptCountAsync(schema.ConnectionString, sessionB, promptB)).ShouldBe(1);

        await harness.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn", sessionId: sessionA);
        await harness.Queue.FlushIfIdleAsync(sessionA, CancellationToken.None);

        (await QueueStatusAsync(schema.ConnectionString, sessionA, promptA)).ShouldBe(QueuedMessageStatus.Sent);
        (await UserPromptCountAsync(schema.ConnectionString, sessionA, promptA)).ShouldBe(1);
        (await UserPromptCountAsync(schema.ConnectionString, sessionA, promptB)).ShouldBe(0);
        (await UserPromptCountAsync(schema.ConnectionString, sessionB, promptB)).ShouldBe(1);
        (await UserPromptCountAsync(schema.ConnectionString, sessionB, promptA)).ShouldBe(0);
        var recorded = await PromptTextAsync(schema.ConnectionString, sessionA, promptA);
        PromptSubmissionMatch.IsCompleteIn(promptA, recorded).ShouldBeTrue();
        var recordedB = await PromptTextAsync(schema.ConnectionString, sessionB, promptB);
        PromptSubmissionMatch.IsCompleteIn(promptB, recordedB).ShouldBeTrue();
    }

    [Test]
    public async Task Removed_runner_keeps_bound_sessions_unknown()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString, configured: Pair(secretA, "secret-b"));
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.AgentSessions.Add(Session(sessionId, "runner-a", now));
            await db.SaveChangesAsync();
        }

        var absent = new PhoneHomeRunnerDirectory(
            new PhoneHomeTestHost.RecordingLocalClient(),
            Options.Create(new PhoneHomeRunnerSettings { Enabled = true, Runners = [] }),
            host.App.Services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System);
        (await absent.GetInventoryAsync("runner-a", CancellationToken.None))
            .ShouldBeOfType<RunnerInventory.Unavailable>();
        await using var dbAfter = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var session = await dbAfter.AgentSessions.SingleAsync(s => s.Id == sessionId);
        session.Status.ShouldBe(SessionStatus.Running);
        session.RunnerId.ShouldBe("runner-a");
    }

    private static PhoneHomeRecoveryPump Pump(PhoneHomeTestHost host, BridgeQueueHarness harness) => new(
        host.Directory,
        Options.Create(new PhoneHomeRunnerSettings { Enabled = true, CatchUpRetrySeconds = 30 }),
        harness.Provider.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<PhoneHomeRecoveryPump>.Instance);

    private static PhoneHomeRecoveryPump Pump(PhoneHomeTestHost host) => new(
        host.Directory,
        Options.Create(new PhoneHomeRunnerSettings { Enabled = true, CatchUpRetrySeconds = 30 }),
        host.App.Services.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<PhoneHomeRecoveryPump>.Instance);

    private static async Task<bool> WaitUntilAsync(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (ready())
                return true;
            await Task.Delay(20);
        }

        return ready();
    }

    private static AgentSession Session(Guid id, string runnerId, DateTime now, Guid? storeId = null) => new()
    {
        Id = id,
        DefinitionName = "grok",
        AgentKind = AgentKind.Grok,
        Status = SessionStatus.Running,
        Cwd = "/work",
        Cols = 80,
        Rows = 24,
        CreatedAt = now,
        StartedAt = now,
        LastSeenAt = now,
        RunnerId = runnerId,
        RunnerStoreId = storeId ?? Guid.NewGuid(),
        RunnerCwd = "/work/" + runnerId,
    };

    private static ServiceProvider TranscriptRuntime(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsAssembly("Antiphon.Server");
            npgsql.SetPostgresVersion(16, 0);
        }));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton<ISessionRunnerClient, PhoneHomeTestHost.RecordingLocalClient>();
        services.AddSingleton<AgentSessionRuntime>();
        return services.BuildServiceProvider();
    }

    private static RunnerSessionDto Running(Guid sessionId) =>
        new(sessionId, 1, DateTime.UtcNow, "Running", null, "", 0, AcceptedStartedAt: DateTime.UtcNow);

    private static void EchoPrompts(PhoneHomeScriptedPeer peer, BridgeQueueHarness harness, Guid sessionId)
    {
        var composer = new StringBuilder();
        peer.Reply = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.Input || frame.Payload is not { } payload)
                return null;
            var input = payload.TryGetProperty("input", out var text) ? text.GetString() ?? "" : "";
            if (!input.EndsWith('\r'))
            {
                composer.Append(input);
                return null;
            }

            composer.Append(input[..^1]);
            var prompt = composer.ToString().Replace("\u001b[200~", "", StringComparison.Ordinal)
                .Replace("\u001b[201~", "", StringComparison.Ordinal);
            composer.Clear();
            if (prompt.Length == 0)
                return null;
            harness.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt, sessionId: sessionId)
                .GetAwaiter().GetResult();
            harness.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn", sessionId: sessionId)
                .GetAwaiter().GetResult();
            return null;
        };
    }

    private static async Task<QueuedMessageStatus> QueueStatusAsync(string connectionString, Guid sessionId, string body)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        return await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId && m.Body == body)
            .Select(m => m.Status)
            .SingleAsync();
    }

    private static async Task<int> UserPromptCountAsync(string connectionString, Guid sessionId, string body)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        return await db.TranscriptEntries.AsNoTracking()
            .CountAsync(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt
                && t.Text != null && t.Text.Contains(body));
    }

    private static async Task<string> PromptTextAsync(string connectionString, Guid sessionId, string body)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        return await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt
                && t.Text != null && t.Text.Contains(body))
            .Select(t => t.Text!)
            .SingleAsync();
    }

    private static RunnerTranscriptDto Transcript(Guid sessionId) =>
        new(sessionId, [new RunnerTranscriptEvent(
            sessionId, 1, TranscriptKinds.UserPrompt, Guid.NewGuid().ToString("N"), null,
            DateTimeOffset.UtcNow, "user", "owned", null, null, null, null, null)], 1);

    private static PhoneHomeRunnerSettings Pair(string secretA, string secretB) => new()
    {
        Enabled = true,
        Runners = new Dictionary<string, PhoneHomeRunnerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["runner-a"] = Entry("runner-a", secretA, "/work/a"),
            ["runner-b"] = Entry("runner-b", secretB, "/work/b"),
        },
    };

    private static PhoneHomeRunnerEntry Entry(string name, string secret, string workspace) => new()
    {
        Enabled = true,
        DisplayName = name,
        AllowDelegatedTasks = true,
        HostWorkspaceRoot = "/work",
        RunnerWorkspace = workspace,
        RunnerRepository = workspace + "/repo",
        CallbackOrigin = "https://" + name + ".test",
        SharedSecret = secret,
        MaxCapacity = 2,
        ChildGrokHome = "/state/" + name + "/grok",
    };
}
