// CARD-0305: record which agent or model a card's stage (or a whole stage) must run on, without composing HTTP by hand. It
// replaced scripts/routing-pin.ps1 (deleted 2026-11-23, unit 24 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md);
// the CLI runs it as `antiphon-scripts routing-pin [...]` (scripts/antiphon routing-pin on sh, scripts\antiphon.cmd on Windows).
// A pin is read by the NEXT create; it does not rewrite work that is already Queued. For a role-wide candidate change use
// complexity-chain set --role <role> --complexity All (its --clear-stage-pin removes the stage pin after the three cell writes);
// this command is for card pins, forbid lists, dates and standing-agent pins.
//
//   routing-pin [get] [--card CARD-0304] [--role Plan] [--json]
//   routing-pin set --role Plan [--card CARD-0304] [--provenance Human|Auto] [--strength Required|Preferred]
//       [--kind Codex] [--level Frontier] | [--candidates ClaudeCode/Frontier,ClaudeCode/High,Codex/Frontier]
//       [--forbidden fable,opus] [--not-before 2026-09-03T00:00:00Z] [--not-after ...] [--agent <guid>] [--reason r] [--json]
//   routing-pin clear --role Plan [--card CARD-0304]
//
// GRAIN. --card pins ONE card's stage; without it the pin is the stage's, for every card. One active pin per grain; a card pin
// outranks the stage pin as a whole row. PROVENANCE: Human (the default) means "the operator said so", and a later Auto
// write onto that row is refused 409 routing_pin_human; Auto is for what an agent decided and is freely replaceable.
// --not-before and --not-after are ISO-8601 with a Z or a numeric offset; a dated pin does not refuse the create (the
// dispatcher holds the work until the instant), the opposite of a model-availability hold, which 409s a new create.
// clear finds the grain's pin (GET) and deletes it by id. The task token (ANTIPHON_TASK_TOKEN) is sent when set, never
// printed. Exit 0 done (clear with no pin for the grain says so); 1 a refused value or a failed request (one stderr line);
// usage errors are NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The routing-pin command's options (routing-pin.ps1 &lt;verb&gt; and its flags).</summary>
    public sealed class RoutingPinCliOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "routing-pin [get [--card <card>] [--role <role>] [--json] | set --role <role> [--card <card>] [--provenance Human|Auto]"
            + " [--strength Required|Preferred] [--kind <kind>] [--level <level>] [--candidates <Kind/Level,...>] [--forbidden <alias,...>] [--not-before <utc>]"
            + " [--not-after <utc>] [--agent <guid>] [--reason <text>] [--json] | clear --role <role> [--card <card>]]";

        /// <summary>The roles a pin names: every hand-delegatable AgentTaskRole (the script's ValidateSet).</summary>
        public static readonly IReadOnlyList<string> Roles = new[]
        {
            "Investigate", "Plan", "TestDesign", "Code", "Mutation", "Review", "Debug", "Coverage", "Docs", "Commit", "Test", "Deploy", "Merge", "Custom",
        };

        private static readonly string[] Verbs = { "get", "set", "clear" };
        private static readonly string[] Valued =
        {
            "--role", "--card", "--provenance", "--strength", "--kind", "--level", "--candidates", "--forbidden", "--not-before", "--not-after", "--agent", "--reason",
        };
        private static readonly string[] Provenances = { "Human", "Auto" };
        private static readonly string[] Strengths = { "Required", "Preferred" };
        private static readonly string[] Kinds = { "ClaudeCode", "Grok", "Codex" };
        private static readonly string[] Levels = { "Frontier", "High", "Medium", "Low" };

        /// <summary>get, set or clear.</summary>
        public string Verb { get; init; } = "get";

        /// <summary>The stage, in its listed spelling; empty: none (get only).</summary>
        public string Role { get; init; } = "";

        /// <summary>The card grain; empty: the stage-wide pin.</summary>
        public string Card { get; init; } = "";

        /// <summary>set: Human or Auto.</summary>
        public string Provenance { get; init; } = "Human";

        /// <summary>set: Required or Preferred.</summary>
        public string Strength { get; init; } = "Preferred";

        /// <summary>set: the one-candidate shorthand's kind.</summary>
        public string Kind { get; init; } = "";

        /// <summary>set: the one-candidate shorthand's level.</summary>
        public string Level { get; init; } = "";

        /// <summary>set: the candidate list as given (Kind/Level or bare Kind tokens, comma separated, in order).</summary>
        public string Candidates { get; init; } = "";

        /// <summary>set: the forbidden aliases as given (comma separated).</summary>
        public string Forbidden { get; init; } = "";

        /// <summary>set: not before, as given.</summary>
        public string NotBefore { get; init; } = "";

        /// <summary>set: not after, as given.</summary>
        public string NotAfter { get; init; } = "";

        /// <summary>set: the standing agent to run the stage on.</summary>
        public string Agent { get; init; } = "";

        /// <summary>set: why.</summary>
        public string Reason { get; init; } = "";

        /// <summary>get and set: print the answer as JSON.</summary>
        public bool Json { get; init; }

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>
        /// The verb (get when the first argument is a flag) and the flags; the API and the task token come from the CLI. A value
        /// outside a flag's choices, set or clear without --role, or a flag the verb does not take is a NightlyUsageException.
        /// </summary>
        public static RoutingPinCliOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken)
        {
            var verb = BoardCli.Verb(arguments, Verbs, "get", out var rest);
            var flags = NightlyArgs.Parse(rest, Valued, new[] { "--json" });
            var declared = Valued.Concat(new[] { "--json" }).ToArray();
            if (verb == "get") BoardCli.Only(flags, verb, declared, new[] { "--card", "--role", "--json" });
            else if (verb == "clear") BoardCli.Only(flags, verb, declared, new[] { "--card", "--role" });
            var role = BoardCli.Choice("--role", flags.Value("--role"), Roles) ?? "";
            if (verb == "set" && role.Length == 0) throw new NightlyUsageException("set requires --role. A pin is per STAGE; add --card to narrow it to one card");
            if (verb == "clear" && role.Length == 0) throw new NightlyUsageException("clear requires --role (and --card when you mean one card rather than the stage)");
            return new RoutingPinCliOptions
            {
                Verb = verb,
                Role = role,
                Card = flags.Value("--card", ""),
                Provenance = BoardCli.Choice("--provenance", flags.Value("--provenance"), Provenances) ?? "Human",
                Strength = BoardCli.Choice("--strength", flags.Value("--strength"), Strengths) ?? "Preferred",
                Kind = BoardCli.Choice("--kind", flags.Value("--kind"), Kinds) ?? "",
                Level = BoardCli.Choice("--level", flags.Value("--level"), Levels) ?? "",
                Candidates = flags.Value("--candidates", ""),
                Forbidden = flags.Value("--forbidden", ""),
                NotBefore = flags.Value("--not-before", ""),
                NotAfter = flags.Value("--not-after", ""),
                Agent = flags.Value("--agent", ""),
                Reason = flags.Value("--reason", ""),
                Json = flags.Switch("--json"),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
            };
        }
    }

    /// <summary>The routing pin read, write and clear (routing-pin.ps1).</summary>
    public static class RoutingPinCli
    {
        /// <summary>Runs the verb: the answer on host.Out; exit 0, or 1 after one line on host.Error.</summary>
        public static int Run(RoutingPinCliOptions options, NightlyHost host, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = BoardCli.Headers(options.TaskToken);
            JsonNode answer;
            string failure;
            switch (options.Verb)
            {
                case "get":
                {
                    if (!BoardCli.Send(http, options.Api, "GET", Query(options), headers, null, out answer, out failure)) return BoardCli.Refuse(host, failure);
                    if (options.Json)
                    {
                        host.Out.WriteLine(OperatorApi.Pretty(answer));
                        return 0;
                    }
                    var pins = BoardCli.Items(BoardCli.Get(answer, "pins"));
                    if (pins.Count == 0) host.Out.WriteLine("No active routing pins.");
                    foreach (var pin in pins) host.Out.WriteLine("  " + PinLine(pin));
                    return 0;
                }
                case "set":
                {
                    if (options.Candidates.Length > 0 && (options.Kind.Length > 0 || options.Level.Length > 0))
                        return BoardCli.Refuse(host, "Send either the agentKind/modelLevel shorthand (--kind, --level) or --candidates, not both.");
                    foreach (var (name, value) in new[] { ("--not-before", options.NotBefore), ("--not-after", options.NotAfter) })
                        if (!string.IsNullOrWhiteSpace(value) && !BoardCli.HasUtcOffset(value))
                            return BoardCli.Refuse(host, name + " must be ISO-8601 UTC with a Z or numeric offset (e.g. 2026-09-03T00:00:00Z). Naive local timestamps are refused.");
                    var body = new JsonObject { ["role"] = options.Role, ["provenance"] = options.Provenance, ["strength"] = options.Strength };
                    if (!string.IsNullOrWhiteSpace(options.Card)) body["card"] = options.Card;
                    if (options.Candidates.Length > 0)
                    {
                        var candidates = ParseCandidates(options.Candidates, out var refusal);
                        if (candidates == null) return BoardCli.Refuse(host, refusal);
                        body["candidates"] = candidates;
                    }
                    else
                    {
                        if (options.Kind.Length > 0) body["agentKind"] = options.Kind;
                        if (options.Level.Length > 0) body["modelLevel"] = options.Level;
                    }
                    if (!string.IsNullOrWhiteSpace(options.Agent)) body["agentId"] = options.Agent;
                    if (!string.IsNullOrWhiteSpace(options.Forbidden))
                        body["forbiddenAliases"] = new JsonArray(options.Forbidden.Split(',').Select(alias => alias.Trim()).Where(alias => alias.Length > 0)
                            .Select(alias => (JsonNode)JsonValue.Create(alias)).ToArray());
                    if (!string.IsNullOrWhiteSpace(options.NotBefore)) body["notBefore"] = options.NotBefore;
                    if (!string.IsNullOrWhiteSpace(options.NotAfter)) body["notAfter"] = options.NotAfter;
                    if (!string.IsNullOrWhiteSpace(options.Reason)) body["reason"] = options.Reason;
                    if (!BoardCli.Send(http, options.Api, "PUT", "/api/routing-pins", headers, OperatorApi.Json(body), out answer, out failure)) return BoardCli.Refuse(host, failure);
                    host.Out.WriteLine(options.Json ? OperatorApi.Pretty(answer) : PinLine(answer));
                    return 0;
                }
                default:
                {
                    // Addressed by grain, deleted by id: the id is what the API takes, and looking it up here keeps the caller
                    // from having to hold one.
                    if (!BoardCli.Send(http, options.Api, "GET", Query(options), headers, null, out answer, out failure)) return BoardCli.Refuse(host, failure);
                    var wantCard = !string.IsNullOrWhiteSpace(options.Card);
                    var match = BoardCli.Items(BoardCli.Get(answer, "pins")).FirstOrDefault(pin =>
                        string.Equals(BoardCli.Text(BoardCli.Get(pin, "role")), options.Role, StringComparison.OrdinalIgnoreCase)
                        && BoardCli.Truthy(BoardCli.Get(pin, "cardId")) == wantCard);
                    if (match == null)
                    {
                        host.Out.WriteLine("No active routing pin for that grain.");
                        return 0;
                    }
                    var route = "/api/routing-pins/" + Uri.EscapeDataString(BoardCli.Text(BoardCli.Get(match, "id")));
                    if (!BoardCli.Send(http, options.Api, "DELETE", route, headers, null, out _, out failure)) return BoardCli.Refuse(host, failure);
                    host.Out.WriteLine("cleared " + PinLine(match));
                    return 0;
                }
            }
        }

        // Get-PinQuery: /api/routing-pins with card= and role= when given.
        private static string Query(RoutingPinCliOptions options)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(options.Card)) parts.Add("card=" + Uri.EscapeDataString(options.Card));
            if (!string.IsNullOrWhiteSpace(options.Role)) parts.Add("role=" + Uri.EscapeDataString(options.Role));
            return "/api/routing-pins" + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
        }

        /// <summary>
        /// Parse-PinCandidates: Kind/Level pairs and bare Kind tokens, comma separated, in order (empty tokens skipped); null with
        /// the refusal for a half pair, a level-only token (*/Level) or no token at all.
        /// </summary>
        public static JsonArray ParseCandidates(string raw, out string refusal)
        {
            refusal = null;
            var pairs = new JsonArray();
            foreach (var token in (raw ?? "").Split(','))
            {
                var item = token.Trim();
                if (item.Length == 0) continue;
                var bits = item.Split(new[] { '/' }, 2);
                if (bits.Length == 2)
                {
                    if (string.IsNullOrWhiteSpace(bits[0]) || string.IsNullOrWhiteSpace(bits[1]))
                    {
                        refusal = "'" + item + "' is not a Kind/Level pair or a bare Kind. Use ClaudeCode/Frontier or Grok.";
                        return null;
                    }
                    if (bits[0].Trim() == "*")
                    {
                        refusal = "'" + item + "' is a level-only token; use --level for a one-candidate pin, not --candidates.";
                        return null;
                    }
                    pairs.Add(new JsonObject { ["agentKind"] = bits[0].Trim(), ["modelLevel"] = bits[1].Trim() });
                }
                else pairs.Add(new JsonObject { ["agentKind"] = item });
            }
            if (pairs.Count == 0)
            {
                refusal = "set --candidates needs at least one Kind/Level or bare Kind token.";
                return null;
            }
            return pairs;
        }

        /// <summary>
        /// Format-PinLine: "&lt;grain&gt;  &lt;role&gt;  &lt;provenance&gt; &lt;strength&gt;  &lt;route&gt;[  &lt;extras&gt;]  &lt;reason&gt;". The route is the head
        /// candidate and "+n: the rest", or the shorthand kind, level and (alias); then "agent &lt;id&gt;" when pinned to one.
        /// </summary>
        public static string PinLine(JsonNode pin)
        {
            string T(JsonNode node, string name) => BoardCli.Text(BoardCli.Get(node, name));
            bool Has(JsonNode node, string name) => BoardCli.Truthy(BoardCli.Get(node, name));
            var grain = Has(pin, "cardIdentifier") ? T(pin, "cardIdentifier") : Has(pin, "cardId") ? T(pin, "cardId") : "stage-wide";
            var candidates = BoardCli.Items(BoardCli.Get(pin, "candidates"));
            var route = new List<string>();
            if (candidates.Count > 0)
            {
                route.Add(CandidateToken(candidates[0]));
                if (candidates.Count > 1) route.Add("+" + (candidates.Count - 1) + ": " + string.Join(", ", candidates.Skip(1).Select(CandidateToken)));
            }
            else
            {
                if (Has(pin, "agentKind")) route.Add(T(pin, "agentKind"));
                if (Has(pin, "modelLevel")) route.Add(T(pin, "modelLevel"));
                if (Has(pin, "modelAlias")) route.Add("(" + T(pin, "modelAlias") + ")");
                if (route.Count == 0) route.Add("no kind/level constraint");
            }
            if (Has(pin, "agentId")) route.Add("agent " + T(pin, "agentId"));
            var extra = new List<string>();
            var forbidden = BoardCli.Items(BoardCli.Get(pin, "forbiddenAliases"));
            if (forbidden.Count > 0) extra.Add("forbids " + string.Join("/", forbidden.Select(BoardCli.Text)));
            if (Has(pin, "notBefore")) extra.Add("notBefore " + T(pin, "notBefore"));
            if (Has(pin, "notAfter")) extra.Add("notAfter " + T(pin, "notAfter"));
            var suffix = extra.Count > 0 ? "  " + string.Join(", ", extra) : "";
            return grain + "  " + T(pin, "role") + "  " + T(pin, "provenance") + " " + T(pin, "strength") + "  " + string.Join(" ", route) + suffix + "  " + T(pin, "reason");
        }

        private static string CandidateToken(JsonNode candidate)
        {
            var kind = BoardCli.Text(BoardCli.Get(candidate, "agentKind"));
            var level = BoardCli.Text(BoardCli.Get(candidate, "modelLevel"));
            var alias = BoardCli.Text(BoardCli.Get(candidate, "alias"));
            if (kind.Length > 0 && level.Length > 0 && alias.Length > 0) return kind + "/" + level + " (" + alias + ")";
            if (kind.Length > 0 && level.Length > 0) return kind + "/" + level;
            if (kind.Length > 0) return kind;
            if (level.Length > 0) return level;
            return "no kind/level constraint";
        }
    }
}
