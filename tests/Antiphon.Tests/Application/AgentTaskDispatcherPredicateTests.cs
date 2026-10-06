using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0672 V-2 (D-1): which queued rows launch without the repository mutation lease. Only the
/// second crossing of a runner-bound Worktree task - desktop worktree cut, mirror recorded - and
/// none of the claim paths that run git (repair source, SourceLanding snapshot, Interim recheck).
/// </summary>
[Category("Unit")]
public sealed class AgentTaskDispatcherPredicateTests
{
    [Test]
    [Arguments("prepared", true)]
    [Arguments("local", false)]
    [Arguments("no-worktree", false)]
    [Arguments("no-mirror", false)]
    [Arguments("repair-source", false)]
    [Arguments("snapshot", false)]
    [Arguments("interim", false)]
    [Arguments("shared", false)]
    public void C672_LaunchesPreparedMirror(string arm, bool expected)
    {
        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            Workspace = WorkspaceMode.Worktree,
            RepoPath = Path.Combine(Path.GetTempPath(), "repo"),
            RunnerId = "server2",
            WorktreePath = Path.Combine(Path.GetTempPath(), "repo-wt"),
            RemoteWorktreePath = "/work/worktrees/task-1",
            VerificationRound = VerificationRound.Final,
        };
        switch (arm)
        {
            case "local": task.RunnerId = null; break;
            case "no-worktree": task.WorktreePath = null; break;
            case "no-mirror": task.RemoteWorktreePath = null; break;
            case "repair-source": task.RepairSourceTaskId = Guid.NewGuid(); break;
            case "snapshot": task.SourceLandingOperationId = Guid.NewGuid(); break;
            case "interim": task.VerificationRound = VerificationRound.Interim; break;
            case "shared": task.Workspace = WorkspaceMode.Shared; break;
        }

        AgentTaskDispatcher.LaunchesPreparedMirror(task).ShouldBe(expected);
    }

    /// <summary>
    /// CARD-1076 D-5. A re-arm skips the lease only when the worktree is cut, the mirror is not
    /// recorded, and the baseline is already captured. Every other arm still takes the lease.
    /// </summary>
    [Test]
    [Arguments("rearm", true)]
    [Arguments("mirror-recorded", false)]
    [Arguments("no-baseline", false)]
    [Arguments("local", false)]
    [Arguments("no-worktree", false)]
    [Arguments("repair-source", false)]
    [Arguments("snapshot", false)]
    [Arguments("interim", false)]
    [Arguments("shared", false)]
    public void C1076_RearmsPreparedWorktree(string arm, bool expected)
    {
        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            Workspace = WorkspaceMode.Worktree,
            RepoPath = Path.Combine(Path.GetTempPath(), "repo"),
            RunnerId = "server2",
            WorktreePath = Path.Combine(Path.GetTempPath(), "repo-wt"),
            RemoteWorktreePath = null,
            ProgressBaselineJson = "{\"schemaVersion\":1}",
            VerificationRound = VerificationRound.Final,
        };
        switch (arm)
        {
            case "mirror-recorded": task.RemoteWorktreePath = "/work/worktrees/task-1"; break;
            case "no-baseline": task.ProgressBaselineJson = null; break;
            case "local": task.RunnerId = null; break;
            case "no-worktree": task.WorktreePath = null; break;
            case "repair-source": task.RepairSourceTaskId = Guid.NewGuid(); break;
            case "snapshot": task.SourceLandingOperationId = Guid.NewGuid(); break;
            case "interim": task.VerificationRound = VerificationRound.Interim; break;
            case "shared": task.Workspace = WorkspaceMode.Shared; break;
        }

        AgentTaskDispatcher.RearmsPreparedWorktree(task).ShouldBe(expected);
    }
}
