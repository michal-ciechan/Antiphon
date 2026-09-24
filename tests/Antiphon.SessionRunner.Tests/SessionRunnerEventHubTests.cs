using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public class SessionRunnerEventHubTests
{
    [Test]
    public async Task Concurrent_publishers_keep_exact_pending_depth()
    {
        const int publishers = 8;
        const int each = 50;
        const int total = publishers * each;
        var overflow = 0;
        var hub = new SessionRunnerEventHub();
        using var cts = new CancellationTokenSource();
        var reader = hub.SubscribeBounded(
            10_000, 10_000_000, () => Interlocked.Exchange(ref overflow, 1), cts.Token);
        var lease = (ISessionRunnerEventLease)reader;
        var json = JsonSerializer.Serialize(new { n = "fixed" });
        var eachBytes = SessionRunnerEventHub.MeasuredBytes("e", json);
        // Dedicated threads: a pool Barrier waits for workers the pool will not inject while they are blocked.
        var tasks = Enumerable.Range(0, publishers).Select(_ => Task.Factory.StartNew(() =>
        {
            for (var i = 0; i < each; i++)
                hub.Publish("e", new { n = "fixed" });
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        await Task.WhenAll(tasks);
        Volatile.Read(ref overflow).ShouldBe(0);
        lease.PendingEvents.ShouldBe(total);
        lease.PendingBytes.ShouldBe(total * eachBytes);

        var got = 0;
        while (reader.TryRead(out var evt))
        {
            lease.Release(evt);
            got++;
        }

        got.ShouldBe(total);
        lease.PendingEvents.ShouldBe(0);
        lease.PendingBytes.ShouldBe(0);
        cts.Cancel();
    }

    [Test]
    public async Task Cancel_before_read_releases_unread_reservations()
    {
        const int count = 5;
        var hub = new SessionRunnerEventHub();
        using var cts = new CancellationTokenSource();
        var reader = hub.SubscribeBounded(100, 1_000_000, () => { }, cts.Token);
        var lease = (ISessionRunnerEventLease)reader;
        var each = SessionRunnerEventHub.MeasuredBytes("e", JsonSerializer.Serialize(new { n = 1 }));
        for (var i = 0; i < count; i++)
            hub.Publish("e", new { n = 1 });

        lease.PendingEvents.ShouldBe(count);
        lease.PendingBytes.ShouldBe(count * each);

        // A publisher still inside Accept must not leave a reservation after the subscription closes.
        var publishing = Task.Factory.StartNew(() =>
        {
            for (var i = 0; i < 200; i++)
                hub.Publish("e", new { n = 1 });
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        cts.Cancel();
        await publishing;
        lease.PendingEvents.ShouldBe(0);
        lease.PendingBytes.ShouldBe(0);
        await reader.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Read_some_then_cancel_releases_the_remainder()
    {
        const int count = 5;
        const int read = 2;
        var hub = new SessionRunnerEventHub();
        using var cts = new CancellationTokenSource();
        var reader = hub.SubscribeBounded(100, 1_000_000, () => { }, cts.Token);
        var lease = (ISessionRunnerEventLease)reader;
        var each = SessionRunnerEventHub.MeasuredBytes("e", JsonSerializer.Serialize(new { n = 1 }));
        for (var i = 0; i < count; i++)
            hub.Publish("e", new { n = 1 });

        var got = 0;
        while (got < read && reader.TryRead(out var evt))
        {
            lease.Release(evt);
            got++;
        }

        got.ShouldBe(read);
        lease.PendingEvents.ShouldBe(count - read);
        lease.PendingBytes.ShouldBe((count - read) * each);
        cts.Cancel();
        lease.PendingEvents.ShouldBe(0);
        lease.PendingBytes.ShouldBe(0);
    }
}
