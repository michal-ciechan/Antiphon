using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>202 body for <c>POST /api/cards/{id}/diagnose</c> (CARD-0352 S4).</summary>
public sealed record DiagnoseQueuedDto(bool Queued);

/// <summary>One Diagnoses ledger row, newest-first on the list endpoint.</summary>
public sealed record DiagnosisDto(
    Guid Id,
    DiagnosisKind Kind,
    DiagnosisOutcome Outcome,
    Guid? CardId,
    string? CardIdentifier,
    Guid? TaskId,
    string? TaskShortId,
    Guid? DiagnoseTaskId,
    string? Answer,
    string? Applied,
    string? Reason,
    string? BundleStamp,
    decimal CostUsd,
    int WaitMs,
    bool Forced,
    DateTime CreatedAt);

/// <summary><c>GET /api/diagnoses/stats</c> — kind × outcome counts, wait percentiles, spend.</summary>
public sealed record DiagnosisStatsDto(
    DateTime? Since,
    int Total,
    decimal TotalCostUsd,
    int P50WaitMs,
    int P90WaitMs,
    IReadOnlyList<DiagnosisOutcomeCountDto> Counts,
    IReadOnlyDictionary<string, int> LabelDistribution);

public sealed record DiagnosisOutcomeCountDto(
    DiagnosisKind Kind,
    DiagnosisOutcome Outcome,
    int Count);
