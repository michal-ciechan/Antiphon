using Antiphon.Agents.Pty;
using FluentAssertions;
using Xunit;

namespace Antiphon.Agents.Pty.Tests;

[Trait("Category", "Unit")]
public class RingBufferTests
{
    [Fact]
    public void Overwrites_oldest_when_full()
    {
        var rb = new RingBuffer<int>(3);
        rb.Add(1); rb.Add(2); rb.Add(3); rb.Add(4); rb.Add(5);
        rb.Count.Should().Be(3);
        rb.Snapshot().Should().Equal(3, 4, 5);
    }

    [Fact]
    public void Under_capacity_returns_in_order()
    {
        var rb = new RingBuffer<string>(5);
        rb.Add("a"); rb.Add("b");
        rb.Snapshot().Should().Equal("a", "b");
    }

    [Fact]
    public void Exactly_at_capacity_no_overwrite()
    {
        var rb = new RingBuffer<int>(3);
        rb.Add(10); rb.Add(20); rb.Add(30);
        rb.Count.Should().Be(3);
        rb.Snapshot().Should().Equal(10, 20, 30);
    }

    [Fact]
    public void Capacity_immutable_count_clamped()
    {
        var rb = new RingBuffer<int>(2);
        rb.Capacity.Should().Be(2);
        for (int i = 0; i < 100; i++) rb.Add(i);
        rb.Count.Should().Be(2);
        rb.Capacity.Should().Be(2);
    }

    [Fact]
    public void Snapshot_returns_independent_copy()
    {
        var rb = new RingBuffer<int>(3);
        rb.Add(1); rb.Add(2);
        var snap = rb.Snapshot();
        rb.Add(3);
        snap.Should().Equal(1, 2);
        rb.Snapshot().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Empty_snapshot_returns_empty_array()
    {
        var rb = new RingBuffer<int>(5);
        rb.Snapshot().Should().BeEmpty();
        rb.Count.Should().Be(0);
    }

    [Fact]
    public void Constructor_rejects_zero_or_negative_capacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(-1));
    }

    [Fact]
    public async Task Concurrent_adds_and_snapshots_do_not_throw()
    {
        var rb = new RingBuffer<int>(64);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var writers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            int i = 0;
            while (!cts.IsCancellationRequested) rb.Add(i++);
        })).ToArray();
        var readers = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                var snap = rb.Snapshot();
                snap.Length.Should().BeLessThanOrEqualTo(64);
            }
        })).ToArray();

        await Task.WhenAll(writers.Concat(readers));
        rb.Count.Should().BeLessThanOrEqualTo(64);
    }
}
