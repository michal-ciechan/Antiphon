using System.Diagnostics;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// CARD-1748: a Unit row killed at its cap (no TRX) must print journal-derived
/// completed/pending counts. Those counts are a diagnostic, never a receipt.
/// </summary>
[Category("Unit")]
public sealed class PhaseJournalKillDiagnosticTests : CheckpointTestBase
{
    public const string Diagnostic =
        "JOURNAL-DIAGNOSTIC CP-1 completed=2 pending=1 outcome-passed=1 outcome-failed=0 outcome-skipped=1 in-flight=Antiphon.Tests.Sample.left";

    [Test]
    public async Task cap_kill_prints_journal_counts_and_turns_the_phase_journal_on()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            WriteKilledJournal(request);
            return Task.FromResult(new DriverResult(134, "", "", Killed: true, TimedOut: true));
        });
        var result = await RunKilled(driver, "tests/Antiphon.Tests");
        var run = driver.Calls.Single(CheckpointFixtures.IsRun);
        run.Environment.ShouldNotBeNull();
        run.Environment!.ContainsKey("ANTIPHON_TEST_PHASE_ROOT").ShouldBeTrue();
        run.Environment["ANTIPHON_TEST_PHASE_RUN_ID"].ShouldBe("run-1748");
        run.Environment["ANTIPHON_TEST_PHASE_GROUP_ID"].ShouldBe("CP-1");
        run.Environment["ANTIPHON_TEST_PHASE_ATTEMPT"].ShouldBe("1");
        run.Environment["ANTIPHON_TEST_PHASE_SUITE"].ShouldBe("antiphon-tests");
        result.ExitCode.ShouldBe(ExitCodes.Timeout);
        result.Trx.ShouldBeNull();
        result.JournalDiagnostic.ShouldBe(Diagnostic);
        result.Output.ShouldContain(Diagnostic);
        result.Line.ShouldContain("executed=n/a");
        result.Line.ShouldContain("passed=n/a");
        result.Line.ShouldContain("failed=n/a");
        result.Line.ShouldContain("skipped=n/a");
        result.Line.ShouldContain("trx=n/a");
        result.Line.ShouldNotContain("completed=");
        result.Line.ShouldNotContain("pending=");
        var refusal = ReportValidator.Validate(new ReportModel
        {
            ExitCode = result.ExitCode,
            Verdict = "RED",
            Reason = "row-deadline-exceeded",
            Commit = new string('a', 40),
            Rows = [new ReportRow { Id = "CP-1", State = result.State, ExitCode = result.ExitCode, Line = result.Line }],
        }, new string('a', 40));
        refusal.ShouldNotBeNull();
    }

    [Test]
    public async Task abort_without_trx_prints_journal_counts_outside_the_receipt()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            WriteKilledJournal(request);
            return Task.FromResult(new DriverResult(134, "", "", Killed: true, TimedOut: false));
        });
        var result = await RunKilled(driver, "tests/Antiphon.Tests");
        result.ExitCode.ShouldBe(ExitCodes.Invalid);
        result.Trx.ShouldBeNull();
        result.JournalDiagnostic.ShouldBe(Diagnostic);
        result.Output.ShouldContain("no TRX");
        result.Output.ShouldContain(Diagnostic);
        result.Line.ShouldNotContain("executed=2");
        result.Line.ShouldNotContain("completed=");
    }

    [Test]
    public async Task other_project_timeout_does_not_print_a_journal_diagnostic()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsRun, (_, _) =>
            Task.FromResult(new DriverResult(134, "", "", Killed: true, TimedOut: true)));
        var result = await RunKilled(driver, "tests/Antiphon.Agents.Pty.Tests");
        result.ExitCode.ShouldBe(ExitCodes.Timeout);
        result.JournalDiagnostic.ShouldBeNull();
        result.Output.ShouldNotContain("JOURNAL-DIAGNOSTIC");
        var run = driver.Calls.Single(CheckpointFixtures.IsRun);
        (run.Environment?.ContainsKey("ANTIPHON_TEST_PHASE_ROOT") ?? false).ShouldBeFalse();
    }

    // The row verb (scripts/run-checkpoint.ps1's body since unit 20): a run that wrote a journal and no TRX is exit 2 with
    // the same diagnostic, and its report line keeps the no-TRX zeros.
    [Test]
    public async Task row_verb_killed_row_prints_the_same_diagnostic()
    {
        var repo = TempDir();
        Directory.CreateDirectory(Path.Combine(repo, "tests", "Antiphon.Tests"));
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            WriteKilledJournal(request);
            return Task.FromResult(new DriverResult(1, "", ""));
        });
        var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync(
        [
            "row", "--repo-root", repo, "--name", "CP-1", "--project", "tests/Antiphon.Tests", "--output-path", "bin-c1748/",
            "--filter", "/*/*/Sample/*", "--no-slot",
        ], new CheckpointApp.Runtime
        {
            Output = output, Driver = driver, Platform = new FakePlatform(),
            SourceCapture = _ => CheckpointFixtures.CleanSource(), EnvironmentLookup = _ => null,
        });
        exit.ShouldBe(ExitCodes.Invalid, output.ToString());
        var lines = output.ToString().Replace("\r\n", "\n").Split('\n');
        lines.Where(line => line.StartsWith("JOURNAL-DIAGNOSTIC", StringComparison.Ordinal)).ShouldBe(new[] { Diagnostic });
        var receipt = lines.Single(line => line.StartsWith("CHECKPOINT CP-1 commit=", StringComparison.Ordinal));
        receipt.ShouldContain(" executed=0 passed=0 failed=0 skipped=0 ");
        receipt.ShouldNotContain("completed=");
    }

    // A row started inside a journaled host clears every inherited ANTIPHON_TEST_PHASE_* name from its children; only an
    // Antiphon.Tests run gets the row's own root back.
    [Test]
    [Arguments("tests/Antiphon.Tests", true)]
    [Arguments("tests/Antiphon.Agents.Pty.Tests", false)]
    public async Task inherited_phase_names_leave_the_row_children(string project, bool journaled)
    {
        var decoy = "ANTIPHON_TEST_PHASE_C1748_DECOY_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(decoy, "outer-extra");
        try
        {
            var repo = TempDir();
            Directory.CreateDirectory(Path.Combine(repo, project));
            var factory = new CapturingFactory();
            var output = new StringWriter();
            await Antiphon.Checkpoints.Program.RunAsync(
            [
                "row", "--repo-root", repo, "--name", "CP-1", "--project", project, "--output-path", "bin-c1748/",
                "--filter", "/*/*/Sample/*", "--no-slot", "--results-root", "results",
            ], new CheckpointApp.Runtime
            {
                Output = output, Driver = new ProcessDriver(factory) { DotnetShim = "fake-dotnet" }, Platform = new FakePlatform(),
                SourceCapture = _ => CheckpointFixtures.CleanSource(), EnvironmentLookup = _ => null,
            });
            factory.Starts.Count.ShouldBe(2, output.ToString());
            foreach (var start in factory.Starts)
                start.Environment.ContainsKey(decoy).ShouldBeFalse("an inherited phase name leaves every child");
            factory.Starts[0].Environment.Keys.ShouldNotContain(key => key.StartsWith("ANTIPHON_TEST_PHASE_", StringComparison.OrdinalIgnoreCase),
                "the build gets no journal");
            var run = factory.Starts[1].Environment;
            if (journaled)
            {
                var results = Directory.GetDirectories(Path.Combine(repo, "results"), "CP-1-*").Single();
                run["ANTIPHON_TEST_PHASE_ROOT"].ShouldBe(Path.GetFullPath(Path.Combine(results, "phase")));
                run["ANTIPHON_TEST_PHASE_GROUP_ID"].ShouldBe("CP-1");
            }
            else
                run.Keys.ShouldNotContain(key => key.StartsWith("ANTIPHON_TEST_PHASE_", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable(decoy, null);
        }
    }

    private sealed class CapturingFactory : IProcessHandleFactory
    {
        public List<ProcessStartInfo> Starts { get; } = [];

        public IProcessHandle Create(ProcessStartInfo startInfo)
        {
            Starts.Add(startInfo);
            return new ImmediateHandle();
        }
    }

    private sealed class ImmediateHandle : IProcessHandle
    {
        public bool Start() => true;
        public void BeginRead(Action<string?> stdout, Action<string?> stderr) { }
        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public bool HasExited => true;
        public int ExitCode => 0;
        public void Kill(bool entireProcessTree) { }
        public void Dispose() { }
    }
    private async Task<RowRunResult> RunKilled(FakeDriver driver, string project)
    {
        var workingDirectory = TempDir();
        Directory.CreateDirectory(Path.Combine(workingDirectory, project));
        var args = BuildStep.PropertyArguments([], false).ToList();
        var layout = BuildStep.AppendSharedLayout(args, workingDirectory, project, "bin-ex/");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(layout.Owned, SafeWrite.Under(workingDirectory)));
        SafeWrite.Require(CheckpointBuildBinding.Expected(
            workingDirectory, project, "bin-ex/", args, SourceObservation.Unknown("not_observed")).Write());
        var runner = new RowRunner(driver, new FakePlatform { IsWindows = false });
        return await runner.RunAsync(new RowRequest
        {
            Name = "CP-1",
            Project = project,
            OutputPath = "bin-ex/",
            Filter = "/*/*/Sample/*",
            ResultsDirectory = Path.Combine(TempDir(), "row"),
            WorkingDirectory = workingDirectory,
            NoBuild = true,
            MinExecuted = 1,
            Commit = new string('a', 40),
            TimingRunId = "run-1748",
        }, TextWriter.Null, CancellationToken.None);
    }

    private static void WriteKilledJournal(DriverRequest request)
    {
        var root = request.Environment is not null
            && request.Environment.TryGetValue("ANTIPHON_TEST_PHASE_ROOT", out var configured)
            && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(CheckpointFixtures.ResultsDirectory(request), "phase");
        var folder = Path.Combine(root, "chunk-c1748");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "cases-4-c1748.jsonl"), """
            {"recordKind":"header","version":1}
            {"recordKind":"start","testId":"case-a","className":"Antiphon.Tests.Sample","method":"done"}
            {"recordKind":"complete","testId":"case-a","className":"Antiphon.Tests.Sample","method":"done","outcome":"Passed"}
            {"recordKind":"start","testId":"case-b","className":"Antiphon.Tests.Sample","method":"skipped"}
            {"recordKind":"complete","testId":"case-b","className":"Antiphon.Tests.Sample","method":"skipped","outcome":"Skipped"}
            {"recordKind":"start","testId":"case-c","className":"Antiphon.Tests.Sample","method":"left"}
            {"recordKind":"fixture","fixtureId":"shared","testId":null}
            {"recordKind":"comp
            """);
    }
}
