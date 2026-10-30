using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcFailureHistoryTests
{
    // Provisional CARD-1404 budget: one shared replay, 120s owned process + 10s cleanup.
    [Test, Timeout(180_000)]
    public Task EvidenceUnion(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-failed-first.ps1", "FF", nameof(EvidenceUnion), 10,
            ["FF EvidenceUnion.trx36", "FF EvidenceUnion.diag7", "FF EvidenceUnion.hang1",
             "FF EvidenceUnion.classes34", "FF EvidenceUnion.fallback", "FF EvidenceUnion.replay",
             "FF EvidenceUnion.allSplits", "FF EvidenceUnion.transitions", "FF EvidenceUnion.exactObservations",
             "FF EvidenceUnion.immutable"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(10)), cancellationToken);
}
