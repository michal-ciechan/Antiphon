using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed record UnlandedSibling(
    Guid TaskId,
    string Branch,
    string BaseRef,
    string CardIdentifier,
    string Tip,
    int CommitsAbove,
    string Subject,
    DateTime CreatedAt,
    string? FullTipSha,
    LandRequestState? RequestState = null,
    int CoveredCount = 0)
{
    public string ShortId => DelegationReportFormatter.Short(TaskId);

    public string HoldDetail =>
        $"held: {CardIdentifier}'s kept branch {Branch} (task {ShortId}) is landing and is not yet in {BaseRef}";

    public string WarningDetail(Guid newTaskId)
    {
        var commits = CommitsAbove == 1
            ? $"1 commit: '{Subject}'"
            : $"{CommitsAbove} commits: '{Subject}'";
        return $"task {DelegationReportFormatter.Short(newTaskId)} branched from {BaseRef} without "
            + $"{CardIdentifier}'s kept branch {Branch} ({FullTipSha ?? Tip}, {commits}). "
            + $"Land {ShortId} first, or expect its commits to be absent from this branch."
            + (RequestState == LandRequestState.NeedsResolution
                ? $" Land request {ShortId} needs conflict resolution."
                : "")
            + (CoveredCount == 0 ? "" : $" At the observed tips, this warning also covers {CoveredCount} other kept sibling branches.");
    }
}

public sealed record SiblingBaseGuard(
    UnlandedSibling? Hold,
    IReadOnlyList<UnlandedSibling> Warnings,
    string? ObservedBaseRef,
    string? Reason = null, int RawRows = 0, int ExpensiveCandidates = 0,
    int InspectedCandidates = 0, int OmittedCandidates = 0, int GitCommands = 0,
    IReadOnlyDictionary<string, string>? FrozenRefs = null, string? Metadata = null,
    bool PendingKnown = true)
{
    public static SiblingBaseGuard Proceed { get; } = new(null, [], null);
}


/// <summary>Read-only legacy warnings and pending-land guard, using frozen refs and a single budget.</summary>
public sealed class CardSiblingInspectionService(AppDbContext db, ILandingGit git,
    IOptions<GitSettings> settings, TimeProvider clock, GitProcessGate gate)
{
    public WorktreeBaseInspection Begin(string repository, CancellationToken ct) =>
        new(git, repository, settings.Value, clock, gate, ct);

    public async Task<string> ConfiguredDefaultAsync(AgentTask task, WorktreeBaseInspection inspection, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var projectDefault = task.ProjectId is Guid projectId
            ? await inspection.ReadAsync(token => db.Projects.AsNoTracking().Where(p => p.Id == projectId)
                .Select(p => p.BaseBranch).FirstOrDefaultAsync(token)) : null;
        return WorktreeBaseResolver.ChooseConfiguredDefault(projectDefault, settings.Value.DefaultBranch);
    }

    public static string MetadataOf(CardSiblingInventory.Snapshot inventory) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            rows = inventory.Rows.Select(t => new { t.Id, t.Status, t.CardId, t.RepoPath, t.WorktreePath,
                t.WorktreeBranch, t.CurrentLandRequestId, t.CreatedAt, t.MergeTargetRef }),
            pending = inventory.PendingLands.Select(p => new { TaskId = p.Task.Id, RequestId = p.Request.Id, p.Request.State,
                p.Request.IsPending, p.Request.ExpectedSourceSha, p.Request.LandingOperationId,
                p.OriginalSourceSha }),
            requests = inventory.Requests.Values.OrderBy(r => r.Id).Select(r => new
                { r.Id, r.State, r.IsPending, r.ExpectedSourceSha, r.LandingOperationId }),
            inventory.Overflow, inventory.PendingOverflow,
        }))));

    public async Task<SiblingBaseGuard> InspectAsync(AgentTask task, CancellationToken ct)
    {
        using var inspection = Begin(task.RepoPath ?? "", ct);
        return await InspectAsync(task, inspection, ct);
    }

    public async Task<SiblingBaseGuard> InspectAsync(AgentTask task, WorktreeBaseInspection inspection, CancellationToken ct)
    {
        if (task.RepoPath is null || !Directory.Exists(task.RepoPath) || task.CardId is null)
            return SiblingBaseGuard.Proceed;
        var warnings = new List<UnlandedSibling>();
        var frozen = new Dictionary<string, string>(StringComparer.Ordinal);
        var raw = 0;
        var expensive = 0;
        var inspected = 0;
        var omitted = 0;
        var pendingKnown = false;
        AgentTask[] candidateRows = [];
        var unresolvedPending = new List<CardSiblingInventory.Pending>();
        string? metadata = null;
        var baseRef = task.WorktreeBaseRequestedRef ?? task.MergeTargetRef ?? settings.Value.DefaultBranch;
        var identifier = "the card";
        UnlandedSibling Describe(AgentTask row, string? sha, LandRequestState? state = null) =>
            new(row.Id, row.WorktreeBranch!, baseRef, identifier, sha is null ? "unknown" : sha[..8],
                0, "detail unavailable", row.CreatedAt, sha, state);
        SiblingBaseGuard Result(UnlandedSibling? hold = null, string? reason = null) =>
            new(hold, hold is null ? warnings.ToArray() : [], baseRef, reason, raw, expensive,
                inspected, omitted, inspection.Commands, new Dictionary<string, string>(frozen), metadata, pendingKnown);
        try
        {
            var inventory = await new CardSiblingInventory(db).ReadAsync(task, true, inspection, ct);
            metadata = MetadataOf(inventory);
            raw = inventory.Rows.Count;
            unresolvedPending = inventory.PendingLands.Where(p => SameRepo(task, p.Task)).ToList();
            pendingKnown = true;
            identifier = await inspection.ReadAsync(token => db.Cards.AsNoTracking()
                .Where(c => c.Id == task.CardId).Select(c => c.Identifier).FirstOrDefaultAsync(token)) ?? identifier;
            if (inventory.Overflow || inventory.PendingOverflow)
            {
                omitted = Math.Max(1, raw - inspection.MaxCandidates);
                throw new WorktreeBaseInspectionLimitException("candidate_limit");
            }
            var rows = inventory.Rows.Where(row => SameRepo(task, row)).ToArray();
            candidateRows = rows;
            if (rows.Length == 0) return Result();
            var configured = await ConfiguredDefaultAsync(task, inspection, ct);
            var defaultSha = await inspection.CommitAsync(configured);
            var landingRef = task.MergeTargetRef ?? (defaultSha is not null ? configured : "HEAD");
            var landingSha = await inspection.CommitAsync(landingRef);
            if (landingSha is null) throw new IOException("git_target_missing");
            frozen[landingRef] = landingSha;
            var resolved = WorktreeBaseResolver.Resolve(null, task.WorktreeBaseRequestedRef,
                task.MergeTargetRef, new DefaultBranchProbe(configured, defaultSha is not null));
            baseRef = resolved.Ref;
            var target = await inspection.CommitAsync(baseRef);
            if (target is null) throw new IOException("git_target_missing");
            frozen[baseRef] = target;
            var branches = rows.Select(t => t.WorktreeBranch!).Distinct(StringComparer.Ordinal).ToArray();
            var commits = await inspection.BranchCommitsAsync(branches);
            var merged = await inspection.MergedBranchesAsync(target, branches);
            bool IsMerged(string branch) => commits.TryGetValue(branch, out var sha)
                && merged.TryGetValue(branch, out var mergedSha) && sha == mergedSha;
            var common = await inspection.CommonAsync(task.RepoPath);
            // Every request identity is visited before grouping refs or reducing warnings.
            foreach (var pending in unresolvedPending.ToArray())
            {
                var reviewed = pending.Request.ExpectedSourceSha ?? pending.OriginalSourceSha;
                var row = pending.Task;
                if (task.WorktreeBaseRequestedRef is not null && reviewed is not null
                    && (target == reviewed || await inspection.AncestorAsync(reviewed, target)))
                {
                    // The reviewed immutable source, rather than a later rewritten branch, grants this exception.
                    frozen[reviewed] = reviewed;
                    unresolvedPending.Remove(pending);
                    continue;
                }
                if (!commits.TryGetValue(row.WorktreeBranch!, out var sha))
                    return Result(Describe(row, null, pending.Request.State), "pending_land_unknown");
                if (IsMerged(row.WorktreeBranch!)
                    || await inspection.ContainsAsync(sha, target) == DelegationWorktreeService.CommitContainment.Contained)
                {
                    frozen["refs/heads/" + row.WorktreeBranch] = sha;
                    unresolvedPending.Remove(pending);
                    continue;
                }
                return Result(Describe(row, sha, pending.Request.State), "pending_land");
            }
            var candidates = rows.Where(row => commits.ContainsKey(row.WorktreeBranch!)
                && !IsMerged(row.WorktreeBranch!)).ToArray();
            expensive = candidates.Select(t => commits[t.WorktreeBranch!]).Distinct(StringComparer.Ordinal).Count();
            // Warning output can be incomplete; it cannot silently confer a source-selection verdict.
            var admittedTips = candidates.Select(t => commits[t.WorktreeBranch!]).Distinct(StringComparer.Ordinal)
                .Take(inspection.MaxCandidates).ToHashSet(StringComparer.Ordinal);
            omitted = candidates.Count(t => !admittedTips.Contains(commits[t.WorktreeBranch!]));
            if (expensive > inspection.MaxCandidates)
            {
                warnings.AddRange(candidates.Where(t => admittedTips.Contains(commits[t.WorktreeBranch!]))
                    .Select(row => Describe(row, commits[row.WorktreeBranch!])));
                return Result(reason: "candidate_limit");
            }
            var containment = new Dictionary<string, DelegationWorktreeService.CommitContainment>(StringComparer.Ordinal);
            foreach (var row in candidates.Where(t => admittedTips.Contains(commits[t.WorktreeBranch!])))
            {
                var sha = commits[row.WorktreeBranch!];
                var repo = row.RepoPath is { } original && Directory.Exists(original) ? original : row.WorktreePath;
                if (repo is null || !Directory.Exists(repo)
                    || await inspection.CommonAsync(repo) != common) continue;
                if (!containment.TryGetValue(sha, out var contained))
                {
                    inspected++;
                    contained = containment[sha] = await inspection.ContainsUnmergedAsync(sha, target);
                }
                if (contained == DelegationWorktreeService.CommitContainment.Contained) continue;
                warnings.Add(Describe(row, sha, row.CurrentLandRequestId is Guid requestId
                    ? inventory.Requests.GetValueOrDefault(requestId)?.State : null));
            }
            var tips = warnings.Select(w => w.FullTipSha!).Distinct(StringComparer.Ordinal).ToArray();
            var edges = new List<(string Ancestor, string Descendant)>();
            string? enrichmentReason = null;
            try
            {
                foreach (var ancestor in tips)
                    foreach (var descendant in tips)
                        if (ancestor != descendant && await inspection.AncestorAsync(ancestor, descendant))
                            edges.Add((ancestor, descendant));
            }
            catch (WorktreeBaseInspectionLimitException ex) { enrichmentReason = ex.Reason; }
            var byTask = warnings.ToDictionary(w => w.TaskId);
            var reduced = SiblingWarningReducer.Reduce(warnings.Select(w =>
                new SiblingWarningReducer.Candidate(w.TaskId, w.Branch, w.CreatedAt, w.FullTipSha)), edges);
            warnings = reduced.Select(g => byTask[g.Representative.TaskId] with { CoveredCount = g.Members.Count - 1 }).ToList();
            try
            {
                for (var i = 0; i < warnings.Count; i++)
                {
                    if (inspection.Remaining < TimeSpan.FromMilliseconds(100)) break;
                    var warning = warnings[i];
                    var count = await inspection.RunAsync(task.RepoPath, "rev-list", "--count", $"{target}..{warning.FullTipSha}");
                    var subject = await inspection.RunAsync(task.RepoPath, "log", "-1", "--format=%s", warning.FullTipSha!);
                    if (count.Succeeded && subject.Succeeded && int.TryParse(count.Output.Trim(), out var n))
                        warnings[i] = warning with { CommitsAbove = n, Subject = subject.Output.Trim() };
                }
            }
            catch (WorktreeBaseInspectionLimitException ex) { enrichmentReason ??= ex.Reason; }
            return Result(reason: omitted > 0 ? "candidate_limit" : enrichmentReason);
        }
        catch (WorktreeBaseInspectionLimitException ex)
        {
            return Result(unresolvedPending.FirstOrDefault() is { } pending
                ? Describe(pending.Task, null, pending.Request.State) : null, ex.Reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (warnings.Count == 0)
            {
                warnings.AddRange(candidateRows.Take(inspection.MaxCandidates).Select(row => Describe(row, null)));
                omitted = Math.Max(0, candidateRows.Length - warnings.Count);
            }
            return Result(unresolvedPending.FirstOrDefault() is { } pending
                ? Describe(pending.Task, null, pending.Request.State) : null, "git_inspection_error");
        }
    }

    private static bool SameRepo(AgentTask task, AgentTask sibling) =>
        DelegationWorktreeService.SharesRepo(task.RepoPath, sibling.RepoPath)
        || DelegationWorktreeService.SharesRepo(task.RepoPath, sibling.WorktreePath);
}
