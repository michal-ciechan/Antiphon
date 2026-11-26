using Antiphon.Tests.TestHelpers;
using TUnit.Core;
using static Antiphon.Tests.Scripts.LiveLaneFingerprintTests;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneFingerprintInvariantTests
{
    [Test]
    public void Stable_checkout_inputs() => CheckTrackedInputs();
}
