using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Pure composition of an ordered, complete-pair candidate list (CARD-0090 + CARD-0322 walker
/// contract). No DB, no availability checks — those live in
/// <see cref="ComplexityRoutingService.WalkCandidatesAsync"/>.
/// </summary>
public static class RoutingCandidates
{
    public const string OriginPin = "pin";
    public const string OriginChain = "chain";
    public const string OriginRolePolicy = "rolePolicy";

    /// <summary>
    /// One complete (kind, level) pair the walker may try. <see cref="Origin"/> is where the
    /// slot came from, not how missing fields were filled.
    /// </summary>
    public sealed record Candidate(
        AgentKind Kind,
        AgentModelLevel Level,
        string Alias,
        string Origin)
    {
        public (AgentKind Kind, AgentModelLevel Level) Pair => (Kind, Level);

        public string Describe() => $"{Kind}/{Level} ({Alias})";
    }

    public sealed record RoutingCandidateList(
        IReadOnlyList<Candidate> Candidates,
        string Source,
        IReadOnlyList<string> Origins,
        bool Walked);

    /// <summary>
    /// Strength / complexity / explicit-narrowing rules. Required pin with a pair wins outright
    /// (chain not consulted). Preferred pin prepends, then the chain, deduped. A Preferred pin
    /// with <paramref name="chain"/> null (CARD-0322, no complexity) appends one role-policy
    /// candidate. Explicit request fields filter the pin's slots; they never rewrite a pair.
    /// </summary>
    public static RoutingCandidateList Compose(
        RoutingPinService.Decision pin,
        IReadOnlyList<Candidate>? chain,
        string? chainLabel,
        AgentKind? requestKind,
        AgentModelLevel? requestLevel,
        Func<AgentKind?, AgentModelLevel?, Candidate> resolveAgainstRolePolicy)
    {
        ArgumentNullException.ThrowIfNull(resolveAgainstRolePolicy);

        var chainList = chain ?? [];
        var pinSlots = PinSlots(pin, requestKind, requestLevel, resolveAgainstRolePolicy);

        IReadOnlyList<Candidate> result;
        if (pin.Applied
            && pin.Pin!.Strength == RoutingPinStrength.Required
            && pinSlots.Count > 0)
        {
            result = pinSlots;
        }
        else
        {
            var combined = new List<Candidate>(pinSlots.Count + chainList.Count + 1);
            foreach (var slot in pinSlots)
                AddUnique(combined, slot);
            foreach (var slot in chainList)
            {
                if (!Compatible(slot, requestKind, requestLevel))
                    continue;
                AddUnique(combined, slot with { Origin = OriginChain });
            }

            // CARD-0322 D2: Preferred pin without a chain falls through to today's role-policy
            // resolution. A chain replaces role resolution (CARD-0090 §1), so this arm is
            // skipped whenever the caller passed a chain list — even an empty one.
            if (chain is null
                && pin.Applied
                && pin.Pin!.Strength == RoutingPinStrength.Preferred)
            {
                var role = resolveAgainstRolePolicy(requestKind, requestLevel)
                    with { Origin = OriginRolePolicy };
                AddUnique(combined, role);
            }

            result = combined;
        }

        var origins = result.Select(c => c.Origin).ToList();
        return new RoutingCandidateList(
            result,
            SourceOf(pin, chain, chainLabel, pinSlots.Count > 0, result),
            origins,
            Walked: result.Count >= 2);
    }

    public static string PinSource(RoutingPinService.Decision pin)
    {
        if (!pin.Applied || pin.Pin is null)
            return "pin";
        if (pin.Pin.CardId is null)
            return $"pin:stage {pin.Pin.Role}";
        var identifier = pin.CardIdentifier ?? pin.Pin.CardId.ToString();
        return $"pin:{identifier} {pin.Pin.Role}";
    }

    public static string ChainSource(TaskComplexity complexity) => $"chain:{complexity}";

    private static List<Candidate> PinSlots(
        RoutingPinService.Decision pin,
        AgentKind? requestKind,
        AgentModelLevel? requestLevel,
        Func<AgentKind?, AgentModelLevel?, Candidate> resolve)
    {
        if (!pin.Applied)
            return [];

        IReadOnlyList<RoutingCandidate> raw = pin.PinCandidates;
        if (raw.Count == 0)
        {
            if (pin.AgentKind is null && pin.ModelLevel is null)
                return [];
            raw = [new RoutingCandidate(pin.AgentKind, pin.ModelLevel)];
        }

        var result = new List<Candidate>(raw.Count);
        foreach (var slot in raw)
        {
            if (requestKind is { } askedKind && slot.AgentKind is { } slotKind && slotKind != askedKind)
                continue;
            if (requestLevel is { } askedLevel && slot.ModelLevel is { } slotLevel && slotLevel != askedLevel)
                continue;

            var complete = resolve(slot.AgentKind ?? requestKind, slot.ModelLevel ?? requestLevel)
                with { Origin = OriginPin };
            if (!Compatible(complete, requestKind, requestLevel))
                continue;
            AddUnique(result, complete);
        }

        return result;
    }

    private static bool Compatible(Candidate candidate, AgentKind? requestKind, AgentModelLevel? requestLevel)
    {
        if (requestKind is { } kind && candidate.Kind != kind)
            return false;
        if (requestLevel is { } level && candidate.Level != level)
            return false;
        return true;
    }

    private static void AddUnique(List<Candidate> list, Candidate candidate)
    {
        if (list.Any(c => c.Kind == candidate.Kind && c.Level == candidate.Level))
            return;
        list.Add(candidate);
    }

    private static string SourceOf(
        RoutingPinService.Decision pin,
        IReadOnlyList<Candidate>? chain,
        string? chainLabel,
        bool pinContributed,
        IReadOnlyList<Candidate> result)
    {
        var pinBit = pin.Applied && pinContributed ? PinSource(pin) : null;
        var chainBit = chain is not null && chainLabel is not null ? $"chain:{chainLabel}" : null;
        if (pinBit is not null && result.Any(c => c.Origin == OriginChain))
        {
            // pin+chain:CARD-0301 Plan/Hard  or  pin+chain:stage Plan/Hard
            var pinTail = pinBit.StartsWith("pin:", StringComparison.Ordinal)
                ? pinBit["pin:".Length..]
                : pinBit;
            var chainTail = chainLabel ?? "chain";
            // CARD-0332 D5: the chain label is "Plan/Hard" when a role cell answered, "Hard"
            // when the any-role row or config did. The pin already names the role, so drop a
            // leading "{pinRole}/" to keep pin+chain:CARD-0301 Plan/Hard either way.
            if (pin.Pin is { } applied)
            {
                var prefix = $"{applied.Role}/";
                if (chainTail.StartsWith(prefix, StringComparison.Ordinal))
                    chainTail = chainTail[prefix.Length..];
            }

            return $"pin+chain:{pinTail}/{chainTail}";
        }

        if (pinBit is not null && (chain is null || result.All(c => c.Origin == OriginPin)))
            return pinBit;
        if (chainBit is not null)
            return chainBit;
        if (pinBit is not null)
            return pinBit;
        return chainLabel is not null ? $"chain:{chainLabel}" : "chain";
    }
}
