using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain;

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
    Task<ProgressRemoteObservation> ObserveExactRefUnderLeaseAsync(
        string repository, string fullRef, string? expectedFingerprint, Guid taskId,
        RepositoryLease lease, CancellationToken ct) =>
        ObserveExactRefAsync(repository, fullRef, expectedFingerprint, taskId, ct);
    Task<bool?> IsAncestorAsync(string repository, string ancestorSha, string descendantSha, CancellationToken ct);

    /// <summary>
    /// CARD-1805 (A-3). Whether one exact full object id is in the repository: true present, false
    /// absent (<c>git cat-file -e</c> exits 1), null for any other answer. Exit 128 is an unreadable
    /// repository as well as a bad name, so it is never absence.
    /// </summary>
    async Task<bool?> HasObjectAsync(string repository, string sha, CancellationToken ct)
    {
        if (!GitObjectId.IsFull(sha))
            return null;
        var result = await RunAsync(repository, ["cat-file", "-e", sha], ct);
        return result.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => null,
        };
    }

    /// <summary>
    /// CARD-0613 D-6. The COMMITTER time of one exact full object id, in UTC. Never the author
    /// date, never a scan of recent history, and never a substitute for ancestry: it is the
    /// operational lower/upper bound on a candidate whose lineage left the baseline.
    /// </summary>
    Task<ProgressCommitTime> CommitTimeAsync(string repository, string sha, CancellationToken ct);
    Task<ProgressPinResult> PinBaselineAsync(string repository, Guid taskId, string name, string sha, CancellationToken ct);
    Task<IReadOnlyList<string>> ListProgressPinsAsync(string repository, Guid taskId, CancellationToken ct);
}
