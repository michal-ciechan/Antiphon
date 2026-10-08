using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1156 D-5: the taskless AlwaysOn boot receipt, one <see cref="AgentIncidentKind.LivenessProbeFailed"/>
/// row per episode and stage. Telemetry only, on the <see cref="BootStallWarningWriter"/> shape: its
/// own short-lived context and transaction, so nothing it stages can ride a later save of the
/// sweep's context, and every fault short of the caller's own cancellation is logged and isolated.
/// It never changes the session, the agent, its supervision state, a task or a queue row, and its
/// result is never an input to anything that could stop, restart, type into or release a session.
///
/// <para><b>The whole episode is revalidated under the session row lock immediately before the
/// insert</b>: the session is still live on the same generation and launch clock, exactly one
/// AlwaysOn agent still points at it, no open task owns it, the episode/stage is not already on
/// record, and the boot predicate still names the same prompt with no reply. Any change committed
/// before that read writes nothing. The lock is <c>FOR UPDATE SKIP LOCKED</c>, so two sweeps cannot
/// both insert (the second skips the held row) and a session write in flight is never waited on: a
/// held row reads as no row, logged at Debug, and the next tick decides again. No runner call is
/// made while the lock is held.</para>
/// </summary>
internal sealed class StandingBootWarningWriter(Func<AppDbContext> contexts, IEventBus? events, ILogger logger)
{
    internal enum Outcome
    {
        Recorded = 0,
        Duplicate = 1,
        IdentityChanged = 2,
        Contended = 3,
        Faulted = 4,
    }

    /// <summary>The episode as the sweep observed it; compared field by field under the lock.</summary>
    internal sealed record Episode(Guid SessionId, Guid AgentId, StandingBootFacts Facts, string PromptKind)
    {
        public string Prefix => StandingBootWatchPolicy.EpisodePrefix(Facts);
    }

    internal async Task<Outcome> RecordAsync(
        Episode episode, StandingBootWatchPolicy.Stage stage, int bootWaitMinutes, int modelWaitMinutes,
        DateTime now, CancellationToken ct)
    {
        var key = StandingBootWatchPolicy.Key(episode.Prefix, stage);
        try
        {
            await using (var db = contexts())
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var session = await db.AgentSessions
                    .FromSqlInterpolated($"SELECT * FROM \"AgentSessions\" WHERE \"Id\" = {episode.SessionId} FOR UPDATE SKIP LOCKED")
                    .AsNoTracking()
                    .SingleOrDefaultAsync(ct);
                if (session is null)
                {
                    logger.LogDebug(
                        "Session {SessionId} is missing or mid-update; standing boot receipt {Key} not recorded this tick",
                        episode.SessionId, key);
                    return Outcome.Contended;
                }

                var owners = await StandingBootWatchObservation.ReadOwnersAsync(db, episode.SessionId, ct);
                var taskOwner = await StandingBootWatchObservation.ReadTaskOwnerAsync(db, episode.SessionId, ct);
                var current = StandingBootWatchObservation.FromRow(
                    session, owners, taskOwner, bootWaitMinutes, modelWaitMinutes, now);
                if (!SameOwnerAndSession(current, episode))
                    return Outcome.IdentityChanged;

                var recorded = await db.AgentIncidents.AsNoTracking()
                    .Where(i => i.SessionId == episode.SessionId
                        && i.Kind == AgentIncidentKind.LivenessProbeFailed
                        && i.FailureReason != null
                        && i.FailureReason.StartsWith(StandingBootWatchPolicy.KeyPrefix))
                    .Select(i => i.FailureReason)
                    .ToListAsync(ct);
                if (StandingBootWatchPolicy.IsRecorded(recorded, episode.Prefix, stage))
                    return Outcome.Duplicate;

                var turn = await BootReplyWatch.LoadBootTurnAsync(db, session, episode.Facts.LaunchClock, ct);
                if (turn is null
                    || turn.PromptSequence != episode.Facts.PromptSequence
                    || turn.PromptAt != episode.Facts.PromptAt)
                {
                    return Outcome.IdentityChanged;
                }

                db.AgentIncidents.Add(new AgentIncident
                {
                    Id = Guid.NewGuid(),
                    AgentId = episode.AgentId,
                    SessionId = episode.SessionId,
                    Kind = AgentIncidentKind.LivenessProbeFailed,
                    Severity = stage == StandingBootWatchPolicy.Stage.NeedsOperator
                        ? AlertSeverity.Error
                        : AlertSeverity.Warning,
                    Message = ColumnText.Clip(
                        StandingBootWatchPolicy.Message(episode.Facts, episode.PromptKind, now),
                        AgentIncident.MessageMaxLength),
                    FailureReason = key,
                    CreatedAt = now,
                });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The context and its transaction are disposed above; nothing staged survives.
            logger.LogWarning(
                ex, "Could not record standing boot receipt {Key} for session {SessionId}; detection is "
                + "unchanged, the session keeps its seat and the next tick retries",
                key, episode.SessionId);
            return Outcome.Faulted;
        }

        logger.LogWarning(
            "Boot reply never came on standing session {SessionId} ({Stage}, episode {Key}); detection only, "
            + "the session is not stopped, restarted, typed into or latched",
            episode.SessionId, StandingBootWatchPolicy.StageName(stage), key);
        if (events is not null)
        {
            try
            {
                await events.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(episode.AgentId), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(
                    ex, "Could not publish the standing boot receipt of session {SessionId}", episode.SessionId);
            }
        }

        return Outcome.Recorded;
    }

    /// <summary>
    /// The locked row and its owners still describe the decided episode: live, not ended, same
    /// generation and launch clock, provider and rules still admitted, exactly one AlwaysOn owner
    /// and it is the same agent, no stamped-owner conflict, and no open task.
    /// </summary>
    private static bool SameOwnerAndSession(StandingBootWatchObservation current, Episode episode) =>
        current.ProviderVerified == true
        && (current.GrokRules is GrokRulesState.None or GrokRulesState.Ready)
        && current.SessionLive == true
        && current.EndedAtNull == true
        && current.Generation == episode.Facts.Generation
        && current.LaunchClock == episode.Facts.LaunchClock
        && current.OwnerCount == 1
        && current.OwnerAlwaysOn == true
        && current.OwnerAgentId == episode.AgentId
        && current.StandingAgentConflict == false
        && current.TaskOwner == StandingBootTaskOwner.None;
}
