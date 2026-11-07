using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// scripts/lib/NightlyCore.cs (compiled per-test nightly work) returns exactly what the PowerShell fallback
// returns: census, plan, discovery index view, TRX/diagnostic cross-check and the evidence JSON writer.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyCoreTests
{
    [Test]
    public Task CoreMatchesFallback(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-core.ps1", "NC", nameof(CoreMatchesFallback), 6,
            ["NC CoreMatchesFallback.coreLoaded", "NC CoreMatchesFallback.census", "NC CoreMatchesFallback.plan",
             "NC CoreMatchesFallback.indexView", "NC CoreMatchesFallback.crossCheck", "NC CoreMatchesFallback.json"],
            ScriptHarnessOptions.Default, cancellationToken);
}
