using System.Threading;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1320 S2. Shortfall for one standing orchestrator: the project's open count against
/// the stored target lowered by the absolute ceiling, or zero when that board has no
/// dispatchable card. A hold does not change the badge.
/// </summary>
public sealed class KeepSixShortfall(
    AppDbContext db,
    DispatchConcurrencySettingsService concurrency,
    TimeProvider clock)
{
    public const string BadgeOff = "off";
    public const string BadgeHealthy = "healthy";
    public const string BadgeUnderTarget = "under-target";
    public const int TopCardLimit = 5;

    /// <summary>
    /// CARD-1320 D1 witness. Production leaves this unset. A reminder test arms a counter on
    /// its own call so a shortfall read is visible without a process-wide tally.
    /// </summary>
    internal static readonly AsyncLocal<ReadProbeCounter?> ReadProbe = new();

    internal sealed class ReadProbeCounter
    {
        public int Count;
    }

    public sealed record Counts(
        int CountedOpen,
        int EffectiveTarget,
        int DispatchableBacklog,
        int Shortfall,
        IReadOnlyList<string> TopCards,
        DateTimeOffset? LastDispatchAt,
        DateTimeOffset? LastCompletionAt,
        string Badge);

    public async Task<IReadOnlyDictionary<Guid, Counts>> ReadAsync(
        IReadOnlyList<Agent> agents,
        IReadOnlyDictionary<Guid, KeepSixOrchestrator> rows,
        CancellationToken ct)
    {
        if (ReadProbe.Value is { } probe)
            probe.Count++;
        if (agents.Count == 0)
            return new Dictionary<Guid, Counts>();

        var boardIds = agents
            .Where(agent => agent.BoardId is Guid)
            .Select(agent => agent.BoardId!.Value)
            .Distinct()
            .ToArray();
        var boardProjects = boardIds.Length == 0
            ? new Dictionary<Guid, Guid>()
            : await db.Boards.AsNoTracking()
                .Where(board => boardIds.Contains(board.Id))
                .ToDictionaryAsync(board => board.Id, board => board.ProjectId, ct)
                .ConfigureAwait(false);
        var projectIds = boardProjects.Values.Distinct().ToArray();
        var asOf = clock.GetUtcNow().UtcDateTime;
        var selection = await PipelineBacklogSelection.SelectAsync(db, PipelineScope.Fleet, asOf, ct)
            .ConfigureAwait(false);
        var candidatesByBoard = selection.Candidates
            .GroupBy(card => card.BoardId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<PipelineBacklogSelection.Row>)group.ToList());
        var stamps = projectIds.Length == 0
            ? new List<TaskStamp>()
            : await db.AgentTasks.AsNoTracking()
                .Where(task => task.ProjectId != null && projectIds.Contains(task.ProjectId.Value))
                .Select(task => new TaskStamp(
                    task.Id, task.ProjectId, task.Role, task.Status, task.CreatedAt,
                    task.CompletedAt, task.CapacityWaitRetained, task.Title))
                .ToListAsync(ct)
                .ConfigureAwait(false);
        var policies = projectIds.Length == 0
            ? null
            : await concurrency.ReadCoherentAsync(projectIds.Select(id => (Guid?)id).ToArray(), ct)
                .ConfigureAwait(false);

        var result = new Dictionary<Guid, Counts>(agents.Count);
        foreach (var agent in agents)
        {
            rows.TryGetValue(agent.Id, out var row);
            var enabled = row?.Enabled ?? false;
            var storedTarget = row?.Target ?? KeepSixOrchestrator.DefaultTarget;
            if (agent.BoardId is not Guid boardId
                || !boardProjects.TryGetValue(boardId, out var projectId))
            {
                result[agent.Id] = Absent(enabled, storedTarget);
                continue;
            }

            var population = DispatchConcurrencyPolicy.Count(
                stamps.Select(stamp => new TaskPopulationRow(
                    stamp.Id, stamp.ProjectId, stamp.Role, stamp.Status, stamp.CreatedAt,
                    stamp.CapacityWaitRetained, stamp.Title)),
                projectId);
            var projectStamps = stamps.Where(stamp =>
                stamp.ProjectId == projectId && !AgentTaskRoles.IsSpecialist(stamp.Role)).ToList();
            var ceiling = policies?.For(projectId) is { } policy ? Ceiling(policy) : (int?)null;
            var effectiveTarget = ceiling is int cap ? Math.Min(storedTarget, cap) : storedTarget;
            candidatesByBoard.TryGetValue(boardId, out var candidates);
            var dispatchable = candidates ?? [];
            var shortfall = Compute(population.Open, effectiveTarget, dispatchable.Count);
            result[agent.Id] = new Counts(
                population.Open,
                effectiveTarget,
                dispatchable.Count,
                shortfall,
                dispatchable.Take(TopCardLimit).Select(card => card.Identifier).ToArray(),
                Latest(projectStamps, stamp => stamp.CreatedAt),
                Latest(projectStamps, stamp => stamp.CompletedAt),
                Badge(enabled, shortfall));
        }

        return result;
    }

    /// <summary>
    /// LegacyOpen uses <see cref="EffectivePolicy.MaxParallel"/>. SeparateQueues adds
    /// <see cref="EffectivePolicy.MaxQueued"/> when that limit has a value.
    /// </summary>
    public static int Ceiling(EffectivePolicy policy) =>
        policy.Mode == DispatchConcurrencyMode.SeparateQueues && policy.MaxQueued is int queued
            ? policy.MaxParallel + queued
            : policy.MaxParallel;

    public static int Compute(int countedOpen, int effectiveTarget, int dispatchableBacklog) =>
        countedOpen >= effectiveTarget || dispatchableBacklog == 0
            ? 0
            : effectiveTarget - countedOpen;

    public static string Badge(bool enabled, int shortfall) =>
        !enabled ? BadgeOff : shortfall == 0 ? BadgeHealthy : BadgeUnderTarget;

    private static Counts Absent(bool enabled, int storedTarget) => new(
        0, storedTarget, 0, 0, [], null, null, Badge(enabled, 0));

    /// <summary>
    /// D-5's last create is the newest non-specialist <see cref="AgentTask.CreatedAt"/>.
    /// The GET field for that instant is <c>lastDispatchAt</c>.
    /// </summary>
    private static DateTimeOffset? Latest(IReadOnlyList<TaskStamp> stamps, Func<TaskStamp, DateTime?> pick)
    {
        DateTime? latest = null;
        foreach (var stamp in stamps)
        {
            var value = pick(stamp);
            if (value is DateTime instant && (latest is null || instant > latest))
                latest = instant;
        }

        return latest is DateTime found
            ? new DateTimeOffset(DateTime.SpecifyKind(found, DateTimeKind.Utc))
            : null;
    }

    private sealed record TaskStamp(
        Guid Id,
        Guid? ProjectId,
        AgentTaskRole Role,
        AgentTaskStatus Status,
        DateTime CreatedAt,
        DateTime? CompletedAt,
        bool CapacityWaitRetained,
        string Title);
}
