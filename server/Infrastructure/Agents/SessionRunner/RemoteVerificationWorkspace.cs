using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// CARD-0604 D-19 (Cut B). The runner half of <see cref="IVerificationWorkspace"/>: the snapshot,
/// its evidence root and its removal all live on the runner, reached through the typed phone-home
/// operations. The desktop filesystem is never touched here, not even as a fallback -- for a
/// remote task there is nothing at those paths, and reading them would answer "absent" about
/// something that exists (G-38).
///
/// Everything the runner returns is re-checked here before it is believed: coordinates must be
/// POSIX and rooted under the runner's declared repository (G-32), and a validation compares the
/// returned coordinates ordinally with the ones the reservation recorded.
/// </summary>
public sealed class RemoteVerificationWorkspace(
    IVerificationWorkspaceTransport client, string runnerRepository, string runnerWorkspaceRoot)
    : IVerificationWorkspace
{
    public async Task<VerificationWorkspaceCreation> CreateAsync(
        string repositoryPath, string identifier, string landedSha, CancellationToken ct)
    {
        var branch = "feat/card-" + identifier;
        var response = await client.CreateAsync(new(identifier, landedSha, branch), ct);
        RequireRooted(response.Coordinates);
        if (response.InitialSha != landedSha || response.Coordinates.Branch != branch
            || response.Coordinates.CreationId == Guid.Empty)
            throw new ConflictException("verification_creation_identity_mismatch");
        return new(response.Coordinates, response.InitialSha);
    }

    public async Task<VerificationWorkspaceValidation> ValidateAsync(
        VerificationCreationCoordinates coordinates, string landedSha, CancellationToken ct)
    {
        RequireRooted(coordinates);
        var response = await client.ValidateAsync(new(coordinates, landedSha), ct);
        if (!response.Valid) return new(false, response.Reason ?? "verification_creation_identity_mismatch");
        // The runner answered "valid" about SOME coordinates; the ones that matter are these.
        // Comparing them ordinally is what makes the answer about this execution's snapshot.
        var inspection = await InspectAsync(coordinates.WorktreePath, ct);
        if (inspection.CreationId != coordinates.CreationId
            || !string.Equals(inspection.WorktreePath, coordinates.WorktreePath, StringComparison.Ordinal)
            || !string.Equals(inspection.Branch, coordinates.Branch, StringComparison.Ordinal)
            || !string.Equals(inspection.GitDirectory, coordinates.WorktreeGitDirectory, StringComparison.Ordinal)
            || !string.Equals(inspection.RepositoryPath, coordinates.RepositoryPath, StringComparison.Ordinal)
            || inspection.InitialSha != landedSha)
            return new(false, "verification_creation_identity_mismatch");
        return new(true, null);
    }

    public async Task<VerificationWorkspaceInspection> InspectAsync(string worktreePath, CancellationToken ct)
    {
        RequireUnderWorkspace(worktreePath);
        var r = await client.InspectAsync(new(worktreePath), ct);
        return new(r.CreationId, r.InitialSha, r.Branch, r.RepositoryPath, r.WorktreePath,
            r.GitDirectory, r.Head, r.Registered, r.Clean, r.Locked);
    }

    public async Task<byte[]?> ReadRestorationAsync(
        string commonGitDirectory, Guid sourceOperationId, Guid taskId, CancellationToken ct)
    {
        RequireUnderRepository(commonGitDirectory);
        var response = await client.ReadRestorationAsync(
            new(commonGitDirectory, sourceOperationId, taskId), ct);
        return response.Restoration;
    }

    public async Task<VerificationWorkspaceRemoval> RemoveAsync(
        VerificationCreationCoordinates coordinates, string expectedSha,
        IReadOnlyList<string> expectedOutputs, CancellationToken ct)
    {
        RequireRooted(coordinates);
        var r = await client.RemoveAsync(
            new(coordinates, expectedSha, expectedOutputs ?? []), ct);
        return new(r.Unregistered, r.DirectoryGone, r.BranchDeleted, r.Residue);
    }

    /// <summary>
    /// G-32. Every path the runner returns must be POSIX and under the repository/workspace it
    /// declared. A coordinate outside them is either a misconfigured runner or a runner answering
    /// about something that is not this execution's snapshot; neither is worth proceeding on.
    /// </summary>
    private void RequireRooted(VerificationCreationCoordinates? coordinates)
    {
        if (coordinates is null) throw new ConflictException("verification_creation_identity_mismatch");
        RequireUnderRepository(coordinates.RepositoryPath);
        RequireUnderRepository(coordinates.CommonGitDirectory);
        RequireUnderWorkspace(coordinates.WorktreePath);
        RequireUnderRepository(coordinates.WorktreeGitDirectory);
        if (string.IsNullOrWhiteSpace(coordinates.Branch))
            throw new ConflictException("verification_creation_identity_mismatch");
    }

    private void RequireUnderRepository(string? path) =>
        RequirePrefix(path, runnerRepository, allowExact: true);

    private void RequireUnderWorkspace(string? path) =>
        RequirePrefix(path, runnerWorkspaceRoot.TrimEnd('/') + "/worktrees", allowExact: false);

    private static void RequirePrefix(string? path, string root, bool allowExact)
    {
        var normalized = (path ?? "").TrimEnd('/');
        var canonicalRoot = root.TrimEnd('/');
        if (normalized.Length == 0 || normalized.Contains('\\', StringComparison.Ordinal)
            || normalized[0] != '/' || normalized.Split('/').Any(s => s is "." or "..")
            || !(normalized.StartsWith(canonicalRoot + "/", StringComparison.Ordinal)
                 || allowExact && normalized == canonicalRoot))
            throw new ConflictException("verification_creation_outside_runner_repository");
    }
}
