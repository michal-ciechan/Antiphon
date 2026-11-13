using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcHistoricalAdmissionTests
{
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-admission.ps1", "RA", name, 1,
            ["RA " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(60_000)] public Task FullProvenanceAdmitsAndReverifies(CancellationToken token) => Run(nameof(FullProvenanceAdmitsAndReverifies), token);
    [Test, Timeout(60_000)] public Task ShaLabelWithoutBuildProofIsUnproven(CancellationToken token) => Run(nameof(ShaLabelWithoutBuildProofIsUnproven), token);
    [Test, Timeout(60_000)] public Task IncompleteChunkAndCaseAliasNeverCarry(CancellationToken token) => Run(nameof(IncompleteChunkAndCaseAliasNeverCarry), token);
    [Test, Timeout(60_000)] public Task ChangedEnvelopeHistoryAndOriginChainRefuse(CancellationToken token) => Run(nameof(ChangedEnvelopeHistoryAndOriginChainRefuse), token);
    [Test, Timeout(60_000)] public Task EveryIngredientMustMatch(CancellationToken token) => Run(nameof(EveryIngredientMustMatch), token);
    [Test, Timeout(60_000)] public Task CollisionAndAlgorithmCoverageForceFull(CancellationToken token) => Run(nameof(CollisionAndAlgorithmCoverageForceFull), token);
    [Test, Timeout(60_000)] public Task ImportIsInertAndWritesUnprovenShard(CancellationToken token) => Run(nameof(ImportIsInertAndWritesUnprovenShard), token);
}
