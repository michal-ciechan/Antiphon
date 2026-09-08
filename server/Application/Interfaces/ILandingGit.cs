using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>Typed, shell-free I/O for the landing protocol. Errors are never absence.</summary>
public interface ILandingGit
{
    Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct);
    Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct);
    Task<string> CommonDirectoryAsync(string repository, CancellationToken ct);
    Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct);
    Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct);
    Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination,
        string sourceSha, string observationRef, CancellationToken ct);
    Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct);
    Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct);
}
