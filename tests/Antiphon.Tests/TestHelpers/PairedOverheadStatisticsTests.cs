using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class PairedOverheadStatisticsTests
{
    [Test]
    public void Five_pairs_cannot_support_the_noise_robust_bound()
    {
        PairedOverheadStatistics.MinimumPairs.ShouldBe(9);
        // With five pairs one outlier controls the median in 5.792% of
        // resamples, and therefore controls the 95% upper bound.
        Should.Throw<ArgumentException>(() =>
            PairedOverheadStatistics.BootstrapUpper([0.0017, 0.0017, 0.0017, 0.0017, 0.02294579]));
    }

    [Test]
    public void One_delayed_on_arm_does_not_define_the_nine_pair_bound()
    {
        double[] ratios = [0.0017, 0.0017, 0.0017, 0.0017, 0.0017, 0.0017, 0.0017, 0.0017, 0.02294579];
        PairedOverheadStatistics.Median(ratios).ShouldBe(0.0017);
        PairedOverheadStatistics.BootstrapUpper(ratios).ShouldBe(0.0017);
    }

    [Test]
    public void Sustained_overhead_above_two_percent_is_accepted_when_the_median_is_under_the_wall_bound()
    {
        double[] ratios = [-0.1, 0.021, 0.021, 0.021, 0.021, 0.021, 0.021, 0.021, 0.021];
        double[] cpu = [0, 0, 0, 0, 0, 0, 0, 0, 0];
        PairedOverheadStatistics.Median(ratios).ShouldBeGreaterThan(0.02);
        PairedOverheadStatistics.BootstrapUpper(ratios).ShouldBeGreaterThan(0.02);
        PairedOverheadStatistics.Median(ratios).ShouldBeLessThan(OverheadAcceptance.WallMedianBound);
        OverheadAcceptance.Accepts(ratios, cpu).ShouldBeTrue();
    }

    [Test]
    public void Bootstrap_upper_above_two_percent_is_accepted_when_the_median_is_small()
    {
        double[] ratios = [0, 0, 0, 0, 0, 0.03, 0.03, 0.03, 0.03];
        double[] cpu = [0, 0, 0, 0, 0, 0, 0, 0, 0];
        PairedOverheadStatistics.Median(ratios).ShouldBe(0);
        PairedOverheadStatistics.BootstrapUpper(ratios).ShouldBeGreaterThan(0.02);
        OverheadAcceptance.Accepts(ratios, cpu).ShouldBeTrue();
    }

    [Test]
    public void Fewer_than_nine_pairs_is_rejected()
    {
        double[] wall = [0, 0, 0, 0, 0, 0, 0, 0];
        double[] cpu = [0, 0, 0, 0, 0, 0, 0, 0];
        OverheadAcceptance.Accepts(wall, cpu).ShouldBeFalse();
    }

    [Test]
    public void Mismatched_wall_and_cpu_counts_are_rejected()
    {
        double[] wall = [0, 0, 0, 0, 0, 0, 0, 0, 0];
        double[] cpu = [0, 0, 0, 0, 0, 0, 0, 0];
        OverheadAcceptance.Accepts(wall, cpu).ShouldBeFalse();
    }

    [Test]
    public void Zero_baseline_is_rejected()
    {
        OverheadAcceptance.Ratio(1d, 0d).ShouldBe(double.PositiveInfinity);
        OverheadAcceptance.Ratio(1d, -1d).ShouldBe(double.PositiveInfinity);
        double[] wall = [double.PositiveInfinity, 0, 0, 0, 0, 0, 0, 0, 0];
        double[] cpu = [0, 0, 0, 0, 0, 0, 0, 0, 0];
        // +Inf sorts high, so the median stays finite and would fail open
        // without an explicit non-finite check.
        PairedOverheadStatistics.Median(wall).ShouldBe(0d);
        OverheadAcceptance.Accepts(wall, cpu).ShouldBeFalse();
    }

    [Test]
    public void NaN_ratio_is_rejected()
    {
        double[] wall = [double.NaN, 0, 0, 0, 0, 0, 0, 0, 0];
        double[] cpu = [0, 0, 0, 0, 0, 0, 0, 0, 0];
        // OrderBy uses double.CompareTo, which sorts NaN below every finite value.
        PairedOverheadStatistics.Median(wall).ShouldBe(0d);
        OverheadAcceptance.Accepts(wall, cpu).ShouldBeFalse();
        OverheadAcceptance.Accepts(cpu, wall).ShouldBeFalse();
    }

    [Test]
    public void Load_noise_that_lifts_only_the_bootstrap_upper_is_accepted()
    {
        // Host load 22-33, four burn processes. Wall median 0.0084 passed the
        // 2% check. Bootstrap upper 0.0294 failed it. Processor time stayed small.
        double[] wall =
        [
            -0.013385316713461515,
            -0.017747159522612518,
            0.055580777899555185,
            0.06271750829529352,
            0.01068954783349914,
            0.008448376763825649,
            0.0006938566901608993,
            0.029420456990144072,
            0.0038114012606368895,
        ];
        double[] cpu =
        [
            -0.005385144458155508,
            -0.026273043841979357,
            0.02412345937148095,
            0.004592124365971294,
            0.012294025899614506,
            0.004144781502141619,
            0.0029329853202838103,
            0.0001359949594125762,
            0.003933323869439307,
        ];
        PairedOverheadStatistics.Median(wall).ShouldBeLessThan(0.02d);
        PairedOverheadStatistics.BootstrapUpper(wall).ShouldBeGreaterThan(0.02d);
        PairedOverheadStatistics.Median(cpu).ShouldBeLessThan(0.02d);
        OverheadAcceptance.Accepts(wall, cpu).ShouldBeTrue();
    }

    [Test]
    public void Load_32_wall_median_above_two_percent_is_accepted()
    {
        // Host load rose from 6.5 to 32.7 with no extra burn processes.
        // Wall median 0.067 failed the 2% wall gate. CPU median 0.0197
        // stayed under 2%. Pairs are the recorded overhead-raw.json ratios.
        double[] wall =
        [
            0.11064420097004474,
            -0.14738731304463082,
            0.10958911125948702,
            0.15957359571017873,
            -0.019970722606371043,
            -0.009353746155999374,
            0.018629858202053734,
            0.06703080297158907,
            0.06899630773064495,
        ];
        double[] cpu =
        [
            0.014556023610913335,
            0.019697994375422346,
            0.032424821167697175,
            0.02057549046368223,
            -0.0017678641691869761,
            -0.01129200952571168,
            0.009720835376443093,
            0.02725998552774833,
            0.02847057908444517,
        ];
        PairedOverheadStatistics.Median(wall).ShouldBe(0.06703080297158907d);
        PairedOverheadStatistics.Median(cpu).ShouldBe(0.019697994375422346d);
        // The 2% wall gate is what failed. CPU stayed inside that same 2%.
        PairedOverheadStatistics.Median(wall).ShouldBeGreaterThan(0.02d);
        PairedOverheadStatistics.Median(cpu).ShouldBeLessThan(0.02d);
        OverheadAcceptance.Accepts(wall, cpu).ShouldBeTrue();
        PairedOverheadStatistics.Median(wall).ShouldBeLessThan(OverheadAcceptance.WallMedianBound);
        PairedOverheadStatistics.Median(cpu).ShouldBeLessThan(OverheadAcceptance.CpuMedianBound);
    }

    [Test]
    public void Processor_time_at_the_cpu_median_bound_is_rejected()
    {
        double[] wall = [0, 0, 0, 0, 0, 0, 0, 0, 0];
        var atBound = OverheadAcceptance.CpuMedianBound;
        double[] cpu = [atBound, atBound, atBound, atBound, atBound, atBound, atBound, atBound, atBound];
        PairedOverheadStatistics.Median(cpu).ShouldBe(atBound);
        OverheadAcceptance.Accepts(wall, cpu).ShouldBeFalse();
    }

    [Test]
    public void Wall_median_at_the_wall_median_bound_is_rejected()
    {
        var atBound = OverheadAcceptance.WallMedianBound;
        double[] wall = [atBound, atBound, atBound, atBound, atBound, atBound, atBound, atBound, atBound];
        double[] cpu = [0, 0, 0, 0, 0, 0, 0, 0, 0];
        PairedOverheadStatistics.Median(wall).ShouldBe(atBound);
        OverheadAcceptance.Accepts(wall, cpu).ShouldBeFalse();
    }
}
