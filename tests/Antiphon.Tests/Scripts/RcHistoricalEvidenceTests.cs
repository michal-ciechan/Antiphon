using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcHistoricalEvidenceTests
{
    // CARD-1836 provisional budget: one streamed corpus, no DB/native qualification child.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-historical-evidence.ps1", "RH", name, 1,
            ["RH " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(110), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(120_000)] public Task LargeSummaryIsBounded(CancellationToken token) => Run(nameof(LargeSummaryIsBounded), token);
    [Test, Timeout(60_000)] public Task TamperAndChangingReadRefuse(CancellationToken token) => Run(nameof(TamperAndChangingReadRefuse), token);
    [Test, Timeout(60_000)] public Task DuplicateTraversalAndTornInputRefuse(CancellationToken token) => Run(nameof(DuplicateTraversalAndTornInputRefuse), token);
}
