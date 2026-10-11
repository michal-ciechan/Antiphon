using System.Diagnostics;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class OutputCleanupTests : CheckpointTestBase
{
    [Test]
    public void deletes_only_this_runs_bin_names()
    {
        var root = Tree();
        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, false);
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeFalse();
        Directory.Exists(Path.Combine(root, "src", "bin-c723a")).ShouldBeFalse();
        File.Exists(Path.Combine(root, "bin-foreign", "keep.txt")).ShouldBeTrue();
        result.Deleted.Count.ShouldBe(2);
    }

    [Test]
    public void never_deletes_bin_obj_workspace_or_foreign_bin_star()
    {
        var root = Tree();
        Directory.CreateDirectory(Path.Combine(root, "bin", "bin-c723a"));
        File.WriteAllText(Path.Combine(root, "bin", "bin-c723a", "sentinel"), "keep");
        File.WriteAllText(Path.Combine(root, "bin", "keep.dll"), "keep");
        OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, false);
        File.Exists(Path.Combine(root, "bin", "keep.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(root, "bin", "bin-c723a", "sentinel")).ShouldBeTrue();
        File.Exists(Path.Combine(root, "obj", "keep.o")).ShouldBeTrue();
        File.Exists(Path.Combine(root, "workspace", "keep.txt")).ShouldBeTrue();
        File.Exists(Path.Combine(root, "bin-foreign", "keep.txt")).ShouldBeTrue();
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeFalse();
    }

    [Test]
    public void red_keeps_unless_clean_on_red()
    {
        var root = Tree();
        var kept = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 1, false, false);
        kept.KeptBecauseRed.ShouldBeTrue();
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeTrue();
        OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 1, true, false);
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeFalse();
    }

    [Test]
    public void older_than_removes_run_folders()
    {
        var root = TempDir();
        var old = Path.Combine(root, "20200101-000000-aaaa");
        var young = Path.Combine(root, "20990101-000000-bbbb");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(young);
        CheckpointFixtures.MarkRun(old, alive: false);
        CheckpointFixtures.MarkRun(young, alive: true);
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));
        var dry = OutputCleanup.RemoveOlderRuns(root, TimeSpan.FromDays(7), dryRun: true);
        dry.Count.ShouldBe(1);
        Directory.Exists(old).ShouldBeTrue();
        var removed = OutputCleanup.RemoveOlderRuns(root, TimeSpan.FromDays(7), dryRun: false);
        removed.Count.ShouldBe(1);
        Directory.Exists(old).ShouldBeFalse();
        Directory.Exists(young).ShouldBeTrue();
    }

    [Test]
    public void evidence_under_antiphon_survives_and_dry_run_says_would_delete()
    {
        var root = Tree();
        var evidence = Path.Combine(root, ".antiphon", "checkpoints", "run", "builds", "bin-c723a");
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "build.log"), "log");
        var dry = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, dryRun: true);
        dry.Deleted.ShouldContain(path => path.EndsWith("bin-c723a", StringComparison.Ordinal) && !path.Contains(".antiphon", StringComparison.Ordinal));
        OutputCleanup.DeletedLine(true, dry.Deleted[0]).ShouldStartWith("would delete ");
        OutputCleanup.DeletedLine(false, dry.Deleted[0]).ShouldStartWith("deleted ");
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeTrue();
        var deleted = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, dryRun: false);
        deleted.Deleted.ShouldAllBe(path => !path.Contains(".antiphon", StringComparison.Ordinal));
        File.Exists(Path.Combine(evidence, "build.log")).ShouldBeTrue();
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeFalse();
    }

    [Test]
    public void refuses_unsafe_build_ids_and_keeps_tracked_files()
    {
        var root = InitGitRepo();
        var tests = Path.Combine(root, "tests");
        var src = Path.Combine(root, "src");
        var trackedBin = Path.Combine(root, "bin-tracked");
        var safe = Path.Combine(root, "bin-safe");
        Directory.CreateDirectory(tests);
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(trackedBin);
        Directory.CreateDirectory(safe);
        File.WriteAllText(Path.Combine(tests, "kept.cs"), "tracked");
        File.WriteAllText(Path.Combine(src, "kept.cs"), "tracked");
        File.WriteAllText(Path.Combine(trackedBin, "kept.txt"), "tracked");
        File.WriteAllText(Path.Combine(safe, "out.dll"), "untracked");
        Git(root, "add", "--", "tests/kept.cs", "src/kept.cs", "bin-tracked/kept.txt");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "card-1199", "--no-verify");

        var outside = Path.Combine(Path.GetTempPath(), "c1199-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "outside");
        var parent = Directory.GetParent(root)!.FullName;
        try
        {
            OutputCleanup.CleanOwnedOutputs(root,
                ["tests", ".", "..", "src", outside, "bin-tracked", "bin-safe"],
                0, false, false);

            File.Exists(Path.Combine(tests, "kept.cs")).ShouldBeTrue();
            File.Exists(Path.Combine(src, "kept.cs")).ShouldBeTrue();
            File.Exists(Path.Combine(trackedBin, "kept.txt")).ShouldBeTrue();
            Directory.Exists(root).ShouldBeTrue();
            Directory.Exists(parent).ShouldBeTrue();
            File.Exists(Path.Combine(outside, "keep.txt")).ShouldBeTrue();
            Directory.Exists(safe).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }

    [Test]
    public void deletes_only_paths_recorded_for_this_run()
    {
        var root = Tree();
        var preexisting = Path.Combine(root, "bin-c723a");
        var nested = Path.Combine(root, "src", "bin-c723a");
        var before = OutputCleanup.MatchingIsolatedDirectories(root, "bin-c723a");
        var fresh = Path.Combine(root, "fresh", "bin-c723a");
        Directory.CreateDirectory(fresh);
        Directory.CreateDirectory(Path.Combine(root, "tests"));
        var state = new RunState();
        OutputCleanup.RecordCreatedOutputs(state, root, "bin-c723a", before);
        OutputCleanup.RecordCreatedOutputs(state, root, "tests", []);
        state.CreatedOutputPaths.ShouldBe([Path.GetFullPath(fresh)]);

        var deleted = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a", "tests"], 0, false, false,
            createdPaths: state.CreatedOutputPaths);
        Directory.Exists(fresh).ShouldBeFalse();
        Directory.Exists(preexisting).ShouldBeTrue();
        Directory.Exists(nested).ShouldBeTrue();
        Directory.Exists(Path.Combine(root, "tests")).ShouldBeTrue();
        deleted.Deleted.Count.ShouldBe(1);

        OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, false, createdPaths: []);
        Directory.Exists(preexisting).ShouldBeTrue();
    }

    private string InitGitRepo()
    {
        var root = TempDir();
        Git(root, "init", "-b", "card1199");
        return root;
    }

    private static void Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment.Remove("GIT_DIR");
        start.Environment.Remove("GIT_WORK_TREE");
        start.Environment.Remove("GIT_INDEX_FILE");
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000).ShouldBeTrue();
        process.ExitCode.ShouldBe(0, stdout + stderr);
    }

    private string Tree()
    {
        var root = TempDir();
        Write(root, "bin-c723a", "out.dll");
        Write(root, Path.Combine("src", "bin-c723a"), "out.dll");
        Write(root, "bin-foreign", "keep.txt");
        Write(root, "obj", "keep.o");
        Write(root, "workspace", "keep.txt");
        Write(root, "bin", "keep.dll");
        return root;
    }

    private static void Write(string root, string dir, string file)
    {
        var path = Path.Combine(root, dir);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, file), "x");
    }
}
