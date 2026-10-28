using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1593 S1. Consume belongs to the committed launch. A rolled-back claim keeps the
/// grant; a committed non-launch releases it; a consume fault follows the four-class contract.
/// A second non-Postgres commit fault since the last dispatch or retry fails the task and releases the grant.
/// </summary>
[Category("Integration")]
[NotInParallel("card-1593-capacity")]
public partial class CapacityAdmissionLifecycleTests
{
    private const string ClosedParallel =
        """{"schemaVersion":1,"mode":"SeparateQueues","maxParallel":0}""";

    private const string OpenParallel =
        """{"schemaVersion":1,"mode":"LegacyOpen"}""";

    [Test]
    [Arguments("stale-sibling")]
    [Arguments("token-lost")]
    [Arguments("lease-preflight")]
    [Arguments("parallel-hold")]
    [Timeout(120_000)]
    [ParallelLimiter<GitProcessSpawnLimit>]
    public async Task C1593_rollback_exit_keeps_reservation(string exit, CancellationToken ct)
    {
        if (exit == "parallel-hold")
        {
            await AssertParallelHoldKeepsReservationAsync(ct);
            return;
        }

        using var repo = new ScratchGitRepo("c1593-" + exit);
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "project-default", "master");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var (card, project) = await SeedProjectCardAsync(db, "CARD-1593-" + exit, ct);
        project.BaseBranch = "master";
        var parent = Guid.NewGuid();
        db.AgentSessions.Add(ParentSession(parent));
        var task = QueuedWorktree(repo.Path, card.Id, parent);
        task.ProjectId = project.Id;
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync(ct);

        await using var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(
            schema.ConnectionString, repo.WorktreeRoot,
            onLeaseAcquired: () => MutateDuringLeaseAsync(schema.ConnectionString, exit, project.Id, task.Id, ct),
            configure: services =>
            {
                services.AddSingleton(Options.Create(EnabledSupervision()));
                services.AddSingleton<CapacityRecoveryService>();
            });
        var wait = await GrantAsync(provider, task.Id, ct);
        task.CapacityWaitId = wait.Id;
        await db.SaveChangesAsync(ct);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickInspectedAsync(ct);
        await AssertReservationKeptAsync(schema.ConnectionString, task.Id, wait, exit, ct);
    }

    [Test]
    [Timeout(120_000)]
    [ParallelLimiter<GitProcessSpawnLimit>]
    public async Task C1593_committed_non_launch_exit_releases_reservation(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c1593-pinned-worktree");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var (card, project) = await SeedProjectCardAsync(db, "CARD-1593-pin", ct);
        var parent = Guid.NewGuid();
        db.AgentSessions.Add(ParentSession(parent));
        var agentId = Guid.NewGuid();
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = "pinned",
            Slug = "pinned-" + agentId.ToString("N")[..8],
            WorkingDirectory = repo.Path,
            Kind = AgentKind.ClaudeCode,
            Status = AgentStatus.Idle,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        var task = QueuedWorktree(repo.Path, card.Id, parent);
        task.ProjectId = project.Id;
        task.AgentId = agentId;
        task.Ephemeral = false;
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync(ct);

        await using var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(
            schema.ConnectionString, repo.WorktreeRoot,
            configure: services =>
            {
                services.AddSingleton(Options.Create(EnabledSupervision()));
                services.AddSingleton<CapacityRecoveryService>();
            });
        var wait = await GrantAsync(provider, task.Id, ct);
        task.CapacityWaitId = wait.Id;
        await db.SaveChangesAsync(ct);
        await using var scope = provider.CreateAsyncScope();
        var tick = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickInspectedAsync(ct);

        await using var verify = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var stored = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
        stored.AdmissionCount.ShouldBe(0, $"tick failures={tick.Failures}");
        stored.State.ShouldBe(CapacityRecoveryWaitState.Canceled);
        stored.OutcomeReason.ShouldBe("claim-not-launched");
        var grant = await verify.CapacityRecoveryProviderStates.AsNoTracking()
            .SingleAsync(s => s.Kind == stored.ExecutionKind, ct);
        grant.GrantedWaitId.ShouldBeNull();
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1593_late_consume_refusal_keeps_tick_identities(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var earlier = DateTime.UtcNow.AddMinutes(-2);
        var a = await SeedSharedAsync(schema.ConnectionString, workspace.Path, "late A", earlier, ct);
        var b = await SeedSharedAsync(schema.ConnectionString, workspace.Path, "late B", earlier.AddMinutes(1), ct);
        var boundary = new RevokeGrantBoundary(schema.ConnectionString, a.Id, b.Id);
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString, configure: services => services.AddSingleton<LandDeliveryBoundary>(boundary));
        var recovery = provider.GetRequiredService<CapacityRecoveryService>();
        var waitA = await recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{a.Id:N}", CapacityWaitConsumerKind.QueuedTask, taskId: a.Id,
            holdAlreadyCleared: true, alias: null, blockedAt: earlier), ct);
        await recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{b.Id:N}", CapacityWaitConsumerKind.QueuedTask, taskId: b.Id,
            holdAlreadyCleared: true, alias: null, blockedAt: earlier.AddMinutes(1)), ct);
        (await recovery.GrantReadyAsync(ct)).ShouldBe(1);

        await using var scope = provider.CreateAsyncScope();
        var tick = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var storedA = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == a.Id, ct);
        var storedB = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == b.Id, ct);
        var admittedA = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == waitA.Id, ct);
        if (boundary.Hits == 0)
        {
            storedA.Status.ShouldBe(AgentTaskStatus.Dispatched, "boundary absent: today's grant launches A");
            admittedA.AdmissionCount.ShouldBe(1);
            storedB.Status.ShouldBe(AgentTaskStatus.Queued);
            tick.Dispatched.ShouldBe(1);
            return;
        }

        storedA.Status.ShouldBe(AgentTaskStatus.Queued);
        var tracked = scope.ServiceProvider.GetRequiredService<AppDbContext>().ChangeTracker
            .Entries<AgentTask>().Single(e => e.Entity.Id == a.Id);
        tracked.State.ShouldBe(EntityState.Unchanged);
        tracked.Entity.Status.ShouldBe(storedA.Status);
        storedB.Status.ShouldBe(AgentTaskStatus.Dispatched);
        var admittedB = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.TaskId == b.Id, ct);
        admittedB.AdmissionCount.ShouldBe(1);
        admittedB.State.ShouldBe(CapacityRecoveryWaitState.StartAccepted);
        admittedB.LaunchSessionId.ShouldBe(storedB.AgentSessionId);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1593_warm_reuse_consumes_one_admission(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var (agentId, sessionId) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var task = await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(
            schema.ConnectionString, workspace.Path, agentId, AgentModelLevel.Medium, "warm reuse");
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString, configure: services => services.AddDispatchConcurrencyAdmission());
        var wait = await GrantAsync(provider, task.Id, ct);
        await CloseParallelAsync(provider, ct);

        await using (var scope = provider.CreateAsyncScope())
        {
            var held = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
            held.Dispatched.ShouldBe(0, "the parallel slot is closed");
            held.Failures.ShouldBe(0);
        }

        await OpenParallelAsync(provider, ct);
        await using var launch = provider.CreateAsyncScope();
        var launched = await launch.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        launched.Dispatched.ShouldBe(1);
        await AssertConsumedAsync(schema.ConnectionString, task.Id, wait.Id, sessionId, ct);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await verify.Agents.AsNoTracking().SingleAsync(a => a.Id == agentId, ct)).Status.ShouldBe(AgentStatus.Running);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1593_standing_placement_consumes_one_admission(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var (agentId, sessionId) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            await db.Agents.Where(a => a.Id == agentId).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsPoolDelegate, false)
                .SetProperty(a => a.AlwaysOn, false), ct);
        }

        var task = await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(
            schema.ConnectionString, workspace.Path, agentId, AgentModelLevel.Medium, "standing placement");
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString, configure: services => services.AddDispatchConcurrencyAdmission());
        var wait = await GrantAsync(provider, task.Id, ct);
        await CloseParallelAsync(provider, ct);
        await using (var scope = provider.CreateAsyncScope())
        {
            var held = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
            held.Dispatched.ShouldBe(0);
            held.Failures.ShouldBe(0);
        }

        await OpenParallelAsync(provider, ct);
        await using var launch = provider.CreateAsyncScope();
        var launched = await launch.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        launched.Dispatched.ShouldBe(1);
        await AssertConsumedAsync(schema.ConnectionString, task.Id, wait.Id, sessionId, ct);
    }

    [Test]
    [Arguments("deadlock-victim")]
    [Arguments("pre-lock-deadlock-victim")]
    [Arguments("lost-commit-ack")]
    [Arguments("deterministic-consume-failure")]
    [Arguments("commit-server-error")]
    [Arguments("non-postgres-commit-fault")]
    [Timeout(120_000)]
    public async Task C1593_consume_failure_leaves_the_task_queued(string mode, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        Guid? warmAgent = null;
        AgentTask task;
        if (mode == "pre-lock-deadlock-victim")
        {
            var seeded = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
                schema.ConnectionString, workspace.Path);
            warmAgent = seeded.AgentId;
            task = await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(
                schema.ConnectionString, workspace.Path, seeded.AgentId, AgentModelLevel.Medium, mode);
        }
        else
        {
            task = await SeedSharedAsync(schema.ConnectionString, workspace.Path, mode, DateTime.UtcNow, ct);
        }

        var commitFault = mode == "non-postgres-commit-fault" ? new NonPostgresCommitFault() : null;
        IInterceptor interceptor = mode switch
        {
            "deterministic-consume-failure" => new StartAcceptedSaveFault("XX000"),
            "commit-server-error" => new CommitServerError(),
            "lost-commit-ack" => new LostCommitAck(),
            "pre-lock-deadlock-victim" => new AgentRowDeadlock(warmAgent!.Value),
            "non-postgres-commit-fault" => commitFault!,
            _ => new StartAcceptedSaveFault("40P01"),
        };
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString, interceptor);
        var wait = await GrantAsync(provider, task.Id, ct);
        Exception? tickError = null;
        AgentTaskDispatcher.TickResult? tick = null;
        try
        {
            await using var scope = provider.CreateAsyncScope();
            tick = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            tickError = ex;
        }

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        var admitted = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
        var detail = $"tick={tick?.Failures}/{tick?.Dispatched} error={tickError?.GetType().Name}: {tickError?.Message}";
        if (mode is "deadlock-victim" or "pre-lock-deadlock-victim")
        {
            stored.Status.ShouldBe(AgentTaskStatus.Queued, detail);
            stored.FailureReason.ShouldBeNull(detail);
            stored.AgentSessionId.ShouldBeNull();
            admitted.State.ShouldBe(CapacityRecoveryWaitState.ActionPending);
            admitted.AdmissionCount.ShouldBe(0);
            (await verify.CapacityRecoveryProviderStates.AsNoTracking()
                .SingleAsync(s => s.Kind == admitted.ExecutionKind, ct)).GrantedWaitId.ShouldBe(wait.Id);
            (tick?.Failures ?? 0).ShouldBe(0);
            (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id
                && e.Type == AgentTaskEventType.Warning && e.Detail!.Contains("capacity consume refused"), ct)).ShouldBe(1);
            return;
        }

        if (mode == "lost-commit-ack")
        {
            stored.Status.ShouldBe(AgentTaskStatus.Dispatched, detail);
            stored.FailureReason.ShouldBeNull();
            admitted.AdmissionCount.ShouldBe(1, detail);
            admitted.State.ShouldBe(CapacityRecoveryWaitState.StartAccepted);
            admitted.LaunchSessionId.ShouldBe(stored.AgentSessionId);
            return;
        }

        if (mode == "non-postgres-commit-fault")
        {
            stored.Status.ShouldBe(AgentTaskStatus.Queued, detail);
            stored.FailureReason.ShouldBeNull(detail);
            stored.AgentSessionId.ShouldBeNull();
            admitted.State.ShouldBe(CapacityRecoveryWaitState.ActionPending);
            admitted.AdmissionCount.ShouldBe(0);
            (await verify.CapacityRecoveryProviderStates.AsNoTracking()
                .SingleAsync(s => s.Kind == admitted.ExecutionKind, ct)).GrantedWaitId.ShouldBe(wait.Id);
            (tick?.Failures ?? 0).ShouldBe(0, detail);
            tickError.ShouldBeNull(detail);
            (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id
                && e.Type == AgentTaskEventType.Warning
                && e.Detail!.Contains("capacity consume refused"), ct)).ShouldBe(1);
            commitFault!.Throws.ShouldBe(1, detail);

            Exception? secondError = null;
            AgentTaskDispatcher.TickResult? second = null;
            try
            {
                await using var secondScope = provider.CreateAsyncScope();
                second = await secondScope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                secondError = ex;
            }

            var secondDetail =
                $"tick={second?.Failures}/{second?.Dispatched} error={secondError?.GetType().Name}: {secondError?.Message}";
            stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
            admitted = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
            commitFault.Throws.ShouldBe(2, secondDetail);
            stored.Status.ShouldBe(AgentTaskStatus.Failed, secondDetail);
            (second?.Failures ?? (secondError is null ? 0 : 1)).ShouldBe(1, secondDetail);
            admitted.State.ShouldBe(CapacityRecoveryWaitState.Canceled);
            admitted.OutcomeReason.ShouldBe("claim-consume-failed");
            admitted.AdmissionCount.ShouldBe(0);
            (await verify.CapacityRecoveryProviderStates.AsNoTracking()
                .SingleAsync(s => s.Kind == admitted.ExecutionKind, ct)).GrantedWaitId.ShouldBeNull();
            (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id
                && e.Type == AgentTaskEventType.Warning
                && e.Detail == AgentTaskDispatcher.NonPostgresCommitFaultMarker, ct)).ShouldBe(1);
            return;
        }

        stored.Status.ShouldBe(AgentTaskStatus.Failed, detail);
        (tick?.Failures ?? (tickError is null ? 0 : 1)).ShouldBe(1, detail);
        admitted.State.ShouldBe(CapacityRecoveryWaitState.Canceled);
        admitted.OutcomeReason.ShouldBe("claim-consume-failed");
        admitted.AdmissionCount.ShouldBe(0);
        (await verify.CapacityRecoveryProviderStates.AsNoTracking()
            .SingleAsync(s => s.Kind == admitted.ExecutionKind, ct)).GrantedWaitId.ShouldBeNull();
    }

    [Test]
    [Arguments("retried")]
    [Arguments("dispatched")]
    [Timeout(120_000)]
    public async Task C1593_one_commit_fault_after_requeue_anchor_stays_queued(string anchor, CancellationToken ct)
    {
        // An old marker before the latest Dispatched or Retried event must not count.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var task = await SeedSharedAsync(schema.ConnectionString, workspace.Path, anchor, DateTime.UtcNow, ct);
        var seededAt = DateTime.UtcNow;
        await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            seed.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(),
                AgentTaskId = task.Id,
                Type = AgentTaskEventType.Warning,
                Detail = AgentTaskDispatcher.NonPostgresCommitFaultMarker,
                At = seededAt.AddMinutes(-10),
            });
            seed.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(),
                AgentTaskId = task.Id,
                Type = anchor == "retried" ? AgentTaskEventType.Retried : AgentTaskEventType.Dispatched,
                Detail = anchor,
                At = seededAt.AddMinutes(-5),
            });
            await seed.SaveChangesAsync(ct);
        }

        var commitFault = new NonPostgresCommitFault();
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString, commitFault);
        var wait = await GrantAsync(provider, task.Id, ct);
        Exception? tickError = null;
        AgentTaskDispatcher.TickResult? tick = null;
        try
        {
            await using var scope = provider.CreateAsyncScope();
            tick = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            tickError = ex;
        }

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        var admitted = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
        var detail =
            $"anchor={anchor} tick={tick?.Failures}/{tick?.Dispatched} error={tickError?.GetType().Name}: {tickError?.Message}";
        stored.Status.ShouldBe(AgentTaskStatus.Queued, detail);
        stored.FailureReason.ShouldBeNull(detail);
        stored.AgentSessionId.ShouldBeNull();
        admitted.State.ShouldBe(CapacityRecoveryWaitState.ActionPending);
        admitted.AdmissionCount.ShouldBe(0);
        (await verify.CapacityRecoveryProviderStates.AsNoTracking()
            .SingleAsync(s => s.Kind == admitted.ExecutionKind, ct)).GrantedWaitId.ShouldBe(wait.Id);
        (tick?.Failures ?? 0).ShouldBe(0, detail);
        tickError.ShouldBeNull(detail);
        commitFault.Throws.ShouldBe(1, detail);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id
            && e.Type == AgentTaskEventType.Warning
            && e.Detail == AgentTaskDispatcher.NonPostgresCommitFaultMarker, ct)).ShouldBe(2, detail);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1593_warm_reuse_lost_commit_ack_delivers_the_brief(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var (agentId, sessionId) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        const string goal = "Warm lost-ack brief must be queued.";
        var task = await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(
            schema.ConnectionString, workspace.Path, agentId, AgentModelLevel.Medium, goal);
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString, new LostCommitAck(),
            configure: services => services.AddDispatchConcurrencyAdmission());
        var wait = await GrantAsync(provider, task.Id, ct);
        await using var adapter = new FakeAgentProtocolAdapter();
        await adapter.EnableClaudeDialogObservationAsync(schema.ConnectionString, sessionId);
        provider.GetRequiredService<AgentSessionRuntime>().Register(sessionId, adapter);
        await using var launch = provider.CreateAsyncScope();
        var launched = await launch.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        launched.Dispatched.ShouldBe(1);
        launched.Failures.ShouldBe(0);
        await AssertConsumedAsync(schema.ConnectionString, task.Id, wait.Id, sessionId, ct);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var queued = await verify.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(m => m.AgentSessionId == sessionId && m.ExecutionTaskId == task.Id, ct);
        queued.Body.ShouldContain(goal);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1593_release_leaves_a_start_accepted_receipt(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var (agentId, sessionId) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var task = await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(
            schema.ConnectionString, workspace.Path, agentId, AgentModelLevel.Medium, "release receipt");
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString, configure: services => services.AddDispatchConcurrencyAdmission());
        var wait = await GrantAsync(provider, task.Id, ct);
        await using var launch = provider.CreateAsyncScope();
        var launched = await launch.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        launched.Dispatched.ShouldBe(1);
        launched.Failures.ShouldBe(0);
        await provider.GetRequiredService<CapacityRecoveryService>()
            .ReleaseClaimReservationAsync(task.Id, AgentKind.ClaudeCode, "claim-consume-failed", ct);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var admitted = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
        admitted.State.ShouldBe(CapacityRecoveryWaitState.StartAccepted);
        admitted.LaunchSessionId.ShouldBe(sessionId);
        admitted.AdmissionCount.ShouldBe(1);
        admitted.OutcomeReason.ShouldNotBe("claim-consume-failed");
    }

    internal static async Task<CapacityRecoveryWait> GrantAsync(
        IServiceProvider provider, Guid taskId, CancellationToken ct, string? alias = null)
    {
        var recovery = provider.GetRequiredService<CapacityRecoveryService>();
        var wait = await recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{taskId:N}", CapacityWaitConsumerKind.QueuedTask,
            taskId: taskId, holdAlreadyCleared: true, alias: alias), ct);
        (await recovery.GrantReadyAsync(ct)).ShouldBeGreaterThan(0);
        return wait;
    }

    internal static async Task AssertReservationKeptAsync(
        string connection, Guid taskId, CapacityRecoveryWait wait, string label, CancellationToken ct)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var stored = await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
        var detail = $"{label}: status={task.Status} reason={task.FailureReason} state={stored.State} admission={stored.AdmissionCount}";
        stored.AdmissionCount.ShouldBe(0, detail);
        stored.State.ShouldBe(CapacityRecoveryWaitState.ActionPending, detail);
        var grant = await db.CapacityRecoveryProviderStates.AsNoTracking()
            .SingleAsync(s => s.Kind == stored.ExecutionKind, ct);
        grant.GrantedWaitId.ShouldBe(wait.Id, detail);
        grant.GrantedActionKey.ShouldBe(wait.ActionKey, detail);
    }

    private static async Task AssertConsumedAsync(
        string connection, Guid taskId, Guid waitId, Guid sessionId, CancellationToken ct)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
        var wait = await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == waitId, ct);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        wait.AdmissionCount.ShouldBe(1);
        wait.State.ShouldBe(CapacityRecoveryWaitState.StartAccepted);
        wait.LaunchSessionId.ShouldBe(sessionId);
        task.AgentSessionId.ShouldBe(sessionId);
    }

    private static async Task AssertParallelHoldKeepsReservationAsync(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var task = await SeedSharedAsync(schema.ConnectionString, workspace.Path, "parallel-hold", DateTime.UtcNow, ct);
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString, configure: services => services.AddDispatchConcurrencyAdmission());
        var wait = await GrantAsync(provider, task.Id, ct);
        await CloseParallelAsync(provider, ct);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        await AssertReservationKeptAsync(schema.ConnectionString, task.Id, wait, "parallel-hold", ct);
    }

    internal static async Task CloseParallelAsync(IServiceProvider provider, CancellationToken ct) =>
        await WriteParallelAsync(provider, ClosedParallel, ct);

    internal static async Task OpenParallelAsync(IServiceProvider provider, CancellationToken ct) =>
        await WriteParallelAsync(provider, OpenParallel, ct);

    private static async Task WriteParallelAsync(IServiceProvider provider, string json, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DispatchConcurrencySettingsService>()
            .EnsureInitializedAsync(ct);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.DispatchConcurrencySettings.Where(s => s.ProjectId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.OverridesJson, json), ct)).ShouldBe(1);
    }

    internal static async Task<AgentTask> SeedSharedAsync(
        string connection, string directory, string title, DateTime createdAt, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = title,
            Goal = title,
            Role = AgentTaskRole.Docs,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Status = AgentTaskStatus.Queued,
            Ephemeral = true,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = createdAt,
            ConcurrencyToken = Guid.NewGuid(),
        };
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync(ct);
        return task;
    }

    private static async Task<(Card Card, Project Project)> SeedProjectCardAsync(
        AppDbContext db, string identifier, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"c1593-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/c1593.git",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = identifier,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = identifier,
            Title = identifier,
            Description = "CARD-1593",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        await db.SaveChangesAsync(ct);
        return (card, project);
    }

    private static AgentTask QueuedWorktree(string repoPath, Guid cardId, Guid parentSessionId)
    {
        var id = Guid.NewGuid();
        return new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "CARD-1593 worktree",
            Goal = "Keep the capacity grant.",
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Target,
            WorkingDirectory = repoPath,
            RepoPath = repoPath,
            CardId = cardId,
            ParentSessionId = parentSessionId,
            ReplyTo = AgentTaskReplyTo.Session,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static AgentSession ParentSession(Guid id) => new()
    {
        Id = id,
        DefinitionName = "c1593-parent",
        AgentKind = AgentKind.ClaudeCode,
        Status = SessionStatus.Running,
        Cwd = Path.GetTempPath(),
        Cols = 120,
        Rows = 30,
        CreatedAt = DateTime.UtcNow.AddHours(-1),
        StartedAt = DateTime.UtcNow.AddHours(-1),
        LastSeenAt = DateTime.UtcNow,
    };

    private static async Task MutateDuringLeaseAsync(
        string connection, string exit, Guid projectId, Guid taskId, CancellationToken ct)
    {
        await using var edit = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        if (exit == "stale-sibling")
        {
            await edit.Projects.Where(p => p.Id == projectId)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.BaseBranch, "project-default"), ct);
            return;
        }

        if (exit == "token-lost")
        {
            await edit.AgentTasks.Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.ConcurrencyToken, Guid.NewGuid()), ct);
            return;
        }

        await edit.AgentTasks.Where(t => t.Id == taskId)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.WorktreeBranch, "side"), ct);
    }

    private static SupervisionSettings EnabledSupervision() => new()
    {
        CapacityRecovery = new CapacityRecoverySettings
        {
            Enabled = true,
            AdmissionIntervalSeconds = 60,
            JitterSeconds = 0,
            MaxEpisodeAttempts = 3,
        },
    };

    internal sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c1593").FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }

    private sealed class RevokeGrantBoundary(string connection, Guid taskA, Guid taskB) : LandDeliveryBoundary
    {
        public int Hits;

        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "capacity-consume-before" || taskId != taskA)
                return;
            Interlocked.Increment(ref Hits);
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
            var winner = await db.CapacityRecoveryWaits.SingleAsync(w => w.TaskId == taskB, ct);
            winner.State = CapacityRecoveryWaitState.ActionPending;
            var state = await db.CapacityRecoveryProviderStates.SingleAsync(s => s.Kind == winner.ExecutionKind, ct);
            state.GrantedWaitId = winner.Id;
            state.GrantedActionKey = winner.ActionKey;
            state.GrantedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    internal sealed class StartAcceptedSaveFault(string sqlState) : SaveChangesInterceptor
    {
        public int Throws;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is AppDbContext db && db.ChangeTracker.Entries<CapacityRecoveryWait>()
                    .Any(e => e.Entity.State == CapacityRecoveryWaitState.StartAccepted)
                && Interlocked.CompareExchange(ref Throws, 1, 0) == 0)
            {
                throw new DbUpdateException(
                    "injected consume save failure",
                    new PostgresException("injected", "ERROR", "ERROR", sqlState));
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class NonPostgresCommitFault : ISaveChangesInterceptor, IDbTransactionInterceptor
    {
        private int _claim;
        public int Throws;

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var db = eventData.Context;
            if (db is not null && db.ChangeTracker.Entries<AgentTask>().Any(e =>
                    e.State == EntityState.Modified && e.Entity.Status == AgentTaskStatus.Dispatched))
                Interlocked.Exchange(ref _claim, 1);
            return ValueTask.FromResult(result);
        }

        public ValueTask<InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _claim, 0) == 1)
            {
                Interlocked.Increment(ref Throws);
                throw new IOException("injected non-postgres commit fault");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitServerError : ISaveChangesInterceptor, IDbTransactionInterceptor
    {
        private int _claim;
        private int _thrown;

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var db = eventData.Context;
            if (db is not null && db.ChangeTracker.Entries<AgentTask>().Any(e =>
                    e.State == EntityState.Modified && e.Entity.Status == AgentTaskStatus.Dispatched))
                Interlocked.Exchange(ref _claim, 1);
            return ValueTask.FromResult(result);
        }

        public ValueTask<InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _claim) == 1 && Interlocked.CompareExchange(ref _thrown, 1, 0) == 0)
                throw new PostgresException("injected commit server error", "ERROR", "ERROR", "XX000");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class LostCommitAck : ISaveChangesInterceptor, IDbTransactionInterceptor
    {
        private int _claim;
        private int _thrown;

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var db = eventData.Context;
            if (db is not null && (db.ChangeTracker.Entries<AgentSession>().Any(e => e.State == EntityState.Added)
                || db.ChangeTracker.Entries<AgentTask>().Any(e =>
                    e.State == EntityState.Modified && e.Entity.Status == AgentTaskStatus.Dispatched)))
                Interlocked.Exchange(ref _claim, 1);
            return ValueTask.FromResult(result);
        }

        public Task TransactionCommittedAsync(
            System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _claim) == 1 && Interlocked.CompareExchange(ref _thrown, 1, 0) == 0)
                throw new PostgresException("injected lost commit ack", "ERROR", "ERROR", "08006");
            return Task.CompletedTask;
        }
    }

    private sealed class AgentRowDeadlock(Guid agentId) : DbCommandInterceptor
    {
        private int _taskLocked;
        private int _thrown;

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Note(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Note(command);
            return ValueTask.FromResult(result);
        }

        private void Note(System.Data.Common.DbCommand command)
        {
            var text = command.CommandText;
            if (text.Contains("\"AgentTasks\"", StringComparison.Ordinal)
                && text.Contains("FOR UPDATE", StringComparison.Ordinal))
                Volatile.Write(ref _taskLocked, 1);
            if (Volatile.Read(ref _taskLocked) != 1 || Interlocked.CompareExchange(ref _thrown, 1, 0) != 0)
                return;
            var writesAgent = text.Contains("\"Agents\"", StringComparison.Ordinal)
                && (text.Contains("UPDATE", StringComparison.Ordinal)
                    || text.Contains("FOR UPDATE", StringComparison.Ordinal));
            if (!writesAgent)
            {
                Interlocked.Exchange(ref _thrown, 0);
                return;
            }

            _ = agentId;
            throw new PostgresException("injected pre-lock deadlock", "ERROR", "ERROR", "40P01");
        }
    }
}
