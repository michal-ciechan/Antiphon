// CARD-0701: the SELECT-only desktop statistics capture of the session-state cache, and its offline comparison. It replaced
// scripts/measure-session-state-cache.ps1, deleted 2026-11-24 (unit 33 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
//
//   antiphon-scripts measure-session-state-cache --evidence-root <dir> [--mode Capture|Compare|Acceptance]
//       [--phase Before|After] [--round R1|R2|R3] [--plan-card <card>] [--baseline-path <file>] [--after-path <file>]
//       [--context-path <file>] [--api <url>] [--postgres-container <name>] [--database <name>] [--database-user <name>]
//       [--warm-seconds <60..3600>] [--window-seconds <180..3600>]
//
// (scripts/antiphon measure-session-state-cache on sh, scripts\antiphon.cmd measure-session-state-cache on Windows). It never
// resets shared statistics, changes settings, activates a build or generates session traffic; Compare works offline.
//
// Capture (Windows only: the canonical desktop lane) needs a sanitized --context-path JSON with openClients, workloadKey and
// siblingShas (the observed deployed SHAs of CARD-0696/0698/0699/0700); before capture on an older build it also supplies
// the runtime observations (identity, live/unknown counts, driver/provider versions, pool flags) the pre-feature 404 of
// /api/diagnostics/session-state falls back to. It reads the canonical checkout's HEAD (the first `git worktree list` entry
// of this checkout), refuses an After phase whose /api/version is not that HEAD, waits --warm-seconds, then records two
// windows of --window-seconds: a snapshot every 30 s (GET /api/version and the runtime, `docker exec <container> psql` of
// StatisticsSql, the container's identity and CPU), each written once (FileMode.CreateNew) under a new
// <evidence-root>/<phase>-<guid>/ directory, then capture.json; a Before capture is also the immutable --baseline-path
// (default <evidence-root>/baseline.json). Compare reads the baseline and --after-path and writes
// <evidence-root>/compare-<guid>.json; Acceptance captures an After phase against the saved baseline and writes
// comparison.json beside it. Never put credentials, connection strings or transcript text in context or evidence.
//
// Exit 0 done (Compare and Acceptance: both gates passed); 1 a gate failed (the summary is still written) or the run was
// refused (one host.Error line: invalid or incompatible evidence, a window invalidated by a restart, reset or negative
// delta, a missing context, a failed observation). Usage errors are NightlyUsageException (the CLI's exit 2). The API reads
// carry the task token the CLI passes in (X-Antiphon-Task-Token) and never print it. HTTP is the seams' transport, docker and
// git the host's launcher (30 s each, output captured, never printed), the process sample the seams', files IFileSystem's,
// the clock and the waits the host's, the lines host.Out's. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The measure-session-state-cache command's options (measure-session-state-cache.ps1's parameters).</summary>
    public sealed class SessionStateCacheOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "measure-session-state-cache --evidence-root <dir> [--mode Capture|Compare|Acceptance] [--phase Before|After] [--round R1|R2|R3]"
            + " [--plan-card <card>] [--baseline-path <file>] [--after-path <file>] [--context-path <file>] [--api <url>] [--postgres-container <name>]"
            + " [--database <name>] [--database-user <name>] [--warm-seconds <60..3600>] [--window-seconds <180..3600>]";

        /// <summary>The modes, in their written spelling.</summary>
        public static readonly IReadOnlyList<string> Modes = new[] { "Capture", "Compare", "Acceptance" };

        /// <summary>The phases.</summary>
        public static readonly IReadOnlyList<string> Phases = new[] { "Before", "After" };

        /// <summary>The rounds.</summary>
        public static readonly IReadOnlyList<string> Rounds = new[] { "R1", "R2", "R3" };

        public string Mode { get; init; } = "Capture";
        public string Phase { get; init; } = "After";
        public string Round { get; init; } = "R3";
        public string PlanCard { get; init; } = "CARD-0701";
        public string EvidenceRoot { get; init; } = "";

        /// <summary>The baseline; empty: &lt;EvidenceRoot&gt;/baseline.json.</summary>
        public string BaselinePath { get; init; } = "";
        public string AfterPath { get; init; } = "";
        public string ContextPath { get; init; } = "";

        /// <summary>The API base, without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token sent as X-Antiphon-Task-Token (empty: none).</summary>
        public string TaskToken { get; init; } = "";
        public string PostgresContainer { get; init; } = "antiphon-postgres";
        public string Database { get; init; } = "antiphon";
        public string DatabaseUser { get; init; } = "antiphon";
        public int WarmSeconds { get; init; } = 60;
        public int WindowSeconds { get; init; } = 180;

        /// <summary>The checkout whose worktree list names the canonical checkout.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The baseline path the run reads and writes.</summary>
        public string Baseline => string.IsNullOrEmpty(BaselinePath) ? Path.Combine(EvidenceRoot, "baseline.json") : BaselinePath;

        /// <summary>The flags after the command name; api, token and the checkout come from the CLI.</summary>
        public static SessionStateCacheOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--mode", "--phase", "--round", "--plan-card", "--evidence-root", "--baseline-path", "--after-path",
                "--context-path", "--api", "--postgres-container", "--database", "--database-user", "--warm-seconds", "--window-seconds" }, Array.Empty<string>());
            string Choice(string flag, IReadOnlyList<string> choices, string fallback)
            {
                var given = flags.Value(flag, "");
                if (given.Length == 0) return fallback;
                return choices.FirstOrDefault(c => string.Equals(c, given, StringComparison.OrdinalIgnoreCase))
                    ?? throw new NightlyUsageException(flag + " expects " + string.Join(", ", choices) + ", not '" + given + "'");
            }
            int Range(string flag, int fallback, int min, int max)
            {
                var value = flags.Int(flag, fallback);
                if (value < min || value > max) throw new NightlyUsageException(flag + " must be between " + min + " and " + max);
                return value;
            }
            string Text(string flag, string fallback) => flags.Value(flag, "") is { Length: > 0 } given ? given : fallback;
            return new SessionStateCacheOptions
            {
                Mode = Choice("--mode", Modes, "Capture"),
                Phase = Choice("--phase", Phases, "After"),
                Round = Choice("--round", Rounds, "R3"),
                PlanCard = Text("--plan-card", "CARD-0701"),
                EvidenceRoot = flags.Required("--evidence-root"),
                BaselinePath = flags.Value("--baseline-path", ""),
                AfterPath = flags.Value("--after-path", ""),
                ContextPath = flags.Value("--context-path", ""),
                Api = OperatorApi.Base(Text("--api", api)),
                TaskToken = taskToken ?? "",
                PostgresContainer = Text("--postgres-container", "antiphon-postgres"),
                Database = Text("--database", "antiphon"),
                DatabaseUser = Text("--database-user", "antiphon"),
                WarmSeconds = Range("--warm-seconds", 60, 60, 3600),
                WindowSeconds = Range("--window-seconds", 180, 180, 3600),
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>One observation of a process (Get-Process): its CPU time, start and working set.</summary>
    public sealed record SessionStateProcessSample(double CpuSeconds, DateTime StartTimeUtc, long WorkingSetBytes);

    /// <summary>The capture's outside reads.</summary>
    public sealed class SessionStateCacheSeams
    {
        /// <summary>The API transport (15 s per read in production).</summary>
        public NightlyReportHttp Http { get; init; }

        /// <summary>A process by id; null when there is none.</summary>
        public Func<int, SessionStateProcessSample> Process { get; init; }

        /// <summary>True on Windows: capture runs on the canonical desktop lane only.</summary>
        public bool IsWindows { get; init; }
    }

    /// <summary>A refusal: the evidence or the window cannot be used. One host.Error line, exit 1.</summary>
    public sealed class SessionStateCacheRefusal : Exception
    {
        public SessionStateCacheRefusal(string message) : base(message) { }
    }

    /// <summary>The session-state cache measurement (measure-session-state-cache.ps1).</summary>
    public static class SessionStateCacheMeasure
    {
        /// <summary>
        /// The statistics read (Get-StatisticsSql): fingerprints and classified shapes only, never query parameters or bodies.
        /// </summary>
        public const string StatisticsSql = @"SELECT json_build_object(
 'recordedAt', clock_timestamp(), 'postmasterStartedAt', pg_postmaster_start_time(),
 'databaseOid', (SELECT oid FROM pg_database WHERE datname = current_database()),
 'info', (SELECT row_to_json(i) FROM pg_stat_statements_info i),
 'pendingQueues', (SELECT count(*) FROM ""SessionQueuedMessages"" WHERE ""Status"" = 0),
 'openTasks', (SELECT count(*) FROM ""AgentTasks"" WHERE ""Status"" IN (0,1,2,3)),
 'statements', COALESCE((SELECT json_agg(x) FROM (
   SELECT dbid, userid, queryid::text, toplevel, calls, rows, total_exec_time,
     md5(query) AS fingerprint,
     CASE
       WHEN query LIKE '%session-state.seed%' THEN 'state-seed'
       WHEN query LIKE '%session-state.fallback%' THEN 'working'
       WHEN query LIKE '%session-state.identity%' THEN 'uuid'
       WHEN query LIKE '%session-state.binding%' THEN 'binding'
       WHEN query LIKE '%session-state.pins%' THEN 'state-pins'
       WHEN query ~* '^DISCARD' THEN 'reset'
       WHEN query LIKE '%""TranscriptEntries""%' AND query ~* '\mINSERT\M' THEN 'transcript-insert'
       WHEN query LIKE '%""TranscriptEntries""%' AND (query LIKE '%end_seq%' OR
         (query LIKE '%""TurnEnd""%' AND query LIKE '%EXISTS%')) THEN 'working'
       WHEN query LIKE '%""TranscriptEntries""%' AND query LIKE '%""Uuid""%' AND query LIKE '%ANY%' THEN 'uuid'
       WHEN query LIKE '%""TranscriptEntries""%' AND query ~* '\mSELECT\M' THEN 'transcript-unknown'
       WHEN query ~ '^SELECT [a-z0-9_]+\.""RunnerId"", [a-z0-9_]+\.""RunnerStoreId"", [a-z0-9_]+\.""RunnerCwd""[[:space:]]+FROM ""AgentSessions""' THEN 'binding'
       WHEN query LIKE '%""Boards""%' AND query LIKE '%""ProjectId""%' THEN 'boards'
       WHEN query LIKE '%""AgentTasks""%' AND query LIKE '%CompletionNote%' AND query ~* '\mUPDATE\M' THEN 'completion-stamp'
       ELSE 'other'
     END AS shape,
     (query LIKE '%""TranscriptEntries""%' AND query ~* '\mSELECT\M' AND query !~* '\mINSERT\M') AS transcript_select
   FROM pg_stat_statements WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
 ) x), '[]'::json))::text;
";

        /// <summary>Each docker or git observation's bound.</summary>
        public const int ToolTimeoutMilliseconds = 30000;

        private static readonly string[] Siblings = { "CARD-0696", "CARD-0698", "CARD-0699", "CARD-0700" };
        private static readonly Regex Sha = new Regex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly JsonNodeOptions IgnoreCase = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions Indented = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>Runs the mode; see the header for the exit codes.</summary>
        public static int Run(SessionStateCacheOptions options, NightlyHost host, IFileSystem files, SessionStateCacheSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            try
            {
                return RunChecked(options, host, files, seams);
            }
            catch (Exception e) when (e is SessionStateCacheRefusal || e is IOException || e is UnauthorizedAccessException || e is JsonException
                || e is InvalidOperationException || e is FormatException || e is System.Net.Http.HttpRequestException || e is OperationCanceledException)
            {
                host.Error.WriteLine(e.Message);
                return 1;
            }
        }

        private static int RunChecked(SessionStateCacheOptions o, NightlyHost host, IFileSystem files, SessionStateCacheSeams seams)
        {
            files.Directory.CreateDirectory(o.EvidenceRoot);
            var phase = o.Phase;
            if (o.Mode == "Acceptance")
            {
                if (!files.File.Exists(o.Baseline)) throw Refuse("Acceptance requires a previously saved baseline; the changed build cannot become before.");
                var baseline = ReadJson(files, o.Baseline);
                AssertBaseline(baseline, o.PlanCard);
                foreach (var window in Items(Get(baseline, "windows"))) WindowDelta(Get(window, "start"), Get(window, "end"));
            }
            if (o.Mode == "Compare")
            {
                if (string.IsNullOrEmpty(o.AfterPath)) throw Refuse("Compare requires AfterPath.");
                var summary = Compare(ReadJson(files, o.Baseline), ReadJson(files, o.AfterPath), o.Round, o.PlanCard);
                var output = Path.Combine(o.EvidenceRoot, "compare-" + Guid.NewGuid().ToString("N") + ".json");
                SaveImmutable(files, output, summary);
                host.Out.WriteLine("Comparison saved: " + output);
                return Passed(summary) ? 0 : 1;
            }
            if (o.Mode == "Acceptance") phase = "After";
            if (phase == "Before" && files.File.Exists(o.Baseline)) throw Refuse("Baseline already exists; select a new evidence root.");
            var destination = Path.Combine(o.EvidenceRoot, phase.ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N"));
            files.Directory.CreateDirectory(destination);
            var capture = Capture(o, phase, destination, host, files, seams);
            if (phase == "Before") SaveImmutable(files, o.Baseline, capture);
            host.Out.WriteLine("Capture saved: " + destination);
            if (o.Mode == "Acceptance")
            {
                var summary = Compare(ReadJson(files, o.Baseline), capture.DeepClone(), o.Round, o.PlanCard);
                SaveImmutable(files, Path.Combine(destination, "comparison.json"), summary);
                if (!Passed(summary)) return 1;
            }
            return 0;
        }

        private static bool Passed(JsonObject summary) => Truthy(Get(summary, "queryAndWorkloadGates")) && Truthy(Get(summary, "cpuGate"));

        private static SessionStateCacheRefusal Refuse(string message) => new SessionStateCacheRefusal(message);

        // ---- values (ConvertFrom-Json -AsHashtable readings) ----------------------------------------------------------------

        private static JsonNode Get(JsonNode node, string key) => node is JsonObject o && o.TryGetPropertyValue(key, out var value) ? value : null;

        private static bool Has(JsonNode node, string key) => node is JsonObject o && o.ContainsKey(key);

        private static IEnumerable<JsonNode> Items(JsonNode node) => node is JsonArray a ? a : node == null ? Enumerable.Empty<JsonNode>() : new[] { node };

        private static int Count(JsonNode node) => node is JsonArray a ? a.Count : node == null ? 0 : 1;

        private static bool IsNumber(JsonNode node, out double value)
        {
            value = 0;
            if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.Number) return false;
            if (v.TryGetValue<double>(out var d)) value = d;
            else if (v.TryGetValue<long>(out var l)) value = l;
            else if (v.TryGetValue<int>(out var i)) value = i;
            else if (v.TryGetValue<decimal>(out var m)) value = (double)m;
            else return false;
            return true;
        }

        // [double]$x: a number or a numeric string; 0 when absent.
        private static double Num(JsonNode node)
        {
            if (IsNumber(node, out var d)) return d;
            var text = Str(node);
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        }

        // [long]$x (rounded half to even).
        private static long Long(JsonNode node) => Convert.ToInt64(Num(node), CultureInfo.InvariantCulture);

        // [string]$x for a scalar.
        private static string Str(JsonNode node)
        {
            if (node is not JsonValue v) return node == null ? "" : node.ToJsonString();
            switch (v.GetValueKind())
            {
                case JsonValueKind.String: return v.GetValue<string>();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Number: return v.TryGetValue<long>(out var whole) ? whole.ToString(CultureInfo.InvariantCulture) : Num(v).ToString("R", CultureInfo.InvariantCulture);
                default: return "";
            }
        }

        // [bool]$x: false for null, false, 0, "" and an empty array; true otherwise.
        private static bool Truthy(JsonNode node)
        {
            if (node == null) return false;
            if (node is JsonArray a) return a.Count > 0;
            if (node is JsonObject) return true;
            switch (((JsonValue)node).GetValueKind())
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Number: return Num(node) != 0;
                case JsonValueKind.String: return Str(node).Length > 0;
                default: return false;
            }
        }

        // a -eq b of two scalars: numerically when both are numbers, else as text ignoring case; null equals null only.
        private static bool Same(JsonNode a, JsonNode b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (IsNumber(a, out var x) && IsNumber(b, out var y)) return x == y;
            return string.Equals(Str(a), Str(b), StringComparison.OrdinalIgnoreCase);
        }

        private static JsonObject ReadJson(IFileSystem files, string path)
        {
            if (!files.File.Exists(path)) throw Refuse("Missing evidence file " + path + ".");
            return JsonNode.Parse(files.File.ReadAllText(path), IgnoreCase) as JsonObject ?? throw Refuse("Evidence file " + path + " is not a JSON object.");
        }

        /// <summary>Save-Immutable: the value as indented JSON (UTF-8, no byte-order mark), created new; an existing file refuses (IOException).</summary>
        public static void SaveImmutable(IFileSystem files, string path, JsonNode value)
        {
            var bytes = Encoding.UTF8.GetBytes(value == null ? "null" : value.ToJsonString(Indented));
            using var stream = files.FileStream.New(path, FileMode.CreateNew, FileAccess.Write);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static JsonObject Obj(params (string Name, JsonNode Value)[] properties)
        {
            var o = new JsonObject();
            foreach (var (name, value) in properties) o[name] = value;
            return o;
        }

        // ---- the window delta -----------------------------------------------------------------------------------------------

        private static void AssertFields(JsonNode value, params string[] fields)
        {
            foreach (var field in fields)
                if (value is not JsonObject o || !o.TryGetPropertyValue(field, out var v) || v == null)
                    throw Refuse("Missing required observation: " + field + ".");
        }

        private static DateTimeOffset Instant(JsonNode node)
        {
            var text = Str(node);
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant))
                throw Refuse("Unreadable observation time '" + text + "'.");
            return instant;
        }

        private static readonly string[] ReadShapes = { "working", "uuid", "state-seed", "state-pins" };
        private static readonly string[] CacheMetrics = { "hits", "loads", "loadFaults", "evictions", "capacityFallbacks", "ingestCalls", "committedRows", "duplicateBatches" };
        private static readonly string[] EfPaths = { "seed", "pins", "fallback", "identity", "binding" };

        /// <summary>
        /// Get-WindowDelta: the counters between two snapshots of one server, PostgreSQL instance and statistics epoch. Refuses a
        /// window shorter than minimumSeconds, a restart, a reset or eviction, a vanished statement and a negative delta.
        /// </summary>
        public static JsonObject WindowDelta(JsonNode start, JsonNode end, double minimumSeconds = 180)
        {
            foreach (var point in new[] { start, end })
            {
                AssertFields(point, "utc", "runtime", "version", "postgres");
                AssertFields(Get(point, "runtime"), "processId", "processStartedAt", "cpuSeconds", "liveSessions", "unknownSessions");
                AssertFields(Get(point, "version"), "version");
                AssertFields(Get(point, "postgres"), "containerIdentity", "containerCpuSeconds", "statistics");
                var statistics = Get(Get(point, "postgres"), "statistics");
                AssertFields(statistics, "postmasterStartedAt", "databaseOid", "info", "statements");
                AssertFields(Get(statistics, "info"), "stats_reset", "dealloc");
                foreach (var row in Items(Get(statistics, "statements")))
                    AssertFields(row, "dbid", "userid", "queryid", "toplevel", "calls", "rows", "total_exec_time", "fingerprint", "shape", "transcript_select");
            }
            var seconds = (Instant(Get(end, "utc")) - Instant(Get(start, "utc"))).TotalSeconds;
            if (seconds <= 0 || seconds < minimumSeconds) throw Refuse("A statistics window is shorter than its required duration.");
            var startRuntime = Get(start, "runtime");
            var endRuntime = Get(end, "runtime");
            foreach (var field in new[] { "processId", "processStartedAt" })
                if (!string.Equals(Str(Get(startRuntime, field)), Str(Get(endRuntime, field)), StringComparison.OrdinalIgnoreCase)) throw Refuse("Server restarted inside the window.");
            if (!Same(Get(Get(start, "version"), "version"), Get(Get(end, "version"), "version"))) throw Refuse("Server build changed inside the window.");
            var a = Get(Get(start, "postgres"), "statistics");
            var b = Get(Get(end, "postgres"), "statistics");
            if (!Same(Get(Get(start, "postgres"), "containerIdentity"), Get(Get(end, "postgres"), "containerIdentity")) || !Same(Get(a, "postmasterStartedAt"), Get(b, "postmasterStartedAt")))
                throw Refuse("PostgreSQL restarted inside the window.");
            if (!Same(Get(Get(a, "info"), "stats_reset"), Get(Get(b, "info"), "stats_reset")) || !Same(Get(Get(a, "info"), "dealloc"), Get(Get(b, "info"), "dealloc"))
                || !Same(Get(a, "databaseOid"), Get(b, "databaseOid")))
                throw Refuse("Statistics reset, eviction, or database identity change invalidated the window.");
            string Key(JsonNode row) => Str(Get(row, "dbid")) + "/" + Str(Get(row, "userid")) + "/" + Str(Get(row, "queryid")) + "/" + Str(Get(row, "toplevel"));
            var prior = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Items(Get(a, "statements"))) prior[Key(row)] = row;
            var current = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var row in Items(Get(b, "statements")))
            {
                var key = Key(row);
                if (!current.ContainsKey(key)) order.Add(key);
                current[key] = row;
            }
            foreach (var key in prior.Keys) if (!current.ContainsKey(key)) throw Refuse("A statement disappeared inside the window.");
            var deltas = new JsonArray();
            long transcriptReads = 0, unknownReads = 0;
            foreach (var key in order)
            {
                var row = current[key];
                prior.TryGetValue(key, out var old);
                var calls = Long(Get(row, "calls"));
                var rows = Long(Get(row, "rows"));
                var execMs = Num(Get(row, "total_exec_time"));
                if (old != null)
                {
                    calls -= Long(Get(old, "calls"));
                    rows -= Long(Get(old, "rows"));
                    execMs -= Num(Get(old, "total_exec_time"));
                }
                if (calls < 0 || rows < 0 || execMs < 0) throw Refuse("Negative statistics delta invalidated the window.");
                var shape = Str(Get(row, "shape"));
                var transcriptSelect = Truthy(Get(row, "transcript_select"));
                // Old schema-1 content rows remain readable, but their unclassified work cannot certify zero.
                if (transcriptSelect)
                {
                    transcriptReads += calls;
                    if (!ReadShapes.Contains(shape, StringComparer.OrdinalIgnoreCase)) unknownReads += calls;
                }
                deltas.Add(Obj(("key", key), ("fingerprint", Get(row, "fingerprint")?.DeepClone()), ("shape", shape), ("calls", calls), ("rows", rows),
                    ("execMs", execMs), ("callsPerSecond", calls / seconds), ("transcriptSelect", Get(row, "transcript_select")?.DeepClone())));
            }
            var serverCpu = (Num(Get(endRuntime, "cpuSeconds")) - Num(Get(startRuntime, "cpuSeconds"))) / seconds;
            var postgresCpu = (Num(Get(Get(end, "postgres"), "containerCpuSeconds")) - Num(Get(Get(start, "postgres"), "containerCpuSeconds"))) / seconds;
            if (serverCpu < 0 || postgresCpu < 0) throw Refuse("Negative CPU delta invalidated the window.");
            JsonObject cacheDelta = null;
            JsonNode perIngest = null;
            if (Has(startRuntime, "cache") && Has(endRuntime, "cache"))
            {
                var startCache = Get(startRuntime, "cache");
                var endCache = Get(endRuntime, "cache");
                if (!Same(Get(startCache, "serverEpoch"), Get(endCache, "serverEpoch"))) throw Refuse("Cache epoch changed inside the window.");
                cacheDelta = new JsonObject();
                foreach (var metric in CacheMetrics)
                {
                    var delta = Long(Get(endCache, metric)) - Long(Get(startCache, metric));
                    if (delta < 0) throw Refuse("Negative cache counter delta invalidated the window.");
                    cacheDelta[metric] = delta;
                }
                var ingest = (long)cacheDelta["ingestCalls"];
                if (ingest > 0) perIngest = (double)transcriptReads / ingest;
            }
            JsonObject efDelta = null;
            if (Has(startRuntime, "efReadAttempts") && Has(endRuntime, "efReadAttempts"))
            {
                efDelta = new JsonObject();
                foreach (var path in EfPaths)
                {
                    var delta = Long(Get(Get(endRuntime, "efReadAttempts"), path)) - Long(Get(Get(startRuntime, "efReadAttempts"), path));
                    if (delta < 0) throw Refuse("Negative EF counter delta invalidated the window.");
                    efDelta[path] = delta;
                }
            }
            return Obj(("seconds", seconds), ("serverCores", serverCpu), ("postgresContainerCores", postgresCpu), ("statements", deltas),
                ("transcriptSelectCalls", transcriptReads), ("unknownTranscriptSelectCalls", unknownReads), ("classificationComplete", unknownReads == 0),
                ("cacheDelta", cacheDelta), ("efReadAttemptDelta", efDelta), ("transcriptSelectsPerIngest", perIngest));
        }

        private static double Rate(JsonNode delta, string shape)
        {
            long calls = 0;
            foreach (var statement in Items(Get(delta, "statements")))
                if (string.Equals(Str(Get(statement, "shape")), shape, StringComparison.OrdinalIgnoreCase)) calls += Long(Get(statement, "calls"));
            return calls / Num(Get(delta, "seconds"));
        }

        // Completed inserts describe natural transcript activity, not the order of capture windows.
        private static string ObservedWorkload(JsonNode delta) => Rate(delta, "transcript-insert") > 0 ? "activity" : "idle";

        // The effective pool settings and driver/provider versions as one comparable key; null when any is missing or invalid.
        private static string ConfigurationKey(JsonNode runtime)
        {
            var pool = Get(runtime, "pool");
            if (pool is not JsonObject) return null;
            var values = new List<string>();
            foreach (var field in new[] { "pooling", "noResetOnClose", "maxAutoPrepare", "multiplexing" })
            {
                if (Get(pool, field) is not JsonValue value) return null;
                var kind = value.GetValueKind();
                if (field == "maxAutoPrepare")
                {
                    if (kind != JsonValueKind.Number || !value.TryGetValue<long>(out var count) || count < 0) return null;
                    values.Add(count.ToString(CultureInfo.InvariantCulture));
                }
                else if (kind == JsonValueKind.True || kind == JsonValueKind.False) values.Add(Str(value));
                else return null;
            }
            foreach (var field in new[] { "driverVersion", "providerVersion" })
            {
                var text = Str(Get(runtime, field));
                if (Get(runtime, field) == null || string.IsNullOrWhiteSpace(text)) return null;
                values.Add(text);
            }
            return JsonSerializer.Serialize(values);
        }

        // The sanitized workload observation as one comparable key; null when it is missing or invalid.
        private static string ObservationKey(JsonNode observation)
        {
            if (observation is not JsonObject) return null;
            foreach (var field in new[] { "openClients", "workloadKey", "siblingShas" })
                if (Get(observation, field) == null) return null;
            if (Num(Get(observation, "openClients")) < 0 || string.IsNullOrWhiteSpace(Str(Get(observation, "workloadKey")))) return null;
            var values = new List<string> { Str(Get(observation, "openClients")), Str(Get(observation, "workloadKey")) };
            foreach (var sibling in Siblings)
            {
                var sha = Str(Get(Get(observation, "siblingShas"), sibling));
                if (!Sha.IsMatch(sha)) return null;
                values.Add(sha);
            }
            return JsonSerializer.Serialize(values);
        }

        private static bool ValidSha(JsonNode node) => Sha.IsMatch(Str(node));

        private static bool SchemaKnown(JsonNode node) => IsNumber(node, out var v) && (v == 1 || v == 2);

        private static void AssertBaseline(JsonNode baseline, string planCard)
        {
            if (!SchemaKnown(Get(baseline, "schemaVersion")) || !string.Equals(Str(Get(baseline, "phase")), "Before", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Str(Get(baseline, "round")), "R1", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Str(Get(baseline, "card")), planCard, StringComparison.OrdinalIgnoreCase)
                || Count(Get(baseline, "windows")) != 2 || !ValidSha(Get(baseline, "canonicalSha")))
                throw Refuse("Missing or incompatible immutable R1 baseline identity.");
        }

        /// <summary>
        /// Compare-Evidence: the R1 baseline against an After capture of round, window by window. Attribution is invalid (with
        /// its reasons) when the workload, configuration, population or sampling cannot be matched; the query gates need the
        /// working (and, in R3, binding) rate down to 5% of before; the CPU gate needs less server CPU and no more PostgreSQL CPU
        /// than the higher before window. accepted is always false: the operator reviews the ordinary evidence.
        /// </summary>
        public static JsonObject Compare(JsonNode before, JsonNode after, string round, string planCard)
        {
            AssertBaseline(before, planCard);
            if (!SchemaKnown(Get(after, "schemaVersion")) || !string.Equals(Str(Get(after, "phase")), "After", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Str(Get(after, "round")), round, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Str(Get(after, "card")), planCard, StringComparison.OrdinalIgnoreCase)
                || Count(Get(after, "windows")) != 2 || !ValidSha(Get(after, "canonicalSha")))
                throw Refuse("Missing or incompatible immutable baseline/after evidence.");
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            var beforeWindows = Items(Get(before, "windows")).ToList();
            var afterWindows = Items(Get(after, "windows")).ToList();
            var configuration = ConfigurationKey(Get(Get(beforeWindows[0], "start"), "runtime"));
            var observation = ObservationKey(Get(before, "context"));
            if (observation == null || !string.Equals(ObservationKey(Get(after, "context")), observation, StringComparison.Ordinal))
                reasons.Add("workload_context_missing_or_changed");
            foreach (var windows in new[] { beforeWindows, afterWindows })
                if (windows.Count(w => string.Equals(Str(Get(w, "workload")), "idle", StringComparison.OrdinalIgnoreCase)) != 1
                    || windows.Count(w => string.Equals(Str(Get(w, "workload")), "activity", StringComparison.OrdinalIgnoreCase)) != 1)
                    reasons.Add("idle_and_activity_windows_required");
            var comparisons = new JsonArray();
            var allGates = true;
            var beforeCores = new List<double>();
            var afterCores = new List<double>();
            var beforePg = new List<double>();
            var afterPg = new List<double>();
            for (var i = 0; i < 2; i++)
            {
                var beforeWindow = beforeWindows[i];
                var afterWindow = afterWindows[i];
                var b = WindowDelta(Get(beforeWindow, "start"), Get(beforeWindow, "end"));
                var a = WindowDelta(Get(afterWindow, "start"), Get(afterWindow, "end"));
                var workload = Str(Get(beforeWindow, "workload"));
                if (!string.Equals(workload, Str(Get(afterWindow, "workload")), StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(workload, ObservedWorkload(b), StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Str(Get(afterWindow, "workload")), ObservedWorkload(a), StringComparison.OrdinalIgnoreCase))
                    reasons.Add("observed_workload_mismatch");
                var population = Get(Get(beforeWindow, "start"), "runtime");
                foreach (var window in new[] { beforeWindow, afterWindow })
                {
                    var points = new List<JsonNode> { Get(window, "start"), Get(window, "end") };
                    var samples = Items(Get(window, "samples")).ToList();
                    if (!Has(window, "samples") || samples.Count < 3) reasons.Add("intermediate_observations_missing");
                    else
                    {
                        if (!SameInstant(Get(samples[0], "utc"), Get(Get(window, "start"), "utc")) || !SameInstant(Get(samples[^1], "utc"), Get(Get(window, "end"), "utc")))
                            reasons.Add("sample_coverage_incomplete");
                        // Check continuity and counters at every interval, including restart-and-return.
                        for (var j = 1; j < samples.Count; j++)
                            if (Num(Get(WindowDelta(samples[j - 1], samples[j], 0), "seconds")) > 60) reasons.Add("sample_gap_over_60_seconds");
                        points.AddRange(samples);
                    }
                    foreach (var point in points)
                    {
                        var runtime = Get(point, "runtime");
                        var key = ConfigurationKey(runtime);
                        if (configuration == null || key == null || !string.Equals(key, configuration, StringComparison.Ordinal)) reasons.Add("effective_configuration_missing_or_changed");
                        if (Num(Get(runtime, "liveSessions")) < 10) reasons.Add("population_below_ten");
                        if (!Same(Get(runtime, "liveSessions"), Get(population, "liveSessions")) || !Same(Get(runtime, "unknownSessions"), Get(population, "unknownSessions")))
                            reasons.Add("population_changed");
                        var observedKey = Has(point, "observation") ? ObservationKey(Get(point, "observation")) : null;
                        if (observedKey == null || !string.Equals(observedKey, observation, StringComparison.Ordinal)) reasons.Add("workload_observation_missing_or_changed");
                        if (!Same(Get(Get(point, "version"), "version"), Get(Get(Get(window, "start"), "version"), "version"))) throw Refuse("Server build changed inside sampled window.");
                    }
                }
                if (!Same(Get(Get(Get(afterWindow, "start"), "version"), "version"), Get(after, "canonicalSha"))) throw Refuse("After served SHA differs from canonical source identity.");
                if (!Truthy(Get(b, "classificationComplete")) || !Truthy(Get(a, "classificationComplete"))) reasons.Add("unclassified_transcript_selects");
                var workingBefore = Rate(b, "working");
                var workingAfter = Rate(a, "working");
                var bindingBefore = Rate(b, "binding");
                var bindingAfter = Rate(a, "binding");
                var workingGate = round == "R3" ? workingBefore > 0 && workingAfter <= workingBefore * 0.05 : workingBefore > 0 && workingAfter < workingBefore;
                var bindingGate = round != "R3" || (bindingBefore > 0 && bindingAfter <= bindingBefore * 0.05);
                var cache = Get(a, "cacheDelta");
                if (Truthy(cache) && (Num(Get(cache, "loadFaults")) > 0 || Num(Get(cache, "capacityFallbacks")) > 0)) allGates = false;
                allGates = allGates && workingGate && bindingGate;
                beforeCores.Add(Num(Get(b, "serverCores")));
                afterCores.Add(Num(Get(a, "serverCores")));
                beforePg.Add(Num(Get(b, "postgresContainerCores")));
                afterPg.Add(Num(Get(a, "postgresContainerCores")));
                comparisons.Add(Obj(("workload", workload), ("workingBefore", workingBefore), ("workingAfter", workingAfter), ("workingPercentageGate", workingGate),
                    ("bindingPercentageGate", bindingGate), ("bindingBefore", bindingBefore), ("bindingAfter", bindingAfter), ("before", b), ("after", a)));
            }
            // Median of two samples is their midpoint. Keep raw windows for noise/third-window review.
            var cpuGate = afterCores.Average() < beforeCores.Average() && afterPg.Average() <= beforePg.Max();
            var attributionValid = reasons.Count == 0;
            allGates = allGates && attributionValid;
            return Obj(("schemaVersion", 2), ("round", round), ("attributionValid", attributionValid),
                ("attributionReasons", new JsonArray(reasons.Select(r => (JsonNode)r).ToArray())), ("queryAndWorkloadGates", allGates), ("cpuGate", cpuGate),
                ("comparisons", comparisons), ("ordinaryEvidence", "Required separately: CP query budgets, correctness, residual attribution and matching workload review"),
                ("accepted", false), ("disposition", allGates && cpuGate
                    ? "Measured gates passed; operator reviews ordinary evidence and workload match"
                    : "Inconclusive or failed; retain evidence and collect another matched window"));
        }

        private static bool SameInstant(JsonNode a, JsonNode b)
        {
            if (DateTimeOffset.TryParse(Str(a), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var x)
                && DateTimeOffset.TryParse(Str(b), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var y))
                return x == y;
            return Same(a, b);
        }

        // ---- capture --------------------------------------------------------------------------------------------------------

        private static JsonNode ReadApi(SessionStateCacheOptions o, SessionStateCacheSeams seams, string path, out int status)
        {
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(o.TaskToken)) headers["X-Antiphon-Task-Token"] = o.TaskToken;
            var response = seams.Http("GET", o.Api + path, headers, null);
            status = response.Status;
            if (status < 200 || status > 299) return null;
            return JsonNode.Parse(string.IsNullOrWhiteSpace(response.Body) ? "null" : response.Body, IgnoreCase);
        }

        private static JsonNode ReadApi(SessionStateCacheOptions o, SessionStateCacheSeams seams, string path)
        {
            var value = ReadApi(o, seams, path, out var status);
            if (status < 200 || status > 299) throw Refuse("GET " + path + " failed (HTTP " + status.ToString(CultureInfo.InvariantCulture) + ").");
            return value;
        }

        // The runtime observation. Only the documented pre-feature 404 (before capture) uses the context's runtime, refreshed
        // with the live process's CPU, start and working set.
        private static JsonNode ReadRuntime(SessionStateCacheOptions o, string phase, SessionStateCacheSeams seams, JsonNode context)
        {
            var runtime = ReadApi(o, seams, "/api/diagnostics/session-state", out var status);
            if (status >= 200 && status <= 299) return runtime;
            if (status != 404 || phase != "Before") throw Refuse("GET /api/diagnostics/session-state failed (HTTP " + status.ToString(CultureInfo.InvariantCulture) + ").");
            if (Get(context, "runtime") is not JsonObject observed) throw Refuse("Pre-feature capture requires context.runtime observations.");
            var pid = (int)Long(Get(observed, "processId"));
            var process = seams.Process(pid) ?? throw Refuse("No process " + pid.ToString(CultureInfo.InvariantCulture) + " for the context's runtime.processId.");
            var refreshed = (JsonObject)observed.DeepClone();
            refreshed["cpuSeconds"] = process.CpuSeconds;
            refreshed["processStartedAt"] = process.StartTimeUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            refreshed["workingSetBytes"] = process.WorkingSetBytes;
            return refreshed;
        }

        // Invoke-Bounded: the tool's trimmed stdout; its stderr and command are never printed.
        private static string Bounded(NightlyHost host, string file, params string[] arguments)
        {
            var result = host.Launch(new NightlyLaunch(file, arguments, "", ToolTimeoutMilliseconds, CaptureOutput: true));
            if (result.TimedOut) throw Refuse(file + " observation timed out.");
            if (result.ExitCode != 0) throw Refuse(file + " observation failed (exit " + result.ExitCode.ToString(CultureInfo.InvariantCulture) + "); no command payload is printed.");
            return (result.StandardOutputText ?? "").Trim();
        }

        private static JsonObject ReadStatistics(SessionStateCacheOptions o, NightlyHost host)
        {
            var raw = Bounded(host, "docker", "exec", o.PostgresContainer, "psql", "-X", "-A", "-t", "-v", "ON_ERROR_STOP=1", "-U", o.DatabaseUser, "-d", o.Database, "-c", StatisticsSql);
            var statistics = JsonNode.Parse(raw, IgnoreCase);
            var identity = Bounded(host, "docker", "inspect", "--format", "{{.Id}}|{{.State.StartedAt}}", o.PostgresContainer);
            var cpu = Bounded(host, "docker", "exec", o.PostgresContainer, "sh", "-c",
                "if [ -r /sys/fs/cgroup/cpu.stat ]; then cat /sys/fs/cgroup/cpu.stat; else cat /sys/fs/cgroup/cpuacct/cpuacct.usage; fi");
            double cpuSeconds;
            var usage = Regex.Match(cpu, @"(?m)^usage_usec\s+(\d+)");
            if (usage.Success) cpuSeconds = double.Parse(usage.Groups[1].Value, CultureInfo.InvariantCulture) / 1e6;
            else if (Regex.IsMatch(cpu, @"^\d+$")) cpuSeconds = double.Parse(cpu, CultureInfo.InvariantCulture) / 1e9;
            else throw Refuse("PostgreSQL container CPU accounting is unavailable.");
            return Obj(("statistics", statistics), ("containerIdentity", identity), ("containerCpuSeconds", cpuSeconds));
        }

        /// <summary>
        /// New-Snapshot: one sample. The context file is read again at every sample, so the pre-feature runtime fallback and
        /// the sanitized observation are this sample's, never the startup's.
        /// </summary>
        public static JsonObject Snapshot(SessionStateCacheOptions o, string phase, NightlyHost host, IFileSystem files, SessionStateCacheSeams seams)
        {
            var observed = ReadJson(files, o.ContextPath);
            var version = ReadApi(o, seams, "/api/version");
            var runtime = ReadRuntime(o, phase, seams, observed);
            var postgres = ReadStatistics(o, host);
            var observation = Obj(("openClients", Get(observed, "openClients")?.DeepClone()), ("workloadKey", Get(observed, "workloadKey")?.DeepClone()),
                ("siblingShas", Get(observed, "siblingShas")?.DeepClone()));
            return Obj(("utc", new DateTimeOffset(host.UtcNow).ToString("o", CultureInfo.InvariantCulture)), ("version", version?.DeepClone()),
                ("runtime", runtime?.DeepClone()), ("postgres", postgres), ("observation", observation));
        }

        private static void WaitObserved(NightlyHost host, int seconds, string label)
        {
            var remaining = seconds;
            while (remaining > 0)
            {
                host.Out.WriteLine(label + " - " + remaining.ToString(CultureInfo.InvariantCulture) + " seconds remaining (read-only; use natural workload).");
                var step = Math.Min(30, remaining);
                host.Sleep(TimeSpan.FromSeconds(step));
                remaining -= step;
            }
        }

        private static JsonObject Capture(SessionStateCacheOptions o, string phase, string destination, NightlyHost host, IFileSystem files, SessionStateCacheSeams seams)
        {
            if (!seams.IsWindows) throw Refuse("Production capture requires the canonical desktop lane; Compare is portable.");
            if (string.IsNullOrEmpty(o.ContextPath) || !files.File.Exists(o.ContextPath)) throw Refuse("Capture requires a sanitized context JSON (openClients, workloadKey, siblingShas).");
            var context = ReadJson(files, o.ContextPath);
            foreach (var key in new[] { "openClients", "workloadKey", "siblingShas" })
                if (!Has(context, key)) throw Refuse("Context is missing " + key + ".");
            if (Num(Get(context, "openClients")) < 0 || string.IsNullOrWhiteSpace(Str(Get(context, "workloadKey")))) throw Refuse("Context has invalid workload/client observations.");
            foreach (var sibling in Siblings)
                if (!ValidSha(Get(Get(context, "siblingShas"), sibling))) throw Refuse("Context requires the observed deployed SHA for " + sibling + ".");
            var worktrees = Bounded(host, "git", "-C", o.RepoRoot, "worktree", "list", "--porcelain");
            var canonical = Regex.Match(worktrees, "(?m)^worktree (.+)$").Groups[1].Value.Trim();
            if (canonical.Length == 0) throw Refuse("Canonical checkout is unavailable.");
            var sha = Bounded(host, "git", "-C", canonical, "rev-parse", "HEAD");
            var identity = ReadApi(o, seams, "/api/version");
            if (phase == "After" && !string.Equals(Str(Get(identity, "version")), sha, StringComparison.OrdinalIgnoreCase))
                throw Refuse("Activated /api/version does not equal canonical checkout HEAD.");
            WaitObserved(host, o.WarmSeconds, "Warm-up");
            var windows = new JsonArray();
            for (var windowNumber = 1; windowNumber <= 2; windowNumber++)
            {
                var start = Snapshot(o, phase, host, files, seams);
                SaveImmutable(files, Path.Combine(destination, "window-" + windowNumber + "-start.json"), start);
                var samples = new List<JsonNode> { start };
                var began = host.Clock.GetTimestamp();
                while (host.Clock.GetElapsedTime(began).TotalSeconds < o.WindowSeconds)
                {
                    var pause = (int)Math.Ceiling(Math.Min(30, o.WindowSeconds - host.Clock.GetElapsedTime(began).TotalSeconds));
                    WaitObserved(host, pause, "Observation window " + windowNumber);
                    var sample = Snapshot(o, phase, host, files, seams);
                    SaveImmutable(files, Path.Combine(destination, "window-" + windowNumber + "-sample-" + samples.Count + ".json"), sample);
                    samples.Add(sample);
                }
                var end = samples[^1];
                var delta = WindowDelta(start, end);
                windows.Add(Obj(("workload", ObservedWorkload(delta)), ("start", start.DeepClone()), ("end", end.DeepClone()),
                    ("samples", new JsonArray(samples.Select(s => s.DeepClone()).ToArray())), ("delta", delta)));
            }
            var evidence = Obj(("schemaVersion", 2), ("provenance", "sampled-runtime-and-context-v1"), ("card", o.PlanCard), ("phase", phase), ("round", o.Round),
                ("canonicalSha", sha), ("capturedAt", new DateTimeOffset(host.UtcNow).ToString("o", CultureInfo.InvariantCulture)), ("context", context.DeepClone()),
                ("windows", windows));
            SaveImmutable(files, Path.Combine(destination, "capture.json"), evidence);
            return evidence;
        }
    }
}
