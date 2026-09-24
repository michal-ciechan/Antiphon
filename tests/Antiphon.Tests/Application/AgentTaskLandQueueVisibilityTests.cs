using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class AgentTaskLandQueueVisibilityTests
{
    [Test]
    public async Task C641_Snapshot_matches_channel_order_and_running_owner()
    {
        var queue = new AgentTaskLandQueue();
        var now = new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var aRequest = Guid.NewGuid();
        var bRequest = Guid.NewGuid();
        var cRequest = Guid.NewGuid();
        queue.TryEnqueue(a, null, aRequest).ShouldBeTrue();
        queue.TryEnqueue(b, null, bRequest).ShouldBeTrue();
        queue.TryEnqueue(c, null, cRequest).ShouldBeTrue();

        var before = queue.Capture(now);
        before.ObservedAt.ShouldBe(now);
        before.Executing.ShouldBeNull();
        before.WaitingCount.ShouldBe(3);
        before.Waiting.Select(entry => entry.TaskId).ShouldBe([a, b, c]);
        before.WaitingPosition(a, aRequest).ShouldBe(1);
        before.WaitingPosition(b, bRequest).ShouldBe(2);
        before.WaitingPosition(c, cRequest).ShouldBe(3);

        queue.TryDequeue(out var first).ShouldBeTrue();
        first.TaskId.ShouldBe(a);
        first.RequestId.ShouldBe(aRequest);
        var running = queue.Capture(now);
        running.Executing.ShouldNotBeNull();
        running.Executing!.TaskId.ShouldBe(a);
        running.Executing.RequestId.ShouldBe(aRequest);
        running.IsExecuting(a, aRequest).ShouldBeTrue();
        running.WaitingPosition(a, aRequest).ShouldBeNull();
        running.Waiting.Select(entry => entry.RequestId).ShouldBe([bRequest, cRequest]);
        running.WaitingPosition(b, bRequest).ShouldBe(1);
        running.WaitingPosition(c, cRequest).ShouldBe(2);

        queue.TryDequeue(out var second).ShouldBeTrue();
        second.RequestId.ShouldBe(bRequest);
        queue.TryDequeue(out var third).ShouldBeTrue();
        third.RequestId.ShouldBe(cRequest);
        queue.TryDequeue(out _).ShouldBeFalse();

        var viaRead = new AgentTaskLandQueue();
        viaRead.TryEnqueue(a, null, aRequest).ShouldBeTrue();
        viaRead.TryEnqueue(b, null, bRequest).ShouldBeTrue();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var reader = viaRead.ReadAllAsync(cts.Token).GetAsyncEnumerator();
        try
        {
            (await reader.MoveNextAsync()).ShouldBeTrue();
            reader.Current.TaskId.ShouldBe(a);
            var promoted = viaRead.Capture(now);
            promoted.Executing.ShouldNotBeNull();
            promoted.Executing!.RequestId.ShouldBe(aRequest);
            promoted.WaitingPosition(a, aRequest).ShouldBeNull();
            promoted.Waiting.Select(entry => entry.RequestId).ShouldBe([bRequest]);
            promoted.WaitingPosition(b, bRequest).ShouldBe(1);
        }
        finally
        {
            cts.Cancel();
            try
            {
                await reader.DisposeAsync();
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [Test]
    public void C641_Duplicate_enqueue_and_release_preserve_positions()
    {
        var queue = new AgentTaskLandQueue();
        var now = new DateTime(2026, 9, 24, 1, 0, 0, DateTimeKind.Utc);
        var task = Guid.NewGuid();
        var first = Guid.NewGuid();
        var rejected = Guid.NewGuid();
        queue.TryEnqueue(task, "first", first).ShouldBeTrue();
        queue.TryEnqueue(task, "later", rejected).ShouldBeFalse();
        var blocked = queue.Capture(now);
        blocked.WaitingCount.ShouldBe(1);
        blocked.Waiting.Single().RequestId.ShouldBe(first);
        blocked.Executing.ShouldBeNull();
        queue.PendingCount.ShouldBe(1);

        queue.TryDequeue(out var running).ShouldBeTrue();
        running.RequestId.ShouldBe(first);
        var executing = queue.Capture(now);
        executing.Executing!.RequestId.ShouldBe(first);
        executing.Waiting.ShouldBeEmpty();
        queue.Release(task);
        var released = queue.Capture(now);
        released.Executing.ShouldBeNull();
        released.Waiting.ShouldBeEmpty();
        queue.IsActive(task).ShouldBeFalse();

        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        queue.TryEnqueue(task, null, older).ShouldBeTrue();
        queue.Release(task);
        var early = queue.Capture(now);
        early.Executing.ShouldBeNull();
        early.Waiting.Select(entry => entry.RequestId).ShouldBe([older]);
        queue.IsActive(task).ShouldBeFalse();
        queue.TryEnqueue(task, null, newer).ShouldBeTrue();
        var both = queue.Capture(now);
        both.Executing.ShouldBeNull();
        both.Waiting.Select(entry => entry.RequestId).ShouldBe([older, newer]);
        both.Waiting[0].EntryId.ShouldNotBe(both.Waiting[1].EntryId);
        both.WaitingPosition(task, older).ShouldBe(1);
        both.WaitingPosition(task, newer).ShouldBe(2);

        queue.TryDequeue(out var oldItem).ShouldBeTrue();
        oldItem.RequestId.ShouldBe(older);
        var after = queue.Capture(now);
        after.Executing!.RequestId.ShouldBe(older);
        after.Waiting.Select(entry => entry.RequestId).ShouldBe([newer]);
        after.WaitingPosition(task, newer).ShouldBe(1);
        after.WaitingPosition(task, older).ShouldBeNull();
    }

    [Test]
    public void C641_Old_entry_completion_cannot_release_new_requeue_claim()
    {
        var queue = new AgentTaskLandQueue();
        var task = Guid.NewGuid();
        var oldRequest = Guid.NewGuid();
        var newRequest = Guid.NewGuid();
        queue.TryEnqueue(task, null, oldRequest).ShouldBeTrue();
        queue.TryDequeue(out var oldItem).ShouldBeTrue();

        queue.Release(task); // request reset before the old worker finishes
        queue.TryEnqueue(task, null, newRequest).ShouldBeTrue();
        queue.Release(oldItem); // old worker's eventual finally

        queue.IsActive(task).ShouldBeTrue();
        queue.PendingCount.ShouldBe(1);
        queue.TryEnqueue(task, null, Guid.NewGuid()).ShouldBeFalse();
        var snapshot = queue.Capture(DateTime.UtcNow);
        snapshot.Executing.ShouldBeNull();
        snapshot.Waiting.Select(entry => entry.RequestId).ShouldBe([newRequest]);

        queue.TryDequeue(out var newItem).ShouldBeTrue();
        newItem.RequestId.ShouldBe(newRequest);
        queue.Release(newItem);
        queue.IsActive(task).ShouldBeFalse();
    }

    [Test]
    public async Task C641_Concurrent_enqueue_snapshot_is_consistent()
    {
        var queue = new AgentTaskLandQueue();
        var ids = Enumerable.Range(0, 32).Select(_ => (TaskId: Guid.NewGuid(), RequestId: Guid.NewGuid())).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observations = new List<LandQueueSnapshot>();
        var workers = ids.Select(id => Task.Run(() =>
        {
            start.Task.Wait();
            queue.TryEnqueue(id.TaskId, null, id.RequestId).ShouldBeTrue();
        })).ToArray();
        var observer = Task.Run(() =>
        {
            start.Task.Wait();
            for (var i = 0; i < 64; i++)
                observations.Add(queue.Capture(DateTime.UtcNow));
        });
        start.SetResult();
        await Task.WhenAll(workers);
        await observer;

        var final = queue.Capture(DateTime.UtcNow);
        final.Executing.ShouldBeNull();
        final.WaitingCount.ShouldBe(ids.Length);
        final.Waiting.Select(entry => entry.TaskId).Distinct().Count().ShouldBe(ids.Length);
        var finalOrder = final.Waiting.Select(entry => entry.TaskId).ToArray();
        foreach (var snapshot in observations)
        {
            snapshot.Executing.ShouldBeNull();
            var seen = snapshot.Waiting.Select(entry => entry.TaskId).ToArray();
            seen.Distinct().Count().ShouldBe(seen.Length);
            finalOrder.Take(seen.Length).ShouldBe(seen);
        }

        var dequeued = new List<Guid>();
        while (queue.TryDequeue(out var item))
            dequeued.Add(item.TaskId);
        dequeued.ShouldBe(finalOrder);
    }

    [Test]
    public void C641_Restart_has_no_invented_running_owner()
    {
        var populated = new AgentTaskLandQueue();
        var task = Guid.NewGuid();
        populated.TryEnqueue(task, null, Guid.NewGuid()).ShouldBeTrue();
        populated.TryDequeue(out _).ShouldBeTrue();
        populated.Capture(DateTime.UtcNow).Executing.ShouldNotBeNull();

        var restarted = new AgentTaskLandQueue();
        var now = new DateTime(2026, 9, 24, 2, 0, 0, DateTimeKind.Utc);
        var snapshot = restarted.Capture(now);
        snapshot.ObservedAt.ShouldBe(now);
        snapshot.Executing.ShouldBeNull();
        snapshot.Waiting.ShouldBeEmpty();
        snapshot.WaitingCount.ShouldBe(0);
        restarted.TryDequeue(out _).ShouldBeFalse();
        restarted.IsActive(task).ShouldBeFalse();
        restarted.PendingCount.ShouldBe(0);
    }
}
