using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskReplyIntegrationTests
{
    [Test]
    [Arguments("repository")]
    [Arguments("status")]
    public async Task C527_retry_prerequisite_failure_preserves_commit_until_complete_parent_receipt(string inspection)
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "b.md"), "b");
        const string report = "Wrote `a.md` and `b.md`.";
        var spy = new RecordingGitWorkspaceService();
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy, saveInterceptor: new ThrowOnceSaveInterceptor());
        var terminal = AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        var before = int.Parse((await repo.GitReadAsync("rev-list", "--count", "HEAD")).Trim());
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        var committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        (await repo.GitReadAsync("log", "-1", "--format=%B")).ShouldContain("antiphon-settlement:");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "later foreign work");
        var failures = 0;
        var recoveredBeforeFailure = false;
        spy.BeforeRun = args =>
        {
            if (args.Any(a => a.StartsWith("--grep=antiphon-settlement:", StringComparison.Ordinal)))
                recoveredBeforeFailure = true;
            return Task.CompletedTask;
        };
        spy.OverrideRun = args =>
        {
            if (!(inspection == "repository" ? args.Contains("--is-inside-work-tree") : args[0] == "status"))
                return null;
            recoveredBeforeFailure.ShouldBeTrue();
            failures++;
            return (128, "", "retry prerequisite unavailable");
        };
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            recoveredBeforeFailure = false;
            // This fixture returns itself as a scope. Give each attempt the fresh scoped
            // DbContext production uses, so failed-save tracked entities cannot leak across retries.
            var retry = C527Factory(repo.WorktreeRoot, gitSpy: spy);
            var retryTerminal = AttachTerminal(retry, seeded.Parent);
            await CreateService(retry).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
            failures.ShouldBe(attempt);
            await Queue(retry).FlushSessionAsync(seeded.Parent, CancellationToken.None);
            await using var db = CreateContext();
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
            (await db.AgentTasks.AnyAsync(t => t.ParentTaskId == seeded.Task.Id)).ShouldBeFalse();
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.Committed)).ShouldBeFalse();
            (await db.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == seeded.Task.Id)).ShouldBeFalse();
            terminal.SubmittedBodies.ShouldBeEmpty();
            retryTerminal.SubmittedBodies.ShouldBeEmpty();
        }
        spy.OverrideRun = null;
        var recovery = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        var recoveredTerminal = AttachTerminal(recovery, seeded.Parent);
        await CreateService(recovery).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(recovery).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        await AssertC527RecoveredReceiptAsync(seeded.Task, seeded.Parent, report, committed, 2);
        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Prompt.Text.ShouldNotBeNull();
        receipt.Prompt.Text.ShouldContain("foreign.md");
        receipt.Prompt.Text.ShouldNotContain("no commit attempted");
        receipt.Prompt.Text.ShouldNotContain("git=landed");
        // A subsequent healthy sweep neither mutates nor delivers a second completion.
        await CreateService(recovery).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(recovery).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        terminal.SubmittedBodies.ShouldBeEmpty();
        recoveredTerminal.SubmittedBodies.Count.ShouldBe(1);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(committed);
        int.Parse((await repo.GitReadAsync("rev-list", "--count", "HEAD")).Trim()).ShouldBe(before + 1);
        (await repo.GitReadAsync("status", "--porcelain")).ShouldContain("?? foreign.md");
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, "foreign.md"))).ShouldBe("later foreign work");
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
    }

    [Test]
    [Arguments("sha")]
    [Arguments("paths")]
    public async Task C527_post_commit_inspection_failure_defers_settlement_until_complete_receipt(string inspection)
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "b.md"), "b");
        const string report = "Wrote `a.md` and `b.md`.";
        var spy = new RecordingGitWorkspaceService();
        spy.FailPostCommitInspection(inspection);
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        var terminal = AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        var before = int.Parse((await repo.GitReadAsync("rev-list", "--count", "HEAD")).Trim());
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        var committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        (await repo.GitReadAsync("log", "-1", "--format=%B")).ShouldContain("antiphon-settlement:");
        spy.PostCommitInspectionFailures.ShouldBe(1);
        // A second unavailable inspection must keep recovery pending as well.
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        spy.PostCommitInspectionFailures.ShouldBe(2);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        await using (var db = CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
            (await db.AgentTasks.AnyAsync(t => t.ParentTaskId == seeded.Task.Id)).ShouldBeFalse();
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.Committed)).ShouldBeFalse();
            (await db.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == seeded.Task.Id)).ShouldBeFalse();
        }
        terminal.SubmittedBodies.ShouldBeEmpty();
        var recovery = C527Factory(repo.WorktreeRoot);
        var recoveredTerminal = AttachTerminal(recovery, seeded.Parent);
        await CreateService(recovery).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(recovery).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        await AssertC527RecoveredReceiptAsync(seeded.Task, seeded.Parent, report, committed, 2);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(committed);
        int.Parse((await repo.GitReadAsync("rev-list", "--count", "HEAD")).Trim()).ShouldBe(before + 1);
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
        recoveredTerminal.SubmittedBodies.Count.ShouldBe(1);
    }

    [Test]
    [Arguments("project", false)]
    [Arguments("project", true)]
    [Arguments("global", false)]
    [Arguments("global", true)]
    public async Task C527_policy_flip_after_commit_save_failure_recovers_exact_parent_receipt(string policy, bool residual)
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        var projectId = Guid.NewGuid();
        if (policy == "project")
        {
            await using var db = CreateContext();
            db.Projects.Add(new Project { Id = projectId, Name = "c527-" + projectId, CommitOnSettle = true });
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).ProjectId = projectId;
            await db.SaveChangesAsync();
        }
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        const string report = "Wrote `a.md`.";
        var failed = C527Factory(repo.WorktreeRoot, saveInterceptor: new ThrowOnceSaveInterceptor());
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        await CreateService(failed).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        var committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        (await repo.GitReadAsync("log", "-1", "--format=%B")).ShouldContain("antiphon-settlement:");
        await using (var db = CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
            (await db.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == seeded.Task.Id)).ShouldBeFalse();
            if (policy == "project")
            {
                (await db.Projects.SingleAsync(p => p.Id == projectId)).CommitOnSettle = false;
                await db.SaveChangesAsync();
            }
        }
        if (residual) await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "later edit");
        var settings = new DelegationSettings { CommitOnSettle = policy != "global", PtySingleChunkBytes = 43_200 };
        var recovery = C527Factory(repo.WorktreeRoot, delegation: settings);
        var terminal = AttachTerminal(recovery, seeded.Parent);
        await CreateService(recovery, settings).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(recovery).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        await AssertC527RecoveredReceiptAsync(seeded.Task, seeded.Parent, report, committed, 1);
        if (residual)
        {
            var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
            receipt.Prompt.Text.ShouldContain("1 dirty path(s) remain after recovered settlement");
            (await File.ReadAllTextAsync(Path.Combine(repo.Path, "a.md"))).ShouldBe("later edit");
        }
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(committed);
        terminal.SubmittedBodies.Count.ShouldBe(1);
    }

    private static async Task AssertC527RecoveredReceiptAsync(AgentTask task, Guid parent, string report, string sha, int files)
    {
        var receipt = await AssertParentReceivedNoteAsync(parent, task, report);
        receipt.Prompt.Text.ShouldContain($"git=committed:{sha[..7]} ({files} files)");
        receipt.Prompt.Text.ShouldNotContain("commit refused");
        receipt.Prompt.Text.ShouldNotContain("commit task ");
        await using var db = CreateContext();
        (await db.AgentTasks.AnyAsync(t => t.ParentTaskId == task.Id)).ShouldBeFalse();
        (await db.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        var committed = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Committed);
        committed.Detail.ShouldContain(sha[..7]);
        committed.Detail.ShouldContain("a.md");
        if (files == 2) committed.Detail.ShouldContain("b.md");
        var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == task.Id);
        receipt.Note.SourceLandNotificationId.ShouldBe(note.Id);
        receipt.Prompt.Text.ShouldBe(note.Body);
    }
}
