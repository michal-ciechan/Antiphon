using System.Collections.Concurrent;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1430 V-5. One ordered log shared by a recording repository mutation lease and a recording
/// progress git, so a test can say which came first: the dispatcher's release of R or the
/// progress-baseline capture. Both recorders forward to the production implementations against a
/// real scratch repository; nothing here fakes git behaviour.
/// </summary>
internal sealed class LeaseSpanLog
{
    public const string DispatchAcquired = "lease:acquired:" + RepositoryLeasePurposes.Dispatch;
    public const string DispatchReleased = "lease:released:" + RepositoryLeasePurposes.Dispatch;
    public const string DispatchOwns = "owns:" + RepositoryLeasePurposes.Dispatch + ":";
    public const string Pin = "progress:pin:";
    public const string CaptureHeadRead = "progress:rev-parse:HEAD";

    private readonly ConcurrentQueue<string> _events = new();

    public IReadOnlyList<string> Events => _events.ToArray();

    public string Trace => string.Join(" > ", Events);

    public void Add(string entry) => _events.Enqueue(entry);

    /// <summary>Index of the first event starting with <paramref name="prefix"/>, or -1.</summary>
    public int IndexOf(string prefix)
    {
        var events = Events;
        for (var i = 0; i < events.Count; i++)
            if (events[i].StartsWith(prefix, StringComparison.Ordinal)) return i;
        return -1;
    }

    public int Count(string prefix) => Events.Count(e => e.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// Replaces the provider's lease and progress git with recording decorators over the production
    /// <see cref="RepositoryMutationLease"/> and <see cref="TaskProgressGit"/>, wired as production
    /// wires them (the progress git takes the lease provider for its own pin and fetch acquires).
    /// </summary>
    public static void Install(IServiceCollection services, LeaseSpanLog log,
        Action<RecordingTaskProgressGit>? configureProgressGit = null)
    {
        services.RemoveAll<IRepositoryMutationLease>();
        services.AddSingleton<IRepositoryMutationLease>(sp =>
            new RecordingRepositoryLease(new RepositoryMutationLease(sp.GetRequiredService<ILandingGit>()), log));
        services.RemoveAll<ITaskProgressGit>();
        services.AddSingleton<ITaskProgressGit>(sp =>
        {
            var git = new RecordingTaskProgressGit(
                new TaskProgressGit(sp.GetRequiredService<IRepositoryMutationLease>()), log);
            configureProgressGit?.Invoke(git);
            return git;
        });
    }
}

/// <summary>
/// Forwards every acquire to the inner provider and hands back a wrapper whose first disposal is
/// logged as <c>lease:released:&lt;purpose&gt;</c>; ownership checks are logged with their answer.
/// The inner provider keeps the OS lock, the journal and the owner ledger.
/// </summary>
internal sealed class RecordingRepositoryLease(IRepositoryMutationLease inner, LeaseSpanLog log) : IRepositoryMutationLease
{
    public bool SupportsVerificationWindows => inner.SupportsVerificationWindows;

    public bool AllowsWorktree(RepositoryLease lease, string path, string branch) =>
        inner.AllowsWorktree(Unwrap(lease), path, branch);

    public Task<RepositoryVerificationWindow?> OpenVerificationWindowAsync(RepositoryLease lease,
        IAsyncDisposable executionLease, RepositoryVerificationIdentity identity, CancellationToken ct) =>
        inner.OpenVerificationWindowAsync(Unwrap(lease), executionLease, identity, ct);

    public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) =>
        WrapAsync("untagged", inner.TryAcquireAsync(repository, ct));

    public Task<RepositoryLease?> TryAcquireAsync(string repository, RepositoryLeaseOwnerTag owner, CancellationToken ct) =>
        WrapAsync(owner.Purpose, inner.TryAcquireAsync(repository, owner, ct));

    public Task<RepositoryLease?> TryAcquirePreflightAsync(string repository, RepositoryPreflight preflight,
        RepositoryLeaseOwnerTag owner, CancellationToken ct) =>
        WrapAsync(owner.Purpose, inner.TryAcquirePreflightAsync(repository, preflight, owner, ct));

    public bool Owns(RepositoryLease lease, string commonDirectory)
    {
        var owns = inner.Owns(Unwrap(lease), commonDirectory);
        log.Add($"owns:{PurposeOf(lease)}:{(owns ? "true" : "false")}");
        return owns;
    }

    public Task<string?> DescribeUnavailableAsync(string repository, CancellationToken ct) =>
        inner.DescribeUnavailableAsync(repository, ct);

    public Task<RepositoryLeaseOwner?> FindOwnerAsync(string repository, CancellationToken ct) =>
        inner.FindOwnerAsync(repository, ct);

    private async Task<RepositoryLease?> WrapAsync(string purpose, Task<RepositoryLease?> acquire)
    {
        var lease = await acquire;
        if (lease is null)
        {
            log.Add($"lease:refused:{purpose}");
            return null;
        }
        log.Add($"lease:acquired:{purpose}");
        return new Recorded(lease, purpose, log);
    }

    private static RepositoryLease Unwrap(RepositoryLease lease) => lease is Recorded recorded ? recorded.Inner : lease;

    private static string PurposeOf(RepositoryLease lease) => lease is Recorded recorded ? recorded.Purpose : "foreign";

    private sealed class Recorded(RepositoryLease inner, string purpose, LeaseSpanLog log) : RepositoryLease
    {
        private int _disposed;

        public RepositoryLease Inner => inner;
        public string Purpose => purpose;
        public override string CommonDirectory => inner.CommonDirectory;

        public override async ValueTask DisposeAsync()
        {
            log.Add(Interlocked.Exchange(ref _disposed, 1) == 0
                ? $"lease:released:{purpose}"
                : $"lease:disposed-again:{purpose}");
            await inner.DisposeAsync();
        }
    }
}

/// <summary>
/// Forwards to the production progress git and logs the calls the baseline capture makes:
/// <c>progress:rev-parse:&lt;revision&gt;</c>, <c>progress:observe:&lt;ref&gt;</c> and
/// <c>progress:pin:&lt;name&gt;</c>. <see cref="FailRevParse"/> makes a chosen HEAD read answer as
/// git does for an unreadable commit, without touching the repository.
/// </summary>
internal sealed class RecordingTaskProgressGit(ITaskProgressGit inner, LeaseSpanLog log) : ITaskProgressGit
{
    /// <summary>(repository, revision) => true to fail that rev-parse with repair_source_identity_unavailable.</summary>
    public Func<string, string, bool>? FailRevParse { get; set; }

    public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct) =>
        inner.RunAsync(repository, arguments, ct);

    public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => inner.CanonicalDirectoryAsync(path, ct);

    public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => inner.CommonDirectoryAsync(repository, ct);

    public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) =>
        inner.RegistrationsAsync(repository, ct);

    public Task<ProgressRevParse> RevParseCommitAsync(string repository, string revision, CancellationToken ct)
    {
        log.Add($"progress:rev-parse:{revision}");
        if (FailRevParse?.Invoke(repository, revision) == true)
            return Task.FromResult(new ProgressRevParse(false, null, "repair_source_identity_unavailable"));
        return inner.RevParseCommitAsync(repository, revision, ct);
    }

    public Task<ProgressSymbolicHead> SymbolicHeadAsync(string repository, CancellationToken ct) => inner.SymbolicHeadAsync(repository, ct);

    public Task<string?> EndpointFingerprintAsync(string repository, CancellationToken ct) => inner.EndpointFingerprintAsync(repository, ct);

    public Task<bool> HasOriginAsync(string repository, CancellationToken ct) => inner.HasOriginAsync(repository, ct);

    public Task<ProgressRemoteObservation> ObserveExactRefAsync(
        string repository, string fullRef, string? expectedFingerprint, Guid taskId, CancellationToken ct)
    {
        log.Add($"progress:observe:{fullRef}");
        return inner.ObserveExactRefAsync(repository, fullRef, expectedFingerprint, taskId, ct);
    }

    public Task<ProgressRemoteObservation> ObserveExactRefUnderLeaseAsync(
        string repository, string fullRef, string? expectedFingerprint, Guid taskId,
        RepositoryLease lease, CancellationToken ct)
    {
        log.Add($"progress:observe-under-lease:{fullRef}");
        return inner.ObserveExactRefUnderLeaseAsync(repository, fullRef, expectedFingerprint, taskId, lease, ct);
    }

    public Task<bool?> IsAncestorAsync(string repository, string ancestorSha, string descendantSha, CancellationToken ct) =>
        inner.IsAncestorAsync(repository, ancestorSha, descendantSha, ct);

    public Task<ProgressCommitTime> CommitTimeAsync(string repository, string sha, CancellationToken ct) =>
        inner.CommitTimeAsync(repository, sha, ct);

    public Task<ProgressPinResult> PinBaselineAsync(string repository, Guid taskId, string name, string sha, CancellationToken ct)
    {
        log.Add($"progress:pin:{name}");
        return inner.PinBaselineAsync(repository, taskId, name, sha, ct);
    }

    public Task<IReadOnlyList<string>> ListProgressPinsAsync(string repository, Guid taskId, CancellationToken ct) =>
        inner.ListProgressPinsAsync(repository, taskId, ct);
}
