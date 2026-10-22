using System.Collections.Concurrent;
using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

internal sealed class DispatcherAdmissionFixture : IAsyncDisposable
{
    public IsolatedTestSchema Schema { get; private set; } = null!;
    public PhoneHomeTestHost Host { get; private set; } = null!;
    private IAsyncDisposable _peer = null!;
    private readonly string _workspace = Directory.CreateTempSubdirectory("c1519-admission-").FullName;
    public ServiceProvider Provider { get; private set; } = null!;
    public AgentTaskDispatcherHostedService Service { get; private set; } = null!;
    public ArmedClock Clock { get; } = new();
    public AdmissionLogger Logger { get; } = new();
    public RecordingSink Sink { get; } = new();
    public RepositoryLeaseWaiters Waiters => Provider.GetRequiredService<RepositoryLeaseWaiters>();
    public SweepInFlightState Sweeps => Provider.GetRequiredService<SweepInFlightState>();
    public Guid Candidate { get; } = Guid.NewGuid();
    public Guid Occupant { get; } = Guid.NewGuid();
    public Guid SentinelSession { get; } = Guid.NewGuid();
    public TaskCompletionSource SweepEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseSweep { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource SweepCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource AdmissionEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource? HoldAdmission { get; set; }
    public AppDbContext? SweepDb { get; private set; }
    public ConcurrentQueue<AppDbContext> AdmissionDbs { get; } = new();
    public bool Cooperative { get; set; }
    public AppDbContext Read() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

    public static async Task<DispatcherAdmissionFixture> CreateAsync()
    {
        var f = new DispatcherAdmissionFixture();
        f.Schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        f.Host = await PhoneHomeTestHost.StartAsync();
        f._peer = await f.Host.ConnectPeerAsync();
        f.Host.Directory.MarkRecovered(await f.Host.WaitLiveAsync());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(f.Schema.ConnectionString)
            .AddInterceptors(new WatchdogQueryObserver(f)));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton<TimeProvider>(f.Clock);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            Enabled = true, PollIntervalSeconds = 5, MaxConcurrentTasks = 512, AllowedRoots = ["/", "C:\\"],
        }));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude.exe" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = f._workspace });
        services.AddSingleton(Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true, AllowedRunnerId = f.Host.AllowedRunnerId, AllowDelegatedTasks = true,
            HostWorkspaceRoot = f._workspace, CallbackOrigin = "https://example.test", SharedSecret = "fixture",
            ClaudeAuthProbeEnabled = false,
        }));
        services.AddSingleton<PhoneHomeLaunchPolicy>();
        services.AddSingleton<ISessionRunnerDirectory>(f.Host.Directory);
        services.AddSingleton<RemoteWorkspaceService>();
        services.AddSingleton<IAgentTaskLaunchSink>(f.Sink);
        services.AddSingleton<RepositoryLeaseWaiters>();
        services.AddSingleton<SweepInFlightState>();
        services.AddSingleton<ILogger<AgentTaskDispatcher>>(f.Logger);
        services.AddScoped<AgentTaskService>();
        services.AddScoped(sp =>
        {
            var d = ActivatorUtilities.CreateInstance<AgentTaskDispatcher>(sp);
            d.CatchUpOverride = async (id, ct) =>
            {
                if (id != f.SentinelSession) return;
                using var canceled = ct.Register(() => f.SweepCanceled.TrySetResult());
                f.SweepEntered.TrySetResult();
                if (f.Cooperative) await f.ReleaseSweep.Task.WaitAsync(ct);
                else await f.ReleaseSweep.Task;
                ct.ThrowIfCancellationRequested();
            };
            d.AfterQueuedSnapshotAsync = async (db, ct) =>
            {
                f.AdmissionDbs.Enqueue(db);
                if (f.HoldAdmission is { } hold)
                {
                    f.AdmissionEntered.TrySetResult();
                    await hold.Task.WaitAsync(ct);
                }
            };
            return d;
        });
        f.Provider = services.BuildServiceProvider();
        f.Service = new AgentTaskDispatcherHostedService(f.Provider.GetRequiredService<IServiceScopeFactory>(),
            f.Provider.GetRequiredService<IOptions<DelegationSettings>>(),
            NullLogger<AgentTaskDispatcherHostedService>.Instance, f.Waiters, f.Clock, f.Sweeps);
        await using var db = f.Read();
        var now = f.Clock.GetUtcNow().UtcDateTime;
        db.AgentSessions.Add(f.Session(f.Occupant, f.Host.AllowedRunnerId, now));
        db.AgentTasks.Add(new AgentTask
        {
            Id = f.Candidate, RootTaskId = f.Candidate, Title = "prepared capacity candidate", Goal = "reply",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Custom, AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = f._workspace, WorktreePath = f._workspace, WorktreeBranch = "feat/c1519",
            RemoteWorktreePath = "/work/c1519-prepared", RunnerId = f.Host.AllowedRunnerId,
            Status = AgentTaskStatus.Queued, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return f;
    }

    private AgentSession Session(Guid id, string runner, DateTime now) => new()
    {
        Id = id, DefinitionName = "claude", AgentKind = AgentKind.ClaudeCode,
        Status = SessionStatus.Running, Cwd = _workspace, RunnerId = runner,
        RunnerStoreId = Host.StoreId, RunnerCwd = "/work/c1519-occupant",
        Cols = 120, Rows = 30, CreatedAt = now, StartedAt = now, LastSeenAt = now,
    };

    public async Task StartHeldAsync()
    {
        await Service.StartAsync(default);
        await WaitUntilAsync(() => Task.FromResult(Clock.HasPoll), "initial poll armed");
        var initialPolls = Clock.Polls;
        Clock.Advance(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(async () => (await HeldAsync())?.Contains("capacity", StringComparison.OrdinalIgnoreCase) == true,
            "candidate held on runner capacity");
        await WaitUntilAsync(() => Task.FromResult(Clock.Polls >= initialPolls && Logger.Active == 0), "loop timers rearmed");
        Waiters.IsEmpty.ShouldBeTrue("capacity holds do not register lease waiters");
    }

    public async Task ArmSweepAsync()
    {
        await using var db = Read();
        var now = Clock.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
        db.AgentSessions.Add(Session(SentinelSession, "sentinel-runner", now));
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "watchdog sentinel", Goal = "wait", Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom, AgentKind = AgentKind.ClaudeCode, Workspace = WorkspaceMode.ReadOnly,
            WorkingDirectory = _workspace, RunnerId = "sentinel-runner", Status = AgentTaskStatus.Dispatched,
            AgentSessionId = SentinelSession, DispatchedAt = now.AddHours(-1), CreatedAt = now.AddHours(-1),
            ReplyTo = AgentTaskReplyTo.None, ConcurrencyToken = Guid.NewGuid(),
        });
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = Guid.NewGuid(), AgentSessionId = SentinelSession, ExecutionTaskId = id,
            Body = "watchdog sentinel brief", Status = QueuedMessageStatus.Pending,
            Origin = QueuedMessageOrigin.Delegation, Sequence = 1, CreatedAt = now.AddHours(-1),
        });
        await db.SaveChangesAsync();
        Clock.Advance(TimeSpan.FromSeconds(5));
        await SweepEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public async Task FreeSeatAsync()
    {
        await using var db = Read();
        await db.AgentSessions.Where(s => s.Id == Occupant)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SessionStatus.Stopped));
    }
    public async Task<string?> HeldAsync()
    {
        await using var db = Read();
        return await db.AgentTaskEvents.Where(e => e.AgentTaskId == Candidate && e.Type == AgentTaskEventType.Held)
            .OrderByDescending(e => e.At).Select(e => e.Detail).FirstOrDefaultAsync();
    }
    public async Task<bool> IsDispatchedAsync()
    {
        await using var db = Read();
        return await db.AgentTasks.AnyAsync(t => t.Id == Candidate && t.Status == AgentTaskStatus.Dispatched);
    }
    public async Task AssertOutcomeAsync(bool dispatched)
    {
        await using var db = Read();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Candidate);
        task.Status.ShouldBe(dispatched ? AgentTaskStatus.Dispatched : AgentTaskStatus.Queued,
            "C1519 freed seat must be admitted while the maintenance barrier remains closed");
        (await db.AgentSessions.CountAsync(s => s.RunnerId == Host.AllowedRunnerId && s.Id != Occupant))
            .ShouldBe(dispatched ? 1 : 0);
        Sink.Sessions.Count.ShouldBe(dispatched ? 1 : 0);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == Candidate && e.Type == AgentTaskEventType.Dispatched))
            .ShouldBe(dispatched ? 1 : 0);
        ReleaseSweep.Task.IsCompleted.ShouldBeFalse();
    }
    public static async Task<bool> ObserveAsync(Func<Task<bool>> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        do { if (await condition()) return true; await Task.Delay(10); } while (DateTime.UtcNow < until);
        return false;
    }
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, string reason) =>
        (await ObserveAsync(condition)).ShouldBeTrue(reason);

    public async ValueTask DisposeAsync()
    {
        if (Service is not null)
        {
            var stop = Service.StopAsync(CancellationToken.None);
            ReleaseSweep.TrySetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(10));
            await Sweeps.WhenReleasedAsync("delivery watchdog").WaitAsync(TimeSpan.FromSeconds(10));
            Service.Dispose();
        }
        if (Provider is not null) await Provider.DisposeAsync();
        if (_peer is not null) await _peer.DisposeAsync();
        if (Host is not null) await Host.DisposeAsync();
        if (Schema is not null) await Schema.DisposeAsync();
        Directory.Delete(_workspace, true);
    }

    public sealed class RecordingSink : IAgentTaskLaunchSink
    {
        public ConcurrentQueue<Guid> Sessions { get; } = new();
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) => Sessions.Enqueue(sessionId);
    }
    private sealed class WatchdogQueryObserver(DispatcherAdmissionFixture fixture) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            // Observe the real watchdog query's context, including its independently owned scope.
            if (command.CommandText.Contains("\"DispatchedAt\" <")
                && command.CommandText.Contains("\"AgentSessionId\" IS NOT NULL"))
                fixture.SweepDb = (AppDbContext?)eventData.Context;
            return ValueTask.FromResult(result);
        }
    }
    public sealed class AdmissionLogger : ILogger<AgentTaskDispatcher>
    {
        private int _active;
        public int Active => Volatile.Read(ref _active);
        public int Maximum { get; private set; }
        public ConcurrentQueue<string> Messages { get; } = new();
        public int FullStarts => Messages.Count(m => m.StartsWith("Runtime phase dispatcher.admission.full start"));
        public int ReadyEnds => Messages.Count(m => m.StartsWith("Runtime phase dispatcher.admission.ready end"));
        public string[] AdmissionStarts => Messages.Where(m => m.StartsWith("Runtime phase dispatcher.admission.")
            && m.Contains(" start ")).ToArray();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, ex);
            Messages.Enqueue(message);
            if (message.StartsWith("Runtime phase dispatcher.admission.") && message.Contains(" start "))
                Maximum = Math.Max(Maximum, Interlocked.Increment(ref _active));
            if (message.StartsWith("Runtime phase dispatcher.admission.") && message.Contains(" end ")) Interlocked.Decrement(ref _active);
        }
    }
    public sealed class ArmedClock : TimeProvider
    {
        private readonly FakeTimeProvider _inner = new(DateTimeOffset.UtcNow);
        private readonly ConcurrentQueue<ArmedTimer> _timers = new();
        public int Polls => _timers.Count(t => !t.Disposed && t.Due > GetUtcNow() && t.Due <= GetUtcNow().AddSeconds(5));
        public bool HasPoll => Polls > 0;
        public bool HasTimer(TimeSpan delay) => _timers.Any(t => !t.Disposed && t.Due == GetUtcNow() + delay);
        public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();
        public override long GetTimestamp() => _inner.GetTimestamp();
        public override long TimestampFrequency => _inner.TimestampFrequency;
        public void Advance(TimeSpan amount) => _inner.Advance(amount);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ArmedTimer(_inner.CreateTimer(callback, state, dueTime, period), GetUtcNow() + dueTime);
            _timers.Enqueue(timer);
            return timer;
        }
        private sealed class ArmedTimer(ITimer inner, DateTimeOffset due) : ITimer
        {
            public DateTimeOffset Due { get; } = due;
            public bool Disposed { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);
            public void Dispose() { Disposed = true; inner.Dispose(); }
            public async ValueTask DisposeAsync() { Disposed = true; await inner.DisposeAsync(); }
        }
    }
}
