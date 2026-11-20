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

    // CARD-2010. Expected output is the pre-port PowerShell result.
    [Test]
    public void PsObjectWrappedAttempt()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("PsObjectWrappedAttempt");
    }

    [Test]
    public void NonIntegerSequence()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("NonIntegerSequence");
    }

    [Test]
    public void ArrayValuedKey()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("ArrayValuedKey");
    }
}
