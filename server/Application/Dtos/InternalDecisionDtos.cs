using System.Text.Json.Serialization;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Caller-authored internal-decision grant document. Server provenance fields are not accepted here.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InternalDecisionPolicyRequest(
    int Version,
    IReadOnlyList<InternalDecisionGrantRequest>? Grants);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InternalDecisionGrantRequest(
    string? Id,
    IReadOnlyList<InternalDecisionCategory>? Categories,
    IReadOnlyList<string>? Paths,
    IReadOnlyList<string>? AttributeTargets,
    string? Preserve);

/// <summary>Immutable stored snapshot, including server-resolved granting identity.</summary>
public sealed record StoredInternalDecisionPolicy(
    int Version,
    IReadOnlyList<StoredInternalDecisionGrant> Grants,
    StoredInternalDecisionGrantedBy GrantedBy,
    DateTime GrantedAt);

public sealed record StoredInternalDecisionGrant(
    string Id,
    IReadOnlyList<InternalDecisionCategory> Categories,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string>? AttributeTargets,
    string Preserve);

public sealed record StoredInternalDecisionGrantedBy(
    string Kind,
    Guid? TaskId,
    Guid? SessionId,
    Guid? CapabilityId,
    string? CapabilityName);
