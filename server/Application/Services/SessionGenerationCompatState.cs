using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0502 D-5: one compatibility report per session per server uptime for unbound
/// legacy exits and runner DTOs that carry no accepted generation.
/// </summary>
public sealed class SessionGenerationCompatState
{
    private readonly ConcurrentDictionary<Guid, byte> _reported = new();

    public bool TryReport(Guid sessionId) => _reported.TryAdd(sessionId, 0);
}
