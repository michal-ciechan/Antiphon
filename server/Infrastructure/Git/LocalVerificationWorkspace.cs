using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// CARD-0604 D-19 (Cut B). The desktop half of <see cref="IVerificationWorkspace"/>. Every call
/// is the same <see cref="IWorktreeManager"/>/<see cref="ILandingGit"/> work the Windows lane has
/// always done -- this type exists so the remote half has something to be the other of, and so
/// the caller can stop knowing which one it is holding.
///
/// Creation and removal still go through the repository lease, because on the desktop the
/// repository is shared with every other worktree operation. On the runner it is not.
/// </summary>
public sealed class LocalVerificationWorkspace(
    IWorktreeManager worktrees, IRepositoryMutationLease leases, IWorktreeRemovalEvidence evidence)
    : IVerificationWorkspace
{
    public async Task<VerificationWorkspaceCreation> CreateAsync(
        string repositoryPath, string identifier, string landedSha, CancellationToken ct)
    {
        await using var lease = await leases.TryAcquireAsync(repositoryPath, ct)
            ?? throw new Application.Exceptions.ConflictException("repository_lease_required");
        var info = await worktrees.CreateVerificationAsync(repositoryPath, identifier, landedSha, lease, ct);
        var creation = await worktrees.ReadVerificationCreationAsync(info.Path, ct)
            ?? throw new Application.Exceptions.ConflictException("verification_creation_identity_mismatch");
        return new(new(creation.RepositoryPath, CommonDirectoryOf(creation), creation.WorktreePath,
            creation.GitDirectory, creation.Branch, creation.CreationId), creation.InitialSha);
    }

    public async Task<VerificationWorkspaceValidation> ValidateAsync(
        VerificationCreationCoordinates coordinates, string landedSha, CancellationToken ct)
    {
        var creation = await worktrees.ReadVerificationCreationAsync(coordinates.WorktreePath, ct);
        if (creation is null || creation.CreationId != coordinates.CreationId
            || creation.InitialSha != landedSha || creation.Branch != coordinates.Branch)
            return new(false, "verification_creation_identity_mismatch");
        return new(true, null);
    }

    public async Task<VerificationWorkspaceInspection> InspectAsync(string worktreePath, CancellationToken ct)
    {
        var creation = await worktrees.ReadVerificationCreationAsync(worktreePath, ct);
        return new(creation?.CreationId, creation?.InitialSha, creation?.Branch, creation?.RepositoryPath,
            creation?.WorktreePath ?? worktreePath, creation?.GitDirectory, creation?.InitialSha,
            creation is not null, true, false);
    }

    public Task<byte[]?> ReadRestorationAsync(
        string commonGitDirectory, Guid sourceOperationId, Guid taskId, CancellationToken ct)
    {
        var path = Path.Combine(commonGitDirectory, "antiphon", "verification",
            sourceOperationId.ToString("N"), taskId.ToString("N"), "restoration.json");
        if (!File.Exists(path)) return Task.FromResult<byte[]?>(null);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new Application.Exceptions.ConflictException("verification_restoration_is_a_link");
        return File.ReadAllBytesAsync(path, ct).ContinueWith(t => (byte[]?)t.Result, ct,
            TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    /// <summary>
    /// Desktop removal stays on <see cref="IWorktreeManager.TryRemoveAsync"/>, which is where the
    /// guarded-removal rules and their durable start record already live. This seam does not
    /// re-implement them; <see cref="VerificationCleanupService"/> keeps calling it directly for
    /// a local task and uses this type only for the reads above.
    /// </summary>
    public Task<VerificationWorkspaceRemoval> RemoveAsync(
        VerificationCreationCoordinates coordinates, string expectedSha,
        IReadOnlyList<string> expectedOutputs, CancellationToken ct) =>
        throw new NotSupportedException(
            "Local verification removal goes through IWorktreeManager.TryRemoveAsync with its durable start record.");

    private static string CommonDirectoryOf(VerificationWorktreeCreation creation) =>
        Path.GetFullPath(Path.Combine(creation.RepositoryPath, ".git"));
}
