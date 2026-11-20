// The TUnit/MTP diagnostic and discovery documents (moved from nightly-coverage.ps1): the strict reader for pinned
// TUnit 1.44 / MTP 2.2 diagnostic ToString() records and for the golden JSON forms, the discovery and execution
// documents, and the discovery producer's document. Nodes are the PowerShell objects the record scanner (DiagScan)
// builds, so this class uses PowerShell types; its callers are the nightly executor and the release evidence checks.
using System;
using System.Collections.Specialized;
using System.IO;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    public static class NightlyDiagnosticLog
    {
        public const string DiscoveryFormat = "antiphon-tunit-discovery-v1";
        public const string ExecutionFormat = "antiphon-tunit-execution-v1";
        public const string SupportedTunit = "1.44.0";
        public const string SupportedMtp = "2.2.2";

        private static readonly Regex TunitHeader = new Regex(@"Version:\s+'1\.44\.0", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex TunitBanner = new Regex(@"TUnit v1\.44\.0", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex MtpHeader = new Regex(@"Version:\s+2\.2\.2", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex StackTail = new Regex(@"^\r?\n   at ", RegexOptions.CultureInvariant);
        private static readonly Regex ReflectedException = new Regex(@"\A[A-Za-z_][A-Za-z0-9_.+`]*Exception: [^\r\n]*(?:\r?\n   at [\s\S]*)?\z", RegexOptions.CultureInvariant);
        private static readonly Regex PropertySuffix = new Regex(@" \} \] \} \}\s*\z", RegexOptions.CultureInvariant);
        private static readonly Regex TimeoutSuffix = new Regex(@", Timeout = (?:(?:\d+\.)?\d{2}:\d{2}:\d{2}(?:\.\d+)?)? \} \] \} \}\s*\z", RegexOptions.CultureInvariant);

        private static NightlyRefusal Refuse(string reason) => new NightlyRefusal(reason);

        private static PSObject Note(params object[] pairs)
        {
            var o = new PSObject();
            for (var i = 0; i < pairs.Length; i += 2) o.Properties.Add(new PSNoteProperty((string)pairs[i], pairs[i + 1]));
            return o;
        }

        /// <summary>The header of a pinned diagnostic log names TUnit 1.44.0 and MTP 2.2.2.</summary>
        public static void AssertPinnedVersions(string text)
        {
            text = text ?? "";
            if (!(TunitHeader.IsMatch(text) || TunitBanner.IsMatch(text)) || !MtpHeader.IsMatch(text))
                throw Refuse("version-drift tunit/mtp header missing or unsupported");
        }

        /// <summary>
        /// ConvertFrom-NightlyDiagnosticLog: the diagnostic log at path as nodes ({ Nodes }), or - for a golden JSON log -
        /// the discovery or execution document it holds. kind: discovery or execution.
        /// </summary>
        public static PSObject Read(string path, string kind)
        {
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path))) throw Refuse("missing diagnostic " + path);
            var text = File.ReadAllText(path);
            var trimmed = text.TrimStart();
            if (trimmed.Length == 0) throw Refuse("empty diagnostic " + path);
            if (trimmed.StartsWith("{"))
            {
                var obj = NightlyJson.ParseText(text, 1024, true) ?? throw Refuse("unreadable diagnostic json " + path);
                if (Same(kind, "discovery")) return DiscoveryDocument(obj, DiscoveryFormat);
                if (Same(kind, "execution")) return ExecutionJson(obj);
                return obj;
            }
            AssertPinnedVersions(text);
            // The record scan refuses in record order and counts the raw markers independently of framing, so an orphan
            // or ambiguous marker in display text still prevents a partial inventory.
            var parsed = DiagScan.ParseRecords(text, kind, path);
            var discoveredCount = Convert.ToInt32(PsValue.Prop(parsed, "DiscoveredCount"), System.Globalization.CultureInfo.InvariantCulture);
            var error = PsValue.Str(PsValue.Prop(parsed, "Error"));
            if (error.Length > 0) throw Refuse(error);
            foreach (var failed in PsValue.AsArray(PsValue.Prop(parsed, "Failed")))
            {
                var message = FailureMessage(PsValue.Str(PsValue.Prop(failed, "Record")), PsValue.Prop(failed, "StateProperty"));
                if (message != null) PSObject.AsPSObject(PsValue.Prop(failed, "Node")).Properties.Add(new PSNoteProperty("Message", message));
            }
            var nodes = PsValue.AsArray(PsValue.Prop(parsed, "Nodes"));
            if (!LanguagePrimitives.IsTrue(PsValue.Prop(parsed, "Recognized"))) throw Refuse("unknown diagnostic format " + path);
            if (Same(kind, "discovery") && discoveredCount != nodes.Length)
                throw Refuse("discovery-count-mismatch discovered=" + discoveredCount + " parsed=" + nodes.Length + " path=" + path);
            return Note("Nodes", nodes);
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Native MTP failure explanations carry the multiline failure message. Only a unique validated field boundary
        /// supplies it; ambiguous or malformed text answers null (the node keeps Failed without a Message).
        /// stateProperty: NightlyDiagnostics.StateProperty's { Name, ExplanationStart }.
        /// </summary>
        public static string FailureMessage(string record, object stateProperty)
        {
            record = record ?? "";
            if (stateProperty == null) stateProperty = NightlyDiagnostics.StateProperty(record);
            var name = PsValue.Str(PsValue.Prop(stateProperty, "Name"));
            var start = Convert.ToInt32(PsValue.Prop(stateProperty, "ExplanationStart"), System.Globalization.CultureInfo.InvariantCulture);
            if (start < 0 || name == "Passed" || name == "Skipped" || name == "Discovered" || name == "InProgress") return null;
            var tail = record.Substring(start);
            // The pinned property is the last item in TestNode.Properties: remove only its structural suffix, never
            // whitespace belonging to the explanation. Native MTP 2.2.2 prints a null Timeout as empty text.
            var close = (name == "Timeout" ? TimeoutSuffix : PropertySuffix).Match(tail);
            if (!close.Success) return null;
            var body = tail.Substring(0, close.Index);
            const string marker = ", Exception = ";
            var allowEmpty = name == "Timeout" || name == "Cancelled";
            string only = null;
            var count = 0;
            for (var offset = body.IndexOf(marker, StringComparison.Ordinal); offset >= 0; offset = body.IndexOf(marker, offset + marker.Length, StringComparison.Ordinal))
            {
                var message = body.Substring(0, offset);
                if (ExceptionTail(message, body.Substring(offset + marker.Length), allowEmpty)) { only = message; count++; }
            }
            // ToString() does not escape messages or stacks: more than one valid split is ambiguity, not a choice.
            return count == 1 && only.Length > 0 ? only : null;
        }

        /// <summary>The text after ", Exception = " is TUnit's echo of the message, a reflected exception, or &lt;null&gt;.</summary>
        public static bool ExceptionTail(string message, string exceptionText, bool allowEmpty)
        {
            if (allowEmpty && exceptionText.Length == 0) return true;
            if (exceptionText == "<null>") return true;
            const string echoType = "TUnit.Engine.Exceptions.TestFailedException: ";
            if (exceptionText.StartsWith(echoType, StringComparison.Ordinal))
            {
                const string prefix = "[Test Failure] ";
                if (!message.StartsWith(prefix, StringComparison.Ordinal)) return false;
                var echo = echoType + message.Substring(prefix.Length);
                if (!exceptionText.StartsWith(echo, StringComparison.Ordinal)) return false;
                var stack = exceptionText.Substring(echo.Length);
                return stack.Length == 0 || StackTail.IsMatch(stack);
            }
            // Reflected Error/Timeout/Cancelled shapes may carry a regular exception: its first line is a type and
            // message, and any remaining text starts with a stack frame.
            return ReflectedException.IsMatch(exceptionText);
        }

        /// <summary>ConvertFrom-NightlyDiagnosticRecord: one record as a node; a failed node gets its validated Message.</summary>
        public static PSObject Record(string record, string state, object stateProperty)
        {
            var converted = NightlyDiagnostics.ConvertRecord(record, state);
            var error = PsValue.Str(PsValue.Prop(converted, "Error"));
            if (error.Length > 0) throw Refuse(error);
            var node = PSObject.AsPSObject(PsValue.Prop(converted, "Node"));
            if (Same(state, "Failed"))
            {
                var message = FailureMessage(record, stateProperty);
                if (message != null) node.Properties.Add(new PSNoteProperty("Message", message));
            }
            return node;
        }

        // ---- documents ----------------------------------------------------------------------------------------

        /// <summary>ConvertFrom-NightlyDiscoveryDocument: { Format, TunitVersion, MtpVersion, AssemblyHash, Nodes }.</summary>
        public static PSObject DiscoveryDocument(object document, string expectedFormat)
        {
            if (document == null) throw Refuse("empty discovery document");
            var format = PsValue.Str(PsValue.Prop(document, "format"));
            if (!Same(format, expectedFormat)) throw Refuse("unknown discovery format " + format);
            var tunit = PsValue.Str(PsValue.Prop(document, "tunitVersion"));
            var mtp = PsValue.Str(PsValue.Prop(document, "mtpVersion"));
            if (!Same(tunit, SupportedTunit) || !Same(mtp, SupportedMtp)) throw Refuse("version-drift tunit=" + tunit + " mtp=" + mtp);
            var assemblyHash = PsValue.Str(PsValue.Prop(document, "assemblyHash"));
            if (string.IsNullOrWhiteSpace(assemblyHash)) throw Refuse("missing assemblyHash");
            var compiled = NightlyDocuments.DiscoveryNodes(PsValue.Prop(document, "nodes"));
            var error = PsValue.Str(PsValue.Prop(compiled, "Error"));
            if (error.Length > 0) throw Refuse(error);
            return Note("Format", format, "TunitVersion", tunit, "MtpVersion", mtp, "AssemblyHash", assemblyHash, "Nodes", PsValue.Prop(compiled, "Nodes"));
        }

        /// <summary>
        /// Read-NightlyDiscoveryDocument: the discovery document file (the producer writes UTF-8 with no BOM; a UTF-8 BOM,
        /// a trailing comma and a BOM-led UTF-16 document are accepted; comments are refused).
        /// </summary>
        public static PSObject ReadDiscovery(string path)
        {
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path))) throw Refuse("missing discovery " + path);
            var read = NightlyJson.ReadDiscovery(Path.GetFullPath(path));
            if (read.Document == null)
            {
                if (read.Reason == "empty") throw Refuse("empty discovery " + path);
                throw Refuse("discovery-unreadable: " + path + ": " + read.Reason);
            }
            return DiscoveryDocument(read.Document, DiscoveryFormat);
        }

        /// <summary>ConvertFrom-NightlyExecutionJson: the golden execution document's terminal nodes ({ Nodes }).</summary>
        public static PSObject ExecutionJson(object document)
        {
            var format = PsValue.Str(PsValue.Prop(document, "format"));
            if (!Same(format, ExecutionFormat)) throw Refuse("unknown execution format " + format);
            var tunit = PsValue.Str(PsValue.Prop(document, "tunitVersion"));
            var mtp = PsValue.Str(PsValue.Prop(document, "mtpVersion"));
            if (!Same(tunit, SupportedTunit) || !Same(mtp, SupportedMtp)) throw Refuse("version-drift tunit=" + tunit + " mtp=" + mtp);
            var nodes = new System.Collections.Generic.List<object>();
            var uids = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in PsValue.AsArray(PsValue.Prop(document, "nodes")))
            {
                var uid = PsValue.Str(PsValue.Prop(n, "uid"));
                var state = PsValue.Str(PsValue.Prop(n, "state"));
                if (string.IsNullOrWhiteSpace(uid)) throw Refuse("truncated execution record");
                var normalized = NightlyClassNames.NormalizedOutcome(state);
                if (Same(state, "InProgress") || Same(state, "Discovered") || Same(normalized, "NotExecuted")) continue;
                if (!uids.Add(uid)) throw Refuse("duplicate execution uid " + uid);
                var typeName = PsValue.Str(PsValue.Prop(n, "type"));
                var ns = PsValue.Str(PsValue.Prop(n, "namespace"));
                var className = PsValue.Str(PsValue.Prop(n, "className"));
                if (string.IsNullOrWhiteSpace(className)) className = string.IsNullOrWhiteSpace(ns) ? typeName : ns + "." + typeName;
                nodes.Add(Note("Uid", uid, "State", state, "Outcome", state, "Type", typeName, "Method", PsValue.Str(PsValue.Prop(n, "method")),
                    "Namespace", ns, "ClassName", className, "Excluded", false, "Categories", new object[0]));
            }
            return Note("Nodes", nodes.ToArray());
        }

        /// <summary>ConvertTo-NightlyDiscoveryDocument: the document the discovery producer writes for these nodes.</summary>
        public static OrderedDictionary NewDiscoveryDocument(object[] nodes, string assemblyHash)
        {
            if (string.IsNullOrWhiteSpace(assemblyHash)) throw Refuse("missing assemblyHash");
            return new OrderedDictionary
            {
                { "format", DiscoveryFormat }, { "tunitVersion", SupportedTunit }, { "mtpVersion", SupportedMtp }, { "assemblyHash", assemblyHash },
                { "nodes", NightlyDocuments.DocumentNodes(nodes ?? new object[0]) },
            };
        }

        /// <summary>
        /// Write-NightlyDiscoveryDocument: the document's text (DiscoveryWriter) to a temporary file replaced over path. A
        /// document holding a value the writer does not claim is refused before anything is written.
        /// </summary>
        public static void WriteDiscoveryDocument(string path, object document)
        {
            var prepared = DiscoveryWriter.Prepare(document) ?? throw Refuse("nightly-discovery-write-failed unsupported-value " + path);
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            DiscoveryWriter.WriteFile(tmp, prepared);
            if (File.Exists(path))
            {
                var bak = path + ".bak";
                File.Replace(tmp, path, bak);
                try { File.Delete(bak); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            else File.Move(tmp, path);
        }

        /// <summary>
        /// Get-NightlyLatestDiagnosticLog: a file path itself, or the newest .diag (else newest file) in a directory; null
        /// when the path does not exist or the directory holds nothing.
        /// </summary>
        public static string Latest(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return null;
            var full = Path.GetFullPath(directory);
            if (File.Exists(full)) return full;
            if (!Directory.Exists(full)) return null;
            var latest = NightlyCoverageParts.LatestDiagnosticLog(full);
            return string.IsNullOrEmpty(latest) ? null : latest;
        }
    }
}
