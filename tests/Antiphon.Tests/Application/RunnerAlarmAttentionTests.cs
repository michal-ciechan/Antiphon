using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class RunnerAlarmAttentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task a_raised_episode_is_one_error_row_and_an_unraised_one_is_none()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var down = Now.AddMinutes(-4);
        var raised = Episode("server2", down, Now.AddMinutes(-1));
        var unraised = Episode("server2-temp", down, null);
        var state = new RunnerAlarmState();
        state.Publish(Snapshot([raised, unraised], []));

        var rows = (await ReadAsync(schema, state)).Items
            .Where(item => item.Kind == AttentionKind.RunnerUnavailable).ToList();
        var row = rows.ShouldHaveSingleItem();
        row.Severity.ShouldBe(AlertSeverity.Error);
        row.Title.ShouldBe("Runner server2 unavailable");
        row.ConditionKey.ShouldBe("runner-unavailable:server2");
        row.Headline.ShouldContain("3 open task(s)");
        row.Headline.ShouldContain("1 live session(s)");
        row.Evidence.ShouldContain("lastReason=transport_abort");
        row.Evidence.ShouldContain("notified=2");
        row.SinceUtc.ShouldBe(down.UtcDateTime);
        row.Actions.ShouldBe([AttentionAction.OpenDrawer]);
        row.TaskId.ShouldBeNull();
        row.SessionId.ShouldBeNull();
        row.AgentId.ShouldBeNull();

        state.Publish(Snapshot([unraised], []));
        (await ReadAsync(schema, state)).Items
            .Where(item => item.Kind == AttentionKind.RunnerUnavailable).ShouldBeEmpty();
    }

    [Test]
    public async Task a_journal_finding_is_one_error_row_naming_the_recovery_command()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var commonA = Path.Combine(Path.GetTempPath(), "c726", "a");
        var commonB = Path.Combine(Path.GetTempPath(), "c726", "b");
        var commonC = Path.Combine(Path.GetTempPath(), "c726", "c");
        var repositoryA = Path.Combine(Path.GetTempPath(), "c726", "repo-a");
        var old = Now.AddMinutes(-11);
        var newer = Now.AddMinutes(-6);
        var staleA = Finding(repositoryA, commonA,
            Record("dead-old", JournalRecordState.Dead, old),
            Record("unknown-new", JournalRecordState.Unknown, newer),
            Record("alive", JournalRecordState.Alive, Now.AddMinutes(-20)));
        var aliveOnly = Finding("repo-b", commonB, Record("alive", JournalRecordState.Alive, old));
        var staleC = Finding("repo-c", commonC, Record("dead", JournalRecordState.Dead, newer));
        var state = new RunnerAlarmState();
        state.Publish(Snapshot([], [staleA, aliveOnly, staleC]));

        var rows = (await ReadAsync(schema, state)).Items
            .Where(item => item.Kind == AttentionKind.RepositoryChildJournalStale).ToList();
        rows.Count.ShouldBe(2);
        var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(commonA));
        var row = rows.Single(item => item.ConditionKey == "journal-stale:" + key);
        row.Severity.ShouldBe(AlertSeverity.Error);
        row.Headline.ShouldStartWith("2 stale");
        row.Evidence.ShouldContain("recover-repository-children.ps1 -Repository " + repositoryA);
        row.Evidence.ShouldContain("-Execute -ConfirmDescendantsExited");
        row.Evidence.ShouldContain("dead-old");
        row.Evidence.ShouldContain("unknown-new");
        row.Evidence.ShouldContain("alive");
        row.SinceUtc.ShouldBe(old.UtcDateTime);
        row.ConditionKey.ShouldNotBe(rows.Single(item => item != row).ConditionKey);
        rows.ShouldNotContain(item => item.ConditionKey == "journal-stale:" + commonB);
    }

    [Test]
    public async Task no_state_means_no_rows_and_the_summary_counts_both_kinds_open()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var baseline = await ReadAsync(schema);
        baseline.Items.Where(IsAlarm).ShouldBeEmpty();
        var state = new RunnerAlarmState();
        state.Publish(Snapshot([Episode("server2", Now.AddMinutes(-4), Now.AddMinutes(-1))],
            [Finding("repo", Path.Combine(Path.GetTempPath(), "c726", "summary"),
                Record("dead", JournalRecordState.Dead, Now.AddMinutes(-6)))]));

        var withAlarms = await ReadAsync(schema, state);
        withAlarms.Items.Count(IsAlarm).ShouldBe(2);
        AttentionSummaryDto.From(withAlarms).Open.ShouldBe(AttentionSummaryDto.From(baseline).Open + 2);
    }

    private static bool IsAlarm(AttentionItemDto item) =>
        item.Kind is AttentionKind.RunnerUnavailable or AttentionKind.RepositoryChildJournalStale;

    private static RunnerOutageEpisode Episode(string id, DateTimeOffset down, DateTimeOffset? raised) =>
        new(id, id, down, "transport_abort", raised, 3, 1,
            [Guid.NewGuid(), Guid.NewGuid()], []);

    private static JournalAlarmRecord Record(string file, JournalRecordState kind, DateTimeOffset at) =>
        new(file, kind, Now - at, 42, at);

    private static JournalFinding Finding(string repository, string common, params JournalAlarmRecord[] records) =>
        new(repository, common, records.Count(record => record.State != JournalRecordState.Alive), Now)
        { Records = records };

    private static RunnerAlarmSnapshot Snapshot(
        IReadOnlyList<RunnerOutageEpisode> episodes, IReadOnlyList<JournalFinding> findings) =>
        new(episodes, new Dictionary<string, DateTimeOffset>(), findings, []);

    private static async Task<AttentionDto> ReadAsync(IsolatedTestSchema schema, RunnerAlarmState? alarms = null)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        return await new AttentionService(
            db, new AttentionServiceTests.FakeRunnerClient(),
            Options.Create(new SupervisionSettings()), Options.Create(new DelegationSettings()),
            new FakeTimeProvider(Now), NullLogger<AttentionService>.Instance,
            alarms: alarms).GetAsync(CancellationToken.None, includeProgressProbe: false);
    }
}
