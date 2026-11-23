// CARD-0727: drain, clear, retire or read the status of a phone-home runner. It replaced scripts/runner-drain.ps1 (deleted
// 2026-11-23, unit 21 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
// `antiphon-scripts runner-drain <status|drain|clear|retire> [--runner-id server2] ...`.
//
//   runner-drain status [--runner-id server2]
//   runner-drain drain  --reason "why" [--runner-id server2] [--redirect-to server2-temp] [--retire-when-idle]
//   runner-drain clear  --reason "why" [--runner-id server2]
//   runner-drain retire --reason "why" --confirm <runnerId> [--runner-id server2]
//
// drain, clear and retire send the operator token from the owner-only file the server creates at startup (the CLI passes
// ANTIPHON_OPERATOR_TOKEN_FILE, else the server's default path); the token is never printed. The task token
// (ANTIPHON_TASK_TOKEN) is sent when set. A status that is not HTTP 200, including 404, is not eligible. The answer's body is
// printed (or "HTTP 200" when it has none). Exit 0 done; 1 not eligible, a failed request or the token file (one stderr line);
// usage errors are NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The runner-drain command's options (runner-drain.ps1 &lt;verb&gt; -RunnerId -Reason -RedirectTo -RetireWhenIdle -Confirm).</summary>
    public sealed class RunnerDrainOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "runner-drain <status|drain|clear|retire> [--runner-id server2] [--reason <why>] [--redirect-to <runner>] [--retire-when-idle] [--confirm <runnerId>]";

        private static readonly string[] Verbs = { "status", "drain", "clear", "retire" };

        /// <summary>status, drain, clear or retire.</summary>
        public string Verb { get; init; } = "status";

        /// <summary>The runner.</summary>
        public string RunnerId { get; init; } = "server2";

        /// <summary>Why (drain, clear and retire require it).</summary>
        public string Reason { get; init; } = "";

        /// <summary>drain: where new work goes meanwhile; empty: nowhere named.</summary>
        public string RedirectTo { get; init; } = "";

        /// <summary>drain: retire the runner once it is idle.</summary>
        public bool RetireWhenIdle { get; init; }

        /// <summary>retire: the runner id again.</summary>
        public string Confirm { get; init; } = "";

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: no task token header.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>The operator token file.</summary>
        public string OperatorTokenFile { get; init; } = "";

        /// <summary>
        /// The verb and the flags after the command name; the API, the task token and the token file come from the CLI. A
        /// verb outside the four, or a missing reason or confirmation, is a NightlyUsageException.
        /// </summary>
        public static RunnerDrainOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken, string operatorTokenFile)
        {
            var verb = arguments != null && arguments.Count > 0 ? arguments[0] ?? "" : "";
            if (!Verbs.Contains(verb, StringComparer.Ordinal)) throw new NightlyUsageException("the first argument is the verb: status, drain, clear or retire, not '" + verb + "'");
            var flags = NightlyArgs.Parse(arguments.Skip(1).ToArray(), new[] { "--runner-id", "--reason", "--redirect-to", "--confirm" }, new[] { "--retire-when-idle" });
            var options = new RunnerDrainOptions
            {
                Verb = verb,
                RunnerId = flags.Value("--runner-id", "server2"),
                Reason = flags.Value("--reason", ""),
                RedirectTo = flags.Value("--redirect-to", ""),
                RetireWhenIdle = flags.Switch("--retire-when-idle"),
                Confirm = flags.Value("--confirm", ""),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
                OperatorTokenFile = operatorTokenFile ?? "",
            };
            if (verb != "status" && string.IsNullOrWhiteSpace(options.Reason)) throw new NightlyUsageException(verb + " requires --reason");
            if (verb == "retire" && (string.IsNullOrWhiteSpace(options.Confirm) || !string.Equals(options.Confirm, options.RunnerId, StringComparison.OrdinalIgnoreCase)))
                throw new NightlyUsageException("retire requires --confirm equal to --runner-id");
            return options;
        }
    }

    /// <summary>The runner drain (runner-drain.ps1).</summary>
    public static class RunnerDrain
    {
        /// <summary>Each request's deadline.</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(5);

        /// <summary>Runs the verb: the answer on host.Out; exit 0, or 1 after one line on host.Error.</summary>
        public static int Run(RunnerDrainOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(options.TaskToken)) headers[OperatorApi.TaskTokenHeader] = options.TaskToken;
            var runner = Uri.EscapeDataString(options.RunnerId);
            string path;
            JsonObject body = null;
            switch (options.Verb)
            {
                case "status":
                    path = "/api/session-runners/" + runner + "/status";
                    break;
                case "drain":
                    body = new JsonObject { ["reason"] = options.Reason, ["retireWhenIdle"] = options.RetireWhenIdle };
                    if (!string.IsNullOrWhiteSpace(options.RedirectTo)) body["redirectTo"] = options.RedirectTo;
                    path = "/api/session-runners/" + runner + "/drain";
                    break;
                case "clear":
                    body = new JsonObject { ["reason"] = options.Reason };
                    path = "/api/session-runners/" + runner + "/drain/clear";
                    break;
                case "retire":
                    body = new JsonObject { ["reason"] = options.Reason, ["confirmRunnerId"] = options.Confirm };
                    path = "/api/session-runners/" + runner + "/retire";
                    break;
                default:
                    throw new NightlyUsageException("unknown verb '" + options.Verb + "'");
            }
            if (body != null)
            {
                var token = OperatorApi.ReadToken(files, options.OperatorTokenFile, out var refusal);
                if (token == null)
                {
                    host.Error.WriteLine(refusal);
                    return 1;
                }
                headers[OperatorApi.OperatorTokenHeader] = token;
            }

            NightlyReportHttpResponse answer;
            try
            {
                answer = http(body == null ? "GET" : "POST", options.Api + path, headers, body == null ? null : OperatorApi.Json(body));
            }
            catch (Exception e)
            {
                // A transport failure: no status. The exception text names the address, never a header.
                host.Error.WriteLine("Runner '" + options.RunnerId + "' request failed: " + e.Message);
                return 1;
            }
            if (answer.Status == 404 || (answer.Status >= 200 && answer.Status <= 299 && answer.Status != 200))
            {
                host.Error.WriteLine("Runner '" + options.RunnerId + "' is not eligible (HTTP " + answer.Status + ").");
                return 1;
            }
            if (answer.Status != 200)
            {
                var detail = (answer.Body ?? "").Trim();
                host.Error.WriteLine("Runner '" + options.RunnerId + "' request failed with HTTP " + answer.Status + "." + (detail.Length == 0 ? "" : " " + detail));
                return 1;
            }
            host.Out.WriteLine(string.IsNullOrWhiteSpace(answer.Body) ? "HTTP 200" : answer.Body);
            return 0;
        }
    }
}
