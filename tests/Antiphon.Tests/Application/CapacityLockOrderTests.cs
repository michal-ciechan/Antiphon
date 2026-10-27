using System.Data.Common;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Two dispatchers, one provider. The claim and the queued reroute must take the capacity
/// provider lock and the task row in the same order. The inverted order deadlocks (40P01)
/// and the dispatcher's generic catch then fails the task.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class CapacityLockOrderTests
{
    [Test]
    [Timeout(120_000)]
    public async Task Claim_and_queued_reroute_share_one_lock_order(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await WallRerouteFixture.SeedChainAsync(schema,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.ClaudeCode, AgentModelLevel.High));
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path, level: AgentModelLevel.Frontier);
        var task = await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(
            schema.ConnectionString, workspace.Path, agentId, AgentModelLevel.Frontier, "lock order");
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id, ct);
            row.Complexity = TaskComplexity.Hard;
            await db.SaveChangesAsync(ct);
        }

        var (capacity, _, capacityProvider) = CapacityRecoveryTestSupport.CreateService(schema);
        await using var capacityScope = capacityProvider;
        var wait = await capacity.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{task.Id:N}", CapacityWaitConsumerKind.QueuedTask,
            executionKind: AgentKind.ClaudeCode, alias: "fable", taskId: task.Id, holdAlreadyCleared: true), ct);
        (await capacity.GrantReadyAsync(ct)).ShouldBe(1);

        var gate = new FirstLockGate(task.Id);
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(schema.ConnectionString, gate);
        await using var scopeA = provider.CreateAsyncScope();
        await using var scopeB = provider.CreateAsyncScope();
        var tickA = scopeA.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        var entered = await Task.WhenAny(gate.Entered.Task, tickA).WaitAsync(TimeSpan.FromSeconds(90), ct);
        if (entered != gate.Entered.Task)
        {
            var early = await tickA;
            throw new InvalidOperationException(
                $"claim never reached a capacity or task lock; tick A finished dispatched={early.Dispatched} failures={early.Failures} skippedCapacity={early.SkippedCapacityWait}");
        }

        // The hold appears only after the claim has its first lock, so this tick rewalks
        // fable → opus (same provider) while the claim still holds that lock.
        await WallRerouteFixture.SeedHoldAsync(schema, AgentKind.ClaudeCode, "fable");
        var tickB = scopeB.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        var sawLockWait = await WaitForLockAsync(schema.ConnectionString, TimeSpan.FromSeconds(20), ct);
        sawLockWait.ShouldBeTrue("the claim and the queued reroute must overlap on one lock; a missed race is not a pass");
        gate.Release.TrySetResult();

        Exception? boom = null;
        AgentTaskDispatcher.TickResult[]? results = null;
        try
        {
            results = await Task.WhenAll(tickA, tickB).WaitAsync(TimeSpan.FromSeconds(45), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            boom = ex;
        }

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        var admitted = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
        boom.ShouldBeNull(boom?.ToString() ?? "");
        results.ShouldNotBeNull();
        results!.All(r => r.Failures == 0).ShouldBeTrue(
            $"failures A={results[0].Failures} B={results[1].Failures}; status={stored.Status} level={stored.ModelLevel}");
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        // The claim already held the provider, so the reroute must not overwrite it.
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        admitted.AdmissionCount.ShouldBe(1);
        admitted.State.ShouldBeOneOf(CapacityRecoveryWaitState.Admitted, CapacityRecoveryWaitState.StartAccepted);
        (await verify.AgentSessions.CountAsync(s => s.Id == stored.AgentSessionId, ct)).ShouldBe(1);
    }

    private static async Task<bool> WaitForLockAsync(string connectionString, TimeSpan budget, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        var until = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();
            await using var command = new NpgsqlCommand(
                """
                SELECT 1
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND pid <> pg_backend_pid()
                  AND wait_event_type = 'Lock'
                LIMIT 1
                """, connection);
            if (await command.ExecuteScalarAsync(ct) is not null)
                return true;
            await Task.Delay(50, ct);
        }

        return false;
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-lock-order").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// Pauses once, after the first capacity-provider or task-row lock has been acquired.
    /// Pausing in Executing would stop before PostgreSQL grants the lock.
    /// </summary>
    private sealed class FirstLockGate : DbCommandInterceptor
    {
        private readonly Guid _taskId;
        private int _paused;

        public FirstLockGate(Guid taskId) => _taskId = taskId;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            await PauseIfFirstLockAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            await PauseIfFirstLockAsync(command, cancellationToken);
            return result;
        }

        private async Task PauseIfFirstLockAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (!IsClaimLock(command))
                return;
            if (Interlocked.Increment(ref _paused) != 1)
                return;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }

        private bool IsClaimLock(DbCommand command)
        {
            var text = command.CommandText ?? string.Empty;
            if (text.Contains("antiphon.capacity.kind.", StringComparison.Ordinal))
                return true;
            if (!text.Contains("FOR UPDATE", StringComparison.Ordinal)
                || !text.Contains("AgentTasks", StringComparison.Ordinal))
                return false;
            foreach (System.Data.Common.DbParameter parameter in command.Parameters)
            {
                if (parameter.Value is Guid id && id == _taskId)
                    return true;
                if (parameter.Value is string textValue && textValue.Contains(_taskId.ToString(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
