using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public sealed record PolicyResolution(
    AgentKind? Kind, AgentModelLevel? Level, string? Alias, string? WithheldReason);

public static class ComplexityEffective
{
    // Compile-only scaffold for CP-10's red-first proof.
    public static ComplexityEffectiveDto Compose(
        RoutingPinService.Decision decision,
        IReadOnlyList<RoutingCandidates.Candidate> chain,
        string chainLabel,
        string chainResolvedFrom,
        PolicyResolution policy,
        bool isDefaultTier,
        string? agentLabel = null) => new("", [], null, false, null);
}
