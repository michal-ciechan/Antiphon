using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1085. Land outcome receipts collapse to the newest row per task. A historical receipt is
/// omitted when the task is Succeeded or Canceled and the newest receipt event's At is at least 24h
/// before the read. Unconfirmed outcome rows do not: CARD-0467 keeps an unconfirmed receipt until it
/// is confirmed. An unresolved LandRefused never ages out. A task with both an eligible receipt and
/// an unresolved refusal contributes one legacy row, represented by the refusal, with the other
/// events folded into evidence (three newest, then "N more").
/// </summary>
[Category("Integration")]
public sealed class AttentionFeedNoiseTests
{
    [Test]
    public async Task C1085_Land_outcome_receipts_collapse_to_the_newest_per_task()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Working, dispatchedMinutesAgo: 240);
        var oldest = await scenario.AddUnconfirmedLandNoteAsync(
            task, session, DateTime.UtcNow.AddHours(-3), body: "oldest outcome");
        var errored = await scenario.AddUnconfirmedLandNoteAsync(
            task, session, DateTime.UtcNow.AddHours(-2), lastErrorCode: "parked", body: "errored outcome");
        var newest = await scenario.AddUnconfirmedLandNoteAsync(
            task, session, DateTime.UtcNow.AddMinutes(-6), body: "newest outcome");
        var dispatch = await scenario.AddUnconfirmedLandNoteAsync(
            task, session, DateTime.UtcNow.AddHours(-2), LandNotificationKind.DispatchBase, body: "dispatch warning");

        var items = await ReadOwnedAsync(scenario);
        var outcomes = items.Where(i => i.Kind == AttentionKind.LandOutcomeUnconfirmed && i.TaskId == task).ToList();

        outcomes.ShouldHaveSingleItem("three unconfirmed receipts are one row");
        outcomes[0].LandNotificationId.ShouldBe(newest);
        outcomes[0].Evidence.ShouldStartWith("receipts=3\n");
        outcomes[0].Severity.ShouldBe(AlertSeverity.Error, "an older error is not hidden by a newer warning");
        outcomes[0].Evidence.ShouldContain("newest outcome");
        items.ShouldContain(i => i.LandNotificationId == dispatch && i.Kind == AttentionKind.DispatchWarningUnconfirmed);
        items.Single(i => i.LandNotificationId == dispatch).Evidence.ShouldNotContain("receipts=");
        items.ShouldNotContain(i => i.LandNotificationId == oldest || i.LandNotificationId == errored);
    }

    [Test]
    public async Task C1085_Legacy_rows_age_out_after_the_task_is_done_and_outcomes_do_not()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var session = await scenario.AddSessionAsync();
        var canceled = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Canceled, dispatchedMinutesAgo: 60 * 24 * 4, completedMinutesAgo: 60 * 24 * 3);
        await scenario.AddLegacyLandEventAsync(canceled, DateTime.UtcNow.AddDays(-3));
        await scenario.AddLegacyLandEventAsync(canceled, DateTime.UtcNow.AddDays(-4));

        var fresh = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 180, completedMinutesAgo: 60);
        var newer = await scenario.AddLegacyLandEventAsync(fresh, DateTime.UtcNow.AddHours(-1));
        await scenario.AddLegacyLandEventAsync(fresh, DateTime.UtcNow.AddHours(-2));

        var open = await scenario.AddTaskAsync(session, AgentTaskStatus.Working, dispatchedMinutesAgo: 60 * 24 * 4);
        await scenario.AddLegacyLandEventAsync(open, DateTime.UtcNow.AddDays(-3));

        var settled = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 60 * 24 * 4, completedMinutesAgo: 60 * 24 * 3);
        await scenario.AddUnconfirmedLandNoteAsync(settled, session, DateTime.UtcNow.AddDays(-3), body: "old-a");
        await scenario.AddUnconfirmedLandNoteAsync(settled, session, DateTime.UtcNow.AddDays(-2), body: "old-b");

        var items = await ReadOwnedAsync(scenario);

        items.ShouldNotContain(i => i.TaskId == canceled && i.Kind == AttentionKind.LandLegacyUnverified);
        var legacy = items.Where(i => i.TaskId == fresh && i.Kind == AttentionKind.LandLegacyUnverified).ShouldHaveSingleItem();
        legacy.Evidence.ShouldContain("receipts=2");
        legacy.ConditionKey.ShouldBe($"legacy-land:{newer:N}");
        items.ShouldContain(i => i.TaskId == open && i.Kind == AttentionKind.LandLegacyUnverified);
        var outcome = items.Where(i => i.TaskId == settled && i.Kind == AttentionKind.LandOutcomeUnconfirmed).ShouldHaveSingleItem();
        outcome.Evidence.ShouldStartWith("receipts=2\n");
    }

    // CARD-1196: this test still clears the refusal by inserting ConfirmedAt. The producer,
    // real queue, and transcript receipt are C1196_Eligible_recipient_receipt_resolves_older_refusal
    // (V-1), C1196_Busy_recipient_waits_for_turn_end_before_resolution (V-2), and
    // C1196_Receipt_save_crash_recovers_without_retyping (V-6). Those witnesses use a controlled
    // provider substitute, not a native PTY or transcript-file ingestion.
    [Test]
    public async Task C1085_Unresolved_land_refusal_survives_the_legacy_age_out()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var session = await scenario.AddSessionAsync();
        var refused = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 60 * 24 * 4, completedMinutesAgo: 60 * 24 * 3);
        var refusal = await scenario.AddLegacyLandEventAsync(
            refused, DateTime.UtcNow.AddDays(-3), AgentTaskEventType.LandRefused);

        var canceled = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Canceled, dispatchedMinutesAgo: 60 * 24 * 4, completedMinutesAgo: 60 * 24 * 3);
        var canceledRefusal = await scenario.AddLegacyLandEventAsync(
            canceled, DateTime.UtcNow.AddDays(-3), AgentTaskEventType.LandRefused);

        var cleared = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 60 * 24 * 4, completedMinutesAgo: 60 * 24 * 3);
        await scenario.AddLegacyLandEventAsync(
            cleared, DateTime.UtcNow.AddDays(-3), AgentTaskEventType.LandRefused);
        var landed = await scenario.AddLegacyLandEventAsync(cleared, DateTime.UtcNow.AddHours(-2));
        await scenario.ConfirmLegacyReceiptAsync(cleared, landed, DateTime.UtcNow.AddHours(-2));

        var items = await ReadOwnedAsync(scenario);

        var row = items.Where(i => i.TaskId == refused && i.Kind == AttentionKind.LandLegacyUnverified)
            .ShouldHaveSingleItem();
        row.ConditionKey.ShouldBe($"legacy-land:{refusal:N}");
        items.ShouldContain(i => i.TaskId == canceled && i.ConditionKey == $"legacy-land:{canceledRefusal:N}");
        items.ShouldNotContain(i => i.TaskId == cleared && i.Kind == AttentionKind.LandLegacyUnverified);
    }

    [Test]
    public async Task C1085_Recent_incidents_stay_one_row_and_name_the_hidden_tail()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var agent = await scenario.AddAgentAsync();
        await scenario.AddIncidentAsync(
            agent, null, AgentIncidentKind.TranscriptBindFailed, AlertSeverity.Error, "oldest bind", minutesAgo: 40);
        await scenario.AddIncidentAsync(
            agent, null, AgentIncidentKind.TranscriptBindFailed, AlertSeverity.Error, "bind two", minutesAgo: 30);
        await scenario.AddIncidentAsync(
            agent, null, AgentIncidentKind.TranscriptBindFailed, AlertSeverity.Error, "bind three", minutesAgo: 20);
        await scenario.AddIncidentAsync(
            agent, null, AgentIncidentKind.TranscriptBindFailed, AlertSeverity.Critical, "newest bind", minutesAgo: 10);

        var mine = (await ReadOwnedAsync(scenario))
            .Where(i => i.Kind == AttentionKind.RecentCriticalIncident && i.AgentId == agent)
            .ToList();

        var row = mine.ShouldHaveSingleItem();
        row.Headline.ShouldContain("4 x");
        row.Severity.ShouldBe(AlertSeverity.Critical);
        row.Evidence.ShouldContain("newest bind");
        row.Evidence.ShouldContain("bind three");
        row.Evidence.ShouldContain("bind two");
        row.Evidence.ShouldEndWith("1 more");
        row.Evidence.ShouldNotContain("oldest bind");
    }

    [Test]
    public async Task C1085_Min_severity_hides_rows_and_keeps_the_kind_census()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Blocked, dispatchedMinutesAgo: 20, failureReason: "Which branch?");
        var agent = await scenario.AddAgentAsync();
        await scenario.AddIncidentAsync(
            agent, null, AgentIncidentKind.Crash, AlertSeverity.Error, "boom", minutesAgo: 5);

        var service = AttentionServiceTests.BuildService(new AttentionServiceTests.FakeRunnerClient());
        var all = await service.GetAsync(CancellationToken.None, includeProgressProbe: false);
        var critical = await service.GetAsync(
            CancellationToken.None, includeProgressProbe: false, minSeverity: AlertSeverity.Critical);

        all.Items.ShouldContain(i => i.TaskId == task && i.Kind == AttentionKind.BlockedQuestion);
        all.Items.ShouldContain(i => i.AgentId == agent && i.Kind == AttentionKind.RecentCriticalIncident);
        critical.Items.ShouldContain(i => i.TaskId == task && i.Kind == AttentionKind.BlockedQuestion);
        critical.Items.ShouldNotContain(i => i.AgentId == agent && i.Kind == AttentionKind.RecentCriticalIncident);
        critical.KindCounts.ShouldNotBeNull();
        critical.KindCounts.ShouldContain(c => c.Kind == AttentionKind.BlockedQuestion && c.Count >= 1);
        critical.KindCounts.ShouldContain(c => c.Kind == AttentionKind.RecentCriticalIncident && c.Count >= 1);
        critical.Items.ShouldAllBe(i => i.Severity >= AlertSeverity.Critical);
    }

    [Test]
    public async Task C1085_Mixed_history_prefers_the_unresolved_refusal_and_caps_the_folded_receipts()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Working, dispatchedMinutesAgo: 240);
        await scenario.AddLegacyLandEventAsync(task, DateTime.UtcNow.AddHours(-5), detail: "receipt-oldest");
        await scenario.AddLegacyLandEventAsync(task, DateTime.UtcNow.AddHours(-4), detail: "receipt-two");
        await scenario.AddLegacyLandEventAsync(task, DateTime.UtcNow.AddHours(-3), detail: "receipt-three");
        await scenario.AddLegacyLandEventAsync(task, DateTime.UtcNow.AddHours(-2), detail: "receipt-newest");
        var refusal = await scenario.AddLegacyLandEventAsync(
            task, DateTime.UtcNow.AddMinutes(-30), AgentTaskEventType.LandRefused, detail: "refusal-row");

        var rows = LegacyRows(await ReadOwnedAsync(scenario), task);

        var row = rows.ShouldHaveSingleItem();
        row.ConditionKey.ShouldBe($"legacy-land:{refusal:N}");
        row.Evidence.ShouldBe(
            "Historical routing/receipt is unknown; no new message is inferred or replayed. receipts=5.\n"
            + "receipt-newest\nreceipt-three\nreceipt-two\n1 more");
        row.Evidence.ShouldNotContain("receipt-oldest");
        row.Evidence.ShouldNotContain("refusal-row");
    }

    [Test]
    public async Task C1085_Aged_out_receipt_plus_unresolved_refusal_is_only_the_refusal()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 60 * 24 * 4, completedMinutesAgo: 60 * 24 * 3);
        await scenario.AddLegacyLandEventAsync(task, DateTime.UtcNow.AddDays(-3), detail: "aged-receipt-marker");
        var refusal = await scenario.AddLegacyLandEventAsync(
            task, DateTime.UtcNow.AddDays(-2), AgentTaskEventType.LandRefused);

        var row = LegacyRows(await ReadOwnedAsync(scenario), task).ShouldHaveSingleItem();

        row.ConditionKey.ShouldBe($"legacy-land:{refusal:N}");
        row.Evidence.ShouldBe(
            "Historical routing/receipt is unknown; no new message is inferred or replayed. receipts=1.");
        row.Evidence.ShouldNotContain("aged-receipt-marker");
    }

    [Test]
    public async Task C1085_Unconfirmed_receipt_stays_when_a_later_publication_resolves_the_refusal()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 180, completedMinutesAgo: 30);
        var refusal = await scenario.AddLegacyLandEventAsync(
            task, DateTime.UtcNow.AddHours(-3), AgentTaskEventType.LandRefused);
        var receipt = await scenario.AddLegacyLandEventAsync(
            task, DateTime.UtcNow.AddMinutes(-30), detail: "live-receipt",
            publication: LandPublicationOutcome.Landed);

        var row = LegacyRows(await ReadOwnedAsync(scenario), task).ShouldHaveSingleItem();

        row.ConditionKey.ShouldBe($"legacy-land:{receipt:N}");
        row.ConditionKey.ShouldNotBe($"legacy-land:{refusal:N}");
        row.Evidence.ShouldBe(
            "Historical routing/receipt is unknown; no new message is inferred or replayed. receipts=1.");
    }

    [Test]
    public async Task C1085_Aged_receipt_and_resolved_refusal_emit_no_legacy_row()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 60 * 24 * 5, completedMinutesAgo: 60 * 24 * 4);
        await scenario.AddLegacyLandEventAsync(
            task, DateTime.UtcNow.AddDays(-4), AgentTaskEventType.LandRefused);
        await scenario.AddLegacyLandEventAsync(
            task, DateTime.UtcNow.AddDays(-3), publication: LandPublicationOutcome.Landed);

        LegacyRows(await ReadOwnedAsync(scenario), task).ShouldBeEmpty();
    }

    [Test]
    public async Task C1085_Historical_land_event_whose_task_row_is_gone_is_omitted()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var session = await scenario.AddSessionAsync();
        var goneReceipt = await scenario.AddTaskAsync(session, AgentTaskStatus.Working, dispatchedMinutesAgo: 240);
        var goneRefusal = await scenario.AddTaskAsync(session, AgentTaskStatus.Working, dispatchedMinutesAgo: 240);
        var kept = await scenario.AddTaskAsync(session, AgentTaskStatus.Working, dispatchedMinutesAgo: 240);
        await scenario.AddLegacyLandEventAsync(goneReceipt, DateTime.UtcNow.AddHours(-1), detail: "orphan-receipt");
        await scenario.AddLegacyLandEventAsync(
            goneRefusal, DateTime.UtcNow.AddHours(-1), AgentTaskEventType.LandRefused, detail: "orphan-refusal");
        var keptRefusal = await scenario.AddLegacyLandEventAsync(
            kept, DateTime.UtcNow.AddHours(-1), AgentTaskEventType.LandRefused, detail: "kept-refusal");
        await scenario.DeleteTaskRowLeavingEventsAsync(goneReceipt);
        await scenario.DeleteTaskRowLeavingEventsAsync(goneRefusal);

        var items = await ReadOwnedAsync(scenario);

        items.ShouldNotContain(i => i.TaskId == goneReceipt || i.TaskId == goneRefusal);
        var row = LegacyRows(items, kept).ShouldHaveSingleItem();
        row.ConditionKey.ShouldBe($"legacy-land:{keptRefusal:N}");
    }

    [Test]
    public async Task C1085_Two_tasks_with_mixed_histories_emit_two_legacy_rows()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var session = await scenario.AddSessionAsync();
        var first = await scenario.AddTaskAsync(session, AgentTaskStatus.Working, dispatchedMinutesAgo: 240);
        var second = await scenario.AddTaskAsync(session, AgentTaskStatus.Working, dispatchedMinutesAgo: 240);
        await scenario.AddLegacyLandEventAsync(first, DateTime.UtcNow.AddHours(-2), detail: "first-receipt");
        var firstRefusal = await scenario.AddLegacyLandEventAsync(
            first, DateTime.UtcNow.AddHours(-1), AgentTaskEventType.LandRefused);
        await scenario.AddLegacyLandEventAsync(second, DateTime.UtcNow.AddHours(-2), detail: "second-receipt");
        var secondRefusal = await scenario.AddLegacyLandEventAsync(
            second, DateTime.UtcNow.AddHours(-1), AgentTaskEventType.LandRefused);

        var items = await ReadOwnedAsync(scenario);
        var rows = items.Where(i => i.Kind == AttentionKind.LandLegacyUnverified).ToList();

        rows.Count.ShouldBe(2);
        var firstRow = rows.Where(i => i.TaskId == first).ShouldHaveSingleItem();
        var secondRow = rows.Where(i => i.TaskId == second).ShouldHaveSingleItem();
        firstRow.ConditionKey.ShouldBe($"legacy-land:{firstRefusal:N}");
        secondRow.ConditionKey.ShouldBe($"legacy-land:{secondRefusal:N}");
        firstRow.Evidence.ShouldBe(
            "Historical routing/receipt is unknown; no new message is inferred or replayed. receipts=2.\n"
            + "first-receipt");
        secondRow.Evidence.ShouldBe(
            "Historical routing/receipt is unknown; no new message is inferred or replayed. receipts=2.\n"
            + "second-receipt");
    }

    private static List<AttentionItemDto> LegacyRows(List<AttentionItemDto> items, Guid taskId) =>
        items.Where(i => i.TaskId == taskId && i.Kind == AttentionKind.LandLegacyUnverified).ToList();

    private static async Task<List<AttentionItemDto>> ReadOwnedAsync(AttentionServiceTests.Scenario scenario)
    {
        var result = await AttentionServiceTests.BuildService(new AttentionServiceTests.FakeRunnerClient())
            .GetAsync(CancellationToken.None, includeProgressProbe: false);
        return result.Items.Where(scenario.Owns).ToList();
    }
}
