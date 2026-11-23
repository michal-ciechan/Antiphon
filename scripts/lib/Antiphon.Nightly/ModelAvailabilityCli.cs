// CARD-0309: read the model availability holds, mark a model or kind unavailable, or clear that hold, without composing HTTP
// by hand. It replaced scripts/model-availability.ps1 (deleted 2026-11-23, unit 24 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as `antiphon-scripts model-availability [...]`
// (scripts/antiphon model-availability on sh, scripts\antiphon.cmd on Windows).
//
//   model-availability [get] [--json]
//   model-availability hold  --kind ClaudeCode --model fable [--until 2026-09-04T00:00:00Z] [--reason r]
//   model-availability hold  --kind ClaudeCode --model * --until 2026-09-04T00:00:00Z
//   model-availability clear --kind ClaudeCode --model fable
//
// --until is ISO-8601 with a Z or a numeric offset; a naive local timestamp is refused before any request. Without --until
// the hold is open-ended (until cleared). Model * is a kind-wide hold. The task token (ANTIPHON_TASK_TOKEN) is sent when set,
// never printed. Exit 0 done; 1 a refused value or a failed request (one stderr line); usage errors are
// NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The model-availability command's options (model-availability.ps1 &lt;verb&gt; -Kind -Model -Until -Reason -Json).</summary>
    public sealed class ModelAvailabilityCliOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "model-availability [get [--json] | hold --kind <ClaudeCode|Grok|Codex> --model <alias|*> [--until <utc>] [--reason <text>]"
            + " | clear --kind <ClaudeCode|Grok|Codex> --model <alias|*>]";

        /// <summary>The kinds a hold names.</summary>
        public static readonly IReadOnlyList<string> Kinds = new[] { "ClaudeCode", "Grok", "Codex" };

        private static readonly string[] Verbs = { "get", "hold", "clear" };
        private static readonly string[] Valued = { "--kind", "--model", "--until", "--reason" };
        private static readonly string[] Switches = { "--json" };

        /// <summary>get, hold or clear.</summary>
        public string Verb { get; init; } = "get";

        /// <summary>hold and clear: the kind, in its listed spelling.</summary>
        public string Kind { get; init; } = "";

        /// <summary>hold and clear: the model alias, or * for the whole kind.</summary>
        public string Model { get; init; } = "";

        /// <summary>hold: until when, as given; empty: until cleared.</summary>
        public string Until { get; init; } = "";

        /// <summary>hold: why; empty: "manual hold" when --until is empty too.</summary>
        public string Reason { get; init; } = "";

        /// <summary>get: print the answer as JSON.</summary>
        public bool Json { get; init; }

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>
        /// The verb (get when the first argument is a flag) and the flags; the API and the task token come from the CLI. hold or
        /// clear without --kind and --model, a kind outside the three, or a flag the verb does not take is a NightlyUsageException.
        /// </summary>
        public static ModelAvailabilityCliOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken)
        {
            var verb = BoardCli.Verb(arguments, Verbs, "get", out var rest);
            var flags = NightlyArgs.Parse(rest, Valued, Switches);
            var declared = new[] { "--kind", "--model", "--until", "--reason", "--json" };
            if (verb == "get") BoardCli.Only(flags, verb, declared, new[] { "--json" });
            else if (verb == "hold") BoardCli.Only(flags, verb, declared, Valued);
            else BoardCli.Only(flags, verb, declared, new[] { "--kind", "--model" });
            var kind = BoardCli.Choice("--kind", flags.Value("--kind"), Kinds);
            if (verb != "get" && (kind == null || string.IsNullOrWhiteSpace(flags.Value("--model")))) throw new NightlyUsageException(verb + " requires --kind and --model");
            return new ModelAvailabilityCliOptions
            {
                Verb = verb,
                Kind = kind ?? "",
                Model = flags.Value("--model", ""),
                Until = flags.Value("--until", ""),
                Reason = flags.Value("--reason", ""),
                Json = flags.Switch("--json"),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
            };
        }
    }

    /// <summary>The model availability read, hold and clear (model-availability.ps1).</summary>
    public static class ModelAvailabilityCli
    {
        /// <summary>Runs the verb: the answer on host.Out; exit 0, or 1 after one line on host.Error.</summary>
        public static int Run(ModelAvailabilityCliOptions options, NightlyHost host, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = BoardCli.Headers(options.TaskToken);
            JsonNode answer;
            string failure;
            if (options.Verb == "get")
            {
                if (!BoardCli.Send(http, options.Api, "GET", "/api/model-availability", headers, null, out answer, out failure)) return BoardCli.Refuse(host, failure);
                if (options.Json)
                {
                    host.Out.WriteLine(OperatorApi.Pretty(answer));
                    return 0;
                }
                var holds = BoardCli.Items(BoardCli.Get(answer, "holds"));
                if (holds.Count == 0) host.Out.WriteLine("All models available.");
                else
                {
                    host.Out.WriteLine("holds:");
                    foreach (var hold in holds) host.Out.WriteLine("  " + HoldLine(hold));
                }
                host.Out.WriteLine("available: " + BoardCli.Join(BoardCli.Get(answer, "available")));
                return 0;
            }

            var route = "/api/model-availability/" + options.Kind + "/" + Uri.EscapeDataString(options.Model);
            if (options.Verb == "clear")
            {
                if (!BoardCli.Send(http, options.Api, "DELETE", route, headers, null, out _, out failure)) return BoardCli.Refuse(host, failure);
                return 0;
            }

            if (!string.IsNullOrWhiteSpace(options.Until) && !BoardCli.HasUtcOffset(options.Until))
                return BoardCli.Refuse(host, "--until must be ISO-8601 UTC with a Z or numeric offset (e.g. 2026-09-04T00:00:00Z). Naive local timestamps are refused.");
            var body = new JsonObject();
            if (!string.IsNullOrWhiteSpace(options.Until)) body["disabledUntil"] = options.Until;
            if (!string.IsNullOrWhiteSpace(options.Reason)) body["reason"] = options.Reason;
            if (body.Count == 0) body["reason"] = "manual hold";
            if (!BoardCli.Send(http, options.Api, "PUT", route, headers, OperatorApi.Json(body), out answer, out failure)) return BoardCli.Refuse(host, failure);
            host.Out.WriteLine(HoldLine(answer));
            return 0;
        }

        /// <summary>"&lt;kind&gt;  &lt;alias&gt;  &lt;source&gt;  until &lt;when&gt;|until cleared  &lt;reason&gt;" (Format-HoldLine; the time as the server wrote it).</summary>
        public static string HoldLine(JsonNode hold)
        {
            var until = BoardCli.Text(BoardCli.Get(hold, "disabledUntil"));
            return BoardCli.Text(BoardCli.Get(hold, "kind")) + "  " + BoardCli.Text(BoardCli.Get(hold, "modelAlias")) + "  " + BoardCli.Text(BoardCli.Get(hold, "source")) + "  "
                + (string.IsNullOrWhiteSpace(until) ? "until cleared" : "until " + until) + "  " + BoardCli.Text(BoardCli.Get(hold, "reason"));
        }
    }
}
