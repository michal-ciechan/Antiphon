using System.Text.Json;
using Polly.CircuitBreaker;
using Polly.RateLimiting;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// CARD-1424 D-3: names why a capabilities read failed. Definite means the runner answered and
/// refused, sent a bad body, or nothing is listening; the rest (deadline, limiter, circuit, other
/// transport) may be a saturated but live runner.
/// </summary>
public static class RunnerCapabilitiesFailure
{
    public static (string Reason, bool Definite) Classify(Exception ex) => ex switch
    {
        OperationCanceledException => ("deadline", false),
        HttpRequestException { InnerException: RateLimiterRejectedException } => ("limiter", false),
        HttpRequestException { InnerException: BrokenCircuitException } => ("circuit", false),
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } => ("refused", true),
        HttpRequestException { StatusCode: { } status } => ($"http-{(int)status}", true),
        JsonException or NotSupportedException => ("bad-body", true),
        _ => ("transport", false),
    };
}
