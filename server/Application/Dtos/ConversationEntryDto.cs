namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// A single entry in the reconstructed conversation timeline for a workflow.
/// Derived from LlmCall and ToolInvocation audit records.
/// </summary>
public record ConversationEntryDto(
    Guid Id,
    /// <summary>"agent" for LLM responses, "tool-call" for tool invocations.</summary>
    string Type,
    /// <summary>Agent response text or tool name.</summary>
    string Content,
    string Timestamp,
    Guid? StageId,
    string StageName,
    string? ToolName,
    string? ToolInput,
    string? ToolOutput,
    /// <summary>Raw FullContent JSON for the detail view (prompt messages, full output, etc.).</summary>
    string? FullContent
);
