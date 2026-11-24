// CARD-0330 S4: read the output-distiller ledger. It replaced scripts/distiller.ps1, deleted 2026-11-24 (unit 29 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as `antiphon-scripts distiller`
// (scripts/antiphon distiller on sh, scripts\antiphon.cmd distiller on Windows).
//
//   distiller --stats [--since <7d|time>]
//   distiller --list [--flagged] [--since <7d|time>]
//
// --stats: GET /api/distillations/stats, printed as "total=<n> cost=$<usd> fullReadRate=<rate>" (the script's format string
// lost the cost: PowerShell read "${1}" as a variable), then "byOutcome:" and "byFeedback:" with "  <name> <count>" lines when
// the answer has those objects, and "topMissing:" with "  <class>" lines when it names any. --list: GET /api/distillations, one
// line per row, "<task>  <outcome>  <raw>-><distilled>  <stamp>  flag=<feedback>" (flag=- for None or no feedback); --flagged
// keeps the LostInformation and Noisy rows (ignoring case). --since <n>d is n days before the host's clock, in UTC round-trip
// form; any other value is sent as given, trimmed (the server parses it). The reads carry no header. The CLI passes
// ANTIPHON_API (default http://localhost:17202) in. Exit 0 printed; 1 a failed request, an answer that is not JSON or a day
// count out of range (one host.Error line, "distiller failed: ..."). Usage errors are NightlyUsageException (the CLI's exit 2).
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The distiller command's options (distiller.ps1 -Stats | -List -Flagged, -Since).</summary>
    public sealed class DistillerLedgerOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "distiller --stats [--since <7d|time>] | distiller --list [--flagged] [--since <7d|time>]";

        /// <summary>True: the stats; false: the ledger's rows.</summary>
        public bool Stats { get; init; }

        /// <summary>The list keeps the LostInformation and Noisy rows only.</summary>
        public bool Flagged { get; init; }

        /// <summary>The window's start as given: "&lt;n&gt;d" or a time the server parses; empty: none.</summary>
        public string Since { get; init; } = "";

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The arguments after the command name; api comes from the CLI. Exactly one of --stats and --list; --flagged with --list only.</summary>
        public static DistillerLedgerOptions Parse(IReadOnlyList<string> arguments, string api)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--since" }, new[] { "--stats", "--list", "--flagged" });
            var stats = flags.Switch("--stats");
            if (stats == flags.Switch("--list")) throw new NightlyUsageException(stats ? "--stats and --list are exclusive" : "one of --stats or --list is required");
            if (stats && flags.Switch("--flagged")) throw new NightlyUsageException("--flagged applies to --list only");
            return new DistillerLedgerOptions
            {
                Stats = stats,
                Flagged = flags.Switch("--flagged"),
                Since = flags.Value("--since", ""),
                Api = OperatorApi.Base(api),
            };
        }
    }

    /// <summary>The output-distiller ledger read (distiller.ps1).</summary>
    public static class DistillerLedger
    {
        private static readonly Regex DayCount = new Regex("^([0-9]+)d$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Prints the stats or the rows; 0, or 1 after one host.Error line.</summary>
        public static int Run(DistillerLedgerOptions options, NightlyHost host, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (http == null) throw new ArgumentNullException(nameof(http));
            if (!Since(options.Since, host.UtcNow, out var since)) return Refuse(host, "--since '" + options.Since.Trim() + "' is out of range");
            var path = (options.Stats ? "/api/distillations/stats" : "/api/distillations") + (since == null ? "" : "?since=" + Uri.EscapeDataString(since));
            NightlyReportHttpResponse response;
            try
            {
                response = http("GET", options.Api + path, new Dictionary<string, string>(StringComparer.Ordinal), null);
            }
            catch (Exception e)
            {
                // A transport failure: no status. The exception text names the address.
                return Refuse(host, e.Message);
            }
            if (!OperatorApi.Succeeded(response)) return Refuse(host, OperatorApi.Failure(response));
            JsonNode answer;
            try
            {
                answer = string.IsNullOrWhiteSpace(response.Body) ? null : JsonNode.Parse(response.Body);
            }
            catch (JsonException e)
            {
                return Refuse(host, "GET " + path + " answered with a body that is not JSON: " + e.Message);
            }
            if (options.Stats) PrintStats(host, answer);
            else PrintRows(host, answer, options.Flagged);
            return 0;
        }

        /// <summary>
        /// The since query value: null for a blank value; for "&lt;n&gt;d" (ignoring case) n days before now in UTC round-trip
        /// form; otherwise the value trimmed. False when the day count cannot be represented.
        /// </summary>
        public static bool Since(string value, DateTime utcNow, out string since)
        {
            since = null;
            var trimmed = (value ?? "").Trim();
            if (trimmed.Length == 0) return true;
            var match = DayCount.Match(trimmed);
            if (!match.Success)
            {
                since = trimmed;
                return true;
            }
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var days)) return false;
            try
            {
                since = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).AddDays(-days).ToString("o", CultureInfo.InvariantCulture);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static void PrintStats(NightlyHost host, JsonNode stats)
        {
            host.Out.WriteLine("total=" + BoardCli.Text(BoardCli.Get(stats, "total")) + " cost=$" + BoardCli.Text(BoardCli.Get(stats, "costUsd"))
                + " fullReadRate=" + BoardCli.Text(BoardCli.Get(stats, "fullReadRate")));
            Counts(host, "byOutcome:", BoardCli.Get(stats, "byOutcome"));
            Counts(host, "byFeedback:", BoardCli.Get(stats, "byFeedback"));
            var missing = BoardCli.Get(stats, "topMissingAnchorClasses");
            if (!BoardCli.Truthy(missing)) return;
            host.Out.WriteLine("topMissing:");
            foreach (var row in BoardCli.Items(missing)) host.Out.WriteLine("  " + BoardCli.Text(row));
        }

        private static void Counts(NightlyHost host, string heading, JsonNode counts)
        {
            if (!BoardCli.Truthy(counts)) return;
            host.Out.WriteLine(heading);
            if (counts is JsonObject named)
                foreach (var pair in named) host.Out.WriteLine("  " + pair.Key + " " + BoardCli.Text(pair.Value));
        }

        private static void PrintRows(NightlyHost host, JsonNode rows, bool flagged)
        {
            foreach (var row in BoardCli.Items(rows))
            {
                var feedback = BoardCli.Get(row, "feedback");
                if (flagged && !Is(feedback, "LostInformation") && !Is(feedback, "Noisy")) continue;
                var flag = BoardCli.Truthy(feedback) && !Is(feedback, "None") ? BoardCli.Text(feedback) : "-";
                host.Out.WriteLine(BoardCli.Text(BoardCli.Get(row, "taskShortId")) + "  " + BoardCli.Text(BoardCli.Get(row, "outcome")) + "  "
                    + BoardCli.Text(BoardCli.Get(row, "rawChars")) + "->" + BoardCli.Text(BoardCli.Get(row, "distilledChars")) + "  "
                    + BoardCli.Text(BoardCli.Get(row, "bundleStamp")) + "  flag=" + flag);
            }
        }

        // PowerShell's -eq on a string: ignoring case.
        private static bool Is(JsonNode value, string expected) => string.Equals(BoardCli.Text(value), expected, StringComparison.OrdinalIgnoreCase);

        private static int Refuse(NightlyHost host, string detail)
        {
            host.Error.WriteLine("distiller failed: " + detail);
            return 1;
        }
    }
}
