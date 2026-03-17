namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Permanent cost ledger entry. These records are NEVER deleted (FR49, NFR22).
/// Stores token counts and USD cost for each LLM call, linked to workflow/stage for aggregation.
/// </summary>
public class CostLedgerEntry
{
    public Guid Id { get; set; }
    public Guid WorkflowId { get; set; }
    public Guid StageId { get; set; }
    public Guid? StageExecutionId { get; set; }

    /// <summary>
    /// Reference to the audit record that contains full details.
    /// </summary>
    public Guid? AuditRecordId { get; set; }

    /// <summary>
    /// LLM model name (e.g. "gpt-4o", "claude-sonnet-4-20250514").
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    public long TokensIn { get; set; }
    public long TokensOut { get; set; }

    /// <summary>
    /// Cost in USD for this individual LLM call.
    /// </summary>
    public decimal CostUsd { get; set; }

    /// <summary>
    /// Duration of the LLM call in milliseconds.
    /// </summary>
    public long DurationMs { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Workflow Workflow { get; set; } = null!;
    public Stage Stage { get; set; } = null!;
    public StageExecution? StageExecution { get; set; }
    public AuditRecord? AuditRecord { get; set; }
}
