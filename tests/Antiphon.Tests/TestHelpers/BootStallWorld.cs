using System.Collections.Concurrent;
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

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1151 A-11: the boot-stall service graph shared by
/// <c>BootStallWorkingTickCharacterizationTests</c> and <c>BootStallDetectionTests</c>. One
/// delegate task on one session over an isolated PostgreSQL database, a
/// <see cref="FakeTimeProvider"/> pinned to the seeding instant (so a due time can be hit to the
/// microsecond and the clock stepped either way), a <see cref="ListedInventoryRunner"/>, an
/// optional counting <see cref="ISessionRunnerDirectory"/>, a <see cref="RecordingSessionStopper"/>
/// and a quiet available workspace probe. Production deadlines stay in force; age is arranged by
/// back-dating rows against the fake clock.
/// </summary>
internal sealed class BootStallWorld : IAsyncDisposable
{
    /// <summary>Present in every seeded boot prompt. No event Detail may ever carry it (A-4).</summary>
    public const string PromptCanary = "c1151-canary-7f3e";

    private readonly ServiceProvider _provider;
    private readonly BootStallLog _log;

    public required ListedInventoryRunner Runner { get; init; }
    public required RecordingSessionStopper Stopper { get; init; }
    public required MockEventBus Events { get; init; }
    public required FakeTimeProvider Clock { get; init; }
    public required CountingRunnerDirectory? Directory { get; init; }
    public required Guid SessionId { get; init; }
    public required Guid TaskId { get; init; }
    public required Guid AgentId { get; init; }
    public required string ConnectionString { get; init; }
    public required DateTime Now0 { get; init; }
    public required DateTime PromptAt { get; init; }

    /// <summary>Optional interceptors on the telemetry writer's own context (V-6 faults).</summary>
    public IInterceptor[] TelemetryInterceptors { get; set; } = [];

    /// <summary>Hands each dispatcher a fresh telemetry-context factory when interceptors are set.</summary>
    public bool UseTelemetryFactory { get; set; }

    public IWorkspaceProgressProbe WorkspaceProbe { get; set; } = new StubWorkspaceProgressProbe(
        new WorkspaceProgressArm(Available: true, LastFileChangeAt: null, LastCommitAt: null, SharedCheckout: false));

    public Func<Guid, CancellationToken, Task>? CatchUp { get; set; }

    private BootStallWorld(ServiceProvider provider, BootStallLog log)
    {
        _provider = provider;
        _log = log;
    }

    public string Warnings() => string.Join('\n', _log.Lines);

    public AppDbContext Read() => new(TestDbFixture.CreateDbContextOptions(ConnectionString));

    public async Task<AgentTaskDispatcher.TickResult> TickAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        return await Prepare(scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>())
            .TickAsync(CancellationToken.None);
    }

    /// <summary>The overdue sweep alone, on a fresh scope.</summary>
    public async Task<int> RunOverdueSweepAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        return await Prepare(scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>())
            .FailOverdueTasksAsync(CancellationToken.None);
    }

    /// <summary>A dispatcher on a caller-owned scope (concurrency and recreation cases).</summary>
    public AgentTaskDispatcher Prepare(AgentTaskDispatcher dispatcher)
    {
        dispatcher.WorkspaceProbeOverride = WorkspaceProbe;
        if (CatchUp is not null)
            dispatcher.CatchUpOverride = CatchUp;
        if (UseTelemetryFactory)
        {
            var interceptors = TelemetryInterceptors;
            dispatcher.BootStallContextFactory = () =>
                new AppDbContext(TestDbFixture.CreateDbContextOptions(ConnectionString, interceptors));
        }

        return dispatcher;
    }

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    public required BootStallWorldOptions Setup { get; init; }

    public async Task<List<AgentTaskEvent>> EventsAsync()
    {
        await using var db = Read();
        return await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == TaskId)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .ToListAsync();
    }

    public async Task<List<string>> BootWarningsAsync() =>
        (await EventsAsync())
            .Where(e => e.Type == AgentTaskEventType.Warning && e.Detail.StartsWith("BootStall", StringComparison.Ordinal))
            .Select(e => e.Detail)
            .ToList();

    public async Task<AgentTask> TaskAsync()
    {
        await using var db = Read();
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == TaskId);
    }

    public async Task<AgentSession> SessionAsync()
    {
        await using var db = Read();
        return await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == SessionId);
    }

    public async Task<int> PromptCountAsync()
    {
        await using var db = Read();
        return await db.TranscriptEntries.CountAsync(
            t => t.AgentSessionId == SessionId && t.Kind == TranscriptKinds.UserPrompt);
    }

    public async Task AddEntryAsync(string kind, string? text, DateTime at)
    {
        await using var db = Read();
        var next = (await db.TranscriptEntries.Where(t => t.AgentSessionId == SessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
        db.TranscriptEntries.Add(Entry(SessionId, next, kind, text, Pg(at)));
        await db.SaveChangesAsync();
    }

    public static Task<BootStallWorld> CreateAsync(string connectionString, BootStallWorldOptions options)
    {
        var now = Pg(DateTime.UtcNow);
        var clock = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
        var runner = new ListedInventoryRunner();
        var stopper = new RecordingSessionStopper { StopsSessionsIn = connectionString };
        CountingRunnerDirectory? directory = options.Directory is { } mode ? new(runner, mode) : null;
        return SeedAndBuildAsync(connectionString, options, clock, runner, stopper, directory, now);
    }

    private static async Task<BootStallWorld> SeedAndBuildAsync(
        string connectionString,
        BootStallWorldOptions options,
        FakeTimeProvider clock,
        ListedInventoryRunner runner,
        RecordingSessionStopper stopper,
        CountingRunnerDirectory? directory,
        DateTime now)
    {
        var (sessionId, taskId, agentId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var dispatched = Pg(now.AddMinutes(-options.MinutesAgo));
        var promptAt = Pg(now - (options.PromptAge ?? TimeSpan.FromMinutes(options.MinutesAgo)));
        await SeedAsync(connectionString, options, sessionId, taskId, agentId, dispatched, promptAt);
        if (options.Listing != BootStallListing.None)
        {
            runner.Sessions.Add(new SessionRunnerSessionDto(
                sessionId, Pid: 4242, StartedAt: dispatched,
                Status: options.Listing == BootStallListing.Exited ? "Exited" : "Running",
                ExitCode: null, ExitReason: AgentExitReason.Unknown, LastSequence: 1,
                AcceptedStartedAt: options.Listing == BootStallListing.WrongGeneration
                    ? dispatched.AddMinutes(-30)
                    : dispatched));
        }

        return Build(connectionString, options, clock, runner, stopper, directory, sessionId, taskId, agentId, now, promptAt);
    }

    /// <summary>
    /// A second provider over an already-seeded world: a restart. A different <paramref name="clock"/>
    /// models a process whose wall clock stepped (a fake clock cannot run backwards in place).
    /// </summary>
    public BootStallWorld Recreate(FakeTimeProvider? clock = null)
    {
        var copy = Build(ConnectionString, Setup, clock ?? Clock, Runner, Stopper, Directory,
            SessionId, TaskId, AgentId, Now0, PromptAt);
        copy.WorkspaceProbe = WorkspaceProbe;
        copy.CatchUp = CatchUp;
        copy.UseTelemetryFactory = UseTelemetryFactory;
        copy.TelemetryInterceptors = TelemetryInterceptors;
        return copy;
    }

    private static BootStallWorld Build(
        string connectionString,
        BootStallWorldOptions options,
        FakeTimeProvider clock,
        ListedInventoryRunner runner,
        RecordingSessionStopper stopper,
        CountingRunnerDirectory? directory,
        Guid sessionId, Guid taskId, Guid agentId, DateTime now0, DateTime promptAt)
    {
        var log = new BootStallLog(options.MinimumLogLevel);
        var events = new MockEventBus();
        var settings = new DelegationSettings { MaxConcurrentTasks = options.MaxConcurrentTasks };
        options.Configure?.Invoke(settings);
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(log).SetMinimumLevel(options.MinimumLogLevel));
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton<IEventBus>(options.EventBus ?? events);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(settings));
        services.AddSingleton(Options.Create(new BlockedTaskParkingOptions { Enabled = options.ParkingEnabled }));
        services.AddOptions<AgentRegistrySettings>();
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<ISessionRunnerClient>(runner);
        if (directory is not null)
            services.AddSingleton<ISessionRunnerDirectory>(directory);
        // The queue constructor requires the runtime. An empty runner transcript persists
        // nothing, so the seeded rows stay the boot clock.
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<DeadSessionFirstSeenState>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c1151-boot"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddSingleton<AgentTaskReplyService>();
        services.AddSingleton(Options.Create(new DelegateBindRefusalRecoverySettings
        {
            ClaudeProjectsRoot = Path.Combine(Path.GetTempPath(), "antiphon-c1151-boot-no-jsonl"),
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
            Events = events,
            Clock = clock,
            Directory = directory,
            SessionId = sessionId,
            TaskId = taskId,
            AgentId = agentId,
            ConnectionString = connectionString,
            Now0 = now0,
            PromptAt = promptAt,
            Setup = options,
        };
    }

    private static async Task SeedAsync(
        string connectionString, BootStallWorldOptions options,
        Guid sessionId, Guid taskId, Guid agentId, DateTime dispatched, DateTime promptAt)
    {
        var cwd = Path.GetTempPath();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        var name = $"boot-{agentId:N}"[..16];
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = name,
            Slug = name,
            WorkingDirectory = cwd,
            Details = "CARD-1151 boot-stall world.",
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
            Status = options.SessionStatus,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = dispatched,
            StartedAt = dispatched,
            LastSeenAt = dispatched,
            EndedAt = options.SessionEnded ? Pg(dispatched.AddMinutes(1)) : null,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "Boot stall world",
            Goal = "Stay on the inherited deadline.",
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = cwd,
            AgentId = agentId,
            AgentSessionId = sessionId,
            Status = options.TaskStatus,
            Attempt = options.Attempt,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = dispatched,
            DispatchedAt = dispatched,
        });
        var sequence = 1L;
        db.TranscriptEntries.Add(Entry(
            sessionId, sequence++, options.PromptKind,
            $"{DelegationReportFormatter.TaskMarker(taskId)} the brief {PromptCanary}", promptAt));
        if (options.AssistantAfterPrompt)
        {
            db.TranscriptEntries.Add(Entry(
                sessionId, sequence++, TranscriptKinds.AssistantText, "working", Pg(promptAt.AddSeconds(30))));
        }

        if (options.InterruptAfterPrompt)
        {
            db.TranscriptEntries.Add(Entry(
                sessionId, sequence++, TranscriptKinds.UserPrompt,
                $"{TranscriptKinds.InterruptedPromptPrefix} by user]", Pg(promptAt.AddSeconds(20))));
        }

        if (options.Brief is QueuedMessageStatus brief)
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 1,
                Body = $"{DelegationReportFormatter.TaskMarker(taskId)} the brief {PromptCanary}",
                Origin = QueuedMessageOrigin.Delegation,
                Status = brief,
                CreatedAt = dispatched,
                SentAt = brief == QueuedMessageStatus.Sent ? Pg(dispatched.AddSeconds(5)) : null,
            });
        }

        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    public static TranscriptEntry Entry(Guid sessionId, long sequence, string kind, string? text, DateTime at) =>
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

    public static DateTime Pg(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
        return new DateTime(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc);
    }

    /// <summary>
    /// Every log entry the provider's loggers write at or above
    /// <see cref="BootStallWorldOptions.MinimumLogLevel"/>, in order. The scoped
    /// <see cref="AppDbContext"/> and the warning writer's default context share this factory, so
    /// EF Core's own command and query errors land here exactly as they would in production.
    /// </summary>
    public IReadOnlyList<BootStallLogEntry> LogEntries() => _log.Entries.ToArray();

    /// <summary>
    /// Warning-level text capture (so a test failure prints what the sweep said) plus the
    /// structured entries at the world's minimum level (CARD-1151 repair 2).
    /// </summary>
    private sealed class BootStallLog(LogLevel minimum) : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ConcurrentQueue<BootStallLogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Category(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Category(BootStallLog log, string name) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= log._minimum;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;
                var message = formatter(state, exception);
                log.Entries.Enqueue(new BootStallLogEntry(logLevel, name, eventId, message, exception));
                if (logLevel >= LogLevel.Warning)
                    log.Lines.Enqueue($"{logLevel}: {message}{(exception is null ? "" : $" [{exception.GetType().Name}: {exception.Message}]")}");
            }
        }

        private readonly LogLevel _minimum = minimum;

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}

internal sealed record BootStallLogEntry(LogLevel Level, string Category, EventId EventId, string Message, Exception? Exception);

internal enum BootStallListing
{
    /// <summary>The runner lists the session, Running, at the matching generation.</summary>
    Matching = 0,
    None = 1,
    Exited = 2,
    WrongGeneration = 3,
}

internal enum BootStallDirectoryMode
{
    Available = 0,
    Unavailable = 1,
    Missing = 2,
    NullList = 3,
}

internal sealed record BootStallWorldOptions
{
    public int MinutesAgo { get; init; } = 9;

    /// <summary>The boot prompt's age; defaults to the dispatch age.</summary>
    public TimeSpan? PromptAge { get; init; }

    /// <summary>
    /// The seeded boot prompt's kind. <c>QueuedUserPrompt</c> models input that was only queued
    /// and never accepted (CARD-1151 R1): no boot episode.
    /// </summary>
    public string PromptKind { get; init; } = TranscriptKinds.UserPrompt;

    public bool AssistantAfterPrompt { get; init; }
    public bool InterruptAfterPrompt { get; init; }

    /// <summary>Zero keeps a tick from claiming anything; the characterization relies on it.</summary>
    public int MaxConcurrentTasks { get; init; }

    public AgentTaskStatus TaskStatus { get; init; } = AgentTaskStatus.Working;
    public int Attempt { get; init; } = 1;
    public SessionStatus SessionStatus { get; init; } = SessionStatus.Running;
    public bool SessionEnded { get; init; }

    /// <summary>The task's own delegation brief row; null seeds none.</summary>
    public QueuedMessageStatus? Brief { get; init; }

    public BootStallListing Listing { get; init; } = BootStallListing.Matching;

    /// <summary>Registers a counting owning-inventory directory when set.</summary>
    public BootStallDirectoryMode? Directory { get; init; }

    public bool ParkingEnabled { get; init; }
    public IEventBus? EventBus { get; init; }
    public Action<DelegationSettings>? Configure { get; init; }

    /// <summary>The capture floor for every logger in the world, EF Core included.</summary>
    public LogLevel MinimumLogLevel { get; init; } = LogLevel.Warning;
}

/// <summary>
/// An owning-inventory directory that counts every inventory, binding and owner read. Under
/// CARD-1151 option B the boot branch reads none of them, so a nonzero count is a defect.
/// </summary>
internal sealed class CountingRunnerDirectory(ListedInventoryRunner local, BootStallDirectoryMode mode)
    : ISessionRunnerDirectory
{
    public int InventoryReads { get; private set; }
    public int BindingReads { get; private set; }
    public int OwnerReads { get; private set; }
    public int Reads => InventoryReads + BindingReads + OwnerReads;

    public ISessionRunnerClient Local => local;

    public ISessionRunnerClient Resolve(string? runnerId) => local;

    public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct)
    {
        OwnerReads++;
        return Task.FromResult<SessionRunnerOwner?>(mode == BootStallDirectoryMode.Unavailable
            ? new SessionRunnerOwner("remote-c1151", Guid.NewGuid(), "/remote")
            : null);
    }

    public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct)
    {
        BindingReads++;
        return Task.FromResult<SessionRunnerBinding>(mode switch
        {
            BootStallDirectoryMode.Missing => SessionRunnerBinding.Missing.Instance,
            BootStallDirectoryMode.Unavailable => new SessionRunnerBinding.Remote(
                new SessionRunnerOwner("remote-c1151", Guid.NewGuid(), "/remote")),
            _ => SessionRunnerBinding.Local.Instance,
        });
    }

    public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct)
    {
        InventoryReads++;
        return Task.FromResult<RunnerInventory>(mode switch
        {
            BootStallDirectoryMode.Unavailable => new RunnerInventory.Unavailable(RunnerInventoryReasons.PhoneHomeUnavailable),
            BootStallDirectoryMode.NullList => new RunnerInventory.Available(null!),
            _ => new RunnerInventory.Available(local.Sessions.ToArray()),
        });
    }

    public IReadOnlyList<string> KnownRunnerIds => [];

    public Guid? GetLiveStoreId(string? runnerId) => null;
}
