using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1085. Land outcome receipts collapse to the newest row per task. Historical legacy rows
/// age out once the task is Succeeded or Canceled and older than the feed's 24h window. Unconfirmed
/// outcome rows do not: CARD-0467 keeps an unconfirmed receipt until it is confirmed.
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

    private static async Task<List<AttentionItemDto>> ReadOwnedAsync(AttentionServiceTests.Scenario scenario)
    {
        var result = await AttentionServiceTests.BuildService(new AttentionServiceTests.FakeRunnerClient())
            .GetAsync(CancellationToken.None, includeProgressProbe: false);
        return result.Items.Where(scenario.Owns).ToList();
    }
}
