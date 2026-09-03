using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0147 S1: serialize count-and-insert for the create-time concurrency cap.
/// Lives on create, not on the dispatcher tick — a tick-level skip would leave the
/// orchestrator thinking the task started.
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

    public DelegationOpenGate(AppDbContext db, IOptions<DelegationSettings> settings)
    {
        _db = db;
        _settings = settings.Value;
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
        int? RoleLimit)
    {
        public int AbsoluteCount => Open.Count;
        public int RoleCount => Open.Count(o => o.Role == Role);
        public bool AbsoluteExceeded => AbsoluteCount >= AbsoluteLimit;
        public bool RoleExceeded => RoleLimit is int limit && RoleCount >= limit;
        public bool WouldRefuse => AbsoluteExceeded || RoleExceeded;
    }

    /// <summary>
    /// Take the xact lock, count open non-specialists, and throw 409 unless
    /// <paramref name="ignoreConcurrencyLimit"/> is set. Caller must already be
    /// inside an EF transaction so the lock is held through insert.
    /// </summary>
    public async Task<Snapshot> EnsureCanCreateAsync(
        AgentTaskRole role,
        bool ignoreConcurrencyLimit,
        CancellationToken ct)
    {
        await TakeLockAsync(ct);
        var snapshot = await LoadSnapshotAsync(role, ct);
        if (ignoreConcurrencyLimit || !snapshot.WouldRefuse)
            return snapshot;

        throw new ConcurrencyLimitException(ToProblem(snapshot));
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
            Override: ConcurrencyLimitException.OverrideFlag);
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

    private async Task<Snapshot> LoadSnapshotAsync(AgentTaskRole role, CancellationToken ct)
    {
        var rows = await _db.AgentTasks
            .AsNoTracking()
            .Where(AgentTaskRoles.NotSpecialist)
            .Where(t => OpenStatuses.Contains(t.Status))
            .Select(t => new { t.Id, t.Role, t.Status, t.Title })
            .ToListAsync(ct);

        var stuck = await LoadStuckLabelsAsync(rows.Select(r => r.Id).ToList(), ct);
        var open = rows
            .Select(r => new Occupant(
                r.Id,
                r.Role,
                r.Status,
                r.Title,
                stuck.GetValueOrDefault(r.Id)))
            .ToList();

        return new Snapshot(open, role, _settings.MaxOpenTasks, _settings.RecommendedInFlightFor(role));
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
}
