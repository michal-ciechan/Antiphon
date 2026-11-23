// Observe new terminal and Blocked task states in one project or board (the rc qualification skill's task watcher). It
// replaced scripts/orchestration/watch-tasks.ps1 (unit 25 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md);
// the CLI runs it as `antiphon-scripts watch-tasks` from a Monitor task (.claude/skills/antiphon-rc-qualification/SKILL.md).
//
//   watch-tasks (--project-id <guid> | --board-id <guid>) --state-file <file> [--api <url>] [--poll-seconds 45]
//               [--max-minutes 28] [--once]
//
// Read-only polling of GET /api/agent-tasks?projectId=<guid> (or boardId), 20 s per request, with X-Antiphon-Task-Token when
// ANTIPHON_TASK_TOKEN is set (the CLI passes it and ANTIPHON_API in; --api overrides the API). It writes only the
// caller-owned state file, {"scope":"projectId=<guid>","lastStatus":{"<task id>":"<status>",...}}, after every successful
// poll, and reuses it on re-arm. The first successful poll of a new state baselines the tasks it finds; later polls print
// "TASK-<STATUS> <id> <role> <title>" when a task's status changes to Succeeded, Failed, Blocked or Canceled (ids and
// statuses compared ignoring case, as the script's hashtable did), so a re-Blocked or re-Failed after Working is reported
// and an unchanged repeat is not. A state of the legacy shape {"scope":...,"seen":[...]} prints
// "TASK-STATE-UPGRADE baselining legacy seen-pair state" and baselines afresh. A failed poll prints
// "TASK-WATCH-ERROR <reason>" and the watch goes on. Between polls it sleeps (the host's Sleep, clamped to the window) until
// --max-minutes have passed, or stops after one poll with --once, and ends with "watch-tasks window ended; re-arm". Exit 0;
// 1 when the state file is unreadable, of another scope or has no lastStatus map (one stderr line; nothing is polled).
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Antiphon.Nightly
{
    /// <summary>The watch-tasks command's options (watch-tasks.ps1 -ProjectId|-BoardId -StateFile -Api -PollSeconds -MaxMinutes -Once).</summary>
    public sealed class WatchTasksOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "watch-tasks (--project-id <guid> | --board-id <guid>) --state-file <file> [--api <url>] [--poll-seconds 45] [--max-minutes 28] [--once]";

        /// <summary>The query scope: "projectId=&lt;guid&gt;" or "boardId=&lt;guid&gt;".</summary>
        public string Scope { get; init; } = "";

        /// <summary>The caller-owned state file.</summary>
        public string StateFile { get; init; } = "";

        /// <summary>The API base, without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>Seconds between polls, 1 to 300.</summary>
        public int PollSeconds { get; init; } = 45;

        /// <summary>The watch window in minutes, 1 to 29.</summary>
        public int MaxMinutes { get; init; } = 28;

        /// <summary>One poll, then stop.</summary>
        public bool Once { get; init; }

        /// <summary>
        /// The arguments after the command name; api (ANTIPHON_API) and taskToken (ANTIPHON_TASK_TOKEN) come from the CLI, and
        /// --api overrides api. Exactly one of --project-id and --board-id, each a GUID, and --state-file are required; a value
        /// out of range is a NightlyUsageException.
        /// </summary>
        public static WatchTasksOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--project-id", "--board-id", "--state-file", "--api", "--poll-seconds", "--max-minutes" }, new[] { "--once" });
            var project = flags.Value("--project-id", "");
            var board = flags.Value("--board-id", "");
            if ((project.Length == 0) == (board.Length == 0)) throw new NightlyUsageException("give one of --project-id and --board-id");
            var named = project.Length > 0 ? "--project-id" : "--board-id";
            if (!Guid.TryParse(project.Length > 0 ? project : board, out var id)) throw new NightlyUsageException(named + " expects a GUID, not '" + (project.Length > 0 ? project : board) + "'");
            var explicitApi = flags.Value("--api", "");
            return new WatchTasksOptions
            {
                Scope = (project.Length > 0 ? "projectId=" : "boardId=") + id.ToString("D"),
                StateFile = flags.Required("--state-file"),
                Api = OperatorApi.Base(explicitApi.Length > 0 ? explicitApi : api),
                TaskToken = taskToken ?? "",
                PollSeconds = WatchMasterOptions.Ranged(flags, "--poll-seconds", 45, 1, 300),
                MaxMinutes = WatchMasterOptions.Ranged(flags, "--max-minutes", 28, 1, 29),
                Once = flags.Switch("--once"),
            };
        }
    }

    /// <summary>The task watcher (watch-tasks.ps1).</summary>
    public static class WatchTasks
    {
        /// <summary>Each poll's deadline (the script's -TimeoutSec 20).</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

        private static readonly string[] Reported = { "Succeeded", "Failed", "Blocked", "Canceled" };

        /// <summary>Polls on host.Out until the window ends or --once; 0, or 1 after one host.Error line for an unusable state file.</summary>
        public static int Run(WatchTasksOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var lastStatus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var first = true;
            if (files.File.Exists(options.StateFile))
            {
                // A damaged or mismatched state is refused rather than silently losing transitions.
                JsonObject saved;
                try
                {
                    saved = JsonNode.Parse(files.File.ReadAllText(options.StateFile)) as JsonObject;
                }
                catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException)
                {
                    return Refuse(host, "watch-tasks: state-file-unreadable " + options.StateFile + ": " + e.Message);
                }
                if (saved == null) return Refuse(host, "watch-tasks: Watcher state has no lastStatus map.");
                if (!string.Equals(BoardCli.Text(BoardCli.Get(saved, "scope")), options.Scope, StringComparison.OrdinalIgnoreCase))
                    return Refuse(host, "watch-tasks: Watcher state scope differs; use a separate StateFile.");
                if (saved.TryGetPropertyValue("lastStatus", out var map))
                {
                    if (map != null && !(map is JsonObject)) return Refuse(host, "watch-tasks: Watcher state has no lastStatus map.");
                    if (map is JsonObject entries)
                        foreach (var entry in entries) lastStatus[entry.Key] = BoardCli.Text(entry.Value);
                    first = false;
                }
                else if (saved.ContainsKey("seen"))
                {
                    // Legacy pairs have no ordering, so their latest status cannot be recovered.
                    host.Out.WriteLine("TASK-STATE-UPGRADE baselining legacy seen-pair state");
                }
                else return Refuse(host, "watch-tasks: Watcher state has no lastStatus map.");
            }
            var headers = BoardCli.Headers(options.TaskToken);
            var deadline = host.UtcNow.AddMinutes(options.MaxMinutes);
            do
            {
                var error = Poll(options, host, files, http, headers, lastStatus, first);
                if (error == null) first = false;
                else host.Out.WriteLine("TASK-WATCH-ERROR " + error);
                if (options.Once || host.UtcNow >= deadline) break;
                var remaining = Math.Max(0.0, Math.Floor((deadline - host.UtcNow).TotalMilliseconds));
                if (remaining > 0) host.Sleep(TimeSpan.FromMilliseconds(Math.Min(options.PollSeconds * 1000.0, remaining)));
            }
            while (host.UtcNow < deadline);
            host.Out.WriteLine("watch-tasks window ended; re-arm");
            return 0;
        }

        // One poll: the transitions printed and the state written; null, or the reason it failed (the state then unchanged).
        private static string Poll(WatchTasksOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers,
            Dictionary<string, string> lastStatus, bool first)
        {
            NightlyReportHttpResponse response;
            try
            {
                response = http("GET", options.Api + "/api/agent-tasks?" + options.Scope, headers, null);
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is TaskCanceledException || e is InvalidOperationException || e is IOException)
            {
                return e.Message;
            }
            if (!OperatorApi.Succeeded(response)) return OperatorApi.Failure(response);
            JsonNode reply;
            try
            {
                reply = string.IsNullOrWhiteSpace(response.Body) ? null : JsonNode.Parse(response.Body);
            }
            catch (JsonException e)
            {
                return "Task list answer is not JSON: " + e.Message;
            }
            if (!(reply is JsonObject envelope) || !envelope.ContainsKey("items")) return "Task list envelope has no items.";
            foreach (var task in BoardCli.Items(envelope["items"]))
            {
                var id = BoardCli.Text(BoardCli.Get(task, "id"));
                var status = BoardCli.Text(BoardCli.Get(task, "status"));
                if (id.Length == 0 || status.Length == 0) continue;
                if (lastStatus.TryGetValue(id, out var last) && string.Equals(last, status, StringComparison.OrdinalIgnoreCase)) continue;
                if (!first && Reported.Contains(status, StringComparer.OrdinalIgnoreCase))
                    host.Out.WriteLine("TASK-" + status.ToUpperInvariant() + " " + id + " " + BoardCli.Text(BoardCli.Get(task, "role")) + " " + BoardCli.Text(BoardCli.Get(task, "title")));
                lastStatus[id] = status;
            }
            var map = new JsonObject();
            foreach (var entry in lastStatus) map[entry.Key] = entry.Value;
            var state = new JsonObject { ["scope"] = options.Scope, ["lastStatus"] = map };
            try
            {
                // Default escaping keeps the file ASCII (the script wrote it with -Encoding ASCII).
                files.File.WriteAllText(options.StateFile, state.ToJsonString() + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return e.Message;
            }
            return null;
        }

        private static int Refuse(NightlyHost host, string message)
        {
            host.Error.WriteLine(message);
            return 1;
        }
    }
}
