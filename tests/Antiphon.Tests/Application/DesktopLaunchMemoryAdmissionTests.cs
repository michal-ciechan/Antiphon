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
        private readonly string _directory;
        private readonly FakeTimeProvider _clock;
        private readonly HostStatsCache _cache;

        private World(
            IsolatedTestSchema schema,
            ServiceProvider provider,
            AgentTaskDispatcher dispatcher,
            CountingSink sink,
            string directory,
            FakeTimeProvider clock,
            HostStatsCache cache)
        {
            _schema = schema;
            _provider = provider;
            Dispatcher = dispatcher;
            Sink = sink;
            _directory = directory;
            _clock = clock;
            _cache = cache;
        }

        public AgentTaskDispatcher Dispatcher { get; }
        public CountingSink Sink { get; }
        public string Checkout => _directory;
        public DateTimeOffset SampleAt =>
            _cache.Latest(RunnerPlatformWire.DesktopId)!.ObservedAt;

        public static async Task<World> OpenAsync(int floorMb)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var clock = new FakeTimeProvider();
            var stats = new HostStatsSettings();
            var cache = new HostStatsCache(stats, clock);
            var sink = new CountingSink();
            var directory = System.IO.Directory.CreateTempSubdirectory("antiphon-mem-admit").FullName;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new DelegationSettings
            {
                PoolReservedForCallerMinutes = 0,
                PoolIdleRetireMinutes = 600,
                PoolMaxIdlePerDirectory = 8,
                MaxConcurrentTasks = 512,
                DesktopLaunchMemoryFloorMb = floorMb,
            }));
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
            services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper
            {
                StopsSessionsIn = schema.ConnectionString,
            });
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddSingleton<IAgentTaskLaunchSink>(sink);
            services.AddSingleton(cache);
            services.AddDelegationWorktreeGraph(new GitSettings
            {
                WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-mem-admit-wt"),
            });
            services.AddScoped<AgentTaskService>();
            services.AddScoped<AgentTaskDispatcher>();
            var provider = services.BuildServiceProvider();
            var dispatcher = provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            return new World(schema, provider, dispatcher, sink, directory, clock, cache);
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

        public async Task<AgentTask> SeedQueuedAsync()
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
                WorkingDirectory = _directory,
                Ephemeral = true,
                Status = AgentTaskStatus.Queued,
                CreatedAt = now,
                ReplyTo = AgentTaskReplyTo.None,
            };
            await using var db = NewContext();
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }

        public Task DispatchQueuedAsync(CancellationToken ct) =>
            Dispatcher.DispatchQueuedAsync(ct);

        public async Task<AgentTask> ReloadAsync(Guid id)
        {
            await using var db = NewContext();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        }

        public async Task<List<string>> HeldDetailsAsync(Guid id)
        {
            await using var db = NewContext();
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == id && e.Type == AgentTaskEventType.Held)
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
            await _provider.DisposeAsync();
            await _schema.DisposeAsync();
            try
            {
                System.IO.Directory.Delete(_directory, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
