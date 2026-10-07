using System.Collections.Concurrent;
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

/// <summary>
/// Pins the inherited boot-stall kill on a real <c>TickAsync</c>. Production boot deadline
/// stays 8 minutes. This is today's behaviour: a Working session can be stopped here even
/// though CARD-0079 is the only automatic stop the session contract allows.
/// </summary>
[Category("Integration")]
public class BootStallWorkingTickCharacterizationTests
{
    [Test]
    public async Task Aged_prompt_only_Working_tick_stops_the_session_and_requeues_once()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        // Cap 0 keeps this tick on the retry's Queued row. Production's cap of 2 would
        // consider that same row for launch before the tick returns.
        await using var world = await BootStallWorld.CreateAsync(
            schema.ConnectionString, minutesAgo: 9, assistantAfterPrompt: false, maxConcurrentTasks: 0);

        var tick = await world.TickAsync();

        tick.SweepFailures.ShouldBe(0, world.Warnings());
        tick.SkippedConcurrency.ShouldBe(1, world.Warnings());
        world.Runner.Kills.ShouldBe(0);
        world.Runner.Starts.ShouldBe(0);
        world.Runner.Releases.ShouldBe(0);
        world.Runner.CompactionStops.ShouldBe(0);
        world.Runner.Inputs.ShouldBe(0);
        world.Stopper.Killed.ShouldBe([world.SessionId]);

        await using var db = world.Read();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Queued);
        task.Attempt.ShouldBe(2);
        task.CompletedAt.ShouldBeNull();
        task.AgentSessionId.ShouldBeNull();
        task.FailureCode.ShouldBe(AgentTaskFailureCode.ProviderUnresponsive);
        task.FailureReason.ShouldNotBeNull();
        task.FailureReason.ShouldContain("Provider never answered the boot prompt");
        task.FailureReason.ShouldContain("killed");
        (await db.AgentSessions.SingleAsync(s => s.Id == world.SessionId)).Status.ShouldBe(SessionStatus.Stopped);
    }

    [Test]
    public async Task Aged_Working_tick_with_an_assistant_row_is_not_stopped()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(
            schema.ConnectionString, minutesAgo: 9, assistantAfterPrompt: true, maxConcurrentTasks: 2);

        var tick = await world.TickAsync();

        tick.SweepFailures.ShouldBe(0, world.Warnings());
        tick.Dispatched.ShouldBe(0);
        tick.SkippedConcurrency.ShouldBe(0);
        world.Stopper.Killed.ShouldBeEmpty();
        world.Runner.Kills.ShouldBe(0);
        world.Runner.Starts.ShouldBe(0);
        world.Runner.Releases.ShouldBe(0);
        world.Runner.CompactionStops.ShouldBe(0);

        await using var db = world.Read();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Working);
        task.Attempt.ShouldBe(1);
        task.FailureCode.ShouldBeNull();
        task.FailureReason.ShouldBeNull();
        task.AgentSessionId.ShouldBe(world.SessionId);
        (await db.AgentSessions.SingleAsync(s => s.Id == world.SessionId)).Status.ShouldBe(SessionStatus.Running);
    }

    [Test]
    public async Task Young_prompt_only_Working_tick_is_not_stopped()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(
            schema.ConnectionString, minutesAgo: 1, assistantAfterPrompt: false, maxConcurrentTasks: 2);

        var tick = await world.TickAsync();

        tick.SweepFailures.ShouldBe(0, world.Warnings());
        tick.Dispatched.ShouldBe(0);
        tick.SkippedConcurrency.ShouldBe(0);
        world.Stopper.Killed.ShouldBeEmpty();
        world.Runner.Kills.ShouldBe(0);
        world.Runner.Starts.ShouldBe(0);
        world.Runner.Releases.ShouldBe(0);
        world.Runner.CompactionStops.ShouldBe(0);

        await using var db = world.Read();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Working);
        task.Attempt.ShouldBe(1);
        task.FailureCode.ShouldBeNull();
        task.AgentSessionId.ShouldBe(world.SessionId);
        (await db.AgentSessions.SingleAsync(s => s.Id == world.SessionId)).Status.ShouldBe(SessionStatus.Running);
    }

    private sealed class BootStallWorld : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly ListLog _log;
        public required ListedInventoryRunner Runner { get; init; }
        public required RecordingSessionStopper Stopper { get; init; }
        public required Guid SessionId { get; init; }
        public required Guid TaskId { get; init; }
        public required string ConnectionString { get; init; }

        private BootStallWorld(ServiceProvider provider, ListLog log)
        {
            _provider = provider;
            _log = log;
        }

        public string Warnings() => string.Join('\n', _log.Lines);

        public AppDbContext Read() => new(TestDbFixture.CreateDbContextOptions(ConnectionString));

        public async Task<AgentTaskDispatcher.TickResult> TickAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            dispatcher.WorkspaceProbeOverride = new StubWorkspaceProgressProbe(
                new WorkspaceProgressArm(Available: true, LastFileChangeAt: null, LastCommitAt: null, SharedCheckout: false));
            return await dispatcher.TickAsync(CancellationToken.None);
        }

        public static async Task<BootStallWorld> CreateAsync(
            string connectionString, int minutesAgo, bool assistantAfterPrompt, int maxConcurrentTasks)
        {
            var dispatched = Pg(DateTime.UtcNow.AddMinutes(-minutesAgo));
            var sessionId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var cwd = Path.GetTempPath();
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString)))
            {
                var name = $"boot-{agentId:N}"[..16];
                db.Agents.Add(new Agent
                {
                    Id = agentId,
                    Name = name,
                    Slug = name,
                    WorkingDirectory = cwd,
                    Details = "CARD-1149/1150 boot-stall characterization.",
                    Status = AgentStatus.Running,
                    Kind = AgentKind.ClaudeCode,
                    ModelLevel = AgentModelLevel.Frontier,
                    IsPoolDelegate = true,
                    PersistentSessionId = sessionId.ToString("D"),
                    CreatedAt = dispatched,
                    UpdatedAt = dispatched,
                });
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId,
                    DefinitionName = "boot-stall",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running,
                    Cwd = cwd,
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = dispatched,
                    StartedAt = dispatched,
                    LastSeenAt = dispatched,
                });
                db.AgentTasks.Add(new AgentTask
                {
                    Id = taskId,
                    RootTaskId = taskId,
                    Title = "Boot stall characterization",
                    Goal = "Stay on the inherited deadline.",
                    Role = AgentTaskRole.Code,
                    AgentKind = AgentKind.ClaudeCode,
                    ModelLevel = AgentModelLevel.Frontier,
                    Workspace = WorkspaceMode.Shared,
                    WorkingDirectory = cwd,
                    AgentId = agentId,
                    AgentSessionId = sessionId,
                    Status = AgentTaskStatus.Working,
                    Attempt = 1,
                    ReplyTo = AgentTaskReplyTo.None,
                    CreatedAt = dispatched,
                    DispatchedAt = dispatched,
                });
                db.TranscriptEntries.Add(Entry(sessionId, 1, TranscriptKinds.UserPrompt, "the brief", dispatched));
                if (assistantAfterPrompt)
                {
                    db.TranscriptEntries.Add(Entry(
                        sessionId, 2, TranscriptKinds.AssistantText, "working", Pg(dispatched.AddSeconds(30))));
                }

                await db.SaveChangesAsync();
            }

            var runner = new ListedInventoryRunner();
            runner.Sessions.Add(new SessionRunnerSessionDto(
                sessionId, Pid: 4242, StartedAt: dispatched, Status: "Running", ExitCode: null,
                ExitReason: AgentExitReason.Unknown, LastSequence: assistantAfterPrompt ? 2 : 1,
                AcceptedStartedAt: dispatched));
            var stopper = new RecordingSessionStopper { StopsSessionsIn = connectionString };
            var log = new ListLog();
            var settings = new DelegationSettings { MaxConcurrentTasks = maxConcurrentTasks };
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddProvider(log).SetMinimumLevel(LogLevel.Warning));
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(settings));
            services.AddOptions<AgentRegistrySettings>();
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper>(stopper);
            services.AddSingleton<ISessionRunnerClient>(runner);
            // The queue constructor requires the runtime. An empty runner transcript
            // persists nothing, so the seeded UserPrompt stays the boot clock.
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<DeadSessionFirstSeenState>();
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddDelegationWorktreeGraph(new GitSettings
            {
                WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c1149-boot"),
            });
            services.AddScoped<AgentTaskService>();
            services.AddSingleton<AgentTaskReplyService>();
            services.AddSingleton(Options.Create(new DelegateBindRefusalRecoverySettings
            {
                ClaudeProjectsRoot = Path.Combine(Path.GetTempPath(), "antiphon-c1149-boot-no-jsonl"),
            }));
            services.AddSingleton<DelegateBindRefusalRecovery>();
            services.AddScoped<ModelAvailability>();
            services.AddScoped<AgentReviewCheckpointService>();
            services.AddScoped<AgentFilesService>();
            services.AddScoped<AgentTaskDispatcher>();

            var provider = services.BuildServiceProvider();
            return new BootStallWorld(provider, log)
            {
                Runner = runner,
                Stopper = stopper,
                SessionId = sessionId,
                TaskId = taskId,
                ConnectionString = connectionString,
            };
        }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

        private static TranscriptEntry Entry(Guid sessionId, long sequence, string kind, string text, DateTime at) =>
            new()
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = sequence,
                Kind = kind,
                Uuid = $"boot-{Guid.NewGuid():N}",
                Role = kind == TranscriptKinds.UserPrompt ? "user" : "assistant",
                Text = text,
                Timestamp = at,
                CreatedAt = at,
            };

        private static DateTime Pg(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
            return new DateTime(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc);
        }
    }

    private sealed class ListLog : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            Lines.Enqueue($"{logLevel}: {formatter(state, exception)}");
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
