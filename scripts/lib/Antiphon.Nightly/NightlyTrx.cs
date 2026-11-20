// The row loop of Read-NightlyTrxIdentities (nightly-coverage.ps1). The file checks and the [xml] load (and so
// the malformed-TRX message) stay in PowerShell; this walks the loaded document and returns exactly the rows the
// PowerShell loop built, or the refusal it would throw. It keeps PowerShell's comparisons: @{} keys are
// OrdinalIgnoreCase, LocalName -eq 'TestMethod' ignores case, -cne 'Output' / -ceq 'DebugTrace' do not, and only
// the inner loop breaks at the first DebugTrace (a later Output element still replaces it).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management.Automation;
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

        /// <summary>Error is the refusal Read-NightlyTrxIdentities would throw (empty when none); Rows its rows.</summary>
        public static PSObject IdentityRows(XmlDocument doc)
        {
            var read = Identities(doc);
            var rows = new object[read.Rows.Length];
            for (var i = 0; i < rows.Length; i++)
            {
                var source = read.Rows[i];
                var row = new PSObject();
                row.Properties.Add(new PSNoteProperty("TestId", source.TestId));
                row.Properties.Add(new PSNoteProperty("Uid", ""));
                row.Properties.Add(new PSNoteProperty("ClassName", source.ClassName));
                row.Properties.Add(new PSNoteProperty("MethodName", source.MethodName));
                row.Properties.Add(new PSNoteProperty("Display", source.Display));
                row.Properties.Add(new PSNoteProperty("Outcome", source.Outcome));
                row.Properties.Add(new PSNoteProperty("Identity", source.Identity));
                row.Properties.Add(new PSNoteProperty("DebugTrace", source.DebugTrace));
                rows[i] = row;
            }
            var result = new PSObject();
            result.Properties.Add(new PSNoteProperty("Error", read.Error));
            result.Properties.Add(new PSNoteProperty("Rows", rows));
            return result;
        }

        /// <summary>Read-TestPhaseTrxRows' rows (test-phase-evidence.ps1): one
        /// {TestId, Outcome, Name, Duration, StdOut, Path} object per scanned row, nulls as "".</summary>
        public static object[] TimingRows(Antiphon.TestPhase.TrxTimingRead read, string path)
        {
            var rows = new object[read.Rows.Count];
            for (var i = 0; i < rows.Length; i++)
            {
                var source = read.Rows[i];
                var row = new PSObject();
                row.Properties.Add(new PSNoteProperty("TestId", source.TestId ?? ""));
                row.Properties.Add(new PSNoteProperty("Outcome", source.Outcome ?? ""));
                row.Properties.Add(new PSNoteProperty("Name", source.Name ?? ""));
                row.Properties.Add(new PSNoteProperty("Duration", source.Duration ?? ""));
                row.Properties.Add(new PSNoteProperty("StdOut", source.StdOut ?? ""));
                row.Properties.Add(new PSNoteProperty("Path", path));
                rows[i] = row;
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
}
