// W2-nightly S05: the readiness evaluator's Windmill adapter, ported from scripts/lib/nightly-health.ps1
// (Get-NightlyWindmillConfig, Invoke-NightlyWindmillHttp, ConvertFrom-NightlyWindmillJob, New-NightlyProductionWindmillApi).
// It reads the nightly script's registration, its schedule and its jobs list; it never writes to Windmill and has no
// notification path (CARD-0545 D-9). An HTTP or JSON failure in one read writes one line to the error writer and
// reads as nothing, as the script's statements did. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The nightly script's registration (scripts/get/p): Script is true whenever Windmill answered.</summary>
    public sealed record NightlyWindmillRegistration(bool Script, string Hash, string Tag, string Path);

    /// <summary>The nightly schedule (schedules/get): Args is the schedule's raw args value.</summary>
    public sealed record NightlyWindmillSchedule(bool Enabled, bool Paused, string Tag, JsonNode Args, string WorkerVersion, string ServerVersion);

    /// <summary>
    /// One jobs/list row as the evaluator reads it. Status: queued, running, success, failed or unknown. Scheduled null
    /// means the row does not say (it counts as scheduled); Trigger, when set, must be "scheduled".
    /// </summary>
    public sealed record NightlyWindmillJob
    {
        /// <summary>The Windmill job id.</summary>
        public string Id { get; init; } = "";

        /// <summary>queued, running, success, failed or unknown.</summary>
        public string Status { get; init; } = "";

        /// <summary>The job ran from a schedule; null when the row does not say.</summary>
        public bool? Scheduled { get; init; }

        /// <summary>The trigger, when the row names one.</summary>
        public string Trigger { get; init; } = "";

        /// <summary>The native run the job's result names.</summary>
        public string NativeRunId { get; init; } = "";

        /// <summary>The sha the job's result names.</summary>
        public string Sha { get; init; } = "";

        /// <summary>The London due date (yyyy-MM-dd) the job's result names.</summary>
        public string LocalDueDate { get; init; } = "";

        /// <summary>The scheduled instant as text (Windmill's scheduled_for, or 00:30 London of LocalDueDate); null when unknown.</summary>
        public string ScheduledFor { get; init; }
    }

    /// <summary>The three Windmill reads the evaluator makes (the WindmillApi seam). A read that fails throws or returns null.</summary>
    public sealed class NightlyWindmillApi
    {
        /// <summary>The nightly script's registration, null when Windmill has none.</summary>
        public required Func<NightlyWindmillRegistration> GetRegistration { get; init; }

        /// <summary>The nightly schedule, null when Windmill has none.</summary>
        public required Func<NightlyWindmillSchedule> GetSchedule { get; init; }

        /// <summary>The newest jobs of the nightly script.</summary>
        public required Func<IReadOnlyList<NightlyWindmillJob>> GetJobs { get; init; }
    }

    /// <summary>Get-NightlyWindmillConfig: where Windmill is and who reads it.</summary>
    public sealed record NightlyWindmillConfig(string BaseUrl, string Token, string Workspace, string ScriptPath)
    {
        /// <summary>A base URL and a token are both present.</summary>
        public bool HasCredentials => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Token);

        /// <summary>
        /// WINDMILL_BASE_URL; WINDMILL_TOKEN, else the trimmed text of the file WINDMILL_TOKEN_FILE names; WINDMILL_WORKSPACE
        /// (default mc); ANTIPHON_NIGHTLY_WINDMILL_SCRIPT (default u/lndcobra/antiphon_nightly_tests).
        /// </summary>
        public static NightlyWindmillConfig FromEnvironment(Func<string, string> environment, TextWriter error)
        {
            string Get(string name) => environment?.Invoke(name) ?? "";
            var token = Get("WINDMILL_TOKEN");
            var tokenFile = Get("WINDMILL_TOKEN_FILE");
            if (string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(tokenFile) && (File.Exists(tokenFile) || Directory.Exists(tokenFile)))
                token = NightlyHealth.Step(error ?? TextWriter.Null, "read WINDMILL_TOKEN_FILE", () => File.ReadAllText(tokenFile).Trim()) ?? "";
            var workspace = Get("WINDMILL_WORKSPACE");
            if (string.IsNullOrWhiteSpace(workspace)) workspace = "mc";
            var scriptPath = Get("ANTIPHON_NIGHTLY_WINDMILL_SCRIPT");
            if (string.IsNullOrWhiteSpace(scriptPath)) scriptPath = "u/lndcobra/antiphon_nightly_tests";
            return new NightlyWindmillConfig(Get("WINDMILL_BASE_URL"), token, workspace, scriptPath);
        }
    }

    /// <summary>The production Windmill adapter.</summary>
    public static class NightlyHealthWindmill
    {
        /// <summary>Each request's bound (Invoke-WebRequest -TimeoutSec 30).</summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        /// <summary>CARD-0545 D-10: at most this many completed scheduled rows have their result fetched.</summary>
        public const int ResultFetchCap = 5;

        /// <summary>
        /// New-NightlyProductionWindmillApi: null without credentials. Registration and schedule are GET scripts/get/p and
        /// schedules/get of the script path; jobs are GET jobs/list (script_path_exact, per_page=20), with the result of at
        /// most the five newest completed scheduled rows fetched from jobs_u/completed/get_result. handler replaces the
        /// network (tests); error receives one line per failed read.
        /// </summary>
        public static NightlyWindmillApi Production(NightlyWindmillConfig config, TextWriter error, HttpMessageHandler handler = null)
        {
            if (config == null || !config.HasCredentials) return null;
            error ??= TextWriter.Null;
            var root = config.BaseUrl.TrimEnd('/');
            var workspace = config.Workspace ?? "";
            var token = config.Token;
            var path = (config.ScriptPath ?? "").Replace('\\', '/');
            var client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            client.Timeout = RequestTimeout;
            JsonNode Get(string url) => Http(client, url, token);
            return new NightlyWindmillApi
            {
                GetRegistration = () =>
                {
                    var registration = NightlyHealth.Step(error, "windmill GET scripts/get", () => Get(root + "/api/w/" + workspace + "/scripts/get/p/" + path));
                    if (registration == null) return null;
                    return new NightlyWindmillRegistration(true, Str(registration, "hash"), Str(registration, "tag"), Str(registration, "path"));
                },
                GetSchedule = () =>
                {
                    var schedule = NightlyHealth.Step(error, "windmill GET schedules/get", () => Get(root + "/api/w/" + workspace + "/schedules/get/" + path));
                    if (schedule == null) return null;
                    var paused = HealthJson.Has(schedule, "paused") && HealthJson.IsTrue(HealthJson.Field(schedule, "paused"));
                    var enabled = !HealthJson.Has(schedule, "enabled") || HealthJson.IsTrue(HealthJson.Field(schedule, "enabled"));
                    return new NightlyWindmillSchedule(enabled, paused, Str(schedule, "tag"), HealthJson.Field(schedule, "args")?.DeepClone(),
                        Str(schedule, "worker_version"), Str(schedule, "server_version"));
                },
                GetJobs = () =>
                {
                    var list = NightlyHealth.Step(error, "windmill GET jobs/list",
                        () => Get(root + "/api/w/" + workspace + "/jobs/list?script_path_exact=" + Uri.EscapeDataString(path) + "&per_page=20"));
                    var rows = new List<NightlyWindmillJob>();
                    var fetched = 0;
                    foreach (var job in Items(list))
                    {
                        if (job == null) continue;
                        var scheduled = HealthJson.Has(job, "schedule_path") && !string.IsNullOrWhiteSpace(Str(job, "schedule_path"));
                        NightlyWindmillJob row;
                        // CARD-0545 D-10: CompletedJob list rows carry no result and no scheduled_for. Fetch the result for
                        // at most the five newest completed scheduled rows; a row that is not fetched, whose fetch fails, or
                        // whose result lacks the native identity stays unknown. Only a fetched result may name the native run.
                        if (HealthJson.Eq(Str(job, "type"), "CompletedJob") && scheduled && fetched < ResultFetchCap)
                        {
                            fetched++;
                            JsonNode result = null;
                            try { result = Get(root + "/api/w/" + workspace + "/jobs_u/completed/get_result/" + Uri.EscapeDataString(Str(job, "id"))); }
                            catch (Exception) { result = null; }
                            row = ConvertJob(job, true, result);
                        }
                        else
                        {
                            row = ConvertJob(job, false, null);
                        }
                        if (HealthJson.Eq(Str(job, "type"), "CompletedJob") && !string.IsNullOrWhiteSpace(row.LocalDueDate))
                        {
                            var date = NightlyLondon.ParseDate(row.LocalDueDate);
                            if (date != null) row = row with { ScheduledFor = NightlyLondon.DueUtc(date.Value).ToString("o", CultureInfo.InvariantCulture) };
                        }
                        rows.Add(row);
                    }
                    return rows;
                },
            };
        }

        /// <summary>
        /// ConvertFrom-NightlyWindmillJob (CARD-0544 D-6) of a row with its own result property, if any: only a completed
        /// job whose success is the real boolean true AND whose result names the native run and due date is success;
        /// queued/running rows keep that state; a completed failure is failed; any other shape (missing result, string
        /// booleans, unknown type) is unknown and can never qualify as a scheduled green.
        /// </summary>
        public static NightlyWindmillJob ConvertJob(JsonNode job) => ConvertJob(job, HealthJson.Has(job, "result"), HealthJson.Field(job, "result"));

        private static NightlyWindmillJob ConvertJob(JsonNode job, bool hasResult, JsonNode result)
        {
            var type = Str(job, "type");
            var status = "unknown";
            string nativeRunId = "", sha = "", localDue = "";
            if (hasResult && result is JsonObject)
            {
                if (HealthJson.Has(result, "nativeRunId")) nativeRunId = Str(result, "nativeRunId");
                if (HealthJson.Has(result, "sha")) sha = Str(result, "sha");
                if (HealthJson.Has(result, "localDueDate")) localDue = Str(result, "localDueDate");
            }
            if (HealthJson.Eq(type, "QueuedJob"))
            {
                var running = HealthJson.Field(job, "running");
                if (HealthJson.Has(job, "running") && IsBoolean(running)) status = HealthJson.IsTrue(running) ? "running" : "queued";
            }
            else if (HealthJson.Eq(type, "CompletedJob"))
            {
                var success = HealthJson.Field(job, "success");
                if (HealthJson.Has(job, "success") && IsBoolean(success))
                {
                    if (!HealthJson.IsTrue(success)) status = "failed";
                    else if (!string.IsNullOrWhiteSpace(nativeRunId) && !string.IsNullOrWhiteSpace(localDue)) status = "success";
                }
            }
            var schedulePath = HealthJson.Has(job, "schedule_path") ? Str(job, "schedule_path") : "";
            var scheduledFor = HealthJson.Field(job, "scheduled_for");
            return new NightlyWindmillJob
            {
                Id = Str(job, "id"),
                Status = status,
                Scheduled = !string.IsNullOrWhiteSpace(schedulePath),
                NativeRunId = nativeRunId,
                Sha = sha,
                LocalDueDate = localDue,
                ScheduledFor = scheduledFor == null ? null : HealthJson.Str(scheduledFor),
            };
        }

        /// <summary>
        /// Invoke-NightlyWindmillHttp (GET): the Bearer token, the response parsed as JSON, null for an empty body. A
        /// non-success status or a body that is not JSON throws.
        /// </summary>
        public static JsonNode Http(HttpClient client, string url, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            using var response = client.Send(request);
            response.EnsureSuccessStatusCode();
            var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!HealthJson.TryParse(text, out var node)) throw new FormatException("the response from " + request.RequestUri.AbsolutePath + " is not JSON");
            return node;
        }

        // @($obj): an array's items, one item otherwise (null included).
        private static IEnumerable<JsonNode> Items(JsonNode node)
        {
            if (node is JsonArray array)
            {
                foreach (var item in array) yield return item;
            }
            else
            {
                yield return node;
            }
        }

        private static bool IsBoolean(JsonNode node)
        {
            var kind = HealthJson.Kind(node);
            return kind == System.Text.Json.JsonValueKind.True || kind == System.Text.Json.JsonValueKind.False;
        }

        private static string Str(JsonNode node, string name) => HealthJson.Str(HealthJson.Field(node, name));
    }
}
