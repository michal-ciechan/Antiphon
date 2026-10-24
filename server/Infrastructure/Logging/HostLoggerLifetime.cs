namespace Antiphon.Server.Infrastructure.Logging;

/// <summary>
/// Owns the exact configured bootstrap logger through host disposal. Serilog's
/// reload integration leaves this lifetime to its caller; an entry-point finally
/// can still be pending after WebApplicationFactory has disposed the host.
/// </summary>
public sealed class HostLoggerLifetime(IDisposable logger) : IDisposable
{
    private readonly object _gate = new();
    private IDisposable? _logger = logger;

    public void Dispose()
    {
        // Both DI disposal and entry-point cleanup may arrive here. The second
        // caller must wait for the first flush, rather than merely observe a flag.
        lock (_gate)
        {
            var owned = _logger;
            _logger = null;
            owned?.Dispose();
        }
    }
}
