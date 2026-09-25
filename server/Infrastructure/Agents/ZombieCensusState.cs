using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>The last successful, report-only census for this server lifetime (CARD-0691).</summary>
public sealed class ZombieCensusState
{
    private ZombieCensusResult? _latest;

    public ZombieCensusResult? Latest => Volatile.Read(ref _latest);

    // Publish the result and its GeneratedAtUtc together, so a reader cannot mix two runs.
    public void Publish(ZombieCensusResult result) => Interlocked.Exchange(ref _latest, result);
}
