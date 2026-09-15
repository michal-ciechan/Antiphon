using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskReplyIntegrationTests
{
    [Test]
    [Arguments("history-and-negative")]
    [Arguments("history-and-error")]
    [Arguments("missing-git-directory")]
    public async Task C527_lost_history_and_repository_preserve_durable_recovery_until_parent_receipt(string failure)
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "b.md"), "b");
        const string report = "Wrote `a.md` and `b.md`.";
        var spy = new RecordingGitWorkspaceService();
        var initial = C527Factory(repo.WorktreeRoot, gitSpy: spy, saveInterceptor: new ThrowOnceSaveInterceptor());
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        var before = int.Parse((await repo.GitReadAsync("rev-list", "--count", "HEAD")).Trim());
        spy.BeforeRun = async args =>
        {
            if (args[0] != "commit") return;
            await using var db = CreateContext();
            var obligation = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.CommitRecoveryStarted);
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id);
            var expectedIdentity = DelegationNoteDigest.Compute(
                $"{task.Id:D}|{task.AgentSessionId:D}|{task.DispatchedAt:O}|{report}");
            obligation.Detail.ShouldBe(expectedIdentity);
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
            (await db.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == seeded.Task.Id)).ShouldBeFalse();
        };
        await CreateService(initial).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        var committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        (await repo.GitReadAsync("log", "-1", "--format=%B")).ShouldContain("antiphon-settlement:");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "later foreign work");
        var historyAttempts = 0;
        var repositoryAttempts = 0;
        spy.BeforeRun = args =>
        {
            if (args.Contains("--verify") && args.Contains("HEAD")) historyAttempts++;
            if (args.Contains("--is-inside-work-tree")) repositoryAttempts++;
            return Task.CompletedTask;
        };
        if (failure != "missing-git-directory")
            spy.OverrideRun = args => args.Contains("--is-inside-work-tree")
                ? (128, "", failure == "history-and-negative"
                    ? "fatal: not a git repository (or any of the parent directories): .git" : "repository unavailable")
                : args.Contains("--verify") && args.Contains("HEAD") ? (128, "", "history unavailable") : null;
        var gitDirectory = Path.Combine(repo.Path, ".git");
        var savedDirectory = Path.Combine(repo.WorktreeRoot, "saved-git");
        if (failure == "missing-git-directory") Directory.Move(gitDirectory, savedDirectory);
        try
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var retry = C527Factory(repo.WorktreeRoot, gitSpy: spy);
                var terminal = AttachTerminal(retry, seeded.Parent);
                await CreateService(retry).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
                await Queue(retry).FlushSessionAsync(seeded.Parent, CancellationToken.None);
                historyAttempts.ShouldBe(attempt);
                repositoryAttempts.ShouldBe(attempt);
                await using var db = CreateContext();
                (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
                (await db.AgentTasks.AnyAsync(t => t.ParentTaskId == seeded.Task.Id)).ShouldBeFalse();
                (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.Task.Id
                    && e.Type == AgentTaskEventType.CommitRecoveryStarted)).ShouldBe(1);
                (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.Task.Id
                    && e.Type == AgentTaskEventType.Committed)).ShouldBeFalse();
                (await db.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == seeded.Task.Id)).ShouldBeFalse();
                terminal.SubmittedBodies.ShouldBeEmpty();
            }
        }
        finally
        {
            if (failure == "missing-git-directory") Directory.Move(savedDirectory, gitDirectory);
            spy.OverrideRun = null;
            spy.BeforeRun = null;
        }
        var recovery = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        var recoveredTerminal = AttachTerminal(recovery, seeded.Parent);
        await CreateService(recovery).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(recovery).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        await AssertC527RecoveredReceiptAsync(seeded.Task, seeded.Parent, report, committed, 2);
        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Prompt.Text.ShouldContain("foreign.md");
        await CreateService(recovery).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(recovery).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        recoveredTerminal.SubmittedBodies.Count.ShouldBe(1);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(committed);
        int.Parse((await repo.GitReadAsync("rev-list", "--count", "HEAD")).Trim()).ShouldBe(before + 1);
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, "foreign.md"))).ShouldBe("later foreign work");
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
    }

    [Test]
    public async Task C527_recovery_obligation_save_failure_prevents_git_mutation()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        var index = await File.ReadAllBytesAsync(Path.Combine(repo.Path, ".git", "index"));
        var spy = new RecordingGitWorkspaceService();
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy, saveInterceptor: new RefuseRecoveryObligation());
        var terminal = AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote `a.md`.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        spy.Verbs.ShouldNotContain("add");
        spy.Verbs.ShouldNotContain("commit");
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        (await File.ReadAllBytesAsync(Path.Combine(repo.Path, ".git", "index"))).ShouldBe(index);
        await using var db = CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await db.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == seeded.Task.Id)).ShouldBeFalse();
        terminal.SubmittedBodies.ShouldBeEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C527_literal_footprint_reaches_complete_parent_receipt_without_matching_neighbor(bool recover)
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        const string selected = "item[ab].md";
        const string neighbor = "itema.md";
        await repo.CommitFileAsync(selected, "selected base");
        await repo.CommitFileAsync(neighbor, "neighbor base");
        await repo.CommitFileAsync(".gitignore", "*.md\n!item\\[ab\\].md\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, selected), "selected work");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, neighbor), "neighbor staged");
        await repo.GitAsync("add", "-f", neighbor);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, neighbor), "neighbor work");
        await SeedFileEditAsync(seeded.SessionId, "Write", Path.Combine(repo.Path, selected), DateTime.UtcNow);
        const string report = "Wrote `item[ab].md`.";
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        var factory = C527Factory(repo.WorktreeRoot, saveInterceptor: recover ? new ThrowOnceSaveInterceptor() : null);
        var terminal = AttachTerminal(factory, seeded.Parent);
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        var committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        if (recover)
        {
            terminal.SubmittedBodies.ShouldBeEmpty();
            factory = C527Factory(repo.WorktreeRoot);
            terminal = AttachTerminal(factory, seeded.Parent);
            await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        }
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Prompt.Text.ShouldContain($"git=committed:{committed[..7]} (1 files)");
        await using var db = CreateContext();
        var commitEvent = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == seeded.Task.Id
            && e.Type == AgentTaskEventType.Committed);
        commitEvent.Detail.ShouldContain(selected);
        commitEvent.Detail.ShouldNotContain(neighbor);
        var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == seeded.Task.Id);
        receipt.Prompt.Text.ShouldBe(note.Body);
        receipt.Note.SourceLandNotificationId.ShouldBe(note.Id);
        (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")).Trim().ShouldBe(selected);
        (await repo.GitReadAsync("show", $"HEAD:{neighbor}")).ShouldBe("neighbor base");
        (await repo.GitReadAsync("show", $":{neighbor}")).ShouldBe("neighbor staged");
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, neighbor))).ShouldBe("neighbor work");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        terminal.SubmittedBodies.Count.ShouldBe(1);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(committed);
    }

    private sealed class RefuseRecoveryObligation : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AgentTaskEvent>().Any(e =>
                    e.Entity.Type == AgentTaskEventType.CommitRecoveryStarted))
                throw new InvalidOperationException("Recovery obligation storage unavailable");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
