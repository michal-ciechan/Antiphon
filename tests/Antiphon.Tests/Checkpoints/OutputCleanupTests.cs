using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class OutputCleanupTests
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
        var root = CheckpointFixtures.TempDir();
        var old = Path.Combine(root, "20200101-000000-aaaa");
        var young = Path.Combine(root, "20990101-000000-bbbb");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(young);
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

    private static string Tree()
    {
        var root = CheckpointFixtures.TempDir();
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
