using Antiphon.Server.Application.Dtos;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class WorkspaceReservationKeyTests
{
    [Test]
    public void C459_Same_matches_git_common_dir_to_repo_path()
    {
        var worktree = Path.Combine(Path.GetTempPath(), "c459-fence", "card-task-abcd1234");
        var repo = Path.Combine(Path.GetTempPath(), "c459-fence", "repo");
        var retirement = WorkspaceReservationKey.For(worktree, "refs/heads/feat/card-task-abcd1234", Path.Combine(repo, ".git"));
        var consumer = WorkspaceReservationKey.ForTask(worktree, worktree, "feat/card-task-abcd1234", repo);
        WorkspaceReservationKey.Same(retirement, consumer).ShouldBeTrue();
        retirement.CommonDirectory.ShouldBe(WorkspaceReservationKey.NormalizePath(repo));
        consumer.SourceFullRef.ShouldBe("refs/heads/feat/card-task-abcd1234");
    }

    [Test]
    public void C459_Same_matches_session_cwd_with_empty_ref()
    {
        var worktree = Path.Combine(Path.GetTempPath(), "c459-session", "card-task-abcd1234");
        var repo = Path.Combine(Path.GetTempPath(), "c459-session", "repo");
        var retirement = WorkspaceReservationKey.For(worktree, "refs/heads/feat/card-task-abcd1234", Path.Combine(repo, ".git"));
        var session = WorkspaceReservationKey.For(worktree, "", worktree);
        WorkspaceReservationKey.Same(retirement, session).ShouldBeTrue();
    }

    [Test]
    public void C459_Same_rejects_a_sibling_worktree()
    {
        var root = Path.Combine(Path.GetTempPath(), "c459-sib");
        var left = WorkspaceReservationKey.For(Path.Combine(root, "card-task-aaaa1111"), "refs/heads/feat/card-task-aaaa1111", Path.Combine(root, "repo"));
        var right = WorkspaceReservationKey.For(Path.Combine(root, "card-task-bbbb2222"), "refs/heads/feat/card-task-bbbb2222", Path.Combine(root, "repo"));
        WorkspaceReservationKey.Same(left, right).ShouldBeFalse();
    }
}
