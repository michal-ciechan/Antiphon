using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0147 S3: cross-reference git's <c>feat/card-task-*</c> registrations against
/// <see cref="AgentTask"/> rows. Detection only — never prune, heal, cancel, or re-dispatch.
/// </summary>
public sealed class WorktreeHealthService
{
    private static readonly AgentTaskStatus[] OpenStatuses =
    [
        AgentTaskStatus.Queued,
        AgentTaskStatus.Dispatched,
        AgentTaskStatus.Working,
    ];

    private readonly AppDbContext _db;
    private readonly IWorktreeManager _worktrees;
    private readonly DelegationSettings _delegation;
    private readonly TimeProvider _clock;
    private readonly ILogger<WorktreeHealthService> _logger;

    public WorktreeHealthService(
        AppDbContext db,
        IWorktreeManager worktrees,
        IOptions<DelegationSettings> delegation,
        TimeProvider clock,
        ILogger<WorktreeHealthService> logger)
    {
        _db = db;
        _worktrees = worktrees;
        _delegation = delegation.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<WorktreeHealthReportDto> SweepAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var tasks = await _db.AgentTasks
            .AsNoTracking()
            .Select(t => new TaskRow(t.Id, t.Status, t.RepoPath, t.WorktreePath, t.WorktreeBranch))
            .ToListAsync(ct);
        var byShort = tasks
            .GroupBy(t => DelegationReportFormatter.Short(t.Id), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<(string Repo, string Branch, WorktreeHealthShape Shape)>(
            RepoBranchShapeComparer.Instance);
        var scannedRepos = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var repo in await CollectReposAsync(tasks, ct))
        {
            IReadOnlyList<DelegateWorktreeScanEntry> scan;
            try
            {
                scan = await _worktrees.ScanDelegateWorktreesAsync(repo, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Worktree health scan skipped for {RepoPath}", repo);
                continue;
            }

            scannedRepos.Add(repo);
            foreach (var classified in Classify(scan, byShort))
            {
                seen.Add((classified.RepoPath, classified.Branch, classified.Shape));
                await UpsertAsync(classified, now, ct);
            }
        }

        var uncleared = await _db.WorktreeHealthFindings
            .Where(f => f.ClearedAt == null)
            .ToListAsync(ct);
        foreach (var row in uncleared)
        {
            if (!scannedRepos.Contains(row.RepoPath))
                continue;
            if (seen.Contains((row.RepoPath, row.Branch, row.Shape)))
                continue;
            row.ClearedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        var live = await _db.WorktreeHealthFindings
            .AsNoTracking()
            .Where(f => f.ClearedAt == null)
            .OrderBy(f => f.FirstSeenAt)
            .ToListAsync(ct);

        return new WorktreeHealthReportDto(
            live.Count,
            live.Select(ToDto).ToList());
    }

    internal static IEnumerable<ClassifiedFinding> Classify(
        IReadOnlyList<DelegateWorktreeScanEntry> scan,
        IReadOnlyDictionary<string, TaskRow> byShort)
    {
        foreach (var entry in scan)
        {
            byShort.TryGetValue(entry.ShortId, out var task);
            var lockBit = entry.Locked
                ? (string.IsNullOrWhiteSpace(entry.LockReason) ? "locked" : $"locked {entry.LockReason}")
                : null;
            var missingDir = !entry.DirectoryExists;
            var missingGit = entry.DirectoryExists && !entry.GitFileExists;

            if (entry.Registered && entry.Locked && (missingDir || missingGit))
            {
                var missing = missingDir ? $"directory {entry.Path} is gone" : $".git file missing at {entry.Path}";
                yield return new ClassifiedFinding(
                    entry.RepoPath,
                    entry.Branch,
                    entry.Path,
                    task?.Id,
                    WorktreeHealthShape.LockedMissing,
                    $"{entry.Branch} {lockBit}; {missing}");
            }

            if (entry.Registered && task is not null && OpenStatuses.Contains(task.Status)
                && (entry.Locked || missingDir))
            {
                var why = lockBit ?? (missingDir ? "directory gone" : "unhealthy");
                yield return new ClassifiedFinding(
                    entry.RepoPath,
                    entry.Branch,
                    entry.Path,
                    task.Id,
                    WorktreeHealthShape.OpenTaskUnhealthy,
                    $"{entry.Branch} still {task.Status}; {why}");
            }

            if (entry.Registered && task is null)
            {
                yield return new ClassifiedFinding(
                    entry.RepoPath,
                    entry.Branch,
                    entry.Path,
                    TaskId: null,
                    WorktreeHealthShape.RegisteredNoTask,
                    $"{entry.Branch} registered with no AgentTask row");
            }

            if (!entry.Registered && task is null)
            {
                yield return new ClassifiedFinding(
                    entry.RepoPath,
                    entry.Branch,
                    entry.Path,
                    TaskId: null,
                    WorktreeHealthShape.DanglingBranchNoTask,
                    $"{entry.Branch} branch has no worktree and no AgentTask row");
            }
        }
    }

    internal sealed record TaskRow(
        Guid Id,
        AgentTaskStatus Status,
        string? RepoPath,
        string? WorktreePath,
        string? WorktreeBranch);

    internal sealed record ClassifiedFinding(
        string RepoPath,
        string Branch,
        string Path,
        Guid? TaskId,
        WorktreeHealthShape Shape,
        string Detail);

    private async Task UpsertAsync(ClassifiedFinding classified, DateTime now, CancellationToken ct)
    {
        var existing = await _db.WorktreeHealthFindings.FirstOrDefaultAsync(
            f => f.RepoPath == classified.RepoPath
                 && f.Branch == classified.Branch
                 && f.Shape == classified.Shape
                 && f.ClearedAt == null,
            ct);

        var detail = Truncate(classified.Detail, WorktreeHealthFinding.DetailMaxLength);
        var path = Truncate(classified.Path, WorktreeHealthFinding.PathMaxLength);
        if (existing is null)
        {
            _db.WorktreeHealthFindings.Add(new WorktreeHealthFinding
            {
                Id = Guid.NewGuid(),
                RepoPath = Truncate(classified.RepoPath, WorktreeHealthFinding.RepoPathMaxLength),
                Branch = Truncate(classified.Branch, WorktreeHealthFinding.BranchMaxLength),
                Path = path,
                TaskId = classified.TaskId,
                Shape = classified.Shape,
                Detail = detail,
                FirstSeenAt = now,
                LastSeenAt = now,
            });
            return;
        }

        existing.LastSeenAt = now;
        existing.Detail = detail;
        existing.Path = path;
        existing.TaskId = classified.TaskId;
    }

    private async Task<IReadOnlyList<string>> CollectReposAsync(
        IReadOnlyList<TaskRow> tasks,
        CancellationToken ct)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var repos = new HashSet<string>(comparer);

        foreach (var known in await _worktrees.ListKnownDelegateRepoPathsAsync(ct))
            AddIfPresent(repos, known);

        foreach (var task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.WorktreePath) && string.IsNullOrWhiteSpace(task.WorktreeBranch))
                continue;
            AddIfPresent(repos, task.RepoPath);
        }

        foreach (var root in _delegation.AllowedRoots)
            AddIfPresent(repos, root);

        return repos.ToList();
    }

    private static void AddIfPresent(HashSet<string> repos, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;
        repos.Add(Path.GetFullPath(path));
    }

    private static WorktreeHealthFindingDto ToDto(WorktreeHealthFinding finding) =>
        new(
            finding.Id,
            finding.RepoPath,
            finding.Branch,
            finding.Path,
            finding.TaskId,
            finding.TaskId is Guid id ? DelegationReportFormatter.Short(id) : TryShortFromBranch(finding.Branch),
            finding.Shape.ToString(),
            finding.Detail,
            finding.Shape == WorktreeHealthShape.DanglingBranchNoTask ? "Warning" : "Error",
            finding.FirstSeenAt,
            finding.LastSeenAt);

    private static string? TryShortFromBranch(string branch)
    {
        const string prefix = "feat/card-task-";
        if (!branch.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var rest = branch[prefix.Length..];
        return rest.Length == 8 ? rest : null;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private sealed class RepoBranchShapeComparer
        : IEqualityComparer<(string Repo, string Branch, WorktreeHealthShape Shape)>
    {
        public static readonly RepoBranchShapeComparer Instance = new();

        public bool Equals(
            (string Repo, string Branch, WorktreeHealthShape Shape) x,
            (string Repo, string Branch, WorktreeHealthShape Shape) y) =>
            string.Equals(x.Repo, y.Repo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Branch, y.Branch, StringComparison.OrdinalIgnoreCase)
            && x.Shape == y.Shape;

        public int GetHashCode((string Repo, string Branch, WorktreeHealthShape Shape) obj) =>
            HashCode.Combine(
                obj.Repo.ToLowerInvariant(),
                obj.Branch.ToLowerInvariant(),
                obj.Shape);
    }
}
