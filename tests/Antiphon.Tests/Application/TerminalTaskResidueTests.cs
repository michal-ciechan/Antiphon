using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.AgentTaskReplyIntegrationTests;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1488 and CARD-1497. A cancelled task leaves no repository lease waiter, so the next land
/// on that repository does not spend its yield budget on a sync that is never coming back; and no
/// never-typed closing-line nudge in its own session or task-wait note in its parent's queue.
/// Cleanup is keyed by the task: another open task's waiter and notes survive. The process-exit
/// failure arm is in <see cref="AgentTaskDeadSessionReconciliationTests"/>.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel]
public class TerminalTaskResidueTests
{
    private const string NudgeText = "Your turn ended without the closing report line.";

    [Test]
    public async Task Cancel_clears_its_settlement_waiter_so_the_next_land_does_not_yield()
    {
        using var workspace = new TempWorkspace();
        var (task, _) = await SeedDispatchedTaskAsync(workspace.Path, configure: t => t.Status = AgentTaskStatus.Working);
        using var factory = NewFactory();
        var waiters = factory.ServiceProvider.GetRequiredService<RepositoryLeaseWaiters>();
        var common = Path.Combine(workspace.Path, ".git");
        waiters.Register(common, task.Id, RepositoryLeasePurposes.WorktreeSettlement, DateTimeOffset.UtcNow);

        await factory.ServiceProvider.GetRequiredService<AgentTaskService>().CancelAsync(task.Id, CancellationToken.None);

        (await ReadStatusAsync(task.Id)).ShouldBe(AgentTaskStatus.Canceled);
        waiters.Snapshot(common).ShouldBeEmpty("a cancelled task's settlement sync is never re-handed");
        waiters.DecideYield(common, Guid.NewGuid(), DateTimeOffset.UtcNow, 90).Outcome
            .ShouldBe(LandYieldOutcome.Proceed, "the next land on that repository must not wait for it");
    }

    [Test]
    public async Task Cancel_keeps_another_open_tasks_waiter_on_the_same_repository()
    {
        using var workspace = new TempWorkspace();
        var (canceled, _) = await SeedDispatchedTaskAsync(workspace.Path, configure: t => t.Status = AgentTaskStatus.Working);
        var (open, _) = await SeedDispatchedTaskAsync(workspace.Path, configure: t => t.Status = AgentTaskStatus.Working);
        using var factory = NewFactory();
        var waiters = factory.ServiceProvider.GetRequiredService<RepositoryLeaseWaiters>();
        var common = Path.Combine(workspace.Path, ".git");
        var now = DateTimeOffset.UtcNow;
        waiters.Register(common, canceled.Id, RepositoryLeasePurposes.WorktreeSettlement, now);
        waiters.Register(common, open.Id, RepositoryLeasePurposes.WorktreeSettlement, now.AddSeconds(1));

        await factory.ServiceProvider.GetRequiredService<AgentTaskService>().CancelAsync(canceled.Id, CancellationToken.None);

        waiters.Snapshot(common).Select(w => w.TaskId).ShouldBe([open.Id],
            "cleanup is keyed by the cancelled task, never by its repository");
        waiters.DecideYield(common, Guid.NewGuid(), now.AddSeconds(2), 90).Outcome
            .ShouldBe(LandYieldOutcome.Yield, "the still-open task's sync keeps its turn");
        (await ReadStatusAsync(open.Id)).ShouldBe(AgentTaskStatus.Working);
    }

    [Test]
    public async Task Cancel_retires_the_unsent_nudge_and_the_parents_wait_note_only_for_that_task()
    {
        using var workspace = new TempWorkspace();
        var parent = await SeedSessionAsync(workspace.Path);
        var (task, session) = await SeedDispatchedTaskAsync(
            workspace.Path, parent, t => t.Status = AgentTaskStatus.Working);
        using var factory = NewFactory();
        var queue = Queue(factory);

        var nudgeId = await EnqueueAsync(queue, session,
            $"{DelegationReportFormatter.TaskMarker(task.Id)} {NudgeText}", QueuedMessageOrigin.Delegation);
        await using (var db = CreateContext())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            row.ReportNudgedAt = DateTime.UtcNow;
            row.ReportNudgedSequence = 3;
            row.ReportNudgeMessageId = nudgeId;
            await db.SaveChangesAsync();
        }
        var waitId = await EnqueueAsync(queue, parent, $"[task {DelegationReportFormatter.Short(task.Id)} waiting] idle",
            QueuedMessageOrigin.Delegation, $"task-wait:{task.Id:N}", task.Id);
        var sibling = Guid.NewGuid();
        var siblingWait = await EnqueueAsync(queue, parent, $"[task {DelegationReportFormatter.Short(sibling)} waiting] other",
            QueuedMessageOrigin.Delegation, $"task-wait:{sibling:N}", sibling);
        var human = await EnqueueAsync(queue, session, "human: also check the logs", QueuedMessageOrigin.Ui);

        await factory.ServiceProvider.GetRequiredService<AgentTaskService>().CancelAsync(task.Id, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Canceled);
        (await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == nudgeId))
            .Status.ShouldBe(QueuedMessageStatus.Canceled, "a cancelled task's closing-line ask is never typed");
        (await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == waitId))
            .Status.ShouldBe(QueuedMessageStatus.Canceled, "the parent must not hear a wait for a cancelled task");
        foreach (var id in new[] { siblingWait, human })
        {
            var row = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
            row.Status.ShouldBe(QueuedMessageStatus.Pending, $"unrelated row '{row.Body}' must survive");
        }
    }

    private static TestScopeFactory NewFactory() =>
        new(configureServices: s => s.AddSingleton<RepositoryLeaseWaiters>());

    private static async Task<AgentTaskStatus> ReadStatusAsync(Guid taskId)
    {
        await using var db = CreateContext();
        return (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId)).Status;
    }

    private static async Task<Guid> EnqueueAsync(
        SessionMessageQueueService queue, Guid session, string body, QueuedMessageOrigin origin,
        string? conversationKey = null, Guid? sourceTaskId = null)
    {
        Guid? created = null;
        await queue.EnqueueAsync(session, body, MessageSendMode.WhenIdle, CancellationToken.None, origin,
            conversationKey, sourceTaskId, onCreated: id => created = id, deliverIfIdle: false);
        return created.ShouldNotBeNull();
    }
}
