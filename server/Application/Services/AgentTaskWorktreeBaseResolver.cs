using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public enum CardWorktreeBaseDecision { Target, Continue, WaitForLand, Ambiguous, Unknown }

public sealed record CardWorktreeBaseSelection(
    CardWorktreeBaseDecision Decision,
    string FallbackRef,
    Guid? SourceTaskId = null,
    string? SourceBranch = null,
    string? SourceSha = null,
    IReadOnlyList<string>? Warnings = null,
    string? Reason = null,
    int TotalCandidates = 0,
    int InspectedCandidates = 0,
    int GitCommands = 0,
    string? LandingTarget = null,
    DateTime? ObservedAt = null)
{
    public IReadOnlyList<string> CandidateWarnings => Warnings ?? [];
    public bool Incomplete => Reason is "inspection_timeout" or "candidate_limit"
        or "git_command_limit" or "git_inspection_error";
}

/// <summary>
/// Selects only a committed, quiescent, same-card tip from the same local Git object store.
/// Selection is advisory at create and is repeated immediately before worktree creation.
/// </summary>
public sealed class AgentTaskWorktreeBaseResolver(
    AppDbContext db, ILandingGit git, IOptions<GitSettings> settings,
    TimeProvider? clock = null, GitProcessGate? processGate = null)
{
    private readonly GitSettings _settings = settings.Value;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly GitProcessGate _processGate = processGate ?? SharedProcessGate;
    private static readonly GitProcessGate SharedProcessGate = new();
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async Task<CardWorktreeBaseSelection> ResolveAsync(AgentTask task, CancellationToken ct)
    {
        var fallback = task.MergeTargetRef;
        if (string.IsNullOrWhiteSpace(fallback))
        {
            var projectDefault = task.ProjectId is Guid projectId
                ? await db.Projects.AsNoTracking().Where(p => p.Id == projectId)
                    .Select(p => p.BaseBranch).FirstOrDefaultAsync(ct)
                : null;
            fallback = WorktreeBaseResolver.ChooseConfiguredDefault(projectDefault, _settings.DefaultBranch);
        }
        CardWorktreeBaseSelection Target() => new(CardWorktreeBaseDecision.Target, fallback,
            LandingTarget: task.MergeTargetRef ?? "master", ObservedAt: DateTime.UtcNow);

        if (task.Workspace != WorkspaceMode.Worktree || task.CardId is null || task.RepoPath is null
            || task.RepairSourceTaskId is not null || task.SourceLandingOperationId is not null
            || task.WorktreeBaseRequestedRef is not null)
            return Target();

        // Completion evidence is read before any candidate-specific Git observation. A retained
        // branch of an already-landed task is not an automatic source, even after a rebase.
        var rows = await db.AgentTasks.AsNoTracking()
            .Where(t => t.Id != task.Id && t.CardId == task.CardId
                && t.Workspace == WorkspaceMode.Worktree && t.WorktreeBranch != null)
            .OrderBy(t => t.Id).ToListAsync(ct);
        if (rows.Count == 0) return Target();
        var rowIds = rows.Select(t => t.Id).ToArray();
        var landed = (await db.AgentTaskEvents.AsNoTracking()
            .Where(e => rowIds.Contains(e.AgentTaskId)
                && (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.LandedWithResidue))
            .Select(e => e.AgentTaskId).Distinct().ToListAsync(ct)).ToHashSet();
        var currentRequestIds = rows.Select(t => t.CurrentLandRequestId).OfType<Guid>().ToArray();
        var pendingLand = (await db.AgentTaskLandRequests.AsNoTracking()
            .Where(r => currentRequestIds.Contains(r.Id) && r.IsPending
                && (r.State == LandRequestState.Queued || r.State == LandRequestState.Held
                    || r.State == LandRequestState.Running))
            .Select(r => r.TaskId).ToListAsync(ct)).ToHashSet();
        var kept = rows.Where(t => !landed.Contains(t.Id)
                || task.RequestedWorktreeBaseMode == RequestedWorktreeBaseMode.Task
                    && task.RequestedWorktreeBaseTaskId == t.Id)
            .GroupBy(t => t.WorktreeBranch!, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(t => t.Id == task.RequestedWorktreeBaseTaskId)
                .ThenByDescending(t => t.Status == AgentTaskStatus.Succeeded)
                .ThenByDescending(t => t.CompletedAt)
                .ThenBy(t => t.Id.ToString("D"), StringComparer.Ordinal).First())
            .OrderBy(t => t.Id).ToArray();
        if (kept.Length == 0) return Target();
        var inspectable = kept.Where(t => t.Status == AgentTaskStatus.Succeeded
            || pendingLand.Contains(t.Id)
            || task.RequestedWorktreeBaseMode == RequestedWorktreeBaseMode.Task
                && task.RequestedWorktreeBaseTaskId == t.Id).ToArray();
        var totalCandidates = inspectable.Length;

        using var inspection = new Inspection(git, task.RepoPath, _settings, _clock, _processGate, ct);
        var warnings = new List<string>();
        foreach (var row in kept.Except(inspectable))
            warnings.Add($"Task {Short(row.Id)} branch {row.WorktreeBranch} is {row.Status}; it is not an automatic source.");
        var inspected = 0;
        CardWorktreeBaseSelection Result(CardWorktreeBaseDecision decision,
            Guid? id = null, string? branch = null, string? sha = null, string? reason = null) =>
            new(decision, fallback, id, branch, sha, warnings.Take(inspection.MaxCandidates).ToArray(),
                reason, totalCandidates, inspected, inspection.Commands, task.MergeTargetRef ?? "master", DateTime.UtcNow);

        try
        {
            // A pending integration takes precedence over both inventory truncation and a
            // divergent-tip refusal. When evidence cannot rule it out, leave the task queued.
            if (inspectable.Length > inspection.MaxCandidates)
            {
                if (kept.Any(t => pendingLand.Contains(t.Id)
                    && SameDestination(t.MergeTargetRef, task.MergeTargetRef)))
                    return Result(CardWorktreeBaseDecision.WaitForLand, reason: "pending_land_uninspected");
                warnings.Add($"Same-card branch inspection incomplete: candidate_limit ({inspectable.Length} total, "
                    + $"0 inspected, {inspectable.Length} omitted). Retry or select a base explicitly.");
                if (task.RequestedWorktreeBaseMode == RequestedWorktreeBaseMode.Task)
                    inspectable = inspectable.Where(t => t.Id == task.RequestedWorktreeBaseTaskId).ToArray();
                else
                    return Result(task.RequestedWorktreeBaseMode == RequestedWorktreeBaseMode.Target
                        ? CardWorktreeBaseDecision.Target : CardWorktreeBaseDecision.Unknown,
                        reason: "candidate_limit");
            }

            var common = await inspection.CommonAsync(task.RepoPath);
            var target = await inspection.CommitAsync(fallback);
            if (target is null && task.MergeTargetRef is null)
            {
                fallback = "HEAD"; // CARD-0508's configured-default probe failed.
                target = await inspection.CommitAsync("HEAD");
            }
            if (target is null)
            {
                warnings.Add($"Base inspection unknown: target {fallback} does not resolve to a commit.");
                return Result(CardWorktreeBaseDecision.Unknown, reason: "target_missing");
            }

            var writers = await db.AgentTasks.AsNoTracking()
                .Where(t => t.Id != task.Id
                    && (t.Status == AgentTaskStatus.Queued || t.Status == AgentTaskStatus.Dispatched
                        || t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Blocked))
                .Select(t => new { t.Id, t.WorktreePath, t.WorkingDirectory }).ToListAsync(ct);
            var eligible = new List<(AgentTask Task, string Sha)>();
            (AgentTask Task, string Sha)? explicitSource = null;
            var branches = inspectable.Select(t => t.WorktreeBranch!).ToArray();
            var commits = await inspection.BranchCommitsAsync(branches);
            var merged = await inspection.MergedBranchesAsync(target, branches);
            var containment = new Dictionary<string, DelegationWorktreeService.CommitContainment>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in inspectable)
            {
                inspected++;
                var branch = row.WorktreeBranch!;
                var requested = task.RequestedWorktreeBaseMode == RequestedWorktreeBaseMode.Task
                    && task.RequestedWorktreeBaseTaskId == row.Id;
                if (!SameDestination(row.MergeTargetRef, task.MergeTargetRef))
                {
                    warnings.Add($"Task {Short(row.Id)} branch {branch} has a different landing destination.");
                    continue;
                }
                // The original checkout may have been removed while a linked checkout and
                // its local branch still survive in the same common object store.
                var repo = row.RepoPath is { } original && Directory.Exists(original)
                    ? original : row.WorktreePath;
                if (repo is null || !Directory.Exists(repo))
                {
                    warnings.Add($"Task {Short(row.Id)} branch {branch} repository is unavailable.");
                    if (pendingLand.Contains(row.Id))
                        return Result(CardWorktreeBaseDecision.WaitForLand, reason: "pending_land_unknown");
                    continue;
                }
                string candidateCommon;
                try { candidateCommon = await inspection.CommonAsync(repo); }
                catch (Exception ex) when (ex is not OperationCanceledException and not InspectionLimitException)
                {
                    warnings.Add($"Task {Short(row.Id)} branch {branch} repository identity is unknown ({ex.GetType().Name}).");
                    if (pendingLand.Contains(row.Id))
                        return Result(CardWorktreeBaseDecision.WaitForLand, reason: "pending_land_unknown");
                    continue;
                }
                if (!string.Equals(common, candidateCommon, PathComparison))
                {
                    warnings.Add($"Task {Short(row.Id)} branch {branch} belongs to another Git repository.");
                    continue;
                }
                var sha = commits.GetValueOrDefault(branch);
                if (sha is null)
                {
                    warnings.Add($"Task {Short(row.Id)} branch {branch} has no local commit.");
                    if (pendingLand.Contains(row.Id))
                        return Result(CardWorktreeBaseDecision.WaitForLand, reason: "pending_land_unknown");
                    continue;
                }
                // An explicit task names its own validated commit even if that history is
                // already on the target or contains an off-target merge. Containment decides
                // automatic inheritance, not whether the caller may choose a safe source.
                var contained = requested && (!pendingLand.Contains(row.Id) || landed.Contains(row.Id))
                    ? DelegationWorktreeService.CommitContainment.NotContained
                    : merged.Contains(branch)
                        ? DelegationWorktreeService.CommitContainment.Contained
                        : containment.TryGetValue(sha, out var cachedContainment) ? cachedContainment
                        : containment[sha] = await inspection.ContainsUnmergedAsync(sha, target);
                if (pendingLand.Contains(row.Id) && !landed.Contains(row.Id)
                    && contained != DelegationWorktreeService.CommitContainment.Contained)
                    return Result(CardWorktreeBaseDecision.WaitForLand, row.Id, branch, sha, "pending_land");
                if (contained == DelegationWorktreeService.CommitContainment.Contained)
                {
                    if (!requested) continue;
                }
                if (contained == DelegationWorktreeService.CommitContainment.Unknown && !requested)
                {
                    warnings.Add($"Task {Short(row.Id)} branch {branch} @ {sha} containment is unknown (merge range or Git error).");
                    continue;
                }
                if (row.Status != AgentTaskStatus.Succeeded && !requested)
                {
                    warnings.Add($"Task {Short(row.Id)} branch {branch} @ {sha} is {row.Status}; it is not an automatic source.");
                    continue;
                }
                // A Blocked source owns its checkout but is quiescent. Another task using
                // that checkout, including a Shared follow-up, is still an open writer.
                if (row.WorktreePath is not null && writers.Any(w => w.Id != row.Id
                    && (string.Equals(w.WorktreePath, row.WorktreePath, PathComparison)
                        || string.Equals(w.WorkingDirectory, row.WorktreePath, PathComparison))))
                {
                    warnings.Add($"Task {Short(row.Id)} branch {branch} has an open writer.");
                    continue;
                }
                if (requested)
                {
                    if (row.Status is AgentTaskStatus.Queued or AgentTaskStatus.Dispatched or AgentTaskStatus.Working
                        || contained == DelegationWorktreeService.CommitContainment.Unknown)
                    {
                        warnings.Add($"Requested task {Short(row.Id)} branch {branch} is not a validated quiescent source.");
                        continue;
                    }
                    bool safe;
                    try { safe = await inspection.CheckoutSafeAsync(branch); }
                    catch (Exception ex) when (ex is not OperationCanceledException and not InspectionLimitException)
                    {
                        warnings.Add($"Task {Short(row.Id)} branch {branch} inspection is unknown ({ex.GetType().Name}).");
                        continue;
                    }
                    if (!safe)
                    {
                        warnings.Add($"Task {Short(row.Id)} branch {branch} has dirty or in-progress working files; those bytes are not inherited.");
                        continue;
                    }
                    explicitSource = (row, sha);
                }
                eligible.Add((row, sha));
            }

            if (task.RequestedWorktreeBaseMode == RequestedWorktreeBaseMode.Target)
            {
                foreach (var tip in eligible)
                    warnings.Add($"Fresh worktree omits task {Short(tip.Task.Id)} branch {tip.Task.WorktreeBranch} @ {tip.Sha}.");
                return Result(CardWorktreeBaseDecision.Target);
            }
            if (task.RequestedWorktreeBaseMode == RequestedWorktreeBaseMode.Task)
                return explicitSource is { } source
                    ? Result(CardWorktreeBaseDecision.Continue, source.Task.Id,
                        source.Task.WorktreeBranch, source.Sha)
                    : Result(CardWorktreeBaseDecision.Unknown, reason: "requested_source_invalid");

            var remaining = eligible.ToList();
            List<(AgentTask Task, string Sha)> maximal;
            while (true)
            {
                var tips = remaining.GroupBy(x => x.Sha, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(x => x.Task.CompletedAt)
                        .ThenBy(x => x.Task.Id.ToString("D"), StringComparer.Ordinal).First()).ToArray();
                maximal = new List<(AgentTask Task, string Sha)>();
                foreach (var tip in tips)
                {
                    var dominated = false;
                    foreach (var other in tips)
                    {
                        if (tip.Sha == other.Sha) continue;
                        if (await inspection.AncestorAsync(tip.Sha, other.Sha)) { dominated = true; break; }
                    }
                    if (!dominated) maximal.Add(tip);
                }
                var unsafeTip = false;
                foreach (var tip in maximal.ToArray())
                {
                    bool safe;
                    var unknown = false;
                    try { safe = await inspection.CheckoutSafeAsync(tip.Task.WorktreeBranch!); }
                    catch (Exception ex) when (ex is not OperationCanceledException and not InspectionLimitException)
                    {
                        warnings.Add($"Task {Short(tip.Task.Id)} branch {tip.Task.WorktreeBranch} inspection is unknown ({ex.GetType().Name}).");
                        safe = false;
                        unknown = true;
                    }
                    if (safe) continue;
                    if (!unknown)
                        warnings.Add($"Task {Short(tip.Task.Id)} branch {tip.Task.WorktreeBranch} has dirty or in-progress working files; those bytes are not inherited.");
                    remaining.RemoveAll(x => x.Task.Id == tip.Task.Id);
                    unsafeTip = true;
                }
                if (!unsafeTip) break;
            }
            if (maximal.Count > 1)
            {
                foreach (var tip in maximal)
                    warnings.Add($"Task {Short(tip.Task.Id)} branch {tip.Task.WorktreeBranch} @ {tip.Sha} is a competing tip.");
                return Result(CardWorktreeBaseDecision.Ambiguous, reason: "worktree_base_ambiguous");
            }
            if (maximal.Count == 1)
            {
                var winner = maximal[0];
                return Result(CardWorktreeBaseDecision.Continue, winner.Task.Id,
                    winner.Task.WorktreeBranch, winner.Sha);
            }
            return Result(CardWorktreeBaseDecision.Target);
        }
        catch (InspectionLimitException ex)
        {
            warnings.Add($"Same-card branch inspection incomplete: {ex.Reason} ({totalCandidates} total, "
                + $"{inspected} inspected, {Math.Max(0, totalCandidates - inspected)} omitted). Retry or select a base explicitly.");
            if (kept.Any(t => pendingLand.Contains(t.Id)
                && SameDestination(t.MergeTargetRef, task.MergeTargetRef)))
                return Result(CardWorktreeBaseDecision.WaitForLand, reason: "pending_land_unknown");
            return Result(task.RequestedWorktreeBaseMode == RequestedWorktreeBaseMode.Target
                ? CardWorktreeBaseDecision.Target : CardWorktreeBaseDecision.Unknown,
                reason: ex.Reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Same-card branch inspection unknown ({ex.GetType().Name}); retry or select a base explicitly.");
            if (kept.Any(t => pendingLand.Contains(t.Id)
                && SameDestination(t.MergeTargetRef, task.MergeTargetRef)))
                return Result(CardWorktreeBaseDecision.WaitForLand, reason: "pending_land_unknown");
            return Result(task.RequestedWorktreeBaseMode == RequestedWorktreeBaseMode.Target
                ? CardWorktreeBaseDecision.Target : CardWorktreeBaseDecision.Unknown,
                reason: "git_inspection_error");
        }
    }

    private static bool SameDestination(string? left, string? right) =>
        string.Equals(left ?? "master", right ?? "master", StringComparison.Ordinal);

    private static string Short(Guid id) => id.ToString("N")[..8];

    private sealed class InspectionLimitException(string reason) : Exception(reason)
    {
        public string Reason { get; } = reason;
    }

    private sealed class Inspection : IDisposable
    {
        private readonly ILandingGit git;
        private readonly string repository;
        private readonly CancellationToken caller;
        private readonly TimeProvider _clock;
        private readonly GitProcessGate _gate;
        private readonly long _started;
        private readonly TimeSpan _limit;
        private readonly CancellationTokenSource _deadline;
        private readonly CancellationTokenSource _budget;
        private readonly int _maxCommands;
        private readonly Dictionary<string, string> _commons = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _commits = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _checkouts = new(StringComparer.Ordinal);
        private bool _worktreesListed;
        public int Commands { get; private set; }
        public int MaxCandidates { get; }

        public Inspection(ILandingGit git, string repository, GitSettings settings,
            TimeProvider clock, GitProcessGate gate, CancellationToken caller)
        {
            this.git = git;
            this.repository = repository;
            this.caller = caller;
            _clock = clock;
            _gate = gate;
            _started = clock.GetTimestamp();
            _limit = TimeSpan.FromSeconds(Math.Max(1, settings.WorktreeBaseInspectionTimeoutSeconds));
            _deadline = new CancellationTokenSource(_limit, clock);
            _budget = CancellationTokenSource.CreateLinkedTokenSource(caller, _deadline.Token);
            _maxCommands = Math.Max(1, settings.WorktreeBaseMaxGitCommands);
            MaxCandidates = Math.Max(1, settings.WorktreeBaseMaxCandidates);
        }

        private void CheckDeadline()
        {
            caller.ThrowIfCancellationRequested();
            if (_clock.GetElapsedTime(_started) >= _limit || _deadline.IsCancellationRequested)
                throw new InspectionLimitException("inspection_timeout");
        }

        private async Task<LandingGitResult> RunAsync(string repo, params string[] args)
        {
            CheckDeadline();
            if (Commands >= _maxCommands) throw new InspectionLimitException("git_command_limit");
            try
            {
                using var admission = await _gate.EnterAsync(_budget.Token);
                CheckDeadline();
                Commands++;
                var result = await git.RunAsync(repo, args, _budget.Token);
                CheckDeadline();
                return result;
            }
            catch (OperationCanceledException) when (!caller.IsCancellationRequested)
            { throw new InspectionLimitException("inspection_timeout"); }
        }

        public async Task<string> CommonAsync(string repo)
        {
            if (_commons.TryGetValue(repo, out var cached)) return cached;
            var result = await RunAsync(repo, "rev-parse", "--path-format=absolute", "--git-common-dir");
            if (!result.Succeeded) throw new IOException("git_common_directory_unavailable");
            string canonical;
            try
            {
                canonical = await git.CanonicalDirectoryAsync(result.Output.Trim(), _budget.Token);
                CheckDeadline();
            }
            catch (OperationCanceledException) when (!caller.IsCancellationRequested)
            { throw new InspectionLimitException("inspection_timeout"); }
            return _commons[repo] = canonical;
        }

        public async Task<string?> CommitAsync(string reference)
        {
            if (_commits.TryGetValue(reference, out var cached)) return cached;
            var result = await RunAsync(repository, "rev-parse", "--verify", "--quiet", $"{reference}^{{commit}}");
            if (!result.Succeeded && result.ExitCode != 1)
                throw new IOException("git_commit_probe_failed");
            var sha = result.Succeeded ? result.Output.Trim() : null;
            return _commits[reference] = sha is { Length: 40 or 64 }
                && sha.All(Uri.IsHexDigit) ? sha.ToLowerInvariant() : null;
        }

        public async Task<Dictionary<string, string>> BranchCommitsAsync(string[] branches)
        {
            var result = await RunAsync(repository,
                ["for-each-ref", "--format=%(objectname) %(refname)",
                    .. branches.Select(b => "refs/heads/" + b)]);
            if (!result.Succeeded) throw new IOException("git_branch_tips_unavailable");
            var commits = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split(' ', 2);
                if (parts.Length != 2 || !parts[1].StartsWith("refs/heads/", StringComparison.Ordinal)
                    || !GitObjectId.IsFull(parts[0])) continue;
                commits[parts[1][11..]] = parts[0].ToLowerInvariant();
            }
            return commits;
        }

        public async Task<HashSet<string>> MergedBranchesAsync(string target, string[] branches)
        {
            var result = await RunAsync(repository,
                ["for-each-ref", "--format=%(refname)", $"--merged={target}",
                    .. branches.Select(b => "refs/heads/" + b)]);
            if (!result.Succeeded) throw new IOException("git_merged_branches_unavailable");
            return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("refs/heads/", StringComparison.Ordinal))
                .Select(line => line[11..]).ToHashSet(StringComparer.Ordinal);
        }

        public async Task<bool> AncestorAsync(string ancestor, string descendant)
        {
            var result = await RunAsync(repository, "merge-base", "--is-ancestor", ancestor, descendant);
            if (result.ExitCode is not (0 or 1)) throw new InspectionLimitException("git_command_error");
            return result.Succeeded;
        }

        public async Task<DelegationWorktreeService.CommitContainment> ContainsAsync(string source, string target)
        {
            var ancestor = await RunAsync(repository, "merge-base", "--is-ancestor", source, target);
            if (ancestor.Succeeded) return DelegationWorktreeService.CommitContainment.Contained;
            if (ancestor.ExitCode != 1) return DelegationWorktreeService.CommitContainment.Unknown;
            return await ContainsUnmergedAsync(source, target);
        }

        public async Task<DelegationWorktreeService.CommitContainment> ContainsUnmergedAsync(string source, string target)
        {
            var merges = await RunAsync(repository, "rev-list", "--max-count=1", "--min-parents=2", $"{target}..{source}");
            if (!merges.Succeeded || !string.IsNullOrWhiteSpace(merges.Output))
                return DelegationWorktreeService.CommitContainment.Unknown;
            var cherry = await RunAsync(repository, "cherry", target, source);
            if (!cherry.Succeeded) return DelegationWorktreeService.CommitContainment.Unknown;
            var lines = cherry.Output.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Any(static line => !line.StartsWith('+') && !line.StartsWith('-')))
                return DelegationWorktreeService.CommitContainment.Unknown;
            return lines.Any(static line => line.StartsWith('+'))
                ? DelegationWorktreeService.CommitContainment.NotContained
                : DelegationWorktreeService.CommitContainment.Contained;
        }

        public async Task<bool> CheckoutSafeAsync(string branch)
        {
            if (!_worktreesListed)
            {
                var listing = await RunAsync(repository, "worktree", "list", "--porcelain");
                if (!listing.Succeeded) throw new IOException("git_worktree_listing_failed");
                string? current = null;
                foreach (var line in listing.Output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                {
                    if (line.StartsWith("worktree ", StringComparison.Ordinal)) current = line[9..];
                    if (line.StartsWith("branch refs/heads/", StringComparison.Ordinal))
                        _checkouts[line[18..]] = current;
                }
                _worktreesListed = true;
            }
            var path = _checkouts.GetValueOrDefault(branch);
            if (path is null) return true;
            if (!Directory.Exists(path)) return false;
            var status = await RunAsync(path, "status", "--porcelain", "--untracked-files=all", "--ignore-submodules=none");
            if (!status.Succeeded) throw new IOException("git_checkout_status_failed");
            if (status.Output.Length != 0) return false;
            foreach (var state in new[] { "MERGE_HEAD", "rebase-merge", "rebase-apply" })
            {
                var location = await RunAsync(path, "rev-parse", "--git-path", state);
                if (!location.Succeeded) throw new IOException("git_checkout_operation_probe_failed");
                var gitPath = location.Output.Trim();
                if (File.Exists(gitPath) || Directory.Exists(gitPath)) return false;
            }
            return true;
        }

        public void Dispose()
        {
            _budget.Dispose();
            _deadline.Dispose();
        }
    }
}
