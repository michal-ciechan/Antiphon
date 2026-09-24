using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0328 S3: classify leftover <c>feat/card-task-*</c> worktrees, directories, and branches
/// against <c>AgentTasks</c>. Removes <see cref="WorktreeResidueLabel.Eligible"/> rows only when
/// <c>WorktreeResidue:Execute</c> is true.
/// </summary>
public sealed class WorktreeResidueSweepService
{
    private static readonly AgentTaskStatus[] LiveStatuses =
    [
        AgentTaskStatus.Queued,
        AgentTaskStatus.Dispatched,
        AgentTaskStatus.Working,
        AgentTaskStatus.Blocked
    ];

    private readonly AppDbContext _db;
    private readonly IWorktreeManager _worktrees;
    private readonly WorktreeResidueSettings _settings;
    private readonly GitSettings _git;
    private readonly TimeProvider _clock;
    private readonly ILogger<WorktreeResidueSweepService> _logger;
    private readonly TaskWorktreeRetirementService? _retirement;
    private readonly AgentTaskLandService? _lands;
    private readonly IWorkspaceReservationJournal? _reservations;

    public WorktreeResidueSweepService(
        AppDbContext db,
        IWorktreeManager worktrees,
        IOptions<WorktreeResidueSettings> settings,
        IOptions<GitSettings> git,
        TimeProvider clock,
        ILogger<WorktreeResidueSweepService> logger,
        TaskWorktreeRetirementService? retirement = null,
        AgentTaskLandService? lands = null,
        IWorkspaceReservationJournal? reservations = null)
    {
        _db = db;
        _worktrees = worktrees;
        _settings = settings.Value;
        _git = git.Value;
        _clock = clock;
        _logger = logger;
        _retirement = retirement;
        _lands = lands;
        _reservations = reservations;
    }

    public Task<WorktreeResidueRunDto> PreviewAsync(Guid? projectId, Guid? boardId, CancellationToken ct) =>
        PersistRunAsync(execute: false, preview: true, projectId, boardId, ct);

    public async Task<WorktreeResidueResult> RunAsync(CancellationToken cancellationToken)
    {
        if (_retirement is not null)
        {
            var dto = await PersistRunAsync(_settings.Execute, preview: false, null, null, cancellationToken);
            return ToLegacyResult(dto);
        }

        var started = _clock.GetUtcNow();
        var utcNow = started.UtcDateTime;
        var defaultBranch = string.IsNullOrWhiteSpace(_git.DefaultBranch) ? "master" : _git.DefaultBranch;

        var tasks = await LoadTasksAsync(cancellationToken);
        var extraRepos = tasks
            .Select(t => t.RepoPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(PathComparer)
            .ToList();

        var scan = await _worktrees.ScanResidueCandidatesAsync(extraRepos, cancellationToken);
        var facts = new List<WorktreeResidueFacts>(scan.Count);
        foreach (var entry in scan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var task = MatchTask(entry, tasks);
            var target = task?.MergeTargetRef;
            if (string.IsNullOrWhiteSpace(target))
                target = defaultBranch;
            var repo = FirstNonEmpty(entry.RepoPath, task?.RepoPath);
            var git = await _worktrees.InspectResidueAsync(
                repo, entry.Path, entry.Branch, target, cancellationToken);
            facts.Add(new WorktreeResidueFacts(
                entry.Path,
                entry.Branch,
                repo,
                target,
                task,
                entry.DirectoryExists,
                git.BranchExists,
                git.IsAncestor,
                git.AheadCount,
                git.HasTrackedModifications,
                git.HasUntrackedModifications));
        }

        var classified = Classify(facts, _settings, utcNow);
        var rows = new List<WorktreeResidueRow>(classified.Count);
        var removed = 0;

        foreach (var row in classified)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_settings.Execute && row.Label == WorktreeResidueLabel.Eligible)
            {
                var acted = await TryRemoveEligibleAsync(row, cancellationToken);
                rows.Add(acted);
                if (acted.Residue is null && !acted.Keep)
                    removed++;
            }
            else
            {
                rows.Add(row);
            }
        }

        var counts = Count(rows, removed);
        return new WorktreeResidueResult(
            started,
            _clock.GetUtcNow() - started,
            _settings.Execute,
            counts,
            rows,
            rows.Where(r => r.Keep).ToList(),
            removed);
    }

    private async Task<WorktreeResidueRunDto> PersistRunAsync(
        bool execute, bool preview, Guid? projectId, Guid? boardId, CancellationToken ct)
    {
        await ReconcileReservationsAsync(ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        var run = new Domain.Entities.WorktreeResidueRun
        {
            Id = Guid.NewGuid(),
            StartedAt = now,
            Execute = execute && !preview,
            Preview = preview,
            ActionBudget = Math.Max(0, _settings.MaxActionsPerRun),
            ProjectId = projectId,
            BoardId = boardId,
        };
        _db.WorktreeResidueRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        var tasks = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.WorktreePath != null || t.WorktreeBranch != null)
            .ToListAsync(ct);
        if (projectId is Guid pid)
            tasks = tasks.Where(t => t.ProjectId == pid).ToList();

        var extraRepos = tasks.Select(t => t.RepoPath).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!)
            .Distinct(PathComparer).ToList();
        var scan = await _worktrees.ScanResidueCandidatesAsync(extraRepos, ct);
        var landings = await _db.AgentTaskLandings.AsNoTracking()
            .Where(o => o.Active && o.Cleanup != LandCleanupStatus.Complete
                && (o.Publication == LandPublicationOutcome.Landed || o.Publication == LandPublicationOutcome.AlreadyPresent))
            .ToListAsync(ct);
        var releases = await _db.TaskWorktreeRetirements.AsNoTracking().Where(r => r.Active).ToListAsync(ct);

        var actions = 0;
        foreach (var task in tasks.OrderBy(t => t.CompletedAt ?? t.CreatedAt).ThenBy(t => t.Id))
        {
            ct.ThrowIfCancellationRequested();
            var release = releases.FirstOrDefault(r => r.TaskId == task.Id && r.TaskAttempt == task.Attempt);
            var outcome = WorktreeResidueCandidateOutcome.Held;
            var code = "release_required";
            if (release?.State == WorktreeRetirementState.Complete)
            {
                outcome = WorktreeResidueCandidateOutcome.Removed;
                code = "removed";
                run.Removed++;
            }
            else
            {
            var (authorized, reason) = _retirement is null
                ? (false, "release_required")
                : await _retirement.EvaluateEligibilityAsync(task, release, ct);
            code = reason ?? "release_required";
            if (authorized && execute && !preview && actions < run.ActionBudget && _retirement is not null && release is not null)
            {
                var removed = await _retirement.TryRetireAsync(release, run.Id, ct);
                actions++;
                if (removed.IsClean)
                {
                    outcome = WorktreeResidueCandidateOutcome.Removed;
                    code = "removed";
                    run.Removed++;
                }
                else if (removed.DirectoryGone || removed.Unregistered || removed.BranchDeleted)
                {
                    outcome = WorktreeResidueCandidateOutcome.Partial;
                    code = removed.Residue ?? "partial";
                    run.Partial++;
                }
                else
                {
                    outcome = WorktreeResidueCandidateOutcome.Refused;
                    code = removed.Residue ?? "refused";
                    run.Refused++;
                    await PersistCooldownAsync("task:" + task.Id.ToString("N"), ct);
                }
            }
            else if (!authorized)
            {
                run.Held++;
            }
            }

            _db.WorktreeResidueRunCandidates.Add(new Domain.Entities.WorktreeResidueRunCandidate
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                Lane = nameof(WorktreeResidueLane.SettledTask),
                Outcome = outcome.ToString(),
                ReasonCode = code,
                TaskId = task.Id,
                RetirementId = release?.Id,
                Path = task.WorktreePath,
                Branch = task.WorktreeBranch,
                EvaluatedAt = now,
            });
            run.Candidates++;
        }

        foreach (var op in landings)
        {
            ct.ThrowIfCancellationRequested();
            var outcome = WorktreeResidueCandidateOutcome.Held;
            var code = "publication_retry";
            Guid? requestId = null;
            if (execute && !preview && actions < run.ActionBudget && _lands is not null)
            {
                try
                {
                    var queued = await _lands.RequestCleanupRetryAsync(op.TaskId, op.Id, run.Id, ct);
                    actions++;
                    outcome = WorktreeResidueCandidateOutcome.Queued;
                    code = "retry_queued";
                    requestId = queued.RequestId;
                    run.Queued++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    outcome = WorktreeResidueCandidateOutcome.Refused;
                    code = "publication_unconfirmed";
                    run.Refused++;
                }
            }
            else
            {
                run.Held++;
            }

            _db.WorktreeResidueRunCandidates.Add(new Domain.Entities.WorktreeResidueRunCandidate
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                Lane = nameof(WorktreeResidueLane.Publication),
                Outcome = outcome.ToString(),
                ReasonCode = code,
                TaskId = op.TaskId,
                LandingOperationId = op.Id,
                LandRequestId = requestId,
                Path = op.WorktreePath,
                Branch = op.SourceFullRef,
                EvaluatedAt = now,
            });
            run.Candidates++;
        }

        foreach (var entry in scan)
        {
            var matched = tasks.Count(t => PathsEqual(t.WorktreePath, entry.Path)
                || BranchesEqual(t.WorktreeBranch, entry.Branch));
            if (matched == 1) continue;
            _db.WorktreeResidueRunCandidates.Add(new Domain.Entities.WorktreeResidueRunCandidate
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                Lane = nameof(WorktreeResidueLane.Inventory),
                Outcome = WorktreeResidueCandidateOutcome.Held.ToString(),
                ReasonCode = matched == 0 ? "identity_unknown" : "identity_ambiguous",
                Path = entry.Path,
                Branch = entry.Branch,
                EvaluatedAt = now,
            });
            run.Candidates++;
            run.Held++;
        }

        run.ActionsAccepted = actions;
        run.FinishedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
        var page = await _db.WorktreeResidueRunCandidates.AsNoTracking()
            .Where(c => c.RunId == run.Id)
            .OrderBy(c => c.EvaluatedAt).ThenBy(c => c.Id)
            .Take(Math.Max(1, _settings.RunResultPageSize))
            .ToListAsync(ct);
        return new WorktreeResidueRunDto(
            run.Id, run.StartedAt, run.FinishedAt, run.Execute, run.Preview, run.ActionBudget, run.ActionsAccepted,
            run.Candidates, run.Held, run.Deferred, run.Queued, run.Refused, run.Partial, run.Removed,
            page.Select(c => new WorktreeResidueCandidateDto(
                c.Id, c.Lane, c.Outcome, c.ReasonCode, c.TaskId, c.RetirementId, c.LandingOperationId, c.LandRequestId,
                c.Path, c.Branch, c.DirectoryRemoved, c.RegistrationRemoved, c.BranchRemoved)).ToList(),
            0, _settings.RunResultPageSize, run.Candidates);
    }

    private async Task PersistCooldownAsync(string key, CancellationToken ct)
    {
        var due = _clock.GetUtcNow().UtcDateTime.AddHours(24);
        var cursor = await _db.WorktreeResidueCandidateCursors.SingleOrDefaultAsync(c => c.CandidateKey == key, ct);
        if (cursor is null)
        {
            _db.WorktreeResidueCandidateCursors.Add(new Domain.Entities.WorktreeResidueCandidateCursor
            {
                Id = Guid.NewGuid(),
                CandidateKey = key,
                LastEvaluatedAt = _clock.GetUtcNow().UtcDateTime,
                NotBefore = due,
            });
        }
        else
        {
            cursor.LastEvaluatedAt = _clock.GetUtcNow().UtcDateTime;
            cursor.NotBefore = due;
        }
    }

    /// <summary>
    /// CARD-0664 D-8: release orphaned workspace-use <c>Launch</c> rows before classifying, on
    /// daily and preview runs alike (<c>Execute</c> gates actions, not this hygiene write).
    /// Best-effort: a failed reconcile never fails the run.
    /// </summary>
    private async Task ReconcileReservationsAsync(CancellationToken ct)
    {
        if (_reservations is null) return;
        try
        {
            var released = await _reservations.ReleaseOrphanedConsumersAsync(ct);
            if (released > 0)
                _logger.LogInformation("Workspace reservations reconciled: released {Count} orphaned Launch rows", released);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Workspace reservation reconcile failed before the worktree-residue run");
        }
    }

    private WorktreeResidueResult ToLegacyResult(WorktreeResidueRunDto dto)
    {
        var rows = dto.Rows.Select(r => new WorktreeResidueRow(
            r.Path ?? "", r.Branch ?? "", null, "", r.TaskId, null,
            r.Outcome == nameof(WorktreeResidueCandidateOutcome.Removed) ? WorktreeResidueLabel.Eligible : WorktreeResidueLabel.Unknown,
            r.ReasonCode, r.ReasonCode, r.Outcome != nameof(WorktreeResidueCandidateOutcome.Removed))).ToList();
        return new WorktreeResidueResult(dto.StartedAt, TimeSpan.Zero, dto.Execute,
            new WorktreeResidueCounts(dto.Held, 0, 0, 0, 0, 0, dto.Held + dto.Refused + dto.Partial, dto.Removed),
            rows, rows.Where(r => r.Keep).ToList(), dto.Removed);
    }

    internal static IReadOnlyList<WorktreeResidueRow> Classify(
        IReadOnlyList<WorktreeResidueFacts> facts,
        WorktreeResidueSettings settings,
        DateTime utcNow)
    {
        var rows = new List<WorktreeResidueRow>(facts.Count);
        foreach (var fact in facts)
            rows.Add(ClassifyOne(fact, settings, utcNow));
        return rows;
    }

    internal static WorktreeResidueRow ClassifyOne(
        WorktreeResidueFacts facts,
        WorktreeResidueSettings settings,
        DateTime utcNow)
    {
        if (facts.Task is null)
            return Row(facts, WorktreeResidueLabel.Unknown, "Unknown", "no AgentTask row", keep: true);

        var task = facts.Task;
        if (LiveStatuses.Contains(task.Status))
            return Row(facts, WorktreeResidueLabel.Live, "Live", $"task is {task.Status}", keep: true);

        var completed = task.CompletedAt;
        if (completed is null
            || utcNow - completed.Value < TimeSpan.FromMinutes(settings.MinSettledMinutes))
        {
            var age = completed is null
                ? "completion time unknown"
                : $"settled {(int)(utcNow - completed.Value).TotalMinutes} minutes ago";
            return Row(
                facts,
                WorktreeResidueLabel.Settling,
                "Settling",
                $"{age} (min {settings.MinSettledMinutes})",
                keep: true);
        }

        if (facts.BranchExists && !facts.IsAncestor)
        {
            return Row(
                facts,
                WorktreeResidueLabel.Unmerged,
                $"Unmerged ({facts.AheadCount} ahead)",
                $"{facts.AheadCount} commit(s) not on {facts.TargetRef}",
                keep: true);
        }

        var includeUntracked = IncludeUntracked(task);
        if (facts.DirectoryExists
            && (facts.HasTrackedModifications || (includeUntracked && facts.HasUntrackedModifications)))
        {
            var why = includeUntracked
                ? "directory has tracked or untracked modifications"
                : "directory has tracked modifications";
            return Row(facts, WorktreeResidueLabel.Dirty, "Dirty", why, keep: true);
        }

        return Row(facts, WorktreeResidueLabel.Unknown, "Evidence required", "Explicit landing cleanup receipt required; legacy events and TTL grant no authority", keep: true);
    }

    private async Task<WorktreeResidueRow> TryRemoveEligibleAsync(
        WorktreeResidueRow row, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(row.RepoPath) || string.IsNullOrWhiteSpace(row.Path))
        {
            _logger.LogWarning(
                "Worktree residue eligible row skipped: missing repo or path branch={Branch} task={TaskId}",
                row.Branch,
                row.TaskId);
            return row with
            {
                Keep = true,
                Detail = "eligible but missing repo or path",
                Residue = "missing repo or path"
            };
        }

        try
        {
            var mergedInto = string.IsNullOrWhiteSpace(row.TargetRef) ? null : row.TargetRef;
            var removal = await _worktrees.TryRemoveAsync(
                row.RepoPath, row.Path, mergedInto, cancellationToken);
            if (removal.IsClean)
            {
                _logger.LogInformation(
                    "Worktree residue removed path={Path} branch={Branch} task={TaskId}",
                    row.Path,
                    row.Branch,
                    row.TaskId);
                return row;
            }

            _logger.LogWarning(
                "Worktree residue remove left residue path={Path} branch={Branch} task={TaskId} residue={Residue}",
                row.Path,
                row.Branch,
                row.TaskId,
                removal.Residue);
            return row with
            {
                Keep = true,
                Detail = removal.Residue ?? "cleanup incomplete",
                Residue = removal.Residue
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Worktree residue remove failed path={Path} branch={Branch} task={TaskId}",
                row.Path,
                row.Branch,
                row.TaskId);
            return row with
            {
                Keep = true,
                Detail = ex.Message,
                Residue = ex.Message
            };
        }
    }

    private async Task<IReadOnlyList<WorktreeResidueTaskSnapshot>> LoadTasksAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _db.AgentTasks
            .AsNoTracking()
            .Where(t => t.WorktreePath != null || t.WorktreeBranch != null)
            .Select(t => new
            {
                t.Id,
                t.Status,
                t.CompletedAt,
                t.WorktreePath,
                t.WorktreeBranch,
                t.MergeTargetRef,
                t.RepoPath,
                EventTypes = t.Events.Select(e => e.Type)
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(t => new WorktreeResidueTaskSnapshot(
                t.Id,
                t.Status,
                t.CompletedAt,
                t.WorktreePath,
                t.WorktreeBranch,
                t.MergeTargetRef,
                t.RepoPath,
                t.EventTypes.ToList()))
            .ToList();
    }

    private static WorktreeResidueTaskSnapshot? MatchTask(
        WorktreeResidueScanEntry entry,
        IReadOnlyList<WorktreeResidueTaskSnapshot> tasks)
    {
        var pathHits = tasks.Where(t => PathsEqual(t.WorktreePath, entry.Path)).ToList();
        var branchHits = tasks.Where(t => BranchesEqual(t.WorktreeBranch, entry.Branch)).ToList();
        var matches = pathHits
            .Concat(branchHits)
            .DistinctBy(t => t.Id)
            .ToList();
        if (matches.Count == 0)
            return null;
        var live = matches.FirstOrDefault(t => LiveStatuses.Contains(t.Status));
        if (live is not null)
            return live;
        if (pathHits.Count == 1)
            return pathHits[0];
        return matches
            .OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue)
            .First();
    }

    private static bool IncludeUntracked(WorktreeResidueTaskSnapshot task) => true;

    private static WorktreeResidueRow Row(
        WorktreeResidueFacts facts,
        WorktreeResidueLabel label,
        string display,
        string detail,
        bool keep) =>
        new(
            facts.Path,
            facts.Branch,
            facts.RepoPath,
            facts.TargetRef,
            facts.Task?.Id,
            facts.Task?.Status,
            label,
            display,
            detail,
            keep);

    private static WorktreeResidueCounts Count(IReadOnlyList<WorktreeResidueRow> rows, int removed) =>
        new(
            Unknown: rows.Count(r => r.Label == WorktreeResidueLabel.Unknown),
            Live: rows.Count(r => r.Label == WorktreeResidueLabel.Live),
            Settling: rows.Count(r => r.Label == WorktreeResidueLabel.Settling),
            Unmerged: rows.Count(r => r.Label == WorktreeResidueLabel.Unmerged),
            Dirty: rows.Count(r => r.Label == WorktreeResidueLabel.Dirty),
            Eligible: rows.Count(r => r.Label == WorktreeResidueLabel.Eligible),
            Kept: rows.Count(r => r.Keep),
            Removed: removed);

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        try
        {
            var a = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var b = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return a.Equals(b, PathComparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, PathComparison);
        }
    }

    private static bool BranchesEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a : !string.IsNullOrWhiteSpace(b) ? b : null;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
