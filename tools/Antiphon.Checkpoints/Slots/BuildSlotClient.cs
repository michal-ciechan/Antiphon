using System.Net;
using System.Text;
using System.Text.Json;

namespace Antiphon.Checkpoints;

public interface IBuildSlotClient
{
    Task<SlotSession> ProbeAsync(CancellationToken cancellationToken);

    Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken);
}

public sealed record SlotSession(string Mode, int MaxCpuCount);

public sealed class SlotLease : IAsyncDisposable
{
    public string State { get; init; } = "unavailable";
    public int WaitedSeconds { get; init; }
    public int MaxCpuCount { get; init; } = 4;
    public int ExitCode { get; init; }
    public string? LeaseId { get; init; }
    public Func<CancellationToken, Task>? ReleaseAsync { get; init; }

    public ValueTask DisposeAsync() =>
        ReleaseAsync is null ? ValueTask.CompletedTask : new ValueTask(ReleaseAsync(CancellationToken.None));
}

public sealed class BuildSlotClient : IBuildSlotClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly TimeSpan _grace;
    private readonly TimeSpan _wait;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<string>? _log;
    private readonly int _pid;
    private readonly string? _processStartUtc;
    private readonly ILeaseHolderSource? _holders;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _held = new(StringComparer.Ordinal);

    public BuildSlotClient(
        HttpMessageHandler handler,
        string endpoint,
        TimeSpan? grace = null,
        TimeSpan? wait = null,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Action<string>? log = null,
        int? pid = null,
        string? processStartUtc = null,
        ILeaseHolderSource? holders = null)
    {
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        _endpoint = endpoint.TrimEnd('/');
        _grace = grace ?? TimeSpan.FromSeconds(60);
        _wait = wait ?? TimeSpan.FromMinutes(45);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
        _log = log;
        _pid = pid ?? Environment.ProcessId;
        _processStartUtc = processStartUtc;
        _holders = holders;
    }

    public static string DefaultEndpoint(bool isWindows)
    {
        var fromEnv = Environment.GetEnvironmentVariable("ANTIPHON_BUILD_SLOTS_URL");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim().TrimEnd('/');
        return isWindows ? "http://localhost:17204/build-slots" : "http://127.0.0.1:8080/build-slots";
    }

    public async Task<SlotSession> ProbeAsync(CancellationToken cancellationToken)
    {
        var started = _clock();
        while (true)
        {
            try
            {
                using var response = await _http.GetAsync(_endpoint, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new SlotSession("unavailable", 4);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    if (body.Contains("\"unlimited\":true", StringComparison.OrdinalIgnoreCase)
                        || body.Contains("\"unlimited\": true", StringComparison.OrdinalIgnoreCase))
                    {
                        var cpu = ReadInt(body, "maxCpuCount", 4);
                        return new SlotSession("unlimited", cpu);
                    }

                    return new SlotSession("enabled", 4);
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }

            if (_clock() - started >= _grace)
                return new SlotSession("unleased", 4);
            var remaining = _grace - (_clock() - started);
            var step = remaining < TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5);
            if (step <= TimeSpan.Zero)
                return new SlotSession("unleased", 4);
            await _delay(step, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken)
    {
        if (session.Mode is "unavailable" or "unleased" or "unlimited" or "off")
        {
            var state = session.Mode == "off" ? "skipped" : session.Mode;
            return new SlotLease { State = state, MaxCpuCount = session.MaxCpuCount, ExitCode = 0 };
        }

        var started = _clock();
        var lastPrinted = started - TimeSpan.FromMinutes(2);
        ILeaseHolder? holder = null;
        try
        {
            holder = _holders?.Open();
            var pid = holder?.Pid ?? _pid;
            var processStart = holder?.ProcessStartUtc ?? _processStartUtc;
            var replacements = 0;
            while (true)
            {
                using var content = new StringContent(JsonSerializer.Serialize(new
                {
                    pid,
                    processStartUtc = processStart,
                    label,
                    sessionId = Environment.GetEnvironmentVariable("ANTIPHON_SESSION_ID"),
                    taskId = Environment.GetEnvironmentVariable("ANTIPHON_TASK_ID"),
                }, Json), Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync(_endpoint, content, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var elapsed = (int)(_clock() - started).TotalSeconds;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    if (body.Contains("\"unlimited\":true", StringComparison.OrdinalIgnoreCase))
                    {
                        var cpu = ReadInt(body, "maxCpuCount", session.MaxCpuCount);
                        _log?.Invoke($"BUILD SLOT unlimited maxcpucount={cpu}");
                        return new SlotLease { State = "unlimited", MaxCpuCount = cpu, WaitedSeconds = elapsed };
                    }

                    var leaseId = ReadString(body, "leaseId") ?? "";
                    if (leaseId.Length == 0 || !_held.TryAdd(leaseId, 0))
                    {
                        if (_holders is null || holder is null || replacements >= 3)
                        {
                            _log?.Invoke($"BUILD SLOT refused shared lease={leaseId} label={label}");
                            return new SlotLease { State = "unleased", MaxCpuCount = 4, WaitedSeconds = elapsed };
                        }

                        await holder.DisposeAsync().ConfigureAwait(false);
                        holder = _holders.Open();
                        pid = holder.Pid;
                        processStart = holder.ProcessStartUtc;
                        replacements++;
                        continue;
                    }

                    var max = ReadInt(body, "maxCpuCount", 4);
                    try
                    {
                        _log?.Invoke($"BUILD SLOT granted lease={leaseId} waited={elapsed}s maxcpucount={max}");
                    }
                    catch
                    {
                        _held.TryRemove(leaseId, out _);
                        try { using var ignored = await _http.DeleteAsync(_endpoint + "/" + leaseId, CancellationToken.None).ConfigureAwait(false); }
                        catch { /* holder disposal in the outer finally is the remaining safety net */ }
                        throw;
                    }
                    var owned = holder;
                    holder = null;
                    return new SlotLease
                    {
                        State = "granted",
                        LeaseId = leaseId,
                        MaxCpuCount = max,
                        WaitedSeconds = elapsed,
                        ReleaseAsync = async token =>
                        {
                            try
                            {
                                await ReleaseAsync(leaseId, token).ConfigureAwait(false);
                            }
                            finally
                            {
                                _held.TryRemove(leaseId, out _);
                                if (owned is not null)
                                    await owned.DisposeAsync().ConfigureAwait(false);
                            }
                        },
                    };
                }

            var type = ReadString(body, "type") ?? "";
            if (response.StatusCode == HttpStatusCode.Conflict
                && (type == "build_slot_busy" || type == "build_slot_memory_floor"))
            {
                if (_clock() - started >= _wait)
                {
                    _log?.Invoke($"BUILD SLOT timeout after {(int)_wait.TotalSeconds}s");
                    return new SlotLease { State = "timeout", ExitCode = ExitCodes.SlotTimeout, WaitedSeconds = elapsed, MaxCpuCount = 0 };
                }

                if (_clock() - lastPrinted >= TimeSpan.FromMinutes(1))
                {
                    var minutes = (int)(_clock() - started).TotalMinutes;
                    _log?.Invoke(type == "build_slot_busy"
                        ? $"BUILD SLOT waiting label={label} elapsed={minutes}m"
                        : $"BUILD SLOT waiting label={label} reason=memory_floor elapsed={minutes}m");
                    lastPrinted = _clock();
                }

                var retryMs = ReadInt(body, "retryAfterMs", 5000);
                await _delay(TimeSpan.FromMilliseconds(Math.Clamp(retryMs, 1, 60_000)), cancellationToken).ConfigureAwait(false);
                continue;
            }

                if (_clock() - started >= _grace)
                    return new SlotLease { State = "unleased", MaxCpuCount = 4, WaitedSeconds = elapsed };
                await _delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (holder is not null)
                await holder.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ReleaseAsync(string leaseId, CancellationToken cancellationToken)
    {
        string line;
        try
        {
            using var response = await _http.DeleteAsync(_endpoint + "/" + leaseId, cancellationToken).ConfigureAwait(false);
            line = response.StatusCode == HttpStatusCode.NoContent
                ? $"BUILD SLOT released lease={leaseId}"
                : $"BUILD SLOT release failed lease={leaseId} status={(int)response.StatusCode}";
        }
        catch (Exception ex)
        {
            line = $"BUILD SLOT release failed lease={leaseId} error={ex.Message}";
        }

        _log?.Invoke(line);
    }

    private static int ReadInt(string json, string name, int fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (doc.RootElement.TryGetProperty(name, out var value) && value.TryGetInt32(out var n))
                return n;
        }
        catch (JsonException)
        {
        }

        return fallback;
    }

    private static string? ReadString(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
