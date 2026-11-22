using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review.Decisions;

/// <summary>
/// Completions on a subscription seat (design 7.2), dialect <c>delegation</c>: one small read-only delegate
/// task (the provider's kind at its level, Low by default: Haiku or Luna) gets the instructions and inputs
/// as its brief and answers in its final message. The call is bounded by the chain entry's timeout
/// (<c>TimeoutSeconds</c>, 240 by default, under the executor's lease): it polls the task until it
/// settles, and a task still running at the deadline is cancelled and the entry times out, so the chain
/// moves on. A seat's cost is api-equivalent (D-20).
/// </summary>
public sealed class DelegationCompletionModel(
    IReviewTaskGateway tasks,
    IOptions<ReviewCompletionSettings> settings,
    ILogger<DelegationCompletionModel> logger,
    TimeSpan? pollInterval = null) : ICompletionModel
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _poll = pollInterval is { } p && p > TimeSpan.Zero ? p : DefaultPollInterval;

    public bool Serves(string dialect) => dialect.Equals(ReviewCompletionDialects.Delegation, StringComparison.OrdinalIgnoreCase);

    public async Task<CompletionResult> CompleteAsync(CompletionRequest request, CompletionOptions options, CancellationToken ct)
    {
        if (!settings.Value.Providers.TryGetValue(options.ProviderId, out var provider))
            throw new CompletionProviderException(DecisionFailureKind.NotConfigured, $"Completion provider '{options.ProviderId}' is not configured.");
        var directory = string.IsNullOrWhiteSpace(provider.WorkingDirectory)
            ? Path.Combine(Path.GetTempPath(), "antiphon-review-completions")
            : provider.WorkingDirectory;
        Directory.CreateDirectory(directory);

        var goal = Goal(request, options.ProviderId);
        if (goal.Length > DelegationReviewAgentRunner.MaxGoalChars)
            throw new CompletionProviderException(DecisionFailureKind.Unavailable,
                $"The request is {goal.Length} characters, over a delegate brief's {DelegationReviewAgentRunner.MaxGoalChars}.");
        var timeout = options.Timeout ?? TimeSpan.FromSeconds(Math.Max(1, provider.TimeoutSeconds));
        var create = new CreateAgentTaskRequest(
            Goal: goal,
            Title: $"Review completion ({request.Expect}) on {options.ProviderId}",
            Kind: AgentTaskKind.Worker,
            Role: AgentTaskRole.Custom,
            ModelLevel: provider.ModelLevel ?? AgentModelLevel.Low,
            AgentKind: provider.AgentKind ?? AgentKind.ClaudeCode,
            Workspace: WorkspaceMode.ReadOnly,
            WorkingDirectory: directory,
            ExpectedMinutes: Math.Clamp((int)Math.Ceiling(timeout.TotalMinutes), 1, 1440),
            AutoContinue: false,
            CommitOnSettle: "Never",
            RunnerId: "local");

        Guid taskId;
        try
        {
            taskId = await tasks.CreateAsync(create, null, directory, ct);
        }
        catch (ValidationException ex)
        {
            throw new CompletionProviderException(DecisionFailureKind.Rejected, $"The delegate task was refused: {ex.Message}", 422);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CompletionProviderException(DecisionFailureKind.Unavailable, $"No delegate task could be created: {ex.Message}");
        }

        try
        {
            while (true)
            {
                await Task.Delay(_poll, ct);
                var task = await tasks.ReadAsync(taskId, ct)
                    ?? throw new CompletionProviderException(DecisionFailureKind.Unavailable, $"Delegate task {taskId:N} no longer exists.");
                switch (task.Status)
                {
                    case AgentTaskStatus.Succeeded:
                        var text = task.ReportText();
                        if (string.IsNullOrWhiteSpace(text))
                            throw new CompletionProviderException(DecisionFailureKind.SchemaFailure, $"Delegate task {taskId:N} answered nothing.");
                        return new CompletionResult(text, new DecisionUsage(task.InputTokens, task.OutputTokens), task.CostUsd, "api-equivalent",
                            new DecisionServedBy(options.ProviderId, "Seat", task.Model ?? provider.Model ?? $"{task.Kind} {task.Level}"));
                    case AgentTaskStatus.Failed:
                    case AgentTaskStatus.Canceled:
                        throw new CompletionProviderException(DecisionFailureKind.Unavailable,
                            $"Delegate task {taskId:N} {task.Status.ToString().ToLowerInvariant()}: {task.FailureReason ?? "no reason recorded"}");
                    case AgentTaskStatus.Blocked:
                        await tasks.CancelAsync(taskId, "The review engine takes no questions from a completion seat.", ct);
                        throw new CompletionProviderException(DecisionFailureKind.SchemaFailure, $"Delegate task {taskId:N} asked a question instead of answering.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The chain's budget ran out (or the server is stopping): the seat's work is not wanted any more.
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await tasks.CancelAsync(taskId, $"The review engine stopped waiting after {timeout.TotalSeconds:0}s.", budget.Token);
            logger.LogInformation("Completion seat task {TaskId} on {Provider} cancelled at its deadline", taskId, options.ProviderId);
            throw;
        }
    }

    /// <summary>The brief: read-only, the instructions, the inputs, and what the final message must be.</summary>
    public static string Goal(CompletionRequest request, string providerId) => new StringBuilder()
        .Append("Answer one completion request for the Antiphon review engine (provider ").Append(providerId).AppendLine(").")
        .AppendLine("This is read-only: do not edit files, run anything that changes state, or delegate. Everything you need is in this brief.")
        .AppendLine()
        .AppendLine("# Instructions")
        .AppendLine(request.Instructions.Trim())
        .AppendLine()
        .AppendLine("# Inputs")
        .AppendLine(CompletionText.Inputs(request))
        .AppendLine("# Answer")
        .AppendLine(CompletionText.Expectation(request))
        .ToString()
        .ReplaceLineEndings("\n");
}
