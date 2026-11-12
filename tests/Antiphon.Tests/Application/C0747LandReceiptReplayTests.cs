using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0747. An orchestrator session that could not take input for two weeks (adopted, withheld)
/// collected about 1,900 land and completion notes. When it restarted, the queue typed them oldest
/// first, one per idle turn, as if they were new. These witnesses pin the three ways a note now
/// stops: a keyed completion row carried to a successor session is confirmed on that session, an
/// aged terminal note settles <see cref="LandNotificationState.LegacyUnverified"/> and its untyped
/// row is canceled, and a Held snapshot whose request already has a terminal outcome is not typed.
/// </summary>
[Category("Integration")]
public sealed class C0747LandReceiptReplayTests
{
    private const long Floor = LandReceiptScanHarness.Floor;

    /// <summary>
    /// case: a TaskCompletion's keyed row was carried by a standing agent's fresh restart to the
    /// successor session and typed there; the receipt is on the successor. mutation: drop the carried
    /// completion rule. red: the note stays AwaitingReceipt with completion_queue_identity_mismatch
    /// (profiled) or completion_delivery_generation_changed (unprofiled), forever.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("profiled")]
    [Arguments("unprofiled")]
    [Arguments("profiled-receipt-on-parent-only")]
    public async Task C0747_Carried_completion_confirms_on_the_session_it_was_typed_to(string arm, CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync(new() { Cache = false });
        var parent = h.SessionId;
        var note = await h.SeedLinkedNoteAsync();
        var startedAt = await h.StartedAtAsync(parent);
        var successor = await h.AddSessionAsync(SessionStatus.Stopped, startedAt.AddHours(-1));
        var row = await h.MarkSentAsync(note, destination: successor);
        await h.UpdateNoteAsync(note.Id, n =>
        {
            n.Kind = LandNotificationKind.TaskCompletion;
            if (arm.StartsWith("profiled", StringComparison.Ordinal))
            {
                n.CompletionSnapshotJson = "{}";
                n.CompletionDeliveryJson = TaskCompletionNotification.SerializeDelivery(new TaskCompletionNotification.Delivery(
                    TaskCompletionNotification.SnapshotVersion, "raw", n.Body, n.Body, TaskCompletionNotification.Sha256(n.Body),
                    [row.Id], null, null, DateTime.UtcNow));
            }
        });
        var receiptSession = arm == "profiled-receipt-on-parent-only" ? parent : successor;
        var at = (await h.IngestEventsAsync(receiptSession,
            [h.Event(receiptSession, Floor + 5, TranscriptKinds.UserPrompt, note.Body)])).Single();
        h.Runner.Transcript = LandReceiptScanHarness.ScriptedTranscriptRunner.Mode.NotFound;

        await h.Service.ReconcileAsync(note.Id, ct);
        await h.Service.ReconcileAsync(note.Id, ct);

        var saved = await h.NoteAsync(note.Id);
        saved.ParentSessionId.ShouldBe(parent, "the note keeps its caller; only the receipt follows the keyed row");
        (await h.RowAsync(row.Id)).AgentSessionId.ShouldBe(successor);
        if (arm == "profiled-receipt-on-parent-only")
        {
            saved.State.ShouldBe(LandNotificationState.AwaitingReceipt, "a prompt on the parent is not this row's delivery");
            saved.ConfirmedAt.ShouldBeNull();
        }
        else
        {
            saved.LastErrorCode.ShouldBeNull();
            saved.State.ShouldBe(LandNotificationState.Confirmed);
            saved.ConfirmingPromptSequence.ShouldBe(at);
        }
        h.Bridge.Adapter.Inputs.ShouldBeEmpty("receipt reconciliation never types");
    }

    /// <summary>
    /// case: an outcome committed eight days ago, still unconfirmed. mutation: remove the aged
    /// settlement from ReconcileAsync. red: the note stays open, its untyped row stays Pending and
    /// the caller's next idle turn types an eight-day-old note. Controls: a six-day-old note, an
    /// aged Held snapshot of a still-pending request and a RetryPending note are untouched, and a row
    /// typed once stays on the ordinary path and confirms on its receipt.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("pending-untyped")]
    [Arguments("sent-unconfirmed")]
    [Arguments("unlinked-destination-unavailable")]
    [Arguments("fresh-control")]
    [Arguments("pending-request-control")]
    [Arguments("typed-pending-control")]
    [Arguments("retry-pending-control")]
    public async Task C0747_Aged_terminal_note_settles_and_is_never_typed(string arm, CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync(new() { Cache = false });
        var age = arm == "fresh-control" ? TimeSpan.FromDays(6) : TimeSpan.FromDays(8);
        AgentTaskLandNotification note;
        if (arm is "unlinked-destination-unavailable" or "retry-pending-control")
        {
            await using var db = h.Fixture();
            note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
            note.State = arm == "retry-pending-control" ? LandNotificationState.RetryPending : LandNotificationState.DestinationUnavailable;
            note.LastErrorCode = "destination_failed";
            note.NextAttemptAt = DateTime.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
        }
        else
            note = await h.SeedLinkedNoteAsync();
        await h.UpdateNoteAsync(note.Id, n => n.CreatedAt = DateTime.UtcNow - age);
        if (arm == "pending-request-control")
        {
            await h.UpdateNoteAsync(note.Id, n => n.Kind = LandNotificationKind.Held);
            await using var db = h.Fixture();
            await db.AgentTaskLandRequests.Where(r => r.Id == note.RequestId).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.IsPending, true).SetProperty(r => r.State, LandRequestState.Held), ct);
        }
        if (arm == "sent-unconfirmed")
            await h.MarkSentAsync(note);
        if (arm == "typed-pending-control")
            await h.UpdateRowAsync(note.QueueMessageId!.Value, r =>
            {
                r.DeliveryAttempts = 1;
                r.LastDeliveryBaselineSequence = Floor;
                r.LastDeliveryStartedAt = DateTime.UtcNow.AddMinutes(-1);
            });
        if (arm == "typed-pending-control")
            await h.IngestEventsAsync(h.SessionId, [h.Event(h.SessionId, Floor + 5, TranscriptKinds.UserPrompt, note.Body)]);
        var before = note.QueueMessageId is Guid linked ? await h.RowAsync(linked) : null;

        await h.Service.ReconcileAsync(note.Id, ct);
        await h.Service.ReconcileAsync(note.Id, ct);

        var saved = await h.NoteAsync(note.Id);
        var settles = arm is "pending-untyped" or "sent-unconfirmed" or "unlinked-destination-unavailable";
        if (settles)
        {
            saved.State.ShouldBe(LandNotificationState.LegacyUnverified);
            saved.LastErrorCode.ShouldBe(AgentTaskLandNotificationService.ReceiptExpiredCode);
            saved.ConfirmedAt.ShouldBeNull("an expired note was never proven delivered");
        }
        else
            saved.State.ShouldBe(arm switch
            {
                "retry-pending-control" => LandNotificationState.RetryPending,
                // An uncertain typed attempt stays on the ordinary path, so its late receipt still confirms.
                "typed-pending-control" => LandNotificationState.Confirmed,
                _ => LandNotificationState.AwaitingReceipt,
            });
        if (arm is "unlinked-destination-unavailable" or "retry-pending-control")
        {
            saved.QueueMessageId.ShouldBeNull();
            await using var db = h.Fixture();
            (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == note.Id, ct)).ShouldBe(0);
            return;
        }

        var row = await h.RowAsync(before!.Id);
        row.Status.ShouldBe(arm switch
        {
            "pending-untyped" => QueuedMessageStatus.Canceled,
            "sent-unconfirmed" => QueuedMessageStatus.Sent,
            _ => QueuedMessageStatus.Pending,
        });
        // A Pending row is what the queue types on the caller's next idle turn; a Canceled one never is.
        if (arm != "pending-untyped") row.CanceledAt.ShouldBeNull();
        h.Bridge.Adapter.Inputs.ShouldBeEmpty();
    }

    /// <summary>
    /// case: a Held snapshot of a request that has since reached a terminal outcome. mutation: drop
    /// the Held supersession. red: the snapshot is enqueued (unlinked) or stays Pending (linked) and
    /// is typed after the outcome. Controls: a pending request still gets its Held note, and a Held
    /// row already typed keeps its receipt scan.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("unlinked")]
    [Arguments("linked-untyped")]
    [Arguments("pending-request-control")]
    [Arguments("sent-control")]
    public async Task C0747_Held_snapshot_after_terminal_outcome_is_not_delivered(string arm, CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync(new() { Cache = false });
        AgentTaskLandNotification note;
        await using (var db = h.Fixture())
        {
            note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
            note.Kind = LandNotificationKind.Held;
            await db.SaveChangesAsync();
        }
        async Task SetRequestPendingAsync(bool pending)
        {
            await using var db = h.Fixture();
            await db.AgentTaskLandRequests.Where(r => r.Id == note.RequestId).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.IsPending, pending)
                .SetProperty(r => r.State, pending ? LandRequestState.Held : LandRequestState.Completed), ct);
        }
        if (arm != "unlinked")
        {
            await SetRequestPendingAsync(true);
            await h.Service.ReconcileAsync(note.Id, ct);
            note = await h.NoteAsync(note.Id);
            note.QueueMessageId.ShouldNotBeNull("a Held note of a pending request is enqueued");
            if (arm == "sent-control")
            {
                await h.MarkSentAsync(note);
                await h.IngestEventsAsync(h.SessionId, [h.Event(h.SessionId, Floor + 5, TranscriptKinds.UserPrompt, note.Body)]);
            }
            if (arm != "pending-request-control") await SetRequestPendingAsync(false);
        }

        await h.Service.ReconcileAsync(note.Id, ct);
        await h.Service.ReconcileAsync(note.Id, ct);

        var saved = await h.NoteAsync(note.Id);
        await using var check = h.Fixture();
        var rows = await check.SessionQueuedMessages.AsNoTracking().Where(m => m.SourceLandNotificationId == note.Id).ToListAsync(ct);
        switch (arm)
        {
            case "unlinked":
                saved.State.ShouldBe(LandNotificationState.NotRequired);
                saved.LastErrorCode.ShouldBe(AgentTaskLandNotificationService.HeldSupersededCode);
                rows.ShouldBeEmpty("no row is created for a superseded snapshot");
                break;
            case "linked-untyped":
                saved.State.ShouldBe(LandNotificationState.NotRequired);
                saved.LastErrorCode.ShouldBe(AgentTaskLandNotificationService.HeldSupersededCode);
                rows.Single().Status.ShouldBe(QueuedMessageStatus.Canceled);
                break;
            case "pending-request-control":
                saved.State.ShouldBe(LandNotificationState.AwaitingReceipt);
                rows.Single().Status.ShouldBe(QueuedMessageStatus.Pending);
                break;
            case "sent-control":
                saved.State.ShouldBe(LandNotificationState.Confirmed, "a typed Held note keeps its receipt scan");
                saved.LastErrorCode.ShouldBeNull();
                rows.Single().Status.ShouldBe(QueuedMessageStatus.Sent);
                break;
        }
        h.Bridge.Adapter.Inputs.ShouldBeEmpty();
    }

    /// <summary>
    /// case: the operator sweep over a mixed cohort. mutation: let the dry run apply, or drop the
    /// minimum age. red: a dry run writes, a younger, non-terminal or already-typed note is settled,
    /// or a six-day sweep is accepted. A second apply finds nothing left to settle.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C0747_Sweep_dry_run_writes_nothing_and_apply_settles_only_aged_terminal_notes(CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync(new() { Cache = false });
        var untyped = await h.SeedLinkedNoteAsync();
        AgentTaskLandNotification unlinked;
        await using (var db = h.Fixture())
        {
            unlinked = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
            unlinked.State = LandNotificationState.DestinationUnavailable;
            unlinked.NextAttemptAt = DateTime.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync(ct);
        }
        var young = await h.SeedLinkedNoteAsync();
        var pendingRequest = await h.SeedLinkedNoteAsync();
        var typed = await h.SeedLinkedNoteAsync();
        foreach (var note in new[] { untyped, unlinked, pendingRequest, typed })
            await h.UpdateNoteAsync(note.Id, n => n.CreatedAt = DateTime.UtcNow.AddDays(-8));
        await h.UpdateNoteAsync(young.Id, n => n.CreatedAt = DateTime.UtcNow.AddDays(-6));
        await h.UpdateNoteAsync(pendingRequest.Id, n => n.Kind = LandNotificationKind.Held);
        await using (var db = h.Fixture())
            await db.AgentTaskLandRequests.Where(r => r.Id == pendingRequest.RequestId).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.IsPending, true).SetProperty(r => r.State, LandRequestState.Held), ct);
        await h.UpdateRowAsync(typed.QueueMessageId!.Value, r => r.DeliveryAttempts = 1);

        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString)));
        // Debug 3b0adade: the sweep also applies the aged queue-row rule through the queue.
        services.AddSingleton(h.Bridge.Queue);
        services.AddScoped(sp => new AgentTaskLandNotificationService(sp.GetRequiredService<AppDbContext>(), h.Bridge.Queue,
            new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System));
        await using var provider = services.BuildServiceProvider();
        await using var sweepDb = h.Fixture();
        var sweep = new LandNoteExpirySweepService(sweepDb, provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        async Task<Dictionary<Guid, (LandNotificationState State, QueuedMessageStatus? Row)>> SnapshotAsync()
        {
            await using var db = h.Fixture();
            var ids = new[] { untyped.Id, unlinked.Id, young.Id, pendingRequest.Id, typed.Id };
            var notes = await db.AgentTaskLandNotifications.AsNoTracking().Where(n => ids.Contains(n.Id)).ToListAsync(ct);
            var result = new Dictionary<Guid, (LandNotificationState, QueuedMessageStatus?)>();
            foreach (var n in notes)
                result[n.Id] = (n.State, n.QueueMessageId is Guid q
                    ? await db.SessionQueuedMessages.Where(m => m.Id == q).Select(m => (QueuedMessageStatus?)m.Status).SingleAsync(ct)
                    : null);
            return result;
        }

        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ValidationException>(() => sweep.SweepAsync(true, 6, ct));
        var before = await SnapshotAsync();
        var dry = await sweep.SweepAsync(apply: false, olderThanDays: null, ct);
        (await SnapshotAsync()).ShouldBe(before, "a dry run writes nothing");
        (dry.Apply, dry.OlderThanDays, dry.Scanned, dry.Failures).ShouldBe((false, 7, 4, 0));
        dry.Outcomes.ShouldBe(new Dictionary<string, int> { ["WouldSettle"] = 2, ["NonTerminal"] = 1, ["TypedPending"] = 1 }, ignoreOrder: true);
        dry.EligibleByState.ShouldBe(new Dictionary<string, int> { ["AwaitingReceipt"] = 1, ["DestinationUnavailable"] = 1 }, ignoreOrder: true);

        var applied = await sweep.SweepAsync(apply: true, olderThanDays: 7, ct);
        applied.Outcomes.ShouldBe(new Dictionary<string, int> { ["Settled"] = 2, ["NonTerminal"] = 1, ["TypedPending"] = 1 }, ignoreOrder: true);
        var after = await SnapshotAsync();
        after[untyped.Id].ShouldBe((LandNotificationState.LegacyUnverified, (QueuedMessageStatus?)QueuedMessageStatus.Canceled));
        after[unlinked.Id].ShouldBe((LandNotificationState.LegacyUnverified, (QueuedMessageStatus?)null));
        after[young.Id].ShouldBe(before[young.Id]);
        after[pendingRequest.Id].ShouldBe(before[pendingRequest.Id]);
        after[typed.Id].ShouldBe(before[typed.Id]);

        var again = await sweep.SweepAsync(apply: true, olderThanDays: 7, ct);
        (again.Scanned, again.Outcomes.GetValueOrDefault("Settled")).ShouldBe((2, 0), "idempotent: only the two left-alone notes remain open");
    }

    /// <summary>
    /// case: an expired note is settled, not confirmed. mutation: drop the LegacyUnverified filter
    /// from the attention projection. red: every expired note stays an unconfirmed-receipt row.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C0747_Expired_note_leaves_the_unconfirmed_receipt_feed(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var expired = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var open = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        expired.CreatedAt = open.CreatedAt = DateTime.UtcNow.AddDays(-8);
        expired.State = LandNotificationState.LegacyUnverified;
        expired.LastErrorCode = AgentTaskLandNotificationService.ReceiptExpiredCode;
        open.State = LandNotificationState.AwaitingReceipt;
        open.LastErrorCode = "destination_failed";
        await db.SaveChangesAsync(ct);

        await using var fresh = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var attention = await new AttentionService(fresh, h.Runner, Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()), TimeProvider.System, NullLogger<AttentionService>.Instance).GetAsync(ct);

        attention.Items.ShouldContain(i => i.LandNotificationId == open.Id && i.Kind == AttentionKind.LandOutcomeUnconfirmed);
        attention.Items.ShouldNotContain(i => i.LandNotificationId == expired.Id);
    }
}
