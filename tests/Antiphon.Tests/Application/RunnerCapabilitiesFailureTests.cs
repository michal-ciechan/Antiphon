using System.Net;
using System.Text.Json;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-1424 D-3/V-5. Each failed desktop capabilities probe names a reason, and only a reason that
// proves the runner is gone or refusing (definite) ends the last-known-good grace at once.
[Category("Unit")]
public sealed class RunnerCapabilitiesFailureTests
{
    [Test]
    [Arguments("deadline", "deadline", false)]
    [Arguments("limiter", "limiter", false)]
    [Arguments("circuit", "circuit", false)]
    [Arguments("transport", "transport", false)]
    [Arguments("refused", "refused", true)]
    [Arguments("http", "http-503", true)]
    [Arguments("json", "bad-body", true)]
    [Arguments("not-supported", "bad-body", true)]
    public void Classify_maps_exception_to_reason(string shape, string reason, bool definite)
    {
        var (actualReason, actualDefinite) = RunnerCapabilitiesFailure.Classify(Build(shape));

        actualReason.ShouldBe(reason);
        actualDefinite.ShouldBe(definite);
    }

    private static Exception Build(string shape) => shape switch
    {
        "deadline" => new TaskCanceledException("x", new TimeoutRejectedException()),
        "limiter" => new HttpRequestException("x", new RateLimiterRejectedException()),
        "circuit" => new HttpRequestException("x", new BrokenCircuitException()),
        "transport" => new HttpRequestException("x"),
        "refused" => new HttpRequestException(HttpRequestError.ConnectionError, "x"),
        "http" => new HttpRequestException("x", null, HttpStatusCode.ServiceUnavailable),
        "json" => new JsonException("x"),
        "not-supported" => new NotSupportedException("x"),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };
}
