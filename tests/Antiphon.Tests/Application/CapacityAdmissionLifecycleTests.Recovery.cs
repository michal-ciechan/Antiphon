using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class CapacityAdmissionLifecycleTests
{
    [Test]
    [Timeout(180_000)]
    public async Task C1593_crash_after_launch_commit_consumes_one_admission(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var task = await SeedSharedAsync(schema.ConnectionString, workspace.Path, "crash after commit", DateTime.UtcNow, ct);
        using var tickCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var boundary = new CrashAfterCommit(tickCts);
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString,
            configure: services =>
            {
                services.AddSingleton<LandDeliveryBoundary>(boundary);
                services.AddSingleton(Options.Create(Roots(workspace.Path)));
            });
        var wait = await GrantAsync(provider, task.Id, ct);
        await using (var scope = provider.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            await Should.ThrowAsync<OperationCanceledException>(async () =>
                await dispatcher.TickAsync(tickCts.Token));
        }

        boundary.Hits.ShouldBe(1, "the crash cut is the committed-claim boundary");
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched, stored.FailureReason);
        var sessionId = stored.AgentSessionId.ShouldNotBeNull();
        var admitted = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
        admitted.State.ShouldBe(CapacityRecoveryWaitState.StartAccepted);
        admitted.AdmissionCount.ShouldBe(1);
        admitted.LaunchSessionId.ShouldBe(sessionId);
        await RecoverOnePromptAsync(schema.ConnectionString, workspace.Path, task.Id, sessionId, stored.AgentId!.Value, ct);
        await using var after = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await after.AgentSessions.CountAsync(s => s.Cwd == workspace.Path, ct)).ShouldBe(1);
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1593_enqueue_failure_consumes_one_admission(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var task = await SeedSharedAsync(schema.ConnectionString, workspace.Path, "enqueue refusal", DateTime.UtcNow, ct);
        var sink = new RefusingLaunchSink();
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString,
            configure: services =>
            {
                services.AddSingleton<IAgentTaskLaunchSink>(sink);
                services.AddSingleton(Options.Create(Roots(workspace.Path)));
            });
        var wait = await GrantAsync(provider, task.Id, ct);
        await using (var scope = provider.CreateAsyncScope())
        {
            var tick = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
            tick.Dispatched.ShouldBe(1);
            tick.Failures.ShouldBe(0);
        }

        sink.Calls.ShouldBe(1);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched, stored.FailureReason);
        stored.FailureReason.ShouldBeNull();
        var sessionId = stored.AgentSessionId.ShouldNotBeNull();
        (await verify.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId, ct)).Status
            .ShouldBe(SessionStatus.Starting);
        var admitted = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
        admitted.AdmissionCount.ShouldBe(1);
        admitted.State.ShouldBe(CapacityRecoveryWaitState.StartAccepted);
        admitted.LaunchSessionId.ShouldBe(sessionId);
        await RecoverOnePromptAsync(schema.ConnectionString, workspace.Path, task.Id, sessionId, stored.AgentId!.Value, ct);
    }

    private static DelegationSettings Roots(string workspace) => new()
    {
        PoolReservedForCallerMinutes = 2,
        PoolIdleRetireMinutes = 5,
        PoolMaxIdlePerDirectory = 3,
        MaxConcurrentTasks = 6,
        AllowedRoots = [workspace, "/"],
    };

    private static async Task RecoverOnePromptAsync(
        string connection, string workspace, Guid taskId, Guid sessionId, Guid agentId, CancellationToken ct)
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = true };
        var settings = Roots(workspace);
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = connection,
            AlwaysOn = false,
            PreserveDatabaseOnDispose = true,
            Delegation = settings,
            ConfigureServices = services =>
                services.AddSingleton<IAgentProtocolAdapterFactory>(new RecoveryAdapterFactory(adapter)),
        });
        adapter.RegisterOnStart = harness.Runtime;
        adapter.OnSubmitted = async body =>
        {
            await BridgeQueueHarness.InsertEntryAsync(
                sessionId, TranscriptKinds.UserPrompt, body, timestamp: DateTime.UtcNow,
                connectionString: connection);
            await BridgeQueueHarness.InsertEntryAsync(
                sessionId, TranscriptKinds.TurnEnd, stopReason: "end_turn", connectionString: connection);
        };
        await adapter.EnableClaudeDialogObservationAsync(connection, sessionId);
        await using var scope = harness.Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentSessionService>()
            .ResumeInterruptedLaunchAsync(sessionId, agentId, ct);
        adapter.SubmittedBodies.Count.ShouldBe(1);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        (await db.TranscriptEntries.CountAsync(e => e.AgentSessionId == sessionId
            && e.Kind == TranscriptKinds.UserPrompt, ct)).ShouldBe(1);
        _ = taskId;
    }

    private sealed class CrashAfterCommit(CancellationTokenSource tick) : LandDeliveryBoundary
    {
        public int Hits;

        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "dispatch-warning-claim-committed")
                return Task.CompletedTask;
            Interlocked.Increment(ref Hits);
            tick.Cancel();
            throw new OperationCanceledException(ct);
        }
    }

    private sealed class RecoveryAdapterFactory(IAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }

    private sealed class RefusingLaunchSink : IAgentTaskLaunchSink
    {
        public int Calls { get; private set; }

        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec)
        {
            Calls++;
            throw new IOException("Injected launch enqueue refusal after the committed claim");
        }
    }
}
