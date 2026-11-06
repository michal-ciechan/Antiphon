using System.Diagnostics;
using Antiphon.Tests.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReportBudgetWorkspacePortabilityTests
{
    [Test]
    public async Task Template_reset_removes_readonly_seed_objects()
    {
        using var repo = new ScratchGitRepo("report-budget-reset");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, ".gitignore"), ".antiphon/\n");
        await repo.CommitFileAsync("seed.txt", "benign fixture");
        var tree = (await repo.GitReadAsync("rev-parse", "HEAD^{tree}")).Trim();
        tree.ShouldBe("831fc61e72b5c6b9570f91d6eabc272ad8f2899f");
        var looseObject = Path.Combine(repo.Path, ".git", "objects", tree[..2], tree[2..]);
        File.SetAttributes(looseObject, File.GetAttributes(looseObject) | FileAttributes.ReadOnly);
        File.GetAttributes(looseObject).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
        var deletes = 0;

        ReportBudgetWorkspace.ResetTemplateDirectory(repo.Path, (path, recursive) =>
        {
            deletes++;
            path.ShouldBe(repo.Path);
            recursive.ShouldBeTrue();
            // Linux can unlink a read-only file. Enforce the Windows refusal at the actual
            // delete boundary so this witness also detects missing attribute clearing here.
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                if (File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly))
                    throw new UnauthorizedAccessException("Access to the path '" + Path.GetFileName(file) + "' is denied.");
            Directory.Delete(path, recursive);
        });

        deletes.ShouldBe(1);
        Directory.Exists(repo.Path).ShouldBeTrue();
        Directory.EnumerateFileSystemEntries(repo.Path).ShouldBeEmpty();
    }

    [Test]
    public void Template_reset_creates_a_missing_directory_without_deleting()
    {
        var root = Directory.CreateTempSubdirectory("report-budget-reset-").FullName;
        try
        {
            var template = Path.Combine(root, "template");
            ReportBudgetWorkspace.ResetTemplateDirectory(template, (_, _) =>
                throw new InvalidOperationException("a missing directory must not be deleted"));
            Directory.Exists(template).ShouldBeTrue();
        }
        finally { ReadOnlyDirectory.Delete(root); }
    }

    [Test]
    public void Template_reset_propagates_delete_failure_without_reusing_stale_contents()
    {
        var root = Directory.CreateTempSubdirectory("report-budget-reset-").FullName;
        try
        {
            var marker = Path.Combine(root, "stale");
            File.WriteAllText(marker, "retained");
            var failure = new IOException("owned fixture deletion failed");
            Should.Throw<IOException>(() => ReportBudgetWorkspace.ResetTemplateDirectory(root,
                (_, _) => throw failure)).ShouldBeSameAs(failure);
            File.ReadAllText(marker).ShouldBe("retained");
        }
        finally { ReadOnlyDirectory.Delete(root); }
    }

    [Test]
    public void Git_launch_uses_the_installed_PATH_command()
    {
        // A resolved Linux absolute path can pass every budget test here while breaking Windows.
        // Require the same executable contract as CheckpointFixtureProcess.Git, then launch it.
        var start = new ProcessStartInfo(ReportBudgetWorkspace.ResolveGitExecutable());
        start.FileName.ShouldBe("git", "the fixture must let the host resolve Git from PATH");
        start.ArgumentList.Add("--version");
        var output = CheckpointFixtureProcess.Run(start, 30_000);
        output.Exit.ShouldBe(0, output.Stderr);
        output.Stdout.ShouldStartWith("git version ");
    }
}
