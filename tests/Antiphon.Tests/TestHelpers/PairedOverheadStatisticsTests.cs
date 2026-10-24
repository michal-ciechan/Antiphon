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
}
