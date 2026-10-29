using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1603 queue witnesses. Selection comes from FlushIfIdleAsync, cancellation from
/// CancelAsync or a real schedule/spill path, and the verdict from ReconcileAsync.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel]
public class CapacityObsoleteQueueTests
{
    [Test]
    [Arguments("human-drop-before-redeem")]
    [Arguments("human-drop-after-redeem")]
    [Arguments("schedule-supersession")]
    [Arguments("spill-undeliverable")]
    public async Task C1603_canceled_selected_message_stops_regranting(string arm)
    {
        await using var fx = await QueueFixture.OpenAsync();
        await fx.SeedWallAsync();
        var wait = await fx.EnsureLiveWaitAsync();
        await fx.Recovery.GrantReadyAsync(CancellationToken.None);
        var before = await fx.ReloadWaitAsync(wait.Id);
        var count = before.AdmissionCount;
        var action = before.ActionKey;
        Guid messageId;
        string? retainedBody = null;

        if (arm == "schedule-supersession")
        {
            messageId = await fx.SupersedeScheduleSelectionAsync();
            retainedBody = "c1603-schedule-follow-up";
        }
        else if (arm == "spill-undeliverable")
        {
            messageId = await fx.SpillUndeliverableAsync();
            count = 1;
        }
        else if (arm == "human-drop-after-redeem")
        {
            messageId = await fx.SelectByFailedDeliveryAsync(leadingUi: false);
            await fx.Queue.CancelAsync(fx.SessionId, messageId, CancellationToken.None);
            count = 1;
        }
        else
        {
            messageId = await fx.SelectByFailedDeliveryAsync(leadingUi: true);
            await fx.Queue.CancelAsync(fx.SessionId, messageId, CancellationToken.None);
        }

        var selected = await fx.ReloadWaitAsync(wait.Id);
        selected.SelectedMessageId.ShouldBe(messageId);
        selected.AdmissionCount.ShouldBe(count);
        await using (var db = fx.Ctx())
        {
            var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
            row.Status.ShouldBe(QueuedMessageStatus.Canceled);
            row.CapacityWaitId.ShouldBe(wait.Id);
        }

        var competitor = await fx.SeedCompetitorAsync(selected.BlockedAt.AddMinutes(1));
        // Due on the third 121s window, so the stall timeout does not expire the grant
        // the assertion still expects to be held.
        await using (var db = fx.Ctx())
        {
            var due = fx.Now.AddSeconds((121 * 2) + 30);
            await db.CapacityRecoveryWaits.Where(w => w.Id == competitor.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.DueAt, due));
            if (arm == "schedule-supersession")
            {
                // Y is already queued, so the same reconcile would grant it. Keep this
                // wait due later; DeliverRetainedScheduledAsync advances to that due time.
                var waitDue = fx.Now.AddMinutes(10);
                await db.CapacityRecoveryWaits.Where(w => w.Id == wait.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.DueAt, waitDue));
            }
        }

        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        var cleaned = await fx.ReloadWaitAsync(wait.Id);
        cleaned.SelectedMessageId.ShouldBeNull();
        cleaned.Id.ShouldBe(wait.Id);
        cleaned.State.ShouldBe(CapacityRecoveryWaitState.Ready);
        cleaned.OutcomeReason.ShouldBe("selected-message-canceled");
        cleaned.ActionKey.ShouldNotBe(action);
        cleaned.AdmissionCount.ShouldBe(count);
        cleaned.NeedsRevalidationGrant.ShouldBeFalse();
        await using (var db = fx.Ctx())
        {
            var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
            row.CapacityWaitId.ShouldBeNull();
            row.CapacityRecoveryActionKey.ShouldBeNull();
        }

        if (arm == "schedule-supersession")
        {
            await fx.DeliverRetainedScheduledAsync(cleaned.Id, retainedBody!);
            return;
        }

        var version = cleaned.Version;
        var rotated = cleaned.ActionKey;
        for (var window = 0; window < 3; window++)
        {
            fx.Advance(TimeSpan.FromSeconds(121));
            await fx.Recovery.ReconcileAsync(CancellationToken.None);
            var stable = await fx.ReloadWaitAsync(wait.Id);
            stable.Version.ShouldBe(version);
            stable.ActionKey.ShouldBe(rotated);
            stable.SelectedMessageId.ShouldBeNull();
            await using var db = fx.Ctx();
            var grant = await db.Set<CapacityRecoveryProviderState>().AsNoTracking()
                .SingleOrDefaultAsync(s => s.Kind == AgentKind.ClaudeCode);
            if (grant?.GrantedActionKey is { } granted)
                granted.ShouldNotBe(action);
        }

        await using (var db = fx.Ctx())
        {
            var grant = await db.Set<CapacityRecoveryProviderState>().AsNoTracking()
                .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
            grant.GrantedWaitId.ShouldBe(competitor.Id);
        }
    }

    [Test]
    [Arguments("finished-drop")]
    [Arguments("finished-parked")]
    [Arguments("gate-busy-past-expiry")]
    [Arguments("demand-loss-key-mismatch-past-expiry")]
    public async Task C1603_finished_wait_with_obsolete_selection_releases_grant(string arm)
    {
        var parked = arm == "finished-parked";
        await using var fx = await QueueFixture.OpenAsync(maxDeliveryAttempts: parked ? 1 : 3);
        await fx.SeedWallAsync();
        var wait = await fx.EnsureLiveWaitAsync();
        if (arm == "demand-loss-key-mismatch-past-expiry")
        {
            await fx.AssertKeyMismatchExpiryAsync(wait.Id);
            return;
        }

        await fx.Recovery.GrantReadyAsync(CancellationToken.None);
        var messageId = await fx.SelectByFailedDeliveryAsync(leadingUi: true);
        if (parked)
        {
            // The non-held head keeps the grant. Stamp the delivery cap on that real
            // selection so it is parked Pending without a redeem that would clear it.
            await using var db = fx.Ctx();
            await db.SessionQueuedMessages.Where(m => m.Id == messageId).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, QueuedMessageStatus.Pending)
                .SetProperty(m => m.DeliveryAttempts, 1));
        }

        if (arm != "gate-busy-past-expiry")
        {
            await fx.Recovery.ObserveTranscriptAsync(
                fx.SessionId, TranscriptKinds.TurnEnd, false, CancellationToken.None,
                stopReason: TranscriptKinds.StopReasons.EndTurn);
            (await fx.ReloadWaitAsync(wait.Id)).State.ShouldBe(CapacityRecoveryWaitState.Progressed);
        }

        if (!parked)
            await fx.Queue.CancelAsync(fx.SessionId, messageId, CancellationToken.None);

        var before = await fx.ReloadWaitAsync(wait.Id);
        before.SelectedMessageId.ShouldBe(messageId);
        await using (var linked = fx.Ctx())
        {
            var row = await linked.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
            row.CapacityWaitId.ShouldBe(wait.Id);
            row.CapacityRecoveryActionKey.ShouldBe(before.ActionKey);
            if (parked)
            {
                row.Status.ShouldBe(QueuedMessageStatus.Pending);
                row.DeliveryAttempts.ShouldBeGreaterThanOrEqualTo(1);
            }
            else
            {
                row.Status.ShouldBe(QueuedMessageStatus.Canceled);
            }

            (await linked.Set<CapacityRecoveryProviderState>().AsNoTracking()
                .SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldBe(wait.Id);
        }

        var competitor = await fx.SeedCompetitorAsync(fx.Now.AddMinutes(-1));
        if (arm == "gate-busy-past-expiry")
        {
            var gate = fx.Queue.GetLock(fx.SessionId);
            (await gate.WaitAsync(TimeSpan.Zero, CancellationToken.None)).ShouldBeTrue();
            try
            {
                fx.Advance(TimeSpan.FromSeconds(121));
                await fx.Recovery.ReconcileAsync(CancellationToken.None);
                var held = await fx.ReloadWaitAsync(wait.Id);
                held.SelectedMessageId.ShouldBe(messageId);
                held.ActionKey.ShouldBe(before.ActionKey);
                held.State.ShouldBe(before.State);
                held.Version.ShouldBe(before.Version);
                await using var db = fx.Ctx();
                (await db.Set<CapacityRecoveryProviderState>().AsNoTracking()
                    .SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldBe(competitor.Id);
            }
            finally
            {
                gate.Release();
            }

            return;
        }

        var stillHeld = false;
        DateTime? grantedAt = null;
        for (var window = 0; window < 3; window++)
        {
            fx.Advance(TimeSpan.FromSeconds(121));
            await fx.Recovery.ReconcileAsync(CancellationToken.None);
            await using var db = fx.Ctx();
            var grant = await db.Set<CapacityRecoveryProviderState>().AsNoTracking()
                .SingleOrDefaultAsync(s => s.Kind == AgentKind.ClaudeCode);
            if (grant?.GrantedWaitId == wait.Id)
            {
                stillHeld = true;
                grantedAt = grant.GrantedAt;
            }
        }

        var finished = await fx.ReloadWaitAsync(wait.Id);
        finished.State.ShouldBe(CapacityRecoveryWaitState.Progressed);
        finished.ActionKey.ShouldBe(before.ActionKey);
        finished.Version.ShouldBe(before.Version);
        stillHeld.ShouldBeFalse($"finished wait kept the grant at {grantedAt:o}");
        await using var end = fx.Ctx();
        (await end.Set<CapacityRecoveryProviderState>().AsNoTracking()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldBe(competitor.Id);
    }

    [Test]
    [Arguments("pending-below-cap-and-due")]
    [Arguments("sent")]
    [Arguments("missing-selected-row")]
    [Arguments("unselected-canceled-row")]
    [Arguments("retained-task")]
    [Arguments("queued-task")]
    [Arguments("launch-receipt")]
    [Arguments("confirmed-prompt")]
    [Arguments("dropped-launch-receipt")]
    [Arguments("parked-launch-receipt")]
    [Arguments("dropped-dispatch-receipt")]
    [Arguments("parked-dispatch-receipt")]
    [Arguments("dropped-confirmed-prompt")]
    [Arguments("parked-confirmed-prompt")]
    [Arguments("dropped-queued-task")]
    [Arguments("parked-queued-task")]
    [Arguments("dropped-retained-task")]
    [Arguments("parked-retained-task")]
    public async Task C1603_queue_cleanup_preserves_real_consumers(string arm)
    {
        await using var fx = await QueueFixture.OpenAsync();
        var wait = await fx.SeedPreservedConsumerAsync(arm);
        var seeded = await fx.ReloadWaitAsync(wait.Id);
        if (arm is not ("confirmed-prompt" or "unselected-canceled-row"))
            await fx.HoldMatchingGrantAsync(seeded);
        var before = await fx.ReloadWaitAsync(wait.Id);
        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        var after = await fx.ReloadWaitAsync(wait.Id);
        after.State.ShouldBe(before.State);
        after.AdmissionCount.ShouldBe(before.AdmissionCount);
        after.ActionKey.ShouldBe(before.ActionKey);
        after.SelectedMessageId.ShouldBe(before.SelectedMessageId);
        after.Version.ShouldBe(before.Version);
        after.NeedsRevalidationGrant.ShouldBe(before.NeedsRevalidationGrant);
        if (arm == "confirmed-prompt")
        {
            await using var db = fx.Ctx();
            (await db.Set<CapacityRecoveryProviderState>().CountAsync(s => s.GrantedWaitId == wait.Id))
                .ShouldBe(0);
        }
    }

    [Test]
    [Arguments("replacement-selection")]
    [Arguments("other-grant")]
    [Arguments("already-progressed")]
    [Arguments("resurrected-pending")]
    [Arguments("message-busy")]
    [Arguments("parked-send-now")]
    [Arguments("parked-late-confirm")]
    public async Task C1603_queue_cleanup_rechecks_selection_and_grant(string arm)
    {
        var boundary = new ProbeBoundary();
        var attempts = arm is "parked-send-now" or "parked-late-confirm" ? 1 : 3;
        await using var fx = await QueueFixture.OpenAsync(boundary, maxDeliveryAttempts: attempts);
        await fx.SeedWallAsync();
        var wait = await fx.EnsureLiveWaitAsync();
        await fx.Recovery.GrantReadyAsync(CancellationToken.None);
        var messageId = arm.StartsWith("parked", StringComparison.Ordinal)
            ? await fx.ParkSelectedAsync()
            : await fx.SelectByFailedDeliveryAsync(leadingUi: true);
        if (!arm.StartsWith("parked", StringComparison.Ordinal))
            await fx.Queue.CancelAsync(fx.SessionId, messageId, CancellationToken.None);
        var before = await fx.ReloadWaitAsync(wait.Id);
        before.SelectedMessageId.ShouldBe(messageId);

        if (arm == "message-busy")
        {
            await fx.AssertGateBusyRetainsAsync(wait.Id, messageId);
            await fx.AssertRowBusyRetainsAsync(wait.Id, messageId);
            await fx.Recovery.ReconcileAsync(CancellationToken.None);
            var detached = await fx.ReloadWaitAsync(wait.Id);
            detached.SelectedMessageId.ShouldBeNull();
            detached.State.ShouldBe(CapacityRecoveryWaitState.Ready);
            return;
        }

        boundary.OnReached = async (name, waitId, _, ct) =>
        {
            if (name != "capacity-obsolete-after-discovery" || waitId != wait.Id)
                return;
            await fx.MutateBeforeRecheckAsync(arm, wait.Id, messageId, ct);
        };

        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        await fx.AssertRecheckAsync(arm, wait.Id, messageId, before);
    }

    [Test]
    public async Task C1603_queue_cleanup_rolls_back_and_releases_gate()
    {
        var boundary = new ProbeBoundary();
        var fail = new ArmableCommitFailure();
        await using var fx = await QueueFixture.OpenAsync(boundary, fail);
        await fx.SeedWallAsync();
        var wait = await fx.EnsureLiveWaitAsync();
        await fx.Recovery.GrantReadyAsync(CancellationToken.None);
        var messageId = await fx.SelectByFailedDeliveryAsync(leadingUi: true);
        await fx.Queue.CancelAsync(fx.SessionId, messageId, CancellationToken.None);
        var before = await fx.ReloadWaitAsync(wait.Id);
        var faultArmed = false;
        boundary.OnReached = (name, waitId, _, _) =>
        {
            if (!faultArmed && name == "capacity-obsolete-before-finish" && waitId == wait.Id)
            {
                faultArmed = true;
                fail.Remaining = 1;
            }

            return Task.CompletedTask;
        };

        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        var rolled = await fx.ReloadWaitAsync(wait.Id);
        rolled.SelectedMessageId.ShouldBe(messageId);
        rolled.Version.ShouldBe(before.Version);
        rolled.AdmissionCount.ShouldBe(before.AdmissionCount);
        await using (var db = fx.Ctx())
        {
            var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
            row.CapacityWaitId.ShouldBe(wait.Id);
            var grant = await db.Set<CapacityRecoveryProviderState>().AsNoTracking()
                .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
            grant.GrantedWaitId.ShouldBe(wait.Id);
        }

        var held = fx.Queue.GetLock(fx.SessionId);
        (await held.WaitAsync(TimeSpan.Zero, CancellationToken.None)).ShouldBeTrue();
        held.Release();

        fail.Remaining = 0;
        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        (await fx.ReloadWaitAsync(wait.Id)).SelectedMessageId.ShouldBeNull();
    }

    [Test]
    public async Task C1603_queue_cleanup_loses_compare_and_set_safely()
    {
        var boundary = new ProbeBoundary();
        await using var fx = await QueueFixture.OpenAsync(boundary);
        await fx.SeedWallAsync();
        var wait = await fx.EnsureLiveWaitAsync();
        await fx.Recovery.GrantReadyAsync(CancellationToken.None);
        var messageId = await fx.SelectByFailedDeliveryAsync(leadingUi: true);
        await fx.Queue.CancelAsync(fx.SessionId, messageId, CancellationToken.None);
        var before = await fx.ReloadWaitAsync(wait.Id);
        Guid? grantAtBoundary = null;
        boundary.OnReached = async (name, waitId, _, ct) =>
        {
            if (name != "capacity-obsolete-before-finish" || waitId != wait.Id)
                return;
            await using var db = fx.Ctx();
            grantAtBoundary = (await db.Set<CapacityRecoveryProviderState>().AsNoTracking()
                .SingleAsync(s => s.Kind == AgentKind.ClaudeCode, ct)).GrantedWaitId;
            await fx.Recovery.ObserveTranscriptAsync(
                fx.SessionId, TranscriptKinds.TurnEnd, false, ct,
                stopReason: TranscriptKinds.StopReasons.EndTurn);
        };

        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        grantAtBoundary.ShouldBe(wait.Id);
        var after = await fx.ReloadWaitAsync(wait.Id);
        after.State.ShouldBe(CapacityRecoveryWaitState.Progressed);
        after.OutcomeReason.ShouldNotBe("selected-message-canceled");
        after.SelectedMessageId.ShouldBe(messageId);
        after.ActionKey.ShouldBe(before.ActionKey);
        await using var db = fx.Ctx();
        (await db.Set<CapacityRecoveryProviderState>().AsNoTracking()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldBeNull();
    }

    [Test]
    public async Task C1603_queue_cleanup_is_bounded_and_idempotent()
    {
        await using var fx = await QueueFixture.OpenAsync(batch: 2);
        var seeded = await fx.SeedCanceledBatchAsync(3);
        var control = seeded.Control;
        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        (await fx.CountClearedAsync(seeded.Ids)).ShouldBe(2);
        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        (await fx.CountClearedAsync(seeded.Ids)).ShouldBe(3);
        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        (await fx.CountClearedAsync(seeded.Ids)).ShouldBe(3);
        foreach (var id in seeded.Ids)
        {
            var wait = await fx.ReloadWaitAsync(id);
            wait.State.ShouldNotBe(CapacityRecoveryWaitState.Canceled);
            wait.State.ShouldNotBe(CapacityRecoveryWaitState.Superseded);
            wait.AdmissionCount.ShouldBe(0);
        }

        var kept = await fx.ReloadWaitAsync(control);
        kept.SelectedMessageId.ShouldNotBeNull();
        kept.ActionKey.ShouldBe(seeded.ControlAction);
    }

    [Test]
    [Arguments("unselected-held-pending-channel")]
    [Arguments("unselected-held-pending-scheduled")]
    [Arguments("later-held-input-after-cleanup-channel")]
    [Arguments("later-held-input-after-cleanup-scheduled")]
    [Arguments("parked-selected-channel")]
    [Arguments("parked-selected-scheduled")]
    public async Task C1603_cleanup_keeps_held_input_deliverable(string arm)
    {
        var parked = arm.StartsWith("parked", StringComparison.Ordinal);
        var scheduled = arm.EndsWith("scheduled", StringComparison.Ordinal);
        var unselected = arm.StartsWith("unselected", StringComparison.Ordinal);
        await using var fx = await QueueFixture.OpenAsync(maxDeliveryAttempts: parked ? 1 : 3);
        await fx.SeedWallAsync();
        await fx.AdvanceCursorPastSessionAsync();
        var wait = await fx.EnsureLiveWaitAsync();
        await fx.Recovery.GrantReadyAsync(CancellationToken.None);
        var origin = scheduled ? QueuedMessageOrigin.Scheduled : QueuedMessageOrigin.Channel;
        Guid selected;
        if (parked)
            selected = await fx.ParkSelectedAsync(origin);
        else
            selected = await fx.SelectByFailedDeliveryAsync(leadingUi: true, origin: origin);

        var beforeDrop = await fx.ReloadWaitAsync(wait.Id);
        beforeDrop.SelectedMessageId.ShouldBe(selected);
        Guid? provenHeld = null;
        if (unselected)
        {
            fx.Advance(TimeSpan.FromSeconds(121));
            await fx.Recovery.ReconcileAsync(CancellationToken.None);
            await fx.Queue.CancelAsync(fx.SessionId, selected, CancellationToken.None);
            // Default WhenIdle enqueue is what stamps the display hold. deliverIfIdle false
            // never reaches ApplyCapacityHoldAsync.
            var follow = await fx.EnqueueHeldAsync(origin, "c1603-already-held", deliverIfIdle: true);
            provenHeld = follow;
            await using (var db = fx.Ctx())
            {
                var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == follow);
                row.Status.ShouldBe(QueuedMessageStatus.Pending);
                row.NoteHeader.ShouldBe("Held");
                row.CapacityRecoveryActionKey.ShouldBeNull();
            }

            // Drop is done and Y is held. Cleanup has to retire X before a later grant
            // can admit Y; a canceled selection still blocks the provider.
            await fx.Recovery.ReconcileAsync(CancellationToken.None);
        }
        else if (!parked)
        {
            await fx.Queue.CancelAsync(fx.SessionId, selected, CancellationToken.None);
            await fx.Recovery.ReconcileAsync(CancellationToken.None);
            for (var window = 0; window < 3; window++)
            {
                fx.Advance(TimeSpan.FromSeconds(121));
                await fx.Recovery.ReconcileAsync(CancellationToken.None);
            }

            await using var quiet = fx.Ctx();
            var grant = await quiet.Set<CapacityRecoveryProviderState>().AsNoTracking()
                .SingleOrDefaultAsync(s => s.Kind == AgentKind.ClaudeCode);
            grant?.GrantedWaitId.ShouldNotBe(wait.Id);
        }

        if (parked)
        {
            await using (var db = fx.Ctx())
            {
                var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == selected);
                row.Status.ShouldBe(QueuedMessageStatus.Pending);
                row.DeliveryAttempts.ShouldBeGreaterThanOrEqualTo(1);
                row.CapacityWaitId.ShouldBe(wait.Id);
            }

            fx.ShowEmptyComposer();
            await fx.Recovery.ReconcileAsync(CancellationToken.None);
            var detached = await fx.ReloadWaitAsync(wait.Id);
            detached.SelectedMessageId.ShouldBeNull();
            detached.NeedsRevalidationGrant.ShouldBeFalse();
            detached.AdmissionCount.ShouldBe(beforeDrop.AdmissionCount);
            await using (var db = fx.Ctx())
            {
                var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == selected);
                row.Status.ShouldBe(QueuedMessageStatus.Pending);
                row.Body.ShouldNotBeNullOrWhiteSpace();
            }

            for (var window = 0; window < 3; window++)
            {
                var version = (await fx.ReloadWaitAsync(wait.Id)).Version;
                fx.Advance(TimeSpan.FromSeconds(121));
                await fx.Recovery.ReconcileAsync(CancellationToken.None);
                (await fx.ReloadWaitAsync(wait.Id)).Version.ShouldBe(version);
            }
        }

        // The unselected arms already hold the row this delivery must use. A second
        // body would sit behind it. A non-held Ui head, when present, takes the first
        // flush; the selected Channel/Scheduled row goes on the next one.
        string body;
        Guid followUp;
        if (provenHeld is Guid existing)
        {
            body = "c1603-already-held";
            followUp = existing;
        }
        else
        {
            body = "c1603-deliver-" + arm;
            followUp = await fx.EnqueueHeldAsync(origin, body);
        }

        var ready = await fx.ReloadWaitAsync(wait.Id);
        if (ready.DueAt is { } due && due > fx.Now)
            fx.Advance(due - fx.Now + TimeSpan.FromSeconds(1));
        await fx.Recovery.GrantReadyAsync(CancellationToken.None);
        fx.Adapter.ThrowOnSend = null;
        fx.ShowEmptyComposer();
        await fx.Queue.FlushIfIdleAsync(fx.SessionId, CancellationToken.None);
        await fx.Queue.FlushIfIdleAsync(fx.SessionId, CancellationToken.None);
        var delivered = await fx.ReloadWaitAsync(wait.Id);
        delivered.Id.ShouldBe(wait.Id);
        await using var verify = fx.Ctx();
        var sent = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == followUp);
        sent.Status.ShouldBe(QueuedMessageStatus.Sent);
        sent.CapacityWaitId.ShouldBe(wait.Id);
        sent.CapacityRecoveryActionKey.ShouldBe(delivered.ActionKey);
        (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == fx.SessionId
            && t.Kind == TranscriptKinds.UserPrompt
            && t.Text != null
            && t.Text.Contains(body))).ShouldBeGreaterThan(0);
    }

    [Test]
    [Arguments("never-selected-empty")]
    [Arguments("detached-empty")]
    [Arguments("independent-task")]
    [Arguments("attempt-cap")]
    [Arguments("parked-only")]
    [Arguments("future-held-only")]
    [Arguments("granted-then-demand-lost-grant-expired")]
    [Arguments("granted-then-demand-lost-new-wall-wave")]
    [Arguments("legacy-null-session-empty")]
    [Arguments("legacy-null-session-held")]
    public async Task C1603_empty_queue_demand_does_not_reserve_provider(string arm)
    {
        await using var fx = await QueueFixture.OpenAsync(batch: 2, maxDeliveryAttempts: 1);
        if (arm is "legacy-null-session-empty" or "legacy-null-session-held")
        {
            await fx.AssertLegacyNullSessionAsync(arm.EndsWith("held", StringComparison.Ordinal));
            return;
        }

        if (arm is "granted-then-demand-lost-grant-expired" or "granted-then-demand-lost-new-wall-wave")
        {
            await fx.AssertDemandLossAsync(arm);
            return;
        }

        if (arm == "independent-task")
        {
            var task = await fx.SeedCompetitorAsync(DateTime.UtcNow.AddMinutes(-10));
            await fx.Recovery.GrantReadyAsync(CancellationToken.None);
            await using var db = fx.Ctx();
            (await db.Set<CapacityRecoveryProviderState>().AsNoTracking().SingleAsync(s => s.Kind == AgentKind.ClaudeCode))
                .GrantedWaitId.ShouldBe(task.Id);
            return;
        }

        if (arm == "attempt-cap")
        {
            await fx.AssertEpisodeCapBlocksFreeGrantAsync();
            return;
        }

        if (arm == "future-held-only")
        {
            // Seeded guard: a selected Pending row with a future HoldUntil cannot be produced by
            // enqueue (HoldUntil is only set there and later cleared). Admitted consumers with no
            // deliverable demand release to Ready because ConfirmPromptOnAsync ignores Ready.
            await fx.AssertFutureHeldGuardAsync();
            return;
        }

        await fx.SeedExcludedQueueWaitsAsync(arm);
        var competitor = await fx.SeedCompetitorAsync(fx.Now.AddMinutes(1));
        await fx.Recovery.GrantReadyAsync(CancellationToken.None);
        await using var granted = fx.Ctx();
        (await granted.Set<CapacityRecoveryProviderState>().AsNoTracking()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode))
            .GrantedWaitId.ShouldBe(competitor.Id);
    }

    private sealed class QueueFixture : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public required BridgeQueueHarness Harness { get; init; }
        public required OffsetClock Time { get; init; }
        public CapacityRecoveryService Recovery => Harness.Provider.GetRequiredService<CapacityRecoveryService>();
        public SessionMessageQueueService Queue => Harness.Queue;
        public FakeAgentProtocolAdapter Adapter => Harness.Adapter;
        public Guid SessionId => Harness.SessionId;
        public DateTime Now => Time.GetUtcNow().UtcDateTime;

        public static async Task<QueueFixture> OpenAsync(
            ProbeBoundary? boundary = null,
            ArmableCommitFailure? commitFailure = null,
            int maxDeliveryAttempts = 3,
            int batch = 100)
        {
            var time = new OffsetClock(DateTimeOffset.UtcNow);
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var harness = await ClaudeDialogFixture.CreateBridgeAsync(new BridgeQueueHarness.HarnessOptions
            {
                AlwaysOn = true,
                ConnectionString = schema.ConnectionString,
                TimeProvider = time,
                Supervision = Supervision(maxDeliveryAttempts, batch),
                ConfigureDbContext = commitFailure is null ? null : options => options.AddInterceptors(commitFailure),
                ConfigureServices = boundary is null
                    ? RegisterSchedule
                    : services =>
                    {
                        RegisterSchedule(services);
                        services.AddSingleton<LandDeliveryBoundary>(boundary);
                    },
            });
            return new QueueFixture { Schema = schema, Harness = harness, Time = time };
        }

        public AppDbContext Ctx() => CapacityRecoveryTestSupport.CreateContext(Schema);

        public void Advance(TimeSpan by) => Time.Advance(by);

        public void ShowEmptyComposer() => Adapter.RenderedScreenOverride = ClaudeDialogFixture.ComposerScreen();

        public async Task SeedWallAsync()
        {
            await using var db = Ctx();
            var now = Now;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = 1,
                Kind = TranscriptKinds.UserPrompt, Text = "hello", CreatedAt = now,
            });
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = 2,
                Kind = TranscriptKinds.TurnEnd, StopReason = TranscriptKinds.StopReasons.Error,
                Text = "usage balance exhausted", IsApiError = true, ApiErrorClass = "payment_required",
                ApiErrorStatus = 402, CreatedAt = now,
            });
            db.ApiErrorRecoveries.Add(new ApiErrorRecovery
            {
                Id = Guid.NewGuid(), AgentSessionId = SessionId, StubSequence = 2,
                Classification = ApiErrorClassification.Wall, ApiErrorClass = "payment_required",
                ApiErrorStatus = 402, DetectedAt = now, ResolvedAt = now,
                ResolvedReason = ApiErrorRecoveryReasons.WallModelPaused,
            });
            await db.SaveChangesAsync();
        }

        public async Task<CapacityRecoveryWait> EnsureLiveWaitAsync()
        {
            return await Recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"session:{SessionId:N}", CapacityWaitConsumerKind.LiveSession,
                holdAlreadyCleared: true, sessionId: SessionId, blockedAt: Now.AddMinutes(-5)),
                CancellationToken.None);
        }

        public async Task<CapacityRecoveryWait> ReloadWaitAsync(Guid id)
        {
            await using var db = Ctx();
            return await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == id);
        }

        public async Task<Guid> SelectByFailedDeliveryAsync(
            bool leadingUi, QueuedMessageOrigin origin = QueuedMessageOrigin.Channel)
        {
            ShowEmptyComposer();
            Adapter.ThrowOnSend = new InvalidOperationException("c1603 delivery failed");
            Adapter.PromptFailure = _ => new InvalidOperationException("c1603 delivery failed");
            if (leadingUi)
            {
                await Queue.EnqueueAsync(SessionId, "ui head", MessageSendMode.WhenIdle, CancellationToken.None,
                    origin: QueuedMessageOrigin.Ui, deliverIfIdle: false);
            }

            var id = Guid.Empty;
            await Queue.EnqueueAsync(
                SessionId, "c1603-selected", MessageSendMode.WhenIdle, CancellationToken.None,
                origin: origin, conversationKey: "telegram:-1001", deliverIfIdle: false,
                onCreated: created => id = created);
            await Recovery.GrantReadyAsync(CancellationToken.None);
            await Queue.FlushIfIdleAsync(SessionId, CancellationToken.None);
            return id;
        }

        public async Task<Guid> ParkSelectedAsync(QueuedMessageOrigin origin = QueuedMessageOrigin.Channel)
        {
            var id = await SelectByFailedDeliveryAsync(leadingUi: false, origin: origin);
            await using var db = Ctx();
            var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            row.DeliveryAttempts.ShouldBeGreaterThanOrEqualTo(1);
            return id;
        }

        public async Task<Guid> SpillUndeliverableAsync()
        {
            ShowEmptyComposer();
            var id = Guid.Empty;
            await Queue.EnqueueAsync(
                SessionId, "spill source", MessageSendMode.WhenIdle, CancellationToken.None,
                origin: QueuedMessageOrigin.Channel, conversationKey: "telegram:-1001", deliverIfIdle: false,
                onCreated: created => id = created);
            var relative = TypedBodySpill.InboxRelativePath(id.ToString("D"));
            await using (var db = Ctx())
            {
                var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
                row.Body = TypedBodySpill.PointerHeadline + "\n\n    " + relative;
                row.RemoteSpillBody = null;
                row.RemoteSpillRelativePath = relative;
                await db.SaveChangesAsync();
            }

            await Recovery.GrantReadyAsync(CancellationToken.None);
            await Queue.FlushIfIdleAsync(SessionId, CancellationToken.None);
            return id;
        }

        public async Task<Guid> SupersedeScheduleSelectionAsync()
        {
            ShowEmptyComposer();
            // The non-held Ui head must stay in front and fail, or the first scheduled
            // copy is typed and is no longer the Pending selection.
            Adapter.ThrowOnSend = new InvalidOperationException("c1603 delivery failed");
            Adapter.PromptFailure = _ => new InvalidOperationException("c1603 delivery failed");
            await Queue.EnqueueAsync(SessionId, "ui head", MessageSendMode.WhenIdle, CancellationToken.None,
                origin: QueuedMessageOrigin.Ui, deliverIfIdle: false);
            var scheduleId = await SeedScheduleAsync("c1603-schedule-original");
            await FireScheduleAsync(scheduleId);
            if ((await ReloadSelectedWaitAsync()).SelectedMessageId is null)
            {
                await Recovery.GrantReadyAsync(CancellationToken.None);
                ShowEmptyComposer();
                await Queue.FlushIfIdleAsync(SessionId, CancellationToken.None);
            }

            Guid selected;
            await using (var db = Ctx())
            {
                selected = await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.SourceScheduleId == scheduleId && m.Status == QueuedMessageStatus.Pending)
                    .Select(m => m.Id)
                    .SingleAsync();
            }

            (await ReloadWaitAsync((await ReloadSelectedWaitAsync()).Id)).SelectedMessageId.ShouldBe(selected);
            await Harness.MarkWorkingAsync();
            await using (var edited = Ctx())
            {
                var schedule = await edited.Schedules.SingleAsync(s => s.Id == scheduleId);
                schedule.PromptText = "c1603-schedule-follow-up";
                await edited.SaveChangesAsync();
            }

            await FireScheduleAsync(scheduleId);
            await using var verify = Ctx();
            var rows = await verify.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.SourceScheduleId == scheduleId)
                .ToListAsync();
            rows.Single(m => m.Id == selected).Status.ShouldBe(QueuedMessageStatus.Canceled);
            rows.Single(m => m.Id == selected).DeliveryAttempts.ShouldBe(0);
            rows.Single(m => m.Id != selected).Status.ShouldBe(QueuedMessageStatus.Pending);
            return selected;
        }

        public async Task DeliverRetainedScheduledAsync(Guid waitId, string token)
        {
            await Harness.MarkIdleAsync();
            var wait = await ReloadWaitAsync(waitId);
            if (wait.DueAt is { } due && due > Now)
                Advance(due - Now + TimeSpan.FromSeconds(1));
            Adapter.ThrowOnSend = null;
            ShowEmptyComposer();
            await Recovery.GrantReadyAsync(CancellationToken.None);
            // The failed Ui head is still in front of the retained scheduled row.
            await Queue.FlushIfIdleAsync(SessionId, CancellationToken.None);
            await Queue.FlushIfIdleAsync(SessionId, CancellationToken.None);
            var after = await ReloadWaitAsync(waitId);
            after.Id.ShouldBe(waitId);
            await using var db = Ctx();
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == SessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Text != null
                && t.Text.Contains(token))).ShouldBeGreaterThan(0);
        }

        public async Task<CapacityRecoveryWait> SeedCompetitorAsync(DateTime blockedAt)
        {
            var taskId = Guid.NewGuid();
            await using (var db = Ctx())
            {
                db.AgentTasks.Add(new AgentTask
                {
                    Id = taskId, RootTaskId = taskId, Title = "c1603 competitor", Goal = "grant",
                    AgentKind = AgentKind.ClaudeCode, Status = AgentTaskStatus.Queued,
                    CreatedAt = Now, ConcurrencyToken = Guid.NewGuid(),
                });
                await db.SaveChangesAsync();
            }

            return await Recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"task:{taskId:N}", CapacityWaitConsumerKind.QueuedTask,
                holdAlreadyCleared: true, taskId: taskId, blockedAt: blockedAt), CancellationToken.None);
        }

        public async Task AdvanceCursorPastSessionAsync()
        {
            await using var db = Ctx();
            var cursor = await db.CapacityRecoveryWaits
                .FirstOrDefaultAsync(w => w.ConsumerKey == CapacityRecoveryService.CompatibilityCursorKey);
            if (cursor is null)
            {
                cursor = new CapacityRecoveryWait
                {
                    Id = Guid.NewGuid(),
                    ConsumerKey = CapacityRecoveryService.CompatibilityCursorKey,
                    ConsumerKind = CapacityWaitConsumerKind.LiveSession,
                    ExecutionKind = AgentKind.ClaudeCode,
                    State = CapacityRecoveryWaitState.Progressed,
                    BlockedAt = Now,
                    ActionKey = CapacityRecoveryService.CompatibilityCursorKey,
                    UpdatedAt = Now,
                    Version = 1,
                };
                db.CapacityRecoveryWaits.Add(cursor);
            }

            cursor.OutcomeReason = "z";
            cursor.State = CapacityRecoveryWaitState.Progressed;
            await db.SaveChangesAsync();
        }

        public async Task<Guid> EnqueueHeldAsync(
            QueuedMessageOrigin origin, string body, bool deliverIfIdle = false)
        {
            var id = Guid.Empty;
            await Queue.EnqueueAsync(SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None,
                origin: origin, conversationKey: "telegram:-1009", deliverIfIdle: deliverIfIdle,
                sourceScheduleId: origin == QueuedMessageOrigin.Scheduled ? Guid.NewGuid() : null,
                onCreated: created => id = created);
            return id;
        }

        public async Task AssertGateBusyRetainsAsync(Guid waitId, Guid messageId)
        {
            var gate = Queue.GetLock(SessionId);
            (await gate.WaitAsync(TimeSpan.Zero, CancellationToken.None)).ShouldBeTrue();
            try
            {
                await Recovery.ReconcileAsync(CancellationToken.None);
                (await ReloadWaitAsync(waitId)).SelectedMessageId.ShouldBe(messageId);
                await using var db = Ctx();
                (await db.Set<CapacityRecoveryProviderState>().AsNoTracking()
                    .SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldBe(waitId);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task AssertRowBusyRetainsAsync(Guid waitId, Guid messageId)
        {
            await using var connection = new NpgsqlConnection(Schema.ConnectionString);
            await connection.OpenAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand(
                """SELECT "Id" FROM "SessionQueuedMessages" WHERE "Id" = @id FOR UPDATE""", connection, tx);
            command.Parameters.AddWithValue("id", messageId);
            (await command.ExecuteScalarAsync()).ShouldBe(messageId);
            try
            {
                await Recovery.ReconcileAsync(CancellationToken.None);
                (await ReloadWaitAsync(waitId)).SelectedMessageId.ShouldBe(messageId);
            }
            finally
            {
                await tx.RollbackAsync();
            }
        }

        public async Task MutateBeforeRecheckAsync(string arm, Guid waitId, Guid messageId, CancellationToken ct)
        {
            await using var db = Ctx();
            if (arm == "replacement-selection")
            {
                // CapacityRecoveryActionKey is unique. Move the linkage off X before Y takes it.
                var old = await db.SessionQueuedMessages.SingleAsync(m => m.Id == messageId, ct);
                old.CapacityRecoveryActionKey = null;
                old.CapacityWaitId = null;
                old.CapacityWaitVersion = null;
                await db.SaveChangesAsync(ct);
                var replacement = await EnqueueHeldAsync(QueuedMessageOrigin.Channel, "c1603-replacement");
                var wait = await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == waitId, ct);
                wait.SelectedMessageId = replacement;
                var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == replacement, ct);
                row.CapacityWaitId = wait.Id;
                row.CapacityRecoveryActionKey = wait.ActionKey;
                row.CapacityWaitVersion = wait.Version;
                await db.SaveChangesAsync(ct);
                return;
            }

            if (arm == "other-grant")
            {
                var other = await SeedCompetitorAsync(Now.AddHours(-2));
                var provider = await db.Set<CapacityRecoveryProviderState>().SingleAsync(s => s.Kind == AgentKind.ClaudeCode, ct);
                provider.GrantedWaitId = other.Id;
                provider.GrantedActionKey = other.ActionKey;
                await db.SaveChangesAsync(ct);
                return;
            }

            if (arm == "already-progressed")
            {
                await db.CapacityRecoveryWaits.Where(w => w.Id == waitId).ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.State, CapacityRecoveryWaitState.Progressed)
                    .SetProperty(w => w.Version, w => w.Version + 1), ct);
                return;
            }

            if (arm == "resurrected-pending")
            {
                await db.SessionQueuedMessages.Where(m => m.Id == messageId).ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, QueuedMessageStatus.Pending)
                    .SetProperty(m => m.DeliveryAttempts, 0)
                    .SetProperty(m => m.CanceledAt, (DateTime?)null), ct);
                return;
            }

            if (arm == "parked-send-now")
            {
                Adapter.ThrowOnSend = null;
                ShowEmptyComposer();
                await Queue.SendNowAsync(SessionId, messageId, ct);
                return;
            }

            if (arm == "parked-late-confirm")
            {
                var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == messageId, ct);
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = 80,
                    Kind = TranscriptKinds.UserPrompt, Text = row.Body, CreatedAt = Now,
                });
                await db.SaveChangesAsync(ct);
                Adapter.ThrowOnSend = null;
                ShowEmptyComposer();
                await Queue.FlushIfIdleAsync(SessionId, ct);
            }
        }

        public async Task AssertRecheckAsync(string arm, Guid waitId, Guid messageId, CapacityRecoveryWait before)
        {
            var after = await ReloadWaitAsync(waitId);
            await using var db = Ctx();
            if (arm == "replacement-selection")
            {
                after.SelectedMessageId.ShouldNotBe(messageId);
                after.SelectedMessageId.ShouldNotBeNull();
                after.State.ShouldBe(before.State);
                return;
            }

            if (arm == "other-grant")
            {
                var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
                row.CapacityWaitId.ShouldBeNull();
                var grant = await db.Set<CapacityRecoveryProviderState>().AsNoTracking()
                    .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
                grant.GrantedWaitId.ShouldNotBe(waitId);
                return;
            }

            if (arm == "already-progressed")
            {
                after.State.ShouldBe(CapacityRecoveryWaitState.Progressed);
                after.SelectedMessageId.ShouldBe(messageId);
                return;
            }

            if (arm == "resurrected-pending")
            {
                after.SelectedMessageId.ShouldBe(messageId);
                var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
                row.Status.ShouldBe(QueuedMessageStatus.Pending);
                row.CapacityWaitId.ShouldBe(waitId);
                return;
            }

            var sent = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
            sent.Status.ShouldBe(QueuedMessageStatus.Sent);
            sent.CapacityWaitId.ShouldBe(waitId);
            after.SelectedMessageId.ShouldBe(messageId);
            after.ActionKey.ShouldBe(before.ActionKey);
        }

        public async Task<CanceledBatch> SeedCanceledBatchAsync(int count)
        {
            var ids = new List<Guid>();
            for (var i = 0; i < count; i++)
            {
                var sessionId = Guid.NewGuid();
                var messageId = Guid.NewGuid();
                await using var db = Ctx();
                var blocked = Now.AddMinutes(-30 + i);
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running, CreatedAt = blocked, StartedAt = blocked, Cwd = "/tmp",
                });
                await db.SaveChangesAsync();
                var wait = await Recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                    $"session:{sessionId:N}", CapacityWaitConsumerKind.LiveSession,
                    holdAlreadyCleared: true, sessionId: sessionId, blockedAt: blocked), CancellationToken.None);
                await using var link = Ctx();
                var current = await link.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
                link.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = messageId, AgentSessionId = sessionId, Sequence = 1, Body = "canceled",
                    Origin = QueuedMessageOrigin.Channel, Status = QueuedMessageStatus.Canceled,
                    CreatedAt = blocked, CapacityWaitId = current.Id, CapacityWaitVersion = current.Version,
                    CapacityRecoveryActionKey = current.ActionKey,
                });
                current.SelectedMessageId = messageId;
                current.State = CapacityRecoveryWaitState.ActionPending;
                await link.SaveChangesAsync();
                ids.Add(wait.Id);
            }

            var controlSession = Guid.NewGuid();
            var controlMessage = Guid.NewGuid();
            await using (var db = Ctx())
            {
                db.AgentSessions.Add(new AgentSession
                {
                    Id = controlSession, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running, CreatedAt = Now, StartedAt = Now, Cwd = "/tmp",
                });
                await db.SaveChangesAsync();
            }

            var control = await Recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"session:{controlSession:N}", CapacityWaitConsumerKind.LiveSession,
                holdAlreadyCleared: true, sessionId: controlSession, blockedAt: Now.AddHours(-1)),
                CancellationToken.None);
            await using var controlDb = Ctx();
            var controlWait = await controlDb.CapacityRecoveryWaits.SingleAsync(w => w.Id == control.Id);
            controlDb.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = controlMessage, AgentSessionId = controlSession, Sequence = 1, Body = "live",
                Origin = QueuedMessageOrigin.Channel, Status = QueuedMessageStatus.Pending,
                CreatedAt = Now, CapacityWaitId = controlWait.Id, CapacityWaitVersion = controlWait.Version,
                CapacityRecoveryActionKey = controlWait.ActionKey,
            });
            controlWait.SelectedMessageId = controlMessage;
            controlWait.State = CapacityRecoveryWaitState.ActionPending;
            await controlDb.SaveChangesAsync();
            return new CanceledBatch(ids, control.Id, controlWait.ActionKey);
        }

        public async Task<int> CountClearedAsync(IReadOnlyList<Guid> ids)
        {
            await using var db = Ctx();
            return await db.CapacityRecoveryWaits.CountAsync(w => ids.Contains(w.Id) && w.SelectedMessageId == null);
        }

        public async Task<CapacityRecoveryWait> SeedPreservedConsumerAsync(string arm)
        {
            if (arm.StartsWith("dropped-", StringComparison.Ordinal) || arm.StartsWith("parked-", StringComparison.Ordinal))
                return await SeedExemptedObsoleteSelectionAsync(arm);

            if (arm is "queued-task" or "retained-task")
            {
                var taskId = Guid.NewGuid();
                await using var db = Ctx();
                db.AgentTasks.Add(new AgentTask
                {
                    Id = taskId, RootTaskId = taskId, Title = arm, Goal = "keep",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = arm == "retained-task" ? AgentTaskStatus.Working : AgentTaskStatus.Queued,
                    CapacityWaitRetained = arm == "retained-task",
                    CreatedAt = Now, ConcurrencyToken = Guid.NewGuid(),
                });
                await db.SaveChangesAsync();
                var wait = await Recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                    $"task:{taskId:N}",
                    arm == "retained-task" ? CapacityWaitConsumerKind.LiveSession : CapacityWaitConsumerKind.QueuedTask,
                    holdAlreadyCleared: true, taskId: taskId, sessionId: arm == "retained-task" ? SessionId : null,
                    blockedAt: Now.AddMinutes(-3)), CancellationToken.None);
                if (arm == "retained-task")
                {
                    await using var link = Ctx();
                    await link.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.CapacityWaitId, wait.Id));
                }

                return wait;
            }

            var live = await EnsureLiveWaitAsync();
            await using var update = Ctx();
            var row = await update.CapacityRecoveryWaits.SingleAsync(w => w.Id == live.Id);
            if (arm == "confirmed-prompt")
            {
                row.State = CapacityRecoveryWaitState.PromptConfirmed;
                row.ConfirmedPromptSequence = 4;
                await update.SaveChangesAsync();
                return row;
            }

            if (arm == "launch-receipt")
            {
                row.LaunchReceipt = "launched";
                row.State = CapacityRecoveryWaitState.ActionPending;
                await update.SaveChangesAsync();
                return row;
            }

            var messageId = Guid.NewGuid();
            var message = new SessionQueuedMessage
            {
                Id = messageId, AgentSessionId = SessionId, Sequence = 1, Body = arm,
                Origin = QueuedMessageOrigin.Channel, CreatedAt = Now,
                CapacityWaitId = row.Id, CapacityWaitVersion = row.Version,
                CapacityRecoveryActionKey = row.ActionKey,
                Status = arm == "sent" ? QueuedMessageStatus.Sent : QueuedMessageStatus.Pending,
            };
            if (arm == "unselected-canceled-row")
            {
                message.Status = QueuedMessageStatus.Canceled;
                message.CapacityWaitId = null;
                message.CapacityRecoveryActionKey = null;
                row.SelectedMessageId = null;
            }
            else if (arm == "missing-selected-row")
            {
                row.SelectedMessageId = Guid.NewGuid();
            }
            else
            {
                row.SelectedMessageId = messageId;
                row.State = CapacityRecoveryWaitState.ActionPending;
            }

            if (arm != "missing-selected-row")
                update.SessionQueuedMessages.Add(message);
            await update.SaveChangesAsync();
            return row;
        }

        public async Task<CapacityRecoveryWait> SeedExemptedObsoleteSelectionAsync(string arm)
        {
            var parked = arm.StartsWith("parked-", StringComparison.Ordinal);
            var kind = arm[(arm.IndexOf('-', StringComparison.Ordinal) + 1)..];
            CapacityRecoveryWait wait;
            if (kind is "queued-task" or "retained-task")
            {
                var taskId = Guid.NewGuid();
                await using var db = Ctx();
                db.AgentTasks.Add(new AgentTask
                {
                    Id = taskId, RootTaskId = taskId, Title = arm, Goal = "keep",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = kind == "retained-task" ? AgentTaskStatus.Working : AgentTaskStatus.Queued,
                    CapacityWaitRetained = kind == "retained-task",
                    CreatedAt = Now, ConcurrencyToken = Guid.NewGuid(),
                });
                await db.SaveChangesAsync();
                wait = await Recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                    $"task:{taskId:N}", CapacityWaitConsumerKind.LiveSession,
                    holdAlreadyCleared: true, taskId: taskId, sessionId: SessionId,
                    blockedAt: Now.AddMinutes(-3)), CancellationToken.None);
                if (kind == "retained-task")
                {
                    await using var link = Ctx();
                    await link.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.CapacityWaitId, wait.Id));
                }
            }
            else
            {
                wait = await EnsureLiveWaitAsync();
            }

            await using var update = Ctx();
            var row = await update.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
            var messageId = Guid.NewGuid();
            update.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = messageId, AgentSessionId = SessionId, Sequence = 1, Body = arm,
                Origin = QueuedMessageOrigin.Channel, CreatedAt = Now,
                CapacityWaitId = row.Id, CapacityWaitVersion = row.Version,
                CapacityRecoveryActionKey = row.ActionKey,
                Status = parked ? QueuedMessageStatus.Pending : QueuedMessageStatus.Canceled,
                DeliveryAttempts = parked ? 3 : 0,
            });
            row.SelectedMessageId = messageId;
            row.State = CapacityRecoveryWaitState.ActionPending;
            row.NeedsRevalidationGrant = true;
            if (kind == "launch-receipt")
                row.LaunchReceipt = "launched";
            else if (kind == "dispatch-receipt")
                row.DispatchAttemptId = Guid.NewGuid();
            else if (kind == "confirmed-prompt")
                row.ConfirmedPromptSequence = 4;
            await update.SaveChangesAsync();
            return row;
        }

        public async Task AssertLegacyNullSessionAsync(bool heldDemand)
        {
            var sessionId = Guid.NewGuid();
            var blocked = Now.AddHours(-3);
            await using (var db = Ctx())
            {
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running, CreatedAt = blocked, StartedAt = blocked, Cwd = "/tmp",
                });
                await db.SaveChangesAsync();
            }

            var wait = await Recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"session:{sessionId:N}", CapacityWaitConsumerKind.LiveSession,
                holdAlreadyCleared: true, sessionId: sessionId, blockedAt: blocked), CancellationToken.None);
            await using (var db = Ctx())
            {
                await db.CapacityRecoveryWaits.Where(w => w.Id == wait.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.SessionId, (Guid?)null));
                if (heldDemand)
                {
                    db.SessionQueuedMessages.Add(new SessionQueuedMessage
                    {
                        Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 1, Body = "c1603-legacy-held",
                        Origin = QueuedMessageOrigin.Channel, Status = QueuedMessageStatus.Pending,
                        DeliveryAttempts = 0, CreatedAt = blocked,
                    });
                    await db.SaveChangesAsync();
                }
            }

            var before = await ReloadWaitAsync(wait.Id);
            before.SessionId.ShouldBeNull();
            before.ConsumerKey.ShouldBe($"session:{sessionId:N}");
            if (!heldDemand)
            {
                var competitor = await SeedCompetitorAsync(Now.AddMinutes(1));
                await Recovery.GrantReadyAsync(CancellationToken.None);
                (await ReloadProviderAsync()).GrantedWaitId.ShouldBe(competitor.Id);
                var after = await ReloadWaitAsync(wait.Id);
                after.State.ShouldBe(before.State);
                after.Version.ShouldBe(before.Version);
                after.ActionKey.ShouldBe(before.ActionKey);
                return;
            }

            await Recovery.GrantReadyAsync(CancellationToken.None);
            (await ReloadProviderAsync()).GrantedWaitId.ShouldBe(wait.Id);
        }

        public async Task HoldMatchingGrantAsync(CapacityRecoveryWait wait)
        {
            await using var db = Ctx();
            var state = await db.Set<CapacityRecoveryProviderState>()
                .SingleOrDefaultAsync(s => s.Kind == wait.ExecutionKind);
            if (state is null)
            {
                state = new CapacityRecoveryProviderState { Kind = wait.ExecutionKind, UpdatedAt = Now };
                db.Set<CapacityRecoveryProviderState>().Add(state);
            }

            state.GrantedWaitId = wait.Id;
            state.GrantedActionKey = wait.ActionKey;
            state.GrantedAt = Now;
            state.ExpectedWaitVersion = wait.Version;
            state.ExpectedOwnerId = wait.TaskId ?? wait.AgentId ?? wait.SessionId;
            state.UpdatedAt = Now;
            await db.SaveChangesAsync();
        }

        public async Task SeedExcludedQueueWaitsAsync(string arm)
        {
            for (var i = 0; i < 3; i++)
            {
                var sessionId = Guid.NewGuid();
                await using var db = Ctx();
                var blocked = Now.AddHours(-2).AddMinutes(i);
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running, CreatedAt = blocked, StartedAt = blocked, Cwd = "/tmp",
                });
                await db.SaveChangesAsync();
                var wait = await Recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                    $"session:{sessionId:N}", CapacityWaitConsumerKind.LiveSession,
                    holdAlreadyCleared: true, sessionId: sessionId, blockedAt: blocked), CancellationToken.None);
                if (arm == "never-selected-empty")
                    continue;
                await using var link = Ctx();
                var current = await link.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
                if (arm == "detached-empty")
                {
                    current.SelectedMessageId = null;
                    current.State = CapacityRecoveryWaitState.Ready;
                    await link.SaveChangesAsync();
                    continue;
                }

                var messageId = Guid.NewGuid();
                link.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = messageId, AgentSessionId = sessionId, Sequence = 1, Body = "parked",
                    Origin = QueuedMessageOrigin.Channel, Status = QueuedMessageStatus.Pending,
                    DeliveryAttempts = 1, CreatedAt = blocked, NoteHeader = "Held",
                });
                await link.SaveChangesAsync();
            }
        }

        public async Task AssertFutureHeldGuardAsync()
        {
            // Seeded guard. This fixture's delivery cap is 1, so a failed flush would park
            // the selection. HoldUntil is applied directly because enqueue is the only
            // production writer and it cannot select the row in the same insert.
            await SeedWallAsync();
            var wait = await EnsureLiveWaitAsync();
            var selected = await EnqueueHeldAsync(QueuedMessageOrigin.Channel, "c1603-selected-future");
            await Recovery.GrantReadyAsync(CancellationToken.None);
            var granted = await ReloadWaitAsync(wait.Id);
            await using (var db = Ctx())
            {
                var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == selected);
                row.CapacityWaitId = wait.Id;
                row.CapacityRecoveryActionKey = granted.ActionKey;
                row.CapacityWaitVersion = granted.Version;
                var current = await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
                current.SelectedMessageId = selected;
                await db.SaveChangesAsync();
            }

            Adapter.ThrowOnSend = null;
            var held = await EnqueueHeldAsync(QueuedMessageOrigin.Channel, "c1603-future");
            var until = Now.AddHours(2);
            await using (var db = Ctx())
            {
                await db.SessionQueuedMessages.Where(m => m.Id == selected || m.Id == held)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.HoldUntil, until));
            }

            var before = await ReloadWaitAsync(wait.Id);
            before.SelectedMessageId.ShouldBe(selected);
            await Recovery.GrantReadyAsync(CancellationToken.None);
            var withheld = await ReloadWaitAsync(wait.Id);
            withheld.SelectedMessageId.ShouldBe(selected);
            await using (var db = Ctx())
            {
                (await db.Set<CapacityRecoveryProviderState>().AsNoTracking()
                    .SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldNotBe(wait.Id);
            }

            Advance(until - Now + TimeSpan.FromSeconds(1));
            await Recovery.GrantReadyAsync(CancellationToken.None);
            ShowEmptyComposer();
            await Queue.FlushIfIdleAsync(SessionId, CancellationToken.None);
            await using var sent = Ctx();
            (await sent.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == SessionId
                && m.Status == QueuedMessageStatus.Sent)).ShouldBeGreaterThan(0);
        }

        public async Task AssertKeyMismatchExpiryAsync(Guid waitId)
        {
            var message = await EnqueueHeldAsync(QueuedMessageOrigin.Channel, "c1603-mismatch");
            await Recovery.GrantReadyAsync(CancellationToken.None);
            var granted = await ReloadWaitAsync(waitId);
            var rotated = CapacityRecoveryPolicy.ActionKey(waitId, granted.ActionOrdinal + 1);
            await using (var db = Ctx())
            {
                await db.CapacityRecoveryWaits.Where(w => w.Id == waitId).ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.ActionKey, rotated));
            }

            await Queue.CancelAsync(SessionId, message, CancellationToken.None);
            await using (var held = Ctx())
            {
                (await held.Set<CapacityRecoveryProviderState>().AsNoTracking()
                    .SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldBe(waitId);
            }

            var competitor = await SeedCompetitorAsync(Now.AddMinutes(-1));
            await Recovery.GrantReadyAsync(CancellationToken.None);
            var early = await ReloadProviderAsync();
            early.GrantedWaitId.ShouldBe(waitId);
            var lost = await ReloadWaitAsync(waitId);
            lost.ActionKey.ShouldBe(rotated);
            lost.State.ShouldBe(CapacityRecoveryWaitState.Ready);
            lost.OutcomeReason.ShouldBe("queue-demand-lost");

            Advance(TimeSpan.FromSeconds(121));
            await Recovery.ReconcileAsync(CancellationToken.None);
            var released = await ReloadWaitAsync(waitId);
            released.ActionKey.ShouldBe(rotated);
            released.State.ShouldBe(CapacityRecoveryWaitState.Ready);
            released.OutcomeReason.ShouldBe("queue-demand-lost");
            (await ReloadProviderAsync()).GrantedWaitId.ShouldBe(competitor.Id);
        }

        public async Task AssertDemandLossAsync(string arm)
        {
            await SeedWallAsync();
            var wait = await EnsureLiveWaitAsync();
            var message = await EnqueueHeldAsync(QueuedMessageOrigin.Channel, "c1603-drop-me");
            await Recovery.GrantReadyAsync(CancellationToken.None);
            var granted = await ReloadWaitAsync(wait.Id);
            var reason = arm.EndsWith("new-wall-wave", StringComparison.Ordinal) ? "new-wall-wave" : "grant-expired";
            await using (var db = Ctx())
            {
                await db.CapacityRecoveryWaits.Where(w => w.Id == wait.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.OutcomeReason, reason));
            }

            await Queue.CancelAsync(SessionId, message, CancellationToken.None);
            var competitor = await SeedCompetitorAsync(Now.AddMinutes(30));
            await using (var db = Ctx())
            {
                await db.CapacityRecoveryWaits.Where(w => w.Id == competitor.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.DueAt, Now.AddMinutes(10)));
            }

            var clock = await ReloadProviderAsync();
            var due = granted.DueAt;
            var wave = clock.WaveRevision;
            await Recovery.GrantReadyAsync(CancellationToken.None);
            var lost = await ReloadWaitAsync(wait.Id);
            lost.State.ShouldBe(CapacityRecoveryWaitState.Ready);
            lost.OutcomeReason.ShouldBe("queue-demand-lost");
            lost.Version.ShouldBe(granted.Version + 1);
            lost.ActionKey.ShouldBe(granted.ActionKey);
            lost.AdmissionCount.ShouldBe(granted.AdmissionCount);
            lost.DueAt.ShouldBe(due);
            lost.NeedsRevalidationGrant.ShouldBe(granted.NeedsRevalidationGrant);
            var released = await ReloadProviderAsync();
            released.GrantedWaitId.ShouldBeNull();
            released.NextAdmissionAt.ShouldBe(clock.NextAdmissionAt);
            released.WaveRevision.ShouldBe(wave);
            await Recovery.GrantReadyAsync(CancellationToken.None);
            (await ReloadWaitAsync(wait.Id)).Version.ShouldBe(lost.Version);
            Advance(TimeSpan.FromMinutes(11));
            await Recovery.GrantReadyAsync(CancellationToken.None);
            (await ReloadProviderAsync()).GrantedWaitId.ShouldBe(competitor.Id);
            Advance(TimeSpan.FromSeconds(121));
            await Recovery.ReconcileAsync(CancellationToken.None);
            await AdvanceCursorPastSessionAsync();
            var body = "c1603-later-demand";
            await EnqueueHeldAsync(QueuedMessageOrigin.Channel, body);
            var again = await ReloadWaitAsync(wait.Id);
            if (again.DueAt is { } next && next > Now)
                Advance(next - Now + TimeSpan.FromSeconds(1));
            Adapter.ThrowOnSend = null;
            ShowEmptyComposer();
            await Recovery.GrantReadyAsync(CancellationToken.None);
            await Queue.FlushIfIdleAsync(SessionId, CancellationToken.None);
            (await ReloadWaitAsync(wait.Id)).Id.ShouldBe(wait.Id);
            await using var dbCheck = Ctx();
            (await dbCheck.TranscriptEntries.CountAsync(t => t.AgentSessionId == SessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text != null && t.Text.Contains(body)))
                .ShouldBeGreaterThan(0);
        }

        public async Task AssertEpisodeCapBlocksFreeGrantAsync()
        {
            await SeedWallAsync();
            var wait = await EnsureLiveWaitAsync();
            await using (var db = Ctx())
            {
                await db.CapacityRecoveryWaits.Where(w => w.Id == wait.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.AdmissionCount, 3)
                    .SetProperty(w => w.State, CapacityRecoveryWaitState.Ready)
                    .SetProperty(w => w.NeedsRevalidationGrant, false));
            }

            await EnqueueHeldAsync(QueuedMessageOrigin.Channel, "c1603-cap-demand");
            await Recovery.GrantReadyAsync(CancellationToken.None);
            var capped = await ReloadWaitAsync(wait.Id);
            capped.AdmissionCount.ShouldBe(3);
            capped.State.ShouldBe(CapacityRecoveryWaitState.Exhausted);
            var body = "c1603-over-cap";
            await EnqueueHeldAsync(QueuedMessageOrigin.Channel, body);
            await Recovery.GrantReadyAsync(CancellationToken.None);
            (await ReloadWaitAsync(wait.Id)).State.ShouldBe(CapacityRecoveryWaitState.Exhausted);
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            await Schema.DisposeAsync();
        }

        private async Task<CapacityRecoveryWait> ReloadSelectedWaitAsync()
        {
            await using var db = Ctx();
            return await db.CapacityRecoveryWaits.AsNoTracking()
                .SingleAsync(w => w.SessionId == SessionId
                    && w.State != CapacityRecoveryWaitState.Progressed
                    && w.State != CapacityRecoveryWaitState.Superseded
                    && w.State != CapacityRecoveryWaitState.Canceled
                    && w.State != CapacityRecoveryWaitState.Exhausted);
        }

        private async Task<CapacityRecoveryProviderState> ReloadProviderAsync()
        {
            await using var db = Ctx();
            return await db.Set<CapacityRecoveryProviderState>().AsNoTracking()
                .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        }

        private async Task<Guid> SeedScheduleAsync(string prompt)
        {
            var id = Guid.NewGuid();
            await using var db = Ctx();
            db.Schedules.Add(new Schedule
            {
                Id = id, Name = "c1603", Kind = ScheduleKind.Prompt, Repeat = ScheduleRepeat.Daily,
                TimeZoneId = "Europe/London", NextFireAt = Now.AddMinutes(-1), Enabled = true,
                CreatedAt = Now, UpdatedAt = Now, ConcurrencyToken = Guid.NewGuid(),
                AgentId = Harness.AgentId, PromptText = prompt,
                WhenTargetDown = ScheduleWhenTargetDown.Queue, AtLocal = "09:00",
            });
            await db.SaveChangesAsync();
            return id;
        }

        private async Task FireScheduleAsync(Guid scheduleId)
        {
            await using var scope = Harness.Provider.CreateAsyncScope();
            var schedules = scope.ServiceProvider.GetRequiredService<ScheduleService>();
            await schedules.FireNowAsync(scheduleId, CancellationToken.None);
            var queue = Harness.Provider.GetRequiredService<ScheduleFireQueue>();
            queue.TryDequeue(out var claim).ShouldBeTrue();
            await schedules.FireAsync(claim, CancellationToken.None);
        }

        private static void RegisterSchedule(IServiceCollection services)
        {
            services.AddSingleton(Options.Create(new ScheduleSettings()));
            services.AddSingleton(Options.Create(new DigestSettings { TimeZone = "Europe/London" }));
            services.AddSingleton<ScheduleFireQueue>();
            services.AddSingleton<IScheduledCardActions>(new UnusedScheduledCardActions());
            services.AddScoped<ScheduleService>();
        }

        private static SupervisionSettings Supervision(int maxDeliveryAttempts, int batch) => new()
        {
            TickSeconds = 1,
            Enabled = true,
            CapacityRecovery = new CapacityRecoverySettings
            {
                Enabled = true,
                AdmissionIntervalSeconds = 60,
                JitterSeconds = 0,
                MaxEpisodeAttempts = 3,
                ReconciliationBatchSize = batch,
            },
            DeliveryVerification = new DeliveryVerificationSettings
            {
                Enabled = true,
                EvidenceTimeoutSeconds = 1,
                PollIntervalMs = 50,
                PostSubmitAdvanceTimeoutSeconds = 1,
                StrandedAgeSeconds = 0,
                TranscriptConfirmTimeoutSeconds = 3,
                ReEnterIntervalSeconds = 1,
                PostEvidenceSettleMs = 0,
                PostFailureConfirmGraceSeconds = 3,
                UnobservableBaselineConfirmClockToleranceSeconds = 30,
                BootPromptRetryDelaySeconds = 0,
                MaxDeliveryAttempts = maxDeliveryAttempts,
            },
        };
    }

    /// <summary>
    /// Manual capacity clock with real timers. A frozen <c>FakeTimeProvider</c> never completes
    /// the queue's composer-settle delay, so a successful delivery would wait forever.
    /// </summary>
    private sealed class OffsetClock : TimeProvider
    {
        private long _offsetTicks;

        public OffsetClock(DateTimeOffset start)
        {
            var aligned = start.Ticks - start.Ticks % 10;
            _offsetTicks = aligned - DateTimeOffset.UtcNow.Ticks;
        }

        public override DateTimeOffset GetUtcNow()
        {
            var ticks = DateTimeOffset.UtcNow.Ticks + Interlocked.Read(ref _offsetTicks);
            return new DateTimeOffset(ticks - ticks % 10, TimeSpan.Zero);
        }

        public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);
    }

    private sealed record CanceledBatch(List<Guid> Ids, Guid Control, string ControlAction);

    private sealed class ProbeBoundary : LandDeliveryBoundary
    {
        public Func<string, Guid, Guid, CancellationToken, Task>? OnReached { get; set; }

        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct) =>
            OnReached is null ? Task.CompletedTask : OnReached(boundary, taskId, identity, ct);
    }

    private sealed class ArmableCommitFailure : DbTransactionInterceptor
    {
        public int Remaining;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Remaining > 0)
            {
                Remaining--;
                throw new InvalidOperationException("c1603 commit failed");
            }

            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class UnusedScheduledCardActions : IScheduledCardActions
    {
        public Task<bool> ApplyAutomatedMoveAsync(
            Guid cardId, CardStatus target, string reason, string movedBy, CancellationToken ct) =>
            throw new InvalidOperationException("prompt schedule");

        public Task<bool> ReleaseAutoDispatchHoldAsync(Guid cardId, string reason, string actor, CancellationToken ct) =>
            throw new InvalidOperationException("prompt schedule");

        public Task<SpawnCardResult> SpawnAsync(Guid cardId, SpawnCardRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("prompt schedule");
    }
}
