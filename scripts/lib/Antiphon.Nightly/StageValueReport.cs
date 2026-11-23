// CARD-0272 S4: the per-stage hit rate against cost. It replaced scripts/stage-value-report.ps1 and its driver
// scripts/test-stage-value-report.ps1 (unit 25 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI
// runs it as `antiphon-scripts stage-value-report` (scripts/antiphon on sh, scripts\antiphon.cmd on Windows).
//
//   stage-value-report [--since <iso>] [--until <iso>] [--stage Rebase|Verify|Cleanup|Review|FollowUp|Deploy]
//                      [--card <ref>] [--json] [--include-superseded] [--api <url>]
//
// GET /api/stage-outcomes with since, until, stage, cardId and (with --include-superseded) latestOnly=false. --card takes a
// card in any written form (CARD-0272, card-272, #272, 272 or its GUID) and resolves it first with GET
// /api/cards/{ref}?cwd=<checkout>, the checkout being git's top level of the working directory (else the working
// directory), as the card command resolves it. --stage matches ignoring case and is sent in its listed spelling. The task token
// (ANTIPHON_TASK_TOKEN) is sent as X-Antiphon-Task-Token when set; the CLI passes it and ANTIPHON_API in, and --api
// overrides the API. Without --json the answer's summary prints as a table (Stage, Runs, Found, Clean, Skipped, Failed,
// Unreported, Hit%, UsdSpent, Usd/Finding, ServerSecs; a hit percentage as the server wrote it, USD as $N2 in the invariant
// culture, '-' for null), or "No stage outcomes in range."; with --json the whole answer prints as indented JSON. Exit 0;
// 1 a failed request (one stderr line "Antiphon GET <path> failed: ..."). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The stage-value-report command's options (stage-value-report.ps1 -Since -Until -Stage -Card -Json -IncludeSuperseded -Api).</summary>
    public sealed class StageValueReportOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "stage-value-report [--since <iso>] [--until <iso>] [--stage <Rebase|Verify|Cleanup|Review|FollowUp|Deploy>] [--card <ref>] [--json]"
            + " [--include-superseded] [--api <url>]";

        /// <summary>The stages, in their listed spelling.</summary>
        public static readonly IReadOnlyList<string> Stages = new[] { "Rebase", "Verify", "Cleanup", "Review", "FollowUp", "Deploy" };

        /// <summary>The lower bound on RecordedAt, as given; empty: none.</summary>
        public string Since { get; init; } = "";

        /// <summary>The upper bound on RecordedAt, as given; empty: none.</summary>
        public string Until { get; init; } = "";

        /// <summary>One stage in its listed spelling; empty: every stage.</summary>
        public string Stage { get; init; } = "";

        /// <summary>A card in any written form; empty: every card.</summary>
        public string Card { get; init; } = "";

        /// <summary>Print the whole answer as JSON instead of the table.</summary>
        public bool Json { get; init; }

        /// <summary>Every row, not only the latest per task and stage (latestOnly=false).</summary>
        public bool IncludeSuperseded { get; init; }

        /// <summary>The API base, without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>The directory a card is resolved from (the CLI's working directory).</summary>
        public string WorkingDirectory { get; init; } = "";

        /// <summary>The arguments after the command name; api, taskToken and workingDirectory come from the CLI, and --api overrides api.</summary>
        public static StageValueReportOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken, string workingDirectory)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--since", "--until", "--stage", "--card", "--api" }, new[] { "--json", "--include-superseded" });
            var explicitApi = flags.Value("--api", "");
            return new StageValueReportOptions
            {
                Since = flags.Value("--since", ""),
                Until = flags.Value("--until", ""),
                Stage = BoardCli.Choice("--stage", flags.Value("--stage", ""), Stages) ?? "",
                Card = flags.Value("--card", ""),
                Json = flags.Switch("--json"),
                IncludeSuperseded = flags.Switch("--include-superseded"),
                Api = OperatorApi.Base(explicitApi.Length > 0 ? explicitApi : api),
                TaskToken = taskToken ?? "",
                WorkingDirectory = workingDirectory ?? "",
            };
        }
    }

    /// <summary>The stage value report (stage-value-report.ps1).</summary>
    public static class StageValueReport
    {
        private static readonly string[] Columns = { "Stage", "Runs", "Found", "Clean", "Skipped", "Failed", "Unreported", "Hit%", "UsdSpent", "Usd/Finding", "ServerSecs" };

        /// <summary>Reads the outcomes and prints the table or the JSON on host.Out; 0, or 1 after one host.Error line.</summary>
        public static int Run(StageValueReportOptions options, NightlyHost host, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = BoardCli.Headers(options.TaskToken);
            var cardId = "";
            if (!string.IsNullOrWhiteSpace(options.Card))
            {
                var path = "/api/cards/" + Uri.EscapeDataString(options.Card.Trim()) + "?cwd=" + Uri.EscapeDataString(CheckoutRoot(host, options.WorkingDirectory));
                if (!BoardCli.Send(http, options.Api, "GET", path, headers, null, out var card, out var failure)) return BoardCli.Refuse(host, failure);
                cardId = BoardCli.Text(BoardCli.Get(card, "id"));
            }
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(options.Since)) query.Add("since=" + Uri.EscapeDataString(options.Since));
            if (!string.IsNullOrWhiteSpace(options.Until)) query.Add("until=" + Uri.EscapeDataString(options.Until));
            if (!string.IsNullOrWhiteSpace(options.Stage)) query.Add("stage=" + Uri.EscapeDataString(options.Stage));
            if (!string.IsNullOrWhiteSpace(cardId)) query.Add("cardId=" + Uri.EscapeDataString(cardId));
            if (options.IncludeSuperseded) query.Add("latestOnly=false");
            var outcomes = "/api/stage-outcomes" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
            if (!BoardCli.Send(http, options.Api, "GET", outcomes, headers, null, out var result, out var refused)) return BoardCli.Refuse(host, refused);

            if (options.Json)
            {
                host.Out.WriteLine(OperatorApi.Pretty(result));
                return 0;
            }
            var summary = BoardCli.Items(BoardCli.Get(result, "summary"));
            if (summary.Count == 0)
            {
                host.Out.WriteLine("No stage outcomes in range.");
                return 0;
            }
            foreach (var line in Table(summary)) host.Out.WriteLine(line);
            return 0;
        }

        /// <summary>The summary rows as the table's lines: the header, the rule, then one line per row; columns two spaces apart, each padded to its widest cell.</summary>
        public static IReadOnlyList<string> Table(IReadOnlyList<JsonNode> summary)
        {
            var rows = summary.Select(row => new[]
            {
                BoardCli.Text(BoardCli.Get(row, "stage")),
                Whole(BoardCli.Get(row, "runs")),
                Whole(BoardCli.Get(row, "found")),
                Whole(BoardCli.Get(row, "clean")),
                Whole(BoardCli.Get(row, "skipped")),
                Whole(BoardCli.Get(row, "failed")),
                Whole(BoardCli.Get(row, "unreported")),
                Percent(BoardCli.Get(row, "hitPercent")),
                Usd(BoardCli.Get(row, "usdSpent")),
                Usd(BoardCli.Get(row, "usdPerFinding")),
                Whole(BoardCli.Get(row, "serverSecs")),
            }).ToList();
            var widths = Columns.Select((column, index) => Math.Max(column.Length, rows.Count == 0 ? 0 : rows.Max(row => row[index].Length))).ToArray();
            string Line(IReadOnlyList<string> cells) => string.Join("  ", cells.Select((cell, index) => cell.PadRight(widths[index])));
            var lines = new List<string> { Line(Columns), Line(widths.Select(width => new string('-', width)).ToArray()) };
            lines.AddRange(rows.Select(row => Line(row)));
            return lines;
        }

        /// <summary>A percentage as the server wrote it, then "%"; "-" for null.</summary>
        public static string Percent(JsonNode value) => IsNull(value) ? "-" : BoardCli.Text(value) + "%";

        /// <summary>A USD amount as "$" and the invariant culture's N2 ("$1,234.50"); "-" for null.</summary>
        public static string Usd(JsonNode value) => IsNull(value) ? "-" : "$" + Number(value).ToString("N2", CultureInfo.InvariantCulture);

        // [int]: a number rounded half to even; 0 for null.
        private static string Whole(JsonNode value) =>
            IsNull(value) ? "0" : Math.Round(Number(value), MidpointRounding.ToEven).ToString("0", CultureInfo.InvariantCulture);

        private static decimal Number(JsonNode value) => value.GetValueKind() == JsonValueKind.String
            ? decimal.Parse(value.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture)
            : value.GetValue<decimal>();

        private static bool IsNull(JsonNode value) => value == null || value.GetValueKind() == JsonValueKind.Null;

        // git's top level of the working directory, else the working directory itself (outside a repository, or no git).
        private static string CheckoutRoot(NightlyHost host, string workingDirectory)
        {
            try
            {
                var result = host.RunGit(workingDirectory, "rev-parse", "--show-toplevel");
                var top = result.Output.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
                if (result.ExitCode == 0 && !string.IsNullOrEmpty(top)) return top;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
                // git is missing: the working directory is the checkout, as in the script.
            }
            return workingDirectory;
        }
    }
}
