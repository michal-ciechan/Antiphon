using System.Text.Json;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class ReportWriterTests
{
    [Test]
    public void block_layout_matches_d10()
    {
        var text = ReportWriter.Markdown(Sample());
        text.ShouldContain("--- checkpoint report ---");
        text.ShouldContain("run: 20260925-190102-ab12");
        text.ShouldContain("commit: " + new string('a', 40));
        text.ShouldContain("branch: feat/card-task-xxxx");
        text.ShouldContain("CHECKPOINT CP-1");
        text.ShouldContain("verdict: RED exit=1");
        text.ShouldContain("wall:");
        text.ShouldContain("sequential-equivalent:");
    }

    [Test]
    public void unlisted_is_none()
    {
        ReportWriter.Markdown(Sample()).ShouldContain("unlisted: none (the tool ran no other build or test command)");
    }

    [Test]
    public void tool_runs_are_not_reported_as_unlisted_none()
    {
        var model = Sample();
        model.Unlisted = ["tool-run: baseline git fetch origin/master", "tool-run: baseline build bin-c723a"];
        var text = ReportWriter.Markdown(model);
        text.ShouldContain("tool-run: baseline git fetch origin/master");
        text.ShouldContain("tool-run: baseline build bin-c723a");
        text.ShouldNotContain("unlisted: none");
    }

    [Test]
    public void empty_outputs_say_none()
    {
        var model = Sample();
        model.ExitCode = 0;
        model.Verdict = "GREEN";
        model.OutputNames = [];
        var text = ReportWriter.Markdown(model);
        text.ShouldContain("outputs: none");
        text.ShouldNotContain("outputs: deleted ");
        text.ShouldNotContain("outputs: deleted\n");
    }

    [Test]
    public void json_carries_every_row_field()
    {
        using var doc = JsonDocument.Parse(ReportWriter.JsonText(Sample()));
        var root = doc.RootElement;
        foreach (var name in new[]
                 {
                     "schemaVersion", "runId", "commit", "branch", "worktree", "host", "startedAt", "endedAt",
                     "wallSeconds", "sequentialEquivalentSeconds", "exitCode", "verdict", "builds", "rows", "unlisted", "evidence",
                 })
            root.TryGetProperty(name, out _).ShouldBeTrue(name);
        var row = root.GetProperty("rows")[0];
        foreach (var name in new[]
                 {
                     "id", "group", "filter", "command", "build", "state", "exitCode", "executed", "passed", "failed",
                     "skipped", "reruns", "trx", "seconds", "failures", "rerunLines", "slowClasses",
                 })
            row.TryGetProperty(name, out _).ShouldBeTrue(name);
        root.GetProperty("host").GetProperty("cores").GetInt32().ShouldBeGreaterThan(0);
    }

    private static ReportModel Sample() => new()
    {
        RunId = "20260925-190102-ab12",
        ManifestPath = ".antiphon/checkpoints/20260925-190102-ab12/manifest.resolved.yaml",
        Commit = new string('a', 40),
        Branch = "feat/card-task-xxxx",
        Worktree = "/work/worktrees/task-xxxx",
        Host = new ReportHost { Os = "linux", Cores = 24, MemAvailableMb = 1024, LoadAvg = "1.0" },
        StartedAt = DateTimeOffset.Parse("2026-09-25T19:01:02Z"),
        EndedAt = DateTimeOffset.Parse("2026-09-25T19:15:05Z"),
        WallSeconds = 843,
        SequentialEquivalentSeconds = 1330,
        ExitCode = 1,
        Verdict = "RED",
        Builds = [new ReportBuild { Id = "bin-c723a", Project = "tests/Antiphon.Tests", State = "ok" }],
        Rows =
        [
            new ReportRow
            {
                Id = "CP-1",
                Group = "tool-core",
                Filter = "/*/*/ExampleSurfaceTests/*",
                Build = "bin-c723a",
                State = "red",
                ExitCode = 1,
                Executed = 3,
                Passed = 2,
                Failed = 1,
                Skipped = 0,
                Reruns = 0,
                Trx = "/tmp/run.trx",
                Seconds = 98,
                Line = "CHECKPOINT CP-1 commit=" + new string('a', 40) + " build=ok filter=/*/*/ExampleSurfaceTests/* executed=3 passed=2 failed=1 skipped=0 trx=/tmp/run.trx slot=unavailable waited=0s",
                Failures = [new ReportFailure { Name = "Antiphon.Tests.Sample.alpha", Message = "no" }],
            },
        ],
        Evidence = "/tmp/report.md",
        OutputNames = ["bin-c723a/"],
    };
}
