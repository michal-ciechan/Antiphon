using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class EvidenceFolderTests : CheckpointTestBase
{
    [Test]
    public void tool_copy_removal_retries_while_a_file_is_still_held_open()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Native file-sharing retry proof requires Windows.");
            return;
        }

        var dir = TempDir();
        CheckpointFixtures.MarkRun(dir, alive: false);
        var tool = Path.Combine(dir, "tool");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(tool, SafeWrite.Under(dir)));
        var locked = Path.Combine(tool, "YamlDotNet.dll");
        SafeWrite.Require(SafeWrite.Text(locked, "x", SafeWrite.Under(tool)));
        SafeWrite.Require(OwnedOutputLedger.Seal(tool));
        using var stream = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var attempts = 0;
        var receipt = new ToolCopyCleanup(beforeDelete: _ =>
        {
            attempts++;
            Console.WriteLine($"held-file run={dir} attempt={attempts} elapsedMs={clock.Elapsed.TotalMilliseconds:F1}");
            // The first native delete must encounter the held handle. Its retry
            // enters this callback again, so release needs no background worker.
            if (attempts == 2) stream.Dispose();
        }).Remove(dir);
        Console.WriteLine($"held-file outcome={receipt.Outcome} attempts={attempts} elapsedMs={clock.Elapsed.TotalMilliseconds:F1}");
        Console.WriteLine(receipt);
        receipt.Outcome.ShouldBe("Removed", "held-file-removed: the native sharing failure must be retried");
        attempts.ShouldBe(2, "held-file-two-attempts: removal must follow exactly one native sharing failure");
        Directory.Exists(tool).ShouldBeFalse("held-file-tool-absent: the removal receipt must match the filesystem");
    }

    [Test]
    public void red_run_writes_failures_with_message_stack_stdout_and_commands()
    {
        var dir = TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "rows", "CP-2"));
        Directory.CreateDirectory(Path.Combine(dir, "tool"));
        File.WriteAllText(Path.Combine(dir, "tool", "Antiphon.Checkpoints.dll"), "x");
        var model = Model(dir, exit: 1);
        EvidenceFolder.Write(dir, model, removeToolCopy: false);
        var text = File.ReadAllText(Path.Combine(dir, "failures.md"));
        text.ShouldContain("Expected 1 but was 2");
        text.ShouldContain("Sample.cs:line 4");
        text.ShouldContain("stdout line");
        text.ShouldContain("INHERITED");
        text.ShouldContain("dotnet run --project tools/Antiphon.Checkpoints -- row");
        text.ShouldContain("--treenode-filter");
        text.ShouldContain("--property:OutputPath=bin-a/");
        text.ShouldContain("--project tests/Antiphon.Tests");
        text.ShouldNotContain("--project bin-a");
        var rowFailures = File.ReadAllText(Path.Combine(dir, "rows", "CP-2", "failures.md"));
        rowFailures.ShouldContain("Expected 1 but was 2");
        File.Exists(Path.Combine(dir, "rows", "CP-2", "rerun.txt")).ShouldBeTrue();
        Directory.Exists(Path.Combine(dir, "tool")).ShouldBeTrue();
    }

    [Test]
    public void rerun_text_is_reset_for_each_row()
    {
        var dir = TempDir();
        var model = Model(dir, exit: 1);
        model.Rows.Add(new ReportRow
        {
            Id = "CP-3",
            Build = "bin-a",
            Failures = [new ReportFailure { Name = "Antiphon.Tests.Other.beta", Message = "other" }],
        });
        EvidenceFolder.Write(dir, model, removeToolCopy: false);
        var first = File.ReadAllText(Path.Combine(dir, "rows", "CP-2", "rerun.txt"));
        var second = File.ReadAllText(Path.Combine(dir, "rows", "CP-3", "rerun.txt"));
        first.ShouldContain("CP-2");
        first.ShouldNotContain("CP-3");
        second.ShouldContain("CP-3");
        second.ShouldNotContain("--name CP-2");
        File.Exists(Path.Combine(dir, "rows", "CP-3", "failures.md")).ShouldBeTrue();
    }

    [Test]
    public void green_run_leaves_the_executor_image()
    {
        var dir = TempDir();
        var tool = Path.Combine(dir, "tool");
        Directory.CreateDirectory(tool);
        File.WriteAllText(Path.Combine(tool, "Antiphon.Checkpoints.dll"), "x");
        var model = Model(dir, exit: 0);
        model.Rows[0].Failures.Clear();
        EvidenceFolder.Write(dir, model, removeToolCopy: true, imageDirectory: tool);
        Directory.Exists(tool).ShouldBeTrue();
        File.Exists(Path.Combine(tool, "Antiphon.Checkpoints.dll")).ShouldBeTrue();
    }

    [Test]
    public void non_image_remove_drops_the_tool_copy()
    {
        var dir = TempDir();
        CheckpointFixtures.MarkRun(dir, alive: false);
        var tool = Path.Combine(dir, "tool");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(tool, SafeWrite.Under(dir)));
        SafeWrite.Require(SafeWrite.Text(Path.Combine(tool, "Antiphon.Checkpoints.dll"), "x", SafeWrite.Under(tool)));
        SafeWrite.Require(OwnedOutputLedger.Seal(tool));
        var model = Model(dir, exit: 0);
        model.Rows[0].Failures.Clear();
        EvidenceFolder.Write(dir, model, removeToolCopy: true);
        File.Exists(Path.Combine(dir, "failures.md")).ShouldBeFalse();
        Directory.Exists(Path.Combine(dir, "tool")).ShouldBeFalse();
    }

    [Test]
    public void host_and_git_snapshots_present()
    {
        var dir = TempDir();
        var host = HostSnapshot.Capture("http://127.0.0.1:9/build-slots");
        var git = GitSnapshot.Capture(CheckpointFixtures.RepoRoot);
        File.WriteAllText(Path.Combine(dir, "host.txt"), host);
        File.WriteAllText(Path.Combine(dir, "git.txt"), git);
        host.ShouldContain("cores=");
        host.ShouldContain("memAvailableMb=");
        git.ShouldContain("HEAD=");
        git.ShouldContain("branch=");
    }

    [Test]
    public void git_snapshot_read_in_process_agrees_with_git()
    {
        var root = Directory.CreateDirectory(Path.Combine(TempDir(), "repo")).FullName;
        CheckpointFixtureProcess.Git(root, "init", "-q", "-b", "snap");
        File.WriteAllText(Path.Combine(root, "tracked.txt"), "one\n");
        CheckpointFixtureProcess.Git(root, "add", "tracked.txt");
        CheckpointFixtureProcess.Git(root, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "first subject");
        File.WriteAllText(Path.Combine(root, "tracked.txt"), "two\n");
        File.WriteAllText(Path.Combine(root, "loose.txt"), "new\n");
        var head = CheckpointFixtureProcess.Git(root, "rev-parse", "HEAD").Trim();

        GitSnapshot.CaptureInProcess(root).ShouldNotBeNull("this checkout is read in process");
        var text = GitSnapshot.Capture(root);
        text.ShouldContain("HEAD=" + head);
        text.ShouldContain("branch=snap");
        text.ShouldContain(" M tracked.txt");
        text.ShouldContain("?? loose.txt");
        text.ShouldContain(head[..7] + " first subject");
        GitSnapshot.Head(root).ShouldBe(head);
        GitSnapshot.Branch(root).ShouldBe(CheckpointFixtureProcess.Git(root, "rev-parse", "--abbrev-ref", "HEAD").Trim());

        // A branch name a tag also carries: git prints heads/<name>, and so does the snapshot (git answers it).
        CheckpointFixtureProcess.Git(root, "tag", "snap");
        GitSnapshot.Branch(root).ShouldBe(CheckpointFixtureProcess.Git(root, "rev-parse", "--abbrev-ref", "HEAD").Trim());
        GitSnapshot.Capture(root).ShouldContain("branch=" + CheckpointFixtureProcess.Git(root, "rev-parse", "--abbrev-ref", "HEAD").Trim());
        CheckpointFixtureProcess.Git(root, "tag", "-d", "snap");

        // Detached: git prints HEAD for the branch.
        CheckpointFixtureProcess.Git(root, "checkout", "-q", "--detach");
        GitSnapshot.Branch(root).ShouldBe("HEAD");
        GitSnapshot.Branch(root).ShouldBe(CheckpointFixtureProcess.Git(root, "rev-parse", "--abbrev-ref", "HEAD").Trim());
        GitSnapshot.Capture(root).ShouldContain("branch=HEAD");
        GitSnapshot.CaptureInProcess(root).ShouldNotBeNull("a detached checkout is read in process too");
    }

    private static ReportModel Model(string dir, int exit) => new()
    {
        RunId = "r",
        ExitCode = exit,
        Evidence = Path.Combine(dir, "report.md"),
        Builds = [new ReportBuild { Id = "bin-a", Project = "tests/Antiphon.Tests" }],
        Rows =
        [
            new ReportRow
            {
                Id = "CP-2",
                Build = "bin-a",
                Filter = "/*/*/Sample/*",
                Failures =
                [
                    new ReportFailure
                    {
                        Name = "Antiphon.Tests.Sample.alpha",
                        Message = "Expected 1 but was 2",
                        StackTrace = "at Sample.cs:line 4",
                        StdOut = "stdout line",
                        Baseline = "INHERITED",
                    },
                ],
            },
        ],
    };
}
