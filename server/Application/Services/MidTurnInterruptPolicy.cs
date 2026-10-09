using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0491. Pure admission for one caller-requested conditional Ctrl+C.
/// A missing fact refuses. The row is judged before working state: a boundary
/// catch-up both ends the turn and flushes the pending refinement, and that
/// shape is named <c>already-sent</c>.
/// </summary>
public sealed record MidTurnInterruptFacts
{
    public required bool Enabled { get; init; }
    public required bool Requested { get; init; }
    public required AgentTaskStatus Status { get; init; }
    public required Guid? TaskSessionId { get; init; }
    public required Guid? RowSessionId { get; init; }
    public required AgentKind Kind { get; init; }
    public required SessionStatus SessionStatus { get; init; }
    public required bool RunnerFound { get; init; }
    public required bool RunnerExited { get; init; }
    public required DateTime? SessionStartedAt { get; init; }
    public required DateTime? RunnerAcceptedStartedAt { get; init; }
    public required bool Working { get; init; }
    public required QueuedMessageStatus RowStatus { get; init; }
    public required int DeliveryAttempts { get; init; }
    public required bool QuestionOpen { get; init; }
    public required bool ModalBlocked { get; init; }
    public required GrokComposerState Composer { get; init; }
    public required bool ConditionalCapability { get; init; }
}

public readonly record struct MidTurnInterruptDecision(string? Reason)
{
    public bool Admitted => Reason is null;

    public static MidTurnInterruptDecision Admit => new(null);

    public static MidTurnInterruptDecision Refuse(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A refusal names its reason.", nameof(reason));
        return new(reason);
    }
}

public static class MidTurnInterruptPolicy
{
    public const string ConversationKeyPrefix = "refine:";

    public static string ConversationKey(Guid taskId, Guid requestId) =>
        $"{ConversationKeyPrefix}{taskId:N}:{requestId:N}";

    public static bool IsRefinementKey(string? conversationKey) =>
        conversationKey is not null
        && conversationKey.StartsWith(ConversationKeyPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Live read of the queue row. Pending until the verdict is a transcript confirm
    /// (<see cref="DeliveryVerdict.LateConfirmed"/> or a transcript-stamped
    /// <see cref="DeliveryVerdict.Delivered"/>). The request-time event stamp is separate.
    /// </summary>
    public static string ReadRefinementDelivered(DeliveryVerdict? verdict) =>
        verdict is DeliveryVerdict.LateConfirmed or DeliveryVerdict.Delivered
            ? "delivered"
            : "pending";

    public static MidTurnInterruptDecision Decide(MidTurnInterruptFacts facts)
    {
        if (!facts.Enabled)
            return MidTurnInterruptDecision.Refuse("disabled");
        if (!facts.Requested)
            return MidTurnInterruptDecision.Refuse("not-requested");
        if (facts.Status is not (AgentTaskStatus.Dispatched or AgentTaskStatus.Working))
            return MidTurnInterruptDecision.Refuse("task-status");
        if (facts.TaskSessionId is null)
            return MidTurnInterruptDecision.Refuse("no-session");
        if (facts.RowSessionId != facts.TaskSessionId)
            return MidTurnInterruptDecision.Refuse("row-session-mismatch");
        if (facts.Kind != AgentKind.Grok)
            return MidTurnInterruptDecision.Refuse("kind-not-grok");
        if (facts.SessionStatus != SessionStatus.Running)
            return MidTurnInterruptDecision.Refuse("session-not-running");
        if (!facts.RunnerFound)
            return MidTurnInterruptDecision.Refuse("runner-missing");
        if (facts.RunnerExited)
            return MidTurnInterruptDecision.Refuse("runner-exited");
        if (facts.SessionStartedAt is null || facts.RunnerAcceptedStartedAt is null)
            return MidTurnInterruptDecision.Refuse("generation-unproven");
        if (!SessionGeneration.Equal(facts.SessionStartedAt, facts.RunnerAcceptedStartedAt))
            return MidTurnInterruptDecision.Refuse("generation-mismatch");
        if (facts.RowStatus == QueuedMessageStatus.Sent)
            return MidTurnInterruptDecision.Refuse("already-sent");
        if (facts.RowStatus == QueuedMessageStatus.Canceled)
            return MidTurnInterruptDecision.Refuse("row-not-pending");
        if (facts.RowStatus != QueuedMessageStatus.Pending || facts.DeliveryAttempts != 0)
            return MidTurnInterruptDecision.Refuse("row-attempted");
        if (!facts.Working)
            return MidTurnInterruptDecision.Refuse("not-working");
        if (facts.QuestionOpen)
            return MidTurnInterruptDecision.Refuse("question-open");
        if (facts.ModalBlocked)
            return MidTurnInterruptDecision.Refuse("modal-blocked");
        if (facts.Composer == GrokComposerState.Draft)
            return MidTurnInterruptDecision.Refuse("composer-not-empty");
        if (facts.Composer != GrokComposerState.Empty)
            return MidTurnInterruptDecision.Refuse("composer-unreadable");
        if (!facts.ConditionalCapability)
            return MidTurnInterruptDecision.Refuse("conditional-input-unsupported");
        return MidTurnInterruptDecision.Admit;
    }
}

/// <summary>Leading record on a Refined event. Truncation keeps this prefix.</summary>
internal static class MidTurnInterruptStamp
{
    private const string Prefix = "[card-0491 ";

    public static string Format(string interruptWritten, string? refinementDelivered, Guid requestId)
    {
        var delivered = string.IsNullOrEmpty(refinementDelivered) ? "-" : refinementDelivered;
        return $"{Prefix}interruptWritten={interruptWritten} refinementDelivered={delivered} requestId={requestId:N}]";
    }

    public static bool TryRead(
        string? detail, out Guid requestId, out string interruptWritten, out string? refinementDelivered)
    {
        requestId = default;
        interruptWritten = "";
        refinementDelivered = null;
        if (string.IsNullOrEmpty(detail) || !detail.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        var end = detail.IndexOf(']');
        if (end < 0)
            return false;

        string? written = null;
        string? delivered = null;
        string? id = null;
        foreach (var part in detail[Prefix.Length..end].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            var value = part[(eq + 1)..];
            switch (part[..eq])
            {
                case "interruptWritten":
                    written = value;
                    break;
                case "refinementDelivered":
                    delivered = value;
                    break;
                case "requestId":
                    id = value;
                    break;
            }
        }

        if (written is null || id is null || !Guid.TryParseExact(id, "N", out requestId))
            return false;
        interruptWritten = written;
        refinementDelivered = delivered is null or "-" ? null : delivered;
        return true;
    }
}
