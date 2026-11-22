namespace Antiphon.Server.Application.Review.Abf;

/// <summary>A finding from <c>review-findings/1</c> (spec 7.1), with its typed values.</summary>
public sealed record AbfFinding(
    string Id,
    string Check,
    string Severity,
    double Confidence,
    string File,
    string Anchor,
    string Body,
    string? Title,
    IReadOnlyList<string> Flags,
    AbfLineRange? Lines,
    string? Suggestion,
    int Line)
{
    public bool Unprompted => Check.Equals("none", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A <c>checks</c> row. <see cref="FindingIds"/> is the detail list when the status is <c>finding</c>.</summary>
public sealed record AbfCheckStatus(string CheckId, string Status, string? Detail, IReadOnlyList<string> FindingIds, int Line);

public sealed record AbfContextItem(string Kind, string Path, string? Reason, int Line);

/// <summary>The typed projection of <c>review-findings/1.x</c>.</summary>
public sealed class ReviewFindingsDocument
{
    public required IReadOnlyList<AbfFinding> Findings { get; init; }
    public required IReadOnlyList<AbfCheckStatus> Checks { get; init; }
    public required IReadOnlyList<AbfContextItem> Context { get; init; }
    public string? Summary { get; init; }

    /// <summary>The projection (rows and records that failed validation are dropped). Check <see cref="AbfDocument.HasErrors"/> first.</summary>
    public static ReviewFindingsDocument? From(AbfDocument document) =>
        document.Type == "review-findings" ? document.Projection as ReviewFindingsDocument : null;
}

public sealed record AbfOption(string Value, string? Description);

/// <summary>A question of <c>decision-request/1</c>: <c>noul</c>, <c>choice</c> or <c>score</c> (spec 7.2).</summary>
public sealed record AbfQuestion(string Name, string Type, string Instructions, IReadOnlyList<AbfOption> Options);

/// <summary>The typed projection of <c>decision-request/1.x</c>.</summary>
public sealed class DecisionRequestDocument
{
    public required string RequestId { get; init; }
    public AbfTypeRef RespondWith { get; init; } = new("decision-response", 1, 0);
    public required string State { get; init; }
    public required IReadOnlyList<AbfQuestion> Questions { get; init; }

    public AbfQuestion? Question(string name) =>
        Questions.FirstOrDefault(q => AbfNames.Fold(q.Name) == AbfNames.Fold(name));

    public static DecisionRequestDocument? From(AbfDocument document) =>
        document.Type == "decision-request" ? document.Projection as DecisionRequestDocument : null;
}

/// <summary>
/// One answer of <c>decision-response/1</c> (spec 7.3). <see cref="Value"/> is the choice value or level
/// label, or for <c>noul</c> the probability of yes as written; <see cref="Probability"/> is that number.
/// </summary>
public sealed record AbfDecisionAnswer(
    string Name,
    string? Kind,
    string? Value,
    double? Probability,
    double? Confidence,
    IReadOnlyDictionary<string, double>? Probabilities,
    bool Refused,
    string? Reason);

/// <summary>The typed projection of <c>decision-response/1.x</c>.</summary>
public sealed class DecisionResponseDocument
{
    public string? RequestId { get; init; }
    public string? Note { get; init; }

    /// <summary>True when the response named another request (<c>request-mismatch</c>): no answer is usable.</summary>
    public bool Rejected { get; init; }

    public required IReadOnlyList<AbfDecisionAnswer> Answers { get; init; }

    public AbfDecisionAnswer? Answer(string name) =>
        Answers.FirstOrDefault(a => AbfNames.Fold(a.Name) == AbfNames.Fold(name));

    /// <summary>Usable answers even when tolerant repairs fired; null only for a different type or a rejected response.</summary>
    public static DecisionResponseDocument? From(AbfDocument document, DecisionRequestDocument? request) =>
        document.Type == "decision-response" && document.Projection is DecisionResponseDocument { Rejected: false } typed
            ? typed
            : null;
}
