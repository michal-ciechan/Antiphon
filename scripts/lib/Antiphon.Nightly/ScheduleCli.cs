// CARD-0057: read and write clock-driven prompt and card schedules from a shell, without composing HTTP by hand. It replaced
// scripts/schedule.ps1 (deleted 2026-11-23, unit 24 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI
// runs it as `antiphon-scripts schedule <verb> [...]` (scripts/antiphon schedule on sh, scripts\antiphon.cmd on Windows).
//
//   schedule list    [--agent <id|slug|name>] [--card <card>] [--json]
//   schedule get     <id> [--json]
//   schedule preview [--agent a] [--name n] [--prompt s | --prompt-file p] [--repeat Once|Interval|Daily] [--fire-at utc]
//                    [--every-minutes n] [--at-local HH:mm] [--days-of-week mask] [--time-zone id] [--when-target-down w]
//                    [--by who] [--card id --to Backlog|InProgress|Review [--start None|Release|Spawn] [--accept-spend]] [--json]
//   schedule new     --name n (--agent a [--prompt s | --prompt-file p] | --card id --to InProgress [--start ...]) ... [--json]
//   schedule enable|disable|remove|fire <id>
//
// The id may also be given as --id. --agent defaults to the agent the CLI passes in (ANTIPHON_AGENT_ID), so a standing agent
// can schedule itself in one line; a pool delegate is refused by the server, which names the candidates. --card takes
// CARD-nnnn, card-51, #51, 51 or the guid, resolved by the server; a 409 lists every candidate. new prints the preview
// first and refuses a schedule that would spend (start a session) without --accept-spend. Long prompt text comes from
// --prompt-file (read as UTF-8). Times are printed as the server wrote them. The task token (ANTIPHON_TASK_TOKEN) is sent
// when set, never printed. Exit 0 done; 1 a refused value, an unacknowledged spend or a failed request (one stderr line;
// a problem answer adds its code, field errors and candidates); usage errors are NightlyUsageException (the CLI's exit 2).
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The schedule command's options (schedule.ps1 &lt;verb&gt; [&lt;id&gt;] and its flags).</summary>
    public sealed class ScheduleCliOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "schedule <list [--agent a] [--card c] [--json] | get <id> [--json] | preview|new [--name n] [--agent a] [--prompt s | --prompt-file p]"
            + " [--repeat Once|Interval|Daily] [--fire-at utc] [--every-minutes n] [--at-local HH:mm] [--days-of-week mask] [--time-zone id] [--when-target-down w]"
            + " [--by who] [--card c --to Backlog|InProgress|Review] [--start None|Release|Spawn] [--accept-spend] [--json] | enable|disable|remove|fire <id>>";

        private static readonly string[] Verbs = { "list", "get", "preview", "new", "enable", "disable", "remove", "fire" };
        private static readonly string[] Valued =
        {
            "--id", "--agent", "--name", "--prompt", "--prompt-file", "--repeat", "--fire-at", "--every-minutes", "--at-local", "--days-of-week", "--time-zone",
            "--when-target-down", "--by", "--card", "--to", "--start",
        };
        private static readonly string[] Switches = { "--accept-spend", "--json" };
        private static readonly string[] CreateFlags =
        {
            "--agent", "--name", "--prompt", "--prompt-file", "--repeat", "--fire-at", "--every-minutes", "--at-local", "--days-of-week", "--time-zone",
            "--when-target-down", "--by", "--card", "--to", "--start", "--accept-spend", "--json",
        };

        private static readonly string[] Repeats = { "Once", "Interval", "Daily" };
        private static readonly string[] Targets = { "Backlog", "InProgress", "Review" };
        private static readonly string[] Starts = { "None", "Release", "Spawn" };

        /// <summary>The verb.</summary>
        public string Verb { get; init; } = "list";

        /// <summary>get, enable, disable, remove, fire: the schedule.</summary>
        public string Id { get; init; } = "";

        /// <summary>The agent reference as given, else the CLI's ANTIPHON_AGENT_ID; empty: none.</summary>
        public string Agent { get; init; } = "";

        /// <summary>The schedule's name.</summary>
        public string Name { get; init; } = "";

        /// <summary>The prompt text given inline.</summary>
        public string Prompt { get; init; } = "";

        /// <summary>The file holding the prompt text.</summary>
        public string PromptFile { get; init; } = "";

        /// <summary>Once, Interval or Daily.</summary>
        public string Repeat { get; init; } = "Once";

        /// <summary>Once: when, as given.</summary>
        public string FireAt { get; init; } = "";

        /// <summary>Interval: minutes between fires; 0: not given.</summary>
        public int EveryMinutes { get; init; }

        /// <summary>Daily: the local time (HH:mm).</summary>
        public string AtLocal { get; init; } = "";

        /// <summary>Daily: the day mask; 0: every day.</summary>
        public int DaysOfWeek { get; init; }

        /// <summary>The time zone id; empty: the server's.</summary>
        public string TimeZone { get; init; } = "";

        /// <summary>What to do when the target is down; empty: the server's default.</summary>
        public string WhenTargetDown { get; init; } = "";

        /// <summary>Who creates it; empty: the server's default.</summary>
        public string By { get; init; } = "";

        /// <summary>The card (a card schedule; list: the filter).</summary>
        public string Card { get; init; } = "";

        /// <summary>A card schedule's target status.</summary>
        public string To { get; init; } = "";

        /// <summary>A card schedule's start: None, Release or Spawn.</summary>
        public string Start { get; init; } = "None";

        /// <summary>Accept a schedule that would start a session.</summary>
        public bool AcceptSpend { get; init; }

        /// <summary>Print the answers as JSON.</summary>
        public bool Json { get; init; }

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>
        /// The verb, the id (positional or --id) and the flags; the API, the task token and the default agent
        /// (ANTIPHON_AGENT_ID) come from the CLI. A missing verb or id, an id given twice, a value outside a flag's choices, a
        /// count that is not an integer, new without --name, or a flag the verb does not take is a NightlyUsageException.
        /// </summary>
        public static ScheduleCliOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken, string agentId)
        {
            var verb = BoardCli.Verb(arguments, Verbs, null, out var rest);
            var positional = BoardCli.Positional(rest);
            var flags = NightlyArgs.Parse(rest, Valued, Switches);
            var declared = Valued.Concat(Switches).ToArray();
            if (positional != null && flags.Has("--id")) throw new NightlyUsageException("give the id once, not as an argument and --id");
            var id = positional ?? flags.Value("--id", "");
            switch (verb)
            {
                case "list":
                    BoardCli.Only(flags, verb, declared, new[] { "--agent", "--card", "--json" });
                    if (positional != null) throw new NightlyUsageException("list takes no id");
                    break;
                case "get":
                    BoardCli.Only(flags, verb, declared, new[] { "--id", "--json" });
                    break;
                case "preview":
                case "new":
                    BoardCli.Only(flags, verb, declared, CreateFlags);
                    if (positional != null) throw new NightlyUsageException(verb + " takes no id");
                    if (verb == "new" && string.IsNullOrWhiteSpace(flags.Value("--name"))) throw new NightlyUsageException("new requires --name");
                    break;
                default:
                    BoardCli.Only(flags, verb, declared, new[] { "--id" });
                    break;
            }
            if ((verb == "get" || verb == "enable" || verb == "disable" || verb == "remove" || verb == "fire") && string.IsNullOrWhiteSpace(id))
                throw new NightlyUsageException(verb + " requires the schedule id");
            var agent = flags.Value("--agent", "");
            return new ScheduleCliOptions
            {
                Verb = verb,
                Id = id,
                Agent = string.IsNullOrWhiteSpace(agent) ? agentId ?? "" : agent,
                Name = flags.Value("--name", ""),
                Prompt = flags.Value("--prompt", ""),
                PromptFile = flags.Value("--prompt-file", ""),
                Repeat = BoardCli.Choice("--repeat", flags.Value("--repeat"), Repeats) ?? "Once",
                FireAt = flags.Value("--fire-at", ""),
                EveryMinutes = flags.Int("--every-minutes", 0),
                AtLocal = flags.Value("--at-local", ""),
                DaysOfWeek = flags.Int("--days-of-week", 0),
                TimeZone = flags.Value("--time-zone", ""),
                WhenTargetDown = flags.Value("--when-target-down", ""),
                By = flags.Value("--by", ""),
                Card = flags.Value("--card", ""),
                To = BoardCli.Choice("--to", flags.Value("--to"), Targets) ?? "",
                Start = BoardCli.Choice("--start", flags.Value("--start"), Starts) ?? "None",
                AcceptSpend = flags.Switch("--accept-spend"),
                Json = flags.Switch("--json"),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
            };
        }
    }

    /// <summary>The schedule list, get, preview, create, enable, disable, remove and fire (schedule.ps1).</summary>
    public static class ScheduleCli
    {
        private static readonly Regex AgentGuid = new Regex("^[0-9a-fA-F-]{36}$", RegexOptions.CultureInvariant);

        /// <summary>Runs the verb: the answer on host.Out; exit 0, or 1 after one line on host.Error.</summary>
        public static int Run(ScheduleCliOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = BoardCli.Headers(options.TaskToken);
            var route = "/api/schedules/" + Uri.EscapeDataString(options.Id);
            JsonNode answer;
            string failure;
            switch (options.Verb)
            {
                case "list":
                {
                    var query = new List<string>();
                    if (AgentGuid.IsMatch(options.Agent)) query.Add("agentId=" + options.Agent);
                    if (!string.IsNullOrWhiteSpace(options.Card)) query.Add("cardId=" + Uri.EscapeDataString(options.Card.Trim()));
                    var path = "/api/schedules" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
                    if (!BoardCli.Send(http, options.Api, "GET", path, headers, null, out answer, out failure)) return BoardCli.Refuse(host, failure);
                    if (options.Json) host.Out.WriteLine(OperatorApi.Pretty(answer));
                    else
                        foreach (var row in BoardCli.Items(BoardCli.Get(answer, "schedules"))) host.Out.WriteLine(ScheduleLine(row));
                    return 0;
                }
                case "get":
                {
                    if (!BoardCli.Send(http, options.Api, "GET", route, headers, null, out answer, out failure)) return BoardCli.Refuse(host, failure);
                    if (options.Json)
                    {
                        host.Out.WriteLine(OperatorApi.Pretty(answer));
                        return 0;
                    }
                    host.Out.WriteLine(ScheduleLine(answer));
                    foreach (var fire in BoardCli.Items(BoardCli.Get(answer, "fires")))
                        host.Out.WriteLine("  fire #" + T(fire, "fireNumber") + "  " + T(fire, "outcome") + "  " + T(fire, "detail"));
                    return 0;
                }
                case "preview":
                case "new":
                {
                    var body = CreateBody(options, files, out var refusal);
                    if (body == null) return BoardCli.Refuse(host, refusal);
                    var text = OperatorApi.Json(body);
                    if (!BoardCli.Send(http, options.Api, "POST", "/api/schedules/preview", headers, text, out var preview, out failure)) return BoardCli.Refuse(host, failure);
                    if (options.Verb == "preview")
                    {
                        if (options.Json) host.Out.WriteLine(OperatorApi.Pretty(preview));
                        else WritePreview(host, preview);
                        return 0;
                    }
                    if (!options.Json) WritePreview(host, preview);
                    var spend = T(preview, "spend");
                    if (spend.Length > 0 && spend != "none" && !options.AcceptSpend)
                        return BoardCli.Refuse(host, "This schedule would start a session (spend=" + spend + "). Re-run with --accept-spend after reading the preview.");
                    if (!BoardCli.Send(http, options.Api, "POST", "/api/schedules", headers, text, out answer, out failure)) return BoardCli.Refuse(host, failure);
                    if (options.Json)
                    {
                        host.Out.WriteLine(OperatorApi.Pretty(answer));
                        return 0;
                    }
                    host.Out.WriteLine("created " + T(answer, "id"));
                    host.Out.WriteLine(ScheduleLine(answer));
                    return 0;
                }
                case "enable":
                case "disable":
                {
                    if (!BoardCli.Send(http, options.Api, "GET", route, headers, null, out var current, out failure)) return BoardCli.Refuse(host, failure);
                    var patch = new JsonObject { ["concurrencyToken"] = BoardCli.Get(current, "concurrencyToken")?.DeepClone(), ["enabled"] = options.Verb == "enable" };
                    if (!BoardCli.Send(http, options.Api, "PATCH", route, headers, OperatorApi.Json(patch), out answer, out failure)) return BoardCli.Refuse(host, failure);
                    host.Out.WriteLine(options.Json ? OperatorApi.Pretty(answer) : ScheduleLine(answer));
                    return 0;
                }
                case "remove":
                    if (!BoardCli.Send(http, options.Api, "DELETE", route, headers, null, out _, out failure)) return BoardCli.Refuse(host, failure);
                    host.Out.WriteLine("removed " + options.Id);
                    return 0;
                default:
                    if (!BoardCli.Send(http, options.Api, "POST", route + "/fire-now", headers, null, out _, out failure)) return BoardCli.Refuse(host, failure);
                    host.Out.WriteLine("fired " + options.Id);
                    return 0;
            }
        }

        // New-CreateBody: a Card schedule when --card is given, else a Prompt one for the agent; then the repeat's own field.
        // null with the refusal when a required value is missing or the prompt file cannot be read.
        private static JsonObject CreateBody(ScheduleCliOptions options, IFileSystem files, out string refusal)
        {
            refusal = null;
            var isCard = !string.IsNullOrWhiteSpace(options.Card);
            var body = new JsonObject { ["name"] = options.Name, ["kind"] = isCard ? "Card" : "Prompt", ["repeat"] = options.Repeat };
            if (!string.IsNullOrWhiteSpace(options.By)) body["createdBy"] = options.By;
            if (isCard)
            {
                if (options.To.Length == 0)
                {
                    refusal = "A card schedule requires --to (Backlog, InProgress, or Review).";
                    return null;
                }
                body["cardId"] = options.Card;
                body["targetStatus"] = options.To;
                body["start"] = options.Start;
                if (options.AcceptSpend) body["acceptSpend"] = true;
            }
            else
            {
                var prompt = options.Prompt;
                if (!string.IsNullOrWhiteSpace(options.PromptFile))
                {
                    if (!string.IsNullOrWhiteSpace(options.Prompt))
                    {
                        refusal = "Pass --prompt or --prompt-file, not both.";
                        return null;
                    }
                    if (!files.File.Exists(options.PromptFile))
                    {
                        refusal = "PromptFile '" + options.PromptFile + "' was not found.";
                        return null;
                    }
                    prompt = files.File.ReadAllText(options.PromptFile, Encoding.UTF8);
                }
                body["agent"] = string.IsNullOrWhiteSpace(options.Agent) ? null : options.Agent;
                if (!string.IsNullOrEmpty(prompt)) body["promptText"] = prompt;
            }
            if (!string.IsNullOrWhiteSpace(options.TimeZone)) body["timeZoneId"] = options.TimeZone;
            if (!string.IsNullOrWhiteSpace(options.WhenTargetDown)) body["whenTargetDown"] = options.WhenTargetDown;
            switch (options.Repeat)
            {
                case "Once":
                    if (string.IsNullOrWhiteSpace(options.FireAt))
                    {
                        refusal = "Once requires --fire-at (UTC, e.g. 2026-09-04T09:00:00Z).";
                        return null;
                    }
                    body["fireAt"] = options.FireAt;
                    break;
                case "Interval":
                    if (options.EveryMinutes < 1)
                    {
                        refusal = "Interval requires --every-minutes (1..10080).";
                        return null;
                    }
                    body["everyMinutes"] = options.EveryMinutes;
                    break;
                default:
                    if (string.IsNullOrWhiteSpace(options.AtLocal))
                    {
                        refusal = "Daily requires --at-local (HH:mm).";
                        return null;
                    }
                    body["atLocal"] = options.AtLocal;
                    if (options.DaysOfWeek != 0) body["daysOfWeek"] = options.DaysOfWeek;
                    break;
            }
            return body;
        }

        /// <summary>Write-Preview: the next occurrences, the target, the effect, the spend, the start, the environment and the warnings.</summary>
        public static void WritePreview(NightlyHost host, JsonNode preview)
        {
            host.Out.WriteLine("Preview");
            foreach (var occurrence in BoardCli.Items(BoardCli.Get(preview, "nextOccurrences")))
                host.Out.WriteLine("  next  " + T(occurrence, "utc") + "  (" + T(occurrence, "local") + ")");
            var target = BoardCli.Get(preview, "target");
            if (BoardCli.Truthy(BoardCli.Get(target, "agentName")))
                host.Out.WriteLine("  agent " + T(target, "agentName") + "  live=" + T(target, "agentLive") + "  alwaysOn=" + T(target, "agentAlwaysOn") + "  session=" + T(target, "sessionStatus"));
            if (BoardCli.Truthy(BoardCli.Get(target, "cardIdentifier")))
                host.Out.WriteLine("  card  " + T(target, "cardIdentifier") + "  status=" + T(target, "cardStatus") + "  column=" + T(target, "cardColumn") + "  archived=" + T(target, "cardArchived"));
            host.Out.WriteLine("  effect " + T(preview, "effect"));
            host.Out.WriteLine("  spend  " + T(preview, "spend"));
            if (BoardCli.Get(preview, "willStartSession") != null)
                host.Out.WriteLine("  start  willStartSession=" + T(preview, "willStartSession") + "  willMove=" + T(preview, "willMove"));
            var environment = BoardCli.Get(preview, "environment");
            if (environment != null)
                host.Out.WriteLine("  env    orchestratorPaused=" + T(environment, "orchestratorPaused") + "  board=" + T(environment, "boardActiveCount") + "/" + T(environment, "boardCap")
                    + "  hold=" + T(environment, "modelAvailabilityHold") + "  assigned=" + T(environment, "assignedAgent") + "  definition=" + T(environment, "defaultDefinition"));
            foreach (var warning in BoardCli.Items(BoardCli.Get(preview, "warnings"))) host.Out.WriteLine("  warn   " + BoardCli.Text(warning));
        }

        /// <summary>Write-Schedule's line: "&lt;id&gt;  &lt;name&gt;  &lt;repeat&gt;  next &lt;local or utc&gt;  last &lt;outcome&gt;".</summary>
        public static string ScheduleLine(JsonNode row)
        {
            var next = BoardCli.Truthy(BoardCli.Get(row, "nextFireAtLocal")) ? T(row, "nextFireAtLocal") : T(row, "nextFireAt");
            return string.Format(CultureInfo.InvariantCulture, "{0}  {1}  {2}  next {3}  last {4}", T(row, "id"), T(row, "name"), T(row, "repeatDescription"), next, T(row, "lastOutcome"));
        }

        private static string T(JsonNode node, params string[] path) => BoardCli.Text(BoardCli.Get(node, path));
    }
}
