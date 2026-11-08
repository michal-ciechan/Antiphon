using System.Diagnostics;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// CARD-1748: a Unit row killed at its cap (no TRX) must print journal-derived
/// completed/pending counts. Those counts are a diagnostic, never a receipt.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
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

    [Test]
    [Timeout(180_000)]
    public async Task script_killed_row_prints_the_same_diagnostic()
    {
        var results = Path.Combine(TempDir(), "c1748-script");
        Directory.CreateDirectory(results);
        var psi = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = CheckpointFixtures.RepoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var token in new[]
        {
            "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "test-run-checkpoint.ps1"),
            "-Case", "C1748_KilledRowPrintsJournalCounts",
            "-ResultsDirectory", results,
        })
            psi.ArgumentList.Add(token);
        StripPhaseEnvironment(psi);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var done = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        await process.WaitForExitAsync(done.Token);
        var text = await stdout + await stderr;
        process.ExitCode.ShouldBe(0, text);
        text.ShouldContain(Diagnostic);
    }

    [Test]
    [Timeout(180_000)]
    public async Task inherited_phase_root_uses_the_row_and_leaves_the_decoy_empty()
    {
        var results = Path.Combine(TempDir(), "c1748-decoy");
        Directory.CreateDirectory(results);
        var psi = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = CheckpointFixtures.RepoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var token in new[]
        {
            "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "test-run-checkpoint.ps1"),
            "-Case", "C1748_InheritedPhaseRootUsesTheRowRoot",
            "-ResultsDirectory", results,
        })
            psi.ArgumentList.Add(token);
        StripPhaseEnvironment(psi);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var done = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        await process.WaitForExitAsync(done.Token);
        var text = await stdout + await stderr;
        process.ExitCode.ShouldBe(0, text);
        text.ShouldContain(Diagnostic);
        text.ShouldContain("PASS C1748 inherited root leaves the decoy empty");
        text.ShouldContain("PASS C1748 journal is under the row phase root");
    }

    private static void StripPhaseEnvironment(ProcessStartInfo psi)
    {
        var keys = new List<string>();
        foreach (var key in psi.Environment.Keys)
        {
            if (key.StartsWith("ANTIPHON_TEST_PHASE_", StringComparison.OrdinalIgnoreCase))
                keys.Add(key);
        }

        foreach (var key in keys)
            psi.Environment.Remove(key);
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
