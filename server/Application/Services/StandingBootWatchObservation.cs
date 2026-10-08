using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>The open task that owns a session, if any (CARD-1156 D-3 item 4).</summary>
internal enum StandingBootTaskOwner
{
    None = 0,
    Queued = 1,
    Dispatched = 2,
    Working = 3,
    Blocked = 4,
}

/// <summary>
/// What the standing boot sweep knows when it decides (CARD-1156 D-3). Every nullable field is
/// "unknown when null", and <see cref="StandingBootWatchPolicy.Decide"/> never admits unknown.
/// Working is deliberately absent: it is not an admission input.
/// </summary>
/// <param name="ProviderVerified">The provider's delivery verification is Supported.</param>
/// <param name="SessionLive">Null when the row is missing; false for a terminal status.</param>
/// <param name="Generation"><c>SessionGeneration.Normalize(StartedAt)</c>; null when unaccepted.</param>
/// <param name="OwnerCount">How many agents point their persistent session at this one.</param>
/// <param name="OwnerAlwaysOn">The single owner's AlwaysOn; null unless exactly one owner.</param>
/// <param name="OwnerAgentId">The single owner; null unless exactly one owner.</param>
/// <param name="StandingAgentConflict">The session's stamped standing agent names somebody else.</param>
/// <param name="TaskOwner">The open task owning the session; terminal history never counts.</param>
/// <param name="PromptSequence">The latest real prompt since the launch clock; null when none.</param>
/// <param name="ReplyObserved">A model row since the launch clock.</param>
/// <param name="IdentityMatches">The latest real prompt is the one the armed watch names.</param>
internal sealed record StandingBootWatchObservation(
    int BootWaitMinutes,
    int ModelWaitMinutes,
    bool? ProviderVerified,
    GrokRulesState? GrokRules,
    bool? SessionLive,
    bool? EndedAtNull,
    DateTime? Generation,
    DateTime? LaunchClock,
    int? OwnerCount,
    bool? OwnerAlwaysOn,
    Guid? OwnerAgentId,
    bool? StandingAgentConflict,
    StandingBootTaskOwner? TaskOwner,
    long? PromptSequence,
    DateTime? PromptAt,
    string? PromptKind,
    bool? ReplyObserved,
    bool? IdentityMatches,
    DateTime Now)
{
    internal static readonly SessionStatus[] LiveStatuses =
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private static readonly AgentTaskStatus[] OpenTaskStatuses =
        [AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked];

    /// <summary>The agent rows whose persistent pointer names this session. One read.</summary>
    internal static async Task<IReadOnlyList<StandingBootOwner>> ReadOwnersAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var id = sessionId.ToString("D");
        return await db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId == id)
            .Select(a => new StandingBootOwner(a.Id, a.AlwaysOn))
            .ToListAsync(ct);
    }

    /// <summary>
    /// The open task owning this session, strongest first: an active (Working, Dispatched) owner
    /// before a waiting (Blocked, Queued) one. One read. Terminal history is not an owner.
    /// </summary>
    internal static async Task<StandingBootTaskOwner> ReadTaskOwnerAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var statuses = await db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId && OpenTaskStatuses.Contains(t.Status))
            .Select(t => t.Status)
            .Distinct()
            .ToListAsync(ct);
        if (statuses.Count == 0)
            return StandingBootTaskOwner.None;
        if (statuses.Contains(AgentTaskStatus.Working))
            return StandingBootTaskOwner.Working;
        if (statuses.Contains(AgentTaskStatus.Dispatched))
            return StandingBootTaskOwner.Dispatched;
        return statuses.Contains(AgentTaskStatus.Blocked) ? StandingBootTaskOwner.Blocked : StandingBootTaskOwner.Queued;
    }

    /// <summary>
    /// The session-level facts that need no query: provider, rules, liveness, generation, launch
    /// clock and owner shape. <paramref name="session"/> null is a missing row (every field unknown).
    /// </summary>
    internal static StandingBootWatchObservation FromRow(
        AgentSession? session, IReadOnlyList<StandingBootOwner>? owners, StandingBootTaskOwner? taskOwner,
        int bootWaitMinutes, int modelWaitMinutes, DateTime now)
    {
        if (session is null)
        {
            return new(bootWaitMinutes, modelWaitMinutes, null, null, null, null, null, null,
                owners?.Count, null, null, null, taskOwner, null, null, null, null, null, now);
        }

        var single = owners is { Count: 1 } ? owners[0] : null;
        return new(
            bootWaitMinutes,
            modelWaitMinutes,
            ProviderContractCatalog.For(session.AgentKind).DeliveryVerification.State == AgentTuiCapabilityState.Supported,
            session.GrokRulesState,
            LiveStatuses.Contains(session.Status),
            session.EndedAt is null,
            session.StartedAt == default ? null : SessionGeneration.Normalize(session.StartedAt),
            session.StartedAt == default ? null : BootReplyWatch.LaunchClock(session),
            owners?.Count,
            single?.AlwaysOn,
            single?.AgentId,
            owners is null ? null : session.StandingAgentId is { } stamped && owners.Any(o => o.AgentId != stamped),
            taskOwner,
            null, null, null, null, null,
            now);
    }

    /// <summary>
    /// The full observation for one overdue standing candidate (CARD-1156 S2), over the owner
    /// pointers and open-task owner the caller read (<see cref="ReadOwnersAsync"/>,
    /// <see cref="ReadTaskOwnerAsync"/>): then the boot predicate on this launch (the model-reply
    /// EXISTS and the prompt rows). A task-owned session stops before any prompt work: no open task
    /// owner is ever admitted, so its prompt fields stay unknown.
    /// </summary>
    /// <param name="watchedSequence">The armed watch's prompt sequence (<c>BootPromptSequence</c>).</param>
    internal static async Task<StandingBootWatchObservation> ReadAsync(
        AppDbContext db, AgentSession session, IReadOnlyList<StandingBootOwner> owners,
        StandingBootTaskOwner taskOwner, long? watchedSequence,
        int bootWaitMinutes, int modelWaitMinutes, DateTime now, CancellationToken ct)
    {
        var observation = FromRow(session, owners, taskOwner, bootWaitMinutes, modelWaitMinutes, now);
        if (taskOwner != StandingBootTaskOwner.None)
            return observation;

        var clock = BootReplyWatch.LaunchClock(session);
        if (await BootReplyWatch.HasModelReplySinceAsync(db, session.Id, clock, ct))
            return observation with { ReplyObserved = true };

        var turn = await BootReplyWatch.LoadPromptTurnAsync(db, session.Id, clock, ct);
        return observation with
        {
            ReplyObserved = false,
            PromptSequence = turn?.Turn.PromptSequence,
            PromptAt = turn?.Turn.PromptAt,
            PromptKind = turn?.LatestKind,
            IdentityMatches = turn is not null && watchedSequence == turn.Turn.PromptSequence,
        };
    }
}

/// <summary>An agent whose persistent pointer names the session.</summary>
internal sealed record StandingBootOwner(Guid AgentId, bool AlwaysOn);
