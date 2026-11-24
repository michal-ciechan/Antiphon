// CARD-0153 S4: commit the uncommitted work in a stalled task's checkout as a WIP checkpoint. It replaced
// scripts/checkpoint-task.ps1, deleted 2026-11-24 (unit 29 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the
// CLI runs it as `antiphon-scripts checkpoint-task` (scripts/antiphon checkpoint-task on sh, scripts\antiphon.cmd
// checkpoint-task on Windows). It never kills the session, and it is safe on a healthy one: a WIP commit on a delegate's branch
// costs nothing and the delegate's next commit sits on top of it. It has nothing to do with checkpoint test runs.
//
//   checkpoint-task --task-id <id> [--push] [--dry-run]
//
// GET /api/agent-tasks/<id> with the task token header when ANTIPHON_TASK_TOKEN is set and the registered caller label
// "checkpoint-task.ps1" (AntiphonCaller; AgentTaskGetPollMetrics meters task GETs by it), on every request. The checkout is the
// task's worktreePath (the summary's first), else its workingDirectory: a Worktree task's edits live in its worktree, and
// workingDirectory is the directory the delegate command ran from (CARD-0217 S5). Another open task (GET
// /api/agent-tasks?status=Working and ?status=Dispatched: the { scope, items, excluded } envelope of CARD-0515; a list that
// cannot be read is skipped) in the same checkout refuses the checkpoint, which would sweep up a colleague's edits; the
// directories are compared in full form, without a trailing separator, ignoring case. Otherwise git status --porcelain (its
// stdout alone) decides: nothing to checkpoint, the dry run's list, or git add -A and one commit
// "wip(checkpoint): [CARD-n ]task <short> - stalled, <n> files, not verified", then its sha and the re-dispatch line. --push
// pushes, and sets the upstream on origin when the branch has none (CARD-0217 S8). git runs on the host's launcher with
// NightlyNativeGit's command line; the add, commit and push lines go to host.Out and host.Error as git wrote them.
//
// Exit 0 committed, or nothing to checkpoint; 2 the task cannot be resolved, it has no usable working directory, or the
// directory is not a git repository; 3 a shared checkout of another active worker; 1 a git step failed. Each refusal is written
// to host.Error. Usage errors are NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The checkpoint-task command's options (checkpoint-task.ps1 -TaskId -Push -DryRun).</summary>
    public sealed class CheckpointTaskOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "checkpoint-task --task-id <id> [--push] [--dry-run]";

        /// <summary>The task whose checkout is checkpointed.</summary>
        public string TaskId { get; init; } = "";

        /// <summary>Push the checkpoint (setting the upstream on origin when the branch has none).</summary>
        public bool Push { get; init; }

        /// <summary>List what would be checkpointed and commit nothing.</summary>
        public bool DryRun { get; init; }

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: no token header.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>The arguments after the command name; api and taskToken come from the CLI. --task-id is required.</summary>
        public static CheckpointTaskOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--task-id" }, new[] { "--push", "--dry-run" });
            return new CheckpointTaskOptions
            {
                TaskId = flags.Required("--task-id"),
                Push = flags.Switch("--push"),
                DryRun = flags.Switch("--dry-run"),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
            };
        }
    }

    /// <summary>The WIP checkpoint of a stalled task (checkpoint-task.ps1).</summary>
    public static class CheckpointTask
    {
        /// <summary>The registered X-Antiphon-Caller label, the deleted script's name (the server's metric key).</summary>
        public const string CallerLabel = "checkpoint-task.ps1";

        /// <summary>The lists of open tasks whose checkout must not be shared.</summary>
        public static readonly IReadOnlyList<string> OpenTaskRoutes = new[] { "/api/agent-tasks?status=Working", "/api/agent-tasks?status=Dispatched" };

        private static readonly Regex CardIdentifier = new Regex(@"(CARD-\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Checkpoints the task's checkout; the exit code (see the file header).</summary>
        public static int Run(CheckpointTaskOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = BoardCli.Headers(options.TaskToken);
            headers[AntiphonCaller.Header] = AntiphonCaller.Label(CallerLabel);

            if (!Get(http, options.Api + "/api/agent-tasks/" + Uri.EscapeDataString(options.TaskId), headers, out var task, out var failure))
                return Refuse(host, 2, "Could not resolve task '" + options.TaskId + "' from " + options.Api + " : " + failure);
            var directory = WorkingDirectory(task);
            var resolvedId = Id(task);
            if (string.IsNullOrWhiteSpace(directory) || !files.Directory.Exists(directory))
                return Refuse(host, 2, "Task " + ShortId(resolvedId) + " has no usable working directory.");
            if (Git(host, directory, "rev-parse", "--is-inside-work-tree").ExitCode != 0)
                return Refuse(host, 2, "Not a git repository: " + directory);

            var others = SharingWorkers(options.Api, http, headers, files, directory, resolvedId);
            if (others.Count > 0)
                return Refuse(host, 3, "Refusing to checkpoint a shared checkout with another active worker: " + string.Join(", ", others.Select(ShortId)));

            var status = Git(host, directory, "status", "--porcelain");
            if (status.ExitCode != 0) return Refuse(host, 1, "git status failed in " + directory);
            var text = status.StandardOutputText ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                host.Out.WriteLine("nothing to checkpoint");
                return 0;
            }
            var changed = text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToList();
            if (options.DryRun)
            {
                host.Out.WriteLine("Would checkpoint:");
                foreach (var line in changed) host.Out.WriteLine("  " + line);
                return 0;
            }

            if (!Step(host, directory, "add", "-A")) return Refuse(host, 1, "git add failed");
            if (!Step(host, directory, "commit", "-m", Subject(resolvedId, Title(task), changed.Count))) return Refuse(host, 1, "git commit failed");
            var sha = (Git(host, directory, "rev-parse", "HEAD").StandardOutputText ?? "").Trim();
            host.Out.WriteLine("checkpoint " + sha);
            host.Out.WriteLine("re-dispatch: scripts/antiphon delegate create --role Code --goal \"resume from checkpoint " + sha + "\" --dir \"" + directory + "\"");
            if (!options.Push) return 0;

            // A Worktree task's branch is usually local until now: a bare push has no upstream and fails (CARD-0217 S8).
            var branch = (Git(host, directory, "rev-parse", "--abbrev-ref", "HEAD").StandardOutputText ?? "").Trim();
            var tracked = Git(host, directory, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}").ExitCode == 0;
            var pushed = tracked ? Step(host, directory, "push") : Step(host, directory, "push", "--set-upstream", "origin", branch);
            return pushed ? 0 : Refuse(host, 1, "git push failed");
        }

        /// <summary>The commit subject: "wip(checkpoint): [CARD-n ]task &lt;short id&gt; - stalled, &lt;n&gt; files, not verified" (the card from the title).</summary>
        public static string Subject(string taskId, string title, int fileCount)
        {
            var card = CardIdentifier.Match(title ?? "");
            var cardBit = card.Success ? card.Groups[1].Value + " " : "";
            return "wip(checkpoint): " + cardBit + "task " + ShortId(taskId) + " - stalled, " + fileCount + " files, not verified";
        }

        /// <summary>The first 8 characters of an id; a blank id or one shorter than 8 as it is.</summary>
        public static string ShortId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length < 8) return id;
            return id.Substring(0, 8);
        }

        /// <summary>A task's checkout: its worktreePath when it has one (summary first), else its workingDirectory; null when neither is set.</summary>
        public static string WorkingDirectory(JsonNode task)
        {
            var worktree = Field(task, "worktreePath");
            if (!string.IsNullOrWhiteSpace(worktree)) return worktree;
            return Field(task, "workingDirectory");
        }

        /// <summary>The rows of an agent task list: the envelope's items (none when they are null), else the answer itself (@($response)).</summary>
        public static IReadOnlyList<JsonNode> ListRows(JsonNode answer)
        {
            if (answer is JsonObject envelope && envelope.TryGetPropertyValue("items", out var items)) return BoardCli.Items(items);
            return BoardCli.Items(answer);
        }

        private static string Id(JsonNode task) => Field(task, "id");

        private static string Title(JsonNode task) => Field(task, "title") ?? "task";

        // The summary's value when it is set, else the task's own; null when neither is (PowerShell's truth: an empty value is unset).
        private static string Field(JsonNode task, string name)
        {
            var nested = BoardCli.Get(task, "summary", name);
            if (BoardCli.Truthy(nested)) return BoardCli.Text(nested);
            var own = BoardCli.Get(task, name);
            return BoardCli.Truthy(own) ? BoardCli.Text(own) : null;
        }

        private static List<string> SharingWorkers(string api, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers, IFileSystem files,
            string directory, string resolvedId)
        {
            var open = new List<JsonNode>();
            foreach (var route in OpenTaskRoutes)
            {
                // A list that cannot be read is skipped: the refusal is a guard on what the server answered.
                if (!Get(http, api + route, headers, out var answer, out _)) continue;
                open.AddRange(ListRows(answer).Where(row => row != null && row.GetValueKind() != JsonValueKind.Null));
            }
            var mine = Normalize(files, directory);
            var others = new List<string>();
            foreach (var row in open)
            {
                var otherId = Id(row);
                var otherDirectory = WorkingDirectory(row);
                if (string.IsNullOrWhiteSpace(otherId)) continue;
                if (string.Equals(otherId, resolvedId, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(otherDirectory)) continue;
                string theirs;
                try
                {
                    theirs = Normalize(files, otherDirectory);
                }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is IOException || e is System.Security.SecurityException)
                {
                    continue;
                }
                if (string.Equals(mine, theirs, StringComparison.Ordinal)) others.Add(otherId);
            }
            return others;
        }

        // Normalize-Dir: the full path without a trailing separator, lower-cased.
        private static string Normalize(IFileSystem files, string path) => files.Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant();

        // One GET: true with the parsed answer (null for an empty body) on a 2xx status; false with the reason otherwise.
        private static bool Get(NightlyReportHttp http, string address, IReadOnlyDictionary<string, string> headers, out JsonNode answer, out string failure)
        {
            answer = null;
            failure = null;
            NightlyReportHttpResponse response;
            try
            {
                response = http("GET", address, headers, null);
            }
            catch (Exception e)
            {
                // A transport failure: no status. The exception text names the address, never a header.
                failure = e.Message;
                return false;
            }
            if (!OperatorApi.Succeeded(response))
            {
                failure = BoardCli.Detail(response);
                return false;
            }
            try
            {
                answer = string.IsNullOrWhiteSpace(response.Body) ? null : JsonNode.Parse(response.Body);
                return true;
            }
            catch (JsonException e)
            {
                failure = "the answer is not JSON: " + e.Message;
                return false;
            }
        }

        // git in the checkout (-C), on the host's launcher; stdout and stderr read apart.
        private static NightlyLaunchResult Git(NightlyHost host, string directory, params string[] arguments) =>
            host.Launch(new NightlyLaunch("git", NightlyNativeGit.CommandLine(directory, arguments), "", CaptureOutput: true));

        // A git step whose own lines are shown: its stdout on host.Out, its stderr on host.Error; true when it exited 0.
        private static bool Step(NightlyHost host, string directory, params string[] arguments)
        {
            var run = Git(host, directory, arguments);
            host.Out.Write(run.StandardOutputText ?? "");
            host.Error.Write(run.StandardErrorText ?? "");
            return run.ExitCode == 0;
        }

        private static int Refuse(NightlyHost host, int code, string message)
        {
            host.Error.WriteLine(message);
            return code;
        }
    }
}
