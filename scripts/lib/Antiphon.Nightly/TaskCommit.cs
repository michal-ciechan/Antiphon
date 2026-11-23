// CARD-0527: commit named paths through the gated server primitive. It replaced scripts/task-commit.ps1 (unit 25 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as `antiphon-scripts task-commit`
// (scripts/antiphon task-commit on sh, scripts\antiphon.cmd task-commit on Windows). The server's commit-child brief and the
// commit-on-settle goal name the command.
//
//   task-commit --paths <a.md,b.md> --message-file <file>
//
// POST /api/agent-tasks/{ANTIPHON_TASK_ID}/commit with {"paths":[...],"message":"..."} and X-Antiphon-Task-Token. The CLI
// passes ANTIPHON_API (default http://localhost:17202), ANTIPHON_TASK_TOKEN and ANTIPHON_TASK_ID in (the delegate
// environment); the token is a header only, never printed. --paths is comma separated, each entry trimmed, empty entries
// dropped. The message file is read as UTF-8 (a BOM is dropped). The server refuses ignored paths and ignore-rule edits.
// Exit 0 with "committed <sha> <file>, <file>" on stdout; 1 with one stderr line: no token or task id, no message file,
// "Commit succeeded; inspection pending. Recover with GET /api/agent-tasks/<task>/commit/<operation> using the task token.
// <answer>" for a commit_inspection_pending answer (the commit stands), or "commit refused: <answer>" for any other
// refusal, quoted verbatim. Usage errors are NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types.
// ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The task-commit command's options (task-commit.ps1 -Paths -MessageFile).</summary>
    public sealed class TaskCommitOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "task-commit --paths <path[,path...]> --message-file <file>";

        /// <summary>The paths to commit.</summary>
        public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();

        /// <summary>The commit message's file.</summary>
        public string MessageFile { get; init; } = "";

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>The task (ANTIPHON_TASK_ID); empty: none.</summary>
        public string TaskId { get; init; } = "";

        /// <summary>The arguments after the command name; api, taskToken and taskId come from the CLI. --paths and --message-file are required.</summary>
        public static TaskCommitOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken, string taskId)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--paths", "--message-file" }, Array.Empty<string>());
            flags.Required("--paths");
            return new TaskCommitOptions
            {
                Paths = flags.List("--paths"),
                MessageFile = flags.Required("--message-file"),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
                TaskId = taskId ?? "",
            };
        }
    }

    /// <summary>The task commit (task-commit.ps1).</summary>
    public static class TaskCommit
    {
        /// <summary>The commit request's deadline: the server inspects the commit before it answers.</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(10);

        /// <summary>Commits the paths; 0 after the committed line on host.Out, or 1 after one host.Error line.</summary>
        public static int Run(TaskCommitOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            if (string.IsNullOrWhiteSpace(options.TaskToken)) return Refuse(host, "ANTIPHON_TASK_TOKEN is required.");
            if (string.IsNullOrWhiteSpace(options.TaskId)) return Refuse(host, "ANTIPHON_TASK_ID is required.");
            if (!files.File.Exists(options.MessageFile)) return Refuse(host, "--message-file '" + options.MessageFile + "' does not exist.");
            var message = files.File.ReadAllText(options.MessageFile, Encoding.UTF8);
            var paths = new JsonArray();
            foreach (var path in options.Paths) paths.Add(path);
            var body = OperatorApi.Json(new JsonObject { ["paths"] = paths, ["message"] = message });
            var headers = new Dictionary<string, string>(StringComparer.Ordinal) { [OperatorApi.TaskTokenHeader] = options.TaskToken };
            var route = "/api/agent-tasks/" + options.TaskId + "/commit";
            NightlyReportHttpResponse answer;
            try
            {
                answer = http("POST", options.Api + route, headers, body);
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is System.Threading.Tasks.TaskCanceledException || e is InvalidOperationException || e is IOException)
            {
                // A transport failure: no status. The exception text names the address, never a header.
                return Refuse(host, "commit refused: " + e.Message);
            }
            var detail = (answer.Body ?? "").Trim();
            if (OperatorApi.Succeeded(answer))
            {
                JsonNode result;
                try
                {
                    result = detail.Length == 0 ? null : JsonNode.Parse(detail);
                }
                catch (JsonException e)
                {
                    return Refuse(host, "Antiphon POST " + route + " answered with a body that is not JSON: " + e.Message);
                }
                host.Out.WriteLine("committed " + BoardCli.Text(BoardCli.Get(result, "sha")) + " " + string.Join(", ", BoardCli.Items(BoardCli.Get(result, "files")).Select(BoardCli.Text)));
                return 0;
            }
            if (detail.Length == 0) detail = "HTTP " + answer.Status;
            JsonNode problem = null;
            try
            {
                problem = JsonNode.Parse(detail);
            }
            catch (JsonException)
            {
                problem = null;
            }
            if (BoardCli.Text(BoardCli.Get(problem, "code")) == "commit_inspection_pending")
                return Refuse(host, "Commit succeeded; inspection pending. Recover with GET /api/agent-tasks/" + options.TaskId + "/commit/"
                    + BoardCli.Text(BoardCli.Get(problem, "operationId")) + " using the task token. " + detail);
            return Refuse(host, "commit refused: " + detail);
        }

        private static int Refuse(NightlyHost host, string message)
        {
            host.Error.WriteLine(message);
            return 1;
        }
    }
}
