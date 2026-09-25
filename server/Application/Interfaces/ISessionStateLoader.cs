using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>One statement snapshot of bounded session metadata, never transcript bodies.</summary>
public interface ISessionStateLoader
{
    Task<IReadOnlyDictionary<Guid, SessionStateSnapshot>> LoadAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct);
    Task<IReadOnlySet<Guid>> LoadPinnedIdsAsync(CancellationToken ct);
}
