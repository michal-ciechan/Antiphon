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
    /// <summary>CARD-0642 R1: always a fresh listing, never an operation-scope cache hit. Another process
    /// switching an existing worktree onto a branch moves no registration stamp, so the checkout decision
    /// at a target-ref mutation boundary reads this.</summary>
    Task<IReadOnlyList<LandingRegistration>> LiveRegistrationsAsync(string repository, CancellationToken ct)
        => RegistrationsAsync(repository, ct);
    Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct);
    /// <summary>CARD-0642 D-5: <see cref="LandInspectionScope.IdentityAndStatus"/> skips the ignored listing and
    /// returns empty <see cref="LandSourceSnapshot.IgnoredPaths"/>; the dirty check is unchanged.</summary>
    Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, LandInspectionScope scope, CancellationToken ct)
        => InspectAsync(coordinates, ct);
    /// <summary>CARD-0642 D-4: read caches bounded by the caller (a land holds the repository lease for the
    /// scope's lifetime). A nested call borrows the live scope.</summary>
    ILandingOperationScope BeginOperationScope() => LandingOperationScope.None;
    Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct);
    Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination,
        string sourceSha, string observationRef, CancellationToken ct);
    Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef,
        string observationPrefix, CancellationToken ct);
    /// <summary>CARD-0642 D-6 / CARD-0688 D-8: the one-round-trip remote source recheck before each mutation.
    /// Fail-closed default: an implementor that cannot answer refuses, never reports absence.</summary>
    Task<LandingSourceRecheck> RecheckSourceRemoteAsync(string repository, string sourceFullRef, string expectedSha,
        string expectedFingerprint, CancellationToken ct)
        => Task.FromResult(new LandingSourceRecheck(null, null, "source_recheck_unsupported"));
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
    /// <summary>Only reviewed source adoption may replace an owner branch; target publication uses PushOwnedAsync.</summary>
    Task<LandingGitResult> PushSourceOwnedAsync(string repository, string sourceFullRef, string sha,
        string? expectedRemoteSha, string expectedFingerprint,
        Func<int, long, CancellationToken, Task> started, CancellationToken ct)
        => Task.FromResult(new LandingGitResult(1, "", "source_push_unsupported"));
    Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct);
}

/// <summary>CARD-0642 D-4/D-7: one land's read-cache lifetime and its git profile.</summary>
public interface ILandingOperationScope : IDisposable
{
    LandingGitProfile Profile { get; }
}

public static class LandingOperationScope
{
    /// <summary>No cache; a fresh profile nothing records into.</summary>
    public static ILandingOperationScope None => new NoScope();

    private sealed class NoScope : ILandingOperationScope
    {
        public LandingGitProfile Profile { get; } = new();
        public void Dispose() { }
    }
}
