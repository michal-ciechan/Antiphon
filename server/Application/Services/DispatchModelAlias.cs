using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>The dispatch and optional-specialist preflight use the same pinned-model rule.</summary>
public static class DispatchModelAlias
{
    public static string Resolve(AgentKind kind, AgentModelLevel level, string? modelId) =>
        ModelAlias.Normalize(kind, modelId) ?? ModelLevelAliases.For(kind, level);
}
