using Antiphon.Tests.TestHelpers;
using TUnit.Core;
using Antiphon.Tests.Scripts;
using static Antiphon.Tests.Scripts.LiveLaneFingerprintTests;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneFingerprintInvariantTests
{
    [Test]
    public Task Stable_checkout_inputs() => Run(nameof(StableCanonicalInputs), Budget(23.9), "real-head", "real-declaration");
}
