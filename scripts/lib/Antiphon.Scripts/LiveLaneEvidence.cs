// CARD-1545 completed-row evidence scan (antiphon-scripts live-lane-evidence <case> [context]).
// Markers are required in the exact TRX test result and in the receipt and public summary.
// Console, HTML and other row files are scanned only for sentinel absence.
// Exit 0 prints "EVIDENCE-SCAN clean"; exit 2 prints "INVALID/evidence-sanitization reason=<reason>".
// Unlike the other antiphon-scripts commands this one never exits NotHandled: there is no PowerShell
// path to fall back to, and any unexpected failure fails closed with reason=unreadable.
//
// Each guard carries a name. LiveLaneEvidenceTests proves every named guard by running the scan with
// that one guard mutated (Run's mutants argument) and showing the outcome changes, then healthy again.
// The command line cannot pass mutants; only an in-process caller can.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;

namespace Antiphon.Scripts
{
    public static class LiveLaneEvidence
    {
        public const string ContextVariable = "ANTIPHON_LIVE_EVIDENCE_CONTEXT";
        public static readonly string[] Cases = { "Scan", "Publish", "CompletedRows", "RunPortableRows", "Roster", "OuterPublish" };

        // The guards a test may mutate. A name outside this set is refused, so a typo cannot make a proof vacuous.
        public static readonly IReadOnlyCollection<string> Guards = new HashSet<string>(StringComparer.Ordinal)
        {
            "context-present", "decode", "row-recursive", "console-absence-only", "row-sentinel",
            "zero-case", "duplicate-uid", "producer-running", "child-red", "stale-source", "stale-build", "cross-run",
            "missing-trx", "zero-summary", "zero-results", "marker-missing", "unreadable", "empty-artifact",
            "receipt-marker", "summary-marker", "report-deferred", "publish-after-scan",
            "context-write", "artifact-read", "receipt-write", "summary-write", "publish-write",
            "wait75", "nonzero-exit", "final-report", "final-report-source", "scanner-before-publish", "sentinel-floor"
        };

        private const long MaxText = 8388608;
        private static readonly string[] SentinelFragments =
        {
            "SENTINEL-LINK-3a7c132f",
            "SENTINEL-SCREEN-3a7c132f",
            "SENTINEL-TOKEN-3a7c132f",
            "SENTINEL-DEVICE-3a7c132f"
        };

        public static string[] PortableRoster() => new[]
        {
            "control-success EvidenceIsSanitized CP-20",
            "control-assertion EvidenceIsSanitized CP-20",
            "control-skip EvidenceIsSanitized CP-20",
            "off-success EvidenceIsSanitized CP-20",
            "off-assertion EvidenceIsSanitized CP-20",
            "off-skip EvidenceIsSanitized CP-20",
            "slash-success EvidenceIsSanitized CP-20",
            "slash-assertion EvidenceIsSanitized CP-20",
            "slash-skip EvidenceIsSanitized CP-20",
            "submit-success EvidenceIsSanitized CP-20",
            "submit-assertion EvidenceIsSanitized CP-20",
            "submit-skip EvidenceIsSanitized CP-20",
            "c514-success EvidenceIsSanitized CP-20",
            "c514-assertion EvidenceIsSanitized CP-20",
            "c514-skip EvidenceIsSanitized CP-20",
            "enc-lf EvidenceIsSanitized CP-20",
            "enc-crlf EvidenceIsSanitized CP-20",
            "enc-ansi EvidenceIsSanitized CP-20",
            "enc-osc8 EvidenceIsSanitized CP-20",
            "enc-json EvidenceIsSanitized CP-20",
            "enc-xml EvidenceIsSanitized CP-20",
            "enc-double EvidenceIsSanitized CP-20",
            "c680-composer C680EvidenceIsSanitized CP-26",
            "c680-draft C680EvidenceIsSanitized CP-26",
            "c680-last-screen C680EvidenceIsSanitized CP-26",
            "c680-transcript C680EvidenceIsSanitized CP-26",
            "c680-observations C680EvidenceIsSanitized CP-26",
            "c680-success C680EvidenceIsSanitized CP-26",
            "c680-assertion C680EvidenceIsSanitized CP-26",
            "c680-skip C680EvidenceIsSanitized CP-26",
            "c680-exception C680EvidenceIsSanitized CP-26"
        };

        /// <summary>The command line entry: the context argument falls back to ANTIPHON_LIVE_EVIDENCE_CONTEXT.</summary>
        public static int RunCommand(string caseName, string context)
        {
            if (string.IsNullOrWhiteSpace(context)) context = Environment.GetEnvironmentVariable(ContextVariable);
            return Run(caseName, context, Console.Out, null);
        }

        /// <summary>Runs one case and writes its answer lines. mutants is for LiveLaneEvidenceTests only.</summary>
        public static int Run(string caseName, string context, TextWriter output, IEnumerable<string> mutants)
        {
            var scanner = new Scanner(mutants);
            if (caseName == "Roster")
            {
                foreach (var row in PortableRoster()) output.WriteLine(row);
                return 0;
            }
            try
            {
                scanner.RunCase(caseName, context, output);
                throw new EvidenceExit(2, "unreadable");
            }
            catch (EvidenceExit exit)
            {
                output.WriteLine(exit.Code == 0 ? "EVIDENCE-SCAN clean" : "INVALID/evidence-sanitization reason=" + exit.Reason);
                return exit.Code;
            }
            catch (Exception)
            {
                output.WriteLine("INVALID/evidence-sanitization reason=unreadable");
                return 2;
            }
        }

        private sealed class EvidenceExit : Exception
        {
            public EvidenceExit(int code, string reason) : base(reason) { Code = code; Reason = reason; }
            public int Code { get; }
            public string Reason { get; }
        }

        private sealed class Producer
        {
            public string Id = "", State = "", Exit = "", Source = "", Build = "", BuildCommit = "", RunId = "";
            public string Trx = "", Console = "", RowDir = "";
        }

        private sealed class EvidenceCase
        {
            public string Case = "", Test = "", Cp = "";
        }

        private sealed class Ctx
        {
            public string Source = "", CheckpointRun = "", ResultsRoot = "", EvidenceRoot = "", Repo = "";
            public string Receipt = "", Summary = "", PublicSummary = "", Report = "", Fault = "", Phase = "", RawExit = "";
            public bool RequirePublic, RequireReport;
            public List<EvidenceCase> Cases = new();
            public List<string> Sentinels = new();
            public List<Producer> Producers = new();
            public List<(string Id, string Path)> Outputs = new();
        }

        private sealed class TextRead
        {
            public bool Ok;
            public string Reason = "";
            public string Text = "";
        }

        private sealed class TrxMap
        {
            public Dictionary<string, List<string>> Defs = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<string>> Results = new(StringComparer.OrdinalIgnoreCase);
            public int Executed;
        }

        private sealed class Scanner
        {
            private readonly HashSet<string> _mutants = new(StringComparer.Ordinal);
            private bool _malformed;

            public Scanner(IEnumerable<string> mutants)
            {
                foreach (var name in mutants ?? Array.Empty<string>())
                {
                    if (!Guards.Contains(name)) throw new ArgumentException("unknown evidence guard " + name, nameof(mutants));
                    _mutants.Add(name);
                }
            }

            private bool Mutated(string guard) => _mutants.Contains(guard);

            private static bool Same(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

            private static EvidenceExit Fail(string reason) => new(2, reason);

            public void RunCase(string caseName, string context, TextWriter output)
            {
                if (caseName == "Scan" || caseName == "Publish")
                {
                    var ctx = ReadContextFile(context);
                    if (caseName == "Scan")
                    {
                        var reason = Scan(ctx);
                        if (reason.Length > 0) throw Fail(reason);
                        throw new EvidenceExit(0, "clean");
                    }
                    var published = Publish(ctx);
                    if (published.Length > 0) throw Fail(published);
                    output.WriteLine("publicationCount=1");
                    throw new EvidenceExit(0, "clean");
                }
                if (caseName == "CompletedRows")
                {
                    var ctx = ReadContextFile(context);
                    if (ctx.Outputs.Count == 0) throw Fail("missing-trx");
                    var scan = FromRun(ctx);
                    // GUARD report-deferred: CP-29 runs before the final report exists.
                    scan.RequireReport = Mutated("report-deferred");
                    scan.Report = ctx.Report;
                    var reason = Scan(scan);
                    if (reason.Length > 0) throw Fail(reason);
                    throw new EvidenceExit(0, "clean");
                }
                if (caseName == "OuterPublish")
                {
                    OuterPublish(ReadContextFile(context), output);
                    return;
                }
                if (caseName == "RunPortableRows") throw Fail("sanitizer-absent");
                throw Fail("usage");
            }

            private void OuterPublish(Ctx ctx, TextWriter output)
            {
                var gate = PublicationGate(ctx);
                if (gate.Length > 0) throw Fail(gate);
                var stateReady = false;
                if (!string.IsNullOrWhiteSpace(ctx.CheckpointRun) && !string.IsNullOrWhiteSpace(ctx.ResultsRoot))
                    stateReady = File.Exists(Path.Combine(ctx.ResultsRoot, ctx.CheckpointRun, "state.json"));
                var scan = ctx;
                if (stateReady || ctx.Producers.Count == 0)
                {
                    if (ctx.Outputs.Count == 0) throw Fail("missing-trx");
                    scan = FromRun(ctx);
                    scan.RequireReport = false;
                    scan.Report = "";
                }
                var reason = Scan(scan);
                if (reason.Length > 0 && !Mutated("scanner-before-publish")) throw Fail(reason); // GUARD scanner-before-publish
                var report = ctx.Report;
                if (string.IsNullOrWhiteSpace(report))
                    report = Path.Combine(ctx.ResultsRoot, ctx.CheckpointRun, "report.json");
                if ((string.IsNullOrWhiteSpace(report) || !File.Exists(report)) && !Mutated("final-report"))
                    throw Fail("report-missing"); // GUARD final-report
                var reportText = File.ReadAllText(report);
                if (!Regex.IsMatch(reportText, Regex.Escape(ctx.Source), RegexOptions.IgnoreCase) && !Mutated("final-report-source"))
                    throw Fail("stale-source"); // GUARD final-report-source
                var written = WritePublicSummary(ctx);
                if (written.Length > 0) throw Fail(written);
                output.WriteLine("publicationCount=1");
                throw new EvidenceExit(0, "clean");
            }

            // The scan object CompletedRows and OuterPublish build: producers come from the checkpoint run's state.
            private Ctx FromRun(Ctx ctx)
            {
                var producers = new List<Producer>();
                foreach (var (id, path) in ctx.Outputs) producers.Add(ProducerFromRun(ctx, id, path));
                return new Ctx
                {
                    Source = ctx.Source,
                    CheckpointRun = ctx.CheckpointRun,
                    ResultsRoot = ctx.ResultsRoot,
                    Cases = ctx.Cases,
                    Sentinels = ctx.Sentinels,
                    Producers = producers,
                    Receipt = ctx.Receipt,
                    Summary = ctx.Summary,
                    PublicSummary = ctx.PublicSummary,
                    RequirePublic = ctx.RequirePublic
                };
            }

            private Ctx ReadContextFile(string path)
            {
                if ((string.IsNullOrWhiteSpace(path) || !File.Exists(path)) && !Mutated("context-present"))
                    throw Fail("context-absent"); // GUARD context-present
                var info = new FileInfo(path);
                if (info.Length == 0 || info.Length > MaxText) throw Fail("unreadable");
                string raw;
                try { raw = File.ReadAllText(path); }
                catch (Exception) { throw Fail("unreadable"); }
                if (string.IsNullOrWhiteSpace(raw)) throw Fail("empty-artifact");
                try { return ParseContext(raw); }
                catch (Exception) { throw Fail("unreadable"); }
            }

            private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
            private static readonly JsonDocumentOptions DocumentOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

            private static JsonNode ParseJson(string text) => JsonNode.Parse(text, NodeOptions, DocumentOptions);

            private static JsonNode Prop(JsonNode node, string name) =>
                node is JsonObject obj && obj.TryGetPropertyValue(name, out var value) ? value : null;

            // PowerShell's [string] of a converted JSON value.
            private static string Str(JsonNode node)
            {
                if (node is null) return "";
                if (node is JsonValue value)
                {
                    if (value.TryGetValue<string>(out var s)) return s;
                    if (value.TryGetValue<bool>(out var b)) return b ? "True" : "False";
                    if (value.TryGetValue<long>(out var l)) return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (value.TryGetValue<double>(out var d)) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return value.ToJsonString();
                }
                if (node is JsonArray array) return string.Join(" ", array.Select(Str));
                return node.ToJsonString();
            }

            // PowerShell's "$value -eq $true".
            private static bool IsTrue(JsonNode node)
            {
                if (node is not JsonValue value) return false;
                if (value.TryGetValue<bool>(out var b)) return b;
                if (value.TryGetValue<string>(out var s)) return string.Equals(s, "True", StringComparison.OrdinalIgnoreCase);
                if (value.TryGetValue<double>(out var d)) return d == 1;
                return false;
            }

            // PowerShell's @($value) with the nulls dropped.
            private static List<JsonNode> Items(JsonNode node)
            {
                var items = new List<JsonNode>();
                if (node is null) return items;
                if (node is JsonArray array) { foreach (var item in array) if (item is not null) items.Add(item); }
                else items.Add(node);
                return items;
            }

            private static Ctx ParseContext(string raw)
            {
                var node = ParseJson(raw);
                var ctx = new Ctx
                {
                    Source = Str(Prop(node, "source")),
                    CheckpointRun = Str(Prop(node, "checkpointRun")),
                    ResultsRoot = Str(Prop(node, "resultsRoot")),
                    EvidenceRoot = Str(Prop(node, "evidenceRoot")),
                    Repo = Str(Prop(node, "repo")),
                    Receipt = Str(Prop(node, "receipt")),
                    Summary = Str(Prop(node, "summary")),
                    PublicSummary = Str(Prop(node, "publicSummary")),
                    Report = Str(Prop(node, "report")),
                    Fault = Str(Prop(node, "fault")),
                    Phase = Str(Prop(node, "phase")),
                    RawExit = Str(Prop(node, "rawExit")),
                    RequirePublic = IsTrue(Prop(node, "requirePublic")),
                    RequireReport = IsTrue(Prop(node, "requireReport"))
                };
                foreach (var item in Items(Prop(node, "cases")))
                    ctx.Cases.Add(new EvidenceCase { Case = Str(Prop(item, "case")), Test = Str(Prop(item, "test")), Cp = Str(Prop(item, "cp")) });
                foreach (var item in Items(Prop(node, "sentinels")))
                    ctx.Sentinels.Add(Str(item));
                foreach (var item in Items(Prop(node, "producers")))
                {
                    ctx.Producers.Add(new Producer
                    {
                        Id = Str(Prop(item, "id")),
                        State = Str(Prop(item, "state")),
                        Exit = Str(Prop(item, "exit")),
                        Source = Str(Prop(item, "source")),
                        Build = Str(Prop(item, "build")),
                        BuildCommit = Str(Prop(item, "buildCommit")),
                        RunId = Str(Prop(item, "runId")),
                        Trx = Str(Prop(item, "trx")),
                        Console = Str(Prop(item, "console")),
                        RowDir = Str(Prop(item, "rowDir"))
                    });
                }
                foreach (var item in Items(Prop(node, "outputs")))
                    ctx.Outputs.Add((Str(Prop(item, "id")), Str(Prop(item, "path"))));
                return ctx;
            }

            private static readonly Regex AnsiCsi = new("\u001b\\[[0-9;?]*[ -/]*[@-~]", RegexOptions.CultureInvariant);
            private static readonly Regex Osc8 = new("\u001b\\]8;;(.*?)(?:\u001b\\\\|\u0007)", RegexOptions.CultureInvariant);
            private static readonly Regex JsonEscape = new("\\\\u([0-9A-Fa-f]{4})", RegexOptions.CultureInvariant);
            private static readonly Regex PercentEscape = new("%([0-9A-Fa-f]{2})", RegexOptions.CultureInvariant);
            private static readonly Regex StrayPercent = new("%(?![0-9A-Fa-f]{2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            private static readonly Regex SentinelWord = new("SENTINEL-", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            private string ExpandEvidenceText(string text)
            {
                _malformed = false;
                if (text is null) return "";
                text = text.Replace("\r\n", "\n").Replace("\r", "\n");
                if (Mutated("decode")) return text; // GUARD decode
                var decoded = Osc8.Replace(text, "$1");
                decoded = AnsiCsi.Replace(decoded, "");
                for (var pass = 0; pass < 3; pass++)
                {
                    decoded = System.Net.WebUtility.HtmlDecode(decoded);
                    decoded = JsonEscape.Replace(decoded, m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
                    decoded = PercentEscape.Replace(decoded, m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
                }
                if (StrayPercent.IsMatch(decoded) && SentinelWord.IsMatch(decoded)) _malformed = true;
                return decoded;
            }

            private bool TestSentinel(string text, IReadOnlyList<string> needles)
            {
                var expanded = ExpandEvidenceText(text);
                foreach (var form in new[] { text ?? "", expanded })
                    foreach (var needle in needles)
                        if (form.Contains(needle, StringComparison.Ordinal)) return true;
                return false;
            }

            private static TextRead ReadRequiredText(string path, string missingReason)
            {
                if (string.IsNullOrWhiteSpace(path) || !(File.Exists(path) || Directory.Exists(path)))
                    return new TextRead { Reason = missingReason };
                if (Directory.Exists(path)) return new TextRead { Reason = "unreadable" };
                var length = new FileInfo(path).Length;
                if (length > MaxText) return new TextRead { Reason = "unreadable" };
                if (length == 0) return new TextRead { Reason = "empty-artifact" };
                try { return new TextRead { Ok = true, Text = File.ReadAllText(path) }; }
                catch (Exception) { return new TextRead { Reason = "unreadable" }; }
            }

            private static readonly Regex ConsoleSucceeded = new(@"(?m)^\s*succeeded:\s*[1-9]", RegexOptions.CultureInvariant);

            private static TrxMap ReadTrx(string trxText)
            {
                var xml = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
                xml.LoadXml(trxText);
                var map = new TrxMap();
                foreach (XmlElement node in xml.SelectNodes("//*[local-name()=\"UnitTest\"]"))
                {
                    var id = node.GetAttribute("id");
                    var method = node.SelectSingleNode(".//*[local-name()=\"TestMethod\"]") as XmlElement;
                    var name = method?.GetAttribute("name") ?? "";
                    if (!map.Defs.TryGetValue(id, out var names)) map.Defs[id] = names = new List<string>();
                    names.Add(name);
                }
                foreach (XmlElement node in xml.SelectNodes("//*[local-name()=\"UnitTestResult\"]"))
                {
                    var id = node.GetAttribute("testId");
                    var stdout = node.SelectSingleNode(".//*[local-name()=\"StdOut\"]");
                    var message = node.SelectSingleNode(".//*[local-name()=\"Message\"]");
                    var body = "";
                    if (stdout is not null) body += stdout.InnerText;
                    if (message is not null) body += "\n" + message.InnerText;
                    if (!map.Results.TryGetValue(id, out var bodies)) map.Results[id] = bodies = new List<string>();
                    bodies.Add(body);
                }
                if (xml.SelectSingleNode("//*[local-name()=\"Counters\"]") is XmlElement counters)
                {
                    var raw = counters.GetAttribute("executed");
                    if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var executed)) map.Executed = executed;
                }
                return map;
            }

            private List<string> Needles(Ctx ctx)
            {
                var custom = ctx.Sentinels.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
                if (Mutated("sentinel-floor") && custom.Count > 0) return custom; // GUARD sentinel-floor
                var needles = new List<string>(SentinelFragments);
                needles.AddRange(custom);
                return needles;
            }

            private string PublicationFault(Ctx ctx)
            {
                foreach (var (fault, reason) in new[]
                {
                    ("context-write", "write-fault"), ("artifact-read", "unreadable"), ("receipt-write", "write-fault"),
                    ("summary-write", "write-fault"), ("publish-write", "write-fault")
                })
                {
                    if (Same(ctx.Fault, fault) && !Mutated(fault)) return reason; // GUARD <fault>
                }
                return "";
            }

            private string PublicationGate(Ctx ctx)
            {
                if (Same(ctx.Phase, "wait75") && !Mutated("wait75")) return "wait75"; // GUARD wait75
                var rawExit = 0;
                if (!string.IsNullOrWhiteSpace(ctx.RawExit)) int.TryParse(ctx.RawExit, out rawExit);
                if ((rawExit == 1 || rawExit == 2 || rawExit == 3 || rawExit == 8) && !Mutated("nonzero-exit")) return "nonzero-exit"; // GUARD nonzero-exit
                return PublicationFault(ctx);
            }

            private string Scan(Ctx ctx)
            {
                var cases = ctx.Cases;
                if (cases.Count == 0 && !Mutated("zero-case")) return "zero-case"; // GUARD zero-case
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in cases)
                {
                    if (!seen.Add(item.Case + "|" + item.Test) && !Mutated("duplicate-uid")) return "duplicate-uid"; // GUARD duplicate-uid
                }
                var needles = Needles(ctx);
                var maps = new Dictionary<string, TrxMap>(StringComparer.OrdinalIgnoreCase);
                if (ctx.Producers.Count == 0) return "missing-trx";
                foreach (var producer in ctx.Producers)
                {
                    if (!Same(producer.State, "green") && !Same(producer.State, "red") && !Mutated("producer-running")) return "producer-running"; // GUARD producer-running
                    var exitCode = 0;
                    if (!string.IsNullOrWhiteSpace(producer.Exit)) int.TryParse(producer.Exit, out exitCode);
                    if ((Same(producer.State, "red") || exitCode != 0) && !Mutated("child-red")) return "child-red"; // GUARD child-red
                    if (!Same(producer.Source, ctx.Source) && !Mutated("stale-source")) return "stale-source"; // GUARD stale-source
                    if ((!Same(producer.Build, "verified") || !Same(producer.BuildCommit, ctx.Source)) && !Mutated("stale-build")) return "stale-build"; // GUARD stale-build
                    if ((!Same(producer.RunId, ctx.CheckpointRun) || string.IsNullOrWhiteSpace(ctx.CheckpointRun)) && !Mutated("cross-run")) return "cross-run"; // GUARD cross-run
                    var trxRead = ReadRequiredText(producer.Trx, "missing-trx");
                    if (!trxRead.Ok) return Mutated("missing-trx") ? "" : trxRead.Reason; // GUARD missing-trx
                    var consoleRead = ReadRequiredText(producer.Console, "missing-trx");
                    if (!consoleRead.Ok) return consoleRead.Reason;
                    if (!ConsoleSucceeded.IsMatch(consoleRead.Text) && !Mutated("zero-summary")) return "zero-summary"; // GUARD zero-summary
                    // GUARD console-absence-only: the console is scanned for sentinels only, never for markers.
                    if (Mutated("console-absence-only") && !Regex.IsMatch(consoleRead.Text, "C1545-EVIDENCE", RegexOptions.IgnoreCase)) return "marker-missing";
                    var map = ReadTrx(trxRead.Text);
                    if (map.Executed < 1 && !Mutated("zero-results")) return "zero-results"; // GUARD zero-results
                    maps[producer.Id] = map;
                    if (string.IsNullOrWhiteSpace(producer.RowDir) || !Directory.Exists(producer.RowDir)) return "missing-trx";
                    List<string> files;
                    try
                    {
                        // GUARD row-recursive: every nested row file is scanned.
                        var depth = Mutated("row-recursive") ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
                        files = Directory.EnumerateFileSystemEntries(producer.RowDir, "*", depth).ToList();
                    }
                    catch (Exception) { return "unreadable"; }
                    foreach (var file in files)
                    {
                        if (TestSentinel(Path.GetFileName(file), needles)) return "sentinel-present";
                        if (Directory.Exists(file)) continue;
                        var length = new FileInfo(file).Length;
                        if (length > MaxText) return "unreadable";
                        if (length == 0) continue;
                        string payload;
                        try { payload = File.ReadAllText(file); }
                        catch (Exception) { return "unreadable"; }
                        if (TestSentinel(payload, needles) && !Mutated("row-sentinel")) return "sentinel-present"; // GUARD row-sentinel
                        if (_malformed) return "encoded-sentinel";
                    }
                }
                foreach (var item in cases)
                {
                    var marker = "C1545-EVIDENCE case=" + item.Case + " test=" + item.Test + " kind=";
                    if (!maps.TryGetValue(item.Cp, out var map)) return "marker-missing";
                    var hits = 0;
                    foreach (var (id, names) in map.Defs)
                    {
                        if (!names.Any(name => Same(name, item.Test))) continue;
                        if (names.Count > 1) return "duplicate-uid";
                        var bodies = map.Results.TryGetValue(id, out var found) ? found : new List<string>();
                        if (bodies.Count > 1) return "duplicate-uid";
                        foreach (var body in bodies) if (body.Contains(marker, StringComparison.Ordinal)) hits++;
                    }
                    if (hits != 1 && !Mutated("marker-missing")) return "marker-missing"; // GUARD marker-missing
                }
                var receipt = ReadRequiredText(ctx.Receipt, "missing-trx");
                if (!receipt.Ok && receipt.Reason == "unreadable") return Mutated("unreadable") ? "" : "unreadable"; // GUARD unreadable
                if (!receipt.Ok) return Mutated("empty-artifact") ? "" : "empty-artifact"; // GUARD empty-artifact
                var summary = ReadRequiredText(ctx.Summary, "missing-trx");
                if (!summary.Ok && summary.Reason == "unreadable") return "unreadable";
                if (!summary.Ok) return "empty-artifact";
                if (TestSentinel(receipt.Text, needles)) return "sentinel-present";
                if (_malformed) return "encoded-sentinel";
                if (TestSentinel(summary.Text, needles)) return "sentinel-present";
                if (_malformed) return "encoded-sentinel";
                foreach (var item in cases)
                {
                    var marker = "C1545-EVIDENCE case=" + item.Case + " test=" + item.Test + " ";
                    if (!receipt.Text.Contains(marker, StringComparison.Ordinal) && !Mutated("receipt-marker")) return "receipt-marker-missing"; // GUARD receipt-marker
                    if (!summary.Text.Contains(marker, StringComparison.Ordinal) && !Mutated("summary-marker")) return "summary-marker-missing"; // GUARD summary-marker
                }
                if (ctx.RequirePublic)
                {
                    var publicRead = ReadRequiredText(ctx.PublicSummary, "missing-trx");
                    if (!publicRead.Ok) return "empty-artifact";
                    if (TestSentinel(publicRead.Text, needles)) return "sentinel-present";
                    if (_malformed) return "encoded-sentinel";
                    foreach (var item in cases)
                    {
                        var marker = "C1545-EVIDENCE case=" + item.Case + " test=" + item.Test + " ";
                        if (!publicRead.Text.Contains(marker, StringComparison.Ordinal)) return "summary-marker-missing";
                    }
                }
                if (ctx.RequireReport)
                {
                    if (string.IsNullOrWhiteSpace(ctx.Report) || !File.Exists(ctx.Report)) return "report-missing";
                    var reportText = ReadRequiredText(ctx.Report, "report-missing");
                    if (!reportText.Ok) return "report-missing";
                    if (!Regex.IsMatch(reportText.Text, Regex.Escape(ctx.Source), RegexOptions.IgnoreCase)) return "stale-source";
                }
                return BoundEvidenceTree(ctx, needles);
            }

            private string BoundEvidenceTree(Ctx ctx, IReadOnlyList<string> needles)
            {
                var roots = new List<string>();
                if (!string.IsNullOrWhiteSpace(ctx.EvidenceRoot)) roots.Add(ctx.EvidenceRoot);
                if (!string.IsNullOrWhiteSpace(ctx.ResultsRoot) && !string.IsNullOrWhiteSpace(ctx.CheckpointRun))
                    roots.Add(Path.Combine(ctx.ResultsRoot, ctx.CheckpointRun));
                foreach (var directory in roots)
                {
                    var reason = EvidenceTree(directory, needles);
                    if (reason.Length > 0) return reason;
                }
                return "";
            }

            private static string ReadBoundedText(string path, long length)
            {
                if (length <= MaxText) return File.ReadAllText(path);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buffer = new byte[MaxText];
                var read = stream.Read(buffer, 0, buffer.Length);
                return Encoding.UTF8.GetString(buffer, 0, read);
            }

            private string EvidenceTree(string directory, IReadOnlyList<string> needles)
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return "";
                List<string> entries;
                try { entries = Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories).ToList(); }
                catch (Exception) { return "unreadable"; }
                foreach (var entry in entries)
                {
                    if (TestSentinel(Path.GetFileName(entry), needles)) return "sentinel-present";
                    if (Directory.Exists(entry)) continue;
                    var length = new FileInfo(entry).Length;
                    if (length == 0) continue;
                    string payload;
                    try { payload = ReadBoundedText(entry, length); }
                    catch (Exception) { return "unreadable"; }
                    if (TestSentinel(payload, needles)) return "sentinel-present";
                    if (_malformed) return "encoded-sentinel";
                }
                return "";
            }

            private static string WritePublicSummary(Ctx ctx)
            {
                var target = ctx.PublicSummary;
                if (string.IsNullOrWhiteSpace(target)) return "write-fault";
                var parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrWhiteSpace(parent) && !Directory.Exists(parent) && !File.Exists(parent))
                    Directory.CreateDirectory(parent);
                if (Directory.Exists(target)) return "write-fault";
                var summary = ReadRequiredText(ctx.Summary, "empty-artifact");
                if (!summary.Ok) return "write-fault";
                try { File.WriteAllText(target, summary.Text); }
                catch (Exception) { return "write-fault"; }
                return File.Exists(target) ? "" : "write-fault";
            }

            private string Publish(Ctx ctx)
            {
                var gate = PublicationGate(ctx);
                if (gate.Length > 0) return gate;
                var reason = Scan(ctx);
                if (reason.Length > 0 && !Mutated("publish-after-scan")) return reason; // GUARD publish-after-scan
                return WritePublicSummary(ctx);
            }

            private static readonly Regex CommitField = new("commit=([0-9a-f]{40})", RegexOptions.CultureInvariant);
            private static readonly Regex BuildSourceField = new("buildSource=([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant);

            private static Producer ProducerFromRun(Ctx ctx, string rowId, string outputRelative)
            {
                var run = ctx.CheckpointRun;
                if (string.IsNullOrWhiteSpace(run) || string.IsNullOrWhiteSpace(ctx.ResultsRoot)) throw Fail("cross-run");
                var runDir = Path.Combine(ctx.ResultsRoot, run);
                var stateRead = ReadRequiredText(Path.Combine(runDir, "state.json"), "missing-trx");
                if (!stateRead.Ok) throw Fail(stateRead.Reason);
                var state = ParseJson(stateRead.Text);
                if (!Same(Str(Prop(state, "runId")), run)) throw Fail("cross-run");
                JsonNode row = null;
                foreach (var candidate in Items(Prop(state, "rows")))
                    if (Same(Str(Prop(candidate, "id")), rowId)) row = candidate;
                if (row is null) throw Fail("producer-running");
                var line = Str(Prop(row, "line"));
                var commit = CommitField.Match(line);
                var buildSource = BuildSourceField.Match(line);
                var binding = ReadRequiredText(Path.Combine(ctx.Repo, outputRelative, "checkpoint-build-source.json"), "stale-build");
                var buildCommit = "";
                if (binding.Ok)
                {
                    try { buildCommit = Str(Prop(ParseJson(binding.Text), "Commit")); }
                    catch (Exception) { throw Fail("stale-build"); }
                }
                var rowDir = Path.Combine(runDir, "rows", rowId);
                return new Producer
                {
                    Id = rowId,
                    State = Str(Prop(row, "state")),
                    Exit = Str(Prop(row, "exitCode")),
                    Source = commit.Success ? commit.Groups[1].Value : "",
                    Build = buildSource.Success ? buildSource.Groups[1].Value : "",
                    BuildCommit = buildCommit,
                    RunId = run,
                    Trx = Path.Combine(rowDir, "run.trx"),
                    Console = Path.Combine(rowDir, "console.log"),
                    RowDir = rowDir
                };
            }
        }
    }
}
