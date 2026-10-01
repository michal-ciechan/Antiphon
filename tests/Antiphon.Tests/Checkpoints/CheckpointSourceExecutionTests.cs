using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointSourceExecutionTests : CheckpointTestBase
{
    private static readonly string Sha = new('a', 40);
    private static readonly string OtherSha = new('b', 40);
    private static readonly string Fingerprint = new('1', 64);

    [Test]
    public async Task clean_and_dirty_runs_publish_bound_source()
    {
        foreach (var dirty in new[] { false, true })
        {
            var source = Observe(Sha, dirty ? 1 : 0);
            var driver = new FakeDriver();
            var manifest = CommandManifest();
            var run = NewRun(manifest, source);
            var exit = await CheckpointApp.ExecuteAsync(run, CancellationToken.None,
                Runtime(driver, _ => source));
            exit.ShouldBe(0, "diagnostic-command-run");
            var report = Read(run);
            report.Source.State.ShouldBe(dirty ? "dirty" : "clean");
            report.Rows.Single().Source.State.ShouldBe(dirty ? "dirty" : "clean");
            report.Rows.Single().Line.ShouldContain("source=" + SourceEvidence.Token(source));
            File.ReadAllText(Path.Combine(run, "report.md")).ShouldContain("source=" + SourceEvidence.Token(source));
            File.ReadAllText(Path.Combine(run, "git.txt")).ShouldContain("source=" + SourceEvidence.Token(source));
            ReportValidator.Validate(report, Sha).ShouldBe(dirty ? "report_source_ineligible" : null);
        }
    }

    [Test]
    public async Task changed_admission_and_driver_boundaries_refuse()
    {
        var before = Observe(Sha, 0);
        var after = Observe(OtherSha, 0);
        var driver = new FakeDriver();
        var run = NewRun(CommandManifest(), before);
        var exit = await CheckpointApp.ExecuteAsync(run, CancellationToken.None, Runtime(driver, _ => after));
        exit.ShouldBe(ExitCodes.Invalid, "queued-source-not-readmitted");
        driver.Calls.ShouldBeEmpty("changed-admission-precedes-driver");
        var report = Read(run);
        report.Source.State.ShouldBe("changed");
        report.Rows.Single().State.ShouldBe("source_changed");
        report.Source.Start.Commit.ShouldBe(Sha, "original-identity-retained");
        report.Source.End!.Commit.ShouldBe(OtherSha);

        var observations = 0;
        var during = new FakeDriver();
        var rowRun = NewRun(CommandManifest(), before);
        var rowExit = await CheckpointApp.ExecuteAsync(rowRun, CancellationToken.None,
            Runtime(during, _ => ++observations <= 2 ? before : after));
        rowExit.ShouldBe(ExitCodes.Invalid, "driver-boundary-drift");
        Read(rowRun).Source.State.ShouldBe("changed");
    }

    [Test]
    public void strict_cli_and_reuse_require_clean_binding()
    {
        var source = Observe(Sha, 0);
        var manifest = CommandManifest();
        Should.Throw<ManifestValidationException>(() => CheckpointApp.CreateRun(manifest,
            new RunRequest { Commit = Sha, Branch = "test", ExpectedSourceSha = OtherSha }, TempDir(),
            new CheckpointApp.Runtime { SourceCapture = _ => source }));
        Should.Throw<ManifestValidationException>(() => CheckpointApp.CreateRun(manifest,
            new RunRequest { Commit = Sha, Branch = "test", ExpectedSourceSha = Sha }, TempDir(),
            new CheckpointApp.Runtime { SourceCapture = _ => Observe(Sha, 1) }));
        var root = TempDir();
        var expected = CheckpointBuildBinding.Expected(root, "sample/sample.csproj", "bin-c835/",
            ["--property:UseAppHost=false"], source);
        expected.Write();
        expected.Check().ShouldBe("verified");
        CheckpointBuildBinding.Expected(root, "sample/sample.csproj", "bin-c835/",
            ["--property:UseAppHost=true"], source).Check().ShouldBe("mismatch", "property-mismatch-no-tests");
        CheckpointBuildBinding.Expected(root, "sample/sample.csproj", "bin-c835/",
            ["--property:UseAppHost=false"], Observe(OtherSha, 0)).Check().ShouldBe("mismatch");
        File.Delete(expected.PathName);
        expected.Check().ShouldBe("unknown", "missing-stamp-refuses-strict-reuse");
    }

    [Test]
    public async Task terminal_paths_do_not_invent_clean_evidence()
    {
        var source = Observe(Sha, 0);
        var run = NewRun(CommandManifest(), source);
        var exit = await CheckpointApp.ExecuteAsync(run, CancellationToken.None,
            Runtime(new FakeDriver(), _ => throw new IOException("capture failure")));
        exit.ShouldBe(ExitCodes.ExecutorCrashed);
        var report = Read(run);
        report.Source.End.ShouldBeNull("executor-error-has-no-observed-end");
        ReportValidator.Validate(report, Sha).ShouldBe("report_source_ineligible");
        report.ExitCode.ShouldBe(ExitCodes.ExecutorCrashed);
    }

    [Test]
    public void merge_cannot_launder_source_identity()
    {
        var clean = ValidReport();
        var earlier = ValidReport();
        earlier.EndedAt = clean.EndedAt.AddMinutes(-1);
        ReportMerger.Merge([earlier, clean]).Rows.Count.ShouldBe(1);
        earlier.Rows.Single().Source = Evidence(Observe(Sha, 1), "dirty", "notApplicable");
        Should.Throw<InvalidOperationException>(() => ReportMerger.Merge([earlier, clean]),
            "old-dirty-row-not-relabeled");
        earlier.Rows.Single().Source = Evidence(Observe(OtherSha, 0), "clean", "notApplicable");
        Should.Throw<InvalidOperationException>(() => ReportMerger.Merge([earlier, clean]), "different-sha");
        earlier.Rows.Single().Source = new SourceEvidence();
        Should.Throw<InvalidOperationException>(() => ReportMerger.Merge([earlier, clean]), "legacy-row");
    }

    [Test]
    public void validation_requires_complete_consistent_source()
    {
        var valid = ValidReport();
        ReportValidator.Validate(valid, Sha).ShouldBeNull();
        valid.SchemaVersion = 1;
        ReportValidator.Validate(valid, Sha).ShouldBe("report_source_ineligible", "legacy-report-ineligible");
        valid.SchemaVersion = 2;
        valid.Rows.Single().Source = Evidence(Observe(OtherSha, 0), "clean", "notApplicable");
        ReportValidator.Validate(valid, Sha).ShouldBe("row_source_disagreement", "row-heading-disagreement");
        valid.Rows.Single().Source = Evidence(Observe(Sha, 0), "clean", "notApplicable");
        valid.Rows.Single().Line += " dirty=0";
        ReportValidator.Validate(valid, Sha).ShouldBe("duplicate_receipt_token");
        valid.Rows.Single().Line = CheckpointLine.Format(new CheckpointLineModel
        {
            Name = "CP-1", Commit = Sha, Build = "n/a", Filter = "true", Command = true,
            ExitCode = 0, Source = valid.Rows.Single().Source,
        });
        valid.Rows.Single().ExitCode = ExitCodes.FailedTests;
        ReportValidator.Validate(valid, Sha).ShouldBe("row_failed");
    }

    private string NewRun(CheckpointManifest manifest, SourceObservation source) =>
        CheckpointApp.CreateRun(manifest, new RunRequest
        {
            Commit = Sha, Branch = "fixture", KeepOutputs = true, Slots = "off",
        }, TempDir(), new CheckpointApp.Runtime { SourceCapture = _ => source });

    private static CheckpointManifest CommandManifest() => new()
    {
        Checkpoints = [new CheckpointSpec { Id = "CP-1", After = ["all"], Command = "true", EstimatedMinutes = 1 }],
    };

    private static CheckpointApp.Runtime Runtime(FakeDriver driver, Func<string, SourceObservation> capture) => new()
    {
        Driver = driver, Slots = new FixedSlotClient("off"), SourceCapture = capture,
    };

    private static ReportModel Read(string run) => JsonSerializer.Deserialize<ReportModel>(
        File.ReadAllText(Path.Combine(run, "report.json")), ReportWriter.Json)!;

    private static SourceObservation Observe(string sha, int dirty) =>
        new(sha, dirty, dirty == 0 ? Fingerprint : new string('2', 64), DateTimeOffset.UtcNow, "known");

    private static SourceEvidence Evidence(SourceObservation observation, string state, string binding) => new()
    {
        Start = observation, End = observation, State = state, BuildSource = binding,
    };

    private static ReportModel ValidReport()
    {
        var source = Evidence(Observe(Sha, 0), "clean", "notApplicable");
        var row = new ReportRow
        {
            Id = "CP-1", Command = "true", State = "green", ExitCode = 0, Source = source,
        };
        row.Line = CheckpointLine.Format(new CheckpointLineModel
        {
            Name = row.Id, Commit = Sha, Build = "n/a", Filter = "true", Command = true,
            ExitCode = 0, Source = source,
        });
        return new ReportModel
        {
            SchemaVersion = 2, Source = source, Commit = Sha, RunId = "fixture",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2), EndedAt = DateTimeOffset.UtcNow,
            Rows = [row],
        };
    }
}
