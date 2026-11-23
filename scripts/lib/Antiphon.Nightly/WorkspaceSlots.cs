// CARD-0824 S1, CARD-1960: runner workspace slots. It replaced scripts/workspace-slots.ps1 (deleted 2026-11-23, unit 21 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as `antiphon-scripts workspace-slots <verb>`.
//
//   workspace-slots cold-reset --runner server2 [--slot ws-03,ws-01] --reason "why"
//   workspace-slots cold-reset --all-runners --reason "rc qualification rc/<date> <sha12>"
//   workspace-slots release --runner server2 --dir-id <12hex> --disposition Pushed --reason "why"
//
// cold-reset calls POST /api/session-runners/{runnerId}/workspace-slots/cold-reset with the operator token from the owner-only
// file the server creates at startup (the CLI passes ANTIPHON_OPERATOR_TOKEN_FILE, else the server's default path); the token
// is never printed. Without --slot every slot of the runner is targeted. A Free slot is cold-reset at once; a leased slot only
// records the request as pending and runs it inside its release, so the call never waits for leased slots. --all-runners
// targets every runner whose features advertise workspaceSlotV1. The request id and each slot's outcome are printed. Exit 0
// when every runner answered, 1 when any call failed.
//
// release (CARD-1960) calls POST /api/session-runners/{runnerId}/workspace-slots/quarantine/{dirId}/release with the same
// token. It releases one open quarantined directory: --dir-id is the dirId of the runner ledger's quarantinedDirectories entry.
// --disposition Pushed rechecks that the work is published and clean first; AcceptLoss retains the diff and untracked files
// under /work/retained/quarantine/<dirId>/; Removed deletes with no check, so use Pushed for published residue and keep
// Removed for a fixed blocker. It prints runner=<id> dir=<dirId> outcome=<outcome> [detail]. Exit 0 only when the runner
// removed the directory, 1 when it refused, failed the removal or the call failed, 2 on a usage error (NightlyUsageException)
// or an unreadable token file. The requester defaults to "workspace-slots.ps1" (the label the script sent, kept) or
// "task <ANTIPHON_TASK_ID>". Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The workspace-slots command's options (workspace-slots.ps1 &lt;verb&gt; -Runner -AllRunners -Slot -DirId -Disposition -Reason -Requester).</summary>
    public sealed class WorkspaceSlotsOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "workspace-slots <cold-reset|release> (--runner <id> | --all-runners) [--slot <ws-01,ws-02>] [--dir-id <12hex>]"
            + " [--disposition Pushed|AcceptLoss|Removed] --reason <why> [--requester <who>]";

        /// <summary>The requester when none is given and no task id is set: the label the deleted script sent.</summary>
        public const string DefaultRequester = "workspace-slots.ps1";

        private static readonly string[] Dispositions = { "Pushed", "AcceptLoss", "Removed" };

        private static readonly Regex DirIdPattern = new Regex("^[0-9a-f]{12}$", RegexOptions.CultureInvariant);

        /// <summary>cold-reset or release.</summary>
        public string Verb { get; init; } = "cold-reset";

        /// <summary>The runner; empty with --all-runners.</summary>
        public string Runner { get; init; } = "";

        /// <summary>cold-reset: every runner that advertises workspaceSlotV1.</summary>
        public bool AllRunners { get; init; }

        /// <summary>cold-reset: the slots; empty: all of them.</summary>
        public IReadOnlyList<string> Slots { get; init; } = Array.Empty<string>();

        /// <summary>release: the quarantined directory's dirId.</summary>
        public string DirId { get; init; } = "";

        /// <summary>release: Pushed, AcceptLoss or Removed, in its canonical spelling.</summary>
        public string Disposition { get; init; } = "";

        /// <summary>Why.</summary>
        public string Reason { get; init; } = "";

        /// <summary>Who asked.</summary>
        public string Requester { get; init; } = DefaultRequester;

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: no task token header.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>The operator token file.</summary>
        public string OperatorTokenFile { get; init; } = "";

        /// <summary>
        /// The verb and the flags after the command name; the API, the task token, the token file and the task id (the default
        /// requester) come from the CLI. Every target and usage error is a NightlyUsageException naming workspace_slots_target.
        /// </summary>
        public static WorkspaceSlotsOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken, string operatorTokenFile, string taskId)
        {
            var verb = arguments != null && arguments.Count > 0 ? arguments[0] ?? "" : "";
            if (verb != "cold-reset" && verb != "release") throw new NightlyUsageException("the first argument is the verb: cold-reset or release, not '" + verb + "'");
            var flags = NightlyArgs.Parse(arguments.Skip(1).ToArray(), new[] { "--runner", "--slot", "--dir-id", "--disposition", "--reason", "--requester" }, new[] { "--all-runners" });
            var runner = flags.Value("--runner", "");
            var all = flags.Switch("--all-runners");
            var slots = flags.List("--slot");
            var dirId = flags.Value("--dir-id", "");
            var disposition = flags.Value("--disposition", "");
            var reason = flags.Value("--reason", "");
            if (string.IsNullOrEmpty(reason)) throw new NightlyUsageException("missing --reason");
            if (disposition.Length > 0)
            {
                // The script's ValidateSet ignored case; the route compares ordinally, so the canonical spelling is sent.
                var canonical = Dispositions.FirstOrDefault(name => string.Equals(name, disposition, StringComparison.OrdinalIgnoreCase));
                if (canonical == null) throw new NightlyUsageException("--disposition must be Pushed, AcceptLoss or Removed, not '" + disposition + "'");
                disposition = canonical;
            }
            if (verb == "release")
            {
                if (all || slots.Count > 0 || string.IsNullOrWhiteSpace(runner))
                    throw new NightlyUsageException("workspace_slots_target: release names one runner's quarantined directory; use --runner and --dir-id, not --all-runners or --slot.");
                if (string.IsNullOrWhiteSpace(dirId) || !DirIdPattern.IsMatch(dirId))
                    throw new NightlyUsageException("workspace_slots_target: release needs --dir-id, the 12 lower-case hex digits of the ledger entry.");
                if (string.IsNullOrWhiteSpace(disposition))
                    throw new NightlyUsageException("workspace_slots_target: release needs --disposition Pushed, AcceptLoss or Removed.");
            }
            else if (!string.IsNullOrWhiteSpace(dirId) || !string.IsNullOrWhiteSpace(disposition))
                throw new NightlyUsageException("workspace_slots_target: --dir-id and --disposition belong to release.");
            if (all && !string.IsNullOrWhiteSpace(runner)) throw new NightlyUsageException("workspace_slots_target: give --runner or --all-runners, not both.");
            if (!all && string.IsNullOrWhiteSpace(runner)) throw new NightlyUsageException("workspace_slots_target: give --runner <name> or --all-runners.");
            if (all && slots.Count > 0) throw new NightlyUsageException("workspace_slots_target: --slot names one runner's slots; use it with --runner.");
            var requester = flags.Value("--requester", "");
            if (string.IsNullOrWhiteSpace(requester)) requester = string.IsNullOrWhiteSpace(taskId) ? DefaultRequester : "task " + taskId;
            return new WorkspaceSlotsOptions
            {
                Verb = verb,
                Runner = runner,
                AllRunners = all,
                Slots = slots,
                DirId = dirId,
                Disposition = disposition,
                Reason = reason,
                Requester = requester,
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
                OperatorTokenFile = operatorTokenFile ?? "",
            };
        }
    }

    /// <summary>The workspace slot cold reset and quarantine release (workspace-slots.ps1).</summary>
    public static class WorkspaceSlots
    {
        /// <summary>Each request's deadline (the runner's quarantine release may take its 5-minute phone-home bound).</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(6);

        /// <summary>Runs the verb: its lines on host.Out; the exit code as the header describes.</summary>
        public static int Run(WorkspaceSlotsOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(options.TaskToken)) headers[OperatorApi.TaskTokenHeader] = options.TaskToken;
            var token = OperatorApi.ReadToken(files, options.OperatorTokenFile, out var refusal);
            if (token == null)
            {
                host.Error.WriteLine(refusal);
                return 2;
            }
            headers[OperatorApi.OperatorTokenHeader] = token;

            if (options.Verb == "release")
            {
                var body = new JsonObject { ["disposition"] = options.Disposition, ["reason"] = options.Reason, ["requester"] = options.Requester };
                var uri = options.Api + "/api/session-runners/" + Uri.EscapeDataString(options.Runner) + "/workspace-slots/quarantine/" + Uri.EscapeDataString(options.DirId) + "/release";
                JsonNode answer;
                try
                {
                    answer = Post(http, uri, headers, body);
                }
                catch (Exception e)
                {
                    host.Out.WriteLine("runner=" + options.Runner + " dir=" + options.DirId + " failed: " + e.Message);
                    return 1;
                }
                var outcome = Text(Field(answer, "outcome"));
                var line = "runner=" + options.Runner + " dir=" + options.DirId + " outcome=" + outcome;
                var detail = Text(Field(answer, "detail"));
                if (!string.IsNullOrWhiteSpace(detail)) line += " " + detail;
                host.Out.WriteLine(line);
                return string.Equals(outcome, "removed", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            }

            var runners = new List<string>();
            if (options.AllRunners)
            {
                NightlyReportHttpResponse listed;
                try
                {
                    listed = http("GET", options.Api + "/api/session-runners", headers, null);
                }
                catch (Exception e)
                {
                    host.Error.WriteLine("workspace-slots: GET /api/session-runners failed: " + e.Message);
                    return 1;
                }
                if (!OperatorApi.Succeeded(listed))
                {
                    host.Error.WriteLine("workspace-slots: GET /api/session-runners failed: " + OperatorApi.Failure(listed));
                    return 1;
                }
                var catalogue = string.IsNullOrWhiteSpace(listed.Body) ? null : JsonNode.Parse(listed.Body);
                var rows = catalogue is JsonArray array ? array.ToArray() : catalogue == null ? Array.Empty<JsonNode>() : new[] { catalogue };
                foreach (var row in rows)
                {
                    var features = Field(row, "features");
                    var names = features is JsonArray list ? list.Select(Text) : features == null ? Enumerable.Empty<string>() : new[] { Text(features) };
                    // PowerShell's -contains: case-insensitive.
                    if (names.Any(name => string.Equals(name, "workspaceSlotV1", StringComparison.OrdinalIgnoreCase))) runners.Add(Text(Field(row, "runnerId")));
                }
                if (runners.Count == 0)
                {
                    host.Out.WriteLine("no runner advertises workspaceSlotV1; nothing to cold-reset");
                    return 0;
                }
            }
            else runners.Add(options.Runner);

            var failed = false;
            foreach (var runnerId in runners)
            {
                var body = new JsonObject { ["all"] = options.Slots.Count == 0, ["reason"] = options.Reason, ["requester"] = options.Requester };
                if (options.Slots.Count > 0) body["slots"] = new JsonArray(options.Slots.Select(slot => (JsonNode)JsonValue.Create(slot)).ToArray());
                var uri = options.Api + "/api/session-runners/" + Uri.EscapeDataString(runnerId) + "/workspace-slots/cold-reset";
                try
                {
                    var answer = Post(http, uri, headers, body);
                    host.Out.WriteLine("runner=" + runnerId + " request=" + Text(Field(answer, "requestId")));
                    var slots = Field(answer, "slots");
                    foreach (var outcome in slots is JsonArray items ? items.ToArray() : slots == null ? Array.Empty<JsonNode>() : new[] { slots })
                        host.Out.WriteLine("  " + Text(Field(outcome, "slotId")) + " " + Text(Field(outcome, "outcome")));
                }
                catch (Exception e)
                {
                    failed = true;
                    host.Out.WriteLine("runner=" + runnerId + " failed: " + e.Message);
                }
            }
            return failed ? 1 : 0;
        }

        // Invoke-RestMethod: the parsed answer; a non-2xx answer throws with its status and body.
        private static JsonNode Post(NightlyReportHttp http, string uri, IReadOnlyDictionary<string, string> headers, JsonObject body)
        {
            var answer = http("POST", uri, headers, OperatorApi.Json(body));
            if (!OperatorApi.Succeeded(answer)) throw new InvalidOperationException(OperatorApi.Failure(answer));
            return string.IsNullOrWhiteSpace(answer.Body) ? null : JsonNode.Parse(answer.Body);
        }

        private static JsonNode Field(JsonNode node, string name) => node is JsonObject fields ? fields[name] : null;

        private static string Text(JsonNode node)
        {
            if (node == null) return "";
            if (node is JsonValue value && value.GetValueKind() == JsonValueKind.String) return value.GetValue<string>();
            return node.ToJsonString();
        }
    }
}
