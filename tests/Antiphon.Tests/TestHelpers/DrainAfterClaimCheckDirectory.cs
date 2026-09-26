using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0727 MS-5. Forwards the directory and, after the first <see cref="ResolveForNewWork"/>
/// for <c>server2</c> returns, drains that id with no redirect. A claim check that still uses
/// <see cref="ISessionRunnerDirectory.Resolve"/> never arms the drain, so remote prep still mirrors.
/// </summary>
internal sealed class DrainAfterClaimCheckDirectory : ISessionRunnerDirectory
{
    private readonly PhoneHomeRunnerDirectory _inner;
    private int _armed;

    public DrainAfterClaimCheckDirectory(PhoneHomeRunnerDirectory inner) => _inner = inner;

    public ISessionRunnerClient Local => _inner.Local;

    public ISessionRunnerClient Resolve(string? runnerId) => _inner.Resolve(runnerId);

    public ISessionRunnerClient ResolveForNewWork(string? runnerId)
    {
        var client = ((ISessionRunnerDirectory)_inner).ResolveForNewWork(runnerId);
        if (string.Equals(runnerId, RollingRunnerSettings.Server2, StringComparison.Ordinal)
            && Interlocked.Exchange(ref _armed, 1) == 0)
        {
            _inner.ApplyState(RollingRunnerSettings.Server2, new RunnerState(
                Draining: true,
                DrainedAt: DateTimeOffset.UtcNow,
                DrainReason: "after claim check",
                RedirectTo: null,
                RetireWhenIdle: false,
                IdleObservedAt: null,
                RetiredAt: null,
                RetireReason: null));
        }

        return client;
    }

    public RunnerState? DrainState(string? runnerId) => _inner.DrainState(runnerId);

    public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
        _inner.GetOwnerAsync(sessionId, ct);

    public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
        _inner.GetBindingAsync(sessionId, ct);

    public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
        _inner.GetInventoryAsync(runnerId, ct);

    public IReadOnlyList<string> KnownRunnerIds => _inner.KnownRunnerIds;

    public Guid? GetLiveStoreId(string? runnerId) => _inner.GetLiveStoreId(runnerId);

    public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct) =>
        _inner.DescribeAsync(runnerId, ct);

    public int? DeclaredCapacity(string runnerId) => _inner.DeclaredCapacity(runnerId);

    public IReadOnlyCollection<Guid> LiveRemoteSessionIds() => _inner.LiveRemoteSessionIds();

    public IReadOnlyCollection<Guid> UnknownRemoteSessionIds() => _inner.UnknownRemoteSessionIds();

    public bool RemoteInventoryPending(string? runnerId) => _inner.RemoteInventoryPending(runnerId);
}
