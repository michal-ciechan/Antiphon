using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-2070 (CARD-2058 review advisory): <see cref="AgentService.PathsMatch"/> and the
/// attention worktree-cwd test used this host's separator and case rules, so the Windows server
/// and a Linux runner disagreed about the same stored paths. Both now follow the spelling-based
/// rule of <see cref="WorkingDirectoryIdentity"/>; every case here has one answer on every host.
/// </summary>
[Category("Unit")]
public class C2070HostIndependentPathMatchTests
{
    [Test]
    [Arguments(@"C:\Work\Repo", "c:/work/repo/")]
    [Arguments(@"C:\Antiphon\worktrees\card-task-1", "C:/Antiphon/worktrees/card-task-1")]
    [Arguments(@"C:/Antiphon\worktrees/card-task-1\", @"c:\antiphon\WORKTREES\card-task-1")]
    [Arguments(@"\\FileHost\Share\Repo", "\\/filehost/share/repo/")]
    [Arguments("/srv/repo", "/srv/repo/")]
    public void Same_directory_spellings_match_on_every_host(string left, string right)
    {
        AgentService.PathsMatch(left, right).ShouldBeTrue();
        AgentService.PathsMatch(right, left).ShouldBeTrue();
    }

    [Test]
    [Arguments("/srv/Team/repo", "/srv/team/repo")]
    [Arguments(@"/srv/alpha\repo", "/srv/alpha/repo")]
    [Arguments("/c/repo", @"C:\c\repo")]
    [Arguments(@"C:\work", @"C:\work-other")]
    [Arguments(@"C:\work", null)]
    [Arguments(" ", " ")]
    public void Distinct_directory_spellings_do_not_match_on_any_host(string left, string? right)
    {
        AgentService.PathsMatch(left, right).ShouldBeFalse();
        AgentService.PathsMatch(right, left).ShouldBeFalse();
    }

    [Test]
    [Arguments(@"C:\Antiphon\.worktrees\task-1", true)]
    [Arguments(@"C:/Antiphon/.worktrees/task-1", true)]
    [Arguments(@"C:\Antiphon/.WORKTREES\task-1", true)]
    [Arguments(@"\\filehost\share\.worktrees\task-1", true)]
    [Arguments("/work/repo/.worktrees/task-1", true)]
    [Arguments(@"/work/repo/alpha\.worktrees\task-1", false)]
    [Arguments(@"C:\Antiphon\worktrees\task-1", false)]
    [Arguments("/work/repo", false)]
    public void Worktree_cwd_detection_follows_the_path_spelling(string workingDirectory, bool worktree) =>
        AttentionService.IsWorktreeCwd(workingDirectory).ShouldBe(worktree);
}
