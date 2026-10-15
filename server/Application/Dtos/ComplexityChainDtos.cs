using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>PUT /api/complexity-chains/{role}/{complexity} (CARD-0090, CARD-0332). Role and complexity are route segments.</summary>
public sealed record PutComplexityChainRequest(
    IReadOnlyList<ComplexityCandidateRequest> Candidates,
    RoutingPinProvenance Provenance = RoutingPinProvenance.Auto,
    string? Reason = null,
    DateTimeOffset? NotAfter = null,
    /// <summary>
    /// CARD-1275: the <see cref="ComplexityChainDto.ConcurrencyToken"/> the writer read. Omitted =
    /// no version check: the write applies to the row stored when it saves, and a save that loses
    /// a race is re-run up to <c>ComplexityChainService.UnconditionalAttempts</c> times before 409
    /// <c>complexity_chain_stale</c>. The empty GUID = "expect no active row"; any other value must
    /// equal the active row's token, else 409 <c>complexity_chain_stale</c> without a re-run.
    /// </summary>
    Guid? ConcurrencyToken = null);

public sealed record ComplexityCandidateRequest(AgentKind AgentKind, AgentModelLevel ModelLevel);

/// <summary>
/// One candidate as GET returns it, with live availability so the panel can show why a chain
/// is exhausted without a dispatch.
/// </summary>
public sealed record ComplexityCandidateDto(
    AgentKind AgentKind,
    AgentModelLevel ModelLevel,
    string Alias,
    bool AvailableNow,
    string? UnavailableReason);

/// <summary>One complexity tier: the active row, or the config default when no row exists.</summary>
public sealed record ComplexityChainDto(
    TaskComplexity Complexity,
    IReadOnlyList<ComplexityCandidateDto> Candidates,
    RoutingPinProvenance? Provenance,
    /// <summary><c>pin</c> when an active row exists, <c>config</c> when falling back to settings.</summary>
    string Source,
    string? Reason,
    DateTime? NotAfter,
    DateTime? UpdatedAt,
    /// <summary>Null = any-role row. Set on a cell and on the <c>?role=</c> effective view.</summary>
    AgentTaskRole? Role = null,
    /// <summary>
    /// <c>role</c> (this cell), <c>any</c> (any-role row), <c>config</c> (settings default),
    /// <c>none</c> (empty — a <c>-Complexity</c> dispatch will Block until set).
    /// </summary>
    string ResolvedFrom = "none",
    ComplexityEffectiveDto? Effective = null,
    /// <summary>
    /// CARD-1275: token of this cell's own active row (the role cell for a role entry, the
    /// any-role row otherwise); null when the cell has no row, including a role cell that
    /// resolves through the any-role row or config. On the <c>?role=</c> view the candidates and
    /// the token come from two reads: a clear and re-create between them pairs the earlier
    /// candidates with the newer row's token, reason and expiry
    /// (<c>ComplexityChainRaceTests.The_role_view_reads_candidates_and_token_separately_and_a_recreate_between_them_shows_the_new_token</c>).
    /// </summary>
    Guid? ConcurrencyToken = null);

public sealed record ComplexityEffectiveDto(
    string Source,
    IReadOnlyList<ComplexityCandidateDto> Candidates,
    RoutingPinRefDto? StagePin,
    bool GovernsDefault,
    string? WithheldReason);

public sealed record ComplexityChainListDto(
    IReadOnlyList<ComplexityChainDto> Chains,
    IReadOnlyList<AgentTaskRole>? Roles = null,
    IReadOnlyList<string>? Complexities = null,
    TaskComplexity DefaultComplexity = TaskComplexity.Medium);

/// <summary>
/// The walk as a DTO: 200 create body, 409 <c>routing_exhausted</c> extension, Blocked event.
/// Carries <see cref="Source"/> and per-candidate <c>origin</c> so CARD-0322 can be a second
/// list source without a second walker.
/// </summary>
public sealed record ComplexityRoutingDto(
    TaskComplexity? Complexity,
    RoutingPinProvenance? ChainProvenance,
    /// <summary>Where the chain list came from: <c>pin</c> (active row) or <c>config</c>.</summary>
    string ChainSource,
    /// <summary>
    /// What was walked: <c>chain:Hard</c>, <c>pin:CARD-0301 Plan</c>,
    /// <c>pin+chain:CARD-0301 Plan/Hard</c>, <c>pin:stage Plan</c>.
    /// </summary>
    string Source,
    IReadOnlyList<ComplexityCandidateOutcomeDto> Candidates,
    IReadOnlyList<string> Available,
    bool Walked,
    AgentTaskRole Role = default,
    AgentTaskRole? ChainRole = null);

public sealed record ComplexityCandidateOutcomeDto(
    AgentKind AgentKind,
    AgentModelLevel ModelLevel,
    string Alias,
    /// <summary><c>chosen</c> or <c>skipped</c>.</summary>
    string Outcome,
    string? Reason,
    /// <summary><c>pin</c>, <c>chain</c>, or <c>rolePolicy</c>.</summary>
    string Origin);
