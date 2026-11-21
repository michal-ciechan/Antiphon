using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1836 S6c. Same witness suffixes as RcDirtyLaunchOrderTests. Red until the compiled type exists.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcDirtyOrderCSharpTests
{
    [Test]
    public void ScratchRepoCleanupClearsReadOnlyObjects()
    {
        var observer = Directory.CreateTempSubdirectory("rc-port-object-").FullName;
        var alias = Path.Combine(observer, "object");
        string? scratch = null;
        try
        {
            RcPortCorpus.WithScratchRepo(repo =>
            {
                scratch = repo;
                var fanout = Directory.CreateDirectory(Path.Combine(repo, ".git", "objects", "ab"));
                var looseObject = Path.Combine(fanout.FullName, new string('c', 38));
                File.WriteAllText(looseObject, "fixture loose object");
                if (OperatingSystem.IsLinux()) NativeHardLink.Create(alias, looseObject);
                File.SetAttributes(looseObject, File.GetAttributes(looseObject) | FileAttributes.ReadOnly);
                File.GetAttributes(looseObject).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
            });

            Directory.Exists(scratch).ShouldBeFalse();
            // Linux can unlink a read-only file. The second name observes whether cleanup cleared the bit.
            if (OperatingSystem.IsLinux()) File.GetAttributes(alias).HasFlag(FileAttributes.ReadOnly).ShouldBeFalse();
        }
        finally
        {
            if (scratch is not null) GitFixtureCleanup.Delete(scratch);
            GitFixtureCleanup.Delete(observer);
        }
    }

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
