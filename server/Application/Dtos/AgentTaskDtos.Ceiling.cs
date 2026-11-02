using System.Text.Json.Serialization;

namespace Antiphon.Server.Application.Dtos;

// CARD-1610 S4a. The create-time ceiling lives on partials as init-only properties, not new
// positional parameters, so the positional request tail stays untouched.
public sealed partial record CreateAgentTaskRequest
{
    /// <summary>
    /// A declared ceiling: 1..720 minutes, with <see cref="CeilingReason"/>. At it the task is held
    /// Working for a typed decision; nothing is killed. Omitted when null, so a request without it
    /// serializes, and its handoff digest hashes, exactly as before the field. A <c>long</c>, so an
    /// out-of-int value is a 422 from the ceiling rule rather than a 400 bind error.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CeilingMinutes { get; init; }

    /// <summary>Why the role default is wrong for this task: 1..1000 characters, no control characters.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CeilingReason { get; init; }
}

public sealed partial record AgentTaskCreatedDto
{
    /// <summary>The stored declared ceiling. <c>delegate.ps1</c> warns <c>ceiling_unconfirmed</c> when it is missing.</summary>
    public int? RequestedCeilingMinutes { get; init; }
}

public sealed partial record AgentTaskDetailDto
{
    /// <summary>CARD-1610 S4a. Always present: the declared and the dispatch-resolved ceiling.</summary>
    public CeilingBudgetDto? CeilingBudget { get; init; }
}

/// <summary>
/// The ceiling as declared at create and as resolved at dispatch. Resolved fields stay null until
/// the dispatcher claims the attempt; Retry clears them so a default re-resolves.
/// </summary>
public sealed record CeilingBudgetDto(int? RequestedMinutes, string? RequestedReason, int? ResolvedMinutes,
    string? ResolvedSource, double? NextCeilingMinutes, int AbsoluteCapMinutes);
