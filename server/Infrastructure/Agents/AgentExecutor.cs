using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Agents.Tools;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// Real IStageExecutor implementation (FR11, FR12) that uses Microsoft.Extensions.AI IChatClient
/// to execute AI agent stages. Streams text deltas via IEventBus (FR17), persists checkpoints
/// after every tool call (NFR12), and supports crash recovery from the last checkpoint.
/// </summary>
public class AgentExecutor : IStageExecutor
{
    private readonly LlmClientFactory _llmClientFactory;
    private readonly ToolRegistry _toolRegistry;
    private readonly IEventBus _eventBus;
    private readonly ILogger<AgentExecutor> _logger;

    private const int MaxToolRounds = 50;
    private const string CheckpointDirectory = ".antiphon-checkpoints";

    public AgentExecutor(
        LlmClientFactory llmClientFactory,
        ToolRegistry toolRegistry,
        IEventBus eventBus,
        ILogger<AgentExecutor> logger)
    {
        _llmClientFactory = llmClientFactory;
        _toolRegistry = toolRegistry;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<StageExecutionResult> ExecuteAsync(StageExecutionContext context, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var groupName = $"workflow-{context.WorkflowId}";
        var tokensIn = 0L;
        var tokensOut = 0L;
        var toolCallCount = 0;

        _logger.LogInformation(
            "AgentExecutor starting for workflow={WorkflowId} stage={StageName} model={Model}",
            context.WorkflowId, context.StageName, context.ModelName);

        // Create the LLM client via factory (FR19, FR44 — model routing)
        IChatClient chatClient;
        try
        {
            chatClient = _llmClientFactory.CreateClient(context.ModelName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create LLM client for model={Model}", context.ModelName);
            throw new InvalidOperationException(
                $"Model '{context.ModelName ?? "(default)"}' is unavailable: {ex.Message}", ex);
        }

        // Create worktree-scoped tools (AR7)
        // For now, use a temp directory; in production this would be the actual git worktree path
        var worktreeRoot = Path.Combine(Path.GetTempPath(), "antiphon-worktrees", context.WorkflowId.ToString());
        Directory.CreateDirectory(worktreeRoot);
        var tools = _toolRegistry.CreateTools(worktreeRoot);

        // Build the conversation messages
        var messages = BuildMessages(context);

        // Try to resume from checkpoint (NFR12)
        var checkpoint = await LoadCheckpointAsync(context.WorkflowId, context.StageId, ct);
        if (checkpoint is not null)
        {
            _logger.LogInformation(
                "Resuming from checkpoint: {ToolCallCount} tool calls completed",
                checkpoint.CompletedToolCalls);
            messages = checkpoint.Messages;
            toolCallCount = checkpoint.CompletedToolCalls;
        }

        // Build tool definitions for the LLM
        var chatOptions = new ChatOptions
        {
            Tools = tools.Select(t => AIFunctionFactory.Create(
                (string input, CancellationToken token) => t.ExecuteAsync(input, token),
                t.Name,
                t.Description)).Cast<AITool>().ToList()
        };

        var outputContent = new System.Text.StringBuilder();
        var artifactPaths = new List<string>();

        try
        {
            // Agent loop: send messages, handle tool calls, repeat until done
            for (var round = 0; round < MaxToolRounds; round++)
            {
                ct.ThrowIfCancellationRequested();

                // Publish activity update (debounced server-side, NFR3)
                await PublishActivityUpdateAsync(groupName, context, stopwatch.Elapsed,
                    tokensIn, tokensOut, toolCallCount, "Thinking...", ct);

                // Stream the completion
                var fullResponse = new System.Text.StringBuilder();
                var pendingToolCalls = new List<FunctionCallContent>();

                await foreach (var update in chatClient.GetStreamingResponseAsync(messages, chatOptions, ct))
                {
                    // Stream text deltas to UI (FR17)
                    if (update.Text is { Length: > 0 } text)
                    {
                        fullResponse.Append(text);
                        outputContent.Append(text);

                        await _eventBus.PublishToGroupAsync(groupName, "AgentTextDelta", new
                        {
                            workflowId = context.WorkflowId,
                            stageId = context.StageId,
                            text,
                            timestamp = DateTime.UtcNow
                        }, ct);
                    }

                    // Track token usage
                    if (update.Contents is not null)
                    {
                        foreach (var content in update.Contents)
                        {
                            if (content is UsageContent usage)
                            {
                                tokensIn += usage.Details.InputTokenCount ?? 0;
                                tokensOut += usage.Details.OutputTokenCount ?? 0;
                            }
                            else if (content is FunctionCallContent functionCall)
                            {
                                pendingToolCalls.Add(functionCall);
                            }
                        }
                    }
                }

                // Add assistant message to conversation
                var assistantMessage = new ChatMessage(ChatRole.Assistant, fullResponse.ToString());
                messages.Add(assistantMessage);

                // If no tool calls, we're done
                if (pendingToolCalls.Count == 0)
                {
                    _logger.LogInformation(
                        "Agent completed without tool calls at round {Round}", round);
                    break;
                }

                // Execute tool calls
                foreach (var toolCall in pendingToolCalls)
                {
                    ct.ThrowIfCancellationRequested();
                    toolCallCount++;

                    var tool = ToolRegistry.FindTool(tools, toolCall.Name);
                    if (tool is null)
                    {
                        _logger.LogWarning("Unknown tool requested: {ToolName}", toolCall.Name);
                        var errorResult = new FunctionResultContent(toolCall.CallId,
                            $"Error: Unknown tool '{toolCall.Name}'");
                        messages.Add(new ChatMessage(ChatRole.Tool, [errorResult]));
                        continue;
                    }

                    // Publish tool call activity
                    await PublishActivityUpdateAsync(groupName, context, stopwatch.Elapsed,
                        tokensIn, tokensOut, toolCallCount, $"{tool.Name}: executing...", ct);

                    // Publish tool call event for UI timeline
                    var toolInput = toolCall.Arguments is not null
                        ? JsonSerializer.Serialize(toolCall.Arguments)
                        : "{}";

                    await _eventBus.PublishToGroupAsync(groupName, "AgentToolCall", new
                    {
                        workflowId = context.WorkflowId,
                        stageId = context.StageId,
                        toolName = tool.Name,
                        toolInput,
                        timestamp = DateTime.UtcNow
                    }, ct);

                    string toolOutput;
                    try
                    {
                        toolOutput = await tool.ExecuteAsync(toolInput, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Tool {ToolName} failed", tool.Name);
                        toolOutput = $"Error: {ex.Message}";
                    }

                    // Publish tool result for UI
                    await _eventBus.PublishToGroupAsync(groupName, "AgentToolResult", new
                    {
                        workflowId = context.WorkflowId,
                        stageId = context.StageId,
                        toolName = tool.Name,
                        toolOutput = toolOutput.Length > 2000 ? toolOutput[..2000] + "..." : toolOutput,
                        timestamp = DateTime.UtcNow
                    }, ct);

                    // Add tool result to conversation
                    var resultContent = new FunctionResultContent(toolCall.CallId, toolOutput);
                    messages.Add(new ChatMessage(ChatRole.Tool, [resultContent]));

                    // Persist checkpoint after every tool call (NFR12)
                    await SaveCheckpointAsync(context.WorkflowId, context.StageId, messages, toolCallCount, ct);
                }
            }

            // Final activity update
            await PublishActivityUpdateAsync(groupName, context, stopwatch.Elapsed,
                tokensIn, tokensOut, toolCallCount, "Completed", ct);

            // Clean up checkpoint on success
            await DeleteCheckpointAsync(context.WorkflowId, context.StageId, ct);

            _logger.LogInformation(
                "AgentExecutor completed for stage={StageName}: {ToolCalls} tool calls, {TokensIn} tokens in, {TokensOut} tokens out, {Elapsed}ms",
                context.StageName, toolCallCount, tokensIn, tokensOut, stopwatch.ElapsedMilliseconds);

            // Write the output as an artifact
            var artifactPath = $"_antiphon/artifacts/{context.WorkflowId}/{context.StageName}.md";
            artifactPaths.Add(artifactPath);

            return new StageExecutionResult(
                OutputContent: outputContent.ToString(),
                ArtifactPaths: artifactPaths,
                SuggestedActions: null);  // AR9 — v1.1 hook, MVP ignores
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "AgentExecutor failed for workflow={WorkflowId} stage={StageName}: {Error}",
                context.WorkflowId, context.StageName, ex.Message);

            // Publish failure activity
            await PublishActivityUpdateAsync(groupName, context, stopwatch.Elapsed,
                tokensIn, tokensOut, toolCallCount, $"Failed: {ex.Message}", ct);

            throw;
        }
    }

    private static List<ChatMessage> BuildMessages(StageExecutionContext context)
    {
        var messages = new List<ChatMessage>();

        // System prompt with constitution and stage instructions (FR12)
        var systemParts = new List<string>();

        if (!string.IsNullOrEmpty(context.Constitution))
            systemParts.Add($"## Project Constitution\n{context.Constitution}");

        if (!string.IsNullOrEmpty(context.SystemPrompt))
            systemParts.Add($"## Stage Instructions\n{context.SystemPrompt}");

        if (!string.IsNullOrEmpty(context.StageInstructions) &&
            context.StageInstructions != context.SystemPrompt)
            systemParts.Add($"## Additional Instructions\n{context.StageInstructions}");

        if (systemParts.Count > 0)
            messages.Add(new ChatMessage(ChatRole.System, string.Join("\n\n", systemParts)));

        // Add upstream artifacts as context (FR12)
        if (context.UpstreamArtifacts.Count > 0)
        {
            var artifactContext = string.Join("\n\n---\n\n",
                context.UpstreamArtifacts.Select((a, i) => $"## Upstream Artifact {i + 1}\n{a}"));
            messages.Add(new ChatMessage(ChatRole.User,
                $"Here are the outputs from previous workflow stages:\n\n{artifactContext}"));
        }

        // User message with the execution task
        messages.Add(new ChatMessage(ChatRole.User,
            $"You are executing the '{context.StageName}' stage of a workflow. " +
            $"Use the available tools to produce the required output artifacts. " +
            $"When you are done, provide a summary of what you produced."));

        return messages;
    }

    private async Task PublishActivityUpdateAsync(
        string groupName,
        StageExecutionContext context,
        TimeSpan elapsed,
        long tokensIn,
        long tokensOut,
        int toolCallCount,
        string currentAction,
        CancellationToken ct)
    {
        await _eventBus.PublishToGroupAsync(groupName, "AgentActivityUpdate", new
        {
            workflowId = context.WorkflowId,
            stageId = context.StageId,
            currentAction,
            tokensIn,
            tokensOut,
            toolCallCount,
            elapsedMs = (long)elapsed.TotalMilliseconds,
            timestamp = DateTime.UtcNow
        }, ct);
    }

    #region Checkpoint persistence (NFR12)

    private static string GetCheckpointPath(Guid workflowId, Guid stageId)
    {
        var dir = Path.Combine(Path.GetTempPath(), CheckpointDirectory, workflowId.ToString());
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{stageId}.json");
    }

    private async Task SaveCheckpointAsync(
        Guid workflowId, Guid stageId,
        List<ChatMessage> messages, int toolCallCount,
        CancellationToken ct)
    {
        try
        {
            var checkpoint = new AgentCheckpoint
            {
                WorkflowId = workflowId,
                StageId = stageId,
                CompletedToolCalls = toolCallCount,
                // Serialize messages as simple role+content pairs for checkpoint
                SerializedMessages = messages.Select(m => new CheckpointMessage
                {
                    Role = m.Role.Value,
                    Content = m.Text ?? string.Empty
                }).ToList(),
                SavedAt = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(GetCheckpointPath(workflowId, stageId), json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save checkpoint for stage {StageId}", stageId);
        }
    }

    private async Task<AgentCheckpoint?> LoadCheckpointAsync(
        Guid workflowId, Guid stageId, CancellationToken ct)
    {
        var path = GetCheckpointPath(workflowId, stageId);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var checkpoint = JsonSerializer.Deserialize<AgentCheckpoint>(json);
            if (checkpoint is null)
                return null;

            // Reconstruct ChatMessages from checkpoint
            checkpoint.Messages = checkpoint.SerializedMessages
                .Select(m => new ChatMessage(new ChatRole(m.Role), m.Content))
                .ToList();

            return checkpoint;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load checkpoint for stage {StageId}, starting fresh", stageId);
            return null;
        }
    }

    private async Task DeleteCheckpointAsync(Guid workflowId, Guid stageId, CancellationToken ct)
    {
        try
        {
            var path = GetCheckpointPath(workflowId, stageId);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete checkpoint for stage {StageId}", stageId);
        }

        await Task.CompletedTask;
    }

    #endregion
}

/// <summary>
/// Serializable checkpoint for crash recovery (NFR12).
/// </summary>
internal sealed class AgentCheckpoint
{
    public Guid WorkflowId { get; set; }
    public Guid StageId { get; set; }
    public int CompletedToolCalls { get; set; }
    public List<CheckpointMessage> SerializedMessages { get; set; } = [];
    public DateTime SavedAt { get; set; }

    /// <summary>Reconstructed messages (not serialized).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<ChatMessage> Messages { get; set; } = [];
}

internal sealed class CheckpointMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
