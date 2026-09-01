using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// External OS-read seam for CARD-0298. Production uses WMI; tests inject fixture processes.
/// A failed snapshot must throw, never return an empty list as a stand-in for "could not read".
/// </summary>
public interface IZombieProcessCensus
{
    Task<IReadOnlyList<ZombieOsProcess>> SnapshotAsync(CancellationToken cancellationToken);
}
