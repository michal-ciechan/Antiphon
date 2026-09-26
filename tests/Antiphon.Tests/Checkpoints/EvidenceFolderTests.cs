using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class EvidenceFolderTests
{
    [Test]
    public void red_run_writes_failures_with_message_stack_stdout_and_commands()
    {
        var dir = CheckpointFixtures.TempDir();
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
        var dir = CheckpointFixtures.TempDir();
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
        var dir = CheckpointFixtures.TempDir();
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
    public void green_run_writes_report_only_and_removes_tool_copy()
    {
        var dir = CheckpointFixtures.TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "tool"));
        File.WriteAllText(Path.Combine(dir, "tool", "Antiphon.Checkpoints.dll"), "x");
        var model = Model(dir, exit: 0);
        model.Rows[0].Failures.Clear();
        EvidenceFolder.Write(dir, model, removeToolCopy: true);
        File.Exists(Path.Combine(dir, "failures.md")).ShouldBeFalse();
        Directory.Exists(Path.Combine(dir, "tool")).ShouldBeFalse();
    }

    [Test]
    public void host_and_git_snapshots_present()
    {
        var dir = CheckpointFixtures.TempDir();
        var host = HostSnapshot.Capture("http://127.0.0.1:9/build-slots");
        var git = GitSnapshot.Capture(CheckpointFixtures.RepoRoot);
        File.WriteAllText(Path.Combine(dir, "host.txt"), host);
        File.WriteAllText(Path.Combine(dir, "git.txt"), git);
        host.ShouldContain("cores=");
        host.ShouldContain("memAvailableMb=");
        git.ShouldContain("HEAD=");
        git.ShouldContain("branch=");
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
