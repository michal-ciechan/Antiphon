using System.Diagnostics;
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

            var repo = TempDir();
            Directory.CreateDirectory(Path.Combine(repo, "sample"));
            File.WriteAllText(Path.Combine(repo, "sample", "sample.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var built = new CheckpointManifest
            {
                Builds = [new BuildSpec { Id = "bin-c835", Project = "sample", OutputPath = "bin-c835/" }],
                Checkpoints = [new CheckpointSpec
                {
                    Id = "CP-1", After = ["all"], Build = "bin-c835",
                    Filter = "/*/*/ExampleSurfaceTests/*", MinExecuted = 1,
                    Expect = ["ExampleSurfaceTests"], EstimatedMinutes = 1,
                }],
            };
            var builtRun = CheckpointApp.CreateRun(built, new RunRequest
            {
                Commit = Sha, Branch = "fixture", KeepOutputs = true, Slots = "off",
            }, repo, new CheckpointApp.Runtime { SourceCapture = _ => source });
            var builtDriver = new FakeDriver();
            builtDriver.When(CheckpointFixtures.IsRun, (request, _) =>
            {
                CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request),
                    ("Antiphon.Tests.ExampleSurfaceTests.case", "Passed"));
                return Task.FromResult(new DriverResult(0, "", ""));
            });
            (await CheckpointApp.ExecuteAsync(builtRun, CancellationToken.None,
                Runtime(builtDriver, _ => source))).ShouldBe(0, "diagnostic-tunit-run");
            var builtReport = Read(builtRun);
            builtReport.Source.BuildSource.ShouldBe("verified");
            builtReport.Rows.Single().Source.State.ShouldBe(dirty ? "dirty" : "clean");
            builtReport.Rows.Single().Source.Start.Fingerprint.ShouldBe(source.Fingerprint);
            builtReport.Rows.Single().Line.ShouldContain("source=" + SourceEvidence.Token(source));
            File.ReadAllText(Path.Combine(builtRun, "git.txt"))
                .ShouldContain("source=" + SourceEvidence.Token(source));
            ReportValidator.Validate(builtReport, Sha).ShouldBe(dirty ? "report_source_ineligible" : null);
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

        var queuedManifest = CommandManifest();
        queuedManifest.Checkpoints[0].Serial = true;
        queuedManifest.Checkpoints.Add(new CheckpointSpec
        {
            Id = "CP-2", After = ["all"], Command = "true", Serial = true, EstimatedMinutes = 1,
        });
        var queuedSource = before;
        var queuedDriver = new FakeDriver();
        queuedDriver.When(_ => true, (_, _) =>
        {
            queuedSource = after;
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var queuedRun = NewRun(queuedManifest, before);
        (await CheckpointApp.ExecuteAsync(queuedRun, CancellationToken.None,
            Runtime(queuedDriver, _ => queuedSource))).ShouldBe(ExitCodes.Invalid);
        queuedDriver.Calls.Count.ShouldBe(1, "driver-drift-stops-next-row");
        Read(queuedRun).Source.Start.Commit.ShouldBe(Sha);

        var repo = TempDir();
        Directory.CreateDirectory(Path.Combine(repo, "sample"));
        File.WriteAllText(Path.Combine(repo, "sample", "sample.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var flaky = new CheckpointManifest
        {
            Builds = [new BuildSpec { Id = "bin-c835", Project = "sample", OutputPath = "bin-c835/" }],
            Checkpoints = [new CheckpointSpec
            {
                Id = "CP-1", After = ["all"], Build = "bin-c835", Filter = "/*/*/ExampleSurfaceTests/*",
                MinExecuted = 1, EstimatedMinutes = 1,
            }],
        };
        var rerun = CheckpointApp.CreateRun(flaky, new RunRequest
        {
            Commit = Sha, Branch = "fixture", KeepOutputs = true, Slots = "off",
            KnownFlaky = ["Antiphon.Tests.ExampleSurfaceTests.case"],
        }, repo, new CheckpointApp.Runtime { SourceCapture = _ => before });
        var rerunSource = before;
        var runCount = 0;
        var flakyDriver = new FakeDriver();
        flakyDriver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            runCount++;
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request),
                ("Antiphon.Tests.ExampleSurfaceTests.case", runCount == 1 ? "Failed" : "Passed"));
            if (runCount == 2) rerunSource = after;
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        (await CheckpointApp.ExecuteAsync(rerun, CancellationToken.None,
            Runtime(flakyDriver, _ => rerunSource))).ShouldBe(ExitCodes.Invalid,
            "rerun-cannot-certify-drift");
        runCount.ShouldBe(2, "known-flaky-rerun-reached");
        Read(rerun).Source.Start.Commit.ShouldBe(Sha, "rerun-retains-original-source");
        Read(rerun).Source.State.ShouldBe("changed");
    }

    [Test]
    public async Task strict_cli_and_reuse_require_clean_binding()
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
        var expected = CheckpointBuildBinding.Expected(root, "sample", "bin-c835/",
            ["--property:UseAppHost=false"], source);
        expected.Write();
        expected.Check().ShouldBe("verified");
        CheckpointBuildBinding.Expected(root, "sample", "bin-c835/",
            ["--property:UseAppHost=true"], source).Check().ShouldBe("mismatch", "property-mismatch-no-tests");
        CheckpointBuildBinding.Expected(root, "sample", "bin-c835/",
            ["--property:UseAppHost=false"], Observe(OtherSha, 0)).Check().ShouldBe("mismatch");
        File.Delete(expected.PathName);
        expected.Check().ShouldBe("unknown", "missing-stamp-refuses-strict-reuse");

        var cliRepo = TempDir();
        var yaml = Path.Combine(cliRepo, "manifest.yaml");
        File.WriteAllText(yaml, ManifestLoader.ToYaml(CommandManifest()));
        var launched = 0;
        var driver = new FakeDriver();
        using var output = new StringWriter();
        var runtime = new CheckpointApp.Runtime
        {
            SourceCapture = _ => source, EnvironmentLookup = _ => null, Driver = driver,
            LaunchWithOutcome = _ => { launched++; return new LaunchOutcome(LaunchKind.NotStarted); },
            Output = output,
        };
        foreach (var verb in new[] { "run", "start" })
        {
            await Should.ThrowAsync<ManifestValidationException>(() =>
                Antiphon.Checkpoints.Program.RunAsync([verb, "--repo-root", cliRepo, yaml,
                    "--expected-source-sha", OtherSha], runtime), verb + "-strict-cli-refuses-wrong-sha");
        }
        launched.ShouldBe(0, "run-and-start-refuse-before-launch");
        var rowExit = await Antiphon.Checkpoints.Program.RunAsync(["row", "--repo-root", cliRepo,
            "--name", "CP-1", "--project", "sample", "--output-path", "bin-c835/",
            "--filter", "/*/*/ExampleSurfaceTests/*", "--expected-source-sha", OtherSha], runtime);
        rowExit.ShouldBe(ExitCodes.Invalid, "row-strict-cli-refuses-wrong-sha");
        driver.Calls.ShouldBeEmpty("wrong-sha-no-driver-for-all-verbs");
        output.ToString().ShouldContain("source_mismatch", Case.Sensitive);
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

        foreach (var terminal in new[] { "build-failed", "missing-trx" })
        {
            var repo = TempDir();
            Directory.CreateDirectory(Path.Combine(repo, "sample"));
            File.WriteAllText(Path.Combine(repo, "sample", "sample.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var manifest = new CheckpointManifest
            {
                Builds = [new BuildSpec { Id = "bin-c835", Project = "sample", OutputPath = "bin-c835/" }],
                Checkpoints = [new CheckpointSpec
                {
                    Id = "CP-1", After = ["all"], Build = "bin-c835",
                    Filter = "/*/*/ExampleSurfaceTests/*", MinExecuted = 1, EstimatedMinutes = 1,
                }],
            };
            var terminalRun = CheckpointApp.CreateRun(manifest, new RunRequest
            {
                Commit = Sha, Branch = "fixture", KeepOutputs = true, Slots = "off",
            }, repo, new CheckpointApp.Runtime { SourceCapture = _ => source });
            var driver = new FakeDriver();
            if (terminal == "build-failed")
                driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(37, "", "")));
            (await CheckpointApp.ExecuteAsync(terminalRun, CancellationToken.None,
                Runtime(driver, _ => source))).ShouldBe(ExitCodes.Invalid, terminal);
            var terminalReport = Read(terminalRun);
            terminalReport.Source.State.ShouldBe("clean", terminal + "-source-remains-observed");
            terminalReport.Source.End.ShouldNotBeNull(terminal);
            terminalReport.ExitCode.ShouldBe(ExitCodes.Invalid);
            ReportValidator.Validate(terminalReport, Sha).ShouldNotBeNull(terminal);
        }
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
        earlier.Rows.Single().Source = Evidence(
            new SourceObservation(Sha, 0, new string('f', 64), DateTimeOffset.UtcNow, "known"),
            "clean", "notApplicable");
        Should.Throw<InvalidOperationException>(() => ReportMerger.Merge([earlier, clean]),
            "different-clean-fingerprint");
        earlier.Rows.Single().Source = Evidence(Observe(Sha, 0), "clean", "unknown");
        Should.Throw<InvalidOperationException>(() => ReportMerger.Merge([earlier, clean]),
            "incompatible-build-binding");
        earlier.Rows.Single().Source = new SourceEvidence();
        Should.Throw<InvalidOperationException>(() => ReportMerger.Merge([earlier, clean]), "legacy-row");
    }

    [Test]
    public async Task validation_requires_complete_consistent_source()
    {
        var valid = ValidReport();
        async Task CheckScriptAsync(int expected, string label)
        {
            var result = await ValidateWithScriptAsync(valid);
            result.Exit.ShouldBe(expected, label + ": " + result.Output);
        }
        ReportValidator.Validate(valid, Sha).ShouldBeNull();
        await CheckScriptAsync(0, "script-tool-valid-parity");
        valid.SchemaVersion = 1;
        ReportValidator.Validate(valid, Sha).ShouldBe("report_source_ineligible", "legacy-report-ineligible");
        await CheckScriptAsync(2, "script-tool-legacy-parity");
        valid.SchemaVersion = 2;
        valid.Rows.Single().Source = Evidence(Observe(OtherSha, 0), "clean", "notApplicable");
        ReportValidator.Validate(valid, Sha).ShouldBe("row_source_disagreement", "row-heading-disagreement");
        await CheckScriptAsync(2, "script-tool-row-heading-parity");
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
        await CheckScriptAsync(2, "script-tool-failed-verdict-parity");
    }

    private async Task<(int Exit, string Output)> ValidateWithScriptAsync(ReportModel report)
    {
        var evidence = Path.Combine(TempDir(), "report.json");
        File.WriteAllText(evidence, ReportWriter.JsonText(report));
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = CheckpointFixtures.RepoRoot,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var token in new[] { "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "validate-checkpoint-receipt.ps1"),
            "-Evidence", evidence, "-ExpectedSourceSha", Sha })
            process.StartInfo.ArgumentList.Add(token);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
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
