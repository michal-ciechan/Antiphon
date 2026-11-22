namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// One provider of completions (review engine design 7.1): instructions over named inputs, answered
/// with an ABF document of the expected type, or free text. Callers name a chain, never a provider;
/// <c>CompletionChain</c> picks the entries and the implementation that serves each one's dialect.
/// </summary>
public interface ICompletionModel
{
    /// <summary>True when this implementation serves a provider of <paramref name="dialect"/>.</summary>
    bool Serves(string dialect);

    /// <summary>Completes the request. Throws <see cref="CompletionProviderException"/> for every failure.</summary>
    Task<CompletionResult> CompleteAsync(CompletionRequest request, CompletionOptions options, CancellationToken ct);
}

/// <summary>One named input (ABF <c>completion-request/1</c> <c>input</c>, spec 7.4).</summary>
public sealed record CompletionInput(string Name, string Body, string? Title = null);

/// <summary>
/// <see cref="Expect"/> is the ABF type the answer must be (<c>decision-response/1.0</c>), or <c>text</c>.
/// </summary>
public sealed record CompletionRequest(string Instructions, IReadOnlyList<CompletionInput> Inputs, string Expect);

public sealed record CompletionOptions(string ProviderId, string? Model = null, TimeSpan? Timeout = null);

public sealed record CompletionResult(string Text, DecisionUsage Usage, decimal CostUsd, string CostBasis, DecisionServedBy ServedBy);

/// <summary>What went wrong with one completion provider; the kind decides fallback as for decisions (design 7.4).</summary>
public sealed class CompletionProviderException(DecisionFailureKind kind, string message, int? statusCode = null)
    : Exception(message)
{
    public DecisionFailureKind Kind { get; } = kind;
    public int? StatusCode { get; } = statusCode;
}
