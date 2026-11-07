using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit"), Timeout(2_000)]
public class C1514HostLoadAggregateTests
{
    private static DateTimeOffset T0 => new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    [Test]
    public void ExactWindowsMatchLiteralStatistics()
    {
        var values = Enumerable.Repeat(0d, 19).Append(100d).Concat(Enumerable.Repeat(20d, 20)).ToArray();
        var points = values.Select((v, i) => new HostLoadMetricPoint(i.ToString(), T0.AddSeconds(i * 5), v)).ToArray();
        var aggregate = new HostLoadAggregation();
        var actual = aggregate.Exact(points, T0, T0.AddSeconds(200));
        actual.Avg.ShouldBe(12.5d); actual.Min.ShouldBe(0d); actual.Max.ShouldBe(100d); actual.P95.ShouldBe(20d);
        actual.Sum.ShouldBe(500d); actual.ValidCount.ShouldBe(40); actual.ExpectedCount.ShouldBe(40); actual.Coverage.ShouldBe(1d);
        actual.P95Approximate.ShouldBeFalse(); actual.Complete.ShouldBeTrue();
        var gap = aggregate.Exact([new("one", T0, 0), new("two", T0.AddSeconds(5), null),
            new("three", T0.AddSeconds(10), 100), new("boundary", T0.AddSeconds(15), 999)], T0, T0.AddSeconds(15));
        gap.Avg.ShouldBe(50d); gap.ValidCount.ShouldBe(2); gap.ExpectedCount.ShouldBe(3); gap.Complete.ShouldBeFalse();
        var replay = aggregate.Exact(points.Concat([points[0]]), T0, T0.AddSeconds(200));
        replay.ValidCount.ShouldBe(40); replay.Sum.ShouldBe(500d);
    }
    [Test]
    public void HistogramPreservesCountsAndErrorBound()
    {
        var first = new HostLoadHistogram(0.01); var second = new HostLoadHistogram(0.01);
        for (var i = 0; i <= 4096; i++) first.Add(i / 100d);
        for (var i = 4096; i >= 0; i--) second.Add(i / 100d);
        foreach (var histogram in new[] { first, second })
        {
            var summary = histogram.Summary(4097);
            summary.ValidCount.ShouldBe(4097); summary.Sum!.Value.ShouldBe(83906.56d, 0.0000001);
            summary.Min.ShouldBe(0d); summary.Max.ShouldBe(40.96d);
            histogram.BinCount.ShouldBeLessThanOrEqualTo(4096); histogram.BinWidth.ShouldBeGreaterThan(0.01);
            summary.P95Approximate.ShouldBeTrue(); summary.P95ErrorBound.ShouldBe(histogram.BinWidth);
            summary.P95!.Value.ShouldBeGreaterThanOrEqualTo(38.92); summary.P95.Value.ShouldBeLessThanOrEqualTo(38.92 + histogram.BinWidth);
        }
        var uneven = new HostLoadHistogram(0.01); uneven.Add(0); uneven.Merge(first); uneven.Merge(second);
        var merged = uneven.Summary(8195); merged.ValidCount.ShouldBe(8195); merged.Sum!.Value.ShouldBe(167813.12d, 0.000001);
        merged.Min.ShouldBe(0d); merged.Max.ShouldBe(40.96d); uneven.BinCount.ShouldBeLessThanOrEqualTo(4096);
    }
    [Test]
    public void EmptyMetricsStayUnknown()
    {
        var aggregate = new HostLoadAggregation();
        foreach (var points in new HostLoadMetricPoint[][] { [], [new("null", T0, null)] })
        {
            var summary = aggregate.Exact(points, T0, T0.AddSeconds(10));
            summary.ValidCount.ShouldBe(0); summary.Avg.ShouldBeNull(); summary.Min.ShouldBeNull(); summary.Max.ShouldBeNull();
            summary.P95.ShouldBeNull(); summary.Sum.ShouldBeNull(); summary.ExpectedCount.ShouldBe(2); summary.Coverage.ShouldBe(0d);
        }
        var zero = aggregate.Exact([new("zero", T0, 0)], T0, T0.AddSeconds(10));
        zero.ValidCount.ShouldBe(1); zero.Avg.ShouldBe(0d); zero.P95.ShouldBe(0d); zero.Coverage.ShouldBe(0.5); zero.Complete.ShouldBeFalse();
    }
}
