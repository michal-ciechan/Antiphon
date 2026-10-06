using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1079 V-7 through V-12. The sampler records occupancy and does not touch sessions.</summary>
[Category("Integration")]
public sealed class SeatOccupancySamplerTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RunnerStarted = Now.AddHours(-3);
    private static readonly DateTime BlockedAt = Now.AddMinutes(-40);
    private static readonly Guid Store = Guid.Parse("10790000-0000-4000-8000-000000000002");

    [Test]
    public async Task C1079_One_sample_per_host_with_in_flight_breakdown_and_dispatched_working()
    {
        await using var rig = await Rig.SeedRemoteAsync(listThrows: false);
        await rig.SampleAsync();

        await using var db = new AppDbContext(rig.DbOptions);
        var rows = await db.HostOccupancySamples.AsNoTracking().ToListAsync();
        rows.Count.ShouldBe(2);
        var server2 = rows.Single(r => r.HostId == "server2");
        server2.InFlight.ShouldBe(4);
        server2.Sessions.ShouldBe(3);
        server2.PendingLaunch.ShouldBe(1);
        server2.InFlightMirrors.ShouldBe(0);
        server2.DispatchedWorking.ShouldBe(1);
        server2.IdleSeats.ShouldBe(2);
        server2.OrphanSlots.ShouldBe(2);
        server2.EffectiveLimit.ShouldBe(10);
        server2.InventoryState.ShouldBe("listed");
        server2.SampledAt.ShouldBe(Now);

        var local = rows.Single(r => r.HostId == "local");
        local.InventoryState.ShouldBe("local");
        local.InFlight.ShouldBe(0);
        local.DispatchedWorking.ShouldBe(0);

        var observed = rig.State.Current.Hosts.Single(h => h.HostId == "server2");
        observed.InFlight.ShouldBe(server2.InFlight);
        observed.Sessions.ShouldBe(server2.Sessions);
        observed.PendingLaunch.ShouldBe(server2.PendingLaunch);
        observed.InFlightMirrors.ShouldBe(server2.InFlightMirrors);
        observed.DispatchedWorking.ShouldBe(server2.DispatchedWorking);
        observed.IdleSeats.ShouldBe(server2.IdleSeats);
        observed.OrphanSlots.ShouldBe(server2.OrphanSlots);
        observed.EffectiveLimit.ShouldBe(server2.EffectiveLimit);
        observed.InventoryState.ShouldBe("listed");
        observed.Seats.Count.ShouldBe(3);

        (await db.AgentTasks.SingleAsync(t => t.Id == rig.WorkingTask)).Status.ShouldBe(AgentTaskStatus.Working);
        (await db.AgentSessions.SingleAsync(s => s.Id == rig.WorkingSession)).Status.ShouldBe(SessionStatus.Running);
    }

    [Test]
    public async Task C1079_Seats_carry_idle_since_class_orphan_and_pushed_from_durable_facts()
    {
        await using var rig = await Rig.SeedRemoteAsync(listThrows: false);
        await rig.SampleAsync();

        var server2 = rig.State.Current.Hosts.Single(h => h.HostId == "server2");
        var blocked = server2.Seats.Single(s => s.SessionId == rig.BlockedSession);
        blocked.Class.ShouldBe(SeatClass.IdleBlocked);
        blocked.IdleSince.ShouldBe(BlockedAt);
        blocked.Orphan.ShouldBeTrue();
        blocked.Pushed.ShouldBe("yes");
        blocked.TaskStatus.ShouldBe(AgentTaskStatus.Blocked);
        blocked.Attempt.ShouldBe(4);

        var unbound = server2.Seats.Single(s => s.SessionId == rig.UnboundSession);
        unbound.Class.ShouldBe(SeatClass.IdleUnbound);
        unbound.IdleSince.ShouldBe(RunnerStarted);
        unbound.Pushed.ShouldBe("unknown");
        unbound.TaskId.ShouldBeNull();

        var working = server2.Seats.Single(s => s.SessionId == rig.WorkingSession);
        working.Class.ShouldBe(SeatClass.Active);
        working.Orphan.ShouldBeFalse();

        server2.OldestIdleSince.ShouldBe(new[] { blocked.IdleSince, unbound.IdleSince }.Min());
    }

    [Test]
    public async Task C1079_Unavailable_inventory_writes_a_sample_without_seats()
    {
        await using var rig = await Rig.SeedRemoteAsync(listThrows: true);
        await rig.SampleAsync();

        await using var db = new AppDbContext(rig.DbOptions);
        var server2 = await db.HostOccupancySamples.AsNoTracking().SingleAsync(r => r.HostId == "server2");
        server2.InventoryState.ShouldBe("unavailable");
        server2.InventoryReason.ShouldNotBeNullOrWhiteSpace();
        server2.Sessions.ShouldBe(3);
        server2.InFlight.ShouldBe(4);
        server2.DispatchedWorking.ShouldBe(1);
        server2.IdleSeats.ShouldBe(2);
        server2.OrphanSlots.ShouldBe(0);

        var observed = rig.State.Current.Hosts.Single(h => h.HostId == "server2");
        observed.Seats.Count.ShouldBe(0);
        observed.InventoryState.ShouldBe("unavailable");
    }

    [Test]
    public async Task C1101_Unavailable_inventory_reason_is_error_without_the_exception_text()
    {
        const string leaked = "runner down: token=super-secret-value";
        await using var rig = await Rig.SeedRemoteAsync(listThrows: false);
        rig.Client.ListError = new InvalidOperationException(leaked);
        await rig.SampleAsync();

        await using var db = new AppDbContext(rig.DbOptions);
        var server2 = await db.HostOccupancySamples.AsNoTracking().SingleAsync(r => r.HostId == "server2");
        server2.InventoryState.ShouldBe("unavailable");
        server2.InventoryReason.ShouldBe("error");
        server2.InventoryReason!.ShouldNotContain("super-secret");
        server2.InventoryReason.ShouldNotContain("runner down");

        var observed = rig.State.Current.Hosts.Single(h => h.HostId == "server2");
        observed.InventoryReason.ShouldBe("error");
        observed.InventoryReason!.ShouldNotContain("super-secret");
    }

    [Test]
    public async Task C1101_Inventory_timeout_reason_is_timeout_without_the_exception_text()
    {
        const string leaked = "timed out talking to secret-host.internal";
        await using var rig = await Rig.SeedRemoteAsync(listThrows: false);
        rig.Client.ListError = new OperationCanceledException(leaked);
        await rig.SampleAsync();

        await using var db = new AppDbContext(rig.DbOptions);
        var server2 = await db.HostOccupancySamples.AsNoTracking().SingleAsync(r => r.HostId == "server2");
        server2.InventoryState.ShouldBe("unavailable");
        server2.InventoryReason.ShouldBe("timeout");
        server2.InventoryReason!.ShouldNotContain("secret-host");

        var observed = rig.State.Current.Hosts.Single(h => h.HostId == "server2");
        observed.InventoryReason.ShouldBe("timeout");
        observed.InventoryReason!.ShouldNotContain("secret-host");
    }

    [Test]
    public async Task C1079_Prune_removes_samples_older_than_retention()
    {
        await using var rig = await Rig.EmptyAsync();
        var agedOut = Guid.NewGuid();
        var kept = Guid.NewGuid();
        await using (var db = new AppDbContext(rig.DbOptions))
        {
            db.HostOccupancySamples.AddRange(
                Sample(agedOut, Now.AddDays(-15)),
                Sample(kept, Now.AddDays(-13)));
            await db.SaveChangesAsync();
        }

        await rig.SampleAsync();

        await using var read = new AppDbContext(rig.DbOptions);
        var rows = await read.HostOccupancySamples.AsNoTracking().ToListAsync();
        rows.ShouldNotContain(r => r.Id == agedOut);
        rows.ShouldContain(r => r.Id == kept);
        rows.Count(r => r.Id != kept).ShouldBe(2);
        rows.ShouldContain(r => r.HostId == "local" && r.SampledAt == Now);
        rows.ShouldContain(r => r.HostId == "server2" && r.SampledAt == Now);
    }

    [Test]
    [Timeout(60_000)]
    public async Task C1079_Disabled_watch_writes_nothing_and_publishes_nothing(CancellationToken cancellationToken)
    {
        await using var rig = await Rig.EmptyAsync();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(rig.Clock);
        services.AddSingleton<ISessionRunnerDirectory>(rig.Directory);
        services.AddSingleton(rig.State);
        services.AddSingleton<IOptions<AttentionSettings>>(
            Options.Create(new AttentionSettings { SeatWatchEnabled = false }));
        services.AddSingleton<IOptions<DelegationSettings>>(
            Options.Create(new DelegationSettings { MaxConcurrentTasks = 4 }));
        services.AddScoped(_ => new AppDbContext(rig.DbOptions));
        services.AddScoped<HostBudgetService>();
        services.AddScoped<SeatOccupancySampler>();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var hosted = new SeatOccupancyHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<AttentionSettings>>(),
            rig.Clock,
            NullLogger<SeatOccupancyHostedService>.Instance);

        await hosted.StartAsync(cancellationToken);
        // CARD-1094: StartAsync queues ExecuteAsync and returns before that delegate runs.
        // StopAsync then cancels the tick before a sample is written, so the empty table
        // below stays green when SeatWatchEnabled is ignored. The disabled path has to
        // finish on its own. An ignored flag stays on the 60s sample timer and this wait fails.
        var execute = hosted.ExecuteTask;
        execute.ShouldNotBeNull();
        await execute.WaitAsync(TimeSpan.FromSeconds(5));
        execute.IsCompletedSuccessfully.ShouldBeTrue();
        await hosted.StopAsync(cancellationToken);

        rig.Client.Lists.ShouldBe(0);
        rig.State.Current.ShouldBeSameAs(SeatOccupancySnapshot.Empty);
        await using var db = new AppDbContext(rig.DbOptions);
        (await db.HostOccupancySamples.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task C1079_Local_host_sample_counts_tasks_and_has_zero_divergence()
    {
        await using var rig = await Rig.EmptyAsync();
        await using (var db = new AppDbContext(rig.DbOptions))
        {
            db.AgentTasks.AddRange(
                LocalTask(Guid.NewGuid(), AgentTaskStatus.Dispatched, AgentTaskRole.Code),
                LocalTask(Guid.NewGuid(), AgentTaskStatus.Working, AgentTaskRole.Review),
                LocalTask(Guid.NewGuid(), AgentTaskStatus.Blocked, AgentTaskRole.Code),
                LocalTask(Guid.NewGuid(), AgentTaskStatus.Working, AgentTaskRole.Check));
            await db.SaveChangesAsync();
        }

        await rig.SampleAsync();

        await using var read = new AppDbContext(rig.DbOptions);
        var local = await read.HostOccupancySamples.AsNoTracking().SingleAsync(r => r.HostId == "local");
        local.InFlight.ShouldBe(2);
        local.DispatchedWorking.ShouldBe(2);
        local.IdleSeats.ShouldBe(0);
        local.InventoryState.ShouldBe("local");
        (local.InFlight - local.DispatchedWorking).ShouldBe(0);
    }

    private static HostOccupancySample Sample(Guid id, DateTime at) => new()
    {
        Id = id,
        HostId = "archive",
        SampledAt = at,
        InventoryState = "listed",
    };

    private static AgentTask LocalTask(Guid id, AgentTaskStatus status, AgentTaskRole role) => new()
    {
        Id = id,
        RootTaskId = id,
        Status = status,
        Role = role,
        Title = "c1079-local",
        Goal = "local count",
        CreatedAt = Now,
        DispatchedAt = Now,
    };

    private sealed class Rig : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public required DbContextOptions<AppDbContext> DbOptions { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required SeatOccupancyState State { get; init; }
        public required ListingClient Client { get; init; }
        public required SingleRunnerDirectory Directory { get; init; }
        public Guid WorkingSession { get; init; }
        public Guid BlockedSession { get; init; }
        public Guid UnboundSession { get; init; }
        public Guid WorkingTask { get; init; }

        public static async Task<Rig> EmptyAsync() => await CreateAsync(seedRemote: false, listThrows: false);

        public static async Task<Rig> SeedRemoteAsync(bool listThrows) =>
            await CreateAsync(seedRemote: true, listThrows);

        public async Task SampleAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            var budgets = new HostBudgetService(
                db, Directory, Options.Create(new DelegationSettings { MaxConcurrentTasks = 4 }), Clock);
            var sampler = new SeatOccupancySampler(
                db, Directory, budgets, State, Options.Create(new AttentionSettings()), Clock,
                NullLogger<SeatOccupancySampler>.Instance);
            await sampler.SampleOnceAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync() => await Schema.DisposeAsync();

        private static async Task<Rig> CreateAsync(bool seedRemote, bool listThrows)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
            var workingSession = Guid.NewGuid();
            var blockedSession = Guid.NewGuid();
            var unboundSession = Guid.NewGuid();
            var workingTask = Guid.NewGuid();
            var blockedTask = Guid.NewGuid();
            var client = new ListingClient();
            if (seedRemote)
            {
                client.Sessions =
                [
                    Listed(workingSession, 11),
                    Listed(blockedSession, 12),
                    Listed(unboundSession, 13),
                ];
                await using var db = new AppDbContext(options);
                db.HostBudgets.Add(new HostBudget
                {
                    HostId = "server2",
                    MaxInFlight = 10,
                    Reason = "c1079",
                    UpdatedAt = Now,
                    Revision = 1,
                });
                db.AgentSessions.AddRange(
                    RemoteSession(workingSession),
                    RemoteSession(blockedSession),
                    RemoteSession(unboundSession));
                var pending = Guid.NewGuid();
                db.AgentTasks.AddRange(
                    RemoteTask(workingTask, workingSession, AgentTaskStatus.Working, Now.AddMinutes(-20), attempt: 2),
                    RemoteTask(blockedTask, blockedSession, AgentTaskStatus.Blocked, Now.AddMinutes(-30), attempt: 4),
                    new AgentTask
                    {
                        Id = pending,
                        RootTaskId = pending,
                        RunnerId = "server2",
                        Status = AgentTaskStatus.Queued,
                        Title = "c1079-pending",
                        Goal = "pending launch",
                        RemoteWorktreePath = "/tmp/c1079-pending",
                        CreatedAt = Now.AddMinutes(-5),
                    });
                db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(),
                    AgentTaskId = blockedTask,
                    AgentSessionId = blockedSession,
                    Type = AgentTaskEventType.Blocked,
                    Detail = "blocked",
                    At = BlockedAt,
                });
                db.AgentTaskParks.Add(new AgentTaskPark
                {
                    Id = Guid.NewGuid(),
                    TaskId = blockedTask,
                    Attempt = 4,
                    BlockEventId = Guid.NewGuid(),
                    TaskConcurrencyToken = Guid.NewGuid(),
                    PublicationReceiptId = Guid.NewGuid(),
                    BlockedAt = BlockedAt,
                    CreatedAt = BlockedAt,
                    UpdatedAt = BlockedAt,
                    ReasonCode = "park_requested",
                });
                await db.SaveChangesAsync();
            }

            if (listThrows)
                client.ListError = new InvalidOperationException("runner down");

            return new Rig
            {
                Schema = schema,
                DbOptions = options,
                Clock = new FakeTimeProvider(new DateTimeOffset(Now)),
                State = new SeatOccupancyState(),
                Client = client,
                Directory = new SingleRunnerDirectory(client, remoteRunnerId: "server2"),
                WorkingSession = workingSession,
                BlockedSession = blockedSession,
                UnboundSession = unboundSession,
                WorkingTask = workingTask,
            };
        }

        private static SessionRunnerSessionDto Listed(Guid sessionId, int pid) =>
            new(sessionId, pid, RunnerStarted, "Running", null, AgentExitReason.Unknown, 0);

        private static AgentSession RemoteSession(Guid id) => new()
        {
            Id = id,
            DefinitionName = "grok",
            AgentKind = AgentKind.Grok,
            Status = SessionStatus.Running,
            Cwd = "/work",
            RunnerId = "server2",
            RunnerStoreId = Store,
            RunnerCwd = "/work/server2",
            Cols = 80,
            Rows = 24,
            CreatedAt = RunnerStarted,
            StartedAt = RunnerStarted,
            LastSeenAt = Now,
        };

        private static AgentTask RemoteTask(
            Guid id, Guid sessionId, AgentTaskStatus status, DateTime created, int attempt) => new()
        {
            Id = id,
            RootTaskId = id,
            AgentSessionId = sessionId,
            RunnerId = "server2",
            Status = status,
            Attempt = attempt,
            Title = "c1079",
            Goal = "seat sample",
            CreatedAt = created,
            DispatchedAt = created,
        };
    }

    private sealed class ListingClient : ISessionRunnerClient
    {
        public IReadOnlyList<SessionRunnerSessionDto> Sessions { get; set; } = [];
        public Exception? ListError { get; set; }
        public int Lists { get; private set; }

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
        {
            Lists++;
            return ListError is not null
                ? Task.FromException<IReadOnlyList<SessionRunnerSessionDto>>(ListError)
                : Task.FromResult(Sessions);
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

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
