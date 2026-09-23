using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0613 V-8..V-10, R-8. The capstone: real Git, an isolated schema, the real dispatcher's
/// baseline capture and <see cref="AgentTaskReplyService.OnTurnEndAsync"/>. A delegate that
/// continued a sibling's work off its own branch must settle Succeeded — and must still NOT get
/// its branch merged back, because alternate evidence is attribution, not integration authority.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class ContinuationSettlementTests
{
    [Test]
    [Timeout(180_000)]
    [Arguments("off-branch-claim")]
    [Arguments("detached-claim")]
    [Arguments("divergent-own-tip")]
    public async Task C613_ContinuationCommitsSettleSucceeded(string shape)
    {
        await using var world = await NewWorldAsync();
        var (task, ancestor) = await DispatchContinuationAsync(world);
        var captured = CapturedAt(task);

        var continuation = await DivergeAsync(world, task, shape, ancestor, captured.AddMinutes(5));
        // A CLEAN worktree: the dirty-file rescue cannot mask a defect in graph attribution.
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "status", "--porcelain")).StdOut.Trim().ShouldBeEmpty();
        Evaluate(world, captured);

        var claim = shape == "divergent-own-tip" ? null : continuation;
        await world.SettleAsync(world.DoneReport("Continued the sibling's work.", claim));

        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.FailureCode.ShouldBeNull();
        settled.FailureReason.ShouldBeNull();
        (await db.AgentIncidents.AnyAsync(i => i.Kind == AgentIncidentKind.DelegateCompletedWithoutProgress))
            .ShouldBeFalse();

        var evidence = TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson).ShouldNotBeNull();
        evidence.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        var positive = evidence.Sources!.Single(s => s.Assessment == CompletionProgressAssessment.ProgressObserved);
        positive.Origin.ShouldBe(ProgressOrigin.PrimaryAlternate);
        positive.VerifiedSha.ShouldBe(continuation);
        positive.RegisteredPath.ShouldBe(task.WorktreePath);
        positive.Reason.ShouldBe(shape == "divergent-own-tip" ? "primary_divergent_commit" : "primary_off_branch_claim");
        positive.ObservedRef.ShouldBe(shape switch
        {
            "detached-claim" => null,
            "off-branch-claim" => "refs/heads/c613-sibling",
            _ => "refs/heads/" + task.WorktreeBranch,
        });
        if (claim is not null) positive.ClaimedSha.ShouldBe(claim);

        // The recorded baseline is evidence, not a mutable field: settlement never rewrites it.
        settled.ProgressBaselineJson.ShouldBe(task.ProgressBaselineJson);
        settled.WorktreeBranch.ShouldBe(task.WorktreeBranch);
        settled.WorktreeBaseSha.ShouldBe(task.WorktreeBaseSha);
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("clean-divergent-own-tip")]
    [Arguments("dirty-divergent-own-tip")]
    [Arguments("off-branch-dirty-files")]
    public async Task C613_AlternateProgressNeverMerges(string shape)
    {
        await using var world = await NewWorldAsync();
        var (task, ancestor) = await DispatchContinuationAsync(world, mergeTarget: "master");
        task.MergeTargetRef.ShouldBe("master");
        var masterBefore = (await world.Repo.GitReadAsync("rev-parse", "refs/heads/master")).Trim();
        var captured = CapturedAt(task);

        string? claim = null;
        if (shape == "off-branch-dirty-files")
        {
            (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "checkout", "-b", "c613-sibling")).Ok.ShouldBeTrue();
            await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "uncommitted.md"), "work in progress\n");
        }
        else
        {
            var sha = await DivergeAsync(world, task, "divergent-own-tip", ancestor, captured.AddMinutes(5));
            claim = null;
            if (shape == "dirty-divergent-own-tip")
                await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "uncommitted.md"), "more\n");
            sha.ShouldNotBe(masterBefore);
        }
        Evaluate(world, captured);

        await world.SettleAsync(world.DoneReport("Continued elsewhere.", claim));

        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);

        // Nothing merged, nothing moved, nothing was taken away.
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Merged))
            .ShouldBeFalse();
        settled.MergeTargetRef.ShouldBe("master");
        settled.WorktreeBranch.ShouldBe(task.WorktreeBranch);
        settled.WorktreePath.ShouldBe(task.WorktreePath);
        Directory.Exists(task.WorktreePath!).ShouldBeTrue();
        (await world.Repo.GitReadAsync("rev-parse", "refs/heads/master")).Trim().ShouldBe(masterBefore);
        world.Git.Trace.Any(a => a.Length > 0 && a[0] is "rebase" or "merge" or "reset" or "push").ShouldBeFalse();
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == task.Id
            && e.Type == AgentTaskEventType.Completed && e.Detail.Contains("left for review"))).ShouldBeTrue();

        // The persisted predicate is what gates merge-back on a later reload, not an evaluation bool.
        var reloaded = TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson).ShouldNotBeNull();
        reloaded.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        reloaded.Sources!.ShouldContain(s => TaskCompletionProgressService.IsAlternateProgress(s));
        reloaded.Sources!.ShouldNotContain(s => s.Origin == ProgressOrigin.Primary
            && s.Assessment == CompletionProgressAssessment.ProgressObserved);
        TaskCompletionProgressService.AllowsAutomaticWorkspaceMutation(reloaded).ShouldBeFalse();

        // A replayed turn-end on a recreated provider settles the same way, once.
        await world.ReplayTurnEndAsync();
        await using var after = world.CreateContext();
        (await after.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Merged))
            .ShouldBe(0);
        (await after.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == task.Id)).ShouldBe(1);
        (await after.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Status
            .ShouldBe(AgentTaskStatus.Succeeded);
    }

    [Test]
    [Timeout(180_000)]
    public async Task C613_CompletionReceipt()
    {
        await using var world = await NewWorldAsync();
        var (task, ancestor) = await DispatchContinuationAsync(world);
        var captured = CapturedAt(task);
        var continuation = await DivergeAsync(world, task, "off-branch-claim", ancestor, captured.AddMinutes(5));
        Evaluate(world, captured);

        await world.SettleAsync(world.DoneReport("Continued the sibling's work.", continuation));

        var note = (await world.Note()).ShouldNotBeNull();
        note.SourceTaskId.ShouldBe(task.Id);
        note.AgentSessionId.ShouldBe(world.CallerSessionId);
        // The caller is told it succeeded, where the work actually is, and that the branch is theirs
        // to land — and is NOT told the delegate produced nothing.
        note.NoteHeader.ShouldNotBeNull().ShouldContain("succeeded");
        note.Body.ShouldContain("progress=primary-alternate");
        note.Body.ShouldContain(continuation);
        note.Body.ShouldContain("left for review");
        note.Body.ShouldNotContain("no attributable post-dispatch progress");

        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        settled.Result.ShouldNotBeNull().ShouldContain(world.ClaimLine(continuation));
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == task.Id
            && e.Type == AgentTaskEventType.Warning && e.Detail.Contains("progress=primary-alternate")))
            .ShouldBeTrue();
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == task.Id)).ShouldBe(1);
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("expected-head")]
    [Arguments("alternate-head")]
    public async Task C613_NoWorkStillFails(string shape)
    {
        await using var world = await NewWorldAsync();
        var (task, ancestor) = await DispatchContinuationAsync(world);
        var captured = CapturedAt(task);
        if (shape == "alternate-head")
        {
            // Off its own branch, at a commit that is nobody's work, with no claim at all.
            (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "checkout", "-b", "c613-sibling", ancestor))
                .Ok.ShouldBeTrue();
        }
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "status", "--porcelain")).StdOut.Trim().ShouldBeEmpty();
        Evaluate(world, captured);

        await world.SettleAsync(world.DoneReport("I read the code and thought about it."));

        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Failed);
        settled.FailureCode.ShouldBe(AgentTaskFailureCode.CompletedWithoutProgress);
        settled.FailureReason.ShouldNotBeNull().ShouldContain("no attributable post-dispatch progress");
        var evidence = TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson).ShouldNotBeNull();
        evidence.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        evidence.Sources!.ShouldNotContain(s => s.Origin == ProgressOrigin.PrimaryAlternate);
        (await db.AgentIncidents.CountAsync(i => i.Kind == AgentIncidentKind.DelegateCompletedWithoutProgress))
            .ShouldBe(1);

        // Replayed: still one incident, still one note, still Failed.
        await world.ReplayTurnEndAsync();
        await using var after = world.CreateContext();
        (await after.AgentIncidents.CountAsync(i => i.Kind == AgentIncidentKind.DelegateCompletedWithoutProgress))
            .ShouldBe(1);
        (await after.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == task.Id)).ShouldBe(1);
    }

    /// <summary>
    /// CARD-0613 V-10. The corrected verdict has to cross the real queue and land in the caller's
    /// terminal as one complete UserPrompt - through every persistence cut on the completion path,
    /// for a busy caller and an eligible one. The delegate's work is off its own branch, so the
    /// note the caller receives is the alternate-attribution one, not a no-progress failure.
    /// </summary>
    [Test]
    [Timeout(600_000)]
    public async Task C613_CompletionDeliveryRecovery()
    {
        var cuts = new[]
        {
            "obligation-insert", "settled-committed", "note-insert", "note-committed",
            "wakeup-dropped", "prompt-accepted",
        };
        foreach (var cut in cuts)
        foreach (var busy in new[] { false, true })
        {
            var row = $"{cut} busy={busy}";
            await using var rig = await C544DeliveryRig.CreateAsync(busy);
            if (cut == "wakeup-dropped") rig.Boundary.DropCompletionWakeup = true;
            else if (cut != "prompt-accepted") rig.Fault.Cut = cut;

            var (taskId, sha) = await SettleContinuationAsync(rig);

            if (cut == "obligation-insert")
            {
                rig.Fault.Throws.ShouldBe(1, row);
                (await rig.NotificationAsync(taskId)).ShouldBeNull(row + ": no obligation without its settlement");
                (await rig.World.TaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Dispatched, row + ": rolled back");
                await rig.RestartAsync();
                var session = (await rig.World.TaskAsync(taskId)).AgentSessionId!.Value;
                await rig.World.Services.GetRequiredService<AgentTaskReplyService>()
                    .OnTurnEndAsync(session, CancellationToken.None);
            }

            var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull(row + ": committed obligation");
            (await rig.World.TaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Succeeded, row);

            if (cut == "prompt-accepted")
            {
                rig.Fault.TaskId = taskId;
                rig.Fault.Cut = cut;
                try
                {
                    if (busy) await rig.EndCallerTurnAsync();
                    else await rig.FlushAsync();
                    await rig.ScanAsync();
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

            await AssertReceivedOnceAsync(rig, taskId, sha, row);
            (await rig.NotificationAsync(taskId))!.Id.ShouldBe(note.Id, row + ": same notification identity");
            (await rig.RowsAsync(taskId)).Count.ShouldBe(1, row + ": one keyed queue row");
        }
    }

    /// <summary>
    /// Exactly one complete UserPrompt in the caller's own transcript, carrying the corrected
    /// succeeded verdict and the alternate attribution - never a no-progress failure sentence.
    /// The receipt is the fake adapter's actual OnSubmitted write; nothing is inserted for it.
    /// </summary>
    private static async Task AssertReceivedOnceAsync(
        C544DeliveryRig rig, Guid taskId, string sha, string row)
    {
        var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull(row);
        var delivery = TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson)
            .ShouldNotBeNull(row + ": rendered wire text");
        var prompts = await rig.CallerPromptsAsync();
        var matching = prompts
            .Where(p => PromptSubmissionMatch.IsCompleteIn(delivery.WireText, p.Text!))
            .ToList();
        matching.Count.ShouldBe(1, row + ": exactly one complete prompt");
        var received = matching[0].Text!;
        received.ShouldContain(DelegationReportFormatter.Short(taskId), customMessage: row);
        received.ShouldContain("succeeded", customMessage: row);
        received.ShouldContain("progress=primary-alternate", customMessage: row);
        received.ShouldContain(sha, customMessage: row);
        received.ShouldContain("left for review", customMessage: row);
        received.ShouldNotContain("no attributable post-dispatch progress", customMessage: row);
        note.State.ShouldBe(LandNotificationState.Confirmed, row);
    }

    /// <summary>
    /// A real profile-v1 Code/Worktree task, provisioned and baselined by the REAL dispatcher tick,
    /// whose delegate then commits on a sibling branch that left the baseline lineage and claims it.
    /// </summary>
    private static async Task<(Guid TaskId, string Sha)> SettleContinuationAsync(C544DeliveryRig rig)
    {
        var created = await rig.World.CreateTaskAsync(new CreateAgentTaskRequest(
            "Continue the sibling's work.", Title: "CARD-0613 continuation", Kind: AgentTaskKind.Worker,
            Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
            WorkingDirectory: rig.World.RepositoryPath, Card: rig.World.Card.Id.ToString()));

        await using (var scope = rig.World.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);

        var task = await rig.World.TaskAsync(created.Id);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson)
            .ShouldNotBeNull("the real dispatcher captures a baseline; a marked-dispatched row does not");

        var path = task.WorktreePath!;
        (await ScratchGitRepo.GitInAsync(path, "checkout", "-b", "c613-sibling", rig.World.BaseSha)).Ok.ShouldBeTrue();
        await File.WriteAllTextAsync(Path.Combine(path, "continuation.md"), "continued work\n");
        (await ScratchGitRepo.GitInAsync(path, "add", "continuation.md")).Ok.ShouldBeTrue();
        var stamp = new DateTimeOffset(baseline.CapturedAt.AddSeconds(5), TimeSpan.Zero).ToString("o");
        var env = new Dictionary<string, string> { ["GIT_AUTHOR_DATE"] = stamp, ["GIT_COMMITTER_DATE"] = stamp };
        (await ScratchGitRepo.GitInAsync(path, env, "commit", "-m", "continued work")).Ok.ShouldBeTrue();
        var sha = (await ScratchGitRepo.GitInAsync(path, "rev-parse", "HEAD")).StdOut.Trim();
        (await ScratchGitRepo.GitInAsync(rig.World.Repo.Path, "merge-base", "--is-ancestor",
            baseline.Primary.LocalSha, sha)).Ok.ShouldBeFalse("the continuation must NOT descend from the baseline");

        // The evaluation clock is the world's; move it past the commit so the D-6 upper bound is met.
        rig.World.Clock.Advance(TimeSpan.FromMinutes(10));

        var session = task.AgentSessionId!.Value;
        var report = $"""
            Continued the sibling's work.

            [antiphon-progress:{created.Id:D} commit={sha}]
            --- next stage ---
            next: review
            handoff: C613 fixture handoff.
            """;
        await rig.World.SeedTurnAsync(session, created.Id, report);
        await rig.World.Services.GetRequiredService<AgentTaskReplyService>()
            .OnTurnEndAsync(session, CancellationToken.None);
        return (created.Id, sha);
    }

    // ---- fixture ---------------------------------------------------------------------------

    private static async Task<RepairSourceWorld> NewWorldAsync() =>
        await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);

    /// <summary>
    /// A real ordinary Code/Worktree task, provisioned and baselined by the real dispatcher. The
    /// returned ancestor is a commit the task's base is a DESCENDANT of, so a commit made on it
    /// genuinely leaves the baseline lineage — a B -> C chain would not reproduce this incident.
    /// </summary>
    private static async Task<(AgentTask Task, string Ancestor)> DispatchContinuationAsync(
        RepairSourceWorld world, string? mergeTarget = null)
    {
        var ancestor = (await world.Repo.GitReadAsync("rev-parse", "refs/heads/master")).Trim();
        await world.Repo.CommitFileAsync("master-moved-on.md", "master advanced\n");
        await world.Repo.GitAsync("push", "origin", "master");
        var baseSha = (await world.Repo.GitReadAsync("rev-parse", "refs/heads/master")).Trim();
        baseSha.ShouldNotBe(ancestor);

        await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "continue the sibling's work", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
            MergeTargetRef: mergeTarget));
        var (task, _) = await world.DispatchAsync();
        task.Status.ShouldBe(AgentTaskStatus.Dispatched);
        task.WorktreePath.ShouldNotBeNull();
        var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson).ShouldNotBeNull();
        baseline.Primary.LocalSha.ShouldBe(baseSha);
        return (task, ancestor);
    }

    private static DateTime CapturedAt(AgentTask task) =>
        TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson)!.CapturedAt;

    /// <summary>
    /// Fix the evaluation instant. Real committer dates are whole seconds and the baseline capture
    /// is a real timestamp, so nothing here may depend on how long the test took to run.
    /// </summary>
    private static void Evaluate(RepairSourceWorld world, DateTime captured) =>
        world.ProgressClock = new FakeTimeProvider(new DateTimeOffset(captured.AddHours(1), TimeSpan.Zero));

    private static async Task<string> DivergeAsync(
        RepairSourceWorld world, AgentTask task, string shape, string ancestor, DateTime committedAt)
    {
        var path = task.WorktreePath!;
        switch (shape)
        {
            case "off-branch-claim":
                (await ScratchGitRepo.GitInAsync(path, "checkout", "-b", "c613-sibling", ancestor)).Ok.ShouldBeTrue();
                break;
            case "detached-claim":
                (await ScratchGitRepo.GitInAsync(path, "checkout", "--detach", ancestor)).Ok.ShouldBeTrue();
                break;
            default:
                // The task branch is RESET into another lineage — the incident this card is about.
                (await ScratchGitRepo.GitInAsync(path, "reset", "--hard", ancestor)).Ok.ShouldBeTrue();
                break;
        }

        await File.WriteAllTextAsync(Path.Combine(path, "continuation.md"), "continued work\n");
        (await ScratchGitRepo.GitInAsync(path, "add", "continuation.md")).Ok.ShouldBeTrue();
        var stamp = new DateTimeOffset(committedAt, TimeSpan.Zero).ToString("o");
        var env = new Dictionary<string, string>
        {
            ["GIT_AUTHOR_DATE"] = stamp,
            ["GIT_COMMITTER_DATE"] = stamp,
        };
        (await ScratchGitRepo.GitInAsync(path, env, "commit", "-m", "continued work")).Ok.ShouldBeTrue();
        var sha = (await ScratchGitRepo.GitInAsync(path, "rev-parse", "HEAD")).StdOut.Trim();

        // Prove the ancestry really left the baseline, rather than trusting the fixture's shape.
        var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson)!;
        (await ScratchGitRepo.GitInAsync(world.Repo.Path, "merge-base", "--is-ancestor",
            baseline.Primary.LocalSha, sha)).Ok.ShouldBeFalse("the continuation must NOT descend from the baseline");
        return sha;
    }
}
