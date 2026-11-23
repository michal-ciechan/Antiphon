// CARD-0459: preview, status, release and revoke worktree residue. It replaced scripts/worktree-residue.ps1 (deleted 2026-11-23,
// unit 21 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
// `antiphon-scripts worktree-residue --action <preview|status|release|revoke|batch-release> ...`. It never trusts a client-supplied
// path, remote or terminal status: the server decides. A release names a full task GUID (a short id is refused before any
// request, in a batch per item). The task token is --token (sent as X-Antiphon-Task-Token, never printed). The server's answer
// body is printed as it came. Exit 0 done; 1 a request failed (one stderr line: its status and body) or a batch item failed (the
// failures on stdout, "; " joined); usage errors are NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types.
// ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The worktree-residue command's options (worktree-residue.ps1 -Action -BaseUrl -ProjectId ... -Token).</summary>
    public sealed class WorktreeResidueOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "worktree-residue --action <preview|status|release|revoke|batch-release> [--base-url <url>] [--project-id <id>] [--board-id <id>]"
            + " [--task-id <guid>] [--retirement-id <id>] [--expected-task-revision <rev>] [--source-sha <sha>] [--report-digest <digest>] [--reason <text>]"
            + " [--manifest-path <file>] [--run-id <id>] [--page <n>] [--token <task token>]";

        /// <summary>The API when --base-url is not given.</summary>
        public const string DefaultBaseUrl = "http://127.0.0.1:17202";

        private static readonly string[] Actions = { "preview", "status", "release", "revoke", "batch-release" };

        private static readonly string[] Valued = { "--action", "--base-url", "--project-id", "--board-id", "--task-id", "--retirement-id", "--expected-task-revision",
            "--source-sha", "--report-digest", "--reason", "--manifest-path", "--run-id", "--page", "--token" };

        /// <summary>preview, status, release, revoke or batch-release.</summary>
        public string Action { get; init; } = "preview";

        /// <summary>The API base, without a trailing slash.</summary>
        public string BaseUrl { get; init; } = DefaultBaseUrl;

        /// <summary>preview: the project scope; empty: none.</summary>
        public string ProjectId { get; init; } = "";

        /// <summary>preview: the board scope; empty: none.</summary>
        public string BoardId { get; init; } = "";

        /// <summary>release and revoke: the full task GUID.</summary>
        public string TaskId { get; init; } = "";

        /// <summary>revoke: the retirement.</summary>
        public string RetirementId { get; init; } = "";

        /// <summary>release: the task revision the caller saw.</summary>
        public string ExpectedTaskRevision { get; init; } = "";

        /// <summary>release: the source SHA.</summary>
        public string SourceSha { get; init; } = "";

        /// <summary>release: the report digest; empty: none.</summary>
        public string ReportDigest { get; init; } = "";

        /// <summary>release: why.</summary>
        public string Reason { get; init; } = "";

        /// <summary>batch-release: the JSON manifest (an array of release items).</summary>
        public string ManifestPath { get; init; } = "";

        /// <summary>status: the run.</summary>
        public string RunId { get; init; } = "";

        /// <summary>status: the page.</summary>
        public int Page { get; init; }

        /// <summary>The task token; empty: no token header.</summary>
        public string Token { get; init; } = "";

        /// <summary>The arguments after the command name. A missing or short id or field is a NightlyUsageException.</summary>
        public static WorktreeResidueOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Array.Empty<string>());
            var action = flags.Required("--action");
            if (!Actions.Contains(action, StringComparer.Ordinal)) throw new NightlyUsageException("--action must be preview, status, release, revoke or batch-release, not '" + action + "'");
            var options = new WorktreeResidueOptions
            {
                Action = action,
                BaseUrl = flags.Value("--base-url", DefaultBaseUrl).TrimEnd('/'),
                ProjectId = flags.Value("--project-id", ""),
                BoardId = flags.Value("--board-id", ""),
                TaskId = flags.Value("--task-id", ""),
                RetirementId = flags.Value("--retirement-id", ""),
                ExpectedTaskRevision = flags.Value("--expected-task-revision", ""),
                SourceSha = flags.Value("--source-sha", ""),
                ReportDigest = flags.Value("--report-digest", ""),
                Reason = flags.Value("--reason", ""),
                ManifestPath = flags.Value("--manifest-path", ""),
                RunId = flags.Value("--run-id", ""),
                Page = flags.Int("--page", 0),
                Token = flags.Value("--token", ""),
            };
            switch (action)
            {
                case "status":
                    if (options.RunId.Length == 0) throw new NightlyUsageException("status requires --run-id");
                    break;
                case "release":
                    if (options.TaskId.Length == 0) throw new NightlyUsageException("release requires a full --task-id");
                    if (options.TaskId.Length != 36) throw new NightlyUsageException("release requires a full task GUID, not a short id");
                    if (options.ExpectedTaskRevision.Length == 0) throw new NightlyUsageException("release requires --expected-task-revision");
                    if (options.SourceSha.Length == 0) throw new NightlyUsageException("release requires --source-sha");
                    if (options.Reason.Length == 0) throw new NightlyUsageException("release requires --reason");
                    break;
                case "revoke":
                    if (options.TaskId.Length == 0 || options.RetirementId.Length == 0) throw new NightlyUsageException("revoke requires --task-id and --retirement-id");
                    break;
                case "batch-release":
                    if (options.ManifestPath.Length == 0) throw new NightlyUsageException("batch-release requires --manifest-path");
                    break;
            }
            return options;
        }
    }

    /// <summary>The worktree residue front door (worktree-residue.ps1).</summary>
    public static class WorktreeResidue
    {
        /// <summary>Each request's deadline.</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(10);

        /// <summary>Runs the action: the answer on host.Out; the exit code as the header describes.</summary>
        public static int Run(WorktreeResidueOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (options.Token.Length > 0) headers[OperatorApi.TaskTokenHeader] = options.Token;
            var root = options.BaseUrl;
            switch (options.Action)
            {
                case "preview":
                    var scope = new JsonObject();
                    if (options.ProjectId.Length > 0) scope["projectId"] = options.ProjectId;
                    if (options.BoardId.Length > 0) scope["boardId"] = options.BoardId;
                    return Single(host, http, "POST", root + "/api/agent-tasks/worktree-residue/preview", headers, scope);
                case "status":
                    return Single(host, http, "GET", root + "/api/agent-tasks/worktree-residue/runs/" + Uri.EscapeDataString(options.RunId) + "?page="
                        + options.Page.ToString(CultureInfo.InvariantCulture), headers, null);
                case "release":
                    return Single(host, http, "POST", RetirementUri(root, options.TaskId), headers, ReleaseBody(JsonValue.Create(options.ExpectedTaskRevision),
                        JsonValue.Create(options.SourceSha), JsonValue.Create(options.ReportDigest), options.Reason));
                case "revoke":
                    return Single(host, http, "DELETE", RetirementUri(root, options.TaskId) + "/" + Uri.EscapeDataString(options.RetirementId), headers, null);
                default:
                    return Batch(options, host, files, http, headers);
            }
        }

        private static string RetirementUri(string root, string taskId) => root + "/api/agent-tasks/" + Uri.EscapeDataString(taskId) + "/worktree-retirement";

        // The script's release payload; noFurtherWorkspaceUse is always true.
        private static JsonObject ReleaseBody(JsonNode expectedTaskRevision, JsonNode sourceSha, JsonNode reportDigest, string reason) => new JsonObject
        {
            ["expectedTaskRevision"] = expectedTaskRevision,
            ["sourceSha"] = sourceSha,
            ["reportDigest"] = reportDigest,
            ["noFurtherWorkspaceUse"] = true,
            ["reason"] = reason,
        };

        private static int Single(NightlyHost host, NightlyReportHttp http, string method, string uri, IReadOnlyDictionary<string, string> headers, JsonObject body)
        {
            NightlyReportHttpResponse answer;
            try
            {
                answer = http(method, uri, headers, body == null ? null : OperatorApi.Json(body));
            }
            catch (Exception e)
            {
                host.Error.WriteLine("worktree-residue: " + method + " failed: " + e.Message);
                return 1;
            }
            if (!OperatorApi.Succeeded(answer))
            {
                host.Error.WriteLine("worktree-residue: " + OperatorApi.Failure(answer));
                return 1;
            }
            if (!string.IsNullOrWhiteSpace(answer.Body)) host.Out.WriteLine(answer.Body);
            return 0;
        }

        private static int Batch(WorktreeResidueOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers)
        {
            JsonNode manifest;
            try
            {
                manifest = JsonNode.Parse(files.File.ReadAllText(options.ManifestPath));
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                host.Error.WriteLine("worktree-residue: cannot read the manifest " + options.ManifestPath + ": " + e.Message);
                return 1;
            }
            var items = manifest is JsonArray array ? array.ToArray() : manifest == null ? Array.Empty<JsonNode>() : new[] { manifest };
            var failures = new List<string>();
            foreach (var item in items)
            {
                var fields = item as JsonObject;
                var taskId = Text(fields?["taskId"]);
                if (fields == null || taskId.Length != 36)
                {
                    failures.Add("invalid-id:" + taskId);
                    continue;
                }
                var body = ReleaseBody(fields["expectedTaskRevision"]?.DeepClone(), fields["sourceSha"]?.DeepClone(), fields["reportDigest"]?.DeepClone(), Text(fields["reason"]));
                try
                {
                    var answer = http("POST", RetirementUri(options.BaseUrl, taskId), headers, OperatorApi.Json(body));
                    if (!OperatorApi.Succeeded(answer)) failures.Add(taskId + ":HTTP " + answer.Status);
                }
                catch (Exception e)
                {
                    failures.Add(taskId + ":" + e.Message);
                }
            }
            if (failures.Count == 0) return 0;
            host.Out.WriteLine(string.Join("; ", failures));
            return 1;
        }

        private static string Text(JsonNode node)
        {
            if (node == null) return "";
            if (node is JsonValue value && value.GetValueKind() == JsonValueKind.String) return value.GetValue<string>();
            return node.ToJsonString();
        }
    }
}
