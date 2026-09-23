using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// CARD-0604 D-19 (Cut B). Where a SourceLanding Mutation's verification snapshot physically
/// lives, and therefore who may create, validate, inspect, read the restoration record from, and
/// remove it.
///
/// For a desktop task that is the local <c>IWorktreeManager</c>, exactly as before. For a task
/// bound to a remote runner it is that runner, and the desktop filesystem is not an alternative:
/// there is no snapshot there, no evidence root there, and a fall-through to a local read would
/// quietly answer "absent" for something that exists and is intact (G-38).
/// </summary>
public interface IVerificationWorkspace
{
    /// <summary>Create the snapshot at the exact landed sha, with schema-2 creation metadata.</summary>
    Task<VerificationWorkspaceCreation> CreateAsync(
        string repositoryPath, string identifier, string landedSha, CancellationToken ct);

    /// <summary>
    /// Re-check the snapshot: exactly one registration, not locked or prunable, no sequencer in
    /// progress, HEAD equal to the landed sha, a symbolic ref, and a clean tracked tree/index.
    /// </summary>
    Task<VerificationWorkspaceValidation> ValidateAsync(
        VerificationCreationCoordinates coordinates, string landedSha, CancellationToken ct);

    /// <summary>What the creation metadata and git state say, without changing anything.</summary>
    Task<VerificationWorkspaceInspection> InspectAsync(string worktreePath, CancellationToken ct);

    /// <summary>The producer's own `restoration.json`, as exact bytes, or null when absent.</summary>
    Task<byte[]?> ReadRestorationAsync(
        string commonGitDirectory, Guid sourceOperationId, Guid taskId, CancellationToken ct);

    /// <summary>Guarded removal: no force, no recursion, unknown files and dirty trees refuse.</summary>
    Task<VerificationWorkspaceRemoval> RemoveAsync(
        VerificationCreationCoordinates coordinates, string expectedSha,
        IReadOnlyList<string> expectedOutputs, CancellationToken ct);
}

public sealed record VerificationWorkspaceCreation(VerificationCreationCoordinates Coordinates, string InitialSha);

public sealed record VerificationWorkspaceValidation(bool Valid, string? Reason);

public sealed record VerificationWorkspaceInspection(
    Guid? CreationId, string? InitialSha, string? Branch, string? RepositoryPath,
    string? WorktreePath, string? GitDirectory, string? Head, bool Registered, bool Clean, bool Locked);

public sealed record VerificationWorkspaceRemoval(
    bool Unregistered, bool DirectoryGone, bool BranchDeleted, string? Residue);

/// <summary>Resolves the workspace a task's snapshot actually lives in, by the task's runner.</summary>
public interface IVerificationWorkspaceDirectory
{
    IVerificationWorkspace Resolve(string? runnerId);
}
