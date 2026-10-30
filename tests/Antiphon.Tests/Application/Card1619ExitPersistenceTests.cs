using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NativeLinuxOnly("CARD-1619 real Linux PTY to committed server storage")]
[ParallelLimiter<ProcessSpawnLimit>]
public class Card1619ExitPersistenceTests
{
    [Test]
    [Arguments(9, 0)]
    [Arguments(15, 0)]
    [Arguments(0, 0)]
    [Arguments(0, 23)]
    public async Task Native_exit_reaches_database_and_api(int signal, int code)
    {
        var (native, json) = await Card1619NativeExitFixture.CaptureAsync(signal, code);
        await using var lifetime = native;
        var evt = RunnerContractMapper.ParseEvent(SessionRunnerEventNames.SessionExited, json)!.Exited!;
        evt.ExitSignal.ShouldBe(signal, "real runner signal survives server event mapping");
        new RunnerContractMapper().Map(native.Snapshot).ExitSignal.ShouldBe(signal);
        await using var store = await Store.CreateAsync(native.Id, native.Generation);
        (await store.Runtime.ObserveExitAsync(evt, default)).ShouldBe(SessionExitDisposition.Applied);
        await using var db = store.Db();
        var row = await db.AgentSessions.SingleAsync();
        row.ExitCode.ShouldBe(code);
        Signal(row).ShouldBe(signal);
        // Exercise the API's actual card/session projection and web JSON optional contract.
        var card = new Card { AgentSessions = [row] };
        var api = BoardService.ToCardDto(card).Sessions.Single();
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(api, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        wire.RootElement.GetProperty("exitSignal").GetInt32().ShouldBe(signal);
        // A later old-runner replay cannot erase a known observation.
        await store.Runtime.ObserveExitAsync(evt with { ExitSignal = null }, default);
        await store.Runtime.ObserveExitAsync(evt with { ExitSignal = 127, ExitCode = 99 }, default);
        await using var reloaded = store.Db();
        var retained = await reloaded.AgentSessions.SingleAsync();
        Signal(retained).ShouldBe(signal);
        retained.ExitCode.ShouldBe(code);
        using var replay = JsonDocument.Parse(JsonSerializer.Serialize(store.Bus.PublishedEvents.Last(e => e.EventName == "SessionExited").Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        replay.RootElement.GetProperty("exitSignal").GetInt32().ShouldBe(signal);
    }

    [Test]
    public async Task Live_event_and_reconciliation_agree()
    {
        var (native, json) = await Card1619NativeExitFixture.CaptureAsync(15);
        await using var lifetime = native;
        var evt = RunnerContractMapper.ParseEvent(SessionRunnerEventNames.SessionExited, json)!.Exited!;
        await using var live = await Store.CreateAsync(native.Id, native.Generation);
        await using var reconciled = await Store.CreateAsync(native.Id, native.Generation);
        await live.Runtime.ObserveExitAsync(evt, default);
        await using (var db = reconciled.Db())
        {
            var runner = new SessionReconciliationServiceTests.FakeRunnerClient {
                Sessions = [new RunnerContractMapper().Map(native.Snapshot)] };
            await SessionReconciliationServiceTests.BuildService(db, runner, new MockEventBus(), launchResumeEnabled: false)
                .ScanAsync(default);
        }
        await using var a = live.Db();
        await using var b = reconciled.Db();
        Signal(await a.AgentSessions.SingleAsync()).ShouldBe(15);
        Signal(await b.AgentSessions.SingleAsync()).ShouldBe(15);
        var published = live.Bus.PublishedEvents.Single(e => e.EventName == "SessionExited");
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(published.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        payload.RootElement.GetProperty("exitSignal").GetInt32().ShouldBe(15);
    }

    [Test]
    public async Task Terminal_row_is_enriched_without_changing_request_source()
    {
        var (native, json) = await Card1619NativeExitFixture.CaptureAsync(9);
        await using var lifetime = native;
        await using var store = await Store.CreateAsync(native.Id, native.Generation, terminal: true);
        var wire = JsonSerializer.Deserialize<RunnerSessionExitedEvent>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!
            with { ExitStopIntent = Intent(native) };
        var evt = RunnerContractMapper.ParseEvent(SessionRunnerEventNames.SessionExited,
            JsonSerializer.Serialize(wire, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.Exited!;
        await store.Runtime.ObserveExitAsync(evt, default);
        await store.Runtime.ObserveExitAsync(evt with { ExitStopIntent = null, ExitSignal = null }, default);
        await using var db = store.Db();
        var row = await db.AgentSessions.SingleAsync();
        Signal(row).ShouldBe(9);
        row.ExitCode.ShouldBe(0);
        row.Status.ShouldBe(SessionStatus.Stopped);
        row.TerminationSource.ShouldBe(SessionTerminationSource.OperatorRequest);
        row.FailureReason.ShouldBe("retained request detail");
        var storedIntent = SessionExitEvidence.ReadIntent((string?)Property(row, "ExitStopIntentJson"));
        storedIntent.ShouldBe(evt.ExitStopIntent);
        await using (var recovered = await Store.CreateAsync(native.Id, native.Generation, terminal: true))
        {
            await using (var scan = recovered.Db())
            {
                var runner = new SessionReconciliationServiceTests.FakeRunnerClient { Sessions = [
                    new RunnerContractMapper().Map(native.Snapshot) with { ExitStopIntent = evt.ExitStopIntent }
                ] };
                await SessionReconciliationServiceTests.BuildService(scan, runner, new MockEventBus(), launchResumeEnabled: false).ScanAsync(default);
            }
            await using var verify = recovered.Db();
            var terminal = await verify.AgentSessions.SingleAsync();
            Signal(terminal).ShouldBe(9);
            terminal.Status.ShouldBe(SessionStatus.Stopped);
            terminal.TerminationSource.ShouldBe(SessionTerminationSource.OperatorRequest);
            SessionExitEvidence.ReadIntent((string?)Property(terminal, "ExitStopIntentJson")).ShouldBe(storedIntent);
        }
        var manifestPath = Antiphon.PtyHost.Protocol.PtyHostManifest.PathFor(Path.Combine(native.Root, "pty-hosts", "manifests"), native.Id);
        var manifest = Antiphon.PtyHost.Protocol.PtyHostManifest.TryLoad(manifestPath)!;
        (manifest with { ExitStopIntent = Intent(native) with { Provenance = ProcessStopProvenance.LiveIssuer } }).SaveAtomic(manifestPath);
        Antiphon.PtyHost.Protocol.PtyHostManifest.TryLoad(manifestPath)!.ExitStopIntent!.Provenance.ShouldBe(ProcessStopProvenance.RecoveredUnverified);
    }

    [Test]
    public async Task Resume_and_stale_events_keep_generation_boundary()
    {
        var (native, json) = await Card1619NativeExitFixture.CaptureAsync(0, 23);
        await using var lifetime = native;
        await using var store = await Store.CreateAsync(native.Id, native.Generation);
        var evt = RunnerContractMapper.ParseEvent(SessionRunnerEventNames.SessionExited, json)!.Exited!;
        evt = evt with { ExitStopIntent = Intent(native) };
        await store.Runtime.ObserveExitAsync(evt, default);
        var next = SessionGeneration.Next(native.Generation, DateTime.UtcNow);
        // Use production re-adoption's reset path: positive running snapshot, same accepted generation.
        await using (var db = store.Db())
        {
            var runner = new SessionReconciliationServiceTests.FakeRunnerClient { Sessions = [
                new RunnerContractMapper().Map(native.Snapshot) with { Status = "Running", ExitCode = null, ExitSignal = null }
            ] };
            await SessionReconciliationServiceTests.BuildService(db, runner, new MockEventBus(), launchResumeEnabled: false).ScanAsync(default);
        }
        await using (var db = store.Db())
        {
            var row = await db.AgentSessions.SingleAsync();
            Signal(row).ShouldBeNull("production re-adoption clears old evidence");
            row.ExitCode.ShouldBeNull();
            Property(row, "ExitStopIntentJson").ShouldBeNull();
            row.StartedAt = next;
            await db.SaveChangesAsync();
        }
        (await store.Runtime.ObserveExitAsync(evt, default)).ShouldBe(SessionExitDisposition.Stale);
        await using (var db = store.Db()) Signal(await db.AgentSessions.SingleAsync()).ShouldBeNull();
        await store.Runtime.ObserveExitAsync(evt with { AcceptedStartedAt = next, ExitSignal = 15 }, default);
        await using (var db = store.Db()) Signal(await db.AgentSessions.SingleAsync()).ShouldBe(15);
    }

    [Test]
    public async Task Migration_preserves_old_rows_and_nullable_contract()
    {
        // Upgrade itself is the subject: one isolated clone is taken back to the previous migration.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var migrations = db.Database.GetMigrations().ToArray();
        var added = migrations.SingleOrDefault(m => m.EndsWith("_AddSessionExitSignalEvidence", StringComparison.Ordinal));
        added.ShouldNotBeNull("the additive nullable migration must exist");
        var index = Array.IndexOf(migrations, added);
        var migrator = db.GetService<IMigrator>();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var phase = TestTiming.BeginPhase(TestPhaseKind.DbMigrate, "card1619-nullable-upgrade");
        var id = Guid.NewGuid();
        db.AgentSessions.Add(new AgentSession { Id = id, DefinitionName = "legacy", Cwd = "/tmp", ExitCode = 23,
            CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct.Token);
        await migrator.MigrateAsync(migrations[index - 1], ct.Token);
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync(added, ct.Token);
        db.ChangeTracker.Clear();
        var row = await db.AgentSessions.SingleAsync(s => s.Id == id, ct.Token);
        row.DefinitionName.ShouldBe("legacy");
        row.ExitCode.ShouldBe(23);
        Signal(row).ShouldBeNull();
        Property(row, "ExitStopIntentJson").ShouldBeNull();
        foreach (var entity in new[] { typeof(AgentSession), typeof(RunAttempt), typeof(AgentIncident) })
            db.Model.FindEntityType(entity)!.FindProperty("ExitSignal")!.IsNullable.ShouldBeTrue();
        db.Model.FindEntityType(typeof(AgentSession))!.FindProperty("ExitStopIntentJson")!.IsNullable.ShouldBeTrue();
    }

    private static ProcessStopIntentDto Intent(Card1619NativeExitFixture native) => new() {
        SessionId = native.Id, AcceptedStartedAt = native.Generation, RequestId = Guid.NewGuid(), IssuerBootId = Guid.NewGuid(),
        HostInstanceId = Guid.NewGuid(), RequestedAtUtc = DateTime.UtcNow, AttemptedSignal = 9,
        Target = ProcessStopTarget.Child, Path = ProcessStopPath.ProcessTree, Outcome = ProcessStopOutcome.Requested,
        Provenance = ProcessStopProvenance.RecoveredUnverified,
    };

    private static object? Property(object value, string name)
    {
        var property = value.GetType().GetProperty(name);
        property.ShouldNotBeNull($"{value.GetType().Name}.{name} stores nullable exit evidence");
        return property!.GetValue(value);
    }
    private static int? Signal(AgentSession value) => (int?)Property(value, "ExitSignal");

    private sealed class Store : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly ServiceProvider _provider;
        public MockEventBus Bus { get; } = new();
        public AgentSessionRuntime Runtime { get; }
        private Store(IsolatedTestSchema schema)
        {
            _schema = schema;
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString, n => n.SetPostgresVersion(16, 0)));
            _provider = services.BuildServiceProvider();
            Runtime = new AgentSessionRuntime(Bus, Options.Create(new AgentSessionSettings { SessionLogPath = Path.Combine(Path.GetTempPath(), "card1619-server-" + Guid.NewGuid().ToString("N")) }),
                _provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<AgentSessionRuntime>.Instance);
        }
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
        public static async Task<Store> CreateAsync(Guid id, DateTime generation, bool terminal = false)
        {
            var value = new Store(await TestDbFixture.CreateIsolatedSchemaAsync());
            await using var db = value.Db();
            db.AgentSessions.Add(new AgentSession { Id = id, DefinitionName = "raw", Cwd = "/tmp", CreatedAt = generation, StartedAt = generation, LastSeenAt = generation,
                Status = terminal ? SessionStatus.Stopped : SessionStatus.Running,
                TerminationSource = terminal ? SessionTerminationSource.OperatorRequest : SessionTerminationSource.Unknown,
                FailureReason = terminal ? "retained request detail" : null });
            await db.SaveChangesAsync();
            return value;
        }
        public async ValueTask DisposeAsync() { await _provider.DisposeAsync(); await _schema.DisposeAsync(); }
    }
}
