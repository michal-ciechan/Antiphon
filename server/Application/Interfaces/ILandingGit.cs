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
    Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct);
    Task<LandingRemoteObservation> ObserveRetirementAsync(string repository, LandingDestination destination,
        string sourceSha, Guid retirementId, string pinName, CancellationToken ct)
        => Task.FromResult(new LandingRemoteObservation(null, false, "retirement_observation_not_implemented"));
    Task<LandingGitResult> PinRetirementAsync(string repository, Guid retirementId, string pinName, string sha, CancellationToken ct)
        => Task.FromResult(new LandingGitResult(1, "", "retirement_pin_not_implemented"));
    Task<LandingGitResult> DeleteRetirementPinAsync(string repository, Guid retirementId, string pinName, string expectedSha, CancellationToken ct)
        => Task.FromResult(new LandingGitResult(1, "", "retirement_pin_not_implemented"));
    Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct);
    Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha,
        Func<int, long, CancellationToken, Task> started, CancellationToken ct);
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
