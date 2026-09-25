using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0688 D-2/D-4/D-7: builds one schema-3 operation. The source is the branch ref plus the reviewed SHA;
/// the task worktree is read once, for its git directory. The rebase base is the observed remote target, and
/// local target must be an ancestor of it (unpushed local target commits are the operator's, not a land's).
/// </summary>
internal static class LandOperationFactory
{
    internal sealed record Outcome(AgentTaskLanding? Operation, string? Reason, LandInspectionDiagnostic? Diagnostic = null);

    public static async Task<Outcome> CreateAsync(ILandingGit git, ILandWorkspace workspace, DateTime now,
        AgentTask task, AgentTaskLandRequest request, LandSourceCoordinates coordinates, string common,
        AgentTaskLanding? previous, CancellationToken ct)
    {
        var expected = request.ExpectedSourceSha!;
        var local = await ReadBranchAsync(git, coordinates.RepositoryPath, coordinates.SourceFullRef, ct);
        if (local.Reason is not null) return new(null, local.Reason);
        if (local.Sha != request.LocalBeforeSha) return new(null, "source_changed");
        var derivation = IsLegacyDerivation(previous, local.Sha!, expected);
        // Revalidate even after a saved Resolved checkpoint: a schema-3 preparation never moves the
        // source branch and therefore cannot authorize a different input through predecessor lineage.
        if (local.Sha != expected && !derivation
            && !(request.SourceRelationship == LandSourceRelationship.Behind && request.RemoteSourceSha == expected))
            return new(null, "reviewed_source_mismatch");

        LandingDestination destination;
        try { destination = await git.DestinationAsync(coordinates.RepositoryPath, coordinates.TargetFullRef, ct); }
        catch (IOException ex) { return new(null, "landing_io_error", LandFailureDiagnostic.FromIo(ex)); }

        var targetArgs = new[] { "rev-parse", "--verify", coordinates.TargetFullRef + "^{commit}" };
        var targetLookup = await git.RunAsync(coordinates.RepositoryPath, targetArgs, ct);
        var localTarget = targetLookup.Output.Trim();
        if (!targetLookup.Succeeded || !GitObjectId.IsFull(localTarget))
            return new(null, "commit_lookup_failed", LandFailureDiagnostic.FromCommand(["rev-parse", "--verify"], targetLookup.ExitCode));

        var id = Guid.NewGuid();
        var prefix = $"refs/antiphon/land/{task.Id:N}/{id:N}";
        var remote = await git.ObserveAsync(coordinates.RepositoryPath, destination, expected, prefix + "/remote-observed", ct);
        if (remote.Reason is not null || remote.Sha is null) return new(null, remote.Reason ?? "remote_unknown");
        if (localTarget != remote.Sha)
        {
            var ancestry = await git.RunAsync(coordinates.RepositoryPath, ["merge-base", "--is-ancestor", localTarget, remote.Sha], ct);
            if (ancestry.ExitCode is not (0 or 1)) return new(null, "ancestry_error");
            if (ancestry.ExitCode == 1) return new(null, "target_local_ahead");
        }

        string worktreePath, gitDirectory;
        if (Directory.Exists(coordinates.WorktreePath))
        {
            worktreePath = await git.CanonicalDirectoryAsync(coordinates.WorktreePath, ct);
            var admin = await git.RunAsync(worktreePath, ["rev-parse", "--absolute-git-dir"], ct);
            if (!admin.Succeeded || admin.Output.Trim().Length == 0)
                return new(null, "source_git_directory_unreadable", LandFailureDiagnostic.FromCommand(["rev-parse", "--absolute-git-dir"], admin.ExitCode));
            gitDirectory = await git.CanonicalDirectoryAsync(admin.Output.Trim(), ct);
        }
        else
        {
            // Cleanup compares the git directory only while the worktree directory exists.
            worktreePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(coordinates.WorktreePath));
            gitDirectory = Path.Combine(common, "worktrees", Path.GetFileName(worktreePath));
        }

        var op = new AgentTaskLanding
        {
            Id = id, TaskId = task.Id, SchemaVersion = 3, Phase = LandPhase.Inspected,
            CreatedAt = now, UpdatedAt = now, RepositoryPath = coordinates.RepositoryPath,
            WorktreePath = worktreePath, CommonDirectory = common, GitDirectory = gitDirectory,
            SourceFullRef = coordinates.SourceFullRef, SourceLocalSha = local.Sha,
            OriginalSourceSha = expected, ReviewedSourceSha = expected,
            PreparationInputSha = derivation ? previous!.RebasedSourceSha : expected,
            PreviousPreparationOperationId = derivation ? previous!.Id : null,
            ApprovalLandRequestId = previous is { OriginalSourceSha: { } prev } && prev == expected
                ? previous.ApprovalLandRequestId ?? request.Id : request.Id,
            ReviewEvidenceId = previous?.ReviewEvidenceId ?? request.ReviewEvidenceId,
            ApprovalKind = request.ApprovalKind,
            ApprovedAt = request.ApprovedAt ?? request.RequestedAt,
            SourceRemoteSha = request.RemoteSourceSha, SourceRemoteRef = request.RemoteSourceRef,
            SourceRemoteFingerprint = request.RemoteSourceFingerprint,
            SourceRemoteObservedAt = request.SourceObservedAt,
            TargetFullRef = coordinates.TargetFullRef, TargetBeforeSha = remote.Sha, RemoteBeforeSha = remote.Sha,
            LocalTargetBeforeSha = localTarget,
            RemoteName = destination.RemoteName, DestinationFullRef = destination.FullRef,
            RemoteFingerprint = destination.Fingerprint, VerificationFilter = request.VerifyFilter ?? task.LandVerifyFilter,
            LandWorktreePath = workspace.PathFor(common),
            TargetCheckoutRecorded = false, TargetCheckoutPath = null,
            RecoveryRefPrefix = prefix,
        };
        return new(op, null);
    }

    internal static bool IsLegacyDerivation(AgentTaskLanding? previous, string local, string expected) =>
        previous is { SchemaVersion: 2, Active: true, RebasedSourceSha: { } prepared }
        && prepared == local && prepared != expected && previous.OriginalSourceSha == expected;

    /// <summary>One <c>show-ref</c>; on failure a second one tells absence (exit 2) from an error.</summary>
    public static async Task<(string? Sha, string? Reason)> ReadBranchAsync(ILandingGit git, string repository,
        string fullRef, CancellationToken ct)
    {
        var read = await git.RunAsync(repository, ["show-ref", "--verify", "--hash", fullRef], ct);
        var sha = read.Output.Trim();
        if (read.Succeeded && GitObjectId.IsFull(sha)) return (sha, null);
        var exists = await git.RunAsync(repository, ["show-ref", "--exists", fullRef], ct);
        return (null, exists.ExitCode == 2 ? "source_ref_missing" : "source_ref_error");
    }
}
