using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Services;

/// <summary>Fixed-cadence arithmetic, half-open UTC windows and exact raw nearest-rank p95.</summary>
public sealed class HostLoadAggregation
{
    public static long ExpectedCount(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from) return 0;
        const long slot = 5 * TimeSpan.TicksPerSecond;
        var first = (from.UtcTicks + slot - 1) / slot;
        var last = (to.UtcTicks - 1) / slot;
        return Math.Max(0, last - first + 1);
    }
    public HostLoadMetricSummary Exact(IEnumerable<HostLoadMetricPoint> points, DateTimeOffset from, DateTimeOffset to)
    {
        var values = points.Where(p => p.At >= from && p.At < to).DistinctBy(p => p.Identity)
            .Select(p => p.Value).Where(v => v is { } n && double.IsFinite(n) && n >= 0).Select(v => v!.Value).Order().ToArray();
        var expected = ExpectedCount(from, to);
        if (values.Length == 0) return new(null, null, null, null, null, 0, expected, 0, false, false, null);
        var sum = values.Sum(); var count = values.LongLength;
        var p95 = values[(int)Math.Ceiling(.95 * count) - 1];
        return new(sum / count, values[0], values[^1], p95, sum, count, expected,
            expected == 0 ? 0 : (double)count / expected, count == expected, false, 0);
    }
}

/// <summary>Mergeable bounded histogram. Width is doubled deterministically; extrema and sum remain observations.</summary>
public sealed class HostLoadHistogram
{
    private readonly SortedDictionary<long, long> _bins = [];
    private long _count;
    private double _sum;
    private double? _min, _max;
    public double BinWidth { get; private set; }
    public int BinCount => _bins.Count;
    public HostLoadHistogram(double binWidth)
    {
        if (!double.IsFinite(binWidth) || binWidth <= 0) throw new ArgumentOutOfRangeException(nameof(binWidth));
        BinWidth = binWidth;
    }
    public HostLoadHistogram(HostLoadHistogramState state) : this(state.BinWidth)
    {
        foreach (var (key, count) in state.Bins)
        {
            if (key < 0 || count < 1) throw new ArgumentException("invalid_host_load_histogram");
            _bins.Add(key, count);
        }
        if (_bins.Count > 4096 || _bins.Values.Sum() != state.Count || !double.IsFinite(state.Sum))
            throw new ArgumentException("invalid_host_load_histogram");
        _count = state.Count; _sum = state.Sum; _min = state.Min; _max = state.Max;
    }
    public HostLoadHistogramState State() => new(BinWidth, new Dictionary<long, long>(_bins), _count, _sum, _min, _max);
    public void Add(double value)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        while (value / BinWidth >= long.MaxValue) Rebin();
        // Decimal division avoids an exact decimal boundary falling into its preceding bin.
        var key = (long)decimal.Floor((decimal)value / (decimal)BinWidth);
        _bins[key] = checked(_bins.GetValueOrDefault(key) + 1);
        _count = checked(_count + 1); _sum += value;
        _min = _min is null ? value : Math.Min(_min.Value, value);
        _max = _max is null ? value : Math.Max(_max.Value, value);
        while (_bins.Count > 4096) Rebin();
    }
    public void Merge(HostLoadHistogram other)
    {
        if (other._count == 0) return;
        if (ReferenceEquals(this, other)) other = new HostLoadHistogram(other.State());
        while (BinWidth < other.BinWidth) Rebin();
        var ratio = BinWidth / other.BinWidth;
        if (ratio < 1 || ratio != Math.Floor(ratio) || (Math.Log2(ratio) % 1) != 0)
            throw new ArgumentException("incompatible_host_load_histograms");
        foreach (var (bin, count) in other._bins)
        {
            var target = (long)Math.Floor(bin / ratio);
            _bins[target] = checked(_bins.GetValueOrDefault(target) + count);
        }
        _count = checked(_count + other._count); _sum += other._sum;
        _min = _min is null ? other._min : Math.Min(_min.Value, other._min!.Value);
        _max = _max is null ? other._max : Math.Max(_max.Value, other._max!.Value);
        while (_bins.Count > 4096) Rebin();
    }
    private void Rebin()
    {
        if (!double.IsFinite(BinWidth * 2)) throw new ArgumentException("host_load_histogram_overflow");
        var rebinned = new SortedDictionary<long, long>();
        foreach (var (key, count) in _bins) rebinned[key / 2] = checked(rebinned.GetValueOrDefault(key / 2) + count);
        _bins.Clear(); foreach (var bin in rebinned) _bins.Add(bin.Key, bin.Value);
        BinWidth *= 2;
    }
    public HostLoadMetricSummary Summary(long expectedCount)
    {
        if (_count == 0) return new(null, null, null, null, null, 0, expectedCount, 0, false, true, BinWidth, BinWidth: BinWidth);
        var rank = (long)Math.Ceiling(.95 * _count); long cumulative = 0; double? p95 = null;
        foreach (var (bin, count) in _bins)
        {
            cumulative += count;
            if (cumulative >= rank) { p95 = (double)((bin + 1) * (decimal)BinWidth); break; }
        }
        return new(_sum / _count, _min, _max, p95, _sum, _count, expectedCount,
            expectedCount == 0 ? 0 : (double)_count / expectedCount, _count == expectedCount,
            true, BinWidth, BinWidth: BinWidth);
    }
}
