// Per-chunk evidence records of test-phase-evidence.ps1, compiled. Each method returns exactly what the named
// PowerShell function computed; the PowerShell functions stay the entry points (they keep every file read, write
// and seam), call into this file when the core is loaded, and keep their bodies as the fallback.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    public static class TestPhaseRecords
    {
        /// <summary>
        /// Test-QualificationSidecarDense: more than limit commas or more than limit colons, or the deadline
        /// passed while counting (a null watch has no deadline).
        /// </summary>
        public static bool Dense(string text, int limit, Stopwatch watch, int deadlineMilliseconds)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (Count(text, ',', limit, watch, deadlineMilliseconds, out var commas)) return true;
            if (Count(text, ':', limit, watch, deadlineMilliseconds, out var colons)) return true;
            return commas > limit || colons > limit;
        }

        // Counts c up to limit + 1; true when the deadline passed first.
        private static bool Count(string text, char c, int limit, Stopwatch watch, int deadlineMilliseconds, out int count)
        {
            count = 0;
            var at = 0;
            while (at >= 0 && count <= limit)
            {
                if (watch != null && watch.ElapsedMilliseconds >= deadlineMilliseconds) return true;
                at = text.IndexOf(c, at);
                if (at < 0) break;
                count++;
                at++;
            }
            return false;
        }

        /// <summary>
        /// New-TestPhaseEnvelopeText: the journal envelope with its own SHA-256 in "digest" (taken over the text
        /// with an empty digest). Strings arrive as the PowerShell [string] parameters held them.
        /// </summary>
        public static string Envelope(string kind, int sequence, string publicationId, string generation, string timingRoot,
            string reason, string suite, string chunk, string settledSuite, string settledChunk, string headerRun,
            string headerAttempt, string headerSuite, string freshJson, string settledJson, string writerEnvironment)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"version\":1");
            sb.Append(",\"kind\":").Append(TestPhaseJson.String(kind ?? ""));
            sb.Append(",\"generation\":").Append(TestPhaseJson.String(generation ?? ""));
            sb.Append(",\"sequence\":").Append(sequence.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"publicationId\":").Append(TestPhaseJson.String(publicationId ?? ""));
            sb.Append(",\"root\":").Append(TestPhaseJson.String(timingRoot ?? ""));
            sb.Append(",\"freshSuiteKey\":").Append(TestPhaseJson.String(B64(suite)));
            sb.Append(",\"settledSuiteKey\":").Append(TestPhaseJson.String(B64(settledSuite)));
            sb.Append(",\"freshChunkKey\":").Append(TestPhaseJson.String(B64(chunk)));
            sb.Append(",\"settledChunkKey\":").Append(TestPhaseJson.String(B64(settledChunk)));
            sb.Append(",\"freshRun\":").Append(TestPhaseJson.String(B64(headerRun)));
            sb.Append(",\"freshAttempt\":").Append(TestPhaseJson.String(B64(headerAttempt)));
            sb.Append(",\"freshHeaderSuite\":").Append(TestPhaseJson.String(B64(headerSuite)));
            sb.Append(",\"digest\":\"\"");
            sb.Append(",\"rowJson\":").Append(TestPhaseJson.String(freshJson ?? ""));
            sb.Append(",\"settledRowJson\":").Append(TestPhaseJson.String(settledJson ?? ""));
            sb.Append(",\"writerEnvironment\":").Append(TestPhaseJson.String(writerEnvironment ?? ""));
            sb.Append(",\"reason\":").Append(TestPhaseJson.String(reason ?? ""));
            sb.Append('}');
            var text = sb.ToString();
            var hash = Sha256Text(text);
            const string needle = "\"digest\":\"\"";
            // PowerShell's $text.IndexOf(string) is a current-culture search.
            var at = text.IndexOf(needle, StringComparison.CurrentCulture);
            if (at < 0) throw new InvalidOperationException("digest-placeholder-missing");
            return text.Remove(at, needle.Length).Insert(at, "\"digest\":\"" + hash + "\"");
        }

        /// <summary>ConvertTo-TestPhaseB64.</summary>
        public static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? ""));

        /// <summary>Get-TestPhaseSha256Text: lower-case hex SHA-256 of the UTF-8 text.</summary>
        public static string Sha256Text(string text)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var sb = new StringBuilder(64);
                foreach (var b in sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        // ---- active-chunk.json (qualification-supervisor-heartbeat.ps1) ----

        private static readonly string[] ActiveStringFields =
        {
            "format", "state", "runId", "attempt", "suite", "chunk", "planFile", "planDigest",
            "sourceDigest", "policyDigest", "costFileDigest", "assemblyDigest",
        };
        private static readonly Regex ActiveRunPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$");
        private static readonly Regex ActiveAttemptPattern = new Regex("^[1-9][0-9]*$");
        private static readonly Regex ActiveChunkPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,200}$");
        private static readonly Regex ActiveSuitePattern = new Regex("^[a-z0-9][a-z0-9-]{0,40}$");
        private static readonly Regex Sha256Pattern = new Regex("^[a-f0-9]{64}$");

        /// <summary>
        /// The text of active-chunk.json bytes as Read-QualificationActiveChunkIdentity decodes them: a
        /// StreamReader over the bytes, UTF-8 unless a byte-order mark says otherwise.
        /// </summary>
        public static string DecodeText(byte[] bytes)
        {
            using (var reader = new System.IO.StreamReader(new System.IO.MemoryStream(bytes ?? new byte[0]), Encoding.UTF8, true))
                return reader.ReadToEnd();
        }

        /// <summary>
        /// Read-QualificationActiveChunkIdentity's checks on the parsed record (ConvertFrom-Json of the bytes): the
        /// record with identityDigest (SHA-256 of exactly those bytes) added, or null where the function returns null.
        /// </summary>
        public static PSObject ActiveChunkIdentity(object record, byte[] bytes)
        {
            try
            {
                if (record == null || bytes == null) return null;
                var ps = PSObject.AsPSObject(record);
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in ps.Properties) names.Add(p.Name);
                foreach (var field in ActiveStringFields)
                {
                    // -cnotcontains: the exact spelling; $record.$field: the value, whatever its case.
                    if (!names.Contains(field) || !(ps.Properties[field]?.Value is string)) return null;
                }
                string S(string name) => (string)ps.Properties[name].Value;
                var generation = ps.Properties["generation"]?.Value;
                if (!(generation is int) && !(generation is long)) return null;
                if (!ActiveRunPattern.IsMatch(S("runId")) || !ActiveAttemptPattern.IsMatch(S("attempt")) || !ActiveChunkPattern.IsMatch(S("chunk"))) return null;
                if (S("format") != "antiphon-qualification-active-chunk-v1" || (S("state") != "Active" && S("state") != "Idle")
                    || Convert.ToInt64(generation, CultureInfo.InvariantCulture) <= 0 || S("runId").Length == 0 || S("attempt").Length == 0
                    || !ActiveSuitePattern.IsMatch(S("suite")) || S("chunk").Length == 0
                    || S("planFile") != "groups-planned-" + S("suite") + ".json" || !Sha256Pattern.IsMatch(S("planDigest"))) return null;
                // Add-Member refuses an existing name (its error made the function return null).
                if (ps.Properties["identityDigest"] != null) return null;
                ps.Properties.Add(new PSNoteProperty("identityDigest", Sha256Hex(bytes)));
                return ps;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Get-QualificationBytesDigest: lower-case hex SHA-256 of the bytes.</summary>
        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var sb = new StringBuilder(64);
                foreach (var b in sha.ComputeHash(bytes ?? new byte[0])) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>
        /// ConvertTo-Json -Depth 12 of the flat active-chunk record (string and integer values): Newtonsoft's
        /// indented form, two-space indent, the platform line ending, and its default string escaping. Throws on an
        /// empty record, a key that is not a string or a value of another type: the writer only builds strings and
        /// integers.
        /// </summary>
        public static string ActiveChunkJson(IDictionary record)
        {
            if (record == null || record.Count == 0) throw new ArgumentException("active-record-empty");
            var nl = Environment.NewLine;
            var sb = new StringBuilder("{");
            var first = true;
            foreach (DictionaryEntry entry in record)
            {
                var key = entry.Key as string;
                if (key == null) throw new ArgumentException("active-record-key");
                sb.Append(first ? "" : ",").Append(nl).Append("  ");
                AppendJsonString(sb, key);
                sb.Append(": ");
                if (entry.Value is string s) AppendJsonString(sb, s);
                else if (entry.Value is int i) sb.Append(i.ToString(CultureInfo.InvariantCulture));
                else if (entry.Value is long l) sb.Append(l.ToString(CultureInfo.InvariantCulture));
                else throw new ArgumentException("active-record-value " + key);
                first = false;
            }
            return sb.Append(nl).Append('}').ToString();
        }

        /// <summary>
        /// A JSON string as Newtonsoft writes it with StringEscapeHandling.Default (ConvertTo-Json's default): the
        /// quote, the backslash and the short control escapes, and a four-digit lower-case hex escape for the other
        /// control characters, U+0085, U+2028 and U+2029. Everything else, apostrophes and non-ASCII included, is
        /// written as is.
        /// </summary>
        private static void AppendJsonString(StringBuilder sb, string s)
        {
            const char bs = (char)0x5C;
            sb.Append('"');
            foreach (var c in s)
            {
                if (c == '"') sb.Append(bs).Append('"');
                else if (c == bs) sb.Append(bs).Append(bs);
                else if (c == (char)0x08) sb.Append(bs).Append('b');
                else if (c == (char)0x09) sb.Append(bs).Append('t');
                else if (c == (char)0x0A) sb.Append(bs).Append('n');
                else if (c == (char)0x0C) sb.Append(bs).Append('f');
                else if (c == (char)0x0D) sb.Append(bs).Append('r');
                else if (c < ' ' || c == (char)0x85 || c == (char)0x2028 || c == (char)0x2029)
                    sb.Append(bs).Append('u').Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            sb.Append('"');
        }

        private static readonly Regex SuitePattern =new Regex("^[a-z0-9][a-z0-9-]{0,40}$", RegexOptions.IgnoreCase);
        private static readonly Regex ChunkPattern = new Regex("^[a-z0-9][a-z0-9._-]{0,80}$", RegexOptions.IgnoreCase);
        private static readonly Regex RunPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,80}$", RegexOptions.IgnoreCase);
        private static readonly Regex AttemptPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,40}$", RegexOptions.IgnoreCase);

        private static readonly string[] OwnerNames =
        {
            "rootExitedAtUtc", "RootExitedAtUtc", "jobEmpty", "JobEmpty", "JobEmptyObserved",
            "descendants", "Descendants", "missedDescendantPids", "MissedDescendantPids",
            "drainMilliseconds", "DrainMilliseconds", "executionMilliseconds", "ExecutionMilliseconds",
            "descendantIdentities", "DescendantIdentities",
        };

        /// <summary>
        /// Write-TestPhaseOwnerObservation's document, or null where that function writes nothing (no verdict, no
        /// owner fields, a suite or chunk outside the file-name patterns, or any conversion that throws, which its
        /// try/catch swallows). The caller still names the file, skips an existing one and writes it.
        /// </summary>
        public static OrderedDictionary OwnerDocument(object verdict, string suite, string chunk, string runId, string attempt)
        {
            try
            {
                return BuildOwnerDocument(verdict, suite ?? "", chunk ?? "", runId ?? "", attempt ?? "");
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Write-TestPhaseOwnerObservation's file key for the run id: the id, or 'run' outside the pattern.</summary>
        public static string OwnerRunKey(string runId) => RunPattern.IsMatch(runId ?? "") ? runId : "run";

        /// <summary>Write-TestPhaseOwnerObservation's file key for the attempt: the attempt, or '0' outside the pattern.</summary>
        public static string OwnerAttemptKey(string attempt) => AttemptPattern.IsMatch(attempt ?? "") ? attempt : "0";

        private static OrderedDictionary BuildOwnerDocument(object verdict, string suite, string chunk, string runId, string attempt)
        {
            if (verdict == null) return null;
            if (!NotePresent(verdict, OwnerNames)) return null;
            if (!SuitePattern.IsMatch(suite)) return null;
            if (!ChunkPattern.IsMatch(chunk)) return null;
            // Missed pids in first-seen order (the PowerShell @{} enumerated in hash order, which varied by process).
            var missed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missedOrder = new List<string>();
            var missedRaw = NoteValue(verdict, "missedDescendantPids", "MissedDescendantPids");
            if (missedRaw != null)
            {
                foreach (var childPid in PsValue.AsArray(missedRaw))
                {
                    if (childPid == null || string.IsNullOrWhiteSpace(PsValue.Str(childPid))) continue;
                    var key = ToInt(childPid).ToString(CultureInfo.InvariantCulture);
                    if (missed.Add(key)) missedOrder.Add(key);
                }
            }
            var rows = new List<OrderedDictionary>();
            var descRaw = NoteValue(verdict, "descendants", "Descendants");
            if (descRaw != null)
            {
                foreach (var row in PsValue.AsArray(descRaw))
                {
                    if (row == null) continue;
                    var pidValue = NoteValue(row, "pid", "Pid");
                    var childPid = 0;
                    if (pidValue != null && !string.IsNullOrWhiteSpace(PsValue.Str(pidValue))) childPid = ToInt(pidValue);
                    var image = OwnerImage(NoteValue(row, "image", "Image", "ProcessName"));
                    if (missed.Contains(childPid.ToString(CultureInfo.InvariantCulture))) image = "unknown";
                    var started = NoteValue(row, "startedAtUtc", "StartedAtUtc");
                    var exited = NoteValue(row, "exitedAtUtc", "ExitedAtUtc");
                    rows.Add(OwnerRow(childPid, image, started == null ? "" : PsValue.Str(started), exited == null ? "" : PsValue.Str(exited)));
                }
            }
            if (rows.Count == 0)
            {
                var identRaw = NoteValue(verdict, "descendantIdentities", "DescendantIdentities");
                var imageRaw = NoteValue(verdict, "descendantImages", "DescendantImages");
                var exitRaw = NoteValue(verdict, "descendantExitedAtUtc", "DescendantExitedAtUtc");
                var idents = identRaw != null ? PsValue.AsArray(identRaw) : new object[0];
                var images = imageRaw != null ? PsValue.AsArray(imageRaw) : new object[0];
                var exitStamps = exitRaw != null ? PsValue.AsArray(exitRaw) : new object[0];
                for (var i = 0; i < idents.Length; i++)
                {
                    var ident = PsValue.Str(idents[i]);
                    // $ident.IndexOf(':') is the ordinal char search.
                    var colon = ident.IndexOf(':');
                    var childPid = 0;
                    var startedText = "";
                    if (colon > 0)
                    {
                        childPid = ToInt(ident.Substring(0, colon));
                        startedText = ident.Substring(colon + 1);
                    }
                    var image = "unknown";
                    if (i < images.Length) image = OwnerImage(images[i]);
                    if (missed.Contains(childPid.ToString(CultureInfo.InvariantCulture))) image = "unknown";
                    var exitedText = "";
                    if (i < exitStamps.Length && exitStamps[i] != null) exitedText = PsValue.Str(exitStamps[i]);
                    rows.Add(OwnerRow(childPid, image, startedText, exitedText));
                }
            }
            var seenPid = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows) seenPid[PsValue.Str(row["pid"])] = true;
            foreach (var key in missedOrder)
            {
                if (seenPid.ContainsKey(PsValue.Str(key))) continue;
                int missedPid;
                try { missedPid = ToInt(key); } catch (Exception) { continue; }
                rows.Add(OwnerRow(missedPid, "unknown", "", ""));
            }
            var job = false;
            if (NotePresent(verdict, "jobEmpty", "JobEmpty", "JobEmptyObserved"))
            {
                var jobValue = NoteValue(verdict, "jobEmpty", "JobEmpty", "JobEmptyObserved");
                if (jobValue != null) job = LanguagePrimitives.IsTrue(jobValue);
            }
            var drain = DoubleNote(verdict, "drainMilliseconds", "DrainMilliseconds");
            var execution = DoubleNote(verdict, "executionMilliseconds", "ExecutionMilliseconds");
            var exitedValue = NoteValue(verdict, "rootExitedAtUtc", "RootExitedAtUtc");
            var exitedAt = exitedValue != null ? PsValue.Str(exitedValue) : "";
            var startedValue = NoteValue(verdict, "rootStartedAtUtc", "RootStartedAtUtc");
            var startedAt = startedValue != null ? PsValue.Str(startedValue) : "";
            var launch = DoubleNote(verdict, "launchMilliseconds", "LaunchMilliseconds");
            var cleanup = DoubleNote(verdict, "cleanupMilliseconds", "CleanupMilliseconds");
            var children = false;
            if (NotePresent(verdict, "childrenExited", "ChildrenExited"))
            {
                var value = NoteValue(verdict, "childrenExited", "ChildrenExited");
                if (value != null) children = LanguagePrimitives.IsTrue(value);
            }
            var drained = false;
            if (NotePresent(verdict, "outputDrained", "OutputDrained"))
            {
                var value = NoteValue(verdict, "outputDrained", "OutputDrained");
                if (value != null) drained = LanguagePrimitives.IsTrue(value);
            }
            var remaining = 0;
            var remainingValue = NoteValue(verdict, "remainingProcessCount", "RemainingProcessCount");
            if (remainingValue != null) remaining = ToInt(remainingValue);
            var missedIds = new List<object>();
            foreach (var key in missedOrder)
            {
                try { missedIds.Add(ToInt(key)); } catch (Exception) { }
            }
            var descendants = new object[rows.Count];
            for (var i = 0; i < rows.Count; i++) descendants[i] = rows[i];
            return new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "version", 1 },
                { "suite", suite },
                { "chunk", chunk },
                { "runId", OwnerRunKey(runId) },
                { "attempt", OwnerAttemptKey(attempt) },
                { "rootStartedAtUtc", startedAt },
                { "rootExitedAtUtc", exitedAt },
                { "jobEmpty", job },
                { "launchMilliseconds", launch },
                { "cleanupMilliseconds", cleanup },
                { "drainMilliseconds", drain },
                { "executionMilliseconds", execution },
                { "childrenExited", children },
                { "outputDrained", drained },
                { "remainingProcessCount", remaining },
                { "missedDescendantPids", missedIds.ToArray() },
                { "descendants", descendants },
            };
        }

        private static OrderedDictionary OwnerRow(int pid, string image, string startedAtUtc, string exitedAtUtc) =>
            new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "pid", pid }, { "image", image }, { "startedAtUtc", startedAtUtc }, { "exitedAtUtc", exitedAtUtc },
            };

        // ConvertTo-TestPhaseOwnerImage.
        private static string OwnerImage(object image)
        {
            var text = image != null ? PsValue.Str(image) : "";
            return string.IsNullOrWhiteSpace(text) ? "unknown" : text;
        }

        private static int ToInt(object value) => (int)LanguagePrimitives.ConvertTo(value, typeof(int), CultureInfo.InvariantCulture);

        private static double DoubleNote(object o, params string[] names)
        {
            var value = NoteValue(o, names);
            return value != null ? (double)LanguagePrimitives.ConvertTo(value, typeof(double), CultureInfo.InvariantCulture) : 0.0;
        }

        // Test-TestPhaseNotePresent: one of the names is among $Object.PSObject.Properties.Name (-contains).
        private static bool NotePresent(object o, params string[] names) => FindNote(o, names) != null;

        // Get-TestPhaseNoteValue: $Object.<first listed name present>.
        private static object NoteValue(object o, params string[] names)
        {
            var name = FindNote(o, names);
            return name == null ? null : PSObject.AsPSObject(o).Properties[name]?.Value;
        }

        private static string FindNote(object o, string[] names)
        {
            if (o == null) return null;
            var present = new List<string>();
            foreach (var p in PSObject.AsPSObject(o).Properties) present.Add(p.Name);
            foreach (var name in names)
            {
                foreach (var have in present)
                {
                    if (string.Equals(have, name, StringComparison.OrdinalIgnoreCase)) return name;
                }
            }
            return null;
        }
    }
}
