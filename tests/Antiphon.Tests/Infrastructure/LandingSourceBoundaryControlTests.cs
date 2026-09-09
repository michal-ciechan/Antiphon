using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandingSourceBoundaryControlTests
{
    [Test]
    [Arguments("advance", false)]
    [Arguments("advance", true)]
    [Arguments("dirty", false)]
    [Arguments("dirty", true)]
    [Arguments("metadata", false)]
    [Arguments("metadata", true)]
    public Task C448_V10_after_fetch(string change, bool contained)
        => new AgentTaskLandBoundaryTests()
            .C448_V10_EachAcknowledgedBoundaryRechecksSource("remote", change, contained);

    [Test]
    [Arguments("advance", false)]
    [Arguments("dirty", false)]
    [Arguments("metadata", false)]
    public Task C448_V10_before_rebase_intent(string change, bool contained)
        => new AgentTaskLandBoundaryTests()
            .C448_V10_EachAcknowledgedBoundaryRechecksSource("BeforeRebaseIntent", change, contained);

    [Test]
    [Arguments("advance", false)]
    [Arguments("dirty", false)]
    [Arguments("metadata", false)]
    public Task C448_V10_before_rebase_child(string change, bool contained)
        => new AgentTaskLandBoundaryTests()
            .C448_V10_EachAcknowledgedBoundaryRechecksSource("RebaseStarted", change, contained);

    [Test]
    [Arguments("advance", false)]
    [Arguments("dirty", false)]
    [Arguments("metadata", false)]
    public Task C448_V10_before_verification(string change, bool contained)
        => new AgentTaskLandBoundaryTests()
            .C448_V10_EachAcknowledgedBoundaryRechecksSource("Prepared", change, contained);

    [Test]
    [Arguments("advance", false)]
    [Arguments("dirty", false)]
    [Arguments("metadata", false)]
    public Task C448_V10_before_target_intent(string change, bool contained)
        => new AgentTaskLandBoundaryTests()
            .C448_V10_EachAcknowledgedBoundaryRechecksSource("Verified", change, contained);

    [Test]
    [Arguments("advance", false)]
    [Arguments("dirty", false)]
    [Arguments("metadata", false)]
    public Task C448_V10_before_target_mutation(string change, bool contained)
        => new AgentTaskLandBoundaryTests()
            .C448_V10_EachAcknowledgedBoundaryRechecksSource("TargetAdvanceStarted", change, contained);

    [Test]
    [Arguments("advance", false)]
    [Arguments("dirty", false)]
    [Arguments("metadata", false)]
    public Task C448_V10_before_publication_observation(string change, bool contained)
        => new AgentTaskLandBoundaryTests()
            .C448_V10_EachAcknowledgedBoundaryRechecksSource("LocalTargetAdvanced", change, contained);

    [Test]
    [Arguments("advance", false)]
    [Arguments("dirty", false)]
    [Arguments("metadata", false)]
    public Task C448_V10_before_push_intent(string change, bool contained)
        => new AgentTaskLandBoundaryTests()
            .C448_V10_EachAcknowledgedBoundaryRechecksSource("BeforePushIntent", change, contained);

    [Test]
    [Arguments("advance", false)]
    [Arguments("dirty", false)]
    [Arguments("metadata", false)]
    public Task C448_V10_before_push(string change, bool contained)
        => new AgentTaskLandBoundaryTests()
            .C448_V10_EachAcknowledgedBoundaryRechecksSource("PushStarted", change, contained);

}
