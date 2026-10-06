using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Durable episode storage, dormant until the publication/release integrations are commissioned.
/// No Git, runner, queue, settlement or card mutation is performed here. State persistence does
/// not authorize any external operation; callers must obtain its separately bound evidence.
/// </summary>
public sealed class BlockedTaskParkingService(
    AppDbContext db, TimeProvider clock, IOptions<BlockedTaskParkingOptions> options)
{
    public async Task<Guid?> RegisterAsync(Guid taskId, int attempt, Guid blockEventId,
        Guid taskConcurrencyToken, CancellationToken ct)
    {
        if (!options.Value.Enabled || !CanOwnTransaction()) return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockTaskAsync(taskId, ct);
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || task.Status != AgentTaskStatus.Blocked || task.Attempt != attempt
            || task.ConcurrencyToken != taskConcurrencyToken) return null;
        var block = await LatestBlockAsync(taskId, ct);
        if (block?.Id != blockEventId) return null;

        // The task row lock serializes cooperating writers across scopes/processes. The unique
        // index independently protects the identity from noncooperating/direct writers.
        var existing = await db.AgentTaskParks.AsNoTracking().SingleOrDefaultAsync(p =>
            p.TaskId == taskId && p.Attempt == attempt && p.BlockEventId == blockEventId, ct);
        if (existing is not null) return existing.Id;

        var session = await db.AgentSessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == task.AgentSessionId, ct);
        var floor = task.AgentSessionId is null ? null : await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == task.AgentSessionId).MaxAsync(t => (long?)t.Sequence, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var baseline = Antiphon.Server.Application.Dtos.TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson)?.Primary;
        var park = new AgentTaskPark
        {
            Id = Guid.NewGuid(), TaskId = taskId, Attempt = attempt, BlockEventId = blockEventId,
            TaskConcurrencyToken = taskConcurrencyToken, AgentId = task.AgentId,
            SessionId = task.AgentSessionId, RunnerId = task.RunnerId,
            RunnerStoreId = session?.RunnerStoreId, AcceptedStartedAt = session?.StartedAt,
            Workspace = task.Workspace, WorktreeId = task.WorktreeId,
            WorktreePath = task.WorktreePath ?? task.WorkingDirectory ?? task.RepoPath,
            RemoteWorktreePath = task.RemoteWorktreePath,
            FullRef = task.WorktreeBranch is null ? baseline?.FullRef : "refs/heads/" + task.WorktreeBranch,
            BaselineSha = task.WorktreeBaseSha ?? baseline?.LocalSha, ReportReference = task.ResultFilePath,
            ReportDigest = await HandoffDigestAsync(db, task, floor, ct),
            TranscriptSequence = floor, BlockedAt = block.At, CompletedAt = task.CompletedAt,
            State = AgentTaskParkState.Requested, CreatedAt = now, UpdatedAt = now
        };
        db.AgentTaskParks.Add(park);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return park.Id;
    }

    /// <summary>
    /// Internal storage CAS for the later evidence-owning coordinator. Deliberately not an API
    /// or a release caller. Original task, report and generation coordinates are immutable.
    /// Reconciliation may persist already accepted work while new registration is disabled.
    /// </summary>
    internal async Task<bool> PersistStateAsync(Guid parkId, long revision, AgentTaskParkState expected,
        AgentTaskParkState next, string reasonCode, CancellationToken ct)
    {
        if (!CanOwnTransaction() || !Allowed(expected, next)) return false;
        if (string.IsNullOrEmpty(reasonCode) || reasonCode.Length > 64
            || reasonCode.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
            throw new ArgumentException("A bounded park reason code is required.", nameof(reasonCode));
        var snapshot = await db.AgentTaskParks.AsNoTracking().SingleOrDefaultAsync(p => p.Id == parkId, ct);
        if (snapshot is null) return false;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockTaskAsync(snapshot.TaskId, ct);
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == snapshot.TaskId, ct);
        if (task is null || task.Status != AgentTaskStatus.Blocked || task.Attempt != snapshot.Attempt
            || task.ConcurrencyToken != snapshot.TaskConcurrencyToken || task.AgentId != snapshot.AgentId
            || task.AgentSessionId != snapshot.SessionId || task.RunnerId != snapshot.RunnerId
            || task.ResultFilePath != snapshot.ReportReference
            || await HandoffDigestAsync(db, task, snapshot.TranscriptSequence, ct) != snapshot.ReportDigest
            || (await LatestBlockAsync(task.Id, ct))?.Id != snapshot.BlockEventId) return false;
        // Same task-before-session lock order as CARD-0667. No lock spans external I/O.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM "AgentSessions" WHERE "Id" = {snapshot.SessionId} FOR UPDATE
            """, ct);
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == snapshot.SessionId, ct);
        if (snapshot.SessionId is not null && (session is null || session.RunnerId != snapshot.RunnerId
            || session.RunnerStoreId != snapshot.RunnerStoreId
            || session.StartedAt != snapshot.AcceptedStartedAt)) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        var changed = await db.AgentTaskParks.Where(p => p.Id == parkId && p.Revision == revision && p.State == expected)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.State, next)
                .SetProperty(p => p.HeldFromState, next == AgentTaskParkState.Held ? expected : (AgentTaskParkState?)null)
                .SetProperty(p => p.ReasonCode, reasonCode).SetProperty(p => p.Revision, p => p.Revision + 1)
                .SetProperty(p => p.UpdatedAt, now)
                .SetProperty(p => p.PublishedAt, p => next == AgentTaskParkState.Published ? now : p.PublishedAt)
                .SetProperty(p => p.ReleasePendingAt, p => next == AgentTaskParkState.ReleasePending ? now : p.ReleasePendingAt)
                .SetProperty(p => p.ParkedAt, p => next == AgentTaskParkState.Parked ? now : p.ParkedAt)
                .SetProperty(p => p.ResumePendingAt, p => next == AgentTaskParkState.ResumePending ? now : p.ResumePendingAt)
                .SetProperty(p => p.ResumedAt, p => next == AgentTaskParkState.Resumed ? now : p.ResumedAt), ct);
        await tx.CommitAsync(ct);
        return changed == 1;
    }

    private bool CanOwnTransaction() => db.Database.CurrentTransaction is null
        && System.Transactions.Transaction.Current is null && !db.ChangeTracker.HasChanges();

    internal static string? Digest(string? value) => value is null ? null
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    // A nonreport hold retains the existing transcript through a fixed coordinate. Never
    // manufacture Result/CompletedAt, and never let later ingestion change this checkpoint.
    internal static async Task<string?> HandoffDigestAsync(AppDbContext db, AgentTask task,
        long? floor, CancellationToken ct)
    {
        if (task.Result is not null) return Digest(task.Result);
        if (task.AgentSessionId is null || floor is null) return null;
        var entries = await db.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == task.AgentSessionId && e.Sequence <= floor)
            .OrderBy(e => e.Sequence)
            .Select(e => new { e.Sequence, e.Kind, e.Uuid, e.Text, e.ToolName, e.ToolInput,
                e.ToolUseId, e.StopReason, e.ApiErrorClass }).ToListAsync(ct);
        return entries.Count == 0 ? null : Digest(JsonSerializer.Serialize(entries));
    }

    private Task<AgentTaskEvent?> LatestBlockAsync(Guid taskId, CancellationToken ct) =>
        db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id).FirstOrDefaultAsync(ct);

    private Task<int> LockTaskAsync(Guid taskId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM "AgentTasks" WHERE "Id" = {taskId} FOR UPDATE
            """, ct);

    private static bool Allowed(AgentTaskParkState from, AgentTaskParkState to) => (from, to) switch
    {
        (AgentTaskParkState.Requested, AgentTaskParkState.Published or AgentTaskParkState.Held) => true,
        (AgentTaskParkState.Published, AgentTaskParkState.ReleasePending or AgentTaskParkState.Held) => true,
        (AgentTaskParkState.Held, AgentTaskParkState.Requested) => true,
        (AgentTaskParkState.ReleasePending, AgentTaskParkState.Parked or AgentTaskParkState.ResumePending) => true,
        (AgentTaskParkState.Parked, AgentTaskParkState.ResumePending) => true,
        (AgentTaskParkState.ResumePending, AgentTaskParkState.Resumed) => true,
        _ => false
    };
}
