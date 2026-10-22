using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1428 V-5/V-6 (D-4, D-5, D-6): the same-card inspection result store on a fake clock. A
/// result is not consumed by being read; it lives until it is invalidated, its intent changes, or
/// the retention cap expires, and completed results do not count against the in-flight capacity.
/// </summary>
[Category("Unit")]
public sealed class SiblingInspectionResultsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 21, 0, 0, TimeSpan.Zero);

    private static CardSiblingInspectionIntent Intent(Guid? task = null, int attempt = 0) => new(
        task ?? Guid.NewGuid(), attempt, Guid.NewGuid(), Path.Combine(Path.GetTempPath(), "c1428-repo"), null,
        RequestedWorktreeBaseMode.Target, null, null, null, "master");

    private static SiblingBaseGuard Completed(SiblingInspectionResults store, CardSiblingInspectionIntent intent)
    {
        var entry = store.Begin(intent, out var started).ShouldNotBeNull();
        started.ShouldBeTrue();
        var result = new SiblingBaseGuard(null, [], "master", Metadata: Guid.NewGuid().ToString("N"));
        store.Complete(entry, result);
        return result;
    }

    [Test]
    public void C1428_a_result_survives_repeated_peeks_until_invalidated()
    {
        var clock = new FakeTimeProvider(T0);
        var store = new SiblingInspectionResults(clock);
        var intent = Intent();
        var result = Completed(store, intent);

        clock.Advance(TimeSpan.FromSeconds(1));
        store.Peek(intent).ShouldBeSameAs(result, "the first read");
        clock.Advance(TimeSpan.FromSeconds(59));
        store.Peek(intent).ShouldBeSameAs(result, "a read is not a consumption (T0+60s)");
        clock.Advance(TimeSpan.FromMinutes(8));
        store.Peek(intent).ShouldBeSameAs(result, "still the same instance at T0+9min");
        store.Count.ShouldBe(1);

        store.Invalidate(intent.TaskId);
        store.Peek(intent).ShouldBeNull("invalidated by its owner");
        store.Count.ShouldBe(0);

        // An intent change evicts the entry: the old result answers neither intent.
        var again = Completed(store, intent);
        store.Peek(intent).ShouldBeSameAs(again);
        var changed = intent with { Attempt = intent.Attempt + 1 };
        store.Peek(changed).ShouldBeNull("a different intent for the same task evicts the entry");
        store.Peek(intent).ShouldBeNull();
        store.Count.ShouldBe(0);
    }

    [Test]
    public void C1428_retained_result_expires_at_the_cap_not_at_30s()
    {
        var clock = new FakeTimeProvider(T0);
        var store = new SiblingInspectionResults(clock);
        var intent = Intent();
        var result = Completed(store, intent);
        SiblingInspectionResults.RetainedResultMaxAge.ShouldBe(TimeSpan.FromMinutes(10), "D-5: the cap is 10 minutes");

        clock.Advance(TimeSpan.FromSeconds(31));
        store.Peek(intent).ShouldBeSameAs(result, "the 30 s limit was the defect");
        clock.Advance(SiblingInspectionResults.RetainedResultMaxAge - TimeSpan.FromSeconds(32));
        store.Peek(intent).ShouldBeSameAs(result, "one second inside the cap");

        clock.Advance(TimeSpan.FromSeconds(1));
        store.Peek(intent).ShouldBeNull("evicted at the cap");
        store.Count.ShouldBe(0, "an expired result is not retained");

        // Reconcile evicts an expired result on its own, without a peek.
        var other = Intent();
        Completed(store, other);
        clock.Advance(SiblingInspectionResults.RetainedResultMaxAge);
        store.Reconcile(new HashSet<Guid> { other.TaskId });
        store.Count.ShouldBe(0);
    }

    [Test]
    public void C1428_completed_results_do_not_consume_enqueue_capacity()
    {
        var clock = new FakeTimeProvider(T0);
        var store = new SiblingInspectionResults(clock);
        for (var i = 0; i < SiblingInspectionResults.InFlightCapacity; i++)
            Completed(store, Intent());
        store.Count.ShouldBe(32);
        store.InFlightCount.ShouldBe(0);

        var admitted = store.Begin(Intent(), out var started);
        admitted.ShouldNotBeNull("32 completed results leave the in-flight capacity free");
        started.ShouldBeTrue();

        var busy = new SiblingInspectionResults(clock);
        for (var i = 0; i < SiblingInspectionResults.InFlightCapacity; i++)
            busy.Begin(Intent(), out _).ShouldNotBeNull();
        busy.InFlightCount.ShouldBe(32);
        busy.Begin(Intent(), out started).ShouldBeNull("32 in-flight entries are the capacity");
        started.ShouldBeFalse();
    }
}
