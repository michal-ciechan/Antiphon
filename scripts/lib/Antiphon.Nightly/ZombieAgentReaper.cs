// CARD-0221: finds - and with --execute, kills - agent processes that nobody owns. It replaced scripts/reap-zombie-agents.ps1
// (deleted 2026-11-23, unit 22 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
// `antiphon-scripts reap-zombie-agents`. Nothing schedules it: the Windmill job CARD-0221 S3 proposed was never registered
// (CARD-0232 closed it for a native census), and the server's Hangfire job antiphon:zombie-census (CARD-0298) is the scheduled,
// report-only census. The server's ZombieCensusClassifier ports the same rules, but it lives in the server assembly, which this
// core does not reference; this file keeps the script's own rules (the server's I5 also skips RefOnly tasks).
//
// DRY RUN BY DEFAULT: without --execute it only prints the census and the verdict per process. A process is a positive only when
// EVERY applicable Z-rule holds. A task row that says Succeeded does not, on its own, imply kill (warm pool, standing agent,
// CARD-0085 recovery).
//
// Identity (first that answers wins; unresolved = "unidentified", never touched):
//   I1  ancestor chain hits a runner pid/hostPid -> that session (runner-claimed)
//   I2  ancestor is Antiphon.PtyHost.exe with a manifest (<session-log-path>\pty-hosts\manifests) -> the manifest's sessionId
//   I3  --session-id <guid> on the command line (NOT --resume; Claude forks ids)
//   I4  --name <slug> -> Agents.Slug/Name -> PersistentSessionId
//   I5  cwd (else the command line) naming card-task-<8hex> -> the task -> AgentSessionId
// Pre-filters (never candidates, printed under "ignored"): an executable under WindowsApps\; an ancestor chain reaching
// WindowsTerminal.exe / explorer.exe / Code.exe / rider64.exe / ssh / sshd BEFORE any Antiphon.PtyHost.exe / herdr parent.
// Rules:
//   Z1  agent-shaped (claude.exe/grok.exe/codex.exe) and passed the pre-filters
//   Z2  identity resolved (I1-I5) to a session id S, and the database has S
//   Z3  pid-reuse: process start >= AgentSessions.StartedAt of S minus --pid-reuse-tolerance-sec
//   Z4  class from the rows: A PoolExpired (IsPoolDelegate, no open task, newest CompletedAt older than --min-done-minutes, S
//       Starting/Running); B ReconcilerOwned (S Stopped/Failed and runner-claimed; never acted on); C EndedButAlive (S
//       Stopped/Failed, EndedAt older than --min-done-minutes, not runner-claimed, and Z5); D Unclaimed (no row; report only)
//   Z5  (class C only) the newest mtime of the session's ansi log and, for Claude, its transcripts
//       (~\.claude\projects\<encoded cwd>\*.jsonl) is older than --quiet-hours
//   Z6  the process is older than --min-done-minutes
//   Z7  --execute acts only on the classes --class names (default PoolExpired; e.g. PoolExpired,EndedButAlive)
// Kill path: the most coherent path that still exists. Class A runner-claimed: POST {server}/api/sessions/{S}/kill, else POST
// {runner}/sessions/{S}/kill, else a tree kill; class A runner-unclaimed and class C: taskkill /T /F from the nearest
// Antiphon-shaped ancestor (else the topmost cmd.exe/node.exe wrapper, else the leaf); then the leaf must be gone within
// --kill-verify-seconds. --min-working-set-gb is REPORT-ONLY. --limit kills the oldest positives first.
//
// Exit codes: 0 a dry run with no positives, or every kill verified; 1 a kill was requested but the leaf is still alive; 2 the
// runner, the database or the process table could not be read; 3 a dry run found positives. Usage errors are
// NightlyUsageException (the CLI's exit 2). The report (logs/zombie-agents/<utc>.json unless --report-path) holds allow-listed
// fields only. The runner, the server, the database, the process table, the tree kill and the liveness check are
// IZombieAgentSeams (production: ZombieAgentReaper.Production, Windows only); files are IFileSystem. Plain C#: no PowerShell
// types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>The reap-zombie-agents command's options (reap-zombie-agents.ps1's parameters, in kebab case).</summary>
    public sealed class ZombieAgentReaperOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "reap-zombie-agents [--execute] [--class PoolExpired[,EndedButAlive]] [--limit 0] [--runner-url http://localhost:17204]"
            + " [--server-url http://localhost:17202] [--session-log-path C:\\logs\\antiphon\\session-runner] [--pg-container antiphon-postgres] [--pg-user antiphon]"
            + " [--pg-database antiphon] [--quiet-hours 6] [--min-done-minutes 120] [--min-working-set-gb 0] [--pid-reuse-tolerance-sec 5]"
            + " [--kill-verify-seconds 20] [--report-path <dir>]";

        /// <summary>Kill the positives of the classes Class names (absent: report only).</summary>
        public bool Execute { get; init; }

        /// <summary>The session runner, without a trailing slash.</summary>
        public string RunnerUrl { get; init; } = "http://localhost:17204";

        /// <summary>The server, without a trailing slash.</summary>
        public string ServerUrl { get; init; } = "http://localhost:17202";

        /// <summary>The session runner's log root (ansi logs, pty-host manifests).</summary>
        public string SessionLogPath { get; init; } = @"C:\logs\antiphon\session-runner";

        /// <summary>The Postgres container the census query runs in.</summary>
        public string PgContainer { get; init; } = "antiphon-postgres";

        /// <summary>The Postgres user.</summary>
        public string PgUser { get; init; } = "antiphon";

        /// <summary>The Postgres database.</summary>
        public string PgDatabase { get; init; } = "antiphon";

        /// <summary>The classes --execute acts on, as given (comma, semicolon or space separated).</summary>
        public string Class { get; init; } = "PoolExpired";

        /// <summary>Class C needs no activity for this many hours.</summary>
        public int QuietHours { get; init; } = 6;

        /// <summary>The age and done floor in minutes.</summary>
        public int MinDoneMinutes { get; init; } = 120;

        /// <summary>Report-only: a working set at least this large (GB) is flagged LargeWs; 0 flags nothing.</summary>
        public double MinWorkingSetGB { get; init; }

        /// <summary>The pid-reuse tolerance in seconds.</summary>
        public int PidReuseToleranceSec { get; init; } = 5;

        /// <summary>How long a killed leaf has to disappear.</summary>
        public int KillVerifySeconds { get; init; } = 20;

        /// <summary>Kill at most this many positives (the oldest first); 0 is no limit.</summary>
        public int Limit { get; init; }

        /// <summary>The report directory.</summary>
        public string ReportPath { get; init; } = "";

        /// <summary>The user profile whose .claude\projects holds the Claude transcripts (USERPROFILE; the CLI passes it).</summary>
        public string UserProfile { get; init; } = "";

        /// <summary>The classes Class names, ignoring case.</summary>
        public IReadOnlySet<string> ExecuteClasses =>
            new HashSet<string>(Regex.Split(Class ?? "", "[,; ]").Select(part => part.Trim()).Where(part => part.Length > 0), StringComparer.OrdinalIgnoreCase);

        /// <summary>The arguments after the command name. userProfile (USERPROFILE) and repoRoot (the default report directory logs/zombie-agents) come from the CLI.</summary>
        public static ZombieAgentReaperOptions Parse(IReadOnlyList<string> arguments, string userProfile, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments,
                new[] { "--runner-url", "--server-url", "--session-log-path", "--pg-container", "--pg-user", "--pg-database", "--class", "--quiet-hours",
                    "--min-done-minutes", "--min-working-set-gb", "--pid-reuse-tolerance-sec", "--kill-verify-seconds", "--limit", "--report-path" },
                new[] { "--execute" });
            var minimum = 0.0;
            var minimumText = flags.Value("--min-working-set-gb", "");
            if (minimumText.Length > 0 && (!double.TryParse(minimumText, NumberStyles.Float, CultureInfo.InvariantCulture, out minimum) || double.IsNaN(minimum) || double.IsInfinity(minimum)))
                throw new NightlyUsageException("--min-working-set-gb expects a number, not '" + minimumText + "'");
            string Text(string flag, string fallback)
            {
                var value = flags.Value(flag, "");
                return value.Length > 0 ? value : fallback;
            }
            return new ZombieAgentReaperOptions
            {
                Execute = flags.Switch("--execute"),
                RunnerUrl = Text("--runner-url", "http://localhost:17204").TrimEnd('/'),
                ServerUrl = Text("--server-url", "http://localhost:17202").TrimEnd('/'),
                SessionLogPath = Text("--session-log-path", @"C:\logs\antiphon\session-runner"),
                PgContainer = Text("--pg-container", "antiphon-postgres"),
                PgUser = Text("--pg-user", "antiphon"),
                PgDatabase = Text("--pg-database", "antiphon"),
                Class = Text("--class", "PoolExpired"),
                QuietHours = flags.Int("--quiet-hours", 6),
                MinDoneMinutes = flags.Int("--min-done-minutes", 120),
                MinWorkingSetGB = minimum,
                PidReuseToleranceSec = flags.Int("--pid-reuse-tolerance-sec", 5),
                KillVerifySeconds = flags.Int("--kill-verify-seconds", 20),
                Limit = flags.Int("--limit", 0),
                ReportPath = Text("--report-path", System.IO.Path.Combine(repoRoot ?? "", "logs", "zombie-agents")),
                UserProfile = userProfile ?? "",
            };
        }
    }

    /// <summary>One process of the census: a Win32_Process row (Cwd is empty on Windows) and its five-second CPU sample.</summary>
    public sealed record ZombieProcess(int ProcessId, int ParentProcessId, string Name, string ExecutablePath, string CommandLine, string Cwd,
        DateTime? CreationDate, long WorkingSetSize, double? CpuDeltaPercent);

    /// <summary>What the census query answered: docker's exit code and its output.</summary>
    public sealed record ZombieDatabaseAnswer(int ExitCode, string Output);

    /// <summary>One kill the run sent: POST and the URI, or TASKKILL and pid:&lt;tree pid&gt;.</summary>
    public sealed record ZombieKillCall(string Method, string Uri);

    /// <summary>The rows per class.</summary>
    public sealed record ZombieClassCounts(int PoolExpired, int ReconcilerOwned, int EndedButAlive, int Unclaimed, int Ignored, int Unidentified, int Positives);

    /// <summary>The machine the reaper reads and, with --execute, changes.</summary>
    public interface IZombieAgentSeams
    {
        /// <summary>One request without a body (GET the runner's sessions, POST a kill): the answer; throws when nothing answers in time.</summary>
        NightlyReportHttpResponse Http(string method, string uri, TimeSpan timeout);

        /// <summary>docker exec &lt;container&gt; psql -U &lt;user&gt; -d &lt;database&gt; -At -c &lt;sql&gt;; throws when docker cannot start.</summary>
        ZombieDatabaseAnswer Query(string container, string user, string database, string sql);

        /// <summary>Every process, each with its CPU over a five-second sample; throws when the table cannot be read.</summary>
        IReadOnlyList<ZombieProcess> Processes();

        /// <summary>taskkill /T /F /PID pid.</summary>
        void TreeKill(int pid);

        /// <summary>True while the process runs.</summary>
        bool IsAlive(int pid);
    }

    /// <summary>One process's verdict (a census row).</summary>
    public sealed class ZombieAgentRow
    {
        /// <summary>The process id.</summary>
        public int Pid { get; init; }

        /// <summary>The executable name.</summary>
        public string Exe { get; init; } = "";

        /// <summary>The process start (UTC); null when unknown.</summary>
        public DateTime? Start { get; init; }

        /// <summary>The working set in GB, three decimals.</summary>
        public double WorkingSetGB { get; init; }

        /// <summary>The CPU percent over the sample; null when unknown.</summary>
        public double? CpuDeltaPct { get; init; }

        /// <summary>I1-I5, or empty.</summary>
        public string IdentityMethod { get; init; } = "";

        /// <summary>The resolved session id (lower case), or empty.</summary>
        public string SessionId { get; init; } = "";

        /// <summary>The session's status in the database, or empty.</summary>
        public string DbStatus { get; init; } = "";

        /// <summary>The owning agent's name, or empty.</summary>
        public string Agent { get; init; } = "";

        /// <summary>PoolExpired, ReconcilerOwned, EndedButAlive, Unclaimed, Ignored, or empty for no zombie class.</summary>
        public string Class { get; init; } = "";

        /// <summary>The rules that failed, "; " separated.</summary>
        public string RulesFailed { get; init; } = "";

        /// <summary>server or taskkill for a zombie class, else empty.</summary>
        public string KillPath { get; init; } = "";

        /// <summary>A runner session claims the process (I1).</summary>
        public bool RunnerClaimed { get; init; }

        /// <summary>The working set reached --min-working-set-gb.</summary>
        public bool LargeWs { get; init; }

        /// <summary>The process a tree kill starts from; 0 when none was chosen.</summary>
        public int TreeKillPid { get; init; }
    }

    /// <summary>What a run found and did (the script's -PassThru record).</summary>
    public sealed class ZombieAgentReaperResult
    {
        /// <summary>The exit code.</summary>
        public int ExitCode { get; init; }

        /// <summary>The report file; null when a prerequisite stopped the run.</summary>
        public string ReportPath { get; init; }

        /// <summary>The positives.</summary>
        public IReadOnlyList<ZombieAgentRow> Positives { get; init; } = Array.Empty<ZombieAgentRow>();

        /// <summary>The pre-filtered rows.</summary>
        public IReadOnlyList<ZombieAgentRow> Ignored { get; init; } = Array.Empty<ZombieAgentRow>();

        /// <summary>The rows no identity resolved.</summary>
        public IReadOnlyList<ZombieAgentRow> Unidentified { get; init; } = Array.Empty<ZombieAgentRow>();

        /// <summary>Every agent-shaped process's row.</summary>
        public IReadOnlyList<ZombieAgentRow> Rows { get; init; } = Array.Empty<ZombieAgentRow>();

        /// <summary>The rows per class.</summary>
        public ZombieClassCounts ClassCounts { get; init; } = new ZombieClassCounts(0, 0, 0, 0, 0, 0, 0);

        /// <summary>The kills sent, in order.</summary>
        public IReadOnlyList<ZombieKillCall> KillCalls { get; init; } = Array.Empty<ZombieKillCall>();
    }

    /// <summary>The zombie agent census and reaper (reap-zombie-agents.ps1).</summary>
    public static class ZombieAgentReaper
    {
        /// <summary>How long the runner's GET /sessions may take.</summary>
        public static readonly TimeSpan RunnerDeadline = TimeSpan.FromSeconds(15);

        /// <summary>How long a kill request may take.</summary>
        public static readonly TimeSpan KillDeadline = TimeSpan.FromSeconds(30);

        /// <summary>The census query: the sessions, agents and tasks as one JSON object.</summary>
        public const string Sql = @"SELECT json_build_object(
  'sessions', COALESCE((SELECT json_agg(json_build_object(
      'id', ""Id"", 'status', ""Status"", 'startedAt', ""StartedAt"", 'endedAt', ""EndedAt"",
      'cwd', ""Cwd"", 'agentKind', ""AgentKind""
    )) FROM ""AgentSessions""), '[]'::json),
  'agents', COALESCE((SELECT json_agg(json_build_object(
      'id', ""Id"", 'name', ""Name"", 'slug', ""Slug"", 'isPoolDelegate', ""IsPoolDelegate"",
      'status', ""Status"", 'persistentSessionId', ""PersistentSessionId"",
      'workingDirectory', ""WorkingDirectory""
    )) FROM ""Agents""), '[]'::json),
  'tasks', COALESCE((SELECT json_agg(json_build_object(
      'id', ""Id"", 'agentId', ""AgentId"", 'agentSessionId', ""AgentSessionId"",
      'status', ""Status"", 'completedAt', ""CompletedAt"", 'workspace', ""Workspace"",
      'workingDirectory', ""WorkingDirectory"", 'worktreePath', ""WorktreePath""
    )) FROM ""AgentTasks""), '[]'::json)
);";

        private static readonly Dictionary<string, string> SessionStatus = Names("Created", "Starting", "Running", "Stopping", "Stopped", "Failed");
        private static readonly Dictionary<string, string> TaskStatus = Names("Queued", "Dispatched", "Working", "Blocked", "Succeeded", "Failed", "Canceled");
        private static readonly Dictionary<string, string> AgentKind = Names("Raw", "ClaudeCode", "Codex", "OpenCode", "Grok");
        private static readonly HashSet<string> OpenTask = new HashSet<string>(new[] { "Queued", "Dispatched", "Working", "Blocked" }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> LiveSession = new HashSet<string>(new[] { "Starting", "Running" }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> TerminalSession = new HashSet<string>(new[] { "Stopped", "Failed" }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> AgentShaped = new HashSet<string>(new[] { "claude.exe", "claude", "grok.exe", "grok", "codex.exe", "codex" },
            StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> AntiphonParent = new HashSet<string>(new[] { "antiphon.ptyhost.exe", "herdr.exe", "herdr" }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> OperatorParent = new HashSet<string>(new[] { "windowsterminal.exe", "explorer.exe", "code.exe", "rider64.exe", "ssh.exe",
            "sshd.exe", "ssh", "sshd" }, StringComparer.OrdinalIgnoreCase);
        private static readonly Regex SessionIdArgument = new Regex(@"--session-id(?:\s+|=)([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
            RegexOptions.CultureInvariant);
        private static readonly Regex NameArgument = new Regex(@"--name(?:\s+|=)([^\s""]+)", RegexOptions.CultureInvariant);
        private static readonly Regex CardTask = new Regex(@"card-task-([0-9a-fA-F]{8})(?:\\|""|$|/)", RegexOptions.CultureInvariant);
        private static readonly Regex CardTaskLeafName = new Regex("^card-task-[0-9a-fA-F]{8}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex ManifestSession = new Regex("\"sessionId\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant);
        private static readonly Regex ManifestHostPid = new Regex("\"hostPid\"\\s*:\\s*([0-9]+)", RegexOptions.CultureInvariant);

        /// <summary>The production seams (Windows): HTTP, docker on the host's launcher, the Win32 process table, taskkill and the host's liveness check.</summary>
        public static IZombieAgentSeams Production(NightlyHost host) => new ZombieAgentOs(host ?? throw new ArgumentNullException(nameof(host)));

        /// <summary>Reads the runner, the database and the process table, classifies every agent-shaped process, kills with --execute and writes the report.</summary>
        public static ZombieAgentReaperResult Run(ZombieAgentReaperOptions options, NightlyHost host, IFileSystem files, IZombieAgentSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            return new Pass(options, host, files, seams).Go();
        }

        /// <summary>Encode-ClaudeProjectDir: every character that is not an ASCII letter or digit becomes '-'.</summary>
        public static string ClaudeProjectDirectory(string cwd)
        {
            if (string.IsNullOrEmpty(cwd)) return "";
            var chars = cwd.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (!char.IsAsciiLetterOrDigit(chars[i])) chars[i] = '-';
            return new string(chars);
        }

        /// <summary>Get-CardTaskLeaf: card-task-&lt;8hex&gt; (lower case) when the path names one, else null.</summary>
        public static string CardTaskLeaf(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var match = CardTask.Match(path);
            if (match.Success) return "card-task-" + match.Groups[1].Value.ToLowerInvariant();
            var leaf = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
            return CardTaskLeafName.IsMatch(leaf ?? "") ? leaf.ToLowerInvariant() : null;
        }

        // A status map: the enum's numbers and names (ignoring case) to its names.
        private static Dictionary<string, string> Names(params string[] names)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < names.Length; i++)
            {
                map[i.ToString(CultureInfo.InvariantCulture)] = names[i];
                map[names[i]] = names[i];
            }
            return map;
        }

        private static string Name(JsonNode value, Dictionary<string, string> map)
        {
            if (value == null) return "";
            var text = Str(value);
            return map.TryGetValue(text, out var name) ? name : text;
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

        // Convert-ToInt: a whole number, 0 otherwise.
        private static int Int(JsonNode node) => int.TryParse(Str(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;

        // Convert-ToUtc: a timestamp's UTC time (a zone-less one is UTC); null when it does not parse.
        private static DateTime? Utc(JsonNode node)
        {
            var text = Str(node);
            if (text.Length == 0) return null;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : (DateTime?)null;
        }

        private static JsonNode Field(JsonNode row, string name) => row is JsonObject fields ? fields[name] : null;

        private static IReadOnlyList<JsonNode> Items(JsonNode node)
        {
            if (node == null) return Array.Empty<JsonNode>();
            return node is JsonArray array ? array.ToArray() : new[] { node };
        }

        private static bool IsTransportFailure(Exception e) =>
            e is HttpRequestException || e is OperationCanceledException || e is System.IO.IOException || e is InvalidOperationException || e is JsonException;

        private sealed class DbSession
        {
            public string Status = "";
            public DateTime? StartedAt;
            public DateTime? EndedAt;
            public string Cwd = "";
            public string AgentKind = "";
        }

        private sealed class DbAgent
        {
            public string Id = "";
            public string Name = "";
            public bool IsPoolDelegate;
            public string PersistentSessionId = "";
        }

        private sealed class DbTask
        {
            public string AgentId = "";
            public string AgentSessionId = "";
            public string Status = "";
            public DateTime? CompletedAt;
            public string WorkingDirectory = "";
            public string WorktreePath = "";
        }

        private sealed record Identity(string Method, string SessionId, bool RunnerClaimed);

        private sealed class Pass
        {
            private readonly ZombieAgentReaperOptions _options;
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly IZombieAgentSeams _seams;
            private readonly List<ZombieKillCall> _killCalls = new List<ZombieKillCall>();
            private readonly Dictionary<int, string> _runnerByPid = new Dictionary<int, string>();
            private readonly Dictionary<string, DbSession> _sessions = new Dictionary<string, DbSession>(StringComparer.Ordinal);
            private readonly List<DbAgent> _agents = new List<DbAgent>();
            private readonly Dictionary<string, DbAgent> _agentBySession = new Dictionary<string, DbAgent>(StringComparer.Ordinal);
            private readonly Dictionary<string, DbAgent> _agentBySlug = new Dictionary<string, DbAgent>(StringComparer.Ordinal);
            private readonly List<DbTask> _tasks = new List<DbTask>();
            private readonly Dictionary<string, DbTask> _taskBySession = new Dictionary<string, DbTask>(StringComparer.Ordinal);
            private readonly Dictionary<string, List<DbTask>> _tasksByAgent = new Dictionary<string, List<DbTask>>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<int, string> _manifests = new Dictionary<int, string>();
            private readonly Dictionary<int, ZombieProcess> _processes = new Dictionary<int, ZombieProcess>();
            private DateTime _now;

            public Pass(ZombieAgentReaperOptions options, NightlyHost host, IFileSystem files, IZombieAgentSeams seams)
            {
                _options = options;
                _host = host;
                _files = files;
                _seams = seams;
            }

            public ZombieAgentReaperResult Go()
            {
                _now = _host.UtcNow;

                // ---- prerequisites: each must answer ("could not look" is never "no row") ----
                var abort = ReadRunner() ?? ReadDatabase();
                if (abort != null) return Abort(abort);
                ReadManifests();
                IReadOnlyList<ZombieProcess> processes;
                try
                {
                    processes = _seams.Processes() ?? Array.Empty<ZombieProcess>();
                }
                catch (Exception e) when (e is PlatformNotSupportedException || e is Win32Exception || e is InvalidOperationException || e is DllNotFoundException
                    || e is EntryPointNotFoundException)
                {
                    return Abort("ABORT: the process table could not be read: " + e.Message);
                }
                foreach (var process in processes) _processes[process.ProcessId] = process;

                // ---- classify ----
                var rows = new List<ZombieAgentRow>();
                var ignored = new List<ZombieAgentRow>();
                var unidentified = new List<ZombieAgentRow>();
                var positives = new List<ZombieAgentRow>();
                foreach (var process in processes) Classify(process, rows, ignored, unidentified, positives);
                var counts = new ZombieClassCounts(
                    rows.Count(row => row.Class == "PoolExpired"), rows.Count(row => row.Class == "ReconcilerOwned"), rows.Count(row => row.Class == "EndedButAlive"),
                    rows.Count(row => row.Class == "Unclaimed"), ignored.Count, unidentified.Count, positives.Count);

                // ---- output ----
                var output = _host.Out;
                output.WriteLine("Processes (agent-shaped): " + rows.Count + "   ignored: " + ignored.Count + "   unidentified: " + unidentified.Count + "   positives: " + positives.Count);
                output.WriteLine("  PoolExpired=" + counts.PoolExpired + "  ReconcilerOwned=" + counts.ReconcilerOwned + "  EndedButAlive=" + counts.EndedButAlive
                    + "  Unclaimed=" + counts.Unclaimed);
                output.WriteLine();
                output.WriteLine("--- census ---");
                Table(new[] { "Pid", "Exe", "Start", "WorkingSetGB", "CpuDeltaPct", "IdentityMethod", "SessionId", "DbStatus", "Agent", "Class", "RulesFailed", "KillPath", "LargeWs" },
                    Oldest(rows).Select(row => new[] { Num(row.Pid), row.Exe, Time(row.Start), Dec(row.WorkingSetGB), row.CpuDeltaPct == null ? "" : Dec(row.CpuDeltaPct.Value),
                        row.IdentityMethod, row.SessionId, row.DbStatus, row.Agent, row.Class, row.RulesFailed, row.KillPath, row.LargeWs ? "True" : "False" }));
                if (ignored.Count > 0)
                {
                    output.WriteLine("--- ignored (pre-filter) ---");
                    Table(new[] { "Pid", "Exe", "RulesFailed" }, ignored.Select(row => new[] { Num(row.Pid), row.Exe, row.RulesFailed }));
                }
                if (unidentified.Count > 0)
                {
                    output.WriteLine("--- unidentified (no I1-I5) ---");
                    Table(new[] { "Pid", "Exe", "RulesFailed" }, unidentified.Select(row => new[] { Num(row.Pid), row.Exe, row.RulesFailed }));
                }
                output.WriteLine("--- positives ---");
                Table(new[] { "Pid", "Exe", "Class", "IdentityMethod", "SessionId", "KillPath", "Agent" },
                    Oldest(positives).Select(row => new[] { Num(row.Pid), row.Exe, row.Class, row.IdentityMethod, row.SessionId, row.KillPath, row.Agent }));

                var reportFile = _files.Path.Combine(_options.ReportPath, _now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + ".json");

                // ---- execute ----
                var failedKills = 0;
                if (_options.Execute) failedKills = Kill(positives, ignored.Count);
                else output.WriteLine("Dry run: nothing was killed. Re-run with --execute to act on PoolExpired (the default --class).");
                var exitCode = _options.Execute ? (failedKills > 0 ? 1 : 0) : (positives.Count > 0 ? 3 : 0);

                // ---- report ----
                try
                {
                    if (!_files.Directory.Exists(_options.ReportPath)) _files.Directory.CreateDirectory(_options.ReportPath);
                    _files.File.WriteAllText(reportFile, OperatorApi.Pretty(Report(rows, counts, exitCode)) + Environment.NewLine);
                    output.WriteLine("Report: " + reportFile);
                }
                catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
                {
                    output.WriteLine("warning: failed to write report: " + e.Message);
                }
                return new ZombieAgentReaperResult
                {
                    ExitCode = exitCode,
                    ReportPath = reportFile,
                    Positives = positives.ToArray(),
                    Ignored = ignored.ToArray(),
                    Unidentified = unidentified.ToArray(),
                    Rows = rows.ToArray(),
                    ClassCounts = counts,
                    KillCalls = _killCalls.ToArray(),
                };
            }

            private ZombieAgentReaperResult Abort(string message)
            {
                if (message.Length > 0) _host.Out.WriteLine(message);
                return new ZombieAgentReaperResult { ExitCode = 2, KillCalls = _killCalls.ToArray() };
            }

            // GET {runner}/sessions: each session's pid and hostPid claim it. Null when it answered; else the abort line.
            private string ReadRunner()
            {
                JsonNode body;
                try
                {
                    var answer = _seams.Http("GET", _options.RunnerUrl + "/sessions", RunnerDeadline);
                    if (!OperatorApi.Succeeded(answer)) throw new InvalidOperationException(OperatorApi.Failure(answer));
                    body = string.IsNullOrWhiteSpace(answer.Body) ? null : JsonNode.Parse(answer.Body);
                }
                catch (Exception e) when (IsTransportFailure(e))
                {
                    return "ABORT: session-runner at " + _options.RunnerUrl + " did not answer GET /sessions: " + e.Message;
                }
                var sessions = body is JsonObject wrapper && wrapper.ContainsKey("sessions") ? Items(wrapper["sessions"]) : Items(body);
                foreach (var session in sessions)
                {
                    if (!(Field(session, "sessionId") is JsonValue raw) || raw.GetValueKind() != JsonValueKind.String) continue;
                    var id = raw.GetValue<string>();
                    if (id.Length == 0 || id.Contains(' ')) continue;
                    id = id.ToLowerInvariant();
                    var child = Int(Field(session, "pid"));
                    var hostPid = Int(Field(session, "hostPid"));
                    if (child > 0) _runnerByPid[child] = id;
                    if (hostPid > 0) _runnerByPid[hostPid] = id;
                }
                return null;
            }

            // The census query: the sessions, agents and tasks. Null when it answered; else the abort line.
            private string ReadDatabase()
            {
                var command = "docker exec " + _options.PgContainer + " psql ...";
                ZombieDatabaseAnswer answer;
                try
                {
                    answer = _seams.Query(_options.PgContainer, _options.PgUser, _options.PgDatabase, Sql);
                }
                catch (Exception e) when (e is Win32Exception || e is System.IO.IOException || e is InvalidOperationException)
                {
                    return "ABORT: the database did not answer (" + command + "): " + e.Message;
                }
                if (answer == null || answer.ExitCode != 0)
                {
                    _host.Out.WriteLine("ABORT: the database did not answer (" + command + " exit " + (answer?.ExitCode ?? 1).ToString(CultureInfo.InvariantCulture) + "):");
                    _host.Out.WriteLine((answer?.Output ?? "").TrimEnd());
                    return "";
                }
                JsonNode db;
                try
                {
                    db = JsonNode.Parse((answer.Output ?? "").Trim());
                }
                catch (JsonException e)
                {
                    return "ABORT: the database did not answer (JSON parse): " + e.Message;
                }
                foreach (var row in Items(Field(db, "sessions")))
                {
                    if (row == null) continue;
                    _sessions[Str(Field(row, "id")).ToLowerInvariant()] = new DbSession
                    {
                        Status = Name(Field(row, "status"), SessionStatus),
                        StartedAt = Utc(Field(row, "startedAt")),
                        EndedAt = Utc(Field(row, "endedAt")),
                        Cwd = Str(Field(row, "cwd")),
                        AgentKind = Name(Field(row, "agentKind"), AgentKind),
                    };
                }
                foreach (var row in Items(Field(db, "agents")))
                {
                    if (row == null) continue;
                    var agent = new DbAgent
                    {
                        Id = Str(Field(row, "id")),
                        Name = Str(Field(row, "name")),
                        IsPoolDelegate = Bool(Field(row, "isPoolDelegate")),
                        PersistentSessionId = Str(Field(row, "persistentSessionId")).ToLowerInvariant(),
                    };
                    _agents.Add(agent);
                    var slug = Str(Field(row, "slug"));
                    if (agent.PersistentSessionId.Length > 0) _agentBySession[agent.PersistentSessionId] = agent;
                    if (slug.Length > 0) _agentBySlug[slug.ToLowerInvariant()] = agent;
                    if (agent.Name.Length > 0) _agentBySlug[agent.Name.ToLowerInvariant()] = agent;
                }
                foreach (var row in Items(Field(db, "tasks")))
                {
                    if (row == null) continue;
                    var task = new DbTask
                    {
                        AgentId = Str(Field(row, "agentId")),
                        AgentSessionId = Str(Field(row, "agentSessionId")).ToLowerInvariant(),
                        Status = Name(Field(row, "status"), TaskStatus),
                        CompletedAt = Utc(Field(row, "completedAt")),
                        WorkingDirectory = Str(Field(row, "workingDirectory")),
                        WorktreePath = Str(Field(row, "worktreePath")),
                    };
                    _tasks.Add(task);
                    if (task.AgentSessionId.Length > 0) _taskBySession[task.AgentSessionId] = task;
                    if (task.AgentId.Length == 0) continue;
                    if (!_tasksByAgent.TryGetValue(task.AgentId, out var list)) _tasksByAgent[task.AgentId] = list = new List<DbTask>();
                    list.Add(task);
                }
                return null;
            }

            // The pty-host manifests: host pid -> session id (the session runner's adoption records).
            private void ReadManifests()
            {
                var directory = _files.Path.Combine(_options.SessionLogPath, "pty-hosts", "manifests");
                if (!_files.Directory.Exists(directory)) return;
                foreach (var file in _files.Directory.EnumerateFiles(directory, "*.json"))
                {
                    string text;
                    try
                    {
                        text = _files.File.ReadAllText(file);
                    }
                    catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
                    {
                        continue;
                    }
                    var session = ManifestSession.Match(text);
                    var hostPid = ManifestHostPid.Match(text);
                    if (session.Success && hostPid.Success && int.TryParse(hostPid.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
                        _manifests[pid] = session.Groups[1].Value;
                }
            }

            private void Classify(ZombieProcess process, List<ZombieAgentRow> rows, List<ZombieAgentRow> ignored, List<ZombieAgentRow> unidentified, List<ZombieAgentRow> positives)
            {
                // Z1
                if (!AgentShaped.Contains(process.Name ?? "")) return;
                var chain = Chain(process.ProcessId);
                var pre = new List<string>();
                if ((process.ExecutablePath ?? "").IndexOf(@"\windowsapps\", StringComparison.OrdinalIgnoreCase) >= 0) pre.Add("WindowsApps");
                if (OperatorLaunched(chain)) pre.Add("operator-launched");
                if (pre.Count > 0)
                {
                    var row = NewRow(process, "", "", "Ignored", string.Join("; ", pre), "", false, null, null, 0);
                    ignored.Add(row);
                    rows.Add(row);
                    return;
                }

                // Z2
                var identity = Resolve(process, chain);
                if (identity == null)
                {
                    var row = NewRow(process, "", "", "Unclaimed", "Z2 identity unresolved", "", false, null, null, 0);
                    unidentified.Add(row);
                    rows.Add(row);
                    return;
                }
                var sessionId = identity.SessionId;
                var agent = Owner(sessionId);
                if (!_sessions.TryGetValue(sessionId, out var session))
                {
                    rows.Add(NewRow(process, identity.Method, sessionId, "Unclaimed", "Z2 no AgentSessions row", "", identity.RunnerClaimed, agent, null, 0));
                    return;
                }

                var failed = new List<string>();
                // Z3
                if (process.CreationDate != null && session.StartedAt != null && process.CreationDate.Value < session.StartedAt.Value.AddSeconds(-_options.PidReuseToleranceSec))
                    failed.Add("Z3 pid-reuse (process start before session StartedAt)");
                // Z6
                var ageOk = true;
                if (process.CreationDate != null)
                {
                    var minutes = (_now - process.CreationDate.Value).TotalMinutes;
                    if (minutes < _options.MinDoneMinutes)
                    {
                        ageOk = false;
                        failed.Add("Z6 process only " + minutes.ToString("N0", CultureInfo.InvariantCulture) + " min old (< "
                            + _options.MinDoneMinutes.ToString(CultureInfo.InvariantCulture) + ")");
                    }
                }

                // Z4
                var pool = agent != null && agent.IsPoolDelegate;
                var open = agent != null && _tasksByAgent.TryGetValue(agent.Id, out var agentTasks) && agentTasks.Any(task => OpenTask.Contains(task.Status));
                DateTime? newestDone = null;
                if (agent != null && _tasksByAgent.TryGetValue(agent.Id, out var doneTasks))
                    foreach (var task in doneTasks)
                        if (task.CompletedAt != null && (newestDone == null || task.CompletedAt.Value > newestDone.Value)) newestDone = task.CompletedAt;
                var doneOld = newestDone != null && (_now - newestDone.Value).TotalMinutes >= _options.MinDoneMinutes;

                var className = "";
                var killPath = "";
                if (LiveSession.Contains(session.Status) && pool && !open && doneOld)
                {
                    className = "PoolExpired";
                    killPath = identity.RunnerClaimed ? "server" : "taskkill";
                }
                else if (TerminalSession.Contains(session.Status) && identity.RunnerClaimed)
                {
                    className = "ReconcilerOwned";
                    failed.Add(string.Equals(session.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                        ? "Z4 reconciler re-adopts Failed (CARD-0056)"
                        : "Z4 reconciler RetryFailedKillAsync owns Stopped");
                }
                else if (TerminalSession.Contains(session.Status))
                {
                    var endedOld = session.EndedAt != null && (_now - session.EndedAt.Value).TotalMinutes >= _options.MinDoneMinutes;
                    if (!endedOld) failed.Add("Z4 EndedAt younger than -MinDoneMinutes");
                    else
                    {
                        // Z5
                        var activity = Activity(sessionId, session);
                        var quiet = true;
                        if (activity != null)
                        {
                            var hours = (_now - activity.Value).TotalHours;
                            if (hours < _options.QuietHours)
                            {
                                quiet = false;
                                failed.Add("Z5 activity " + hours.ToString("N1", CultureInfo.InvariantCulture) + " h ago (< "
                                    + _options.QuietHours.ToString(CultureInfo.InvariantCulture) + " QuietHours)");
                            }
                        }
                        if (quiet)
                        {
                            className = "EndedButAlive";
                            killPath = "taskkill";
                        }
                    }
                }
                else failed.Add("Z4 not a zombie class (warm/standing/live)");

                var verdict = NewRow(process, identity.Method, sessionId, className, string.Join("; ", failed), killPath, identity.RunnerClaimed, agent, session,
                    TreeKillPid(chain));
                rows.Add(verdict);
                if ((className == "PoolExpired" || className == "EndedButAlive") && failed.Count == 0 && ageOk) positives.Add(verdict);
            }

            // Get-AncestorChain: the leaf, then each parent the table knows, until a pid repeats or leaves the table.
            private List<int> Chain(int leaf)
            {
                var chain = new List<int>();
                var seen = new HashSet<int>();
                var current = leaf;
                while (current > 0 && seen.Add(current))
                {
                    chain.Add(current);
                    if (!_processes.TryGetValue(current, out var process)) break;
                    current = process.ParentProcessId;
                }
                return chain;
            }

            private string ProcessName(int pid) => _processes.TryGetValue(pid, out var process) ? process.Name ?? "" : "";

            // Test-OperatorLaunched: an operator parent before any Antiphon parent.
            private bool OperatorLaunched(IEnumerable<int> chain)
            {
                foreach (var pid in chain)
                {
                    var name = ProcessName(pid);
                    if (AntiphonParent.Contains(name)) return false;
                    if (OperatorParent.Contains(name)) return true;
                }
                return false;
            }

            // Get-TopAntiphonAncestor: the nearest Antiphon parent, else the topmost cmd.exe/node.exe, else the leaf.
            private int TreeKillPid(IReadOnlyList<int> chain)
            {
                if (chain.Count == 0) return 0;
                var last = chain[0];
                foreach (var pid in chain)
                {
                    var name = ProcessName(pid);
                    if (AntiphonParent.Contains(name)) return pid;
                    if (string.Equals(name, "cmd.exe", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "node.exe", StringComparison.OrdinalIgnoreCase)) last = pid;
                }
                return last;
            }

            // Resolve-Identity: I1-I5, first that answers wins.
            private Identity Resolve(ZombieProcess process, IReadOnlyList<int> chain)
            {
                foreach (var pid in chain)
                    if (_runnerByPid.TryGetValue(pid, out var claimed)) return new Identity("I1", claimed, true);
                foreach (var pid in chain)
                    if (string.Equals(ProcessName(pid), "Antiphon.PtyHost.exe", StringComparison.OrdinalIgnoreCase) && _manifests.TryGetValue(pid, out var manifest))
                        return new Identity("I2", manifest.ToLowerInvariant(), false);
                var commandLine = process.CommandLine ?? "";
                var fromCommand = SessionIdArgument.Match(commandLine);
                if (fromCommand.Success) return new Identity("I3", fromCommand.Groups[1].Value.ToLowerInvariant(), false);
                var name = NameArgument.Match(commandLine);
                if (name.Success && _agentBySlug.TryGetValue(name.Groups[1].Value.ToLowerInvariant(), out var named) && named.PersistentSessionId.Length > 0)
                    return new Identity("I4", named.PersistentSessionId, false);
                var leaf = CardTaskLeaf(process.Cwd) ?? CardTaskLeaf(commandLine);
                if (leaf != null)
                    foreach (var task in _tasks)
                        if ((CardTaskLeaf(task.WorktreePath) ?? CardTaskLeaf(task.WorkingDirectory)) == leaf && task.AgentSessionId.Length > 0)
                            return new Identity("I5", task.AgentSessionId, false);
                return null;
            }

            // Get-OwnerAgent: the agent whose persistent session it is, else the agent of the task that ran it.
            private DbAgent Owner(string sessionId)
            {
                if (_agentBySession.TryGetValue(sessionId, out var agent)) return agent;
                if (_taskBySession.TryGetValue(sessionId, out var task))
                    return _agents.FirstOrDefault(candidate => string.Equals(candidate.Id, task.AgentId, StringComparison.OrdinalIgnoreCase));
                return null;
            }

            // Get-ActivityMtime: the newest of the session's ansi log and, for Claude, its transcripts; null when none exists.
            private DateTime? Activity(string sessionId, DbSession session)
            {
                DateTime? best = null;
                var ansi = _files.Path.Combine(_options.SessionLogPath, sessionId.Replace("-", "") + ".ansi.log");
                if (_files.File.Exists(ansi)) best = _files.File.GetLastWriteTimeUtc(ansi);
                if (string.Equals(session.AgentKind, "ClaudeCode", StringComparison.OrdinalIgnoreCase) && session.Cwd.Length > 0)
                {
                    var project = _files.Path.Combine(_options.UserProfile, ".claude", "projects", ClaudeProjectDirectory(session.Cwd));
                    if (_files.Directory.Exists(project))
                        foreach (var file in _files.Directory.EnumerateFiles(project, "*.jsonl"))
                        {
                            var written = _files.File.GetLastWriteTimeUtc(file);
                            if (best == null || written > best.Value) best = written;
                        }
                }
                return best;
            }

            private ZombieAgentRow NewRow(ZombieProcess process, string method, string sessionId, string className, string rulesFailed, string killPath,
                bool runnerClaimed, DbAgent agent, DbSession session, int treeKillPid)
            {
                var gigabytes = process.WorkingSetSize != 0 ? Math.Round(process.WorkingSetSize / 1073741824.0, 3) : 0;
                return new ZombieAgentRow
                {
                    Pid = process.ProcessId,
                    Exe = process.Name ?? "",
                    Start = process.CreationDate,
                    WorkingSetGB = gigabytes,
                    CpuDeltaPct = process.CpuDeltaPercent,
                    IdentityMethod = method,
                    SessionId = sessionId,
                    DbStatus = session?.Status ?? "",
                    Agent = agent?.Name ?? "",
                    Class = className,
                    RulesFailed = rulesFailed,
                    KillPath = killPath,
                    RunnerClaimed = runnerClaimed,
                    LargeWs = _options.MinWorkingSetGB > 0 && gigabytes >= _options.MinWorkingSetGB,
                    TreeKillPid = treeKillPid,
                };
            }

            // The positives of the --class classes, oldest first (at most --limit): the coherent kill, then the leaf must be gone.
            private int Kill(List<ZombieAgentRow> positives, int ignoredCount)
            {
                var output = _host.Out;
                var classes = _options.ExecuteClasses;
                var queue = Oldest(positives).ToList();
                if (_options.Limit > 0 && queue.Count > _options.Limit)
                {
                    output.WriteLine("--limit " + _options.Limit + ": killing the " + _options.Limit + " oldest of " + queue.Count + " positives this run.");
                    queue = queue.Take(_options.Limit).ToList();
                }
                var killed = 0;
                var failedKills = 0;
                foreach (var row in queue)
                {
                    if (!classes.Contains(row.Class))
                    {
                        output.WriteLine("skip         pid " + row.Pid + "  class " + row.Class + " not in --class " + _options.Class);
                        continue;
                    }
                    var ok = false;
                    var path = row.KillPath;
                    if (row.Class == "PoolExpired" && row.RunnerClaimed)
                    {
                        var server = Post(_options.ServerUrl + "/api/sessions/" + row.SessionId + "/kill");
                        if (server == null)
                        {
                            ok = true;
                            path = "server";
                        }
                        else
                        {
                            output.WriteLine("server kill failed  " + row.SessionId + "  " + server + "  falling back to runner");
                            var runner = Post(_options.RunnerUrl + "/sessions/" + row.SessionId + "/kill");
                            if (runner == null)
                            {
                                ok = true;
                                path = "runner";
                            }
                            else
                            {
                                output.WriteLine("runner kill failed  " + row.SessionId + "  " + runner + "  falling through to taskkill");
                                path = "taskkill";
                            }
                        }
                    }
                    else if (row.Class == "PoolExpired") path = "taskkill";

                    if (path == "taskkill")
                    {
                        var tree = row.TreeKillPid != 0 ? row.TreeKillPid : row.Pid;
                        _killCalls.Add(new ZombieKillCall("TASKKILL", "pid:" + tree.ToString(CultureInfo.InvariantCulture)));
                        _seams.TreeKill(tree);
                        ok = true;
                    }
                    if (!ok)
                    {
                        failedKills++;
                        continue;
                    }

                    var gone = false;
                    var deadline = _host.UtcNow.AddSeconds(_options.KillVerifySeconds);
                    while (_host.UtcNow < deadline)
                    {
                        if (!_seams.IsAlive(row.Pid))
                        {
                            gone = true;
                            break;
                        }
                        _host.Sleep(TimeSpan.FromMilliseconds(500));
                    }
                    if (gone)
                    {
                        killed++;
                        output.WriteLine("killed       pid " + row.Pid + "  " + row.Class + "  " + path + "  " + row.SessionId);
                    }
                    else
                    {
                        failedKills++;
                        output.WriteLine("STILL ALIVE  pid " + row.Pid + "  after " + _options.KillVerifySeconds + "s");
                    }
                }
                output.WriteLine();
                output.WriteLine("Killed and verified: " + killed + "   failed: " + failedKills + "   positives: " + positives.Count + "   ignored: " + ignoredCount);
                return failedKills;
            }

            // One kill request: null when it answered 2xx, else why not.
            private string Post(string uri)
            {
                _killCalls.Add(new ZombieKillCall("POST", uri));
                try
                {
                    var answer = _seams.Http("POST", uri, KillDeadline);
                    return OperatorApi.Succeeded(answer) ? null : OperatorApi.Failure(answer);
                }
                catch (Exception e) when (IsTransportFailure(e))
                {
                    return e.Message;
                }
            }

            private JsonObject Report(IEnumerable<ZombieAgentRow> rows, ZombieClassCounts counts, int exitCode) => new JsonObject
            {
                ["generatedAt"] = _host.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["now"] = _now.ToString("o", CultureInfo.InvariantCulture),
                ["execute"] = _options.Execute,
                ["classFilter"] = _options.Class,
                ["quietHours"] = _options.QuietHours,
                ["minDoneMinutes"] = _options.MinDoneMinutes,
                ["minWorkingSetGB"] = _options.MinWorkingSetGB,
                ["counts"] = new JsonObject
                {
                    ["PoolExpired"] = counts.PoolExpired,
                    ["ReconcilerOwned"] = counts.ReconcilerOwned,
                    ["EndedButAlive"] = counts.EndedButAlive,
                    ["Unclaimed"] = counts.Unclaimed,
                    ["Ignored"] = counts.Ignored,
                    ["Unidentified"] = counts.Unidentified,
                    ["Positives"] = counts.Positives,
                },
                ["rows"] = new JsonArray(rows.Select(row => (JsonNode)new JsonObject
                {
                    ["pid"] = row.Pid,
                    ["exe"] = row.Exe,
                    ["start"] = row.Start == null ? null : row.Start.Value.ToString("o", CultureInfo.InvariantCulture),
                    ["workingSetGB"] = row.WorkingSetGB,
                    ["cpuDeltaPct"] = row.CpuDeltaPct,
                    ["identityMethod"] = row.IdentityMethod,
                    ["sessionId"] = row.SessionId,
                    ["dbStatus"] = row.DbStatus,
                    ["agent"] = row.Agent,
                    ["class"] = row.Class,
                    ["rulesFailed"] = row.RulesFailed,
                    ["killPath"] = row.KillPath,
                    ["largeWs"] = row.LargeWs,
                    ["runnerClaimed"] = row.RunnerClaimed,
                }).ToArray()),
                ["killCalls"] = new JsonArray(_killCalls.Select(call => (JsonNode)new JsonObject { ["Method"] = call.Method, ["Uri"] = call.Uri }).ToArray()),
                ["exitCode"] = exitCode,
            };

            // Format-Table: a header, a rule and one line per row, each column as wide as its widest cell; nothing for no rows.
            private void Table(string[] headers, IEnumerable<string[]> cells)
            {
                var rows = cells.ToList();
                if (rows.Count == 0) return;
                var widths = headers.Select((header, i) => Math.Max(header.Length, rows.Max(row => (row[i] ?? "").Length))).ToArray();
                _host.Out.WriteLine(string.Join(" ", headers.Select((header, i) => header.PadRight(widths[i]))).TrimEnd());
                _host.Out.WriteLine(string.Join(" ", widths.Select(width => new string('-', width))));
                foreach (var row in rows) _host.Out.WriteLine(string.Join(" ", row.Select((cell, i) => (cell ?? "").PadRight(widths[i]))).TrimEnd());
                _host.Out.WriteLine();
            }

            // Sort-Object Start: the oldest first, an unknown start before any known one (stable).
            private static IEnumerable<ZombieAgentRow> Oldest(IEnumerable<ZombieAgentRow> rows) => rows.OrderBy(row => row.Start ?? DateTime.MinValue);

            private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

            private static string Dec(double value) => value.ToString(CultureInfo.InvariantCulture);

            private static string Time(DateTime? value) => value == null ? "" : value.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        // The production machine (Windows): HTTP on one client (each request under its own deadline), docker on the host's launcher
        // (its output read), the Win32 process table sampled twice five seconds apart, taskkill on the host's launcher and the
        // host's process table for liveness.
        private sealed class ZombieAgentOs : IZombieAgentSeams
        {
            private static readonly HttpClient Client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            private readonly NightlyHost _host;

            public ZombieAgentOs(NightlyHost host) => _host = host;

            public NightlyReportHttpResponse Http(string method, string uri, TimeSpan timeout)
            {
                using var deadline = new CancellationTokenSource(timeout);
                using var request = new HttpRequestMessage(new HttpMethod(method), uri);
                using var response = Client.SendAsync(request, deadline.Token).GetAwaiter().GetResult();
                var body = response.Content.ReadAsStringAsync(deadline.Token).GetAwaiter().GetResult();
                return new NightlyReportHttpResponse((int)response.StatusCode, body ?? "");
            }

            public ZombieDatabaseAnswer Query(string container, string user, string database, string sql)
            {
                var run = _host.Launch(new NightlyLaunch("docker", new[] { "exec", container, "psql", "-U", user, "-d", database, "-At", "-c", sql }, "", CaptureOutput: true));
                return new ZombieDatabaseAnswer(run.ExitCode, run.ExitCode == 0 ? run.StandardOutputText : run.LogText);
            }

            public IReadOnlyList<ZombieProcess> Processes()
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("the census reads the Windows process table.");
                var first = Win32Processes.Read();
                _host.Sleep(TimeSpan.FromSeconds(5));
                var second = new Dictionary<int, long>();
                foreach (var row in Win32Processes.Read()) second[row.Process.ProcessId] = row.CpuTime;
                var cores = Math.Max(1, Environment.ProcessorCount);
                const double Interval = 5.0 * 10000000.0;
                return first.Select(row => row.Process with
                {
                    CpuDeltaPercent = second.TryGetValue(row.Process.ProcessId, out var later)
                        ? Math.Round(Math.Max(0, later - row.CpuTime) / Interval * 100.0 / cores, 1)
                        : (double?)null,
                }).ToArray();
            }

            public void TreeKill(int pid) =>
                _host.Launch(new NightlyLaunch("taskkill", new[] { "/T", "/F", "/PID", pid.ToString(CultureInfo.InvariantCulture) }, "", CaptureOutput: true));

            public bool IsAlive(int pid) => _host.Processes.IsAlive(pid);
        }

        // Win32_Process's fields without WMI: the toolhelp snapshot (pid, parent, executable name), then per process the image
        // path, creation and CPU times (a limited-query handle; a process that refuses one keeps them empty, as WMI does), the
        // command line (QualificationOs) and the working set (the system process list). PtyHostReaper and WatchedTestRun read the
        // same table (unit 38).
        internal static class Win32Processes
        {
            private const uint SnapProcess = 0x2;
            private const int QueryLimited = 0x1000;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct ProcessEntry
            {
                public uint Size;
                public uint Usage;
                public uint ProcessId;
                public IntPtr DefaultHeapId;
                public uint ModuleId;
                public uint Threads;
                public uint ParentProcessId;
                public int PriorityClassBase;
                public uint Flags;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
            }

            [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
            [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool First(IntPtr snapshot, ref ProcessEntry entry);
            [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Next(IntPtr snapshot, ref ProcessEntry entry);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int processId);
            [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ImageName(IntPtr process, int flags, StringBuilder name, ref int size);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
            [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

            public static List<(ZombieProcess Process, long CpuTime)> Read()
            {
                var workingSets = new Dictionary<int, long>();
                foreach (var process in Process.GetProcesses())
                    using (process)
                    {
                        try { workingSets[process.Id] = process.WorkingSet64; }
                        catch (InvalidOperationException) { }
                    }
                var commandLines = new QualificationOs();
                var rows = new List<(ZombieProcess Process, long CpuTime)>();
                var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
                if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateToolhelp32Snapshot");
                try
                {
                    var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
                    for (var more = First(snapshot, ref entry); more; more = Next(snapshot, ref entry))
                    {
                        var pid = (int)entry.ProcessId;
                        if (pid <= 0) continue;
                        string path = "";
                        DateTime? created = null;
                        long cpu = 0;
                        var handle = OpenProcess(QueryLimited, false, pid);
                        if (handle != IntPtr.Zero)
                        {
                            try
                            {
                                var name = new StringBuilder(1024);
                                var size = name.Capacity;
                                if (ImageName(handle, 0, name, ref size)) path = name.ToString(0, size);
                                if (GetProcessTimes(handle, out var creation, out _, out var kernel, out var user))
                                {
                                    if (creation > 0) created = DateTime.FromFileTimeUtc(creation);
                                    cpu = kernel + user;
                                }
                            }
                            finally
                            {
                                CloseHandle(handle);
                            }
                        }
                        rows.Add((new ZombieProcess(pid, (int)entry.ParentProcessId, entry.ExeFile ?? "", path, commandLines.CommandLine(pid) ?? "", "", created,
                            workingSets.TryGetValue(pid, out var workingSet) ? workingSet : 0, null), cpu));
                        entry.Size = (uint)Marshal.SizeOf<ProcessEntry>();
                    }
                }
                finally
                {
                    CloseHandle(snapshot);
                }
                return rows;
            }
        }
    }
}
