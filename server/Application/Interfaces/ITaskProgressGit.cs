using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>Typed Git I/O for completion attribution. Errors are never absence.</summary>
public interface ITaskProgressGit
{
    Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct);
    Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct);
    Task<string> CommonDirectoryAsync(string repository, CancellationToken ct);
    Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct);
    Task<ProgressRevParse> RevParseCommitAsync(string repository, string revision, CancellationToken ct);
    Task<ProgressSymbolicHead> SymbolicHeadAsync(string repository, CancellationToken ct);
    Task<string?> EndpointFingerprintAsync(string repository, CancellationToken ct);
    Task<bool> HasOriginAsync(string repository, CancellationToken ct);
    Task<ProgressRemoteObservation> ObserveExactRefAsync(
        string repository, string fullRef, string? expectedFingerprint, Guid taskId, CancellationToken ct);
    Task<bool?> IsAncestorAsync(string repository, string ancestorSha, string descendantSha, CancellationToken ct);
    Task<ProgressPinResult> PinBaselineAsync(string repository, Guid taskId, string name, string sha, CancellationToken ct);
    Task<IReadOnlyList<string>> ListProgressPinsAsync(string repository, Guid taskId, CancellationToken ct);
}
