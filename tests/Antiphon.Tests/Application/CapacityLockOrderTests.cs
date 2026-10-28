using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Postgres lock-state witnesses for CARD-1593. Waits poll pg_locks and pg_stat_activity.
/// </summary>
[Category("Integration")]
[NotInParallel("card-1593-capacity")]
public sealed class CapacityLockOrderTests
{
    private const string ClaudeKey = "antiphon.capacity.kind.ClaudeCode";
    private const string GrokKey = "antiphon.capacity.kind.Grok";

    [Test]
    [Timeout(120_000)]
    [Arguments("claim")]
    [Arguments("claim-agent-row")]
    [Arguments("queued-reroute")]
    [Arguments("routing-resume")]
    [Arguments("continuation")]
    [Arguments("continuation-agent-row")]
    [Arguments("cancel")]
    [Arguments("retry")]
    public async Task C1593_path_blocked_on_task_holds_no_provider_lock(string path, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await using var armed = await ArmBlockedPathAsync(schema, workspace.Path, path, ct);
        await AssertBlockedWithoutAdvisoryAsync(
            schema.ConnectionString, armed.Table, armed.RowId, armed.KindKey, armed.AlsoParallel,
            armed.Start, ct);
    }

    [Test]
    [Timeout(120_000)]
    [Arguments("claim-launch")]
    [Arguments("queued-reroute")]
    [Arguments("continuation")]
    public async Task C1593_path_blocked_on_provider_already_holds_task(string path, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        if (path == "claim-launch")
        {
            await AssertClaimLaunchHoldsTaskAsync(schema, workspace.Path, ct);
            return;
        }

        await using var armed = await ArmProviderPathAsync(schema, workspace.Path, path, ct);
        await using var holder = await CapacityLockProbe.OpenAsync(schema.ConnectionString, ct);
        var holderPid = await CapacityLockProbe.BackendPidAsync(holder, ct);
        await CapacityLockProbe.HoldAdvisoryAsync(holder, armed.KindKey, ct);
        var started = armed.Start(ct);
        Exception? assertion = null;
        try
        {
            await CapacityLockProbe.PollBlockedByAsync(
                schema.ConnectionString, holderPid, started, CapacityLockProbe.Deadline, ct);
            var acquired = await CapacityLockProbe.TryNowaitAsync(
                schema.ConnectionString, "AgentTasks", armed.TaskId, ct);
            acquired.ShouldBeFalse($"55P03: {path} must already hold task {armed.TaskId} while blocked on {armed.KindKey}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            assertion = ex;
        }
        finally
        {
            try { await CapacityLockProbe.UnlockAdvisoryAsync(holder, armed.KindKey, CancellationToken.None); }
            catch (Exception ex) when (ex is not OperationCanceledException) { assertion ??= ex; }
        }

        await FinishStartedAsync(started, assertion);
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1593_competing_claim_and_reroute_complete_without_deadlock(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await WallRerouteFixture.SeedChainAsync(schema,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.ClaudeCode, AgentModelLevel.High));
        var task = await CapacityAdmissionLifecycleTests.SeedSharedAsync(
            schema.ConnectionString, workspace.Path, "competing reroute", DateTime.UtcNow, ct);
        await SetLevelAsync(schema.ConnectionString, task.Id, AgentModelLevel.Frontier, TaskComplexity.Hard, ct);
        var boundary = new PauseBoundary();
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString, boundary, configure: services => services.AddSingleton<LandDeliveryBoundary>(boundary));
        await CapacityAdmissionLifecycleTests.GrantAsync(provider, task.Id, ct);
        await using var claimScope = provider.CreateAsyncScope();
        var claim = claimScope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        var paused = await Task.WhenAny(boundary.Entered("capacity-consume-before").Task, claim)
            .WaitAsync(TimeSpan.FromSeconds(90), ct);
        if (paused != boundary.Entered("capacity-consume-before").Task)
        {
            var early = await claim;
            await WallRerouteFixture.SeedHoldAsync(schema, AgentKind.ClaudeCode, "fable");
            var reroute = await TickOrCaptureAsync(provider, ct);
            Deadlock.ShouldBeFalse(reroute.Error, "reroute");
            early.Dispatched.ShouldBe(1);
            early.Failures.ShouldBe(0);
            await AssertSingleDispatchAsync(schema.ConnectionString, task.Id, AgentModelLevel.Frontier, ct);
            return;
        }

        await WallRerouteFixture.SeedHoldAsync(schema, AgentKind.ClaudeCode, "fable");
        var other = TickOrCaptureAsync(provider, ct);
        await WaitForOverlapAsync(schema.ConnectionString, ClaudeKey, other, boundary, ct);
        boundary.Release("capacity-consume-before");
        if (boundary.Entered("capacity-supersede-after-lock").Task.IsCompleted)
        {
            await WaitForClaimAdvisoryWaitAsync(schema.ConnectionString, boundary.BackendPid, other, ct);
        }

        boundary.Release("capacity-supersede-after-lock");
        var claimResult = await claim;
        var rerouteResult = await other;
        Deadlock.ShouldBeFalse(rerouteResult.Error, "reroute");
        claimResult.Failures.ShouldBe(0);
        (claimResult.Dispatched + rerouteResult.Result?.Dispatched ?? 0).ShouldBe(1);
        await AssertSingleDispatchAsync(schema.ConnectionString, task.Id, AgentModelLevel.Frontier, ct);
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1593_competing_claim_and_continuation_complete_without_deadlock(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path, level: AgentModelLevel.Medium);
        // Unpinned, so warm reuse binds X inside the claim. B's committed AgentId stays null
        // and the continuation's sole-owner read does not see the uncommitted bind.
        var claimTask = await CapacityAdmissionLifecycleTests.SeedSharedAsync(
            schema.ConnectionString, workspace.Path, "warm reuse continuation", DateTime.UtcNow, ct);
        var continued = await SeedReleasedAnswerAsync(
            schema.ConnectionString, workspace.Path, agentId, persistentSession: false, ct);
        var boundary = new PauseBoundary();
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString, boundary,
            configure: services =>
            {
                services.AddSingleton<LandDeliveryBoundary>(boundary);
                services.AddDispatchConcurrencyAdmission();
            });
        await CapacityAdmissionLifecycleTests.GrantAsync(provider, claimTask.Id, ct);
        await EnsureWaitAsync(provider, continued.TaskId, ct);
        await using var claimScope = provider.CreateAsyncScope();
        var claim = claimScope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        var paused = await Task.WhenAny(boundary.Entered("capacity-consume-before").Task, claim)
            .WaitAsync(TimeSpan.FromSeconds(90), ct);
        if (paused != boundary.Entered("capacity-consume-before").Task)
        {
            var early = await claim;
            var continuation = await AcceptOrCaptureAsync(provider, continued.TaskId, ct);
            Deadlock.ShouldBeFalse(continuation, "continuation");
            if (early.Dispatched != 1)
            {
                await using var detail = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                var stored = await detail.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == claimTask.Id, ct);
                var grant = await detail.CapacityRecoveryProviderStates.AsNoTracking()
                    .SingleOrDefaultAsync(s => s.Kind == AgentKind.ClaudeCode, ct);
                early.Dispatched.ShouldBe(1,
                    $"failures={early.Failures} status={stored.Status} reason={stored.FailureReason} grant={grant?.GrantedWaitId}");
            }
            early.Failures.ShouldBe(0);
            await AssertSingleDispatchAsync(schema.ConnectionString, claimTask.Id, AgentModelLevel.Medium, ct);
            await AssertClaimHoldsWarmAgentAsync(schema.ConnectionString, claimTask.Id, agentId, ct);
            return;
        }

        var other = AcceptOrCaptureAsync(provider, continued.TaskId, ct);
        await WaitForOverlapAsync(schema.ConnectionString, ClaudeKey, other, boundary, ct);
        boundary.Release("capacity-consume-before");
        if (boundary.Entered("capacity-supersede-after-lock").Task.IsCompleted)
            await WaitForClaimAdvisoryWaitAsync(schema.ConnectionString, boundary.BackendPid, other, ct);
        boundary.Release("capacity-supersede-after-lock");
        var claimResult = await claim;
        var continuationError = await other;
        Deadlock.ShouldBeFalse(continuationError, "continuation");
        claimResult.Dispatched.ShouldBe(1);
        claimResult.Failures.ShouldBe(0);
        await AssertSingleDispatchAsync(schema.ConnectionString, claimTask.Id, AgentModelLevel.Medium, ct);
        await AssertClaimHoldsWarmAgentAsync(schema.ConnectionString, claimTask.Id, agentId, ct);
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("stop")]
    [Arguments("pool-session-exit")]
    [Arguments("standing-session-exit")]
    public async Task C1593_stop_and_warm_reuse_complete_without_deadlock(string mode, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var (agentId, sessionId) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path, level: AgentModelLevel.Medium);
        if (mode == "standing-session-exit")
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            await db.Agents.Where(a => a.Id == agentId).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsPoolDelegate, false)
                .SetProperty(a => a.AlwaysOn, false), ct);
        }

        var task = await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(
            schema.ConnectionString, workspace.Path, agentId, AgentModelLevel.Medium, mode);
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString, configure: services => services.AddDispatchConcurrencyAdmission());
        await CapacityAdmissionLifecycleTests.GrantAsync(provider, task.Id, ct);
        await using var holder = await CapacityLockProbe.OpenAsync(schema.ConnectionString, ct);
        var holderPid = await CapacityLockProbe.BackendPidAsync(holder, ct);
        await using var transaction = await holder.BeginTransactionAsync(ct);
        await CapacityLockProbe.HoldRowAsync(holder, transaction, "AgentSessions", sessionId, ct);
        await using var scope = provider.CreateAsyncScope();
        var tick = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        var committed = false;
        Exception? assertion = null;
        var waiting = "";
        try
        {
            var waiter = await CapacityLockProbe.PollInClaimSessionWaitAsync(
                schema.ConnectionString, holderPid, tick, CapacityLockProbe.Deadline, ct);
            waiting = await CapacityLockProbe.DescribePidAsync(schema.ConnectionString, waiter, ct);
            (await CapacityLockProbe.HoldsAdvisoryAsync(
                schema.ConnectionString, waiter, DispatchConcurrencyPolicy.ParallelAdvisoryKey, ct))
                .ShouldBeFalse($"pid {waiter} holds the parallel key while blocked on session {sessionId}: {waiting}");
            (await CapacityLockProbe.HoldsAdvisoryAsync(schema.ConnectionString, waiter, ClaudeKey, ct))
                .ShouldBeFalse($"pid {waiter} holds {ClaudeKey} while blocked on session {sessionId}");
            if (mode == "stop")
            {
                await CapacityLockProbe.HoldRowAsync(holder, transaction, "Agents", agentId, ct);
                await MarkStoppedAsync(holder, transaction, agentId, sessionId, ct);
            }
            else
            {
                // Exit observer: S becomes Stopped or Failed and an Idle agent is left unchanged.
                var status = mode == "standing-session-exit" ? (int)SessionStatus.Failed : (int)SessionStatus.Stopped;
                await MarkSessionStatusAsync(holder, transaction, sessionId, status, ct);
            }

            await transaction.CommitAsync(ct);
            committed = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            assertion = ex;
        }
        finally
        {
            if (!committed)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                catch (Exception ex) when (ex is not OperationCanceledException) { assertion ??= ex; }
            }
        }

        AgentTaskDispatcher.TickResult? result = null;
        try
        {
            result = await tick;
        }
        catch (Exception ex) when (assertion is not null && (ex is not OperationCanceledException || !ct.IsCancellationRequested))
        {
            // The assertion is the witness. A later tick failure must not replace it.
        }

        if (assertion is not null)
            throw assertion;
        Deadlock.ShouldBeFalse(result is null ? new InvalidOperationException("tick returned no result") : null, mode);
        result!.Dispatched.ShouldBe(1);
        result.Failures.ShouldBe(0);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.FailureReason.ShouldBeNull();
        stored.AgentSessionId.ShouldNotBeNull();
        stored.AgentSessionId.ShouldNotBe(sessionId, $"{mode} kept the session the holder already exited");
        var wait = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.TaskId == task.Id, ct);
        wait.AdmissionCount.ShouldBe(1);
        wait.State.ShouldBe(CapacityRecoveryWaitState.StartAccepted);
        wait.LaunchSessionId.ShouldBe(stored.AgentSessionId);
    }

    private static async Task<ArmedPath> ArmBlockedPathAsync(
        IsolatedTestSchema schema, string directory, string path, CancellationToken ct)
    {
        switch (path)
        {
            case "claim":
            {
                var task = await CapacityAdmissionLifecycleTests.SeedSharedAsync(
                    schema.ConnectionString, directory, "claim row", DateTime.UtcNow, ct);
                var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(schema.ConnectionString);
                await CapacityAdmissionLifecycleTests.GrantAsync(provider, task.Id, ct);
                return new ArmedPath("AgentTasks", task.Id, task.Id, ClaudeKey, false, token => TickAsync(provider, token), provider);
            }
            case "claim-agent-row":
            {
                var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
                    schema.ConnectionString, directory, level: AgentModelLevel.Medium);
                await ClearPersistentSessionAsync(schema.ConnectionString, agentId, ct);
                var task = await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(
                    schema.ConnectionString, directory, agentId, AgentModelLevel.Medium, "cold agent row");
                var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
                    schema.ConnectionString, configure: services => services.AddDispatchConcurrencyAdmission());
                await CapacityAdmissionLifecycleTests.GrantAsync(provider, task.Id, ct);
                return new ArmedPath("Agents", agentId, task.Id, ClaudeKey, true, token => TickAsync(provider, token), provider);
            }
            case "queued-reroute":
            {
                await WallRerouteFixture.SeedChainAsync(schema,
                    (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                    (AgentKind.ClaudeCode, AgentModelLevel.High));
                var task = await CapacityAdmissionLifecycleTests.SeedSharedAsync(
                    schema.ConnectionString, directory, "queued reroute", DateTime.UtcNow, ct);
                await SetLevelAsync(schema.ConnectionString, task.Id, AgentModelLevel.Frontier, TaskComplexity.Hard, ct);
                await WallRerouteFixture.SeedHoldAsync(schema, AgentKind.ClaudeCode, "fable");
                var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(schema.ConnectionString);
                await EnsureWaitAsync(provider, task.Id, ct);
                return new ArmedPath("AgentTasks", task.Id, task.Id, ClaudeKey, false, token => TickAsync(provider, token), provider);
            }
            case "routing-resume":
            {
                await WallRerouteFixture.SeedChainAsync(schema, (AgentKind.ClaudeCode, AgentModelLevel.High));
                var first = await CapacityAdmissionLifecycleTests.SeedSharedAsync(
                    schema.ConnectionString, directory, "routing A", DateTime.UtcNow, ct);
                await MarkRoutingBlockedAsync(schema.ConnectionString, first.Id, ct);
                var second = await CapacityAdmissionLifecycleTests.SeedSharedAsync(
                    schema.ConnectionString, directory, "routing B", DateTime.UtcNow.AddSeconds(1), ct);
                await MarkRoutingBlockedAsync(schema.ConnectionString, second.Id, ct);
                var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(schema.ConnectionString);
                await EnsureWaitAsync(provider, second.Id, ct, AgentKind.Grok, "grok-4.7");
                return new ArmedPath("AgentTasks", first.Id, first.Id, GrokKey, false, token => TickAsync(provider, token), provider);
            }
            case "continuation":
            case "continuation-agent-row":
            {
                var (agentId, sessionId) = await SeedRunningPoolAsync(schema.ConnectionString, directory, ct);
                var released = await SeedReleasedAnswerAsync(
                    schema.ConnectionString, directory, agentId, persistentSession: true, ct, sessionId);
                var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(schema.ConnectionString);
                await EnsureWaitAsync(provider, released.TaskId, ct);
                var table = path == "continuation" ? "AgentTasks" : "Agents";
                var row = path == "continuation" ? released.TaskId : agentId;
                return new ArmedPath(table, row, released.TaskId, ClaudeKey, false,
                    token => AcceptAsync(provider, released.TaskId, token), provider);
            }
            case "cancel":
            {
                var task = await CapacityAdmissionLifecycleTests.SeedSharedAsync(
                    schema.ConnectionString, directory, "cancel row", DateTime.UtcNow, ct);
                var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(schema.ConnectionString);
                await EnsureWaitAsync(provider, task.Id, ct);
                return new ArmedPath("AgentTasks", task.Id, task.Id, ClaudeKey, false,
                    token => CancelAsync(provider, task.Id, token), provider);
            }
            case "retry":
            {
                var task = await CapacityAdmissionLifecycleTests.SeedSharedAsync(
                    schema.ConnectionString, directory, "retry row", DateTime.UtcNow, ct);
                await SetStatusAsync(schema.ConnectionString, task.Id, AgentTaskStatus.Failed, ct);
                var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(schema.ConnectionString);
                await EnsureWaitAsync(provider, task.Id, ct);
                return new ArmedPath("AgentTasks", task.Id, task.Id, ClaudeKey, false,
                    token => RetryAsync(provider, task.Id, token), provider);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(path), path, "unknown blocked path");
        }
    }

    private static async Task<ArmedPath> ArmProviderPathAsync(
        IsolatedTestSchema schema, string directory, string path, CancellationToken ct)
    {
        if (path == "queued-reroute")
            return await ArmBlockedPathAsync(schema, directory, path, ct);
        if (path == "continuation")
            return await ArmBlockedPathAsync(schema, directory, path, ct);
        throw new ArgumentOutOfRangeException(nameof(path), path, "unknown provider path");
    }

    private static async Task AssertClaimLaunchHoldsTaskAsync(
        IsolatedTestSchema schema, string directory, CancellationToken ct)
    {
        var task = await CapacityAdmissionLifecycleTests.SeedSharedAsync(
            schema.ConnectionString, directory, "claim launch", DateTime.UtcNow, ct);
        var boundary = new PauseBoundary();
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString, boundary, configure: services => services.AddSingleton<LandDeliveryBoundary>(boundary));
        await CapacityAdmissionLifecycleTests.GrantAsync(provider, task.Id, ct);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tick = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        var winner = await Task.WhenAny(boundary.Entered("capacity-consume-before").Task, tick)
            .WaitAsync(TimeSpan.FromSeconds(90), ct);
        if (winner != boundary.Entered("capacity-consume-before").Task)
        {
            var early = await ObserveTickAsync(tick);
            true.ShouldBeFalse(
                $"claim never blocked on the provider while holding the task; dispatched={early.Result?.Dispatched} failures={early.Result?.Failures} error={early.Error?.GetType().Name}:{early.Error?.Message}");
            return;
        }

        var dirty = db.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && entry.Entity is AgentTask or Agent or AgentSession)
            .Select(entry => entry.Metadata.Name + ":" + entry.State)
            .ToList();
        await using var holder = await CapacityLockProbe.OpenAsync(schema.ConnectionString, ct);
        var holderPid = await CapacityLockProbe.BackendPidAsync(holder, ct);
        await CapacityLockProbe.HoldAdvisoryAsync(holder, ClaudeKey, ct);
        boundary.Release("capacity-consume-before");
        Exception? assertion = null;
        try
        {
            await CapacityLockProbe.PollBlockedByAsync(
                schema.ConnectionString, holderPid, tick, CapacityLockProbe.Deadline, ct);
            var acquired = await CapacityLockProbe.TryNowaitAsync(
                schema.ConnectionString, "AgentTasks", task.Id, ct);
            acquired.ShouldBeFalse("55P03: the claim must already hold the task while blocked on the provider");
            dirty.ShouldBeEmpty();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            assertion = ex;
        }
        finally
        {
            try { await CapacityLockProbe.UnlockAdvisoryAsync(holder, ClaudeKey, CancellationToken.None); }
            catch (Exception ex) when (ex is not OperationCanceledException) { assertion ??= ex; }
        }

        await FinishStartedAsync(tick, assertion);
    }

    private static async Task AssertBlockedWithoutAdvisoryAsync(
        string connection, string table, Guid rowId, string kindKey, bool alsoParallel,
        Func<CancellationToken, Task> start, CancellationToken ct)
    {
        await using var holder = await CapacityLockProbe.OpenAsync(connection, ct);
        var holderPid = await CapacityLockProbe.BackendPidAsync(holder, ct);
        await using var transaction = await holder.BeginTransactionAsync(ct);
        await CapacityLockProbe.HoldRowAsync(holder, transaction, table, rowId, ct);
        var started = start(ct);
        Exception? assertion = null;
        try
        {
            var waiter = await CapacityLockProbe.PollBlockedByAsync(
                connection, holderPid, started, CapacityLockProbe.Deadline, ct);
            (await CapacityLockProbe.HoldsAdvisoryAsync(connection, waiter, kindKey, ct))
                .ShouldBeFalse($"pid {waiter} holds {kindKey} while blocked on {table} {rowId}");
            if (alsoParallel)
            {
                (await CapacityLockProbe.HoldsAdvisoryAsync(
                    connection, waiter, DispatchConcurrencyPolicy.ParallelAdvisoryKey, ct))
                    .ShouldBeFalse($"pid {waiter} holds the parallel key while blocked on {table} {rowId}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            assertion = ex;
        }
        finally
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch (Exception ex) when (ex is not OperationCanceledException) { assertion ??= ex; }
        }

        await FinishStartedAsync(started, assertion);
    }

    private static async Task FinishStartedAsync(Task started, Exception? assertion)
    {
        try
        {
            await started;
        }
        catch (Exception ex) when (assertion is not null && ex is not OperationCanceledException)
        {
            // Prefer the lock assertion over a follow-on failure after the holder is released.
        }

        if (assertion is not null)
            throw assertion;
    }

    private static async Task TickAsync(ServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
    }

    private static async Task AcceptAsync(ServiceProvider provider, Guid taskId, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId, ct);
        await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
            .TryAcceptReleasedSeatAnswerAsync(task, "continue the released seat", AnswerOrigin.Web, null, ct);
    }

    private static async Task CancelAsync(ServiceProvider provider, Guid taskId, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CancelAsync(taskId, ct);
    }

    private static async Task RetryAsync(ServiceProvider provider, Guid taskId, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(taskId, ct);
    }

    private static async Task<CapturedTick> TickOrCaptureAsync(ServiceProvider provider, CancellationToken ct)
    {
        try
        {
            await using var scope = provider.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
            return new CapturedTick(result, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new CapturedTick(null, ex);
        }
    }

    private static async Task<Exception?> AcceptOrCaptureAsync(ServiceProvider provider, Guid taskId, CancellationToken ct)
    {
        try
        {
            await AcceptAsync(provider, taskId, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ex;
        }
    }

    private static async Task<CapturedTick> ObserveTickAsync(Task<AgentTaskDispatcher.TickResult> tick)
    {
        try
        {
            return new CapturedTick(await tick, null);
        }
        catch (Exception ex)
        {
            return new CapturedTick(null, ex);
        }
    }

    private static async Task WaitForOverlapAsync(
        string connection, string kindKey, Task other, PauseBoundary boundary, CancellationToken ct)
    {
        var until = DateTime.UtcNow + CapacityLockProbe.Deadline;
        while (DateTime.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();
            if (other.IsCompleted)
                throw new InvalidOperationException(
                    "the competing path finished before it overlapped the claim: " + await CapacityLockProbe.DescribeAsync(other));
            if (boundary.Entered("capacity-supersede-after-lock").Task.IsCompleted)
                return;
            if (await CapacityLockProbe.PidHoldingAdvisoryAsync(connection, kindKey, ct) is not null)
                return;
            if (await CapacityLockProbe.AnyLockWaitPidAsync(connection, ct) is not null)
                return;
            await Task.Delay(20, ct);
        }

        throw new TimeoutException("the competing path neither held the provider key nor waited on a row");
    }

    private static async Task WaitForClaimAdvisoryWaitAsync(
        string connection, int claimPid, Task other, CancellationToken ct)
    {
        var until = DateTime.UtcNow + CapacityLockProbe.Deadline;
        while (DateTime.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();
            if (other.IsCompleted || claimPid == 0)
                return;
            if (await CapacityLockProbe.BlockedByAsync(connection, claimPid, ct) is not null
                || await ClaimWaitingAsync(connection, claimPid, ct))
                return;
            await Task.Delay(20, ct);
        }

        throw new TimeoutException($"claim pid {claimPid} did not wait on the provider advisory");
    }

    private static async Task<bool> ClaimWaitingAsync(string connection, int claimPid, CancellationToken ct)
    {
        await using var opened = await CapacityLockProbe.OpenAsync(connection, ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM pg_stat_activity
                WHERE pid = @pid AND wait_event_type = 'Lock')
            """, opened);
        command.Parameters.AddWithValue("pid", claimPid);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task EnsureWaitAsync(
        IServiceProvider provider, Guid taskId, CancellationToken ct,
        AgentKind kind = AgentKind.ClaudeCode, string alias = "opus")
    {
        var recovery = provider.GetRequiredService<CapacityRecoveryService>();
        await recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{taskId:N}", CapacityWaitConsumerKind.QueuedTask,
            executionKind: kind, alias: alias, taskId: taskId, holdAlreadyCleared: true), ct);
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedRunningPoolAsync(
        string connection, string directory, CancellationToken ct)
    {
        var (agentId, sessionId) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(connection, directory);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var agent = await db.Agents.SingleAsync(a => a.Id == agentId, ct);
        agent.Status = AgentStatus.Running;
        agent.PoolIdleSince = null;
        await db.SaveChangesAsync(ct);
        return (agentId, sessionId);
    }

    private static async Task<ReleasedAnswer> SeedReleasedAnswerAsync(
        string connection, string directory, Guid agentId, bool persistentSession, CancellationToken ct, Guid? sessionId = null)
    {
        var now = DateTime.UtcNow;
        var started = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        var settled = started;
        var token = Guid.NewGuid();
        var store = Guid.NewGuid();
        const string runner = "runner-c1593";
        var ownedSession = sessionId ?? Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        if (sessionId is null)
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = ownedSession,
                DefinitionName = "c1593-released",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = directory,
                Cols = 120,
                Rows = 30,
                CreatedAt = started,
                StartedAt = started,
                LastSeenAt = started,
                RunnerId = runner,
                RunnerStoreId = store,
                RunnerCwd = directory,
            });
        }
        else
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == ownedSession, ct);
            session.RunnerId = runner;
            session.RunnerStoreId = store;
            session.RunnerCwd = directory;
            session.StartedAt = started;
            session.Status = SessionStatus.Running;
        }

        if (persistentSession)
        {
            var agent = await db.Agents.SingleAsync(a => a.Id == agentId, ct);
            agent.PersistentSessionId = ownedSession.ToString("D");
            agent.Status = AgentStatus.Running;
            agent.PoolIdleSince = null;
            agent.IsPoolDelegate = true;
        }

        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "released seat",
            Goal = "continue after the seat release",
            Role = AgentTaskRole.Docs,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Status = AgentTaskStatus.Blocked,
            AgentId = agentId,
            AgentSessionId = ownedSession,
            Ephemeral = true,
            Attempt = 1,
            RunnerId = runner,
            ConcurrencyToken = token,
            CompletedAt = settled,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = started,
        });
        db.RunnerSeatReleases.Add(new RunnerSeatRelease
        {
            Id = Guid.NewGuid(),
            RunnerId = runner,
            RunnerStoreId = store,
            SessionId = ownedSession,
            AcceptedStartedAt = started,
            TaskId = taskId,
            Attempt = 1,
            AgentId = agentId,
            SettlementRevision = token,
            SettledAt = settled,
            State = RunnerSeatReleaseState.Confirmed,
            Revision = 1,
            ActionId = Guid.NewGuid(),
            ReasonCode = "Released",
            OutcomeCode = "Released",
            ConfirmedAt = settled,
            CreatedAt = started,
            UpdatedAt = settled,
        });
        await db.SaveChangesAsync(ct);
        return new ReleasedAnswer(taskId);
    }

    private static async Task ClearPersistentSessionAsync(string connection, Guid agentId, CancellationToken ct)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var agent = await db.Agents.SingleAsync(a => a.Id == agentId, ct);
        agent.PersistentSessionId = null;
        await db.SaveChangesAsync(ct);
    }

    private static async Task SetLevelAsync(
        string connection, Guid taskId, AgentModelLevel level, TaskComplexity complexity, CancellationToken ct)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId, ct);
        task.ModelLevel = level;
        task.Complexity = complexity;
        await db.SaveChangesAsync(ct);
    }

    private static async Task MarkRoutingBlockedAsync(string connection, Guid taskId, CancellationToken ct)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId, ct);
        task.Status = AgentTaskStatus.Blocked;
        task.AgentKind = AgentKind.Grok;
        task.Complexity = TaskComplexity.Hard;
        task.FailureReason = ComplexityRoutingService.RoutingExhaustedPrefix + "c1593";
        await db.SaveChangesAsync(ct);
    }

    private static async Task SetStatusAsync(string connection, Guid taskId, AgentTaskStatus status, CancellationToken ct)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId, ct);
        task.Status = status;
        await db.SaveChangesAsync(ct);
    }

    private static async Task AssertSingleDispatchAsync(
        string connection, Guid taskId, AgentModelLevel level, CancellationToken ct)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched, stored.FailureReason);
        stored.ModelLevel.ShouldBe(level);
        (await db.AgentSessions.CountAsync(s => s.Id == stored.AgentSessionId, ct)).ShouldBe(1);
    }

    private static async Task AssertClaimHoldsWarmAgentAsync(
        string connection, Guid taskId, Guid agentId, CancellationToken ct)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
        stored.AgentId.ShouldBe(agentId, "the claim must warm-reuse X so the continuation waits on that row");
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("supervisor-start")]
    public async Task C1593_S3_path_blocked_on_row_holds_no_provider_lock(string path, CancellationToken ct)
    {
        path.ShouldBe("supervisor-start");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentSupervisionTests.NewTempRoot();
        var tail = new ProviderTail();
        var adapter = new FakeAgentProtocolAdapter();
        await using var harness = AgentSupervisionTests.BuildHarness(
            tempRoot,
            [adapter],
            definitionKind: "ClaudeCode",
            connectionString: schema.ConnectionString,
            configureDb: options => options.AddInterceptors(tail),
            supervision: new Antiphon.Server.Application.Settings.SupervisionSettings
            {
                TickSeconds = 1,
                BackoffBaseSeconds = 5,
                HealthyUptimeResetMinutes = 10,
                FreshAfterResumeFailures = 2,
                CapacityRecovery = new Antiphon.Server.Application.Settings.CapacityRecoverySettings
                {
                    Enabled = true,
                    JitterSeconds = 0,
                },
            });
        try
        {
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(harness, tempRoot);
            var sessionId = Guid.NewGuid();
            var started = DateTime.UtcNow.AddMinutes(-10);
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var owner = await db.Agents.SingleAsync(a => a.Id == agent.Id, ct);
                owner.Kind = AgentKind.ClaudeCode;
                owner.PersistentSessionId = sessionId.ToString("D");
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId,
                    AgentKind = AgentKind.ClaudeCode,
                    DefinitionName = "fake",
                    Cwd = owner.WorkingDirectory,
                    Status = SessionStatus.Failed,
                    CreatedAt = started,
                    StartedAt = started,
                });
                await db.SaveChangesAsync(ct);
            }

            var recovery = harness.Provider.GetRequiredService<CapacityRecoveryService>();
            var wait = await recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"agent:{agent.Id:N}", CapacityWaitConsumerKind.StandingStart,
                holdAlreadyCleared: true, agentId: agent.Id, sessionId: sessionId), ct);
            await recovery.ReconcileAsync(ct);
            await using var holder = await CapacityLockProbe.OpenAsync(schema.ConnectionString, ct);
            var holderPid = await CapacityLockProbe.BackendPidAsync(holder, ct);
            await using var transaction = await holder.BeginTransactionAsync(ct);
            await CapacityLockProbe.HoldRowAsync(holder, transaction, "AgentSessions", sessionId, ct);
            await using var tickScope = harness.Provider.CreateAsyncScope();
            var tick = tickScope.ServiceProvider.GetRequiredService<AgentSupervisorService>().TickAsync(ct);
            var waiter = await CapacityLockProbe.PollBlockedByAsync(
                schema.ConnectionString, holderPid, tick, CapacityLockProbe.Deadline, ct);
            (await CapacityLockProbe.HoldsAdvisoryAsync(schema.ConnectionString, waiter, ClaudeKey, ct))
                .ShouldBeFalse($"pid {waiter} holds {ClaudeKey} while blocked on session {sessionId}");
            await transaction.CommitAsync(ct);
            (await tick).ShouldBeGreaterThan(0);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), ct);
            foreach (var sql in tail.AfterProvider)
                sql.Trim().Equals("COMMIT", StringComparison.OrdinalIgnoreCase).ShouldBeTrue(sql);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            (await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct))
                .AdmissionCount.ShouldBe(1);
        }
        finally
        {
            await harness.DisposeAsync();
            await AgentSupervisionTests.CleanupAsync(tempRoot);
        }
    }

    private sealed class ProviderTail : DbCommandInterceptor
    {
        private bool _seen;
        public List<string> AfterProvider { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Note(command, eventData.Context);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Note(command, eventData.Context);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Note(command, eventData.Context);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Note(DbCommand command, DbContext? context)
        {
            if (context?.ChangeTracker.Entries<Agent>().Any() != true)
                return;
            var text = command.CommandText;
            if (!_seen
                && text.Contains("CapacityRecoveryProviderStates", StringComparison.Ordinal)
                && text.Contains("UPDATE", StringComparison.Ordinal))
            {
                _seen = true;
                return;
            }

            if (_seen)
                AfterProvider.Add(text);
        }
    }

    private static async Task MarkStoppedAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid agentId, Guid sessionId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE "Agents" SET "Status" = 4, "PoolIdleSince" = NULL, "UpdatedAt" = NOW() WHERE "Id" = @agent;
            UPDATE "AgentSessions" SET "Status" = 4, "EndedAt" = NOW() WHERE "Id" = @session;
            """, connection, transaction);
        command.Parameters.AddWithValue("agent", agentId);
        command.Parameters.AddWithValue("session", sessionId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task MarkSessionStatusAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sessionId, int status, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """UPDATE "AgentSessions" SET "Status" = @status, "EndedAt" = NOW() WHERE "Id" = @session;""",
            connection, transaction);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("session", sessionId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private sealed class ArmedPath : IAsyncDisposable
    {
        public ArmedPath(
            string table, Guid rowId, Guid taskId, string kindKey, bool alsoParallel,
            Func<CancellationToken, Task> start, ServiceProvider provider)
        {
            Table = table;
            RowId = rowId;
            TaskId = taskId;
            KindKey = kindKey;
            AlsoParallel = alsoParallel;
            Start = start;
            Provider = provider;
        }

        public string Table { get; }
        public Guid RowId { get; }
        public Guid TaskId { get; }
        public string KindKey { get; }
        public bool AlsoParallel { get; }
        public Func<CancellationToken, Task> Start { get; }
        public ServiceProvider Provider { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private readonly record struct ReleasedAnswer(Guid TaskId);

    private readonly record struct CapturedTick(AgentTaskDispatcher.TickResult? Result, Exception? Error);

    private static class Deadlock
    {
        public static void ShouldBeFalse(Exception? error, string path)
        {
            if (error is null)
                return;
            Contains40P01(error).ShouldBeFalse($"{path} hit 40P01: {error}");
            throw error;
        }

        private static bool Contains40P01(Exception error)
        {
            for (Exception? current = error; current is not null; current = current.InnerException)
            {
                if (current is PostgresException postgres && postgres.SqlState == "40P01")
                    return true;
            }

            return error.ToString().Contains("40P01", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Pauses only on the named CARD-1593 boundaries. Other delivery points return immediately.
    /// </summary>
    private sealed class PauseBoundary : LandDeliveryBoundary, IDbCommandInterceptor
    {
        private readonly Dictionary<string, TaskCompletionSource> _entered = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskCompletionSource> _release = new(StringComparer.Ordinal);
        private int _backendPid;

        public PauseBoundary()
        {
            foreach (var name in new[] { "capacity-consume-before", "capacity-supersede-after-lock" })
            {
                _entered[name] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _release[name] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public int BackendPid => _backendPid;

        public TaskCompletionSource Entered(string boundary) => _entered[boundary];

        public void Release(string boundary) => _release[boundary].TrySetResult();

        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (!_entered.TryGetValue(boundary, out var entered))
                return;
            entered.TrySetResult();
            await _release[boundary].Task.WaitAsync(ct);
        }

        public ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return new ValueTask<DbDataReader>(result);
        }

        public ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return new ValueTask<int>(result);
        }

        private void Capture(DbCommand command)
        {
            if (command.Connection is NpgsqlConnection connection && connection.ProcessID != 0)
                _backendPid = connection.ProcessID;
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c1593-lock").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
