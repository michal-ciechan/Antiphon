using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1156: one taskless AlwaysOn session on its own isolated PostgreSQL database, watched by
/// the real <see cref="BootReplyWatchdogService"/> sweep. A <see cref="FakeTimeProvider"/> pinned at
/// <see cref="Now0"/> (this graph registers no message queue, so the CARD-0222 frozen-clock hazard
/// does not apply; CARD-1165's witness attaches a <see cref="BridgeQueueHarness"/> on the system
/// clock instead and reads the projection at fake times), a <see cref="ListedInventoryRunner"/> listing the session Running at its
/// accepted generation, the real <see cref="AgentSessionRuntime"/> for the transcript pull, a
/// <see cref="RecordingSessionStopper"/> that would close the row if anything asked it to, a
/// <see cref="MockEventBus"/> and a capturing logger. Age is arranged by back-dating the prompt
/// against the fake clock; production deadlines (8 and 20 minutes) stay in force.
/// </summary>
internal sealed class StandingBootWatchFixture : IAsyncDisposable
{
    /// <summary>Present in every seeded prompt. No receipt or log line may ever carry it.</summary>
    public const string PromptCanary = "c1156-canary-4b9d";

    private readonly IsolatedTestSchema? _schema;
    private readonly ServiceProvider _provider;
    private readonly CapturingLog _log;

    private StandingBootWatchFixture(IsolatedTestSchema? schema, ServiceProvider provider, CapturingLog log)
    {
        _schema = schema;
        _provider = provider;
        _log = log;
    }

    public required string ConnectionString { get; init; }
    public required StandingBootWatchOptions Setup { get; init; }
    public required FakeTimeProvider Clock { get; init; }
    public required ListedInventoryRunner Runner { get; init; }
    public required RecordingSessionStopper Stopper { get; init; }
    public required MockEventBus Events { get; init; }
    public required Guid SessionId { get; init; }
    public required Guid AgentId { get; init; }
    public required DateTime Now0 { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime PromptAt { get; init; }

    /// <summary>Interceptors on the receipt writer's own context only (V-6, V-7 seams).</summary>
    public IInterceptor[] WriterInterceptors { get; set; } = [];

    public static async Task<StandingBootWatchFixture> CreateAsync(StandingBootWatchOptions? options = null)
    {
        options ??= new StandingBootWatchOptions();
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        try
        {
            var now = Pg(DateTime.UtcNow);
            var clock = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
            var runner = new ListedInventoryRunner();
            var stopper = new RecordingSessionStopper { StopsSessionsIn = schema.ConnectionString };
            var (sessionId, agentId) = (Guid.NewGuid(), Guid.NewGuid());
            var startedAt = Pg(now - options.StartedAge);
            var promptAt = Pg(now - options.PromptAge);
            await SeedAsync(schema.ConnectionString, options, sessionId, agentId, startedAt, promptAt);
            if (options.Listed)
            {
                runner.Sessions.Add(new SessionRunnerSessionDto(
                    sessionId, Pid: 4242, StartedAt: startedAt, Status: "Running", ExitCode: null,
                    ExitReason: AgentExitReason.Unknown, LastSequence: 1, AcceptedStartedAt: startedAt));
            }

            var fixture = Build(schema, schema.ConnectionString, options, clock, runner, stopper,
                sessionId, agentId, now, startedAt, promptAt);
            if (options.Arm)
                await fixture.ArmAsync();
            return fixture;
        }
        catch
        {
            await schema.DisposeAsync();
            throw;
        }
    }

    /// <summary>A second provider over the same database, runner and stopper: a restart.</summary>
    public StandingBootWatchFixture Recreate() =>
        Build(null, ConnectionString, Setup, Clock, Runner, Stopper, SessionId, AgentId, Now0, StartedAt, PromptAt);

    private static StandingBootWatchFixture Build(
        IsolatedTestSchema? schema, string connectionString, StandingBootWatchOptions options,
        FakeTimeProvider clock, ListedInventoryRunner runner, RecordingSessionStopper stopper,
        Guid sessionId, Guid agentId, DateTime now0, DateTime startedAt, DateTime promptAt)
    {
        var log = new CapturingLog(options.MinimumLogLevel);
        var events = options.EventBus ?? new MockEventBus();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(log).SetMinimumLevel(options.MinimumLogLevel));
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(connectionString);
            if (options.Interceptors.Length > 0)
                o.AddInterceptors(options.Interceptors);
        });
        services.AddSingleton<IEventBus>(events);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<ISessionRunnerClient>(runner);
        services.AddSingleton<AgentSessionRuntime>();
        var provider = services.BuildServiceProvider();
        return new StandingBootWatchFixture(schema, provider, log)
        {
            ConnectionString = connectionString,
            Setup = options,
            Clock = clock,
            Runner = runner,
            Stopper = stopper,
            Events = events,
            SessionId = sessionId,
            AgentId = agentId,
            Now0 = now0,
            StartedAt = startedAt,
            PromptAt = promptAt,
        };
    }

    public DelegationSettings Settings { get; } = new();

    /// <summary>The real sweep, with or without the runtime, on this fixture's provider.</summary>
    public BootReplyWatchdogService Sweep(bool runtime = true)
    {
        var sweep = new BootReplyWatchdogService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(Settings),
            Clock,
            _provider.GetRequiredService<ILogger<BootReplyWatchdogService>>(),
            runtime: runtime ? _provider.GetRequiredService<AgentSessionRuntime>() : null,
            events: Events);
        if (WriterInterceptors.Length > 0)
        {
            var interceptors = WriterInterceptors;
            sweep.WriterContextFactory = () =>
                new AppDbContext(TestDbFixture.CreateDbContextOptions(ConnectionString, interceptors));
        }

        return sweep;
    }

    public Task<int> SweepAsync(bool runtime = true, CancellationToken ct = default) =>
        Sweep(runtime).SweepAsync(ct);

    public void At(DateTime utc) => Clock.SetUtcNow(new DateTimeOffset(utc, TimeSpan.Zero));

    public AppDbContext Read() => new(TestDbFixture.CreateDbContextOptions(ConnectionString));

    /// <summary>A logger on this fixture's captured log (<see cref="LogEntries"/>).</summary>
    public ILogger<T> Logger<T>() => _provider.GetRequiredService<ILogger<T>>();

    public async Task ArmAsync()
    {
        await using var db = Read();
        (await BootReplyWatch.TryArmAsync(db, SessionId, Settings.BootModelWaitDeadlineMinutes, CancellationToken.None))
            .ShouldNotBeNull("the fixture's prompt arms the session watch");
        await db.SaveChangesAsync();
    }

    /// <summary>Every LivenessProbeFailed incident on the session, oldest first.</summary>
    public async Task<List<AgentIncident>> IncidentsAsync()
    {
        await using var db = Read();
        return await db.AgentIncidents.AsNoTracking()
            .Where(i => i.SessionId == SessionId && i.Kind == AgentIncidentKind.LivenessProbeFailed)
            .OrderBy(i => i.CreatedAt).ThenBy(i => i.FailureReason)
            .ToListAsync();
    }

    /// <summary>The standing receipts (<c>standingBoot:v1;</c>) on the session, oldest first.</summary>
    public async Task<List<AgentIncident>> ReceiptsAsync() =>
        (await IncidentsAsync())
            .Where(i => i.FailureReason?.StartsWith(StandingBootWatchPolicy.KeyPrefix, StringComparison.Ordinal) == true)
            .ToList();

    public async Task<AgentSession> SessionAsync(Guid? id = null)
    {
        var sessionId = id ?? SessionId;
        await using var db = Read();
        return await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
    }

    /// <summary>Working, through the production transcript query (no fixture-supplied bool).</summary>
    public async Task<bool> WorkingAsync()
    {
        await using var db = Read();
        return await SessionMessageQueueService.IsWorkingAsync(db, SessionId, CancellationToken.None);
    }

    public async Task AddEntryAsync(string kind, string? text, DateTime at)
    {
        await using var db = Read();
        var next = (await db.TranscriptEntries.Where(t => t.AgentSessionId == SessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
        db.TranscriptEntries.Add(Entry(SessionId, next, kind, text, Pg(at)));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Everything the sweep must leave alone (D-7): the session's lifecycle and identity columns,
    /// the agent's pointer, every supervision column (or its absence), the queue rows with their
    /// body hashes, park/release/hold rows, restart incidents and the prompt-row count. Watch
    /// columns are excluded: re-arming is the sweep's own bookkeeping.
    /// </summary>
    public async Task<CustodySnapshot> SnapshotAsync()
    {
        await using var db = Read();
        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == SessionId);
        var agent = await db.Agents.AsNoTracking().SingleAsync(a => a.Id == AgentId);
        var state = await db.AgentSupervisionStates.AsNoTracking().SingleOrDefaultAsync(s => s.AgentId == AgentId);
        var queue = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == SessionId)
            .OrderBy(m => m.Sequence)
            .ToListAsync();
        // Every scalar of every queue row (status, attempts, delivery baseline, spill fields), with
        // the body bytes reduced to their hash.
        var queueText = EntityScalarSnapshot.Of(db, queue) + "\n"
            + string.Join('\n', queue.Select(m => $"{m.Id}|{Sha(m.Body)}|{Sha(m.RemoteSpillBody)}"));
        return new CustodySnapshot(
            $"{session.Status}|{session.TerminationSource}|{session.EndedAt:o}|{session.StartedAt:o}|"
            + $"{session.LaunchResumedAt:o}|{session.StandingAgentId}|{session.CardId}|{session.RestartFailureKind}",
            $"{agent.PersistentSessionId}|{agent.AlwaysOn}|{agent.Status}",
            state is null ? "absent" : EntityScalarSnapshot.Of(db, state),
            queue.Count,
            queueText,
            await db.AgentTaskParks.CountAsync(),
            await db.RunnerSeatReleases.CountAsync(),
            await db.ModelAvailabilityHolds.CountAsync(),
            await db.AgentIncidents.CountAsync(i => i.AgentId == AgentId && i.Kind == AgentIncidentKind.RestartScheduled),
            await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == SessionId
                && (t.Kind == TranscriptKinds.UserPrompt || t.Kind == TranscriptKinds.QueuedUserPrompt)));
    }

    /// <summary>The fail-closed custody assertions every CARD-1156 sweep test makes.</summary>
    public void AssertNothingDestructive()
    {
        Stopper.Killed.ShouldBeEmpty("no boot evidence ever requests a stop");
        Runner.Kills.ShouldBe(0, "no runner kill");
        Runner.Starts.ShouldBe(0, "no runner start");
        Runner.Releases.ShouldBe(0, "no seat release");
        Runner.CompactionStops.ShouldBe(0, "no conditional compaction stop");
        Runner.Inputs.ShouldBe(0, "nothing is typed into the session");
    }

    public string Warnings() => string.Join('\n', _log.Entries
        .Where(e => e.Level >= LogLevel.Warning)
        .Select(e => $"{e.Level}: {e.Message}{(e.Exception is null ? "" : $" [{e.Exception.GetType().Name}: {e.Exception.Message}]")}"));

    public IReadOnlyList<StandingBootLogEntry> LogEntries() => _log.Entries.ToArray();

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        if (_schema is not null)
            await _schema.DisposeAsync();
    }

    private static async Task SeedAsync(
        string connectionString, StandingBootWatchOptions options,
        Guid sessionId, Guid agentId, DateTime startedAt, DateTime promptAt)
    {
        var cwd = Path.GetTempPath();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        var name = $"sbw-{agentId:N}"[..16];
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = name,
            Slug = name,
            WorkingDirectory = cwd,
            Details = "CARD-1156 standing boot watch fixture.",
            Status = AgentStatus.Running,
            Kind = options.Kind,
            ModelLevel = AgentModelLevel.Frontier,
            AlwaysOn = options.AlwaysOn,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = startedAt,
            UpdatedAt = startedAt,
        });
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "standing-boot",
            AgentKind = options.Kind,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = startedAt,
            StartedAt = startedAt,
            LastSeenAt = startedAt,
            StandingAgentId = agentId,
            LaunchResumedAt = options.ResumedAfterStart is { } resumed ? Pg(startedAt + resumed) : null,
            GrokRulesState = options.Kind == AgentKind.Grok ? GrokRulesState.Ready : GrokRulesState.None,
        });
        if (options.LegacyState)
        {
            var now = startedAt.AddHours(6);
            db.AgentSupervisionStates.Add(new AgentSupervisionState
            {
                AgentId = agentId,
                ConsecutiveFailures = 2,
                LivenessLatchedAt = Pg(now.AddDays(-1)),
                NextRestartAt = Pg(now.AddHours(1)),
                RestartBackoffFailures = 3,
                LastEscalationTier = 1,
                HerdrConsecutiveFailures = 1,
                LastHealthyAt = Pg(now.AddHours(-2)),
                UpdatedAt = Pg(now.AddHours(-2)),
            });
        }

        if (options.TerminalTaskHistory)
        {
            foreach (var status in new[] { AgentTaskStatus.Succeeded, AgentTaskStatus.Failed })
            {
                var id = Guid.NewGuid();
                db.AgentTasks.Add(new AgentTask
                {
                    Id = id,
                    RootTaskId = id,
                    Title = $"finished {status}",
                    Goal = "terminal history only",
                    Role = AgentTaskRole.Code,
                    AgentKind = options.Kind,
                    ModelLevel = AgentModelLevel.Frontier,
                    Workspace = WorkspaceMode.Shared,
                    WorkingDirectory = cwd,
                    AgentId = agentId,
                    AgentSessionId = sessionId,
                    Status = status,
                    ReplyTo = AgentTaskReplyTo.None,
                    CreatedAt = startedAt,
                    DispatchedAt = startedAt,
                    CompletedAt = startedAt.AddMinutes(30),
                });
            }
        }

        var sequence = 1L;
        if (options.SeedPrompt)
        {
            db.TranscriptEntries.Add(Entry(
                sessionId, sequence++, options.PromptKind, $"the standing brief {PromptCanary}", promptAt));
        }

        if (options.InterruptAfterPrompt)
        {
            db.TranscriptEntries.Add(Entry(
                sessionId, sequence++, TranscriptKinds.UserPrompt,
                $"{TranscriptKinds.InterruptedPromptPrefix} by user]", Pg(promptAt.AddSeconds(20))));
        }

        if (options.QueuedMessage)
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 1,
                Body = $"the standing brief {PromptCanary}",
                Origin = QueuedMessageOrigin.Ui,
                Status = QueuedMessageStatus.Pending,
                CreatedAt = promptAt,
            });
        }

        await db.SaveChangesAsync();
    }

    public static TranscriptEntry Entry(Guid sessionId, long sequence, string kind, string? text, DateTime at) =>
        new()
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = sequence,
            Kind = kind,
            Uuid = $"sbw-{Guid.NewGuid():N}",
            Role = kind is TranscriptKinds.UserPrompt or TranscriptKinds.QueuedUserPrompt ? "user" : "assistant",
            Text = text,
            Timestamp = at,
            CreatedAt = at,
        };

    public static DateTime Pg(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
        return new DateTime(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc);
    }

    private static string Sha(string? text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)));

    private sealed class CapturingLog(LogLevel minimum) : ILoggerProvider
    {
        public ConcurrentQueue<StandingBootLogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Category(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Category(CapturingLog log, string name) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    log.Entries.Enqueue(new StandingBootLogEntry(logLevel, name, eventId, formatter(state, exception), exception));
            }

            private readonly LogLevel minimum = log._minimum;
        }

        private readonly LogLevel _minimum = minimum;
    }
}

internal sealed record StandingBootLogEntry(LogLevel Level, string Category, EventId EventId, string Message, Exception? Exception);

/// <summary>What <see cref="StandingBootWatchFixture.SnapshotAsync"/> compares before and after.</summary>
internal sealed record CustodySnapshot(
    string Session,
    string AgentPointer,
    string Supervision,
    int QueueRows,
    string QueueBodies,
    int Parks,
    int Releases,
    int Holds,
    int RestartIncidents,
    int PromptRows);

internal sealed record StandingBootWatchOptions
{
    public AgentKind Kind { get; init; } = AgentKind.ClaudeCode;
    public TimeSpan PromptAge { get; init; } = TimeSpan.FromMinutes(9);

    /// <summary>How long before <c>Now0</c> the session started (its generation and launch clock).</summary>
    public TimeSpan StartedAge { get; init; } = TimeSpan.FromHours(6);
    public string PromptKind { get; init; } = TranscriptKinds.UserPrompt;

    /// <summary>
    /// CARD-1165: false seeds no prompt row, so a real producer (<c>SessionMessageQueueService</c>
    /// through an attached <see cref="BridgeQueueHarness"/>) writes the session's only prompt records.
    /// </summary>
    public bool SeedPrompt { get; init; } = true;
    public bool InterruptAfterPrompt { get; init; }
    public bool AlwaysOn { get; init; } = true;
    public bool Arm { get; init; } = true;
    public bool Listed { get; init; } = true;

    /// <summary>Seeds <c>LaunchResumedAt = StartedAt + this</c>, so the launch clock is the resume.</summary>
    public TimeSpan? ResumedAfterStart { get; init; }

    /// <summary>Seeds a supervision row with nonzero failures, a latch and nondefault fields.</summary>
    public bool LegacyState { get; init; }

    /// <summary>Seeds a Succeeded and a Failed task bound to the session.</summary>
    public bool TerminalTaskHistory { get; init; }

    /// <summary>Seeds one pending user queue row whose body bytes must survive unchanged.</summary>
    public bool QueuedMessage { get; init; } = true;

    public MockEventBus? EventBus { get; init; }
    public LogLevel MinimumLogLevel { get; init; } = LogLevel.Information;

    /// <summary>Interceptors on the sweep scope's context (the writer's default context inherits them).</summary>
    public IInterceptor[] Interceptors { get; init; } = [];
}
