using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Records a single audit event with two-tier storage: relational summary fields (permanent)
/// and JSONB full content (archivable after retention period).
/// FR48, FR50, FR51, FR53, NFR17, NFR22, NFR23.
/// </summary>
public class AuditRecord
{
    public Guid Id { get; set; }
    public Guid? WorkflowId { get; set; }
    public Guid? StageId { get; set; }
    public Guid? StageExecutionId { get; set; }
    public AuditEventType EventType { get; set; }

    /// <summary>
    /// LLM model name used (e.g. "gpt-4o", "claude-sonnet-4-20250514").
    /// </summary>
    public string? ModelName { get; set; }

    public long TokensIn { get; set; }
    public long TokensOut { get; set; }
    public decimal CostUsd { get; set; }

    /// <summary>
    /// Duration of the LLM call or tool invocation in milliseconds.
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Client IP address that triggered the request (FR50).
    /// </summary>
    public string? ClientIp { get; set; }

    /// <summary>
    /// Git tag associated with the stage execution (FR51).
    /// </summary>
    public string? GitTagName { get; set; }

    /// <summary>
    /// User who triggered this event (null for system-initiated events).
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Brief summary of the event for display purposes.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Full audit content stored as JSONB: prompts, responses, tool call inputs/outputs.
    /// This is the archivable tier (NFR23). Set to null after retention period cleanup.
    /// </summary>
    public string? FullContent { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Workflow? Workflow { get; set; }
    public Stage? Stage { get; set; }
    public StageExecution? StageExecution { get; set; }
    public User? User { get; set; }
}
