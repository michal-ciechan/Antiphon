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
    public void Sustained_overhead_above_two_percent_is_rejected_despite_a_fast_on_outlier()
    {
        double[] ratios = [-0.1, 0.021, 0.021, 0.021, 0.021, 0.021, 0.021, 0.021, 0.021];
        PairedOverheadStatistics.Median(ratios).ShouldBeGreaterThan(0.02);
        PairedOverheadStatistics.BootstrapUpper(ratios).ShouldBeGreaterThan(0.02);
    }

    [Test]
    public void Uncertain_upper_bound_is_rejected_even_when_the_median_is_small()
    {
        double[] ratios = [0, 0, 0, 0, 0, 0.03, 0.03, 0.03, 0.03];
        PairedOverheadStatistics.Median(ratios).ShouldBe(0);
        PairedOverheadStatistics.BootstrapUpper(ratios).ShouldBeGreaterThan(0.02);
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
    public void Processor_time_above_two_percent_is_rejected()
    {
        double[] wall = [0, 0, 0, 0, 0, 0, 0, 0, 0];
        double[] cpu = [0.03, 0.03, 0.03, 0.03, 0.03, 0.03, 0.03, 0.03, 0.03];
        OverheadAcceptance.Accepts(wall, cpu).ShouldBeFalse();
    }

    [Test]
    public void Wall_median_above_two_percent_is_rejected()
    {
        double[] wall = [0.021, 0.021, 0.021, 0.021, 0.021, 0.021, 0.021, 0.021, 0.021];
        double[] cpu = [0, 0, 0, 0, 0, 0, 0, 0, 0];
        OverheadAcceptance.Accepts(wall, cpu).ShouldBeFalse();
    }
}
