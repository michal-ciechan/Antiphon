using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>CARD-1884. Ranking is not implemented in this slice. The witness tests fail closed.</summary>
internal sealed class HostTopConsumerTracker
{
    public IReadOnlyList<HostTopConsumerSample> Sample(IHostProcessTable? table, TimeProvider time)
    {
        if (table is null) return [];
        return [];
    }
}
