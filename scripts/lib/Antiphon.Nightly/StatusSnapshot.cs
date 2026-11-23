// CARD-1500, CARD-2004: raw, read-only status facts for an operator status update. It replaced scripts/status-snapshot.ps1,
// deleted 2026-11-23 (unit 7 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
// `antiphon-scripts status-snapshot` (.claude/skills/antiphon-status-update/SKILL.md).
//
// Every request is a GET to the API (the CLI passes ANTIPHON_API, default http://localhost:17202) with the task token header
// when ANTIPHON_TASK_TOKEN is set and the registered caller label (AntiphonCaller, the C# reading of
// scripts/lib/antiphon-caller.ps1: the label stays "status-snapshot.ps1", the server's registered metric key). No fetch,
// refresh, dispatch, slot release, state writer or output file is invoked. --project-id scopes the task counts; without it a
// unique project is discovered from the pipeline, and ambiguity is an incomplete observation, never fleet counts presented
// as project counts. git reads (remote master and rc refs, cached origin/master history) run with fixed arguments, no
// credential helper or prompt, and a 20 s deadline. The rc facts (TRX count, schedule, active chunk, last run, heartbeat
// age) are read through RcQualificationRecords. Exit 0: every requested fact read; 2: partial facts, the problems in the
// output. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The registered X-Antiphon-Caller labels (CARD-2004), as scripts/lib/antiphon-caller.ps1 and the server list them.</summary>
    public static class AntiphonCaller
    {
        /// <summary>The header name.</summary>
        public const string Header = "X-Antiphon-Caller";

        /// <summary>The registered labels (AgentTaskGetPollMetrics.RegisteredLabels).</summary>
        public static readonly IReadOnlyList<string> RegisteredLabels = new[] { "board-watch", "card.ps1", "checkpoint-task.ps1", "delegate.ps1", "status-snapshot.ps1", "useagenttask" };

        /// <summary>
        /// Add-AntiphonCallerHeader: the label trimmed and lower-cased when it is registered; InvalidOperationException
        /// otherwise (the value is not copied into the message).
        /// </summary>
        public static string Label(string label)
        {
            var text = (label ?? "").Trim().ToLowerInvariant();
            if (text.Length == 0 || text.Length > 256 || !RegisteredLabels.Contains(text, StringComparer.Ordinal))
                throw new InvalidOperationException("X-Antiphon-Caller is not a registered label.");
            return text;
        }
    }

    /// <summary>The status-snapshot command's options.</summary>
    public sealed class StatusSnapshotOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "status-snapshot [--project-id <guid>] [--runner server2] [--json] [--rc-run-log-dir <dir>] [--rc-state-dir <dir>]"
            + " [--rc-heartbeat-path <file>] [--repository <dir>]";

        private static readonly Regex RunnerPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant);

        /// <summary>The project whose tasks are counted; empty: discovered from the pipeline.</summary>
        public string ProjectId { get; init; } = "";

        /// <summary>The session runner whose slots are read.</summary>
        public string Runner { get; init; } = "server2";

        /// <summary>One indented JSON document instead of the text lines.</summary>
        public bool Json { get; init; }

        /// <summary>The rc run's log directory (TRX count, schedule, active chunk); empty: not read.</summary>
        public string RcRunLogDir { get; init; } = "";

        /// <summary>The rc run's state directory (last-run.json; its heartbeat when no heartbeat path is given); empty: not read.</summary>
        public string RcStateDir { get; init; } = "";

        /// <summary>The supervisor heartbeat (normally in the task directory); empty: not read.</summary>
        public string RcHeartbeatPath { get; init; } = "";

        /// <summary>The checkout whose git facts are read.</summary>
        public string Repository { get; init; } = "";

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = "http://localhost:17202";

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: no token header.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>The arguments after the command name; the API, the token and the default repository come from the CLI.</summary>
        public static StatusSnapshotOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken, string defaultRepository)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--project-id", "--runner", "--rc-run-log-dir", "--rc-state-dir", "--rc-heartbeat-path", "--repository" }, new[] { "--json" });
            var runner = flags.Value("--runner", "server2");
            if (!RunnerPattern.IsMatch(runner ?? "")) throw new NightlyUsageException("--runner must match " + RunnerPattern + ", not '" + runner + "'");
            var repository = flags.Value("--repository", "");
            return new StatusSnapshotOptions
            {
                ProjectId = flags.Value("--project-id", ""),
                Runner = runner,
                Json = flags.Switch("--json"),
                RcRunLogDir = flags.Value("--rc-run-log-dir", ""),
                RcStateDir = flags.Value("--rc-state-dir", ""),
                RcHeartbeatPath = flags.Value("--rc-heartbeat-path", ""),
                Repository = string.IsNullOrWhiteSpace(repository) ? defaultRepository ?? "" : repository,
                Api = (string.IsNullOrWhiteSpace(api) ? "http://localhost:17202" : api).TrimEnd('/'),
                TaskToken = taskToken ?? "",
            };
        }
    }

    /// <summary>The status snapshot (status-snapshot.ps1).</summary>
    public static class StatusSnapshot
    {
        /// <summary>The registered caller label this command sends (unchanged from the script, the server's metric key).</summary>
        public const string CallerLabel = "status-snapshot.ps1";

        /// <summary>Each request's and each git read's deadline.</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

        private static readonly Regex RefLine = new Regex(@"^([0-9a-f]{40,64})\s+(refs/heads/.+)$", RegexOptions.CultureInvariant);

        /// <summary>
        /// The snapshot on host.Out: text lines (OBSERVED, TASKS, MASTER, one compact JSON line per fact, UNVERIFIED per problem)
        /// or, with --json, one indented document. http answers each GET (production: NightlyReport.HttpTransport with the
        /// 20 s deadline); git each git read (production: ReadGit). Exit 0 every fact read, 2 otherwise.
        /// </summary>
        public static int Run(StatusSnapshotOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, NightlyGit git)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            if (git == null) throw new ArgumentNullException(nameof(git));
            var problems = new List<string>();
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(options.TaskToken)) headers["X-Antiphon-Task-Token"] = options.TaskToken;
            headers[AntiphonCaller.Header] = AntiphonCaller.Label(CallerLabel);

            JsonNode Get(string path)
            {
                try
                {
                    var answer = http("GET", options.Api + path, headers, null);
                    if (answer.Status < 200 || answer.Status > 299)
                    {
                        // Never echo a body, URL, header or credential helper output.
                        problems.Add("GET " + path + " unavailable (HTTP " + ((System.Net.HttpStatusCode)answer.Status).ToString() + ").");
                        return null;
                    }
                    return string.IsNullOrWhiteSpace(answer.Body) ? null : JsonNode.Parse(answer.Body);
                }
                catch (JsonException)
                {
                    problems.Add("GET " + path + " unavailable (invalid JSON).");
                    return null;
                }
                catch (Exception)
                {
                    problems.Add("GET " + path + " unavailable (HTTP unreachable).");
                    return null;
                }
            }
            JsonNode Fact(string path)
            {
                var read = RcQualificationRecords.Read(files, path);
                if (!read.Ok)
                {
                    problems.Add(path + " unavailable (" + read.Reason + ").");
                    return null;
                }
                return JsonNode.Parse(read.Record.Value.GetRawText());
            }
            string Git(string label, params string[] arguments)
            {
                try
                {
                    var result = git(Path.GetFullPath(options.Repository), new[] { "-c", "credential.helper=", "-c", "credential.interactive=false" }.Concat(arguments).ToArray());
                    if (result.ExitCode != 0) throw new InvalidOperationException("git unavailable");
                    return result.Output.Trim();
                }
                catch (Exception)
                {
                    problems.Add(label + " unavailable.");
                    return null;
                }
            }

            var projectId = options.ProjectId;
            JsonNode pipeline = null;
            if (string.IsNullOrWhiteSpace(projectId))
            {
                var discovery = Get("/api/agent-tasks/pipeline");
                var projects = Items(discovery?["concurrencyScopes"]).Select(scope => Text(scope?["projectId"])).Where(id => !string.IsNullOrEmpty(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (projects.Length == 1) projectId = projects[0];
                else problems.Add("Project cannot be resolved uniquely from pipeline; supply --project-id.");
            }
            if (!string.IsNullOrEmpty(projectId))
            {
                if (!Guid.TryParse(projectId, out var parsed)) problems.Add("ProjectId must be a GUID.");
                else pipeline = Get("/api/agent-tasks/pipeline?projectId=" + parsed.ToString());
            }
            JsonObject counts = null;
            var details = new JsonArray();
            if (pipeline?["stages"] is JsonArray stages)
            {
                int inFlight = 0, queued = 0, blocked = 0;
                var taskIds = new List<string>();
                foreach (var stage in stages)
                {
                    var flying = Items(stage?["inFlight"]).ToArray();
                    var waiting = Items(stage?["queued"]).ToArray();
                    var held = Items(stage?["blocked"]).ToArray();
                    inFlight += flying.Length;
                    queued += waiting.Length;
                    blocked += held.Length;
                    foreach (var row in flying.Concat(waiting).Concat(held))
                        if (!string.IsNullOrEmpty(Text(row?["taskId"]))) taskIds.Add(Text(row["taskId"]));
                    foreach (var row in Items(stage?["ready"]))
                        if (!string.IsNullOrEmpty(Text(row?["sourcePlanTaskId"]))) taskIds.Add(Text(row["sourcePlanTaskId"]));
                }
                counts = new JsonObject { ["inFlight"] = inFlight, ["queued"] = queued, ["blocked"] = blocked };
                foreach (var id in taskIds.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
                {
                    var detail = Get("/api/agent-tasks/" + Uri.EscapeDataString(id));
                    if (detail != null) details.Add(detail);
                }
            }
            else
            {
                problems.Add("Scoped pipeline stages unavailable; task counts unknown.");
            }
            var slots = Get("/api/session-runners/" + Uri.EscapeDataString(options.Runner) + "/slots");
            var instructions = Get("/api/orchestrator-instructions");
            var refs = Git("Remote master/rc refs", "ls-remote", "--heads", "origin", "refs/heads/master", "refs/heads/rc/*");
            string tip = null;
            var rcRefs = new JsonArray();
            foreach (var line in (refs ?? "").Split('\n'))
            {
                var match = RefLine.Match(line.TrimEnd('\r'));
                if (!match.Success) continue;
                if (match.Groups[2].Value == "refs/heads/master") tip = match.Groups[1].Value;
                else rcRefs.Add(new JsonObject { ["sha"] = match.Groups[1].Value, ["ref"] = match.Groups[2].Value });
            }
            if (tip == null) problems.Add("Remote master tip unknown.");
            var history = Git("Cached origin/master history", "log", "-8", "--format=%H%x09%s", "origin/master");

            JsonObject rc = null;
            if (!string.IsNullOrEmpty(options.RcRunLogDir) || !string.IsNullOrEmpty(options.RcStateDir) || !string.IsNullOrEmpty(options.RcHeartbeatPath))
                rc = RcFacts(options, host, files, problems, Fact);

            var snapshot = new JsonObject
            {
                ["observedAtUtc"] = host.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["projectId"] = string.IsNullOrEmpty(projectId) ? null : projectId,
                ["runner"] = options.Runner,
                ["counts"] = counts,
                ["pipeline"] = pipeline,
                ["taskDetails"] = details,
                ["slots"] = slots,
                ["orchestratorInstructions"] = instructions,
                ["master"] = new JsonObject { ["tip"] = tip, ["cachedOriginMasterLog"] = history, ["historyIsCached"] = true },
                ["rcRefs"] = rcRefs,
                ["rc"] = rc,
                ["errors"] = new JsonArray(problems.Select(problem => (JsonNode)problem).ToArray()),
            };
            if (options.Json)
            {
                host.Out.WriteLine(RcQualificationRecords.Indented(snapshot));
            }
            else
            {
                host.Out.WriteLine("OBSERVED " + (string)snapshot["observedAtUtc"] + " project=" + projectId + " runner=" + options.Runner);
                host.Out.WriteLine(counts == null ? "TASKS counts unknown" : "TASKS inFlight=" + counts["inFlight"] + " queued=" + counts["queued"] + " blocked=" + counts["blocked"]);
                host.Out.WriteLine("MASTER tip=" + tip + " (remote); history below is cached origin/master, fetch separately");
                foreach (var key in new[] { "pipeline", "taskDetails", "slots", "orchestratorInstructions", "master", "rcRefs", "rc" })
                    host.Out.WriteLine(key + "=" + RcQualificationRecords.Compact(snapshot[key]));
                foreach (var problem in problems) host.Out.WriteLine("UNVERIFIED " + problem);
            }
            return problems.Count > 0 ? 2 : 0;
        }

        // The rc block: TRX count, newest file, schedule and active chunk under the run log directory; last-run.json (and the
        // active chunk under its timing root) in the state directory; the heartbeat and its age.
        private static JsonObject RcFacts(StatusSnapshotOptions options, NightlyHost host, IFileSystem files, List<string> problems, Func<string, JsonNode> fact)
        {
            var rc = new JsonObject
            {
                ["runLogDir"] = Optional(options.RcRunLogDir),
                ["stateDir"] = Optional(options.RcStateDir),
                ["trxCount"] = null,
                ["scheduledEntries"] = null,
                ["newestFile"] = null,
                ["newestFileAgeSeconds"] = null,
                ["activeChunk"] = null,
                ["lastRun"] = null,
                ["heartbeat"] = null,
                ["heartbeatAgeSeconds"] = null,
            };
            var now = host.UtcNow;
            if (!string.IsNullOrEmpty(options.RcRunLogDir))
            {
                IFileInfo[] logFiles = Array.Empty<IFileInfo>();
                try
                {
                    // Get-ChildItem -File -Recurse without -Force: hidden and system files left out; an unreadable directory fails.
                    logFiles = files.DirectoryInfo.New(options.RcRunLogDir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false }).ToArray();
                    rc["trxCount"] = logFiles.Count(file => string.Equals(file.Extension, ".trx", StringComparison.OrdinalIgnoreCase));
                    var newest = logFiles.OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
                    if (newest != null)
                    {
                        rc["newestFile"] = newest.FullName;
                        rc["newestFileAgeSeconds"] = Math.Max(0, (now - newest.LastWriteTimeUtc).TotalSeconds);
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is System.Security.SecurityException)
                {
                    problems.Add("RcRunLogDir could not be enumerated; TRX count unknown.");
                }
                var schedule = fact(files.Path.Combine(options.RcRunLogDir, "dispatch-schedule.json"));
                var entries = schedule?["entries"];
                if (entries != null) rc["scheduledEntries"] = entries is JsonArray array ? array.Count : 1;
                // The active identity may live beneath the run's timing root. Refuse ambiguity.
                var active = logFiles.Where(file => string.Equals(file.Name, "active-chunk.json", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (active.Length == 1) rc["activeChunk"] = fact(active[0].FullName);
                else if (active.Length > 1) problems.Add("Multiple active-chunk records; identity requires owner verification.");
            }
            var heartbeatPath = options.RcHeartbeatPath;
            if (!string.IsNullOrEmpty(options.RcStateDir))
            {
                var lastRun = fact(files.Path.Combine(options.RcStateDir, "last-run.json"));
                rc["lastRun"] = lastRun;
                var timingRoot = Text(lastRun?["timingRoot"]);
                if (rc["activeChunk"] == null && !string.IsNullOrEmpty(timingRoot)) rc["activeChunk"] = fact(files.Path.Combine(timingRoot, "active-chunk.json"));
                if (string.IsNullOrEmpty(heartbeatPath)) heartbeatPath = files.Path.Combine(options.RcStateDir, "supervisor-heartbeat.json");
            }
            if (!string.IsNullOrEmpty(heartbeatPath))
            {
                var heartbeat = fact(heartbeatPath);
                rc["heartbeat"] = heartbeat;
                var stamp = Text(heartbeat?["observedAtUtc"]);
                if (string.IsNullOrEmpty(stamp)) stamp = Text(heartbeat?["utc"]);
                if (!string.IsNullOrEmpty(stamp))
                {
                    if (DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                        rc["heartbeatAgeSeconds"] = Math.Max(0, (new DateTimeOffset(now, TimeSpan.Zero) - at).TotalSeconds);
                    else problems.Add("Heartbeat timestamp could not be parsed.");
                }
                else if (heartbeat != null)
                {
                    problems.Add("Heartbeat timestamp unknown.");
                }
            }
            if (!string.IsNullOrEmpty(options.RcRunLogDir) && rc["activeChunk"] == null) problems.Add("Active chunk unknown; inspect the recorded timing root.");
            return rc;
        }

        private static JsonNode Optional(string text) => string.IsNullOrEmpty(text) ? null : JsonValue.Create(text);

        // An array's items; one value as one item; nothing for null.
        private static IEnumerable<JsonNode> Items(JsonNode node) => node switch
        {
            null => Array.Empty<JsonNode>(),
            JsonArray array => array,
            _ => new[] { node },
        };

        // A scalar's text ([string]): a string as is, another value as its JSON text; null for null.
        private static string Text(JsonNode node)
        {
            if (node == null) return null;
            if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
            return node.ToJsonString();
        }

        /// <summary>
        /// The production git read (Read-GitFact): git with the caller's arguments in the working directory, prompts and
        /// optional locks off (GIT_TERMINAL_PROMPT=0, GIT_OPTIONAL_LOCKS=0), stdout only, killed with its tree after 20 s
        /// (exit 1 then). The output is stdout as read.
        /// </summary>
        public static NightlyGitResult ReadGit(string workingDirectory, IReadOnlyList<string> arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? "",
                StandardOutputEncoding = new UTF8Encoding(false),
            };
            foreach (var argument in arguments ?? Array.Empty<string>()) start.ArgumentList.Add(argument);
            start.Environment["GIT_TERMINAL_PROMPT"] = "0";
            start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            using var child = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit((int)Deadline.TotalMilliseconds))
            {
                try { child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                child.WaitForExit(5000);
                return new NightlyGitResult(1, "");
            }
            child.WaitForExit();
            if (!stdout.Wait(1000) || !stderr.Wait(1000)) return new NightlyGitResult(1, "");
            return new NightlyGitResult(child.ExitCode, stdout.Result);
        }
    }
}
