using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit"), Timeout(2_000)]
public class C1514HostLoadRecommendationTests
{
    private static DateTimeOffset T0 => new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static HostLoadPressureInput Input => new("host-a", "host-a", T0, 44, 30, 40);
    private static HostLoadCapacityInput Capacity => new(T0, 4, 3, 2, 1);
    private static HostLoadAdvice Evaluate(HostLoadPressureInput? input = null, HostLoadCapacityInput? capacity = null,
        IReadOnlyList<HostLoadLaunchObservation>? launches = null, bool scoped = true) =>
        new HostLoadRecommendation().Evaluate(input ?? Input, capacity ?? Capacity, launches ?? [], T0, scoped, scoped);
    [Test]
    public void PressureUsesLatestAndAverages()
    {
        var latest = Evaluate(Input with { LatestCpu = 90, Average15s = 20, Average60s = 30 });
        latest.Decision.CommandPressure.ShouldBe(90d); latest.Decision.DispatchPressure.ShouldBe(90d);
        latest.Recommendation.RecommendedAdditionalTasks.ShouldBe(0);
        var means = Evaluate(Input with { LatestCpu = 20, Average15s = 90, Average60s = 80 });
        means.Decision.CommandPressure.ShouldBe(90d); means.Decision.DispatchPressure.ShouldBe(80d);
        var distinct = Evaluate(Input with { LatestCpu = 20, Average15s = 30, Average60s = 40 });
        distinct.Decision.CommandPressure.ShouldBe(30d); distinct.Decision.DispatchPressure.ShouldBe(40d);
    }
    [Test]
    public void UnknownAndStalePressureRecommendZero()
    {
        foreach (var input in new[] { Input with { LatestCpu = null }, Input with { LatestCpu = double.NaN },
            Input with { LatestCpu = double.PositiveInfinity }, Input with { PhysicalHostKey = "foreign" },
            Input with { BindingKnown = false }, Input with { Valid60s = 11 }, Input with { Average60s = null },
            Input with { SampledAt = T0.AddMilliseconds(-15001) }, Input with { SampledAt = T0.AddSeconds(2) } })
        {
            var answer = Evaluate(input); answer.Recommendation.RecommendedAdditionalTasks.ShouldBe(0);
            answer.Decision.Dispatch.ShouldBe("unknown");
        }
        Evaluate(Input with { SampledAt = T0.AddSeconds(-15) }).Recommendation.RecommendedAdditionalTasks.ShouldBe(1);
        Evaluate(capacity: Capacity with { ObservedAt = T0.AddMilliseconds(-15001) }).Recommendation.RecommendedAdditionalTasks.ShouldBe(0);
    }
    [Test]
    public void CapacityClampsAreIndependent()
    {
        var all = new HostLoadCapacityInput(T0, 7, 7, 7, 7);
        foreach (var capacity in new[] { all with { HostRemaining = 1 }, all with { RunnerSeatsRemaining = 1 },
            all with { ProjectRemaining = 1 }, all with { StageRemaining = 1 } })
            Evaluate(Input with { LatestCpu = 5, Average15s = 5, Average60s = 5 }, capacity).Recommendation.RecommendedAdditionalTasks.ShouldBe(1);
        foreach (var capacity in new[] { all with { HostRemaining = null }, all with { RunnerSeatsRemaining = null },
            all with { ProjectRemaining = null }, all with { StageRemaining = null } })
            Evaluate(capacity: capacity).Recommendation.RecommendedAdditionalTasks.ShouldBe(0);
    }
    [Test]
    public void StartupDebtIsSubtractedOnce()
    {
        var launch = new HostLoadLaunchObservation(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 1, 1, T0.AddSeconds(-1), false, false);
        var answer = Evaluate(launches: [launch, launch]);
        answer.Recommendation.StartupDebtPercent.ShouldBe(10d); answer.Recommendation.CpuHeadroom.ShouldBe(2);
        foreach (var cleared in new[] { launch with { Represented = true }, launch with { StartupFailed = true } })
            Evaluate(launches: [cleared]).Recommendation.StartupDebtPercent.ShouldBe(0d);
        Evaluate(launches: [launch with { AcceptedAt = T0.AddHours(-1) }]).Recommendation.StartupDebtPercent.ShouldBe(10d);
    }
    [Test]
    public void ThresholdsAndHostOnlyScopeStayExplicit()
    {
        var launch = new HostLoadLaunchObservation(Guid.NewGuid(), 1, 1, T0, false, false);
        var scoped = Evaluate(launches: [launch]); scoped.Recommendation.RecommendedAdditionalTasks.ShouldBe(1);
        var host = Evaluate(launches: [launch], scoped: false);
        host.Recommendation.Scope.ShouldBe("hostOnly"); host.Recommendation.RecommendedAdditionalTasks.ShouldBe(2);
        host.Recommendation.MissingConstraints.ShouldBe(["project", "role"]);
        Evaluate(Input with { LatestCpu = 75 }).Decision.Dispatch.ShouldBe("hold");
        Evaluate(Input with { LatestCpu = 85 }).Decision.Command.ShouldBe("hold");
        Evaluate(Input with { LatestCpu = 80 }).Decision.Command.ShouldBe("eligible");
        Evaluate(Input with { LatestCpu = 64.9 }).Recommendation.CpuHeadroom.ShouldBe(1);
        Evaluate(Input with { LatestCpu = 65.1 }).Recommendation.CpuHeadroom.ShouldBe(0);
    }
}
