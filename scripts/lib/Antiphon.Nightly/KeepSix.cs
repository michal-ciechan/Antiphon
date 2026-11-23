// CARD-1320: read the keep-six status, declare or clear a hold, or acknowledge a reminder for a standing orchestrator. It
// replaced scripts/keep-six.ps1 (unit 23 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; the .ps1 is now the cutover's thin launcher);
// the CLI runs it as `antiphon-scripts keep-six [...]` (scripts/antiphon keep-six on sh, scripts\antiphon.cmd on Windows).
//
//   keep-six
//       GET /api/orchestrators/keep-six. This does not create a task.
//   keep-six --hold --agent <guid> --reason <text> --until <iso8601>
//       POST /api/orchestrators/{agent}/keep-six/hold. The reason is 1 to 200 characters (trimmed); until is in the future
//       and at most 7 days ahead, sent as UTC to the second.
//   keep-six --clear-hold --agent <guid>
//       DELETE /api/orchestrators/{agent}/keep-six/hold.
//   keep-six --ack --agent <guid>
//       POST /api/orchestrators/{agent}/keep-six/ack. The pending reminder stays pending.
//
// --hold, --clear-hold and --ack are the verbs (at most one). They send X-Antiphon-Task-Token from ANTIPHON_TASK_TOKEN (the
// CLI passes it in) and refuse without it; the status read sends it when it is set. The token is a header only, never
// printed. The answer is printed as one compact JSON line ("null" for an empty body). Exit 0 done; 1 a refused value, a
// missing task token or a failed request (one stderr line); usage errors are NightlyUsageException (the CLI's exit 2). Plain
// C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The keep-six command's options (keep-six.ps1 -Hold -ClearHold -Ack -Agent -Reason -Until).</summary>
    public sealed class KeepSixOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "keep-six [--hold --agent <guid> --reason <text> --until <iso8601> | --clear-hold --agent <guid> | --ack --agent <guid>]";

        /// <summary>status, hold, clear-hold or ack.</summary>
        public string Verb { get; init; } = "status";

        /// <summary>The orchestrator agent (hold, clear-hold and ack), as given.</summary>
        public string Agent { get; init; } = "";

        /// <summary>hold: why, as given.</summary>
        public string Reason { get; init; } = "";

        /// <summary>hold: until when, as given.</summary>
        public string Until { get; init; } = "";

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>
        /// The flags after the command name; the API and the task token come from the CLI. Two verbs, --agent without a verb,
        /// a verb without --agent, or --reason/--until outside --hold (or missing from it) is a NightlyUsageException, as the
        /// script's parameter sets refused them.
        /// </summary>
        public static KeepSixOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--agent", "--reason", "--until" }, new[] { "--hold", "--clear-hold", "--ack" });
            var verbs = new[] { "--hold", "--clear-hold", "--ack" }.Where(flags.Switch).ToArray();
            if (verbs.Length > 1) throw new NightlyUsageException("give one of --hold, --clear-hold and --ack, not " + string.Join(" and ", verbs));
            var verb = verbs.Length == 0 ? "status" : verbs[0].Substring(2);
            if (verb == "status")
            {
                foreach (var flag in new[] { "--agent", "--reason", "--until" })
                    if (flags.Has(flag)) throw new NightlyUsageException(flag + " needs --hold, --clear-hold or --ack");
            }
            else
            {
                if (string.IsNullOrEmpty(flags.Value("--agent"))) throw new NightlyUsageException(verb + " requires --agent");
                if (verb == "hold")
                {
                    if (string.IsNullOrEmpty(flags.Value("--reason"))) throw new NightlyUsageException("hold requires --reason");
                    if (string.IsNullOrEmpty(flags.Value("--until"))) throw new NightlyUsageException("hold requires --until");
                }
                else
                {
                    foreach (var flag in new[] { "--reason", "--until" })
                        if (flags.Has(flag)) throw new NightlyUsageException(flag + " belongs to --hold");
                }
            }
            return new KeepSixOptions
            {
                Verb = verb,
                Agent = flags.Value("--agent", ""),
                Reason = flags.Value("--reason", ""),
                Until = flags.Value("--until", ""),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
            };
        }
    }

    /// <summary>The keep-six status, hold, clear and acknowledgement (keep-six.ps1).</summary>
    public static class KeepSix
    {
        /// <summary>Each request's deadline.</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

        /// <summary>The longest hold the server accepts.</summary>
        public static readonly TimeSpan LongestHold = TimeSpan.FromDays(7);

        /// <summary>Runs the verb: the answer on host.Out; exit 0, or 1 after one line on host.Error.</summary>
        public static int Run(KeepSixOptions options, NightlyHost host, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(options.TaskToken)) headers[OperatorApi.TaskTokenHeader] = options.TaskToken;
            else if (options.Verb != "status") return Refuse(host, "ANTIPHON_TASK_TOKEN is required for hold, clear, and ack.");
            if (options.Verb == "status") return Call(host, http, "GET", options.Api, "/api/orchestrators/keep-six", headers, null);

            if (!Guid.TryParse(options.Agent, out var agent)) return Refuse(host, "agent must be a GUID.");
            var route = "/api/orchestrators/" + agent.ToString("D") + "/keep-six";
            if (options.Verb == "ack") return Call(host, http, "POST", options.Api, route + "/ack", headers, null);
            if (options.Verb == "clear-hold") return Call(host, http, "DELETE", options.Api, route + "/hold", headers, null);

            var reason = (options.Reason ?? "").Trim();
            if (reason.Length < 1 || reason.Length > 200) return Refuse(host, "reason is required and must be at most 200 characters.");
            if (!DateTimeOffset.TryParse(options.Until, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var until))
                return Refuse(host, "until must be a date and time.");
            var untilUtc = until.ToUniversalTime();
            var now = host.Clock.GetUtcNow();
            if (untilUtc <= now || untilUtc > now + LongestHold) return Refuse(host, "until must be in the future and at most 7 days ahead.");
            var body = new JsonObject { ["reason"] = reason, ["until"] = untilUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) };
            return Call(host, http, "POST", options.Api, route + "/hold", headers, OperatorApi.Json(body));
        }

        // Invoke-RestMethod, then ConvertTo-Json -Compress of its answer: a non-2xx status or a failed transport is one line
        // "Antiphon <METHOD> <path> failed: <detail>" and exit 1.
        private static int Call(NightlyHost host, NightlyReportHttp http, string method, string api, string path, IReadOnlyDictionary<string, string> headers, string body)
        {
            NightlyReportHttpResponse answer;
            try
            {
                answer = http(method, api + path, headers, body);
            }
            catch (Exception e)
            {
                // A transport failure: no status. The exception text names the address, never a header.
                return Refuse(host, "Antiphon " + method + " " + path + " failed: " + e.Message);
            }
            if (!OperatorApi.Succeeded(answer)) return Refuse(host, "Antiphon " + method + " " + path + " failed: " + OperatorApi.Failure(answer));
            JsonNode parsed;
            try
            {
                parsed = string.IsNullOrWhiteSpace(answer.Body) ? null : JsonNode.Parse(answer.Body);
            }
            catch (System.Text.Json.JsonException e)
            {
                return Refuse(host, "Antiphon " + method + " " + path + " answered with a body that is not JSON: " + e.Message);
            }
            host.Out.WriteLine(OperatorApi.Json(parsed));
            return 0;
        }

        private static int Refuse(NightlyHost host, string message)
        {
            host.Error.WriteLine(message);
            return 1;
        }
    }
}
