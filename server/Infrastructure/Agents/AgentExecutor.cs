using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Agents.Tools;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// IStageExecutor implementation (FR11, FR12) that delegates to IChatClient directly with
/// UseFunctionInvocation middleware. Streams text deltas via IEventBus (FR17).
/// </summary>
public class AgentExecutor : IStageExecutor
{
    private readonly LlmClientFactory _llmClientFactory;
    private readonly ToolRegistry _toolRegistry;
    private readonly IEventBus _eventBus;
    private readonly AuditService _auditService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AgentExecutor> _logger;

    private const int MaxToolRounds = 50;

    public AgentExecutor(
        LlmClientFactory llmClientFactory,
        ToolRegistry toolRegistry,
        IEventBus eventBus,
        AuditService auditService,
        ILoggerFactory loggerFactory,
        ILogger<AgentExecutor> logger)
    {
        _llmClientFactory = llmClientFactory;
        _toolRegistry = toolRegistry;
        _eventBus = eventBus;
        _auditService = auditService;
        _loggerFactory = loggerFactory;
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

        var worktreeRoot = Path.Combine(
            Path.GetTempPath(), "antiphon-worktrees", context.WorkflowId.ToString()
        );
        Directory.CreateDirectory(worktreeRoot);

        var tools = _toolRegistry.CreateTools(worktreeRoot);

        var wrappedTools = tools.Select(t => new EventEmittingToolWrapper(
            t,
            _eventBus,
            _auditService,
            groupName,
            context.WorkflowId,
            context.StageId,
            context.StageExecutionId,
            () => toolCallCount++
        )).ToList();

        // Wrap each tool as an AIFunction that exposes the correct JSON schema so the LLM
        // receives proper parameter names (command, path, etc.) rather than a generic "input".
        var aiTools = wrappedTools
            .Select(t => (AITool)new AgentToolAIFunction(t))
            .ToList();

        var configuredClient = chatClient
            .AsBuilder()
            .UseFunctionInvocation(
                _loggerFactory,
                opts => opts.MaximumIterationsPerRequest = MaxToolRounds
            )
            .Build();

        var messages = BuildMessages(context);
        var chatOptions = new ChatOptions { Tools = aiTools };

        var outputContent = new StringBuilder();
        var artifactPaths = new List<string>();

        try
        {
            await foreach (var update in configuredClient.GetStreamingResponseAsync(messages, chatOptions, ct))
            {
                if (update.Text is { Length: > 0 } text)
                {
                    outputContent.Append(text);

                    await _eventBus.PublishToGroupAsync(groupName, "AgentTextDelta", new
                    {
                        workflowId = context.WorkflowId,
                        stageId = context.StageId,
                        text,
                        timestamp = DateTime.UtcNow
                    }, ct);

                    await PublishActivityUpdateAsync(
                        groupName, context, stopwatch.Elapsed,
                        tokensIn, tokensOut, toolCallCount, "Thinking...", ct
                    );
                }

                if (update.Contents is not null)
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is UsageContent usage)
                        {
                            tokensIn += usage.Details.InputTokenCount ?? 0;
                            tokensOut += usage.Details.OutputTokenCount ?? 0;
                        }
                    }
                }
            }

            await _auditService.RecordLlmCallAsync(
                context.WorkflowId,
                context.StageId,
                context.StageExecutionId,
                context.ModelName ?? "unknown",
                tokensIn,
                tokensOut,
                costUsd: 0m,
                stopwatch.ElapsedMilliseconds,
                clientIp: null,
                gitTagName: null,
                userId: null,
                fullContentJson: System.Text.Json.JsonSerializer.Serialize(new
                {
                    model = context.ModelName,
                    stage = context.StageName,
                    tokensIn,
                    tokensOut,
                    outputLength = outputContent.Length,
                    outputPreview = outputContent.Length > 500
                        ? outputContent.ToString()[..500] + "..."
                        : outputContent.ToString()
                }),
                ct
            );

            await PublishActivityUpdateAsync(
                groupName, context, stopwatch.Elapsed,
                tokensIn, tokensOut, toolCallCount, "Completed", ct
            );

            _logger.LogInformation(
                "AgentExecutor completed for stage={StageName}: {ToolCalls} tool calls, {TokensIn} tokens in, {TokensOut} tokens out, {Elapsed}ms",
                context.StageName, toolCallCount, tokensIn, tokensOut, stopwatch.ElapsedMilliseconds);

            var artifactPath = $"_antiphon/artifacts/{context.WorkflowId}/{context.StageName}.md";
            artifactPaths.Add(artifactPath);

            return new StageExecutionResult(
                OutputContent: outputContent.ToString(),
                ArtifactPaths: artifactPaths,
                SuggestedActions: null
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "AgentExecutor failed for workflow={WorkflowId} stage={StageName}: {Error}",
                context.WorkflowId, context.StageName, ex.Message);

            await PublishActivityUpdateAsync(
                groupName, context, stopwatch.Elapsed,
                tokensIn, tokensOut, toolCallCount, $"Failed: {ex.Message}", ct
            );

            throw;
        }
    }

    private static List<ChatMessage> BuildMessages(StageExecutionContext context)
    {
        var messages = new List<ChatMessage>();

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

        if (context.UpstreamArtifacts.Count > 0)
        {
            var artifactContext = string.Join("\n\n---\n\n",
                context.UpstreamArtifacts.Select((a, i) => $"## Upstream Artifact {i + 1}\n{a}"));
            messages.Add(new ChatMessage(
                ChatRole.User,
                $"Here are the outputs from previous workflow stages:\n\n{artifactContext}"
            ));
        }

        if (!string.IsNullOrEmpty(context.InitialContext))
        {
            messages.Add(new ChatMessage(
                ChatRole.User,
                $"## Workflow Context\n{context.InitialContext}"
            ));
        }

        if (!string.IsNullOrEmpty(context.BranchName))
        {
            messages.Add(new ChatMessage(
                ChatRole.User,
                $"The git branch for this workflow is: {context.BranchName}\n" +
                $"When creating or checking out a branch, use exactly this branch name."
            ));
        }

        messages.Add(new ChatMessage(
            ChatRole.User,
            $"You are executing the '{context.StageName}' stage of a workflow. " +
            $"Use the available tools to produce the required output artifacts. " +
            $"When you are done, provide a summary of what you produced."
        ));

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
}
