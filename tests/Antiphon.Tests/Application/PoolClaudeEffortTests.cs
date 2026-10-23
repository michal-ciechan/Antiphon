using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class PoolClaudeEffortTests
{
    [Test]
    [Arguments("separate", "high")]
    [Arguments("equals", "xhigh")]
    [Arguments("maximum", "max")]
    [Arguments("repeat", "high")]
    [Arguments("absent", null)]
    [Arguments("missing", null)]
    [Arguments("conflict", null)]
    [Arguments("invalid", null)]
    public void C1491_resolved_argv_stamp(string shape, string? expected)
    {
        string[] argv = shape switch
        {
            "separate" => ["--effort", "high"],
            "equals" => ["--effort=XhIgH"],
            "maximum" => ["--effort", "max"],
            "repeat" => ["--effort", "high", "--effort=HIGH"],
            "absent" => ["--append-system-prompt", "text --effort high", "--", "--effort", "high"],
            "missing" => ["--effort"],
            "conflict" => ["--effort", "high", "--effort=xhigh"],
            _ => ["--effort", "bogus"],
        };
        var before = argv.ToArray();
        ClaudeLaunchArgs.ReadLaunchEffort(argv).ShouldBe(expected);
        argv.ShouldBe(before);
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task C1491_dispatch_launch_persists_effort(bool wedge)
    {
        await using var h = await Harness.CreateAsync();
        h.Settings.ClaudeEffortByRole["Code"] = "xhigh";
        var seed = await h.SeedAsync(wedge ? "high" : null, wedge: wedge);
        await h.LaunchAsync(seed, wedge);
        var first = h.Sink.Launches.ShouldHaveSingleItem();
        AssertClaudeEffort(first.Spec, "xhigh");
        first.Stamp.ShouldBe("xhigh", "the database stamp must exist before enqueue");
        (await h.AgentAsync(seed.AgentId)).PoolClaudeEffort.ShouldBe("xhigh");
        first.SessionId.ShouldNotBe(seed.SessionId);

        // Feed the actual writer's persisted row into the consumer, without repairing its stamp.
        await using (var db = h.Db())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seed.TaskId);
            task.Status = AgentTaskStatus.Succeeded;
            var agent = await db.Agents.SingleAsync(a => a.Id == seed.AgentId);
            agent.Status = AgentStatus.Idle;
            agent.PoolIdleSince = DateTime.UtcNow.AddMinutes(-5);
            (await db.AgentSessions.SingleAsync(s => s.Id == first.SessionId)).Status = SessionStatus.Running;
            await db.SaveChangesAsync();
        }
        var review = await h.TaskAsync(AgentTaskRole.Review);
        (await h.ReuseAsync(review)).ShouldBe(AgentTaskDispatcher.ReuseOutcome.SpawnFresh);
        review.AgentSessionId.ShouldBeNull();
        h.Settings.ClaudeEffortByRole.Clear();
        await using (var db = h.Db())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seed.TaskId);
            task.Status = AgentTaskStatus.Dispatched;
            await db.SaveChangesAsync();
        }
        await h.RelaunchAsync(seed.TaskId, first.SessionId);
        h.Sink.Launches.Count.ShouldBe(2);
        var second = h.Sink.Launches[^1];
        second.SessionId.ShouldNotBe(first.SessionId);
        AssertClaudeEffort(second.Spec, "high");
        second.Stamp.ShouldBe("high");
        (await h.AgentAsync(seed.AgentId)).PoolClaudeEffort.ShouldBe("high");
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task C1491_review_rejects_overridden_process(bool pinned)
    {
        await using var h = await Harness.CreateAsync();
        var seed = await h.SeedAsync("xhigh", settled: true);
        var task = await h.TaskAsync(AgentTaskRole.Review, pinned ? seed.AgentId : null);
        (await h.ReuseAsync(task)).ShouldBe(AgentTaskDispatcher.ReuseOutcome.SpawnFresh);
        task.AgentSessionId.ShouldBeNull();
        await h.AssertUnchangedAsync(seed, "xhigh");
    }

    [Test]
    [Arguments(false, "high")] [Arguments(true, "high")]
    [Arguments(false, "xhigh")] [Arguments(true, "xhigh")]
    public async Task C1491_matching_effort_reuses(bool pinned, string effort)
    {
        await using var h = await Harness.CreateAsync();
        if (effort == "xhigh") h.Settings.ClaudeEffortByRole["Review"] = "xhigh";
        var seed = await h.SeedAsync(effort, settled: true); // Previous role was Code.
        var task = await h.TaskAsync(AgentTaskRole.Review, pinned ? seed.AgentId : null);
        if (pinned && effort == "xhigh")
        {
            await h.TickAsync();
            await using var db = h.Db();
            task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched);
        }
        else (await h.ReuseAsync(task)).ShouldBe(AgentTaskDispatcher.ReuseOutcome.Reused);
        task.AgentId.ShouldBe(seed.AgentId);
        task.AgentSessionId.ShouldBe(seed.SessionId);
        h.Sink.Launches.ShouldBeEmpty("matching pinned effort needs no second process");
        (await h.AgentAsync(seed.AgentId)).PoolClaudeEffort.ShouldBe(effort);
    }

    [Test]
    [Arguments(false, null)] [Arguments(true, null)]
    [Arguments(false, "bogus")] [Arguments(true, "bogus")]
    public async Task C1491_unknown_effort_declines(bool pinned, string? stamp)
    {
        await using var h = await Harness.CreateAsync();
        var seed = await h.SeedAsync(stamp, settled: true);
        var task = await h.TaskAsync(AgentTaskRole.Review, pinned ? seed.AgentId : null);
        (await h.ReuseAsync(task)).ShouldBe(AgentTaskDispatcher.ReuseOutcome.SpawnFresh);
        task.AgentSessionId.ShouldBeNull();
        await h.AssertUnchangedAsync(seed, stamp);
    }

    [Test]
    public async Task C1491_search_selects_matching_candidate()
    {
        await using var h = await Harness.CreateAsync();
        var older = await h.SeedAsync("high", settled: true, idleMinutes: 10);
        var newer = await h.SeedAsync("xhigh", settled: true, idleMinutes: 5);
        var task = await h.TaskAsync(AgentTaskRole.Review);
        (await h.ReuseAsync(task)).ShouldBe(AgentTaskDispatcher.ReuseOutcome.Reused);
        task.AgentId.ShouldBe(older.AgentId);
        task.AgentSessionId.ShouldBe(older.SessionId);
        await h.AssertUnchangedAsync(newer, "xhigh");
    }

    [Test]
    [Arguments(AgentKind.Codex)] [Arguments(AgentKind.Grok)]
    public async Task C1491_non_claude_ignores_stamp(AgentKind kind)
    {
        await using var h = await Harness.CreateAsync();
        h.Settings.ClaudeEffortByRole["Code"] = "xhigh";
        var seed = await h.SeedAsync(kind == AgentKind.Codex ? null : "xhigh", kind, settled: true);
        var task = await h.TaskAsync(AgentTaskRole.Code, kind: kind);
        (await h.ReuseAsync(task)).ShouldBe(AgentTaskDispatcher.ReuseOutcome.Reused);
        task.AgentId.ShouldBe(seed.AgentId);
        task.AgentSessionId.ShouldBe(seed.SessionId);
    }

    [Test]
    [Arguments(AgentKind.Codex)] [Arguments(AgentKind.Grok)]
    public async Task C1491_non_claude_launch_clears_stamp(AgentKind kind)
    {
        await using var h = await Harness.CreateAsync();
        var seed = await h.SeedAsync("xhigh", kind, wedge: true);
        await h.LaunchAsync(seed, true);
        var launch = h.Sink.Launches.ShouldHaveSingleItem();
        launch.Spec.Kind.ShouldBe(kind);
        launch.Spec.Args.ShouldNotContain("--effort");
        if (kind == AgentKind.Codex) launch.Spec.Args.ShouldContain("model_reasoning_effort=high");
        else
        {
            var args = launch.Spec.Args.ToList();
            args[args.IndexOf("--reasoning-effort") + 1].ShouldBe("high");
        }
        launch.Stamp.ShouldBeNull();
        (await h.AgentAsync(seed.AgentId)).PoolClaudeEffort.ShouldBeNull();
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task C1491_interrupted_launch_keeps_unknown(bool wedge)
    {
        await using var h = await Harness.CreateAsync();
        // Deliberately bypass startup validation, failing spec construction after the claim commits.
        h.Settings.ClaudeEffortByRole["Code"] = "bogus";
        var seed = await h.SeedAsync("xhigh", wedge: wedge);
        await h.LaunchAsync(seed, wedge);
        h.Errors.Errors.ShouldContain(e => e.ToString().Contains("Delegation:ClaudeEffortByRole:Code"));
        h.Sink.Launches.ShouldBeEmpty();
        await using var db = h.Db();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == seed.TaskId);
        task.AgentSessionId.ShouldNotBe(seed.SessionId, "the replacement claim committed before spec failure");
        (await h.AgentAsync(seed.AgentId)).PoolClaudeEffort.ShouldBeNull();
    }

    private static void AssertClaudeEffort(AgentLaunchSpec spec, string expected)
    {
        var args = spec.Args.ToList();
        args.ShouldContain("--effort");
        args[args.IndexOf("--effort") + 1].ShouldBe(expected);
    }

    private sealed record Seed(Guid AgentId, Guid SessionId, Guid TaskId);
    private sealed record Launch(Guid SessionId, AgentLaunchSpec Spec, string? Stamp);

    private sealed class RecordingSink(string connectionString) : IAgentTaskLaunchSink
    {
        public List<Launch> Launches { get; } = [];
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec)
        {
            using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
            Launches.Add(new(sessionId, spec, db.Agents.AsNoTracking().Single(a => a.Id == agentId).PoolClaudeEffort));
        }
    }

    private sealed class ErrorLog : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<Exception> Errors { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public bool IsEnabled(LogLevel level) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
        { if (ex is not null) Errors.Enqueue(ex); }
        public void Dispose() { }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly string _directory = Directory.CreateTempSubdirectory("antiphon-c1491").FullName;
        private readonly ServiceProvider _provider;
        public readonly ErrorLog Errors = new();
        public readonly RecordingSink Sink;
        public readonly DelegationSettings Settings = new()
        {
            AllowedRoots = ["/", "C:\\"], MaxConcurrentTasks = 512, BootWedgeRelaunchLimit = 5,
            PoolReservedForCallerMinutes = 0, PoolIdleRetireMinutes = 600, PoolMaxIdlePerDirectory = 8,
        };
        private Harness(IsolatedTestSchema schema)
        {
            _schema = schema;
            Sink = new(schema.ConnectionString);
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(Errors));
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(Options.Create(Settings));
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new AgentSessionSettings()));
            services.AddOptions<AgentRegistrySettings>().Configure(s =>
            {
                s.DefaultDefinition = "claude";
                s.GrokCredentialProbeEnabled = false;
                s.Definitions["claude"] = new() { Kind = "ClaudeCode", Exe = "claude" };
                s.Definitions["codex"] = new() { Kind = "Codex", Exe = "codex" };
                s.Definitions["grok"] = new() { Kind = "Grok", Exe = "grok" };
            });
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper { StopsSessionsIn = schema.ConnectionString });
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.Combine(_directory, "worktrees") });
            services.AddSingleton<IAgentTaskLaunchSink>(Sink);
            services.AddScoped<AgentTaskService>();
            services.AddScoped<AgentTaskDispatcher>();
            _provider = services.BuildServiceProvider();
        }
        public static async Task<Harness> CreateAsync() => new(await TestDbFixture.CreateIsolatedSchemaAsync());
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
        public async Task<Agent> AgentAsync(Guid id)
        {
            await using var db = Db();
            return await db.Agents.AsNoTracking().SingleAsync(a => a.Id == id);
        }
        public async Task AssertUnchangedAsync(Seed seed, string? stamp)
        {
            var agent = await AgentAsync(seed.AgentId);
            agent.PoolClaudeEffort.ShouldBe(stamp);
            agent.Status.ShouldBe(AgentStatus.Idle);
            agent.PoolIdleSince.ShouldNotBeNull();
            agent.PersistentSessionId.ShouldBe(seed.SessionId.ToString("D"));
        }
        public async Task<AgentTask> TaskAsync(AgentTaskRole role, Guid? pin = null, AgentKind kind = AgentKind.ClaudeCode)
        {
            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id, RootTaskId = id, Title = "c1491 effort", Goal = "c1491 effort",
                Kind = AgentTaskKind.Worker, Role = role, AgentKind = kind, ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared, WorkingDirectory = _directory, Ephemeral = true,
                Status = AgentTaskStatus.Queued, ReplyTo = AgentTaskReplyTo.None,
                AgentId = pin, CreatedAt = DateTime.UtcNow,
            };
            await using var db = Db();
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }
        public async Task<Seed> SeedAsync(string? stamp, AgentKind kind = AgentKind.ClaudeCode,
            bool wedge = false, bool settled = false, int idleMinutes = 5)
        {
            var now = DateTime.UtcNow;
            var agentId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var task = await TaskAsync(AgentTaskRole.Code, kind: kind);
            await using var db = Db();
            var rules = Encoding.UTF8.GetBytes(InstructionBundleComposer.Compose(
                InstructionBundles.ForDelegate(AgentTaskKind.Worker, AgentTaskRole.Code)).Text);
            var receipt = kind == AgentKind.Grok ? new GrokRulesReceipt(
                $"C:\\runner\\instructions\\grok\\{sessionId:N}\\rules.md", GrokRulesTransport.Hash(rules), rules.Length, 1, Guid.NewGuid()) : null;
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, DefinitionName = kind == AgentKind.ClaudeCode ? "claude" : kind.ToString().ToLowerInvariant(),
                AgentKind = kind, Status = SessionStatus.Running, Cwd = _directory, Cols = 120, Rows = 30,
                CreatedAt = now, StartedAt = now, LastSeenAt = now,
                GrokRulesReceiptJson = receipt is null ? null : JsonSerializer.Serialize(receipt),
                GrokRulesGeneration = receipt?.Generation, GrokRulesExpectedSha256 = receipt?.Sha256,
                GrokRulesExpectedByteCount = receipt?.ByteCount,
                GrokRulesState = receipt is null ? GrokRulesState.None : GrokRulesState.Ready,
            });
            db.Agents.Add(new Agent
            {
                Id = agentId, Name = $"c1491-{agentId:N}", Slug = $"c1491-{agentId:N}",
                WorkingDirectory = _directory, Kind = kind, ModelLevel = AgentModelLevel.High,
                IsPoolDelegate = true, PoolClaudeEffort = stamp,
                Status = wedge ? AgentStatus.Running : AgentStatus.Idle,
                PoolIdleSince = wedge ? null : now.AddMinutes(-idleMinutes),
                PersistentSessionId = sessionId.ToString("D"), CreatedAt = now, UpdatedAt = now,
            });
            var stored = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            stored.AgentId = agentId;
            stored.AgentSessionId = settled || wedge ? sessionId : null;
            stored.Status = settled ? AgentTaskStatus.Succeeded : wedge ? AgentTaskStatus.Dispatched : AgentTaskStatus.Queued;
            stored.DispatchedAt = settled || wedge ? now : null;
            // Force a replacement even on the pre-fix matcher, independently of its effort guard.
            if (!settled && !wedge) stored.LaunchEnvOverrideJson = "{\"C1491_REPLACE\":\"1\"}";
            await db.SaveChangesAsync();
            return new(agentId, sessionId, task.Id);
        }
        public async Task<AgentTaskDispatcher.ReuseOutcome> ReuseAsync(AgentTask task)
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .TryReuseWarmAgentAsync(task, DateTime.UtcNow, CancellationToken.None);
        }
        public async Task TickAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
        }
        public Task LaunchAsync(Seed seed, bool wedge) => wedge ? RelaunchAsync(seed.TaskId, seed.SessionId) : TickAsync();
        public async Task RelaunchAsync(Guid taskId, Guid sessionId)
        {
            await using var scope = _provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().RelaunchWedgedAsync(taskId, sessionId, CancellationToken.None);
        }
        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _schema.DisposeAsync();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
