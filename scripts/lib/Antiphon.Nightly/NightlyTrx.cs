// The row loop of Read-NightlyTrxIdentities (nightly-coverage.ps1). The file checks and the [xml] load (and so
// the malformed-TRX message) stay in PowerShell; this walks the loaded document and returns exactly the rows the
// PowerShell loop built, or the refusal it would throw. It keeps PowerShell's comparisons: @{} keys are
// OrdinalIgnoreCase, LocalName -eq 'TestMethod' ignores case, -cne 'Output' / -ceq 'DebugTrace' do not, and only
// the inner loop breaks at the first DebugTrace (a later Output element still replaces it).
using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>Error is the refusal Read-NightlyTrxIdentities would throw (empty when none); Rows its rows.</summary>
        public static PSObject IdentityRows(XmlDocument doc)
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
            var rows = new List<object>();
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
                var row = new PSObject();
                row.Properties.Add(new PSNoteProperty("TestId", testId));
                row.Properties.Add(new PSNoteProperty("Uid", ""));
                row.Properties.Add(new PSNoteProperty("ClassName", className));
                row.Properties.Add(new PSNoteProperty("MethodName", methodName));
                row.Properties.Add(new PSNoteProperty("Display", display));
                row.Properties.Add(new PSNoteProperty("Outcome", outcome));
                row.Properties.Add(new PSNoteProperty("Identity", className + "." + methodName));
                row.Properties.Add(new PSNoteProperty("DebugTrace", debugTrace));
                rows.Add(row);
            }
            var result = new PSObject();
            result.Properties.Add(new PSNoteProperty("Error", error));
            result.Properties.Add(new PSNoteProperty("Rows", rows.ToArray()));
            return result;
        }
    }
}
