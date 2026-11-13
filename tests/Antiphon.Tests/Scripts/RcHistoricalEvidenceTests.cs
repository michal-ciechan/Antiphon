using Antiphon.Tests.TestHelpers;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcHistoricalEvidenceTests
{
    // CARD-1836 provisional budget: one streamed corpus, no DB/native qualification child.
    private static async Task Run(string name, CancellationToken token)
    {
        const string harness = "test-rc-historical-evidence.ps1";
        var result = await ScriptHarnessProcess.RunAsync(harness, "RH", name,
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", harness),
            new ScriptHarnessOptions(TimeSpan.FromSeconds(name == nameof(LargeSummaryIsBounded) ? 110 : 45),
                TimeSpan.FromSeconds(10)), token);
        var output = result.Stdout + result.Stderr;
        result.ExitCode.ShouldBe(0, output);
        output.ShouldContain("PASS RH " + name + ".contract", Case.Sensitive, output);
        output.ShouldContain("C487: 1 passed, 0 failed, 1 rows", Case.Sensitive, output);
        output.ShouldContain("C487 HARNESS EXIT CODE: 0", Case.Sensitive, output);
        output.Split('\n').ShouldNotContain(line => line.StartsWith("FAIL ", StringComparison.Ordinal), output);
        foreach (var line in output.Split('\n').Where(line => line.StartsWith("RH metrics ", StringComparison.Ordinal)))
            Console.WriteLine(line);
    }

    [Test, Timeout(120_000)] public Task LargeSummaryIsBounded(CancellationToken token) => Run(nameof(LargeSummaryIsBounded), token);
    [Test, Timeout(60_000)] public Task TamperAndChangingReadRefuse(CancellationToken token) => Run(nameof(TamperAndChangingReadRefuse), token);
    [Test, Timeout(60_000)] public Task DuplicateTraversalAndTornInputRefuse(CancellationToken token) => Run(nameof(DuplicateTraversalAndTornInputRefuse), token);
}
