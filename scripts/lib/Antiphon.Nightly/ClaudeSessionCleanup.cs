// CARD-0144: reports (default) or archives/deletes stale Claude Code remote-control sessions through the code-sessions REST
// API. It replaced the body of scripts/cleanup-claude-sessions.ps1 (unit 22 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md), which stays as the thin launcher of the Windmill schedule
// u/lndcobra/claude_session_cleanup on server2 (Monday 09:15 Europe/London; never a Windows Scheduled Task); the CLI runs it
// as `antiphon-scripts cleanup-claude-sessions`.
//
// GET /v1/code/sessions?statuses=active&statuses=paused&limit=100 is classified row by row. C1 only status active; C2
// last_event_at strictly older than --older-than-days; C3 (default on, --require-disconnected false turns it off) only
// connection_status disconnected; C4 not status_bucket working or blocked; C5 not worker_status running or requires_action;
// C6 not the bridge session of a live local claude*/node* process (~/.claude/sessions/*.json bridgeSessionId, or
// --live-bridges-json); --skip-unread also skips unread rows. A row is a candidate only when no rule skips it.
//
// Default is a dry run. --execute archives each candidate (POST .../{id}/archive) or, with --action Delete, deletes it
// (DELETE .../{id}); --max-per-run refuses (exit 4) rather than truncates; a 404 or not_found_error answer is already gone
// (success); any other failure is counted and the rest still run (exit 5). A list of 100 or more rows is truncated: exit 3,
// nothing acted on. --input-json classifies a saved list body and forces --execute off. The OAuth access token
// (~/.claude/.credentials.json claudeAiOauth.accessToken) is held in memory and sent only as the Authorization header: it is
// never printed, logged or written, and never refreshed (missing, unreadable or expiring within 60 s: exit 2). The report
// (logs/claude-session-cleanup/<utc stamp>.json unless --report-path) is built from an allow-list of fields, never a raw
// answer. Exit 0 done; 1 the list could not be read; 2 no or stale credentials; 3 truncated; 4 over the cap; 5 a mutation
// failed; usage errors are NightlyUsageException (the CLI's exit 2). HTTP is the NightlyReportHttp seam, files IFileSystem,
// the live process names a delegate (production: ProcessName). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The cleanup-claude-sessions command's options (cleanup-claude-sessions.ps1's parameters, in kebab case).</summary>
    public sealed class ClaudeSessionCleanupOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "cleanup-claude-sessions [--older-than-days 7] [--action Archive|Delete] [--execute] [--max-per-run 10] [--skip-unread]"
            + " [--require-disconnected true|false] [--report-path <dir>] [--input-json <file>] [--live-bridges-json <file>] [--sessions-dir <dir>]"
            + " [--credentials-path <file>] [--now <timestamp>]";

        /// <summary>C2: a session is old when last_event_at is strictly older than this many days.</summary>
        public int OlderThanDays { get; init; } = 7;

        /// <summary>Archive (POST .../archive) or Delete (DELETE .../{id}).</summary>
        public string Action { get; init; } = "Archive";

        /// <summary>Act on the candidates (absent: report only).</summary>
        public bool Execute { get; init; }

        /// <summary>More candidates than this refuses the action.</summary>
        public int MaxPerRun { get; init; } = 10;

        /// <summary>Unread rows are not candidates.</summary>
        public bool SkipUnread { get; init; }

        /// <summary>C3: only disconnected rows are candidates.</summary>
        public bool RequireDisconnected { get; init; } = true;

        /// <summary>The report directory.</summary>
        public string ReportPath { get; init; } = "";

        /// <summary>A saved GET /v1/code/sessions body to classify instead of calling out; forces Execute off.</summary>
        public string InputJson { get; init; } = "";

        /// <summary>A JSON array of objects with bridgeSessionId (or of ids) taken as the live bridges; empty: read SessionsDir.</summary>
        public string LiveBridgesJson { get; init; } = "";

        /// <summary>The Claude Code sessions directory (~/.claude/sessions).</summary>
        public string SessionsDir { get; init; } = "";

        /// <summary>The Claude Code credentials file (~/.claude/.credentials.json).</summary>
        public string CredentialsPath { get; init; } = "";

        /// <summary>The time the ages are measured from (UTC); null: the host's clock.</summary>
        public DateTime? Now { get; init; }

        /// <summary>
        /// The arguments after the command name. userProfile (USERPROFILE) chooses the default sessions directory and credentials
        /// file, repoRoot the default report directory (logs/claude-session-cleanup); the CLI passes them.
        /// </summary>
        public static ClaudeSessionCleanupOptions Parse(IReadOnlyList<string> arguments, string userProfile, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments,
                new[] { "--older-than-days", "--action", "--max-per-run", "--require-disconnected", "--report-path", "--input-json", "--live-bridges-json",
                    "--sessions-dir", "--credentials-path", "--now" },
                new[] { "--execute", "--skip-unread" });
            var actionText = flags.Value("--action", "");
            string action;
            if (actionText.Length == 0 || string.Equals(actionText, "Archive", StringComparison.OrdinalIgnoreCase)) action = "Archive";
            else if (string.Equals(actionText, "Delete", StringComparison.OrdinalIgnoreCase)) action = "Delete";
            else throw new NightlyUsageException("--action must be Archive or Delete, not '" + actionText + "'");
            var requireText = flags.Value("--require-disconnected", "");
            bool require;
            if (requireText.Length == 0 || string.Equals(requireText, "true", StringComparison.OrdinalIgnoreCase)) require = true;
            else if (string.Equals(requireText, "false", StringComparison.OrdinalIgnoreCase)) require = false;
            else throw new NightlyUsageException("--require-disconnected must be true or false, not '" + requireText + "'");
            DateTime? now = null;
            var nowText = flags.Value("--now", "");
            if (nowText.Length > 0)
            {
                now = ClaudeSessionCleanup.UtcDate(nowText);
                if (now == null) throw new NightlyUsageException("Could not parse --now '" + nowText + "' as a timestamp.");
            }
            var claude = System.IO.Path.Combine(userProfile ?? "", ".claude");
            var report = flags.Value("--report-path", "");
            var sessions = flags.Value("--sessions-dir", "");
            var credentials = flags.Value("--credentials-path", "");
            return new ClaudeSessionCleanupOptions
            {
                OlderThanDays = flags.Int("--older-than-days", 7),
                Action = action,
                Execute = flags.Switch("--execute"),
                MaxPerRun = flags.Int("--max-per-run", 10),
                SkipUnread = flags.Switch("--skip-unread"),
                RequireDisconnected = require,
                ReportPath = report.Length > 0 ? report : System.IO.Path.Combine(repoRoot ?? "", "logs", "claude-session-cleanup"),
                InputJson = flags.Value("--input-json", ""),
                LiveBridgesJson = flags.Value("--live-bridges-json", ""),
                SessionsDir = sessions.Length > 0 ? sessions : System.IO.Path.Combine(claude, "sessions"),
                CredentialsPath = credentials.Length > 0 ? credentials : System.IO.Path.Combine(claude, ".credentials.json"),
                Now = now,
            };
        }
    }

    /// <summary>One archive or delete the run attempted. StatusCode is the answer's (0 when nothing answered).</summary>
    public sealed record ClaudeSessionMutation(string Id, string Action, string Method, bool Ok, bool AlreadyGone, int StatusCode);

    /// <summary>What a run found and did (the script's -PassThru record).</summary>
    public sealed class ClaudeSessionCleanupResult
    {
        /// <summary>The exit code.</summary>
        public int ExitCode { get; init; }

        /// <summary>The report file; null when the run stopped before writing one.</summary>
        public string ReportPath { get; init; }

        /// <summary>The candidates.</summary>
        public int CandidateCount { get; init; }

        /// <summary>The rows skipped.</summary>
        public int SkipCount { get; init; }

        /// <summary>The candidates' ids, newest last_event_at first.</summary>
        public IReadOnlyList<string> CandidateIds { get; init; } = Array.Empty<string>();

        /// <summary>The skipped rows' ids, newest last_event_at first.</summary>
        public IReadOnlyList<string> SkipIds { get; init; } = Array.Empty<string>();

        /// <summary>The list had 100 or more rows.</summary>
        public bool Truncated { get; init; }

        /// <summary>The archives or deletes attempted.</summary>
        public IReadOnlyList<ClaudeSessionMutation> Mutations { get; init; } = Array.Empty<ClaudeSessionMutation>();
    }

    /// <summary>The stale Claude Code remote-control session cleanup (cleanup-claude-sessions.ps1).</summary>
    public static class ClaudeSessionCleanup
    {
        /// <summary>The code-sessions API.</summary>
        public const string ApiBase = "https://api.anthropic.com";

        /// <summary>The anthropic-version header.</summary>
        public const string AnthropicVersion = "2023-06-01";

        /// <summary>The list's page size: a list this long is truncated.</summary>
        public const int ListLimit = 100;

        /// <summary>The production live-process lookup: the running process's name (without .exe); null when no such process runs.</summary>
        public static string ProcessName(int pid)
        {
            if (pid <= 0) return null;
            try
            {
                using var process = Process.GetProcessById(pid);
                return process.ProcessName;
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Classifies the sessions, prints the table on host.Out, archives or deletes with --execute and writes the report. http
        /// answers each request (it throws when nothing answers); processName answers for a live session file's pid.
        /// </summary>
        public static ClaudeSessionCleanupResult Run(ClaudeSessionCleanupOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http,
            Func<int, string> processName)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            if (processName == null) throw new ArgumentNullException(nameof(processName));
            return new Pass(options, host, files, http, processName).Go();
        }

        /// <summary>ConvertTo-UtcDate: a timestamp's UTC time (a zone-less one is UTC); null when it does not parse.</summary>
        public static DateTime? UtcDate(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.UtcDateTime : (DateTime?)null;
        }

        /// <summary>ConvertTo-SessionSuffix: an id without its cse_ or session_ prefix, so a bridge id and a list id compare.</summary>
        public static string Suffix(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (id.Length > 4 && id.StartsWith("cse_", StringComparison.Ordinal)) return id.Substring(4);
            if (id.Length > 8 && id.StartsWith("session_", StringComparison.Ordinal)) return id.Substring(8);
            return id;
        }

        // Get-ExpiresAtUtc: epoch milliseconds (over 1e12) or seconds (over 1e9), else a timestamp.
        private static DateTime? ExpiresAt(JsonNode value)
        {
            var text = Str(value);
            if (text.Length == 0) return null;
            if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            {
                var epoch = DateTime.SpecifyKind(new DateTime(1970, 1, 1), DateTimeKind.Utc);
                if (number > 1000000000000) return epoch.AddMilliseconds(number);
                if (number > 1000000000) return epoch.AddSeconds(number);
            }
            return UtcDate(text);
        }

        // [string]$value: a string as is, a boolean True or False, a number its JSON text, null empty.
        private static string Str(JsonNode node)
        {
            if (node == null) return "";
            if (node is JsonValue value)
            {
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String: return value.GetValue<string>();
                    case JsonValueKind.True: return "True";
                    case JsonValueKind.False: return "False";
                    case JsonValueKind.Null: return "";
                }
            }
            return node.ToJsonString();
        }

        // [bool]$value: true and false as is, a string when it is not empty, a number when it is not zero, null false.
        private static bool Bool(JsonNode node)
        {
            if (node == null) return false;
            if (node is JsonValue value)
            {
                switch (value.GetValueKind())
                {
                    case JsonValueKind.True: return true;
                    case JsonValueKind.False:
                    case JsonValueKind.Null: return false;
                    case JsonValueKind.String: return value.GetValue<string>().Length > 0;
                    case JsonValueKind.Number: return value.GetValue<double>() != 0;
                }
            }
            return true;
        }

        private static JsonNode Field(JsonNode row, string name) => row is JsonObject fields ? fields[name] : null;

        private static IReadOnlyList<JsonNode> Items(JsonNode node)
        {
            if (node == null) return Array.Empty<JsonNode>();
            return node is JsonArray array ? array.ToArray() : new[] { node };
        }

        // Format-UtcStamp: the UTC time to the second, else the value's own text.
        private static string Stamp(JsonNode value)
        {
            var text = Str(value);
            var date = UtcDate(text);
            return date == null ? text : date.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        private static bool IsTransportFailure(Exception e) =>
            e is HttpRequestException || e is OperationCanceledException || e is System.IO.IOException || e is InvalidOperationException;

        private sealed class Entry
        {
            public string Id;
            public string Title;
            public string Status;
            public string ConnectionStatus;
            public string StatusBucket;
            public string WorkerStatus;
            public string CreatedAt;
            public string LastEventAt;
            public bool Unread;
            public string[] Tags;
            public double AgeDays;
            public string Reason;
            public bool Candidate;

            public JsonObject ToJson() => new JsonObject
            {
                ["id"] = Id,
                ["title"] = Title,
                ["status"] = Status,
                ["connection_status"] = ConnectionStatus,
                ["status_bucket"] = StatusBucket,
                ["worker_status"] = WorkerStatus,
                ["created_at"] = CreatedAt,
                ["last_event_at"] = LastEventAt,
                ["unread"] = Unread,
                ["tags"] = new JsonArray(Tags.Select(tag => (JsonNode)JsonValue.Create(tag)).ToArray()),
                ["ageDays"] = AgeDays,
                ["reason"] = Reason,
                ["candidate"] = Candidate,
            };
        }

        private sealed class Pass
        {
            private readonly ClaudeSessionCleanupOptions _options;
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly NightlyReportHttp _http;
            private readonly Func<int, string> _processName;
            private readonly List<Entry> _candidates = new List<Entry>();
            private readonly List<Entry> _skipped = new List<Entry>();
            private readonly List<ClaudeSessionMutation> _mutations = new List<ClaudeSessionMutation>();
            private bool _execute;
            private DateTime _now;
            private int _listCount;
            private bool _truncated;
            private int _liveBridgeCount;
            private string _reportFile;

            public Pass(ClaudeSessionCleanupOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, Func<int, string> processName)
            {
                _options = options;
                _host = host;
                _files = files;
                _http = http;
                _processName = processName;
            }

            public ClaudeSessionCleanupResult Go()
            {
                // A saved list is never acted on, and the list is never fetched for it.
                _execute = _options.Execute;
                if (_options.InputJson.Length > 0)
                {
                    if (_execute) _host.Out.WriteLine("--input-json is set; --execute is ignored (dry run only).");
                    _execute = false;
                }
                _now = _options.Now ?? _host.UtcNow;
                if (!_files.Directory.Exists(_options.ReportPath)) _files.Directory.CreateDirectory(_options.ReportPath);
                _reportFile = _files.Path.Combine(_options.ReportPath, _host.UtcNow.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture) + ".json");

                // 1. credentials (not for a saved list). Held in memory only: never written, printed or passed on a command line.
                string token = null;
                if (_options.InputJson.Length == 0)
                {
                    token = Token(out var refusal);
                    if (token == null)
                    {
                        _host.Out.WriteLine(refusal);
                        return Complete(2);
                    }
                }

                // 2. the list.
                IReadOnlyList<JsonNode> rows;
                if (_options.InputJson.Length > 0)
                {
                    if (!_files.File.Exists(_options.InputJson))
                    {
                        _host.Out.WriteLine("--input-json not found: " + _options.InputJson);
                        return Complete(1);
                    }
                    try
                    {
                        rows = ListRows(JsonNode.Parse(_files.File.ReadAllText(_options.InputJson)));
                    }
                    catch (JsonException e)
                    {
                        _host.Out.WriteLine("--input-json is not JSON: " + e.Message);
                        return Complete(1);
                    }
                }
                else
                {
                    var uri = ApiBase + "/v1/code/sessions?statuses=active&statuses=paused&limit=" + ListLimit.ToString(CultureInfo.InvariantCulture);
                    NightlyReportHttpResponse answer = null;
                    try
                    {
                        answer = _http("GET", uri, Headers(token), null);
                        rows = OperatorApi.Succeeded(answer) ? ListRows(string.IsNullOrWhiteSpace(answer.Body) ? null : JsonNode.Parse(answer.Body)) : null;
                    }
                    catch (Exception e) when (IsTransportFailure(e) || e is JsonException)
                    {
                        rows = null;
                    }
                    if (rows == null)
                    {
                        _host.Out.WriteLine("GET /v1/code/sessions failed (status " + (answer?.Status ?? 0).ToString(CultureInfo.InvariantCulture) + ").");
                        return Complete(1);
                    }
                }
                _listCount = rows.Count;
                if (_listCount >= ListLimit)
                {
                    _host.Out.WriteLine("WARNING: session list is truncated (data.Count=" + _listCount.ToString(CultureInfo.InvariantCulture) + " >= limit="
                        + ListLimit.ToString(CultureInfo.InvariantCulture) + "). Refusing to sweep a partial view.");
                    _truncated = true;
                    return Complete(3);
                }

                // 3. the local exclusions (C6).
                HashSet<string> live;
                try
                {
                    live = LiveSuffixes();
                }
                catch (Exception e) when (e is InvalidOperationException || e is JsonException || e is System.IO.IOException || e is UnauthorizedAccessException)
                {
                    // The script stopped here without a report.
                    _host.Error.WriteLine(e.Message);
                    return new ClaudeSessionCleanupResult { ExitCode = 1 };
                }
                _liveBridgeCount = live.Count;

                // 4. classify, then the table.
                Classify(rows, live);
                WriteTable();
                _host.Out.WriteLine();
                _host.Out.WriteLine("candidates=" + _candidates.Count.ToString(CultureInfo.InvariantCulture) + " skipped=" + _skipped.Count.ToString(CultureInfo.InvariantCulture)
                    + " liveBridges=" + _liveBridgeCount.ToString(CultureInfo.InvariantCulture) + " execute=" + (_execute ? "True" : "False") + " action=" + _options.Action
                    + " requireDisconnected=" + (_options.RequireDisconnected ? "True" : "False"));

                // 5. act (only with --execute).
                if (!_execute) return Complete(0);
                if (_candidates.Count > _options.MaxPerRun)
                {
                    _host.Out.WriteLine("Refusing to " + _options.Action + " " + _candidates.Count.ToString(CultureInfo.InvariantCulture) + " candidates; --max-per-run is "
                        + _options.MaxPerRun.ToString(CultureInfo.InvariantCulture) + ". The cap refuses, it does not truncate.");
                    return Complete(4);
                }
                var headers = Headers(token);
                var failures = 0;
                foreach (var row in _candidates)
                {
                    var delete = _options.Action == "Delete";
                    var method = delete ? "DELETE" : "POST";
                    var uri = ApiBase + "/v1/code/sessions/" + row.Id + (delete ? "" : "/archive");
                    var ok = false;
                    var gone = false;
                    var status = 0;
                    var error = "";
                    try
                    {
                        var answer = _http(method, uri, headers, null);
                        status = answer?.Status ?? 0;
                        if (OperatorApi.Succeeded(answer)) ok = true;
                        else error = answer?.Body ?? "";
                    }
                    catch (Exception e) when (IsTransportFailure(e))
                    {
                        error = e.Message ?? "";
                    }
                    // Test-AlreadyGone: a 404, or a not_found_error body, is already gone.
                    if (!ok && (status == 404 || error.IndexOf("not_found_error", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        ok = true;
                        gone = true;
                    }
                    _mutations.Add(new ClaudeSessionMutation(row.Id, _options.Action, method, ok, gone, status));
                    if (ok && gone) _host.Out.WriteLine(_options.Action + " " + row.Id + " already gone (404 not_found_error) - treating as success");
                    else if (ok) _host.Out.WriteLine(_options.Action + " " + row.Id + " ok");
                    else
                    {
                        failures++;
                        var snippet = error.Length > 180 ? error.Substring(0, 180) : error;
                        _host.Out.WriteLine(_options.Action + " " + row.Id + " FAILED status=" + status.ToString(CultureInfo.InvariantCulture) + " " + snippet);
                    }
                }
                if (failures == 0) return Complete(0);
                _host.Out.WriteLine(failures.ToString(CultureInfo.InvariantCulture) + " mutation(s) failed.");
                return Complete(5);
            }

            private static Dictionary<string, string> Headers(string token) => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Authorization"] = "Bearer " + token,
                ["anthropic-version"] = AnthropicVersion,
                ["Accept"] = "application/json",
            };

            // The credentials: the access token, or null and the line that says why (it never names the token).
            private string Token(out string refusal)
            {
                const string Missing = "CLI credentials missing - run/refresh Claude Code on this machine.";
                if (!_files.File.Exists(_options.CredentialsPath))
                {
                    refusal = Missing;
                    return null;
                }
                JsonNode credentials;
                try
                {
                    credentials = JsonNode.Parse(_files.File.ReadAllText(_options.CredentialsPath));
                }
                catch (Exception e) when (e is JsonException || e is System.IO.IOException || e is UnauthorizedAccessException)
                {
                    refusal = "CLI credentials unreadable - run/refresh Claude Code on this machine.";
                    return null;
                }
                var oauth = Field(credentials, "claudeAiOauth");
                var token = Str(Field(oauth, "accessToken"));
                if (!(oauth is JsonObject) || token.Length == 0)
                {
                    refusal = Missing;
                    return null;
                }
                var expires = ExpiresAt(Field(oauth, "expiresAt"));
                if (expires == null || expires.Value < _now.AddSeconds(60))
                {
                    refusal = "CLI credentials stale - run/refresh Claude Code on this machine.";
                    return null;
                }
                refusal = null;
                return token;
            }

            // The list body's rows: its data, else the body itself (an array's items, or one row).
            private static IReadOnlyList<JsonNode> ListRows(JsonNode body)
            {
                if (body is JsonObject fields && fields.ContainsKey("data")) return Items(fields["data"]);
                return Items(body);
            }

            // Get-LiveBridgeSuffixes: the suffixes of the live bridge ids.
            private HashSet<string> LiveSuffixes()
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_options.LiveBridgesJson.Length > 0)
                {
                    if (!_files.File.Exists(_options.LiveBridgesJson)) throw new InvalidOperationException("--live-bridges-json not found: " + _options.LiveBridgesJson);
                    var raw = JsonNode.Parse(_files.File.ReadAllText(_options.LiveBridgesJson));
                    var items = raw is JsonObject wrapper && wrapper.ContainsKey("bridges") ? Items(wrapper["bridges"]) : Items(raw);
                    foreach (var item in items)
                    {
                        if (item == null) continue;
                        var suffix = Suffix(item is JsonObject ? Str(Field(item, "bridgeSessionId")) : Str(item));
                        if (suffix.Length > 0) set.Add(suffix);
                    }
                    return set;
                }
                if (!_files.Directory.Exists(_options.SessionsDir)) return set;
                foreach (var file in _files.Directory.EnumerateFiles(_options.SessionsDir, "*.json"))
                {
                    JsonNode session;
                    try
                    {
                        session = JsonNode.Parse(_files.File.ReadAllText(file));
                    }
                    catch (Exception e) when (e is JsonException || e is System.IO.IOException || e is UnauthorizedAccessException)
                    {
                        continue;
                    }
                    var bridge = Str(Field(session, "bridgeSessionId"));
                    if (bridge.Length == 0) continue;
                    // The pid the file names, else its base name (<pid>.json).
                    int.TryParse(Str(Field(session, "pid")), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid);
                    if (pid <= 0) int.TryParse(_files.Path.GetFileNameWithoutExtension(file), NumberStyles.Integer, CultureInfo.InvariantCulture, out pid);
                    if (pid <= 0) continue;
                    var name = _processName(pid);
                    if (name == null) continue;
                    if (!name.StartsWith("claude", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("node", StringComparison.OrdinalIgnoreCase)) continue;
                    var suffix = Suffix(bridge);
                    if (suffix.Length > 0) set.Add(suffix);
                }
                return set;
            }

            private void Classify(IReadOnlyList<JsonNode> rows, HashSet<string> live)
            {
                foreach (var row in rows)
                {
                    if (row == null) continue;
                    var id = Str(Field(row, "id"));
                    var status = Str(Field(row, "status"));
                    var connection = Str(Field(row, "connection_status"));
                    var bucket = Str(Field(row, "status_bucket"));
                    var worker = Str(Field(row, "worker_status"));
                    var lastEvent = UtcDate(Str(Field(row, "last_event_at")));
                    var age = lastEvent == null ? 0.0 : (_now - lastEvent.Value).TotalDays;
                    var reasons = new List<string>();
                    // C1: only sidebar-active rows.
                    if (!string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)) reasons.Add("status=" + status);
                    // C2: last_event_at older than --older-than-days.
                    if (lastEvent == null) reasons.Add("no last_event_at");
                    else if (age <= _options.OlderThanDays) reasons.Add("fresh");
                    // C3: narrowing only; never adds candidates.
                    if (_options.RequireDisconnected && !string.Equals(connection, "disconnected", StringComparison.OrdinalIgnoreCase)) reasons.Add("connected");
                    // C4
                    if (string.Equals(bucket, "working", StringComparison.OrdinalIgnoreCase) || string.Equals(bucket, "blocked", StringComparison.OrdinalIgnoreCase))
                        reasons.Add("bucket=" + bucket);
                    // C5
                    if (string.Equals(worker, "running", StringComparison.OrdinalIgnoreCase) || string.Equals(worker, "requires_action", StringComparison.OrdinalIgnoreCase))
                        reasons.Add("worker=" + worker);
                    // C6: a live local bridge target.
                    var suffix = Suffix(id);
                    if (suffix.Length > 0 && live.Contains(suffix)) reasons.Add("LOCAL-LIVE");
                    // The optional unread skip (default off).
                    var unread = Bool(Field(row, "unread"));
                    if (_options.SkipUnread && unread) reasons.Add("unread");

                    var candidate = reasons.Count == 0;
                    var entry = new Entry
                    {
                        Id = id,
                        Title = Str(Field(row, "title")),
                        Status = status,
                        ConnectionStatus = connection,
                        StatusBucket = bucket,
                        WorkerStatus = worker,
                        CreatedAt = Stamp(Field(row, "created_at")),
                        LastEventAt = Stamp(Field(row, "last_event_at")),
                        Unread = unread,
                        Tags = Items(Field(row, "tags")).Select(Str).ToArray(),
                        AgeDays = Math.Round(age, 3),
                        Reason = string.Join(", ", reasons),
                        Candidate = candidate,
                    };
                    (candidate ? _candidates : _skipped).Add(entry);
                }
                Sort(_candidates);
                Sort(_skipped);
            }

            // Sort-Object last_event_at -Descending (stable).
            private static void Sort(List<Entry> entries)
            {
                var sorted = entries.OrderByDescending(entry => entry.LastEventAt, StringComparer.OrdinalIgnoreCase).ToList();
                entries.Clear();
                entries.AddRange(sorted);
            }

            private void WriteTable()
            {
                _host.Out.WriteLine("=== CANDIDATES (" + _candidates.Count.ToString(CultureInfo.InvariantCulture) + ") ===");
                foreach (var row in _candidates)
                    _host.Out.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}  {1,-12} {2,-13} {3,-8} age={4}d  {5}",
                        row.Id, row.ConnectionStatus, row.StatusBucket, row.WorkerStatus, row.AgeDays.ToString("0.0", CultureInfo.InvariantCulture), row.Title));
                _host.Out.WriteLine();
                _host.Out.WriteLine("=== SKIPPED (" + _skipped.Count.ToString(CultureInfo.InvariantCulture) + ") ===");
                foreach (var row in _skipped)
                    _host.Out.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}  {1,-12} {2,-13} {3,-8} age={4}d  <- {5}",
                        row.Id, row.ConnectionStatus, row.StatusBucket, row.WorkerStatus, row.AgeDays.ToString("0.0", CultureInfo.InvariantCulture), row.Reason));
            }

            // Complete-Run: the report (an allow-list; a failure to write it is a warning), then the result.
            private ClaudeSessionCleanupResult Complete(int code)
            {
                if (_reportFile != null)
                {
                    try
                    {
                        var report = new JsonObject
                        {
                            ["generatedAt"] = _host.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                            ["now"] = _now.ToString("o", CultureInfo.InvariantCulture),
                            ["olderThanDays"] = _options.OlderThanDays,
                            ["action"] = _options.Action,
                            ["execute"] = _execute,
                            ["maxPerRun"] = _options.MaxPerRun,
                            ["skipUnread"] = _options.SkipUnread,
                            ["requireDisconnected"] = _options.RequireDisconnected,
                            ["inputJson"] = _options.InputJson.Length > 0,
                            ["listCount"] = _listCount,
                            ["candidateCount"] = _candidates.Count,
                            ["skipCount"] = _skipped.Count,
                            ["truncated"] = _truncated,
                            ["liveBridgeCount"] = _liveBridgeCount,
                            ["candidates"] = new JsonArray(_candidates.Select(entry => (JsonNode)entry.ToJson()).ToArray()),
                            ["skipped"] = new JsonArray(_skipped.Select(entry => (JsonNode)entry.ToJson()).ToArray()),
                            ["mutations"] = new JsonArray(_mutations.Select(mutation => (JsonNode)new JsonObject
                            {
                                ["id"] = mutation.Id,
                                ["action"] = mutation.Action,
                                ["method"] = mutation.Method,
                                ["ok"] = mutation.Ok,
                                ["alreadyGone"] = mutation.AlreadyGone,
                                ["statusCode"] = mutation.StatusCode,
                            }).ToArray()),
                            ["exitCode"] = code,
                        };
                        _files.File.WriteAllText(_reportFile, OperatorApi.Pretty(report) + Environment.NewLine);
                    }
                    catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
                    {
                        _host.Out.WriteLine("warning: failed to write report: " + e.Message);
                    }
                }
                return new ClaudeSessionCleanupResult
                {
                    ExitCode = code,
                    ReportPath = _reportFile,
                    CandidateCount = _candidates.Count,
                    SkipCount = _skipped.Count,
                    CandidateIds = _candidates.Select(entry => entry.Id).ToArray(),
                    SkipIds = _skipped.Select(entry => entry.Id).ToArray(),
                    Truncated = _truncated,
                    Mutations = _mutations.ToArray(),
                };
            }
        }
    }
}
