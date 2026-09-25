using Antiphon.Server.Application.Services;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class CompletionNoteWorkQueueTests
{
    [Test]
    public void Duplicate_events_coalesce_and_events_during_work_survive()
    {
        var queue = new CompletionNoteRecoveryQueue();
        var id = Guid.NewGuid();
        queue.Check(id);
        queue.Check(id);
        queue.Take(DateTime.UtcNow).Tasks.ShouldBe(new[] { id });
        queue.Check(id);
        queue.Take(DateTime.UtcNow).Tasks.ShouldBe(new[] { id });
        queue.Take(DateTime.UtcNow).Tasks.ShouldBeEmpty();
    }

    [Test]
    public void Overflow_requests_a_sweep_and_retains_a_bounded_batch()
    {
        var queue = new CompletionNoteRecoveryQueue();
        for (var i = 0; i < 129; i++) queue.Check(Guid.NewGuid());
        var batch = queue.Take(DateTime.UtcNow);
        batch.Sweep.ShouldBeTrue();
        batch.Tasks.Length.ShouldBe(128);
        queue.Take(DateTime.UtcNow).Sweep.ShouldBeFalse();
    }

    [Test]
    public async Task Held_publication_wakes_at_its_deadline_and_release_can_wake_it_early()
    {
        var queue = new CompletionNoteRecoveryQueue();
        var clock = new FakeTimeProvider();
        var id = Guid.NewGuid();
        var due = clock.GetUtcNow().UtcDateTime.AddSeconds(45);
        queue.ScheduleFlush(id, due);
        queue.Take(clock.GetUtcNow().UtcDateTime).Sessions.ShouldBeEmpty();
        var wait = queue.WaitAsync(due.AddMinutes(15), clock, default);
        clock.Advance(TimeSpan.FromSeconds(44));
        wait.IsCompleted.ShouldBeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));
        await wait.WaitAsync(TimeSpan.FromSeconds(2));
        queue.Take(clock.GetUtcNow().UtcDateTime).Sessions.ShouldBe(new[] { id });
        queue.ScheduleFlush(id, due.AddMinutes(1));
        queue.ScheduleFlush(id, DateTime.MinValue);
        queue.Take(clock.GetUtcNow().UtcDateTime).Sessions.ShouldBe(new[] { id });
    }

    [Test]
    public async Task Publication_during_a_flush_is_not_lost()
    {
        var queue = new CompletionNoteFlushQueue();
        var id = Guid.NewGuid();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var reader = queue.ReadAllAsync(stop.Token).GetAsyncEnumerator();
        queue.TryEnqueue(id).ShouldBeTrue();
        (await reader.MoveNextAsync()).ShouldBeTrue();
        reader.Current.ShouldBe(id);
        queue.TryEnqueue(id).ShouldBeTrue();
        queue.Complete(id);
        (await reader.MoveNextAsync()).ShouldBeTrue();
        reader.Current.ShouldBe(id);
        queue.Complete(id);
    }
}
