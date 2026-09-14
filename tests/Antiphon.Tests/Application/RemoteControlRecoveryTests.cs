using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("RemoteControlRecovery")]
public class RemoteControlRecoveryTests
{
    [Test]
    public async Task C514_Dismissal_writes_only_one_Esc_payload()
    {
        await using var h = await ReadyIdleMenuAsync();
        var episode = await DetectAsync(h);
        var result = await DismissAsync(h, episode);
        result.ShouldBe(RemoteControlDismissalResult.DismissedVerified);
        h.Adapter.ConditionalInputs.ShouldBe(["\u001b"]);
        h.Adapter.ConditionalInputs.ShouldNotContain("\r");
        h.Adapter.Inputs.ShouldBe(["\u001b"]);
    }

    [Test]
    public async Task C514_Clear_without_Esc_is_ObservedClear()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Adapter.RemoteControlMenuOpen = true;
        var episode = await DetectAsync(h);
        h.Adapter.RemoteControlMenuOpen = false;
        var result = await DismissAsync(h, episode);
        result.ShouldBe(RemoteControlDismissalResult.ObservedClear);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
        await using var db = h.CreateDb();
        var reloaded = await db.RemoteControlModalEpisodes.SingleAsync(e => e.Id == episode);
        reloaded.Resolution.ShouldBe(RemoteControlEpisodeResolution.ObservedClear);
        reloaded.DismissalSentAt.ShouldBeNull();
        reloaded.DismissalVerifiedAt.ShouldBeNull();
    }

    [Test]
    public async Task C514_Remnant_after_Esc_keeps_barrier_unverified()
    {
        await using var h = await ReadyIdleMenuAsync();
        var episode = await DetectAsync(h);
        h.Adapter.SwallowEsc = 1;
        h.Runner.RenderedScreenOverride = "Disconnect this session\nEsc to continue";
        var result = await DismissAsync(h, episode);
        result.ShouldBe(RemoteControlDismissalResult.EscSentUnverified);
        await using var db = h.CreateDb();
        var reloaded = await db.RemoteControlModalEpisodes.SingleAsync(e => e.Id == episode);
        reloaded.DismissalVerifiedAt.ShouldBeNull();
        reloaded.Resolution.ShouldBe(RemoteControlEpisodeResolution.DismissUnverified);
        (await h.Queue.IsModalBlockedAsync(h.SessionId, CancellationToken.None)).ShouldBeTrue();
    }

    [Test]
    public async Task C514_Recovery_pulls_before_deciding_idle()
    {
        await using var h = await ReadyIdleMenuAsync();
        await h.MarkWorkingAsync();
        var episode = await DetectAsync(h);
        var result = await DismissAsync(h, episode);
        result.ShouldBe(RemoteControlDismissalResult.WithheldWorking);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Unproven_transcript_never_authorizes_Esc()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync(transcriptBound: false);
        h.Adapter.RemoteControlMenuOpen = true;
        var episode = await DetectAsync(h);
        var result = await DismissAsync(h, episode);
        result.ShouldBe(RemoteControlDismissalResult.WithheldUnboundTranscript);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Fresh_working_menu_never_receives_automatic_Esc()
    {
        await using var h = await ReadyIdleMenuAsync();
        await h.MarkWorkingAsync();
        var episode = await DetectAsync(h);
        (await DismissAsync(h, episode)).ShouldBe(RemoteControlDismissalResult.WithheldWorking);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Grid_is_reobserved_after_waiting_for_delivery_lock()
    {
        await using var h = await ReadyIdleMenuAsync();
        var episode = await DetectAsync(h);
        h.Adapter.RemoteControlMenuOpen = false;
        var result = await DismissAsync(h, episode);
        result.ShouldBe(RemoteControlDismissalResult.ObservedClear);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Esc_intent_failure_has_zero_side_effects()
    {
        await using var h = await ReadyIdleMenuAsync();
        var episode = await DetectAsync(h);
        h.Recovery.BeforePersist = name => Task.FromResult(name == "esc-intent");
        var result = await DismissAsync(h, episode);
        result.ShouldBe(RemoteControlDismissalResult.DetectionOnly);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
        await using var db = h.CreateDb();
        (await db.RemoteControlModalEpisodes.SingleAsync(e => e.Id == episode))
            .DismissalIntentAt.ShouldBeNull();
    }

    [Test]
    public async Task C514_Possibly_sent_Esc_is_not_repeated_after_restart()
    {
        await using var h = await ReadyIdleMenuAsync();
        var episode = await DetectAsync(h);
        h.Adapter.SwallowEsc = 5;
        await DismissAsync(h, episode);
        h.Adapter.ConditionalInputs.Count.ShouldBe(1);
        for (var i = 0; i < 3; i++)
        {
            var again = await DismissAsync(h, episode);
            again.ShouldBe(RemoteControlDismissalResult.EscSentUnverified);
        }

        h.Adapter.ConditionalInputs.ShouldBe(["\u001b"]);
    }

    [Test]
    public async Task C514_Automatic_Esc_does_not_create_manual_turn_evidence()
    {
        await using var h = await ReadyIdleMenuAsync();
        var episode = await DetectAsync(h);
        await DismissAsync(h, episode);
        await using var db = h.CreateDb();
        (await db.TranscriptEntries.CountAsync(t =>
            t.AgentSessionId == h.SessionId && t.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
        h.Adapter.ConditionalInputs.ShouldBe(["\u001b"]);
    }

    [Test]
    public async Task C514_Dismissal_needs_two_separated_clear_observations()
    {
        await using var h = await ReadyIdleMenuAsync();
        var episode = await DetectAsync(h);
        var snapshots = 0;
        h.Runner.SnapshotOverride = _ =>
        {
            snapshots++;
            var screen = snapshots switch
            {
                1 => RemoteControlRecoveryHarness.MenuScreen,
                2 => "> ",
                _ => "Disconnect this session\nEsc to continue",
            };
            return Task.FromResult(new Antiphon.Server.Application.Dtos.SessionRunnerSnapshotDto(
                h.SessionId, screen, screen, h.Adapter.SnapshotSequence, h.Generation, h.Generation));
        };
        var result = await DismissAsync(h, episode);
        result.ShouldNotBe(RemoteControlDismissalResult.DismissedVerified);
        await using var db = h.CreateDb();
        (await db.RemoteControlModalEpisodes.SingleAsync(e => e.Id == episode))
            .DismissalVerifiedAt.ShouldBeNull();
    }

    [Test]
    public async Task C514_Postread_failure_or_deadline_remains_unverified()
    {
        await using var h = await ReadyIdleMenuAsync();
        var episode = await DetectAsync(h);
        var n = 0;
        h.Runner.SnapshotOverride = async id =>
        {
            n++;
            if (n > 1)
                throw new InvalidOperationException("post-read failed");
            return await DefaultSnapshot(h, id);
        };
        var result = await DismissAsync(h, episode);
        result.ShouldBe(RemoteControlDismissalResult.EscSentUnverified);
        await using var db = h.CreateDb();
        (await db.RemoteControlModalEpisodes.SingleAsync(e => e.Id == episode))
            .DismissalVerifiedAt.ShouldBeNull();
    }

    [Test]
    public async Task C514_Clear_frames_from_replacement_do_not_verify_old_Esc()
    {
        await using var h = await ReadyIdleMenuAsync();
        var episode = await DetectAsync(h);
        var replacement = SessionGeneration.Next(h.Generation, DateTime.UtcNow.AddMinutes(1));
        var n = 0;
        h.Runner.SnapshotOverride = _ =>
        {
            n++;
            var gen = n == 1 ? h.Generation : replacement;
            var screen = n == 1 ? RemoteControlRecoveryHarness.MenuScreen : "> ";
            return Task.FromResult(new Antiphon.Server.Application.Dtos.SessionRunnerSnapshotDto(
                h.SessionId, screen, screen, 0, gen, gen));
        };
        var result = await DismissAsync(h, episode);
        result.ShouldNotBe(RemoteControlDismissalResult.DismissedVerified);
        await using var db = h.CreateDb();
        var reloaded = await db.RemoteControlModalEpisodes.SingleAsync(e => e.Id == episode);
        reloaded.Resolution.ShouldNotBe(RemoteControlEpisodeResolution.DismissedVerified);
    }

    [Test]
    public async Task C514_Fresh_external_clear_releases_original_work()
    {
        await using var h = await ReadyIdleMenuAsync();
        var episode = await DetectAsync(h);
        var work = "held work prompt body c514xx";
        await h.Queue.EnqueueAsync(h.SessionId, work, MessageSendMode.WhenIdle, CancellationToken.None);
        h.Adapter.Inputs.ShouldBeEmpty();
        h.Adapter.RemoteControlMenuOpen = false;
        var result = await DismissAsync(h, episode);
        result.ShouldBe(RemoteControlDismissalResult.ObservedClear);
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldContain(work);
    }

    [Test]
    public async Task C514_Intent_is_committed_without_holding_transaction_during_IO()
    {
        await using var h = await ReadyIdleMenuAsync();
        var episode = await DetectAsync(h);
        var sawIntent = false;
        h.Recovery.HoldDuringIo = async () =>
        {
            await using var db = h.CreateDb();
            db.Database.CurrentTransaction.ShouldBeNull();
            (await db.RemoteControlModalEpisodes.SingleAsync(e => e.Id == episode))
                .DismissalIntentAt.ShouldNotBeNull();
            sawIntent = true;
        };
        await DismissAsync(h, episode);
        sawIntent.ShouldBeTrue();
    }

    [Test]
    public async Task C514_Detection_commit_failure_cannot_start_dismissal()
    {
        await using var h = await ReadyIdleMenuAsync();
        h.Recovery.BeforePersist = name => Task.FromResult(name == "detection");
        var observation = await h.Recovery.ObserveAsync(h.SessionId, h.Generation, CancellationToken.None);
        var episode = await h.Recovery.DetectAsync(
            h.SessionId, h.Generation, observation, null, CancellationToken.None);
        episode.ShouldBeNull();
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
        await using var db = h.CreateDb();
        (await db.RemoteControlModalEpisodes.CountAsync(e => e.SessionId == h.SessionId)).ShouldBe(0);
    }

    [Test]
    public async Task C514_Operator_raw_input_remains_available_during_modal()
    {
        await using var h = await ReadyIdleMenuAsync();
        _ = await DetectAsync(h);
        (await h.Queue.IsModalBlockedAsync(h.SessionId, CancellationToken.None)).ShouldBeTrue();
        await h.Runtime.SendInputAsync(h.SessionId, "\u001b", CancellationToken.None, trackManualTurn: false);
        h.Adapter.Inputs.ShouldContain("\u001b");
        await h.Queue.EnqueueAsync(h.SessionId, "held ordinary work c514", MessageSendMode.WhenIdle, CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    private static async Task<RemoteControlRecoveryHarness> ReadyIdleMenuAsync()
    {
        var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Adapter.RemoteControlMenuOpen = true;
        return h;
    }

    private static async Task<Guid> DetectAsync(RemoteControlRecoveryHarness h)
    {
        var observation = await h.Recovery.ObserveAsync(h.SessionId, h.Generation, CancellationToken.None);
        observation.Menu.IsPresent.ShouldBeTrue();
        var episode = await h.Recovery.DetectAsync(
            h.SessionId, h.Generation, observation, null, CancellationToken.None);
        episode.ShouldNotBeNull();
        return episode!.Id;
    }

    private static async Task<RemoteControlDismissalResult> DismissAsync(
        RemoteControlRecoveryHarness h, Guid episodeId)
    {
        var sem = h.Queue.GetLock(h.SessionId);
        await sem.WaitAsync(CancellationToken.None);
        try
        {
            return await h.Recovery.TryDismissIdleUnderLockAsync(
                h.SessionId, episodeId, CancellationToken.None);
        }
        finally
        {
            sem.Release();
        }
    }

    private static Task<Antiphon.Server.Application.Dtos.SessionRunnerSnapshotDto> DefaultSnapshot(
        RemoteControlRecoveryHarness h, Guid id) =>
        Task.FromResult(new Antiphon.Server.Application.Dtos.SessionRunnerSnapshotDto(
            id,
            h.Adapter.SnapshotRawOutput(),
            h.Adapter.SnapshotRenderedScreen(),
            h.Adapter.SnapshotSequence,
            h.Generation,
            h.Generation));
}
