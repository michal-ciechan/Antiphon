using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The worktree side of a delegated task's life: created at dispatch, merged back when the work
/// succeeds, removed when the merge lands.
///
/// Merging follows the repo convention — rebase then fast-forward, never a merge commit — and a
/// task merges into ITS PARENT'S branch, not master: integration happens once per level of the
/// tree, resolved by the one party (the sub-orchestrator) that knows what each child was supposed
/// to do. A conflict never auto-resolves: the task blocks and a Merge-role delegate gets the
/// conflict list.
/// </summary>
public sealed class DelegationWorktreeService
{
    private readonly IRepositoryMutationLease? _leases;
    private readonly ILandingGit? _landingGit;
    private readonly IWorktreeManager _worktrees;
    private readonly IGitService _git;
    private readonly GitWorkspaceService _gitWorkspace;
    private readonly ILogger<DelegationWorktreeService> _logger;
    private readonly SourceLandingAdmission? _sourceLanding;

    public DelegationWorktreeService(
        IWorktreeManager worktrees,
        IGitService git,
        ILogger<DelegationWorktreeService> logger,
        GitWorkspaceService gitWorkspace,
        IRepositoryMutationLease? leases = null, ILandingGit? landingGit = null,
        SourceLandingAdmission? sourceLanding = null)
    {
        _leases = leases;
        _sourceLanding = sourceLanding;
        _landingGit = landingGit;
        _worktrees = worktrees;
        _git = git;
        _logger = logger;
        _gitWorkspace = gitWorkspace;
    }

    /// <summary>
    /// What happened to a finished Worktree task's branch. Exactly one of these is true, and the
    /// caller acts on which: Merged removes the worktree, Conflicted spawns a Merge task,
    /// LeftForHuman and Failed keep the branch alive and say so in the report.
    /// </summary>
    public sealed record MergeOutcome(
        MergeResult Result,
        IReadOnlyList<string> ConflictFiles,
        string? Detail);

    public enum MergeResult
    {
        /// <summary>Rebased onto the target and fast-forwarded it; the worktree is gone.</summary>
        Merged = 0,

        /// <summary>The delegate wrote nothing; Detail reports whether guarded cleanup completed.</summary>
        NothingToMerge = 1,

        /// <summary>Rebase hit conflicts (aborted cleanly); the worktree is intact for a Merge task.</summary>
        Conflicted = 2,

        /// <summary>No merge target was set — the branch is deliberately left for a human.</summary>
        LeftForHuman = 3,

        /// <summary>A git operation failed for a non-conflict reason; branch kept, nothing lost.</summary>
        Failed = 4,

        /// <summary>
        /// Legacy outcome retained for compatibility. Missing source alone no longer produces it
        /// and is never publication or deletion authority.
        /// </summary>
        AlreadyCleanedUp = 5,
    }

    /// <summary>The rebase half of an explicit land operation.</summary>
    public sealed record LandPreparation(
        bool Succeeded,
        bool Conflicted,
        bool BaseMoved,
        string? Target,
        string? Branch,
        string? Detail,
        IReadOnlyList<string> ConflictFiles);

    /// <summary>The fast-forward/push/cleanup half of an explicit land operation.</summary>
    public sealed record LandFinalization(bool Pushed, string? Sha, string? Detail, string? Residue = null);

    /// <summary>
    /// Legacy split API: refuses because it cannot establish the durable landing checkpoints.
    /// </summary>
    [Obsolete("Use AgentTaskLandingProtocol; legacy preparation has no durable authority.")]
    public Task<LandPreparation> PrepareLandAsync(AgentTask task, CancellationToken ct) =>
        Task.FromResult(new LandPreparation(false, false, false, null, null, "durable_landing_protocol_required", []));

    [Obsolete("Use AgentTaskLandingProtocol; legacy finalization has no durable authority.")]
    public Task<LandFinalization> FinalizeLandAsync(AgentTask task, string target, CancellationToken ct) =>
        Task.FromResult(new LandFinalization(false, null, "durable_landing_protocol_required"));

    public async Task<bool> KeptBranchExistsAsync(string repo, string branch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(branch) || !Directory.Exists(repo))
            return false;
        var exists = await GitAsync(repo, ct, "show-ref", "--verify", "--quiet", $"refs/heads/{branch}");
        return exists.Ok;
    }

    /// <summary>
    /// True when <paramref name="branch"/> is an ancestor of <paramref name="baseRef"/>
    /// (<c>git merge-base --is-ancestor</c>). A missing ref is not an ancestor.
    /// </summary>
    public async Task<bool> IsAncestorOfBaseAsync(
        string repo, string branch, string baseRef, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(branch) || string.IsNullOrWhiteSpace(baseRef)
            || !Directory.Exists(repo))
            return false;
        var result = await GitAsync(repo, ct, "merge-base", "--is-ancestor", branch, baseRef);
        return result.Ok;
    }

    /// <summary>Tip, commit count, and subject of <paramref name="branch"/> above <paramref name="baseRef"/>.</summary>
    public async Task<KeptBranchInfo?> DescribeKeptBranchAsync(
        string repo, string branch, string baseRef, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(branch) || !Directory.Exists(repo))
            return null;
        var tip = await GitAsync(repo, ct, "rev-parse", "--short", branch);
        if (!tip.Ok)
            return null;
        var count = await GitAsync(repo, ct, "rev-list", "--count", $"{baseRef}..{branch}");
        _ = int.TryParse(count.StdOut.Trim(), out var commits);
        var subject = await GitAsync(repo, ct, "log", "-1", "--format=%s", branch);
        return new KeptBranchInfo(tip.StdOut.Trim(), commits, subject.StdOut.Trim());
    }

    /// <summary>RepoPath equal-or-within, either direction — two worktrees of one checkout share refs.</summary>
    internal static bool SharesRepo(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && (DelegationWorkspaceResolver.IsWithinRoot(left, right)
            || DelegationWorkspaceResolver.IsWithinRoot(right, left));

    public sealed record KeptBranchInfo(string Tip, int CommitsAbove, string Subject);

    /// <summary>Create or reuse the task's managed checkout; never infer publication here.</summary>
    public async Task CreateForTaskAsync(AgentTask task, CancellationToken ct)
    {
        if (_leases is null || task.RepoPath is null)
            throw new ConflictException("repository_lease_required");
        await using var lease = await _leases.TryAcquireAsync(task.RepoPath, ct);
        if (lease is null) throw new ConflictException("repository_busy");
        await CreateForTaskAsync(task, lease, ct);
    }

    public async Task CreateForTaskAsync(AgentTask task, RepositoryLease lease, CancellationToken ct, string? startAtSha = null)
    {
        if (task.RepoPath is not { } repoPath)
            throw new ValidationException(nameof(task.RepoPath), "A worktree task needs a git repository.");
        if (_leases is null || _landingGit is null
            || !_leases.Owns(lease, await _landingGit.CommonDirectoryAsync(repoPath, ct)))
            throw new ConflictException("repository_lease_required");

        var identifier = $"task-{DelegationReportFormatter.Short(task.Id)}";
        var baseRef = startAtSha ?? task.MergeTargetRef ?? "HEAD";

        if (task.SourceLandingOperationId is not null)
        {
            if (_sourceLanding is null) throw new ConflictException("verification_source_admission_unavailable");
            var source = await _sourceLanding.RequireSourceAsync(task, ct);
            await _sourceLanding.RequireSupportAsync(ct);
            var snapshot = await _worktrees.CreateVerificationAsync(repoPath, identifier, source.VerifiedSourceSha!, lease, ct);
            task.WorktreePath = snapshot.Path;
            task.WorktreeBranch = snapshot.Branch;
            task.WorktreeBaseSha = source.VerifiedSourceSha;
            await ValidateVerificationAsync(task, lease, ct);
            return;
        }

        Dtos.WorktreeInfo info;
        try
        {
            info = await _worktrees.CreateAsync(repoPath, identifier, baseRef, lease, ct);
        }
        catch (ConflictException ex)
        {
            // A crashed or requeued earlier attempt left its worktree behind. Same task, same
            // branch — reusing it preserves whatever the last attempt committed, which is exactly
            // what the handoff wants. If the directory is gone, include the inner diagnosis
            // (CARD-0220 heal failure names the commands tried) instead of replacing it.
            var existing = (await _worktrees.ListAsync(repoPath, ct))
                .FirstOrDefault(w => w.CardId == identifier && Directory.Exists(w.Path));
            if (existing is null)
            {
                throw new ConflictException(
                    $"Worktree for task {DelegationReportFormatter.Short(task.Id)} exists but its "
                    + $"directory is gone. {ex.Message}",
                    ex);
            }

            info = existing;
        }

        task.WorktreePath = info.Path;
        task.WorktreeBranch = info.Branch;
        task.WorktreeBaseSha = await _gitWorkspace.GetHeadShaAsync(info.Path, ct);
        _logger.LogInformation(
            "Task {ShortId}: worktree at {Path} on {Branch} (base {BaseRef}, sha {Sha})",
            DelegationReportFormatter.Short(task.Id), info.Path, info.Branch, baseRef,
            task.WorktreeBaseSha ?? "(unknown)");
    }

    /// <summary>
    /// The PreToolUse deny hook, verbatim. Blocks the edit tools with exit code 2 — Claude Code
    /// feeds the stderr message back to the model — so "you are an orchestrator, delegate this"
    /// becomes an invariant instead of advice. powershell.exe (5.1) on purpose: always present on
    /// Windows and invocable identically from cmd or sh, whichever shell runs the hook.
    /// </summary>
    internal const string DenyHookSettingsJson = """
        {
          "hooks": {
            "PreToolUse": [
              {
                "matcher": "Edit|Write|MultiEdit|NotebookEdit",
                "hooks": [
                  {
                    "type": "command",
                    "command": "powershell -NoProfile -Command \"[Console]::Error.WriteLine('This session is an orchestrator: do not edit files yourself. Delegate the change with the antiphon-delegate skill (pwsh -NoProfile -File scripts/delegate.ps1 ...) and end your turn; the report will reach you.'); exit 2\""
                  }
                ]
              }
            ]
          }
        }
        """;

    private const string DenyHookRelativePath = ".claude/settings.local.json";

    public async Task ValidateVerificationAsync(AgentTask task, RepositoryLease lease, CancellationToken ct)
    {
        if (_landingGit is null || _leases is null || task.RepoPath is null || task.WorktreePath is null
            || task.WorktreeBranch != $"feat/card-task-{DelegationReportFormatter.Short(task.Id)}"
            || task.SourceLandingSha is null || task.WorktreeBaseSha != task.SourceLandingSha
            || task.VerificationCleanupSealJson is not null)
            throw new ConflictException("verification_creation_identity_mismatch");
        var common = await _landingGit.CommonDirectoryAsync(task.RepoPath, ct);
        if (!_leases.Owns(lease, common)) throw new ConflictException("repository_lease_required");
        var creation = await _worktrees.ReadVerificationCreationAsync(task.WorktreePath, ct);
        if (creation is null || creation.CreationId == Guid.Empty || creation.InitialSha != task.SourceLandingSha
            || creation.Branch != task.WorktreeBranch)
            throw new ConflictException("verification_creation_identity_mismatch");
        var repository = await _landingGit.CanonicalDirectoryAsync(task.RepoPath, ct);
        var path = await _landingGit.CanonicalDirectoryAsync(task.WorktreePath, ct);
        var admin = await _landingGit.RunAsync(path, ["rev-parse", "--absolute-git-dir"], ct);
        if (!admin.Succeeded) throw new ConflictException("verification_creation_identity_mismatch");
        var gitDirectory = await _landingGit.CanonicalDirectoryAsync(admin.Output.Trim(), ct);
        var coordinates = new global::Antiphon.SessionRunner.Contracts.VerificationCreationCoordinates(
            repository, common, path, gitDirectory, task.WorktreeBranch, creation.CreationId);
        var metadataCoordinates = coordinates with
        {
            RepositoryPath = await _landingGit.CanonicalDirectoryAsync(creation.RepositoryPath, ct),
            WorktreePath = await _landingGit.CanonicalDirectoryAsync(creation.WorktreePath, ct),
            WorktreeGitDirectory = await _landingGit.CanonicalDirectoryAsync(creation.GitDirectory, ct),
        };
        if (coordinates != metadataCoordinates)
            throw new ConflictException("verification_creation_identity_mismatch");
        var registrations = await _landingGit.RegistrationsAsync(repository, ct);
        var matches = registrations.Where(r => r.Branch == "refs/heads/" + task.WorktreeBranch).ToList();
        if (matches.Count != 1 || matches[0].Locked || matches[0].Prunable
            || await _landingGit.CanonicalDirectoryAsync(matches[0].Path, ct) != path
            || await _landingGit.HasActiveSequencerAsync(path, ct))
            throw new ConflictException("verification_creation_identity_mismatch");
        var head = await _landingGit.RunAsync(path, ["rev-parse", "--verify", "HEAD^{commit}"], ct);
        var symbolic = await _landingGit.RunAsync(path, ["symbolic-ref", "-q", "HEAD"], ct);
        var status = await _landingGit.RunAsync(path, ["status", "--porcelain=v1", "-z", "--untracked-files=no", "--ignore-submodules=none"], ct);
        if (!head.Succeeded || head.Output.Trim() != task.SourceLandingSha || !symbolic.Succeeded
            || symbolic.Output.Trim() != "refs/heads/" + task.WorktreeBranch || !status.Succeeded || status.Output.Length != 0)
            throw new ConflictException("verification_snapshot_not_restored");
        var serialized = System.Text.Json.JsonSerializer.Serialize(coordinates);
        if (task.VerificationCreationJson is not null && task.VerificationCreationJson != serialized)
            throw new ConflictException("verification_creation_identity_mismatch");
        task.VerificationCreationJson = serialized;
    }

    /// <summary>
    /// Arm the deny hook in a task's OWN worktree — the only place it is ever written, because a
    /// settings file in a shared directory changes every session that runs there. The file is also
    /// added to the repo's shared git exclude so the merge-back's commit-all can never sweep the
    /// hook onto the target branch. Returns false (with a log) rather than failing the dispatch:
    /// an orchestrator without its guardrail still beats no orchestrator.
    /// </summary>
    public async Task<bool> ArmDenyHookAsync(AgentTask task, CancellationToken ct)
    {
        if (task.WorktreePath is not { } worktree || !Directory.Exists(worktree))
            return false;

        try
        {
            var settingsPath = Path.Combine(worktree, DenyHookRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(settingsPath))
            {
                // A tracked settings.local.json shouldn't exist (it's the personal-override file),
                // but clobbering whatever put it there is worse than skipping the hook.
                _logger.LogWarning(
                    "Task {ShortId}: {Path} already exists — deny hook NOT armed",
                    DelegationReportFormatter.Short(task.Id), settingsPath);
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            await File.WriteAllTextAsync(settingsPath, DenyHookSettingsJson, ct);
            await EnsureGitExcludeAsync(worktree, DenyHookRelativePath, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Task {ShortId}: could not arm the deny hook",
                DelegationReportFormatter.Short(task.Id));
            return false;
        }
    }

    /// <summary>
    /// Add <paramref name="relativePath"/> to the repo's shared info/exclude (worktrees share it
    /// via the common git dir). Without this, merge-back's `git add -A` commits the hook file onto
    /// the parent's branch — a settings file escaping its sandbox is exactly the failure the
    /// worktree placement exists to prevent.
    /// </summary>
    private async Task EnsureGitExcludeAsync(string worktree, string relativePath, CancellationToken ct)
    {
        var common = await GitAsync(worktree, ct, "rev-parse", "--git-common-dir");
        if (!common.Ok)
            return;

        var commonDir = common.StdOut.Trim();
        if (!Path.IsPathRooted(commonDir))
            commonDir = Path.GetFullPath(Path.Combine(worktree, commonDir));

        var excludePath = Path.Combine(commonDir, "info", "exclude");
        Directory.CreateDirectory(Path.GetDirectoryName(excludePath)!);
        var existing = File.Exists(excludePath) ? await File.ReadAllTextAsync(excludePath, ct) : string.Empty;
        if (existing.Split('\n').Any(l => l.Trim() == relativePath))
            return;

        var newline = existing.Length == 0 || existing.EndsWith('\n') ? string.Empty : "\n";
        await File.AppendAllTextAsync(excludePath, $"{newline}{relativePath}\n", ct);
    }

    /// <summary>
    /// Land the task's work on its merge target. Commit-all → rebase onto the target → fast-forward
    /// the target — and remove the worktree only when the target actually moved.
    /// </summary>
    public async Task<MergeOutcome> TryMergeBackAsync(AgentTask task, CancellationToken ct)
    {
        if (task.Role == Domain.Enums.AgentTaskRole.Mutation || task.SourceLandingOperationId is not null)
            return new MergeOutcome(MergeResult.LeftForHuman, [], "Verification snapshot retained; no autosave or publication.");
        if (task.WorktreePath is not { } worktree || task.WorktreeBranch is not { } branch
            || task.RepoPath is not { } repo)
        {
            return new MergeOutcome(MergeResult.Failed, [], "The task has no worktree recorded.");
        }

        if (_leases is null || _landingGit is null)
            return new MergeOutcome(MergeResult.Failed, [], "repository_lease_required");
        await using var lease = await _leases.TryAcquireAsync(repo, ct);
        if (lease is null) return new MergeOutcome(MergeResult.Failed, [], "repository_busy");
        // Missing registration is unknown, never evidence that this child merged.
        if (!await IsRegisteredWorktreeAsync(repo, worktree, ct))
            return new MergeOutcome(MergeResult.Failed, [], "source_registration_unknown");
        var symbolic = await _landingGit.RunAsync(worktree, ["symbolic-ref", "-q", "HEAD"], ct);
        if (!symbolic.Succeeded || symbolic.Output.Trim() != "refs/heads/" + branch)
            return new MergeOutcome(MergeResult.Failed, [], "source_branch_mismatch");
        var targetRef = task.MergeTargetRef is { } requested
            ? (requested.StartsWith("refs/heads/", StringComparison.Ordinal) ? requested : "refs/heads/" + requested) : null;
        string? targetBefore = null;
        string? targetCheckoutBefore = null;
        if (targetRef is not null)
        {
            targetBefore = await ReadCleanTargetAsync(repo, targetRef, ct);
            if (targetBefore is null || targetRef == "refs/heads/" + branch)
                return new MergeOutcome(MergeResult.Failed, [], "target_dirty_or_unknown");
            targetCheckoutBefore = await FindCheckoutOfBranchAsync(repo, targetRef[11..], ct);
        }

        try
        {
            // The delegate may have left uncommitted work — a report that says "done" with a dirty
            // tree is normal, not an error. Sweep it into the task branch first.
            await _git.CommitAllChangesAsync(
                worktree, $"task {DelegationReportFormatter.Short(task.Id)}: {task.Title}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MergeOutcome(MergeResult.Failed, [], $"Committing the delegate's work failed: {ex.Message}");
        }

        if (task.MergeTargetRef is not { } target)
            return new MergeOutcome(MergeResult.LeftForHuman, [], $"Branch {branch} kept — no merge target was set.");

        // Nothing on the branch beyond the target means the delegate changed nothing (a read-only
        // investigation, or work it decided against). Don't leave an empty branch around.
        var source = await _landingGit.InspectAsync(new(task.Id, repo, worktree, "refs/heads/" + branch, targetRef!), ct);
        if (!source.Accepted) return new MergeOutcome(MergeResult.Failed, [], source.Reason);
        var ahead = await GitAsync(worktree, ct, "rev-list", "--count", $"{targetBefore}..{source.Snapshot!.HeadSha}");
        if (ahead.Ok && ahead.StdOut.Trim() == "0")
        {
            var cleanup = await RemoveLocalAsync(task, target, targetBefore!, targetCheckoutBefore, lease, ct);
            return new MergeOutcome(MergeResult.NothingToMerge, [], cleanup.IsClean
                ? "No changes beyond target; cleanup complete." : $"No changes beyond target; cleanup retained: {cleanup.Residue}");
        }

        // Rebase, never merge commits (repo convention). A conflict aborts cleanly: the worktree is
        // left exactly as the delegate finished it, which is what the Merge task needs to see.
        if (await ReadCleanTargetAsync(repo, targetRef!, ct) != targetBefore
            || await FindCheckoutOfBranchAsync(repo, targetRef![11..], ct) != targetCheckoutBefore)
            return new MergeOutcome(MergeResult.Failed, [], "target_changed");
        var rebase = await GitAsync(worktree, ct, "-c", "rebase.autoStash=false", "-c", "rebase.updateRefs=false", "rebase", targetBefore!);
        if (!rebase.Ok)
        {
            var conflicts = await GitAsync(worktree, ct, "diff", "--name-only", "--diff-filter=U");
            var files = conflicts.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            await GitAsync(worktree, ct, "rebase", "--abort");

            if (files.Count > 0)
                return new MergeOutcome(MergeResult.Conflicted, files, rebase.StdErr.Trim());

            return new MergeOutcome(MergeResult.Failed, [], $"Rebase onto {target} failed: {rebase.StdErr.Trim()}");
        }

        var prepared = await _landingGit.InspectAsync(source.Snapshot!.Coordinates, ct);
        if (!prepared.Accepted || prepared.Snapshot!.GitDirectory != source.Snapshot.GitDirectory
            || rebase.RebaseHeadSha is null || prepared.Snapshot.HeadSha != rebase.RebaseHeadSha)
            return new MergeOutcome(MergeResult.Failed, [], "source_changed");
        var advanced = await AdvanceTargetAsync(repo, prepared.Snapshot!.HeadSha, targetRef!, targetBefore!, targetCheckoutBefore, ct);
        if (advanced is { } failure)
            return new MergeOutcome(MergeResult.Failed, [], failure);

        var removal = await RemoveLocalAsync(task, target, prepared.Snapshot.HeadSha, targetCheckoutBefore, lease, ct);
        var detail = $"{branch} → {target}";
        if (!removal.IsClean && removal.Residue is not null)
            detail += $"; cleanup incomplete: {removal.Residue}";
        return new MergeOutcome(MergeResult.Merged, [], detail);
    }

    /// <summary>
    /// Fast-forward the captured target using expected-old-SHA CAS, or merge --ff-only in
    /// the same clean checkout captured before preparation. Returns null only after rechecking it.
    /// </summary>
    private async Task<string?> AdvanceTargetAsync(
        string repo, string sourceSha, string target, string expected, string? expectedCheckout, CancellationToken ct)
    {
        if (await ReadCleanTargetAsync(repo, target, ct) != expected) return "target_changed";
        if (!(await GitAsync(repo, ct, "merge-base", "--is-ancestor", expected, sourceSha)).Ok)
            return "target_not_fast_forward";
        var checkout = await FindCheckoutOfBranchAsync(repo, target[11..], ct);
        if (checkout != expectedCheckout) return "target_checkout_changed";
        var advance = checkout is null
            ? await GitAsync(repo, ct, "update-ref", "--no-deref", target, sourceSha, expected)
            : await GitAsync(checkout, ct, "-c", "merge.autoStash=false", "merge", "--ff-only", sourceSha);
        return advance.Ok && await ReadCleanTargetAsync(repo, target, ct) == sourceSha ? null : "target_advance_unconfirmed";
    }

    private async Task<string?> ReadCleanTargetAsync(string repo, string fullRef, CancellationToken ct)
    {
        if (!(await GitAsync(repo, ct, "check-ref-format", fullRef)).Ok) return null;
        if (_landingGit is null || (await _landingGit.RunAsync(repo, ["symbolic-ref", "-q", fullRef], ct)).ExitCode != 1) return null;
        var sha = await GitAsync(repo, ct, "rev-parse", "--verify", fullRef + "^{commit}");
        if (!sha.Ok) return null;
        var rows = (await _landingGit.RegistrationsAsync(repo, ct)).Where(r => r.Branch == fullRef).ToList();
        if (rows.Count > 1 || rows.Any(r => r.Locked || r.Prunable)) return null;
        foreach (var row in rows)
        {
            var path = await _landingGit.CanonicalDirectoryAsync(row.Path, ct);
            if (await _landingGit.HasActiveSequencerAsync(path, ct)) return null;
            var symbolic = await GitAsync(path, ct, "symbolic-ref", "-q", "HEAD");
            var status = await GitAsync(path, ct, "status", "--porcelain", "-z", "--untracked-files=all", "--ignore-submodules=none");
            var head = await GitAsync(path, ct, "rev-parse", "HEAD");
            if (!symbolic.Ok || symbolic.StdOut.Trim() != fullRef || !status.Ok || status.StdOut.Length != 0
                || !head.Ok || head.StdOut.Trim() != sha.StdOut.Trim()) return null;
        }
        return sha.StdOut.Trim();
    }

    /// <summary>
    /// True when <paramref name="worktree"/> still exists as this repo's registered worktree and
    /// git will actually run there. A missing directory, a missing .git, a prune leftover, or a
    /// stale gitdir is unknown source identity and cannot establish cleanup or merge success.
    /// </summary>
    private async Task<bool> IsRegisteredWorktreeAsync(string repo, string worktree, CancellationToken ct)
    {
        if (!Directory.Exists(worktree))
            return false;

        var gitMarker = Path.Combine(worktree, ".git");
        if (!File.Exists(gitMarker) && !Directory.Exists(gitMarker))
            return false;

        if (!await IsListedAsWorktreeAsync(repo, worktree, ct))
            return false;

        var inside = await GitAsync(worktree, ct, "rev-parse", "--is-inside-work-tree");
        return inside.Ok && inside.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> IsListedAsWorktreeAsync(string repo, string worktree, CancellationToken ct)
    {
        if (_landingGit is null) return false;
        var wanted = await _landingGit.CanonicalDirectoryAsync(worktree, ct);
        var rows = await _landingGit.RegistrationsAsync(repo, ct);
        var matching = new List<LandingRegistration>();
        foreach (var row in rows)
        {
            if (!Directory.Exists(row.Path)) continue;
            var path = await _landingGit.CanonicalDirectoryAsync(row.Path, ct);
            if (string.Equals(path, wanted, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                matching.Add(row);
        }
        return matching.Count == 1 && !matching[0].Locked && !matching[0].Prunable;
    }

    /// <summary>Where <paramref name="branch"/> is currently checked out, if anywhere.</summary>
    private async Task<string?> FindCheckoutOfBranchAsync(string repo, string branch, CancellationToken ct)
    {
        if (_landingGit is null) throw new IOException("target_inspection_unavailable");
        var rows = (await _landingGit.RegistrationsAsync(repo, ct)).Where(r => r.Branch == "refs/heads/" + branch).ToList();
        if (rows.Count > 1 || rows.Any(r => r.Locked || r.Prunable)) throw new IOException("target_registration_unavailable");
        return rows.Count == 0 ? null : await _landingGit.CanonicalDirectoryAsync(rows[0].Path, ct);
    }

    private async Task<WorktreeRemoval> RemoveLocalAsync(AgentTask task, string target, string expectedTarget,
        string? expectedCheckout, RepositoryLease lease, CancellationToken ct)
    {
        var coordinates = new LandSourceCoordinates(task.Id, task.RepoPath!, task.WorktreePath!,
            "refs/heads/" + task.WorktreeBranch, target.StartsWith("refs/heads/", StringComparison.Ordinal) ? target : "refs/heads/" + target);
        var inspected = await _landingGit!.InspectAsync(coordinates, ct);
        if (!inspected.Accepted) return new(false, false, false, inspected.Reason ?? "source_unknown");
        var source = inspected.Snapshot!;
        return await _worktrees.TryRemoveAsync(new(WorktreeRemovalPurpose.LocalMerge, coordinates,
            source.CommonDirectory, source.GitDirectory, source.HeadSha, expectedTarget, null, lease,
            TargetCheckoutRecorded: true, TargetCheckoutPath: expectedCheckout), ct);
    }

    private sealed record GitResult(bool Ok, string StdOut, string StdErr, string? RebaseHeadSha = null);

    private async Task<GitResult> GitAsync(string workingDirectory, CancellationToken ct, params string[] args)
    {
        if (_landingGit is not null)
        {
            var result = await _landingGit.RunAsync(workingDirectory, args, ct);
            return new(result.Succeeded, result.Output, result.Diagnostic, result.RebaseHeadSha);
        }
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        return new GitResult(process.ExitCode == 0, await stdout, await stderr);
    }
}
