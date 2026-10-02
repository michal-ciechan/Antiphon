using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskReplyIntegrationTests
{
    [Test]
    [Arguments("no-change")]
    [Arguments("committed")]
    [Arguments("no-change-future-dispatch")]
    public async Task C788_NoChangeFutureDispatchCleansUp(string variant)
    {
        using var repo = new ScratchGitRepo("antiphon-c788-clock-skew");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var factory = new TestScopeFactory(repo.WorktreeRoot,
            configureServices: services => services.AddSingleton<TimeProvider>(clock));
        var parentSessionId = await SeedSessionAsync(repo.Path);
        var (task, sessionId) = await SeedDispatchedTaskAsync(repo.Path, parentSessionId, t =>
        {
            t.Workspace = WorkspaceMode.Worktree;
            t.RepoPath = repo.Path;
            t.MergeTargetRef = "feat/parent";
            t.DispatchedAt = clock.GetUtcNow().UtcDateTime.AddMinutes(
                variant == "no-change-future-dispatch" ? 1 : -1);
        });
        await CreateWorktreeForAsync(factory, task);
        var worktree = TaskWorktreePath(task)!;
        if (variant == "committed")
        {
            await File.WriteAllTextAsync(Path.Combine(worktree, "feature.md"), "feature\n");
            (await ScratchGitRepo.GitInAsync(worktree, "add", "feature.md")).Ok.ShouldBeTrue();
            (await ScratchGitRepo.GitInAsync(worktree, "commit", "-m", "feature")).Ok.ShouldBeTrue();
        }

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.");
        await CreateService(factory, timeProvider: clock).OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        var events = await verify.AgentTaskEvents.Where(e => e.AgentTaskId == task.Id)
            .Select(e => e.Detail).ToListAsync();
        events.ShouldContain(detail => detail != null && detail.Contains(
            variant == "committed" ? "Merged: " : "No changes beyond target", StringComparison.Ordinal));
        if (variant == "committed")
            (await repo.GitReadAsync("show", "feat/parent:feature.md")).ShouldContain("feature");
        else
            Directory.Exists(worktree).ShouldBeFalse($"{variant} should clean up its worktree");
    }
}
