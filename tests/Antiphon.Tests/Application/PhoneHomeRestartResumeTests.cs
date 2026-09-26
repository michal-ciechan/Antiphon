using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
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
/// CARD-0716 D-5. A restart scan re-attaches a remote Starting row the runner still holds,
/// and a row the runner never received stays failed with the shutdown's task event.
/// </summary>
[Category("Integration")]
public class PhoneHomeRestartResumeTests
{
    [Test]
    public async Task Starting_row_bound_to_the_runner_is_reattached_by_the_restart_scan()
    {
        await using var world = await ResumeWorld.CreateAsync(listOnRunner: true);
        var queue = world.Queue;
        await using var db = world.OpenDb();
        var service = SessionReconciliationServiceTests.BuildService(
            db, world.Host.Local, new MockEventBus(),
            ownership: queue,
            directory: world.Host.Directory);

        await service.ScanAsync(CancellationToken.None);
        await queue.WaitForIdleAsync(TimeSpan.FromSeconds(3), CancellationToken.None);

        var session = await world.ReadSessionAsync();
        session.Status.ShouldBe(SessionStatus.Running);
        world.Peer.RequestCount(PhoneHomeOperation.Get).ShouldBeGreaterThanOrEqualTo(1);
        world.Peer.Launches.Count.ShouldBe(0);
        world.Peer.RequestCount(PhoneHomeOperation.KillGeneration).ShouldBe(0);
    }

    [Test]
    public async Task Shutdown_records_the_interrupted_launch_and_the_scan_fails_the_pre_ack_row_with_the_restart_reason()
    {
        await using var world = await ResumeWorld.CreateAsync(listOnRunner: false);
        var started = DateTime.SpecifyKind((await world.ReadSessionAsync()).StartedAt, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(new DateTimeOffset(started.AddSeconds(120)));
        await using var db = world.OpenDb();
        var coordinator = world.Host.App.Services.GetRequiredService<OperatorShutdownCoordinator>();
        await coordinator.RecordInterruptedLaunchesAsync(db, "restart-apphost", CancellationToken.None);

        var service = SessionReconciliationServiceTests.BuildService(
            db, world.Host.Local, new MockEventBus(),
            ownership: world.Queue,
            directory: world.Host.Directory,
            time: clock);
        await service.ScanAsync(CancellationToken.None);

        var events = await world.ReadTaskWarningsAsync();
        events.Count.ShouldBe(1);
        events[0].ShouldContain("operator shutdown");
        events[0].ShouldContain(world.RunnerId);
        var session = await world.ReadSessionAsync();
        session.Status.ShouldBe(SessionStatus.Failed);
        session.FailureReason.ShouldContain("server restarted");
    }

    private sealed class ResumeWorld : IAsyncDisposable
    {
        private IsolatedTestSchema _schema = null!;
        private BridgeQueueHarness _harness = null!;

        public PhoneHomeTestHost Host { get; private set; } = null!;
        public PhoneHomeScriptedPeer Peer { get; private set; } = null!;
        public Guid SessionId { get; } = Guid.NewGuid();
        public Guid TaskId { get; } = Guid.NewGuid();
        public string RunnerId => Host.AllowedRunnerId;
        public AgentSessionLaunchQueue Queue =>
            _harness.Provider.GetRequiredService<AgentSessionLaunchQueue>();

        public static async Task<ResumeWorld> CreateAsync(bool listOnRunner)
        {
            var world = new ResumeWorld();
            world._schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            world.Host = await PhoneHomeTestHost.StartAsync(connectionString: world._schema.ConnectionString);
            world.Peer = await world.Host.ConnectPeerAsync();
            world.Host.Directory.MarkRecovered(await world.Host.WaitLiveAsync());
            var host = world.Host;
            world._harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                AlwaysOn = false,
                ConnectionString = world._schema.ConnectionString,
                ConfigureServices = s =>
                {
                    s.AddSingleton<IAgentProtocolAdapterFactory>(sp => new AgentProtocolAdapterFactory(
                        Options.Create(new AgentRegistrySettings()),
                        sp.GetRequiredService<ISessionRunnerClient>(),
                        directory: host.Directory));
                    s.AddSingleton<ISessionRunnerDirectory>(host.Directory);
                },
            });
            await world.SeedAsync(listOnRunner);
            return world;
        }

        public AppDbContext OpenDb() =>
            new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));

        public async Task<AgentSession> ReadSessionAsync()
        {
            await using var db = OpenDb();
            return await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == SessionId);
        }

        public async Task<List<string>> ReadTaskWarningsAsync()
        {
            await using var db = OpenDb();
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == TaskId && e.Type == AgentTaskEventType.Warning)
                .OrderBy(e => e.At)
                .Select(e => e.Detail ?? "")
                .ToListAsync();
        }

        private async Task SeedAsync(bool listOnRunner)
        {
            var now = DateTime.UtcNow;
            await using var db = OpenDb();
            db.AgentSessions.Add(new AgentSession
            {
                Id = SessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.Raw,
                SessionBackend = SessionBackend.PtyHost,
                Status = SessionStatus.Starting,
                Cwd = _harness.TempRoot,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                RunnerId = Host.AllowedRunnerId,
                RunnerStoreId = Host.StoreId,
                RunnerCwd = "/work",
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = TaskId,
                RootTaskId = TaskId,
                Title = "restart resume",
                Goal = "Do the thing.",
                Role = AgentTaskRole.Code,
                AgentKind = AgentKind.Raw,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = _harness.TempRoot,
                AgentSessionId = SessionId,
                AgentId = _harness.AgentId,
                Status = AgentTaskStatus.Dispatched,
                CreatedAt = now,
                DispatchedAt = now,
            });
            await db.SaveChangesAsync();
            var startedAt = (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == SessionId)).StartedAt;
            if (listOnRunner)
            {
                Peer.Sessions.Add(new RunnerSessionDto(
                    SessionId, 1, startedAt, "Running", null, "", 0, AcceptedStartedAt: startedAt));
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_harness is not null)
                await _harness.DisposeAsync();
            if (Peer is not null)
                await Peer.DisposeAsync();
            if (Host is not null)
                await Host.DisposeAsync();
            if (_schema is not null)
                await _schema.DisposeAsync();
        }
    }
}
