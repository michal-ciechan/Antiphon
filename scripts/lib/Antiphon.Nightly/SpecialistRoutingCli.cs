// CARD-0415: inspect or configure the Check interpreter's declared routing. Configuration does not qualify a model. It
// replaced scripts/specialist-routing.ps1 (deleted 2026-11-23, unit 24 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md);
// the CLI runs it as `antiphon-scripts specialist-routing [...]` (scripts/antiphon specialist-routing on sh).
//
//   specialist-routing [inspect] --agent <guid>
//   specialist-routing set --agent <guid> (--candidates ClaudeCode/High,Codex/Low | --disable)
//   specialist-routing revalidate --agent <guid>
//
// Each verb reads GET /api/agents/{agent}/specialist-routing first. set PUTs the current concurrency token, enabled (not
// --disable) and the candidates (with --disable alone: the current ones). revalidate POSTs the current token to /revalidate
// and refuses when no chain is declared. The answer is printed as indented JSON. The task token (ANTIPHON_TASK_TOKEN) is
// sent when set, never printed. Exit 0 done; 1 an invalid candidate, no declared chain or a failed request (one stderr line);
// usage errors are NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The specialist-routing command's options (specialist-routing.ps1 &lt;verb&gt; -Agent -Candidates -Disable).</summary>
    public sealed class SpecialistRoutingCliOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "specialist-routing [inspect | set (--candidates <Kind/Level,...> | --disable) | revalidate] --agent <guid>";

        private static readonly string[] Verbs = { "inspect", "set", "revalidate" };

        /// <summary>inspect, set or revalidate.</summary>
        public string Verb { get; init; } = "inspect";

        /// <summary>The standing agent.</summary>
        public Guid Agent { get; init; }

        /// <summary>set: the candidate pairs as given (comma separated Kind/Level); empty: keep the current ones.</summary>
        public string Candidates { get; init; } = "";

        /// <summary>set: disable the routing.</summary>
        public bool Disable { get; init; }

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>
        /// The verb (inspect when the first argument is a flag) and the flags; the API and the task token come from the CLI. No
        /// --agent or one that is not a GUID, --candidates or --disable outside set, or set with neither is a NightlyUsageException.
        /// </summary>
        public static SpecialistRoutingCliOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken)
        {
            var verb = BoardCli.Verb(arguments, Verbs, "inspect", out var rest);
            var flags = NightlyArgs.Parse(rest, new[] { "--agent", "--candidates" }, new[] { "--disable" });
            if (!Guid.TryParse(flags.Value("--agent", ""), out var agent)) throw new NightlyUsageException("--agent is required and must be a GUID");
            if (verb != "set" && (flags.Has("--candidates") || flags.Has("--disable"))) throw new NightlyUsageException("--candidates and --disable apply only to set");
            if (verb == "set" && string.IsNullOrWhiteSpace(flags.Value("--candidates")) && !flags.Switch("--disable"))
                throw new NightlyUsageException("set requires --candidates Kind/Level,... or --disable");
            return new SpecialistRoutingCliOptions
            {
                Verb = verb,
                Agent = agent,
                Candidates = flags.Value("--candidates", ""),
                Disable = flags.Switch("--disable"),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
            };
        }
    }

    /// <summary>The Check interpreter's routing inspect, set and revalidate (specialist-routing.ps1).</summary>
    public static class SpecialistRoutingCli
    {
        private static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["claudecode"] = "ClaudeCode",
            ["codex"] = "Codex",
        };

        private static readonly IReadOnlyDictionary<string, string> Levels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["frontier"] = "Frontier",
            ["high"] = "High",
            ["medium"] = "Medium",
            ["low"] = "Low",
        };

        /// <summary>Runs the verb: the answer on host.Out as indented JSON; exit 0, or 1 after one line on host.Error.</summary>
        public static int Run(SpecialistRoutingCliOptions options, NightlyHost host, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var pairs = new JsonArray();
            if (options.Verb == "set" && !string.IsNullOrWhiteSpace(options.Candidates))
            {
                foreach (var pair in options.Candidates.Split(','))
                {
                    var parts = pair.Trim().Split('/');
                    if (parts.Length != 2 || !Kinds.TryGetValue(parts[0].Trim(), out var kind) || !Levels.TryGetValue(parts[1].Trim(), out var level))
                        return BoardCli.Refuse(host, "Invalid candidate '" + pair + "'; use a complete ClaudeCode/Level or Codex/Level pair.");
                    pairs.Add(new JsonObject { ["agentKind"] = kind, ["modelLevel"] = level });
                }
            }

            var headers = BoardCli.Headers(options.TaskToken);
            var route = "/api/agents/" + options.Agent.ToString("D") + "/specialist-routing";
            if (!BoardCli.Send(http, options.Api, "GET", route, headers, null, out var current, out var failure)) return BoardCli.Refuse(host, failure);
            var result = current;
            if (options.Verb == "set")
            {
                if (pairs.Count == 0) pairs = new JsonArray(BoardCli.Items(BoardCli.Get(current, "candidates")).Select(candidate => candidate?.DeepClone()).ToArray());
                var body = new JsonObject
                {
                    ["concurrencyToken"] = BoardCli.Get(current, "concurrencyToken")?.DeepClone(),
                    ["enabled"] = !options.Disable,
                    ["candidates"] = pairs,
                };
                if (!BoardCli.Send(http, options.Api, "PUT", route, headers, OperatorApi.Json(body), out result, out failure)) return BoardCli.Refuse(host, failure);
            }
            else if (options.Verb == "revalidate")
            {
                var token = BoardCli.Get(current, "concurrencyToken");
                if (!BoardCli.Truthy(token)) return BoardCli.Refuse(host, "Declare a routing chain before requesting revalidation.");
                var body = new JsonObject { ["concurrencyToken"] = token.DeepClone() };
                if (!BoardCli.Send(http, options.Api, "POST", route + "/revalidate", headers, OperatorApi.Json(body), out result, out failure)) return BoardCli.Refuse(host, failure);
            }
            host.Out.WriteLine(OperatorApi.Pretty(result));
            return 0;
        }
    }
}
