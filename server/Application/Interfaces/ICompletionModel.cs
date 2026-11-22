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

/// <summary>
/// How a completion request reads as chat text, for the HTTP adapters and the delegate seat: each
/// input under its name, its body in a fence longer than any backtick run inside it, then what the
/// answer must be.
/// </summary>
public static class CompletionText
{
    /// <summary>The user turn: the inputs and the expected answer.</summary>
    public static string UserMessage(CompletionRequest request) => Inputs(request) + Expectation(request);

    public static string Inputs(CompletionRequest request)
    {
        var text = new System.Text.StringBuilder();
        foreach (var input in request.Inputs)
        {
            text.Append("## ").Append(input.Name);
            if (!string.IsNullOrWhiteSpace(input.Title))
                text.Append(" (").Append(input.Title.Trim()).Append(')');
            text.Append('\n');
            var fence = new string('`', Math.Max(3, LongestRun(input.Body, '`') + 1));
            text.Append(fence).Append('\n').Append(input.Body.TrimEnd()).Append('\n').Append(fence).Append("\n\n");
        }

        return text.ToString();
    }

    public static string Expectation(CompletionRequest request) =>
        string.Equals(request.Expect, "text", StringComparison.OrdinalIgnoreCase)
            ? "Answer in plain text."
            : $"Answer with one ABF {request.Expect} document as the instructions describe, not inside a code fence.";

    private static int LongestRun(string text, char c)
    {
        int longest = 0, run = 0;
        foreach (var ch in text)
        {
            run = ch == c ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return longest;
    }
}
