using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class PhoneHomeTaskDispatchProjectionTests
{
    [Test]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("enqueue-fails")]
    public async Task LowDiskRecoveryDeliversTheCompleteQueuedBrief(string scenario)
    {
        const string goal = "First line of disk recovery.\nKeep this middle line.\nThis is the final line.";
        const long floor = 20L << 30;
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync(capabilities: new RunnerCapabilitiesDto(
            "UnixPty", "unix", "fixture", false, Platform: "linux"));
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        peer.Reply = AnswerDispatchSave;
        using var workspace = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, AgentKind.ClaudeCode, goal);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = new HostStatsCache(new HostStatsSettings(), clock);
        void Sample(long free) => cache.Record(host.AllowedRunnerId, new RunnerHostStatsDto(
            clock.GetUtcNow(), 5, 60, 4, new RunnerHostCurrentDto(null, null, null, null, null, null, null, null, null,
                [new RunnerHostDiskDto("/work", free, 1L << 40)], null, []),
            new Dictionary<string, RunnerHostWindowRollups>(), null));
        var guard = new WorkspaceDiskAdmission(cache, Options.Create(new WorkspaceDiskAlarmSettings
        {
            AdmissionRunners = { [host.AllowedRunnerId] = "/work" },
        }), clock, NullLogger<WorkspaceDiskAdmission>.Instance);
        var sink = new RecordingLaunchSink();
        var graph = CreateDispatcher(schema, host, sink, diskAdmission: guard);
        await using var services = graph.Services;
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        async Task TickAsync()
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
        }

        Sample(floor - 1);
        await TickAsync();
        var held = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        held.Status.ShouldBe(AgentTaskStatus.Queued);
        held.AgentSessionId.ShouldBeNull();
        (await db.AgentTaskEvents.AsNoTracking().SingleAsync(e =>
            e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Held)).Detail.ShouldContain("workspace_disk_low");
        (await db.SessionQueuedMessages.CountAsync()).ShouldBe(0);
        peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(0);
        sink.Specs.ShouldBeEmpty();

        var queue = services.GetRequiredService<SessionMessageQueueService>();
        var enqueueFailures = 0;
        if (scenario == "enqueue-fails")
        {
            queue.BeforeDispatchBriefRowLock = _ =>
            {
                enqueueFailures++;
                throw new IOException("fixture rejects the first brief enqueue");
            };
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        Sample(floor);
        await TickAsync();
        await graph.Preparer.WhenIdleAsync();
        await TickAsync();

        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        var sessionId = task.AgentSessionId.ShouldNotBeNull();
        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        session.RunnerId.ShouldBe(host.AllowedRunnerId);
        sink.Specs.ShouldHaveSingleItem().Cwd.ShouldBe(DispatchSaveMirror);

        if (scenario == "enqueue-fails")
        {
            enqueueFailures.ShouldBe(1);
            (await db.SessionQueuedMessages.CountAsync()).ShouldBe(0, "the interrupted queue transaction rolled back");
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == sessionId)).ShouldBe(0);
            queue.BeforeDispatchBriefRowLock = null;
            await RecoverThroughReconciliationAsync(schema.ConnectionString, host, peer, task.AgentId.ShouldNotBeNull(),
                taskId, sessionId, session.StartedAt, goal);
            peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(1);
            sink.Specs.Count.ShouldBe(1, "recovery used the original recipient");
            return;
        }

        var pending = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.ExecutionTaskId == taskId);
        pending.AgentSessionId.ShouldBe(sessionId);
        pending.Status.ShouldBe(QueuedMessageStatus.Pending);
        pending.DeliveryAttempts.ShouldBe(0);
        pending.RemoteSpillBody.ShouldBeNull("the reported runner backend supports the complete inline brief");
        pending.Body.ShouldContain(goal);
        await using var recipient = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
            PreserveDatabaseOnDispose = true,
            AttachSessionId = sessionId,
            AttachAgentId = task.AgentId.ShouldNotBeNull(),
            Delegation = new DelegationSettings { PtySingleChunkBytes = 86_400, BriefInlineMaxBytes = 43_200 },
        });
        await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(
            schema.ConnectionString, recipient, pending, sessionId, busy: scenario == "busy");

        var prompt = await db.TranscriptEntries.AsNoTracking().SingleAsync(t =>
            t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt);
        PromptSubmissionMatch.Normalize(prompt.Text!).ShouldBe(PromptSubmissionMatch.Normalize(pending.Body));
        peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(1);
    }
    private static async Task RecoverThroughReconciliationAsync(string connection, PhoneHomeTestHost host,
        PhoneHomeScriptedPeer peer, Guid agentId, Guid taskId, Guid sessionId, DateTime generation, string goal)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        // The dispatch fixture captures the launch. Model the remote runner having accepted that same
        // generation and persisted Running before the next reconciliation tick discovers brief debt.
        var updated = await db.AgentSessions.Where(s => s.Id == sessionId && s.Status == SessionStatus.Starting)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.Status, SessionStatus.Running));
        updated.ShouldBe(1);
        var listing = new RunnerSessionDto(sessionId, 1, generation, "Running", null, "", 0,
            AcceptedStartedAt: generation);
        peer.Reply = frame => frame.Operation switch
        {
            PhoneHomeOperation.List => Result(frame, new[] { listing }),
            PhoneHomeOperation.Get => Result(frame, listing),
            PhoneHomeOperation.Transcript => Result(frame,
                new RunnerTranscriptDto(sessionId, [], 0, AcceptedStartedAt: generation)),
            _ => AnswerDispatchSave(frame),
        };

        BridgeQueueHarness? bridge = null;
        await using var recipient = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = connection,
            PreserveDatabaseOnDispose = true,
            AttachSessionId = sessionId,
            AttachAgentId = agentId,
            Delegation = new DelegationSettings { PtySingleChunkBytes = 86_400, BriefInlineMaxBytes = 43_200 },
            ConfigureServices = services =>
            {
                services.AddSingleton<ISessionRunnerDirectory>(host.Directory);
                services.AddSingleton<IAgentProtocolAdapterFactory>(new DiskRecoveryAdapterFactory(() => bridge!.Adapter));
            },
        });
        bridge = recipient;
        var launchQueue = recipient.Provider.GetRequiredService<AgentSessionLaunchQueue>();
        var runner = recipient.Provider.GetRequiredService<ISessionRunnerClient>();
        async Task ScanAsync()
        {
            await using var scan = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
            var reconciliation = SessionReconciliationServiceTests.BuildService(
                scan, runner, recipient.EventBus, ownership: launchQueue, directory: host.Directory);
            await reconciliation.ScanAsync(CancellationToken.None);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await launchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), budget.Token, TimeProvider.System);
        }

        await ScanAsync();
        recipient.Adapter.Attached.ShouldBeTrue("the watchdog scheduled the production recovery handoff");
        await ScanAsync();

        var recovered = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.ExecutionTaskId == taskId);
        recovered.AgentSessionId.ShouldBe(sessionId);
        recovered.RemoteSpillBody.ShouldBeNull();
        recovered.Body.ShouldContain(goal);
        recipient.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        var prompt = await db.TranscriptEntries.AsNoTracking().SingleAsync(t =>
            t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt);
        PromptSubmissionMatch.Normalize(prompt.Text!).ShouldBe(PromptSubmissionMatch.Normalize(recovered.Body));
    }

    private sealed class DiskRecoveryAdapterFactory(Func<IAgentProtocolAdapter> create) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => create();
    }
}
