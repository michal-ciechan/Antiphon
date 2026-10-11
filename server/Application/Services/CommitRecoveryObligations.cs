using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0547 D-1. The single reader of <see cref="AgentTaskEventType.CommitRecoveryStarted"/>
/// obligations. Settlement, the overdue watchdog, the dead-session reconciler, every requeue and
/// the attention projection decide through this one rule, so an obligation that settlement still
/// has to recover is never silently orphaned by a terminal or requeue path.
/// </summary>
public static class CommitRecoveryObligations
{
    public sealed record Pending(Guid TaskId, Guid EventId, string Settlement, DateTime StartedAt);

    private static readonly AgentTaskEventType[] RelevantTypes =
    [
        AgentTaskEventType.Committed,
        AgentTaskEventType.CommitRecoveryStarted,
        AgentTaskEventType.CommitRecoveryNotNeeded,
        AgentTaskEventType.CommitRecoveryAbandoned,
    ];

    /// <summary>
    /// Pure rule. An obligation s (type 34) is unresolved unless the same task has a
    /// CommitRecoveryNotNeeded or CommitRecoveryAbandoned row whose Detail starts with
    /// <c>"{s.Id:D} "</c>, or a Committed row with <c>At &gt;= s.At</c>. When
    /// <paramref name="taskId"/> is given, other tasks' rows are ignored.
    /// </summary>
    public static IReadOnlyList<Pending> Unresolved(IEnumerable<AgentTaskEvent> events, Guid? taskId = null)
    {
        var rows = events.Where(e => taskId is null || e.AgentTaskId == taskId).ToList();
        return rows
            .Where(s => s.Type == AgentTaskEventType.CommitRecoveryStarted && !rows.Any(r =>
                r.AgentTaskId == s.AgentTaskId
                && (r.Type is AgentTaskEventType.CommitRecoveryNotNeeded or AgentTaskEventType.CommitRecoveryAbandoned
                        && r.Detail.StartsWith($"{s.Id:D} ", StringComparison.Ordinal)
                    || r.Type == AgentTaskEventType.Committed && r.At >= s.At)))
            .OrderBy(s => s.At)
            .Select(s => new Pending(s.AgentTaskId, s.Id, s.Detail, s.At))
            .ToArray();
    }

    /// <summary>
    /// Both sweeps hold a task while its oldest unresolved obligation is younger than the hold.
    /// A non-positive hold disables holding (the terminal path still abandons by name).
    /// </summary>
    public static bool ShouldHold(IReadOnlyList<Pending> pending, TimeSpan hold, DateTime now) =>
        pending.Count > 0 && hold > TimeSpan.Zero && now - pending.Min(p => p.StartedAt) < hold;

    public static async Task<IReadOnlyList<Pending>> LoadUnresolvedAsync(
        AppDbContext db, Guid taskId, CancellationToken ct)
    {
        var events = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && RelevantTypes.Contains(e.Type))
            .ToListAsync(ct);
        return Unresolved(events, taskId);
    }

    /// <summary>One query for the whole set; tasks with nothing pending are omitted.</summary>
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Pending>>> LoadUnresolvedAsync(
        AppDbContext db, IReadOnlyCollection<Guid> taskIds, CancellationToken ct)
    {
        if (taskIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<Pending>>();
        var ids = taskIds.Distinct().ToArray();
        var events = await db.AgentTaskEvents.AsNoTracking()
            .TagWith("CommitRecoveryObligations.LoadUnresolved")
            .Where(e => ids.Contains(e.AgentTaskId) && RelevantTypes.Contains(e.Type))
            .ToListAsync(ct);
        return Unresolved(events)
            .GroupBy(p => p.TaskId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Pending>)g.ToArray());
    }

    /// <summary>Sweep name the dead-session reconciler passes to <see cref="Abandon"/>.</summary>
    public const string DeadSessionReconcilerSweep = "dead-session reconciler";

    /// <summary>Type-36 detail limit. A longer writer string is stored as this prefix.</summary>
    public const int AbandonmentDetailMaxLength = 4000;

    /// <summary>Header value and warning paragraphs for one saved failure. Null when nothing is proven.</summary>
    public sealed record AbandonedGuidance(string? Git, string? Warning);

    /// <summary>The type-36 row. Same <c>"{started id} "</c> prefix convention as NotNeeded.</summary>
    public static AgentTaskEvent Abandon(Pending p, string by, string reason, DateTime now)
    {
        var detail = $"{p.EventId:D} {by}: {reason}";
        return new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = p.TaskId,
            Type = AgentTaskEventType.CommitRecoveryAbandoned,
            Detail = detail.Length <= AbandonmentDetailMaxLength ? detail : detail[..AbandonmentDetailMaxLength],
            At = now,
        };
    }

    /// <summary>
    /// CARD-1186 D-1. Starts abandoned for this saved failure. The abandonment must name the
    /// start, the saved reason must name that start and its settlement, and the stored detail
    /// must equal the dead-session writer's text (or its 4,000-character prefix). Ordered by
    /// start time, then id. An unproven pair contributes nothing.
    /// </summary>
    public static IReadOnlyList<Pending> SelectAbandonedForSavedFailure(
        IEnumerable<AgentTaskEvent> events, Guid taskId, string? failureReason)
    {
        if (string.IsNullOrEmpty(failureReason))
            return [];

        var rows = events.Where(e => e.AgentTaskId == taskId).ToList();
        var abandonments = rows
            .Where(e => e.Type == AgentTaskEventType.CommitRecoveryAbandoned)
            .ToList();
        var selected = new Dictionary<Guid, Pending>();
        foreach (var start in rows.Where(e => e.Type == AgentTaskEventType.CommitRecoveryStarted))
        {
            if (selected.ContainsKey(start.Id))
                continue;
            if (!failureReason.Contains(ObligationMarker(start.Id, start.Detail), StringComparison.Ordinal))
                continue;

            var expected = ExpectedDeadSessionAbandonment(start.Id, failureReason);
            var prefix = $"{start.Id:D} ";
            var linked = abandonments.Any(abandonment =>
                abandonment.AgentTaskId == start.AgentTaskId
                && abandonment.Detail.StartsWith(prefix, StringComparison.Ordinal)
                && string.Equals(abandonment.Detail, expected, StringComparison.Ordinal));
            if (!linked)
                continue;

            selected.Add(start.Id, new Pending(start.AgentTaskId, start.Id, start.Detail, start.At));
        }

        return InGuidanceOrder(selected.Values);
    }

    /// <summary>Type-34 starts and type-36 abandonments for one task. A failed read throws.</summary>
    public static async Task<IReadOnlyList<Pending>> LoadAbandonedForSavedFailureAsync(
        AppDbContext db, Guid taskId, string failureReason, CancellationToken ct)
    {
        var events = await db.AgentTaskEvents.AsNoTracking()
            .TagWith("CommitRecoveryObligations.LoadAbandonedForSavedFailure")
            .Where(e => e.AgentTaskId == taskId
                && (e.Type == AgentTaskEventType.CommitRecoveryStarted
                    || e.Type == AgentTaskEventType.CommitRecoveryAbandoned))
            .ToListAsync(ct);
        return SelectAbandonedForSavedFailure(events, taskId, failureReason);
    }

    /// <summary>Same order on the initial failure path and the recovered note.</summary>
    public static IReadOnlyList<Pending> InGuidanceOrder(IEnumerable<Pending> obligations) =>
        obligations.OrderBy(p => p.StartedAt).ThenBy(p => p.EventId).ToArray();

    /// <summary>
    /// <c>commit-recovery-abandoned:</c> short ids plus one <see cref="Describe"/> paragraph each.
    /// </summary>
    public static AbandonedGuidance FormatAbandonedGuidance(Guid taskId, IReadOnlyList<Pending> obligations)
    {
        var ordered = InGuidanceOrder(obligations);
        if (ordered.Count == 0)
            return new AbandonedGuidance(null, null);

        var git = "commit-recovery-abandoned:" + string.Join(",",
            ordered.Select(p => p.EventId.ToString("N")[..8]));
        var warning = string.Join("\n\n", ordered.Select(p => Describe(taskId, p)));
        return new AbandonedGuidance(git, warning);
    }

    private static string ObligationMarker(Guid startId, string settlement) =>
        $"{startId:D} (settlement {settlement})";

    private static string ExpectedDeadSessionAbandonment(Guid startId, string failureReason)
    {
        var canonical = $"{startId:D} {DeadSessionReconcilerSweep}: {failureReason}";
        return canonical.Length <= AbandonmentDetailMaxLength
            ? canonical
            : canonical[..AbandonmentDetailMaxLength];
    }

    /// <summary>The recovery recipe settlement itself uses, so a human can find the commit without the server.</summary>
    public static string Recipe(Guid taskId, string settlement) =>
        $"git log --all --reflog --fixed-strings --all-match --grep={taskId:D} --grep={settlement} --format=%H";

    public static string Describe(Guid taskId, Pending p) =>
        $"Commit recovery obligation {p.EventId:D} for task {taskId:D} (settlement {p.Settlement}) was not resolved. "
        + "The gated commit, if it exists, is local and unpushed; find it with: "
        + Recipe(taskId, p.Settlement) + ". Nothing was pushed.";
}
