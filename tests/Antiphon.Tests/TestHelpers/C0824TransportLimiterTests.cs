using System.Reflection;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class C0824TransportLimiterTests
{
    [Test]
    public void Scripted_rig_classes_share_bounded_admission()
    {
        foreach (var type in new[] { typeof(C0824WorkspaceSlotServerTests), typeof(C1677RetentionServerTests) })
        {
            type.GetCustomAttributes().Where(attribute => attribute.GetType().IsGenericType
                && attribute.GetType().GetGenericTypeDefinition() == typeof(ParallelLimiterAttribute<>))
                .ShouldBeEmpty("class limiters override the strict method lane in the pinned TUnit runtime");
            foreach (var method in type.GetMethods().Where(method => method.IsDefined(typeof(TestAttribute))))
            {
                var strict = method.GetCustomAttribute<ParallelLimiterAttribute<ProcessSpawnLimit>>();
                var scripted = method.GetCustomAttribute<ParallelLimiterAttribute<WorkspaceRigConcurrencyLimit>>();
                ((strict is not null) ^ (scripted is not null)).ShouldBeTrue(method.Name + " must bind exactly one admission lane");
            }
        }
    }

    [Test]
    [Arguments(typeof(C0824WorkspaceSlotServerTests), nameof(C0824WorkspaceSlotServerTests.C0824_LeaseRoundTripRealRunner))]
    [Arguments(typeof(C0824WorkspaceSlotServerTests), nameof(C0824WorkspaceSlotServerTests.C0824_LeasedTreeReadSpawnsNoScratchGit))]
    [Arguments(typeof(C0824WorkspaceSlotServerTests), nameof(C0824WorkspaceSlotServerTests.C0824_BusyRunnerLeaseSerialisedNotDuplicated))]
    [Arguments(typeof(C0824WorkspaceSlotServerTests), nameof(C0824WorkspaceSlotServerTests.C0824_LeaseRecoveryAtEachHandoff))]
    [Arguments(typeof(C0824WorkspaceSlotServerTests), nameof(C0824WorkspaceSlotServerTests.C0824_ReleaseRoundTripRealRunner))]
    [Arguments(typeof(C1677RetentionServerTests), nameof(C1677RetentionServerTests.Retain_round_trip_real_runner))]
    [Arguments(typeof(C1677RetentionServerTests), nameof(C1677RetentionServerTests.Busy_runner_retain_is_serialised))]
    [Arguments(typeof(C1677RetentionServerTests), nameof(C1677RetentionServerTests.Retain_recovery_at_each_handoff))]
    [Arguments(typeof(C1677RetentionServerTests), nameof(C1677RetentionServerTests.Ignored_secrets_survive_the_real_runner_and_a_lost_reply_replay))]
    public void Real_transport_methods_use_the_assembly_local_strict_lane(Type type, string name)
    {
        var method = type.GetMethod(name).ShouldNotBeNull();
        method.GetCustomAttribute<ParallelLimiterAttribute<ProcessSpawnLimit>>().ShouldNotBeNull(
            "real transports reach web hosts and the custodied scratch-Git helper");
        method.GetCustomAttribute<ParallelLimiterAttribute<GitProcessSpawnLimit>>().ShouldBeNull();
    }
}
