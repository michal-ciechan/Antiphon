using System.Collections.Concurrent;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1759. Reconcile lookups compiled once per EF model. A compiled query is bound to the
/// model it first ran against, and a test process builds more than one; production has one.
/// The map caches the plan, not the row. Each call reads the database.
/// </summary>
internal static class LandNotificationReconcileQueries
{
    private static readonly ConcurrentDictionary<IModel, Func<AppDbContext, Guid, CancellationToken, Task<AgentTaskLandNotification>>> NoteByIdQuery = new();
    private static readonly ConcurrentDictionary<IModel, Func<AppDbContext, Guid, CancellationToken, Task<LandSessionScan?>>> SessionScanByIdQuery = new();
    private static readonly ConcurrentDictionary<IModel, Func<AppDbContext, Guid, CancellationToken, Task<SessionStatus>>> SessionStatusByIdQuery = new();
    private static readonly ConcurrentDictionary<IModel, Func<AppDbContext, Guid, CancellationToken, Task<DateTime>>> SessionStartedAtByIdQuery = new();
    private static readonly ConcurrentDictionary<IModel, Func<AppDbContext, Guid, CancellationToken, Task<DateTime?>>> SessionStartedAtOrDefaultByIdQuery = new();
    private static readonly ConcurrentDictionary<IModel, Func<AppDbContext, Guid, CancellationToken, Task<string>>> SessionCwdByIdQuery = new();
    private static readonly ConcurrentDictionary<IModel, Func<AppDbContext, Guid, CancellationToken, Task<Guid>>> RootTaskIdByIdQuery = new();
    private static readonly ConcurrentDictionary<IModel, Func<AppDbContext, Guid, CancellationToken, Task<SessionQueuedMessage?>>> MessageBySourceNotificationQuery = new();
    private static readonly ConcurrentDictionary<IModel, Func<AppDbContext, Guid, CancellationToken, Task<SessionQueuedMessage>>> MessageByIdQuery = new();
    private static readonly ConcurrentDictionary<IModel, Func<AppDbContext, Guid, CancellationToken, Task<SessionQueuedMessage?>>> MessageOrDefaultByIdQuery = new();
    private static readonly ConcurrentDictionary<IModel, Func<AppDbContext, DateTime, IAsyncEnumerable<Guid>>> DueNoteIdsQuery = new();
    private static readonly ConcurrentDictionary<IModel, Func<AppDbContext, DateTime, Guid, IAsyncEnumerable<Guid>>> DueNoteIdsAfterQuery = new();

    internal static Task<AgentTaskLandNotification> NoteById(AppDbContext db, Guid id, CancellationToken ct) =>
        NoteByIdQuery.GetOrAdd(db.Model, static _ =>
            EF.CompileAsyncQuery((AppDbContext db, Guid id, CancellationToken ct) =>
                db.AgentTaskLandNotifications.Single(n => n.Id == id)))(db, id, ct);

    internal static Task<LandSessionScan?> SessionScanById(AppDbContext db, Guid id, CancellationToken ct) =>
        SessionScanByIdQuery.GetOrAdd(db.Model, static _ =>
            EF.CompileAsyncQuery((AppDbContext db, Guid id, CancellationToken ct) =>
                db.AgentSessions.Where(s => s.Id == id)
                    .Select(s => new LandSessionScan(s.Status, s.StartedAt))
                    .SingleOrDefault()))(db, id, ct);

    internal static Task<SessionStatus> SessionStatusById(AppDbContext db, Guid id, CancellationToken ct) =>
        SessionStatusByIdQuery.GetOrAdd(db.Model, static _ =>
            EF.CompileAsyncQuery((AppDbContext db, Guid id, CancellationToken ct) =>
                db.AgentSessions.Where(s => s.Id == id).Select(s => s.Status).Single()))(db, id, ct);

    internal static Task<DateTime> SessionStartedAtById(AppDbContext db, Guid id, CancellationToken ct) =>
        SessionStartedAtByIdQuery.GetOrAdd(db.Model, static _ =>
            EF.CompileAsyncQuery((AppDbContext db, Guid id, CancellationToken ct) =>
                db.AgentSessions.Where(s => s.Id == id).Select(s => s.StartedAt).Single()))(db, id, ct);

    internal static Task<DateTime?> SessionStartedAtOrDefaultById(AppDbContext db, Guid id, CancellationToken ct) =>
        SessionStartedAtOrDefaultByIdQuery.GetOrAdd(db.Model, static _ =>
            EF.CompileAsyncQuery((AppDbContext db, Guid id, CancellationToken ct) =>
                db.AgentSessions.AsNoTracking().Where(s => s.Id == id)
                    .Select(s => (DateTime?)s.StartedAt)
                    .SingleOrDefault()))(db, id, ct);

    internal static Task<string> SessionCwdById(AppDbContext db, Guid id, CancellationToken ct) =>
        SessionCwdByIdQuery.GetOrAdd(db.Model, static _ =>
            EF.CompileAsyncQuery((AppDbContext db, Guid id, CancellationToken ct) =>
                db.AgentSessions.AsNoTracking().Where(s => s.Id == id).Select(s => s.Cwd).Single()))(db, id, ct);

    internal static Task<Guid> RootTaskIdById(AppDbContext db, Guid id, CancellationToken ct) =>
        RootTaskIdByIdQuery.GetOrAdd(db.Model, static _ =>
            EF.CompileAsyncQuery((AppDbContext db, Guid id, CancellationToken ct) =>
                db.AgentTasks.Where(t => t.Id == id).Select(t => t.RootTaskId).Single()))(db, id, ct);

    internal static Task<SessionQueuedMessage?> MessageBySourceNotification(AppDbContext db, Guid noteId, CancellationToken ct) =>
        MessageBySourceNotificationQuery.GetOrAdd(db.Model, static _ =>
            EF.CompileAsyncQuery((AppDbContext db, Guid noteId, CancellationToken ct) =>
                db.SessionQueuedMessages.AsNoTracking().SingleOrDefault(m => m.SourceLandNotificationId == noteId)))(db, noteId, ct);

    internal static Task<SessionQueuedMessage> MessageById(AppDbContext db, Guid id, CancellationToken ct) =>
        MessageByIdQuery.GetOrAdd(db.Model, static _ =>
            EF.CompileAsyncQuery((AppDbContext db, Guid id, CancellationToken ct) =>
                db.SessionQueuedMessages.AsNoTracking().Single(m => m.Id == id)))(db, id, ct);

    internal static Task<SessionQueuedMessage?> MessageOrDefaultById(AppDbContext db, Guid id, CancellationToken ct) =>
        MessageOrDefaultByIdQuery.GetOrAdd(db.Model, static _ =>
            EF.CompileAsyncQuery((AppDbContext db, Guid id, CancellationToken ct) =>
                db.SessionQueuedMessages.AsNoTracking().SingleOrDefault(m => m.Id == id)))(db, id, ct);

    internal static IAsyncEnumerable<Guid> DueNoteIds(AppDbContext db, DateTime now) =>
        DueNoteIdsQuery.GetOrAdd(db.Model, static _ =>
            EF.CompileAsyncQuery((AppDbContext db, DateTime now) =>
                db.AgentTaskLandNotifications.AsNoTracking().Where(n =>
                        n.State != LandNotificationState.Confirmed
                        && n.State != LandNotificationState.NotRequired
                        && n.State != LandNotificationState.LegacyUnverified
                        && (n.QueueMessageId != null
                            || n.NextAttemptAt <= now
                            || (n.Kind == LandNotificationKind.ProviderInputWait
                                && n.ReplyTo != AgentTaskReplyTo.Session)))
                    .OrderBy(n => n.Id).Select(n => n.Id).Take(128)))(db, now);

    internal static IAsyncEnumerable<Guid> DueNoteIdsAfter(AppDbContext db, DateTime now, Guid after) =>
        DueNoteIdsAfterQuery.GetOrAdd(db.Model, static _ =>
            EF.CompileAsyncQuery((AppDbContext db, DateTime now, Guid after) =>
                db.AgentTaskLandNotifications.AsNoTracking().Where(n =>
                        n.State != LandNotificationState.Confirmed
                        && n.State != LandNotificationState.NotRequired
                        && n.State != LandNotificationState.LegacyUnverified
                        && n.Id.CompareTo(after) > 0
                        && (n.QueueMessageId != null
                            || n.NextAttemptAt <= now
                            || (n.Kind == LandNotificationKind.ProviderInputWait
                                && n.ReplyTo != AgentTaskReplyTo.Session)))
                    .OrderBy(n => n.Id).Select(n => n.Id).Take(128)))(db, now, after);
}

/// <summary>Status and start of one session, projected so the reconcile does not track the row.</summary>
internal sealed record LandSessionScan(SessionStatus Status, DateTime StartedAt);
