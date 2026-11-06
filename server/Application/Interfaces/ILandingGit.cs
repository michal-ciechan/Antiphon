using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>CARD-1076 D-1: optional budget and journal tag for one git command.</summary>
public sealed record LandingGitRunOptions(TimeSpan? Budget = null, RepositoryChildTag? Child = null);

/// <summary>Task and purpose stored on a mutating git child's journal record.</summary>
public sealed record RepositoryChildTag(Guid? TaskId, string Purpose, Guid? WindowId = null);

/// <summary>Purpose strings a child journal may carry. The reader, not the writer, decides which stay off the lease fence.</summary>
public static class RepositoryChildPurposes
{
    public const string RemotePrepPush = "remote-prep-push";
}

/// <summary>Typed, shell-free I/O for the landing protocol. Errors are never absence.</summary>
public interface ILandingGit
{
    Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct);

    /// <summary>CARD-1076 D-1. Forwards to the three-argument overload so existing fakes keep working.</summary>
    Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, LandingGitRunOptions options, CancellationToken ct)
        => RunAsync(repository, arguments, ct);
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
    /// <summary>Fail closed for doubles that do not implement recovery inspection.</summary>
    Task<LandRecoveryCheckoutInspection> InspectRecoveryCheckoutAsync(LandSourceCoordinates coordinates,
        string oldSha, string expectedSha, CancellationToken ct)
        => Task.FromResult(LandRecoveryCheckoutInspection.Refused());
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
    /// <summary>
    /// The source recheck (D-8) and the target observation of one pre-push boundary. The default makes the two
    /// separate reads, which keeps every double's command shapes; the real implementation reads both refs with
    /// one <c>ls-remote</c>. The target is observed only when the source recheck accepted
    /// <paramref name="expectedSourceSha"/>, so a refusal on the source never reads the target.
    /// </summary>
    Task<LandingPrePushObservation> ObserveSourceAndTargetAsync(string repository, string sourceFullRef,
        string expectedSourceSha, string expectedFingerprint, LandingDestination destination, string ancestrySha,
        string observationRef, CancellationToken ct)
        => ObserveSourceAndTargetAsync(repository, sourceFullRef, expectedSourceSha, expectedFingerprint, destination,
            ancestrySha, observationRef, null, ct);
    /// <summary>
    /// Runs the local source fence after accepting the remote source and before observing or pinning the target.
    /// A fence refusal propagates without target work; a remote source refusal never calls the fence.
    /// </summary>
    async Task<LandingPrePushObservation> ObserveSourceAndTargetAsync(string repository, string sourceFullRef,
        string expectedSourceSha, string expectedFingerprint, LandingDestination destination, string ancestrySha,
        string observationRef, Func<Task>? beforeTarget, CancellationToken ct)
    {
        var source = await RecheckSourceRemoteAsync(repository, sourceFullRef, expectedSourceSha, expectedFingerprint, ct);
        if (!source.Accepted || source.Sha != expectedSourceSha || source.Fingerprint != expectedFingerprint)
            return new(source, null);
        if (beforeTarget is not null) await beforeTarget();
        return new(source, await ObserveAsync(repository, destination, ancestrySha, observationRef, ct));
    }
    /// <summary>
    /// CARD-1432 G1: the object ids of <paramref name="fullRefs"/>. The result holds every ref read before the first
    /// unreadable one (missing or broken), in request order, so a caller checking refs in order refuses on the first
    /// absent entry. The default reads one ref per process, which keeps every fake's command shapes; a real
    /// implementation reads them in one process.
    /// </summary>
    async Task<IReadOnlyDictionary<string, string>> ReadRefsAsync(string repository, IReadOnlyList<string> fullRefs, CancellationToken ct)
    {
        var shas = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fullRef in fullRefs)
        {
            var read = await RunAsync(repository, ["show-ref", "--verify", "--hash", fullRef], ct);
            var sha = read.Output.Trim();
            if (!read.Succeeded || !Antiphon.Server.Domain.GitObjectId.IsFull(sha)) break;
            shas[fullRef] = sha;
        }
        return shas;
    }
    Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct);
    Task<LandingRemoteObservation> ObserveRetirementAsync(string repository, LandingDestination destination,
        string sourceSha, Guid retirementId, string pinName, CancellationToken ct)
        => Task.FromResult(new LandingRemoteObservation(null, false, "retirement_observation_not_implemented"));
    Task<LandingGitResult> PinRetirementAsync(string repository, Guid retirementId, string pinName, string sha, CancellationToken ct)
        => Task.FromResult(new LandingGitResult(1, "", "retirement_pin_not_implemented"));
    Task<LandingGitResult> DeleteRetirementPinAsync(string repository, Guid retirementId, string pinName, string expectedSha, CancellationToken ct)
        => Task.FromResult(new LandingGitResult(1, "", "retirement_pin_not_implemented"));
    /// <summary>CARD-1432 D-3: deletes <c>refs/antiphon/{land,progress}/</c> refs in one <c>update-ref --stdin</c>
    /// transaction with old-value checks; any other ref refuses the whole batch before git runs.</summary>
    Task<LandingGitResult> UpdateRefsAsync(string repository, IReadOnlyList<(string Ref, string Oid)> deletes, CancellationToken ct)
        => Task.FromResult(new LandingGitResult(1, "", "update_refs_not_implemented"));
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

    /// <summary>
    /// CARD-0824 S2 (B-2). Deletes <paramref name="sourceFullRef"/> on origin with
    /// <c>--force-with-lease=&lt;ref&gt;:&lt;expectedRemoteSha&gt;</c> after the endpoint fingerprint check. Fail-closed default:
    /// an implementor that cannot delete under a lease refuses.
    /// </summary>
    Task<LandingGitResult> DeleteSourceBranchAsync(string repository, string sourceFullRef, string expectedRemoteSha,
        string expectedFingerprint, CancellationToken ct)
        => Task.FromResult(new LandingGitResult(1, "", "delete_unsupported"));

    /// <summary>
    /// CARD-0824 S2. A fresh read (never a cached observation) of whether origin's <paramref name="targetFullRef"/>
    /// contains <paramref name="sha"/>. Fail-closed default: unsupported is never containment.
    /// </summary>
    Task<LandingRemoteObservation> ConfirmTargetContainsAsync(string repository, string targetFullRef, string sha,
        string expectedFingerprint, CancellationToken ct)
        => Task.FromResult(new LandingRemoteObservation(null, false, "containment_unsupported"));

    /// <summary>
    /// CARD-0824 S2 (B-3 step 7). Deletes the local <c>refs/remotes/origin/&lt;b&gt;</c> with its old value when it is at,
    /// or behind, <paramref name="expectedSha"/>; absent is success. Never touches <c>refs/heads/&lt;b&gt;</c>.
    /// </summary>
    Task<LandingGitResult> DeleteTrackingRefAsync(string repository, string sourceFullRef, string expectedSha, CancellationToken ct)
        => Task.FromResult(new LandingGitResult(1, "", "tracking_delete_unsupported"));
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
