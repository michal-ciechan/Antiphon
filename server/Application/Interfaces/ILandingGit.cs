using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>Typed, shell-free I/O for the landing protocol. Errors are never absence.</summary>
public interface ILandingGit
{
    Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct);
    Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments,
        Func<int, long, CancellationToken, Task> started, CancellationToken ct);
    Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct);
    Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct);
    Task<string> CommonDirectoryAsync(string repository, CancellationToken ct);
    Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct);
    Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct);
    Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct);
    Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct);
    Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination,
        string sourceSha, string observationRef, CancellationToken ct);
    Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef,
        string observationPrefix, CancellationToken ct);
    Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct);
    Task<LandingRemoteObservation> ObserveRetirementAsync(string repository, LandingDestination destination,
        string sourceSha, Guid retirementId, string pinName, CancellationToken ct)
        => Task.FromResult(new LandingRemoteObservation(null, false, "retirement_observation_not_implemented"));
    Task<LandingGitResult> PinRetirementAsync(string repository, Guid retirementId, string pinName, string sha, CancellationToken ct)
        => Task.FromResult(new LandingGitResult(1, "", "retirement_pin_not_implemented"));
    Task<LandingGitResult> DeleteRetirementPinAsync(string repository, Guid retirementId, string pinName, string expectedSha, CancellationToken ct)
        => Task.FromResult(new LandingGitResult(1, "", "retirement_pin_not_implemented"));
    /// <summary>
    /// Drops Git's administrative entry for a linked worktree whose directory is already gone, without
    /// handing Git the path (CARD-0665 review 0c0b9a4e item 2). The entry's <c>gitdir</c> must name
    /// <paramref name="worktreePath"/>; the working tree is never read or deleted.
    /// </summary>
    Task<LandingGitResult> UnregisterWorktreeAsync(string repository, string worktreePath, string gitDirectory, CancellationToken ct)
        => Task.FromResult(new LandingGitResult(1, "", "worktree_unregister_not_implemented"));
    Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct);
    Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha,
        Func<int, long, CancellationToken, Task> started, CancellationToken ct);
    Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct);
}
