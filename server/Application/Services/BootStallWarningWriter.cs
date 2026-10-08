using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1151 D-3: the boot-stall Warning event, one per episode key and stage. Telemetry only:
/// it runs in its own short-lived context and transaction so nothing it stages can ride a later
/// save of the sweep's context, it holds the task row lock while it checks the episode identity
/// and the existing key, and every fault short of the caller's own cancellation is logged and
/// swallowed. It never changes a task's status, attempt, binding or failure fields, and its
/// result is never an input to the disposition.
///
/// <para><b>The whole episode identity is revalidated immediately before the insert</b>
/// (CARD-1151 R2): the task's attempt, session and dispatch under the task row lock, then the
/// session's accepted generation and launch clock under a share lock on the session row, then
/// the latest accepted prompt through the same boot predicate the decision read. A relaunch,
/// resume, newer accepted prompt or model reply between the decision and the write therefore
/// writes nothing, silently. The session lock is taken with SKIP LOCKED so this telemetry write
/// can never wait on, or deadlock with, a session write: a held row reads as no row, which is
/// treated as a changing one. Contention is therefore an ordinary empty result, not a database
/// error, and logs nothing above Debug (CARD-1151 repair 2: NOWAIT raised 55P03, which EF Core
/// logged at Error before any handler ran).</para>
/// </summary>
internal sealed class BootStallWarningWriter(Func<AppDbContext> contexts, IEventBus events, ILogger logger)
{
    internal enum Outcome
    {
        Recorded = 0,
        Duplicate = 1,
        IdentityChanged = 2,
        Faulted = 3,
    }

    /// <summary>
    /// The episode identity the event belongs to, as the sweep observed it. <paramref name="Boot"/>
    /// carries the generation, launch clock and accepted prompt that the key encodes; they are
    /// compared field by field, never through the opaque key.
    /// </summary>
    internal sealed record Episode(
        Guid TaskId, Guid RootTaskId, int Attempt, Guid SessionId, DateTime? DispatchedAt,
        BootStallFacts Boot, string Key);

    /// <summary>
    /// A-7's cheap read: is this stage (or a higher one) already on record for the key? A fault
    /// answers false, which only costs the caller the runner pull it would have made anyway.
    /// </summary>
    internal async Task<bool> IsRecordedAsync(Episode episode, BootStallPolicy.Stage stage, CancellationToken ct)
    {
        try
        {
            await using var db = contexts();
            return BootStallPolicy.IsRecorded(await ReadDetailsAsync(db, episode.TaskId, ct), episode.Key, stage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(
                ex, "Could not read boot-stall warnings for task {ShortId}; the episode is re-checked under the lock",
                DelegationReportFormatter.Short(episode.TaskId));
            return false;
        }
    }

    internal async Task<Outcome> RecordAsync(
        Episode episode, BootStallPolicy.Stage stage, string detail, DateTime now, CancellationToken ct)
    {
        try
        {
            await using (var db = contexts())
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var current = await db.AgentTasks
                    .FromSqlInterpolated($"SELECT * FROM \"AgentTasks\" WHERE \"Id\" = {episode.TaskId} FOR UPDATE")
                    .AsNoTracking()
                    .SingleOrDefaultAsync(ct);
                if (current is null
                    || current.Status is not (AgentTaskStatus.Dispatched or AgentTaskStatus.Working)
                    || current.Attempt != episode.Attempt
                    || current.AgentSessionId != episode.SessionId
                    || current.DispatchedAt != episode.DispatchedAt)
                {
                    return Outcome.IdentityChanged;
                }

                if (BootStallPolicy.IsRecorded(await ReadDetailsAsync(db, episode.TaskId, ct), episode.Key, stage))
                    return Outcome.Duplicate;

                if (!await SameEpisodeAsync(db, episode, stage, current.DispatchedAt, ct))
                    return Outcome.IdentityChanged;

                db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(),
                    AgentTaskId = episode.TaskId,
                    Type = AgentTaskEventType.Warning,
                    Detail = ColumnText.Clip(detail, 4000),
                    At = now,
                });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The context and its transaction are disposed above; nothing staged survives.
            logger.LogWarning(
                ex, "Could not record {Token} for task {ShortId}; detection is unchanged and the next tick retries",
                BootStallPolicy.Token(stage), DelegationReportFormatter.Short(episode.TaskId));
            return Outcome.Faulted;
        }

        logger.LogWarning(
            "Task {ShortId}: {Token} on session {SessionId} (episode {Key}); detection only, the session "
            + "is not stopped, failed, retried or released",
            DelegationReportFormatter.Short(episode.TaskId), BootStallPolicy.Token(stage),
            episode.SessionId, episode.Key);
        try
        {
            await events.PublishToAllAsync(
                "AgentTaskChanged", new { taskId = episode.TaskId, rootId = episode.RootTaskId }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not publish the boot-stall change of task {ShortId}",
                DelegationReportFormatter.Short(episode.TaskId));
        }

        return Outcome.Recorded;
    }

    /// <summary>
    /// R2: the session's generation and launch clock (session row share-locked, SKIP LOCKED), then
    /// the latest accepted prompt with no model reply since, all equal to the decided episode. A
    /// missing or write-locked row, a missing generation or a resolved turn is a mismatch.
    /// </summary>
    private async Task<bool> SameEpisodeAsync(
        AppDbContext db, Episode episode, BootStallPolicy.Stage stage, DateTime? dispatchedAt, CancellationToken ct)
    {
        var session = await db.AgentSessions
            .FromSqlInterpolated($"SELECT * FROM \"AgentSessions\" WHERE \"Id\" = {episode.SessionId} FOR SHARE SKIP LOCKED")
            .AsNoTracking()
            .Select(s => new { s.StartedAt, s.LaunchResumedAt })
            .SingleOrDefaultAsync(ct);
        if (session is null)
        {
            // Gone, or being written right now (a held row is skipped): its identity is in flux.
            // Nothing is written; the next tick decides again on the settled row.
            logger.LogDebug(
                "Task {ShortId}: session {SessionId} is missing or mid-update; {Token} not recorded this tick",
                DelegationReportFormatter.Short(episode.TaskId), episode.SessionId, BootStallPolicy.Token(stage));
            return false;
        }

        if (episode.Boot.SessionStartedAt is not DateTime generation
            || session.StartedAt != generation)
        {
            return false;
        }

        var clock = TaskDeadlinePolicy.LaunchClock(dispatchedAt, session.LaunchResumedAt);
        if (clock != episode.Boot.LaunchClock)
            return false;

        var turn = await BootReplyWatch.LoadBootTurnAsync(db, episode.SessionId, clock, ct);
        return turn?.AcceptedSequence == episode.Boot.PromptSequence
            && turn.AcceptedAt == episode.Boot.PromptAt;
    }

    private static Task<List<string>> ReadDetailsAsync(AppDbContext db, Guid taskId, CancellationToken ct) =>
        db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId
                && e.Type == AgentTaskEventType.Warning
                && e.Detail.StartsWith("BootStall"))
            .Select(e => e.Detail)
            .ToListAsync(ct);
}
