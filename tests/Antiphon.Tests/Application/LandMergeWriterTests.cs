using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class LandMergeWriterTests
{
    [Test]
    public void C753_MergeHelperOnlyHoldsLandWithConfirmedLivePrompt()
    {
        var repo = Path.Combine(Path.GetTempPath(), "antiphon-land-merge-scope");
        var landing = new AgentTask
        {
            Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "landing",
            Workspace = WorkspaceMode.Worktree, Role = AgentTaskRole.Code,
            RepoPath = repo, WorkingDirectory = repo,
        };
        var helper = new AgentTask
        {
            Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "merge helper",
            Workspace = WorkspaceMode.Shared, Role = AgentTaskRole.Merge,
            RepoPath = repo, WorkingDirectory = repo, Status = AgentTaskStatus.Dispatched,
        };
        AgentTaskLandService.IsHeldBehindSharedWriter(landing, [helper]).ShouldBeFalse();
        AgentTaskLandService.IsHeldBehindSharedWriter(landing, [helper], _ => false).ShouldBeFalse();
        AgentTaskLandService.IsHeldBehindSharedWriter(landing, [helper], _ => true).ShouldBeTrue();
    }
}
