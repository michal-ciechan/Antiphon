using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0499. Task-scoped claim plus Git corroboration for Code/Worktree completion.</summary>
public sealed class TaskCompletionProgressService
{
    private static readonly Regex ClaimLine = new(
        @"^\[antiphon-progress:([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}) commit=([0-9a-f]{40}|[0-9a-f]{64})\]$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly ITaskProgressGit _git;
    private readonly IWorkspaceProgressProbe? _files;

    public TaskCompletionProgressService(ITaskProgressGit git, IWorkspaceProgressProbe? files = null)
    {
        _git = git;
        _files = files;
    }

    public sealed record Evaluation(
        CompletionProgressAssessment Assessment,
        string? Reason,
        string? Claim,
        string? ClaimWarning,
        CompletionProgressEvidence Evidence,
        string? WarningDetail,
        bool PrimaryDirectProgress,
        bool AllowsAutomaticWorkspaceMutation)
    {
        public bool IsProgress => Assessment == CompletionProgressAssessment.ProgressObserved;
        public bool IsNegative => Assessment == CompletionProgressAssessment.NoAttributedProgress;
        public bool IsIndeterminate => Assessment == CompletionProgressAssessment.Indeterminate;
    }

    public async Task<Evaluation> EvaluateAsync(AgentTask task, string body, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var claim = ParseClaim(task.Id, body);
        var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson);
        if (baseline is null)
            return await EvaluateLegacyAsync(task, claim, ct);

        var sources = new List<CompletionProgressSource>();
        var primary = await EvaluateSourceAsync(
            task, baseline.Primary, baseline, claim.Sha, isRepair: false, sources, ct);
        if (baseline.RepairSource is { } repair)
            await EvaluateSourceAsync(task, repair, baseline, claim.Sha, isRepair: true, sources, ct);

        return Aggregate(task, claim, sources);
    }

    public static ProgressClaimParse ParseClaim(Guid taskId, string? text)
    {
        if (string.IsNullOrEmpty(text))
            return new(null, null);

        var seen = new List<string>();
        var foreign = false;
        var malformed = false;
        var inFence = false;
        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var trimmed = raw.TrimEnd();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            var line = trimmed.TrimStart();
            if (inFence)
            {
                if (line.StartsWith("[antiphon-progress:", StringComparison.Ordinal))
                    malformed = true;
                continue;
            }
            if (line.StartsWith('>')) continue;
            if (!line.StartsWith("[antiphon-progress:", StringComparison.Ordinal))
                continue;
            var match = ClaimLine.Match(line);
            if (!match.Success)
            {
                malformed = true;
                continue;
            }
            if (!Guid.TryParse(match.Groups[1].Value, out var named) || named != taskId)
            {
                foreign = true;
                continue;
            }
            seen.Add(match.Groups[2].Value);
        }

        if (seen.Count == 0)
        {
            if (foreign) return new(null, "claim_not_for_this_task");
            if (malformed) return new(null, "claim_malformed_or_quoted");
            return new(null, null);
        }

        var distinct = seen.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count > 1)
            return new(null, "claim_conflicting");
        return new(distinct[0].ToLowerInvariant(), null);
    }

    public static string FailureSentence(AgentTask task, string? namedSources = null)
    {
        var sources = namedSources
            ?? string.Join(" and ", new[] { task.WorktreePath, RepairFullRef(task) }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return "The delegate reported completion but Antiphon observed no attributable post-dispatch progress"
            + (string.IsNullOrWhiteSpace(sources) ? "." : $" at {sources}.");
    }

    public static string? ProgressWarning(CompletionProgressEvidence evidence)
    {
        if (evidence.Assessment == CompletionProgressAssessment.Indeterminate)
            return $"progress=unavailable; reason={evidence.Reason ?? "unknown"}";
        if (evidence.ClaimWarning is not null)
            return $"progress=unavailable; reason={evidence.ClaimWarning}";
        if (evidence.Assessment != CompletionProgressAssessment.ProgressObserved)
            return null;
        var repair = evidence.Sources?.FirstOrDefault(s =>
            s.Assessment == CompletionProgressAssessment.ProgressObserved
            && s.Origin is ProgressOrigin.RepairSource or ProgressOrigin.RepairSourceRemote);
        if (repair is not null)
        {
            var owner = repair.OwnerTaskId is Guid id ? DelegationReportFormatter.Short(id) : "unknown";
            var sha = repair.VerifiedSha ?? repair.ClaimedSha ?? "";
            return $"progress=repair-source; owner={owner}; commit={sha}";
        }
        var remote = evidence.Sources?.FirstOrDefault(s =>
            s.Assessment == CompletionProgressAssessment.ProgressObserved
            && s.Origin == ProgressOrigin.PrimaryRemote);
        if (remote is not null)
            return $"progress=primary-remote; commit={remote.VerifiedSha ?? remote.ClaimedSha}";
        return null;
    }

    public static bool AllowsAutomaticWorkspaceMutation(CompletionProgressEvidence? evidence)
    {
        if (evidence is null) return true;
        if (evidence.Assessment == CompletionProgressAssessment.Indeterminate) return false;
        if (evidence.Assessment != CompletionProgressAssessment.ProgressObserved) return false;
        return evidence.Sources?.Any(s =>
            s.Assessment == CompletionProgressAssessment.ProgressObserved
            && s.Origin == ProgressOrigin.Primary) == true;
    }

    private async Task<Evaluation> EvaluateLegacyAsync(AgentTask task, ProgressClaimParse claim, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.WorktreePath) || !Directory.Exists(task.WorktreePath))
        {
            var evidence = new CompletionProgressEvidence(1, CompletionProgressAssessment.Indeterminate,
                "primary_checkout_missing", claim.Sha, claim.Warning,
                [new(ProgressOrigin.Primary, CompletionProgressAssessment.Indeterminate, Reason: "primary_checkout_missing", Complete: false)]);
            return ToEvaluation(claim, evidence, primaryDirect: false);
        }

        WorkspaceProgressArm arm;
        try
        {
            arm = _files is null
                ? new WorkspaceProgressArm(false, null, null, false)
                : await _files.ProbeProgressAsync(task.WorktreePath, task.DispatchedAt ?? task.CreatedAt, false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failed = new CompletionProgressEvidence(1, CompletionProgressAssessment.Indeterminate,
                "primary_status_unavailable", claim.Sha, claim.Warning,
                [new(ProgressOrigin.Primary, CompletionProgressAssessment.Indeterminate, Reason: "primary_status_unavailable", Complete: false)]);
            return ToEvaluation(claim, failed, primaryDirect: false);
        }

        if (!arm.Available)
        {
            var reason = Directory.Exists(task.WorktreePath) ? "primary_status_unavailable" : "primary_checkout_missing";
            var evidence = new CompletionProgressEvidence(1, CompletionProgressAssessment.Indeterminate,
                reason, claim.Sha, claim.Warning,
                [new(ProgressOrigin.Primary, CompletionProgressAssessment.Indeterminate, Reason: reason, Complete: false)]);
            return ToEvaluation(claim, evidence, primaryDirect: false);
        }

        if (arm.LastCommitAt is not null || arm.LastFileChangeAt is not null)
        {
            var evidence = new CompletionProgressEvidence(1, CompletionProgressAssessment.ProgressObserved,
                null, claim.Sha, claim.Warning,
                [new(ProgressOrigin.Primary, CompletionProgressAssessment.ProgressObserved, RegisteredPath: task.WorktreePath)]);
            return ToEvaluation(claim, evidence, primaryDirect: true);
        }

        var negative = new CompletionProgressEvidence(1, CompletionProgressAssessment.NoAttributedProgress,
            "no_movement", claim.Sha, claim.Warning,
            [new(ProgressOrigin.Primary, CompletionProgressAssessment.NoAttributedProgress, Reason: "no_movement", RegisteredPath: task.WorktreePath)]);
        return ToEvaluation(claim, negative, primaryDirect: false);
    }

    private async Task<CompletionProgressSource> EvaluateSourceAsync(
        AgentTask task,
        ProgressSourceBaseline source,
        ProgressBaselineSnapshot baseline,
        string? claim,
        bool isRepair,
        List<CompletionProgressSource> sink,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var originLocal = isRepair ? ProgressOrigin.RepairSource : ProgressOrigin.Primary;
        var originRemote = isRepair ? ProgressOrigin.RepairSourceRemote : ProgressOrigin.PrimaryRemote;
        var repo = source.CanonicalRepository;
        try
        {
            var common = await _git.CommonDirectoryAsync(repo, ct);
            if (!PathsEqual(common, source.CanonicalCommonDirectory))
            {
                var drifted = Arm(originLocal, CompletionProgressAssessment.Indeterminate, "source_repository_changed", false, source, null, null);
                sink.Add(drifted);
                return drifted;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failed = Arm(originLocal, CompletionProgressAssessment.Indeterminate, "source_repository_changed", false, source, null, null);
            sink.Add(failed);
            return failed;
        }

        var symbolic = isRepair
            ? new ProgressSymbolicHead(true, source.FullRef, null)
            : await ReadPrimaryHeadAsync(source, ct);
        if (!symbolic.Succeeded && !isRepair)
        {
            var failed = Arm(originLocal, CompletionProgressAssessment.Indeterminate, "source_ref_changed", false, source, null, null);
            sink.Add(failed);
            return failed;
        }

        var local = await _git.RevParseCommitAsync(repo, source.FullRef, ct);
        if (!local.Succeeded)
        {
            var failed = Arm(originLocal, CompletionProgressAssessment.Indeterminate, "source_ref_changed", false, source, null, null);
            sink.Add(failed);
            return failed;
        }

        ProgressRemoteObservation remote;
        try
        {
            remote = await _git.ObserveExactRefAsync(repo, source.FullRef, source.Remote.EndpointFingerprint, task.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            remote = new ProgressRemoteObservation(ProgressRemoteState.Unavailable, Reason: "source_remote_unreadable");
        }
        if (remote.State == ProgressRemoteState.Unavailable
            && remote.Reason is "source_remote_endpoint_changed")
        {
            var drifted = Arm(originRemote, CompletionProgressAssessment.Indeterminate, remote.Reason, false, source, local.Sha, null);
            sink.Add(drifted);
            return drifted;
        }

        WorkspaceProgressArm? files = null;
        if (!isRepair && _files is not null && !string.IsNullOrWhiteSpace(source.RegisteredCheckout))
        {
            try
            {
                files = await _files.ProbeProgressAsync(source.RegisteredCheckout, baseline.FileProbeCutoff, false, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                files = new WorkspaceProgressArm(false, null, null, false);
            }
        }

        var primaryOnExpectedBranch = isRepair || string.Equals(symbolic.FullRef, source.FullRef, StringComparison.Ordinal);
        var localTip = local.Sha!;
        var remoteTip = remote.State == ProgressRemoteState.Present ? remote.Sha : null;

        await PinIfPossible(repo, task.Id, isRepair ? "repair-local" : "primary-local", source.LocalSha, ct);
        if (source.Remote is { State: ProgressRemoteState.Present, Sha: { } br })
            await PinIfPossible(repo, task.Id, isRepair ? "repair-remote" : "primary-remote", br, ct);

        var filePositive = !isRepair && files is { LastFileChangeAt: not null };
        var fileUnavailable = !isRepair && files is { Available: false } && files.LastFileChangeAt is null && files.LastCommitAt is null;
        var statusUnavailable = !isRepair && files is { Available: false };

        if (!isRepair && filePositive && primaryOnExpectedBranch)
        {
            var fileArm = Arm(ProgressOrigin.Primary, CompletionProgressAssessment.ProgressObserved, null, true, source, localTip, remoteTip);
            sink.Add(fileArm);
            return fileArm;
        }

        var claimed = claim;
        CompletionProgressSource result;
        if (isRepair)
            result = await EvaluateClaimedOrUnclaimedAsync(
                repo, source, claimed, localTip, remoteTip, remote, originLocal, originRemote, requireClaim: true, ct);
        else if (!primaryOnExpectedBranch)
            result = Arm(originLocal, CompletionProgressAssessment.NoAttributedProgress, "no_movement", true, source, localTip, remoteTip);
        else
            result = await EvaluatePrimaryGraphAsync(
                repo, source, claimed, localTip, remoteTip, remote, originLocal, originRemote, ct);

        if (statusUnavailable && result.Assessment == CompletionProgressAssessment.NoAttributedProgress)
        {
            var reason = files is { LastCommitAt: not null } ? "primary_log_unavailable" : "primary_status_unavailable";
            result = result with { Assessment = CompletionProgressAssessment.Indeterminate, Reason = reason, Complete = false };
        }

        sink.Add(result);
        if (result.Assessment == CompletionProgressAssessment.ProgressObserved)
            return result;

        if (!isRepair && filePositive)
        {
            var fileArm = Arm(ProgressOrigin.Primary, CompletionProgressAssessment.ProgressObserved, null, true, source, localTip, remoteTip);
            sink.Add(fileArm);
            return fileArm;
        }

        return result;
    }

    private async Task<CompletionProgressSource> EvaluatePrimaryGraphAsync(
        string repo,
        ProgressSourceBaseline source,
        string? claim,
        string localTip,
        string? remoteTip,
        ProgressRemoteObservation remote,
        ProgressOrigin originLocal,
        ProgressOrigin originRemote,
        CancellationToken ct)
    {
        var localNovel = await IsNovelCommitAsync(repo, localTip, source, ct);
        if (localNovel == true && localTip != source.LocalSha)
        {
            var lineage = await LineageHoldsAsync(repo, source.LocalSha, localTip, ct);
            if (lineage == false)
                return Arm(originLocal, CompletionProgressAssessment.Indeterminate, "baseline_lineage_broken", true, source, localTip, remoteTip);
            if (lineage is null)
                return Arm(originLocal, CompletionProgressAssessment.Indeterminate, "primary_log_unavailable", false, source, localTip, remoteTip);
            return Arm(originLocal, CompletionProgressAssessment.ProgressObserved, null, true, source, localTip, remoteTip, verified: localTip);
        }
        if (localNovel is null)
            return Arm(originLocal, CompletionProgressAssessment.Indeterminate, "primary_log_unavailable", false, source, localTip, remoteTip);

        if (remote.State == ProgressRemoteState.Unavailable)
            return Arm(originRemote, CompletionProgressAssessment.Indeterminate, remote.Reason ?? "source_remote_unreadable", false, source, localTip, null);

        if (remoteTip is not null && remoteTip != source.Remote.Sha && remoteTip != source.LocalSha)
        {
            if (string.IsNullOrEmpty(claim))
                return Arm(originRemote, CompletionProgressAssessment.NoAttributedProgress, "unclaimed_or_unmatched_commit", true, source, localTip, remoteTip);
            return await QualifyClaimAsync(repo, source, claim, localTip, remoteTip, remote, originRemote, ct);
        }

        if (!string.IsNullOrEmpty(claim))
            return await QualifyClaimAsync(repo, source, claim, localTip, remoteTip, remote, originLocal, ct);

        if (localTip != source.LocalSha)
            return Arm(originLocal, CompletionProgressAssessment.NoAttributedProgress, "unclaimed_or_unmatched_commit", true, source, localTip, remoteTip);

        return Arm(originLocal, CompletionProgressAssessment.NoAttributedProgress, "no_movement", true, source, localTip, remoteTip);
    }

    private async Task<CompletionProgressSource> EvaluateClaimedOrUnclaimedAsync(
        string repo,
        ProgressSourceBaseline source,
        string? claim,
        string localTip,
        string? remoteTip,
        ProgressRemoteObservation remote,
        ProgressOrigin originLocal,
        ProgressOrigin originRemote,
        bool requireClaim,
        CancellationToken ct)
    {
        if (remote.State == ProgressRemoteState.Unavailable)
            return Arm(originRemote, CompletionProgressAssessment.Indeterminate, remote.Reason ?? "source_remote_unreadable", false, source, localTip, null);

        var moved = localTip != source.LocalSha
            || (remoteTip is not null && remoteTip != source.Remote.Sha && source.Remote.State == ProgressRemoteState.Present)
            || (remoteTip is not null && source.Remote.State is ProgressRemoteState.Missing or ProgressRemoteState.NotConfigured);

        if (string.IsNullOrEmpty(claim))
        {
            if (!moved)
                return Arm(originLocal, CompletionProgressAssessment.NoAttributedProgress, "no_movement", true, source, localTip, remoteTip);
            return Arm(originLocal, CompletionProgressAssessment.NoAttributedProgress, "unclaimed_or_unmatched_commit", true, source, localTip, remoteTip);
        }

        var localHasClaim = await _git.IsAncestorAsync(repo, claim, localTip, ct);
        var remoteHasClaim = remoteTip is null ? (bool?)false : await _git.IsAncestorAsync(repo, claim, remoteTip, ct);
        var origin = remoteHasClaim == true && localHasClaim != true ? originRemote : originLocal;
        return await QualifyClaimAsync(repo, source, claim, localTip, remoteTip, remote, origin, ct);
    }

    private async Task<CompletionProgressSource> QualifyClaimAsync(
        string repo,
        ProgressSourceBaseline source,
        string claim,
        string localTip,
        string? remoteTip,
        ProgressRemoteObservation remote,
        ProgressOrigin origin,
        CancellationToken ct)
    {
        if (source.Remote.State == ProgressRemoteState.Unavailable && origin is ProgressOrigin.RepairSourceRemote or ProgressOrigin.PrimaryRemote)
            return Arm(origin, CompletionProgressAssessment.Indeterminate, "baseline_remote_unavailable", false, source, localTip, remoteTip, claim);

        var tip = origin is ProgressOrigin.RepairSourceRemote or ProgressOrigin.PrimaryRemote
            ? remoteTip
            : localTip;
        if (tip is null)
            return Arm(origin, CompletionProgressAssessment.NoAttributedProgress, "claimed_commit_unreachable", true, source, localTip, remoteTip, claim);

        var reachable = await _git.IsAncestorAsync(repo, claim, tip, ct);
        if (reachable is null)
            return Arm(origin, CompletionProgressAssessment.Indeterminate, "source_remote_unreadable", false, source, localTip, remoteTip, claim);
        if (reachable == false)
            return Arm(origin, CompletionProgressAssessment.NoAttributedProgress, "claimed_commit_unreachable", true, source, localTip, remoteTip, claim);

        foreach (var baselineTip in PresentBaselineTips(source))
        {
            var fromBaseline = await _git.IsAncestorAsync(repo, claim, baselineTip, ct);
            if (fromBaseline is null)
                return Arm(origin, CompletionProgressAssessment.Indeterminate, "source_remote_unreadable", false, source, localTip, remoteTip, claim);
            if (fromBaseline == true)
                return Arm(origin, CompletionProgressAssessment.NoAttributedProgress, "claimed_commit_not_novel", true, source, localTip, remoteTip, claim);
        }

        var corresponding = origin is ProgressOrigin.RepairSourceRemote or ProgressOrigin.PrimaryRemote
            ? (source.Remote.State == ProgressRemoteState.Present ? source.Remote.Sha : source.LocalSha)
            : source.LocalSha;
        if (corresponding is not null)
        {
            var lineage = await _git.IsAncestorAsync(repo, corresponding, tip, ct);
            if (lineage is null)
                return Arm(origin, CompletionProgressAssessment.Indeterminate, "source_remote_unreadable", false, source, localTip, remoteTip, claim);
            if (lineage == false)
                return Arm(origin, CompletionProgressAssessment.Indeterminate, "baseline_lineage_broken", true, source, localTip, remoteTip, claim);
        }

        return Arm(origin, CompletionProgressAssessment.ProgressObserved, null, true, source, localTip, remoteTip, claim, claim);
    }

    private async Task<bool?> IsNovelCommitAsync(string repo, string tip, ProgressSourceBaseline source, CancellationToken ct)
    {
        if (tip == source.LocalSha) return false;
        foreach (var baselineTip in PresentBaselineTips(source))
        {
            var contained = await _git.IsAncestorAsync(repo, tip, baselineTip, ct);
            if (contained is null) return null;
            if (contained == true) return false;
        }
        var fromLocal = await _git.IsAncestorAsync(repo, source.LocalSha, tip, ct);
        return fromLocal;
    }

    private async Task<bool?> LineageHoldsAsync(string repo, string baseline, string tip, CancellationToken ct) =>
        await _git.IsAncestorAsync(repo, baseline, tip, ct);

    private async Task<ProgressSymbolicHead> ReadPrimaryHeadAsync(ProgressSourceBaseline source, CancellationToken ct)
    {
        var path = source.RegisteredCheckout ?? source.CanonicalRepository;
        return await _git.SymbolicHeadAsync(path, ct);
    }

    private async Task PinIfPossible(string repo, Guid taskId, string name, string? sha, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sha) || !GitObjectId.IsFull(sha)) return;
        try { await _git.PinBaselineAsync(repo, taskId, name, sha, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
    }

    private static IEnumerable<string> PresentBaselineTips(ProgressSourceBaseline source)
    {
        if (GitObjectId.IsFull(source.LocalSha)) yield return source.LocalSha;
        if (source.Remote.State == ProgressRemoteState.Present && GitObjectId.IsFull(source.Remote.Sha))
            yield return source.Remote.Sha!;
    }

    private static CompletionProgressSource Arm(
        ProgressOrigin origin,
        CompletionProgressAssessment assessment,
        string? reason,
        bool complete,
        ProgressSourceBaseline source,
        string? local,
        string? remote,
        string? claim = null,
        string? verified = null) =>
        new(origin, assessment, source.OwnerTaskId, claim, verified, local, remote, source.RegisteredCheckout, reason, complete);

    private static Evaluation Aggregate(AgentTask task, ProgressClaimParse claim, List<CompletionProgressSource> sources)
    {
        var positives = sources.Where(s => s.Assessment == CompletionProgressAssessment.ProgressObserved).ToList();
        if (positives.Count > 0)
        {
            var evidence = new CompletionProgressEvidence(1, CompletionProgressAssessment.ProgressObserved,
                null, claim.Sha, claim.Warning, sources);
            var primaryDirect = positives.Any(s => s.Origin == ProgressOrigin.Primary);
            return ToEvaluation(claim, evidence, primaryDirect);
        }

        var unknown = sources.Where(s => s.Assessment == CompletionProgressAssessment.Indeterminate || !s.Complete).ToList();
        if (unknown.Count > 0)
        {
            var reason = unknown[0].Reason ?? "source_remote_unreadable";
            var evidence = new CompletionProgressEvidence(1, CompletionProgressAssessment.Indeterminate,
                reason, claim.Sha, claim.Warning, sources);
            return ToEvaluation(claim, evidence, false);
        }

        var unclaimed = sources.Any(s => s.Reason == "unclaimed_or_unmatched_commit");
        var reasonNeg = unclaimed ? "unclaimed_or_unmatched_commit"
            : sources.Select(s => s.Reason).FirstOrDefault(r => r is not null) ?? "no_movement";
        if (!string.IsNullOrEmpty(claim.Warning) && reasonNeg == "no_movement")
            reasonNeg = "unclaimed_or_unmatched_commit";
        var negative = new CompletionProgressEvidence(1, CompletionProgressAssessment.NoAttributedProgress,
            reasonNeg, claim.Sha, claim.Warning, sources);
        return ToEvaluation(claim, negative, false);
    }

    private static Evaluation ToEvaluation(ProgressClaimParse claim, CompletionProgressEvidence evidence, bool primaryDirect)
    {
        var warning = ProgressWarning(evidence);
        if (claim.Warning is not null && warning is null && evidence.Assessment != CompletionProgressAssessment.ProgressObserved)
            warning = $"progress=unavailable; reason={claim.Warning}";
        return new Evaluation(
            evidence.Assessment,
            evidence.Reason,
            claim.Sha,
            claim.Warning,
            evidence,
            warning,
            primaryDirect,
            AllowsAutomaticWorkspaceMutation(evidence) && primaryDirect);
    }

    private static string? RepairFullRef(AgentTask task)
    {
        var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson);
        return baseline?.RepairSource?.FullRef;
    }

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static string FullRef(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch)) return "";
        return branch.StartsWith("refs/heads/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;
    }
}
