using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0547 D-5: the CommitRecoveryPending attention arm.
public partial class AttentionServiceTests
{
    private static string C547Digest() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static async Task<(Guid Session, Guid Task)> AddRunningDispatchedTaskAsync(Scenario scenario)
    {
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 10);
        await scenario.AddTranscriptAsync(session, (TranscriptKinds.UserPrompt, "the brief", null));
        return (session, task);
    }

    [Test]
    public async Task a_task_holding_a_commit_recovery_obligation_older_than_two_minutes_is_an_error_row_with_the_recipe()
    {
        await using var scenario = new Scenario();
        var (session, task) = await AddRunningDispatchedTaskAsync(scenario);
        var digest = C547Digest();
        var (id, at) = await scenario.AddTaskEventAsync(task, AgentTaskEventType.CommitRecoveryStarted, digest, 3);

        var item = (await ItemsForAsync(scenario, Running(session))).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.CommitRecoveryPending);
        ((int)item.Kind).ShouldBe(41);
        item.Severity.ShouldBe(AlertSeverity.Error);
        item.Headline.ShouldContain("Held for recovery");
        item.SinceUtc.ShouldNotBeNull();
        (item.SinceUtc!.Value - at).Duration().Ticks.ShouldBeLessThanOrEqualTo(10);
        item.Actions.ShouldBe([AttentionAction.OpenDrawer, AttentionAction.Cancel]);
        item.Evidence.ShouldNotBeNull();
        item.Evidence.ShouldContain($"obligation {id:D}");
        item.Evidence.ShouldContain($"settlement {digest}");
        item.Evidence.ShouldContain($"git log --all --reflog --fixed-strings --all-match --grep={task:D} --grep={digest} --format=%H");
        item.Evidence.ShouldContain("hold expires");
        item.Evidence.ShouldContain("\"abandonCommitRecovery\":true");
        item.Evidence.Length.ShouldBeGreaterThan(400, "fixture sanity: the evidence would have been excerpted");
        AttentionSummaryDto.From(new AttentionDto(DateTime.UtcNow, true, [item])).Open.ShouldBe(1);
    }

    [Test]
    public async Task a_commit_recovery_obligation_becomes_visible_only_after_two_full_minutes()
    {
        // Postgres stores microseconds; a sub-microsecond `now` would make the stored 120 s row older.
        var now = DateTime.UtcNow;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        await using var scenario = new Scenario();
        var (_, atGate) = await AddRunningDispatchedTaskAsync(scenario);
        await scenario.AddTaskEventAsync(atGate, AgentTaskEventType.CommitRecoveryStarted, C547Digest(), 0, at: now.AddSeconds(-120));
        var (_, pastGate) = await AddRunningDispatchedTaskAsync(scenario);
        await scenario.AddTaskEventAsync(pastGate, AgentTaskEventType.CommitRecoveryStarted, C547Digest(), 0, at: now.AddSeconds(-121));

        var items = await ItemsForAsync(scenario, new FakeTimeProvider(now));

        items.Any(i => i.TaskId == atGate && i.Kind == AttentionKind.CommitRecoveryPending).ShouldBeFalse("120 s");
        items.Any(i => i.TaskId == pastGate && i.Kind == AttentionKind.CommitRecoveryPending).ShouldBeTrue("121 s");
    }

    [Test]
    [Arguments("not-needed")]
    [Arguments("abandoned")]
    [Arguments("committed-after")]
    [Arguments("committed-before")]
    public async Task a_resolved_commit_recovery_obligation_is_not_listed(string resolution)
    {
        await using var scenario = new Scenario();
        var (_, task) = await AddRunningDispatchedTaskAsync(scenario);
        var (id, _) = await scenario.AddTaskEventAsync(task, AgentTaskEventType.CommitRecoveryStarted, C547Digest(), 5);
        _ = resolution switch
        {
            "not-needed" => await scenario.AddTaskEventAsync(task, AgentTaskEventType.CommitRecoveryNotNeeded, $"{id:D} NothingToCommit", 4),
            "abandoned" => await scenario.AddTaskEventAsync(task, AgentTaskEventType.CommitRecoveryAbandoned, $"{id:D} overdue-task deadline: x", 4),
            "committed-after" => await scenario.AddTaskEventAsync(task, AgentTaskEventType.Committed, "abc1234 a.md", 4),
            _ => await scenario.AddTaskEventAsync(task, AgentTaskEventType.Committed, "abc1234 a.md", 6),
        };

        var items = await ItemsForAsync(scenario);

        items.Any(i => i.TaskId == task && i.Kind == AttentionKind.CommitRecoveryPending)
            .ShouldBe(resolution == "committed-before", resolution);
    }

    [Test]
    public async Task a_failed_task_with_an_open_commit_recovery_obligation_is_not_listed()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Failed, dispatchedMinutesAgo: 10,
            completedMinutesAgo: 1, failureReason: "failed while holding");
        await scenario.AddTaskEventAsync(task, AgentTaskEventType.CommitRecoveryStarted, C547Digest(), 5);

        (await ItemsForAsync(scenario)).ShouldNotContain(i => i.TaskId == task && i.Kind == AttentionKind.CommitRecoveryPending);
    }

    [Test]
    public async Task a_dead_session_holding_a_commit_recovery_obligation_is_listed_as_the_hold_not_the_death()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync(SessionStatus.Failed, endedMinutesAgo: 5);
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 10);
        await scenario.AddTaskEventAsync(task, AgentTaskEventType.CommitRecoveryStarted, C547Digest(), 3);

        var items = (await ItemsForAsync(scenario)).Where(i => i.TaskId == task).ToList();

        items.ShouldHaveSingleItem().Kind.ShouldBe(AttentionKind.CommitRecoveryPending);
        items.ShouldNotContain(i => i.Kind == AttentionKind.DeadSession);
    }

    [Test]
    public async Task commit_recovery_rows_cost_one_events_query_for_the_whole_open_set()
    {
        await using var scenario = new Scenario();
        var (first, firstTask) = await AddRunningDispatchedTaskAsync(scenario);
        await scenario.AddTaskEventAsync(firstTask, AgentTaskEventType.CommitRecoveryStarted, C547Digest(), 3);

        var c1 = await CountEventsQueriesAsync(Running(first));

        var sessions = new List<Guid> { first };
        for (var i = 0; i < 3; i++)
        {
            var (session, task) = await AddRunningDispatchedTaskAsync(scenario);
            await scenario.AddTaskEventAsync(task, AgentTaskEventType.CommitRecoveryStarted, C547Digest(), 3);
            sessions.Add(session);
        }
        var c4 = await CountEventsQueriesAsync([.. sessions.Select(s => Running(s))]);

        c1.ShouldBeGreaterThan(0);
        c4.ShouldBeGreaterThan(0);
        c4.ShouldBe(c1, "one obligations query covers the whole open set");
    }

    private static async Task<int> CountEventsQueriesAsync(params SessionRunnerSessionDto[] running)
    {
        var counter = new CountingCommandInterceptor();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions())
            .AddInterceptors(counter).Options);
        await BuildService(new FakeRunnerClient { Sessions = running }, db: db).GetAsync(CancellationToken.None);
        return counter.Count("AgentTaskEvents");
    }
}
