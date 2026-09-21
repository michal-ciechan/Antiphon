namespace Antiphon.Server.Application.Services;

/// <summary>
/// Process-wide discovery cadence for CARD-0079. Reconciliation of an episode
/// that already owns the seat is not gated; a new look at silent seats is.
/// </summary>
public sealed class CheckCompactionContinuationGate
{
    private readonly object _gate = new();
    private DateTime _lastDiscoveryUtc = DateTime.MinValue;

    public bool TryEnterDiscovery(DateTime nowUtc)
    {
        var utc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        lock (_gate)
        {
            if (utc - _lastDiscoveryUtc < TimeSpan.FromMinutes(1))
                return false;
            _lastDiscoveryUtc = utc;
            return true;
        }
    }
}
