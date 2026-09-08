using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public sealed record CheckReadingValidation(bool IsValid, string? Reading, string? Reason);

/// <summary>
/// Validates a correlated Check report before presentation. Syntax is not evidence of semantic
/// correctness, tool denial, full input delivery, qualification, or an in-budget winning result.
/// Those are separate admission/attempt gates; callers must supply transcript-derived correlation.
/// </summary>
public static class CheckInterpretationValidator
{
    public const int MaxReadingCharacters = 240;
    public const string Version = "check-reading-v1";

    public static CheckReadingValidation Validate(
        Guid expectedTaskId, long expectedPromptSequence, Guid actualTaskId, long actualPromptSequence,
        AgentTaskStatus terminalStatus, string? report)
    {
        if (expectedTaskId == Guid.Empty || actualTaskId != expectedTaskId
            || expectedPromptSequence <= 0 || actualPromptSequence != expectedPromptSequence)
            return Invalid("The reading does not belong to the current Check turn.");
        if (terminalStatus != AgentTaskStatus.Succeeded)
            return Invalid("The Check did not terminate successfully.");
        if (!DelegationReportFormatter.TryReadReportVerdict(expectedTaskId, report, out var verdict, out var reading)
            || verdict != "done")
            return Invalid("The reading has no matching successful Check report token.");
        if (string.IsNullOrWhiteSpace(reading))
            return Invalid("The reading is empty.");
        if (reading.Length > MaxReadingCharacters)
            return Invalid("The reading exceeds 240 characters.");
        if (reading.Contains('\n') || reading.Contains('\r'))
            return Invalid("The reading must occupy one physical line.");
        if (reading.Contains("[antiphon-task:", StringComparison.OrdinalIgnoreCase)
            || reading.Contains("[antiphon-report:", StringComparison.OrdinalIgnoreCase))
            return Invalid("The reading contains a leftover task or report marker.");
        if (!HasPrefix(reading, "On track") && !HasPrefix(reading, "Needs attention")
            && !HasPrefix(reading, "Unclear") && !HasPrefix(reading, "Settled at capture"))
            return Invalid("The reading needs a current classification and an evidence clause.");
        return new(true, reading, null);
    }

    private static bool HasPrefix(string reading, string prefix) =>
        reading.StartsWith(prefix, StringComparison.Ordinal)
        && reading.Length > prefix.Length
        && " :,-\u2014".Contains(reading[prefix.Length])
        && reading[prefix.Length..].Trim(' ', ':', ',', '-', '\u2014').Length > 0;

    private static CheckReadingValidation Invalid(string reason) => new(false, null, reason);
}
