using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1706: the reply-merge path wrote every conflicted file into its Conflicted event, so a
/// rebase with many long conflicting names filled AgentTaskEvents.Detail (varchar 4000) and was
/// cut mid-name. It now uses the CARD-1637 D-1 summary: the first 20 names, then "... and N more".
/// </summary>
public partial class AgentTaskReplyIntegrationTests
{
    [Test]
    public async Task C1706_a_reply_merge_conflict_in_eighty_long_names_writes_a_bounded_conflicted_detail()
    {
        var (repo, factory, _, task, sessionId, names) = await SeedC1706EightyNameConflictAsync(null);
        using var _repo = repo;
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var blocked = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        blocked.Status.ShouldBe(AgentTaskStatus.Blocked, "the settlement completed instead of sticking Running");
        blocked.FailureReason.ShouldContain("conflicted in 80 file(s)");

        var conflicted = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Conflicted);
        conflicted.Detail.Length.ShouldBeLessThanOrEqualTo(3600, conflicted.Detail);
        conflicted.Detail.ShouldBe("Conflicts: " + string.Join(", ", names.Take(20)) + ", ... and 60 more");

        var merge = await verify.AgentTasks.SingleAsync(t => t.ParentTaskId == task.Id);
        merge.Role.ShouldBe(AgentTaskRole.Merge);
        merge.Goal.ShouldContain(names[79], customMessage: "the resolver still gets the whole list");
    }

    /// <summary>
    /// The cap-hit arm: the task already sits at MaxDepth, so CreateMergeTaskAsync returns null and
    /// the settlement header names the conflict for a human. That header uses the same bounded
    /// summary as the Conflicted event, never the whole eighty-name list.
    /// </summary>
    [Test]
    public async Task C1706_a_reply_merge_conflict_at_the_task_cap_writes_a_bounded_cap_header()
    {
        var maxDepth = new DelegationSettings().MaxDepth;
        var (repo, factory, parentSessionId, task, sessionId, names) =
            await SeedC1706EightyNameConflictAsync(t => t.Depth = maxDepth);
        using var _repo = repo;
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var blocked = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        blocked.Status.ShouldBe(AgentTaskStatus.Blocked);
        blocked.FailureReason.ShouldContain("conflicted in 80 file(s)");
        (await verify.AgentTasks.AnyAsync(t => t.ParentTaskId == task.Id))
            .ShouldBeFalse("a task at MaxDepth must not get a Merge child");

        var header = "MERGE CONFLICT in " + string.Join(", ", names.Take(20))
            + ", ... and 60 more — run at task cap, resolve by hand";
        var note = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == parentSessionId);
        note.Body.ShouldContain(header, customMessage: note.Body);
        note.Body.ShouldNotContain(names[20], customMessage: "the cap header lists only the first 20 names");
    }

    private static async Task<(ScratchGitRepo Repo, TestScopeFactory Factory, Guid ParentSessionId, AgentTask Task, Guid SessionId, List<string> Names)>
        SeedC1706EightyNameConflictAsync(Action<AgentTask>? configure)
    {
        var repo = new ScratchGitRepo("antiphon-reply-conflict-many");
        await repo.CommitFileAsync(".gitignore", ".antiphon/\n");
        var names = Enumerable.Range(0, 80)
            .Select(i => $"src/Conflicts/C1706/LongConflictingFileNameForTheReplyMergeWitness_{i:00}.cs")
            .ToList();
        names.ShouldAllBe(n => n.Length >= 70 && n.Length <= 75);
        foreach (var name in names)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo.Path, name))!);
            await File.WriteAllTextAsync(Path.Combine(repo.Path, name), "original\n");
        }
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "eighty files");
        await repo.GitAsync("branch", "feat/parent");
        var factory = new TestScopeFactory(repo.WorktreeRoot);

        var parentSessionId = await SeedSessionAsync(repo.Path);
        var (task, sessionId) = await SeedDispatchedTaskAsync(repo.Path, parentSessionId, t =>
        {
            t.Workspace = WorkspaceMode.Worktree;
            t.RepoPath = repo.Path;
            t.MergeTargetRef = "feat/parent";
            configure?.Invoke(t);
        });
        await CreateWorktreeForAsync(factory, task);
        foreach (var name in names)
            await File.WriteAllTextAsync(Path.Combine(TaskWorktreePath(task)!, name), "delegate version\n");
        await repo.GitAsync("checkout", "feat/parent");
        foreach (var name in names)
            await File.WriteAllTextAsync(Path.Combine(repo.Path, name), "target version\n");
        await repo.GitAsync("commit", "-q", "-am", "target rewrites all eighty");

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Rewrote eighty files.");
        return (repo, factory, parentSessionId, task, sessionId, names);
    }
}
