using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandingAdmissionControlTests
{
    [Test]
    [Arguments("Fresh")]
    [Arguments("AlreadyPresent")]
    [Arguments("ResumePublication")]
    [Arguments("CleanupRetry")]
    public Task C448_V14_shared_land_first(string mode)
        => new AgentTaskLandAdmissionTests()
            .C448_V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther("shared", mode, "land-first");

    [Test]
    [Arguments("Fresh")]
    [Arguments("AlreadyPresent")]
    [Arguments("ResumePublication")]
    [Arguments("CleanupRetry")]
    public Task C448_V14_shared_dispatch_first(string mode)
        => new AgentTaskLandAdmissionTests()
            .C448_V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther("shared", mode, "dispatch-first");

    [Test]
    [Arguments("Fresh")]
    [Arguments("AlreadyPresent")]
    [Arguments("ResumePublication")]
    [Arguments("CleanupRetry")]
    public Task C448_V14_shared_dispatch_before_acquire(string mode)
        => new AgentTaskLandAdmissionTests()
            .C448_V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther("shared", mode, "dispatch-before-acquire");

    [Test]
    [Arguments("Fresh")]
    [Arguments("AlreadyPresent")]
    [Arguments("ResumePublication")]
    [Arguments("CleanupRetry")]
    public Task C448_V14_follow_up_land_first(string mode)
        => new AgentTaskLandAdmissionTests()
            .C448_V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther("follow-up", mode, "land-first");

    [Test]
    [Arguments("Fresh")]
    [Arguments("AlreadyPresent")]
    [Arguments("ResumePublication")]
    [Arguments("CleanupRetry")]
    public Task C448_V14_follow_up_dispatch_first(string mode)
        => new AgentTaskLandAdmissionTests()
            .C448_V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther("follow-up", mode, "dispatch-first");

    [Test]
    [Arguments("Fresh")]
    [Arguments("AlreadyPresent")]
    [Arguments("ResumePublication")]
    [Arguments("CleanupRetry")]
    public Task C448_V14_follow_up_dispatch_before_acquire(string mode)
        => new AgentTaskLandAdmissionTests()
            .C448_V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther("follow-up", mode, "dispatch-before-acquire");

}
