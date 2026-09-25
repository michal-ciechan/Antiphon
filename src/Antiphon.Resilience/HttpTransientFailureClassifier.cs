using System.Net.Sockets;
using Polly;

namespace Antiphon.Resilience;

public static class HttpTransientFailureClassifier
{
    public static bool IsRetryable(
        Outcome<HttpResponseMessage> outcome,
        ResilienceSettings settings,
        string dependency,
        string? operation,
        HttpMethod? method,
        ResilienceBudget? budget,
        TimeProvider time,
        CancellationToken callerCancellation)
    {
        if (callerCancellation.IsCancellationRequested)
            return false;
        if (budget is { Expired: true })
            return false;
        if (outcome.Exception is OperationCanceledException or Polly.Timeout.TimeoutRejectedException)
            return false;
        if (method is null || !ResilienceOperations.TryAdmit(dependency, operation, method, out var admitted))
            return false;
        if (admitted.Profile is not null
            && settings.Profiles.TryGetValue(admitted.Profile, out var profile)
            && profile.Enabled == false)
            return false;

        if (outcome.Result is { } response)
        {
            if (!settings.Http.AllowedStatusCodes.Contains((int)response.StatusCode))
                return false;
            if (settings.Http.HonorRetryAfter
                && ResilienceRetryAfter.TryGet(response, time, out var hint)
                && ExceedsCap(hint, settings, budget))
                return false;
            return true;
        }

        return outcome.Exception switch
        {
            HttpRequestException http => IsAllowedTransport(http, settings),
            TimeoutException => true,
            _ => false,
        };
    }

    public static bool IsTimeoutRejection(Exception exception) =>
        exception is Polly.Timeout.TimeoutRejectedException;

    public static string Reason(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Result is { } response)
            return "http:" + (int)response.StatusCode;
        return outcome.Exception switch
        {
            HttpRequestException http when TrySocket(http, out var socket) => "socket:" + socket.SocketErrorCode,
            TimeoutException => "timeout",
            OperationCanceledException => "canceled",
            Polly.Timeout.TimeoutRejectedException => "attempt-timeout",
            _ => outcome.Exception?.GetType().Name ?? "unknown",
        };
    }

    private static bool ExceedsCap(TimeSpan hint, ResilienceSettings settings, ResilienceBudget? budget)
    {
        if (hint > TimeSpan.FromMilliseconds(settings.MaxDelayMilliseconds))
            return true;
        return budget is not null && hint > budget.Remaining;
    }

    private static bool IsAllowedTransport(HttpRequestException exception, ResilienceSettings settings)
    {
        if (!TrySocket(exception, out var socket))
            return false;
        return settings.Http.AllowedSocketErrors.Contains(socket.SocketErrorCode.ToString(), StringComparer.Ordinal);
    }

    private static bool TrySocket(HttpRequestException exception, out SocketException socket)
    {
        var inner = exception.InnerException;
        if (inner is IOException io)
            inner = io.InnerException;
        if (inner is SocketException found)
        {
            socket = found;
            return true;
        }

        socket = null!;
        return false;
    }
}

public static class ResilienceRetryAfter
{
    public static bool TryGet(HttpResponseMessage response, TimeProvider time, out TimeSpan delay)
    {
        delay = default;
        var header = response.Headers.RetryAfter;
        if (header is null)
            return false;
        if (header.Delta is { } delta)
        {
            delay = delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
            return true;
        }

        if (header.Date is not { } date)
            return false;
        delay = date - time.GetUtcNow();
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        return true;
    }
}
