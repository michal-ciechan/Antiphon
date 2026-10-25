using Antiphon.SessionRunner;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class RunnerWorkspaceCwdWindowsTests
{
    [Test]
    [Arguments(@"C:\work", @"C:\work", true)]
    [Arguments(@"C:\work\", @"C:\work", true)]
    [Arguments(@"C:\work", @"C:\work\worktrees\task-deadbeef", true)]
    [Arguments(@"C:\work", "C:/work/worktrees/task-deadbeef", true)]
    [Arguments(@"C:\work", @"c:\WORK\WORKTREES\task-deadbeef", true)]
    [Arguments(@"C:\work", @"C:\workspace\worktrees\task-deadbeef", false)]
    [Arguments(@"C:\work", @"C:\work-other", false)]
    [Arguments(@"C:\work", @"C:\work\worktrees", false)]
    [Arguments(@"C:\work", @"C:\work\worktrees\", false)]
    [Arguments(@"C:\work", @"C:\work\worktrees\.", false)]
    [Arguments(@"C:\work", @"C:\work\worktrees\..", false)]
    [Arguments(@"C:\work", @"C:\work\worktrees\..\outside", false)]
    [Arguments(@"C:\work", "C:/work/worktrees/../outside", false)]
    [Arguments(@"C:\work", @"C:\work/worktrees/task\child", false)]
    [Arguments(@"C:\work", @"C:\work\worktrees\task/child", false)]
    [Arguments(@"C:\work", "C:/work/worktrees/task:stream", false)]
    [Arguments(@"C:\work", "C:/work/worktrees/task.", false)]
    [Arguments(@"C:\work", "C:/work/worktrees/task ", false)]
    [Arguments(@"C:\work", @"C:work\worktrees\task", false)]
    [Arguments(@"C:\work", @"D:\work\worktrees\task", false)]
    [Arguments(@"\\server\share\work", @"\\SERVER\share\work\worktrees\task", true)]
    public void Windows_paths_keep_the_single_worktree_boundary(string root, string cwd, bool expected) =>
        RunnerWorkspaceCwd.IsAdmitted(cwd, root, windows: true).ShouldBe(expected);

    [Test]
    [Arguments("/work", true)]
    [Arguments("/work/worktrees/task", true)]
    [Arguments("/WORK/worktrees/task", false)]
    [Arguments("/work/worktrees/task/child", false)]
    [Arguments("/work/worktrees/..", false)]
    [Arguments(@"\work\worktrees\task", false)]
    public void Unix_paths_retain_ordinal_slash_semantics(string cwd, bool expected) =>
        RunnerWorkspaceCwd.IsAdmitted(cwd, "/work", windows: false).ShouldBe(expected);
}
