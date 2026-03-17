namespace Antiphon.Server.Application.Dtos;

public record AuditRecordDto(
    Guid Id,
    Guid? WorkflowId,
    Guid? StageId,
    Guid? StageExecutionId,
    string EventType,
    string? ModelName,
    long TokensIn,
    long TokensOut,
    decimal CostUsd,
    long DurationMs,
    string? ClientIp,
    string? GitTagName,
    Guid? UserId,
    string Summary,
    string? FullContent,
    DateTime CreatedAt
);

public record CostLedgerEntryDto(
    Guid Id,
    Guid WorkflowId,
    Guid StageId,
    Guid? StageExecutionId,
    string ModelName,
    long TokensIn,
    long TokensOut,
    decimal CostUsd,
    long DurationMs,
    DateTime CreatedAt
);

public record AuditQueryResult(
    List<AuditRecordDto> Records,
    int TotalCount,
    CostSummaryDto CostSummary
);

public record CostSummaryDto(
    decimal TotalCostUsd,
    long TotalTokensIn,
    long TotalTokensOut,
    int TotalLlmCalls,
    int TotalToolCalls,
    List<CostByModelDto> ByModel
);

public record CostByModelDto(
    string ModelName,
    decimal CostUsd,
    long TokensIn,
    long TokensOut,
    int CallCount
);

public record ArchiveResultDto(
    int ArchivedCount,
    DateTime OlderThan
);
