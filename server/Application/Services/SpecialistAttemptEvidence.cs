using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

public sealed record SpecialistAttemptVerdict(SpecialistAttemptOutcome Outcome, string? Reading = null,
    string? Reason = null, long? PromptSequence = null, long? ReportSequence = null);

/// <summary>Reads the complete native turn; neither task.Result nor screen text can establish a win.</summary>
public static class SpecialistAttemptEvidence
{
    public static SpecialistAttemptVerdict? Evaluate(SpecialistAttempt attempt, AgentTask task,
        SessionQueuedMessage? message, IReadOnlyList<TranscriptEntry> entries, DateTime now)
    {
        var prompt = message is null ? null : entries.FirstOrDefault(e => e.Kind == TranscriptKinds.UserPrompt
            && e.Sequence > (message.LastDeliveryBaselineSequence ?? 0)
            && SpecialistInputPolicy.CompletePromptEquals(message.Body, e.Text));
        var turn = prompt is null ? [] : entries.Where(e => e.Sequence > prompt.Sequence
            && e.Sequence < (entries.FirstOrDefault(p => p.Sequence > prompt.Sequence
                && p.Kind is TranscriptKinds.UserPrompt or TranscriptKinds.QueuedUserPrompt)?.Sequence ?? long.MaxValue)).ToArray();
        if (now >= attempt.DeadlineAt)
            return new(prompt is not null ? SpecialistAttemptOutcome.TimedOutAfterDispatch
                : message?.DeliveryAttempts > 0 ? SpecialistAttemptOutcome.DeliveryUnconfirmed
                : SpecialistAttemptOutcome.ExpiredBeforeDispatch, Reason: "The absolute attempt deadline expired.", PromptSequence: prompt?.Sequence);
        if (turn.Any(e => e.Kind == TranscriptKinds.ToolCall))
            return new(SpecialistAttemptOutcome.ToolAttempt, Reason: "The Check attempted a tool.", PromptSequence: prompt!.Sequence);
        if (!AgentTaskService.IsSettled(task.Status) && task.Status != AgentTaskStatus.Blocked) return null;
        if (task.FailureCode == AgentTaskFailureCode.AuthenticationRequired)
            return new(SpecialistAttemptOutcome.AuthenticationUnavailable, Reason: "Provider authentication is required.");
        if (task.FailureCode == AgentTaskFailureCode.SpecialistIdentityMismatch)
            return new(SpecialistAttemptOutcome.IdentityMismatch, Reason: "Execution identity changed.");
        if (prompt is null || message?.DeliveryVerdict != DeliveryVerdict.Delivered)
            return new(SpecialistAttemptOutcome.DeliveryUnconfirmed, Reason: "No complete native Check input receipt.");
        if (task.Status != AgentTaskStatus.Succeeded)
            return new(SpecialistAttemptOutcome.TaskFailedUnknown, Reason: "The Check task did not succeed.", PromptSequence: prompt.Sequence);
        var boundary = turn.LastOrDefault(e => TranscriptKinds.IsReportBoundary(e.Kind, e.StopReason));
        if (boundary is null || boundary.IsApiError == true || string.IsNullOrEmpty(boundary.ApiCallId))
            return new(SpecialistAttemptOutcome.InvalidReading, Reason: "No successful native final response boundary.", PromptSequence: prompt.Sequence);
        var report = string.Join("\n", turn.Where(e => e.Kind == TranscriptKinds.AssistantText
            && e.ApiCallId == boundary.ApiCallId && e.IsApiError != true).Select(e => e.Text)).Trim();
        if (report.Length == 0)
            return new(SpecialistAttemptOutcome.Empty, Reason: "The native final response was empty.", PromptSequence: prompt.Sequence, ReportSequence: boundary.Sequence);
        var validation = CheckInterpretationValidator.Validate(task.Id, prompt.Sequence, attempt.TaskId,
            prompt.Sequence, task.Status, report);
        return new(validation.IsValid ? SpecialistAttemptOutcome.ValidReading : SpecialistAttemptOutcome.InvalidReading,
            validation.Reading, validation.Reason, prompt.Sequence, boundary.Sequence);
    }
}
