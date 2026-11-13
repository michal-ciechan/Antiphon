using System.Diagnostics;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[Category("Slow")]
[Category("C835")]
[ParallelLimiter<ProcessSpawnLimit>]
[SharedCheckoutChild]
public sealed class CheckpointSourceExecutionTests : CheckpointTestBase
{
    private static readonly string Sha = new('a', 40);
    private static readonly string OtherSha = new('b', 40);
    private static readonly string Fingerprint = new('1', 64);

    [Test]
    public async Task host_written_entries_under_the_row_output_are_adopted_and_cleaned_on_green()
    {
        var source = Observe(Sha, 0);
        var repo = TempDir();
        CheckpointFixtureProcess.Git(repo, "init", "-b", "card1368");
        Directory.CreateDirectory(Path.Combine(repo, "sample"));
        File.WriteAllText(Path.Combine(repo, "sample", "sample.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var output = Path.Combine(repo, "sample", "bin-c1368");
        var html = Path.Combine(output, "TestResults", "sample_report.html");
        var manifest = new CheckpointManifest
        {
            Builds = [new BuildSpec { Id = "bin-c1368", Project = "sample", OutputPath = "bin-c1368/" }],
            Checkpoints = [new CheckpointSpec
            {
                Id = "CP-1", After = ["all"], Build = "bin-c1368",
                Filter = "/*/*/ExampleSurfaceTests/*", MinExecuted = 1,
                Expect = ["ExampleSurfaceTests"], EstimatedMinutes = 1,
            }],
        };
        var run = CheckpointApp.CreateRun(manifest, new RunRequest
        {
            Commit = Sha, Branch = "fixture", KeepOutputs = false, Slots = "off",
        }, repo, new CheckpointApp.Runtime { SourceCapture = _ => source });
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request),
                ("Antiphon.Tests.ExampleSurfaceTests.case", "Passed"));
            Directory.CreateDirectory(Path.GetDirectoryName(html)!);
            File.WriteAllText(html, "<html>host</html>");
            return Task.FromResult(new DriverResult(0, "", "")
            {
                Exited = CheckpointFixtures.AfterFileClock(html),
            });
        });

        var exit = await CheckpointApp.ExecuteAsync(run, CancellationToken.None, Runtime(driver, _ => source));
        var report = Read(run);
        // Preserve the nested receipt in the outer TRX before the fixture removes its scratch root.
        Console.WriteLine("W-1 nested receipt: " + ReportWriter.JsonText(report));
        exit.ShouldBe(0, "host-output-cleanup");
        report.Verdict.ShouldBe("GREEN");
        report.ExitCode.ShouldBe(0);
        report.CleanedOutputs.ShouldBeTrue();
        report.CleanupRefusals.ShouldBeEmpty();
        Directory.Exists(output).ShouldBeFalse();
        var evidence = Path.Combine(run, "report.json");
        (await Antiphon.Checkpoints.Program.RunAsync(["validate", "--repo-root", repo,
            "--evidence", evidence, "--expected-source-sha", Sha])).ShouldBe(0);
        var script = await ValidateFileWithScriptAsync(evidence);
        script.Exit.ShouldBe(0, script.Output);
    }

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
            report.Rows.Single().Source.State.ShouldBe(dirty ? "dirty" : "clean", "dirty-row-preserved");
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

        var sameCountAfter = new SourceObservation(Sha, 0, new string('3', 64),
            DateTimeOffset.UtcNow, "known");
        var sameCountObservations = 0;
        var sameCountRun = NewRun(CommandManifest(), before);
        (await CheckpointApp.ExecuteAsync(sameCountRun, CancellationToken.None,
            Runtime(new FakeDriver(), _ => ++sameCountObservations <= 2 ? before : sameCountAfter)))
            .ShouldBe(ExitCodes.Invalid, "driver-same-count-fingerprint-drift");
        var sameCountReport = Read(sameCountRun);
        sameCountReport.Source.Start.Commit.ShouldBe(sameCountReport.Source.End!.Commit);
        sameCountReport.Source.Start.DirtyFiles.ShouldBe(sameCountReport.Source.End.DirtyFiles);
        sameCountReport.Source.Start.Fingerprint.ShouldNotBe(sameCountReport.Source.End.Fingerprint);
        sameCountReport.Source.State.ShouldBe("changed");

        var slotRepo = TempDir();
        Directory.CreateDirectory(Path.Combine(slotRepo, "sample"));
        File.WriteAllText(Path.Combine(slotRepo, "sample", "sample.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var slotManifest = new CheckpointManifest
        {
            Builds = [new BuildSpec { Id = "bin-c835", Project = "sample", OutputPath = "bin-c835/" }],
            Checkpoints = [new CheckpointSpec
            {
                Id = "CP-1", After = ["all"], Build = "bin-c835",
                Filter = "/*/*/ExampleSurfaceTests/*", MinExecuted = 1, EstimatedMinutes = 1,
            }],
        };
        var slotRun = CheckpointApp.CreateRun(slotManifest, new RunRequest
        {
            Commit = Sha, Branch = "fixture", KeepOutputs = true, Slots = "off",
        }, slotRepo, new CheckpointApp.Runtime { SourceCapture = _ => before });
        var slotSource = before;
        var slotDriver = new FakeDriver();
        var slotClient = new DriftingSlotClient(() => slotSource = sameCountAfter);
        (await CheckpointApp.ExecuteAsync(slotRun, CancellationToken.None,
            new CheckpointApp.Runtime
            {
                Driver = slotDriver, Slots = slotClient, SourceCapture = _ => slotSource,
            })).ShouldBe(ExitCodes.Invalid, "slot-wait-fingerprint-drift");
        slotClient.Acquires.ShouldBe(1, "slot-wait-acquired-once");
        slotDriver.Calls.ShouldBeEmpty("slot-wait-no-driver");
        Read(slotRun).Source.State.ShouldBe("changed");

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
        ReportValidator.Validate(report, Sha).ShouldBe("report_failed");
        report.ExitCode.ShouldBe(ExitCodes.ExecutorCrashed);
        // Even a forged green verdict cannot make the missing source observation eligible.
        report.ExitCode = ExitCodes.Green;
        report.Verdict = "GREEN";
        ReportValidator.Validate(report, Sha).ShouldBe("report_source_ineligible");

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
    [Arguments("changed")]
    [Arguments("unknown")]
    [Timeout(30_000)]
    public async Task C1734_Failure_receipt_preserves_observed_source_history(string history)
    {
        var original = Observe(Sha, 0);
        var interrupted = history == "changed" ? Observe(OtherSha, 0) : SourceObservation.Unknown("fixture-observation-failed");
        var observations = 0;
        var run = NewRun(CommandManifest(), original);
        var exit = await CheckpointApp.ExecuteAsync(run, CancellationToken.None, new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null, Driver = new FakeDriver(), Slots = new FixedSlotClient("off"),
            SourceCapture = _ => ++observations == 1 ? interrupted : original,
            LogSinkFactory = _ => new C1734FailingFlush(), Output = TextWriter.Null,
        });
        exit.ShouldBe(ExitCodes.ExecutorCrashed);
        observations.ShouldBeGreaterThanOrEqualTo(3, "the terminal fallback observed restored source after the earlier refusal");
        var report = Read(run);
        report.Source.State.ShouldBe(history, "terminal recovery must retain the earlier observation");
        report.Source.End!.Commit.ShouldBe(Sha, "the latest observation is restored, not an invented drift");
        ReportWriter.RequiredReceiptRefusal(run, new RunStateStore().TryRead(Path.Combine(run, "state.json"))!).ShouldBeNull();
        report.ExitCode = 0; report.Verdict = "GREEN";
        ReportValidator.Validate(report, Sha).ShouldBe("report_source_ineligible");
    }

    private sealed class C1734FailingFlush : IExecutorLogSink
    {
        public Task AppendAsync(string line, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken) => Task.FromException(new IOException("C1734 terminal flush failure"));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
    public async Task validators_refuse_changed_count_tokens()
    {
        var report = CountReport();
        var line = report.Rows.Single().Line!;
        ReportValidator.Validate(report, Sha).ShouldBeNull("C# valid count control");
        var cases = new List<(string Key, string Raw)> { ("valid", ReportWriter.JsonText(report)) };
        foreach (var (token, value) in new[] { ("passed", "3"), ("failed", "0"), ("skipped", "0"), ("executed", "3") })
        foreach (var replacement in new[] { token + "=99", "", token + "=0" + value })
        {
            report.Rows.Single().Line = line.Replace(token + "=" + value, replacement, StringComparison.Ordinal);
            cases.Add((token + ":" + replacement, ReportWriter.JsonText(report)));
        }
        var root = TempDir();
        var corpus = cases.Select((item, index) => new { key = item.Key, path = Path.Combine(root, index + ".json") }).ToArray();
        for (var i = 0; i < cases.Count; i++) File.WriteAllText(corpus[i].path, cases[i].Raw);
        var records = await ValidateCorpusWithScriptAsync(root, corpus);
        records.Select(r => r.GetProperty("key").GetString()).ShouldBe(cases.Select(c => c.Key));
        for (var i = 0; i < cases.Count; i++)
        {
            var expected = i == 0 ? 0 : 2;
            var label = "preserved:B-4:" + cases[i].Key;
            records[i].GetProperty("exit").GetInt32().ShouldBe(expected, label + ": " + records[i]);
            var tool = await Antiphon.Checkpoints.Program.RunAsync(["validate", "--evidence", corpus[i].path, "--expected-source-sha", Sha]);
            tool.ShouldBe(expected, label + ":C:verdict");
            if (i > 0)
            {
                records[i].GetProperty("output").GetString()!.ShouldContain("reason=receipt_counts", Case.Sensitive);
                records[i].GetProperty("output").GetString()!.ShouldNotContain("CHECKPOINT SOURCE VALID", Case.Sensitive);
                ReportValidator.Validate(ReceiptJson.Parse<ReportModel>(cases[i].Raw, ReportWriter.Json), Sha)
                    .ShouldBe("receipt_counts", label + ":C:reason");
            }
            File.ReadAllText(corpus[i].path).ShouldBe(cases[i].Raw, label + ":input unchanged");
        }
    }

    private static async Task<JsonElement[]> ValidateCorpusWithScriptAsync(string root, object corpus, int expectedRepeat = 1)
    {
        var input = Path.Combine(root, "corpus.json");
        var output = Path.Combine(root, "results.json");
        var driver = Path.Combine(root, "validate.ps1");
        File.WriteAllText(input, JsonSerializer.Serialize(corpus));
        File.WriteAllText(driver, """
            param($Corpus, $Result, $Validator, $Sha, $ExpectedRepeat, $Progress)
            $ErrorActionPreference = 'Stop'
            $records = @(foreach ($case in (Get-Content -Raw -LiteralPath $Corpus | ConvertFrom-Json)) {
                $global:LASTEXITCODE = 99
                $text = (& $Validator -Evidence $case.path -ExpectedSourceSha $Sha -ExpectedRepeat $ExpectedRepeat *>&1 | Out-String)
                [pscustomobject]@{ key = $case.key; exit = $LASTEXITCODE; output = $text }
                [IO.File]::AppendAllText($Progress, '.')
            })
            [IO.File]::WriteAllText($Result, (ConvertTo-Json -InputObject $records -Depth 8 -EscapeHandling EscapeNonAscii))
            """);
        var progress = Path.Combine(root, "progress");
        var start = new ProcessStartInfo("pwsh") { WorkingDirectory = CheckpointFixtures.RepoRoot };
        foreach (var token in new[] { "-NoProfile", "-NonInteractive", "-File", driver, input, output,
            Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "validate-checkpoint-receipt.ps1"), Sha, expectedRepeat.ToString(), progress })
            start.ArgumentList.Add(token);
        // rc run 97150a0b g109: one fixed 30 s over the whole corpus (326 validator calls for the XML
        // parity corpus) expired on a saturated Windows host while the child was still progressing.
        // 30 s now covers pwsh start, validator load and the first case; each later case gets 10 s
        // (tens of ms when idle). A stall fails naming the case; success logs the observed timings.
        var keys = JsonSerializer.SerializeToElement(corpus).EnumerateArray().Select(item => item.GetProperty("key").GetString() ?? "").ToArray();
        var result = await CheckpointFixtureProcess.RunStepsAsync(start, progress, keys, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));
        result.Exit.ShouldBe(0, result.Stdout + result.Stderr);
        using var document = JsonDocument.Parse(File.ReadAllText(output));
        return document.RootElement.EnumerateArray().Select(record => record.Clone()).ToArray();
    }

    [Test]
    public async Task validators_agree_on_xml_character_references()
    {
        var root = TempDir();
        var template = File.ReadAllText(CheckpointRepeatFixture.WriteTemplate(root, 2, methods: ["alpha"]))
            .Replace("__NONCE__", "xml-parity", StringComparison.Ordinal)
            .Replace("failed=\"0\"", "failed=\"0\" notExecuted=\"0\"", StringComparison.Ordinal)
            .Replace("<Output>", "<Output><DebugTrace>Skipped: fixture capability unavailable</DebugTrace>", StringComparison.Ordinal)
            .Replace("testName=\"display collision\"", "testName=\"same display\"", StringComparison.Ordinal);
        var cases = new List<(string Key, string Xml, int Expected)> { ("ordinary", template, 0) };
        foreach (var (reference, value) in C1217ConditionalSkipFixture.CharacterReferences())
        foreach (var context in new[] { "text", "attribute", "reason" })
            cases.Add((context + ":" + reference, C1217ConditionalSkipFixture.WithReference(template, reference, context), value is null ? 2 : 0));

        cases.AddRange(C1217ConditionalSkipFixture.DtdCases(template));

        var corpus = cases.Select((item, index) => new { key = item.Key, path = Path.Combine(root, index + ".json") }).ToArray();
        for (var i = 0; i < cases.Count; i++)
        {
            var path = Path.Combine(root, i + ".trx");
            File.WriteAllText(path, cases[i].Xml);
            var report = CountReport();
            report.SchemaVersion = 3;
            var row = report.Rows.Single();
            row.Trx = path;
            row.Executed = row.Passed = 2;
            // Bind independently to the intact native-repeat control, never the altered input.
            var control = Path.Combine(root, "control.trx");
            File.WriteAllText(control, template);
            var repeat = RepeatEvidenceValidator.Validate(TrxReport.Parse(control), 2, "xml-parity", 1, []);
            repeat.Ok.ShouldBeTrue(repeat.Error);
            row.Repeat = repeat.Evidence;
            row.Line = row.Line!.Replace("executed=3", "executed=2", StringComparison.Ordinal)
                .Replace("passed=3", "passed=2", StringComparison.Ordinal) + " repeat=2 repetitions=2/2 hostInvocations=1";
            File.WriteAllText(corpus[i].path, ReportWriter.JsonText(report));
        }
        // One PowerShell host for the corpus, under the per-case deadline of ValidateCorpusWithScriptAsync.
        var records = await ValidateCorpusWithScriptAsync(root, corpus, 2);
        records.Select(record => record.GetProperty("key").GetString()).ShouldBe(cases.Select(item => item.Key));
        var disagreements = new List<string>();
        for (var i = 0; i < cases.Count; i++)
        {
            var label = "preserved:R2-1:parity:" + cases[i].Key;
            records[i].GetProperty("exit").GetInt32().ShouldBe(cases[i].Expected, label + ":P:" + records[i]);
            var path = Path.Combine(root, i + ".trx");
            StrictTrxReport.ParseText(cases[i].Xml, 2).ExitCode.ShouldBe(cases[i].Expected, label + ":strict-text");
            TrxReport.ParseStrict(path, 2).ExitCode.ShouldBe(cases[i].Expected, label + ":strict-file");
            var tool = await Antiphon.Checkpoints.Program.RunAsync(["validate", "--evidence", corpus[i].path,
                "--expected-source-sha", Sha, "--expected-repeat", "2"]);
            if (tool != cases[i].Expected) disagreements.Add(label + ":C expected=" + cases[i].Expected + " actual=" + tool);
            File.ReadAllText(path).ShouldBe(cases[i].Xml, label + ":input unchanged");
        }
        disagreements.ShouldBeEmpty("preserved:R3-1:real-validator-parity");
    }

    [Test]
    public async Task validators_refuse_malformed_receipt_encoding()
    {
        var report = CountReport();
        report.Branch = "BADBYTE";
        var root = TempDir();
        var raw = ReportWriter.JsonText(report);
        var cases = new List<(string Key, byte[] Bytes, int Exit)>();
        foreach (var encoding in new System.Text.Encoding[]
        {
            new System.Text.UTF8Encoding(false, true), new System.Text.UTF8Encoding(true, true),
            new System.Text.UnicodeEncoding(false, true, true), new System.Text.UnicodeEncoding(true, true, true),
            new System.Text.UTF32Encoding(false, true, true), new System.Text.UTF32Encoding(true, true, true),
        })
            cases.Add((encoding.WebName + ":" + encoding.GetPreamble().Length,
                encoding.GetPreamble().Concat(encoding.GetBytes(raw)).ToArray(), 0));
        var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
        var offset = bytes.AsSpan().IndexOf("BADBYTE"u8);
        offset.ShouldBeGreaterThan(0);
        bytes[offset] = 0xff;
        cases.Add(("malformed-utf8", bytes, 2));
        var corpus = cases.Select((item, index) => new { key = item.Key, path = Path.Combine(root, index + ".json") }).ToArray();
        for (var i = 0; i < cases.Count; i++) File.WriteAllBytes(corpus[i].path, cases[i].Bytes);
        var records = await ValidateCorpusWithScriptAsync(root, corpus);
        records.Select(r => r.GetProperty("key").GetString()).ShouldBe(cases.Select(c => c.Key));
        for (var i = 0; i < cases.Count; i++)
        {
            var tool = await Antiphon.Checkpoints.Program.RunAsync(["validate", "--evidence", corpus[i].path, "--expected-source-sha", Sha]);
            tool.ShouldBe(cases[i].Exit, "preserved:B-1:C:" + cases[i].Key);
            records[i].GetProperty("exit").GetInt32().ShouldBe(cases[i].Exit, "preserved:B-1:P:" + cases[i].Key + ":" + records[i]);
            if (cases[i].Exit != 0)
                records[i].GetProperty("output").GetString()!.ShouldNotContain("CHECKPOINT SOURCE VALID", Case.Sensitive);
            File.ReadAllBytes(corpus[i].path).ShouldBe(cases[i].Bytes, "validation is read-only");
        }
    }

    private static ReportModel CountReport()
    {
        var report = ValidReport();
        report.MarkdownSha256 = new string('c', 64);
        report.Source.BuildSource = "verified";
        var row = report.Rows.Single();
        row.Command = null;
        row.Executed = row.Passed = 3;
        row.Failed = row.Skipped = 0;
        row.Line = $"CHECKPOINT CP-1 commit={Sha} build=ok executed=3 passed=3 failed=0 skipped=0 dirty=0 source={Sha} sourceState=clean buildSource=verified";
        return report;
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
        var legacyScript = await ValidateWithScriptAsync(valid);
        legacyScript.Output.ShouldContain("reason=legacy_report", Case.Sensitive, "script-tool-legacy-parity");
        legacyScript.Exit.ShouldBe(2, "script-tool-legacy-parity");
        valid.SchemaVersion = 2;
        valid.Rows.Single().Source = Evidence(
            new SourceObservation(Sha, 0, new string('f', 64), DateTimeOffset.UtcNow, "known"),
            "clean", "notApplicable");
        valid.Rows.Single().Line = CheckpointLine.Format(new CheckpointLineModel
        {
            Name = "CP-1", Commit = Sha, Build = "n/a", Filter = "true", Command = true,
            ExitCode = 0, Source = valid.Rows.Single().Source,
        });
        ReportValidator.IsSourceEligible(valid.Rows.Single().Source, Sha).ShouldBeTrue("fingerprint-row-valid-alone");
        ReportValidator.Validate(valid, Sha).ShouldBe("row_source_disagreement", "fingerprint-row-heading-disagreement");
        var fingerprintScript = await ValidateWithScriptAsync(valid);
        fingerprintScript.Output.ShouldContain("reason=row_heading_disagreement", Case.Sensitive,
            "script-fingerprint-row-heading-parity");
        fingerprintScript.Exit.ShouldBe(2, "script-fingerprint-row-heading-parity");
        valid.Rows.Single().Source = Evidence(Observe(Sha, 0), "clean", "verified");
        valid.Rows.Single().Command = null;
        valid.Rows.Single().Executed = 1;
        valid.Rows.Single().Passed = 1;
        valid.Rows.Single().Failed = 0;
        valid.Rows.Single().Skipped = 0;
        valid.Source = Evidence(Observe(Sha, 0), "clean", "notApplicable");
        valid.Rows.Single().Line = CheckpointLine.Format(new CheckpointLineModel
        {
            Name = "CP-1", Commit = Sha, Build = "built", Filter = "true", Command = false,
            ExitCode = 0, Executed = "1", Passed = "1", Source = valid.Rows.Single().Source,
        });
        ReportValidator.IsSourceEligible(valid.Rows.Single().Source, Sha).ShouldBeTrue("build-row-valid-alone");
        ReportValidator.Validate(valid, Sha).ShouldBe("row_source_disagreement", "build-row-heading-disagreement");
        var bindingScript = await ValidateWithScriptAsync(valid);
        bindingScript.Output.ShouldContain("reason=row_heading_disagreement", Case.Sensitive,
            "script-build-row-heading-parity");
        bindingScript.Exit.ShouldBe(2, "script-build-row-heading-parity");
        valid.Source.BuildSource = "verified";
        valid.Rows.Single().Line = CheckpointLine.Format(new CheckpointLineModel
        {
            Name = "CP-1", Commit = Sha, Build = "built", Filter = "true", Command = false,
            ExitCode = 0, Executed = "1", Passed = "1", Source = valid.Rows.Single().Source,
        });
        valid.Rows.Single().Line += " dirty=0";
        ReportValidator.Validate(valid, Sha).ShouldBe("duplicate_receipt_token", "tool-duplicate-receipt-token");
        valid.Rows.Single().Line = CheckpointLine.Format(new CheckpointLineModel
        {
            Name = "CP-1", Commit = Sha, Build = "built", Filter = "true", Command = false,
            ExitCode = 0, Executed = "1", Passed = "1", Source = valid.Rows.Single().Source,
        });
        valid.Rows.Single().ExitCode = ExitCodes.FailedTests;
        ReportValidator.Validate(valid, Sha).ShouldBe("row_failed", "tool-row-failed");
        var failedScript = await ValidateWithScriptAsync(valid);
        failedScript.Exit.ShouldBe(2, "script-tool-failed-verdict-parity: " + failedScript.Output);
        failedScript.Output.ShouldContain("reason=row_failed", Case.Sensitive, "script-tool-failed-verdict-parity");

        async Task RefusesAsync(string label, string toolReason, string scriptReason,
            Action<ReportModel> change)
        {
            var report = ValidReport();
            change(report);
            ReportValidator.Validate(report, Sha).ShouldBe(toolReason, label + " tool");
            var script = await ValidateWithScriptAsync(report);
            script.Exit.ShouldBe(2, label + " script: " + script.Output);
            script.Output.ShouldContain("reason=" + scriptReason, Case.Sensitive, label + " script reason");
        }
        await RefusesAsync("report-heading-source", "report_source_ineligible", "source_ineligible",
            report => report.Source = Evidence(Observe(Sha, 0), "dirty", "notApplicable"));
        var wrongReportCommit = ValidReport();
        wrongReportCommit.Commit = OtherSha;
        ReportValidator.Validate(wrongReportCommit, Sha).ShouldBe("report_source_ineligible", "tool-report-commit");
        await RefusesAsync("row-source-ineligible", "row_source_disagreement", "row_source_ineligible",
            report => report.Rows.Single().Source = Evidence(Observe(Sha, 0), "dirty", "notApplicable"));
        await RefusesAsync("row-receipt-missing", "row_receipt_missing", "receipt_disagreement",
            report => report.Rows.Single().Line = null);
        await RefusesAsync("row-receipt-source", "receipt_source_disagreement", "receipt_disagreement",
            report => report.Rows.Single().Line = report.Rows.Single().Line!
                .Replace("source=" + Sha, "source=" + OtherSha, StringComparison.Ordinal));

        var missing = ValidReport();
        ReportValidator.Validate(missing, Sha, ["CP-2"]).ShouldBe("selected_rows_missing", "tool-selected-rows-missing");
        var missingScript = await ValidateWithScriptAsync(missing, "CP-2");
        missingScript.Exit.ShouldBe(2, "script-selected-rows-missing: " + missingScript.Output);
        missingScript.Output.ShouldContain("reason=selected_rows_missing", Case.Sensitive, "script-selected-rows-missing");
        var empty = ValidReport();
        empty.Rows = [];
        ReportValidator.Validate(empty, Sha).ShouldBe("selected_rows_missing", "tool-empty-rows-missing");
        var emptyScript = await ValidateWithScriptAsync(empty);
        emptyScript.Output.ShouldContain("reason=selected_rows_missing", Case.Sensitive, "script-empty-rows-missing");
        emptyScript.Exit.ShouldBe(2, "script-empty-rows-missing");

        var counts = ValidReport();
        counts.Rows.Single().Command = null;
        counts.Rows.Single().Executed = 1;
        counts.Rows.Single().Passed = 1;
        counts.Rows.Single().Failed = 0;
        counts.Rows.Single().Skipped = 0;
        counts.Source.BuildSource = "verified";
        counts.Rows.Single().Source.BuildSource = "verified";
        counts.Rows.Single().Line = CheckpointLine.Format(new CheckpointLineModel
        {
            Name = "CP-1", Commit = Sha, Build = "built", Filter = "true", Command = false,
            ExitCode = 0, Executed = "1", Passed = "1", Source = counts.Rows.Single().Source,
        });
        ReportValidator.Validate(counts, Sha).ShouldBeNull("tool-count-control");
        counts.Rows.Single().Line = counts.Rows.Single().Line!.Replace("executed=1", "executed=2", StringComparison.Ordinal);
        ReportValidator.Validate(counts, Sha).ShouldBe("receipt_counts", "tool-receipt-count-disagreement");
        var countScript = await ValidateWithScriptAsync(counts);
        countScript.Exit.ShouldBe(2, "script-receipt-count-disagreement: " + countScript.Output);
        countScript.Output.ShouldContain("reason=receipt_counts", Case.Sensitive, "script-receipt-count-disagreement");

        var badCounts = ValidReport();
        badCounts.Rows.Single().Command = null;
        badCounts.Rows.Single().Executed = 1;
        badCounts.Rows.Single().Passed = 0;
        badCounts.Rows.Single().Failed = 1;
        ReportValidator.Validate(badCounts, Sha).ShouldBe("row_failed", "tool-tunit-failed-counts");
        var badCountsScript = await ValidateWithScriptAsync(badCounts);
        badCountsScript.Output.ShouldContain("reason=row_failed", Case.Sensitive, "script-tunit-failed-counts");
        badCountsScript.Exit.ShouldBe(2, "script-tunit-failed-counts");

        void SourceRefuses(string label, Action<SourceEvidence> change)
        {
            var evidence = Evidence(Observe(Sha, 0), "clean", "verified");
            change(evidence);
            ReportValidator.IsSourceEligible(evidence, Sha).ShouldBeFalse(label);
        }
        ReportValidator.IsSourceEligible(null, Sha).ShouldBeFalse("tool-null-source");
        ReportValidator.IsSourceEligible(Evidence(Observe("bad", 0), "clean", "verified"), "bad")
            .ShouldBeFalse("tool-invalid-expected-sha");
        SourceRefuses("tool-source-version", source => source.Version = 0);
        SourceRefuses("tool-source-end-missing", source => source.End = null);
        SourceRefuses("tool-source-state", source => source.State = "dirty");
        SourceRefuses("tool-source-build-binding", source => source.BuildSource = "unknown");
        SourceRefuses("tool-source-start-capture", source => source.Start = source.Start with { CaptureStatus = "unknown" });
        SourceRefuses("tool-source-end-capture", source => source.End = source.End! with { CaptureStatus = "unknown" });
        SourceRefuses("tool-source-start-commit", source => source.Start = source.Start with { Commit = OtherSha });
        SourceRefuses("tool-source-end-commit", source => source.End = source.End! with { Commit = OtherSha });
        SourceRefuses("tool-source-start-dirty", source => source.Start = source.Start with { DirtyFiles = 1 });
        SourceRefuses("tool-source-end-dirty", source => source.End = source.End! with { DirtyFiles = 1 });
        SourceRefuses("tool-source-fingerprint-shape", source => source.Start = source.Start with { Fingerprint = "bad" });
        SourceRefuses("tool-source-fingerprint-match", source => source.End = source.End! with { Fingerprint = new string('f', 64) });
    }

    [Test]
    public async Task command_only_selection_beside_built_row_receipt_passes_both_validators()
    {
        var source = Observe(Sha, 0);
        var repo = TempDir();
        Directory.CreateDirectory(Path.Combine(repo, "sample"));
        File.WriteAllText(Path.Combine(repo, "sample", "sample.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var manifest = new CheckpointManifest
        {
            Builds = [new BuildSpec { Id = "bin-c1240", Project = "sample", OutputPath = "bin-c1240/" }],
            Checkpoints =
            [
                new CheckpointSpec { Id = "CP-1", After = ["all"], Command = "true", EstimatedMinutes = 1 },
                new CheckpointSpec
                {
                    Id = "CP-2", After = ["all"], Build = "bin-c1240", Filter = "/*/*/ExampleSurfaceTests/*",
                    MinExecuted = 1, Expect = ["ExampleSurfaceTests"], EstimatedMinutes = 1,
                },
            ],
        };
        string Start(List<string> rows) => CheckpointApp.CreateRun(manifest, new RunRequest
        {
            Commit = Sha, Branch = "fixture", KeepOutputs = true, Slots = "off", Rows = rows,
        }, repo, new CheckpointApp.Runtime { SourceCapture = _ => source });
        string? Required(string run) =>
            ReportWriter.RequiredReceiptRefusal(run, new RunStateStore().TryRead(Path.Combine(run, "state.json"))!);
        async Task<int> ToolValidateAsync(string evidence) => await Antiphon.Checkpoints.Program.RunAsync(
            ["validate", "--repo-root", repo, "--evidence", evidence, "--expected-source-sha", Sha]);

        var commandRun = Start(["CP-1"]);
        var commandDriver = new FakeDriver();
        (await CheckpointApp.ExecuteAsync(commandRun, CancellationToken.None, Runtime(commandDriver, _ => source)))
            .ShouldBe(0, "command-only-run");
        commandDriver.Calls.ShouldNotContain(call => CheckpointFixtures.IsBuild(call), "unselected-build-not-run");
        var commandReport = Read(commandRun);
        commandReport.Builds.Single().State.ShouldBe("unused");
        commandReport.OutputNames.ShouldBeEmpty("unused-build-names-no-output");
        File.ReadAllText(Path.Combine(commandRun, "report.md")).ShouldContain("outputs: none");
        Required(commandRun).ShouldBeNull("command-only-receipt-readable");
        var commandEvidence = Path.Combine(commandRun, "report.json");
        (await ToolValidateAsync(commandEvidence)).ShouldBe(0, "tool-validates-command-only");
        var commandScript = await ValidateFileWithScriptAsync(commandEvidence);
        commandScript.Exit.ShouldBe(0, "script-validates-command-only: " + commandScript.Output);

        var builtRun = Start(["CP-1", "CP-2"]);
        var builtDriver = new FakeDriver();
        builtDriver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request),
                ("Antiphon.Tests.ExampleSurfaceTests.case", "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        (await CheckpointApp.ExecuteAsync(builtRun, CancellationToken.None, Runtime(builtDriver, _ => source)))
            .ShouldBe(0, "two-row-run");
        var builtReport = Read(builtRun);
        builtReport.OutputNames.ShouldBe(["bin-c1240/"]);
        builtReport.OutputPaths.ShouldNotBeEmpty("built-row-records-output-paths");
        Required(builtRun).ShouldBeNull("two-row-receipt-readable");
        var builtEvidence = Path.Combine(builtRun, "report.json");
        (await ToolValidateAsync(builtEvidence)).ShouldBe(0, "tool-validates-two-rows");
        var builtScript = await ValidateFileWithScriptAsync(builtEvidence);
        builtScript.Exit.ShouldBe(0, "script-validates-two-rows: " + builtScript.Output);

        builtReport.OutputPaths = [];
        File.WriteAllText(builtEvidence, ReportWriter.JsonText(builtReport));
        Required(builtRun).ShouldBe("receipt-unreadable: JsonException: receipt-shape: outputPaths required for outputNames",
            "declared-outputs-without-paths-still-refused");
        (await ToolValidateAsync(builtEvidence)).ShouldBe(ExitCodes.Invalid, "tool-refuses-declared-outputs-without-paths");
    }

    [Test]
    public async Task cleanup_refusals_disqualify_green_rows_in_both_validators()
    {
        foreach (var selection in new string?[] { null, "CP-1" })
        {
            var report = ValidReport();
            ReportValidator.Validate(report, Sha).ShouldBeNull();
            (await ValidateWithScriptAsync(report, selection)).Exit.ShouldBe(0);
            report.CleanupRefusals = ["output-ledger-mismatch: late-user.txt"];
            ReportValidator.Validate(report, Sha, selection is null ? null : [selection]).ShouldBe("cleanup_refused");
            var refused = await ValidateWithScriptAsync(report, selection);
            refused.Exit.ShouldBe(2, refused.Output);
            refused.Output.ShouldContain("reason=cleanup_refused");
        }
    }

    // CARD-1570 S1 W-1: output kept because the run is red is evidence, not a cleanup refusal.
    [Test]
    public async Task red_run_kept_outputs_are_not_recorded_as_cleanup_refusals()
    {
        var (repo, output, run, driver, source) = RedRunFixture();

        var exit = await CheckpointApp.ExecuteAsync(run, CancellationToken.None, Runtime(driver, _ => source));
        var report = Read(run);
        Console.WriteLine("C1570 W-1 receipt: " + ReportWriter.JsonText(report));
        exit.ShouldBe(1, "red-row-run");
        report.ExitCode.ShouldBe(1);
        report.Verdict.ShouldBe("RED");
        report.Rows.Single(row => row.Id == "CP-1").State.ShouldBe("green");
        report.Rows.Single(row => row.Id == "CP-2").State.ShouldNotBe("green");
        report.CleanupRefusals.ShouldBeEmpty();
        report.CleanedOutputs.ShouldBeFalse();
        Directory.Exists(output).ShouldBeTrue("kept-because-red");
        File.ReadAllText(Path.Combine(run, "report.md")).ShouldNotContain("refused: still-present");
        var evidence = Path.Combine(run, "report.json");
        foreach (var selection in new string?[] { null, "CP-1" })
        {
            ReportValidator.Validate(report, Sha, selection is null ? null : [selection]).ShouldBe("report_failed");
            var script = await ValidateFileWithScriptAsync(evidence, selection);
            script.Exit.ShouldBe(2, script.Output);
            script.Output.ShouldContain("reason=report_failed");
        }
    }

    // CARD-1570 S1 W-1b: a red run still records output it did not keep on purpose.
    [Test]
    public async Task red_run_still_refuses_output_it_did_not_create()
    {
        var (repo, output, run, driver, source) = RedRunFixture();
        var foreign = Path.Combine(repo, "other", "bin-c1570");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "foreign.txt"), "not this run");

        var exit = await CheckpointApp.ExecuteAsync(run, CancellationToken.None, Runtime(driver, _ => source));
        var report = Read(run);
        Console.WriteLine("C1570 W-1b receipt: " + ReportWriter.JsonText(report));
        exit.ShouldBe(1, "red-row-run");
        report.CleanupRefusals.ShouldBe(["still-present"]);
        report.CleanedOutputs.ShouldBeFalse();
        Directory.Exists(output).ShouldBeTrue("kept-because-red");
        Directory.Exists(foreign).ShouldBeTrue();
        var evidence = Path.Combine(run, "report.json");
        foreach (var selection in new string?[] { null, "CP-1" })
        {
            ReportValidator.Validate(report, Sha, selection is null ? null : [selection]).ShouldBe("cleanup_refused");
            var script = await ValidateFileWithScriptAsync(evidence, selection);
            script.Exit.ShouldBe(2, script.Output);
            script.Output.ShouldContain("reason=cleanup_refused");
        }
    }

    private (string Repo, string Output, string Run, FakeDriver Driver, SourceObservation Source) RedRunFixture()
    {
        var source = Observe(Sha, 0);
        var repo = TempDir();
        CheckpointFixtureProcess.Git(repo, "init", "-b", "card1570");
        Directory.CreateDirectory(Path.Combine(repo, "sample"));
        File.WriteAllText(Path.Combine(repo, "sample", "sample.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var output = Path.Combine(repo, "sample", "bin-c1570");
        var manifest = new CheckpointManifest
        {
            Builds = [new BuildSpec { Id = "bin-c1570", Project = "sample", OutputPath = "bin-c1570/" }],
            Checkpoints =
            [
                new CheckpointSpec
                {
                    Id = "CP-1", After = ["all"], Build = "bin-c1570",
                    Filter = "/*/*/GreenSurfaceTests/*", MinExecuted = 1,
                    Expect = ["GreenSurfaceTests"], EstimatedMinutes = 1,
                },
                new CheckpointSpec
                {
                    Id = "CP-2", After = ["all"], Build = "bin-c1570",
                    Filter = "/*/*/RedSurfaceTests/*", MinExecuted = 1,
                    Expect = ["RedSurfaceTests"], EstimatedMinutes = 1,
                },
            ],
        };
        var run = CheckpointApp.CreateRun(manifest, new RunRequest
        {
            Commit = Sha, Branch = "fixture", KeepOutputs = false, CleanOnRed = false, Slots = "off",
        }, repo, new CheckpointApp.Runtime { SourceCapture = _ => source });
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) =>
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "sample.dll"), "built");
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            var green = request.Arguments.Any(arg => arg.Contains("GreenSurfaceTests", StringComparison.Ordinal));
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), green
                ? ("Antiphon.Tests.GreenSurfaceTests.case", "Passed")
                : ("Antiphon.Tests.RedSurfaceTests.case", "Failed"));
            return Task.FromResult(new DriverResult(green ? 0 : 2, "", ""));
        });
        return (repo, output, run, driver, source);
    }

    private async Task<(int Exit, string Output)> ValidateWithScriptAsync(ReportModel report, string? rows = null)
    {
        var evidence = Path.Combine(TempDir(), "report.json");
        File.WriteAllText(evidence, ReportWriter.JsonText(report));
        return await ValidateFileWithScriptAsync(evidence, rows);
    }

    private static async Task<(int Exit, string Output)> ValidateFileWithScriptAsync(string evidence, string? rows = null)
    {
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
        if (rows is not null)
        {
            process.StartInfo.ArgumentList.Add("-Rows");
            process.StartInfo.ArgumentList.Add(rows);
        }
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

    private sealed class DriftingSlotClient(Action onAcquire) : IBuildSlotClient
    {
        public int Acquires { get; private set; }
        public Task<SlotSession> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SlotSession("off", 4));
        public Task<SlotLease> AcquireAsync(SlotSession session, string label,
            CancellationToken cancellationToken)
        {
            Acquires++;
            onAcquire();
            return Task.FromResult(new SlotLease { State = "skipped", MaxCpuCount = 4 });
        }
    }

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
