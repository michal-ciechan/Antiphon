// CARD-0599 D-4/D-8 and CARD-1311/CARD-1339: the release gate's journals. The publication journal
// (<ReleaseRoot>\publications.json) reserves each candidate's tag once, records its publication and its promoted final
// tag; the publish journal (<candidate>\publish-journal.json) records each completed publication step before the next
// begins, so a crash or a lost response resumes the same journal instead of allocating a new identity. Moved from
// scripts/lib/release-gate.ps1 (Get-ReleaseGatePublicationJournal, Get-ReleaseGateTagProposal,
// New-ReleaseGateTagReservation, Set-ReleaseGateReservationPublished, Test-ReleaseGatePromotionReservation,
// Set-ReleaseGateReservationFinalTag, Write-ReleaseGateJournalStep and Test-ReleaseGateJournalStep), deleted with unit 12
// of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md.
//
// The documents are JSON nodes read with case-insensitive names (as ConvertFrom-Json read them) and written back with
// every other value unchanged; strings are never reinterpreted as dates. Every write goes through one writer seam
// (path, text): the atomic replace in production, a fault in a test (the promoter's journal-write fault cases). The
// clock stamps reservedAt, publishedAt and updatedAt. Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>
    /// Get-ReleaseGateTagProposal: the reservation a candidate has or would get. Reused: the candidate already holds Row.
    /// Journal is the publication journal the proposal was read from.
    /// </summary>
    public sealed class ReleaseGateProposal
    {
        /// <summary>The tag (vYYYY.MM.DD.N, or vYYYY.MM.DD.N-rc.K for a long-lived candidate).</summary>
        public string Tag { get; init; } = "";

        /// <summary>The daily sequence N.</summary>
        public int Sequence { get; init; }

        /// <summary>K of a pre-release; 0 for a stamp tag.</summary>
        public int Rc { get; init; }

        /// <summary>The candidate's existing reservation was returned.</summary>
        public bool Reused { get; init; }

        /// <summary>The existing row when Reused; null otherwise.</summary>
        public JsonObject Row { get; init; }

        /// <summary>The journal as read.</summary>
        public JsonObject Journal { get; init; }
    }

    /// <summary>Test-ReleaseGatePromotionReservation: Ok with the journal and the one admitted row, or the refusal reason.</summary>
    public sealed class ReleaseGatePromotionCheck
    {
        /// <summary>The published reservation admits this promotion.</summary>
        public bool Ok { get; init; }

        /// <summary>promotion-reservation-&lt;missing|invalid|ambiguous|candidate-mismatch|sha-mismatch|tag-mismatch|unpublished|release-identity|final-conflict&gt;.</summary>
        public string Reason { get; init; } = "";

        /// <summary>The journal as read, when Ok.</summary>
        public JsonObject Journal { get; init; }

        /// <summary>The admitted row (a node of Journal), when Ok.</summary>
        public JsonObject Row { get; init; }
    }

    /// <summary>The release gate's publication and publish journals.</summary>
    public sealed class ReleaseGateJournal
    {
        private static readonly JsonNodeOptions CaseInsensitive = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
        private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        private static readonly JsonSerializerOptions Indented = new JsonSerializerOptions
        {
            WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly TimeProvider _clock;
        private readonly Action<string, string> _write;

        /// <summary>Journals stamped from clock and written by write (path, text); a null write is WriteAtomic.</summary>
        public ReleaseGateJournal(TimeProvider clock, Action<string, string> write = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _write = write ?? WriteAtomic;
        }

        /// <summary>The production journals: the system clock and the atomic writer.</summary>
        public static ReleaseGateJournal Production() => new ReleaseGateJournal(TimeProvider.System);

        private string Now() => _clock.GetUtcNow().UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

        // ---- reading and writing -----------------------------------------------------------------------------------

        /// <summary>
        /// Write-NightlyAtomicJson's replace for text: UTF-8 without BOM to a temporary file beside path, then renamed over
        /// it (the directory is created when missing).
        /// </summary>
        public static void WriteAtomic(string path, string text)
        {
            var full = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, text ?? "", new System.Text.UTF8Encoding(false));
                File.Move(temp, full, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        /// <summary>A document as the journals write it: indented JSON.</summary>
        public static string Text(JsonNode document) => document == null ? "null" : document.ToJsonString(Indented);

        private void Write(string path, JsonNode document) => _write(path, Text(document));

        /// <summary>One JSON document with case-insensitive names (comments and trailing commas allowed); throws JsonException.</summary>
        public static JsonNode Parse(string text) => JsonNode.Parse(text ?? "", CaseInsensitive, Lenient);

        /// <summary>
        /// Get-ReleaseGatePublicationJournal: the journal at path, or { schemaVersion: 1, reservations: [] } when path is
        /// blank or missing. A file that is not a JSON object refuses ("unreadable publication journal &lt;path&gt;: ...").
        /// </summary>
        public static JsonObject Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new JsonObject(CaseInsensitive) { ["schemaVersion"] = ReleaseGatePublication.ManifestSchema, ["reservations"] = new JsonArray() };
            try
            {
                if (Parse(File.ReadAllText(path)) is JsonObject journal) return journal;
                throw new NightlyRefusal("unreadable publication journal " + path + ": not a JSON object");
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException)
            {
                throw new NightlyRefusal("unreadable publication journal " + path + ": " + e.Message);
            }
        }

        /// <summary>@($journal.reservations): the array's entries, one entry for a single value, none when absent.</summary>
        public static IReadOnlyList<JsonNode> Entries(JsonObject journal)
        {
            var value = journal?["reservations"];
            if (value == null) return Array.Empty<JsonNode>();
            if (value is JsonArray array) return new List<JsonNode>(array);
            return new[] { value };
        }

        /// <summary>The reservation rows (the entries that are JSON objects), in journal order.</summary>
        public static IReadOnlyList<JsonObject> Rows(JsonObject journal)
        {
            var rows = new List<JsonObject>();
            foreach (var entry in Entries(journal))
                if (entry is JsonObject row) rows.Add(row);
            return rows;
        }

        /// <summary>[string] of a JSON value: a string as is, a number or boolean as text (True/False), null or absent "".</summary>
        public static string Str(JsonNode value)
        {
            if (value == null) return "";
            switch (value.GetValueKind())
            {
                case JsonValueKind.String: return value.GetValue<string>();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Null: return "";
                default: return value.ToJsonString();
            }
        }

        /// <summary>[bool] of a JSON value: true, a non-empty string, a non-zero number or any object or array.</summary>
        public static bool Truthy(JsonNode value)
        {
            if (value == null) return false;
            switch (value.GetValueKind())
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.String: return value.GetValue<string>().Length > 0;
                case JsonValueKind.Number:
                    return double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number != 0;
                case JsonValueKind.Object:
                case JsonValueKind.Array: return true;
                default: return false;
            }
        }

        private static bool IsString(JsonNode value) => value != null && value.GetValueKind() == JsonValueKind.String;

        private static bool IsBool(JsonNode value) => value != null && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False;

        private static bool IsInteger(JsonNode value, out long whole)
        {
            whole = 0;
            if (value == null || value.GetValueKind() != JsonValueKind.Number) return false;
            var raw = value.ToJsonString();
            return raw.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out whole);
        }

        // [int] of a row field; absent or blank 0 (Get-ReleaseGateOptionalInt).
        private static int Int(JsonObject row, string name)
        {
            var text = Str(row?[name]);
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return (int)Math.Round(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture), MidpointRounding.ToEven);
        }

        private static JsonNode Clone(JsonNode node) => node?.DeepClone();

        /// <summary>
        /// New-ReleaseGateReservationEntry: a row with the historical keys in order; publishedAt, rc and finalTag are written
        /// only when set, so a later publish of the same row neither drops nor invents them.
        /// </summary>
        public static JsonObject Entry(string candidateId, string sha, string tag, int sequence, string reservedAt, bool published, string releaseId,
            string releaseUrl, string publishedAt, int rc, string finalTag)
        {
            var row = new JsonObject(CaseInsensitive)
            {
                ["candidateId"] = candidateId ?? "", ["sha"] = sha ?? "", ["tag"] = tag ?? "", ["sequence"] = sequence,
                ["reservedAt"] = reservedAt ?? "", ["published"] = published, ["releaseId"] = releaseId ?? "", ["releaseUrl"] = releaseUrl ?? "",
            };
            if (!string.IsNullOrWhiteSpace(publishedAt)) row["publishedAt"] = publishedAt;
            if (rc >= 1) row["rc"] = rc;
            if (!string.IsNullOrWhiteSpace(finalTag)) row["finalTag"] = finalTag;
            return row;
        }

        private void WriteReservations(string path, IEnumerable<JsonNode> entries)
        {
            var array = new JsonArray();
            foreach (var entry in entries) array.Add(entry);
            Write(path, new JsonObject(CaseInsensitive) { ["schemaVersion"] = ReleaseGatePublication.ManifestSchema, ["reservations"] = array });
        }

        // ---- reservations (D-4, D-8) -------------------------------------------------------------------------------

        /// <summary>
        /// Get-ReleaseGateTagProposal: the reservation Reserve would make, computed read-only - the existing row for this
        /// candidate (or this SHA), or the next free daily sequence. It never writes, so a publish -WhatIf reports the
        /// proposed tag without reserving it. The same candidate id at another SHA, or the same SHA under another
        /// candidate, refuses (D-3 immutability). A long-lived candidate (rc-YYYY-MM-DD-&lt;sha12&gt;) is proposed
        /// vYYYY.MM.DD.N-rc.K (ReleaseGateTags.Allocation over the journal and the remote tags); a legacy stamp candidate the
        /// smallest N free that UTC day. cutUtc MinValue is now. remoteTags is required: the caller passes the remote
        /// inventory (empty is explicit), never nothing.
        /// </summary>
        public ReleaseGateProposal Proposal(string journalPath, string candidateId, string sha, DateTime cutUtc, string[] remoteTags)
        {
            if (remoteTags == null) throw new ArgumentNullException(nameof(remoteTags), "RemoteTags is required (the remote tag inventory; empty is explicit)");
            if (!ReleaseGateCredit.FullSha(sha)) throw new NightlyRefusal("reservation requires a full sha, got " + sha);
            if (cutUtc == DateTime.MinValue) cutUtc = _clock.GetUtcNow().UtcDateTime;
            var journal = Read(journalPath);
            var rows = Rows(journal);
            JsonObject existing = null;
            foreach (var row in rows)
                if (string.Equals(Str(row["candidateId"]), candidateId ?? "", StringComparison.OrdinalIgnoreCase)) { existing = row; break; }
            if (existing == null)
                foreach (var row in rows)
                    if (string.Equals(Str(row["sha"]), sha, StringComparison.OrdinalIgnoreCase)) { existing = row; break; }
            if (existing != null)
            {
                if (!string.Equals(Str(existing["candidateId"]), candidateId ?? "", StringComparison.OrdinalIgnoreCase))
                    throw new NightlyRefusal("sha " + sha + " already reserved under candidate " + Str(existing["candidateId"]));
                if (!string.Equals(Str(existing["sha"]), sha, StringComparison.OrdinalIgnoreCase))
                    throw new NightlyRefusal("candidate " + candidateId + " already reserved for sha " + Str(existing["sha"]));
                return new ReleaseGateProposal
                {
                    Tag = Str(existing["tag"]), Sequence = Int(existing, "sequence"), Rc = Int(existing, "rc"), Reused = true, Row = existing, Journal = journal,
                };
            }
            if (ReleaseGateTags.LongLivedCandidateId(candidateId))
            {
                var occupancy = new List<(string Tag, string FinalTag)>();
                foreach (var row in rows) occupancy.Add((Str(row["tag"]), Str(row["finalTag"])));
                foreach (var remote in remoteTags) occupancy.Add((remote ?? "", ""));
                var allocation = ReleaseGateTags.Allocation(occupancy, cutUtc);
                return new ReleaseGateProposal
                {
                    Tag = ReleaseGateTags.NewPreReleaseTag(cutUtc, allocation.Sequence, allocation.Rc), Sequence = allocation.Sequence, Rc = allocation.Rc,
                    Journal = journal,
                };
            }
            var day = cutUtc.ToUniversalTime().ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
            var used = new HashSet<int>();
            var tags = new List<string>();
            foreach (var row in rows) tags.Add(Str(row["tag"]));
            tags.AddRange(remoteTags);
            foreach (var text in tags)
            {
                var parsed = ReleaseGateTags.Tag(text);
                if (parsed.Ok && ReleaseGateTags.SameDay(parsed, day)) used.Add(parsed.Sequence);
            }
            var next = 1;
            while (used.Contains(next)) next++;
            return new ReleaseGateProposal { Tag = ReleaseGateTags.NewTag(cutUtc, next), Sequence = next, Journal = journal };
        }

        /// <summary>
        /// New-ReleaseGateTagReservation: the candidate's existing reservation (Reused), or the proposal appended to the
        /// journal as an unpublished row stamped reservedAt now. Idempotent by candidate id; refuses as Proposal does.
        /// </summary>
        public ReleaseGateProposal Reserve(string journalPath, string candidateId, string sha, DateTime cutUtc, string[] remoteTags)
        {
            var proposal = Proposal(journalPath, candidateId, sha, cutUtc, remoteTags);
            if (proposal.Reused) return proposal;
            var entries = new List<JsonNode>();
            foreach (var entry in Entries(proposal.Journal)) entries.Add(Clone(entry));
            entries.Add(Entry(candidateId, sha, proposal.Tag, proposal.Sequence, Now(), false, "", "", "", proposal.Rc, ""));
            WriteReservations(journalPath, entries);
            return new ReleaseGateProposal { Tag = proposal.Tag, Sequence = proposal.Sequence, Rc = proposal.Rc, Journal = proposal.Journal };
        }

        /// <summary>
        /// Set-ReleaseGateReservationPublished: the candidate's row (matched ignoring case) rewritten as published with the
        /// release id and URL and publishedAt now; its rc and finalTag kept. No row for the candidate refuses.
        /// </summary>
        public void MarkPublished(string journalPath, string candidateId, string releaseId, string releaseUrl)
        {
            var journal = Read(journalPath);
            var entries = new List<JsonNode>();
            var found = false;
            foreach (var entry in Entries(journal))
            {
                if (entry is JsonObject row && string.Equals(Str(row["candidateId"]), candidateId ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    entries.Add(Entry(Str(row["candidateId"]), Str(row["sha"]), Str(row["tag"]), Int(row, "sequence"), Str(row["reservedAt"]), true,
                        releaseId ?? "", releaseUrl ?? "", Now(), Int(row, "rc"), Str(row["finalTag"])));
                }
                else entries.Add(Clone(entry));
            }
            if (!found) throw new NightlyRefusal("no reservation for candidate " + candidateId);
            WriteReservations(journalPath, entries);
        }

        /// <summary>
        /// The release notes' previous release: the tag of the last published row of another candidate (ignoring case);
        /// empty when there is none.
        /// </summary>
        public static string PreviousPublishedTag(string journalPath, string candidateId)
        {
            var previous = "";
            foreach (var row in Rows(Read(journalPath)))
                if (Truthy(row["published"]) && !string.Equals(Str(row["candidateId"]), candidateId ?? "", StringComparison.OrdinalIgnoreCase))
                    previous = Str(row["tag"]);
            return previous;
        }

        // ---- promotion (CARD-1311, CARD-1339) ----------------------------------------------------------------------

        /// <summary>
        /// Test-ReleaseGatePromotionReservation: promotion consumes an existing published reservation; it never creates one.
        /// The journal must be a JSON object with an integer schemaVersion 1 and a reservations array of objects, each with
        /// string candidateId, sha, tag, releaseId and releaseUrl, a boolean published and, when present, a string finalTag.
        /// Exactly one row may name the candidate (ordinal), the SHA (ignoring case) or the pre-release tag (ordinal); it
        /// must be that candidate, a full SHA equal to sha, that tag, published with a release id and URL, and finalTag must
        /// be the pre-release's final spelling (and the row's finalTag, when set, that same tag).
        /// </summary>
        public static ReleaseGatePromotionCheck PromotionReservation(string journalPath, string candidateId, string sha, string preReleaseTag, string finalTag)
        {
            ReleaseGatePromotionCheck Fail(string reason) => new ReleaseGatePromotionCheck { Reason = "promotion-reservation-" + reason };
            if (string.IsNullOrEmpty(journalPath) || !File.Exists(journalPath)) return Fail("missing");
            JsonObject journal;
            try
            {
                var raw = File.ReadAllText(journalPath);
                // A root array (even of one object) is no journal.
                if (!raw.TrimStart().StartsWith("{", StringComparison.Ordinal)) return Fail("invalid");
                journal = Parse(raw) as JsonObject;
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException) { return Fail("invalid"); }
            if (journal == null || !IsInteger(journal["schemaVersion"], out var schema) || schema != 1 || !(journal["reservations"] is JsonArray reservations))
                return Fail("invalid");
            var matching = new List<JsonObject>();
            foreach (var entry in reservations)
            {
                if (!(entry is JsonObject row)) return Fail("invalid");
                foreach (var field in new[] { "candidateId", "sha", "tag", "releaseId", "releaseUrl" })
                    if (!IsString(row[field])) return Fail("invalid");
                if (!IsBool(row["published"]) || (row.ContainsKey("finalTag") && !IsString(row["finalTag"]))) return Fail("invalid");
                if (string.Equals(Str(row["candidateId"]), candidateId ?? "", StringComparison.Ordinal)
                    || string.Equals(Str(row["sha"]), sha ?? "", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Str(row["tag"]), preReleaseTag ?? "", StringComparison.Ordinal))
                    matching.Add(row);
            }
            if (matching.Count == 0) return Fail("missing");
            if (matching.Count != 1) return Fail("ambiguous");
            var admitted = matching[0];
            if (!string.Equals(Str(admitted["candidateId"]), candidateId ?? "", StringComparison.Ordinal)) return Fail("candidate-mismatch");
            if (!ReleaseGateCredit.FullSha(Str(admitted["sha"])) || !string.Equals(Str(admitted["sha"]), sha ?? "", StringComparison.OrdinalIgnoreCase))
                return Fail("sha-mismatch");
            if (!string.Equals(Str(admitted["tag"]), preReleaseTag ?? "", StringComparison.Ordinal)) return Fail("tag-mismatch");
            if (!admitted["published"].GetValue<bool>()) return Fail("unpublished");
            if (string.IsNullOrWhiteSpace(Str(admitted["releaseId"])) || string.IsNullOrWhiteSpace(Str(admitted["releaseUrl"]))) return Fail("release-identity");
            var parsed = ReleaseGateTags.Tag(preReleaseTag);
            var recorded = Str(admitted["finalTag"]);
            if (!parsed.Ok || !parsed.Prerelease || !string.Equals(finalTag ?? "", ReleaseGateTags.NewTag(parsed.Date, parsed.Sequence), StringComparison.Ordinal)
                || (!string.IsNullOrEmpty(recorded) && !string.Equals(recorded, finalTag, StringComparison.Ordinal)))
                return Fail("final-conflict");
            return new ReleaseGatePromotionCheck { Ok = true, Journal = journal, Row = admitted };
        }

        /// <summary>
        /// Set-ReleaseGateReservationFinalTag: re-reads and validates the admitted tuple immediately before the atomic
        /// replace, records finalTag on its row (every other value unchanged), and validates again. False when either
        /// check refuses or the recorded finalTag is not this one; a writer failure throws.
        /// </summary>
        public bool RecordFinalTag(string journalPath, string candidateId, string sha, string preReleaseTag, string finalTag)
        {
            var check = PromotionReservation(journalPath, candidateId, sha, preReleaseTag, finalTag);
            if (!check.Ok) return false;
            check.Row["finalTag"] = finalTag;
            Write(journalPath, check.Journal);
            var recorded = PromotionReservation(journalPath, candidateId, sha, preReleaseTag, finalTag);
            return recorded.Ok && string.Equals(Str(recorded.Row["finalTag"]), finalTag, StringComparison.Ordinal);
        }

        // ---- the publish journal ------------------------------------------------------------------------------------

        /// <summary>
        /// Write-ReleaseGateJournalStep: durable intent before every remote write. The journal at path (its values kept)
        /// gains step in steps (once), the given fields (replacing earlier values) and updatedAt now, and is replaced
        /// atomically. An unreadable journal refuses ("unreadable journal &lt;path&gt;: ...").
        /// </summary>
        public JsonObject WriteStep(string path, string step, IDictionary fields)
        {
            var journal = new JsonObject(CaseInsensitive);
            if (File.Exists(path))
            {
                try
                {
                    if (Parse(File.ReadAllText(path)) is JsonObject existing)
                        foreach (var property in existing) journal[property.Key] = Clone(property.Value);
                }
                catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException)
                {
                    throw new NightlyRefusal("unreadable journal " + path + ": " + e.Message);
                }
            }
            var steps = new JsonArray();
            var seen = new List<string>();
            foreach (var entry in StepEntries(journal))
            {
                var text = Str(entry);
                seen.Add(text);
                steps.Add(text);
            }
            if (!seen.Exists(s => string.Equals(s, step ?? "", StringComparison.OrdinalIgnoreCase))) steps.Add(step ?? "");
            journal["steps"] = steps;
            if (fields != null)
                foreach (DictionaryEntry field in fields) journal[PsValue.Str(field.Key)] = Node(field.Value);
            journal["updatedAt"] = Now();
            Write(path, journal);
            return journal;
        }

        /// <summary>Test-ReleaseGateJournalStep: the journal at path lists step (ignoring case); false when it is missing or unreadable.</summary>
        public static bool HasStep(string path, string step)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            JsonObject journal;
            try { journal = Parse(File.ReadAllText(path)) as JsonObject; }
            catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException) { return false; }
            foreach (var entry in StepEntries(journal))
                if (string.Equals(Str(entry), step ?? "", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static IEnumerable<JsonNode> StepEntries(JsonObject journal)
        {
            var steps = journal?["steps"];
            if (steps == null) yield break;
            if (steps is JsonArray array)
            {
                foreach (var entry in array) yield return entry;
                yield break;
            }
            yield return steps;
        }

        // A field value from the caller's bag: strings, booleans and numbers as themselves, anything else as its text.
        private static JsonNode Node(object value)
        {
            value = PsValue.Unwrap(value);
            switch (value)
            {
                case null: return null;
                case string s: return JsonValue.Create(s);
                case bool b: return JsonValue.Create(b);
                case int i: return JsonValue.Create(i);
                case long l: return JsonValue.Create(l);
                case double d: return JsonValue.Create(d);
                case DateTime instant: return JsonValue.Create(instant.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                case JsonNode node: return Clone(node);
                default: return JsonValue.Create(PsValue.Str(value));
            }
        }
    }
}
