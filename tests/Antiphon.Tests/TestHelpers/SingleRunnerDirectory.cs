using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0604 D-19. A directory with exactly one runner, for the in-memory worlds that used to
/// hand <see cref="Antiphon.Server.Application.Services.SourceLandingAdmission"/> a bare client.
///
/// Custody support, custody reads and verification removal are now resolved through the TASK's
/// runner id rather than through whichever client happens to be local (G-31, G-33), so a world
/// that wires only a client cannot construct those services at all. This says "there is one
/// runner and everything resolves to it", which is exactly the shape those worlds model: a
/// desktop with a local runner and no phone-home peer.
///
/// <para><paramref name="remoteRunnerId"/> lets a test model a bound REMOTE runner: any other id
/// is unavailable, so the 503 path can be exercised without a live socket.</para>
/// </summary>
internal sealed class SingleRunnerDirectory(
    ISessionRunnerClient client, string? remoteRunnerId = null) : ISessionRunnerDirectory
{
    public ISessionRunnerClient Local { get; } = client;

    public List<string?> Resolved { get; } = [];

    public ISessionRunnerClient Resolve(string? runnerId)
    {
        Resolved.Add(runnerId);
        if (string.IsNullOrWhiteSpace(runnerId) || runnerId == "local") return Local;
        if (remoteRunnerId is not null && runnerId == remoteRunnerId) return Local;
        throw new Antiphon.Server.Application.Exceptions.ServiceUnavailableException(
            $"Runner '{runnerId}' is unavailable in this world.",
            Antiphon.SessionRunner.Contracts.PhoneHomeProblemTypes.Unavailable);
    }

    public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult<SessionRunnerOwner?>(null);

    public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);

    public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
        Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("no inventory in this world"));

    public IReadOnlyList<string> KnownRunnerIds => remoteRunnerId is null ? [] : [remoteRunnerId];

    public Guid? LiveStoreId => null;
}
