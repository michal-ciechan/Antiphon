using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Interfaces;

public interface IAuditService
{
    Task<AuditRecord> RecordLlmCallAsync(
        Guid workflowId, Guid stageId, Guid? stageExecutionId, string modelName,
        long tokensIn, long tokensOut, decimal costUsd, long durationMs,
        string? clientIp, string? gitTagName, Guid? userId, string? fullContentJson,
        CancellationToken cancellationToken);

    Task<AuditRecord> RecordToolInvocationAsync(
        Guid workflowId, Guid stageId, Guid? stageExecutionId, string toolName,
        long durationMs, string? clientIp, Guid? userId, string? fullContentJson,
        CancellationToken cancellationToken);

    Task<AuditRecord> RecordEventAsync(
        AuditEventType eventType, Guid? workflowId, Guid? stageId,
        Guid? stageExecutionId, string summary, string? clientIp, Guid? userId,
        string? gitTagName, string? fullContentJson, CancellationToken cancellationToken);
}
