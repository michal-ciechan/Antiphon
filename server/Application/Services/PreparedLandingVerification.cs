using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>Ephemeral execution input. It is valid only after fresh leased validation of durable authority.</summary>
public sealed record PreparedLandingVerification(
    RepositoryVerificationIdentity Identity, string Repository, string Common, string LandGitDirectory,
    string TargetBeforeSha, LandingDestination Destination, string? Filter, string? ExpectedSourceSha,
    Guid? ReviewEvidenceId, LandRecoveryMode RecoveryMode, Guid? RecoverySourceTaskId,
    string RecoveryRefPrefix, string[] SourceRegistrations, string? SourceCanonical);

internal sealed class LandRepositoryLeaseScope(IRepositoryMutationLease leases, ILandingGit git,
    string repository, RepositoryLeaseOwnerTag owner, RepositoryLease initial) : IAsyncDisposable
{
    private RepositoryLease? _lease = initial;
    private ILandingOperationScope? _scope = git.BeginOperationScope();
    private readonly LandingGitProfile _finished = new();
    public RepositoryLease Lease => _lease ?? throw new InvalidOperationException("repository_lease_released");
    /// <summary>The live lease, or null while it is released (verification window, or after disposal).</summary>
    public RepositoryLease? Held => _lease;
    public LandingGitProfile Profile
    {
        get
        {
            var sum = new LandingGitProfile();
            sum.Add(_finished);
            if (_scope is not null) sum.Add(_scope.Profile);
            return sum;
        }
    }
    public async Task ReleaseAsync()
    {
        if (_scope is not null)
        {
            _scope.Dispose();
            _finished.Add(_scope.Profile);
            _scope = null;
        }
        var released = _lease;
        _lease = null;
        if (released is not null) await released.DisposeAsync();
    }
    public async Task ReacquireAsync(CancellationToken ct)
    {
        if (_lease is not null) throw new InvalidOperationException("repository_lease_already_owned");
        while ((_lease = await leases.TryAcquireAsync(repository, owner, ct)) is null)
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
    }
    // Begin synchronously in the resumed caller's execution context: AsyncLocal cache
    // assignments inside ReacquireAsync would not flow back across its await boundary.
    public void BeginReadScope()
    {
        if (_lease is null || _scope is not null) throw new InvalidOperationException("repository_scope_boundary_invalid");
        _scope = git.BeginOperationScope();
    }
    public async ValueTask DisposeAsync() => await ReleaseAsync();
}
