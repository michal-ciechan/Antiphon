// The test-phase journal read back, compiled: Read-TestPhaseGenerationPrefix's record loop, Get-TestPhaseReduction,
// and the trx/chunk/suite fields Get-TestPhaseSummary reads from each reduced chunk document.
//
// Every record is still re-read from disk and its digest re-checked (a publication can be corrupted after it was
// written). Each envelope is read with a strict reader that answers only when ConvertFrom-Json would give the same
// values for the fields read here (plain strings, a whole-number sequence). Only our own writer produces these
// files, so anything else is a break (malformed-record), and a chunk document it cannot read is skipped as an
// unparseable one was.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly
{
    public static class TestPhaseJournalRead
    {
        /// <summary>Where ReadPrefix stopped: the sequence of the first missing record, or of the record that
        /// broke the prefix (Corrupt) with its reason.</summary>
        public sealed class PrefixStep
        {
            public int Expected;
            public bool Corrupt;
            public string Reason = "";
        }

        private static readonly string[] EnvelopeFields =
        {
            "kind", "sequence", "rowJson", "settledRowJson", "freshSuiteKey", "freshChunkKey", "settledSuiteKey",
            "settledChunkKey", "freshRun", "freshAttempt", "freshHeaderSuite", "publicationId",
        };

        private static readonly string[] ChunkDocumentFields = { "trx", "chunk", "suite" };

        // A captured value that is neither a string, a number nor null.
        private static readonly object OtherValue = new object();

        /// <summary>
        /// Read-TestPhaseGenerationPrefix's loop from record number expected: adds each record object
        /// (Kind, Sequence, FreshJson, SettledJson, the decoded keys, PublicationId) to loaded until the prefix ends
        /// or breaks (unreadable-record, bad-digest, malformed-record, sequence-mismatch).
        /// </summary>
        public static PrefixStep ReadPrefix(string generationDir, int expected, bool deferred, List<object> loaded)
        {
            var step = new PrefixStep();
            while (true)
            {
                step.Expected = expected;
                var path = Path.Combine(generationDir ?? "", expected.ToString("00000000", CultureInfo.CurrentCulture) + ".json");
                // Test-NightlyEvidencePath (Test-Path -LiteralPath)
                var exists = deferred ? EvidenceFiles.PathExists(path) : (File.Exists(path) || Directory.Exists(path));
                if (!exists) return step;
                string text;
                try { text = deferred ? EvidenceFiles.ReadAllText(path) : File.ReadAllText(path); }
                catch (Exception) { step.Corrupt = true; step.Reason = "unreadable-record"; return step; }
                if (string.IsNullOrWhiteSpace(text) || !DigestMatches(text))
                {
                    step.Corrupt = true;
                    step.Reason = "bad-digest";
                    return step;
                }
                var fields = TopFields(text, EnvelopeFields);
                PSObject record = null;
                int sequence = 0;
                if (fields == null || !TryRecord(fields, out record, out sequence))
                {
                    step.Corrupt = true;
                    step.Reason = "malformed-record";
                    return step;
                }
                if (sequence != expected) { step.Corrupt = true; step.Reason = "sequence-mismatch"; return step; }
                loaded.Add(record);
                expected++;
            }
        }

        /// <summary>Test-TestPhaseRecordDigest, with the same culture-sensitive searches and -eq.</summary>
        public static bool DigestMatches(string text)
        {
            const string needle = "\"digest\":\"";
            var start = text.IndexOf(needle, StringComparison.CurrentCulture);
            if (start < 0) return false;
            var hexStart = start + needle.Length;
            var end = text.IndexOf("\"", hexStart, StringComparison.CurrentCulture);
            if (end < 0) return false;
            var hex = text.Substring(hexStart, end - hexStart).ToLowerInvariant();
            var probe = text.Remove(start, (end + 1) - start).Insert(start, "\"digest\":\"\"");
            string actual;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(probe));
                var sb = new StringBuilder(64);
                foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                actual = sb.ToString();
            }
            return LanguagePrimitives.Equals(actual, hex, true, CultureInfo.InvariantCulture);
        }

        // The record object over the captured envelope fields; false for an envelope our writer does not produce
        // (a missing or non-whole sequence, a non-string field, invalid base64).
        private static bool TryRecord(Dictionary<string, object> fields, out PSObject record, out int sequence)
        {
            record = null;
            sequence = 0;
            if (!fields.TryGetValue("sequence", out var rawSequence) || !(rawSequence is JsonNumber number)) return false;
            if (!number.IsWhole || !int.TryParse(number.Raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out sequence)) return false;
            string kind, fresh, settled, freshSuite, freshChunk, settledSuite, settledChunk, run, attempt, headerSuite, publication;
            if (!Text(fields, "kind", out kind) || !Text(fields, "rowJson", out fresh) || !Text(fields, "settledRowJson", out settled)
                || !Text(fields, "publicationId", out publication)
                || !Key(fields, "freshSuiteKey", out freshSuite) || !Key(fields, "freshChunkKey", out freshChunk)
                || !Key(fields, "settledSuiteKey", out settledSuite) || !Key(fields, "settledChunkKey", out settledChunk)
                || !Key(fields, "freshRun", out run) || !Key(fields, "freshAttempt", out attempt)
                || !Key(fields, "freshHeaderSuite", out headerSuite))
                return false;
            record = new PSObject();
            record.Properties.Add(new PSNoteProperty("Kind", kind));
            record.Properties.Add(new PSNoteProperty("Sequence", sequence));
            record.Properties.Add(new PSNoteProperty("FreshJson", fresh));
            record.Properties.Add(new PSNoteProperty("SettledJson", settled));
            record.Properties.Add(new PSNoteProperty("FreshSuite", freshSuite));
            record.Properties.Add(new PSNoteProperty("FreshChunk", freshChunk));
            record.Properties.Add(new PSNoteProperty("SettledSuite", settledSuite));
            record.Properties.Add(new PSNoteProperty("SettledChunk", settledChunk));
            record.Properties.Add(new PSNoteProperty("HeaderRun", run));
            record.Properties.Add(new PSNoteProperty("HeaderAttempt", attempt));
            record.Properties.Add(new PSNoteProperty("HeaderSuite", headerSuite));
            record.Properties.Add(new PSNoteProperty("PublicationId", publication));
            return true;
        }

        // [string]$o.name for a captured field: a string, or "" for null or absent; false for anything else.
        private static bool Text(Dictionary<string, object> fields, string name, out string value)
        {
            value = "";
            if (!fields.TryGetValue(name, out var raw) || raw == null) return true;
            if (raw is string s) { value = s; return true; }
            return false;
        }

        // ConvertFrom-TestPhaseB64 ([string]$o.name); false when the base64 is invalid.
        private static bool Key(Dictionary<string, object> fields, string name, out string value)
        {
            if (!Text(fields, name, out var text)) { value = null; return false; }
            value = "";
            if (string.IsNullOrEmpty(text)) return true;
            try { value = Encoding.UTF8.GetString(Convert.FromBase64String(text)); return true; }
            catch (FormatException) { value = null; return false; }
        }

        /// <summary>
        /// The trx, chunk and suite of a reduced chunk document as Get-TestPhaseSummary reads them
        /// ([string]$chunkObj.trx when the property exists, [string]$chunkObj.chunk, [string]$chunkObj.suite), or
        /// null for a document our writer does not produce (not one strict JSON object, or a value that is not a
        /// plain string); the caller skips that row.
        /// </summary>
        public static string[] ChunkFields(string json)
        {
            var fields = TopFields(json, ChunkDocumentFields);
            if (fields == null) return null;
            if (!Text(fields, "trx", out var trx) || !Text(fields, "chunk", out var chunk) || !Text(fields, "suite", out var suite)) return null;
            return new[] { trx, chunk, suite };
        }

        private sealed class JsonNumber
        {
            public string Raw;
            public bool IsWhole;
        }

        private sealed class GiveUp : Exception { }

        // The named top-level properties (case-insensitive) of text when it is one strict JSON object that
        // ConvertFrom-Json reads without error and without converting those properties' strings to dates: strings,
        // JsonNumber, null, or OtherValue for containers and booleans. Null otherwise.
        private static Dictionary<string, object> TopFields(string text, string[] wanted)
        {
            if (string.IsNullOrEmpty(text) || text[0] == (char)0xFEFF) return null;
            // Encoding.UTF8 would replace a lone surrogate.
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return null;
                    i++;
                }
                else if (char.IsLowSurrogate(c)) return null;
            }
            var want = new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text),
                    new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = 64 });
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
                var names = new Stack<HashSet<string>>();
                names.Push(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                var containers = 1;
                string capture = null;
                while (containers > 0)
                {
                    if (!reader.Read()) return null;
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.PropertyName:
                            {
                                var name = reader.GetString();
                                // ConvertFrom-Json refuses empty and repeated names; "$" names may be metadata.
                                if (string.IsNullOrEmpty(name) || name[0] == '$' || !names.Peek().Add(name)) return null;
                                capture = containers == 1 && want.Contains(name) ? name : null;
                                continue;
                            }
                        case JsonTokenType.StartObject:
                            names.Push(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                            containers++;
                            if (capture != null) result[capture] = OtherValue;
                            break;
                        case JsonTokenType.StartArray:
                            names.Push(null);
                            containers++;
                            if (capture != null) result[capture] = OtherValue;
                            break;
                        case JsonTokenType.EndObject:
                        case JsonTokenType.EndArray:
                            names.Pop();
                            containers--;
                            break;
                        case JsonTokenType.String:
                            {
                                var s = reader.GetString();
                                // Microsoft "\/Date(ticks)\/" strings are dates (or errors) to ConvertFrom-Json.
                                if (s.StartsWith("/Date(", StringComparison.Ordinal)) return null;
                                if (capture != null)
                                {
                                    // Possibly an ISO date-time, which ConvertFrom-Json turns into a DateTime
                                    // (yyyy-MM-ddT...; base64 keys never carry '-').
                                    if (s.Length >= 19 && s.Length <= 40 && s[0] >= '0' && s[0] <= '9' && s[4] == '-' && s[7] == '-' && s[10] == 'T') return null;
                                    result[capture] = s;
                                }
                                break;
                            }
                        case JsonTokenType.Number:
                            {
                                var raw = Encoding.ASCII.GetString(reader.ValueSpan.ToArray());
                                var whole = raw.IndexOfAny(new[] { '.', 'e', 'E' }) < 0;
                                if (!whole)
                                {
                                    var d = reader.GetDouble();
                                    if (double.IsNaN(d) || double.IsInfinity(d)) return null;
                                }
                                if (capture != null) result[capture] = new JsonNumber { Raw = raw, IsWhole = whole };
                                break;
                            }
                        case JsonTokenType.True:
                        case JsonTokenType.False:
                            if (capture != null) result[capture] = OtherValue;
                            break;
                        case JsonTokenType.Null:
                            if (capture != null) result[capture] = null;
                            break;
                        default:
                            return null;
                    }
                    capture = null;
                }
                if (reader.Read()) return null;
                return result;
            }
            catch (GiveUp) { return null; }
            catch (JsonException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (FormatException) { return null; }
        }

        // ---- reduction ----

        private sealed class PairComparer : IEqualityComparer<KeyValuePair<string, string>>
        {
            private static readonly StringComparer Cmp = StringComparer.InvariantCultureIgnoreCase;
            public bool Equals(KeyValuePair<string, string> x, KeyValuePair<string, string> y) =>
                Cmp.Equals(x.Key, y.Key) && Cmp.Equals(x.Value, y.Value);
            public int GetHashCode(KeyValuePair<string, string> p) =>
                unchecked(Cmp.GetHashCode(p.Key) * 31 + Cmp.GetHashCode(p.Value));
        }

        /// <summary>
        /// Get-TestPhaseReduction: the seed rows, then each chunk record after removing every earlier entry whose
        /// Suite and Chunk equal its FreshSuite and FreshChunk (each compared on its own, invariant culture ignoring
        /// case), in the same order; with the chunk count and the last chunk's (or the context's) header.
        /// </summary>
        public static PSObject Reduce(object context)
        {
            var entries = new List<PSObject>();
            var alive = new List<bool>();
            var byKey = new Dictionary<KeyValuePair<string, string>, List<int>>(new PairComparer());
            void Append(PSObject entry, string suite, string chunk)
            {
                var key = new KeyValuePair<string, string>(suite, chunk);
                if (!byKey.TryGetValue(key, out var at)) byKey[key] = at = new List<int>();
                at.Add(entries.Count);
                entries.Add(entry);
                alive.Add(true);
            }

            if (LanguagePrimitives.IsTrue(PsValue.Prop(context, "HasSeed")))
            {
                foreach (var row in Each(PsValue.Prop(context, "SeedRows")))
                {
                    var json = PsValue.Str(PsValue.Prop(row, "SettledJson"));
                    var suite = PsValue.Str(PsValue.Prop(row, "SettledSuite"));
                    var chunk = PsValue.Str(PsValue.Prop(row, "SettledChunk"));
                    Append(Entry(json, json, suite, chunk), suite, chunk);
                }
            }
            var chunkCount = 0;
            object last = null;
            foreach (var rec in Each(PsValue.Prop(context, "Records")))
            {
                if (rec == null) continue;
                if (!LanguagePrimitives.Equals(PsValue.Str(PsValue.Prop(rec, "Kind")), "chunk", true, CultureInfo.InvariantCulture)) continue;
                chunkCount++;
                var fresh = new KeyValuePair<string, string>(PsValue.Str(PsValue.Prop(rec, "FreshSuite")), PsValue.Str(PsValue.Prop(rec, "FreshChunk")));
                if (byKey.TryGetValue(fresh, out var earlier))
                {
                    foreach (var i in earlier) alive[i] = false;
                    byKey.Remove(fresh);
                }
                var suite = PsValue.Str(PsValue.Prop(rec, "SettledSuite"));
                var chunk = PsValue.Str(PsValue.Prop(rec, "SettledChunk"));
                Append(Entry(PsValue.Str(PsValue.Prop(rec, "SettledJson")), PsValue.Str(PsValue.Prop(rec, "FreshJson")), suite, chunk), suite, chunk);
                last = rec;
            }
            var list = new ArrayList();
            for (var i = 0; i < entries.Count; i++) if (alive[i]) list.Add(entries[i]);
            string headerRun = "", headerAttempt = "", headerSuite = "";
            if (last != null)
            {
                headerRun = PsValue.Str(PsValue.Prop(last, "HeaderRun"));
                headerAttempt = PsValue.Str(PsValue.Prop(last, "HeaderAttempt"));
                headerSuite = PsValue.Str(PsValue.Prop(last, "HeaderSuite"));
            }
            else if (LanguagePrimitives.IsTrue(PsValue.Prop(context, "HeaderReady")))
            {
                headerRun = PsValue.Str(PsValue.Prop(context, "HeaderRun"));
                headerAttempt = PsValue.Str(PsValue.Prop(context, "HeaderAttempt"));
                headerSuite = PsValue.Str(PsValue.Prop(context, "HeaderSuite"));
            }
            var result = new PSObject();
            result.Properties.Add(new PSNoteProperty("Entries", list));
            result.Properties.Add(new PSNoteProperty("ChunkCount", chunkCount));
            result.Properties.Add(new PSNoteProperty("HeaderRun", headerRun));
            result.Properties.Add(new PSNoteProperty("HeaderAttempt", headerAttempt));
            result.Properties.Add(new PSNoteProperty("HeaderSuite", headerSuite));
            return result;
        }

        private static PSObject Entry(string json, string fresh, string suite, string chunk)
        {
            var entry = new PSObject();
            entry.Properties.Add(new PSNoteProperty("Json", json));
            entry.Properties.Add(new PSNoteProperty("Fresh", fresh));
            entry.Properties.Add(new PSNoteProperty("Suite", suite));
            entry.Properties.Add(new PSNoteProperty("Chunk", chunk));
            return entry;
        }

        // foreach ($x in $o): nothing for null, one item for a scalar, string or dictionary.
        private static IEnumerable<object> Each(object o)
        {
            if (o == null) yield break;
            var baseObject = o is PSObject ps ? ps.BaseObject : o;
            if (baseObject is string || baseObject is IDictionary || !(baseObject is IEnumerable enumerable))
            {
                yield return o;
                yield break;
            }
            foreach (var item in enumerable) yield return item;
        }
    }
}
