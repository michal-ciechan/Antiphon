using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1205 S1. Production blobs under test match origin/master
/// a61db0f08448b87421b8dd9334f9f2631691cde7 (pump, HTTP client, runtime, hub, events route).
/// CP-1 is the expected-red baseline: T01 fails on the missing end; T02's grace pull passes.
/// </summary>
[Category("Integration")]
public class TurnEndGapReproductionTests
{
    [Test]
    public async Task C1205_Gap_end_is_stored_before_any_grace_pull(CancellationToken ct)
    {
        // V-1/R-1. Case: TurnEnd published with no hub subscriber, then one subscribed keepalive.
        // Oracle: that end is stored once and the caller is idle before any grace pull.
        // PC-1 removes the post-readiness catch-up; preGraceEndCount.ShouldBe(1) fails while the
        // grace control still stores the same end. Unmodified production has no second catch-up,
        // so this assertion is the baseline red (actual 0).
        await using var harness = await TurnEndGapHarness.CreateAsync();
        await harness.ReachSubscribedKeepaliveAsync(ct);
        (await harness.CountAsync(harness.PriorEndUuid, TranscriptKinds.TurnEnd)).ShouldBe(1);
        (await harness.CountAsync(harness.ToolCallUuid, TranscriptKinds.ToolCall)).ShouldBe(1);
        AssertChronological(harness.Order,
            "caller-transcript",
            "other-pull-entered",
            "custody-append",
            "hub-publish subscribers=0",
            "other-pull-released",
            "connected-consumed",
            "hub-subscribed replay=false",
            "keepalive-consumed",
            "post-keepalive-read");

        var preGraceEndCount = await harness.CountAsync(harness.MissingEndUuid, TranscriptKinds.TurnEnd);
        var preGraceSqlWorking = await harness.SqlWorkingAsync(ct);
        var preGraceCacheWorking = await harness.CacheWorkingAsync(ct);
        var stored = await harness.GracePullAsync(ct);
        var postGraceEndCount = await harness.CountAsync(harness.MissingEndUuid, TranscriptKinds.TurnEnd);
        var postGraceSqlWorking = await harness.SqlWorkingAsync(ct);
        var postGraceCacheWorking = await harness.CacheWorkingAsync(ct);
        Console.WriteLine(
            "C1205 T01 order " + string.Join(" > ", harness.Order)
            + $" preGraceEndCount={preGraceEndCount} preSql={preGraceSqlWorking} preCache={preGraceCacheWorking}"
            + $" graceStored={stored} postGraceEndCount={postGraceEndCount} postSql={postGraceSqlWorking}"
            + $" postCache={postGraceCacheWorking} uuid={harness.MissingEndUuid} gets={harness.CallerTranscriptGets}");

        postGraceEndCount.ShouldBe(1);
        postGraceSqlWorking.ShouldBeFalse();
        postGraceCacheWorking.ShouldBeFalse();
        preGraceEndCount.ShouldBe(1);
        preGraceSqlWorking.ShouldBeFalse();
        preGraceCacheWorking.ShouldBeFalse();
    }

    [Test]
    public async Task C1205_Grace_pull_recovers_the_unsubscribed_tail(CancellationToken ct)
    {
        // V-2. Case: subscription stays unregistered; the tail is absent until CatchUpTranscriptAsync.
        // PC-2 returns before PullAndPersistAsync; postGraceEndCount.ShouldBe(1) fails.
        await using var harness = await TurnEndGapHarness.CreateAsync();
        await harness.ReachUnsubscribedTailAsync(ct);
        harness.SubscribeCount.ShouldBe(0);
        AssertChronological(harness.Order,
            "caller-transcript",
            "other-pull-entered",
            "custody-append",
            "hub-publish subscribers=0");
        var beforeEndCount = await harness.CountAsync(harness.MissingEndUuid, TranscriptKinds.TurnEnd);
        var beforeSqlWorking = await harness.SqlWorkingAsync(ct);
        var beforeCacheWorking = await harness.CacheWorkingAsync(ct);
        beforeEndCount.ShouldBe(0);
        beforeSqlWorking.ShouldBeTrue();
        beforeCacheWorking.ShouldBeTrue();

        var stored = await harness.GracePullAsync(ct);
        var postGraceEndCount = await harness.CountAsync(harness.MissingEndUuid, TranscriptKinds.TurnEnd);
        Console.WriteLine(
            "C1205 T02 order " + string.Join(" > ", harness.Order)
            + $" beforeEndCount={beforeEndCount} graceStored={stored} postGraceEndCount={postGraceEndCount}"
            + $" uuid={harness.MissingEndUuid}");
        stored.ShouldBeTrue();
        postGraceEndCount.ShouldBe(1);
        (await harness.SqlWorkingAsync(ct)).ShouldBeFalse();
        (await harness.CacheWorkingAsync(ct)).ShouldBeFalse();
    }

    private static void AssertChronological(IReadOnlyList<string> order, params string[] markers)
    {
        var cursor = -1;
        foreach (var marker in markers)
        {
            var index = -1;
            for (var i = cursor + 1; i < order.Count; i++)
            {
                if (order[i] == marker)
                {
                    index = i;
                    break;
                }
            }

            index.ShouldBeGreaterThan(cursor, $"'{marker}' missing or out of order in [{string.Join(" > ", order)}]");
            cursor = index;
        }
    }
}
