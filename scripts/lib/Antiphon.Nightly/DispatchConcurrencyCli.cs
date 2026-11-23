// CARD-0505: read and replace the durable dispatch-concurrency policy without composing HTTP by hand. It replaced
// scripts/dispatch-concurrency.ps1 (deleted 2026-11-23, unit 24 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md);
// the CLI runs it as `antiphon-scripts dispatch-concurrency [...]` (scripts/antiphon dispatch-concurrency on sh).
//
//   dispatch-concurrency [get] [--project <guid>]
//   dispatch-concurrency history [--project <guid>] [--before-revision n] [--limit n]
//   dispatch-concurrency set [--project <guid>] (--settings-file path | --settings '{...}') (--reason text | --reason-file path)
//       [--provenance Human|Auto] [--expected-revision n] [--expected-global-revision n]
//   dispatch-concurrency clear [--project <guid>] (--reason text | --reason-file path) [--provenance Human|Auto]
//       [--expected-revision n] [--expected-global-revision n]
//
// set and clear GET the current revisions, then send one PUT. An explicit revision is sent as given and never replaced by the
// GET's value. The settings document is sent verbatim as the overrides; clear sends an empty overrides object, not a copy of
// the current policy. A 409 is shown and not retried. Answers are printed as compact JSON. The task token
// (ANTIPHON_TASK_TOKEN) is a header only, never printed. Exit 0 done; 1 a refused value (a project that is not a GUID, a
// settings document that is not JSON, a missing file, a revision that is not an integer) or a failed request (one stderr
// line); usage errors are NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The dispatch-concurrency command's options (dispatch-concurrency.ps1 &lt;verb&gt; and its flags).</summary>
    public sealed class DispatchConcurrencyCliOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "dispatch-concurrency [get [--project <guid>] | history [--project <guid>] [--before-revision n] [--limit n]"
            + " | set [--project <guid>] (--settings-file <json> | --settings <json>) (--reason <text> | --reason-file <file>) [--provenance Human|Auto]"
            + " [--expected-revision n] [--expected-global-revision n] | clear [--project <guid>] (--reason <text> | --reason-file <file>) [--provenance Human|Auto]"
            + " [--expected-revision n] [--expected-global-revision n]]";

        private static readonly string[] Verbs = { "get", "history", "set", "clear" };
        private static readonly string[] Valued =
        {
            "--project", "--settings-file", "--settings", "--reason", "--reason-file", "--provenance", "--expected-revision", "--expected-global-revision",
            "--before-revision", "--limit",
        };
        private static readonly string[] Provenances = { "Human", "Auto" };

        /// <summary>get, history, set or clear.</summary>
        public string Verb { get; init; } = "get";

        /// <summary>The project scope as given; empty: the global scope.</summary>
        public string Project { get; init; } = "";

        /// <summary>set: the settings file.</summary>
        public string SettingsFile { get; init; } = "";

        /// <summary>set: the settings given inline.</summary>
        public string Settings { get; init; } = "";

        /// <summary>set and clear: the reason given inline.</summary>
        public string Reason { get; init; } = "";

        /// <summary>set and clear: the reason file.</summary>
        public string ReasonFile { get; init; } = "";

        /// <summary>set and clear: Human or Auto.</summary>
        public string Provenance { get; init; } = "Human";

        /// <summary>set and clear: the expected revision as given; empty: the current one.</summary>
        public string ExpectedRevision { get; init; } = "";

        /// <summary>set and clear with --project: the expected global revision as given; empty: the current one.</summary>
        public string ExpectedGlobalRevision { get; init; } = "";

        /// <summary>history: the page's upper bound.</summary>
        public string BeforeRevision { get; init; } = "";

        /// <summary>history: the page size.</summary>
        public string Limit { get; init; } = "";

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>
        /// The verb (get when the first argument is a flag) and the flags; the API and the task token come from the CLI. The
        /// script's combination refusals are NightlyUsageException, in its order: --settings with --settings-file, --reason
        /// with --reason-file, a write argument on a read, set without exactly one settings source, settings on clear, a write
        /// without exactly one reason, --before-revision or --limit outside history, --expected-global-revision without
        /// --project; and a provenance outside Human and Auto.
        /// </summary>
        public static DispatchConcurrencyCliOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken)
        {
            var verb = BoardCli.Verb(arguments, Verbs, "get", out var rest);
            var flags = NightlyArgs.Parse(rest, Valued, Array.Empty<string>());
            bool Given(string flag) => !string.IsNullOrWhiteSpace(flags.Value(flag));
            var hasSettings = Given("--settings");
            var hasSettingsFile = Given("--settings-file");
            var hasReason = Given("--reason");
            var hasReasonFile = Given("--reason-file");
            var isWrite = verb == "set" || verb == "clear";
            if (hasSettings && hasSettingsFile) throw new NightlyUsageException("--settings and --settings-file are mutually exclusive");
            if (hasReason && hasReasonFile) throw new NightlyUsageException("--reason and --reason-file are mutually exclusive");
            if (!isWrite && (hasSettings || hasSettingsFile || hasReason || hasReasonFile || Given("--expected-revision") || Given("--expected-global-revision")
                    || flags.Has("--provenance")))
                throw new NightlyUsageException(verb + " does not accept settings, reason, provenance, or revision arguments");
            if (verb == "set" && !(hasSettings ^ hasSettingsFile)) throw new NightlyUsageException("set needs --settings or --settings-file, and not both");
            if (verb == "clear" && (hasSettings || hasSettingsFile)) throw new NightlyUsageException("clear does not take a settings document; it sends an empty overrides object");
            if (isWrite && !(hasReason ^ hasReasonFile)) throw new NightlyUsageException(verb + " needs --reason or --reason-file, and not both");
            if (verb != "history" && (Given("--before-revision") || Given("--limit"))) throw new NightlyUsageException("--before-revision and --limit belong to history");
            if (Given("--expected-global-revision") && !Given("--project")) throw new NightlyUsageException("--expected-global-revision is only valid with --project");
            return new DispatchConcurrencyCliOptions
            {
                Verb = verb,
                Project = flags.Value("--project", ""),
                SettingsFile = flags.Value("--settings-file", ""),
                Settings = flags.Value("--settings", ""),
                Reason = flags.Value("--reason", ""),
                ReasonFile = flags.Value("--reason-file", ""),
                Provenance = BoardCli.Choice("--provenance", flags.Value("--provenance"), Provenances) ?? "Human",
                ExpectedRevision = flags.Value("--expected-revision", ""),
                ExpectedGlobalRevision = flags.Value("--expected-global-revision", ""),
                BeforeRevision = flags.Value("--before-revision", ""),
                Limit = flags.Value("--limit", ""),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
            };
        }
    }

    /// <summary>The dispatch-concurrency read, history and replace (dispatch-concurrency.ps1).</summary>
    public static class DispatchConcurrencyCli
    {
        /// <summary>Runs the verb: the answer on host.Out as compact JSON; exit 0, or 1 after one line on host.Error.</summary>
        public static int Run(DispatchConcurrencyCliOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var hasProject = !string.IsNullOrWhiteSpace(options.Project);
            if (hasProject && !Guid.TryParse(options.Project, out _)) return BoardCli.Refuse(host, "project must be a GUID.");

            string overrides = null;
            if (options.Verb == "set")
            {
                if (!string.IsNullOrWhiteSpace(options.SettingsFile))
                {
                    overrides = ReadFile(files, options.SettingsFile, "settings file", out var missing);
                    if (overrides == null) return BoardCli.Refuse(host, missing);
                }
                else overrides = options.Settings;
                try
                {
                    JsonDocument.Parse(overrides).Dispose();
                }
                catch (JsonException)
                {
                    return BoardCli.Refuse(host, "settings are not valid JSON.");
                }
            }
            else if (options.Verb == "clear") overrides = "{}";

            string reason = null;
            if (options.Verb == "set" || options.Verb == "clear")
            {
                if (!string.IsNullOrWhiteSpace(options.ReasonFile))
                {
                    reason = ReadFile(files, options.ReasonFile, "reason file", out var missing);
                    if (reason == null) return BoardCli.Refuse(host, missing);
                }
                else reason = options.Reason;
            }

            long explicitRevision = 0, explicitGlobal = 0;
            var hasRevision = !string.IsNullOrWhiteSpace(options.ExpectedRevision);
            var hasGlobal = !string.IsNullOrWhiteSpace(options.ExpectedGlobalRevision);
            if (hasRevision && !long.TryParse(options.ExpectedRevision, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out explicitRevision))
                return BoardCli.Refuse(host, "expectedRevision must be an integer.");
            if (hasGlobal && !long.TryParse(options.ExpectedGlobalRevision, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out explicitGlobal))
                return BoardCli.Refuse(host, "expectedGlobalRevision must be an integer.");

            var headers = BoardCli.Headers(options.TaskToken);
            var scope = hasProject ? "/api/projects/" + options.Project + "/dispatch-concurrency" : "/api/dispatch-concurrency";
            JsonNode answer;
            string failure;
            if (options.Verb == "get" || options.Verb == "history")
            {
                var path = scope;
                if (options.Verb == "history")
                {
                    var query = new List<string>();
                    if (!string.IsNullOrWhiteSpace(options.Limit)) query.Add("limit=" + Uri.EscapeDataString(options.Limit));
                    if (!string.IsNullOrWhiteSpace(options.BeforeRevision)) query.Add("beforeRevision=" + Uri.EscapeDataString(options.BeforeRevision));
                    path = scope + "/revisions" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
                }
                if (!BoardCli.Send(http, options.Api, "GET", path, headers, null, out answer, out failure)) return BoardCli.Refuse(host, failure);
                host.Out.WriteLine(OperatorApi.Json(answer));
                return 0;
            }

            if (!BoardCli.Send(http, options.Api, "GET", scope, headers, null, out var current, out failure)) return BoardCli.Refuse(host, failure);
            long revision;
            if (hasRevision) revision = explicitRevision;
            else if (!TryLong(BoardCli.Get(current, "revision"), out revision)) return BoardCli.Refuse(host, "The current policy has no integer revision; pass --expected-revision.");
            long? global = null;
            if (hasProject)
            {
                if (hasGlobal) global = explicitGlobal;
                else if (TryLong(BoardCli.Get(current, "globalRevision"), out var read)) global = read;
                else return BoardCli.Refuse(host, "The current project policy has no integer globalRevision; pass --expected-global-revision.");
            }
            var body = new StringBuilder("{\"expectedRevision\":").Append(revision.ToString(CultureInfo.InvariantCulture));
            if (global.HasValue) body.Append(",\"expectedGlobalRevision\":").Append(global.Value.ToString(CultureInfo.InvariantCulture));
            body.Append(",\"overrides\":").Append(overrides.Trim())
                .Append(",\"reason\":").Append(OperatorApi.Json(JsonValue.Create(reason)))
                .Append(",\"provenance\":").Append(OperatorApi.Json(JsonValue.Create(options.Provenance)))
                .Append('}');
            if (!BoardCli.Send(http, options.Api, "PUT", scope, headers, body.ToString(), out answer, out failure)) return BoardCli.Refuse(host, failure);
            host.Out.WriteLine(OperatorApi.Json(answer));
            return 0;
        }

        // Read-Utf8File: the whole file as UTF-8 (a BOM is dropped); null and "<label> was not found: <path>" when it is missing.
        private static string ReadFile(IFileSystem files, string path, string label, out string missing)
        {
            missing = null;
            if (files.File.Exists(path)) return files.File.ReadAllText(path, new UTF8Encoding(false));
            missing = label + " was not found: " + path;
            return null;
        }

        private static bool TryLong(JsonNode node, out long value)
        {
            value = 0;
            return node is JsonValue number && number.GetValueKind() == JsonValueKind.Number && number.TryGetValue(out value);
        }
    }
}
