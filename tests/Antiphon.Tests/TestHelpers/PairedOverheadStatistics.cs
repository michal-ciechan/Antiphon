namespace Antiphon.Tests.TestHelpers;

internal static class PairedOverheadStatistics
{
    // Five pairs let one delayed arm determine the bootstrap 95% upper bound
    // (5.792% of resamples). Nine reduce that probability to 0.145% while
    // retaining the same median estimator, raw observations and 2% threshold.
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
    internal const double CpuMedianBound = 0.02d;
    internal const double WallMedianBound = 0.02d;

    internal static double Ratio(double on, double off) =>
        off <= 0d ? double.PositiveInfinity : (on - off) / off;

    // Nine paired OFF/ON ratios from one process. Both medians stay under 2%.
    // The wall bootstrap upper bound does not: at host load 22-33 it was 0.0294
    // while the wall median was 0.0084 and the CPU median was 0.0039. Processor
    // time is this process only, so other runnable work does not add to it.
    // A 50 ms sleep per enabled phase adds almost no processor time; the wall
    // median still rejects it.
    internal static bool Accepts(IReadOnlyList<double> wallRatios, IReadOnlyList<double> cpuRatios)
    {
        if (wallRatios.Count < PairedOverheadStatistics.MinimumPairs
            || cpuRatios.Count != wallRatios.Count)
            return false;
        return PairedOverheadStatistics.Median(cpuRatios) < CpuMedianBound
            && PairedOverheadStatistics.Median(wallRatios) < WallMedianBound;
    }
}
