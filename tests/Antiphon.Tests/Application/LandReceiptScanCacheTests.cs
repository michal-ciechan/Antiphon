using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1121 S1 policy lane (no database). Every row drives the production
/// <see cref="LandReceiptScanCache.TryBuildContext"/>, <see cref="LandReceiptScanCache.StateStamp.TryCreate"/>,
/// <see cref="LandReceiptScanCache.Publish"/> and <see cref="LandReceiptScanCache.TryReuse"/> the reconciler calls.
/// </summary>
[Category("Unit")]
public sealed class LandReceiptScanCacheTests
{
    private static readonly DateTime StartedAt = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);
    private const string Body = "Land outcome for task 1121: merged.\nThe receipt scan cache stays fail-closed.";

    [Test]
    // identity: reuse side only
    [Arguments("note-id")]
    [Arguments("queue-message-id")]
    [Arguments("parent-session")]
    [Arguments("queue-destination")]
    [Arguments("source-land-notification-id")]
    // attempt: reuse side only
    [Arguments("delivery-attempts")]
    [Arguments("baseline-sequence")]
    [Arguments("last-delivery-started-at")]
    [Arguments("last-delivery-started-at-null")]
    [Arguments("last-delivery-generation")]
    [Arguments("last-delivery-generation-null")]
    // payload: reuse side only
    [Arguments("expected-text-one-char")]
    [Arguments("is-legacy")]
    [Arguments("kind-held-vs-aged")]
    [Arguments("queue-status-sent-vs-pending")]
    [Arguments("verdict-null-vs-delivered")]
    [Arguments("verdict-delivered-vs-late-confirmed")]
    [Arguments("note-state-awaiting-vs-queued")]
    [Arguments("destination-status-stopped-vs-failed")]
    // eligibility: both sides, so the named guard alone stands between the shape and a reuse
    [Arguments("attempts-zero")]
    [Arguments("baseline-null")]
    [Arguments("baseline-negative")]
    [Arguments("kind-legacy-check-note")]
    [Arguments("kind-undefined-enum")]
    [Arguments("note-state-confirmed")]
    [Arguments("note-state-not-required")]
    [Arguments("note-state-legacy-unverified")]
    [Arguments("note-state-undefined-enum")]
    [Arguments("queue-status-undefined-enum")]
    [Arguments("verdict-undefined-enum")]
    [Arguments("completion-snapshot-present")]
    [Arguments("completion-delivery-present")]
    [Arguments("pointer-headline-body")]
    [Arguments("remote-spill-body-present")]
    [Arguments("queue-body-not-ordinal-equal")]
    [Arguments("queue-body-case-differs")]
    [Arguments("expected-text-not-body")]
    [Arguments("text-over-cap")]
    [Arguments("destination-running")]
    [Arguments("destination-starting")]
    [Arguments("destination-status-undefined-enum")]
    // state: both sides
    [Arguments("readiness-cold")]
    [Arguments("readiness-loading")]
    [Arguments("readiness-missing")]
    [Arguments("readiness-faulted")]
    [Arguments("accepted-generation-null")]
    [Arguments("accepted-generation-not-equal-started-at")]
    [Arguments("server-epoch-empty")]
    [Arguments("revision-zero")]
    [Arguments("revision-negative")]
    [Arguments("reset-epoch-negative")]
    [Arguments("count-negative")]
    [Arguments("last-sequence-below-count")]
    [Arguments("observation-unknown")]
    // certificate: publish side
    [Arguments("no-certificate")]
    [Arguments("before-after-differ")]
    [Arguments("match-observed")]
    [Arguments("save-not-completed")]
    [Arguments("expired")]
    [Arguments("stamp-session")]
    [Arguments("stamp-epoch")]
    [Arguments("stamp-revision")]
    [Arguments("stamp-reset-epoch")]
    [Arguments("stamp-generation")]
    [Arguments("stamp-count")]
    [Arguments("stamp-last-sequence")]
    // positive: both sides identical
    [Arguments("positive-held")]
    [Arguments("positive-aged")]
    [Arguments("positive-conflict")]
    [Arguments("positive-outcome")]
    [Arguments("positive-outcome-legacy")]
    [Arguments("positive-dispatch-base")]
    [Arguments("positive-delivery-failure")]
    [Arguments("positive-task-completion-unprofiled")]
    [Arguments("positive-queue-status-pending-parked")]
    [Arguments("positive-queue-status-canceled")]
    [Arguments("positive-note-state-queued")]
    [Arguments("positive-note-state-retry-pending")]
    [Arguments("positive-note-state-destination-unavailable")]
    [Arguments("positive-note-state-canceled")]
    [Arguments("positive-destination-failed")]
    [Arguments("positive-text-at-cap")]
    [Arguments("positive-generation-null-bound")]
    [Arguments("positive-started-at-null-bound")]
    [Arguments("positive-verdict-null")]
    [Arguments("positive-verdict-delivered")]
    [Arguments("positive-verdict-no-composer-evidence")]
    [Arguments("positive-verdict-no-submit-output")]
    [Arguments("positive-verdict-no-transcript-record")]
    [Arguments("positive-verdict-truncated")]
    [Arguments("positive-verdict-forbidden-body")]
    [Arguments("positive-verdict-local-command-not-accepted")]
    [Arguments("positive-verdict-backend-unreachable")]
    [Arguments("positive-verdict-late-confirmed")]
    [Arguments("positive-verdict-modal-blocked")]
    [Arguments("positive-verdict-spill-body-missing")]
    public void C1121_OnlyWhitelistedEvidenceReusesNegativeScan(string flip)
    {
        var ids = Ids.New();
        var publish = Side.Positive(ids);
        var reuse = Side.Positive(ids);
        var certificate = new Certificate();
        var (expectReuse, expectedRefusal) = Apply(flip, publish, reuse, certificate);

        var clock = new ManualClock();
        var cache = new LandReceiptScanCache(clock);
        var published = false;
        if (publish.TryBuild(out var context, out var stamp, out _) && certificate.Publish)
        {
            var proofStamp = certificate.EditStamp(stamp!);
            var before = certificate.BeforeDiffers ? proofStamp with { Revision = proofStamp.Revision + 1 } : proofStamp;
            published = cache.Publish(context!, before, proofStamp, certificate.MatchObserved,
                certificate.SaveCompleted, cache.TryGetTimestamp());
        }
        clock.Advance(certificate.ReuseAfter);

        var reused = reuse.TryBuild(out var current, out var currentStamp, out var refusal)
            && cache.TryReuse(current!, currentStamp!, out refusal);

        reused.ShouldBe(expectReuse, flip);
        refusal.ShouldBe(expectedRefusal, flip);
        if (expectReuse)
            published.ShouldBeTrue(flip);
        if (certificate.PublishIsNoOp)
        {
            published.ShouldBeFalse(flip);
            cache.ProofCount.ShouldBe(0, flip);
        }
    }

    [Test]
    [Timeout(60_000)]
    [Arguments("lifetime-reuse-one-tick-before")]
    [Arguments("lifetime-refuse-at-five-minutes")]
    [Arguments("hit-does-not-extend")]
    [Arguments("capacity-overflow-evicts-oldest")]
    [Arguments("eviction-keeps-exact-binding")]
    [Arguments("text-at-cap-admitted")]
    [Arguments("text-over-cap-refused")]
    [Arguments("terminal-removal")]
    [Arguments("new-singleton-miss")]
    [Arguments("concurrent-publications-keep-own-binding")]
    [Arguments("invalid-elapsed-refuses")]
    [Arguments("metrics-never-throw-and-count")]
    public async Task C1121_CacheLifetimeAndCapacityFailClosed(string rule, CancellationToken ct)
    {
        var clock = new ManualClock();
        var cache = new LandReceiptScanCache(clock);
        string refusal;
        switch (rule)
        {
            case "lifetime-reuse-one-tick-before":
            {
                var (context, stamp) = PublishPositive(cache, Ids.New());
                clock.Advance(LandReceiptScanCache.Lifetime - TimeSpan.FromTicks(1));
                cache.TryReuse(context, stamp, out refusal).ShouldBeTrue(refusal);
                break;
            }
            case "lifetime-refuse-at-five-minutes":
            {
                var (context, stamp) = PublishPositive(cache, Ids.New());
                clock.Advance(LandReceiptScanCache.Lifetime);
                cache.TryReuse(context, stamp, out refusal).ShouldBeFalse();
                refusal.ShouldBe("certificate:Lifetime");
                break;
            }
            case "hit-does-not-extend":
            {
                var (context, stamp) = PublishPositive(cache, Ids.New());
                foreach (var at in new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), new TimeSpan(0, 4, 59) })
                {
                    clock.Set(at);
                    cache.TryReuse(context, stamp, out refusal).ShouldBeTrue($"{at}: {refusal}");
                }
                clock.Set(TimeSpan.FromMinutes(5));
                cache.TryReuse(context, stamp, out refusal).ShouldBeFalse();
                refusal.ShouldBe("certificate:Lifetime");
                break;
            }
            case "capacity-overflow-evicts-oldest":
            {
                var proofs = PublishMany(cache, LandReceiptScanCache.MaxProofs + 1);
                cache.ProofCount.ShouldBe(LandReceiptScanCache.MaxProofs);
                cache.TryReuse(proofs[0].Context, proofs[0].Stamp, out refusal).ShouldBeFalse();
                refusal.ShouldBe("no-proof");
                cache.TryReuse(proofs[1].Context, proofs[1].Stamp, out refusal).ShouldBeTrue(refusal);
                cache.TryReuse(proofs[^1].Context, proofs[^1].Stamp, out refusal).ShouldBeTrue(refusal);
                break;
            }
            case "eviction-keeps-exact-binding":
            {
                var proofs = PublishMany(cache, LandReceiptScanCache.MaxProofs + 1);
                // Survivors keep exactly their own pair: a survivor's context with another entry's
                // stamp, or the evicted note's own pair, never reuses.
                cache.TryReuse(proofs[1].Context, proofs[2].Stamp, out refusal).ShouldBeFalse();
                refusal.ShouldBe("stamp:Revision");
                cache.TryReuse(proofs[2].Context, proofs[^1].Stamp, out refusal).ShouldBeFalse();
                refusal.ShouldBe("stamp:Revision");
                cache.TryReuse(proofs[1].Context, proofs[1].Stamp, out refusal).ShouldBeTrue(refusal);
                cache.TryReuse(proofs[2].Context, proofs[2].Stamp, out refusal).ShouldBeTrue(refusal);
                cache.TryReuse(proofs[0].Context, proofs[0].Stamp, out refusal).ShouldBeFalse();
                refusal.ShouldBe("no-proof");
                break;
            }
            case "text-at-cap-admitted":
            {
                var side = Side.Positive(Ids.New());
                side.SetBody(new string('a', LandReceiptScanCache.MaxExpectedTextChars));
                side.TryBuild(out var context, out var stamp, out refusal).ShouldBeTrue(refusal);
                cache.Publish(context!, stamp, stamp, false, true, cache.TryGetTimestamp()).ShouldBeTrue();
                cache.TryReuse(context!, stamp!, out refusal).ShouldBeTrue(refusal);
                break;
            }
            case "text-over-cap-refused":
            {
                var (context, stamp) = Build(Side.Positive(Ids.New()));
                var oversized = context with { ExpectedText = new string('a', LandReceiptScanCache.MaxExpectedTextChars + 1) };
                cache.Publish(oversized, stamp, stamp, false, true, cache.TryGetTimestamp()).ShouldBeFalse();
                cache.ProofCount.ShouldBe(0);
                cache.GetMetrics().Refusals.GetValueOrDefault("eligibility:MaxExpectedTextChars").ShouldBe(1);
                cache.TryReuse(oversized, stamp, out refusal).ShouldBeFalse();
                refusal.ShouldBe("no-proof");
                break;
            }
            case "terminal-removal":
            {
                var first = PublishPositive(cache, Ids.New());
                var second = PublishPositive(cache, Ids.New());
                cache.ProofCount.ShouldBe(2);
                cache.Invalidate(first.Context.NoteId);
                cache.ProofCount.ShouldBe(1);
                cache.TryReuse(first.Context, first.Stamp, out refusal).ShouldBeFalse();
                refusal.ShouldBe("no-proof");
                cache.TryReuse(second.Context, second.Stamp, out refusal).ShouldBeTrue(refusal);
                break;
            }
            case "new-singleton-miss":
            {
                var (context, stamp) = PublishPositive(cache, Ids.New());
                var restarted = new LandReceiptScanCache(clock);
                restarted.TryReuse(context, stamp, out refusal).ShouldBeFalse();
                refusal.ShouldBe("no-proof");
                cache.TryReuse(context, stamp, out refusal).ShouldBeTrue(refusal);
                break;
            }
            case "concurrent-publications-keep-own-binding":
                await ConcurrentPublicationsKeepOwnBindingAsync(cache, ct);
                break;
            case "invalid-elapsed-refuses":
            {
                var (context, stamp) = Build(Side.Positive(Ids.New()));
                cache.Publish(context, stamp, stamp, false, true, clock.Now + 1).ShouldBeTrue();
                cache.TryReuse(context, stamp, out refusal).ShouldBeFalse();
                refusal.ShouldBe("certificate:Elapsed");

                cache.Publish(context, stamp, stamp, false, true, cache.TryGetTimestamp()).ShouldBeTrue();
                clock.Advance(TimeSpan.FromSeconds(1));
                cache.TryReuse(context, stamp, out refusal).ShouldBeTrue(refusal);
                clock.Fault = true;
                var reused = true;
                Should.NotThrow(() => reused = cache.TryReuse(context, stamp, out refusal));
                reused.ShouldBeFalse();
                refusal.ShouldBe("cache-fault");
                cache.TryGetTimestamp().ShouldBeNull();
                cache.Publish(context, stamp, stamp, false, true, cache.TryGetTimestamp()).ShouldBeFalse();
                cache.GetMetrics().Refusals.GetValueOrDefault("certificate:Timestamp").ShouldBe(1);
                break;
            }
            case "metrics-never-throw-and-count":
            {
                var (context, stamp) = PublishPositive(cache, Ids.New());
                cache.TryReuse(context, stamp, out _).ShouldBeTrue();
                var moved = stamp with { Revision = stamp.Revision + 1 };
                cache.TryReuse(context, moved, out _).ShouldBeFalse();
                cache.Publish(context, stamp, stamp, true, true, cache.TryGetTimestamp()).ShouldBeFalse();
                cache.RecordRefusal("eligibility:Kind");

                var metrics = cache.GetMetrics();
                metrics.Proofs.ShouldBe(1);
                metrics.Publishes.ShouldBe(1);
                metrics.Hits.ShouldBe(1);
                metrics.Misses.ShouldBe(1);
                metrics.Refusals.OrderBy(p => p.Key, StringComparer.Ordinal).ShouldBe(new KeyValuePair<string, long>[]
                {
                    new("certificate:MatchObserved", 1),
                    new("eligibility:Kind", 1),
                    new("stamp:Revision", 1),
                });

                // A throwing telemetry sink changes no decision and escapes nowhere.
                cache.MetricsProbe = _ => throw new InvalidOperationException("metrics sink down");
                cache.TryReuse(context, stamp, out refusal).ShouldBeTrue(refusal);
                cache.TryReuse(context, moved, out refusal).ShouldBeFalse();
                refusal.ShouldBe("stamp:Revision");
                var other = Build(Side.Positive(Ids.New()));
                cache.Publish(other.Context, other.Stamp, other.Stamp, false, true, cache.TryGetTimestamp()).ShouldBeTrue();
                cache.ProofCount.ShouldBe(2);
                cache.TryReuse(other.Context, other.Stamp, out refusal).ShouldBeTrue(refusal);
                Should.NotThrow(() => cache.RecordRefusal("eligibility:Kind"));
                cache.GetMetrics().Hits.ShouldBe(1);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(rule), rule, null);
        }
    }

    private static async Task ConcurrentPublicationsKeepOwnBindingAsync(LandReceiptScanCache cache, CancellationToken ct)
    {
        var ids = Ids.New();
        var (c1, s1) = Build(Side.Positive(ids));
        var second = Side.Positive(ids);
        second.Row.DeliveryAttempts = 2;
        second.Snapshot = second.Snapshot with { Revision = 8, Count = 50, LastSequence = 59 };
        var (c2, s2) = Build(second);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var crossReuses = 0;
        var publishers = 0;

        Task Publisher(LandReceiptScanCache.Context context, LandReceiptScanCache.StateStamp stamp) => Task.Run(async () =>
        {
            await start.Task;
            try
            {
                for (var i = 0; i < 200; i++)
                    cache.Publish(context, stamp, stamp, false, true, cache.TryGetTimestamp()).ShouldBeTrue();
            }
            finally { Interlocked.Increment(ref publishers); }
        }, ct);

        var observer = Task.Run(async () =>
        {
            await start.Task;
            while (Volatile.Read(ref publishers) < 2)
            {
                if (cache.TryReuse(c1, s2, out _) || cache.TryReuse(c2, s1, out _))
                    Interlocked.Increment(ref crossReuses);
            }
        }, ct);
        var tasks = new[] { Publisher(c1, s1), Publisher(c2, s2), observer };
        start.SetResult();
        await Task.WhenAll(tasks);

        crossReuses.ShouldBe(0);
        cache.ProofCount.ShouldBe(1);
        cache.TryReuse(c1, s2, out _).ShouldBeFalse();
        cache.TryReuse(c2, s1, out _).ShouldBeFalse();
        var own = new[] { cache.TryReuse(c1, s1, out _), cache.TryReuse(c2, s2, out _) };
        own.Count(x => x).ShouldBe(1);
    }

    private static (bool Reuse, string Refusal) Apply(string flip, Side publish, Side reuse, Certificate certificate)
    {
        const string NoProof = "no-proof";
        void Both(Action<Side> change)
        {
            change(publish);
            change(reuse);
        }
        (bool, string) Positive(Action<Side> change)
        {
            Both(change);
            return (true, "");
        }
        (bool, string) Eligibility(string member, Action<Side> change)
        {
            Both(change);
            return (false, "eligibility:" + member);
        }
        (bool, string) State(string member, Func<SessionStateSnapshot, SessionStateSnapshot> change)
        {
            Both(s => s.Snapshot = change(s.Snapshot));
            return (false, "state:" + member);
        }
        (bool, string) Reuse(string refusal, Action<Side> change)
        {
            change(reuse);
            return (false, refusal);
        }
        (bool, string) Context(string member, Action<Side> change, Action<Side>? publishSide = null)
        {
            publishSide?.Invoke(publish);
            return Reuse("context:" + member, change);
        }
        (bool, string) Stamp(string member, Func<LandReceiptScanCache.StateStamp, LandReceiptScanCache.StateStamp> edit)
        {
            certificate.EditStamp = edit;
            return (false, "stamp:" + member);
        }
        (bool, string) NoOp(Action change)
        {
            change();
            certificate.PublishIsNoOp = true;
            return (false, NoProof);
        }
        (bool, string) Unknown(string reason)
        {
            Both(s => s.Unknown = reason);
            return (false, "state:Observation:" + reason);
        }
        (bool, string) Expired()
        {
            certificate.ReuseAfter = LandReceiptScanCache.Lifetime;
            return (false, "certificate:Lifetime");
        }

        return flip switch
        {
            "note-id" => Reuse(NoProof, s => s.Note.Id = Guid.NewGuid()),
            "queue-message-id" => Context("QueueMessageId", s => s.Note.QueueMessageId = s.Row.Id = Guid.NewGuid()),
            "parent-session" => Context("ParentSessionId", s => s.Note.ParentSessionId = Guid.NewGuid()),
            "queue-destination" => Context("QueueDestination", s => s.Row.AgentSessionId = Guid.NewGuid()),
            "source-land-notification-id" => Context("SourceLandNotificationId", s => s.Row.SourceLandNotificationId = Guid.NewGuid()),

            "delivery-attempts" => Context("DeliveryAttempts", s => s.Row.DeliveryAttempts = 2),
            "baseline-sequence" => Context("BaselineSequence", s => s.Row.LastDeliveryBaselineSequence = 11),
            "last-delivery-started-at" => Context("LastDeliveryStartedAt", s => s.Row.LastDeliveryStartedAt = s.Row.LastDeliveryStartedAt!.Value.AddTicks(10)),
            "last-delivery-started-at-null" => Context("LastDeliveryStartedAt", s => s.Row.LastDeliveryStartedAt = null),
            "last-delivery-generation" => Context("LastDeliveryGeneration", s => s.Row.LastDeliveryGeneration = s.Row.LastDeliveryGeneration!.Value.AddTicks(10)),
            "last-delivery-generation-null" => Context("LastDeliveryGeneration", s => s.Row.LastDeliveryGeneration = null),

            "expected-text-one-char" => Context("ExpectedText", s => s.SetBody(Body.Replace("merged", "merget", StringComparison.Ordinal))),
            "is-legacy" => Context("IsLegacy", s => s.Note.IsLegacy = true),
            "kind-held-vs-aged" => Context("Kind", s => s.Note.Kind = LandNotificationKind.Aged,
                s => s.Note.Kind = LandNotificationKind.Held),
            "queue-status-sent-vs-pending" => Context("QueueStatus", s => s.Row.Status = QueuedMessageStatus.Pending),
            "verdict-null-vs-delivered" => Context("Verdict", s => s.Row.DeliveryVerdict = DeliveryVerdict.Delivered),
            "verdict-delivered-vs-late-confirmed" => Context("Verdict", s => s.Row.DeliveryVerdict = DeliveryVerdict.LateConfirmed,
                s => s.Row.DeliveryVerdict = DeliveryVerdict.Delivered),
            "note-state-awaiting-vs-queued" => Context("NoteState", s => s.Note.State = LandNotificationState.Queued),
            "destination-status-stopped-vs-failed" => Context("DestinationStatus", s => s.DestinationStatus = SessionStatus.Failed),

            "attempts-zero" => Eligibility("DeliveryAttempts", s => s.Row.DeliveryAttempts = 0),
            "baseline-null" => Eligibility("LastDeliveryBaselineSequence", s => s.Row.LastDeliveryBaselineSequence = null),
            "baseline-negative" => Eligibility("LastDeliveryBaselineSequence", s => s.Row.LastDeliveryBaselineSequence = -1),
            "kind-legacy-check-note" => Eligibility("Kind", s => s.Note.Kind = LandNotificationKind.LegacyCheckNote),
            "kind-undefined-enum" => Eligibility("Kind", s => s.Note.Kind = (LandNotificationKind)99),
            "note-state-confirmed" => Eligibility("NoteState", s => s.Note.State = LandNotificationState.Confirmed),
            "note-state-not-required" => Eligibility("NoteState", s => s.Note.State = LandNotificationState.NotRequired),
            "note-state-legacy-unverified" => Eligibility("NoteState", s => s.Note.State = LandNotificationState.LegacyUnverified),
            "note-state-undefined-enum" => Eligibility("NoteState", s => s.Note.State = (LandNotificationState)99),
            "queue-status-undefined-enum" => Eligibility("QueueStatus", s => s.Row.Status = (QueuedMessageStatus)99),
            "verdict-undefined-enum" => Eligibility("DeliveryVerdict", s => s.Row.DeliveryVerdict = (DeliveryVerdict)99),
            "completion-snapshot-present" => Eligibility("CompletionSnapshotJson", s =>
            {
                s.Note.Kind = LandNotificationKind.TaskCompletion;
                s.Note.CompletionSnapshotJson = "{}";
            }),
            "completion-delivery-present" => Eligibility("CompletionDeliveryJson", s =>
            {
                s.Note.Kind = LandNotificationKind.TaskCompletion;
                s.Note.CompletionDeliveryJson = "{}";
            }),
            "pointer-headline-body" => Eligibility("PointerHeadline", s => s.SetBody(
                TypedBodySpill.PointerHeadline + "\nRead .antiphon/inbox/" + s.Row.Id.ToString("D") + ".md")),
            "remote-spill-body-present" => Eligibility("RemoteSpillBody", s => s.Row.RemoteSpillBody = Body),
            "queue-body-not-ordinal-equal" => Eligibility("QueueBody", s => s.Row.Body = Body + " "),
            "queue-body-case-differs" => Eligibility("QueueBody", s => s.Row.Body = Body.ToUpperInvariant()),
            "expected-text-not-body" => Eligibility("ExpectedText", s => s.Expected = "rendered wire text"),
            "text-over-cap" => Eligibility("MaxExpectedTextChars", s => s.SetBody(new string('a', LandReceiptScanCache.MaxExpectedTextChars + 1))),
            "destination-running" => Eligibility("DestinationStatus", s => s.DestinationStatus = SessionStatus.Running),
            "destination-starting" => Eligibility("DestinationStatus", s => s.DestinationStatus = SessionStatus.Starting),
            "destination-status-undefined-enum" => Eligibility("DestinationStatus", s => s.DestinationStatus = (SessionStatus)99),

            "readiness-cold" => State("Readiness", s => s with { Readiness = SessionStateReadiness.Cold }),
            "readiness-loading" => State("Readiness", s => s with { Readiness = SessionStateReadiness.Loading }),
            "readiness-missing" => State("Readiness", s => s with { Readiness = SessionStateReadiness.Missing }),
            "readiness-faulted" => State("Readiness", s => s with { Readiness = SessionStateReadiness.Faulted }),
            "accepted-generation-null" => State("AcceptedGeneration", s => s with { AcceptedGeneration = null }),
            "accepted-generation-not-equal-started-at" => State("AcceptedGeneration", s => s with { AcceptedGeneration = StartedAt.AddSeconds(1) }),
            "server-epoch-empty" => State("ServerEpoch", s => s with { ServerEpoch = Guid.Empty }),
            "revision-zero" => State("Revision", s => s with { Revision = 0 }),
            "revision-negative" => State("Revision", s => s with { Revision = -1 }),
            "reset-epoch-negative" => State("ResetEpoch", s => s with { ResetEpoch = -1 }),
            "count-negative" => State("Count", s => s with { Count = -1 }),
            "last-sequence-below-count" => State("LastSequence", s => s with { LastSequence = s.Count - 1 }),
            "observation-unknown" => Unknown("pull_failed"),

            "no-certificate" => NoOp(() => certificate.Publish = false),
            "before-after-differ" => NoOp(() => certificate.BeforeDiffers = true),
            "match-observed" => NoOp(() => certificate.MatchObserved = true),
            "save-not-completed" => NoOp(() => certificate.SaveCompleted = false),
            "expired" => Expired(),
            "stamp-session" => Stamp("SessionId", s => s with { SessionId = Guid.NewGuid() }),
            "stamp-epoch" => Stamp("ServerEpoch", s => s with { ServerEpoch = Guid.NewGuid() }),
            "stamp-revision" => Stamp("Revision", s => s with { Revision = s.Revision - 1 }),
            "stamp-reset-epoch" => Stamp("ResetEpoch", s => s with { ResetEpoch = s.ResetEpoch + 1 }),
            "stamp-generation" => Stamp("AcceptedGeneration", s => s with { AcceptedGeneration = s.AcceptedGeneration.AddTicks(10) }),
            "stamp-count" => Stamp("Count", s => s with { Count = s.Count - 1 }),
            "stamp-last-sequence" => Stamp("LastSequence", s => s with { LastSequence = s.LastSequence - 1 }),

            "positive-held" => Positive(s => s.Note.Kind = LandNotificationKind.Held),
            "positive-aged" => Positive(s => s.Note.Kind = LandNotificationKind.Aged),
            "positive-conflict" => Positive(s => s.Note.Kind = LandNotificationKind.Conflict),
            "positive-outcome" => Positive(s => s.Note.Kind = LandNotificationKind.Outcome),
            "positive-outcome-legacy" => Positive(s => s.Note.IsLegacy = true),
            "positive-dispatch-base" => Positive(s => s.Note.Kind = LandNotificationKind.DispatchBase),
            "positive-delivery-failure" => Positive(s => s.Note.Kind = LandNotificationKind.DeliveryFailure),
            "positive-task-completion-unprofiled" => Positive(s => s.Note.Kind = LandNotificationKind.TaskCompletion),
            "positive-queue-status-pending-parked" => Positive(s =>
            {
                s.Row.Status = QueuedMessageStatus.Pending;
                s.Row.DeliveryAttempts = 5;
            }),
            "positive-queue-status-canceled" => Positive(s => s.Row.Status = QueuedMessageStatus.Canceled),
            "positive-note-state-queued" => Positive(s => s.Note.State = LandNotificationState.Queued),
            "positive-note-state-retry-pending" => Positive(s => s.Note.State = LandNotificationState.RetryPending),
            "positive-note-state-destination-unavailable" => Positive(s => s.Note.State = LandNotificationState.DestinationUnavailable),
            "positive-note-state-canceled" => Positive(s => s.Note.State = LandNotificationState.Canceled),
            "positive-destination-failed" => Positive(s => s.DestinationStatus = SessionStatus.Failed),
            "positive-text-at-cap" => Positive(s => s.SetBody(new string('a', LandReceiptScanCache.MaxExpectedTextChars))),
            "positive-generation-null-bound" => Positive(s => s.Row.LastDeliveryGeneration = null),
            "positive-started-at-null-bound" => Positive(s => s.Row.LastDeliveryStartedAt = null),
            "positive-verdict-null" => Positive(s => s.Row.DeliveryVerdict = null),
            "positive-verdict-delivered" => Positive(s => s.Row.DeliveryVerdict = DeliveryVerdict.Delivered),
            "positive-verdict-no-composer-evidence" => Positive(s => s.Row.DeliveryVerdict = DeliveryVerdict.NoComposerEvidence),
            "positive-verdict-no-submit-output" => Positive(s => s.Row.DeliveryVerdict = DeliveryVerdict.NoSubmitOutput),
            "positive-verdict-no-transcript-record" => Positive(s => s.Row.DeliveryVerdict = DeliveryVerdict.NoTranscriptRecord),
            "positive-verdict-truncated" => Positive(s => s.Row.DeliveryVerdict = DeliveryVerdict.Truncated),
            "positive-verdict-forbidden-body" => Positive(s => s.Row.DeliveryVerdict = DeliveryVerdict.ForbiddenBody),
            "positive-verdict-local-command-not-accepted" => Positive(s => s.Row.DeliveryVerdict = DeliveryVerdict.LocalCommandNotAccepted),
            "positive-verdict-backend-unreachable" => Positive(s => s.Row.DeliveryVerdict = DeliveryVerdict.BackendUnreachable),
            "positive-verdict-late-confirmed" => Positive(s => s.Row.DeliveryVerdict = DeliveryVerdict.LateConfirmed),
            "positive-verdict-modal-blocked" => Positive(s => s.Row.DeliveryVerdict = DeliveryVerdict.ModalBlocked),
            "positive-verdict-spill-body-missing" => Positive(s => s.Row.DeliveryVerdict = DeliveryVerdict.SpillBodyMissing),
            _ => throw new ArgumentOutOfRangeException(nameof(flip), flip, null),
        };
    }

    private static (LandReceiptScanCache.Context Context, LandReceiptScanCache.StateStamp Stamp) Build(Side side)
    {
        side.TryBuild(out var context, out var stamp, out var refusal).ShouldBeTrue(refusal);
        return (context!, stamp!);
    }

    private static (LandReceiptScanCache.Context Context, LandReceiptScanCache.StateStamp Stamp) PublishPositive(
        LandReceiptScanCache cache, Ids ids)
    {
        var (context, stamp) = Build(Side.Positive(ids));
        cache.Publish(context, stamp, stamp, false, true, cache.TryGetTimestamp()).ShouldBeTrue();
        return (context, stamp);
    }

    // Distinct notes to one destination whose stamps differ only in revision (i + 1), published in order.
    private static (LandReceiptScanCache.Context Context, LandReceiptScanCache.StateStamp Stamp)[] PublishMany(
        LandReceiptScanCache cache, int count)
    {
        var destination = Ids.New();
        var proofs = new (LandReceiptScanCache.Context, LandReceiptScanCache.StateStamp)[count];
        for (var i = 0; i < count; i++)
        {
            var side = Side.Positive(destination with { Note = Guid.NewGuid(), Row = Guid.NewGuid() });
            side.Snapshot = side.Snapshot with { Revision = i + 1 };
            proofs[i] = Build(side);
            cache.Publish(proofs[i].Item1, proofs[i].Item2, proofs[i].Item2, false, true, cache.TryGetTimestamp()).ShouldBeTrue();
        }
        return proofs;
    }

    private sealed record Ids(Guid Note, Guid Row, Guid Parent, Guid Epoch)
    {
        public static Ids New() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }

    /// <summary>One side of a publish/reuse pair: the evidence the reconciler would hold on that pass.</summary>
    private sealed class Side
    {
        public required AgentTaskLandNotification Note { get; init; }
        public required SessionQueuedMessage Row { get; init; }
        public SessionStatus DestinationStatus { get; set; } = SessionStatus.Stopped;
        public DateTime? DestinationStartedAt { get; set; } = StartedAt;
        public required string Expected { get; set; }
        public required SessionStateSnapshot Snapshot { get; set; }
        public string? Unknown { get; set; }

        // Non-legacy Outcome awaiting receipt; keyed row Sent once above baseline 10 with the same
        // body; destination Stopped; snapshot Ready, revision 7, 49 rows through sequence 58.
        public static Side Positive(Ids ids) => new()
        {
            Note = new AgentTaskLandNotification
            {
                Id = ids.Note, Kind = LandNotificationKind.Outcome, State = LandNotificationState.AwaitingReceipt,
                ParentSessionId = ids.Parent, QueueMessageId = ids.Row, Body = Body,
            },
            Row = new SessionQueuedMessage
            {
                Id = ids.Row, AgentSessionId = ids.Parent, SourceLandNotificationId = ids.Note, Body = Body,
                Status = QueuedMessageStatus.Sent, DeliveryAttempts = 1, LastDeliveryBaselineSequence = 10,
                LastDeliveryStartedAt = StartedAt.AddMinutes(3), LastDeliveryGeneration = StartedAt,
            },
            Expected = Body,
            Snapshot = new SessionStateSnapshot(ids.Parent)
            {
                ServerEpoch = ids.Epoch, Revision = 7, ResetEpoch = 0, Readiness = SessionStateReadiness.Ready,
                AcceptedGeneration = StartedAt, Count = 49, LastSequence = 58,
            },
        };

        public void SetBody(string body)
        {
            Note.Body = body;
            Row.Body = body;
            Expected = body;
        }

        public bool TryBuild(out LandReceiptScanCache.Context? context, out LandReceiptScanCache.StateStamp? stamp, out string refusal)
        {
            stamp = null;
            if (!LandReceiptScanCache.TryBuildContext(Note, Row, DestinationStatus, Expected, out context, out refusal))
                return false;
            var observation = Unknown is null
                ? LandReceiptScanCache.Observation.Known(Snapshot)
                : LandReceiptScanCache.Observation.Unknown(Unknown);
            return LandReceiptScanCache.StateStamp.TryCreate(observation, DestinationStartedAt, out stamp, out refusal);
        }
    }

    /// <summary>The publish-side certificate inputs; certificate rows change exactly one.</summary>
    private sealed class Certificate
    {
        public bool Publish { get; set; } = true;
        public bool BeforeDiffers { get; set; }
        public bool MatchObserved { get; set; }
        public bool SaveCompleted { get; set; } = true;
        public bool PublishIsNoOp { get; set; }
        public TimeSpan ReuseAfter { get; set; } = TimeSpan.FromSeconds(1);
        public Func<LandReceiptScanCache.StateStamp, LandReceiptScanCache.StateStamp> EditStamp { get; set; } = s => s;
    }

    /// <summary>A monotonic cache clock the test moves by hand, including backwards and into a fault.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private const long Origin = 1_000_000_000_000;
        public long Now { get; private set; } = Origin;
        public bool Fault { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Fault ? throw new InvalidOperationException("cache clock fault") : Now;
        public void Advance(TimeSpan by) => Now += by.Ticks;
        public void Set(TimeSpan sinceOrigin) => Now = Origin + sinceOrigin.Ticks;
    }
}
