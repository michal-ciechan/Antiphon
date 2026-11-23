// CARD-0653: list or force-release phone-home runner seats. It replaced scripts/runner-slots.ps1 (deleted 2026-11-23, unit 23
// of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
// `antiphon-scripts runner-slots <list|release|release-orphans> [...]`.
//
//   runner-slots list [--runner-id server2]
//   runner-slots release --session-id <guid> --reason "why" [--runner-id server2]
//   runner-slots release-orphans --reason "why" [--runner-id server2]
//
// The release verbs send the operator token from the owner-only file the server creates at startup (the CLI passes
// ANTIPHON_OPERATOR_TOKEN_FILE, else the server's default path); the token is never printed. The task token
// (ANTIPHON_TASK_TOKEN) is sent when set. The answer is printed as indented JSON ("null" for an empty body). Exit 0 answered;
// 1 a failed request or an unreadable token file (one stderr line); usage errors are NightlyUsageException (the CLI's exit
// 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The runner-slots command's options (runner-slots.ps1 &lt;verb&gt; -RunnerId -SessionId -Reason).</summary>
    public sealed class RunnerSlotsOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "runner-slots <list|release|release-orphans> [--runner-id server2] [--session-id <guid>] [--reason <why>]";

        private static readonly string[] Verbs = { "list", "release", "release-orphans" };

        /// <summary>list, release or release-orphans.</summary>
        public string Verb { get; init; } = "list";

        /// <summary>The runner.</summary>
        public string RunnerId { get; init; } = "server2";

        /// <summary>release: the seat's session.</summary>
        public string SessionId { get; init; } = "";

        /// <summary>Why (release and release-orphans require it).</summary>
        public string Reason { get; init; } = "";

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: no task token header.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>The operator token file.</summary>
        public string OperatorTokenFile { get; init; } = "";

        /// <summary>
        /// The verb and the flags after the command name; the API, the task token and the token file come from the CLI. A verb
        /// outside the three, or a release without --session-id or --reason, is a NightlyUsageException.
        /// </summary>
        public static RunnerSlotsOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken, string operatorTokenFile)
        {
            var verb = arguments != null && arguments.Count > 0 ? arguments[0] ?? "" : "";
            if (!Verbs.Contains(verb, StringComparer.Ordinal)) throw new NightlyUsageException("the first argument is the verb: list, release or release-orphans, not '" + verb + "'");
            var flags = NightlyArgs.Parse(arguments.Skip(1).ToArray(), new[] { "--runner-id", "--session-id", "--reason" }, Array.Empty<string>());
            var options = new RunnerSlotsOptions
            {
                Verb = verb,
                RunnerId = flags.Value("--runner-id", "server2"),
                SessionId = flags.Value("--session-id", ""),
                Reason = flags.Value("--reason", ""),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
                OperatorTokenFile = operatorTokenFile ?? "",
            };
            if (string.IsNullOrWhiteSpace(options.RunnerId)) throw new NightlyUsageException("--runner-id is empty");
            if (verb == "release" && string.IsNullOrWhiteSpace(options.SessionId)) throw new NightlyUsageException("release requires --session-id");
            if (verb != "list" && string.IsNullOrWhiteSpace(options.Reason)) throw new NightlyUsageException(verb + " requires --reason");
            return options;
        }
    }

    /// <summary>The runner seat list and force-release (runner-slots.ps1).</summary>
    public static class RunnerSlots
    {
        /// <summary>Each request's deadline (a release kills the seat's process tree on the runner first).</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(5);

        /// <summary>Runs the verb: the answer on host.Out; exit 0, or 1 after one line on host.Error.</summary>
        public static int Run(RunnerSlotsOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(options.TaskToken)) headers[OperatorApi.TaskTokenHeader] = options.TaskToken;
            var slots = "/api/session-runners/" + Uri.EscapeDataString(options.RunnerId) + "/slots";
            string path;
            string body = null;
            switch (options.Verb)
            {
                case "list":
                    path = slots;
                    break;
                case "release":
                    path = slots + "/" + Uri.EscapeDataString(options.SessionId) + "/release";
                    body = OperatorApi.Json(new JsonObject { ["reason"] = options.Reason });
                    break;
                case "release-orphans":
                    path = slots + "/release-orphans";
                    body = OperatorApi.Json(new JsonObject { ["reason"] = options.Reason });
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

            var method = body == null ? "GET" : "POST";
            NightlyReportHttpResponse answer;
            try
            {
                answer = http(method, options.Api + path, headers, body);
            }
            catch (Exception e)
            {
                // A transport failure: no status. The exception text names the address, never a header.
                host.Error.WriteLine("runner-slots: " + method + " " + path + " failed: " + e.Message);
                return 1;
            }
            if (!OperatorApi.Succeeded(answer))
            {
                host.Error.WriteLine("runner-slots: " + method + " " + path + " failed: " + OperatorApi.Failure(answer));
                return 1;
            }
            JsonNode parsed;
            try
            {
                parsed = string.IsNullOrWhiteSpace(answer.Body) ? null : JsonNode.Parse(answer.Body);
            }
            catch (System.Text.Json.JsonException e)
            {
                host.Error.WriteLine("runner-slots: " + method + " " + path + " answered with a body that is not JSON: " + e.Message);
                return 1;
            }
            host.Out.WriteLine(OperatorApi.Pretty(parsed));
            return 0;
        }
    }
}
