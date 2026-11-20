using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcIncrementalShadowTests
{
    // CARD-1836 S8b provisional process-only budget; collect timings in CP-11.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-shadow-sample.ps1", "RS", name, 1,
            ["RS " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(60_000)] public Task SampleSizeStrataAndReplay(CancellationToken token) => Run(nameof(SampleSizeStrataAndReplay), token);
    [Test, Timeout(60_000)] public Task MismatchInvalidatesCarries(CancellationToken token) => Run(nameof(MismatchInvalidatesCarries), token);
    [Test, Timeout(60_000)] public Task NoCarryAndOnePilotDoNotGrantTrust(CancellationToken token) => Run(nameof(NoCarryAndOnePilotDoNotGrantTrust), token);

    // CARD-1836 S8b follow-up. A-1 through A-4 must fail on the pre-fix trust counter.
    [Test, Timeout(60_000)] public Task OneExecutedSampleIsNotAPilot(CancellationToken token) => Run(nameof(OneExecutedSampleIsNotAPilot), token);
    [Test, Timeout(60_000)] public Task EmptyDigestIsUnproven(CancellationToken token) => Run(nameof(EmptyDigestIsUnproven), token);
    [Test, Timeout(60_000)] public Task MissingCarryableIsDirty(CancellationToken token) => Run(nameof(MissingCarryableIsDirty), token);
    [Test, Timeout(60_000)] public Task EvidenceDerivesSampleFailure(CancellationToken token) => Run(nameof(EvidenceDerivesSampleFailure), token);

    // CARD-1836 S8b follow-up round 2. D-1 and D-2 must fail on ccf2dbb0.
    // AbsentEvidenceKeepsCarriesUntilWired pins the current fail-open.
    [Test, Timeout(60_000)] public Task NonBooleanProvenanceFailureInvalidates(CancellationToken token) => Run(nameof(NonBooleanProvenanceFailureInvalidates), token);
    [Test, Timeout(60_000)] public Task CarriedChunkResultInvalidatesCarries(CancellationToken token) => Run(nameof(CarriedChunkResultInvalidatesCarries), token);
    [Test, Timeout(60_000)] public Task AbsentEvidenceKeepsCarriesUntilWired(CancellationToken token) => Run(nameof(AbsentEvidenceKeepsCarriesUntilWired), token);
}
