using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CompletionWarningDeliveryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C788_ProgressWarningReceipt(bool busy)
    {
        foreach (var distilledSpill in new[] { false, true })
        {
            var row = $"busy={busy} distilledSpill={distilledSpill}";
            await using var rig = await C544DeliveryRig.CreateAsync(busy, spill: distilledSpill,
                distill: distilledSpill);
            var taskId = await SettlePlanAsync(rig);
            var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull(row);
            TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson)!.NoteHeader
                .ShouldContain("progress=none; reason=no_movement");
            rig.Caller.SubmittedBodies.ShouldBeEmpty(row);
            if (distilledSpill)
            {
                await rig.FlushAsync();
                rig.DistillQueue.TryDequeue(out var request).ShouldBeTrue(row);
                (await rig.Queue.TryApplyDistillationAsync(request, note.ContentDigest,
                    C544DeliveryRig.Summary, CancellationToken.None)).ShouldBeNull(row);
            }
            await rig.DeliverAsync(row);
            await rig.ScanAsync();
            await VerificationRoundDeliveryTests.AssertReceivedOnceAsync(rig, taskId, row,
                ["progress=none; reason=no_movement", "refs/heads/feat/card-task-"],
                expectKind: distilledSpill ? "distilled" : "raw", expectSpill: distilledSpill);
        }
    }

    [Test]
    [Arguments("obligation-insert")]
    [Arguments("settled-committed")]
    [Arguments("note-insert")]
    [Arguments("note-committed")]
    [Arguments("wakeup-dropped")]
    [Arguments("render-committed")]
    [Arguments("spill-written")]
    [Arguments("attempt-committed")]
    [Arguments("prompt-accepted")]
    public async Task C788_ProgressWarningRecovery(string cut)
    {
        foreach (var busy in new[] { false, true })
        foreach (var distilledSpill in new[] { false, true })
        {
            var row = $"{cut} busy={busy} distilledSpill={distilledSpill}";
            await using var rig = await C544DeliveryRig.CreateAsync(busy, spill: distilledSpill,
                distill: distilledSpill);
            if (cut == "wakeup-dropped") rig.Boundary.DropCompletionWakeup = true;
            else if (cut is not ("render-committed" or "spill-written" or "attempt-committed" or "prompt-accepted"))
                rig.Fault.Cut = cut;
            var taskId = await SettlePlanAsync(rig, expectRollback: cut == "obligation-insert");
            if (cut == "obligation-insert")
            {
                rig.Fault.Throws.ShouldBe(1, row + ": cut reached");
                (await rig.NotificationAsync(taskId)).ShouldBeNull(row);
                await rig.RestartAsync();
                var session = (await rig.World.TaskAsync(taskId)).AgentSessionId!.Value;
                await rig.World.Services.GetRequiredService<AgentTaskReplyService>()
                    .OnTurnEndAsync(session, CancellationToken.None);
            }
            var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull(row);
            var header = TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson)!.NoteHeader;
            header.ShouldContain("progress=none; reason=no_movement");
            if (distilledSpill && cut is not ("settled-committed" or "note-insert"))
            {
                if (rig.DistillQueue.TryDequeue(out var request))
                    (await rig.Queue.TryApplyDistillationAsync(request, note.ContentDigest,
                        C544DeliveryRig.Summary, CancellationToken.None)).ShouldBeNull(row);
            }
            if (cut is "render-committed" or "spill-written" or "attempt-committed" or "prompt-accepted")
            {
                rig.Fault.TaskId = taskId;
                rig.Fault.Cut = cut;
                try
                {
                    if (busy) await rig.EndCallerTurnAsync();
                    else await rig.FlushAsync();
                    if (cut == "prompt-accepted") await rig.ScanAsync();
                }
                catch (IOException e) when (e.Message.StartsWith("c544 completion persistence cut", StringComparison.Ordinal)) { }
                rig.Fault.Throws.ShouldBe(1, row + ": cut reached");
            }
            await rig.RestartAsync();
            rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
            await rig.ScanAsync();
            if (rig.Busy) await rig.EndCallerTurnAsync();
            await rig.FlushAsync();
            await rig.ScanAsync();
            await VerificationRoundDeliveryTests.AssertReceivedOnceAsync(rig, taskId, row,
                ["progress=none; reason=no_movement", "refs/heads/feat/card-task-"],
                expectKind: null, expectSpill: distilledSpill ? null : false);
            (await rig.NotificationAsync(taskId))!.Id.ShouldBe(note.Id);
        }
    }

    private static async Task<Guid> SettlePlanAsync(C544DeliveryRig rig, bool expectRollback = false)
    {
        var request = new CreateAgentTaskRequest("Write a plan.", Title: "CARD-0788 local plan",
            Role: AgentTaskRole.Plan, Workspace: WorkspaceMode.Worktree,
            WorkingDirectory: rig.World.RepositoryPath, Card: rig.World.Card.Id.ToString("D"));
        var (taskId, _) = await rig.SettleReviewAsync(request,
            transformReport: (id, _) => "Plan complete.\n\n--- next stage ---\nnext: code\nhandoff: Implement the plan.\n"
                + DelegationReportFormatter.ReportToken(id, "done"),
            beforeSettlement: async (world, id) =>
            {
                var branch = $"feat/card-task-{id:N}";
                var path = Path.Combine(world.Repo.WorktreeRoot, id.ToString("N"));
                await world.Repo.GitAsync("worktree", "add", "-b", branch, path, "master");
                await using var db = world.CreateContext();
                var task = await db.AgentTasks.SingleAsync(t => t.Id == id);
                task.RepoPath = world.RepositoryPath;
                task.WorktreePath = path;
                task.WorktreeBranch = branch;
                task.WorktreeBaseSha = world.BaseSha;
                // The worktree is provisioned by this test after manual dispatch. Its files are
                // the baseline, so place the progress cutoff after that provisioning step.
                task.DispatchedAt = DateTime.UtcNow.AddMinutes(1);
                task.VerificationProfileVersion = 1;
                task.VerificationRound = VerificationRound.Final;
                task.ReplyTo = AgentTaskReplyTo.Session;
                task.ParentSessionId = world.CallerSessionId;
                await db.SaveChangesAsync();
            });
        var settled = await rig.World.TaskAsync(taskId);
        if (expectRollback)
        {
            settled.Status.ShouldBe(AgentTaskStatus.Dispatched, "obligation insertion rolls back settlement");
            return taskId;
        }
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded,
            $"completion producer status={settled.Status} failure={settled.FailureCode}: {settled.FailureReason}");
        var progress = TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson);
        progress.ShouldNotBeNull("real local progress evaluation ran");
        progress.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress,
            $"local assessment={progress.Assessment} reason={progress.Reason}");
        return taskId;
    }
}
