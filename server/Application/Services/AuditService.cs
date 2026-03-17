using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Records audit events, LLM calls, and tool invocations. Manages two-tier storage:
/// relational summary (permanent) and JSONB full content (archivable).
/// FR47, FR48, FR49, FR50, FR51, FR53, NFR16, NFR17, NFR22, NFR23, NFR24.
/// </summary>
public class AuditService
{
    private readonly AppDbContext _db;
    private readonly IOptions<AuditSettings> _auditSettings;

    public AuditService(AppDbContext db, IOptions<AuditSettings> auditSettings)
    {
        _db = db;
        _auditSettings = auditSettings;
    }

    /// <summary>
    /// Record an LLM call audit event with full content (FR47, FR48, NFR16, NFR17).
    /// </summary>
    public async Task<AuditRecord> RecordLlmCallAsync(
        Guid workflowId,
        Guid stageId,
        Guid? stageExecutionId,
        string modelName,
        long tokensIn,
        long tokensOut,
        decimal costUsd,
        long durationMs,
        string? clientIp,
        string? gitTagName,
        Guid? userId,
        string? fullContentJson,
        CancellationToken cancellationToken)
    {
        var record = new AuditRecord
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflowId,
            StageId = stageId,
            StageExecutionId = stageExecutionId,
            EventType = AuditEventType.LlmCall,
            ModelName = modelName,
            TokensIn = tokensIn,
            TokensOut = tokensOut,
            CostUsd = costUsd,
            DurationMs = durationMs,
            ClientIp = clientIp,
            GitTagName = gitTagName,
            UserId = userId,
            Summary = $"LLM call to {modelName}: {tokensIn} tokens in, {tokensOut} tokens out (${costUsd:F6})",
            FullContent = _auditSettings.Value.EnableFullContent ? fullContentJson : null,
            CreatedAt = DateTime.UtcNow
        };

        _db.AuditRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
        return record;
    }

    /// <summary>
    /// Record a tool invocation audit event (FR48, NFR17).
    /// </summary>
    public async Task<AuditRecord> RecordToolInvocationAsync(
        Guid workflowId,
        Guid stageId,
        Guid? stageExecutionId,
        string toolName,
        long durationMs,
        string? clientIp,
        Guid? userId,
        string? fullContentJson,
        CancellationToken cancellationToken)
    {
        var record = new AuditRecord
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflowId,
            StageId = stageId,
            StageExecutionId = stageExecutionId,
            EventType = AuditEventType.ToolInvocation,
            DurationMs = durationMs,
            ClientIp = clientIp,
            UserId = userId,
            Summary = $"Tool invocation: {toolName}",
            FullContent = _auditSettings.Value.EnableFullContent ? fullContentJson : null,
            CreatedAt = DateTime.UtcNow
        };

        _db.AuditRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
        return record;
    }

    /// <summary>
    /// Record a first-class audit event (go back, update based on diff, gate decisions, etc.) (FR53).
    /// </summary>
    public async Task<AuditRecord> RecordEventAsync(
        AuditEventType eventType,
        Guid? workflowId,
        Guid? stageId,
        Guid? stageExecutionId,
        string summary,
        string? clientIp,
        Guid? userId,
        string? gitTagName,
        string? fullContentJson,
        CancellationToken cancellationToken)
    {
        var record = new AuditRecord
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflowId,
            StageId = stageId,
            StageExecutionId = stageExecutionId,
            EventType = eventType,
            ClientIp = _auditSettings.Value.EnableIpLogging ? clientIp : null,
            GitTagName = gitTagName,
            UserId = userId,
            Summary = summary,
            FullContent = _auditSettings.Value.EnableFullContent ? fullContentJson : null,
            CreatedAt = DateTime.UtcNow
        };

        _db.AuditRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
        return record;
    }

    /// <summary>
    /// Query audit records with filters (FR52, NFR18).
    /// </summary>
    public async Task<AuditQueryResult> QueryAsync(
        Guid? workflowId,
        Guid? stageId,
        DateTime? from,
        DateTime? to,
        decimal? minCost,
        decimal? maxCost,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.AuditRecords.AsQueryable();

        if (workflowId.HasValue)
            query = query.Where(r => r.WorkflowId == workflowId.Value);

        if (stageId.HasValue)
            query = query.Where(r => r.StageId == stageId.Value);

        if (from.HasValue)
            query = query.Where(r => r.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(r => r.CreatedAt <= to.Value);

        if (minCost.HasValue)
            query = query.Where(r => r.CostUsd >= minCost.Value);

        if (maxCost.HasValue)
            query = query.Where(r => r.CostUsd <= maxCost.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var records = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(r => new AuditRecordDto(
                r.Id,
                r.WorkflowId,
                r.StageId,
                r.StageExecutionId,
                r.EventType.ToString(),
                r.ModelName,
                r.TokensIn,
                r.TokensOut,
                r.CostUsd,
                r.DurationMs,
                r.ClientIp,
                r.GitTagName,
                r.UserId,
                r.Summary,
                r.FullContent,
                r.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        // Build cost summary from the filtered set (without pagination)
        var costSummary = await BuildCostSummaryAsync(query, cancellationToken);

        return new AuditQueryResult(records, totalCount, costSummary);
    }

    /// <summary>
    /// Archive (delete) full audit content older than specified days (NFR24).
    /// Only removes JSONB FullContent — relational summary data and cost ledger entries are preserved.
    /// </summary>
    public async Task<ArchiveResultDto> ArchiveFullContentAsync(int olderThanDays, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);

        var recordsToArchive = await _db.AuditRecords
            .Where(r => r.CreatedAt < cutoff && r.FullContent != null)
            .ToListAsync(cancellationToken);

        foreach (var record in recordsToArchive)
        {
            record.FullContent = null;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new ArchiveResultDto(recordsToArchive.Count, cutoff);
    }

    private static async Task<CostSummaryDto> BuildCostSummaryAsync(
        IQueryable<AuditRecord> query,
        CancellationToken cancellationToken)
    {
        var totalCost = await query.SumAsync(r => r.CostUsd, cancellationToken);
        var totalTokensIn = await query.SumAsync(r => r.TokensIn, cancellationToken);
        var totalTokensOut = await query.SumAsync(r => r.TokensOut, cancellationToken);
        var totalLlmCalls = await query.CountAsync(r => r.EventType == AuditEventType.LlmCall, cancellationToken);
        var totalToolCalls = await query.CountAsync(r => r.EventType == AuditEventType.ToolInvocation, cancellationToken);

        var byModel = await query
            .Where(r => r.ModelName != null)
            .GroupBy(r => r.ModelName!)
            .Select(g => new CostByModelDto(
                g.Key,
                g.Sum(r => r.CostUsd),
                g.Sum(r => r.TokensIn),
                g.Sum(r => r.TokensOut),
                g.Count()
            ))
            .ToListAsync(cancellationToken);

        return new CostSummaryDto(totalCost, totalTokensIn, totalTokensOut, totalLlmCalls, totalToolCalls, byModel);
    }
}
