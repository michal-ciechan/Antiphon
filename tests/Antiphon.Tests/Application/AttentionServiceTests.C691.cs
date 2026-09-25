using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AttentionServiceTests
{
    [Test]
    [NotInParallel("C691Census")]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task overlapping_session_leaks_have_one_full_feed_row_until_resolved(bool remote, bool unowned)
    {
        await using var w = await CensusWorld.CreateAsync(remote);
        if (unowned) await w.RemoveOwnerAsync();
        w.Session.Status = SessionStatus.Stopping;
        w.Session.LastSeenAt = w.Now.AddMinutes(-6);
        w.Session.FailureReason = "kill not delivered: transport lost";
        await w.Db.SaveChangesAsync();

        string? key = null;
        for (var poll = 0; poll < 2; poll++)
        {
            // Check every open row, not just the kind we expect to win. Failed-task history
            // is context and is deliberately excluded from the attention badge itself.
            var row = (await w.FeedAsync()).Items.Where(i => i.Kind != AttentionKind.RecentFailure)
                .ShouldHaveSingleItem();
            row.Kind.ShouldBe(AttentionKind.SessionStopStuck);
            row.SessionId.ShouldBe(w.Session.Id);
            row.Evidence.ShouldContain(w.Session.FailureReason);
            row.Evidence.ShouldContain(unowned ? "no agent" : w.Task.Id.ToString());
            row.Evidence.ShouldContain(unowned ? "SessionUnowned" : "PoolDelegateUnreleased");
            if (poll > 0) row.ConditionKey.ShouldBe(key);
            key = row.ConditionKey;
            w.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        // A second, independent leak must retain its own row.
        var other = new AgentSession
        {
            Id = Guid.NewGuid(), DefinitionName = "other leak", AgentKind = AgentKind.Codex,
            Cwd = Path.GetTempPath(), Status = SessionStatus.Stopping,
            CreatedAt = w.Now.AddHours(-1), StartedAt = w.Now.AddHours(-1), LastSeenAt = w.Now.AddMinutes(-6),
        };
        w.Db.AgentSessions.Add(other);
        await w.Db.SaveChangesAsync();
        var both = (await w.FeedAsync()).Items.Where(i => i.Kind != AttentionKind.RecentFailure).ToList();
        both.Count.ShouldBe(2);
        both.Select(i => i.SessionId).ShouldBe([w.Session.Id, other.Id], ignoreOrder: true);

        w.Session.Status = other.Status = SessionStatus.Stopped;
        w.Session.EndedAt = other.EndedAt = w.Now;
        w.RunnerLive = w.Directory.Live = false;
        await w.Db.SaveChangesAsync();
        for (var poll = 0; poll < 2; poll++)
            (await w.FeedAsync()).Items.Where(i => i.Kind != AttentionKind.RecentFailure).ShouldBeEmpty();
    }

    [Test]
    [NotInParallel("C691Census")]
    public async Task census_inspection_includes_every_candidate_beyond_the_feed_preview()
    {
        await using var w = await CensusWorld.CreateAsync();
        var candidates = Enumerable.Range(1, 7)
            .Select(pid => CensusCandidate(pid, ZombieCensusClass.Unclaimed, null)).ToArray();
        w.Census.Publish(new ZombieCensusResult(w.Clock.GetUtcNow(), TimeSpan.Zero,
            new ZombieCensusCounts(0, 0, 0, 7, 0, 0, 7), candidates, candidates, []));
        var row = (await w.RowsAsync(AttentionKind.ZombieCensusReport)).ShouldHaveSingleItem();
        // Assert the producer-to-client contract without a production DTO addition in this red commit.
        var json = System.Text.Json.JsonSerializer.SerializeToElement(row,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        json.TryGetProperty("censusCandidates", out var details).ShouldBeTrue();
        details.GetArrayLength().ShouldBe(7);
        details.EnumerateArray().Select(r => r.GetProperty("pid").GetInt32()).ShouldBe(Enumerable.Range(1, 7));
        row.Evidence.ShouldNotContain("pid=6");
    }

    [Test]
    [NotInParallel("C691Census")]
    [Arguments(false)]
    [Arguments(true)]
    public async Task a_running_pool_delegate_with_a_failed_task_older_than_twice_the_grace_is_unreleased(bool remote)
    {
        await using var w = await CensusWorld.CreateAsync(remote);
        var item = (await w.RowsAsync(AttentionKind.PoolDelegateUnreleased)).ShouldHaveSingleItem();
        item.Severity.ShouldBe(AlertSeverity.Error);
        item.AgentId.ShouldBe(w.Agent.Id);
        item.TaskId.ShouldBe(w.Task.Id);
        item.SessionId.ShouldBe(w.Session.Id);
        item.ModelKind.ShouldBe("Codex");
        item.Evidence.ShouldContain(w.Task.Id.ToString());
        item.Evidence.ShouldContain("Failed");
        item.Evidence.ShouldContain("Codex");
        item.Actions.ShouldBe([AttentionAction.OpenAgent, AttentionAction.OpenDrawer]);
        // The projection is read-only, including when the sweep is disabled.
        w.GraceSeconds = 0;
        (await w.RowsAsync(AttentionKind.PoolDelegateUnreleased)).ShouldHaveSingleItem();
        (await w.Db.Agents.AsNoTracking().SingleAsync()).Status.ShouldBe(AgentStatus.Running);
    }

    [Test]
    [NotInParallel("C691Census")]
    [Arguments("grace")]
    [Arguments("queued")]
    [Arguments("dispatched")]
    [Arguments("working")]
    [Arguments("blocked")]
    [Arguments("warm")]
    [Arguments("stopped")]
    [Arguments("always-on")]
    [Arguments("standing")]
    [Arguments("sourced")]
    [Arguments("terminal")]
    [Arguments("mid-turn")]
    public async Task a_pool_delegate_inside_twice_the_grace_is_not_listed(string exclusion)
    {
        await using var w = await CensusWorld.CreateAsync();
        (await w.RowsAsync(AttentionKind.PoolDelegateUnreleased)).ShouldHaveSingleItem();
        switch (exclusion)
        {
            case "grace": w.Task.CompletedAt = w.Now.AddSeconds(-240); break;
            case "queued": w.Task.Status = AgentTaskStatus.Queued; break;
            case "dispatched": w.Task.Status = AgentTaskStatus.Dispatched; break;
            case "working": w.Task.Status = AgentTaskStatus.Working; break;
            case "blocked": w.Task.Status = AgentTaskStatus.Blocked; break;
            case "warm": w.Agent.PoolIdleSince = w.Now.AddMinutes(-20); break;
            case "stopped": w.Agent.Status = AgentStatus.Stopped; break;
            case "always-on": w.Agent.AlwaysOn = true; break;
            case "standing": w.Agent.StandingSpecialistRole = AgentTaskRole.Check; break;
            case "sourced":
                var operationId = Guid.NewGuid();
                w.Db.AgentTaskLandings.Add(new AgentTaskLanding
                {
                    Id = operationId, TaskId = w.Task.Id, CreatedAt = w.Now, UpdatedAt = w.Now,
                });
                await w.Db.SaveChangesAsync();
                var sourcedId = Guid.NewGuid();
                w.Db.AgentTasks.Add(new AgentTask
                {
                    Id = sourcedId, RootTaskId = sourcedId, AgentId = w.Agent.Id, AgentSessionId = w.Session.Id,
                    Title = "sourced owner", Goal = "verify source", WorkingDirectory = Path.GetTempPath(),
                    Role = AgentTaskRole.Mutation, Status = AgentTaskStatus.Succeeded,
                    SourceLandingOperationId = operationId, SourceLandingSha = new string('a', 40),
                    CreatedAt = w.Now.AddHours(-1), CompletedAt = w.Now.AddMinutes(-8),
                });
                break;
            case "terminal": w.Session.Status = SessionStatus.Stopped; break;
            case "mid-turn":
                w.Db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = w.Session.Id, Sequence = 1,
                    Kind = TranscriptKinds.UserPrompt, Text = "Still working", Timestamp = w.Now,
                });
                break;
        }
        await w.Db.SaveChangesAsync();
        (await w.RowsAsync(AttentionKind.PoolDelegateUnreleased)).ShouldBeEmpty();
    }

    [Test]
    [NotInParallel("C691Census")]
    [Arguments(false)]
    [Arguments(true)]
    public async Task a_stopping_session_older_than_five_minutes_is_stop_stuck(bool remote)
    {
        await using var w = await CensusWorld.CreateAsync(remote);
        w.Session.Status = SessionStatus.Stopping;
        w.Session.LastSeenAt = w.Now.AddMinutes(-6);
        w.Session.FailureReason = "kill not delivered: transport lost";
        await w.Db.SaveChangesAsync();
        var item = (await w.RowsAsync(AttentionKind.SessionStopStuck)).ShouldHaveSingleItem();
        item.Severity.ShouldBe(AlertSeverity.Error);
        item.ModelKind.ShouldBe("Codex");
        item.Evidence.ShouldContain(w.Session.FailureReason);
        item.Evidence.ShouldContain("confirmed live");
        w.Session.LastSeenAt = w.Now.AddMinutes(-5);
        await w.Db.SaveChangesAsync();
        (await w.RowsAsync(AttentionKind.SessionStopStuck)).ShouldBeEmpty();
    }

    [Test]
    [NotInParallel("C691Census")]
    public async Task a_pending_remote_kill_remains_visible_without_claiming_an_unknown_session_is_live()
    {
        await using var w = await CensusWorld.CreateAsync(remote: true);
        w.Session.Status = SessionStatus.Failed;
        w.Directory.Unknown = true;
        var intentId = await RunnerSlotService.RecordDeferredKillAsync(w.Db, "server2", w.Session.Id,
            w.Session.StartedAt, "kill after transport loss", CancellationToken.None);
        var intent = await w.Db.AgentIncidents.SingleAsync(i => i.Id == intentId);
        intent.CreatedAt = w.Now.AddMinutes(-6);
        await w.Db.SaveChangesAsync();
        var item = (await w.RowsAsync(AttentionKind.SessionStopStuck)).ShouldHaveSingleItem();
        item.Evidence.ShouldContain("pending:kill-generation:server2:");
        item.Evidence.ShouldContain("unknown");
        item.Headline.ShouldContain("Deferred kill");
        // It is durable stop debt, not a recency-windowed incident.
        w.Clock.Advance(TimeSpan.FromDays(2));
        (await w.RowsAsync(AttentionKind.SessionStopStuck)).ShouldHaveSingleItem();
        intent.FailureReason = "reconciled:done";
        await w.Db.SaveChangesAsync();
        (await w.RowsAsync(AttentionKind.SessionStopStuck)).ShouldBeEmpty();
    }

    [Test]
    [NotInParallel("C691Census")]
    [Arguments(false)]
    [Arguments(true)]
    public async Task a_live_session_no_agent_names_is_unowned(bool remote)
    {
        await using var w = await CensusWorld.CreateAsync(remote);
        await w.RemoveOwnerAsync();
        var item = (await w.RowsAsync(AttentionKind.SessionUnowned)).ShouldHaveSingleItem();
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.ModelKind.ShouldBe("Codex");
        item.SessionId.ShouldBe(w.Session.Id);
        item.Actions.ShouldBe([AttentionAction.OpenDrawer]);
        w.Session.StartedAt = w.Now.AddMinutes(-10);
        await w.Db.SaveChangesAsync();
        (await w.RowsAsync(AttentionKind.SessionUnowned)).ShouldBeEmpty();
    }

    [Test]
    [NotInParallel("C691Census")]
    [Arguments("agent")]
    [Arguments("standing")]
    [Arguments("task")]
    [Arguments("ended")]
    public async Task a_live_session_an_agent_names_is_not_unowned(string owner)
    {
        await using var w = await CensusWorld.CreateAsync();
        w.Agent.PersistentSessionId = null;
        await w.Db.SaveChangesAsync();
        (await w.RowsAsync(AttentionKind.SessionUnowned)).ShouldHaveSingleItem();
        switch (owner)
        {
            case "agent": w.Agent.PersistentSessionId = w.Session.Id.ToString("N").ToUpperInvariant(); break;
            case "standing": w.Session.StandingAgentId = w.Agent.Id; break;
            case "task": w.Task.Status = AgentTaskStatus.Queued; break;
            case "ended": w.Session.EndedAt = w.Now.AddMinutes(-2); break;
        }
        await w.Db.SaveChangesAsync();
        (await w.RowsAsync(AttentionKind.SessionUnowned)).ShouldBeEmpty();
    }

    [Test]
    [NotInParallel("C691Census")]
    [Arguments("unknown")]
    [Arguments("pending")]
    [Arguments("gone")]
    public async Task remote_leak_rows_require_confirmed_liveness(string state)
    {
        await using var w = await CensusWorld.CreateAsync(remote: true);
        (await w.RowsAsync(AttentionKind.PoolDelegateUnreleased)).ShouldHaveSingleItem();
        w.Directory.Unknown = state == "unknown";
        w.Directory.Pending = state == "pending";
        w.Directory.Live = state != "gone";
        (await w.RowsAsync(AttentionKind.PoolDelegateUnreleased)).ShouldBeEmpty();
        await w.RemoveOwnerAsync();
        (await w.RowsAsync(AttentionKind.SessionUnowned)).ShouldBeEmpty();
        w.Directory.Unknown = w.Directory.Pending = false;
        w.Directory.Live = true;
        (await w.RowsAsync(AttentionKind.SessionUnowned)).ShouldHaveSingleItem();
    }

    [Test]
    [NotInParallel("C691Census")]
    public async Task the_last_census_result_is_one_row_per_class_with_candidates()
    {
        await using var w = await CensusWorld.CreateAsync();
        var candidates = Enumerable.Range(1, 7).Select(pid => CensusCandidate(pid, ZombieCensusClass.PoolExpired, w.Session.Id))
            .Concat([CensusCandidate(8, ZombieCensusClass.EndedButAlive, w.Session.Id),
                CensusCandidate(9, ZombieCensusClass.Unclaimed, null)]).ToArray();
        w.Census.Publish(new ZombieCensusResult(w.Clock.GetUtcNow(), TimeSpan.FromSeconds(1),
            new ZombieCensusCounts(7, 0, 1, 1, 0, 0, 9), candidates, candidates, []));
        var rows = await w.RowsAsync(AttentionKind.ZombieCensusReport);
        rows.Count.ShouldBe(3);
        rows.Select(r => r.ConditionKey).Distinct().Count().ShouldBe(3);
        var pool = rows.Single(r => r.Headline.Contains("PoolExpired"));
        pool.Headline.ShouldContain("7");
        pool.Evidence.ShouldContain("pid=1");
        pool.Evidence.ShouldContain("pid=5");
        pool.Evidence.ShouldNotContain("pid=6");
        pool.Evidence.ShouldContain(w.Clock.GetUtcNow().ToString("O"));
        pool.ModelKind.ShouldBe("Codex");
        rows.ShouldAllBe(r => r.Severity == AlertSeverity.Warning);
        w.Census = new ZombieCensusState();
        (await w.RowsAsync(AttentionKind.ZombieCensusReport)).ShouldBeEmpty();
        w.Census.Publish(new ZombieCensusResult(w.Clock.GetUtcNow(), TimeSpan.Zero,
            new ZombieCensusCounts(0, 0, 0, 0, 0, 0, 0), [], [], []));
        (await w.RowsAsync(AttentionKind.ZombieCensusReport)).ShouldBeEmpty();
    }

    private static ZombieCensusRow CensusCandidate(int pid, ZombieCensusClass kind, Guid? sessionId) =>
        new(pid, "fake-agent", null, 0, null, ZombieIdentityMethod.I1, sessionId, "Running", "pool-agent",
            kind, [], ZombieFutureAction.None, false, pid, true);

    private sealed class CensusWorld : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        public AppDbContext Db { get; }
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));
        public DateTime Now => Clock.GetUtcNow().UtcDateTime;
        public AgentSession Session { get; }
        public Agent Agent { get; }
        public AgentTask Task { get; }
        public CensusDirectory Directory { get; }
        public ZombieCensusState Census { get; set; } = new();
        public int GraceSeconds { get; set; } = 120;
        public bool RunnerLive { get; set; } = true;

        private CensusWorld(IsolatedTestSchema schema, bool remote)
        {
            _schema = schema;
            Db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            Session = new AgentSession
            {
                Id = Guid.NewGuid(), DefinitionName = "c691", AgentKind = AgentKind.Codex,
                Cwd = Path.GetTempPath(), Status = SessionStatus.Running,
                RunnerId = remote ? "server2" : null,
                RunnerStoreId = remote ? Guid.NewGuid() : null,
                RunnerCwd = remote ? Path.GetTempPath() : null,
                CreatedAt = Now.AddHours(-1), StartedAt = Now.AddHours(-1), LastSeenAt = Now,
            };
            Agent = new Agent
            {
                Id = Guid.NewGuid(), Name = "c691-pool", Slug = "c691-pool", Kind = AgentKind.Codex,
                WorkingDirectory = Path.GetTempPath(), IsPoolDelegate = true, Status = AgentStatus.Running,
                PersistentSessionId = Session.Id.ToString(), CreatedAt = Now, UpdatedAt = Now,
            };
            var taskId = Guid.NewGuid();
            Task = new AgentTask
            {
                Id = taskId, RootTaskId = taskId, AgentId = Agent.Id, AgentSessionId = Session.Id,
                Title = "c691 task", Goal = "test release attention", WorkingDirectory = Path.GetTempPath(),
                Status = AgentTaskStatus.Failed, Workspace = WorkspaceMode.Shared,
                CreatedAt = Now.AddHours(-1), CompletedAt = Now.AddMinutes(-6),
            };
            Directory = new CensusDirectory(Session.Id);
        }

        public static async Task<CensusWorld> CreateAsync(bool remote = false)
        {
            var w = new CensusWorld(await TestDbFixture.CreateIsolatedSchemaAsync(), remote);
            w.Db.AddRange(w.Session, w.Agent, w.Task);
            await w.Db.SaveChangesAsync();
            return w;
        }

        public async Task<List<AttentionItemDto>> RowsAsync(AttentionKind kind) =>
            (await FeedAsync()).Items.Where(i => i.Kind == kind).ToList();

        public async Task<AttentionDto> FeedAsync()
        {
            var runner = new FakeRunnerClient { Sessions = RunnerLive ? [Running(Session.Id)] : [] };
            var service = new AttentionService(Db, runner, Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings { PoolReleaseGraceSeconds = GraceSeconds }), Clock,
                NullLogger<AttentionService>.Instance, censusState: Census, runnerDirectory: Directory);
            return await service.GetAsync(CancellationToken.None);
        }

        public async Task RemoveOwnerAsync()
        {
            Db.AgentTasks.Remove(Task);
            Db.Agents.Remove(Agent);
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _schema.DisposeAsync();
        }
    }

    private sealed class CensusDirectory(Guid sessionId) : ISessionRunnerDirectory
    {
        public bool Live { get; set; } = true;
        public bool Unknown { get; set; }
        public bool Pending { get; set; }
        public ISessionRunnerClient Local { get; } = new FakeRunnerClient();
        public IReadOnlyList<string> KnownRunnerIds => ["server2"];
        public Guid? GetLiveStoreId(string? runnerId) => null;
        public ISessionRunnerClient Resolve(string? runnerId) => throw new NotSupportedException("Attention must use cached inventory");
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) => throw new NotSupportedException();
        public IReadOnlyCollection<Guid> LiveRemoteSessionIds() => Live ? [sessionId] : [];
        public IReadOnlyCollection<Guid> UnknownRemoteSessionIds() => Unknown ? [sessionId] : [];
        public bool RemoteInventoryPending(string? runnerId) => Pending;
    }
}
