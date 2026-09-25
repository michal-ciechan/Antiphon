using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0208: process-spawning classes share a 1-wide lane in this assembly. The roster is a floor, not a census.</summary>
[Category("Unit")]
public class ProcessSpawnLimitTests
{
    [Test]
    public void Caps_concurrent_process_spawning_tests_at_one()
    {
        new ProcessSpawnLimit().Limit.ShouldBe(1);
    }

    [Test]
    public void Process_spawning_classes_carry_the_limiter()
    {
        Type[] expected =
        [
            typeof(DaemonLogRotationTests),
            typeof(DaemonStartupDiagnosticsTests),
            typeof(FirstWriteRaceTests),
            typeof(GrokRulesAdoptionTests),
            typeof(GrokRulesFileLaunchTests),
            typeof(GrokRulesHttpAcceptanceTests),
            typeof(GrokRulesStoreFailureTests),
            typeof(HerdrAdoptionSweepTests),
            typeof(HerdrAttachTests),
            typeof(HerdrGrokNativeSessionLiveTests),
            typeof(HerdrNamedTabPlacementLiveTests),
            typeof(HerdrPaneChildKillTests),
            typeof(PtyBackendSeamTests),
            typeof(PtyHostAdoptionTests),
            typeof(RunnerRestartHealthTests),
            typeof(RunnerRestartScriptCompatibilityTests),
            typeof(RunnerRestartPreflightSafetyTests),
            typeof(RunnerStartupReadinessTests),
            typeof(SessionBufferBoundsTests),
            typeof(SessionCpuWatchdogTests),
            typeof(SessionLivenessTests),
            typeof(TranscriptAdoptionSafetyTests),
            typeof(CodexCommandLengthHttpAcceptanceTests),
            typeof(HerdrLabelFollowLiveTests),
            typeof(HerdrLabelFollowSchedulingTests),
            typeof(HerdrLabelSnapshotTests),
            typeof(HerdrPaneDisposalGuardedLiveTests),
            typeof(HerdrPaneDisposalServiceTests),
            typeof(HerdrPaneDisposalStopRegressionTests),
            typeof(RemoteControlConditionalInputTests),
            typeof(RunnerCustodyCrashTests),
            typeof(RunnerCustodyTests),
            typeof(RunnerSessionGenerationTests),
            typeof(BuildSlotEndToEndTests),
        ];

        var actual = typeof(ProcessSpawnLimit).Assembly.GetTypes()
            .Where(type => type.IsClass
                           && Attribute.GetCustomAttribute(type, typeof(ParallelLimiterAttribute<ProcessSpawnLimit>)) is not null)
            .ToHashSet();

        actual.ShouldNotBeEmpty();
        actual.IsSupersetOf(expected).ShouldBeTrue(
            "missing: "
            + string.Join(", ", expected.Except(actual).Select(type => type.Name).OrderBy(name => name, StringComparer.Ordinal)));
    }
}
