using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0726 V-8..V-13, V-27, V-28. The runner and journal alarm state machine.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerAlarmCoordinatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 1, 2, 3, TimeSpan.Zero);

    [Test]
    public async Task a_runner_down_past_the_grace_raises_once_with_counts_and_one_note_per_caller()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var seed = await SeedServer2Async(db);
        var source = Down("server2", "transport_abort");
        var state = new RunnerAlarmState();
        var notifier = new RecordingNotifier();
        var logger = new ListLogger<RunnerAlarmCoordinator>();
        var coordinator = Build(db, source, state, notifier, logger: logger);

        await coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        var opened = state.Current.Episodes.ShouldHaveSingleItem();
        opened.RaisedAt.ShouldBeNull();
        notifier.Notes.ShouldBeEmpty();

        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(179), CancellationToken.None);
        state.Current.Episodes.ShouldHaveSingleItem().RaisedAt.ShouldBeNull();

        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);
        var raised = state.Current.Episodes.ShouldHaveSingleItem();
        raised.RaisedAt.ShouldBe(T0.AddSeconds(180));
        raised.PinnedOpenTasks.ShouldBe(4);
        raised.LiveSessions.ShouldBe(1);
        raised.LastReason.ShouldBe("transport_abort");
        notifier.Notes.Select(note => note.SessionId).ShouldBe([seed.P1, seed.P2], ignoreOrder: true);
        notifier.Notes.ShouldAllBe(note => note.Header == "[runner server2 unavailable]");
        var p1 = notifier.Notes.Single(note => note.SessionId == seed.P1).Body;
        p1.ShouldContain(Short(seed.T1));
        p1.ShouldContain(Short(seed.T3));
        p1.ShouldNotContain(Short(seed.T2));
        notifier.Notes.Single(note => note.SessionId == seed.P2).Body.ShouldContain(Short(seed.T2));
        raised.NotifiedSessionIds.ShouldBe([seed.P1, seed.P2], ignoreOrder: true);
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("server2", StringComparison.Ordinal));

        await AddTaskAsync(db, "server2", AgentTaskStatus.Working, seed.P1, AgentTaskReplyTo.Session, AgentTaskRole.Code);
        await db.SaveChangesAsync();
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(240), CancellationToken.None);
        state.Current.Episodes.ShouldHaveSingleItem().PinnedOpenTasks.ShouldBe(5);
        notifier.Notes.Count.ShouldBe(2);
    }

    [Test]
    public async Task a_flap_inside_the_grace_leaves_no_trace()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var source = Down("server2", "transport_abort");
        var state = new RunnerAlarmState();
        var notifier = new RecordingNotifier();
        var logger = new ListLogger<RunnerAlarmCoordinator>();
        var coordinator = Build(db, source, state, notifier, logger: logger);

        await coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        source.Rows[0] = source.Rows[0] with { Eligible = true, DisconnectReason = null };
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(60), CancellationToken.None);

        state.Current.Episodes.ShouldBeEmpty();
        notifier.Notes.ShouldBeEmpty();
        logger.Entries.ShouldNotContain(entry => entry.Level > LogLevel.Debug
            && entry.Message.Contains("server2", StringComparison.Ordinal));
    }

    [Test]
    public async Task recovery_resolves_and_tells_only_the_notified_callers()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var seed = await SeedServer2Async(db);
        var source = Down("server2", "transport_abort");
        var state = new RunnerAlarmState();
        var notifier = new RecordingNotifier();
        var logger = new ListLogger<RunnerAlarmCoordinator>();
        var coordinator = Build(db, source, state, notifier, logger: logger);
        await coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);

        await AddTaskAsync(db, "server2", AgentTaskStatus.Queued, seed.P3, AgentTaskReplyTo.Session, AgentTaskRole.Code);
        await db.SaveChangesAsync();
        source.Rows[0] = source.Rows[0] with { Eligible = true, DisconnectReason = null };
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(444), CancellationToken.None);

        state.Current.Episodes.ShouldBeEmpty();
        var recovery = notifier.Notes.Where(note => note.Header == "[runner server2 recovered]").ToList();
        recovery.Select(note => note.SessionId).ShouldBe([seed.P1, seed.P2], ignoreOrder: true);
        recovery.ShouldAllBe(note => note.Body.Contains("after 7.4 min", StringComparison.Ordinal));
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Information);
    }

    [Test]
    public async Task a_draining_or_retired_runner_never_raises_and_a_disabled_entry_is_skipped()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var exclusion = new FakeExclusion();
        var notifier = new RecordingNotifier();
        var state = new RunnerAlarmState();
        var source = new FakeEligibilitySource();
        source.Rows.Add(Row("server2", enabled: true, eligible: false, "transport_abort"));
        exclusion.Reasons["server2"] = "draining";
        var coordinator = Build(db, source, state, notifier, exclusion);

        await coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(600), CancellationToken.None);
        state.Current.Episodes.ShouldBeEmpty();
        notifier.Notes.ShouldBeEmpty();

        source.Rows.Add(Row("off", enabled: false, eligible: false, "socket_closed"));
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(601), CancellationToken.None);
        state.Current.Episodes.ShouldNotContain(episode => episode.RunnerId == "off");

        exclusion.Reasons.Remove("server2");
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(700), CancellationToken.None);
        state.Current.Episodes.Single(episode => episode.RunnerId == "server2").DownSince.ShouldBe(T0.AddSeconds(700));
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(879), CancellationToken.None);
        state.Current.Episodes.Single(episode => episode.RunnerId == "server2").RaisedAt.ShouldBeNull();
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(880), CancellationToken.None);
        state.Current.Episodes.Single(episode => episode.RunnerId == "server2").RaisedAt.ShouldBe(T0.AddSeconds(880));

        var rolling = new FakeEligibilitySource();
        var rollingState = new RunnerAlarmState();
        var rollingNotes = new RecordingNotifier();
        var rollingExclusion = new FakeExclusion { Reasons = { ["r2"] = "draining" } };
        rolling.Rows.Add(Row("r2", enabled: true, eligible: true, null));
        var rollingCoordinator = Build(db, rolling, rollingState, rollingNotes, rollingExclusion);
        await rollingCoordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        rolling.Rows[0] = rolling.Rows[0] with { Eligible = false, DisconnectReason = "socket_closed" };
        await rollingCoordinator.EvaluateRunnersAsync(T0.AddMinutes(10), CancellationToken.None);
        rolling.Rows[0] = rolling.Rows[0] with { Eligible = true, DisconnectReason = null };
        await rollingCoordinator.EvaluateRunnersAsync(T0.AddMinutes(11), CancellationToken.None);
        rollingExclusion.Reasons.Remove("r2");
        await rollingCoordinator.EvaluateRunnersAsync(T0.AddMinutes(12), CancellationToken.None);
        rollingState.Current.Episodes.ShouldBeEmpty();
        rollingNotes.Notes.ShouldBeEmpty();

        var p6 = Guid.NewGuid();
        await AddSessionAsync(db, p6, "r3", SessionStatus.Running);
        await AddTaskAsync(db, "r3", AgentTaskStatus.Working, p6, AgentTaskReplyTo.Session, AgentTaskRole.Code);
        await db.SaveChangesAsync();
        var retiredSource = Down("r3", "socket_closed");
        var retiredState = new RunnerAlarmState();
        var retiredNotes = new RecordingNotifier();
        var retiredExclusion = new FakeExclusion();
        var retired = Build(db, retiredSource, retiredState, retiredNotes, retiredExclusion);
        await retired.EvaluateRunnersAsync(T0, CancellationToken.None);
        await retired.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);
        retiredState.Current.Episodes.ShouldHaveSingleItem().RaisedAt.ShouldNotBeNull();
        retiredExclusion.Reasons["r3"] = "retired";
        await retired.EvaluateRunnersAsync(T0.AddSeconds(181), CancellationToken.None);
        retiredState.Current.Episodes.ShouldBeEmpty();
        retiredNotes.Notes.Where(note => note.Header == "[runner r3 recovered]").Select(note => note.SessionId)
            .ShouldBe([p6]);
    }

    [Test]
    public async Task startup_opens_an_episode_per_enabled_remote_runner_and_a_normal_reconnect_closes_it()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var pa = Guid.NewGuid();
        var pb = Guid.NewGuid();
        await AddSessionAsync(db, pa, "a", SessionStatus.Stopped);
        await AddSessionAsync(db, pb, "b", SessionStatus.Running);
        await AddTaskAsync(db, "a", AgentTaskStatus.Working, pa, AgentTaskReplyTo.Session, AgentTaskRole.Code);
        await AddTaskAsync(db, "b", AgentTaskStatus.Working, pb, AgentTaskReplyTo.Session, AgentTaskRole.Code);
        await db.SaveChangesAsync();
        var source = new FakeEligibilitySource();
        source.Rows.Add(Row("a", true, false, "transport_abort"));
        source.Rows.Add(Row("b", true, false, "transport_abort"));
        var state = new RunnerAlarmState();
        var notifier = new RecordingNotifier();
        var logger = new ListLogger<RunnerAlarmCoordinator>();
        var coordinator = Build(db, source, state, notifier, logger: logger);

        await coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        state.Current.Episodes.Count.ShouldBe(2);
        state.Current.Episodes.ShouldAllBe(episode => episode.DownSince == T0);

        source.Rows[0] = source.Rows[0] with { Eligible = true, DisconnectReason = null };
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(40), CancellationToken.None);
        state.Current.Episodes.ShouldNotContain(episode => episode.RunnerId == "a");
        notifier.Notes.ShouldBeEmpty();
        logger.Entries.ShouldNotContain(entry => entry.Level > LogLevel.Debug && entry.Message.Contains("runner-a", StringComparison.Ordinal) == false
            && entry.Message.Contains(" a", StringComparison.Ordinal));

        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);
        notifier.Notes.Select(note => note.SessionId).ShouldBe([pb]);
        notifier.Notes.ShouldNotContain(note => note.SessionId == pa);
    }

    [Test]
    public async Task down_since_uses_the_last_disconnect_after_the_last_resolution()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var source = new FakeEligibilitySource();
        source.Rows.Add(Row("server2", true, true, null));
        var state = new RunnerAlarmState();
        var coordinator = Build(db, source, state, new RecordingNotifier());

        await coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        state.Current.Episodes.ShouldBeEmpty();

        source.Rows[0] = Row("server2", true, false, "transport_abort", T0.AddSeconds(10));
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(100), CancellationToken.None);
        state.Current.Episodes.ShouldHaveSingleItem().DownSince.ShouldBe(T0.AddSeconds(10));
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(189), CancellationToken.None);
        state.Current.Episodes.ShouldHaveSingleItem().RaisedAt.ShouldBeNull();
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(190), CancellationToken.None);
        state.Current.Episodes.ShouldHaveSingleItem().RaisedAt.ShouldBe(T0.AddSeconds(190));

        source.Rows[0] = source.Rows[0] with { Eligible = true, DisconnectReason = null };
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(300), CancellationToken.None);
        source.Rows[0] = Row("server2", true, false, "transport_abort", T0.AddSeconds(10));
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(400), CancellationToken.None);
        state.Current.Episodes.ShouldHaveSingleItem().DownSince.ShouldBe(T0.AddSeconds(400));
    }

    [Test]
    public async Task a_failed_note_is_retried_on_the_next_wake_and_never_sent_twice()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var seed = await SeedServer2Async(db);
        var source = Down("server2", "transport_abort");
        var state = new RunnerAlarmState();
        var notifier = new RecordingNotifier();
        notifier.ThrowOnce(seed.P2);
        var logger = new ListLogger<RunnerAlarmCoordinator>();
        var coordinator = Build(db, source, state, notifier, logger: logger);
        await coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);

        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);
        notifier.Notes.Select(note => note.SessionId).ShouldBe([seed.P1]);
        state.Current.Episodes.ShouldHaveSingleItem().NotifiedSessionIds.ShouldBe([seed.P1]);
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains(seed.P2.ToString(), StringComparison.Ordinal));

        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(181), CancellationToken.None);
        notifier.Notes.Count(note => note.SessionId == seed.P2).ShouldBe(1);
        state.Current.Episodes.ShouldHaveSingleItem().NotifiedSessionIds.ShouldBe([seed.P1, seed.P2], ignoreOrder: true);

        var before = notifier.Notes.Count;
        await coordinator.EvaluateRunnersAsync(T0.AddSeconds(182), CancellationToken.None);
        notifier.Notes.Count.ShouldBe(before);
        notifier.Notes.Count(note => note.SessionId == seed.P1).ShouldBe(1);
    }

    [Test]
    public async Task journal_findings_are_published_per_repository_and_cleared_when_recovered()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        using var repo1 = new ScratchGitRepo("c726-journal-a");
        using var repo2 = new ScratchGitRepo("c726-journal-b");
        var git = new LandingGit();
        var common1 = await git.CommonDirectoryAsync(repo1.Path, CancellationToken.None);
        var worktree = Path.Combine(repo1.WorktreeRoot, "linked");
        await repo1.GitAsync("worktree", "add", worktree);
        db.Projects.Add(new Project
        {
            Id = Guid.NewGuid(), Name = "one", GitRepositoryUrl = "https://example.test/one",
            LocalRepositoryPath = repo1.Path, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        db.Projects.Add(new Project
        {
            Id = Guid.NewGuid(), Name = "two", GitRepositoryUrl = "https://example.test/two",
            LocalRepositoryPath = repo2.Path, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        db.Projects.Add(new Project
        {
            Id = Guid.NewGuid(), Name = "gone", GitRepositoryUrl = "https://example.test/gone",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), "c726-missing-" + Guid.NewGuid().ToString("N")),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await AddTaskAsync(db, null, AgentTaskStatus.Working, Guid.NewGuid(), AgentTaskReplyTo.None, AgentTaskRole.Code, worktree);
        await db.SaveChangesAsync();
        var ticks = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks + 1;
        var children = Path.Combine(common1, "antiphon", "children");
        Directory.CreateDirectory(children);
        var record = Path.Combine(children, Guid.NewGuid().ToString("N") + ".json");
        var now = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(record, System.Text.Json.JsonSerializer.Serialize(
            new RepositoryChildJournal.ChildRecord(1, common1, Environment.ProcessId, ticks)));
        File.SetLastWriteTimeUtc(record, now.AddMinutes(-10).UtcDateTime);
        var inspector = new CountingInspector(git);
        var state = new RunnerAlarmState();
        var logger = new ListLogger<RunnerAlarmCoordinator>();
        var coordinator = Build(db, new FakeEligibilitySource(), state, new RecordingNotifier(), journals: inspector, logger: logger);

        await coordinator.EvaluateJournalsAsync(null, now, CancellationToken.None);
        var finding = state.Current.Journals.ShouldHaveSingleItem();
        LandingGit.PathsEqual(finding.CommonDirectory, common1).ShouldBeTrue();
        finding.StaleCount.ShouldBe(1);
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Debug);
        var calls = inspector.Calls;

        File.Delete(record);
        await coordinator.EvaluateJournalsAsync(null, now, CancellationToken.None);
        state.Current.Journals.ShouldBeEmpty();

        await File.WriteAllTextAsync(record, System.Text.Json.JsonSerializer.Serialize(
            new RepositoryChildJournal.ChildRecord(1, common1, Environment.ProcessId, ticks)));
        File.SetLastWriteTimeUtc(record, now.AddMinutes(-10).UtcDateTime);
        var beforeDisabled = inspector.Calls;
        var disabled = Build(db, new FakeEligibilitySource(), state, new RecordingNotifier(),
            settings: new AlarmSettings { JournalEnabled = false }, journals: inspector, logger: logger);
        await disabled.EvaluateJournalsAsync(null, now, CancellationToken.None);
        state.Current.Journals.ShouldBeEmpty();
        inspector.Calls.ShouldBe(beforeDisabled);
        _ = calls;
    }

    [Test]
    public async Task journal_findings_publish_each_records_file_state_age_process_and_write_time()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        using var repo = new ScratchGitRepo("c726-journal-record");
        var git = new LandingGit();
        var common = await git.CommonDirectoryAsync(repo.Path, CancellationToken.None);
        db.Projects.Add(new Project
        {
            Id = Guid.NewGuid(), Name = "one", GitRepositoryUrl = "https://example.test/one",
            LocalRepositoryPath = repo.Path, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var ticks = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks + 1;
        var children = Path.Combine(common, "antiphon", "children");
        Directory.CreateDirectory(children);
        var record = Path.Combine(children, Guid.NewGuid().ToString("N") + ".json");
        var now = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(record, System.Text.Json.JsonSerializer.Serialize(
            new RepositoryChildJournal.ChildRecord(1, common, Environment.ProcessId, ticks)));
        File.SetLastWriteTimeUtc(record, now.AddMinutes(-10).UtcDateTime);
        var state = new RunnerAlarmState();
        var coordinator = Build(db, new FakeEligibilitySource(), state, new RecordingNotifier());

        await coordinator.EvaluateJournalsAsync(null, now, CancellationToken.None);

        var finding = state.Current.Journals.ShouldHaveSingleItem();
        finding.StaleCount.ShouldBe(1);
        var published = finding.Records.ShouldHaveSingleItem();
        LandingGit.PathsEqual(published.File, record).ShouldBeTrue();
        published.State.ShouldBe(JournalRecordState.Dead);
        published.ProcessId.ShouldBe(Environment.ProcessId);
        published.Age.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(5));
        published.WrittenAt.ShouldBe(new DateTimeOffset(File.GetLastWriteTimeUtc(record), TimeSpan.Zero));
    }

    private static RunnerAlarmCoordinator Build(
        AppDbContext db, FakeEligibilitySource source, RunnerAlarmState state, RecordingNotifier notifier,
        FakeExclusion? exclusion = null, AlarmSettings? settings = null,
        RepositoryChildJournalInspector? journals = null, ListLogger<RunnerAlarmCoordinator>? logger = null) =>
        new(source, exclusion ?? new FakeExclusion(), state,
            journals ?? new RepositoryChildJournalInspector(new LandingGit()), db, notifier,
            new CompletionNoteFlushQueue(), Options.Create(settings ?? new AlarmSettings()),
            logger ?? new ListLogger<RunnerAlarmCoordinator>());

    private static AppDbContext Context(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static FakeEligibilitySource Down(string id, string reason)
    {
        var source = new FakeEligibilitySource();
        source.Rows.Add(Row(id, true, false, reason));
        return source;
    }

    private static RunnerEligibilitySnapshot Row(string id, bool enabled, bool eligible, string? reason,
        DateTimeOffset? disconnected = null) =>
        new(id, id, enabled, eligible, reason, disconnected, 0);

    private static async Task<Server2Seed> SeedServer2Async(AppDbContext db)
    {
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var p3 = Guid.NewGuid();
        var p4 = Guid.NewGuid();
        var p5 = Guid.NewGuid();
        await AddSessionAsync(db, p1, "server2", SessionStatus.Running);
        await AddSessionAsync(db, p2, "server2", SessionStatus.Stopped);
        await AddSessionAsync(db, p3, "server2", SessionStatus.Stopped);
        await AddSessionAsync(db, p4, "server2", SessionStatus.Stopped);
        await AddSessionAsync(db, p5, "server2", SessionStatus.Stopped);
        var t1 = await AddTaskAsync(db, "server2", AgentTaskStatus.Working, p1, AgentTaskReplyTo.Session, AgentTaskRole.Plan);
        var t2 = await AddTaskAsync(db, "server2", AgentTaskStatus.Queued, p2, AgentTaskReplyTo.Session, AgentTaskRole.Code);
        var t3 = await AddTaskAsync(db, "server2", AgentTaskStatus.Blocked, p1, AgentTaskReplyTo.Session, AgentTaskRole.Code);
        await AddTaskAsync(db, "server2", AgentTaskStatus.Succeeded, p3, AgentTaskReplyTo.Session, AgentTaskRole.Code);
        await AddTaskAsync(db, null, AgentTaskStatus.Working, p3, AgentTaskReplyTo.Session, AgentTaskRole.Code);
        await AddTaskAsync(db, "server2", AgentTaskStatus.Working, p4, AgentTaskReplyTo.Session, AgentTaskRole.Check);
        var t7 = await AddTaskAsync(db, "server2", AgentTaskStatus.Dispatched, p5, AgentTaskReplyTo.None, AgentTaskRole.Code);
        await db.SaveChangesAsync();
        return new Server2Seed(p1, p2, p3, p4, p5, t1, t2, t3, t7);
    }

    private static async Task AddSessionAsync(AppDbContext db, Guid id, string runnerId, SessionStatus status)
    {
        var now = DateTime.UtcNow;
        db.AgentSessions.Add(new AgentSession
        {
            Id = id,
            Status = status,
            RunnerId = runnerId,
            RunnerStoreId = Guid.NewGuid(),
            RunnerCwd = "/work",
            Cwd = "/work",
            DefinitionName = "test",
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await Task.CompletedTask;
    }

    private static async Task<Guid> AddTaskAsync(AppDbContext db, string? runnerId, AgentTaskStatus status,
        Guid parent, AgentTaskReplyTo replyTo, AgentTaskRole role, string? repoPath = null)
    {
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = role + " task",
            Goal = "goal",
            Role = role,
            Status = status,
            RunnerId = runnerId,
            ParentSessionId = parent,
            ReplyTo = replyTo,
            RepoPath = repoPath,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            CreatedAt = DateTime.UtcNow,
        });
        await Task.CompletedTask;
        return id;
    }

    private static string Short(Guid id) => id.ToString("N")[..8];

    private sealed record Server2Seed(Guid P1, Guid P2, Guid P3, Guid P4, Guid P5, Guid T1, Guid T2, Guid T3, Guid T7);

    private sealed class CountingInspector(ILandingGit git) : RepositoryChildJournalInspector(git)
    {
        public int Calls;
        public override async Task<JournalInspection> InspectAsync(
            string repository, TimeSpan staleAfter, DateTimeOffset now, CancellationToken ct)
        {
            Calls++;
            return await base.InspectAsync(repository, staleAfter, now, ct);
        }
    }
}
