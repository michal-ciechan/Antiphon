namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Abstraction for executing a single workflow stage (FR11).
/// Implementations include MockExecutor (AR6) for testing and future AI agent executors.
/// </summary>
public interface IStageExecutor
{
    Task<StageExecutionResult> ExecuteAsync(StageExecutionContext context, CancellationToken ct);
}

/// <summary>
/// Context provided to a stage executor, containing all information needed to produce stage output.
/// </summary>
public sealed record StageExecutionContext(
    Guid WorkflowId,
    Guid StageId,
    string StageName,
    string ExecutorType,
    string? ModelName,
    string? SystemPrompt,
    IReadOnlyList<string> UpstreamArtifacts,
    string? Constitution,
    string? StageInstructions);

/// <summary>
/// Result produced by a stage executor after execution.
/// </summary>
public sealed record StageExecutionResult(
    string OutputContent,
    IReadOnlyList<string> ArtifactPaths,
    IReadOnlyList<string>? SuggestedActions = null);
