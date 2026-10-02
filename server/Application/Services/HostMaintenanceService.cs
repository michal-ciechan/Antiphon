using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Exceptions;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Durable, try-only admission for one bounded cleanup operation. Entry-point integration
/// remains required: maintenance must observe a drained intent before repository or process work.
/// Inventory never calls this service. This service cannot delete, kill or acquire a repository lease.
/// </summary>
public sealed class HostMaintenanceService(IHostMaintenanceStore store, IHostMaintenanceCustodyProbe probe,
    TimeProvider clock)
{
    public Task<HostMaintenanceState> ReadAsync(string storageId, string hostId, CancellationToken ct)
    {
        Require(Code(storageId, 200) && Code(hostId, 64));
        return store.ReadOrCreateAsync(storageId, hostId, ct);
    }

    public async Task<HostMaintenanceDecision> ReconcileAsync(string storageId, string hostId, CancellationToken ct)
    {
        var state = await ReadAsync(storageId, hostId, ct);
        HostMaintenanceCustody custody;
        try { custody = await probe.ReadAsync(state, ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { custody = HostMaintenanceCustody.Unknown; }
        var stopped = custody == HostMaintenanceCustody.Stopped;
        var replacement = state with
        {
            CustodyReconciled = stopped,
            OperationId = stopped ? null : state.OperationId,
            Worker = stopped ? null : state.Worker,
        };
        return await ReplaceAsync(state, replacement, stopped,
            stopped ? "custody_reconciled" : custody == HostMaintenanceCustody.Live ? "worker_live" : "custody_unknown", ct);
    }

    public async Task<HostMaintenanceDecision> RequestAsync(string storageId, string hostId, Guid intentId,
        string kind, CancellationToken ct)
    {
        Require(intentId != Guid.Empty && kind is "land" or "restart" or "deploy");
        var state = await ReadAsync(storageId, hostId, ct);
        if (state.IntentId is not null)
            return state.IntentId == intentId && state.Kind == kind ?
                new(true, DrainReason(state), state) : new(false, "maintenance_pending", state);
        if (state.Epoch == long.MaxValue) return new(false, "epoch_exhausted", state);
        // The intent and new epoch commit before the caller waits. Preserve the operation:
        // its bounded native work must finish, rather than be abandoned or killed here.
        var requested = state with { IntentId = intentId, Kind = kind,
            RequestedAt = clock.GetUtcNow().UtcDateTime, Epoch = state.Epoch + 1 };
        return await ReplaceAsync(state, requested, true, DrainReason(requested), ct);
    }

    public async Task<HostMaintenanceDecision> TryBeginCleanupAsync(string storageId, string hostId, long epoch,
        Guid operationId, HostMaintenanceWorkerIdentity worker, CancellationToken ct)
    {
        Require(operationId != Guid.Empty && worker is not null && Code(worker.StoreId, 200) &&
            Code(worker.BootId, 200) && worker.Pid > 0 && worker.StartToken > 0);
        var state = await ReadAsync(storageId, hostId, ct);
        if (state.Epoch != epoch) return new(false, "epoch_changed", state);
        if (state.IntentId is not null) return new(false, "maintenance_pending", state);
        if (!state.CustodyReconciled) return new(false, "custody_unknown", state);
        if (state.OperationId is not null) return new(false, "operation_active", state);
        return await ReplaceAsync(state, state with { OperationId = operationId, Worker = worker },
            true, "admitted", ct);
    }

    public async Task<HostMaintenanceDecision> CheckCleanupAsync(string storageId, string hostId, long epoch,
        Guid operationId, CancellationToken ct)
    {
        var state = await ReadAsync(storageId, hostId, ct);
        if (state.Epoch != epoch) return new(false, "epoch_changed", state);
        if (state.IntentId is not null) return new(false, "maintenance_pending", state);
        if (!state.CustodyReconciled || state.Worker is null) return new(false, "custody_unknown", state);
        return state.OperationId == operationId && operationId != Guid.Empty ?
            new(true, "admitted", state) : new(false, "operation_changed", state);
    }

    public async Task<HostMaintenanceDecision> CompleteCleanupAsync(string storageId, string hostId,
        Guid operationId, bool outcomeKnown, CancellationToken ct)
    {
        var state = await ReadAsync(storageId, hostId, ct);
        if (operationId == Guid.Empty || state.OperationId != operationId)
            return new(false, "operation_changed", state);
        // The unguessable operation ID belongs to the trusted bounded-operation caller. An
        // unknown result retains its custody even if maintenance changed the epoch meanwhile.
        var replacement = outcomeKnown ? state with { OperationId = null, Worker = null } :
            state with { CustodyReconciled = false };
        return await ReplaceAsync(state, replacement, outcomeKnown,
            outcomeKnown ? "operation_complete" : "custody_unknown", ct);
    }

    public async Task<HostMaintenanceDecision> FinishAsync(string storageId, string hostId, Guid intentId,
        bool outcomeKnown, CancellationToken ct)
    {
        var state = await ReadAsync(storageId, hostId, ct);
        if (intentId == Guid.Empty || state.IntentId != intentId) return new(false, "intent_changed", state);
        if (state.OperationId is not null) return new(false, "draining", state);
        if (!outcomeKnown) return await ReplaceAsync(state, state with { CustodyReconciled = false },
            false, "custody_unknown", ct);
        if (state.Epoch == long.MaxValue) return new(false, "epoch_exhausted", state);
        // A restarted process must positively reconcile again; successful maintenance is not
        // evidence that a prior remote worker or in-flight native operation stopped.
        return await ReplaceAsync(state, state with { IntentId = null, Kind = null, RequestedAt = null,
            Epoch = state.Epoch + 1, CustodyReconciled = false }, true, "maintenance_complete", ct);
    }

    private async Task<HostMaintenanceDecision> ReplaceAsync(HostMaintenanceState expected,
        HostMaintenanceState replacement, bool accepted, string reason, CancellationToken ct)
    {
        replacement = replacement with { Revision = Guid.NewGuid(), UpdatedAt = clock.GetUtcNow().UtcDateTime };
        if (await store.TryReplaceAsync(expected, replacement, ct)) return new(accepted, reason, replacement);
        return new(false, "gate_changed", await ReadAsync(expected.StorageId, expected.HostId, ct));
    }

    private static string DrainReason(HostMaintenanceState state) => !state.CustodyReconciled ?
        "custody_unknown" : state.OperationId is null ? "drained" : "draining";
    private static bool Code(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');
    private static void Require(bool valid)
    {
        if (!valid) throw new ValidationException("maintenance", "Invalid host maintenance identity.");
    }
}
