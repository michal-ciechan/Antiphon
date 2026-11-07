using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// scripts/lib/NightlyCore.cs (compiled per-test nightly work) returns exactly what the PowerShell fallback
// returns: census, plan, discovery index view, TRX/diagnostic cross-check, the evidence JSON writer, the pinned
// diagnostic record parser (on the captured C1325/C1375 records and their truncations), the summary helpers and
// the per-batch verdict loops (outcome classification, expanded rows, cross-check, required classes).
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyCoreTests
{
    [Test]
    public Task CoreMatchesFallback(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-core.ps1", "NC", nameof(CoreMatchesFallback), 10,
            ["NC CoreMatchesFallback.coreLoaded", "NC CoreMatchesFallback.census", "NC CoreMatchesFallback.plan",
             "NC CoreMatchesFallback.indexView", "NC CoreMatchesFallback.crossCheck", "NC CoreMatchesFallback.json",
             "NC CoreMatchesFallback.diagnostics", "NC CoreMatchesFallback.summaryHelpers",
             "NC CoreMatchesFallback.verdicts", "NC CoreMatchesFallback.groupFilter"],
            ScriptHarnessOptions.Default, cancellationToken);
}
