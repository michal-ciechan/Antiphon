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
