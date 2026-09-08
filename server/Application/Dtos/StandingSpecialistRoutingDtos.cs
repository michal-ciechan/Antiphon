using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record PutStandingSpecialistRoutingRequest(
    Guid? ConcurrencyToken, bool Enabled, IReadOnlyList<RoutingCandidate>? Candidates);
public sealed record RevalidateStandingSpecialistRequest(Guid ConcurrencyToken);
public sealed record StandingSpecialistCandidateDto(
    Guid Id, AgentKind AgentKind, AgentModelLevel ModelLevel, string ModelAlias, Guid? PhysicalAgentId,
    bool Enabled, StandingSpecialistCandidateStatus Status, string? Reason, DateTime DeclaredAt,
    DateTime? UnprovisionedAt, DateTime? LastAdmissionRefusedAt, DateTime? NextEligibleAt,
    int TransientFailures);
public sealed record StandingSpecialistRoutingDto(
    Guid AgentId, Guid? ConcurrencyToken, bool? Enabled, AgentKind PrimaryKind,
    AgentModelLevel PrimaryLevel, string PrimaryModelAlias, IReadOnlyList<RoutingCandidate> Candidates,
    IReadOnlyList<StandingSpecialistCandidateDto> CandidateStates);
