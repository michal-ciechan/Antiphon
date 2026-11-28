namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// One provider of typed decisions (review engine design 7.1): Jev System One over HTTP, or a
/// standing seat answering <c>decision-request/1</c>. Callers name a chain, never a provider;
/// <c>DecisionChain</c> picks the entries and the implementation that serves each one's dialect.
/// </summary>
public interface IDecisionModel
{
    /// <summary>True when this implementation serves a provider of <paramref name="dialect"/>.</summary>
    bool Serves(string dialect);

    /// <summary>
    /// Answers the questions. Throws <see cref="DecisionProviderException"/> for every failure, with
    /// the kind that decides fallback (design 7.4).
    /// </summary>
    Task<DecisionResult> DecideAsync(DecisionRequest request, DecisionOptions options, CancellationToken ct);
}

public enum DecisionQuestionKind
{
    /// <summary>Yes/no: the answer is the probability of yes.</summary>
    Noul,
    Choice,
    Score,
}

/// <summary>A choice value or a score level label, with an optional description (ABF 7.2, OQ-3).</summary>
public sealed record DecisionOption(string Value, string? Description = null);

public sealed record DecisionQuestion(string Name, DecisionQuestionKind Kind, string Instructions, IReadOnlyList<DecisionOption> Options)
{
    public static DecisionQuestion Noul(string name, string instructions) => new(name, DecisionQuestionKind.Noul, instructions, []);
}

/// <summary>One shared state and named questions (Jev System One shape).</summary>
public sealed record DecisionRequest(string State, IReadOnlyList<DecisionQuestion> Questions);

/// <summary>The chain entry being tried: its provider id, the model to ask for, and its time budget.</summary>
public sealed record DecisionOptions(string ProviderId, string? Model = null, TimeSpan? Timeout = null)
{
    // The chain passes a deadline-linked token to the model. Keep the original cancellation
    // available when a response status is already known and only its diagnostic is optional.
    internal CancellationToken? CallerCancellationToken { get; init; }
}

public sealed record DecisionServedBy(string Provider, string Transport, string ResolvedModel);

/// <summary>
/// One answer. <see cref="Value"/> is the choice value or level label; for <c>noul</c> it is
/// <c>yes</c> or <c>no</c> and <see cref="Probabilities"/> holds the probability of each.
/// </summary>
public sealed record DecisionAnswer(
    string Name,
    DecisionQuestionKind Kind,
    string? Value,
    double? Confidence,
    IReadOnlyDictionary<string, double>? Probabilities,
    bool Refused,
    DecisionServedBy? ServedBy = null);

public sealed record DecisionUsage(long InputTokens, long OutputTokens);

/// <summary>
/// A provider's answers. <see cref="CostBasis"/> is <c>actual</c> for HTTP and <c>api-equivalent</c>
/// for seats (D-20).
/// </summary>
public sealed record DecisionResult(
    IReadOnlyDictionary<string, DecisionAnswer> Answers,
    DecisionUsage Usage,
    decimal CostUsd,
    string CostBasis,
    DecisionServedBy ServedBy);

/// <summary>What went wrong with one provider call; decides fallback versus failing loudly (design 7.4).</summary>
public enum DecisionFailureKind
{
    /// <summary>The provider has no endpoint, key or implementation. Fall back.</summary>
    NotConfigured,

    /// <summary>429 or 529. Back off once, then fall back.</summary>
    RateLimited,

    /// <summary>The call ran out of time. Fall back.</summary>
    Timeout,

    /// <summary>A 5xx or a transport failure. Fall back.</summary>
    Unavailable,

    /// <summary>The response did not have the expected shape. Fall back.</summary>
    SchemaFailure,

    /// <summary>400/401/403/422 (and 413/415): our request or credentials are wrong. Fail loudly.</summary>
    Rejected,
}

public sealed class DecisionProviderException(DecisionFailureKind kind, string message, int? statusCode = null)
    : Exception(message)
{
    public DecisionFailureKind Kind { get; } = kind;
    public int? StatusCode { get; } = statusCode;
}
