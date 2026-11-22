using System.Text.Json;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Review;

// Wire DTOs of docs/features/012-review-engine/api.md. Enums serialise as PascalCase strings.

public sealed record ReviewListDto<T>(IReadOnlyList<T> Items, int Total);

public sealed record ReviewItemsDto<T>(IReadOnlyList<T> Items);

public sealed record ReviewSubjectDto(
    Guid Id,
    ReviewHost Host,
    string Repo,
    int? PrNumber,
    string Title,
    string? Url,
    string AuthorLogin,
    string? AuthorDisplayName,
    string? AuthorResolvedBy,
    string OpenedByLogin,
    string BaseRef,
    string HeadRef,
    string BaseSha,
    string HeadSha,
    IReadOnlyList<ReviewOwnerDto> Owners);

/// <summary>A code owner: <c>Kind</c> is <c>Team</c> or <c>User</c>.</summary>
public sealed record ReviewOwnerDto(string Kind, string Name);

public sealed record BlastRadiusDto(int Reach, int ExternalContract, int Data, int RollbackAtRelease, int ReversalAfterMonths, int Exposure);

public class ReviewRunSummaryDto
{
    public required Guid Id { get; init; }
    public required ReviewSubjectDto Subject { get; init; }
    public required ReviewRunStatus Status { get; init; }
    public required ReviewStage Stage { get; init; }
    public required int Round { get; init; }
    public required int BotOnlyRounds { get; init; }
    public required bool? NeedsHumanApproval { get; init; }
    public required int OpenFindings { get; init; }
    public required int OpenBlockers { get; init; }
    public required IReadOnlyList<string> Models { get; init; }

    /// <summary>Actual plus estimated.</summary>
    public required decimal CostUsd { get; init; }

    /// <summary>API calls (cost basis <c>actual</c>).</summary>
    public required decimal CostActualUsd { get; init; }

    /// <summary>Seat API-equivalent (cost basis <c>api-equivalent</c>, D-20).</summary>
    public required decimal CostEstimatedUsd { get; init; }

    public required BlastRadiusDto? BlastRadius { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public required ReviewWaitingOn? WaitingOn { get; init; }
}

public sealed class ReviewRunDetailDto : ReviewRunSummaryDto
{
    public required IReadOnlyList<string> Categories { get; init; }
    public required Guid? WorkflowRevisionId { get; init; }
    public required IReadOnlyList<ReviewIterationDto> Iterations { get; init; }
    public required IReadOnlyList<NodeExecutionDto> Nodes { get; init; }
    public required IReadOnlyList<ReviewGateDto> Gates { get; init; }
}

public sealed record ReviewIterationDto(int Iteration, int Round, string Trigger, DateTime StartedAt);

public sealed record NodeExecutionDto(
    Guid Id,
    string NodeId,
    int Iteration,
    ReviewNodeKind Kind,
    ReviewNodeStatus Status,
    string? ItemKey,
    string? Outcome,
    string? Provider,
    string? Model,
    ReviewTransport? Transport,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? Summary);

public sealed record FindingEventDto(
    Guid Id,
    ReviewFindingAction Action,
    string ActorLogin,
    string? ActorDisplayName,
    ReviewReasonCategory? ReasonCategory,
    ReviewDismissScope? Scope,
    string? Text,
    ReviewSyncState SyncState,
    DateTime CreatedAt);

public sealed record ReviewFindingDto(
    Guid Id,
    Guid RunId,
    string Key,
    string? CheckId,
    string? CheckWhy,
    ReviewSeverity Severity,
    IReadOnlyList<string> Flags,
    bool BypassesRoundBar,
    double Confidence,
    string FilePath,
    int? LineStart,
    int? LineEnd,
    string Anchor,
    string Title,
    string Body,
    string? Suggestion,
    string? Hunk,
    ReviewFindingStatus Status,
    int RoundRaised,
    bool Posted,
    ReviewSyncState SyncState,
    string? ExternalThreadUrl,
    IReadOnlyList<FindingEventDto> Events);

public sealed record FindingResponseRequest(
    ReviewFindingAction Action,
    ReviewReasonCategory? ReasonCategory = null,
    ReviewDismissScope? Scope = null,
    string? Text = null);

public sealed record BatchFindingResponse(
    Guid FindingId,
    ReviewFindingAction Action,
    ReviewReasonCategory? ReasonCategory = null,
    ReviewDismissScope? Scope = null,
    string? Text = null)
{
    public FindingResponseRequest ToRequest() => new(Action, ReasonCategory, Scope, Text);
}

public sealed record BatchFindingResponsesRequest(IReadOnlyList<BatchFindingResponse> Responses);

public sealed record ReviewGateDto(
    Guid Id,
    Guid RunId,
    ReviewGateKind Kind,
    ReviewQueueItemStatus Status,
    string? TeamKey,
    string? AssigneeLogin,
    string? AssigneeDisplayName,
    DateTime OpenedAt,
    DateTime? DecidedAt,
    string? DecidedBy,
    ReviewGateDecision? Decision,
    string? SteerPrompt,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<ReviewGateDecision> AllowedDecisions,
    bool ViewerCanDecide,
    string? ViewerCannotDecideReason,
    IReadOnlyList<GatePassDto> Passes);

public sealed record GatePassDto(string Login, string Reason, DateTime At);

public sealed record GateDecisionRequest(ReviewGateDecision Decision, string? SteerPrompt = null);

public sealed record GateAssignRequest(string? Login = null);

public sealed record GatePassRequest(string? Reason);

public sealed record QueueItemDto(ReviewGateDto Gate, ReviewRunSummaryDto Run, int AgeWorkingMinutes, int SlaWorkingMinutes, string SlaState);

/// <summary><c>GET /api/review-me</c>.</summary>
public sealed record ReviewViewerDto(string Login, string? DisplayName, IReadOnlyList<string> Teams);

public sealed record ModelUsageDto(string Model, string Provider, ReviewTransport Transport, int Calls, decimal CostUsd);

public sealed record ReviewKpisDto(
    int InReview,
    int WaitingOnHumans,
    double AverageRounds,
    decimal CostTodayUsd,
    decimal? CostPerAcceptedFindingUsd,
    IReadOnlyList<ModelUsageDto> ModelUsage);

/// <summary>Manual intake (<c>POST /api/review-runs</c>).</summary>
public sealed record CreateReviewRunRequest(
    ReviewHost Host,
    string Repo,
    int? PrNumber = null,
    string? BaseSha = null,
    string? HeadSha = null,
    string? Title = null);

/// <summary>
/// The workflow graph. <c>overflow</c> and <c>description</c> on edges are additions to the v0.1
/// contract: the over-limit gate of D-2 has nowhere else to live.
/// </summary>
public sealed record WorkflowGraphDto(
    string Name,
    Guid? RevisionId,
    string? Hash,
    IReadOnlyList<WorkflowSectionDto> Sections,
    IReadOnlyList<WorkflowNodeDto> Nodes,
    IReadOnlyList<WorkflowEdgeDto> Edges);

public sealed record WorkflowSectionDto(string Id, string Title, IReadOnlyList<string> NodeIds, bool Collapsed = false);

public sealed record WorkflowNodeDto(string Id, ReviewNodeKind Kind, string? Run, string? Description, IReadOnlyDictionary<string, object?>? Params);

public sealed record WorkflowEdgeDto(
    string From,
    string To,
    string? Label,
    int? MaxIterations,
    bool IsBackEdge,
    string? Overflow = null,
    string? Description = null);

public sealed record WorkflowEdgeRefDto(string From, string To);

/// <summary><c>Severity</c> is <c>Error</c> or <c>Warning</c>; a missing description is a warning (design 3.3).</summary>
public sealed record WorkflowDiagnosticDto(string Code, string Severity, string Message, IReadOnlyList<string> NodeIds, WorkflowEdgeRefDto? Edge);

public sealed record WorkflowValidationDto(bool Valid, IReadOnlyList<WorkflowDiagnosticDto> Diagnostics);

/// <summary>The SignalR payload of <c>ReviewRunChanged</c>.</summary>
public sealed record ReviewRunChangedEvent(Guid RunId);

/// <summary>Converts <c>params</c> values that arrive as <see cref="JsonElement"/> into plain CLR values.</summary>
public static class ReviewJsonValues
{
    public static object? ToClr(object? value) => value switch
    {
        JsonElement element => FromElement(element),
        _ => value,
    };

    private static object? FromElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var l) => l,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => element.EnumerateArray().Select(FromElement).ToList(),
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => FromElement(p.Value), StringComparer.Ordinal),
        _ => null,
    };
}
