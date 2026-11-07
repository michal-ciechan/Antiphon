using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Services;

// API skeleton for the committed red-first arithmetic witnesses; implemented in the next slice.
public sealed class HostLoadAggregation
{
    public HostLoadMetricSummary Exact(IEnumerable<HostLoadMetricPoint> points, DateTimeOffset from, DateTimeOffset to)
        => new(null, null, null, null, null, 0, 0, 0, false, false, null);
}
public sealed class HostLoadHistogram(double binWidth)
{
    public double BinWidth => binWidth;
    public int BinCount => 0;
    public void Add(double value) { }
    public void Merge(HostLoadHistogram other) { }
    public HostLoadMetricSummary Summary(long expectedCount) => new(null, null, null, null, null, 0, expectedCount, 0, false, true, binWidth, BinWidth: binWidth);
}
