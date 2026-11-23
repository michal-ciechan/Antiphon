// The row loop of Read-NightlyTrxIdentities (nightly-coverage.ps1). The file checks and the [xml] load (and so
// the malformed-TRX message) stay in PowerShell; this walks the loaded document and returns exactly the rows the
// PowerShell loop built, or the refusal it would throw. It keeps PowerShell's comparisons: @{} keys are
// OrdinalIgnoreCase, LocalName -eq 'TestMethod' ignores case, -cne 'Output' / -ceq 'DebugTrace' do not, and only
// the inner loop breaks at the first DebugTrace (a later Output element still replaces it).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace Antiphon.Nightly
{
    public static class NightlyTrx
    {
        private sealed class Definition
        {
            public string ClassName;
            public string MethodName;
        }

        private static bool PsEquals(string a, string b, bool ignoreCase) =>
            CultureInfo.InvariantCulture.CompareInfo.Compare(a, b, ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None) == 0;

        private static XmlNodeList Select(XmlDocument doc, XmlNamespaceManager ns, bool qualified, string qualifiedPath, string barePath) =>
            qualified ? doc.SelectNodes(qualifiedPath, ns) : doc.SelectNodes(barePath);

        /// <summary>
        /// The identity rows of a loaded TRX, or the refusal Read-NightlyTrxIdentities would throw (Error, empty when
        /// none; Rows then holds the rows read before it). Plain records: no PowerShell types.
        /// </summary>
        public static TrxIdentityRead Identities(XmlDocument doc)
        {
            var nsm = doc.DocumentElement == null ? "" : doc.DocumentElement.NamespaceURI;
            var qualified = !string.IsNullOrEmpty(nsm);
            var ns = new XmlNamespaceManager(doc.NameTable);
            if (qualified) ns.AddNamespace("t", nsm);
            var definitions = new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlNode node in Select(doc, ns, qualified, "//t:TestDefinitions/t:UnitTest", "//TestDefinitions/UnitTest"))
            {
                var unit = (XmlElement)node;
                var id = unit.GetAttribute("id");
                if (string.IsNullOrEmpty(id)) continue;
                XmlElement method = null;
                foreach (XmlNode child in unit.ChildNodes)
                {
                    if (PsEquals(child.LocalName, "TestMethod", true)) { method = child as XmlElement; break; }
                }
                string className = null;
                string methodName = null;
                if (method != null)
                {
                    className = method.GetAttribute("className");
                    methodName = method.GetAttribute("name");
                }
                definitions[id] = new Definition { ClassName = className, MethodName = methodName };
            }
            var results = Select(doc, ns, qualified, "//t:Results/t:UnitTestResult", "//Results/UnitTestResult");
            if (results.Count == 0) results = Select(doc, ns, qualified, "//t:UnitTestResult", "//UnitTestResult");
            var rows = new List<TrxIdentity>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var error = "";
            foreach (XmlNode node in results)
            {
                var n = (XmlElement)node;
                var testId = n.GetAttribute("testId");
                var outcome = n.GetAttribute("outcome");
                var display = n.GetAttribute("testName");
                Definition def = null;
                if (!string.IsNullOrEmpty(testId)) definitions.TryGetValue(testId, out def);
                if (def == null) { error = "missing definition for testId=" + testId + " display=" + display; break; }
                if (!seen.Add(testId)) { error = "duplicate identity testId=" + testId; break; }
                var className = def.ClassName ?? "";
                var methodName = def.MethodName ?? "";
                if (string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(methodName))
                {
                    error = "unresolved identity testId=" + testId + " display=" + display;
                    break;
                }
                var debugTrace = "";
                foreach (XmlNode child in n.ChildNodes)
                {
                    if (!PsEquals(child.LocalName, "Output", false)) continue;
                    foreach (XmlNode outChild in child.ChildNodes)
                    {
                        if (PsEquals(outChild.LocalName, "DebugTrace", false))
                        {
                            debugTrace = outChild.InnerText ?? "";
                            break;
                        }
                    }
                }
                rows.Add(new TrxIdentity
                {
                    TestId = testId, ClassName = className, MethodName = methodName, Display = display, Outcome = outcome,
                    Identity = className + "." + methodName, DebugTrace = debugTrace,
                });
            }
            return new TrxIdentityRead { Error = error, Rows = rows.ToArray() };
        }

        /// <summary>
        /// Read-NightlyTrxIdentities: the identity rows of the TRX file at path. Refuses (NightlyRefusal) a missing file,
        /// anything but a regular file, XML that does not load, and a row the identity walk refuses.
        /// </summary>
        public static TrxIdentity[] ReadIdentities(string path)
        {
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path))) throw new NightlyRefusal("missing TRX " + path);
            if (TestPhaseFiles.Kind(path, out _) != "regular") throw new NightlyRefusal("refused TRX " + path);
            var doc = new XmlDocument();
            try { doc.LoadXml(File.ReadAllText(path)); }
            catch (Exception ex) when (ex is XmlException || ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new NightlyRefusal("malformed TRX " + path + ": " + ex.Message);
            }
            var read = Identities(doc);
            if (read.Error.Length > 0) throw new NightlyRefusal(read.Error);
            return read.Rows;
        }

        /// <summary>
        /// Read-NightlyTrxRunSummary over a loaded TRX: the ResultSummary outcome and its Counters. Not present
        /// (counters -1) without a ResultSummary/Counters element or without integer total, executed and notExecuted
        /// counters; passed and failed read 0 when they do not parse. Plain values: no PowerShell types.
        /// </summary>
        public static TrxRunSummaryRead RunSummary(XmlDocument doc)
        {
            if (!(doc?.SelectSingleNode("//*[local-name()=\"ResultSummary\"]") is XmlElement summary)) return TrxRunSummaryRead.Missing();
            if (!(summary.SelectSingleNode("./*[local-name()=\"Counters\"]") is XmlElement counters)) return TrxRunSummaryRead.Missing();
            int Read(string name, out bool ok)
            {
                ok = int.TryParse(counters.GetAttribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
                return value;
            }
            var total = Read("total", out var totalOk);
            var executed = Read("executed", out var executedOk);
            var notExecuted = Read("notExecuted", out var notOk);
            if (!totalOk || !executedOk || !notOk) return TrxRunSummaryRead.Missing();
            return new TrxRunSummaryRead
            {
                Present = true, Outcome = summary.GetAttribute("outcome"), Total = total, Executed = executed,
                Passed = Read("passed", out _), Failed = Read("failed", out _), NotExecuted = notExecuted,
            };
        }

        /// <summary>
        /// The class-cost refresh's read of a loaded TRX (ClassCostRefresh): one (class, seconds) pair per
        /// TestRun/Results/UnitTestResult whose testId names a TestRun/TestDefinitions/UnitTest with a TestMethod className,
        /// in result order. The class name is cut at its first '(' (a parameterised class); a result with no duration, or
        /// whose definition names no class, is left out. Elements are matched by local name, test ids ignoring case (a
        /// repeated definition id: the last wins). FormatException for a duration that is not a TimeSpan.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, double>> ClassDurations(XmlDocument doc)
        {
            var classById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<KeyValuePair<string, double>>();
            var run = doc?.DocumentElement;
            if (run == null || run.LocalName != "TestRun") return rows;
            foreach (var test in Children(run, "TestDefinitions").SelectMany(definitions => Children(definitions, "UnitTest")))
            {
                var name = Children(test, "TestMethod").Select(method => method.GetAttribute("className")).FirstOrDefault() ?? "";
                var paren = name.IndexOf('(');
                classById[test.GetAttribute("id")] = paren >= 0 ? name.Substring(0, paren) : name;
            }
            foreach (var result in Children(run, "Results").SelectMany(results => Children(results, "UnitTestResult")))
            {
                var duration = result.GetAttribute("duration");
                if (!classById.TryGetValue(result.GetAttribute("testId"), out var className)
                    || string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(duration)) continue;
                rows.Add(new KeyValuePair<string, double>(className, TimeSpan.Parse(duration, CultureInfo.InvariantCulture).TotalSeconds));
            }
            return rows;
        }

        private static IEnumerable<XmlElement> Children(XmlElement parent, string localName) =>
            parent.ChildNodes.OfType<XmlElement>().Where(child => child.LocalName == localName);

        /// <summary>Error is the refusal Read-NightlyTrxIdentities would throw (empty when none); Rows its rows.</summary>
        public static object IdentityRows(XmlDocument doc)
        {
            var read = Identities(doc);
            var rows = new object[read.Rows.Length];
            for (var i = 0; i < rows.Length; i++)
            {
                var source = read.Rows[i];
                rows[i] = PsValue.Record(("TestId", source.TestId), ("Uid", ""), ("ClassName", source.ClassName), ("MethodName", source.MethodName),
                    ("Display", source.Display), ("Outcome", source.Outcome), ("Identity", source.Identity), ("DebugTrace", source.DebugTrace));
            }
            return PsValue.Record(("Error", read.Error), ("Rows", rows));
        }

        /// <summary>Read-TestPhaseTrxRows' rows (test-phase-evidence.ps1): one
        /// {TestId, Outcome, Name, Duration, StdOut, Path} object per scanned row, nulls as "".</summary>
        public static object[] TimingRows(Antiphon.TestPhase.TrxTimingRead read, string path)
        {
            var rows = new object[read.Rows.Count];
            for (var i = 0; i < rows.Length; i++)
            {
                var source = read.Rows[i];
                rows[i] = PsValue.Record(("TestId", source.TestId ?? ""), ("Outcome", source.Outcome ?? ""), ("Name", source.Name ?? ""),
                    ("Duration", source.Duration ?? ""), ("StdOut", source.StdOut ?? ""), ("Path", path));
            }
            return rows;
        }
    }

    /// <summary>One TRX result row with its resolved identity (Read-NightlyTrxIdentities' row).</summary>
    public sealed class TrxIdentity
    {
        public string TestId { get; set; } = "";
        public string Uid { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string MethodName { get; set; } = "";
        public string Display { get; set; } = "";
        public string Outcome { get; set; } = "";
        public string Identity { get; set; } = "";
        public string DebugTrace { get; set; } = "";
    }

    public sealed class TrxIdentityRead
    {
        public string Error { get; set; } = "";
        public TrxIdentity[] Rows { get; set; } = new TrxIdentity[0];
    }

    /// <summary>The TRX ResultSummary (Read-NightlyTrxRunSummary): counters are -1 when it is not present.</summary>
    public sealed class TrxRunSummaryRead
    {
        public bool Present { get; set; }
        public string Outcome { get; set; } = "";
        public int Total { get; set; } = -1;
        public int Executed { get; set; } = -1;
        public int Passed { get; set; } = -1;
        public int Failed { get; set; } = -1;
        public int NotExecuted { get; set; } = -1;

        public static TrxRunSummaryRead Missing() => new TrxRunSummaryRead();
    }
}
