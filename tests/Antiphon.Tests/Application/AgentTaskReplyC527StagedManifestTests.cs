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
    [Arguments(false)]
    [Arguments(true)]
    public async Task C527_selected_revert_delivers_exact_parent_receipt(bool recover)
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await repo.CommitFileAsync("b.md", "base");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "b.md"), "staged change");
        await repo.GitAsync("add", "b.md");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign staged");
        await repo.GitAsync("add", "foreign.md");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign work");
        const string report = "Wrote `a.md` and reverted `b.md`.";
        var spy = new RecordingGitWorkspaceService();
        spy.BeforeRun = async args =>
        {
            if (args[0] == "add") await File.WriteAllTextAsync(Path.Combine(repo.Path, "b.md"), "base");
        };
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy,
            saveInterceptor: recover ? new ThrowOnceSaveInterceptor() : null);
        var terminal = AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        var committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        if (recover)
        {
            await using var db = CreateContext();
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
            (await db.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == seeded.Task.Id)).ShouldBeFalse();
            terminal.SubmittedBodies.ShouldBeEmpty();
            factory = C527Factory(repo.WorktreeRoot, gitSpy: spy);
            terminal = AttachTerminal(factory, seeded.Parent);
            await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        }
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        await AssertC527RecoveredReceiptAsync(seeded.Task, seeded.Parent, report, committed, 1);
        await using (var db = CreateContext())
        {
            var commitEvent = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == seeded.Task.Id
                && e.Type == AgentTaskEventType.Committed);
            commitEvent.Detail.ShouldNotContain("b.md");
            commitEvent.Detail.ShouldNotContain("foreign.md");
        }
        (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", committed)).Trim().ShouldBe("a.md");
        (await repo.GitReadAsync("show", "HEAD:b.md")).ShouldBe("base");
        (await repo.GitReadAsync("show", ":foreign.md")).ShouldBe("foreign staged");
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, "foreign.md"))).ShouldBe("foreign work");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        terminal.SubmittedBodies.Count.ShouldBe(1);
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(committed);
    }
}
