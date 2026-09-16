using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskReplyIntegrationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task C527_equals_separator_delivers_exact_parent_receipt(bool recover, bool busy)
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await repo.GitAsync("config", "trailer.separators", "=");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        const string report = "Wrote `a.md`.";
        var spy = new RecordingGitWorkspaceService();
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy,
            saveInterceptor: recover ? new ThrowOnceSaveInterceptor() : null);
        var terminal = AttachTerminal(factory, seeded.Parent);
        if (busy) await SeedEntryAsync(seeded.Parent, TranscriptKinds.AssistantText, "still working", DateTime.UtcNow);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        var committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        (await repo.GitReadAsync("log", "-1", "--format=%B")).ShouldContain($"antiphon-task= {seeded.Task.Id:D}");
        if (recover)
        {
            await AssertC527RecoveryPendingAsync(seeded.Task.Id);
            terminal.SubmittedBodies.ShouldBeEmpty();
            factory = C527Factory(repo.WorktreeRoot, gitSpy: spy);
            terminal = AttachTerminal(factory, seeded.Parent);
            await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        }
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        if (busy)
        {
            terminal.SubmittedBodies.ShouldBeEmpty();
            await SeedEntryAsync(seeded.Parent, TranscriptKinds.TurnEnd, null, DateTime.UtcNow);
            await Queue(factory).OnTurnEndAsync(seeded.Parent, CancellationToken.None);
        }
        await AssertC527RecoveredReceiptAsync(seeded.Task, seeded.Parent, report, committed, 1);
        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Prompt.Text.ShouldNotContain("git=landed");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        terminal.SubmittedBodies.Count.ShouldBe(1);
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
        spy.Verbs.ShouldNotContain("push");
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(committed);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task C527_successful_empty_history_preserves_recovery_until_parent_receipt(bool busy, bool foreign)
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        const string report = "Wrote `a.md`.";
        var spy = new RecordingGitWorkspaceService();
        var initial = C527Factory(repo.WorktreeRoot, gitSpy: spy, saveInterceptor: new ThrowOnceSaveInterceptor());
        if (busy) await SeedEntryAsync(seeded.Parent, TranscriptKinds.AssistantText, "still working", DateTime.UtcNow);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        await CreateService(initial).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        var committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        committed.ShouldNotBe(baseline);
        await repo.GitAsync("reset", "--hard", baseline);
        await repo.GitAsync("reflog", "expire", "--expire=now", "--all");
        await repo.GitAsync("cat-file", "-e", committed);
        if (foreign)
        {
            await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign staged");
            await repo.GitAsync("add", "foreign.md");
            await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign work");
        }
        var index = await repo.GitReadAsync("write-tree");
        await using (var db = CreateContext())
        {
            var obligation = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.CommitRecoveryStarted);
            var found = await spy.FindSettlementCommitsAsync(repo.Path, seeded.Task.Id, obligation.Detail, CancellationToken.None);
            found.Succeeded.ShouldBeTrue();
            found.Items.ShouldBeEmpty();
        }
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var pending = C527Factory(repo.WorktreeRoot, gitSpy: spy);
            var terminal = AttachTerminal(pending, seeded.Parent);
            await CreateService(pending).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
            await Queue(pending).FlushSessionAsync(seeded.Parent, CancellationToken.None);
            await AssertC527RecoveryPendingAsync(seeded.Task.Id);
            terminal.SubmittedBodies.ShouldBeEmpty();
            (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(baseline);
            (await repo.GitReadAsync("write-tree")).ShouldBe(index);
            spy.Verbs.Count(v => v == "commit").ShouldBe(1);
        }
        await repo.GitAsync("tag", "recovered-attempt", committed);
        var recovery = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        var recoveredTerminal = AttachTerminal(recovery, seeded.Parent);
        await CreateService(recovery).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(recovery).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        if (busy)
        {
            recoveredTerminal.SubmittedBodies.ShouldBeEmpty();
            await SeedEntryAsync(seeded.Parent, TranscriptKinds.TurnEnd, null, DateTime.UtcNow);
            await Queue(recovery).OnTurnEndAsync(seeded.Parent, CancellationToken.None);
        }
        await AssertC527RecoveredReceiptAsync(seeded.Task, seeded.Parent, report, committed, 1);
        await CreateService(recovery).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(recovery).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        recoveredTerminal.SubmittedBodies.Count.ShouldBe(1);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(baseline);
        (await repo.GitReadAsync("write-tree")).ShouldBe(index);
        if (foreign)
        {
            (await repo.GitReadAsync("show", ":foreign.md")).ShouldBe("foreign staged");
            (await File.ReadAllTextAsync(Path.Combine(repo.Path, "foreign.md"))).ShouldBe("foreign work");
        }
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
        spy.Verbs.ShouldNotContain("push");
    }

    private static async Task AssertC527RecoveryPendingAsync(Guid taskId)
    {
        await using var db = CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == taskId)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await db.AgentTasks.AnyAsync(t => t.ParentTaskId == taskId)).ShouldBeFalse();
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
            && e.Type == AgentTaskEventType.CommitRecoveryStarted)).ShouldBe(1);
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Committed)).ShouldBeFalse();
        (await db.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == taskId)).ShouldBeFalse();
    }
}
