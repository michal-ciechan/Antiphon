using System.Reflection;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class C0824TransportLimiterTests
{
    [Test]
    [Arguments(typeof(C0824WorkspaceSlotServerTests), nameof(C0824WorkspaceSlotServerTests.C0824_LeaseRoundTripRealRunner))]
    [Arguments(typeof(C0824WorkspaceSlotServerTests), nameof(C0824WorkspaceSlotServerTests.C0824_BusyRunnerLeaseSerialisedNotDuplicated))]
    [Arguments(typeof(C0824WorkspaceSlotServerTests), nameof(C0824WorkspaceSlotServerTests.C0824_LeaseRecoveryAtEachHandoff))]
    [Arguments(typeof(C0824WorkspaceSlotServerTests), nameof(C0824WorkspaceSlotServerTests.C0824_ReleaseRoundTripRealRunner))]
    [Arguments(typeof(C1677RetentionServerTests), nameof(C1677RetentionServerTests.Retain_round_trip_real_runner))]
    [Arguments(typeof(C1677RetentionServerTests), nameof(C1677RetentionServerTests.Busy_runner_retain_is_serialised))]
    [Arguments(typeof(C1677RetentionServerTests), nameof(C1677RetentionServerTests.Retain_recovery_at_each_handoff))]
    public void Real_transport_methods_use_the_assembly_local_strict_lane(Type type, string name)
    {
        var method = type.GetMethod(name).ShouldNotBeNull();
        method.GetCustomAttribute<ParallelLimiterAttribute<ProcessSpawnLimit>>().ShouldNotBeNull(
            "real transports reach web hosts and the custodied scratch-Git helper");
        method.GetCustomAttribute<ParallelLimiterAttribute<GitProcessSpawnLimit>>().ShouldBeNull();
    }
}
