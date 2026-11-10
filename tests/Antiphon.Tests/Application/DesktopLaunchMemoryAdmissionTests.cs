using System.Globalization;
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
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1478. A fresh desktop memory sample below the operator floor holds a new local worker
/// before lease, claim, session, or launch. The floor stays off until an operator sets it.
/// </summary>
[Category("Integration")]
[NotInParallel]
public sealed class DesktopLaunchMemoryAdmissionTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Low_memory_holds_before_launch(CancellationToken ct)
    {
        await using var world = await World.OpenAsync(floorMb: 6144);
        world.RecordDesktop(6143);
        var task = await world.SeedQueuedAsync();
        var token = task.ConcurrencyToken;

        await world.DispatchQueuedAsync(ct);

        world.Sink.Count.ShouldBe(0, "launch-count");
        var stored = await world.ReloadAsync(task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Queued);
        stored.ConcurrencyToken.ShouldBe(token);
        stored.AgentSessionId.ShouldBeNull();
        stored.WorktreePath.ShouldBeNull();
        stored.RunnerId.ShouldBeNull();
        var held = await world.HeldDetailsAsync(task.Id);
        held.Count.ShouldBe(1);
        held[0].ShouldBe(Hold("fresh", world.SampleAt, 6143, 6144));
        (await world.SessionCountAsync()).ShouldBe(0);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Equality_and_above_admit(CancellationToken ct)
    {
        await using var world = await World.OpenAsync(floorMb: 6144);
        world.RecordDesktop(6144);
        var atFloor = await world.SeedQueuedAsync();

        await world.DispatchQueuedAsync(ct);

        world.Sink.Count.ShouldBe(1, "launch-count");
        (await world.ReloadAsync(atFloor.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await world.MemoryHoldsAsync(atFloor.Id)).ShouldBeEmpty();

        world.Advance(TimeSpan.FromSeconds(1));
        world.RecordDesktop(6145);
        var above = await world.SeedQueuedAsync(directory: world.NewCheckout());

        await world.DispatchQueuedAsync(ct);

        world.Sink.Count.ShouldBe(2, "launch-count");
        (await world.ReloadAsync(above.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await world.MemoryHoldsAsync(atFloor.Id)).ShouldBeEmpty();
        (await world.MemoryHoldsAsync(above.Id)).ShouldBeEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Unknown_or_stale_is_held_when_enabled(CancellationToken ct)
    {
        await using var world = await World.OpenAsync(floorMb: 6144);
        var task = await world.SeedQueuedAsync();

        await world.DispatchQueuedAsync(ct);

        world.Sink.Count.ShouldBe(0, "launch-count");
        (await world.ReloadAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Queued);
        var unknown = await world.MemoryHoldsAsync(task.Id);
        unknown.Count.ShouldBe(1);
        unknown[0].ShouldBe(Hold("unknown", null, null, 6144));

        world.RecordDesktop(100);
        var sample = world.SampleAt;
        world.Advance(TimeSpan.FromMilliseconds(15001));
        await world.DispatchQueuedAsync(ct);

        world.Sink.Count.ShouldBe(0, "launch-count");
        (await world.ReloadAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Queued);
        var held = await world.MemoryHoldsAsync(task.Id);
        held.Count.ShouldBe(2);
        held[1].ShouldBe(Hold("stale", sample, null, 6144));
        held[1].ShouldNotContain("availableMb=100");
        held[1].ShouldNotContain("availableMb=0");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Disabled_preserves_existing_behavior(CancellationToken ct)
    {
        await using var world = await World.OpenAsync(floorMb: 0);
        world.RecordDesktop(100);
        var task = await world.SeedQueuedAsync();

        await world.DispatchQueuedAsync(ct);

        world.Sink.Count.ShouldBe(1, "launch-count");
        (await world.ReloadAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await world.MemoryHoldsAsync(task.Id)).ShouldBeEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Recovery_releases_once_and_deduplicates_hold(CancellationToken ct)
    {
        await using var world = await World.OpenAsync(floorMb: 6144);
        world.RecordDesktop(6143);
        var sample = world.SampleAt;
        var task = await world.SeedQueuedAsync();

        await world.DispatchQueuedAsync(ct);
        await world.DispatchQueuedAsync(ct);

        world.Sink.Count.ShouldBe(0, "launch-count");
        (await world.ReloadAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Queued);
        var held = await world.MemoryHoldsAsync(task.Id);
        held.Count.ShouldBe(1);
        held[0].ShouldBe(Hold("fresh", sample, 6143, 6144));

        world.Advance(TimeSpan.FromSeconds(1));
        world.RecordDesktop(7000);
        await world.DispatchQueuedAsync(ct);

        world.Sink.Count.ShouldBe(1, "launch-count");
        (await world.ReloadAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        var after = await world.MemoryHoldsAsync(task.Id);
        after.Count.ShouldBe(1);
        after[0].ShouldBe(held[0]);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Remote_and_warm_reuse_are_unchanged(CancellationToken ct)
    {
        await using var world = await World.OpenAsync(floorMb: 6144, remoteRunners: true);
        world.RecordDesktop(6143);
        await world.SeedWarmAsync(world.Checkout);
        var warm = await world.SeedQueuedAsync();

        await world.DispatchQueuedAsync(ct);

        world.Sink.Count.ShouldBe(0, "launch-count");
        (await world.ReloadAsync(warm.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        var reused = await world.DetailsAsync(warm.Id, AgentTaskEventType.Dispatched);
        reused.Count.ShouldBe(1);
        reused[0].ShouldContain("Reused warm delegate");
        reused[0].ShouldContain("no cold start");
        (await world.MemoryHoldsAsync(warm.Id)).ShouldBeEmpty();

        var remote = await world.SeedQueuedAsync(
            directory: world.NewCheckout(),
            runnerId: "runner-b",
            remoteWorktreePath: "/antiphon/remote-card-1478");
        await world.DispatchQueuedAsync(ct);

        world.Sink.Count.ShouldBe(1, "launch-count");
        var stored = await world.ReloadAsync(remote.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.RunnerId.ShouldBe("runner-b");
        (await world.MemoryHoldsAsync(remote.Id)).ShouldBeEmpty();
        (await world.MemoryHoldsAsync(warm.Id)).ShouldBeEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Ready_waiter_cannot_bypass_guard(CancellationToken ct)
    {
        await using var world = await World.OpenAsync(floorMb: 6144);
        world.RecordDesktop(6143);
        var task = await world.SeedQueuedAsync();
        var token = task.ConcurrencyToken;
        world.ArmReady(task.Id);

        await world.DispatchReadyWaitersAsync(ct);

        world.Sink.Count.ShouldBe(0, "launch-count");
        var stored = await world.ReloadAsync(task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Queued);
        stored.ConcurrencyToken.ShouldBe(token);
        stored.AgentSessionId.ShouldBeNull();
        var held = await world.MemoryHoldsAsync(task.Id);
        held.Count.ShouldBe(1);
        held[0].ShouldBe(Hold("fresh", world.SampleAt, 6143, 6144));
    }

    [Test]
    [Timeout(180_000)]
    public async Task Existing_sessions_and_platform_binding_are_unchanged(CancellationToken ct)
    {
        await using var world = await World.OpenAsync(floorMb: 6144);
        world.RecordDesktop(6143);
        var live = await world.SeedWorkingAsync(world.NewCheckout());
        var queued = await world.SeedQueuedAsync(platform: RequiredPlatform.Windows);

        await world.DispatchQueuedAsync(ct);

        world.Stopper.Killed.ShouldBeEmpty();
        (await world.ReloadSessionAsync(live.SessionId)).Status.ShouldBe(SessionStatus.Running);
        (await world.ReloadAsync(live.TaskId)).Status.ShouldBe(AgentTaskStatus.Working);
        world.Sink.Count.ShouldBe(0, "launch-count");
        var stored = await world.ReloadAsync(queued.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Queued);
        stored.RequiredPlatform.ShouldBe(RequiredPlatform.Windows);
        stored.RunnerId.ShouldBeNull();
        var held = await world.MemoryHoldsAsync(queued.Id);
        held.Count.ShouldBe(1);
        held[0].ShouldBe(Hold("fresh", world.SampleAt, 6143, 6144));
    }

    private static string Hold(string freshness, DateTimeOffset? sampleAt, long? availableMb, int floorMb)
    {
        var at = sampleAt is { } observed ? observed.ToString("O") : "unknown";
        var available = availableMb is long mb
            ? mb.ToString(CultureInfo.InvariantCulture)
            : "unknown";
        return $"desktop_launch_memory: freshness={freshness} sampleAt={at} availableMb={available} floorMb={floorMb.ToString(CultureInfo.InvariantCulture)}";
    }

    private sealed class CountingSink : IAgentTaskLaunchSink
    {
        public int Count { get; private set; }

        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) =>
            Count++;
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        private readonly string _directory;
        private readonly List<string> _extras = [];
        private readonly FakeTimeProvider _clock;
        private readonly HostStatsCache _cache;
        private readonly RepositoryLeaseWaiters _waiters;

        private World(
            IsolatedTestSchema schema,
            ServiceProvider provider,
            IServiceScope scope,
            AgentTaskDispatcher dispatcher,
            CountingSink sink,
            RecordingSessionStopper stopper,
            string directory,
            FakeTimeProvider clock,
            HostStatsCache cache,
            RepositoryLeaseWaiters waiters)
        {
            _schema = schema;
            _provider = provider;
            _scope = scope;
            Dispatcher = dispatcher;
            Sink = sink;
            Stopper = stopper;
            _directory = directory;
            _clock = clock;
            _cache = cache;
            _waiters = waiters;
        }

        public AgentTaskDispatcher Dispatcher { get; }
        public CountingSink Sink { get; }
        public RecordingSessionStopper Stopper { get; }
        public string Checkout => _directory;
        public DateTimeOffset SampleAt =>
            _cache.Latest(RunnerPlatformWire.DesktopId)!.ObservedAt;

        public static async Task<World> OpenAsync(int floorMb, bool remoteRunners = false)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var clock = new FakeTimeProvider();
            var stats = new HostStatsSettings();
            var cache = new HostStatsCache(stats, clock);
            var sink = new CountingSink();
            var stopper = new RecordingSessionStopper { StopsSessionsIn = schema.ConnectionString };
            var directory = System.IO.Directory.CreateTempSubdirectory("antiphon-mem-admit").FullName;
            var delegation = new DelegationSettings
            {
                PoolReservedForCallerMinutes = 0,
                PoolIdleRetireMinutes = 600,
                PoolMaxIdlePerDirectory = 8,
                MaxConcurrentTasks = 512,
                DesktopLaunchMemoryFloorMb = floorMb,
            };
            var waiters = new RepositoryLeaseWaiters(Options.Create(delegation), clock);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(delegation));
            services.AddSingleton(waiters);
            services.AddOptions<AgentRegistrySettings>().Configure(s =>
            {
                s.DefaultDefinition = "claude";
                s.GrokCredentialProbeEnabled = false;
                s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
            });
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper>(stopper);
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddSingleton<IAgentTaskLaunchSink>(sink);
            services.AddSingleton(cache);
            services.AddSingleton(new DesktopLaunchMemoryAdmission(cache, stats, clock));
            services.AddDelegationWorktreeGraph(new GitSettings
            {
                WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-mem-admit-wt"),
            });
            if (remoteRunners)
            {
                services.AddSingleton<ISessionRunnerDirectory>(new ConcurrencyRunnerDirectory());
                services.AddSingleton<RemoteWorkspaceService>();
            }

            services.AddScoped<AgentTaskService>();
            services.AddScoped<AgentTaskDispatcher>();
            var provider = services.BuildServiceProvider();
            var scope = provider.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            return new World(schema, provider, scope, dispatcher, sink, stopper, directory, clock, cache, waiters);
        }

        public void Advance(TimeSpan by) => _clock.Advance(by);

        public string NewCheckout()
        {
            var path = System.IO.Directory.CreateTempSubdirectory("antiphon-mem-admit").FullName;
            _extras.Add(path);
            return path;
        }

        public void ArmReady(Guid taskId)
        {
            _waiters.Register(
                Checkout, taskId, RepositoryLeasePurposes.Dispatch, _clock.GetUtcNow(), verificationCompatible: true);
            _waiters.SignalReady();
        }

        public void RecordDesktop(long? availableMb)
        {
            var bytes = availableMb is long mb ? mb * 1024L * 1024L : (long?)null;
            _cache.Record(RunnerPlatformWire.DesktopId, new RunnerHostStatsDto(
                At: _clock.GetUtcNow(),
                IntervalSeconds: 5,
                RetentionMinutes: 30,
                Cores: 8,
                Current: new RunnerHostCurrentDto(
                    null, null, null, null,
                    null, bytes, 32L * 1024 * 1024 * 1024,
                    null, null, [], null, []),
                Rollups: new Dictionary<string, RunnerHostWindowRollups>(),
                BuildSlots: null));
        }

        public async Task<AgentTask> SeedQueuedAsync(
            string? directory = null,
            RequiredPlatform platform = RequiredPlatform.Any,
            string? runnerId = null,
            string? remoteWorktreePath = null)
        {
            var id = Guid.NewGuid();
            var now = _clock.GetUtcNow().UtcDateTime;
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "desktop memory admission",
                Goal = "desktop memory admission",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Docs,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = directory ?? _directory,
                Ephemeral = true,
                Status = AgentTaskStatus.Queued,
                CreatedAt = now,
                ReplyTo = AgentTaskReplyTo.None,
                RequiredPlatform = platform,
                RunnerId = runnerId,
                RemoteWorktreePath = remoteWorktreePath,
            };
            await using var db = NewContext();
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }

        public async Task SeedWarmAsync(string directory)
        {
            var sessionId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var now = _clock.GetUtcNow().UtcDateTime;
            var name = $"p{agentId:N}"[..12];
            await using var db = NewContext();
            db.AgentSessions.Add(LiveSession(sessionId, directory, now));
            db.Agents.Add(new Agent
            {
                Id = agentId,
                Name = name,
                Slug = name,
                WorkingDirectory = directory,
                Details = "CARD-1478 warm pool delegate.",
                Status = AgentStatus.Idle,
                ModelLevel = AgentModelLevel.Medium,
                Kind = AgentKind.ClaudeCode,
                IsPoolDelegate = true,
                PoolIdleSince = now.AddMinutes(-10),
                PoolClaudeEffort = "medium",
                PersistentSessionId = sessionId.ToString("D"),
                AlwaysOn = false,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        public async Task<(Guid TaskId, Guid SessionId)> SeedWorkingAsync(string directory)
        {
            var sessionId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            var now = _clock.GetUtcNow().UtcDateTime;
            var name = $"w{agentId:N}"[..12];
            await using var db = NewContext();
            db.AgentSessions.Add(LiveSession(sessionId, directory, now));
            db.Agents.Add(new Agent
            {
                Id = agentId,
                Name = name,
                Slug = name,
                WorkingDirectory = directory,
                Details = "CARD-1478 existing session.",
                Status = AgentStatus.Running,
                ModelLevel = AgentModelLevel.Medium,
                Kind = AgentKind.ClaudeCode,
                IsPoolDelegate = false,
                PersistentSessionId = sessionId.ToString("D"),
                AlwaysOn = false,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "already working",
                Goal = "already working",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Docs,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.ReadOnly,
                WorkingDirectory = directory,
                Ephemeral = true,
                Status = AgentTaskStatus.Working,
                CreatedAt = now,
                DispatchedAt = now,
                ReplyTo = AgentTaskReplyTo.None,
                AgentId = agentId,
                AgentSessionId = sessionId,
            });
            await db.SaveChangesAsync();
            return (taskId, sessionId);
        }

        private static AgentSession LiveSession(Guid sessionId, string directory, DateTime now) => new()
        {
            Id = sessionId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        };

        public Task DispatchQueuedAsync(CancellationToken ct) =>
            Dispatcher.DispatchQueuedAsync(ct);

        public Task DispatchReadyWaitersAsync(CancellationToken ct) =>
            Dispatcher.DispatchReadyWaitersAsync(ct);

        public async Task<AgentTask> ReloadAsync(Guid id)
        {
            await using var db = NewContext();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        }

        public async Task<AgentSession> ReloadSessionAsync(Guid id)
        {
            await using var db = NewContext();
            return await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == id);
        }

        public Task<List<string>> HeldDetailsAsync(Guid id) =>
            DetailsAsync(id, AgentTaskEventType.Held);

        public async Task<List<string>> MemoryHoldsAsync(Guid id) =>
            (await HeldDetailsAsync(id))
            .Where(detail => detail.StartsWith("desktop_launch_memory:", StringComparison.Ordinal))
            .ToList();

        public async Task<List<string>> DetailsAsync(Guid id, AgentTaskEventType type)
        {
            await using var db = NewContext();
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == id && e.Type == type)
                .OrderBy(e => e.At).ThenBy(e => e.Id)
                .Select(e => e.Detail)
                .ToListAsync();
        }

        public async Task<int> SessionCountAsync()
        {
            await using var db = NewContext();
            return await db.AgentSessions.CountAsync();
        }

        private AppDbContext NewContext() =>
            new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));

        public async ValueTask DisposeAsync()
        {
            _scope.Dispose();
            await _provider.DisposeAsync();
            await _schema.DisposeAsync();
            TryDelete(_directory);
            foreach (var extra in _extras)
                TryDelete(extra);
        }

        private static void TryDelete(string path)
        {
            try
            {
                System.IO.Directory.Delete(path, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
