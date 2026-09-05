using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.IO.Abstractions.TestingHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-0298: fixture-parity classifier tests plus isolated-schema service tests.
/// Never calls WMI or the production session-runner.
/// </summary>
[Category("Integration")]
public class ZombieCensusServiceTests
{
    private static readonly DateTimeOffset CensusNow = new(2026, 8, 28, 22, 0, 0, TimeSpan.Zero);
    private static readonly ZombieCensusThresholds Thresholds = new(120, 6, 5);
    private static readonly ZombieCensusClassifier Classifier = new();

    private static Guid Id(int n) => Guid.Parse($"aaaaaaaa-0000-0000-0000-{n:D12}");

    private static ZombieOsProcess Proc(
        int pid, int parent, string name, string path, string cmd, string cwd,
        DateTimeOffset start, long ws = 40_000_000, double cpu = 0.1) =>
        new(pid, parent, name, path, cmd, cwd, start, ws, cpu);

    private static SessionRunnerSessionDto Runner(
        Guid sessionId, int pid, int hostPid, DateTimeOffset started, string status = "Running") =>
        new(sessionId, pid, started.UtcDateTime, status, null, AgentExitReason.Unknown, 0, hostPid);

    private static ZombieCensusResult Classify(
        IReadOnlyList<ZombieOsProcess> processes,
        IReadOnlyList<SessionRunnerSessionDto> runner,
        ZombieCensusDbSnapshot db,
        IReadOnlyDictionary<int, Guid>? manifests = null,
        DateTimeOffset? now = null) =>
        Classifier.Classify(processes, runner, db, manifests ?? new Dictionary<int, Guid>(),
            Thresholds, now ?? CensusNow);

    [Test]
    public async Task Normal_22_process_fixture_is_21_I1_one_operator_exclusion_six_codex_zero_candidates()
    {
        var start = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
        var processes = new List<ZombieOsProcess>();
        var runner = new List<SessionRunnerSessionDto>();
        var sessions = new List<ZombieCensusSessionRow>();
        var agents = new List<ZombieCensusAgentRow>();
        var kinds = Enumerable.Repeat("claude", 10).Concat(Enumerable.Repeat("grok", 5))
            .Concat(Enumerable.Repeat("codex", 6)).ToList();

        for (var i = 1; i <= 21; i++)
        {
            var sid = Id(i);
            var kind = kinds[i - 1];
            var hostPid = 30000 + i;
            processes.Add(Proc(hostPid, 4, "Antiphon.PtyHost.exe",
                @"C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe",
                "Antiphon.PtyHost.exe", @"C:\src\Antiphon", start));
            int childPid;
            if (kind == "codex")
            {
                var cmdPid = 40000 + i;
                var nodePid = 41000 + i;
                var codexPid = 42000 + i;
                childPid = cmdPid;
                processes.Add(Proc(cmdPid, hostPid, "cmd.exe", @"C:\Windows\System32\cmd.exe",
                    "cmd.exe /c codex", @"C:\src\Antiphon", start, 8_000_000));
                processes.Add(Proc(nodePid, cmdPid, "node.exe", @"C:\Program Files\nodejs\node.exe",
                    "node.exe", @"C:\src\Antiphon", start, 20_000_000, 1.0));
                processes.Add(Proc(codexPid, nodePid, "codex.exe",
                    @"C:\Users\lndco\AppData\Roaming\npm\codex.exe",
                    "codex.exe", @"C:\src\Antiphon", start, 300_000_000, 2.0));
            }
            else
            {
                var leafPid = 50000 + i;
                childPid = leafPid;
                var exe = kind == "grok" ? "grok.exe" : "claude.exe";
                var path = kind == "grok"
                    ? @"C:\Users\lndco\AppData\Local\grok\grok.exe"
                    : @"C:\Users\lndco\AppData\Local\claude\claude.exe";
                processes.Add(Proc(leafPid, hostPid, exe, path, exe, @"C:\src\Antiphon", start,
                    400_000_000, 5.0));
            }

            runner.Add(Runner(sid, childPid, hostPid, start));
            sessions.Add(new ZombieCensusSessionRow(sid, SessionStatus.Running, start, null,
                @"C:\src\Antiphon", AgentKind.ClaudeCode, null));
            agents.Add(new ZombieCensusAgentRow(Id(100 + i), $"agent-{i}", $"agent-{i}", false,
                AgentStatus.Running, sid, @"C:\src\Antiphon"));
        }

        processes.Add(Proc(27580, 4, "WindowsTerminal.exe",
            @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal\WindowsTerminal.exe",
            "WindowsTerminal.exe", @"C:\src\ClaudeBot",
            new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero), 50_000_000, 0.2));
        processes.Add(Proc(27590, 27580, "cmd.exe", @"C:\Windows\System32\cmd.exe",
            "cmd.exe", @"C:\src\ClaudeBot",
            new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero), 5_000_000));
        processes.Add(Proc(27592, 27590, "claude.exe",
            @"C:\Users\lndco\AppData\Local\claude\claude.exe",
            "claude.exe --name ClaudeBot", @"C:\src\ClaudeBot",
            new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero), 520_000_000, 1.5));

        runner.Add(Runner(Id(22), 59999, 59998, start));
        sessions.Add(new ZombieCensusSessionRow(Id(22), SessionStatus.Running, start, null,
            @"C:\src\Antiphon", AgentKind.ClaudeCode, null));

        var result = Classify(processes, runner, new ZombieCensusDbSnapshot(sessions, agents, []));

        result.Rows.Count(r => r.IdentityMethod == ZombieIdentityMethod.I1).ShouldBe(21);
        result.Counts.Ignored.ShouldBe(1);
        result.Rows.Single(r => r.Class == ZombieCensusClass.Ignored).Pid.ShouldBe(27592);
        result.Rows.Single(r => r.Class == ZombieCensusClass.Ignored).FailedRules
            .ShouldContain(x => x.Contains("operator", StringComparison.OrdinalIgnoreCase));
        result.Rows.Count(r => r.Exe.Equals("codex.exe", StringComparison.OrdinalIgnoreCase)
            && r.IdentityMethod == ZombieIdentityMethod.I1).ShouldBe(6);
        result.Counts.Candidates.ShouldBe(0);
        result.Candidates.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Historical_pool_expiry_is_PoolExpired_I1_candidate_with_server_future_action()
    {
        var host = 10756;
        var pid = 17088;
        var session = Guid.Parse("71bd54b1-0000-4000-8000-000000000001");
        var agent = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000aa");
        var start = new DateTimeOffset(2026, 8, 20, 7, 45, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
        var result = Classify(
            [
                Proc(host, 4, "Antiphon.PtyHost.exe",
                    @"C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe",
                    "Antiphon.PtyHost.exe", @"C:\Antiphon\worktrees\card-task-0ea601b2", start),
                Proc(pid, host, "claude.exe",
                    @"C:\Users\lndco\AppData\Local\claude\claude.exe",
                    "claude.exe", @"C:\Antiphon\worktrees\card-task-0ea601b2", start, 500_000_000, 92.5)
            ],
            [Runner(session, pid, host, start)],
            new ZombieCensusDbSnapshot(
                [new ZombieCensusSessionRow(session, SessionStatus.Running, start, null,
                    @"C:\Antiphon\worktrees\card-task-0ea601b2", AgentKind.ClaudeCode, null)],
                [new ZombieCensusAgentRow(agent, "task-0ea601b2", "task-0ea601b2", true,
                    AgentStatus.Running, session, @"C:\Antiphon\worktrees\card-task-0ea601b2")],
                [new ZombieCensusTaskRow(Guid.Parse("0ea601b2-0000-4000-8000-000000000001"), agent,
                    session, AgentTaskStatus.Succeeded,
                    new DateTimeOffset(2026, 8, 20, 7, 55, 22, TimeSpan.Zero), WorkspaceMode.Worktree,
                    @"C:\Antiphon\worktrees\card-task-0ea601b2",
                    @"C:\Antiphon\worktrees\card-task-0ea601b2")]),
            now: now);

        var row = result.Candidates.ShouldHaveSingleItem();
        row.Class.ShouldBe(ZombieCensusClass.PoolExpired);
        row.IdentityMethod.ShouldBe(ZombieIdentityMethod.I1);
        row.Pid.ShouldBe(pid);
        row.FutureAction.ShouldBe(ZombieFutureAction.ServerSessionKill);
        row.RunnerClaimed.ShouldBeTrue();
        result.Counts.Candidates.ShouldBe(1);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Warm_pooled_task_20_minutes_old_fails_age_and_is_not_a_candidate()
    {
        var session = Guid.Parse("a503916a-0000-4000-8000-000000000001");
        var agent = Guid.Parse("a503916a-0000-4000-8000-0000000000aa");
        var start = new DateTimeOffset(2026, 8, 28, 22, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 8, 28, 22, 37, 0, TimeSpan.Zero);
        var result = Classify(
            [
                Proc(9001, 4, "Antiphon.PtyHost.exe",
                    @"C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe",
                    "Antiphon.PtyHost.exe", @"C:\src\Antiphon", start),
                Proc(9002, 9001, "claude.exe",
                    @"C:\Users\lndco\AppData\Local\claude\claude.exe",
                    "claude.exe", @"C:\src\Antiphon", start, 400_000_000, 1.0)
            ],
            [Runner(session, 9002, 9001, start)],
            new ZombieCensusDbSnapshot(
                [new ZombieCensusSessionRow(session, SessionStatus.Running, start, null,
                    @"C:\src\Antiphon", AgentKind.ClaudeCode, null)],
                [new ZombieCensusAgentRow(agent, "task-a503916a", "task-a503916a", true,
                    AgentStatus.Idle, session, @"C:\src\Antiphon")],
                [new ZombieCensusTaskRow(Guid.Parse("a503916a-0000-4000-8000-000000000002"), agent,
                    session, AgentTaskStatus.Succeeded,
                    new DateTimeOffset(2026, 8, 28, 22, 17, 0, TimeSpan.Zero), WorkspaceMode.Shared,
                    @"C:\src\Antiphon", null)]),
            now: now);

        result.Candidates.ShouldBeEmpty();
        var row = result.Rows.Single(r => r.Pid == 9002);
        row.FailedRules.ShouldContain(r => r.Contains("Z4") || r.Contains("Z6"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Failed_runner_claimed_session_is_ReconcilerOwned_not_a_candidate()
    {
        var session = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
        var start = new DateTimeOffset(2026, 8, 20, 7, 0, 0, TimeSpan.Zero);
        var result = Classify(
            [
                Proc(8001, 4, "Antiphon.PtyHost.exe",
                    @"C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe",
                    "Antiphon.PtyHost.exe", @"C:\src\Antiphon", start),
                Proc(8002, 8001, "claude.exe",
                    @"C:\Users\lndco\AppData\Local\claude\claude.exe",
                    "claude.exe", @"C:\src\Antiphon", start, 400_000_000, 1.0)
            ],
            [Runner(session, 8002, 8001, start)],
            new ZombieCensusDbSnapshot(
                [new ZombieCensusSessionRow(session, SessionStatus.Failed, start,
                    new DateTimeOffset(2026, 8, 20, 8, 0, 0, TimeSpan.Zero),
                    @"C:\src\Antiphon", AgentKind.ClaudeCode, null)],
                [],
                []));

        var row = result.Rows.Single(r => r.Pid == 8002);
        row.Class.ShouldBe(ZombieCensusClass.ReconcilerOwned);
        row.IsCandidate.ShouldBeFalse();
        row.FailedRules.ShouldContain(r => r.Contains("re-adopts Failed", StringComparison.Ordinal));
        result.Candidates.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Unclaimed_terminal_recent_activity_fails_Z5_old_activity_is_EndedButAlive_observation()
    {
        var session = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
        var start = new DateTimeOffset(2026, 8, 20, 7, 0, 0, TimeSpan.Zero);
        var ended = new DateTimeOffset(2026, 8, 20, 8, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
        var processes = new[]
        {
            Proc(7001, 4, "Antiphon.PtyHost.exe",
                @"C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe",
                "Antiphon.PtyHost.exe", @"C:\src\Antiphon", start),
            Proc(7002, 7001, "claude.exe",
                @"C:\Users\lndco\AppData\Local\claude\claude.exe",
                "claude.exe --session-id cccccccc-0000-4000-8000-000000000001",
                @"C:\src\Antiphon", start, 400_000_000, 1.0)
        };
        var manifests = new Dictionary<int, Guid> { [7001] = session };

        var recent = Classify(processes, [], new ZombieCensusDbSnapshot(
            [new ZombieCensusSessionRow(session, SessionStatus.Stopped, start, ended,
                @"C:\src\Antiphon", AgentKind.ClaudeCode,
                new DateTimeOffset(2026, 8, 28, 11, 50, 0, TimeSpan.Zero))],
            [], []), manifests, now);
        var recentRow = recent.Rows.Single(r => r.Pid == 7002);
        recentRow.FailedRules.ShouldContain(r => r.Contains("Z5"));
        recent.Candidates.ShouldBeEmpty();
        recentRow.IdentityMethod.ShouldBe(ZombieIdentityMethod.I2);

        var quiet = Classify(processes, [], new ZombieCensusDbSnapshot(
            [new ZombieCensusSessionRow(session, SessionStatus.Stopped, start, ended,
                @"C:\src\Antiphon", AgentKind.ClaudeCode,
                new DateTimeOffset(2026, 8, 28, 4, 0, 0, TimeSpan.Zero))],
            [], []), manifests, now);
        var quietRow = quiet.Candidates.ShouldHaveSingleItem();
        quietRow.Class.ShouldBe(ZombieCensusClass.EndedButAlive);
        quietRow.IdentityMethod.ShouldBe(ZombieIdentityMethod.I2);
        quietRow.FutureAction.ShouldBe(ZombieFutureAction.ProcessTreeKill);
        quietRow.TreeKillPid.ShouldBe(7001);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Pid_created_before_session_start_fails_Z3_and_is_not_a_candidate()
    {
        var session = Guid.Parse("dddddddd-0000-4000-8000-000000000001");
        var agent = Guid.Parse("dddddddd-0000-4000-8000-0000000000aa");
        var procStart = new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero);
        var sessionStart = new DateTimeOffset(2026, 8, 20, 7, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
        var result = Classify(
            [
                Proc(6001, 4, "Antiphon.PtyHost.exe",
                    @"C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe",
                    "Antiphon.PtyHost.exe", @"C:\src\Antiphon", procStart),
                Proc(6002, 6001, "claude.exe",
                    @"C:\Users\lndco\AppData\Local\claude\claude.exe",
                    "claude.exe", @"C:\src\Antiphon", procStart, 400_000_000, 1.0)
            ],
            [Runner(session, 6002, 6001, sessionStart)],
            new ZombieCensusDbSnapshot(
                [new ZombieCensusSessionRow(session, SessionStatus.Running, sessionStart, null,
                    @"C:\src\Antiphon", AgentKind.ClaudeCode, null)],
                [new ZombieCensusAgentRow(agent, "stale", "stale", true, AgentStatus.Running,
                    session, @"C:\src\Antiphon")],
                [new ZombieCensusTaskRow(Guid.Parse("dddddddd-0000-4000-8000-0000000000bb"), agent,
                    session, AgentTaskStatus.Succeeded,
                    new DateTimeOffset(2026, 8, 20, 8, 0, 0, TimeSpan.Zero), WorkspaceMode.Shared,
                    @"C:\src\Antiphon", null)]),
            now: now);

        var row = result.Rows.Single(r => r.Pid == 6002);
        row.FailedRules.ShouldContain(r => r.Contains("Z3"));
        result.Candidates.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Service_runner_or_census_failure_is_thrown_and_kill_is_never_called()
    {
        await using var world = await World.CreateAsync();
        world.Census.Exception = new InvalidOperationException("wmi down");
        var wmi = await Should.ThrowAsync<InvalidOperationException>(() => world.RunAsync());
        wmi.Message.ShouldContain("OS process list");
        world.Runner.KillCalls.ShouldBe(0);

        world.Census.Exception = null;
        world.Runner.ListException = new HttpRequestException("runner down");
        var runner = await Should.ThrowAsync<InvalidOperationException>(() => world.RunAsync());
        runner.Message.ShouldContain("session-runner");
        world.Runner.KillCalls.ShouldBe(0);
    }

    [Test]
    public async Task Service_db_projection_failure_is_thrown_with_no_mutation()
    {
        await using var world = await World.CreateAsync();
        await world.DisposeContextAsync();
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => world.RunAsync());
        ex.Message.ShouldContain("AgentSessions");
        world.Runner.KillCalls.ShouldBe(0);
    }

    [Test]
    public async Task Isolated_schema_pool_expiry_is_a_candidate_and_does_not_call_kill()
    {
        await using var world = await World.CreateAsync();
        var sessionId = Guid.Parse("71bd54b1-0000-4000-8000-000000000001");
        var agentId = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000aa");
        var start = new DateTimeOffset(2026, 8, 20, 7, 45, 0, TimeSpan.Zero);
        await world.SeedSessionAsync(sessionId, SessionStatus.Running, start);
        await world.SeedAgentAsync(agentId, "task-0ea601b2", sessionId, isPool: true);
        await world.SeedTaskAsync(agentId, sessionId, AgentTaskStatus.Succeeded,
            new DateTimeOffset(2026, 8, 20, 7, 55, 22, TimeSpan.Zero));
        world.Census.Processes =
        [
            Proc(10756, 4, "Antiphon.PtyHost.exe",
                @"C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe",
                "Antiphon.PtyHost.exe", @"C:\Antiphon\worktrees\card-task-0ea601b2", start),
            Proc(17088, 10756, "claude.exe",
                @"C:\Users\lndco\AppData\Local\claude\claude.exe",
                "claude.exe", @"C:\Antiphon\worktrees\card-task-0ea601b2", start, 500_000_000, 92.5)
        ];
        world.Runner.Sessions = [Runner(sessionId, 17088, 10756, start)];
        world.Clock.SetUtcNow(new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero));

        var result = await world.RunAsync();
        var row = result.Candidates.ShouldHaveSingleItem();
        row.SessionId.ShouldBe(sessionId);
        row.Class.ShouldBe(ZombieCensusClass.PoolExpired);
        world.Runner.KillCalls.ShouldBe(0);
        world.Runner.ListCalls.ShouldBe(1);
        world.Census.Calls.ShouldBe(1);
    }

    [Test]
    public async Task Job_logs_a_candidate_as_Warning_and_does_not_fail_the_Hangfire_run()
    {
        await using var world = await World.CreateAsync();
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 8, 20, 7, 45, 0, TimeSpan.Zero);
        await world.SeedSessionAsync(sessionId, SessionStatus.Running, start);
        await world.SeedAgentAsync(agentId, "pool-expired", sessionId, isPool: true);
        await world.SeedTaskAsync(agentId, sessionId, AgentTaskStatus.Succeeded, start.AddMinutes(10));
        world.Census.Processes =
        [
            Proc(11, 4, "Antiphon.PtyHost.exe",
                @"C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe",
                "Antiphon.PtyHost.exe", @"C:\src\Antiphon", start),
            Proc(12, 11, "claude.exe",
                @"C:\Users\lndco\AppData\Local\claude\claude.exe",
                "claude.exe", @"C:\src\Antiphon", start, 400_000_000)
        ];
        world.Runner.Sessions = [Runner(sessionId, 12, 11, start)];
        world.Clock.SetUtcNow(new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero));

        var logger = new ListLogger<ZombieCensusJob>();
        var job = new ZombieCensusJob(world.Service, logger);
        var result = await job.ExecuteAsync(CancellationToken.None);
        result.Counts.Candidates.ShouldBe(1);
        logger.Messages.ShouldContain(m => m.Contains("Warning") && m.Contains("candidate"));
        logger.Messages.ShouldContain(m => m.Contains("Information") && m.Contains("completed"));
        world.Runner.KillCalls.ShouldBe(0);
    }

    [Test]
    public async Task Job_prerequisite_failure_is_logged_Error_and_rethrown()
    {
        await using var world = await World.CreateAsync();
        world.Runner.ListException = new HttpRequestException("nope");
        var logger = new ListLogger<ZombieCensusJob>();
        var job = new ZombieCensusJob(world.Service, logger);
        await Should.ThrowAsync<InvalidOperationException>(() => job.ExecuteAsync(CancellationToken.None));
        logger.Messages.ShouldContain(m => m.Contains("Error") && m.Contains("failed"));
        world.Runner.KillCalls.ShouldBe(0);
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private AppDbContext? _db;

        private World(
            IsolatedTestSchema schema,
            AppDbContext db,
            FakeCensus census,
            FakeRunner runner,
            FakeTimeProvider clock,
            MockFileSystem files,
            ZombieCensusService service)
        {
            _schema = schema;
            _db = db;
            Census = census;
            Runner = runner;
            Clock = clock;
            Files = files;
            Service = service;
        }

        public FakeCensus Census { get; }
        public FakeRunner Runner { get; }
        public FakeTimeProvider Clock { get; }
        public MockFileSystem Files { get; }
        public ZombieCensusService Service { get; }

        public static async Task<World> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var census = new FakeCensus();
            var runner = new FakeRunner();
            var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
            var files = new MockFileSystem();
            var settings = Options.Create(new ZombieCensusSettings
            {
                SessionLogPath = @"C:\logs\antiphon\session-runner"
            });
            var service = new ZombieCensusService(census, runner, db, files, clock, settings);
            return new World(schema, db, census, runner, clock, files, service);
        }

        public Task<ZombieCensusResult> RunAsync() => Service.RunAsync(CancellationToken.None);

        public async Task SeedSessionAsync(Guid id, SessionStatus status, DateTimeOffset started)
        {
            _db!.AgentSessions.Add(new AgentSession
            {
                Id = id,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = status,
                Cwd = @"C:\src\Antiphon",
                Cols = 120,
                Rows = 30,
                CreatedAt = started.UtcDateTime,
                StartedAt = started.UtcDateTime,
                LastSeenAt = started.UtcDateTime,
                EndedAt = status is SessionStatus.Stopped or SessionStatus.Failed
                    ? started.UtcDateTime.AddHours(1)
                    : null
            });
            await _db.SaveChangesAsync();
        }

        public async Task SeedAgentAsync(Guid id, string name, Guid sessionId, bool isPool)
        {
            _db!.Agents.Add(new Agent
            {
                Id = id,
                Name = name,
                Slug = name,
                WorkingDirectory = @"C:\src\Antiphon",
                Status = AgentStatus.Running,
                IsPoolDelegate = isPool,
                PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                UpdatedAt = DateTime.UtcNow.AddDays(-10)
            });
            await _db.SaveChangesAsync();
        }

        public async Task SeedTaskAsync(Guid agentId, Guid sessionId, AgentTaskStatus status, DateTimeOffset completed)
        {
            var id = Guid.NewGuid();
            _db!.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "census fixture",
                Goal = "census fixture",
                Role = AgentTaskRole.Code,
                WorkingDirectory = @"C:\src\Antiphon",
                AgentId = agentId,
                AgentSessionId = sessionId,
                Status = status,
                CreatedAt = completed.UtcDateTime.AddMinutes(-20),
                DispatchedAt = completed.UtcDateTime.AddMinutes(-15),
                CompletedAt = completed.UtcDateTime
            });
            await _db.SaveChangesAsync();
        }

        public async Task DisposeContextAsync()
        {
            if (_db is not null)
            {
                await _db.DisposeAsync();
                _db = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_db is not null)
                await _db.DisposeAsync();
            await _schema.DisposeAsync();
        }
    }

    private sealed class FakeCensus : IZombieProcessCensus
    {
        public IReadOnlyList<ZombieOsProcess> Processes { get; set; } = [];
        public Exception? Exception { get; set; }
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ZombieOsProcess>> SnapshotAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Exception is not null)
                throw Exception;
            return Task.FromResult(Processes);
        }
    }

    private sealed class FakeRunner : ISessionRunnerClient
    {
        public IReadOnlyList<SessionRunnerSessionDto> Sessions { get; set; } = [];
        public Exception? ListException { get; set; }
        public int ListCalls { get; private set; }
        public int KillCalls { get; private set; }

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
        {
            ListCalls++;
            if (ListException is not null)
                throw ListException;
            return Task.FromResult(Sessions);
        }

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
        {
            KillCalls++;
            throw new InvalidOperationException("CARD-0298 v1 must not kill.");
        }

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add($"{logLevel}: {formatter(state, exception)}");

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
