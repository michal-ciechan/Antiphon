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

    public Task<CardWorktreeBaseSelection> ResolveAsync(AgentTask task, CancellationToken ct) =>
        ResolveAsync(task, null, ct);

    public async Task<CardWorktreeBaseSelection> ResolveAsync(AgentTask task,
        WorktreeBaseInspection? sharedInspection, CancellationToken ct)
    {
        var fallback = task.MergeTargetRef ?? _settings.DefaultBranch;
        CardWorktreeBaseSelection Target() => new(CardWorktreeBaseDecision.Target, fallback,
            LandingTarget: task.MergeTargetRef ?? "master", ObservedAt: _clock.GetUtcNow().UtcDateTime);
        using var ownedInspection = sharedInspection is null
            ? new WorktreeBaseInspection(git, task.RepoPath ?? "", _settings, _clock, _processGate, ct) : null;
        var inspection = sharedInspection ?? ownedInspection!;
        var warnings = new List<string>();
        var totalCandidates = 0;
        var inspected = 0;
        var pendingKnown = false;
        var pendingLand = new HashSet<Guid>();
        var landed = new HashSet<Guid>();
        AgentTask[] kept = [];
        CardWorktreeBaseSelection Result(CardWorktreeBaseDecision decision,
            Guid? id = null, string? branch = null, string? sha = null, string? reason = null) =>
            new(decision, fallback, id, branch, sha, warnings.Take(inspection.MaxCandidates).ToArray(),
                reason, totalCandidates, inspected, inspection.Commands, task.MergeTargetRef ?? "master", _clock.GetUtcNow().UtcDateTime);
        bool HasPending() => !pendingKnown || pendingLand.Count > 0;
        try
        {
            if (string.IsNullOrWhiteSpace(task.MergeTargetRef))
            {
                var projectDefault = task.ProjectId is Guid projectId
                    ? await inspection.ReadAsync(token => db.Projects.AsNoTracking().Where(p => p.Id == projectId)
                        .Select(p => p.BaseBranch).FirstOrDefaultAsync(token)) : null;
                fallback = WorktreeBaseResolver.ChooseConfiguredDefault(projectDefault, _settings.DefaultBranch);
            }
            if (task.Workspace != WorkspaceMode.Worktree || task.CardId is null || task.RepoPath is null
                || task.RepairSourceTaskId is not null || task.SourceLandingOperationId is not null
                || task.WorktreeBaseRequestedRef is not null)
                return Target();
            var inventory = await new CardSiblingInventory(db).ReadAsync(task, false, inspection, ct);
            pendingKnown = true;
            landed = inventory.Landed.ToHashSet();
            pendingLand = inventory.PendingLands.Where(p => SameDestination(p.Task.MergeTargetRef, task.MergeTargetRef)
                    && !landed.Contains(p.Task.Id)).Select(p => p.Task.Id).ToHashSet();
            totalCandidates = inventory.Rows.Count;
            if (inventory.Overflow || inventory.PendingOverflow)
                throw new WorktreeBaseInspectionLimitException("candidate_limit");
            kept = inventory.Rows.Where(t => !landed.Contains(t.Id)
                    || task.RequestedWorktreeBaseMode == RequestedWorktreeBaseMode.Task
                        && task.RequestedWorktreeBaseTaskId == t.Id)
                .GroupBy(t => t.WorktreeBranch!, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(t => t.Id == task.RequestedWorktreeBaseTaskId)
                    .ThenByDescending(t => t.Status == AgentTaskStatus.Succeeded)
                    .ThenByDescending(t => t.CompletedAt).ThenBy(t => t.Id).First())
                .OrderBy(t => t.Id).ToArray();
            var inspectable = kept.Where(t => t.Status == AgentTaskStatus.Succeeded
                || pendingLand.Contains(t.Id)
                || task.RequestedWorktreeBaseMode == RequestedWorktreeBaseMode.Task
                    && task.RequestedWorktreeBaseTaskId == t.Id).ToArray();
            foreach (var row in kept.Except(inspectable))
                warnings.Add($"Task {Short(row.Id)} branch {row.WorktreeBranch} is {row.Status}; it is not an automatic source.");
            totalCandidates = inspectable.Length;
            if (inspectable.Length == 0 && !HasPending()) return Target();
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

            var eligible = new List<(AgentTask Task, string Sha)>();
            (AgentTask Task, string Sha)? explicitSource = null;
            var branches = inspectable.Select(t => t.WorktreeBranch!)
                .Concat(inventory.PendingLands.Where(p => pendingLand.Contains(p.Task.Id))
                    .Select(p => p.Task.WorktreeBranch!)).Distinct(StringComparer.Ordinal).ToArray();
            var commits = await inspection.BranchCommitsAsync(branches);
            var merged = await inspection.MergedBranchesAsync(target, branches);
            bool IsMerged(string branch) => commits.TryGetValue(branch, out var tip)
                && merged.TryGetValue(branch, out var mergedTip) && tip == mergedTip;
            // Check every pending identity before branch deduplication and the expensive cap.
            foreach (var pending in inventory.PendingLands.Where(p => pendingLand.Contains(p.Task.Id)))
            {
                var row = pending.Task;
                var repo = row.RepoPath is { } original && Directory.Exists(original) ? original : row.WorktreePath;
                if (repo is null || !Directory.Exists(repo))
                    return Result(CardWorktreeBaseDecision.WaitForLand, reason: "pending_land_unknown");
                if (!string.Equals(common, await inspection.CommonAsync(repo), PathComparison))
                {
                    pendingLand.Remove(row.Id);
                    continue;
                }
                if (!commits.TryGetValue(row.WorktreeBranch!, out var pendingSha))
                    return Result(CardWorktreeBaseDecision.WaitForLand, row.Id, row.WorktreeBranch, reason: "pending_land_unknown");
                if (!IsMerged(row.WorktreeBranch!)
                    && await inspection.ContainsAsync(pendingSha, target) != DelegationWorktreeService.CommitContainment.Contained)
                    return Result(CardWorktreeBaseDecision.WaitForLand, row.Id, row.WorktreeBranch, pendingSha, "pending_land");
                pendingLand.Remove(row.Id);
            }
            inspectable = inspectable.Where(t =>
                task.RequestedWorktreeBaseMode == RequestedWorktreeBaseMode.Task && t.Id == task.RequestedWorktreeBaseTaskId
                || commits.ContainsKey(t.WorktreeBranch!) && !IsMerged(t.WorktreeBranch!)).ToArray();
            totalCandidates = inspectable.Length;
            if (totalCandidates > inspection.MaxCandidates)
                throw new WorktreeBaseInspectionLimitException("candidate_limit");
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
                catch (Exception ex) when (ex is not OperationCanceledException and not WorktreeBaseInspectionLimitException)
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
                    : IsMerged(branch)
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
                if (row.WorktreePath is not null && await inspection.ReadAsync(token => db.AgentTasks.AsNoTracking()
                    .AnyAsync(w => w.Id != task.Id && w.Id != row.Id
                        && (w.Status == AgentTaskStatus.Queued || w.Status == AgentTaskStatus.Dispatched
                            || w.Status == AgentTaskStatus.Working || w.Status == AgentTaskStatus.Blocked)
                        && (w.WorktreePath == row.WorktreePath || w.WorkingDirectory == row.WorktreePath), token)))
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
                    catch (Exception ex) when (ex is not OperationCanceledException and not WorktreeBaseInspectionLimitException)
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
                    catch (Exception ex) when (ex is not OperationCanceledException and not WorktreeBaseInspectionLimitException)
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
        catch (WorktreeBaseInspectionLimitException ex)
        {
            warnings.Add($"Same-card branch inspection incomplete: {ex.Reason} ({totalCandidates} total, "
                + $"{inspected} inspected, {Math.Max(0, totalCandidates - inspected)} omitted). Retry or select a base explicitly.");
            if (HasPending())
                return Result(CardWorktreeBaseDecision.WaitForLand, reason: ex.Reason);
            return Result(task.RequestedWorktreeBaseMode == RequestedWorktreeBaseMode.Target
                ? CardWorktreeBaseDecision.Target : CardWorktreeBaseDecision.Unknown,
                reason: ex.Reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Same-card branch inspection unknown ({ex.GetType().Name}); retry or select a base explicitly.");
            if (HasPending())
                return Result(CardWorktreeBaseDecision.WaitForLand, reason: "pending_land_unknown");
            return Result(task.RequestedWorktreeBaseMode == RequestedWorktreeBaseMode.Target
                ? CardWorktreeBaseDecision.Target : CardWorktreeBaseDecision.Unknown,
                reason: "git_inspection_error");
        }
    }

    private static bool SameDestination(string? left, string? right) =>
        string.Equals(left ?? "master", right ?? "master", StringComparison.Ordinal);

    private static string Short(Guid id) => id.ToString("N")[..8];

}
