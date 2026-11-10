using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// Off-Windows census. A snapshot throws; it is never an empty process list.
/// </summary>
public sealed class ZombieCensusUnsupportedPlatform : IZombieProcessCensus
{
    public Task<IReadOnlyList<ZombieOsProcess>> SnapshotAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException(ZombieCensusSettingsValidator.OffWindowsOmission);
}
