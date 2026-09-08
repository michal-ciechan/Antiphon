using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>Validates declared complete pairs without adding role-policy defaults or routing pins.</summary>
public static class StandingSpecialistRoutingPolicy
{
    public static IReadOnlyList<RoutingCandidate> Validate(Agent primary, IReadOnlyList<RoutingCandidate>? candidates)
    {
        if (candidates is null || candidates.Count is < 1 or > 3)
            throw new ValidationException("candidates", "Declare between one and three complete candidates.");
        if (candidates.Any(c => c is null || c.AgentKind is null || c.ModelLevel is null
            || c.AgentKind is not (AgentKind.ClaudeCode or AgentKind.Codex)
            || !Enum.IsDefined(c.ModelLevel.Value)))
            throw new ValidationException("candidates", "Each candidate must name a supported kind and model level.");
        if (candidates.Distinct().Count() != candidates.Count)
            throw new ValidationException("candidates", "Candidate pairs must be unique.");
        if (candidates[0].AgentKind != primary.Kind || candidates[0].ModelLevel != primary.ModelLevel)
            throw new ValidationException("candidates", "The first candidate must match the current primary kind and level.");
        return candidates.ToArray();
    }
}
