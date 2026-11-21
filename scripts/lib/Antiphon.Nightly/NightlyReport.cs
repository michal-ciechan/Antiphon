// W2-nightly S06: the nightly run card (CARD-0124; lanes CARD-0599 R-4), ported from scripts/nightly-report.ps1 (plan
// 2026-11-21-w2-nightly-port-slices.md). `antiphon-scripts nightly-report` runs NightlyReport.Run with
// NightlyReportSeams.Production; the run core's report hop (scripts/lib/nightly-run-impl.ps1) starts it through
// scripts/lib/antiphon-cli.ps1. NightlyReportTests build the seams directly.
//
// It reads summary.json (and an optional previous summary for the new/still/fixed delta), composes the run section and
// title, and applies the one-open-card-per-lane rule: red creates, updates or reopens the lane's card; green auto-closes
// only an unassigned Backlog card of an eligible run and otherwise comments. All HTTP goes through the NightlyReportHttp
// seam with no headers (no token is ever sent). One failed call is retried once, after 60 s on the host clock; a second
// failure (or any failure after the board read began) writes card.md next to the summary and exits 3.
//
// The summary and the API responses are read the way the script's pwsh 7 ConvertFrom-Json / Invoke-RestMethod read them:
// property names match case-insensitively, [string] and [bool] are PowerShell's conversions, -eq on text ignores case,
// an ISO date-time string is a date, and @($x) of a missing value is one null element. Exit codes: 0 done (including
// dry-run and nothing to file), 1 the summary is missing or cannot be read or composed, 3 the board could not be read or
// written. Never prints the API base or any credential. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>One HTTP answer: the status code and the body text (empty when there is none).</summary>
    public sealed record NightlyReportHttpResponse(int Status, string Body);

    /// <summary>
    /// The report's HTTP surface (the script's -HttpShim, param($Method, $Uri, $Headers, $Body)): one request, its answer.
    /// A transport failure throws. headers is always empty; body is null for a GET.
    /// </summary>
    public delegate NightlyReportHttpResponse NightlyReportHttp(string method, string uri, IReadOnlyDictionary<string, string> headers, string body);

    /// <summary>The report's parameters (nightly-report.ps1 -Summary -PreviousSummary -DryRun -Board -Api).</summary>
    public sealed class NightlyReportOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "nightly-report --summary <summary.json> [--previous-summary <summary.json>] [--dry-run] [--board <name|guid>] [--api <url>]";

        private static readonly string[] Valued = { "--summary", "--previous-summary", "--board", "--api" };
        private static readonly string[] Switches = { "--dry-run" };

        /// <summary>This run's summary.json.</summary>
        public string Summary { get; init; } = "";

        /// <summary>The previous run's summary.json for the failure delta; empty for none.</summary>
        public string PreviousSummary { get; init; } = "";

        /// <summary>Print the writes that would be sent and write card.md; no HTTP writes.</summary>
        public bool DryRun { get; init; }

        /// <summary>The board name or id.</summary>
        public string Board { get; init; } = "Antiphon";

        /// <summary>The API base; empty means ANTIPHON_API, else http://localhost:17202.</summary>
        public string Api { get; init; } = "";

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line or a missing --summary.</summary>
        public static NightlyReportOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            return new NightlyReportOptions
            {
                Summary = flags.Required("--summary"),
                PreviousSummary = flags.Value("--previous-summary", ""),
                DryRun = flags.Switch("--dry-run"),
                Board = flags.Value("--board", "Antiphon"),
                Api = flags.Value("--api", ""),
            };
        }
    }

    /// <summary>
    /// The observations the report makes. Seams replace observation only, never decisions, and never cross a process: the
    /// CLI fills Production, tests build one directly.
    /// </summary>
    public sealed class NightlyReportSeams
    {
        /// <summary>The host clock: the run's now and the retry wait. TimeProvider.System in production.</summary>
        public required TimeProvider Clock { get; init; }

        /// <summary>The HTTP transport. Production: NightlyReport.HttpTransport().</summary>
        public required NightlyReportHttp Http { get; init; }

        /// <summary>Environment variables (ANTIPHON_API): null when unset.</summary>
        public required Func<string, string> Environment { get; init; }

        /// <summary>The report's lines (the script's Write-Host).</summary>
        public required TextWriter Out { get; init; }

        /// <summary>Where a summary that cannot be read or composed is reported.</summary>
        public required TextWriter Error { get; init; }

        /// <summary>The production seams: the host's clock and writers, the HTTP transport and the process environment.</summary>
        public static NightlyReportSeams Production(NightlyHost host, Func<string, string> environment)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            return new NightlyReportSeams { Clock = host.Clock, Http = NightlyReport.HttpTransport(), Environment = environment, Out = host.Out, Error = host.Error };
        }
    }

    /// <summary>A write the report sent, or in a dry run would have sent.</summary>
    public sealed record NightlyReportWrite(string Method, string Uri, string Body);

    /// <summary>The report's outcome (the script's -PassThru record).</summary>
    public sealed class NightlyReportResult
    {
        internal readonly List<NightlyReportWrite> WriteList = new List<NightlyReportWrite>();

        /// <summary>0 done, 1 the summary could not be read or composed, 3 the board could not be read or written.</summary>
        public int ExitCode { get; internal set; }

        /// <summary>none, closed, discussion, created, updated, reopened, dry-run or reporting-failed.</summary>
        public string Action { get; internal set; } = "none";

        /// <summary>The card title.</summary>
        public string Title { get; internal set; } = "";

        /// <summary>The card body (the new run section, then the card's earlier description when one is updated).</summary>
        public string Body { get; internal set; } = "";

        /// <summary>card.md next to the summary (logDir): written on a dry run and when the board cannot be reached.</summary>
        public string CardMdPath { get; internal set; }

        /// <summary>The writes in order, sent or (dry run) intended.</summary>
        public IReadOnlyList<NightlyReportWrite> Writes => WriteList;
    }

    /// <summary>A board call that failed: a non-success status or no answer.</summary>
    public sealed class NightlyReportApiException : Exception
    {
        /// <summary>A failed call with its reason.</summary>
        public NightlyReportApiException(string message) : base(message) { }
    }

    /// <summary>A summary value the script's casts reject ([int], [double]).</summary>
    public sealed class NightlyReportValueException : Exception
    {
        /// <summary>A rejected value with its reason.</summary>
        public NightlyReportValueException(string message) : base(message) { }
    }

    /// <summary>The report (scripts/nightly-report.ps1).</summary>
    public static class NightlyReport
    {
        /// <summary>The exit code when the board could not be read or written (card.md holds the body).</summary>
        public const int ApiFailed = 3;

        /// <summary>The exit code when the summary is missing or cannot be read or composed.</summary>
        public const int SummaryFailed = 1;

        /// <summary>The one retry waits this long on the host clock.</summary>
        public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(60);

        /// <summary>Each production request's bound (Invoke-RestMethod's HttpClient default).</summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(100);

        /// <summary>The API base when neither --api nor ANTIPHON_API names one.</summary>
        public const string DefaultApi = "http://localhost:17202";

        internal const int MaxTitle = 300;
        internal const int MaxDescription = 20000;

        /// <summary>Runs the report. Never throws for a bad summary or a dead board: the result carries the exit code.</summary>
        public static NightlyReportResult Run(NightlyReportOptions options, NightlyReportSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            return new ReportRun(options, seams).Execute();
        }

        /// <summary>
        /// The production transport (Invoke-RestMethod without a token): one HttpClient, the request's own method and URI, a
        /// non-empty body sent as UTF-8 'application/json; charset=utf-8', the answer's status and text. handler replaces
        /// the network (tests).
        /// </summary>
        public static NightlyReportHttp HttpTransport(HttpMessageHandler handler = null)
        {
            var client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            client.Timeout = RequestTimeout;
            return (method, uri, headers, body) =>
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), uri);
                foreach (var header in headers ?? new Dictionary<string, string>()) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                if (!string.IsNullOrEmpty(body))
                {
                    request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
                    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");
                }
                using var response = client.SendAsync(request).GetAwaiter().GetResult();
                var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return new NightlyReportHttpResponse((int)response.StatusCode, text ?? "");
            };
        }

        // ---- the summary: composition (outside the board try) ----

        /// <summary>Format-Duration: whole seconds (half to even), "42s", "3m" or "3m7s"; negative is 0s.</summary>
        public static string FormatDuration(double seconds)
        {
            var s = Convert.ToInt32(Math.Round(seconds, MidpointRounding.ToEven));
            if (s < 0) s = 0;
            if (s < 60) return s.ToString(CultureInfo.InvariantCulture) + "s";
            var m = (int)Math.Floor(s / 60.0);
            var r = s % 60;
            if (r == 0) return m.ToString(CultureInfo.InvariantCulture) + "m";
            return m.ToString(CultureInfo.InvariantCulture) + "m" + r.ToString(CultureInfo.InvariantCulture) + "s";
        }

        /// <summary>The failed tests a suite names: their names (in order, unique), the details known, and how many were left out.</summary>
        public sealed class FailedTestList
        {
            /// <summary>The names, first occurrence first.</summary>
            public IReadOnlyList<string> Names { get; init; } = Array.Empty<string>();

            /// <summary>(name, detail) pairs as read; a name may repeat.</summary>
            public IReadOnlyList<KeyValuePair<string, string>> Details { get; init; } = Array.Empty<KeyValuePair<string, string>>();

            /// <summary>Failures past the cap.</summary>
            public int Extra { get; init; }
        }

        private static readonly Regex Ansi = new Regex("[\\x1B]\\[[0-?]*[ -/]*[@-~]", RegexOptions.CultureInvariant);
        private static readonly Regex TunitFailed = new Regex("^failed\\s+(.+?)\\s+\\(", RegexOptions.CultureInvariant);
        private static readonly Regex VitestFail = new Regex("^\\s*FAIL\\s+(\\S+)\\s+>\\s+(.+?)\\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex VitestCross = new Regex("^\\s*[\\u00D7x]\\s+(.+?)\\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex VitestDuration = new Regex("\\s+\\d+m?s$", RegexOptions.CultureInvariant);
        private static readonly Regex NextFailed = new Regex("^failed\\s+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex NextFail = new Regex("^\\s*FAIL\\s+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex LineBreak = new Regex("\\r?\\n", RegexOptions.CultureInvariant);

        /// <summary>
        /// Get-FailedTests: the failed tests a TUnit ("failed Name (..)") or vitest (" FAIL file > test", " x file > test 4ms")
        /// log names, each with the next non-blank line as its detail (300 characters at most), unique by name ignoring
        /// case, at most cap of them. A missing or unreadable log names none.
        /// </summary>
        public static FailedTestList FailedTests(string logPath, int cap = 25)
        {
            string text;
            try { text = File.Exists(logPath) ? File.ReadAllText(logPath) : null; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException) { text = null; }
            if (string.IsNullOrEmpty(text)) return new FailedTestList();
            text = Ansi.Replace(text, "");
            var lines = LineBreak.Split(text);
            var items = new List<KeyValuePair<string, string>>();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                string name = null;
                var m = TunitFailed.Match(line);
                if (m.Success) name = m.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    m = VitestFail.Match(line);
                    if (m.Success) name = m.Groups[1].Value.Trim() + " > " + m.Groups[2].Value.Trim();
                }
                if (string.IsNullOrEmpty(name))
                {
                    m = VitestCross.Match(line);
                    if (m.Success) name = VitestDuration.Replace(m.Groups[1].Value.Trim(), "");
                }
                if (string.IsNullOrEmpty(name)) continue;
                var detail = "";
                for (var j = i + 1; j < lines.Length; j++)
                {
                    var next = lines[j].Trim();
                    if (next.Length == 0) continue;
                    if (NextFailed.IsMatch(next) || NextFail.IsMatch(next)) break;
                    detail = next.Length > 300 ? next.Substring(0, 300) : next;
                    break;
                }
                items.Add(new KeyValuePair<string, string>(name, detail));
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unique = items.Where(item => seen.Add(item.Key)).ToList();
            var extra = 0;
            if (unique.Count > cap)
            {
                extra = unique.Count - cap;
                unique = unique.Take(cap).ToList();
            }
            return new FailedTestList { Names = unique.Select(item => item.Key).ToArray(), Details = unique, Extra = extra };
        }

        /// <summary>
        /// Get-SuiteFailed: the suite's failedTests (names, or {name, detail} rows); when it names none, the failures its log
        /// names; else its failedDetails and failedExtra.
        /// </summary>
        public static FailedTestList SuiteFailed(JsonNode suite)
        {
            var names = new List<string>();
            var details = new List<KeyValuePair<string, string>>();
            var extra = 0;
            var failedTests = J.Field(suite, "failedTests");
            if (failedTests != null)
            {
                foreach (var n in J.Items(failedTests))
                {
                    if (n == null) continue;
                    if (J.IsString(n)) names.Add(J.Str(n));
                    else if (J.IsTrue(J.Field(n, "name")))
                    {
                        names.Add(J.Str(J.Field(n, "name")));
                        if (J.IsTrue(J.Field(n, "detail"))) details.Add(new KeyValuePair<string, string>(J.Str(J.Field(n, "name")), J.Str(J.Field(n, "detail"))));
                    }
                }
            }
            if (names.Count == 0 && J.IsTrue(J.Field(suite, "log")))
            {
                var parsed = FailedTests(J.Str(J.Field(suite, "log")));
                return parsed;
            }
            if (J.IsTrue(J.Field(suite, "failedDetails")))
            {
                details = J.Items(J.Field(suite, "failedDetails"))
                    .Select(d => new KeyValuePair<string, string>(J.Str(J.Field(d, "name")), J.Str(J.Field(d, "detail")))).ToList();
                if (J.IsTrue(J.Field(suite, "failedExtra"))) extra = J.CastInt(J.Field(suite, "failedExtra"), "failedExtra");
            }
            else if (J.IsTrue(J.Field(suite, "failedExtra")))
            {
                extra = J.CastInt(J.Field(suite, "failedExtra"), "failedExtra");
            }
            return new FailedTestList { Names = names, Details = details, Extra = extra };
        }

        private static List<string> FailedNames(JsonNode summary) =>
            J.Items(J.Field(summary, "suites")).SelectMany(suite => SuiteFailed(suite).Names).ToList();

        /// <summary>Format-CountLine: the suite row's detail column.</summary>
        public static string CountLine(JsonNode suite)
        {
            if (J.IsTrue(J.Field(suite, "skipped"))) return "skipped: build failed";
            if (J.IsTrue(J.Field(suite, "timedOut"))) return "TIMEOUT - see log";
            if (J.IsTrue(J.Field(suite, "countsParsed")))
            {
                var failed = J.Field(suite, "failed");
                var passed = J.Field(suite, "passed");
                var skippedCount = J.Field(suite, "skippedCount");
                var failedN = failed != null ? J.Str(failed) : "0";
                var passedN = passed != null ? J.Str(passed) : "0";
                var skippedN = skippedCount != null ? J.Str(skippedCount) : "0";
                var duration = FormatDuration(J.CastDouble(J.Field(suite, "durationSeconds"), "durationSeconds"));
                if (!J.EqualsZero(J.Field(suite, "exitCode")))
                    return failedN + " failed / " + passedN + " passed / " + skippedN + " skipped - " + duration;
                return passedN + " passed - " + duration;
            }
            var code = J.Field(suite, "exitCode");
            var codeText = code == null ? "?" : J.Str(code);
            var tolerated = J.Has(suite, "processExitTolerated") && J.IsTrue(J.Field(suite, "processExitTolerated"));
            var resultText = J.Has(suite, "result") ? J.Str(J.Field(suite, "result")) : "";
            if (tolerated || (string.Equals(resultText, "pass", StringComparison.Ordinal) && J.Eq(codeText, "8"))) return "expected skips tolerated";
            return "exit " + codeText + ", counts unparsed - see log";
        }

        /// <summary>ConvertTo-UtcDate: an ISO date-time (a date to ConvertFrom-Json) or any text DateTimeOffset reads, in UTC; null otherwise.</summary>
        public static DateTime? ToUtcDate(JsonNode value) => J.UtcDate(value);

        /// <summary>Get-LondonStamp: "yyyy-MM-dd HH:mm Europe/London" (an unspecified time is UTC); UTC when the zone is unknown.</summary>
        public static string LondonStamp(DateTime utc)
        {
            utc = utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc.ToUniversalTime();
            var zone = London();
            if (zone == null) return utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
            return TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " Europe/London";
        }

        private static TimeZoneInfo London()
        {
            try { return NightlyLondon.TimeZone; }
            catch (InvalidOperationException) { return null; }
        }

        /// <summary>New-RunSection: the "## Run" block (header, steps table, expected skips, failure delta, logs, re-run hint).</summary>
        public static string RunSection(JsonNode summary, JsonNode previous, DateTime nowUtc)
        {
            var started = J.UtcDate(J.Field(summary, "startedAt")) ?? nowUtc;
            var stamp = LondonStamp(started);
            var succeeded = J.IsTrue(J.Field(summary, "succeeded"));
            var outcome = J.Str(J.Field(summary, "outcome"));
            if (string.IsNullOrWhiteSpace(outcome)) outcome = succeeded ? "green" : "TESTS";
            var verdict = succeeded ? "GREEN" : "RED (" + outcome + ")";
            var duration = FormatDuration(J.CastDouble(J.Field(summary, "durationSeconds"), "durationSeconds"));
            var sha = J.Str(J.Field(summary, "sha"));
            if (string.IsNullOrWhiteSpace(sha)) sha = "unknown";
            var gitRef = J.Str(J.Field(summary, "gitRef"));
            if (string.IsNullOrWhiteSpace(gitRef)) gitRef = "origin/master";
            var clone = J.Str(J.Field(summary, "clone"));
            if (string.IsNullOrWhiteSpace(clone)) clone = @"C:\Antiphon\nightly\checkout";
            var logDir = J.Str(J.Field(summary, "logDir"));
            var preflight = J.Field(summary, "preflight");
            var concurrent = 0;
            if (J.IsTrue(preflight) && J.IsTrue(J.Field(preflight, "concurrent")))
                concurrent = J.CastInt(J.Field(J.Field(preflight, "concurrent"), "total"), "preflight.concurrent.total");
            var dockerVersion = "";
            var docker = J.Field(preflight, "docker");
            if (J.IsTrue(preflight) && J.IsTrue(docker) && J.IsTrue(J.Field(docker, "version"))) dockerVersion = J.Str(J.Field(docker, "version"));
            var dockerBit = dockerVersion.Length > 0 ? "Docker " + dockerVersion : "Docker unknown";

            var lines = new List<string>
            {
                "## Run " + stamp + " -- " + verdict + " -- " + duration + " -- " + gitRef + " " + sha,
                "Concurrent test processes at start: " + concurrent.ToString(CultureInfo.InvariantCulture) + " | " + dockerBit + " | clone " + clone,
                "",
                "| Step | Result | Detail |",
                "| --- | --- | --- |",
            };
            foreach (var build in J.Items(J.Field(summary, "builds")))
            {
                var result = J.Str(J.Field(build, "result"));
                if (string.IsNullOrWhiteSpace(result)) result = J.EqualsZero(J.Field(build, "exitCode")) ? "pass" : "FAIL";
                var detail = J.Str(J.Field(build, "detail"));
                if (string.IsNullOrWhiteSpace(detail)) detail = FormatDuration(J.CastDouble(J.Field(build, "durationSeconds"), "durationSeconds"));
                lines.Add("| " + J.Str(J.Field(build, "name")) + " | " + result + " | " + detail + " |");
            }
            foreach (var suite in J.Items(J.Field(summary, "suites")))
            {
                var result = J.Str(J.Field(suite, "result"));
                if (string.IsNullOrWhiteSpace(result))
                {
                    if (J.IsTrue(J.Field(suite, "skipped"))) result = "skipped";
                    else if (J.EqualsZero(J.Field(suite, "exitCode"))) result = "pass";
                    else result = "FAIL";
                }
                lines.Add("| " + J.Str(J.Field(suite, "name")) + " | " + result + " | " + CountLine(suite) + " |");
            }
            if (J.Has(summary, "expectedSkipRosterDigest") || J.Has(summary, "toleratedSkips"))
            {
                var digest = J.Has(summary, "expectedSkipRosterDigest") ? J.Str(J.Field(summary, "expectedSkipRosterDigest")) : "";
                lines.Add("");
                lines.Add("Expected skip roster: " + digest);
                var shown = new List<string>();
                if (J.Has(summary, "toleratedSkips"))
                {
                    foreach (var row in J.Items(J.Field(summary, "toleratedSkips")))
                    {
                        if (row == null) continue;
                        var id = J.Has(row, "identity") ? J.Str(J.Field(row, "identity")) : "";
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        var reasonClass = "platform";
                        if (J.Has(row, "reasonClass") && !string.IsNullOrWhiteSpace(J.Str(J.Field(row, "reasonClass")))) reasonClass = J.Str(J.Field(row, "reasonClass"));
                        shown.Add("- " + id + " (" + reasonClass + ")");
                    }
                }
                if (shown.Count == 0) lines.Add("Tolerated skips: none");
                else
                {
                    lines.Add("Tolerated skips:");
                    lines.AddRange(shown);
                }
            }

            var current = FailedNames(summary);
            var prior = previous != null ? FailedNames(previous) : new List<string>();
            var currentSet = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
            var priorSet = new HashSet<string>(prior, StringComparer.OrdinalIgnoreCase);
            var newNames = current.Where(n => !priorSet.Contains(n)).ToList();
            var stillNames = current.Where(n => priorSet.Contains(n)).ToList();
            var fixedNames = prior.Where(n => !currentSet.Contains(n)).ToList();
            var detailByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var suite in J.Items(J.Field(summary, "suites")))
                foreach (var pair in SuiteFailed(suite).Details) detailByName[pair.Key ?? ""] = pair.Value ?? "";

            if (!succeeded)
            {
                lines.Add("");
                lines.Add("### New since last run (" + newNames.Count.ToString(CultureInfo.InvariantCulture) + ")");
                if (newNames.Count == 0) lines.Add("- none");
                else
                {
                    foreach (var n in newNames)
                    {
                        detailByName.TryGetValue(n, out var detail);
                        lines.Add(string.IsNullOrEmpty(detail) ? "- " + n : "- " + n + "  " + detail);
                    }
                }
                lines.Add("### Still failing (" + stillNames.Count.ToString(CultureInfo.InvariantCulture) + ")  |  ### Fixed since last run ("
                    + fixedNames.Count.ToString(CultureInfo.InvariantCulture) + ")");
                foreach (var n in stillNames) lines.Add("- still: " + n);
                foreach (var n in fixedNames) lines.Add("- fixed: " + n);
            }

            lines.Add("");
            if (!string.IsNullOrEmpty(logDir))
                lines.Add("Logs: " + logDir + " (antiphon-tests.log, agents-pty-tests.log, client-tests.log, build.log, summary.json)");
            string rerun = null;
            foreach (var suite in J.Items(J.Field(summary, "suites")))
            {
                var failed = SuiteFailed(suite);
                if (failed.Names.Count == 0) continue;
                var first = failed.Names[0] ?? "";
                if (J.Eq(J.Str(J.Field(suite, "id")), "client") || J.Eq(J.Str(J.Field(suite, "name")), "client"))
                {
                    rerun = "pwsh -File scripts/test-client.ps1 " + ClientFile(first);
                }
                else
                {
                    var exe = "tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.exe";
                    if (J.Eq(J.Str(J.Field(suite, "id")), "agents-pty")) exe = "tests/Antiphon.Agents.Pty.Tests/bin/Debug/net9.0/Antiphon.Agents.Pty.Tests.exe";
                    rerun = exe + " --treenode-filter \"/*/*/*/" + first + "\"";
                }
                break;
            }
            if (rerun != null) lines.Add("Re-run one: " + rerun);
            return string.Join("\n", lines);
        }

        private static readonly Regex ClientFileName = new Regex("^(\\S+)\\s+>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static string ClientFile(string name)
        {
            var m = ClientFileName.Match(name ?? "");
            return m.Success ? m.Groups[1].Value : name;
        }

        /// <summary>
        /// Get-NightlyRerunCommand: the command that re-runs one failed test of a suite, from the policy's suite row (project
        /// and assembly) or, without a policy, the known suites' projects. The report's run section does not use it (it names
        /// the Antiphon.Tests or Agents.Pty executable); it is kept with its case (C487_G090).
        /// </summary>
        public static string RerunCommand(JsonNode suite, string failedName, JsonNode policy)
        {
            var suiteId = J.Str(J.Field(suite, "id"));
            var project = "tests/Antiphon.Tests/Antiphon.Tests.csproj";
            var exe = "tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.exe";
            if (J.IsTrue(policy) && J.IsTrue(J.Field(policy, "suites")))
            {
                var row = J.Field(J.Field(policy, "suites"), suiteId);
                if (J.IsTrue(row) && J.IsTrue(J.Field(row, "project")))
                {
                    project = J.Str(J.Field(row, "project")).Replace('\\', '/');
                    if (J.IsTrue(J.Field(row, "assembly")))
                    {
                        var assembly = J.Str(J.Field(row, "assembly"));
                        exe = "tests/" + assembly + "/bin/Debug/net9.0/" + assembly + ".exe";
                    }
                }
            }
            else if (J.Eq(suiteId, "agents-pty"))
            {
                exe = "tests/Antiphon.Agents.Pty.Tests/bin/Debug/net9.0/Antiphon.Agents.Pty.Tests.exe";
            }
            else if (J.Eq(suiteId, "session-runner"))
            {
                exe = "tests/Antiphon.SessionRunner.Tests/bin/Debug/net9.0/Antiphon.SessionRunner.Tests.exe";
                project = "tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj";
            }
            else if (J.Eq(suiteId, "pty-host"))
            {
                exe = "tests/Antiphon.PtyHost.Tests/bin/Debug/net9.0/Antiphon.PtyHost.Tests.exe";
                project = "tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj";
            }
            else if (J.Eq(suiteId, "messaging"))
            {
                exe = "tests/Antiphon.Messaging.Tests/bin/Debug/net9.0/Antiphon.Messaging.Tests.exe";
                project = "tests/Antiphon.Messaging.Tests/Antiphon.Messaging.Tests.csproj";
            }
            if (J.Eq(suiteId, "client") || J.Eq(J.Str(J.Field(suite, "name")), "client"))
                return "pwsh -File scripts/test-client.ps1 " + ClientFile(failedName);
            return "dotnet run --project " + project + " -- --treenode-filter \"/*/*/*/" + failedName + "\"  (" + exe + ")";
        }

        /// <summary>New-NightlyTitle: "&lt;Lane&gt; red yyyy-MM-dd: &lt;what failed&gt;" (London date), 300 characters at most.</summary>
        public static string RedTitle(JsonNode summary, DateTime startedUtc, string laneTitle)
        {
            var utc = startedUtc.ToUniversalTime();
            var zone = London();
            var date = zone != null
                ? TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var outcome = J.Str(J.Field(summary, "outcome"));
            if (J.Eq(outcome, "BUILD")) return Cap(laneTitle + " red " + date + ": BUILD FAILED", MaxTitle);
            var bits = new List<string>();
            foreach (var suite in J.Items(J.Field(summary, "suites")))
            {
                var name = J.Str(J.Field(suite, "name"));
                var failCount = SuiteFailCount(suite);
                if (failCount > 0) bits.Add(name + " " + failCount.ToString(CultureInfo.InvariantCulture) + " failed");
                else if (J.IsTrue(J.Field(suite, "timedOut"))) bits.Add(name + " TIMEOUT");
                else if (!J.IsTrue(J.Field(suite, "skipped")) && !J.EqualsZero(J.Field(suite, "exitCode")))
                {
                    var exempt = (J.Has(suite, "processExitTolerated") && J.IsTrue(J.Field(suite, "processExitTolerated")))
                        || (string.Equals(J.Str(J.Field(suite, "result")), "pass", StringComparison.Ordinal) && J.Eq(J.Str(J.Field(suite, "exitCode")), "8"));
                    if (!exempt) bits.Add(name + " exit " + J.Str(J.Field(suite, "exitCode")));
                }
            }
            foreach (var build in J.Items(J.Field(summary, "builds")))
            {
                var exitCode = J.Field(build, "exitCode");
                if (J.Eq(J.Str(J.Field(build, "result")), "FAIL") || (exitCode != null && !J.EqualsZero(exitCode) && !J.IsTrue(J.Field(build, "skipped"))))
                    bits.Add(J.Str(J.Field(build, "name")) + " failed");
            }
            var rest = bits.Count > 0 ? string.Join(", ", bits) : outcome;
            if (string.IsNullOrWhiteSpace(rest)) rest = "failed";
            return Cap(laneTitle + " red " + date + ": " + rest, MaxTitle);
        }

        // [int]$s.failed when present, else the failedTests count.
        private static int SuiteFailCount(JsonNode suite)
        {
            var failed = J.Field(suite, "failed");
            if (failed != null) return J.CastInt(failed, "failed");
            var failedTests = J.Field(suite, "failedTests");
            if (J.IsTrue(failedTests)) return J.Items(failedTests).Count;
            return 0;
        }

        private static string Cap(string text, int max) => text.Length > max ? text.Substring(0, max) : text;

        /// <summary>
        /// Limit-Description: at most max characters, dropping whole "## Run " sections from the end (oldest first) and
        /// truncating only when one section is left over the limit.
        /// </summary>
        public static string LimitDescription(string text, int max = MaxDescription)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
            var parts = RunSplit.Split(text).ToList();
            while (text.Length > max && parts.Count > 1)
            {
                parts.RemoveAt(parts.Count - 1);
                text = string.Concat(parts);
            }
            if (text.Length > max) text = text.Substring(0, max);
            return text;
        }

        private static readonly Regex RunSplit = new Regex("(?m)(?=^## Run )", RegexOptions.CultureInvariant);

        /// <summary>
        /// Test-NightlyGreenClosureEligible: only a complete, scheduled green of origin/master (or master) that may report,
        /// at the required policy when one is named, may close a card.
        /// </summary>
        public static bool GreenClosureEligible(JsonNode summary)
        {
            if (!J.IsTrue(J.Field(summary, "succeeded"))) return false;
            if (!J.IsTrue(J.Field(summary, "coverageComplete"))) return false;
            var trigger = J.Str(J.Field(summary, "trigger"));
            if (string.IsNullOrWhiteSpace(trigger)) trigger = "scheduled";
            if (!J.Eq(trigger, "scheduled")) return false;
            var gitRef = J.Str(J.Field(summary, "gitRef"));
            if (!J.Eq(gitRef, "origin/master") && !J.Eq(gitRef, "master")) return false;
            if (J.IsTrue(J.Field(summary, "noReport"))) return false;
            if (J.IsTrue(J.Field(summary, "requiredPolicyHash")) && !J.Eq(J.Str(J.Field(summary, "policyHash")), J.Str(J.Field(summary, "requiredPolicyHash")))) return false;
            return true;
        }

        // ---- one run ----

        private sealed class ReportRun
        {
            private static readonly JsonSerializerOptions BodyJson = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = false };
            private static readonly string[] Statuses = { "Backlog", "InProgress", "Review", "NeedsDecision", "Done" };
            private const int PageCap = 10000;

            private readonly NightlyReportOptions _options;
            private readonly NightlyReportSeams _seams;
            private readonly NightlyReportResult _result = new NightlyReportResult();
            private string _api;
            private bool _didRetry;

            public ReportRun(NightlyReportOptions options, NightlyReportSeams seams)
            {
                _options = options;
                _seams = seams;
            }

            private DateTime UtcNow => _seams.Clock.GetUtcNow().UtcDateTime;

            private void Line(string message) => _seams.Out.WriteLine(message);

            public NightlyReportResult Execute()
            {
                var api = _options.Api;
                if (string.IsNullOrWhiteSpace(api)) api = _seams.Environment?.Invoke("ANTIPHON_API");
                if (string.IsNullOrWhiteSpace(api)) api = DefaultApi;
                _api = api.TrimEnd('/');

                var summaryPath = _options.Summary ?? "";
                if (!Exists(summaryPath))
                {
                    _seams.Error.WriteLine("Summary '" + summaryPath + "' does not exist.");
                    _result.ExitCode = SummaryFailed;
                    return _result;
                }
                Composed composed;
                try
                {
                    composed = Compose(summaryPath);
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    // The script stopped here (a terminating error outside its board try): pwsh -File exits 1.
                    _seams.Error.WriteLine("nightly-report: " + e.GetType().Name + ": " + e.Message);
                    _result.ExitCode = SummaryFailed;
                    return _result;
                }
                Deliver(composed);
                return _result;
            }

            private static bool Exists(string path)
            {
                if (string.IsNullOrEmpty(path)) return false;
                try { return File.Exists(path) || Directory.Exists(path); }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException) { return false; }
            }

            private sealed class Composed
            {
                public JsonNode Summary;
                public string Lane;
                public string LaneLabel;
                public string ClassLabel;
                public string Importance;
                public string DateOnly;
                public string Sha;
                public bool Succeeded;
                public string CountPhrase;
            }

            private Composed Compose(string summaryPath)
            {
                var summary = J.ReadFile(summaryPath);
                JsonNode previous = null;
                if (!string.IsNullOrWhiteSpace(_options.PreviousSummary) && Exists(_options.PreviousSummary)) previous = J.ReadFile(_options.PreviousSummary);

                var logDir = J.Str(J.Field(summary, "logDir"));
                if (string.IsNullOrWhiteSpace(logDir)) logDir = Parent(FullPathSafe(summaryPath));
                if (string.IsNullOrWhiteSpace(logDir)) logDir = Parent(summaryPath);
                _result.CardMdPath = Path.Combine(logDir ?? "", "card.md");

                var now = UtcNow;
                var started = J.UtcDate(J.Field(summary, "startedAt")) ?? now;
                var succeeded = J.IsTrue(J.Field(summary, "succeeded"));
                var outcome = J.Str(J.Field(summary, "outcome"));
                if (string.IsNullOrWhiteSpace(outcome)) outcome = succeeded ? "green" : "TESTS";
                var sha = J.Str(J.Field(summary, "sha"));
                if (string.IsNullOrWhiteSpace(sha)) sha = "unknown";
                var dateOnly = LondonStamp(started).Substring(0, 10);

                // CARD-0599 R-4: the incident identity is lane-specific. A release-candidate run files and closes its OWN
                // incident under the release-gate label; it can never find, update or auto-close the master nightly card.
                var composed = new Composed { Summary = summary, Lane = "nightly", LaneLabel = "nightly", DateOnly = dateOnly, Sha = sha, Succeeded = succeeded };
                var laneTitle = "Nightly";
                if (J.Str(J.Field(summary, "profile")).Trim().ToLowerInvariant() == "rc")
                {
                    composed.Lane = "rc";
                    composed.LaneLabel = "release-gate";
                    laneTitle = "Release-candidate";
                }

                _result.Body = RunSection(summary, previous, now);
                _result.Title = succeeded ? laneTitle + " green " + dateOnly : RedTitle(summary, started, laneTitle);

                composed.Importance = "Normal";
                composed.ClassLabel = "tests";
                if (J.Eq(outcome, "BUILD"))
                {
                    composed.Importance = "High";
                    composed.ClassLabel = "build";
                }
                var failCounts = new List<string>();
                foreach (var suite in J.Items(J.Field(summary, "suites")))
                {
                    var n = SuiteFailCount(suite);
                    if (n > 0) failCounts.Add(J.Str(J.Field(suite, "name")) + " " + n.ToString(CultureInfo.InvariantCulture) + " failed");
                }
                composed.CountPhrase = failCounts.Count > 0 ? string.Join(", ", failCounts) : outcome;
                return composed;
            }

            private static string FullPathSafe(string path)
            {
                try { return Path.GetFullPath(path); }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException || e is System.Security.SecurityException) { return path; }
            }

            private static string Parent(string path)
            {
                try { return Path.GetDirectoryName(path) ?? ""; }
                catch (Exception e) when (e is ArgumentException || e is PathTooLongException) { return ""; }
            }

            // ---- the board (inside the script's try: any failure is exit 3 with card.md) ----

            private void Deliver(Composed c)
            {
                try
                {
                    DeliverCore(c);
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    _result.ExitCode = ApiFailed;
                    _result.Action = "reporting-failed";
                    Line("REPORTING: could not file card, body at " + _result.CardMdPath);
                    Line(e.Message);
                    try { WriteCardMd(); }
                    catch (Exception w) when (w is IOException || w is UnauthorizedAccessException || w is ArgumentException || w is NotSupportedException)
                    {
                        Line("could not write card.md: " + w.Message);
                    }
                }
            }

            private void DeliverCore(Composed c)
            {
                var boards = J.Flat(Invoke("GET", _api + "/api/boards"));
                JsonNode board = null;
                if (Guid.TryParse(_options.Board ?? "", out _)) board = boards.FirstOrDefault(b => J.Eq(J.Str(J.Field(b, "id")), _options.Board));
                board ??= boards.FirstOrDefault(b => J.Eq(J.Str(J.Field(b, "name")), _options.Board));
                if (board == null)
                    throw new InvalidOperationException("No board named '" + _options.Board + "'. Known: " + string.Join(", ", boards.Select(b => J.Str(J.Field(b, "name")))));
                var boardId = J.Str(J.Field(board, "id"));

                var open = new List<JsonNode>();
                var done = new List<JsonNode>();
                foreach (var status in Statuses)
                {
                    foreach (var card in Census(boardId, status))
                    {
                        if (!HasLabel(card, c.LaneLabel) || !Unarchived(card)) continue;
                        if (status == "Done") done.Add(card);
                        else open.Add(card);
                    }
                }

                var columns = J.Flat(Invoke("GET", _api + "/api/boards/" + boardId + "/columns"));
                var terminal = J.StableSort(columns.Where(column => J.IsTrue(J.Field(column, "isTerminal"))), column => J.Field(column, "columnOrder"), J.CompareValues, false)
                    .FirstOrDefault();

                if (c.Succeeded) Green(c, open, terminal);
                else Red(c, open, done, boardId);
            }

            private static bool HasLabel(JsonNode card, string label) => J.Items(J.Field(card, "labels")).Any(l => J.Eq(J.Str(l), label));

            // ($null -eq $_.archivedAt -or $_.archivedAt -eq ''): no archive time, or an empty one.
            private static bool Unarchived(JsonNode card) => Blank(J.Field(card, "archivedAt"));

            private static JsonNode Latest(IEnumerable<JsonNode> cards) =>
                J.StableSort(cards, card => J.DateNode(J.UtcDate(J.Field(card, "updatedAt"))), J.CompareValues, true).FirstOrDefault();

            private void Green(Composed c, List<JsonNode> open, JsonNode terminal)
            {
                var eligible = GreenClosureEligible(c.Summary);
                if (open.Count == 0)
                {
                    _result.Action = "none";
                    Line("green; no open nightly card; nothing to file.");
                    return;
                }
                var card = Latest(open);
                var status = J.Str(J.Field(card, "status"));
                var assigned = J.Field(card, "assignedAgentId");
                var owner = J.Field(card, "ownerSessionId");
                var unassigned = Blank(assigned) && Blank(owner);
                var id = J.Str(J.Field(card, "id"));
                var discussion = "green on " + c.DateOnly + " at " + c.Sha + "; not closing because the card is " + status + "/" + (J.IsTrue(assigned) ? J.Str(assigned) : "unassigned");
                if (eligible && J.Eq(status, "Backlog") && unassigned && terminal != null)
                {
                    var reason = "[" + c.Lane + " auto-close] green on " + c.DateOnly + " at " + c.Sha + " - reopen if this was a flake you want tracked";
                    var move = Body(("boardColumnId", J.Str(J.Field(terminal, "id"))), ("concurrencyToken", CardToken(card)), ("reason", reason));
                    var uri = _api + "/api/cards/" + id;
                    _result.WriteList.Add(new NightlyReportWrite("PATCH", uri, move));
                    if (_options.DryRun)
                    {
                        Line("DRYRUN PATCH /api/cards/" + id + " close: " + reason);
                        DryRunEnd();
                        return;
                    }
                    Invoke("PATCH", uri, move);
                    _result.Action = "closed";
                    Line("closed " + J.Str(J.Field(card, "identifier")) + " (" + reason + ")");
                    return;
                }
                var body = Body(("body", discussion), ("author", "nightly"));
                var discussionUri = _api + "/api/cards/" + id + "/discussion";
                _result.WriteList.Add(new NightlyReportWrite("POST", discussionUri, body));
                if (_options.DryRun)
                {
                    Line("DRYRUN POST /api/cards/" + id + "/discussion: " + discussion);
                    DryRunEnd();
                    return;
                }
                Invoke("POST", discussionUri, body);
                _result.Action = "discussion";
                Line("discussion on " + J.Str(J.Field(card, "identifier")) + ": " + discussion);
            }

            private void Red(Composed c, List<JsonNode> open, List<JsonNode> done, string boardId)
            {
                JsonNode target = null;
                var reopened = false;
                if (open.Count > 0)
                {
                    target = Latest(open);
                }
                else
                {
                    var cutoff = UtcNow.AddDays(-7);
                    var marker = "[" + c.Lane + " auto-close]";
                    var reopenable = done.Where(card =>
                    {
                        if (!J.Str(J.Field(card, "terminalReason")).StartsWith(marker, StringComparison.Ordinal)) return false;
                        var updated = J.UtcDate(J.Field(card, "updatedAt"));
                        return updated != null && updated.Value >= cutoff;
                    });
                    var latest = Latest(reopenable);
                    if (latest != null)
                    {
                        target = latest;
                        var reopenBody = Body(("concurrencyToken", CardToken(target)), ("reason", "red again on " + c.DateOnly));
                        var uri = _api + "/api/cards/" + J.Str(J.Field(target, "id")) + "/reopen";
                        _result.WriteList.Add(new NightlyReportWrite("POST", uri, reopenBody));
                        if (!_options.DryRun)
                        {
                            target = CardObject(Invoke("POST", uri, reopenBody));
                            Line("reopened " + J.Str(J.Field(target, "identifier")));
                        }
                        reopened = true;
                    }
                }

                var existing = target != null && J.IsTrue(J.Field(target, "description")) ? J.Str(J.Field(target, "description")) : "";
                var combined = _result.Body;
                if (!string.IsNullOrWhiteSpace(existing)) combined = _result.Body + "\n\n" + existing;
                _result.Body = LimitDescription(combined, MaxDescription);

                if (target == null)
                {
                    var create = Body(("title", _result.Title), ("description", _result.Body), ("importance", c.Importance), ("urgency", "Normal"),
                        ("labels", new[] { c.LaneLabel, c.ClassLabel }));
                    var uri = _api + "/api/boards/" + boardId + "/cards";
                    _result.WriteList.Add(new NightlyReportWrite("POST", uri, create));
                    if (_options.DryRun)
                    {
                        Line("DRYRUN POST /api/boards/" + boardId + "/cards title=" + _result.Title);
                        DryRunEnd();
                        return;
                    }
                    var created = Invoke("POST", uri, create);
                    _result.Action = "created";
                    Line("created " + J.Str(J.Field(created, "identifier")) + " " + _result.Title);
                    return;
                }

                var merged = new List<string>();
                foreach (var label in J.Items(J.Field(target, "labels")))
                {
                    var text = J.Str(label);
                    if (text.Length > 0 && !merged.Contains(text, StringComparer.OrdinalIgnoreCase)) merged.Add(text);
                }
                // The script adds "nightly" whatever the lane (kept as ported).
                foreach (var label in new[] { "nightly", c.ClassLabel })
                    if (!merged.Contains(label, StringComparer.OrdinalIgnoreCase)) merged.Add(label);
                if (merged.Count < 2) merged.Add("nightly");
                var targetId = J.Str(J.Field(target, "id"));
                var patch = Body(("concurrencyToken", CardToken(target)), ("reason", "nightly red on " + c.DateOnly), ("title", _result.Title),
                    ("description", _result.Body), ("labels", merged.ToArray()), ("editedBy", "nightly"), ("importance", c.Importance));
                var discussion = Body(("body", "still red on " + c.DateOnly + ": " + c.CountPhrase), ("author", "nightly"));
                var contentUri = _api + "/api/cards/" + targetId + "/content";
                var discussionUri = _api + "/api/cards/" + targetId + "/discussion";
                _result.WriteList.Add(new NightlyReportWrite("PATCH", contentUri, patch));
                _result.WriteList.Add(new NightlyReportWrite("POST", discussionUri, discussion));
                if (_options.DryRun)
                {
                    Line("DRYRUN PATCH /api/cards/" + targetId + "/content + discussion");
                    DryRunEnd();
                    return;
                }
                Invoke("PATCH", contentUri, patch);
                Invoke("POST", discussionUri, discussion);
                _result.Action = reopened ? "reopened" : "updated";
                Line(_result.Action + " " + J.Str(J.Field(target, "identifier")));
            }

            private void DryRunEnd()
            {
                WriteCardMd();
                _result.Action = "dry-run";
            }

            private static bool Blank(JsonNode value) => value == null || (J.IsString(value) && J.Str(value).Length == 0);

            // Get-CardToken: the card's concurrencyToken, else its card.concurrencyToken, else null.
            private static string CardToken(JsonNode card)
            {
                if (card == null) return null;
                if (J.IsTrue(J.Field(card, "concurrencyToken"))) return J.Str(J.Field(card, "concurrencyToken"));
                var inner = J.Field(card, "card");
                if (J.IsTrue(inner) && J.IsTrue(J.Field(inner, "concurrencyToken"))) return J.Str(J.Field(inner, "concurrencyToken"));
                return null;
            }

            // Get-CardObject: a response's card when it wraps one.
            private static JsonNode CardObject(JsonNode response)
            {
                if (response == null) return null;
                var card = J.Field(response, "card");
                return J.IsTrue(card) ? card : response;
            }

            // ConvertTo-NightlyJson: one compact object, keys in the order given; strings, nulls and string arrays.
            private static string Body(params (string Key, object Value)[] fields)
            {
                var obj = new JsonObject();
                foreach (var (key, value) in fields)
                {
                    switch (value)
                    {
                        case null: obj[key] = null; break;
                        case string text: obj[key] = text; break;
                        case string[] list: obj[key] = new JsonArray(list.Select(item => (JsonNode)JsonValue.Create(item)).ToArray()); break;
                        default: throw new ArgumentException("unsupported body value for " + key);
                    }
                }
                return obj.ToJsonString(BodyJson);
            }

            // Write-CardMdFile: title, a blank line, the body; UTF-8 without a byte-order mark.
            private void WriteCardMd()
            {
                var path = _result.CardMdPath;
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, _result.Title + "\r\n\r\n" + _result.Body + System.Environment.NewLine, new UTF8Encoding(false));
            }

            // Get-NightlyCensusCards: every card of one status. A capped page cannot prove a status is empty or complete:
            // follow nextPageToken, and refuse when the list stops short, repeats a card or repeats a token.
            private List<JsonNode> Census(string boardId, string status)
            {
                var cards = new List<JsonNode>();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var tokens = new HashSet<string>(StringComparer.Ordinal);
                var token = "";
                Exception Short() => new InvalidOperationException("truncated card census on status " + status + " cannot prove absence");
                for (var pageNumber = 0; pageNumber < PageCap; pageNumber++)
                {
                    var uri = _api + "/api/cards?boardId=" + boardId + "&status=" + status;
                    if (!string.IsNullOrEmpty(token)) uri += "&pageToken=" + Uri.EscapeDataString(token);
                    var page = Invoke("GET", uri);
                    var batch = new List<JsonNode>();
                    if (page is JsonObject && J.Field(page, "cards") != null) batch = J.Flat(J.Field(page, "cards"));
                    else if (page is JsonArray) batch = J.Flat(page);

                    var truncated = false;
                    if (J.Has(page, "truncated"))
                    {
                        var value = J.Field(page, "truncated");
                        var kind = J.Kind(value);
                        if (kind != JsonValueKind.True && kind != JsonValueKind.False) throw Short();
                        truncated = kind == JsonValueKind.True;
                    }
                    var hasNext = false;
                    var next = "";
                    if (J.Has(page, "nextPageToken"))
                    {
                        hasNext = true;
                        var value = J.Field(page, "nextPageToken");
                        if (value != null) next = J.Str(value);
                    }
                    if (truncated && batch.Count == 0) throw Short();
                    foreach (var item in batch)
                    {
                        var id = item != null && J.Field(item, "id") != null ? J.Str(J.Field(item, "id")) : "";
                        if (!string.IsNullOrWhiteSpace(id) && !ids.Add(id)) throw Short();
                        cards.Add(item);
                    }
                    if (!truncated)
                    {
                        if (hasNext && !string.IsNullOrWhiteSpace(next)) throw Short();
                        return cards;
                    }
                    if (string.IsNullOrWhiteSpace(next) || !tokens.Add(next)) throw Short();
                    token = next;
                }
                throw Short();
            }

            // Invoke-Antiphon: one call with no headers; the first failure of the run is retried once, 60 s later on the host clock.
            private JsonNode Invoke(string method, string uri, string body = null)
            {
                NightlyReportHttpResponse response;
                try
                {
                    response = Once(method, uri, body);
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    if (_didRetry) throw;
                    _didRetry = true;
                    Line("API failed; retrying once after 60s.");
                    Wait(RetryDelay);
                    response = Once(method, uri, body);
                }
                return J.ParseText(response.Body);
            }

            private NightlyReportHttpResponse Once(string method, string uri, string body)
            {
                var response = _seams.Http(method, uri, new Dictionary<string, string>(), body);
                if (response == null) throw new NightlyReportApiException("No response to " + method + " " + PathOf(uri) + ".");
                if (response.Status < 200 || response.Status > 299)
                {
                    // Invoke-RestMethod's wording: the code and its standard reason phrase.
                    string reason;
                    using (var phrase = new HttpResponseMessage((System.Net.HttpStatusCode)response.Status)) reason = phrase.ReasonPhrase ?? "";
                    throw new NightlyReportApiException("Response status code does not indicate success: " + response.Status.ToString(CultureInfo.InvariantCulture) + " (" + reason + ").");
                }
                return response;
            }

            // The request path without the API base (which may carry credentials).
            private string PathOf(string uri) => uri != null && uri.StartsWith(_api, StringComparison.Ordinal) ? uri.Substring(_api.Length) : "";

            private void Wait(TimeSpan delay)
            {
                using var elapsed = new ManualResetEventSlim(false);
                using (_seams.Clock.CreateTimer(_ => elapsed.Set(), null, delay, Timeout.InfiniteTimeSpan))
                    elapsed.Wait();
            }
        }

        // ---- PowerShell readings of ConvertFrom-Json values ----

        internal static class J
        {
            private static readonly JsonNodeOptions NodeOptions = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
            private static readonly JsonDocumentOptions DocumentOptions = new JsonDocumentOptions { MaxDepth = 1024, CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

            // Newtonsoft's ISO date-time (what ConvertFrom-Json turns into a [datetime]).
            private static readonly Regex IsoDateTime = new Regex(
                "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(\\.\\d{1,7})?(Z|[+-]\\d{2}:\\d{2})?$", RegexOptions.CultureInvariant);

            /// <summary>Get-Content -Raw | ConvertFrom-Json: null for an empty file; JsonException for malformed text.</summary>
            public static JsonNode ReadFile(string path)
            {
                var bytes = File.ReadAllBytes(path);
                var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                var span = new ReadOnlySpan<byte>(bytes, start, bytes.Length - start);
                var blank = true;
                foreach (var b in span)
                {
                    if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r' && b != (byte)'\n') { blank = false; break; }
                }
                if (blank) return null;
                return Touch(JsonNode.Parse(span, NodeOptions, DocumentOptions));
            }

            /// <summary>Invoke-RestMethod's reading of a response body: JSON when it parses, else the text itself; null when empty.</summary>
            public static JsonNode ParseText(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return null;
                try { return Touch(JsonNode.Parse(text, NodeOptions, DocumentOptions)); }
                catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException) { return JsonValue.Create(text); }
            }

            // A JsonObject builds its properties lazily: a repeated name only throws when it is read.
            private static JsonNode Touch(JsonNode node)
            {
                if (node is JsonObject obj) foreach (var pair in obj) Touch(pair.Value);
                else if (node is JsonArray array) foreach (var item in array) Touch(item);
                return node;
            }

            /// <summary>
            /// $o.Name. On an array this is member enumeration: one value per element (null where it has none), array values
            /// opened, a single result unwrapped, no result null (preflight.docker is a one-row array in run summaries).
            /// </summary>
            public static JsonNode Field(JsonNode node, string name)
            {
                if (!(node is JsonArray array)) return HealthJson.Field(node, name);
                var values = new List<JsonNode>();
                foreach (var item in array)
                {
                    var value = Field(item, name);
                    if (value is JsonArray inner) values.AddRange(inner);
                    else values.Add(value);
                }
                if (values.Count == 0) return null;
                if (values.Count == 1) return values[0];
                return new JsonArray(values.Select(value => value?.DeepClone()).ToArray());
            }

            public static bool Has(JsonNode node, string name) => HealthJson.Has(node, name);

            public static JsonValueKind Kind(JsonNode node) => HealthJson.Kind(node);

            public static bool IsString(JsonNode node) => HealthJson.IsString(node);

            /// <summary>[string]$value; a date-time string reads as its text.</summary>
            public static string Str(JsonNode node) => HealthJson.Str(node);

            /// <summary>[bool]$value.</summary>
            public static bool IsTrue(JsonNode node) => HealthJson.IsTrue(node);

            /// <summary>-eq on text: invariant culture, case-insensitive.</summary>
            public static bool Eq(string first, string second) => HealthJson.Eq(first, second);

            /// <summary>@($value): a missing or null value is one null element, an array its elements, anything else itself.</summary>
            public static IReadOnlyList<JsonNode> Items(JsonNode node) => node is JsonArray array ? array.ToList() : new List<JsonNode> { node };

            /// <summary>ConvertTo-FlatArray over a streamed result: none for null, an array's elements (nested arrays opened once), else the value.</summary>
            public static List<JsonNode> Flat(JsonNode node)
            {
                var list = new List<JsonNode>();
                if (node == null) return list;
                if (node is JsonArray array)
                {
                    foreach (var item in array)
                    {
                        if (item is JsonArray inner) list.AddRange(inner);
                        else list.Add(item);
                    }
                    return list;
                }
                list.Add(node);
                return list;
            }

            /// <summary>$value -eq 0 (left operand decides): a number equal to 0, the text "0", false; never null.</summary>
            public static bool EqualsZero(JsonNode node)
            {
                switch (Kind(node))
                {
                    case JsonValueKind.Null: return false;
                    case JsonValueKind.Number:
                        return double.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number == 0;
                    case JsonValueKind.String: return string.Equals(Str(node), "0", StringComparison.OrdinalIgnoreCase);
                    case JsonValueKind.False: return true;
                    default: return false;
                }
            }

            /// <summary>[int]$value; NightlyReportValueException when the cast fails.</summary>
            public static int CastInt(JsonNode node, string what)
            {
                var value = HealthJson.CastInt(node);
                if (value == null) throw new NightlyReportValueException("Cannot convert the " + what + " value \"" + Str(node) + "\" to type System.Int32.");
                return value.Value;
            }

            /// <summary>[double]$value (null and blank text are 0); NightlyReportValueException when the cast fails.</summary>
            public static double CastDouble(JsonNode node, string what)
            {
                switch (Kind(node))
                {
                    case JsonValueKind.Null: return 0;
                    case JsonValueKind.True: return 1;
                    case JsonValueKind.False: return 0;
                    case JsonValueKind.Number:
                        return double.Parse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                    case JsonValueKind.String:
                        var text = Str(node);
                        if (string.IsNullOrWhiteSpace(text)) return 0;
                        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number)) return number;
                        break;
                }
                throw new NightlyReportValueException("Cannot convert the " + what + " value \"" + Str(node) + "\" to type System.Double.");
            }

            /// <summary>
            /// ConvertTo-UtcDate over a ConvertFrom-Json value: an ISO date-time string is the instant it names (no offset reads
            /// as UTC); other text is parsed by DateTimeOffset (round-trip kind; no offset is local); null when empty or unreadable.
            /// </summary>
            public static DateTime? UtcDate(JsonNode node)
            {
                if (node == null) return null;
                var text = Str(node);
                if (text.Length == 0) return null;
                if (IsString(node) && IsoDateTime.IsMatch(text))
                {
                    var hasZone = text.EndsWith("Z", StringComparison.Ordinal) || text.Length > 19 && (text[text.Length - 6] == '+' || text[text.Length - 6] == '-');
                    if (hasZone && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var zoned)) return zoned.UtcDateTime;
                    if (!hasZone && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var plain))
                        return DateTime.SpecifyKind(plain, DateTimeKind.Utc);
                }
                try
                {
                    return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime;
                }
                catch (Exception e) when (e is FormatException || e is ArgumentException)
                {
                    return null;
                }
            }

            /// <summary>A date as a sortable node (null stays null).</summary>
            public static JsonNode DateNode(DateTime? value) => value == null ? null : JsonValue.Create(value.Value.Ticks);

            /// <summary>Sort-Object's order of two values: null first, numbers by value, anything else as text ignoring case.</summary>
            public static int CompareValues(JsonNode first, JsonNode second)
            {
                var firstNull = Kind(first) == JsonValueKind.Null;
                var secondNull = Kind(second) == JsonValueKind.Null;
                if (firstNull || secondNull) return firstNull == secondNull ? 0 : firstNull ? -1 : 1;
                if (Kind(first) == JsonValueKind.Number && Kind(second) == JsonValueKind.Number
                    && decimal.TryParse(first.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(second.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                    return a.CompareTo(b);
                return string.Compare(Str(first), Str(second), CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);
            }

            /// <summary>Sort-Object (stable): by key, ascending or descending; nulls sort first ascending and last descending.</summary>
            public static IEnumerable<JsonNode> StableSort(IEnumerable<JsonNode> items, Func<JsonNode, JsonNode> key, Comparison<JsonNode> compare, bool descending)
            {
                var keyed = items.Select((item, index) => (Item: item, Key: key(item), Index: index)).ToList();
                keyed.Sort((x, y) =>
                {
                    var order = compare(x.Key, y.Key);
                    if (descending) order = -order;
                    return order != 0 ? order : x.Index.CompareTo(y.Index);
                });
                return keyed.Select(entry => entry.Item);
            }
        }
    }
}
