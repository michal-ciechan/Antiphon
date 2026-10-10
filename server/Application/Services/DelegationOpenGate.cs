using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0147 S1: serialize count-and-insert for the create-time concurrency cap,
/// keyed by the task's project scope (CARD-0366). Lives on create, not on the
/// dispatcher tick — a tick-level skip would leave the orchestrator thinking the
/// task started.
/// </summary>
public sealed class DelegationOpenGate
{
    public const string AdvisoryLockKey = "antiphon.delegation.max-open-tasks";

    private static readonly AgentTaskStatus[] OpenStatuses =
    [
        AgentTaskStatus.Queued,
        AgentTaskStatus.Dispatched,
        AgentTaskStatus.Working,
    ];

    private readonly AppDbContext _db;
    private readonly DelegationSettings _settings;
    private readonly DispatchConcurrencySettingsService? _concurrency;

    public DelegationOpenGate(
        AppDbContext db,
        IOptions<DelegationSettings> settings,
        DispatchConcurrencySettingsService? concurrency = null)
    {
        _db = db;
        _settings = settings.Value;
        _concurrency = concurrency;
    }

    public sealed record Occupant(
        Guid TaskId,
        AgentTaskRole Role,
        AgentTaskStatus Status,
        string Title,
        string? Stuck);

    public sealed record Snapshot(
        IReadOnlyList<Occupant> Open,
        AgentTaskRole Role,
        int AbsoluteLimit,
        int? RoleLimit,
        Guid? ProjectId)
    {
        public int AbsoluteCount => Open.Count;
        public int RoleCount => Open.Count(o => o.Role == Role);
        public bool AbsoluteExceeded => AbsoluteCount >= AbsoluteLimit;
        public bool RoleExceeded => RoleLimit is int limit && RoleCount >= limit;
        public bool WouldRefuse => AbsoluteExceeded || RoleExceeded;
    }

    /// <summary>
    /// Take the xact lock, count open non-specialists in <paramref name="projectId"/>
    /// (null is its own bucket), and throw 409 unless
    /// <paramref name="ignoreConcurrencyLimit"/> is set. Caller must already be
    /// inside an EF transaction so the lock is held through insert.
    /// </summary>
    public async Task<Snapshot> EnsureCanCreateAsync(
        Guid? projectId,
        AgentTaskRole role,
        bool ignoreConcurrencyLimit,
        CancellationToken ct)
    {
        await TakeLockAsync(ct);
        if (_concurrency is null)
        {
            var snapshot = await LoadSnapshotAsync(projectId, role, ct);
            if (ignoreConcurrencyLimit || !snapshot.WouldRefuse)
                return snapshot;
            throw new ConcurrencyLimitException(ToProblem(snapshot));
        }

        var population = DispatchConcurrencyPolicy.Count(await LoadPopulationAsync(ct), projectId);
        var policy = await _concurrency.ReadEffectiveAsync(projectId, ct);
        var decision = DispatchConcurrencyPolicy.DecideCreate(policy, population, role, ignoreConcurrencyLimit);
        var open = population.OpenRows
            .Select(row => new Occupant(row.Id, row.Role, row.Status, row.Title, row.Stuck))
            .ToList();
        var admitted = new Snapshot(
            open, role, policy.MaxParallel, policy.Role(role).MaxParallel, projectId);
        if (!decision.Admit)
            throw new ConcurrencyLimitException(ToPolicyProblem(policy, decision, population, projectId));
        return admitted;
    }

    public static ConcurrencyLimitProblemDto ToProblem(Snapshot snapshot)
    {
        var axis = snapshot.AbsoluteExceeded ? "absolute" : "role";
        var occupants = axis == "absolute"
            ? snapshot.Open
            : snapshot.Open.Where(o => o.Role == snapshot.Role).ToList();
        var listed = occupants
            .Take(ConcurrencyLimitException.OccupantListCap)
            .Select(ToOccupantDto)
            .ToList();

        return new ConcurrencyLimitProblemDto(
            Axis: axis,
            Role: axis == "role" ? snapshot.Role.ToString() : null,
            Count: axis == "absolute" ? snapshot.AbsoluteCount : snapshot.RoleCount,
            Limit: axis == "absolute" ? snapshot.AbsoluteLimit : snapshot.RoleLimit ?? 0,
            Open: listed,
            Override: ConcurrencyLimitException.OverrideFlag,
            ProjectId: snapshot.ProjectId);
    }

    public static ConcurrencyLimitOccupantDto ToOccupantDto(Occupant occupant) =>
        new(
            occupant.TaskId,
            DelegationReportFormatter.Short(occupant.TaskId),
            occupant.Role.ToString(),
            occupant.Status.ToString(),
            occupant.Title,
            occupant.Stuck);

    private async Task TakeLockAsync(CancellationToken ct) =>
        await _db.Database.ExecuteSqlRawAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('{AdvisoryLockKey}'))",
            cancellationToken: ct);

    private async Task<Snapshot> LoadSnapshotAsync(Guid? projectId, AgentTaskRole role, CancellationToken ct)
    {
        var rows = await _db.AgentTasks
            .AsNoTracking()
            .Where(AgentTaskRoles.NotSpecialist)
            .Where(t => OpenStatuses.Contains(t.Status))
            .Where(t => t.ProjectId == projectId)
            .Select(t => new { t.Id, t.Role, t.Status, t.Title, t.CreatedAt })
            .ToListAsync(ct);

        var stuck = await LoadStuckLabelsAsync(rows.Select(r => r.Id).ToList(), ct);
        var open = rows
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Select(r => new Occupant(
                r.Id,
                r.Role,
                r.Status,
                r.Title,
                stuck.GetValueOrDefault(r.Id)))
            .ToList();

        return new Snapshot(open, role, _settings.MaxOpenTasks, _settings.RecommendedInFlightFor(role), projectId);
    }

    /// <summary>
    /// CARD-0147 S3: uncleared <c>WorktreeHealthFinding</c> rows for the occupants.
    /// Create must not talk to git — the sweep (or <c>delegate.ps1 -WorktreeHealth</c>) writes
    /// the rows; this only reads them.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> LoadStuckLabelsAsync(
        IReadOnlyList<Guid> taskIds,
        CancellationToken ct)
    {
        if (taskIds.Count == 0)
            return new Dictionary<Guid, string>();

        var rows = await _db.WorktreeHealthFindings
            .AsNoTracking()
            .Where(f => f.ClearedAt == null && f.TaskId != null && taskIds.Contains(f.TaskId.Value))
            .Select(f => new { TaskId = f.TaskId!.Value, f.Detail, f.Shape })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.TaskId)
            .ToDictionary(
                g => g.Key,
                g => string.Join("; ", g
                    .OrderBy(x => x.Shape)
                    .Select(x => CompactStuck(x.Detail))
                    .Distinct(StringComparer.Ordinal)));
    }

    internal static string CompactStuck(string detail)
    {
        // Occupant labels are the parenthetical after "stuck:". Keep them short: drop the
        // branch prefix when the 409 already names the short id.
        const string prefix = "feat/card-task-";
        var text = detail.Trim();
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var space = text.IndexOf(' ');
            if (space > 0 && space + 1 < text.Length)
                text = text[(space + 1)..];
        }

        return text.Trim().TrimStart(';').Trim();
    }

    private async Task<List<TaskPopulationRow>> LoadPopulationAsync(CancellationToken ct)
    {
        var rows = await _db.AgentTasks.AsNoTracking()
            .Select(t => new
            {
                t.Id,
                t.ProjectId,
                t.Role,
                t.Status,
                t.CreatedAt,
                t.CapacityWaitRetained,
                t.Title,
            })
            .ToListAsync(ct);
        var stuck = await LoadStuckLabelsAsync(rows.Select(row => row.Id).ToList(), ct);
        return rows.Select(row => new TaskPopulationRow(
            row.Id,
            row.ProjectId,
            row.Role,
            row.Status,
            row.CreatedAt,
            row.CapacityWaitRetained,
            row.Title,
            stuck.GetValueOrDefault(row.Id))).ToList();
    }

    private static ConcurrencyLimitProblemDto ToPolicyProblem(
        EffectivePolicy policy,
        DispatchDecision decision,
        PopulationSnapshot population,
        Guid? projectId)
    {
        var byId = population.OpenRows
            .Concat(population.ParallelRows)
            .Concat(population.QueuedRows)
            .GroupBy(row => row.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var listed = decision.ListedOccupantIds
            .Select(id => byId[id])
            .Select(row => ToOccupantDto(new Occupant(row.Id, row.Role, row.Status, row.Title, row.Stuck)))
            .ToList();
        var primary = decision.Exceeded.First(item =>
            item.Population == decision.Population && item.Axis == decision.Axis);
        var roleName = primary.Axis == DispatchConcurrencyPolicy.AxisRole ? primary.Role : null;
        ResolvedRole? resolved = roleName is not null && Enum.TryParse<AgentTaskRole>(roleName, out var parsed)
            ? policy.Role(parsed)
            : null;
        return new ConcurrencyLimitProblemDto(
            primary.Axis,
            roleName,
            decision.Count,
            decision.Limit,
            listed,
            ConcurrencyLimitException.OverrideFlag,
            projectId,
            decision.Population ?? DispatchConcurrencyPolicy.PopulationOpen,
            policy.Mode.ToString(),
            primary.Source,
            policy.GlobalRevision,
            policy.ProjectRevision,
            decision.CanOverride,
            decision.Exceeded,
            decision.TotalOccupants,
            decision.Omitted,
            policy.ParallelSource,
            policy.QueuedSource,
            resolved?.ParallelSource,
            resolved?.QueuedSource);
    }
}
