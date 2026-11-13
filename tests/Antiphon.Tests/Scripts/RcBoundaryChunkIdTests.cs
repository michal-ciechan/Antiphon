using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcBoundaryChunkIdTests
{
    // CARD-1836 provisional process-only budget; no DB, provider or qualification host.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-boundary.ps1", "RB", name, 1,
            ["RB " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(120_000)] public Task StableIdIgnoresOrder(CancellationToken token) => Run(nameof(StableIdIgnoresOrder), token);
    [Test, Timeout(120_000)] public Task MovedClassChangesId(CancellationToken token) => Run(nameof(MovedClassChangesId), token);
    [Test, Timeout(120_000)] public Task UnassignedStaysAlone(CancellationToken token) => Run(nameof(UnassignedStaysAlone), token);
    [Test, Timeout(120_000)] public Task TwoBoundariesNeverShare(CancellationToken token) => Run(nameof(TwoBoundariesNeverShare), token);
    [Test, Timeout(120_000)] public Task CanonicalEncodingKeepsSpecialCharacters(CancellationToken token) => Run(nameof(CanonicalEncodingKeepsSpecialCharacters), token);
}
