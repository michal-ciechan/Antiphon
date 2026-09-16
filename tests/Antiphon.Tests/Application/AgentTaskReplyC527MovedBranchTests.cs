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
    [Arguments("branch")]
    [Arguments("reflog")]
    [Arguments("unborn")]
    public async Task C527_branch_movement_after_failed_save_recovers_original_parent_receipt(string checkout)
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "b.md"), "b");
        const string report = "Wrote `a.md` and `b.md`.";
        var spy = new RecordingGitWorkspaceService();
        var initial = C527Factory(repo.WorktreeRoot, gitSpy: spy, saveInterceptor: new ThrowOnceSaveInterceptor());
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        await CreateService(initial).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        var committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        committed.ShouldNotBe(baseline);
        await repo.GitAsync("switch", "-c", "other", baseline);
        if (checkout == "reflog") await repo.GitAsync("branch", "-D", "master");
        if (checkout == "unborn") await repo.GitAsync("symbolic-ref", "HEAD", "refs/heads/unborn");
        if (checkout == "reflog")
            (await repo.GitReadAsync("log", "--all", "--format=%H")).ShouldNotContain(committed);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign staged");
        await repo.GitAsync("add", "foreign.md");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign work");
        var branch = await repo.GitReadAsync("symbolic-ref", "HEAD");
        var index = await repo.GitReadAsync("write-tree");
        spy.OverrideRun = args => args.Contains("--all") ? (128, "", "history unavailable") : null;
        var unavailable = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        var pendingTerminal = AttachTerminal(unavailable, seeded.Parent);
        await CreateService(unavailable).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(unavailable).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        await using (var db = CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.Task.Id && e.Type == AgentTaskEventType.Committed)).ShouldBeFalse();
            (await db.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == seeded.Task.Id)).ShouldBeFalse();
        }
        pendingTerminal.SubmittedBodies.ShouldBeEmpty();
        spy.OverrideRun = null;
        var recovery = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        var terminal = AttachTerminal(recovery, seeded.Parent);
        await CreateService(recovery).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(recovery).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        await AssertC527RecoveredReceiptAsync(seeded.Task, seeded.Parent, report, committed, 2);
        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Prompt.Text.ShouldContain("foreign.md");
        receipt.Prompt.Text.ShouldNotContain("git=landed");
        await CreateService(recovery).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(recovery).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        terminal.SubmittedBodies.Count.ShouldBe(1);
        (await repo.GitReadAsync("symbolic-ref", "HEAD")).ShouldBe(branch);
        (await repo.GitReadAsync("write-tree")).ShouldBe(index);
        (await repo.GitReadAsync("show", ":foreign.md")).ShouldBe("foreign staged");
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, "foreign.md"))).ShouldBe("foreign work");
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
        spy.Verbs.ShouldNotContain("push");
    }
}
