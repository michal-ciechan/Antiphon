using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public enum WorktreeBaseDecisionKind
{
    Target,
    Continue,
    WaitForLand,
    Ambiguous,
    Incomplete,
    Invalid,
}

public sealed record WorktreeBaseResolution(
    WorktreeBaseDecisionKind Decision,
    string? StartSha,
    Guid? SourceTaskId,
    string? SourceBranch,
    string? Reason,
    WorktreeBasePreviewDto Preview,
    bool HoldForLand,
    bool Block,
    string? BlockDetail);

/// <summary>CARD-0442: pick the committed tip a new Worktree task should start from.</summary>
public sealed class AgentTaskWorktreeBaseResolver
{
    public const string AmbiguousCode = "worktree_base_ambiguous";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IGitService _git;
    private readonly GitSettings _settings;
    private readonly TimeProvider _clock;

    public AgentTaskWorktreeBaseResolver(
        AppDbContext db,
        IGitService git,
        IOptions<GitSettings> settings,
        TimeProvider clock)
    {
        _db = db;
        _git = git;
        _settings = settings.Value;
        _clock = clock;
    }

    public static string EffectiveDestination(string? mergeTargetRef)
    {
        var value = string.IsNullOrWhiteSpace(mergeTargetRef) ? "master" : mergeTargetRef.Trim();
        const string heads = "refs/heads/";
        if (value.StartsWith(heads, StringComparison.Ordinal))
            value = value[heads.Length..];
        return value;
    }

    public static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        try
        {
            var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
            var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
            return string.Equals(a, b,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static WorktreeBasePreviewDto? TryReadPreview(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<WorktreeBasePreviewDto>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    public static string SerializePreview(WorktreeBasePreviewDto preview) =>
        JsonSerializer.Serialize(preview, JsonOptions);

    public async Task<WorktreeBaseResolution> ResolveAsync(AgentTask task, CancellationToken ct)
    {
        var started = _clock.GetUtcNow();
        var session = new WorktreeBaseGitSession(_settings, _clock, started);
        var warnings = new List<string>();
        var candidates = new List<WorktreeBaseCandidateDto>();
        try
        {
            return await ResolveCoreAsync(task, session, warnings, candidates, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Incomplete(task, session, warnings, candidates, "inspection_timeout");
        }
        catch (WorktreeBaseBudgetExceededException ex)
        {
            return Incomplete(task, session, warnings, candidates, ex.Reason);
        }
    }

    public WorktreeContainment ObserveContainment(
        string repo, string sourceSha, string baseSpec, WorktreeBaseGitSession session, CancellationToken ct) =>
        ObserveContainmentAsync(repo, sourceSha, baseSpec, session, ct).GetAwaiter().GetResult();

    public async Task<WorktreeContainment> ObserveContainmentAsync(
        string repo, string sourceSha, string baseSpec, WorktreeBaseGitSession session, CancellationToken ct)
    {
        var baseSha = await RevParseAsync(repo, baseSpec + "^{commit}", session, ct);
        if (baseSha is null) return WorktreeContainment.Unknown;
        var ancestor = await IsAncestorAsync(repo, sourceSha, baseSha, session, ct);
        if (ancestor is true) return WorktreeContainment.Contained;
        if (ancestor is null) return WorktreeContainment.Unknown;
        var merge = await RangeContainsMergeAsync(repo, baseSha, sourceSha, session, ct);
        if (merge is null) return WorktreeContainment.Unknown;
        if (merge is true) return WorktreeContainment.UnknownMerge;
        var cherry = await CherryHasPlusAsync(repo, baseSha, sourceSha, session, ct);
        if (cherry is null) return WorktreeContainment.Unknown;
        return cherry is false ? WorktreeContainment.Contained : WorktreeContainment.NotContained;
    }

    private async Task<WorktreeBaseResolution> ResolveCoreAsync(
        AgentTask task,
        WorktreeBaseGitSession session,
        List<string> warnings,
        List<WorktreeBaseCandidateDto> candidates,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var mode = task.WorktreeBaseMode;
        if (mode == AgentTaskWorktreeBaseMode.Auto && task.CardId is null)
            return Target(task, session, warnings, candidates, "no_card_auto", task.MergeTargetRef ?? "HEAD");

        if (mode == AgentTaskWorktreeBaseMode.Task && task.CardId is null)
            return Invalid(task, session, warnings, candidates, "task_without_card");

        if (task.RepoPath is null || !Directory.Exists(task.RepoPath))
            return Target(task, session, warnings, candidates, "missing_repository", task.MergeTargetRef ?? "HEAD");

        var repoCommon = await CommonDirAsync(task.RepoPath, session, ct);
        if (repoCommon is null)
            return Incomplete(task, session, warnings, candidates, "git_error");

        var fallback = task.MergeTargetRef ?? "HEAD";
        var dest = EffectiveDestination(task.MergeTargetRef);
        var siblings = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Id != task.Id
                && t.CardId == task.CardId
                && t.Workspace == WorkspaceMode.Worktree
                && t.WorktreeBranch != null)
            .Select(t => new SiblingRow(
                t.Id, t.WorktreeBranch!, t.RepoPath, t.WorktreePath, t.WorkingDirectory,
                t.Status, t.MergeTargetRef, t.LandRequestedAt, t.CompletedAt, t.CardId))
            .ToListAsync(ct);

        var completed = await LoadCompletedLandAsync(siblings.Select(s => s.Id).ToList(), ct);
        var openWriters = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Id != task.Id
                && (t.Status == AgentTaskStatus.Queued
                    || t.Status == AgentTaskStatus.Dispatched
                    || t.Status == AgentTaskStatus.Working))
            .Where(AgentTaskRoles.NotSpecialist)
            .Select(t => new OpenWriter(t.Id, t.Workspace, t.WorktreePath, t.WorkingDirectory, t.FollowUpOfTaskId))
            .ToListAsync(ct);

        var pendingLand = new List<SiblingRow>();
        var inspectable = new List<SiblingRow>();
        foreach (var sibling in siblings)
        {
            if (completed.Contains(sibling.Id))
                continue;
            if (!await SameRepoAsync(repoCommon, sibling, session, ct))
                continue;
            inspectable.Add(sibling);
            if (sibling.LandRequestedAt is not null)
                pendingLand.Add(sibling);
        }

        if (pendingLand.Count > 0)
        {
            foreach (var hold in pendingLand)
            {
                var contained = false;
                var sha = await RevParseAsync(task.RepoPath, hold.Branch + "^{commit}", session, ct);
                if (sha is not null)
                {
                    var observation = await ObserveContainmentAsync(task.RepoPath, sha, fallback, session, ct);
                    contained = observation == WorktreeContainment.Contained;
                }
                if (!contained)
                {
                    return Wait(task, session, warnings, candidates, hold,
                        $"waiting for task {DelegationReportFormatter.Short(hold.Id)} land");
                }
            }
        }

        if (mode == AgentTaskWorktreeBaseMode.Task)
        {
            if (task.RequestedWorktreeBaseTaskId is not Guid requestedId)
                return Invalid(task, session, warnings, candidates, "missing_base_task");
            var source = siblings.FirstOrDefault(s => s.Id == requestedId)
                ?? await LoadRequestedAsync(requestedId, ct);
            if (source is null)
                return Invalid(task, session, warnings, candidates, "source_not_found");
            if (source.CardId != task.CardId)
                return Invalid(task, session, warnings, candidates, "cross_card");
            if (!await SameRepoAsync(repoCommon, source, session, ct))
                return Invalid(task, session, warnings, candidates, "cross_repository");
            if (EffectiveDestination(source.MergeTargetRef) != dest)
                return Invalid(task, session, warnings, candidates, "destination_mismatch");
            if (IsActiveWriter(source, openWriters))
                return Invalid(task, session, warnings, candidates, "active_writer");
            var checkout = source.WorktreePath is { } path && Directory.Exists(path)
                ? await InspectCheckoutAsync(path, session, ct)
                : null;
            if (checkout is { IsUnsafe: true })
                return Invalid(task, session, warnings, candidates, checkout.Reason ?? "unsafe_checkout");
            var sha = await RevParseAsync(task.RepoPath, source.Branch + "^{commit}", session, ct);
            if (sha is null)
                return Invalid(task, session, warnings, candidates, "missing_commit");
            CollectOmissions(inspectable, source.Id, sha, warnings, candidates);
            return Continue(task, session, warnings, candidates, source, sha, "explicit_task");
        }

        var eligible = new List<(SiblingRow Row, string Sha)>();
        var uncertain = new List<WorktreeBaseCandidateDto>();
        var remaining = inspectable
            .OrderBy(s => s.Id)
            .ToList();
        if (remaining.Count > session.MaxCandidates)
        {
            var omitted = remaining.Count - session.MaxCandidates;
            remaining = remaining.Take(session.MaxCandidates).ToList();
            warnings.Add($"incomplete inspection: candidate_limit ({omitted} omitted)");
            return Incomplete(task, session, warnings, candidates, "candidate_limit", remaining.Count, omitted);
        }

        foreach (var sibling in remaining)
        {
            ct.ThrowIfCancellationRequested();
            if (EffectiveDestination(sibling.MergeTargetRef) != dest)
            {
                warnings.Add($"{DelegationReportFormatter.Short(sibling.Id)} omitted: destination mismatch");
                candidates.Add(new(sibling.Id, sibling.Branch, null, "destination_mismatch"));
                continue;
            }

            if (sibling.Status != AgentTaskStatus.Succeeded)
            {
                warnings.Add($"{DelegationReportFormatter.Short(sibling.Id)} omitted: {sibling.Status}");
                candidates.Add(new(sibling.Id, sibling.Branch, null, sibling.Status.ToString()));
                continue;
            }

            if (IsActiveWriter(sibling, openWriters))
            {
                warnings.Add($"{DelegationReportFormatter.Short(sibling.Id)} omitted: active writer");
                candidates.Add(new(sibling.Id, sibling.Branch, null, "active_writer"));
                continue;
            }

            var checkout = sibling.WorktreePath is { } wt && Directory.Exists(wt)
                ? await InspectCheckoutAsync(wt, session, ct)
                : null;
            if (checkout is { IsUnsafe: true })
            {
                warnings.Add($"{DelegationReportFormatter.Short(sibling.Id)} omitted: {checkout.Reason}");
                candidates.Add(new(sibling.Id, sibling.Branch, null, checkout.Reason));
                continue;
            }

            var sha = await RevParseAsync(task.RepoPath, sibling.Branch + "^{commit}", session, ct);
            if (sha is null)
            {
                warnings.Add($"{DelegationReportFormatter.Short(sibling.Id)} omitted: missing local branch");
                candidates.Add(new(sibling.Id, sibling.Branch, null, "missing_local_branch"));
                continue;
            }

            var containment = await ObserveContainmentAsync(task.RepoPath, sha, fallback, session, ct);
            if (containment == WorktreeContainment.Contained)
            {
                candidates.Add(new(sibling.Id, sibling.Branch, sha, "contained"));
                continue;
            }

            if (containment == WorktreeContainment.UnknownMerge)
            {
                uncertain.Add(new(sibling.Id, sibling.Branch, sha, "merge_range"));
                warnings.Add($"{DelegationReportFormatter.Short(sibling.Id)} uncertain: merge_range");
                candidates.Add(new(sibling.Id, sibling.Branch, sha, "merge_range"));
                continue;
            }

            if (containment == WorktreeContainment.Unknown)
            {
                warnings.Add($"{DelegationReportFormatter.Short(sibling.Id)} omitted: inspection unknown");
                candidates.Add(new(sibling.Id, sibling.Branch, sha, "unknown"));
                continue;
            }

            eligible.Add((sibling, sha));
            candidates.Add(new(sibling.Id, sibling.Branch, sha, "eligible"));
        }

        if (mode == AgentTaskWorktreeBaseMode.Target)
        {
            foreach (var row in eligible)
                warnings.Add($"{DelegationReportFormatter.Short(row.Row.Id)} omitted: fresh worktree");
            return Target(task, session, warnings, candidates, "fresh_target", fallback);
        }

        var maxima = await MaximalTipsAsync(task.RepoPath, eligible, session, ct);
        if (maxima.Count >= 2)
        {
            var ids = maxima.Select(m => m.Row.Id).ToList();
            return Ambiguous(task, session, warnings, candidates, maxima, fallback);
        }

        if (maxima.Count == 1)
        {
            var chosen = maxima[0];
            if (uncertain.Count > 0)
                warnings.Add("uncertain merge ranges excluded from maxima");
            return Continue(task, session, warnings, candidates, chosen.Row, chosen.Sha, "auto");
        }

        var reason = uncertain.Count > 0 ? "uncertain_only" : "no_eligible_source";
        return Target(task, session, warnings, candidates, reason, fallback);
    }

    private async Task<List<(SiblingRow Row, string Sha)>> MaximalTipsAsync(
        string repo,
        List<(SiblingRow Row, string Sha)> eligible,
        WorktreeBaseGitSession session,
        CancellationToken ct)
    {
        var groups = eligible
            .GroupBy(e => e.Sha, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(x => x.Row.CompletedAt ?? DateTime.MinValue)
                .ThenBy(x => x.Row.Id.ToString("D"), StringComparer.Ordinal)
                .First())
            .ToList();

        var maxima = new List<(SiblingRow Row, string Sha)>();
        foreach (var candidate in groups)
        {
            var dominated = false;
            foreach (var other in groups)
            {
                if (other.Sha == candidate.Sha) continue;
                var ancestor = await IsAncestorAsync(repo, candidate.Sha, other.Sha, session, ct);
                if (ancestor is true) { dominated = true; break; }
            }
            if (!dominated)
                maxima.Add(candidate);
        }

        return maxima;
    }

    private static bool IsActiveWriter(SiblingRow source, List<OpenWriter> open)
    {
        if (source.Status is AgentTaskStatus.Queued or AgentTaskStatus.Dispatched or AgentTaskStatus.Working)
            return true;
        foreach (var row in open)
        {
            if (row.FollowUpOfTaskId == source.Id)
                return true;
            if (row.WorktreePath is not null && SamePath(row.WorktreePath, source.WorktreePath))
                return true;
            if (row.Workspace == WorkspaceMode.Shared
                && (SamePath(row.WorkingDirectory, source.WorktreePath)
                    || SamePath(row.WorkingDirectory, source.WorkingDirectory)))
                return true;
        }

        return false;
    }

    private void CollectOmissions(
        List<SiblingRow> inspectable, Guid chosen, string chosenSha, List<string> warnings, List<WorktreeBaseCandidateDto> candidates)
    {
        foreach (var sibling in inspectable)
        {
            if (sibling.Id == chosen) continue;
            if (candidates.Any(c => c.TaskId == sibling.Id)) continue;
            warnings.Add($"{DelegationReportFormatter.Short(sibling.Id)} omitted");
            candidates.Add(new(sibling.Id, sibling.Branch, null, "omitted"));
        }
    }

    private async Task<HashSet<Guid>> LoadCompletedLandAsync(List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var landed = await _db.AgentTaskEvents.AsNoTracking()
            .Where(e => ids.Contains(e.AgentTaskId)
                && (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.LandedWithResidue))
            .Select(e => e.AgentTaskId)
            .Distinct()
            .ToListAsync(ct);
        return landed.ToHashSet();
    }

    private async Task<SiblingRow?> LoadRequestedAsync(Guid id, CancellationToken ct)
    {
        var row = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new { t.Id, t.WorktreeBranch, t.RepoPath, t.WorktreePath, t.WorkingDirectory,
                t.Status, t.MergeTargetRef, t.LandRequestedAt, t.CompletedAt, t.CardId, t.Workspace })
            .FirstOrDefaultAsync(ct);
        if (row is null || row.WorktreeBranch is null || row.Workspace != WorkspaceMode.Worktree)
            return null;
        return new SiblingRow(row.Id, row.WorktreeBranch, row.RepoPath, row.WorktreePath, row.WorkingDirectory,
            row.Status, row.MergeTargetRef, row.LandRequestedAt, row.CompletedAt, row.CardId);
    }

    private async Task<bool> SameRepoAsync(string repoCommon, SiblingRow sibling, WorktreeBaseGitSession session, CancellationToken ct)
    {
        foreach (var path in new[] { sibling.RepoPath, sibling.WorktreePath, sibling.WorkingDirectory })
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) continue;
            var common = await CommonDirAsync(path, session, ct);
            if (common is not null && SamePath(common, repoCommon))
                return true;
        }
        return false;
    }

    private async Task<string?> CommonDirAsync(string path, WorktreeBaseGitSession session, CancellationToken ct)
    {
        var result = await _git.RunWorktreeBaseGitAsync(path, ["rev-parse", "--absolute-git-common-dir"], session, ct);
        if (result.ExitCode != 0) return null;
        var dir = result.Stdout.Trim();
        return string.IsNullOrWhiteSpace(dir) ? null : Path.GetFullPath(dir);
    }

    private async Task<string?> RevParseAsync(string repo, string spec, WorktreeBaseGitSession session, CancellationToken ct)
    {
        var result = await _git.RunWorktreeBaseGitAsync(repo, ["rev-parse", "--verify", spec], session, ct);
        if (result.ExitCode != 0) return null;
        var sha = result.Stdout.Trim();
        return GitObjectId.IsFull(sha) ? sha : null;
    }

    private async Task<bool?> IsAncestorAsync(string repo, string ancestor, string descendant, WorktreeBaseGitSession session, CancellationToken ct)
    {
        var result = await _git.RunWorktreeBaseGitAsync(repo,
            ["merge-base", "--is-ancestor", ancestor, descendant], session, ct);
        return result.ExitCode switch { 0 => true, 1 => false, _ => null };
    }

    private async Task<bool?> RangeContainsMergeAsync(string repo, string from, string to, WorktreeBaseGitSession session, CancellationToken ct)
    {
        var result = await _git.RunWorktreeBaseGitAsync(repo,
            ["rev-list", "--min-parents=2", $"{from}..{to}"], session, ct);
        if (result.ExitCode != 0) return null;
        return result.Stdout.Trim().Length > 0;
    }

    private async Task<bool?> CherryHasPlusAsync(string repo, string upstream, string head, WorktreeBaseGitSession session, CancellationToken ct)
    {
        var result = await _git.RunWorktreeBaseGitAsync(repo, ["cherry", upstream, head], session, ct);
        if (result.ExitCode != 0) return null;
        foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("+", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private async Task<CheckoutInspection?> InspectCheckoutAsync(string path, WorktreeBaseGitSession session, CancellationToken ct)
    {
        var status = await _git.RunWorktreeBaseGitAsync(path,
            ["status", "--porcelain=v1", "-z", "--untracked-files=normal"], session, ct);
        if (status.ExitCode != 0)
            return new CheckoutInspection(true, "git_error");
        if (status.Stdout.Length > 0)
        {
            if (status.Stdout.Contains("??", StringComparison.Ordinal))
                return new CheckoutInspection(true, "untracked");
            var staged = status.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.Length >= 2 && line[0] is not ' ' and not '?');
            if (staged) return new CheckoutInspection(true, "staged");
            return new CheckoutInspection(true, "tracked_unstaged");
        }

        var merge = await _git.RunWorktreeBaseGitAsync(path, ["rev-parse", "--verify", "MERGE_HEAD"], session, ct);
        if (merge.ExitCode == 0) return new CheckoutInspection(true, "merge_in_progress");
        var rebaseMerge = await _git.RunWorktreeBaseGitAsync(path, ["rev-parse", "--git-path", "rebase-merge"], session, ct);
        if (rebaseMerge.ExitCode == 0 && File.Exists(Path.Combine(path, rebaseMerge.Stdout.Trim())))
            return new CheckoutInspection(true, "rebase_in_progress");
        var rebaseApply = await _git.RunWorktreeBaseGitAsync(path, ["rev-parse", "--git-path", "rebase-apply"], session, ct);
        if (rebaseApply.ExitCode == 0 && File.Exists(Path.Combine(path, rebaseApply.Stdout.Trim())))
            return new CheckoutInspection(true, "rebase_in_progress");
        return new CheckoutInspection(false, null);
    }

    private WorktreeBaseResolution Target(
        AgentTask task, WorktreeBaseGitSession session, List<string> warnings, List<WorktreeBaseCandidateDto> candidates,
        string reason, string fallback) =>
        Finish(WorktreeBaseDecisionKind.Target, null, null, null, reason, session, warnings, candidates, fallback, false, false, null);

    private WorktreeBaseResolution Continue(
        AgentTask task, WorktreeBaseGitSession session, List<string> warnings, List<WorktreeBaseCandidateDto> candidates,
        SiblingRow source, string sha, string reason) =>
        Finish(WorktreeBaseDecisionKind.Continue, sha, source.Id, source.Branch, reason, session, warnings, candidates,
            task.MergeTargetRef ?? "HEAD", false, false, null);

    private WorktreeBaseResolution Wait(
        AgentTask task, WorktreeBaseGitSession session, List<string> warnings, List<WorktreeBaseCandidateDto> candidates,
        SiblingRow hold, string reason) =>
        Finish(WorktreeBaseDecisionKind.WaitForLand, null, hold.Id, hold.Branch, reason, session, warnings, candidates,
            task.MergeTargetRef ?? "HEAD", true, false, null);

    private WorktreeBaseResolution Ambiguous(
        AgentTask task, WorktreeBaseGitSession session, List<string> warnings, List<WorktreeBaseCandidateDto> candidates,
        List<(SiblingRow Row, string Sha)> maxima, string fallback)
    {
        var names = string.Join(", ", maxima.Select(m =>
            $"{DelegationReportFormatter.Short(m.Row.Id)} {m.Row.Branch} @{m.Sha}"));
        var detail = $"worktree_base_ambiguous: competing tips {names}. Use -BaseTask or -FreshWorktree.";
        return Finish(WorktreeBaseDecisionKind.Ambiguous, null, null, null, AmbiguousCode, session, warnings, candidates,
            fallback, false, true, detail);
    }

    private WorktreeBaseResolution Invalid(
        AgentTask task, WorktreeBaseGitSession session, List<string> warnings, List<WorktreeBaseCandidateDto> candidates,
        string reason) =>
        Finish(WorktreeBaseDecisionKind.Invalid, null, null, null, reason, session, warnings, candidates,
            task.MergeTargetRef ?? "HEAD", false, true, reason);

    private WorktreeBaseResolution Incomplete(
        AgentTask task, WorktreeBaseGitSession session, List<string> warnings, List<WorktreeBaseCandidateDto> candidates,
        string reason, int inspected = 0, int omitted = 0)
    {
        warnings.Add($"incomplete inspection: {reason}");
        if (task.WorktreeBaseMode == AgentTaskWorktreeBaseMode.Task)
            return Invalid(task, session, warnings, candidates, reason);
        return Finish(WorktreeBaseDecisionKind.Incomplete, null, null, null, reason, session, warnings, candidates,
            task.MergeTargetRef ?? "HEAD", false, false, null, inspected, omitted);
    }

    private WorktreeBaseResolution Finish(
        WorktreeBaseDecisionKind decision,
        string? sha,
        Guid? sourceId,
        string? sourceBranch,
        string reason,
        WorktreeBaseGitSession session,
        List<string> warnings,
        List<WorktreeBaseCandidateDto> candidates,
        string fallback,
        bool hold,
        bool block,
        string? blockDetail,
        int inspected = 0,
        int omitted = 0)
    {
        var capped = candidates.Take(session.MaxCandidates).ToList();
        var preview = new WorktreeBasePreviewDto(
            Decision: decision.ToString(),
            FallbackRef: fallback,
            SourceTaskId: sourceId,
            SourceBranch: sourceBranch,
            SourceSha: sha,
            Reason: reason,
            Candidates: capped,
            Warnings: warnings,
            ObservedAt: session.StartedAt.UtcDateTime,
            CandidateTotal: candidates.Count + omitted,
            CandidateInspected: inspected == 0 ? capped.Count : inspected,
            CandidateOmitted: omitted,
            CommandCount: session.Starts,
            ElapsedSeconds: session.ElapsedSeconds);
        return new WorktreeBaseResolution(decision, sha, sourceId, sourceBranch, reason, preview, hold, block, blockDetail);
    }

    public enum WorktreeContainment { Contained, NotContained, Unknown, UnknownMerge }

    private sealed record CheckoutInspection(bool IsUnsafe, string? Reason);

    private sealed record OpenWriter(
        Guid Id, WorkspaceMode Workspace, string? WorktreePath, string? WorkingDirectory, Guid? FollowUpOfTaskId);

    private sealed class SiblingRow(
        Guid id, string branch, string? repoPath, string? worktreePath, string? workingDirectory,
        AgentTaskStatus status, string? mergeTargetRef, DateTime? landRequestedAt, DateTime? completedAt,
        Guid? cardId = null)
    {
        public Guid Id { get; } = id;
        public string Branch { get; } = branch;
        public string? RepoPath { get; } = repoPath;
        public string? WorktreePath { get; } = worktreePath;
        public string? WorkingDirectory { get; } = workingDirectory;
        public AgentTaskStatus Status { get; } = status;
        public string? MergeTargetRef { get; } = mergeTargetRef;
        public DateTime? LandRequestedAt { get; } = landRequestedAt;
        public DateTime? CompletedAt { get; } = completedAt;
        public Guid? CardId { get; set; } = cardId;
    }
}
