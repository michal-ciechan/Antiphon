using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0585 V-5..V-14. Each method runs the matching <c>Test-C585_*</c> case of
/// <c>scripts/test-run-checkpoint.ps1</c> — an offline harness whose shim stands in for
/// <c>dotnet</c> and copies a fixture TRX into the run's <c>--results-directory</c>, so no build
/// and no test ever runs here. Exit 0 plus the case's named PASS inventory is the verdict.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunCheckpointScriptTests
{
    [Test]
    public Task C585_Green() => RunCaseAsync("C585_Green", 5,
        "C585 Green exit code 0",
        "C585 Green line reports build=ok and executed=3 passed=3 failed=0 skipped=0",
        "C585 Green line names the fresh TRX it parsed",
        "C585 Green prints the executed Class.Method roster",
        "C585 Green trailer names exit code 0");

    [Test]
    public Task C585_Failures() => RunCaseAsync("C585_Failures", 3,
        "C585 Failures exit code 1",
        "C585 Failures line reports failed=1",
        "C585 Failures names the class-qualified method, not the display name");

    [Test]
    public Task C585_ZeroExecuted() => RunCaseAsync("C585_ZeroExecuted", 3,
        "C585 ZeroExecuted exit code 3",
        "C585 ZeroExecuted line reports executed=0",
        "C585 ZeroExecuted prints no roster lines");

    [Test]
    public Task C585_RosterMiss() => RunCaseAsync("C585_RosterMiss", 5,
        "C585 RosterMiss exit code 3",
        "C585 RosterMiss names the token that matched nothing",
        "C585 RosterMiss control a matching -Expect token is green",
        "C585 RosterMiss a comma-separated -Expect value splits into tokens",
        "C585 RosterMiss one bad token in a comma-separated value is still red");

    [Test]
    public Task C585_BadOutputPath() => RunCaseAsync("C585_BadOutputPath", 10,
        @"C585 BadOutputPath 'bin-x\' exit code 2",
        @"C585 BadOutputPath 'bin-x\' message names the forward slash rule",
        @"C585 BadOutputPath 'bin-x\' invokes no dotnet",
        "C585 BadOutputPath 'bin-x/ ' exit code 2",
        "C585 BadOutputPath 'x/' exit code 2",
        "C585 BadOutputPath control bin-c585h/ is accepted");

    [Test]
    public Task C585_BuildFailed() => RunCaseAsync("C585_BuildFailed", 3,
        "C585 BuildFailed exit code 2",
        "C585 BuildFailed line reports build=failed",
        "C585 BuildFailed never runs the tests");

    [Test]
    public Task C585_FreshResultsDir() => RunCaseAsync("C585_FreshResultsDir", 4,
        "C585 FreshResultsDir exit code 2",
        "C585 FreshResultsDir refuses a pre-existing results directory",
        "C585 FreshResultsDir invokes no dotnet",
        "C585 FreshResultsDir a different stamp succeeds");

    [Test]
    public Task C585_NoBuild() => RunCaseAsync("C585_NoBuild", 3,
        "C585 NoBuild never builds",
        "C585 NoBuild line reports build=reused",
        "C585 NoBuild exit code 0");

    [Test]
    public Task C585_LineFormat() => RunCaseAsync("C585_LineFormat", 2,
        "C585 LineFormat first line is the pinned CHECKPOINT report line",
        "C585 LineFormat last line is the exit-code trailer");

    [Test]
    public Task C585_AsciiOnly() => RunCaseAsync("C585_AsciiOnly", 2,
        "C585 AsciiOnly run-checkpoint.ps1 is ASCII-only",
        "C585 AsciiOnly the harness is ASCII-only");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-run-checkpoint.ps1", "C585", caseName, expectedRows, requiredRows);
}
