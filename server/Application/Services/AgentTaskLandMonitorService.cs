using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class AgentTaskLandMonitorService(AppDbContext db, TimeProvider clock,
    IOptions<DelegationSettings> settings, IEventBus events, AgentTaskLandQueue? queue = null,
    IRepositoryMutationLease? leases = null)
{
    public async Task SweepAsync(CancellationToken ct)
    {
        var ids = await db.AgentTaskLandRequests.AsNoTracking().Where(r => r.IsPending)
            .Select(r => r.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == id, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {request.TaskId} FOR UPDATE", ct);
            await db.Entry(request).ReloadAsync(ct);
            if (!request.IsPending) continue;
            var now = clock.GetUtcNow().UtcDateTime;
            request.LastEvaluatedAt = now;
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == request.TaskId, ct);
            request.ReconciliationError = task.CurrentLandRequestId != request.Id || task.LandRequestedAt != request.RequestedAt
                || task.LandAttempt != request.Attempt ? "land_request_mirror_disagreement" : null;
            var age = (now - request.LastProgressAt).TotalSeconds;
            var operation = age >= settings.Value.LandWarningSeconds && task.ActiveLandingId is Guid operationId
                ? await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == operationId && o.TaskId == task.Id, ct)
                : null;
            var changed = false;
            if (age >= settings.Value.LandWarningSeconds && request.WarningAt is null)
            { request.WarningAt = now; await AddAgedAsync(request, operation, "Warning", now, ct); changed = true; }
            if (age >= settings.Value.LandErrorSeconds && request.ErrorAt is null)
            { request.ErrorAt = now; await AddAgedAsync(request, operation, "Error", now, ct); changed = true; }
            request.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            if (changed) await events.PublishToAllAsync("AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);
        }
        // Publication is terminal, receipt is not. Its clock starts at the outcome commit.
        var outcomes = await db.AgentTaskLandNotifications.AsNoTracking().Where(n => !n.IsLegacy && n.Kind == LandNotificationKind.Outcome
            && n.ConfirmedAt == null && n.State != LandNotificationState.NotRequired && n.ErrorAt == null)
            .Select(n => new { n.Id, n.TaskId }).ToListAsync(ct);
        foreach (var outcome in outcomes)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {outcome.TaskId} FOR UPDATE", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTaskLandNotifications\" WHERE \"Id\" = {outcome.Id} FOR UPDATE", ct);
            var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == outcome.Id, ct);
            await db.Entry(note).ReloadAsync(ct);
            if (note.ConfirmedAt is not null || note.State == LandNotificationState.NotRequired) continue;
            var now = clock.GetUtcNow().UtcDateTime;
            var age = (now - note.CreatedAt).TotalSeconds;
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == note.RequestId, ct);
            var original = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == note.SourceEventId, ct);
            var changed = false;
            if (age >= settings.Value.LandWarningSeconds && note.WarningAt is null)
            { note.WarningAt = now; AddReceiptAged(request, note, original, "Warning", now); changed = true; }
            if (age >= settings.Value.LandErrorSeconds && note.ErrorAt is null)
            { note.ErrorAt = now; AddReceiptAged(request, note, original, "Error", now); changed = true; }
            if (!changed) continue;
            note.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            var rootId = await db.AgentTasks.Where(t => t.Id == outcome.TaskId).Select(t => t.RootTaskId).SingleAsync(ct);
            await events.PublishToAllAsync("AgentTaskChanged", new { taskId = outcome.TaskId, rootId }, ct);
        }
    }

    private void AddReceiptAged(AgentTaskLandRequest request, AgentTaskLandNotification note, AgentTaskEvent original, string severity, DateTime now)
    {
        var source = new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = request.TaskId, LandRequestId = request.Id,
            Type = AgentTaskEventType.LandAged, At = now,
            LandingOperationId = original.LandingOperationId, LandingPublication = original.LandingPublication,
            LandingCleanup = original.LandingCleanup, LandingMode = original.LandingMode,
            Detail = $"{severity}: outcome receipt unconfirmed; notification={note.Id:N}; outcome committed={note.CreatedAt:O}; destination={note.ParentSessionId:N}; queue={note.QueueMessageId:N}; state={note.State}; error={note.LastErrorCode}." };
        db.AgentTaskEvents.Add(source);
        db.AgentTaskLandNotifications.Add(LandNotificationPayload.Create(request, source, LandNotificationKind.Aged));
    }

    private async Task AddAgedAsync(AgentTaskLandRequest request, AgentTaskLanding? operation, string severity, DateTime now, CancellationToken ct)
    {
        var source = new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = request.TaskId, LandRequestId = request.Id,
            Type = AgentTaskEventType.LandAged, At = now,
            LandingOperationId = operation?.Id, LandingPublication = operation?.Publication,
            LandingCleanup = operation?.Cleanup, LandingMode = operation?.Mode,
            Detail = $"{severity}: Land {await DescribeWaitingAsync(request, now, ct)}",
        };
        db.AgentTaskEvents.Add(source);
        db.AgentTaskLandNotifications.Add(LandNotificationPayload.Create(request, source, LandNotificationKind.Aged));
    }

    private async Task<string> DescribeWaitingAsync(AgentTaskLandRequest request, DateTime now, CancellationToken ct)
    {
        var reason = string.IsNullOrWhiteSpace(request.HoldReasonCode) ? "none" : request.HoldReasonCode;
        var snapshot = queue?.Capture(now);
        var queueClause = DescribeQueue(request, snapshot, now);
        var who = snapshot is null || snapshot.Executing is null || snapshot.IsExecuting(request.TaskId, request.Id)
            || snapshot.WaitingPosition(request.TaskId, request.Id) is null
            ? DescribePersistedHolder(request)
            : await DescribePredecessorAsync(request, snapshot, ct);
        who = await AppendLeaseOwnerAsync(request, who, ct);
        return $"{request.State}; requested {request.RequestedAt:O}; no progress since {request.LastProgressAt:O}; "
            + $"attempt={request.Attempt}; request={request.Id:N}; reason={reason}; {queueClause}; {who}.";
    }

    private async Task<string> AppendLeaseOwnerAsync(AgentTaskLandRequest request, string who, CancellationToken ct)
    {
        if (leases is null || string.IsNullOrWhiteSpace(request.RepositoryPathSnapshot))
            return who;
        RepositoryLeaseOwner? observed;
        try
        {
            observed = await leases.FindOwnerAsync(request.RepositoryPathSnapshot, ct);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            return who;
        }

        if (observed?.State == RepositoryLeaseOwnerState.Known)
        {
            var id = observed.TaskId is Guid taskId ? taskId.ToString("N") : "none";
            return who + $"; repository lease owner={id} purpose={observed.Purpose}";
        }

        if (observed?.State == RepositoryLeaseOwnerState.Untagged)
            return who + "; repository lease owner=untagged in-process";
        if (observed?.State == RepositoryLeaseOwnerState.Unknown
            && request.HoldReasonCode == "repository_mutation_lease_busy"
            && request.HoldingTaskId is Guid previous)
            return who + $"; last-known lease owner={previous:N}";
        return who;
    }

    private static string DescribeQueue(AgentTaskLandRequest request, LandQueueSnapshot? snapshot, DateTime now)
    {
        if (snapshot is null)
            return $"queue position=unknown (awaiting replay); observed={now:O}";
        if (snapshot.IsExecuting(request.TaskId, request.Id))
            return $"queue position=running; waiting={snapshot.WaitingCount}; observed={now:O}";
        var position = snapshot.WaitingPosition(request.TaskId, request.Id);
        return position is int waiting
            ? $"queue position={waiting}; waiting={snapshot.WaitingCount}; observed={now:O}"
            : $"queue position=unknown (awaiting replay); observed={now:O}";
    }

    private async Task<string> DescribePredecessorAsync(AgentTaskLandRequest request, LandQueueSnapshot snapshot, CancellationToken ct)
    {
        var executing = snapshot.Executing ?? throw new InvalidOperationException("queue predecessor requires an executing entry");
        var predecessor = executing.RequestId is Guid requestId
            ? await db.AgentTaskLandRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == requestId, ct)
            : null;
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == executing.TaskId, ct);
        var status = task?.Status.ToString() ?? "unknown";
        var state = predecessor?.State.ToString() ?? "unknown";
        var requestLabel = executing.RequestId is Guid id ? id.ToString("N") : "unknown";
        if (SameRepository(request.RepositoryPathSnapshot, predecessor?.RepositoryPathSnapshot))
            return $"holder={executing.TaskId:N} request={requestLabel} status={status} state={state}";

        var blocker = $"queue blocker={executing.TaskId:N} request={requestLabel} status={status} state={state}";
        if (request.HoldingTaskId is Guid owner)
            blocker += $"; historical repository owner={owner:N} status={request.HoldingTaskStatus?.ToString() ?? "unknown"}";
        return blocker;
    }

    private static string DescribePersistedHolder(AgentTaskLandRequest request)
    {
        if (request.HoldingTaskId is Guid holder)
            return $"historical holder={holder:N} status={request.HoldingTaskStatus?.ToString() ?? "unknown"}";
        return "holder=unknown";
    }

    private static bool SameRepository(string? left, string? right) =>
        TryNormalize(left, out var a) && TryNormalize(right, out var b)
        && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool TryNormalize(string? path, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
