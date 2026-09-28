using System.Text.Json.Serialization;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InternalDecisionQuestionRequest(
    Guid RequestId,
    int Attempt,
    string? GrantId,
    InternalDecisionCategory? Category,
    IReadOnlyList<string>? Paths,
    IReadOnlyList<string>? AttributeTargets,
    IReadOnlyList<string>? Attributes,
    InternalDecisionImpact? Impact,
    string? Question,
    string? ProposedAction,
    string? PreservationEvidence);

public sealed record InternalDecisionQuestionResponse(
    Guid QuestionId,
    InternalDecisionDisposition Disposition,
    string Reason,
    string? GrantId,
    string? Answer);

public sealed record InternalDecisionQuestionHistoryDto(
    Guid QuestionId,
    int Attempt,
    Guid AgentSessionId,
    Guid RequestId,
    string CanonicalPayloadJson,
    string PayloadHash,
    int? PolicyVersion,
    string? PolicyHash,
    string? GrantId,
    InternalDecisionDisposition Disposition,
    string Reason,
    DateTime CreatedAt);
