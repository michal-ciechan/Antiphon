using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseSelectionHopSeedTests
{
    private static Task Run(string name, int rows, CancellationToken token, params string[] required) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-selection-hop-seed.ps1", "HS", name, rows, required,
            new ScriptHarnessOptions(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(90_000)]
    public Task SeededRcHopBinds(CancellationToken token) => Run(nameof(SeededRcHopBinds), 7, token,
        "HS SeededRcHopBinds argument shape",
        "HS SeededRcHopBinds PriorFailuresPath",
        "HS SeededRcHopBinds PriorFailuresDigest",
        "HS SeededRcHopBinds AcceptCrossCandidateSeed",
        "HS SeededRcHopBinds SeedSourceCandidateId");

    [Test, Timeout(90_000)]
    public Task SwitchDriftGuard(CancellationToken token) => Run(nameof(SwitchDriftGuard), 3, token,
        "HS SwitchDriftGuard refuses the hand list",
        "HS SwitchDriftGuard every target switch keeps its value");

    [Test, Timeout(90_000)]
    public Task HopFailureRecord(CancellationToken token) => Run(nameof(HopFailureRecord), 6, token,
        "HS HopFailureRecord log has the exception",
        "HS HopFailureRecord log has the command",
        "HS HopFailureRecord last-run error",
        "HS HopFailureRecord catch writes the failure");
}
