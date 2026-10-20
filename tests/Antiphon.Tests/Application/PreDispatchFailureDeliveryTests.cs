using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.RolePolicyToolBundleDispatchTests;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class PreDispatchFailureDeliveryTests
{
    private const string UnknownKey = "tool-does-not-exist";

    [Test]
    [Timeout(120_000)]
    public async Task Pre_dispatch_failed_commit_keeps_the_caller_obligation(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var parent = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            PreserveDatabaseOnDispose = true,
        });
        using var interrupted = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var fault = new FailedCommitCut(interrupted);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sink = new RecordingSink();
        Guid taskId;
        await using (var db = parent.CreateObserverContext())
            taskId = (await SeedQueuedAsync(db, parent.TempRoot, AgentTaskRole.Review, parent.SessionId)).Id;

        await using (var producer = BuildProvider(schema.ConnectionString, sink, UnknownKey,
            parent, clock, fault, checkEnabled: false))
            await Should.ThrowAsync<OperationCanceledException>(() => TickAsync(producer, interrupted.Token));

        fault.Hits.ShouldBe(1, "cut is triggered by the Failed transition, independent of an outbox insert");
        sink.Items.ShouldBeEmpty();
        await using var verify = parent.CreateObserverContext();
        var failed = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.DispatchedAt.ShouldBeNull();
        var failedEvent = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Failed, ct);
        failedEvent.Detail.ShouldContain("stage_tool_bundle_unknown");
        (await RowsAsync(schema.ConnectionString, parent.SessionId)).ShouldBeEmpty();
        (await NotePromptsAsync(schema.ConnectionString, parent.SessionId)).ShouldBeEmpty();
        var obligation = (await verify.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.TaskId == taskId).ToListAsync(ct)).ShouldHaveSingleItem(
                "the first committed Failed save must already own durable caller delivery");
        obligation.SourceEventId.ShouldBe(failedEvent.Id);
        obligation.Kind.ShouldBe(LandNotificationKind.DeliveryFailure);
        obligation.Body.ShouldContain("stage_tool_bundle_unknown");
        obligation.Body.ShouldContain("Delegation:RolePolicy:Review:Bundles");
        obligation.Body.ShouldContain(UnknownKey);
        failed.NextCheckAt.ShouldBeNull();

        await using var recovered = BuildProvider(schema.ConnectionString, sink, UnknownKey,
            parent, clock, checkEnabled: false);
        await RecoverAsync(recovered, ct);
        await parent.Queue.FlushStrandedQueuesAsync(ct);
        await RecoverAsync(recovered, ct);
        var row = (await RowsAsync(schema.ConnectionString, parent.SessionId)).ShouldHaveSingleItem();
        row.SourceLandNotificationId.ShouldBe(obligation.Id);
        row.Body.ShouldBe(obligation.Body);
        await AssertDeliveredOnceAsync(parent, taskId, row.Id, "recovered committed failure");
        (await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == obligation.Id, ct))
            .State.ShouldBe(LandNotificationState.Confirmed);
        await RecoverAsync(recovered, ct);
        await parent.Queue.OnTurnEndAsync(parent.SessionId, ct);
        await AssertDeliveredOnceAsync(parent, taskId, row.Id, "replay");
    }

    private static async Task RecoverAsync(ServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().RemindUnacknowledgedFailuresAsync(ct);
    }

    private sealed class FailedCommitCut(CancellationTokenSource interrupted) : SaveChangesInterceptor
    {
        public int Hits { get; private set; }
        private bool _armed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            _armed = Hits == 0 && data.Context!.ChangeTracker.Entries<AgentTask>().Any(
                e => e.State == EntityState.Modified && e.Entity.Status == AgentTaskStatus.Failed);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (_armed)
            {
                Hits++;
                interrupted.Cancel();
                throw new OperationCanceledException(interrupted.Token);
            }
            return ValueTask.FromResult(result);
        }
    }
}
