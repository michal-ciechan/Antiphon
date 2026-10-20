using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyExecutionDiagnosticTests
{
    [Test]
    public Task D13_ErrorTerminal() => RunCaseAsync(nameof(D13_ErrorTerminal), 10,
        "D13 LF failure identity", "D13 LF failure message", "D13 LF complete failed verdict",
        "D13 CRLF failure identity", "D13 CRLF failure message", "D13 CRLF complete failed verdict");

    [Test]
    public Task D13_ExecutionCompatibility() => RunCaseAsync(nameof(D13_ExecutionCompatibility), 3,
        "D13 pass and skip unchanged", "D13 legacy Failed retained", "D13 nonterminal updates omitted");

    [Test]
    public Task D13_ExecutionGuards() => RunCaseAsync(nameof(D13_ExecutionGuards), 4,
        "D13 duplicate Error refuses", "D13 unsupported version refuses",
        "D13 missing failed UID refuses", "D13 extra failed identity refuses");

    [Test]
    public Task D13_StateClassification() => RunCaseAsync(nameof(D13_StateClassification), 6,
        "D13 timeout-only is failed",
        "D13 cancelled-only is failed",
        "D13 mixed pass and timeout keeps both",
        "D13 error quoting PassedTestNodeStateProperty stays failed",
        "D13 explanation keeps embedded exception delimiters exactly",
        "D13 unknown state is failed");

    [Test]
    public Task D13_ExplanationBoundary() => RunCaseAsync(nameof(D13_ExplanationBoundary), 22,
        Enumerable.Range(1, 11).SelectMany(id => new[]
        {
            $"D13 EB E{id:00} identity", $"D13 EB E{id:00} message"
        }).ToArray());

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-tests.ps1", "D13", caseName, expectedRows, requiredRows);
}
