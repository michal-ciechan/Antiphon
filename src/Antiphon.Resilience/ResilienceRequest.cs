using Polly;

namespace Antiphon.Resilience;

public static class ResilienceRequest
{
    public static readonly HttpRequestOptionsKey<string> Operation = new("Antiphon.Resilience.Operation");

    public static readonly HttpRequestOptionsKey<ResilienceBudget> Budget = new("Antiphon.Resilience.Budget");

    public static readonly ResiliencePropertyKey<string> OperationProperty = new("Antiphon.Resilience.Operation");

    public static readonly ResiliencePropertyKey<ResilienceBudget> BudgetProperty = new("Antiphon.Resilience.Budget");

    public static readonly ResiliencePropertyKey<HttpMethod> MethodProperty = new("Antiphon.Resilience.Method");

    public const int MaxBufferedBodyBytes = 65_536;

    public static void Stamp(HttpRequestMessage request, string operation, ResilienceBudget? budget = null)
    {
        request.Options.Set(Operation, operation);
        if (budget is not null)
            request.Options.Set(Budget, budget);
    }

    public static void CopyDefaultHeaders(HttpClient source, HttpRequestMessage request)
    {
        foreach (var header in source.DefaultRequestHeaders)
        {
            if (!request.Headers.Contains(header.Key))
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    public static string NormalizeAuthority(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
            return "unspecified";
        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }
}

public sealed class ResilienceAdmissionException : InvalidOperationException
{
    public ResilienceAdmissionException(string reason)
        : base($"Resilience admission refused: {reason}.")
    {
        Reason = reason;
    }

    public string Reason { get; }
}
