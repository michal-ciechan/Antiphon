using System.Net;
using System.Text;
using System.Text.Json;

namespace Antiphon.Checkpoints;

public interface IBuildSlotClient
{
    Task<SlotSession> ProbeAsync(CancellationToken cancellationToken);
    Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken);
}

public sealed record SlotSession(string Mode, int MaxCpuCount, int ExitCode = 0,
    string? SlotReason = null, SlotDiagnostic? Diagnostic = null);

public sealed class SlotLease : IAsyncDisposable
{
    public string State { get; init; } = "unavailable";
    public int WaitedSeconds { get; init; }
    public int MaxCpuCount { get; init; } = 4;
    public int ExitCode { get; init; }
    public string? SlotReason { get; init; }
    public SlotDiagnostic? Diagnostic { get; init; }
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
    private readonly string? _sensitiveToken;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _held = new(StringComparer.Ordinal);

    public BuildSlotClient(HttpMessageHandler handler, string endpoint, TimeSpan? grace = null,
        TimeSpan? wait = null, Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Action<string>? log = null,
        int? pid = null, string? processStartUtc = null, ILeaseHolderSource? holders = null,
        string? sensitiveToken = null)
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
        _sensitiveToken = sensitiveToken;
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
        SlotDiagnostic? answered = null;
        SlotDiagnostic? last = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await _http.GetAsync(_endpoint, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var elapsed = Seconds(started);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    var notFound = SlotDiagnostic.Answer("probe", 404, "broker_not_found", body, elapsed, _sensitiveToken);
                    Note(notFound);
                    return new SlotSession("unavailable", 4, SlotReason: notFound.Reason, Diagnostic: notFound);
                }
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    if (!TryListing(body, out var enabled, out var cpu))
                        return RefusedSession(Observe("probe", 200, "invalid_listing", body, elapsed));
                    var mode = enabled ? "enabled" : "unlimited";
                    _log?.Invoke($"BUILD SLOT operation=probe status=200 reason={mode} maxcpucount={cpu} elapsed={elapsed}s");
                    return new SlotSession(mode, cpu);
                }
                var reason = SlotDiagnostic.ReasonOf(body, "http_" + (int)response.StatusCode);
                last = Observe("probe", (int)response.StatusCode, reason, body, elapsed);
                if ((int)response.StatusCode < 500)
                    return RefusedSession(last);
                answered = last;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                last = SlotDiagnostic.Failure("probe", "runner_unreachable", ex, Seconds(started), _sensitiveToken);
                Note(last);
            }
            if (SecondsSince(started) >= _grace)
                break;
            await DelayGrace(started, cancellationToken).ConfigureAwait(false);
        }
        if (answered is not null)
            return RefusedSession(answered with { ElapsedSeconds = Seconds(started) });
        var fallback = last ?? new SlotDiagnostic("probe", null, "runner_unreachable", null, null, Seconds(started));
        _log?.Invoke(fallback.Line() + " fallback=unleased");
        return new SlotSession("unleased", 4, SlotReason: "runner_unreachable", Diagnostic: fallback);
    }

    public async Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken)
    {
        if (session.ExitCode != 0 || session.Mode == "refused")
            return RefusedLease(session.Diagnostic ?? new SlotDiagnostic("probe", null,
                session.SlotReason ?? "probe_refused", null, null, 0), session.Diagnostic?.ElapsedSeconds ?? 0, label);
        if (session.Mode is "unavailable" or "unleased" or "unlimited" or "off")
            return new SlotLease
            {
                State = session.Mode == "off" ? "skipped" : session.Mode,
                MaxCpuCount = session.MaxCpuCount,
                SlotReason = session.SlotReason,
                Diagnostic = session.Diagnostic,
            };
        if (session.Mode != "enabled")
            return RefusedLease(new SlotDiagnostic("probe", null, "invalid_session", null, null, 0), 0, label);

        var started = _clock();
        var lastPrinted = started - TimeSpan.FromMinutes(2);
        SlotDiagnostic? answered = null;
        SlotDiagnostic? last = null;
        DateTimeOffset? unansweredStarted = null;
        ILeaseHolder? holder = null;
        try
        {
            try
            {
                holder = _holders?.Open();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return RefusedLease(SlotDiagnostic.Failure("acquire", "holder_identity_unavailable", ex, Seconds(started), _sensitiveToken), Seconds(started), label);
            }
            var pid = holder?.Pid ?? _pid;
            var processStart = holder?.ProcessStartUtc ?? _processStartUtc;
            if (pid <= 0 || string.IsNullOrWhiteSpace(processStart))
                return RefusedLease(new SlotDiagnostic("acquire", null, "holder_identity_unavailable", null, null, Seconds(started)), Seconds(started), label);
            var replacements = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
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
                    var elapsed = Seconds(started);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        if (!TryGrant(body, out var leaseId, out var cpu, out var renewEvery, out var unlimited))
                        {
                            // A syntactically identifiable grant may need cleanup even if another field is invalid.
                            var orphanId = ReadString(body, "leaseId");
                            if (!string.IsNullOrWhiteSpace(orphanId) && !_held.ContainsKey(orphanId))
                                await ReleaseAsync(orphanId, label, CancellationToken.None).ConfigureAwait(false);
                            return RefusedLease(Observe("acquire", 200, "invalid_grant", body, elapsed, label), elapsed, label);
                        }
                        if (unlimited)
                        {
                            _log?.Invoke($"BUILD SLOT label={SlotDiagnostic.Excerpt(label, _sensitiveToken)} operation=acquire status=200 reason=unlimited maxcpucount={cpu} elapsed={elapsed}s");
                            return new SlotLease { State = "unlimited", MaxCpuCount = cpu, WaitedSeconds = elapsed };
                        }
                        if (!_held.TryAdd(leaseId!, 0))
                        {
                            if (_holders is null || holder is null || replacements >= 3)
                                return RefusedLease(Observe("acquire", 200, "shared_lease", body, elapsed, label), elapsed, label);
                            await holder.DisposeAsync().ConfigureAwait(false);
                            holder = null;
                            try { holder = _holders.Open(); }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                return RefusedLease(SlotDiagnostic.Failure("acquire", "holder_identity_unavailable", ex, Seconds(started), _sensitiveToken), Seconds(started), label);
                            }
                            pid = holder.Pid;
                            processStart = holder.ProcessStartUtc;
                            if (pid <= 0 || string.IsNullOrWhiteSpace(processStart))
                                return RefusedLease(new SlotDiagnostic("acquire", null, "holder_identity_unavailable", null, null, Seconds(started)), Seconds(started), label);
                            replacements++;
                            continue;
                        }
                        try
                        {
                            _log?.Invoke($"BUILD SLOT granted lease={leaseId} label={SlotDiagnostic.Excerpt(label, _sensitiveToken)} waited={elapsed}s maxcpucount={cpu}");
                        }
                        catch
                        {
                            _held.TryRemove(leaseId!, out _);
                            await ReleaseAsync(leaseId!, label, CancellationToken.None).ConfigureAwait(false);
                            throw;
                        }
                        var owned = holder;
                        holder = null;
                        var renewalStop = renewEvery > 0 ? new CancellationTokenSource() : null;
                        var renewal = renewalStop is null ? Task.CompletedTask
                            : RenewUntilReleasedAsync(leaseId!, label, renewEvery, renewalStop.Token);
                        return new SlotLease
                        {
                            State = "granted", LeaseId = leaseId, MaxCpuCount = cpu,
                            WaitedSeconds = elapsed,
                            ReleaseAsync = async token =>
                            {
                                try
                                {
                                    if (renewalStop is not null)
                                    {
                                        renewalStop.Cancel();
                                        await renewal.ConfigureAwait(false);
                                    }
                                    await ReleaseAsync(leaseId!, label, token).ConfigureAwait(false);
                                }
                                finally
                                {
                                    renewalStop?.Dispose();
                                    _held.TryRemove(leaseId!, out _);
                                    if (owned is not null)
                                        await owned.DisposeAsync().ConfigureAwait(false);
                                }
                            },
                        };
                    }
                    var reason = SlotDiagnostic.ReasonOf(body, "http_" + (int)response.StatusCode);
                    last = Observe("acquire", (int)response.StatusCode, reason, body, elapsed, label);
                    if (response.StatusCode == HttpStatusCode.Conflict && reason is ("build_slot_busy" or "build_slot_memory_floor"))
                    {
                        // A busy reply is a live broker answer. Transport grace starts afresh
                        // only if a later request stops receiving an answer.
                        unansweredStarted = null;
                        answered = null;
                        if (SecondsSince(started) >= _wait)
                        {
                            _log?.Invoke($"BUILD SLOT timeout label={SlotDiagnostic.Excerpt(label, _sensitiveToken)} after {(int)_wait.TotalSeconds}s reason={reason}");
                            return new SlotLease { State = "timeout", ExitCode = ExitCodes.SlotTimeout,
                                WaitedSeconds = elapsed, MaxCpuCount = 0, SlotReason = reason, Diagnostic = last };
                        }
                        if (_clock() - lastPrinted >= TimeSpan.FromMinutes(1))
                        {
                            _log?.Invoke($"BUILD SLOT waiting label={SlotDiagnostic.Excerpt(label, _sensitiveToken)} reason={reason} elapsed={(int)(_clock() - started).TotalMinutes}m");
                            lastPrinted = _clock();
                        }
                        var retryMs = ReadInt(body, "retryAfterMs", 5000);
                        await _delay(TimeSpan.FromMilliseconds(Math.Clamp(retryMs, 1, 60_000)), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    if ((int)response.StatusCode < 500)
                        return RefusedLease(last, elapsed, label);
                    answered = last;
                    unansweredStarted ??= _clock();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
                {
                    last = SlotDiagnostic.Failure("acquire", "runner_unreachable", ex, Seconds(started), _sensitiveToken);
                    Note(last, label);
                    unansweredStarted ??= _clock();
                }
                if (_clock() - unansweredStarted.Value >= _grace)
                    break;
                await DelayGrace(unansweredStarted.Value, cancellationToken).ConfigureAwait(false);
            }
            if (answered is not null)
                return RefusedLease(answered, Seconds(started), label);
            var fallback = last ?? new SlotDiagnostic("acquire", null, "runner_unreachable", null, null, Seconds(started));
            _log?.Invoke(fallback.Line(label, _sensitiveToken) + " fallback=unleased");
            return new SlotLease { State = "unleased", MaxCpuCount = 4,
                WaitedSeconds = Seconds(started), SlotReason = "runner_unreachable", Diagnostic = fallback };
        }
        finally
        {
            if (holder is not null)
                await holder.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RenewUntilReleasedAsync(string leaseId, string label, int everySeconds, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(everySeconds), ct).ConfigureAwait(false);
                using var response = await _http.PostAsync(_endpoint + "/" + leaseId + "/renew", null, ct).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.NoContent)
                {
                    var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var diagnostic = Observe("renew", (int)response.StatusCode,
                        SlotDiagnostic.ReasonOf(body, "http_" + (int)response.StatusCode), body, 0, label);
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        return;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                Note(SlotDiagnostic.Failure("renew", "runner_unreachable", ex, 0, _sensitiveToken), label);
            }
        }
    }

    private async Task ReleaseAsync(string leaseId, string label, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.DeleteAsync(_endpoint + "/" + leaseId, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NoContent)
                _log?.Invoke($"BUILD SLOT released lease={leaseId} label={SlotDiagnostic.Excerpt(label, _sensitiveToken)}");
            else
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                Observe("release", (int)response.StatusCode,
                    SlotDiagnostic.ReasonOf(body, "http_" + (int)response.StatusCode), body, 0, label);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Note(SlotDiagnostic.Failure("release", "runner_unreachable", ex, 0, _sensitiveToken), label);
        }
    }

    private SlotSession RefusedSession(SlotDiagnostic diagnostic)
    {
        _log?.Invoke(diagnostic.Line() + " final=refused");
        return new SlotSession("refused", 0, ExitCodes.Invalid, diagnostic.Reason, diagnostic);
    }

    private SlotLease RefusedLease(SlotDiagnostic diagnostic, int waited, string label)
    {
        _log?.Invoke(diagnostic.Line(label, _sensitiveToken) + " final=refused");
        return new SlotLease { State = "refused", ExitCode = ExitCodes.Invalid,
            MaxCpuCount = 0, WaitedSeconds = waited, SlotReason = diagnostic.Reason, Diagnostic = diagnostic };
    }

    private SlotDiagnostic Observe(string operation, int status, string reason, string body, int elapsed, string? label = null)
    {
        var diagnostic = SlotDiagnostic.Answer(operation, status, reason, body, elapsed, _sensitiveToken);
        Note(diagnostic, label);
        return diagnostic;
    }

    private void Note(SlotDiagnostic diagnostic, string? label = null) => _log?.Invoke(diagnostic.Line(label, _sensitiveToken));
    private int Seconds(DateTimeOffset started) => Math.Max(0, (int)(_clock() - started).TotalSeconds);
    private TimeSpan SecondsSince(DateTimeOffset started) => _clock() - started;

    private async Task DelayGrace(DateTimeOffset started, CancellationToken cancellationToken)
    {
        var remaining = _grace - SecondsSince(started);
        if (remaining > TimeSpan.Zero)
            await _delay(remaining < TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
    }

    private static bool TryListing(string body, out bool enabled, out int cpu)
    {
        enabled = false;
        cpu = 0;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("enabled", out var flag)
                || flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("maxCpuCount", out var max)
                || max.ValueKind != JsonValueKind.Number || !max.TryGetInt32(out cpu) || cpu <= 0
                || !root.TryGetProperty("budget", out var budget)
                || budget.ValueKind != JsonValueKind.Number || !budget.TryGetInt32(out var n) || n <= 0)
                return false;
            enabled = flag.GetBoolean();
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryGrant(string body, out string? leaseId, out int cpu, out int renewEvery, out bool unlimited)
    {
        leaseId = null; cpu = 0; renewEvery = 0; unlimited = false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("maxCpuCount", out var max)
                || max.ValueKind != JsonValueKind.Number || !max.TryGetInt32(out cpu) || cpu <= 0)
                return false;
            if (root.TryGetProperty("unlimited", out var flag))
            {
                if (flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                unlimited = flag.GetBoolean();
            }
            if (unlimited) return true;
            if (!root.TryGetProperty("leaseId", out var id) || id.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(leaseId = id.GetString())) return false;
            if (root.TryGetProperty("renewEverySeconds", out var renew) && renew.ValueKind != JsonValueKind.Null)
            {
                if (renew.ValueKind != JsonValueKind.Number || !renew.TryGetInt32(out renewEvery) || renewEvery < 0)
                    return false;
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static int ReadInt(string json, string name, int fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)) return n;
        }
        catch (JsonException) { }
        return fallback;
    }

    private static string? ReadString(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        catch (JsonException) { }
        return null;
    }
}
