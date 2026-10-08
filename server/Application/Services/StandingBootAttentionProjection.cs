using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1156 D-6 (operator decision option A, detection only): the attention row for a taskless
/// AlwaysOn boot stall is DERIVED FROM CURRENT FACTS, not from a receipt. The sweep's
/// <c>standingBoot:v1;</c> incidents are optional history: a failed save, a receipt older than the
/// 24-hour recency window or one the prune deleted never hides a current episode, and a receipt
/// whose episode a model reply, a terminal session or a newer launch has resolved never shows one.
///
/// <para><b>Read-only.</b> No runner call, no queue operation, no arm, no save and no telemetry
/// write happens during a GET. The candidate reads are bulk (owners, live sessions, open tasks,
/// current receipts) and each candidate then costs the boot predicate's two reads over the loaded
/// row: the model-reply EXISTS and the prompt rows. The receipt read is optional: when it fails
/// (anything but the caller's cancellation) a Warning is logged and every episode is projected
/// from its clock alone, as if it had no receipt.</para>
///
/// <para>Admission is <see cref="StandingBootWatchPolicy.Decide"/>, the sweep's own whitelist, so
/// the row and the receipt can never disagree about who is watched. The episode identity is the
/// session's CURRENT latest real prompt on its CURRENT launch clock, never the armed watch columns:
/// a row whose watch an old raise cleared still projects, and a refined prompt is a new episode.</para>
///
/// <para>The Error stage is a bounded request for an operator decision, not a promise that anyone
/// noticed and not a recovery: the row offers the agent's own controls (<see cref="AttentionAction.OpenAgent"/>,
/// <see cref="AttentionAction.OpenDrawer"/>) and nothing task-shaped, and no deadline ends it.</para>
/// </summary>
internal static class StandingBootAttentionProjection
{
    private static readonly AgentTaskStatus[] OpenTaskStatuses =
        [AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked];

    /// <param name="Items">One <see cref="AttentionKind.LivenessProbeFailed"/> row per current episode.</param>
    /// <param name="Covered">
    /// Every live session an AlwaysOn agent points at, whether or not a stage is due. Legacy
    /// <c>bootSeq=</c> rows on these sessions are history of the retired restart ladder and are
    /// suppressed, both as attention rows and in the recent-incident sweep, so they can neither
    /// duplicate the current row nor show obsolete wording.
    /// </param>
    /// <param name="TaskOwned">Live sessions an open task owns: no boot row of either format.</param>
    /// <param name="AttachedReceipts">The current episodes' receipts, consumed as their row's own evidence.</param>
    internal sealed record Result(
        IReadOnlyList<AttentionItemDto> Items,
        IReadOnlySet<Guid> Covered,
        IReadOnlySet<Guid> TaskOwned,
        IReadOnlyList<Guid> AttachedReceipts)
    {
        internal static readonly Result Empty = new([], new HashSet<Guid>(), new HashSet<Guid>(), []);
    }

    internal static async Task<Result> ProjectAsync(
        AppDbContext db, DelegationSettings delegation, DateTime now, ILogger logger, CancellationToken ct)
    {
        // Every agent sharing a persistent pointer with an AlwaysOn agent, so the owner shape (count,
        // AlwaysOn, conflict) is read exactly as the sweep reads it. One read.
        var pointers = await db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId != null
                && db.Agents.Any(b => b.AlwaysOn && b.PersistentSessionId == a.PersistentSessionId))
            .Select(a => new { a.Id, a.Name, a.AlwaysOn, a.PersistentSessionId })
            .ToListAsync(ct);
        if (pointers.Count == 0)
            return Result.Empty;

        var owners = new Dictionary<Guid, List<(Guid Id, string Name, bool AlwaysOn)>>();
        foreach (var pointer in pointers)
        {
            if (!Guid.TryParse(pointer.PersistentSessionId, out var sessionId))
                continue;
            if (!owners.TryGetValue(sessionId, out var list))
                owners[sessionId] = list = [];
            list.Add((pointer.Id, pointer.Name, pointer.AlwaysOn));
        }

        if (owners.Count == 0)
            return Result.Empty;

        var ids = owners.Keys.ToList();
        var sessions = await db.AgentSessions.AsNoTracking()
            .Where(s => ids.Contains(s.Id) && StandingBootWatchObservation.LiveStatuses.Contains(s.Status))
            .ToListAsync(ct);
        if (sessions.Count == 0)
            return Result.Empty;

        var liveIds = sessions.Select(s => s.Id).ToList();
        var taskOwned = (await db.AgentTasks.AsNoTracking()
                .Where(t => t.AgentSessionId != null
                    && liveIds.Contains(t.AgentSessionId.Value)
                    && OpenTaskStatuses.Contains(t.Status))
                .Select(t => t.AgentSessionId!.Value)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        // Optional history: a failed read projects every episode as if it had no receipt (the
        // ordinary clock stage), so it can neither abort the feed nor hide a current row.
        ILookup<Guid, (Guid Id, Guid SessionId, string FailureReason)> receipts;
        try
        {
            receipts = (await db.AgentIncidents.AsNoTracking()
                    .Where(i => i.SessionId != null
                        && liveIds.Contains(i.SessionId.Value)
                        && i.Kind == AgentIncidentKind.LivenessProbeFailed
                        && i.FailureReason != null
                        && i.FailureReason.StartsWith(StandingBootWatchPolicy.KeyPrefix))
                    .Select(i => new { i.Id, SessionId = i.SessionId!.Value, i.FailureReason })
                    .ToListAsync(ct))
                .Select(r => (r.Id, r.SessionId, r.FailureReason!))
                .ToLookup(r => r.SessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(
                ex, "Could not read the standing boot receipts; current boot attention is projected without them");
            receipts = Array.Empty<(Guid Id, Guid SessionId, string FailureReason)>().ToLookup(r => r.SessionId);
        }

        var items = new List<AttentionItemDto>();
        var attached = new List<Guid>();
        foreach (var session in sessions)
        {
            // An open task owns the session's boot silence: CARD-1151's task row, not this one.
            if (taskOwned.Contains(session.Id))
                continue;

            var sessionOwners = owners[session.Id];
            var observation = StandingBootWatchObservation.FromRow(
                session,
                sessionOwners.Select(o => new StandingBootOwner(o.Id, o.AlwaysOn)).ToList(),
                StandingBootTaskOwner.None,
                delegation.BootModelWaitDeadlineMinutes,
                delegation.ModelWaitDeadlineMinutes,
                now);
            if (observation.LaunchClock is not DateTime clock
                || observation.GrokRules is GrokRulesState.Pending or GrokRulesState.Failed)
            {
                continue;
            }

            // The boot predicate over the loaded row, revalidated now: any model row on this launch
            // resolves the episode, whoever produced it.
            if (await BootReplyWatch.HasModelReplySinceAsync(db, session.Id, clock, ct))
                continue;

            var turn = await BootReplyWatch.LoadPromptTurnAsync(db, session.Id, clock, ct);
            var decision = StandingBootWatchPolicy.Decide(observation with
            {
                ReplyObserved = false,
                PromptSequence = turn?.Turn.PromptSequence,
                PromptAt = turn?.Turn.PromptAt,
                PromptKind = turn?.LatestKind,
                // The identity IS the current latest real prompt; the armed columns are not consulted.
                IdentityMatches = turn is not null,
            });
            // Facts are set only when every whitelist condition holds; Stage.None with facts means the
            // episode is valid and unresolved but no stage is due on the clock alone.
            if (decision.Facts is not { } facts || observation.OwnerAgentId is not Guid agentId)
                continue;

            var prefix = StandingBootWatchPolicy.EpisodePrefix(facts);
            var episodeReceipts = receipts[session.Id]
                .Where(r => r.FailureReason.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
            // Once the operator stage is on record for THIS episode it stays the displayed stage,
            // even before the boot due, so a clock stepping back cannot downgrade or hide the row.
            var stage = StandingBootWatchPolicy.IsRecorded(
                    episodeReceipts.Select(r => (string?)r.FailureReason), prefix, StandingBootWatchPolicy.Stage.NeedsOperator)
                ? StandingBootWatchPolicy.Stage.NeedsOperator
                : decision.Stage;
            if (stage == StandingBootWatchPolicy.Stage.None)
                continue;
            attached.AddRange(episodeReceipts.Select(r => r.Id));

            var name = sessionOwners.First(o => o.Id == agentId).Name;
            items.Add(Item(session.Id, agentId, name, facts, turn!.LatestKind, stage, now));
        }

        return new Result(items, owners.Keys.Where(liveIds.Contains).ToHashSet(), taskOwned, attached);
    }

    private static AttentionItemDto Item(
        Guid sessionId, Guid agentId, string agentName, StandingBootFacts facts, string promptKind,
        StandingBootWatchPolicy.Stage stage, DateTime now)
    {
        var age = now - facts.PromptAt;
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;
        var waited = StandingBootWatchPolicy.Describe(age);
        var headline = stage == StandingBootWatchPolicy.Stage.NeedsOperator
            ? $"Standing boot stall needs an operator decision: no model reply {waited} after the prompt."
            : $"Standing boot stall detected: no model reply {waited} after the prompt.";
        // Only the latest prompt record's kind, sequence and time are known here; nothing matches it
        // against the intended request, so the row states no delivery verdict either way.
        var prompt = promptKind == TranscriptKinds.QueuedUserPrompt
            ? $"Queued prompt record #{facts.PromptSequence} at {facts.PromptAt:u}, {waited} ago; no reply observed."
            : $"Prompt #{facts.PromptSequence} ({promptKind}) at {facts.PromptAt:u}, {waited} ago; no assistant, "
                + "thinking, tool or turn-end row since.";
        var lines = new[]
        {
            "Inspect the session or its transcript, then choose: keep waiting, reply through the session, "
            + "or explicitly Stop and Start/resume the agent.",
            "Detection only: the session keeps running and keeps its seat; nothing is stopped, typed, "
            + "restarted or latched automatically, and no deadline ends this episode.",
            prompt,
            $"Boot notice due {facts.BootDueAt:u}.",
            $"Operator decision due {facts.OperatorDueAt:u}.",
        };

        return new AttentionItemDto(
            AttentionKind.LivenessProbeFailed,
            stage == StandingBootWatchPolicy.Stage.NeedsOperator ? AlertSeverity.Error : AlertSeverity.Warning,
            null,
            sessionId,
            agentId,
            null,
            agentName,
            headline,
            string.Join("\n", lines),
            facts.PromptAt,
            null,
            [AttentionAction.OpenAgent, AttentionAction.OpenDrawer]);
    }

    /// <summary>
    /// The receipt keys of every CURRENT unresolved standing episode, for
    /// <c>AgentSupervisorService.PruneIncidentsAsync</c> (D-5): the prune must never delete the dedup
    /// evidence of an episode that is still open, or the next sweep would re-mint it.
    ///
    /// <para>This checks episode RESOLUTION, not the notification whitelist: a disabled deadline, a
    /// changed AlwaysOn flag, a moved pointer or a bound task does not prove the episode ended, so
    /// the candidates are simply the live sessions that hold a <c>standingBoot:v1;</c> receipt. An
    /// episode is released only by a positive fact: the session is no longer live, a model row
    /// landed on its launch, or its generation, launch clock or latest real prompt moved on (a new
    /// episode). One candidate read, then the boot predicate's two reads per candidate; no receipts
    /// are loaded. A fault is the caller's to handle, and it must then retain every receipt.</para>
    /// </summary>
    internal static async Task<IReadOnlyList<string>> CurrentEpisodeKeysAsync(AppDbContext db, CancellationToken ct)
    {
        var candidates = await db.AgentSessions.AsNoTracking()
            .Where(s => StandingBootWatchObservation.LiveStatuses.Contains(s.Status)
                && db.AgentIncidents.Any(i => i.SessionId == s.Id
                    && i.Kind == AgentIncidentKind.LivenessProbeFailed
                    && i.FailureReason != null
                    && i.FailureReason.StartsWith(StandingBootWatchPolicy.KeyPrefix)))
            .ToListAsync(ct);

        var keys = new List<string>();
        foreach (var session in candidates)
        {
            if (session.StartedAt == default)
                continue;
            var clock = BootReplyWatch.LaunchClock(session);
            if (await BootReplyWatch.HasModelReplySinceAsync(db, session.Id, clock, ct))
                continue;
            var turn = await BootReplyWatch.LoadPromptTurnAsync(db, session.Id, clock, ct);
            if (turn is null)
                continue;

            var prefix = StandingBootWatchPolicy.EpisodePrefix(
                SessionGeneration.Normalize(session.StartedAt), clock, turn.Turn.PromptSequence);
            keys.Add(StandingBootWatchPolicy.Key(prefix, StandingBootWatchPolicy.Stage.Detected));
            keys.Add(StandingBootWatchPolicy.Key(prefix, StandingBootWatchPolicy.Stage.NeedsOperator));
        }

        return keys;
    }
}
