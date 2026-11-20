using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// CheckpointIndexPaths (scripts/lib/CheckpointIndexPaths.cs) answers run-checkpoint.ps1's tracked-path guards from the
/// index file. Wherever it answers, the answer is git's (`git ls-files -z -- path` printed anything); where git must
/// decide, it answers null.
/// </summary>
[Category("Integration")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointIndexPathsTests : CheckpointTestBase
{
    [Test]
    [Timeout(120_000)]
    public void Index_answer_matches_git_ls_files_wherever_it_answers(CancellationToken cancellationToken)
    {
        var root = Directory.CreateDirectory(Path.Combine(TempDir(), "repo")).FullName;
        CheckpointFixtureProcess.Git(root, "init", "-q");
        Write(root, "tracked.txt");
        Write(root, "src/app/Program.cs");
        Write(root, "src/app/bin/output.dll");
        Write(root, "loose/untracked.txt");
        Write(root, "src/app/new.txt");
        CheckpointFixtureProcess.Git(root, "add", "tracked.txt", "src/app/Program.cs");
        CheckpointFixtureProcess.Git(root, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "seed");
        // The toplevel git itself reports: the guard only trusts the index for that checkout.
        var top = CheckpointFixtureProcess.Git(root, "rev-parse", "--show-toplevel").Trim();
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "tracked.txt")], null!).ShouldBeNull("git has not vouched");
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "tracked.txt")], Path.GetDirectoryName(root)!)
            .ShouldBeNull("git reports a different toplevel");

        foreach (var relative in new[] { "tracked.txt", "src", "src/app", "src/app/Program.cs", "src/app/bin",
                     "src/app/bin/output.dll", "loose", "loose/untracked.txt", "src/app/new.txt", "missing", "missing/deeper.txt" })
        {
            var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            var answer = CheckpointIndexPaths.AnyTracked(root, [full], top);
            answer.ShouldNotBeNull(relative + " is a plain index read");
            answer.ShouldBe(GitTracks(root, full), relative + " matches git ls-files");
        }
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "loose"), Path.Combine(root, "tracked.txt")], top)
            .ShouldBe(true, "any of several paths");
        CheckpointIndexPaths.AnyTracked(root, [root], top).ShouldBe(true, "the checkout root holds tracked files");
        if (OperatingSystem.IsWindows())
            CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "TRACKED.TXT")], top)
                .ShouldBe(true, "case-insensitive on Windows can only call more paths tracked");

        // Git decides these.
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "src", "*.cs")], top).ShouldBeNull("pathspec glob");
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(Path.GetDirectoryName(root)!, "elsewhere.txt")], top)
            .ShouldBeNull("outside the checkout");
        if (OperatingSystem.IsWindows())
            CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "tracked.txt.")], top)
                .ShouldBeNull("a path Path.GetFullPath would change (trailing dot)");

        // A repository with no index yet is git's to answer.
        var fresh = Directory.CreateDirectory(Path.Combine(TempDir(), "fresh")).FullName;
        CheckpointFixtureProcess.Git(fresh, "init", "-q");
        CheckpointIndexPaths.AnyTracked(fresh, [Path.Combine(fresh, "a.txt")], fresh).ShouldBeNull("no index file");

        // A linked worktree's .git file names its gitdir.
        var linked = Path.Combine(Path.GetDirectoryName(root)!, "linked");
        CheckpointFixtureProcess.Git(root, "worktree", "add", "-q", "--detach", linked);
        var linkedTop = CheckpointFixtureProcess.Git(linked, "rev-parse", "--show-toplevel").Trim();
        var linkedTracked = Path.Combine(linked, "src", "app", "Program.cs");
        CheckpointIndexPaths.AnyTracked(linked, [linkedTracked], linkedTop).ShouldBe(true, "linked worktree index");
        CheckpointIndexPaths.AnyTracked(linked, [Path.Combine(linked, "loose")], linkedTop).ShouldBe(false);

        // A version 3 index (skip-worktree) is git's.
        CheckpointFixtureProcess.Git(root, "update-index", "--skip-worktree", "tracked.txt");
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "tracked.txt")], top).ShouldBeNull("version 3 index");
        CheckpointFixtureProcess.Git(root, "update-index", "--no-skip-worktree", "tracked.txt");
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "tracked.txt")], top).ShouldBe(true, "version 2 again");

        // core.worktree elsewhere moves the work tree: git reports that toplevel, and the index is not trusted here.
        var elsewhere = Directory.CreateDirectory(Path.Combine(TempDir(), "elsewhere")).FullName;
        CheckpointFixtureProcess.Git(root, "config", "core.worktree", elsewhere);
        var moved = CheckpointFixtureProcess.Git(root, "rev-parse", "--show-toplevel").Trim();
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "tracked.txt")], moved).ShouldBeNull("core.worktree");
    }

    private static bool GitTracks(string root, string full) =>
        CheckpointFixtureProcess.Git(root, "ls-files", "-z", "--", full).Length != 0;

    private static void Write(string root, string relative)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relative + "\n");
    }
}
