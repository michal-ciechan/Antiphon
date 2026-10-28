namespace Antiphon.Tests.TestHelpers;

internal static class PairedOverheadStatistics
{
    // Five pairs let one delayed arm determine the bootstrap 95% upper bound
    // (5.792% of resamples). Nine reduce that probability to 0.145% while
    // retaining the same median estimator and raw observations. Acceptance
    // bounds live on OverheadAcceptance.
    internal const int MinimumPairs = 9;

    internal static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted[sorted.Length / 2];
    }

    internal static double BootstrapUpper(IReadOnlyList<double> values)
    {
        if (values.Count < MinimumPairs)
            throw new ArgumentException($"At least {MinimumPairs} independent pairs are required.", nameof(values));
        const int draws = 2000;
        var rng = new Random(1514);
        var medians = new double[draws];
        for (var draw = 0; draw < draws; draw++)
        {
            var sample = new double[values.Count];
            for (var i = 0; i < values.Count; i++)
                sample[i] = values[rng.Next(values.Count)];
            medians[draw] = Median(sample);
        }

        Array.Sort(medians);
        var index = (int)Math.Ceiling(0.95d * draws) - 1;
        return medians[Math.Clamp(index, 0, draws - 1)];
    }
}

internal static class OverheadAcceptance
{
    // Processor time is this process only. At host load about 33 the CPU
    // median was 0.0197 and the worst pair was 0.0324; the idle CPU median
    // was about 0.002. Five percent clears that noise and still rejects
    // about 500 ms of extra CPU on a 10 s arm.
    internal const double CpuMedianBound = 0.05d;

    // Other runnable work stretches wall time. The worst recorded wall
    // median was 0.067 at host load about 33 (worst pair 0.160), so a 2%
    // wall median flakes. Ten percent is 1.5 times that median. The bound
    // is the median, so one pair may still exceed it. Equality fails.
    internal const double WallMedianBound = 0.10d;

    internal static double Ratio(double on, double off) =>
        off <= 0d ? double.PositiveInfinity : (on - off) / off;

    // Nine paired OFF/ON ratios from one process. Accept when both medians
    // stay under the bounds above and every ratio is finite. A zero baseline
    // is +Inf and a NaN sorts below every finite value, so either one would
    // otherwise leave the median inside the bound. The JSON evidence still
    // records the wall bootstrap upper bound.
    internal static bool Accepts(IReadOnlyList<double> wallRatios, IReadOnlyList<double> cpuRatios)
    {
        if (wallRatios.Count < PairedOverheadStatistics.MinimumPairs
            || cpuRatios.Count != wallRatios.Count)
            return false;
        for (var i = 0; i < wallRatios.Count; i++)
        {
            if (!double.IsFinite(wallRatios[i]) || !double.IsFinite(cpuRatios[i]))
                return false;
        }

        return PairedOverheadStatistics.Median(cpuRatios) < CpuMedianBound
            && PairedOverheadStatistics.Median(wallRatios) < WallMedianBound;
    }
}
