using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcBoundaryPackTests
{
    // CARD-1836: provisional 45s child + 10s joined cleanup, 60s test budget.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-boundary-pack.ps1", "RBP", name, 1,
            ["RBP " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(60_000)] public Task TwoBoundariesAndPrefixCollisionsStaySeparate(CancellationToken token) => Run(nameof(TwoBoundariesAndPrefixCollisionsStaySeparate), token);
    [Test, Timeout(60_000)] public Task RepackPreservesCaseUniverse(CancellationToken token) => Run(nameof(RepackPreservesCaseUniverse), token);
    [Test, Timeout(60_000)] public Task DefaultFullPackKeepsIds(CancellationToken token) => Run(nameof(DefaultFullPackKeepsIds), token);
    [Test, Timeout(60_000)] public Task CanonicalDigestIncludesCaseKeys(CancellationToken token) => Run(nameof(CanonicalDigestIncludesCaseKeys), token);
    [Test, Timeout(60_000)] public Task ShortIdCollisionUsesS2Policy(CancellationToken token) => Run(nameof(ShortIdCollisionUsesS2Policy), token);
    [Test, Timeout(60_000)] public Task SerializerEquivalentConstructorOrder(CancellationToken token) => Run(nameof(SerializerEquivalentConstructorOrder), token);
}
