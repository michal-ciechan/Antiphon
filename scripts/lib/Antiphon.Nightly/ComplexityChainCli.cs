// CARD-0090, CARD-0332, CARD-0097 S-D, CARD-1275: record the ordered (kind, level) fallback list for a Hard/Medium/Easy
// dispatch, without composing HTTP by hand. It replaced scripts/complexity-chain.ps1 (deleted 2026-11-23, unit 26 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as `antiphon-scripts complexity-chain
// [...]` (scripts/antiphon complexity-chain on sh, scripts\antiphon.cmd on Windows). A chain is read by the NEXT create
// that passes a complexity; it does not rewrite work that is already Queued (unless that work itself was chain-chosen and
// its snapshot alias can no longer run).
//
//   complexity-chain [get] [--role Plan] [--json]
//   complexity-chain set [--role Plan|Any] --complexity Hard|Medium|Easy|All --candidates ClaudeCode/Frontier,Codex/Frontier
//       [--provenance Human|Auto] [--reason r | --reason-file path] [--not-after 2026-09-05T00:00:00Z]
//       [--clear-stage-pin] (set with All and a named role only) [--json]
//   complexity-chain clear [--role Plan|Any] --complexity Hard|Medium|Easy|All
//
// All writes Hard, Medium, Easy in order and stops on the first refusal; earlier writes remain (the three requests are not
// atomic). --clear-stage-pin deletes the named role's stage pin after the three writes succeed (the routing-pin clear grain
// lookup: GET by role, the row without a card, DELETE by id).
//
// CONCURRENCY (CARD-1275). set and clear first read GET /api/complexity-chains once and send each cell's concurrencyToken
// from that read (the empty GUID when the cell has no row). A set on a cell someone else wrote, created or cleared between
// that read and the write is refused 409 complexity_chain_stale. A clear is refused 409 only when the cell still has a row
// whose token is not the one read; a clear of a cell someone else already cleared is a 204 and reports cleared. The command
// stops on a refusal like any other, and a rerun reads the new version.
//
// GRAIN. One active chain per (role?, Hard/Medium/Easy). No role, or Any, writes the any-role row. A walk on (role,
// complexity) reads the cell, then the any-role row, then the config default, then Blocked. A Required pin still bypasses
// the cell. Config defaults (Delegation:ComplexityChains) fill a tier with no row and ship EMPTY until a human sets them.
// Auto never overwrites Human (409 complexity_chain_human), and an Auto cell write is refused when the any-role row is Human.
//
// --candidates is Kind/Level pairs, comma separated, order preserved. This is not a routing-pin verb: that command is
// card+stage grain and this is neither. The command always uses the three-segment path (/any/Hard or /Plan/Hard); the
// two-segment alias exists for CARD-0090 callers. --reason-file is read as UTF-8 and sent as is. The task token
// (ANTIPHON_TASK_TOKEN) is sent when set, never printed. Exit 0 done; 1 a refused value, a missing reason file or a failed
// request (a partial All names the written, refused and unwritten tiers on stdout; anything else one stderr line); usage
// errors are NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The complexity-chain command's options (complexity-chain.ps1 &lt;verb&gt; and its flags).</summary>
    public sealed class ComplexityChainCliOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "complexity-chain [get [--role <role>] [--json] | set [--role <role>|Any] --complexity Hard|Medium|Easy|All --candidates <Kind/Level,...>"
            + " [--provenance Human|Auto] [--reason <text> | --reason-file <path>] [--not-after <utc>] [--clear-stage-pin] [--json]"
            + " | clear [--role <role>|Any] --complexity Hard|Medium|Easy|All]";

        /// <summary>The roles a cell names: every hand-delegatable AgentTaskRole and Any (the any-role row; the script's ValidateSet).</summary>
        public static readonly IReadOnlyList<string> Roles = new[]
        {
            "Investigate", "Plan", "TestDesign", "Code", "Mutation", "Review", "Debug", "Coverage", "Docs", "Commit", "Test", "Deploy", "Merge", "Custom", "Any",
        };

        /// <summary>The tiers a write takes: Hard, Medium, Easy, or All of them in that order.</summary>
        public static readonly IReadOnlyList<string> Complexities = new[] { "Hard", "Medium", "Easy", "All" };

        private static readonly string[] Verbs = { "get", "set", "clear" };
        private static readonly string[] Valued = { "--role", "--complexity", "--candidates", "--provenance", "--not-after", "--reason", "--reason-file" };
        private static readonly string[] Switches = { "--clear-stage-pin", "--json" };
        private static readonly string[] Provenances = { "Human", "Auto" };

        /// <summary>get, set or clear.</summary>
        public string Verb { get; init; } = "get";

        /// <summary>The role in its listed spelling (Any: the any-role row); empty: none.</summary>
        public string Role { get; init; } = "";

        /// <summary>set and clear: Hard, Medium, Easy or All.</summary>
        public string Complexity { get; init; } = "";

        /// <summary>set: the candidate list as given (Kind/Level pairs, comma separated, in order).</summary>
        public string Candidates { get; init; } = "";

        /// <summary>set: Human or Auto.</summary>
        public string Provenance { get; init; } = "Human";

        /// <summary>set: the cell's expiry, as given.</summary>
        public string NotAfter { get; init; } = "";

        /// <summary>set: why.</summary>
        public string Reason { get; init; } = "";

        /// <summary>set: the UTF-8 file whose text is the reason; empty: --reason.</summary>
        public string ReasonFile { get; init; } = "";

        /// <summary>set --complexity All with a named role: delete the role's stage pin after the three writes.</summary>
        public bool ClearStagePin { get; init; }

        /// <summary>get and set: print the answer as JSON.</summary>
        public bool Json { get; init; }

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>
        /// The verb (get when the first argument is a flag) and the flags; the API and the task token come from the CLI. A value
        /// outside a flag's choices, --clear-stage-pin outside set --complexity All with a named role, --reason with
        /// --reason-file, --reason-file outside set, set or clear without --complexity, or a flag the verb does not take is a
        /// NightlyUsageException.
        /// </summary>
        public static ComplexityChainCliOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken)
        {
            var verb = BoardCli.Verb(arguments, Verbs, "get", out var rest);
            var flags = NightlyArgs.Parse(rest, Valued, Switches);
            var role = BoardCli.Choice("--role", flags.Value("--role"), Roles) ?? "";
            var complexity = BoardCli.Choice("--complexity", flags.Value("--complexity"), Complexities) ?? "";
            var clearStagePin = flags.Switch("--clear-stage-pin");
            if (clearStagePin && (verb != "set" || complexity != "All" || role.Length == 0 || role == "Any"))
                throw new NightlyUsageException("--clear-stage-pin requires set --complexity All and a named --role (not Any)");
            var reason = flags.Value("--reason", "");
            var reasonFile = flags.Value("--reason-file", "");
            if (!string.IsNullOrWhiteSpace(reasonFile))
            {
                if (!string.IsNullOrWhiteSpace(reason)) throw new NightlyUsageException("--reason and --reason-file are mutually exclusive");
                if (verb != "set") throw new NightlyUsageException("--reason-file requires set");
            }
            var declared = Valued.Concat(Switches).ToArray();
            if (verb == "get") BoardCli.Only(flags, verb, declared, new[] { "--role", "--json" });
            else if (verb == "clear") BoardCli.Only(flags, verb, declared, new[] { "--role", "--complexity" });
            if (verb != "get" && complexity.Length == 0) throw new NightlyUsageException(verb + " requires --complexity Hard|Medium|Easy|All");
            return new ComplexityChainCliOptions
            {
                Verb = verb,
                Role = role,
                Complexity = complexity,
                Candidates = flags.Value("--candidates", ""),
                Provenance = BoardCli.Choice("--provenance", flags.Value("--provenance"), Provenances) ?? "Human",
                NotAfter = flags.Value("--not-after", ""),
                Reason = reason,
                ReasonFile = reasonFile,
                ClearStagePin = clearStagePin,
                Json = flags.Switch("--json"),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
            };
        }
    }

    /// <summary>The complexity chain read, write and clear (complexity-chain.ps1).</summary>
    public static class ComplexityChainCli
    {
        /// <summary>The token a write sends for a cell with no row: "expect no active row".</summary>
        public const string EmptyToken = "00000000-0000-0000-0000-000000000000";

        private static readonly string[] Tiers = { "Hard", "Medium", "Easy" };

        /// <summary>Runs the verb: the answer on host.Out; exit 0, or 1 after one line (a partial set: its status line on host.Out).</summary>
        public static int Run(ComplexityChainCliOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = BoardCli.Headers(options.TaskToken);
            switch (options.Verb)
            {
                case "get": return Get(options, host, http, headers);
                case "set": return Set(options, host, files, http, headers);
                default: return Clear(options, host, http, headers);
            }
        }

        private static int Get(ComplexityChainCliOptions options, NightlyHost host, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers)
        {
            var effective = options.Role.Length > 0 && options.Role != "Any";
            var path = effective ? "/api/complexity-chains?role=" + Uri.EscapeDataString(options.Role) : "/api/complexity-chains";
            if (!BoardCli.Send(http, options.Api, "GET", path, headers, null, out var answer, out var failure)) return BoardCli.Refuse(host, failure);
            if (options.Json)
            {
                host.Out.WriteLine(OperatorApi.Pretty(answer));
                return 0;
            }
            var chains = BoardCli.Items(BoardCli.Get(answer, "chains"));
            if (chains.Count == 0)
            {
                host.Out.WriteLine("No complexity chains.");
                return 0;
            }
            foreach (var chain in chains)
            {
                host.Out.WriteLine("  " + ChainLine(chain, effective));
                var overlay = BoardCli.Get(chain, "effective");
                if (!effective || IsNull(overlay)) continue;
                var bits = BoardCli.Items(BoardCli.Get(overlay, "candidates")).Select(CandidateText).ToList();
                var list = bits.Count > 0 ? string.Join(" -> ", bits)
                    : BoardCli.Truthy(BoardCli.Get(overlay, "withheldReason")) ? "not launchable: " + BoardCli.Text(BoardCli.Get(overlay, "withheldReason"))
                    : "(empty)";
                host.Out.WriteLine("    effective: " + BoardCli.Text(BoardCli.Get(overlay, "source")) + " " + list);
            }
            return 0;
        }

        private static int Set(ComplexityChainCliOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers)
        {
            var reason = options.Reason;
            if (!string.IsNullOrWhiteSpace(options.ReasonFile))
            {
                var path = files.Path.GetFullPath(options.ReasonFile);
                if (!files.File.Exists(path)) return BoardCli.Refuse(host, "--reason-file '" + options.ReasonFile + "' does not exist.");
                reason = files.File.ReadAllText(path, Encoding.UTF8);
            }
            if (!string.IsNullOrWhiteSpace(options.NotAfter) && !BoardCli.HasUtcOffset(options.NotAfter))
                return BoardCli.Refuse(host, "--not-after must be ISO-8601 UTC with a Z or numeric offset (e.g. 2026-09-05T00:00:00Z). Naive local timestamps are refused.");
            var candidates = ParseCandidates(options.Candidates, out var refusal);
            if (candidates == null) return BoardCli.Refuse(host, refusal);
            var body = new JsonObject { ["candidates"] = candidates, ["provenance"] = options.Provenance };
            if (!string.IsNullOrWhiteSpace(options.NotAfter)) body["notAfter"] = options.NotAfter;
            if (!string.IsNullOrWhiteSpace(reason)) body["reason"] = reason;

            var all = options.Complexity == "All";
            var tiers = all ? Tiers : new[] { options.Complexity };
            var segment = Segment(options);
            if (!Tokens(options, http, headers, segment, out var tokens, out var failure)) return BoardCli.Refuse(host, failure);
            var written = new List<string>();
            var chains = new JsonArray();
            foreach (var tier in tiers)
            {
                var request = (JsonObject)body.DeepClone();
                request["concurrencyToken"] = tokens.TryGetValue(tier, out var token) ? token : EmptyToken;
                var path = "/api/complexity-chains/" + segment + "/" + tier;
                if (!BoardCli.Send(http, options.Api, "PUT", path, headers, OperatorApi.Json(request), out var chain, out var refused))
                {
                    // One status line on stdout: what was written, the refusal, and what was not (the stage pin is kept).
                    var status = written.Select(done => done + " written").ToList();
                    status.Add(tier + " refused: " + refused);
                    status.AddRange(tiers.Skip(written.Count + 1).Select(rest => rest + " not written"));
                    if (options.ClearStagePin) status.Add("stage pin kept");
                    host.Out.WriteLine(string.Join("; ", status));
                    return 1;
                }
                written.Add(tier);
                chains.Add(chain?.DeepClone());
                if (options.Json) continue;
                if (all) host.Out.WriteLine(tier + " written");
                host.Out.WriteLine(ChainLine(chain, false));
            }

            string pinStatus = null;
            if (options.ClearStagePin)
            {
                // The routing-pin clear grain lookup and DELETE by id, reached only after the three PUTs answered 2xx.
                var query = "/api/routing-pins?role=" + Uri.EscapeDataString(options.Role);
                if (!BoardCli.Send(http, options.Api, "GET", query, headers, null, out var pins, out failure)) return BoardCli.Refuse(host, failure);
                var match = BoardCli.Items(BoardCli.Get(pins, "pins")).FirstOrDefault(pin =>
                    string.Equals(BoardCli.Text(BoardCli.Get(pin, "role")), options.Role, StringComparison.OrdinalIgnoreCase) && IsNull(BoardCli.Get(pin, "cardId")));
                if (match == null) pinStatus = "No active routing pin for that grain.";
                else
                {
                    var route = "/api/routing-pins/" + Uri.EscapeDataString(BoardCli.Text(BoardCli.Get(match, "id")));
                    if (!BoardCli.Send(http, options.Api, "DELETE", route, headers, null, out _, out failure)) return BoardCli.Refuse(host, failure);
                    pinStatus = "cleared " + options.Role + " stage pin";
                }
                if (!options.Json) host.Out.WriteLine(pinStatus);
            }
            if (options.Json)
            {
                if (options.ClearStagePin) host.Out.WriteLine(OperatorApi.Pretty(new JsonObject { ["chains"] = chains, ["stagePin"] = pinStatus }));
                else if (all) host.Out.WriteLine(OperatorApi.Pretty(chains));
                else host.Out.WriteLine(OperatorApi.Pretty(chains[0]));
            }
            return 0;
        }

        private static int Clear(ComplexityChainCliOptions options, NightlyHost host, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers)
        {
            var segment = Segment(options);
            var tiers = options.Complexity == "All" ? Tiers : new[] { options.Complexity };
            if (!Tokens(options, http, headers, segment, out var tokens, out var failure)) return BoardCli.Refuse(host, failure);
            foreach (var tier in tiers)
            {
                var token = tokens.TryGetValue(tier, out var read) ? read : EmptyToken;
                var path = "/api/complexity-chains/" + segment + "/" + tier + "?concurrencyToken=" + Uri.EscapeDataString(token);
                if (!BoardCli.Send(http, options.Api, "DELETE", path, headers, null, out _, out failure)) return BoardCli.Refuse(host, failure);
                host.Out.WriteLine("cleared " + (segment == "any" ? tier : segment + "/" + tier) + " (config default applies again)");
            }
            return 0;
        }

        // Get-RoleSegment: the any-role row for no role or Any, else the role.
        private static string Segment(ComplexityChainCliOptions options) => options.Role.Length == 0 || options.Role == "Any" ? "any" : options.Role;

        // Get-CellTokens: each tier's own token from one list read, for the any-role rows (role null) or the role's cells.
        private static bool Tokens(ComplexityChainCliOptions options, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers, string segment,
            out Dictionary<string, string> tokens, out string failure)
        {
            tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!BoardCli.Send(http, options.Api, "GET", "/api/complexity-chains", headers, null, out var list, out failure)) return false;
            foreach (var chain in BoardCli.Items(BoardCli.Get(list, "chains")))
            {
                var role = BoardCli.Text(BoardCli.Get(chain, "role"));
                if (!string.Equals(string.IsNullOrWhiteSpace(role) ? "any" : role, segment, StringComparison.OrdinalIgnoreCase)) continue;
                var token = BoardCli.Text(BoardCli.Get(chain, "concurrencyToken"));
                if (!string.IsNullOrWhiteSpace(token)) tokens[BoardCli.Text(BoardCli.Get(chain, "complexity"))] = token;
            }
            return true;
        }

        /// <summary>
        /// Parse-Candidates: Kind/Level pairs, comma separated, in order (empty tokens skipped); null with the refusal for no
        /// list, a token that is not a pair, or no pair at all.
        /// </summary>
        public static JsonArray ParseCandidates(string raw, out string refusal)
        {
            refusal = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                refusal = "set requires --candidates as Kind/Level pairs, comma-separated (e.g. ClaudeCode/Frontier,Grok/Frontier).";
                return null;
            }
            var pairs = new JsonArray();
            foreach (var token in raw.Split(','))
            {
                var item = token.Trim();
                if (item.Length == 0) continue;
                var bits = item.Split(new[] { '/' }, 2);
                if (bits.Length != 2 || string.IsNullOrWhiteSpace(bits[0]) || string.IsNullOrWhiteSpace(bits[1]))
                {
                    refusal = "'" + item + "' is not a Kind/Level pair. Use ClaudeCode/Frontier, Codex/High, Grok/Frontier.";
                    return null;
                }
                pairs.Add(new JsonObject { ["agentKind"] = bits[0].Trim(), ["modelLevel"] = bits[1].Trim() });
            }
            if (pairs.Count == 0)
            {
                refusal = "set requires at least one Kind/Level pair in --candidates.";
                return null;
            }
            return pairs;
        }

        /// <summary>
        /// Format-ChainLine. A row: "&lt;label&gt;  &lt;source&gt;/&lt;provenance|none&gt;  &lt;candidates|(empty)&gt;[  notAfter &lt;t&gt;, &lt;reason&gt;]". An
        /// effective cell (get --role): "&lt;label&gt;  (own)|(via any)|(via config)|(empty)  &lt;candidates&gt;", an empty one "(empty - Blocked
        /// until set)" when nothing resolves it. The label is role/complexity, or the complexity for the any-role row; the
        /// candidates are "Kind/Level (alias) available|&lt;reason&gt;" joined with " -&gt; ".
        /// </summary>
        public static string ChainLine(JsonNode chain, bool effective)
        {
            string T(string name) => BoardCli.Text(BoardCli.Get(chain, name));
            var label = BoardCli.Truthy(BoardCli.Get(chain, "role")) ? T("role") + "/" + T("complexity") : T("complexity");
            var bits = BoardCli.Items(BoardCli.Get(chain, "candidates")).Select(CandidateText).ToList();
            if (effective)
            {
                var from = T("resolvedFrom");
                var marker = Is(from, "role") ? "(own)" : Is(from, "any") ? "(via any)" : Is(from, "config") ? "(via config)" : "(empty)";
                var cells = bits.Count > 0 ? string.Join(" -> ", bits)
                    : Is(from, "none") || string.IsNullOrWhiteSpace(from) ? "(empty - Blocked until set)" : "(empty)";
                return label + "  " + marker + "  " + cells;
            }
            var provenance = BoardCli.Truthy(BoardCli.Get(chain, "provenance")) ? T("provenance") : "none";
            var list = bits.Count == 0 ? "(empty)" : string.Join(" -> ", bits);
            var extra = new List<string>();
            if (BoardCli.Truthy(BoardCli.Get(chain, "notAfter"))) extra.Add("notAfter " + T("notAfter"));
            if (BoardCli.Truthy(BoardCli.Get(chain, "reason"))) extra.Add(T("reason"));
            var suffix = extra.Count > 0 ? "  " + string.Join(", ", extra) : "";
            return label + "  " + T("source") + "/" + provenance + "  " + list + suffix;
        }

        // Format-Candidate: "Kind/Level (alias) available", or its unavailable reason ("unavailable" when it has none).
        private static string CandidateText(JsonNode candidate)
        {
            string T(string name) => BoardCli.Text(BoardCli.Get(candidate, name));
            var state = BoardCli.Truthy(BoardCli.Get(candidate, "availableNow")) ? "available" : T("unavailableReason");
            if (string.IsNullOrWhiteSpace(state)) state = "unavailable";
            return T("agentKind") + "/" + T("modelLevel") + " (" + T("alias") + ") " + state;
        }

        private static bool Is(string value, string expected) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

        // PowerShell's $null: a missing property or a JSON null.
        private static bool IsNull(JsonNode node) => node == null || node.GetValueKind() == JsonValueKind.Null;
    }
}
