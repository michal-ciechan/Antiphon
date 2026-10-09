using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using Mode = Antiphon.Tests.TestHelpers.LandReceiptScanHarness.ScriptedTranscriptRunner.Mode;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1157 S3. The reconciler follows a whitelisted keyed row onto session B, and only then.
/// A miss on B stays open. Any one refused condition, or a missing session row, scans the parent.
/// A proof for one scan target does not certify the other.
/// </summary>
[Category("Integration")]
public sealed class ReceiptScanDestinationIntegrationTests
{
    private const long Floor = LandReceiptScanHarness.Floor;
    private const long PromptAt = 15;

    private sealed record Pass(
        int Commands,
        int ReceiptSelects,
        int ReceiptRows,
        IReadOnlyList<long?> Floors,
        IReadOnlyList<Guid?> Sessions,
        int PullsA,
        int PullsB,
        int SessionProjections,
        int TranscriptStatements,
        IReadOnlyList<string> Sql,
        string Roster);

    private sealed record FollowFixture(
        AgentTaskLandNotification Note,
        SessionQueuedMessage Row,
        Guid Parent,
        Guid Destination,
        DateTime ParentStartedAt);

    private static async Task<Pass> PassAsync(LandReceiptScanHarness h, Guid noteId, Guid destination, CancellationToken ct)
    {
        h.ResetCounters();
        await h.Service.ReconcileAsync(noteId, ct);
        var scans = h.Receipts.Scans;
        var transcript = h.Commands.Commands.Count(c => c.Contains("\"TranscriptEntries\"", StringComparison.Ordinal)
            && !c.Contains("session-state.seed", StringComparison.Ordinal));
        return new(h.Commands.Total, scans.Count, scans.Sum(s => s.Rows), scans.Select(s => s.Floor).ToList(),
            scans.Select(s => s.Session).ToList(), h.Runner.Pulls(h.SessionId), h.Runner.Pulls(destination),
            h.SessionProjections, transcript, scans.Select(s => s.Sql).ToList(), h.Commands.Roster());
    }

    private static async Task<IReadOnlyList<Pass>> PassesAsync(
        LandReceiptScanHarness h, Guid noteId, Guid destination, int count, CancellationToken ct)
    {
        var passes = new List<Pass>();
        for (var i = 0; i < count; i++) passes.Add(await PassAsync(h, noteId, destination, ct));
        return passes;
    }

    private static string Rosters(IReadOnlyList<Pass> passes) => string.Join("\n----\n", passes.Select(p => p.Roster));

    // V-2 method 1 -------------------------------------------------------------------------------

    [Test]
    [Timeout(180_000)]
    [Arguments("stopped")]
    [Arguments("failed")]
    [Arguments("stopped-framed-prompt")]
    [Arguments("running")]
    [Arguments("running-runner-holds-prompt")]
    [Arguments("stopped-after-session-select-fault")]
    public async Task C1157_SentRowDestinationReceiptConfirms(string destination, CancellationToken ct)
    {
        switch (destination)
        {
            case "stopped-after-session-select-fault":
                await ConfirmAfterSessionSelectFaultAsync(ct);
                return;
            case "running-runner-holds-prompt":
                await ConfirmFromRunnerTranscriptAsync(ct);
                return;
        }

        await using var h = await LandReceiptScanHarness.CreateAsync();
        var status = destination switch
        {
            "failed" => SessionStatus.Failed,
            "running" => SessionStatus.Running,
            _ => SessionStatus.Stopped,
        };
        var onB = destination == "stopped-framed-prompt"
            ? At(PromptAt, text: body => "prefix\n" + body + "\nsuffix")
            : At(PromptAt);
        var fx = await ArrangeFollowAsync(h, status, onB: onB);

        var passes = await PassesAsync(h, fx.Note.Id, fx.Destination, 3, ct);

        var roster = Rosters(passes);
        passes[0].Sessions.ShouldBe([fx.Destination], "the scanning pass reads B\n" + roster);
        passes[0].Sessions.Count.ShouldBe(1);
        await ShouldBeConfirmedAtAsync(h, fx.Note.Id, PromptAt, fx.Destination);
        var metrics = h.Cache!.GetMetrics();
        metrics.Publishes.ShouldBe(0);
        metrics.Proofs.ShouldBe(0);
        passes.Select(p => p.PullsA).ShouldBe([0, 0, 0]);
        passes.Select(p => p.PullsB).ShouldBe([1, 0, 0], "catch-up runs on the confirming pass");
        switch (destination)
        {
            case "stopped":
            case "failed":
                passes.Select(p => p.Commands).ShouldBe([7, 2, 2], roster);
                passes.Select(p => p.ReceiptSelects).ShouldBe([1, 0, 0]);
                passes.Select(p => p.SessionProjections).ShouldBe([2, 0, 0], roster);
                passes[0].Floors.ShouldBe([Floor]);
                if (destination == "stopped")
                    await ConfirmWithoutCacheAsync(h, fx, ct);
                break;
            case "running":
                passes.Select(p => p.Commands).ShouldBe([7, 2, 2], roster);
                Refusal(h, "eligibility:DestinationStatus").ShouldBe(1);
                (await h.StatusAsync(fx.Destination)).ShouldBe(SessionStatus.Running);
                (await h.NoteAsync(fx.Note.Id)).LastErrorCode.ShouldBeNull();
                break;
        }

        await ShouldHaveLeftTheDeliveryUntouchedAsync(h, fx, SessionStatus.Stopped, status);
    }

    /// <summary>
    /// B Running holds the body only in the runner. The reconciler's own catch-up of B persists it.
    /// </summary>
    private static async Task ConfirmFromRunnerTranscriptAsync(CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync();
        var fx = await ArrangeFollowAsync(h, SessionStatus.Running);
        h.Runner.Transcript = Mode.Entries;
        h.Runner.NextEntries = id =>
        {
            if (id != fx.Destination)
                throw new InvalidOperationException("a pull of a session other than B");
            return
            [
                h.Event(fx.Destination, 60, TranscriptKinds.UserPrompt, fx.Note.Body),
                h.Event(fx.Destination, 61, TranscriptKinds.TurnEnd, null),
            ];
        };

        var passes = await PassesAsync(h, fx.Note.Id, fx.Destination, 3, ct);

        var roster = Rosters(passes);
        passes.Select(p => p.Commands).ShouldBe([11, 2, 2], roster);
        passes[0].PullsB.ShouldBe(1);
        passes[0].PullsA.ShouldBe(0);
        passes.Select(p => p.ReceiptSelects).ShouldBe([1, 0, 0]);
        passes[0].Sessions.ShouldBe([fx.Destination]);
        (await PromptSequenceAsync(h, fx.Destination, fx.Note.Body)).ShouldBe(60);
        await ShouldBeConfirmedAtAsync(h, fx.Note.Id, 60, fx.Destination);
        h.Cache!.GetMetrics().Publishes.ShouldBe(0);
        (await h.StatusAsync(fx.Destination)).ShouldBe(SessionStatus.Running);
        await ShouldHaveLeftTheDeliveryUntouchedAsync(h, fx, SessionStatus.Stopped, SessionStatus.Running);
    }

    /// <summary>
    /// The second session projection (the D-3 select) throws once. The next pass scans B and confirms.
    /// </summary>
    private static async Task ConfirmAfterSessionSelectFaultAsync(CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync();
        var fx = await ArrangeFollowAsync(h, SessionStatus.Stopped, onB: At(PromptAt));
        var before = await h.NoteAsync(fx.Note.Id);
        h.SessionProjectionFault.Arm(2);

        var first = await PassAsync(h, fx.Note.Id, fx.Destination, ct);
        var afterFault = await h.NoteAsync(fx.Note.Id);
        first.Commands.ShouldBe(7, first.Roster);
        first.ReceiptSelects.ShouldBe(0);
        first.PullsA.ShouldBe(0);
        first.PullsB.ShouldBe(0);
        afterFault.LastErrorCode.ShouldStartWith("notification_reconcile_failed:InvalidOperationException");
        afterFault.EnqueueAttempts.ShouldBe(before.EnqueueAttempts + 1);
        afterFault.State.ShouldBe(LandNotificationState.AwaitingReceipt);
        afterFault.ConfirmedAt.ShouldBeNull();

        var second = await PassAsync(h, fx.Note.Id, fx.Destination, ct);
        second.Commands.ShouldBe(7, second.Roster);
        second.ReceiptSelects.ShouldBe(1);
        second.PullsB.ShouldBe(1);
        second.Sessions.ShouldBe([fx.Destination]);
        await ShouldBeConfirmedAtAsync(h, fx.Note.Id, PromptAt, fx.Destination);

        var third = await PassAsync(h, fx.Note.Id, fx.Destination, ct);
        third.Commands.ShouldBe(2, third.Roster);
        third.ReceiptSelects.ShouldBe(0);
        third.PullsB.ShouldBe(0);
        var faultPasses = new[] { first, second, third };
        faultPasses.Select(p => p.ReceiptSelects).ShouldBe([0, 1, 0], Rosters(faultPasses));
        faultPasses.Select(p => p.SessionProjections).ShouldBe([2, 2, 0], Rosters(faultPasses));
        await ShouldHaveLeftTheDeliveryUntouchedAsync(h, fx, SessionStatus.Stopped, SessionStatus.Stopped);
    }

    // V-2 method 2 -------------------------------------------------------------------------------

    [Test]
    [Timeout(180_000)]
    [Arguments("parent-holds-prompt")]
    [Arguments("third-session-holds-prompt")]
    [Arguments("head-only-on-destination")]
    [Arguments("tail-altered-on-destination")]
    [Arguments("below-floor-on-destination")]
    [Arguments("assistant-text-on-destination")]
    public async Task C1157_FollowScanNeverConfirmsFromAnotherSessionOrResemblance(string arm, CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync();
        Func<string, Func<long, (string, string)>?>? onA = arm == "parent-holds-prompt" ? At(PromptAt) : null;
        Func<string, Func<long, (string, string)>?>? onB = arm switch
        {
            "head-only-on-destination" => At(PromptAt, text: body => body[..300]),
            "tail-altered-on-destination" => At(PromptAt, text: body => body[..^1] + (body[^1] == 'x' ? "y" : "x")),
            "assistant-text-on-destination" => At(PromptAt, TranscriptKinds.AssistantText),
            "parent-holds-prompt" or "third-session-holds-prompt" or "below-floor-on-destination" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, null),
        };
        var fx = await ArrangeFollowAsync(h, SessionStatus.Stopped, onB: onB, onA: onA,
            bodyAtFloorOnB: arm == "below-floor-on-destination");
        Guid? third = null;
        if (arm == "third-session-holds-prompt")
        {
            third = await h.AddSessionAsync(SessionStatus.Stopped, fx.ParentStartedAt.AddHours(-2));
            (await h.IngestEventsAsync(third.Value, [h.Event(third.Value, PromptAt, TranscriptKinds.UserPrompt, fx.Note.Body)]))
                .Single().ShouldBe(PromptAt);
            h.ResetCounters();
        }
        if (arm == "head-only-on-destination")
        {
            fx.Note.Body.Length.ShouldBeGreaterThan(300);
            await using var db = h.Fixture();
            var text = await db.TranscriptEntries.AsNoTracking()
                .Where(t => t.AgentSessionId == fx.Destination && t.Sequence == PromptAt)
                .Select(t => t.Text).SingleAsync(ct);
            PromptSubmissionMatch.IsConfirmedBy(fx.Note.Body, text).ShouldBeTrue(arm);
            PromptSubmissionMatch.IsCompleteIn(fx.Note.Body, text).ShouldBeFalse(arm);
        }

        var thirdPulls = new List<int>();
        var passes = new List<Pass>();
        for (var i = 0; i < 3; i++)
        {
            passes.Add(await PassAsync(h, fx.Note.Id, fx.Destination, ct));
            if (third is Guid other) thirdPulls.Add(h.Runner.Pulls(other));
        }

        var roster = Rosters(passes);
        passes[0].Sessions.ShouldBe([fx.Destination], roster);
        passes[0].Sessions.Count.ShouldBe(1);
        passes.Select(p => p.ReceiptSelects).ShouldBe([1, 0, 0]);
        passes.Select(p => p.Commands).ShouldBe([7, 6, 6], roster);
        var rows = arm == "assistant-text-on-destination" ? new[] { 47, 0, 0 } : new[] { 48, 0, 0 };
        passes.Select(p => p.ReceiptRows).ShouldBe(rows, roster);
        await ShouldStayOpenAsync(h, fx.Note.Id);
        var metrics = h.Cache!.GetMetrics();
        metrics.Publishes.ShouldBe(1);
        metrics.Proofs.ShouldBe(1);
        metrics.Hits.ShouldBe(2);
        (await h.InMemoryFirstReceiptAsync(fx.Destination, Floor, false, LandNotificationKind.Outcome, fx.Note.Body))
            .ShouldBeNull("B above the floor is not a receipt");
        passes.Select(p => p.PullsA).ShouldBe([0, 0, 0]);
        passes.Select(p => p.PullsB).ShouldBe([1, 1, 1]);
        switch (arm)
        {
            case "parent-holds-prompt":
                (await h.InMemoryFirstReceiptAsync(fx.Parent, Floor, false, LandNotificationKind.Outcome, fx.Note.Body))
                    .ShouldBe(PromptAt, "the parent prompt is real and was not scanned");
                break;
            case "third-session-holds-prompt":
                thirdPulls.ShouldBe([0, 0, 0], "C is never the scanned session");
                break;
            case "below-floor-on-destination":
                (await h.InMemoryFirstReceiptAsync(fx.Destination, Floor - 1, false, LandNotificationKind.Outcome, fx.Note.Body))
                    .ShouldBe(Floor, "only the floor excludes the at-floor prompt");
                break;
        }

        await ShouldHaveLeftTheDeliveryUntouchedAsync(h, fx, SessionStatus.Stopped, SessionStatus.Stopped);
    }

    // V-2 method 3 -------------------------------------------------------------------------------

    [Test]
    [Timeout(180_000)]
    [Arguments("one-kind-dispatch-base")]
    [Arguments("is-legacy-outcome")]
    [Arguments("status-pending")]
    [Arguments("back-pointer-other")]
    public async Task C1157_RefusedFollowKeepsParentScan(string flip, CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync();
        var fx = await ApplyFlipAsync(h, await ArrangeFollowAsync(h, SessionStatus.Stopped, onB: At(PromptAt)), flip);

        var passes = await PassesAsync(h, fx.Note.Id, fx.Destination, 3, ct);

        var roster = Rosters(passes);
        passes[0].Sessions.ShouldBe([fx.Parent], roster);
        passes.Select(p => p.ReceiptSelects).ShouldBe([1, 0, 0]);
        passes.Select(p => p.ReceiptRows).ShouldBe([48, 0, 0], roster);
        passes.Select(p => p.Commands).ShouldBe([6, 5, 5], roster);
        passes.Select(p => p.SessionProjections).ShouldBe([1, 1, 1], roster);
        passes.Select(p => p.PullsA).ShouldBe([1, 1, 1]);
        passes.Select(p => p.PullsB).ShouldBe([0, 0, 0]);
        await ShouldStayOpenAsync(h, fx.Note.Id);
        h.Cache!.GetMetrics().Publishes.ShouldBe(1, "the parent proof");
        (await h.InMemoryFirstReceiptAsync(fx.Destination, Floor, false, LandNotificationKind.Outcome, fx.Note.Body))
            .ShouldBe(PromptAt, "a receipt existed where the row went and was not consulted");
        if (flip == "one-kind-dispatch-base")
            passes[0].Sql.Single().ShouldContain("t.\"Kind\" = 'UserPrompt'");
        (await h.NoteAsync(fx.Note.Id)).LastErrorCode.ShouldBeNull();
        await ShouldHaveLeftTheDeliveryUntouchedAsync(h, fx, SessionStatus.Stopped, SessionStatus.Stopped);
    }

    // V-2 method 4 -------------------------------------------------------------------------------

    [Test]
    [Timeout(180_000)]
    [Arguments("confirm")]
    [Arguments("quiet-miss")]
    [Arguments("row-session-missing")]
    [Arguments("running-quiet-miss")]
    [Arguments("parent-running-row-stopped-quiet-miss")]
    public async Task C1157_FollowScanStatementBudget(string arm, CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync();
        var status = arm == "running-quiet-miss" ? SessionStatus.Running : SessionStatus.Stopped;
        var fx = await ArrangeFollowAsync(h, status, onB: arm == "confirm" ? At(PromptAt) : null);
        var parentStatus = SessionStatus.Stopped;
        SessionStatus? destinationStatus = status;
        if (arm == "row-session-missing")
        {
            await h.DetachQueueRowsFromSessionsAsync();
            await h.DeleteSessionAsync(fx.Destination);
            destinationStatus = null;
            h.ResetCounters();
        }
        else if (arm == "parent-running-row-stopped-quiet-miss")
        {
            await h.SetStatusAsync(fx.Parent, SessionStatus.Running);
            parentStatus = SessionStatus.Running;
            h.ResetCounters();
        }

        var passes = await PassesAsync(h, fx.Note.Id, fx.Destination, 3, ct);

        var roster = Rosters(passes);
        switch (arm)
        {
            case "confirm":
                passes.Select(p => p.Commands).ShouldBe([7, 2, 2], roster);
                passes.Select(p => p.ReceiptSelects).ShouldBe([1, 0, 0]);
                passes.Select(p => p.SessionProjections).ShouldBe([2, 0, 0], roster);
                passes[0].Sessions.ShouldBe([fx.Destination]);
                h.Cache!.GetMetrics().Publishes.ShouldBe(0);
                await ShouldBeConfirmedAtAsync(h, fx.Note.Id, PromptAt, fx.Destination);
                break;
            case "quiet-miss":
                passes.Select(p => p.Commands).ShouldBe([7, 6, 6], roster);
                passes.Select(p => p.ReceiptSelects).ShouldBe([1, 0, 0]);
                passes.Select(p => p.ReceiptRows).ShouldBe([48, 0, 0], roster);
                passes.Select(p => p.SessionProjections).ShouldBe([2, 2, 2], roster);
                passes.Select(p => p.PullsB).ShouldBe([1, 1, 1]);
                passes[0].Sessions.ShouldBe([fx.Destination]);
                var quiet = h.Cache!.GetMetrics();
                quiet.Proofs.ShouldBe(1);
                quiet.Publishes.ShouldBe(1);
                quiet.Hits.ShouldBe(2);
                Refusal(h, "identity:ScanSession").ShouldBe(0);
                Refusal(h, "state:AcceptedGeneration").ShouldBe(0);
                Refusal(h, "context:ScanSessionId").ShouldBe(0);
                await ShouldStayOpenAsync(h, fx.Note.Id);
                (await h.NoteAsync(fx.Note.Id)).LastErrorCode.ShouldBeNull();
                break;
            case "row-session-missing":
                passes.Select(p => p.Commands).ShouldBe([7, 6, 6], roster);
                passes.Select(p => p.ReceiptSelects).ShouldBe([1, 0, 0]);
                passes.Select(p => p.ReceiptRows).ShouldBe([48, 0, 0], roster);
                passes.Select(p => p.SessionProjections).ShouldBe([2, 2, 2], roster);
                passes[0].Sessions.ShouldBe([fx.Parent]);
                passes.Select(p => p.PullsA).ShouldBe([1, 1, 1]);
                passes.Select(p => p.PullsB).ShouldBe([0, 0, 0]);
                h.Cache!.GetMetrics().Publishes.ShouldBe(1);
                await ShouldStayOpenAsync(h, fx.Note.Id);
                (await h.NoteAsync(fx.Note.Id)).LastErrorCode.ShouldBeNull();
                await using (var db = h.Fixture())
                    (await db.AgentSessions.CountAsync(s => s.Id == fx.Destination, ct)).ShouldBe(0);
                break;
            case "running-quiet-miss":
                passes.Select(p => p.Commands).ShouldBe([7, 7, 7], roster);
                passes.Select(p => p.ReceiptSelects).ShouldBe([1, 1, 1]);
                passes.Select(p => p.ReceiptRows).ShouldBe([48, 48, 48], roster);
                passes.Select(p => p.Sessions.Single()).ShouldBe([fx.Destination, fx.Destination, fx.Destination]);
                passes.Select(p => p.PullsB).ShouldBe([1, 1, 1]);
                var running = h.Cache!.GetMetrics();
                running.Proofs.ShouldBe(0);
                running.Publishes.ShouldBe(0);
                running.Hits.ShouldBe(0);
                Refusal(h, "eligibility:DestinationStatus").ShouldBe(3);
                (await h.StatusAsync(fx.Destination)).ShouldBe(SessionStatus.Running);
                (await h.NoteAsync(fx.Note.Id)).LastErrorCode.ShouldBeNull();
                await ShouldStayOpenAsync(h, fx.Note.Id);
                break;
            case "parent-running-row-stopped-quiet-miss":
                passes.Select(p => p.Commands).ShouldBe([7, 6, 6], roster);
                passes.Select(p => p.ReceiptSelects).ShouldBe([1, 0, 0]);
                passes.Select(p => p.SessionProjections).ShouldBe([2, 2, 2], roster);
                passes[0].Sessions.ShouldBe([fx.Destination]);
                h.Cache!.GetMetrics().Publishes.ShouldBe(1, "the scanned session is terminal");
                (await h.StatusAsync(fx.Parent)).ShouldBe(SessionStatus.Running);
                await ShouldStayOpenAsync(h, fx.Note.Id);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(arm), arm, null);
        }

        ShouldBeTwoKindScan(passes);
        if (arm != "row-session-missing")
        {
            passes.Select(p => p.PullsA).ShouldBe([0, 0, 0]);
            if (arm != "quiet-miss" && arm != "running-quiet-miss")
                passes.Where(p => p.ReceiptSelects == 1).Select(p => p.PullsB).ShouldAllBe(p => p == 1);
        }

        await ShouldHaveLeftTheDeliveryUntouchedAsync(h, fx, parentStatus, destinationStatus);
    }

    // V-2 method 5 -------------------------------------------------------------------------------

    [Test]
    [Timeout(180_000)]
    [Arguments("parent-proof-then-destination-appears")]
    [Arguments("destination-proof-then-destination-vanishes")]
    public async Task C1157_ProofForOneScanTargetNeverCertifiesTheOther(string direction, CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync();
        await h.DetachQueueRowsFromSessionsAsync();
        var appear = direction == "parent-proof-then-destination-appears";
        var fx = appear
            ? await ArrangeFollowAsync(h, SessionStatus.Stopped, createDestination: false)
            : direction == "destination-proof-then-destination-vanishes"
                ? await ArrangeFollowAsync(h, SessionStatus.Stopped, onA: At(PromptAt))
                : throw new ArgumentOutOfRangeException(nameof(direction), direction, null);

        var opening = await PassesAsync(h, fx.Note.Id, fx.Destination, 2, ct);
        var roster = Rosters(opening);
        opening.Select(p => p.Commands).ShouldBe([7, 6], roster);
        opening.Select(p => p.ReceiptSelects).ShouldBe([1, 0]);
        opening.Select(p => p.PullsA).ShouldBe(appear ? [1, 1] : [0, 0]);
        opening.Select(p => p.PullsB).ShouldBe(appear ? [0, 0] : [1, 1]);
        opening[0].Sessions.ShouldBe([appear ? fx.Parent : fx.Destination], roster);
        h.Cache!.GetMetrics().Publishes.ShouldBe(1);
        await ShouldStayOpenAsync(h, fx.Note.Id);

        if (appear)
        {
            await h.AddSessionAsync(SessionStatus.Stopped, fx.ParentStartedAt.AddHours(-1), fx.Destination);
            (await h.IngestEventsAsync(fx.Destination, [h.Event(fx.Destination, PromptAt, TranscriptKinds.UserPrompt, fx.Note.Body)]))
                .Single().ShouldBe(PromptAt);
            await h.AssertCoherentAsync(fx.Destination);
        }
        else
        {
            await h.DeleteSessionAsync(fx.Destination);
        }

        var third = await PassAsync(h, fx.Note.Id, fx.Destination, ct);
        third.ReceiptSelects.ShouldBe(1, third.Roster);
        third.PullsA.ShouldBe(appear ? 0 : 1);
        third.PullsB.ShouldBe(appear ? 1 : 0);
        third.Sessions.ShouldBe([appear ? fx.Destination : fx.Parent], third.Roster);
        await ShouldBeConfirmedAtAsync(h, fx.Note.Id, PromptAt, appear ? fx.Destination : fx.Parent);
        h.Cache!.GetMetrics().Hits.ShouldBe(1);
        Refusal(h, "context:ScanSessionId").ShouldBe(1);
        await ShouldHaveLeftTheDeliveryUntouchedAsync(h, fx, SessionStatus.Stopped, appear ? SessionStatus.Stopped : null);
    }

    private static async Task<FollowFixture> ArrangeFollowAsync(LandReceiptScanHarness h, SessionStatus destinationStatus,
        Func<string, Func<long, (string, string)>?>? onB = null,
        Func<string, Func<long, (string, string)>?>? onA = null,
        bool bodyAtFloorOnB = false, bool createDestination = true, bool stopParent = true)
    {
        var parent = h.SessionId;
        var parentStartedAt = await h.StartedAtAsync(parent);
        var note = await h.SeedLinkedNoteAsync(beforeEnqueue: async seeded =>
            await h.SeedTranscriptAsync(parent, candidate: onA?.Invoke(seeded.Body)));
        Guid destination;
        SessionQueuedMessage row;
        if (createDestination)
        {
            destination = await h.AddSessionAsync(destinationStatus, parentStartedAt.AddHours(-1));
            await h.SeedTranscriptAsync(destination, atFloorText: bodyAtFloorOnB ? note.Body : "below the floor",
                candidate: onB?.Invoke(note.Body));
            row = await h.MarkSentAsync(note, destination: destination);
            await h.AssertCoherentAsync(destination);
        }
        else
        {
            destination = Guid.NewGuid();
            row = await h.MarkSentAsync(note, change: queued => queued.AgentSessionId = destination);
        }

        if (stopParent) await h.SetStatusAsync(parent, SessionStatus.Stopped);
        await h.AssertCoherentAsync(parent);
        h.Runner.Transcript = Mode.NotFound;
        h.ResetCounters();
        return new FollowFixture(await h.NoteAsync(note.Id), row, parent, destination, parentStartedAt);
    }

    private static async Task<FollowFixture> ApplyFlipAsync(LandReceiptScanHarness h, FollowFixture fx, string flip)
    {
        switch (flip)
        {
            case "one-kind-dispatch-base":
                await h.UpdateNoteAsync(fx.Note.Id, n => n.Kind = LandNotificationKind.DispatchBase);
                break;
            case "is-legacy-outcome":
                await h.UpdateNoteAsync(fx.Note.Id, n => n.IsLegacy = true);
                break;
            case "status-pending":
                await h.UpdateRowAsync(fx.Row.Id, r => r.Status = QueuedMessageStatus.Pending);
                break;
            case "back-pointer-other":
                await h.UpdateRowAsync(fx.Row.Id, r => r.SourceLandNotificationId = Guid.NewGuid());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(flip), flip, null);
        }

        h.ResetCounters();
        return fx with { Note = await h.NoteAsync(fx.Note.Id), Row = await h.RowAsync(fx.Row.Id) };
    }

    private static Func<string, Func<long, (string, string)>?> At(long sequence, string kind = TranscriptKinds.UserPrompt,
        Func<string, string>? text = null) =>
        body =>
        {
            var prompt = text?.Invoke(body) ?? body;
            return seq => seq == sequence
                ? (kind, prompt)
                : (TranscriptKinds.UserPrompt, LandReceiptScanHarness.Filler(seq));
        };

    /// <summary>§3 no-cache control: a service with no scan cache confirms the same tuple.</summary>
    private static async Task ConfirmWithoutCacheAsync(LandReceiptScanHarness h, FollowFixture fx, CancellationToken ct)
    {
        await h.SetStatusAsync(fx.Parent, SessionStatus.Running);
        var control = await h.SeedLinkedNoteAsync();
        await h.SetStatusAsync(fx.Parent, SessionStatus.Stopped);
        var controlDestination = await h.AddSessionAsync(SessionStatus.Stopped, fx.ParentStartedAt.AddHours(-1));
        (await h.IngestEventsAsync(controlDestination,
            [h.Event(controlDestination, PromptAt, TranscriptKinds.UserPrompt, control.Body)])).Single().ShouldBe(PromptAt);
        var controlRow = await h.MarkSentAsync(control, destination: controlDestination);
        await using var db = h.Fixture();
        var today = new AgentTaskLandNotificationService(db, h.Bridge.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        for (var i = 0; i < 3; i++) await today.ReconcileAsync(control.Id, ct);
        var a = await h.NoteAsync(fx.Note.Id);
        var b = await h.NoteAsync(control.Id);
        (a.State, a.ConfirmingPromptSequence, a.LastErrorCode, a.EnqueueAttempts, a.ParentSessionId, a.QueueMessageId is null)
            .ShouldBe((b.State, b.ConfirmingPromptSequence, b.LastErrorCode, b.EnqueueAttempts, b.ParentSessionId, b.QueueMessageId is null));
        b.ConfirmedAt.ShouldNotBeNull();
        (await h.RowAsync(controlRow.Id)).AgentSessionId.ShouldBe(controlDestination);
    }

    private static async Task ShouldBeConfirmedAtAsync(LandReceiptScanHarness h, Guid noteId, long expected, Guid session)
    {
        var saved = await h.NoteAsync(noteId);
        var oracle = await h.InMemoryFirstReceiptAsync(session, Floor, saved.IsLegacy, saved.Kind, saved.Body);
        oracle.ShouldBe(expected, "the in-memory CARD-0641 rule over the committed rows");
        saved.State.ShouldBe(LandNotificationState.Confirmed);
        saved.ConfirmedAt.ShouldNotBeNull();
        saved.ConfirmingPromptSequence.ShouldBe(expected);
        saved.LastErrorCode.ShouldBeNull();
    }

    private static async Task ShouldStayOpenAsync(LandReceiptScanHarness h, Guid noteId)
    {
        var saved = await h.NoteAsync(noteId);
        saved.State.ShouldBe(LandNotificationState.AwaitingReceipt);
        saved.ConfirmedAt.ShouldBeNull();
        saved.ConfirmingPromptSequence.ShouldBeNull();
    }

    private static async Task ShouldHaveLeftTheDeliveryUntouchedAsync(LandReceiptScanHarness h, FollowFixture fx,
        SessionStatus parentStatus, SessionStatus? destinationStatus)
    {
        h.Bridge.Adapter.Inputs.ShouldBeEmpty("receipt reconciliation must never type");
        h.Runner.KillCalls.ShouldBe(0);
        var after = await h.RowAsync(fx.Row.Id);
        after.Status.ShouldBe(fx.Row.Status);
        after.SentAt.ShouldBe(fx.Row.SentAt);
        after.DeliveryAttempts.ShouldBe(fx.Row.DeliveryAttempts);
        after.AgentSessionId.ShouldBe(fx.Row.AgentSessionId);
        (await h.StatusAsync(fx.Parent)).ShouldBe(parentStatus);
        if (destinationStatus is SessionStatus status)
            (await h.StatusAsync(fx.Destination)).ShouldBe(status);
        var note = await h.NoteAsync(fx.Note.Id);
        note.State.ShouldNotBe(LandNotificationState.DestinationUnavailable);
        note.State.ShouldNotBe(LandNotificationState.Canceled);
        if (note.State != LandNotificationState.AwaitingReceipt || note.LastErrorCode is null)
            note.LastErrorCode?.ShouldNotStartWith("notification_reconcile_failed");
    }

    private static void ShouldBeTwoKindScan(IReadOnlyList<Pass> passes)
    {
        passes.Select(p => p.TranscriptStatements).ShouldBe(passes.Select(p => p.ReceiptSelects).ToArray(),
            "no per-note existence, count or max probe\n" + Rosters(passes));
        passes.SelectMany(p => p.Floors).ShouldAllBe(floor => floor == Floor, "the floor is the keyed row's baseline on every scan");
        foreach (var sql in passes.SelectMany(p => p.Sql))
        {
            ReceiptScanRecognizer.IsReceiptScan(sql).ShouldBeTrue(sql);
            sql.ShouldContain("t.\"Kind\" IN ('UserPrompt', 'QueuedUserPrompt')");
        }
    }

    private static async Task<long> PromptSequenceAsync(LandReceiptScanHarness h, Guid session, string body)
    {
        await using var db = h.Fixture();
        var prompts = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == session && t.Kind == TranscriptKinds.UserPrompt && t.Text == body)
            .Select(t => t.Sequence).ToListAsync();
        return prompts.ShouldHaveSingleItem("the complete prompt is committed exactly once");
    }

    private static long Refusal(LandReceiptScanHarness h, string reason) =>
        h.Cache!.GetMetrics().Refusals.TryGetValue(reason, out var count) ? count : 0;
}
