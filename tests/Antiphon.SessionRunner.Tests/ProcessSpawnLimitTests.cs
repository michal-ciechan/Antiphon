using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0208: process-spawning classes share a 1-wide lane in this assembly.</summary>
[Category("Unit")]
public class ProcessSpawnLimitTests
{
    [Test]
    public void Caps_concurrent_process_spawning_tests_at_one()
    {
        new ProcessSpawnLimit().Limit.ShouldBe(1);
    }

    [Test]
    public void Process_spawning_classes_are_exactly_the_limiter_population()
    {
        Type[] expected =
        [
            typeof(DaemonLogRotationTests),
            typeof(FirstWriteRaceTests),
            typeof(HerdrAdoptionSweepTests),
            typeof(HerdrAttachTests),
            typeof(HerdrGrokNativeSessionLiveTests),
            typeof(HerdrPaneChildKillTests),
            typeof(PtyBackendSeamTests),
            typeof(PtyHostAdoptionTests),
            typeof(SessionBufferBoundsTests),
            typeof(SessionCpuWatchdogTests),
            typeof(SessionLivenessTests),
            typeof(TranscriptAdoptionSafetyTests),
        ];

        foreach (var type in expected)
        {
            Attribute.GetCustomAttribute(type, typeof(ParallelLimiterAttribute<ProcessSpawnLimit>))
                .ShouldNotBeNull($"{type.Name} must carry [ParallelLimiter<ProcessSpawnLimit>]");
        }

        var actual = typeof(ProcessSpawnLimit).Assembly.GetTypes()
            .Where(type => type.IsClass
                           && Attribute.GetCustomAttribute(type, typeof(ParallelLimiterAttribute<ProcessSpawnLimit>)) is not null)
            .ToHashSet();

        actual.SetEquals(expected).ShouldBeTrue(
            "unexpected limiter types: "
            + string.Join(", ", actual.Except(expected).Select(type => type.Name).OrderBy(name => name, StringComparer.Ordinal))
            + "; missing: "
            + string.Join(", ", expected.Except(actual).Select(type => type.Name).OrderBy(name => name, StringComparer.Ordinal)));
    }
}
