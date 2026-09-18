using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0527 S2 (D-2): a settlement with no durable obligation and no found settlement commit has
/// nothing to recover, so a failed git inspection degrades to an honest note and settles ONCE
/// instead of throwing settlement_recovery_unavailable and re-handing the boundary forever.
/// </summary>
public partial class AgentTaskReplyIntegrationTests
{
    // A-6: the production symptom. A failing history search with no obligation settles first time.
    [Test]
    public async Task C527_obligation_free_history_search_failure_settles_once_with_an_uncommitted_note()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "b.md"), "b");
        await SeedFileEditAsync(seeded.SessionId, "Write", Path.Combine(repo.Path, "a.md"), DateTime.UtcNow);
        await SeedFileEditAsync(seeded.SessionId, "Write", Path.Combine(repo.Path, "b.md"), DateTime.UtcNow);
        const string report = "Wrote `a.md` and `b.md`.";
        var spy = new RecordingGitWorkspaceService();
        // The exact tuple RunCoreAsync returns when a git command exceeds its budget.
        spy.OverrideRun = args => args.Contains("--all") ? (-1, "", "timeout") : null;
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        var terminal = AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var index = await repo.GitReadAsync("write-tree");
        var branch = await repo.GitReadAsync("symbolic-ref", "HEAD");

        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);

        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Prompt.Text.ShouldContain("git=uncommitted:2 (history search unavailable)");
        receipt.Prompt.Text.ShouldNotContain("git=committed:");
        receipt.Prompt.Text.ShouldNotContain("commit task ");
        await using (var db = CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
            (await db.AgentTasks.AnyAsync(t => t.ParentTaskId == seeded.Task.Id)).ShouldBeFalse();
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.CommitRecoveryStarted)).ShouldBeFalse();
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.Committed)).ShouldBeFalse();
            var warning = (await db.AgentTaskEvents.Where(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.Warning).ToListAsync())
                .ShouldHaveSingleItem();
            warning.Detail.ShouldStartWith("Report names 2 file(s) still uncommitted in the shared checkout:");
            warning.Detail.ShouldContain("a.md");
            warning.Detail.ShouldContain("b.md");
            warning.Detail.ShouldContain("Commit-on-settle skipped: history search unavailable (timeout).");
        }
        spy.Verbs.ShouldNotContain("commit");
        spy.Verbs.ShouldNotContain("push");
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
        (await repo.GitReadAsync("write-tree")).ShouldBe(index);
        (await repo.GitReadAsync("symbolic-ref", "HEAD")).ShouldBe(branch);

        // The whole point: the boundary is spent, not re-handed once a sweep forever.
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        terminal.SubmittedBodies.Count.ShouldBe(1);
    }

    // A-7: a clean tree has nothing to warn about; the hook stands aside for the fallback arm.
    [Test]
    public async Task C527_obligation_free_history_search_failure_on_a_clean_tree_still_reports_landed()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await repo.CommitFileAsync("a.md", "a");
        const string report = "Wrote `a.md`.";
        var spy = new RecordingGitWorkspaceService();
        spy.OverrideRun = args => args.Contains("--all") ? (-1, "", "timeout") : null;
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);

        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);

        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Prompt.Text.ShouldContain("git=landed");
        receipt.Prompt.Text.ShouldNotContain("unavailable");
        await using var db = CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.Task.Id
            && e.Type == AgentTaskEventType.Warning)).ShouldBeFalse();
    }

    // A-8: a failed repository inspection degrades; Git's explicit negative still stands aside.
    [Test]
    [Arguments("unavailable")]
    [Arguments("negative")]
    public async Task C527_obligation_free_repository_inspection_failure_settles_once(string inspection)
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        await SeedFileEditAsync(seeded.SessionId, "Write", Path.Combine(repo.Path, "a.md"), DateTime.UtcNow);
        const string report = "Wrote `a.md`.";
        var spy = new RecordingGitWorkspaceService();
        spy.OverrideRun = args => args.Contains("--is-inside-work-tree")
            ? (128, "", inspection == "negative"
                ? "fatal: not a git repository (or any of the parent directories): .git"
                : "retry prerequisite unavailable")
            : null;
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        var terminal = AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);

        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);

        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        if (inspection == "unavailable")
            receipt.Prompt.Text.ShouldContain("git=uncommitted:1 (repository inspection unavailable)");
        else
            receipt.Prompt.Text.ShouldContain("git=uncommitted:1");
        receipt.Prompt.Text.ShouldNotContain("git=committed:");
        await using (var db = CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
            (await db.AgentTasks.AnyAsync(t => t.ParentTaskId == seeded.Task.Id)).ShouldBeFalse();
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.CommitRecoveryStarted)).ShouldBeFalse();
        }
        spy.Verbs.ShouldNotContain("commit");
        terminal.SubmittedBodies.Count.ShouldBe(1);
    }

    // A-9: search AND status both unavailable — no commit is attempted and the note says so.
    [Test]
    public async Task C527_obligation_free_search_and_status_failure_refuses_the_commit_once()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        await SeedFileEditAsync(seeded.SessionId, "Write", Path.Combine(repo.Path, "a.md"), DateTime.UtcNow);
        const string report = "Wrote `a.md`.";
        var spy = new RecordingGitWorkspaceService();
        spy.OverrideRun = args => args.Contains("--all") || args[0] == "status"
            ? (-1, "", "timeout") : null;
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        var terminal = AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);

        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);

        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Prompt.Text.ShouldContain("git=commit refused: status inspection unavailable");
        await using (var db = CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.CommitRecoveryStarted)).ShouldBeFalse();
        }
        spy.Verbs.ShouldNotContain("commit");
        terminal.SubmittedBodies.Count.ShouldBe(1);
    }

    // A-11: a RESOLVED no-commit obligation is itself the durable answer; a failed search on the
    // re-hand falls through to the ordinary no-op note exactly as a successful empty search does.
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C527_resolved_no_commit_obligation_survives_a_failed_search_on_the_rehand(bool foreign)
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await repo.CommitFileAsync("a.md", "base");
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "obsolete staged change");
        await repo.GitAsync("add", "a.md");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "base");
        if (foreign)
            await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign work");
        const string report = "Reverted `a.md`.";
        var spy = new RecordingGitWorkspaceService();
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy, saveInterceptor: new ThrowOnceSaveInterceptor());
        var terminal = AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        terminal.SubmittedBodies.ShouldBeEmpty();
        await using (var seedCheck = CreateContext())
        {
            var started = await seedCheck.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.CommitRecoveryStarted);
            (await seedCheck.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.CommitRecoveryNotNeeded))
                .Detail.ShouldBe($"{started.Id:D} NothingToCommit");
        }

        // Re-hand with the history search unavailable: the obligation is resolved, so no hold.
        spy.OverrideRun = args => args.Contains("--all") ? (-1, "", "timeout") : null;
        var rehand = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        var rehandTerminal = AttachTerminal(rehand, seeded.Parent);
        await CreateService(rehand).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(rehand).FlushSessionAsync(seeded.Parent, CancellationToken.None);

        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Prompt.Text.ShouldContain(foreign ? "git=uncommitted:1 (selected changes reverted)" : "git=landed");
        receipt.Prompt.Text.ShouldNotContain("history search unavailable");
        await using (var db = CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.CommitRecoveryStarted)).ShouldBe(1);
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.Committed)).ShouldBeFalse();
        }
        spy.Verbs.Count(v => v == "commit").ShouldBe(0);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(baseline);
        rehandTerminal.SubmittedBodies.Count.ShouldBe(1);
    }

    // A-13b: the same pending receipt seen through a real Worktree settle.
    [Test]
    public async Task C527_a_worktree_settle_with_a_pending_gated_receipt_still_reports_merged()
    {
        using var repo = new ScratchGitRepo("c527-receipt-settle");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");
        var spy = new RecordingGitWorkspaceService();
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        var parentSessionId = await SeedSessionAsync(repo.Path);
        var (task, sessionId) = await SeedDispatchedTaskAsync(repo.Path, parentSessionId, t =>
        {
            t.Workspace = WorkspaceMode.Worktree;
            t.RepoPath = repo.Path;
            t.MergeTargetRef = "feat/parent";
        });
        await CreateWorktreeForAsync(factory, task);
        await File.WriteAllTextAsync(Path.Combine(TaskWorktreePath(task)!, "feature.md"), "the work\n");
        spy.OverrideRun = args => spy.Verbs.Contains("commit") && args.Contains("--all")
            ? (-1, "", "timeout") : null;

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Wrote feature.md.");
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        (await repo.GitReadAsync("show", "feat/parent:feature.md")).ShouldBe("the work\n");
        var merged = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Merged);
        merged.Detail.ShouldNotBeNull();
        merged.Detail.ShouldContain("receipt pending");
        (await verify.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == task.Id
            && e.Type == AgentTaskEventType.Failed)).ShouldBeFalse();
        var note = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == parentSessionId);
        note.Body.ShouldContain("merged → feat/parent");
    }
}
