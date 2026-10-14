using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public sealed record PolicyResolution(
    AgentKind? Kind, AgentModelLevel? Level, string? Alias, string? WithheldReason);

public static class ComplexityEffective
{
    /// <summary>
    /// Projects the dispatcher's shared composition, without walking availability or choosing a head.
    /// The caller supplies availability after this pure projection.
    /// </summary>
    public static ComplexityEffectiveDto Compose(
        RoutingPinService.Decision decision,
        IReadOnlyList<RoutingCandidates.Candidate> chain,
        string chainLabel,
        string chainResolvedFrom,
        PolicyResolution policy,
        bool isDefaultTier,
        string? agentLabel = null)
    {
        var stagePin = decision.Applied ? RoutingPinService.ToRef(decision.Pin!, decision.CardIdentifier) : null;
        if (agentLabel is not null && decision.Applied)
        {
            return new("agent", [], stagePin, false,
                $"stage pin binds standing agent {agentLabel}; an ordinary dispatch runs on that agent and does not walk this row");
        }

        // Missing policy fields must never become a default launchable pair. The shared composer
        // requires a complete Candidate, so unresolved slots use undefined enum values and are
        // withheld below. Complete pin/chain slots remain independently usable.
        RoutingCandidates.Candidate Resolve(AgentKind? kind, AgentModelLevel? level)
        {
            var k = kind ?? policy.Kind ?? (AgentKind)(-1);
            var l = level ?? policy.Level ?? (AgentModelLevel)(-1);
            return new(k, l, Usable(k, l) ? ModelLevelAliases.For(k, l) : "", RoutingCandidates.OriginRolePolicy);
        }

        var composed = RoutingCandidates.Compose(decision,
            chainResolvedFrom == "none" ? null : chain, chainLabel, null, null, Resolve);
        var hasPin = composed.Candidates.Any(c => c.Origin == RoutingCandidates.OriginPin);
        var source = hasPin
            ? decision.Pin!.Strength == RoutingPinStrength.Required ? "pin" : "pin+chain"
            : chainResolvedFrom switch
            {
                "role" => "role",
                "any" => "any",
                "config" => "config",
                "none" => "rolePolicy",
                _ => throw new ArgumentException("Unknown chain resolution source.", nameof(chainResolvedFrom)),
            };
        var candidates = composed.Candidates.ToList();
        if (candidates.Count == 0 && chainResolvedFrom == "none")
            candidates.Add(Resolve(null, null));

        var withheld = candidates.Any(c => !Usable(c.Kind, c.Level))
            ? policy.WithheldReason ?? "routing candidate names a kind or level which delegation cannot launch"
            : null;
        return new(source,
            candidates.Where(c => Usable(c.Kind, c.Level))
                .Select(c => new ComplexityCandidateDto(c.Kind, c.Level, c.Alias, true, null)).ToList(),
            stagePin, isDefaultTier, withheld);
    }

    private static bool Usable(AgentKind kind, AgentModelLevel level) =>
        AgentTaskService.DelegatableKinds.Contains(kind) && Enum.IsDefined(level);
}
