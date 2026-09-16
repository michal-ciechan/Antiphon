using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskReplyIntegrationTests
{
    [Test]
    [Arguments(false, "immediate")]
    [Arguments(true, "immediate")]
    [Arguments(false, "save")]
    [Arguments(true, "save")]
    [Arguments(false, "status")]
    [Arguments(true, "status")]
    public async Task C527_all_selected_reverted_delivers_no_op_without_Commit_child(bool foreign, string interruption)
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await repo.CommitFileAsync("a.md", "base");
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "obsolete staged change");
        await repo.GitAsync("add", "a.md");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "base");
        if (foreign)
        {
            await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign staged");
            await repo.GitAsync("add", "foreign.md");
            await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign work");
        }
        const string report = "Reverted `a.md`.";
        var spy = new RecordingGitWorkspaceService();
        if (interruption == "status")
            spy.OverrideRun = args => spy.Verbs.Contains("add") && args[0] == "status"
                ? (128, "", "residual status unavailable") : null;
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy,
            saveInterceptor: interruption == "save" ? new ThrowOnceSaveInterceptor() : null);
        var terminal = AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        if (interruption != "immediate")
        {
            await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
            await AssertC527RecoveryPendingAsync(seeded.Task.Id);
            terminal.SubmittedBodies.ShouldBeEmpty();
            spy.OverrideRun = null;
            factory = C527Factory(repo.WorktreeRoot, gitSpy: spy);
            terminal = AttachTerminal(factory, seeded.Parent);
            await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        }
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Prompt.Text.ShouldContain(foreign ? "git=uncommitted:1 (selected changes reverted)" : "git=landed");
        receipt.Prompt.Text.ShouldNotContain("commit task ");
        receipt.Prompt.Text.ShouldNotContain("commit refused");
        receipt.Prompt.Text.ShouldNotContain("git=committed:");
        await using (var db = CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
            (await db.AgentTasks.AnyAsync(t => t.ParentTaskId == seeded.Task.Id)).ShouldBeFalse();
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.Committed)).ShouldBeFalse();
            var started = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.CommitRecoveryStarted);
            var resolved = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.CommitRecoveryNotNeeded);
            resolved.Detail.ShouldBe($"{started.Id:D} NothingToCommit");
            var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == seeded.Task.Id);
            receipt.Prompt.Text.ShouldBe(note.Body);
            receipt.Note.SourceLandNotificationId.ShouldBe(note.Id);
        }
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(baseline);
        (await repo.GitReadAsync("show", "HEAD:a.md")).ShouldBe("base");
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, "a.md"))).ShouldBe("base");
        if (foreign)
        {
            (await repo.GitReadAsync("show", ":foreign.md")).ShouldBe("foreign staged");
            (await File.ReadAllTextAsync(Path.Combine(repo.Path, "foreign.md"))).ShouldBe("foreign work");
            (await repo.GitReadAsync("diff", "--cached", "--name-only")).Trim().ShouldBe("foreign.md");
            receipt.Prompt.Text.ShouldContain("Other dirty paths left as found: foreign.md.");
        }
        else (await repo.GitReadAsync("status", "--porcelain")).ShouldBeEmpty();
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        terminal.SubmittedBodies.Count.ShouldBe(1);
        spy.Verbs.ShouldNotContain("commit");
        spy.Verbs.ShouldNotContain("push");
        spy.Verbs.Count(v => v == "add").ShouldBe(1);
    }
}
