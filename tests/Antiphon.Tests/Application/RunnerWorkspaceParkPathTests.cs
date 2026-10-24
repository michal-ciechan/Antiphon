using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class RunnerWorkspaceParkPathTests
{
    [Test, Arguments(false), Arguments(true)]
    public void Native_and_slash_roots_admit_only_the_owned_direct_child(bool slashRoot)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1565-path");
        if (slashRoot) root = root.Replace('\\', '/');
        var workspace = new RunnerWorkspaceService(Path.Combine(root, "repo"), root);
        var park = new RunnerWorkspaceParkService(workspace);
        var taskId = Guid.Parse("73936f77-10da-404c-8106-a9e0333d899d");
        var binding = new WorkspaceParkBinding(Guid.NewGuid(), Guid.NewGuid(), taskId, 1,
            Guid.NewGuid(), Guid.NewGuid(), "fixture", Guid.NewGuid(), Guid.NewGuid(),
            DateTime.UnixEpoch, Guid.NewGuid(), "report", "repository", "endpoint",
            "refs/heads/feat/card-task-73936f77", new string('a', 40));
        var path = Path.Combine(root, "worktrees", "task-73936f77");
        park.ValidateLexicalTarget(new(path, binding)).ShouldBeNull();
        park.ValidateLexicalTarget(new(path.Replace('\\', '/'), binding)).ShouldBeNull();
        park.ValidateLexicalTarget(new(Path.Combine(root, "worktrees-other", "task-73936f77"), binding))
            .ShouldBe("park_path_unowned");
        park.ValidateLexicalTarget(new(Path.Combine(path, "task-73936f77"), binding))
            .ShouldBe("park_path_unowned");
        park.ValidateLexicalTarget(new(Path.Combine(root, "worktrees", "task-00000000"), binding))
            .ShouldBe("park_invalid_target");
    }
}
