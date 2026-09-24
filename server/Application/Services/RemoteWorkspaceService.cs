using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0604 D-15. The desktop half of the mirror-worktree design for remote ordinary tasks.
///
/// The DESKTOP worktree stays canonical: it is created exactly as it is today, it is what
/// <see cref="AgentTask.WorktreePath"/> names, and landing, retirement, residue and progress
/// accounting all keep reading it. The branch on origin is the unit of exchange, and the runner
/// holds a working copy of it. Nothing here is ever the only copy of anything:
///
///   create desktop worktree -> push branch -> mirror on runner -> session works and pushes
///   -> settlement fast-forwards the desktop worktree from origin -> land as usual.
///
/// The sync is fast-forward ONLY. A diverged branch or a dirty desktop tree is a settlement
/// warning and the task keeps its report; it is never a reset, because a reset would silently
/// throw away whichever side the operator actually wanted.
/// </summary>
public sealed class RemoteWorkspaceService
{
    private readonly ISessionRunnerDirectory _runners;
    private readonly ILandingGit _git;
    private readonly ILogger<RemoteWorkspaceService> _logger;
    private readonly ITaskProgressGit? _progressGit;
    private readonly IRepositoryMutationLease? _leases;
    private readonly IWorkspaceReservationJournal? _reservations;

    public RemoteWorkspaceService(
        ISessionRunnerDirectory runners,
        ILandingGit git,
        ILogger<RemoteWorkspaceService> logger,
        ITaskProgressGit? progressGit = null,
        IRepositoryMutationLease? leases = null,
        IWorkspaceReservationJournal? reservations = null)
    {
        _runners = runners;
        _git = git;
        _logger = logger;
        _progressGit = progressGit;
        _leases = leases;
        _reservations = reservations;
    }

    /// <summary>CARD-0657 D-3. The whole settlement sync attempt's deadline.</summary>
    public TimeSpan SyncBudget { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>The mirror directory name for a task: the dispatcher's own short form.</summary>
    public static string MirrorName(Guid taskId) => "task-" + taskId.ToString("N")[..8];

    /// <summary>
    /// Pushes the task branch to origin so the runner has something to fetch. A push failure is
    /// NOT a fallback to a local launch: a remote task whose branch never reached origin has no
    /// source to run against, so the caller keeps it Queued with a warning.
    /// </summary>
    public async Task<RemotePushResult> PushBranchAsync(AgentTask task, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.WorktreePath) || string.IsNullOrWhiteSpace(task.WorktreeBranch))
            return new RemotePushResult(false, null, "the task has no desktop worktree or branch");

        var head = await _git.RunAsync(task.WorktreePath, ["rev-parse", "HEAD"], ct);
        if (head.ExitCode != 0)
            return new RemotePushResult(false, null, "could not read the desktop worktree HEAD: " + Tail(head.Diagnostic));
        var sha = head.Output.Trim();

        var push = await _git.RunAsync(
            task.WorktreePath, ["push", "-u", "origin", task.WorktreeBranch], ct);
        if (push.ExitCode != 0)
            return new RemotePushResult(false, sha, "git push failed: " + Tail(push.Diagnostic));
        return new RemotePushResult(true, sha, null);
    }

    /// <summary>
    /// Asks the runner for a mirror of the pushed branch at the exact sha. Returns the POSIX path
    /// the session's cwd becomes.
    /// </summary>
    public async Task<string> MirrorAsync(AgentTask task, string sha, CancellationToken ct)
    {
        var client = Remote(task.RunnerId);
        var response = await client.MirrorWorkspaceAsync(
            new PhoneHomeWorkspaceMirrorRequest(task.WorktreeBranch!, sha, MirrorName(task.Id)), ct);
        return response.Path;
    }

    /// <summary>
    /// CARD-0657 D-2. An ordinary runner-bound Worktree task. RunnerId and the task coordinates
    /// decide this, never whether a mirror path happens to be recorded.
    /// </summary>
    public static bool IsEligible(AgentTask task) =>
        task.Workspace == WorkspaceMode.Worktree
        && !string.IsNullOrWhiteSpace(task.RunnerId)
        && task.Role != AgentTaskRole.Mutation
        && task.SourceLandingOperationId is null;

    /// <summary>CARD-0657 D-2. The only branch a task may be synchronized from, derived from its id.</summary>
    public static string OwnedBranch(Guid taskId) => "feat/card-task-" + taskId.ToString("N")[..8];

    /// <summary>
    /// CARD-0657 D-1..D-3. Settlement sync: confirm the canonical desktop worktree at the commit the
    /// session pushed to the task's OWN branch. The exact ref is observed and pinned once, the
    /// checkout's identity is revalidated under the repository lease, and the only mutation is a
    /// fast-forward to that full object id. A refusal leaves the checkout exactly as found; an
    /// unreadable answer is Unavailable, never a guessed success.
    /// </summary>
    public async Task<RemoteSettlementSyncResult> SyncAsync(AgentTask task, CancellationToken ct)
    {
        if (!IsEligible(task))
            return RemoteSettlementSyncResult.NotApplicable;

        var branch = OwnedBranch(task.Id);
        var fullRef = "refs/heads/" + branch;
        if (!string.Equals(task.WorktreeBranch, branch, StringComparison.Ordinal))
            return Outcome(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.BranchMismatch, fullRef);
        if (_progressGit is null || _leases is null)
            return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.DependencyUnavailable, fullRef);

        var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson)?.Primary;
        if (baseline is null || !GitObjectId.IsFull(baseline.LocalSha)
            || baseline.Remote.EndpointFingerprint is not { Length: > 0 })
            return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.BaselineUnavailable, fullRef);
        if (!string.Equals(baseline.FullRef, fullRef, StringComparison.Ordinal))
            return Outcome(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.BranchMismatch, fullRef);
        if ((baseline.OwnerTaskId is Guid owner && owner != task.Id)
            || string.IsNullOrWhiteSpace(task.WorktreePath)
            || string.IsNullOrWhiteSpace(baseline.RegisteredCheckout)
            || !PathsEqual(task.WorktreePath, baseline.RegisteredCheckout))
            return Outcome(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.IdentityMismatch, fullRef);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(SyncBudget);
        try
        {
            return await SyncOwnedCheckoutAsync(task, baseline, fullRef, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.Timeout, fullRef, baseline.LocalSha);
        }
        catch (TimeoutException)
        {
            return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.Timeout, fullRef, baseline.LocalSha);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Settlement sync for task {Task} could not inspect its checkout ({Type})",
                task.Id, ex.GetType().Name);
            return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.InspectionUnavailable,
                fullRef, baseline.LocalSha);
        }
    }

    private async Task<RemoteSettlementSyncResult> SyncOwnedCheckoutAsync(
        AgentTask task, ProgressSourceBaseline baseline, string fullRef, CancellationToken ct)
    {
        var repo = baseline.CanonicalRepository;
        var b = baseline.LocalSha;
        var fingerprint = baseline.Remote.EndpointFingerprint!;

        var before = await ValidateCheckoutAsync(task, baseline, fullRef, ct);
        if (before.Refusal is { } refused)
            return refused with { FullRef = fullRef, BaselineSha = b };
        var l0 = before.Head!;

        // Observe (and, if absent locally, fetch) the exact ref BEFORE taking the lease: the
        // observation takes its own lease for the fetch, and nested acquisition is not reentrant.
        var observed = await _progressGit!.ObserveExactRefAsync(repo, fullRef, fingerprint, task.Id, ct);
        switch (observed.State)
        {
            case ProgressRemoteState.Missing:
                return Outcome(RemoteSettlementSyncState.NoPushedProgress, RemoteSettlementSyncReasons.BranchNotPushed,
                    fullRef, b, desktopBefore: l0, fingerprint: fingerprint);
            case ProgressRemoteState.NotConfigured:
                return Outcome(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.EndpointAmbiguous, fullRef, b);
            case ProgressRemoteState.Unavailable:
                return observed.Reason switch
                {
                    "source_remote_endpoint_changed" => Outcome(RemoteSettlementSyncState.Refused,
                        RemoteSettlementSyncReasons.EndpointChanged, fullRef, b),
                    "source_remote_endpoint_ambiguous" => Outcome(RemoteSettlementSyncState.Refused,
                        RemoteSettlementSyncReasons.EndpointAmbiguous, fullRef, b),
                    "repository_lease_busy" => Outcome(RemoteSettlementSyncState.Unavailable,
                        RemoteSettlementSyncReasons.LeaseBusy, fullRef, b),
                    _ => Outcome(RemoteSettlementSyncState.Unavailable,
                        RemoteSettlementSyncReasons.FetchUnavailable, fullRef, b),
                };
        }

        var s = observed.Sha;
        if (!GitObjectId.IsFull(s))
            return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.FetchUnavailable, fullRef, b);

        await using var lease = await _leases!.TryAcquireAsync(
            repo, new RepositoryLeaseOwnerTag(task.Id, RepositoryLeasePurposes.WorktreeSettlement), ct);
        if (lease is null)
            return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.LeaseBusy, fullRef, b, s);

        var under = await ValidateCheckoutAsync(task, baseline, fullRef, ct);
        if (under.Refusal is { } refusedUnder)
            return refusedUnder with { FullRef = fullRef, BaselineSha = b, RemoteSha = s };
        var l = under.Head!;
        if (!string.Equals(l, l0, StringComparison.Ordinal))
            return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.ChangedDuringValidation, fullRef, b, s);

        // The task-owned pin always names S, whether or not the object was already local.
        if (observed.ObservationRef is { } observationRef)
        {
            var pinned = await _progressGit.RevParseCommitAsync(repo, observationRef, ct);
            if (!pinned.Succeeded || !string.Equals(pinned.Sha, s, StringComparison.Ordinal))
                return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.ChangedDuringValidation, fullRef, b, s);
        }
        var pin = SettlementPin(task.Id);
        var update = await _git.RunAsync(repo, ["update-ref", pin, s], ct);
        if (!update.Succeeded)
            return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.InspectionUnavailable, fullRef, b, s);

        if (!string.Equals(s, b, StringComparison.Ordinal))
        {
            var descends = await _progressGit.IsAncestorAsync(repo, b, s, ct);
            if (descends is null)
                return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.InspectionUnavailable, fullRef, b, s);
            if (descends == false)
            {
                var rewound = await _progressGit.IsAncestorAsync(repo, s, b, ct);
                if (rewound is null)
                    return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.InspectionUnavailable, fullRef, b, s);
                return Outcome(RemoteSettlementSyncState.Refused,
                    rewound == true ? RemoteSettlementSyncReasons.Rewound : RemoteSettlementSyncReasons.Diverged,
                    fullRef, b, s, l, observationRef: pin, fingerprint: fingerprint);
            }
        }

        if (!string.Equals(l, s, StringComparison.Ordinal))
        {
            var behind = await _progressGit.IsAncestorAsync(repo, l, s, ct);
            if (behind is null)
                return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.InspectionUnavailable, fullRef, b, s);
            if (behind == false)
            {
                // `merge --ff-only` alone would accept an already-ahead desktop and report its tip.
                var ahead = await _progressGit.IsAncestorAsync(repo, s, l, ct);
                if (ahead is null)
                    return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.InspectionUnavailable, fullRef, b, s);
                return Outcome(RemoteSettlementSyncState.Refused,
                    ahead == true ? RemoteSettlementSyncReasons.LocalAhead : RemoteSettlementSyncReasons.Diverged,
                    fullRef, b, s, l, observationRef: pin, fingerprint: fingerprint);
            }

            var merge = await _git.RunAsync(task.WorktreePath!, ["-c", "merge.autoStash=false", "merge", "--ff-only", s], ct);
            if (!merge.Succeeded)
                return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.MergeFailed, fullRef, b, s, l);
        }

        // Postconditions: the same registered checkout, on the same branch, clean, at exactly S.
        ValidatedCheckout after;
        try
        {
            after = await ValidateCheckoutAsync(task, baseline, fullRef, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not TimeoutException)
        {
            after = new(Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.PostconditionUnavailable), null);
        }
        if (after.Refusal is not null || !string.Equals(after.Head, s, StringComparison.Ordinal))
            return Outcome(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.PostconditionUnavailable,
                fullRef, b, s, l, observationRef: pin, fingerprint: fingerprint);

        return string.Equals(s, b, StringComparison.Ordinal)
            ? Outcome(RemoteSettlementSyncState.NoPushedProgress, RemoteSettlementSyncReasons.NoPushedProgress,
                fullRef, b, s, l, s, pin, fingerprint)
            : Outcome(RemoteSettlementSyncState.Synchronized, null, fullRef, b, s, l, s, pin, fingerprint);
    }

    private sealed record ValidatedCheckout(RemoteSettlementSyncResult? Refusal, string? Head);

    /// <summary>
    /// CARD-0657 D-2/D-3. The recorded endpoint, repository, registration, symbolic HEAD, branch
    /// tip, sequencer and clean status. Read-only: it never switches, stashes or resets anything.
    /// </summary>
    private async Task<ValidatedCheckout> ValidateCheckoutAsync(
        AgentTask task, ProgressSourceBaseline baseline, string fullRef, CancellationToken ct)
    {
        static ValidatedCheckout Refuse(RemoteSettlementSyncState state, string reason) =>
            new(new RemoteSettlementSyncResult(state, reason), null);

        var repo = baseline.CanonicalRepository;
        var fingerprint = await _progressGit!.EndpointFingerprintAsync(repo, ct);
        if (fingerprint is null)
            return Refuse(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.EndpointAmbiguous);
        if (!string.Equals(fingerprint, baseline.Remote.EndpointFingerprint, StringComparison.OrdinalIgnoreCase))
            return Refuse(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.EndpointChanged);

        if (_reservations is not null)
        {
            var active = await _reservations.ReadActiveAsync(WorkspaceReservationKey.ForTask(
                task.WorktreePath, task.WorkingDirectory, task.WorktreeBranch, task.RepoPath), ct);
            if (active.Any(r => r.Kind == WorkspaceReservationKind.Retirement))
                return Refuse(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.RetirementReserved);
        }

        string worktree;
        try
        {
            worktree = await _git.CanonicalDirectoryAsync(task.WorktreePath!, ct);
            var repoCommon = await _git.CommonDirectoryAsync(repo, ct);
            var worktreeCommon = await _git.CommonDirectoryAsync(worktree, ct);
            if (!PathsEqual(repoCommon, baseline.CanonicalCommonDirectory)
                || !PathsEqual(worktreeCommon, baseline.CanonicalCommonDirectory))
                return Refuse(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.IdentityMismatch);
        }
        catch (IOException)
        {
            return Refuse(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.IdentityMismatch);
        }

        var registrations = (await _git.RegistrationsAsync(repo, ct)).Where(r => PathsEqual(r.Path, worktree)).ToList();
        if (registrations.Count != 1 || registrations[0].Locked || registrations[0].Prunable)
            return Refuse(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.IdentityMismatch);
        var registered = registrations[0];

        var symbolic = await _git.RunAsync(worktree, ["symbolic-ref", "-q", "HEAD"], ct);
        if (symbolic.ExitCode == 1)
            return Refuse(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.BranchMismatch);
        if (!symbolic.Succeeded)
            return Refuse(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.InspectionUnavailable);
        if (!string.Equals(symbolic.Output.Trim(), fullRef, StringComparison.Ordinal)
            || !string.Equals(registered.Branch, fullRef, StringComparison.Ordinal))
            return Refuse(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.BranchMismatch);

        var head = await _progressGit.RevParseCommitAsync(worktree, "HEAD", ct);
        var tip = await _progressGit.RevParseCommitAsync(worktree, fullRef, ct);
        if (!head.Succeeded || !tip.Succeeded || head.Sha is null)
            return Refuse(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.InspectionUnavailable);
        if (!string.Equals(head.Sha, tip.Sha, StringComparison.Ordinal)
            || !string.Equals(head.Sha, registered.Head, StringComparison.Ordinal))
            return Refuse(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.ChangedDuringValidation);

        if (await _git.HasActiveSequencerAsync(worktree, ct))
            return Refuse(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.Sequencer);

        var status = await _git.RunAsync(worktree,
            ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"], ct);
        if (!status.Succeeded)
            return Refuse(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.InspectionUnavailable);
        if (status.Output.Length != 0)
            return Refuse(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.Dirty);

        return new(null, head.Sha);
    }

    /// <summary>The task-owned settlement pin: one name per task, rewritten, never accumulated.</summary>
    public static string SettlementPin(Guid taskId) => $"refs/antiphon/progress/{taskId:N}/settlement-sync";

    private static RemoteSettlementSyncResult Outcome(
        RemoteSettlementSyncState state, string? reason, string? fullRef = null, string? baselineSha = null,
        string? remoteSha = null, string? desktopBefore = null, string? desktopAfter = null,
        string? observationRef = null, string? fingerprint = null) =>
        new(state, reason, fullRef, baselineSha, remoteSha, desktopBefore, desktopAfter, observationRef, fingerprint);

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Removes the runner-side mirror at retirement. Best effort: a mirror that cannot be removed
    /// is recorded as residue on the task for the operator's sweep, never deleted on a guess and
    /// never allowed to fail retirement of the canonical desktop worktree.
    /// </summary>
    public async Task<string?> RemoveMirrorAsync(AgentTask task, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.RemoteWorktreePath) || string.IsNullOrWhiteSpace(task.RunnerId))
            return null;
        try
        {
            var response = await Remote(task.RunnerId).RemoveWorkspaceAsync(
                new PhoneHomeWorkspaceRemoveRequest(task.RemoteWorktreePath), ct);
            return response.Removed ? null : response.Residue ?? task.RemoteWorktreePath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Remote mirror {Path} for task {Task} could not be removed", task.RemoteWorktreePath, task.Id);
            return task.RemoteWorktreePath;
        }
    }

    private PhoneHomeRunnerClient Remote(string? runnerId) =>
        _runners.Resolve(runnerId) as PhoneHomeRunnerClient
        ?? throw new InvalidOperationException(
            $"Runner '{runnerId}' did not resolve to a phone-home client; a remote workspace has no meaning locally.");

    private static string Tail(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Where(line => line.Trim().Length > 0).ToList();
        return lines.Count == 0 ? "" : lines[^1];
    }
}

public sealed record RemotePushResult(bool Pushed, string? Sha, string? Warning);

