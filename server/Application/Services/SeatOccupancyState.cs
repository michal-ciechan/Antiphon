using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>CARD-1079: the snapshot the sampler publishes and attention will read.</summary>
public sealed class SeatOccupancyState
{
    private SeatOccupancySnapshot _current = SeatOccupancySnapshot.Empty;

    public SeatOccupancySnapshot Current => Volatile.Read(ref _current);

    public void Publish(SeatOccupancySnapshot snapshot) => Interlocked.Exchange(ref _current, snapshot);
}
