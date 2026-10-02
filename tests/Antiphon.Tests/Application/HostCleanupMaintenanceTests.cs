using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class HostCleanupMaintenanceTests
{
    private static readonly HostMaintenanceWorkerIdentity Worker = new("store-a", "boot-a", 42, 1234);
    private sealed class Probe : IHostMaintenanceCustodyProbe
    {
        public HostMaintenanceCustody Result { get; set; } = HostMaintenanceCustody.Stopped;
        public Task<HostMaintenanceCustody> ReadAsync(HostMaintenanceState state, CancellationToken ct) =>
            Task.FromResult(Result);
    }
    private static HostMaintenanceService Service(HostCleanupServerFixture f, AppDbContext db, Probe? probe = null) =>
        new(new HostMaintenanceStore(db, f.Clock), probe ?? new(), f.Clock);
    private static async Task SeedAsync(HostCleanupServerFixture f, bool reconciled = true,
        Guid? operation = null, Guid? intent = null)
    {
        await using var db = f.Db();
        db.HostMaintenanceActivities.Add(new HostMaintenanceActivity
        { StorageId = "storage-a", HostId = "host-a", Epoch = 7, Revision = Guid.NewGuid(),
            CustodyReconciled = reconciled, CleanupOperationId = operation,
            WorkerStoreId = operation is null ? null : Worker.StoreId,
            WorkerBootId = operation is null ? null : Worker.BootId,
            WorkerPid = operation is null ? null : Worker.Pid,
            WorkerStartToken = operation is null ? null : Worker.StartToken,
            MaintenanceIntentId = intent, MaintenanceKind = intent is null ? null : "land",
            MaintenanceRequestedAt = intent is null ? null : f.Clock.UtcNow.UtcDateTime,
            UpdatedAt = f.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task Land_intent_prevents_cleanup_admission()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        var intent = Guid.NewGuid(); await SeedAsync(f, intent: intent);
        await using var db = f.Db(); var service = Service(f, db);
        var decision = await service.TryBeginCleanupAsync("storage-a", "host-a", 7, Guid.NewGuid(), Worker, default);
        decision.Accepted.ShouldBeFalse(); decision.Reason.ShouldBe("maintenance_pending", "C826.land-intent-veto");
        (await service.ReadAsync("storage-a", "host-a", default)).OperationId.ShouldBeNull();
    }

    [Test]
    public async Task Restart_intent_prevents_cleanup_admission()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync(); await SeedAsync(f);
        await using var db = f.Db(); var service = Service(f, db); var intent = Guid.NewGuid();
        var requested = await service.RequestAsync("storage-a", "host-a", intent, "restart", default);
        requested.Accepted.ShouldBeTrue("C826.restart-intent-committed");
        requested.State.Epoch.ShouldBe(8); requested.State.IntentId.ShouldBe(intent);
        var admission = await service.TryBeginCleanupAsync("storage-a", "host-a", 8, Guid.NewGuid(), Worker, default);
        admission.Reason.ShouldBe("maintenance_pending"); admission.State.OperationId.ShouldBeNull();
    }

    [Test]
    public async Task Cleanup_yields_to_pending_maintenance()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync(); var operation = Guid.NewGuid();
        await SeedAsync(f, operation: operation);
        await using var db = f.Db(); var service = Service(f, db);
        var intent = await service.RequestAsync("storage-a", "host-a", Guid.NewGuid(), "land", default);
        intent.Reason.ShouldBe("draining", "C826.bounded-operation-still-owned");
        intent.State.OperationId.ShouldBe(operation);
        (await service.CheckCleanupAsync("storage-a", "host-a", 7, operation, default)).Accepted.ShouldBeFalse();
        var next = await service.TryBeginCleanupAsync("storage-a", "host-a", intent.State.Epoch,
            Guid.NewGuid(), Worker, default);
        next.Reason.ShouldBe("maintenance_pending");
        (await service.CompleteCleanupAsync("storage-a", "host-a", operation, true, default)).State.OperationId.ShouldBeNull();
        var drained = await service.ReadAsync("storage-a", "host-a", default);
        drained.IntentId.ShouldBe(intent.State.IntentId); drained.Epoch.ShouldBe(8);
    }

    [Test]
    public async Task Stale_epoch_refuses_delete()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync(); await SeedAsync(f);
        await using var db = f.Db(); var service = Service(f, db);
        var refusal = await service.TryBeginCleanupAsync("storage-a", "host-a", 6, Guid.NewGuid(), Worker, default);
        refusal.Reason.ShouldBe("epoch_changed", "C826.stale-epoch-refused"); refusal.State.OperationId.ShouldBeNull();
        var operation = Guid.NewGuid();
        (await service.TryBeginCleanupAsync("storage-a", "host-a", 7, operation, Worker, default))
            .Accepted.ShouldBeTrue();
        (await service.CheckCleanupAsync("storage-a", "host-a", 6, operation, default)).Reason.ShouldBe("epoch_changed");
        (await service.CheckCleanupAsync("storage-a", "host-a", 7, operation, default)).Accepted.ShouldBeTrue();
    }

    [Test]
    public async Task Lost_connection_does_not_expire_live_claim()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync(); var operation = Guid.NewGuid();
        await SeedAsync(f, operation: operation); f.Clock.UtcNow = f.Clock.UtcNow.AddDays(365);
        await using var db = f.Db(); var service = Service(f, db, new() { Result = HostMaintenanceCustody.Unknown });
        var admission = await service.TryBeginCleanupAsync("storage-a", "host-a", 7, Guid.NewGuid(),
            Worker with { Pid = 43 }, default);
        admission.Reason.ShouldBe("operation_active", "C826.elapsed-time-is-not-death");
        (await service.ReconcileAsync("storage-a", "host-a", default)).Accepted.ShouldBeFalse();
        (await service.ReadAsync("storage-a", "host-a", default)).OperationId.ShouldBe(operation);
    }

    [Test]
    public async Task Restart_requires_positive_custody_reconciliation()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync(); var operation = Guid.NewGuid();
        await SeedAsync(f, reconciled: false, operation: operation);
        await using var db = f.Db(); var probe = new Probe { Result = HostMaintenanceCustody.Unknown };
        var service = Service(f, db, probe);
        var admission = await service.TryBeginCleanupAsync("storage-a", "host-a", 7, Guid.NewGuid(), Worker, default);
        admission.Reason.ShouldBe("custody_unknown", "C826.restart-fails-closed");
        (await service.ReconcileAsync("storage-a", "host-a", default)).Accepted.ShouldBeFalse();
        probe.Result = HostMaintenanceCustody.Live;
        (await service.ReconcileAsync("storage-a", "host-a", default)).Accepted.ShouldBeFalse();
        probe.Result = HostMaintenanceCustody.Stopped;
        var reconciled = await service.ReconcileAsync("storage-a", "host-a", default);
        reconciled.Accepted.ShouldBeTrue(); reconciled.State.OperationId.ShouldBeNull();
        (await service.TryBeginCleanupAsync("storage-a", "host-a", 7, Guid.NewGuid(), Worker, default)).Accepted.ShouldBeTrue();
    }

    [Test]
    public async Task Independent_clients_observe_the_same_host_gate()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync(); await SeedAsync(f);
        await using var a = f.Db(); await using var b = f.Db();
        var first = Service(f, a); var second = Service(f, b);
        var snapshot = await first.ReadAsync("storage-a", "host-a", default);
        var intentId = Guid.NewGuid();
        (await second.RequestAsync("storage-a", "alias-of-host-a", intentId, "deploy", default))
            .Accepted.ShouldBeTrue("C826.second-client-publishes-shared-epoch");
        // First context already read the old row. A fresh read and CAS must use durable state.
        (await first.TryBeginCleanupAsync("storage-a", "host-a", snapshot.Epoch, Guid.NewGuid(), Worker, default))
            .Accepted.ShouldBeFalse();
        var current = await first.ReadAsync("storage-a", "host-a", default);
        current.IntentId.ShouldBe(intentId); current.Epoch.ShouldBe(snapshot.Epoch + 1);
        var staleReplace = snapshot with { Revision = Guid.NewGuid(), OperationId = Guid.NewGuid(), Worker = Worker };
        (await new HostMaintenanceStore(a, f.Clock).TryReplaceAsync(snapshot, staleReplace, default)).ShouldBeFalse();
        (await second.ReadAsync("storage-a", "host-a", default)).OperationId.ShouldBeNull();
    }

    [Test]
    public async Task Gate_is_released_on_success_and_failure()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync(); var operation = Guid.NewGuid();
        await SeedAsync(f, operation: operation); await using var db = f.Db(); var service = Service(f, db);
        var uncertain = await service.CompleteCleanupAsync("storage-a", "host-a", operation, false, default);
        uncertain.Reason.ShouldBe("custody_unknown", "C826.unknown-outcome-retains-operation");
        uncertain.State.OperationId.ShouldBe(operation); uncertain.State.CustodyReconciled.ShouldBeFalse();
        (await service.CompleteCleanupAsync("storage-a", "host-a", Guid.NewGuid(), true, default)).Accepted.ShouldBeFalse();
        var complete = await service.CompleteCleanupAsync("storage-a", "host-a", operation, true, default);
        complete.Accepted.ShouldBeTrue(); complete.State.OperationId.ShouldBeNull(); complete.State.Worker.ShouldBeNull();
        complete.State.CustodyReconciled.ShouldBeFalse();
        await service.ReconcileAsync("storage-a", "host-a", default);
        var intent = Guid.NewGuid(); await service.RequestAsync("storage-a", "host-a", intent, "restart", default);
        (await service.FinishAsync("storage-a", "host-a", intent, false, default)).Accepted.ShouldBeFalse();
        (await service.ReadAsync("storage-a", "host-a", default)).IntentId.ShouldBe(intent);
        var finished = await service.FinishAsync("storage-a", "host-a", intent, true, default);
        finished.Accepted.ShouldBeTrue(); finished.State.Epoch.ShouldBe(9); finished.State.CustodyReconciled.ShouldBeFalse();
    }

    [Test]
    public async Task Inventory_never_holds_land_admission()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync(); await SeedAsync(f);
        await using var db = f.Db(); var service = Service(f, db);
        var probe = new PausedInventoryProbe();
        var inventory = new HostCleanupWorktreeInventory(new WorktreeIgnoredContentClassifier(
            Options.Create(new WorktreeCleanupSettings())), f.Clock);
        var scan = inventory.ReadAsync("host-a", probe, probe);
        try
        {
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            scan.IsCompleted.ShouldBeFalse();
            var intent = await service.RequestAsync("storage-a", "host-a", Guid.NewGuid(), "land", default)
                .WaitAsync(TimeSpan.FromSeconds(30));
            intent.Reason.ShouldBe("drained", "C826.inventory-does-not-own-destructive-gate");
            intent.State.OperationId.ShouldBeNull();
        }
        finally { probe.Release.TrySetResult(); await scan; }
    }

    private sealed class PausedInventoryProbe : IHostCleanupWorktreeProbe, IHostCleanupExistingOwnerStatusProbe
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<HostCleanupWorktreePage> ReadPageAsync(string hostId, string? cursor, int take,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
            return new([], null, true);
        }
        public Task<string?> ReadAvailabilityReasonAsync(string owner, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }
}
