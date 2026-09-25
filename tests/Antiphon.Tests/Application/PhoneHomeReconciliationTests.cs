using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class PhoneHomeReconciliationTests
{
    [Test]
    public async Task Unavailable_owner_does_not_close_rows_or_block_local_scan()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var remote = await SeedAsync(db, "grok-linux", SessionStatus.Running);
        var originalRemoteStatus = remote.Status;
        var directory = new InventoryDirectory(new RunnerInventory.Unavailable("down"));
        var service = Build(db, directory);
        await service.ScanAsync(CancellationToken.None);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var row = await verify.AgentSessions.SingleAsync(s => s.Id == remote.Id);
        var remoteStatus = row.Status;
        remoteStatus.ShouldBe(originalRemoteStatus);
    }

    [Test]
    public async Task Every_pass_uses_only_its_owner_partition()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var other = await SeedAsync(db, "other-runner", SessionStatus.Running);
        var directory = new InventoryDirectory(new RunnerInventory.Available([]));
        var service = Build(db, directory);
        await service.ScanAsync(CancellationToken.None);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var otherOwnerChanges = await verify.AgentSessions.Where(s => s.Id == other.Id && s.Status != SessionStatus.Running).ToListAsync();
        otherOwnerChanges.ShouldBeEmpty();
    }

    [Test]
    public async Task Disconnect_after_list_blocks_absence_write()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var remote = await SeedAsync(db, "grok-linux", SessionStatus.Running);
        var flipping = new FlippingDirectory();
        var service = Build(db, flipping);
        await service.ScanAsync(CancellationToken.None);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var row = await verify.AgentSessions.SingleAsync(s => s.Id == remote.Id);
        row.Status.ShouldBe(SessionStatus.Running);
    }

    [Test]
    public async Task List_consumers_keep_remote_unknown_and_local_pids_separate()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var remote = await SeedAsync(db, "grok-linux", SessionStatus.Running);
        var local = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = @"C:\work",
            Cols = 80,
            Rows = 24,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            LastSeenAt = DateTime.UtcNow,
        };
        db.AgentSessions.Add(local);
        await db.SaveChangesAsync();
        var overlappingPid = 4242;
        var census = new RecordingCensus([
            new ZombieOsProcess(overlappingPid, 4, "Antiphon.PtyHost.exe", @"C:\x\Antiphon.PtyHost.exe", "Antiphon.PtyHost.exe", @"C:\src", DateTimeOffset.UtcNow, 1, 0),
        ]);
        var runner = new EmptyClient();
        var service = new Antiphon.Server.Infrastructure.Agents.ZombieCensusService(
            census,
            runner,
            db,
            new System.IO.Abstractions.FileSystem(),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new Antiphon.Server.Application.Settings.ZombieCensusSettings
            {
                SessionLogPath = Path.Combine(Path.GetTempPath(), "c490-census"),
            }));
        var result = await service.RunAsync(CancellationToken.None);
        var localDecisionsAttributedToRemote = result.Rows
            .Where(r => r.SessionId == remote.Id)
            .Select(r => overlappingPid)
            .ToList();
        localDecisionsAttributedToRemote.ShouldBeEmpty();
        result.Rows.ShouldNotContain(r => r.SessionId == remote.Id);
    }

    private static async Task<AgentSession> SeedAsync(AppDbContext db, string runnerId, SessionStatus status)
    {
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "grok",
            AgentKind = AgentKind.Grok,
            Status = status,
            Cwd = @"C:\work",
            Cols = 80,
            Rows = 24,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            LastSeenAt = DateTime.UtcNow,
            RunnerId = runnerId,
            RunnerStoreId = Guid.NewGuid(),
            RunnerCwd = "/work",
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    private static SessionReconciliationService Build(AppDbContext db, ISessionRunnerDirectory directory) =>
        new(
            db,
            directory.Local,
            new NullBus(),
            new NullAlerts(),
            new RunnerReachabilityState(),
            new SessionReAdoptionState(),
            new SessionGenerationCompatState(),
            new HerdrPendingAlertState(),
            new NullCensus(),
            new PtyHostCensusAlertState(),
            Options.Create(new SessionReconciliationSettings { Enabled = true, StartingGraceMs = 0 }),
            TimeProvider.System,
            NullLogger<SessionReconciliationService>.Instance,
            directory: directory);

    private sealed class InventoryDirectory(RunnerInventory inventory) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local { get; } = new EmptyClient();
        public IReadOnlyList<string> KnownRunnerIds => ["local", "grok-linux"];
        public Guid? GetLiveStoreId(string? runnerId) => null;
        public ISessionRunnerClient Resolve(string? runnerId) => Local;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult(runnerId is null ? new RunnerInventory.Available([]) : inventory);
    }

    private sealed class FlippingDirectory : ISessionRunnerDirectory
    {
        private int _calls;
        public ISessionRunnerClient Local { get; } = new EmptyClient();
        public IReadOnlyList<string> KnownRunnerIds => ["grok-linux"];
        public Guid? GetLiveStoreId(string? runnerId) => null;
        public ISessionRunnerClient Resolve(string? runnerId) => Local;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct)
        {
            _calls++;
            return Task.FromResult<RunnerInventory>(_calls == 1
                ? new RunnerInventory.Available([])
                : new RunnerInventory.Unavailable("disconnected"));
        }
    }

    private sealed class RecordingCensus(IReadOnlyList<ZombieOsProcess> processes) : IZombieProcessCensus
    {
        public Task<IReadOnlyList<ZombieOsProcess>> SnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(processes);
    }

    private sealed class EmptyClient : ISessionRunnerClient
    {
        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new KeyNotFoundException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => Empty();
        private static async IAsyncEnumerable<SessionRunnerEvent> Empty() { await Task.CompletedTask; yield break; }
    }

    private sealed class NullBus : IEventBus
    {
        public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NullAlerts : IAlertService
    {
        public Task RaiseAsync(AlertRaise raise, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NullCensus : IPtyHostCensusProbe
    {
        public PtyHostCensus Take() => PtyHostCensus.Unavailable;
    }
}
