using System.Net.Http.Headers;
using System.Text.Json;

namespace Antiphon.Checkpoints;

/// <summary>Observes the task that owns a detached executor. The token is transport-only.</summary>
public sealed class TaskOwnerGuard : IDisposable
{
    private readonly HttpClient _http;
    private readonly string? _taskId;
    private readonly string? _api;
    private readonly string? _token;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<TimeSpan, CancellationToken, CancellationTokenSource> _deadline;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _uncertaintyBudget;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _read = new(1, 1);
    private readonly CancellationTokenSource _ended = new();
    private string? _sessionId;

    public TaskOwnerGuard(
        Func<string, string?>? environment = null,
        HttpMessageHandler? handler = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        string? expectedTaskId = null,
        string? expectedSessionId = null,
        Func<TimeSpan, CancellationToken, CancellationTokenSource>? deadline = null,
        Func<DateTimeOffset>? now = null,
        TimeSpan? uncertaintyBudget = null,
        Action<string>? log = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        _taskId = expectedTaskId ?? environment("ANTIPHON_TASK_ID");
        _api = environment("ANTIPHON_API");
        _token = environment("ANTIPHON_TASK_TOKEN");
        _sessionId = expectedSessionId ?? environment("ANTIPHON_SESSION_ID");
        _delay = delay ?? Task.Delay;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _uncertaintyBudget = uncertaintyBudget ?? TimeSpan.FromMinutes(3);
        _log = log;
        _deadline = deadline ?? ((span, token) =>
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(token);
            source.CancelAfter(span);
            return source;
        });
        _http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: handler is null)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public bool Bound => !string.IsNullOrWhiteSpace(_taskId);
    public string? TaskId => _taskId;
    public string? SessionId => _sessionId;
    public string? Reason { get; private set; }
    public CancellationToken Ended => _ended.Token;

    public async Task<bool> EnsureLiveAsync(CancellationToken cancellationToken)
    {
        if (!Bound)
            return true;
        await _read.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ended.IsCancellationRequested)
                return false;
            if (string.IsNullOrWhiteSpace(_api) || string.IsNullOrWhiteSpace(_token)
                || !Guid.TryParse(_taskId, out _))
            {
                _log?.Invoke("owner read failed: missing task API, token, or valid task id");
                End("owner-unverified");
                return false;
            }
            DateTimeOffset? firstFailure = null;
            while (true)
            {
                var result = await ReadOnceAsync(cancellationToken).ConfigureAwait(false);
                if (result.State == "live")
                {
                    return true;
                }
                if (result.State == "owner-ended")
                {
                    End("owner-ended");
                    return false;
                }
                firstFailure ??= _now();
                _log?.Invoke("owner read failed: " + result.Failure);
                var remaining = _uncertaintyBudget - (_now() - firstFailure.Value);
                if (remaining <= TimeSpan.Zero)
                {
                    End("owner-unverified");
                    return false;
                }
                await _delay(TimeSpan.FromSeconds(3) < remaining ? TimeSpan.FromSeconds(3) : remaining,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _read.Release();
        }
    }

    public async Task WatchAsync(CancellationToken cancellationToken)
    {
        if (!Bound)
            return;
        try
        {
            while (!_ended.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                await _delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                await EnsureLiveAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _ended.IsCancellationRequested)
        {
        }
    }

    private readonly record struct OwnerReadResult(string State, string? Failure = null);

    private async Task<OwnerReadResult> ReadOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var deadline = _deadline(TimeSpan.FromSeconds(12), cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                _api.TrimEnd('/') + "/api/agent-tasks/" + Uri.EscapeDataString(_taskId!));
            request.Headers.TryAddWithoutValidation("X-Antiphon-Task-Token", _token);
            using var response = await _http.SendAsync(request, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new("owner-unverified", "http " + (int)response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false));
            var root = document.RootElement;
            if (!root.TryGetProperty("summary", out var summary)
                || !summary.TryGetProperty("id", out var id)
                || !Guid.TryParse(id.GetString(), out var parsed)
                || !parsed.ToString().Equals(_taskId, StringComparison.OrdinalIgnoreCase)
                || !summary.TryGetProperty("status", out var statusElement))
                return new("owner-unverified", "invalid task identity or status in response");
            var status = statusElement.GetString();
            if (status is "Succeeded" or "Failed" or "Canceled")
                return new("owner-ended");
            if (status is not ("Dispatched" or "Working" or "Blocked"))
                return new("owner-unverified", "unknown task status " + status);
            if (!summary.TryGetProperty("agentSessionId", out var boundId)
                || !Guid.TryParse(boundId.GetString(), out var sessionId))
                return new("owner-unverified", "missing session identity in response");
            if (_sessionId is not null && !sessionId.ToString().Equals(_sessionId, StringComparison.OrdinalIgnoreCase))
                return new("owner-unverified", "session identity mismatch");
            _sessionId ??= sessionId.ToString();
            if (!root.TryGetProperty("session", out var session) || session.ValueKind != JsonValueKind.Object
                || !session.TryGetProperty("sessionId", out var actualId)
                || !Guid.TryParse(actualId.GetString(), out var parsedSession)
                || parsedSession != sessionId
                || !session.TryGetProperty("status", out var sessionStatus))
                return new("owner-unverified", "invalid session identity or status in response");
            return sessionStatus.GetString() switch
            {
                "Stopped" or "Failed" or "Stopping" => new("owner-ended"),
                "Running" or "Starting" or "Created" => new("live"),
                var unknown => new("owner-unverified", "unknown session status " + unknown),
            };
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested
            && ex is HttpRequestException or IOException or OperationCanceledException or JsonException or InvalidOperationException or UriFormatException)
        {
            if (ex is OperationCanceledException)
                return new("owner-unverified", "timeout after 12s");
            return new("owner-unverified", ex.GetType().Name + ": " + ex.Message.Replace(_token ?? "", "[redacted]", StringComparison.Ordinal));
        }
    }

    private void End(string reason)
    {
        Reason = reason;
        _ended.Cancel();
    }

    public void Dispose()
    {
        _http.Dispose();
        _read.Dispose();
        _ended.Dispose();
    }
}
