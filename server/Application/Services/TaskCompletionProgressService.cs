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
    private readonly TimeProvider _clock;
    private readonly IRemoteSettlementSync? _remoteSync;

    public TaskCompletionProgressService(
        ITaskProgressGit git, IWorkspaceProgressProbe? files = null, TimeProvider? clock = null,
        IRemoteSettlementSync? remoteSync = null)
    {
        _git = git;
        _files = files;
        _clock = clock ?? TimeProvider.System;
        _remoteSync = remoteSync;
    }

    /// <summary>
    /// CARD-0613 D-6. What the candidate tip is, relative to the captured baseline. Distinguishing
    /// 'left the baseline lineage' from 'no progress' is the whole fix: the old boolean answered
    /// false to both, and a task branch reset into a divergent lineage settled as a false failure.
    /// </summary>
    private enum CommitNovelty
    {
        /// <summary>Equal to, or already contained in, a captured baseline history.</summary>
        Contained,

        /// <summary>Descends from the captured local baseline — ordinary, decisive progress.</summary>
        Descendant,

        /// <summary>Post-baseline work whose ancestry left the base branch. Qualified by D-6 time.</summary>
        Divergent,

        /// <summary>A required ancestry query could not be answered. Never a negative.</summary>
        Unknown,
    }

    /// <summary>
    /// CARD-0613 D-4. What the task's OWN registered checkout is actually on right now — not what
    /// its recorded branch says. Read from the registered path only, never from prose in a report
    /// and never by scanning sibling refs. A null <see cref="ObservedRef"/> on a succeeded
    /// observation is a detached HEAD, which is a valid alternate candidate; a failed read is
    /// unknown, which is neither detached nor 'nothing moved'.
    /// </summary>
    private sealed record PrimaryObservation(
        bool Succeeded, string? Reason, string? ObservedRef, string? ObservedSha);

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

    public Task<Evaluation> EvaluateAsync(AgentTask task, string body, CancellationToken ct) =>
        EvaluateAsync(task, body, prepared: null, ct);

    public async Task<Evaluation> EvaluateAsync(
        AgentTask task, string body, RemoteSettlementSyncResult? prepared, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var claim = ParseClaim(task.Id, body);
        var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson);
        if (baseline is null)
            return await EvaluateLegacyAsync(task, claim, ct);

        // CARD-0613 D-6. ONE evaluation instant for the whole assessment, so two arms cannot
        // disagree about whether a candidate is in the future.
        var now = _clock.GetUtcNow().UtcDateTime;
        var sources = new List<CompletionProgressSource>();
        var primary = await EvaluateSourceAsync(
            task, baseline.Primary, baseline, now, claim.Sha, isRepair: false, sources, ct);
        if (baseline.RepairSource is { } repair)
            await EvaluateSourceAsync(task, repair, baseline, now, claim.Sha, isRepair: true, sources, ct);

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
        // CARD-0613 D-7. Attribution the caller can act on: the work is real, but it is not on the
        // ref this task's branch names, so nothing merged itself back.
        var alternate = evidence.Sources?.FirstOrDefault(IsAlternateProgress);
        if (alternate is not null)
        {
            var where = alternate.ObservedRef ?? "detached HEAD";
            var commit = alternate.VerifiedSha ?? alternate.ClaimedSha ?? "";
            return $"progress=primary-alternate; reason={alternate.Reason ?? "unknown"}; at={where}"
                + (commit.Length == 0 ? "" : $"; commit={commit}");
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
        DateTime now,
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

        // CARD-0613 D-4. Observe the registered checkout itself: its repository identity, the ref
        // it is on (possibly none), and the commit HEAD names. The recorded branch is what the
        // task was GIVEN; this is where its delegate actually worked.
        var observation = isRepair
            ? new PrimaryObservation(true, null, source.FullRef, null)
            : await ObservePrimaryCheckoutAsync(source, ct);
        if (!observation.Succeeded && !isRepair)
        {
            var failed = Arm(originLocal, CompletionProgressAssessment.Indeterminate,
                observation.Reason ?? "source_ref_changed", false, source, null, null);
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

        // CARD-0613 D-5. The unclaimed expected-ref fast path needs BOTH: the checkout is on that
        // ref, and its HEAD agrees with the ref tip. A ref that moved while the checkout sat
        // somewhere else cannot bypass the off-branch claim requirement.
        var onExpectedRef = isRepair || string.Equals(observation.ObservedRef, source.FullRef, StringComparison.Ordinal);
        var stableOnExpectedRef = isRepair
            || (onExpectedRef && string.Equals(observation.ObservedSha, local.Sha, StringComparison.OrdinalIgnoreCase));
        var localTip = local.Sha!;
        var remoteTip = remote.State == ProgressRemoteState.Present ? remote.Sha : null;

        await PinIfPossible(repo, task.Id, isRepair ? "repair-local" : "primary-local", source.LocalSha, ct);
        if (source.Remote is { State: ProgressRemoteState.Present, Sha: { } br })
            await PinIfPossible(repo, task.Id, isRepair ? "repair-remote" : "primary-remote", br, ct);

        var filePositive = !isRepair && files is { LastFileChangeAt: not null };
        var fileUnavailable = !isRepair && files is { Available: false } && files.LastFileChangeAt is null && files.LastCommitAt is null;
        var statusUnavailable = !isRepair && files is { Available: false };

        // CARD-0613 D-5. Only a STABLE expected-ref observation whose local history is equal to or
        // descends from the baseline confers Primary file authority; everything else still counts
        // as progress, with alternate authority, further down.
        var primaryFileAuthority = false;
        if (!isRepair && filePositive && stableOnExpectedRef)
        {
            var fileLineage = string.Equals(localTip, source.LocalSha, StringComparison.OrdinalIgnoreCase)
                ? true
                : await _git.IsAncestorAsync(repo, source.LocalSha, localTip, ct);
            primaryFileAuthority = fileLineage == true;
        }

        if (!isRepair && filePositive && primaryFileAuthority)
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
        else if (!stableOnExpectedRef)
            result = await ConfirmStableObservationAsync(source, observation, await EvaluateOffExpectedRefAsync(
                repo, source, baseline, now, claimed, localTip, remoteTip, remote, observation,
                originLocal, originRemote, ct), ct);
        else
            result = await ConfirmStableObservationAsync(source, observation, await EvaluatePrimaryGraphAsync(
                repo, source, baseline, now, claimed, localTip, remoteTip, remote, observation,
                originLocal, originRemote, ct), ct);

        if (statusUnavailable && result.Assessment == CompletionProgressAssessment.NoAttributedProgress)
        {
            var reason = files is { LastCommitAt: not null } ? "primary_log_unavailable" : "primary_status_unavailable";
            result = result with { Assessment = CompletionProgressAssessment.Indeterminate, Reason = reason, Complete = false };
        }

        sink.Add(result);
        if (result.Assessment == CompletionProgressAssessment.ProgressObserved)
            return result;

        // CARD-0613 D-5. The independent dirty-file rescue survives a graph negative OR an
        // unavailable graph, off branch and on a divergent own tip alike. Reaching here means the
        // Primary-authority arm above did not fire, so this evidence is alternate: it prevents a
        // false failure and confers no merge-back authority.
        if (!isRepair && filePositive)
        {
            var fileArm = AlternateArm(CompletionProgressAssessment.ProgressObserved,
                "primary_off_branch_files", true, source, observation, localTip, remoteTip,
                claim: null, verified: null);
            sink.Add(fileArm);
            return fileArm;
        }

        return result;
    }

    /// <summary>
    /// CARD-0613 D-4. HEAD as the registered checkout itself reports it. The common-directory
    /// comparison is what stops a replaced or re-created directory at the same path from lending
    /// its history to this task.
    /// </summary>
    private async Task<PrimaryObservation> ObservePrimaryCheckoutAsync(
        ProgressSourceBaseline source, CancellationToken ct)
    {
        var path = string.IsNullOrWhiteSpace(source.RegisteredCheckout)
            ? source.CanonicalRepository
            : source.RegisteredCheckout;

        if (!string.IsNullOrWhiteSpace(source.RegisteredCheckout))
        {
            try
            {
                var common = await _git.CommonDirectoryAsync(path, ct);
                if (!PathsEqual(common, source.CanonicalCommonDirectory))
                    return new(false, "primary_checkout_identity_changed", null, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new(false, "primary_checkout_identity_changed", null, null);
            }
        }

        var symbolic = await _git.SymbolicHeadAsync(path, ct);
        if (!symbolic.Succeeded)
            return new(false, "source_ref_changed", null, null);

        var head = await _git.RevParseCommitAsync(path, "HEAD", ct);
        if (!head.Succeeded || head.Sha is null)
            return new(false, "primary_head_unreadable", null, null);

        return new(true, symbolic.Reason, symbolic.FullRef, head.Sha);
    }

    /// <summary>
    /// CARD-0613 D-4. A positive is only as good as the snapshot it was read from. Re-read the ref
    /// and SHA; a checkout that moved underneath the evaluation is Indeterminate, not a verdict.
    /// An independent arm (files, or ordinary ancestry) is evaluated separately and still stands.
    /// </summary>
    private async Task<CompletionProgressSource> ConfirmStableObservationAsync(
        ProgressSourceBaseline source,
        PrimaryObservation first,
        CompletionProgressSource result,
        CancellationToken ct)
    {
        if (result.Assessment != CompletionProgressAssessment.ProgressObserved) return result;

        var again = await ObservePrimaryCheckoutAsync(source, ct);
        if (again.Succeeded
            && string.Equals(again.ObservedRef, first.ObservedRef, StringComparison.Ordinal)
            && string.Equals(again.ObservedSha, first.ObservedSha, StringComparison.OrdinalIgnoreCase))
            return result;

        return result with
        {
            Assessment = CompletionProgressAssessment.Indeterminate,
            Reason = "primary_head_changed",
            Complete = false,
        };
    }

    /// <summary>
    /// CARD-0613 D-5. The checkout is off its expected ref, or detached. A valid task-scoped claim
    /// reachable from the ACTUAL HEAD can prevent a false failure; unrelated branch movement with
    /// no claim still earns nothing. Whatever qualifies here is alternate evidence: it never
    /// authorizes merge-back, and it never changes the task's recorded branch or base.
    /// </summary>
    private async Task<CompletionProgressSource> EvaluateOffExpectedRefAsync(
        string repo,
        ProgressSourceBaseline source,
        ProgressBaselineSnapshot baseline,
        DateTime now,
        string? claim,
        string localTip,
        string? remoteTip,
        ProgressRemoteObservation remote,
        PrimaryObservation observation,
        ProgressOrigin originLocal,
        ProgressOrigin originRemote,
        CancellationToken ct)
    {
        var observedHead = observation.ObservedSha!;

        if (!string.IsNullOrEmpty(claim))
        {
            var alternate = await QualifyPrimaryAlternateAsync(
                repo, source, baseline, now, claim, observedHead, "primary_off_branch_claim",
                claim, observation, localTip, remoteTip, ct);
            if (alternate.Assessment == CompletionProgressAssessment.ProgressObserved)
                return alternate;

            // The local checkout changed branch; the EXPECTED ref's remote observation keeps its
            // own exact-ref corroboration rules unchanged.
            if (remote.State != ProgressRemoteState.Unavailable
                && remoteTip is not null && remoteTip != source.Remote.Sha && remoteTip != source.LocalSha)
            {
                var viaRemote = await QualifyClaimAsync(
                    repo, source, claim, localTip, remoteTip, remote, originRemote, ct);
                if (viaRemote.Assessment == CompletionProgressAssessment.ProgressObserved)
                    return viaRemote;
            }

            return alternate;
        }

        var moved = !string.Equals(observedHead, source.LocalSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(localTip, source.LocalSha, StringComparison.OrdinalIgnoreCase);
        return AlternateArm(
            CompletionProgressAssessment.NoAttributedProgress,
            moved ? "unclaimed_or_unmatched_commit" : "no_movement",
            true, source, observation, localTip, remoteTip, claim: null, verified: null);
    }

    /// <summary>
    /// CARD-0613 D-6/D-9. The shared primary-local qualifier for a candidate whose ancestry left
    /// the captured baseline: reachable from what was actually observed, absent from BOTH present
    /// baseline histories, and committed strictly after capture and no later than this
    /// evaluation's single clock reading. Committer time is an operational bound, not proof of
    /// authorship - D-7 is what keeps it from granting integration authority. Every unknown is
    /// Indeterminate; there is no skew allowance and no retry that manufactures a positive.
    /// </summary>
    private async Task<CompletionProgressSource> QualifyPrimaryAlternateAsync(
        string repo,
        ProgressSourceBaseline source,
        ProgressBaselineSnapshot baseline,
        DateTime now,
        string candidate,
        string reachableFrom,
        string reason,
        string? claim,
        PrimaryObservation observation,
        string localTip,
        string? remoteTip,
        CancellationToken ct)
    {
        CompletionProgressSource Result(
            CompletionProgressAssessment assessment, string? why, bool complete, string? verified = null) =>
            AlternateArm(assessment, why, complete, source, observation, localTip, remoteTip, claim, verified);

        if (!GitObjectId.IsFull(candidate))
            return Result(CompletionProgressAssessment.NoAttributedProgress, "claimed_commit_unreachable", true);

        var reachable = await _git.IsAncestorAsync(repo, candidate, reachableFrom, ct);
        if (reachable is null)
            return Result(CompletionProgressAssessment.Indeterminate, "primary_log_unavailable", false);
        if (reachable == false)
            return Result(CompletionProgressAssessment.NoAttributedProgress, "claimed_commit_unreachable", true);

        foreach (var baselineTip in PresentBaselineTips(source))
        {
            var contained = await _git.IsAncestorAsync(repo, candidate, baselineTip, ct);
            if (contained is null)
                return Result(CompletionProgressAssessment.Indeterminate, "primary_log_unavailable", false);
            if (contained == true)
                return Result(CompletionProgressAssessment.NoAttributedProgress, "claimed_commit_not_novel", true);
        }

        var when = await _git.CommitTimeAsync(repo, candidate, ct);
        if (!when.Available || when.CommitterUtc is not DateTime committed)
            return Result(CompletionProgressAssessment.Indeterminate, "primary_commit_time_unavailable", false);

        // Git records whole seconds, so a stamp inside the capture second cannot be ordered
        // against it. Unknown ordering is Indeterminate, never a verdict either way.
        var stamp = WholeSecond(committed);
        var captured = WholeSecond(baseline.CapturedAt);
        if (stamp > WholeSecond(now))
            return Result(CompletionProgressAssessment.Indeterminate, "primary_commit_time_unavailable", false);
        if (stamp == captured)
            return Result(CompletionProgressAssessment.Indeterminate, "primary_commit_time_overlaps_capture", false);
        if (stamp < captured)
            return Result(CompletionProgressAssessment.NoAttributedProgress, "primary_commit_predates_dispatch", true);

        return Result(CompletionProgressAssessment.ProgressObserved, reason, true, candidate);
    }

    private static DateTime WholeSecond(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    /// <summary>
    /// CARD-0613 D-7. Alternate evidence carries the verified candidate, the checkout's ACTUAL
    /// HEAD and ref, the registered path, and a reason naming which alternate route qualified it.
    /// A positive is <see cref="ProgressOrigin.PrimaryAlternate"/>, which
    /// <see cref="AllowsAutomaticWorkspaceMutation"/> deliberately does not accept; anything short
    /// of a positive keeps the ordinary Primary origin so the aggregate reads unchanged.
    /// </summary>
    private static CompletionProgressSource AlternateArm(
        CompletionProgressAssessment assessment,
        string? reason,
        bool complete,
        ProgressSourceBaseline source,
        PrimaryObservation observation,
        string localTip,
        string? remoteTip,
        string? claim,
        string? verified) =>
        new(assessment == CompletionProgressAssessment.ProgressObserved
                ? ProgressOrigin.PrimaryAlternate
                : ProgressOrigin.Primary,
            assessment,
            source.OwnerTaskId,
            claim,
            verified,
            observation.ObservedSha ?? localTip,
            remoteTip,
            source.RegisteredCheckout,
            reason,
            complete,
            observation.ObservedRef);

    private async Task<CompletionProgressSource> EvaluatePrimaryGraphAsync(
        string repo,
        ProgressSourceBaseline source,
        ProgressBaselineSnapshot baseline,
        DateTime now,
        string? claim,
        string localTip,
        string? remoteTip,
        ProgressRemoteObservation remote,
        PrimaryObservation observation,
        ProgressOrigin originLocal,
        ProgressOrigin originRemote,
        CancellationToken ct)
    {
        var localNovel = await ClassifyNoveltyAsync(repo, localTip, source, ct);
        if (localNovel == CommitNovelty.Descendant && localTip != source.LocalSha)
        {
            var lineage = await LineageHoldsAsync(repo, source.LocalSha, localTip, ct);
            if (lineage == false)
                return Arm(originLocal, CompletionProgressAssessment.Indeterminate, "baseline_lineage_broken", true, source, localTip, remoteTip);
            if (lineage is null)
                return Arm(originLocal, CompletionProgressAssessment.Indeterminate, "primary_log_unavailable", false, source, localTip, remoteTip);
            return Arm(originLocal, CompletionProgressAssessment.ProgressObserved, null, true, source, localTip, remoteTip, verified: localTip);
        }
        if (localNovel == CommitNovelty.Unknown)
            return Arm(originLocal, CompletionProgressAssessment.Indeterminate, "primary_log_unavailable", false, source, localTip, remoteTip);

        // CARD-0613 D-6. The task is on its own expected ref, but that ref was reset into a
        // lineage the baseline is not part of. Before this card the divergent tip fell through to
        // `unclaimed_or_unmatched_commit` and settled a delegate that had done real work as
        // Failed. Qualify the exact commit by time instead, with alternate authority only.
        //
        // CARD-0613 review fix. The alternate result is kept only when it is POSITIVE, exactly as
        // EvaluateOffExpectedRefAsync does for D-5. Returning it unconditionally swallowed the two
        // arms below that the pre-card code still reached for this same input, reintroducing two
        // false complete negatives: a divergent own tip whose claim is reachable from the remote
        // but not locally (base: PrimaryRemote positive), and a divergent own tip with an
        // unreadable remote observation (base: Indeterminate, fail-open per D-6/D-8).
        //
        // CARD-0613 review fix, third pass. Keeping it only on a POSITIVE was still not enough:
        // every arm below was then overridden whenever the divergent arm produced a COMPLETE
        // NEGATIVE, so an Indeterminate PrimaryRemote verdict lost to it and the delegate settled
        // Failed again. `PreferLeastCommittal` is what decides between them now.
        CompletionProgressSource? divergent = null;
        if (localNovel == CommitNovelty.Divergent)
        {
            var candidate = string.IsNullOrEmpty(claim) ? localTip : claim;
            divergent = await QualifyPrimaryAlternateAsync(
                repo, source, baseline, now, candidate, localTip, "primary_divergent_commit",
                claim, observation, localTip, remoteTip, ct);
            if (divergent.Assessment == CompletionProgressAssessment.ProgressObserved)
                return divergent;
        }

        if (remote.State == ProgressRemoteState.Unavailable)
            return Arm(originRemote, CompletionProgressAssessment.Indeterminate, remote.Reason ?? "source_remote_unreadable", false, source, localTip, null);

        if (remoteTip is not null && remoteTip != source.Remote.Sha && remoteTip != source.LocalSha)
        {
            if (string.IsNullOrEmpty(claim))
                return PreferLeastCommittal(
                    Arm(originRemote, CompletionProgressAssessment.NoAttributedProgress, "unclaimed_or_unmatched_commit", true, source, localTip, remoteTip),
                    divergent);
            return PreferLeastCommittal(
                await QualifyClaimAsync(repo, source, claim, localTip, remoteTip, remote, originRemote, ct),
                divergent);
        }

        if (!string.IsNullOrEmpty(claim))
            return PreferLeastCommittal(
                await QualifyClaimAsync(repo, source, claim, localTip, remoteTip, remote, originLocal, ct),
                divergent);

        // Nothing else qualified: the divergent alternate verdict is the D-6 answer for this tip.
        if (divergent is not null) return divergent;

        if (localTip != source.LocalSha)
            return Arm(originLocal, CompletionProgressAssessment.NoAttributedProgress, "unclaimed_or_unmatched_commit", true, source, localTip, remoteTip);

        return Arm(originLocal, CompletionProgressAssessment.NoAttributedProgress, "no_movement", true, source, localTip, remoteTip);
    }

    /// <summary>
    /// CARD-0613 D-6/D-8. The divergent alternate verdict is a FALLBACK, never an override: it
    /// speaks only when the arm that actually qualified the claim is ITSELF a complete negative.
    /// A positive, an Indeterminate, or an incomplete observation is strictly less committal than
    /// a complete negative, and only a complete negative settles a working delegate Failed.
    /// <para>
    /// Without this, the divergent arm's own `claimed_commit_unreachable` - reached because it
    /// only ever looks at the LOCAL tip - overrode an Indeterminate PrimaryRemote verdict
    /// (`baseline_remote_unavailable`, `source_remote_unreadable` or `baseline_lineage_broken`)
    /// and reintroduced a false Failed against the pre-card base, which fell through to the same
    /// <see cref="QualifyClaimAsync"/> call and answered Indeterminate. D-6 requires an
    /// unavailable baseline remote to fail open; D-8 requires complete applicable observations
    /// before any negative.
    /// </para>
    /// </summary>
    private static CompletionProgressSource PreferLeastCommittal(
        CompletionProgressSource qualified,
        CompletionProgressSource? divergentFallback) =>
        divergentFallback is not null
        && qualified.Assessment == CompletionProgressAssessment.NoAttributedProgress
        && qualified.Complete
            ? divergentFallback
            : qualified;

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

    /// <summary>
    /// CARD-0613 D-6. Containment in EACH present baseline history is excluded first, so a
    /// re-dated commit that was already there can never reach the time fallback. Only after both
    /// exclusions does a failed descendant check mean 'divergent' rather than 'not new'.
    /// </summary>
    private async Task<CommitNovelty> ClassifyNoveltyAsync(
        string repo, string tip, ProgressSourceBaseline source, CancellationToken ct)
    {
        if (tip == source.LocalSha) return CommitNovelty.Contained;
        foreach (var baselineTip in PresentBaselineTips(source))
        {
            var contained = await _git.IsAncestorAsync(repo, tip, baselineTip, ct);
            if (contained is null) return CommitNovelty.Unknown;
            if (contained == true) return CommitNovelty.Contained;
        }
        var fromLocal = await _git.IsAncestorAsync(repo, source.LocalSha, tip, ct);
        return fromLocal switch
        {
            true => CommitNovelty.Descendant,
            false => CommitNovelty.Divergent,
            _ => CommitNovelty.Unknown,
        };
    }

    private async Task<bool?> LineageHoldsAsync(string repo, string baseline, string tip, CancellationToken ct) =>
        await _git.IsAncestorAsync(repo, baseline, tip, ct);

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

    /// <summary>
    /// CARD-0613 D-7. A stored alternate positive must not become Primary after a reload, and a
    /// negative or unknown arm is never alternate. One predicate, used by the evidence projection
    /// and by the tests that reload persisted JSON.
    /// </summary>
    public static bool IsAlternateProgress(CompletionProgressSource source) =>
        source.Origin == ProgressOrigin.PrimaryAlternate
        && source.Assessment == CompletionProgressAssessment.ProgressObserved;

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
