using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1836 S6c. The whole pre-port golden, including the twenty added edges.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcShadowDirtyDifferentialTests
{
    [Test]
    public void CorpusMatchesGolden()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay(null);
    }
}
