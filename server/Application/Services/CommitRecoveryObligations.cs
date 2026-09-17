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
            .Where(e => ids.Contains(e.AgentTaskId) && RelevantTypes.Contains(e.Type))
            .ToListAsync(ct);
        return Unresolved(events)
            .GroupBy(p => p.TaskId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Pending>)g.ToArray());
    }

    /// <summary>The type-36 row. Same <c>"{started id} "</c> prefix convention as NotNeeded.</summary>
    public static AgentTaskEvent Abandon(Pending p, string by, string reason, DateTime now)
    {
        var detail = $"{p.EventId:D} {by}: {reason}";
        return new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = p.TaskId,
            Type = AgentTaskEventType.CommitRecoveryAbandoned,
            Detail = detail.Length <= 4000 ? detail : detail[..4000],
            At = now,
        };
    }

    /// <summary>The recovery recipe settlement itself uses, so a human can find the commit without the server.</summary>
    public static string Recipe(Guid taskId, string settlement) =>
        $"git log --all --reflog --fixed-strings --all-match --grep={taskId:D} --grep={settlement} --format=%H";

    public static string Describe(Guid taskId, Pending p) =>
        $"Commit recovery obligation {p.EventId:D} for task {taskId:D} (settlement {p.Settlement}) was not resolved. "
        + "The gated commit, if it exists, is local and unpushed; find it with: "
        + Recipe(taskId, p.Settlement) + ". Nothing was pushed.";
}
