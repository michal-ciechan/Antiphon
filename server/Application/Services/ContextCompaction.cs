using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Resolves the three per-agent auto-compact overrides against <see cref="ContextCompactionSettings"/>.
/// Null on the Agent column means "use the global"; a stored value wins. CARD-0082 S2.
/// </summary>
public static class ContextCompaction
{
    public static ResolvedContextCompaction Resolve(ContextCompactionSettings settings, Agent agent)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(agent);
        return settings.Resolve(
            agent.AutoCompactEnabled,
            agent.AutoCompactIdleMinutes,
            agent.AutoCompactContextPercent);
    }
}
