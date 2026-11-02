using Antiphon.Tests.TestHelpers;
using Antiphon.TestSupport.LiveClaude;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1545 S2a-2: the live-lane environment attestation is exact, fails closed and is shadow-only.
// Each method produces its documents from the real S1 objects, then runs the PowerShell consumer.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneAttestationTests
{
    private static readonly TimeSpan MethodBudget = TimeSpan.FromSeconds(60);

    [Test]
    public Task CliVersionOrBytesInvalidates() => Run(nameof(CliVersionOrBytesInvalidates),
        "same-version-new-bytes", "version", "installed-target", "pre-post-drift", "equal");

    [Test]
    public Task WrapperPayloadInvalidates() => Run(nameof(WrapperPayloadInvalidates),
        "chain-payload", "chain-shape", "chain-unresolved", "observed-chain");

    [Test]
    public Task AttestedPathMismatchRefuses() => Run(nameof(AttestedPathMismatchRefuses),
        "launched-path", "launched-hash", "path-route", "missing-observation", "any-row");

    [Test]
    public Task ConfigurationInvalidates() => Run(nameof(ConfigurationInvalidates),
        "user", "project-local", "managed", "global-state", "overlay", "model", "env-tristate", "runtime-identity",
        "volatile-only", "credential-free", "credential-case");

    [Test]
    public Task UnknownSettingsRefuseReuse() => Run(nameof(UnknownSettingsRefuseReuse),
        "unknown-key", "managed-unverified", "unreadable", "missing-source-entry", "additional-source", "forged-eligible",
        "runtime-missing", "string-boolean", "schema", "linked-document", "size-cap", "roster-bound");

    [Test]
    public Task ExternalEpochIsNotInferred() => Run(nameof(ExternalEpochIsNotInferred),
        "always-rerun-default", "no-epoch", "bounded-fresh", "expired", "future-skew", "probe-stale", "probe-foreign-nonce",
        "probe-failed", "horizon-fixed", "host-account", "incident-revoked", "binding-missing", "probe-time-token",
        "executed-host-account");

    private static async Task Run(string method, params string[] cases)
    {
        var directory = LiveClaudeProfileFixture.CleanTempSubdirectory("c1545-attestation-");
        try
        {
            LiveLaneAttestationExport.WriteAll(directory);
            await ScriptHarness.RunHarnessCaseAsync("test-live-lane-attestation.ps1", "C1545", "C1545_" + method, cases.Length,
                cases.Select(name => $"C1545 {method}/{name}").ToArray(),
                new ScriptHarnessOptions(MethodBudget, TimeSpan.FromSeconds(10), PostExitDrainBudget: TimeSpan.FromSeconds(10),
                    AdditionalArguments: ["-AttestationDirectory", directory]),
                CancellationToken.None);
        }
        finally { Directory.Delete(directory, true); }
    }
}
