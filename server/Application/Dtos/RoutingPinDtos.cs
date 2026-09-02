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
    string? Reason = null);

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
    DateTime UpdatedAt);

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
    string Reason);
