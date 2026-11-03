using Antiphon.Tests.TestHelpers;
using Antiphon.TestSupport.LiveClaude;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1545 S2b: live-lane receipts are exact, typed, lineage-bound and revocable; a reuse is a use
// record, never an execution, and no state carries gate credit (shadow-only).
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneReceiptTests
{
    private static readonly TimeSpan MethodBudget = TimeSpan.FromSeconds(60);

    [Test]
    public Task ExactRosterRequired() => Run(nameof(ExactRosterRequired), false,
        "exact", "dropped", "duplicate", "extra-expansion", "renamed", "planned-executed", "skipped", "zero");

    [Test]
    public Task TypesAndCountsFailClosed() => Run(nameof(TypesAndCountsFailClosed), false,
        "valid", "string-bool", "string-count", "negative", "counts-outcomes", "unknown-field", "kind", "schema-version", "null-required",
        "outcome-enum", "classifier-exit", "artifact-digest", "time-order");

    // Measured budgets: LiveLaneFingerprintTests.Budget names the measurement and the factor.
    [Test]
    public Task LineageAndHostBound() => Run(nameof(LineageAndHostBound), false, LiveLaneFingerprintTests.Budget(9.8),
        "same-line-ancestor", "foreign-repo", "other-line", "not-ancestor", "missing-object", "host", "account", "lane", "dirty", "issuer");

    [Test]
    public Task NewerRedRevokes() => Run(nameof(NewerRedRevokes), false,
        "green-then-red", "newer-inconclusive", "revocation", "red-then-green", "other-fingerprint-red", "last-green-only", "order-independent",
        "contradictory-duplicate");

    // The policy-default case runs the status front door with a real attestation document.
    [Test]
    public Task ReuseIsNotExecution() => Run(nameof(ReuseIsNotExecution), true, LiveLaneFingerprintTests.Budget(17.8),
        "use-record", "receipt-immutable", "no-chained-reuse", "forged-executed", "no-overwrite", "policy-default", "gate-credit");

    [Test]
    public Task InputDriftRefuses() => Run(nameof(InputDriftRefuses), false,
        "equal-all", "fingerprint-unequal", "component-tamper", "roster-changed", "unknown-input", "pre-post", "attestation-unknown", "stale");

    private static Task Run(string method, bool attestation, params string[] cases) => Run(method, attestation, MethodBudget, cases);

    private static async Task Run(string method, bool attestation, TimeSpan budget, params string[] cases)
    {
        var directory = LiveClaudeProfileFixture.CleanTempSubdirectory("c1545-receipt-");
        try
        {
            if (attestation) LiveLaneAttestationExport.WriteAll(directory);
            await ScriptHarness.RunHarnessCaseAsync("test-live-lane-receipt.ps1", "C1545", "C1545_" + method, cases.Length,
                cases.Select(name => $"C1545 {method}/{name}").ToArray(),
                new ScriptHarnessOptions(budget, TimeSpan.FromSeconds(10), PostExitDrainBudget: TimeSpan.FromSeconds(10),
                    AdditionalArguments: ["-AttestationDirectory", directory]),
                CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }
}
