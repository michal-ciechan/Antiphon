using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// PUT /api/routing-pins (CARD-0305). Upserts the ONE active pin for the grain: a card pin when
/// <see cref="Card"/> is given, else the stage-wide pin for <see cref="Role"/>.
///
/// <para><see cref="Provenance"/> is ASSERTED by the caller, not inferred from the bearer — the
/// orchestrator records <c>Human</c> when the operator said so and <c>Auto</c> when it chose for
/// itself. An <c>Auto</c> write onto an active <c>Human</c> row is 409 <c>routing_pin_human</c>.</para>
/// </summary>
public sealed record PutRoutingPinRequest(
    AgentTaskRole Role,
    /// <summary>Any identifier <c>card.ps1</c> accepts, or a guid. Omitted = stage-wide.</summary>
    string? Card = null,
    RoutingPinProvenance Provenance = RoutingPinProvenance.Auto,
    RoutingPinStrength Strength = RoutingPinStrength.Preferred,
    AgentKind? AgentKind = null,
    AgentModelLevel? ModelLevel = null,
    Guid? AgentId = null,
    /// <summary>Canonical aliases this stage may not use (<c>fable</c>, <c>opus</c>, …).</summary>
    IReadOnlyList<string>? ForbiddenAliases = null,
    DateTimeOffset? NotBefore = null,
    DateTimeOffset? NotAfter = null,
    string? Reason = null,
    /// <summary>
    /// Ordered candidates (CARD-0322). Mutually exclusive with the 1-candidate
    /// <see cref="AgentKind"/>/<see cref="ModelLevel"/> shorthand.
    /// </summary>
    IReadOnlyList<RoutingCandidateRequest>? Candidates = null);

/// <summary>One (possibly partial) pair on PUT <c>candidates</c>.</summary>
public sealed record RoutingCandidateRequest(AgentKind? AgentKind = null, AgentModelLevel? ModelLevel = null);

/// <summary>One active pin, as GET/PUT return it. Property names camelCase on the wire.</summary>
public sealed record RoutingPinDto(
    Guid Id,
    Guid? CardId,
    string? CardIdentifier,
    AgentTaskRole Role,
    RoutingPinProvenance Provenance,
    RoutingPinStrength Strength,
    AgentKind? AgentKind,
    AgentModelLevel? ModelLevel,
    /// <summary>What <see cref="AgentKind"/>+<see cref="ModelLevel"/> resolve to, when both are pinned.</summary>
    string? ModelAlias,
    Guid? AgentId,
    IReadOnlyList<string> ForbiddenAliases,
    DateTime? NotBefore,
    DateTime? NotAfter,
    string Reason,
    Guid? SourceTaskId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<RoutingPinCandidateDto>? Candidates = null,
    int CandidateCount = 0);

/// <summary>
/// One pin candidate as GET returns it, with live availability so <c>routing-pin.ps1 get</c>
/// can say why a list is exhausted without a dispatch.
/// </summary>
public sealed record RoutingPinCandidateDto(
    AgentKind? AgentKind,
    AgentModelLevel? ModelLevel,
    string? Alias,
    bool AvailableNow,
    string? UnavailableReason);

/// <summary>GET /api/routing-pins. Stage-wide rows carry a null <c>cardId</c>.</summary>
public sealed record RoutingPinListDto(IReadOnlyList<RoutingPinDto> Pins);

/// <summary>
/// The <c>routingPin</c> problem-details extension on a 409 <c>routing_pin_conflict</c> /
/// <c>routing_pin_forbidden</c>, and the additive pin summary on the CARD-0304 pipeline.
/// </summary>
public sealed record RoutingPinRefDto(
    Guid Id,
    Guid? CardId,
    string? CardIdentifier,
    AgentTaskRole Role,
    RoutingPinProvenance Provenance,
    RoutingPinStrength Strength,
    AgentKind? AgentKind,
    AgentModelLevel? ModelLevel,
    DateTime? NotBefore,
    string Reason,
    int CandidateCount = 0);
