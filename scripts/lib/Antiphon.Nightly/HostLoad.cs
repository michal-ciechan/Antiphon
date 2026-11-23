// CARD-1514 S1: the current host load, its last-hour statistics and the advisory MORE-task capacity. It replaced
// scripts/host-load.ps1 (deleted 2026-11-23, unit 21 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI
// runs it as `antiphon-scripts host-load`. GET requests only: an omitted host resolves from the live global runner default and
// the runner catalogue; nothing changes admission, budgets or placement. Every read has a 3 s deadline and a 1 MiB body cap
// (HostLoad.Transport). --json prints exactly one compact object (the server's answer with "placement" added; its values keep
// the server's text); otherwise four text lines. A failed read, placement or validation prints no stdout, one fixed stderr
// line (remote bodies and exception text can echo credentials) and exits 1. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>The host-load command's options (host-load.ps1 -HostId -Project -Role -Json).</summary>
    public sealed class HostLoadOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "host-load [--host-id <runner>] [--project <guid>] [--role <role>] [--json]";

        /// <summary>The runner to read; empty: the live global runner default.</summary>
        public string HostId { get; init; } = "";

        /// <summary>The project scope (a GUID); empty: host only.</summary>
        public string Project { get; init; } = "";

        /// <summary>The role scope; empty: host only.</summary>
        public string Role { get; init; } = "";

        /// <summary>One compact JSON object instead of the text lines.</summary>
        public bool Json { get; init; }

        /// <summary>The API base (ANTIPHON_API; default http://localhost:17202), without a trailing slash.</summary>
        public string Api { get; init; } = "http://localhost:17202";

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: no token header.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>The arguments after the command name; the API and the token come from the CLI.</summary>
        public static HostLoadOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--host-id", "--project", "--role" }, new[] { "--json" });
            return new HostLoadOptions
            {
                HostId = flags.Value("--host-id", ""),
                Project = flags.Value("--project", ""),
                Role = flags.Value("--role", ""),
                Json = flags.Switch("--json"),
                Api = (string.IsNullOrEmpty(api) ? "http://localhost:17202" : api).TrimEnd('/'),
                TaskToken = taskToken ?? "",
            };
        }
    }

    /// <summary>The host-load read (host-load.ps1).</summary>
    public static class HostLoad
    {
        /// <summary>Each read's deadline.</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

        /// <summary>The largest body a read accepts.</summary>
        public const int BodyLimit = 1048576;

        /// <summary>The one stderr line of every failure.</summary>
        public const string FailureLine = "host-load: read, placement or response validation failed; no capacity suggestion is available.";

        private static readonly string[] LoadFields = { "schemaVersion", "hostId", "physicalHostKey", "observedAt", "current", "currentAgeSeconds", "lastHour", "coverage",
            "policy", "authority", "decision", "recommendation", "counters" };
        private static readonly string[] PolicyFields = { "mode", "revision", "shadow", "commandThresholdPercent", "dispatchThresholdPercent", "perTaskCpuPoints" };
        private static readonly string[] CounterFields = { "countedAt", "tasksInFlight", "byStage", "byKind", "pendingLaunches", "pendingMirrors", "queued", "held",
            "sessionsLive", "effectiveTaskCapacity" };
        private static readonly string[] SummaryFields = { "avg", "min", "max", "p95", "sum", "validCount", "expectedCount", "coverage", "complete", "p95Approximate",
            "p95ErrorBound" };
        private static readonly string[] CurrentFields = { "schemaVersion", "source", "sampledAt", "cpuPercent", "hostLogicalCores", "memory", "swap", "processes",
            "unknownReasons", "authority" };
        private static readonly string[] SourceFields = { "runnerId", "runnerStoreId", "bootId", "sequence" };
        private static readonly string[] RecommendationFields = { "recommendedAdditionalTasks", "scope", "missingConstraints", "pressurePercent", "startupDebtPercent",
            "cpuHeadroom", "hostRemaining", "runnerSeatsRemaining", "projectRemaining", "stageRemaining", "inputsAt", "reasons" };

        /// <summary>
        /// The production transport: one HttpClient, the request read with response headers first, every read bounded by
        /// Deadline and BodyLimit, the body decoded as strict UTF-8. A read over the limit, invalid UTF-8 or a transport failure
        /// throws. handler replaces the network (tests).
        /// </summary>
        public static NightlyReportHttp Transport(HttpMessageHandler handler = null)
        {
            var client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            client.Timeout = Timeout.InfiniteTimeSpan;
            var strict = new UTF8Encoding(false, true);
            return (method, uri, headers, body) =>
            {
                using var deadline = new CancellationTokenSource(Deadline);
                using var request = new HttpRequestMessage(new HttpMethod(method), uri);
                foreach (var header in headers ?? new Dictionary<string, string>()) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                using var response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).GetAwaiter().GetResult();
                using var stream = response.Content.ReadAsStreamAsync(deadline.Token).GetAwaiter().GetResult();
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int read;
                while ((read = stream.ReadAsync(chunk, 0, chunk.Length, deadline.Token).GetAwaiter().GetResult()) != 0)
                {
                    if (buffer.Length + read > BodyLimit) throw new InvalidDataException("read_failed");
                    buffer.Write(chunk, 0, read);
                }
                return new NightlyReportHttpResponse((int)response.StatusCode, strict.GetString(buffer.ToArray()));
            };
        }

        /// <summary>
        /// Reads the load and writes the answer on host.Out (one compact JSON object with --json, else four text lines). Exit 0;
        /// 1 after FailureLine on host.Error, with nothing on host.Out.
        /// </summary>
        public static int Run(HostLoadOptions options, NightlyHost host, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (http == null) throw new ArgumentNullException(nameof(http));
            string text;
            try
            {
                text = Read(options, http);
            }
            catch (Exception)
            {
                // Remote bodies and exception text can echo credentials; neither is diagnostic output.
                host.Error.WriteLine(FailureLine);
                return 1;
            }
            host.Out.Write(text);
            return 0;
        }

        private static string Read(HostLoadOptions options, NightlyReportHttp http)
        {
            if (!Uri.TryCreate(options.Api, UriKind.Absolute, out var api) || (api.Scheme != "http" && api.Scheme != "https") || api.UserInfo.Length > 0)
                throw new InvalidDataException("invalid_api");
            var project = options.Project ?? "";
            if (project.Length > 0)
            {
                if (!Guid.TryParse(project, out var parsed)) throw new InvalidDataException("invalid_project");
                project = parsed.ToString("D");
            }
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(options.TaskToken)) headers["X-Antiphon-Task-Token"] = options.TaskToken;
            JsonNode Get(string path)
            {
                var answer = http("GET", options.Api + path, headers, null);
                if (answer.Status < 200 || answer.Status > 299) throw new InvalidDataException("read_failed");
                return JsonNode.Parse(answer.Body ?? "");
            }

            var hostId = options.HostId ?? "";
            JsonNode revision = null;
            string source;
            if (string.IsNullOrWhiteSpace(hostId))
            {
                var defaults = Get("/api/runner-defaults");
                Require(defaults, "globalRunnerId", "revision");
                var global = Plain(defaults["globalRunnerId"]);
                if (string.IsNullOrWhiteSpace(global)) throw new InvalidDataException("placement_missing");
                hostId = global;
                revision = defaults["revision"]?.DeepClone();
                source = "globalDefault";
            }
            else source = "explicit";

            var catalogue = Get("/api/session-runners");
            var rows = catalogue is JsonArray array ? array.ToArray() : catalogue == null ? Array.Empty<JsonNode>() : new[] { catalogue };
            var canonical = Canonical(hostId);
            var matches = rows.Where(row => string.Equals(Canonical(Plain(row is JsonObject o ? o["runnerId"] : null)), canonical, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1 || string.IsNullOrWhiteSpace(Plain(((JsonObject)matches[0])["runnerId"]))) throw new InvalidDataException("placement_missing");
            var runner = Plain(((JsonObject)matches[0])["runnerId"]);

            var query = new List<string>();
            if (project.Length > 0) query.Add("projectId=" + Uri.EscapeDataString(project));
            if (!string.IsNullOrEmpty(options.Role)) query.Add("role=" + Uri.EscapeDataString(options.Role));
            var path = "/api/hosts/" + Uri.EscapeDataString(runner) + "/load";
            if (query.Count > 0) path += "?" + string.Join("&", query);
            var load = Get(path);
            Require(load, LoadFields);
            var loadObject = (JsonObject)load;
            if (!IsNumber(loadObject["schemaVersion"], 1) || !string.Equals(Canonical(Plain(loadObject["hostId"])), canonical, StringComparison.Ordinal))
                throw new InvalidDataException("invalid_response");
            var policy = loadObject["policy"];
            Require(policy, PolicyFields);
            Require(loadObject["counters"], CounterFields);
            var lastHour = loadObject["lastHour"];
            Require(lastHour, "cpuPercent");
            var summary = lastHour["cpuPercent"];
            Require(summary, SummaryFields);
            var current = loadObject["current"];
            if (current != null)
            {
                Require(current, CurrentFields);
                Require(current["source"], SourceFields);
            }
            var recommendation = loadObject["recommendation"];
            Require(recommendation, RecommendationFields);
            var additional = recommendation["recommendedAdditionalTasks"];
            if (!(additional is JsonValue additionalValue) || additionalValue.GetValueKind() != JsonValueKind.Number) throw new InvalidDataException("invalid_response");
            var tasks = additionalValue.GetValue<double>();
            if (!double.IsFinite(tasks) || tasks < 0 || tasks > int.MaxValue || Math.Floor(tasks) != tasks) throw new InvalidDataException("invalid_response");
            if (!(policy["shadow"] is JsonValue shadow) || shadow.GetValueKind() != JsonValueKind.True) throw new InvalidDataException("invalid_response");
            if (tasks > 0 && (current == null || recommendation["pressurePercent"] == null)) throw new InvalidDataException("invalid_response");

            loadObject["placement"] = new JsonObject { ["runnerId"] = runner, ["source"] = source, ["revision"] = revision };
            if (options.Json) return loadObject.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) + Environment.NewLine;

            var lines = new StringBuilder();
            var cpu = current != null ? Number(current["cpuPercent"]) : "unknown";
            lines.AppendLine("Host " + runner + "; age=" + Number(loadObject["currentAgeSeconds"]) + "s; mode=" + Plain(policy["mode"]) + "; shadow=" + Plain(policy["shadow"]));
            lines.AppendLine("Current CPU=" + cpu + "%; load1=" + Number(current?["load1"]) + "; hour avg/min/max/p95=" + Number(summary["avg"]) + "/" + Number(summary["min"])
                + "/" + Number(summary["max"]) + "/" + Number(summary["p95"]) + "; coverage=" + Number(loadObject["coverage"]) + "; approximate=" + Plain(summary["p95Approximate"]));
            lines.AppendLine("MORE tasks=" + Plain(additional) + "; scope=" + Plain(recommendation["scope"]) + "; pressure=" + Number(recommendation["pressurePercent"])
                + "; startup debt=" + Number(recommendation["startupDebtPercent"]) + "; CPU headroom=" + Plain(recommendation["cpuHeadroom"]) + "; host/seats/project/stage="
                + Number(recommendation["hostRemaining"]) + "/" + Number(recommendation["runnerSeatsRemaining"]) + "/" + Number(recommendation["projectRemaining"]) + "/"
                + Number(recommendation["stageRemaining"]));
            var authority = loadObject["authority"] as JsonObject;
            lines.AppendLine("Thresholds command/dispatch=" + Plain(policy["commandThresholdPercent"]) + "/" + Plain(policy["dispatchThresholdPercent"]) + "; heavy ceiling="
                + Number(authority?["hardCeiling"]) + "; authority occupancy=" + Number(authority?["occupied"]) + "; missing=" + Joined(recommendation["missingConstraints"])
                + "; reasons=" + Joined(recommendation["reasons"]));
            return lines.ToString();
        }

        // Get-CanonicalRunner: local and desktop are one runner.
        private static string Canonical(string value) =>
            string.Equals(value, "local", StringComparison.Ordinal) || string.Equals(value, "desktop", StringComparison.Ordinal) ? "desktop" : value ?? "";

        // Require-Fields: an object holding every field (a null value counts).
        private static void Require(JsonNode node, params string[] fields)
        {
            if (!(node is JsonObject obj)) throw new InvalidDataException("invalid_response");
            foreach (var field in fields)
                if (!obj.ContainsKey(field)) throw new InvalidDataException("invalid_response");
        }

        private static bool IsNumber(JsonNode node, double expected) =>
            node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.GetValue<double>() == expected;

        // Show-Number: null is 'unknown'; a value its invariant text.
        private static string Number(JsonNode node) => node == null ? "unknown" : Plain(node);

        // A value as the script formatted it: a string as is, a number invariant, a boolean True or False, null empty.
        private static string Plain(JsonNode node)
        {
            if (node == null) return "";
            if (!(node is JsonValue value)) return node.ToJsonString();
            switch (value.GetValueKind())
            {
                case JsonValueKind.String: return value.GetValue<string>();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Number:
                    var raw = value.ToJsonString();
                    if (long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)) return whole.ToString(CultureInfo.InvariantCulture);
                    return value.GetValue<double>().ToString(CultureInfo.InvariantCulture);
                default: return "";
            }
        }

        private static string Joined(JsonNode node) =>
            node is JsonArray items ? string.Join(",", items.Select(Plain)) : Plain(node);
    }
}
