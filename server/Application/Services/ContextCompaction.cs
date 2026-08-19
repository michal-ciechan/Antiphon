using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Resolves the three per-agent auto-compact overrides against <see cref="ContextCompactionSettings"/>.
/// Null on the Agent column means "use the global"; a stored value wins. CARD-0082 S2.
/// </summary>
public static class ContextCompaction
{
    /// <summary>
    /// Per-agent overrides apply only when <paramref name="agent"/> is non-null. An eligible
    /// session nobody claims (CARD-0056 re-adoption; PersistentSessionId points elsewhere or
    /// nowhere) uses <paramref name="settings"/> globally — there is no Agent row to read from.
    /// </summary>
    public static ResolvedContextCompaction Resolve(ContextCompactionSettings settings, Agent? agent)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return agent is null
            ? new ResolvedContextCompaction(settings.Enabled, settings.IdleMinutes, settings.ContextPercent)
            : settings.Resolve(
                agent.AutoCompactEnabled,
                agent.AutoCompactIdleMinutes,
                agent.AutoCompactContextPercent);
    }
}
