using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Antiphon.NightlyWatchdog;

/// <summary>
/// CARD-0545 D-3 probes over Windmill's REST API. Connect failures, timeouts and 5xx classify as
/// <c>Unreachable</c>; 401/403 as <c>Unauthorized</c>. <c>jobs/list</c> rows carry no result: a row's
/// <see cref="WindmillJob.Result"/> stays null until <c>jobs_u/completed/get_result</c> is fetched.
/// </summary>
public sealed class WindmillHttpApi(HttpClient http, WatchdogOptions options) : IWindmillApi
{
    private string Base => (options.WindmillBaseUrl ?? "").TrimEnd('/');
    private string Ws => Uri.EscapeDataString(options.WindmillWorkspace);

    public async Task<WindmillCall<string>> GetVersionAsync(CancellationToken ct)
    {
        var (status, text, error) = await GetAsync("/api/version", authenticated: false, ct);
        return status == WindmillCallStatus.Ok ? WindmillCall<string>.Ok(text!.Trim().Trim('"')) : new(status, null, error);
    }

    public async Task<WindmillCall<bool>> WhoAmIAsync(CancellationToken ct)
    {
        var (status, _, error) = await GetAsync("/api/users/whoami", authenticated: true, ct);
        return status == WindmillCallStatus.Ok ? WindmillCall<bool>.Ok(true) : new(status, false, error);
    }

    public async Task<WindmillCall<ScriptInfo>> GetScriptAsync(CancellationToken ct)
    {
        var (status, text, error) = await GetAsync($"/api/w/{Ws}/scripts/get/p/{options.ScriptPath}", authenticated: true, ct);
        if (status != WindmillCallStatus.Ok) return new(status, null, error);
        var hash = (JsonNode.Parse(text!) as JsonObject)?["hash"]?.GetValue<string>() ?? "";
        return WindmillCall<ScriptInfo>.Ok(new ScriptInfo(hash));
    }

    public async Task<WindmillCall<ScheduleInfo>> GetScheduleAsync(CancellationToken ct)
    {
        var (status, text, error) = await GetAsync($"/api/w/{Ws}/schedules/get/{options.SchedulePath}", authenticated: true, ct);
        if (status != WindmillCallStatus.Ok) return new(status, null, error);
        var enabled = (JsonNode.Parse(text!) as JsonObject)?["enabled"]?.GetValue<bool>() ?? false;
        return WindmillCall<ScheduleInfo>.Ok(new ScheduleInfo(enabled));
    }

    public async Task<WindmillCall<IReadOnlyList<WorkerPing>>> ListWorkersAsync(CancellationToken ct)
    {
        var (status, text, error) = await GetAsync("/api/workers/list?ping_since=900", authenticated: true, ct);
        if (status != WindmillCallStatus.Ok) return new(status, null, error);
        var now = DateTime.UtcNow;
        var rows = new List<WorkerPing>();
        foreach (var node in JsonNode.Parse(text!) as JsonArray ?? [])
        {
            if (node is not JsonObject w) continue;
            var group = w["worker_group"]?.GetValue<string>() ?? "";
            // Windmill reports last_ping as seconds since the worker's last ping (S6 re-verifies the shape).
            var secondsAgo = w["last_ping"] is JsonValue v && v.TryGetValue<double>(out var s) ? s : double.NaN;
            if (!double.IsNaN(secondsAgo)) rows.Add(new WorkerPing(group, now.AddSeconds(-secondsAgo)));
        }
        return WindmillCall<IReadOnlyList<WorkerPing>>.Ok(rows);
    }

    public async Task<WindmillCall<IReadOnlyList<WindmillJob>>> ListJobsAsync(CancellationToken ct)
    {
        var path = $"/api/w/{Ws}/jobs/list?script_path_exact={Uri.EscapeDataString(options.ScriptPath)}&per_page=20";
        var (status, text, error) = await GetAsync(path, authenticated: true, ct);
        if (status != WindmillCallStatus.Ok) return new(status, null, error);
        var jobs = new List<WindmillJob>();
        foreach (var node in JsonNode.Parse(text!) as JsonArray ?? [])
        {
            if (node is not JsonObject row) continue;
            var type = row["type"]?.GetValue<string>();
            var kind = type == "CompletedJob" ? JobKind.Completed
                : row["running"] is JsonValue r && r.TryGetValue<bool>(out var running) && running ? JobKind.Running : JobKind.Queued;
            bool? success = row["success"] is JsonValue sv && sv.TryGetValue<bool>(out var sb) ? sb : null;
            var created = Date(row["created_at"]) ?? DateTime.MinValue;
            var started = Date(row["started_at"]);
            DateTime? completed = Date(row["completed_at"]);
            if (completed is null && kind == JobKind.Completed && started is not null && row["duration_ms"] is JsonValue d
                && d.TryGetValue<double>(out var ms))
                completed = started.Value.AddMilliseconds(ms);
            jobs.Add(new WindmillJob(row["id"]?.GetValue<string>() ?? "", kind, success, row["schedule_path"]?.GetValue<string>(),
                created, started, completed, "", null));
        }
        return WindmillCall<IReadOnlyList<WindmillJob>>.Ok(jobs);
    }

    public async Task<WindmillCall<JsonObject?>> GetCompletedResultAsync(string jobId, CancellationToken ct)
    {
        var (status, text, error) = await GetAsync($"/api/w/{Ws}/jobs_u/completed/get_result/{Uri.EscapeDataString(jobId)}", authenticated: true, ct);
        if (status != WindmillCallStatus.Ok) return new(status, null, error);
        return WindmillCall<JsonObject?>.Ok(JsonNode.Parse(text!) as JsonObject);
    }

    public async Task<WindmillCall<string>> GetLogsAsync(string jobId, CancellationToken ct)
    {
        var (status, text, error) = await GetAsync($"/api/w/{Ws}/jobs_u/get_logs/{Uri.EscapeDataString(jobId)}", authenticated: true, ct);
        return status == WindmillCallStatus.Ok ? WindmillCall<string>.Ok(text ?? "") : new(status, null, error);
    }

    private async Task<(WindmillCallStatus Status, string? Text, string? Error)> GetAsync(string path, bool authenticated, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Base + path);
            if (authenticated)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.WindmillToken ?? "");
            using var response = await http.SendAsync(request, ct);
            var status = (int)response.StatusCode;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return (WindmillCallStatus.Unauthorized, null, $"windmill {status}");
            if (response.StatusCode == HttpStatusCode.NotFound)
                return (WindmillCallStatus.NotFound, null, "windmill 404");
            if (status >= 500)
                return (WindmillCallStatus.Unreachable, null, $"windmill {status}");
            if (!response.IsSuccessStatusCode)
                return (WindmillCallStatus.Unreachable, null, $"windmill {status}");
            return (WindmillCallStatus.Ok, await response.Content.ReadAsStringAsync(ct), null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (WindmillCallStatus.Unreachable, null, "windmill timeout");
        }
        catch (HttpRequestException ex)
        {
            return (WindmillCallStatus.Unreachable, null, $"windmill connect failed: {ex.HttpRequestError}");
        }
    }

    private static DateTime? Date(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s)
        && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
}
