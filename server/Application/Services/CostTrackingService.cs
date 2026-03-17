using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Manages cost ledger entries — the permanent tier of the two-tier audit storage.
/// Cost ledger entries are NEVER deleted (FR49, NFR22).
/// </summary>
public class CostTrackingService
{
    private readonly AppDbContext _db;

    public CostTrackingService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Record a cost ledger entry for an LLM call. These entries are permanent and never deleted.
    /// </summary>
    public async Task<CostLedgerEntry> RecordCostAsync(
        Guid workflowId,
        Guid stageId,
        Guid? stageExecutionId,
        Guid? auditRecordId,
        string modelName,
        long tokensIn,
        long tokensOut,
        decimal costUsd,
        long durationMs,
        CancellationToken cancellationToken)
    {
        var entry = new CostLedgerEntry
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflowId,
            StageId = stageId,
            StageExecutionId = stageExecutionId,
            AuditRecordId = auditRecordId,
            ModelName = modelName,
            TokensIn = tokensIn,
            TokensOut = tokensOut,
            CostUsd = costUsd,
            DurationMs = durationMs,
            CreatedAt = DateTime.UtcNow
        };

        _db.CostLedgerEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return entry;
    }

    /// <summary>
    /// Get cost breakdown for a workflow.
    /// </summary>
    public async Task<CostSummaryDto> GetWorkflowCostSummaryAsync(
        Guid workflowId,
        CancellationToken cancellationToken)
    {
        var query = _db.CostLedgerEntries.Where(e => e.WorkflowId == workflowId);
        return await BuildSummaryAsync(query, cancellationToken);
    }

    /// <summary>
    /// Get cost breakdown for a stage.
    /// </summary>
    public async Task<CostSummaryDto> GetStageCostSummaryAsync(
        Guid stageId,
        CancellationToken cancellationToken)
    {
        var query = _db.CostLedgerEntries.Where(e => e.StageId == stageId);
        return await BuildSummaryAsync(query, cancellationToken);
    }

    /// <summary>
    /// Get cost ledger entries with optional filters. These are permanent and always available.
    /// </summary>
    public async Task<List<CostLedgerEntryDto>> QueryAsync(
        Guid? workflowId,
        Guid? stageId,
        DateTime? from,
        DateTime? to,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.CostLedgerEntries.AsQueryable();

        if (workflowId.HasValue)
            query = query.Where(e => e.WorkflowId == workflowId.Value);

        if (stageId.HasValue)
            query = query.Where(e => e.StageId == stageId.Value);

        if (from.HasValue)
            query = query.Where(e => e.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.CreatedAt <= to.Value);

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(e => new CostLedgerEntryDto(
                e.Id,
                e.WorkflowId,
                e.StageId,
                e.StageExecutionId,
                e.ModelName,
                e.TokensIn,
                e.TokensOut,
                e.CostUsd,
                e.DurationMs,
                e.CreatedAt
            ))
            .ToListAsync(cancellationToken);
    }

    private static async Task<CostSummaryDto> BuildSummaryAsync(
        IQueryable<CostLedgerEntry> query,
        CancellationToken cancellationToken)
    {
        var totalCost = await query.SumAsync(e => e.CostUsd, cancellationToken);
        var totalTokensIn = await query.SumAsync(e => e.TokensIn, cancellationToken);
        var totalTokensOut = await query.SumAsync(e => e.TokensOut, cancellationToken);
        var totalCalls = await query.CountAsync(cancellationToken);

        var byModel = await query
            .GroupBy(e => e.ModelName)
            .Select(g => new CostByModelDto(
                g.Key,
                g.Sum(e => e.CostUsd),
                g.Sum(e => e.TokensIn),
                g.Sum(e => e.TokensOut),
                g.Count()
            ))
            .ToListAsync(cancellationToken);

        return new CostSummaryDto(totalCost, totalTokensIn, totalTokensOut, totalCalls, 0, byModel);
    }
}
