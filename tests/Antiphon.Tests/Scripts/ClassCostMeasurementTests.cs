using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ClassCostMeasurementTests
{
    // Provisional harness budget, CARD-1842; no DB, provider, or native child.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-class-cost-measurement.ps1", "CM", name, 1,
            ["CM " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(120_000)] public Task RefreshKeepsRowSource(CancellationToken token) => Run(nameof(RefreshKeepsRowSource), token);
    [Test, Timeout(120_000)] public Task RefreshRequiresHostOsAndRun(CancellationToken token) => Run(nameof(RefreshRequiresHostOsAndRun), token);
    [Test, Timeout(120_000)] public Task ForeignOsDoesNotReplaceSeconds(CancellationToken token) => Run(nameof(ForeignOsDoesNotReplaceSeconds), token);
    [Test, Timeout(120_000)] public Task PackerStillReadsPrimarySeconds(CancellationToken token) => Run(nameof(PackerStillReadsPrimarySeconds), token);
}
