using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1836 S6c. Same witness suffixes as RcDirtyLaunchOrderTests. Red until the compiled type exists.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcDirtyOrderCSharpTests
{
    [Test]
    public void OnlyDirtyAndSampleLaunch()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("OnlyDirtyAndSampleLaunch");
    }

    [Test]
    public void OpenFailurePrecedesChanged()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("OpenFailurePrecedesChanged");
    }

    [Test]
    public void SameTipPassCannotClearFailure()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("SameTipPassCannotClearFailure");
    }

    [Test]
    public void CaseDistinctKeysStaySeparate()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("CaseDistinctKeysStaySeparate");
    }

    [Test]
    public void CarryWithOpenFailureRefuses()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("CarryWithOpenFailureRefuses");
    }

    [Test]
    public void UnknownShaRefuses()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("UnknownShaRefuses");
    }
}
