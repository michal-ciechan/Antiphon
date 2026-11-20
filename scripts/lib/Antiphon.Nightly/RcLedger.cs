// CARD-1836 S1 (moved from rc-ledger.ps1): schema antiphon-rc-ledger-v1 - read, write, validate and carry decisions.
// CARD-1842 S6: antiphon-rc-ledger-v2 is v1 plus optional, nullable chunk hostId and os. SetDurations fills durations on
// either format; SetHost needs v2. Read is default-deny: duplicate names (case-insensitive), names outside the closed
// schema and JSON values of the wrong type are ledger-invalid before any carry. Pure: no network, no git, no process,
// no PowerShell. Self-contained: the scripts CLI links this file for its rc-ledger-stamp command.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Antiphon.Nightly
{
    /// <summary>A ledger refusal: ledger-missing, ledger-exists, ledger-invalid: ..., ledger-schema, ledger-host: ..., duration-...</summary>
    public sealed class RcLedgerRefusal : Exception
    {
        public RcLedgerRefusal(string message) : base(message) { }
    }

    public sealed class RcLedgerContract
    {
        public string PolicyHash { get; set; }
        public string CatalogHash { get; set; }
        public string SelectorHash { get; set; }
        public string RuntimeHash { get; set; }
    }

    public sealed class RcCarriedFrom
    {
        public string CandidateId { get; set; }
        public string RunId { get; set; }
        public string CandidateSha { get; set; }
        public string ChunkId { get; set; }
        public string InputDigest { get; set; }
    }

    public sealed class RcLedgerIdentity
    {
        public string Suite { get; set; }
        public string ClassName { get; set; }
        public string Method { get; set; }
        public string Boundary { get; set; }
        public string InputDigest { get; set; }
        public string Outcome { get; set; }
        public double? DurationSeconds { get; set; }
        public RcCarriedFrom CarriedFrom { get; set; }
        public string Key => (ClassName ?? "") + "/" + (Method ?? "");
    }

    public sealed class RcLedgerChunk
    {
        private string hostId, os;
        public string ChunkId { get; set; }
        public string Boundary { get; set; }
        public string Suite { get; set; }
        public string[] IdentityKeys { get; set; }
        public string InputDigest { get; set; }
        public string Outcome { get; set; }
        public double? DurationSeconds { get; set; }
        // v2 only. A v1 chunk that names either field, even as null, is invalid.
        public string HostId { get => hostId; set { hostId = value; HostIdNamed = true; } }
        public string Os { get => os; set { os = value; OsNamed = true; } }
        public bool HostIdNamed { get; private set; }
        public bool OsNamed { get; private set; }
    }

    public sealed class RcLedgerDocument
    {
        public string Format { get; set; } = RcLedger.Format;
        public string Platform { get; set; }
        public string Repository { get; set; }
        public string CandidateId { get; set; }
        public string CandidateSha { get; set; }
        public string BaselineSha { get; set; }
        public string BaselineRef { get; set; }
        public string RunId { get; set; }
        public double Attempt { get; set; }
        public RcLedgerContract Contract { get; set; }
        public string[] ForcedFull { get; set; }
        public RcLedgerIdentity[] Identities { get; set; }
        public RcLedgerChunk[] Chunks { get; set; }
    }

    public sealed class RcLedgerCheck
    {
        public bool Valid => Errors.Length == 0;
        public string[] Errors { get; init; }
    }

    public enum RcDigestRelation { Invalid, Same, Different }

    public sealed class RcCarryDecision
    {
        public string Decision { get; init; }
        public string Reason { get; init; }
        public string IdentityKey { get; init; }
        public string Outcome { get; init; }
        public RcCarriedFrom CarriedFrom { get; init; }
        public bool Carries => Decision == "carry";
    }

    public static class RcLedger
    {
        public const string Format = "antiphon-rc-ledger-v1";
        public const string FormatV2 = "antiphon-rc-ledger-v2";
        private static readonly string[] Outcomes = { "passed", "failed", "interrupted", "not-run", "unexpected-skip", "expected-skip", "unproven" };

        // ---- shapes ---------------------------------------------------------------------------------------------

        /// <summary>Not blank, and ASCII.</summary>
        public static bool RequiredText(string text) => !string.IsNullOrWhiteSpace(text) && text.All(ch => ch <= 127);

        private static bool DigestText(string digest) =>
            digest != null && digest.Length == 64 && digest.All(ch => (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f'));

        private static bool StoredDigest(string digest) => digest == "unknown" || DigestText(digest);

        private static bool Duration(double? value) => value == null || (!double.IsNaN(value.Value) && value.Value >= 0);

        private static bool UnknownBoundary(string boundary) => string.IsNullOrWhiteSpace(boundary) || boundary == "unknown" || boundary == "unassigned";

        private static int OutcomeRank(string outcome) => outcome switch
        {
            "passed" => 0,
            "expected-skip" => 1,
            "not-run" => 2,
            "interrupted" => 3,
            "unproven" => 4,
            "unexpected-skip" => 5,
            "failed" => 6,
            _ => 100,
        };

        /// <summary>The worst outcome (the first of the highest rank); not-run for none.</summary>
        public static string WorstOutcome(IEnumerable<string> outcomes)
        {
            string best = null;
            var rank = -1;
            foreach (var outcome in outcomes)
            {
                var text = outcome ?? "";
                var next = OutcomeRank(text);
                if (next > rank) { rank = next; best = text; }
            }
            return best ?? "not-run";
        }

        // Distinct (case-insensitive, first wins), in ordinal order.
        private static string[] SortText(IEnumerable<string> values)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var value in values ?? Array.Empty<string>()) if (seen.Add(value ?? "")) list.Add(value ?? "");
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        /// <summary>Both digests 64 lower-case hex: same or different; otherwise invalid.</summary>
        public static RcDigestRelation CompareInputDigest(string left, string right)
        {
            if (!DigestText(left) || !DigestText(right)) return RcDigestRelation.Invalid;
            return string.Equals(left, right, StringComparison.Ordinal) ? RcDigestRelation.Same : RcDigestRelation.Different;
        }

        // ---- carry decisions ---------------------------------------------------------------------------------------

        private static RcCarryDecision Decision(string decision, string reason, string key, string outcome, RcCarriedFrom carriedFrom = null) =>
            new RcCarryDecision { Decision = decision, Reason = reason, IdentityKey = key, Outcome = outcome, CarriedFrom = carriedFrom };

        // The identity's own provenance when it was itself carried, otherwise the source ledger's.
        private static RcCarriedFrom CarriedFrom(RcLedgerDocument ledger, RcLedgerIdentity identity, RcLedgerChunk chunk)
        {
            var existing = identity?.CarriedFrom;
            if (!string.IsNullOrWhiteSpace(existing?.RunId))
                return new RcCarriedFrom
                {
                    CandidateId = existing.CandidateId ?? "", RunId = existing.RunId, CandidateSha = existing.CandidateSha ?? "",
                    ChunkId = existing.ChunkId ?? "", InputDigest = existing.InputDigest ?? "",
                };
            return new RcCarriedFrom
            {
                CandidateId = ledger?.CandidateId ?? "", RunId = ledger?.RunId ?? "", CandidateSha = ledger?.CandidateSha ?? "",
                ChunkId = chunk?.ChunkId ?? "", InputDigest = identity?.InputDigest ?? "",
            };
        }

        private static bool CarryProvenance(RcCarriedFrom carried) =>
            carried != null && RequiredText(carried.CandidateId) && RequiredText(carried.RunId) && RequiredText(carried.CandidateSha)
            && RequiredText(carried.ChunkId) && DigestText(carried.InputDigest);

        // A pass (or expected skip) and a failure recorded for the same input digest.
        private static bool SameDigestFlake(RcLedgerIdentity sourceIdentity, IEnumerable<RcLedgerIdentity> history, string newDigest)
        {
            var sawPass = false;
            var sawFail = false;
            var rows = new List<RcLedgerIdentity>();
            if (sourceIdentity != null) rows.Add(sourceIdentity);
            rows.AddRange((history ?? Array.Empty<RcLedgerIdentity>()).Where(r => r != null));
            foreach (var row in rows)
            {
                if (!DigestText(row.InputDigest) || row.InputDigest != newDigest) continue;
                if (row.Outcome == "passed" || row.Outcome == "expected-skip") sawPass = true;
                else if (row.Outcome == "failed") sawFail = true;
            }
            return sawPass && sawFail;
        }

        private static bool Forced(IEnumerable<string> forcedFull) => (forcedFull ?? Array.Empty<string>()).Any(f => !string.IsNullOrWhiteSpace(f));

        /// <summary>
        /// Carry or dirty for one identity. A passed or expected-skip row of the same platform and input digest, inside a
        /// closed chunk with complete provenance, carries. A forced Full, an unknown boundary, a missing row, another
        /// platform, a missing or changed digest, any other outcome, an open chunk or an unproven same-digest flake is
        /// dirty. NotExecuted and Skipped rows are expected skips only when the roster names them.
        /// </summary>
        public static RcCarryDecision IdentityCarryDecision(string platform, string boundary, string newDigest, RcLedgerDocument sourceLedger,
            RcLedgerIdentity sourceIdentity, RcLedgerChunk sourceChunk, IEnumerable<RcLedgerIdentity> history, bool fixProven,
            IEnumerable<string> expectedSkipRoster, IEnumerable<string> forcedFull)
        {
            var key = sourceIdentity?.Key ?? "";
            if (Forced(forcedFull)) return Decision("dirty", "forced-full", key, "unproven");
            if (UnknownBoundary(boundary) || (sourceIdentity != null && UnknownBoundary(sourceIdentity.Boundary))) return Decision("dirty", "unknown-boundary", key, "unproven");
            if (sourceIdentity == null || sourceLedger == null) return Decision("dirty", "missing-row", key, "not-run");
            if ((sourceLedger.Platform ?? "") != (platform ?? "")) return Decision("dirty", "cross-platform", key, "unproven");
            var compared = CompareInputDigest(sourceIdentity.InputDigest, newDigest);
            if (compared == RcDigestRelation.Invalid) return Decision("dirty", "missing-digest", key, "unproven");
            if (compared != RcDigestRelation.Same) return Decision("dirty", "digest-mismatch", key, "unproven");
            var outcome = sourceIdentity.Outcome ?? "";
            if (outcome == "NotExecuted" || outcome == "Skipped")
                outcome = (expectedSkipRoster ?? Array.Empty<string>()).Any(name => (name ?? "") == key) ? "expected-skip" : "unexpected-skip";
            if (outcome == "unexpected-skip" || outcome == "interrupted" || outcome == "not-run" || outcome == "failed" || outcome == "unproven")
                return Decision("dirty", outcome, key, outcome);
            if (outcome != "passed" && outcome != "expected-skip") return Decision("dirty", "unknown-outcome", key, "unproven");
            if (sourceChunk == null) return Decision("dirty", "chunk-unknown", key, outcome);
            if (sourceChunk.Outcome == "interrupted") return Decision("dirty", "chunk-interrupted", key, outcome);
            if (sourceChunk.Outcome != "passed" && sourceChunk.Outcome != "expected-skip") return Decision("dirty", "chunk-open", key, outcome);
            if (SameDigestFlake(sourceIdentity, history, newDigest) && !fixProven) return Decision("dirty", "flake", key, "unproven");
            var carried = CarriedFrom(sourceLedger, sourceIdentity, sourceChunk);
            if (!CarryProvenance(carried)) return Decision("dirty", "provenance-incomplete", key, outcome);
            return Decision("carry", "carry", key, outcome, carried);
        }

        /// <summary>
        /// Carry or dirty for a chunk: its own row must carry (platform, digest, closed outcome) and so must every
        /// identity decision; otherwise dirty, naming the ordinally first dirty identity and its reason.
        /// </summary>
        public static RcCarryDecision ChunkCarryDecision(string platform, string newDigest, RcLedgerDocument sourceLedger, RcLedgerChunk sourceChunk,
            IEnumerable<RcCarryDecision> identityDecisions, IEnumerable<string> forcedFull)
        {
            if (Forced(forcedFull)) return Decision("dirty", "forced-full", "", "unproven");
            if (sourceChunk == null || sourceLedger == null) return Decision("dirty", "chunk-unknown", "", "not-run");
            if (UnknownBoundary(sourceChunk.Boundary)) return Decision("dirty", "unknown-boundary", "", "unproven");
            if ((sourceLedger.Platform ?? "") != (platform ?? "")) return Decision("dirty", "cross-platform", "", "unproven");
            var compared = CompareInputDigest(sourceChunk.InputDigest, newDigest);
            if (compared == RcDigestRelation.Invalid) return Decision("dirty", "missing-digest", "", "unproven");
            if (compared != RcDigestRelation.Same) return Decision("dirty", "digest-mismatch", "", "unproven");
            var chunkOutcome = sourceChunk.Outcome ?? "";
            if (chunkOutcome == "interrupted") return Decision("dirty", "chunk-interrupted", "", chunkOutcome);
            if (chunkOutcome != "passed" && chunkOutcome != "expected-skip") return Decision("dirty", "chunk-open", "", chunkOutcome);
            var decisions = (identityDecisions ?? Array.Empty<RcCarryDecision>()).Where(d => d != null).ToList();
            if (decisions.Count == 0) return Decision("dirty", "identity-missing", "", chunkOutcome);
            RcCarryDecision first = null;
            foreach (var decision in decisions)
                if (decision.Decision != "carry" && (first == null || string.CompareOrdinal(decision.IdentityKey ?? "", first.IdentityKey ?? "") < 0)) first = decision;
            if (first != null) return Decision("dirty", "identity:" + first.IdentityKey + ":" + first.Reason, "", chunkOutcome);
            return Decision("carry", "carry", "", chunkOutcome);
        }

        // ---- the document -------------------------------------------------------------------------------------------

        /// <summary>Every schema and cross-reference fault, in document order. Identity keys compare case-insensitively.</summary>
        public static RcLedgerCheck CheckDocument(RcLedgerDocument document)
        {
            var errors = new List<string>();
            if (document == null) return new RcLedgerCheck { Errors = new[] { "document-missing" } };
            var format = document.Format ?? "";
            if (format != Format && format != FormatV2) errors.Add("format");
            if (document.Platform != "windows" && document.Platform != "linux") errors.Add("platform");
            if (!RequiredText(document.Repository)) errors.Add("repository");
            if (!RequiredText(document.CandidateId)) errors.Add("candidateId");
            if (!RequiredText(document.CandidateSha)) errors.Add("candidateSha");
            if (!RequiredText(document.RunId)) errors.Add("runId");
            if (document.BaselineSha != null && !RequiredText(document.BaselineSha)) errors.Add("baselineSha");
            if (document.BaselineRef != null && !RequiredText(document.BaselineRef)) errors.Add("baselineRef");
            var attempt = document.Attempt;
            if (double.IsNaN(attempt) || attempt != Math.Truncate(attempt) || attempt < 1 || attempt > int.MaxValue) errors.Add("attempt");
            var contract = document.Contract;
            if (!RequiredText(contract?.PolicyHash)) errors.Add("contract-policyHash");
            if (!RequiredText(contract?.CatalogHash)) errors.Add("contract-catalogHash");
            if (!RequiredText(contract?.SelectorHash)) errors.Add("contract-selectorHash");
            if (!RequiredText(contract?.RuntimeHash)) errors.Add("contract-runtimeHash");
            var identityMap = new Dictionary<string, RcLedgerIdentity>(StringComparer.OrdinalIgnoreCase);
            var identities = document.Identities ?? Array.Empty<RcLedgerIdentity>();
            if (identities.Length == 0) errors.Add("identities-empty");
            foreach (var identity in identities)
            {
                if (identity == null) { errors.Add("identity-missing"); continue; }
                var key = identity.Key;
                if (identityMap.ContainsKey(key)) errors.Add("duplicate-identity:" + key);
                identityMap[key] = identity;
                foreach (var (name, text) in new[] { ("suite", identity.Suite), ("className", identity.ClassName), ("method", identity.Method), ("boundary", identity.Boundary) })
                    if (!RequiredText(text) || text.Contains('/')) errors.Add("identity-" + name);
                if (!StoredDigest(identity.InputDigest)) errors.Add("identity-digest");
                if (Array.IndexOf(Outcomes, identity.Outcome) < 0) errors.Add("identity-outcome:" + identity.Outcome);
                if (!Duration(identity.DurationSeconds)) errors.Add("identity-duration");
                if (identity.CarriedFrom != null && !CarryProvenance(identity.CarriedFrom)) errors.Add("carriedFrom");
            }
            var chunks = document.Chunks ?? Array.Empty<RcLedgerChunk>();
            if (chunks.Length == 0) errors.Add("chunks-empty");
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in chunks)
            {
                if (chunk == null) { errors.Add("chunk-missing"); continue; }
                if (!RequiredText(chunk.ChunkId)) errors.Add("chunk-id");
                if (!RequiredText(chunk.Boundary) || !RequiredText(chunk.Suite)) errors.Add("chunk-boundary");
                if (!StoredDigest(chunk.InputDigest)) errors.Add("chunk-digest");
                if (!Duration(chunk.DurationSeconds)) errors.Add("chunk-duration");
                foreach (var (name, named, value) in new[] { ("hostId", chunk.HostIdNamed, chunk.HostId), ("os", chunk.OsNamed, chunk.Os) })
                {
                    if (!named) continue;
                    if (format != FormatV2) { errors.Add("unknown-property:" + name); continue; }
                    if (value == null) continue;
                    if (!RequiredText(value)) errors.Add("chunk-" + name);
                    else if (name == "os" && value != "windows" && value != "linux") errors.Add("chunk-os");
                }
                var keys = chunk.IdentityKeys ?? Array.Empty<string>();
                if (keys.Length == 0) errors.Add("chunk-keys-empty");
                var memberOutcomes = new List<string>();
                foreach (var keyValue in keys)
                {
                    var key = keyValue ?? "";
                    if (!identityMap.TryGetValue(key, out var identity)) { errors.Add("chunk-missing-identity:" + key); continue; }
                    if (!seenKeys.Add(key)) errors.Add("identity-two-chunks:" + key);
                    if (identity.Boundary != chunk.Boundary) errors.Add("boundary-disagreement:" + key);
                    if (identity.Suite != chunk.Suite) errors.Add("suite-disagreement:" + key);
                    if (identity.InputDigest != chunk.InputDigest) errors.Add("digest-disagreement:" + key);
                    memberOutcomes.Add(identity.Outcome);
                }
                if (chunk.Outcome != WorstOutcome(memberOutcomes)) errors.Add("chunk-outcome:" + chunk.ChunkId);
            }
            foreach (var key in identityMap.Keys)
                if (!seenKeys.Contains(key)) errors.Add("identity-without-chunk:" + key);
            return new RcLedgerCheck { Errors = errors.ToArray() };
        }

        private static void Refuse(RcLedgerCheck check)
        {
            if (!check.Valid) throw new RcLedgerRefusal("ledger-invalid: " + string.Join("; ", check.Errors));
        }

        /// <summary>
        /// The v1 ledger of a document: identities ordered by suite, class and method; chunk identity keys distinct and
        /// ordinally sorted; chunks ordered by id, each chunk's outcome its members' worst; forcedFull distinct and
        /// sorted; carried provenance copied. Refuses a document that does not validate.
        /// </summary>
        public static RcLedgerDocument Normalize(RcLedgerDocument document)
        {
            if (document == null) throw new RcLedgerRefusal("ledger-invalid: document-missing");
            var identities = (document.Identities ?? Array.Empty<RcLedgerIdentity>()).Select(identity => new RcLedgerIdentity
            {
                Suite = identity?.Suite ?? "", ClassName = identity?.ClassName ?? "", Method = identity?.Method ?? "",
                Boundary = identity?.Boundary ?? "", InputDigest = identity?.InputDigest ?? "", Outcome = identity?.Outcome ?? "",
                DurationSeconds = identity?.DurationSeconds,
                CarriedFrom = identity?.CarriedFrom != null ? CarriedFrom(null, identity, null) : null,
            }).OrderBy(i => i.Suite, StringComparer.Ordinal).ThenBy(i => i.ClassName, StringComparer.Ordinal).ThenBy(i => i.Method, StringComparer.Ordinal).ToArray();
            var byKey = new Dictionary<string, RcLedgerIdentity>(StringComparer.OrdinalIgnoreCase);
            foreach (var identity in identities) byKey[identity.Key] = identity;
            var chunks = (document.Chunks ?? Array.Empty<RcLedgerChunk>()).Select(chunk =>
            {
                var keys = SortText(chunk?.IdentityKeys);
                return new RcLedgerChunk
                {
                    ChunkId = chunk?.ChunkId ?? "", Boundary = chunk?.Boundary ?? "", Suite = chunk?.Suite ?? "", IdentityKeys = keys,
                    InputDigest = chunk?.InputDigest ?? "", DurationSeconds = chunk?.DurationSeconds,
                    Outcome = WorstOutcome(keys.Where(byKey.ContainsKey).Select(k => byKey[k].Outcome)),
                };
            }).OrderBy(c => c.ChunkId, StringComparer.Ordinal).ToArray();
            var contract = document.Contract;
            var normalized = new RcLedgerDocument
            {
                Format = Format, Platform = document.Platform ?? "", Repository = document.Repository ?? "", CandidateId = document.CandidateId ?? "",
                CandidateSha = document.CandidateSha ?? "", BaselineSha = document.BaselineSha, BaselineRef = document.BaselineRef, RunId = document.RunId ?? "",
                Attempt = document.Attempt,
                Contract = new RcLedgerContract
                {
                    PolicyHash = contract?.PolicyHash ?? "", CatalogHash = contract?.CatalogHash ?? "",
                    SelectorHash = contract?.SelectorHash ?? "", RuntimeHash = contract?.RuntimeHash ?? "",
                },
                ForcedFull = SortText(document.ForcedFull), Identities = identities, Chunks = chunks,
            };
            Refuse(CheckDocument(normalized));
            return normalized;
        }

        // ---- the writer ---------------------------------------------------------------------------------------------

        private static void JsonString(StringBuilder sb, string text)
        {
            sb.Append('"');
            foreach (var ch in text ?? "")
            {
                if (ch > 127 || (ch < 32 && ch != '\n' && ch != '\r' && ch != '\t')) throw new RcLedgerRefusal("rc-ledger-non-ascii");
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            sb.Append('"');
        }

        private static void JsonNumber(StringBuilder sb, double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number)) throw new RcLedgerRefusal("rc-ledger-bad-number");
            var whole = Math.Truncate(number);
            sb.Append(number == whole ? checked((long)whole).ToString(CultureInfo.InvariantCulture) : number.ToString("G17", CultureInfo.InvariantCulture));
        }

        private static void JsonNullable(StringBuilder sb, string value)
        {
            if (value == null) sb.Append("null"); else JsonString(sb, value);
        }

        private static void JsonDuration(StringBuilder sb, double? value)
        {
            if (value == null) sb.Append("null"); else JsonNumber(sb, value.Value);
        }

        private static void Field(StringBuilder sb, string name, bool first = false)
        {
            if (!first) sb.Append(',');
            sb.Append('"').Append(name).Append("\":");
        }

        private static void JsonStrings(StringBuilder sb, IEnumerable<string> values)
        {
            sb.Append('[');
            var first = true;
            foreach (var value in values ?? Array.Empty<string>())
            {
                if (!first) sb.Append(',');
                first = false;
                JsonString(sb, value);
            }
            sb.Append(']');
        }

        /// <summary>The ledger's one canonical, ASCII-only JSON text (v2 chunks always name both host fields).</summary>
        public static string ToJson(RcLedgerDocument document)
        {
            var sb = new StringBuilder();
            var format = document.Format == FormatV2 ? FormatV2 : Format;
            var contract = document.Contract ?? new RcLedgerContract();
            sb.Append('{');
            Field(sb, "format", true); JsonString(sb, format);
            Field(sb, "platform"); JsonString(sb, document.Platform);
            Field(sb, "repository"); JsonString(sb, document.Repository);
            Field(sb, "candidateId"); JsonString(sb, document.CandidateId);
            Field(sb, "candidateSha"); JsonString(sb, document.CandidateSha);
            Field(sb, "baselineSha"); JsonNullable(sb, document.BaselineSha);
            Field(sb, "baselineRef"); JsonNullable(sb, document.BaselineRef);
            Field(sb, "runId"); JsonString(sb, document.RunId);
            Field(sb, "attempt"); JsonNumber(sb, document.Attempt);
            Field(sb, "contract"); sb.Append('{');
            Field(sb, "policyHash", true); JsonString(sb, contract.PolicyHash);
            Field(sb, "catalogHash"); JsonString(sb, contract.CatalogHash);
            Field(sb, "selectorHash"); JsonString(sb, contract.SelectorHash);
            Field(sb, "runtimeHash"); JsonString(sb, contract.RuntimeHash);
            sb.Append('}');
            Field(sb, "forcedFull"); JsonStrings(sb, document.ForcedFull);
            Field(sb, "identities"); sb.Append('[');
            var firstIdentity = true;
            foreach (var identity in document.Identities ?? Array.Empty<RcLedgerIdentity>())
            {
                if (!firstIdentity) sb.Append(',');
                firstIdentity = false;
                sb.Append('{');
                Field(sb, "suite", true); JsonString(sb, identity.Suite);
                Field(sb, "className"); JsonString(sb, identity.ClassName);
                Field(sb, "method"); JsonString(sb, identity.Method);
                Field(sb, "boundary"); JsonString(sb, identity.Boundary);
                Field(sb, "inputDigest"); JsonString(sb, identity.InputDigest);
                Field(sb, "outcome"); JsonString(sb, identity.Outcome);
                Field(sb, "durationSeconds"); JsonDuration(sb, identity.DurationSeconds);
                Field(sb, "carriedFrom");
                var carried = identity.CarriedFrom;
                if (carried == null) sb.Append("null");
                else
                {
                    sb.Append('{');
                    Field(sb, "candidateId", true); JsonString(sb, carried.CandidateId);
                    Field(sb, "runId"); JsonString(sb, carried.RunId);
                    Field(sb, "candidateSha"); JsonString(sb, carried.CandidateSha);
                    Field(sb, "chunkId"); JsonString(sb, carried.ChunkId);
                    Field(sb, "inputDigest"); JsonString(sb, carried.InputDigest);
                    sb.Append('}');
                }
                sb.Append('}');
            }
            sb.Append(']');
            Field(sb, "chunks"); sb.Append('[');
            var firstChunk = true;
            foreach (var chunk in document.Chunks ?? Array.Empty<RcLedgerChunk>())
            {
                if (!firstChunk) sb.Append(',');
                firstChunk = false;
                sb.Append('{');
                Field(sb, "chunkId", true); JsonString(sb, chunk.ChunkId);
                Field(sb, "boundary"); JsonString(sb, chunk.Boundary);
                Field(sb, "suite"); JsonString(sb, chunk.Suite);
                Field(sb, "identityKeys"); JsonStrings(sb, chunk.IdentityKeys);
                Field(sb, "inputDigest"); JsonString(sb, chunk.InputDigest);
                Field(sb, "outcome"); JsonString(sb, chunk.Outcome);
                Field(sb, "durationSeconds"); JsonDuration(sb, chunk.DurationSeconds);
                if (format == FormatV2)
                {
                    Field(sb, "hostId"); JsonNullable(sb, chunk.HostId);
                    Field(sb, "os"); JsonNullable(sb, chunk.Os);
                }
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>The validated ledger's JSON and a newline to a new file (UTF-8, no BOM); refuses an existing path.</summary>
        public static void Write(string path, RcLedgerDocument document)
        {
            if (File.Exists(path)) throw new RcLedgerRefusal("ledger-exists");
            Refuse(CheckDocument(document));
            var json = ToJson(document) + "\n";
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        // ---- durations and host (CARD-1842 S6) ------------------------------------------------------------------------

        private static Dictionary<string, double> TrxDurations(IEnumerable<string> trxPaths)
        {
            var totals = new Dictionary<string, double>(StringComparer.Ordinal);
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in trxPaths ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var xml = new XmlDocument { XmlResolver = null };
                xml.Load(path);
                var ns = new XmlNamespaceManager(xml.NameTable);
                ns.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010");
                var methods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (XmlElement test in xml.SelectNodes("//t:UnitTest", ns))
                    if (test.SelectSingleNode("t:TestMethod", ns) is XmlElement method)
                        methods[test.GetAttribute("id")] = method.GetAttribute("className") + "/" + method.GetAttribute("name");
                foreach (XmlElement result in xml.SelectNodes("//t:UnitTestResult", ns))
                {
                    methods.TryGetValue(result.GetAttribute("testId"), out var key);
                    var text = result.GetAttribute("duration");
                    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(text)) continue;
                    if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var span)) throw new RcLedgerRefusal("duration-invalid: " + key);
                    if (owners.TryGetValue(key, out var owner) && owner != path) throw new RcLedgerRefusal("duration-ambiguous: " + key);
                    owners[key] = path;
                    totals.TryGetValue(key, out var total);
                    totals[key] = total + span.TotalSeconds;
                }
            }
            return totals;
        }

        /// <summary>
        /// Chunk durationSeconds from the timing sidecar runWallSeconds whose suite and chunk equal the chunk's suite and
        /// chunkId; identity durationSeconds the sum of the TRX UnitTestResult durations whose TestMethod className and
        /// name equal the identity's. A chunk or identity without a match keeps its value; format and host are untouched.
        /// </summary>
        public static RcLedgerDocument SetDurations(RcLedgerDocument document, IEnumerable<string> timingPaths, IEnumerable<string> trxPaths)
        {
            var walls = new Dictionary<string, double?>(StringComparer.Ordinal);
            foreach (var path in timingPaths ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                JsonDocument timing;
                try { timing = JsonDocument.Parse(File.ReadAllText(path)); }
                catch (JsonException) { throw new RcLedgerRefusal("duration-timing-invalid: " + Path.GetFileName(path)); }
                using (timing)
                {
                    var root = timing.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) throw new RcLedgerRefusal("duration-timing-invalid: " + Path.GetFileName(path));
                    var key = Text(root, "suite") + "\n" + Text(root, "chunk");
                    if (walls.ContainsKey(key)) throw new RcLedgerRefusal("duration-duplicate: " + Path.GetFileName(path));
                    walls[key] = root.TryGetProperty("runWallSeconds", out var wall) && wall.ValueKind == JsonValueKind.Number
                        && wall.TryGetDouble(out var seconds) && seconds >= 0 ? seconds : (double?)null;
                }
            }
            foreach (var chunk in document.Chunks ?? Array.Empty<RcLedgerChunk>())
                if (walls.TryGetValue((chunk.Suite ?? "") + "\n" + (chunk.ChunkId ?? ""), out var wall) && wall.HasValue) chunk.DurationSeconds = wall.Value;
            var totals = TrxDurations(trxPaths);
            foreach (var identity in document.Identities ?? Array.Empty<RcLedgerIdentity>())
                if (totals.TryGetValue(identity.Key, out var total)) identity.DurationSeconds = total;
            return document;
        }

        private static string Text(JsonElement o, string name) =>
            o.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Null ? "" : value.GetRawText() : "";

        /// <summary>Every chunk's hostId and os from the caller's arguments (v2 only; never the machine name).</summary>
        public static RcLedgerDocument SetHost(RcLedgerDocument document, string hostId, string os)
        {
            if (document.Format != FormatV2) throw new RcLedgerRefusal("ledger-schema");
            if (!RequiredText(hostId)) throw new RcLedgerRefusal("ledger-host: hostId");
            if (os != "windows" && os != "linux") throw new RcLedgerRefusal("ledger-host: os");
            foreach (var chunk in document.Chunks ?? Array.Empty<RcLedgerChunk>()) { chunk.HostId = hostId; chunk.Os = os; }
            return document;
        }

        /// <summary>
        /// rc-ledger-stamp: copy a ledger to a new path with durations from timing sidecars and TRX. Only includeHost
        /// writes antiphon-rc-ledger-v2 and a chunk host; without it the output is v1 with no host. The host is the
        /// caller's argument, never the machine name. Refuses an existing output path. No nightly caller (CARD-1836).
        /// </summary>
        public static void Stamp(string ledgerPath, string outputPath, IEnumerable<string> timingPaths, IEnumerable<string> trxPaths, bool includeHost, string hostId, string os)
        {
            if (File.Exists(outputPath)) throw new RcLedgerRefusal("ledger-exists");
            if (!includeHost && (!string.IsNullOrEmpty(hostId) || !string.IsNullOrEmpty(os))) throw new RcLedgerRefusal("ledger-host: --host-id and --os need --include-host");
            if (includeHost)
            {
                if (!RequiredText(hostId)) throw new RcLedgerRefusal("ledger-host: hostId");
                if (os != "windows" && os != "linux") throw new RcLedgerRefusal("ledger-host: os");
            }
            var document = SetDurations(Read(ledgerPath), timingPaths, trxPaths);
            if (includeHost)
            {
                document.Format = FormatV2;
                SetHost(document, hostId, os);
            }
            Write(outputPath, document);
        }

        // ---- the reader ---------------------------------------------------------------------------------------------

        /// <summary>
        /// The ledger at path (a UTF-8 BOM allowed), after the closed-schema scan and the document check. ledger-missing,
        /// or ledger-invalid with the first fault (or every document fault).
        /// </summary>
        public static RcLedgerDocument Read(string path)
        {
            if (!File.Exists(path)) throw new RcLedgerRefusal("ledger-missing");
            var bytes = File.ReadAllBytes(path);
            var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset); }
            catch (ArgumentException) { throw Invalid("json"); }
            var scan = CheckJsonText(text);
            RcLedgerDocument document;
            try
            {
                using var json = JsonDocument.Parse(scan);
                document = FromJson(json.RootElement);
            }
            catch (JsonException) { throw Invalid("json"); }
            Refuse(CheckDocument(document));
            return document;
        }

        // The scan has proved the closed schema, the value types and unique names.
        private static RcLedgerDocument FromJson(JsonElement root)
        {
            string S(JsonElement o, string name) => o.GetProperty(name).ValueKind == JsonValueKind.Null ? null : o.GetProperty(name).GetString();
            double? N(JsonElement o, string name) => o.GetProperty(name).ValueKind == JsonValueKind.Null ? null : o.GetProperty(name).GetDouble();
            string[] Strings(JsonElement a) => a.EnumerateArray().Select(e => e.GetString()).ToArray();
            var contract = root.GetProperty("contract");
            return new RcLedgerDocument
            {
                Format = S(root, "format"), Platform = S(root, "platform"), Repository = S(root, "repository"), CandidateId = S(root, "candidateId"),
                CandidateSha = S(root, "candidateSha"), BaselineSha = S(root, "baselineSha"), BaselineRef = S(root, "baselineRef"), RunId = S(root, "runId"),
                Attempt = root.GetProperty("attempt").GetDouble(),
                Contract = new RcLedgerContract
                {
                    PolicyHash = S(contract, "policyHash"), CatalogHash = S(contract, "catalogHash"),
                    SelectorHash = S(contract, "selectorHash"), RuntimeHash = S(contract, "runtimeHash"),
                },
                ForcedFull = Strings(root.GetProperty("forcedFull")),
                Identities = root.GetProperty("identities").EnumerateArray().Select(i =>
                {
                    var carried = i.GetProperty("carriedFrom");
                    return new RcLedgerIdentity
                    {
                        Suite = S(i, "suite"), ClassName = S(i, "className"), Method = S(i, "method"), Boundary = S(i, "boundary"),
                        InputDigest = S(i, "inputDigest"), Outcome = S(i, "outcome"), DurationSeconds = N(i, "durationSeconds"),
                        CarriedFrom = carried.ValueKind == JsonValueKind.Null ? null : new RcCarriedFrom
                        {
                            CandidateId = S(carried, "candidateId"), RunId = S(carried, "runId"), CandidateSha = S(carried, "candidateSha"),
                            ChunkId = S(carried, "chunkId"), InputDigest = S(carried, "inputDigest"),
                        },
                    };
                }).ToArray(),
                Chunks = root.GetProperty("chunks").EnumerateArray().Select(c =>
                {
                    var chunk = new RcLedgerChunk
                    {
                        ChunkId = S(c, "chunkId"), Boundary = S(c, "boundary"), Suite = S(c, "suite"), IdentityKeys = Strings(c.GetProperty("identityKeys")),
                        InputDigest = S(c, "inputDigest"), Outcome = S(c, "outcome"), DurationSeconds = N(c, "durationSeconds"),
                    };
                    if (c.TryGetProperty("hostId", out _)) chunk.HostId = S(c, "hostId");
                    if (c.TryGetProperty("os", out _)) chunk.Os = S(c, "os");
                    return chunk;
                }).ToArray(),
            };
        }

        // ---- the closed-schema scan ---------------------------------------------------------------------------------

        private static readonly Regex Token = new Regex(
            "\"(?:\\\\.|[^\"\\\\])*\"|-?(?:0|[1-9][0-9]*)(?:\\.[0-9]+)?(?:[eE][+\\-]?[0-9]+)?|true|false|null|[{}\\[\\]:,]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly (string Name, string Type)[] DocumentSchema =
        {
            ("format", "s"), ("platform", "s"), ("repository", "s"), ("candidateId", "s"), ("candidateSha", "s"), ("baselineSha", "sn"),
            ("baselineRef", "sn"), ("runId", "s"), ("attempt", "n"), ("contract", "o:contract"), ("forcedFull", "as"), ("identities", "ai"), ("chunks", "ac"),
        };
        private static readonly (string Name, string Type)[] ContractSchema = { ("policyHash", "s"), ("catalogHash", "s"), ("selectorHash", "s"), ("runtimeHash", "s") };
        private static readonly (string Name, string Type)[] IdentitySchema =
        {
            ("suite", "s"), ("className", "s"), ("method", "s"), ("boundary", "s"), ("inputDigest", "s"), ("outcome", "s"), ("durationSeconds", "nn"), ("carriedFrom", "on:carried"),
        };
        // hostId and os are optional (?) here; CheckDocument refuses them on v1.
        private static readonly (string Name, string Type)[] ChunkSchema =
        {
            ("chunkId", "s"), ("boundary", "s"), ("suite", "s"), ("identityKeys", "as"), ("inputDigest", "s"), ("outcome", "s"), ("durationSeconds", "nn"),
            ("hostId", "sn?"), ("os", "sn?"),
        };
        private static readonly (string Name, string Type)[] CarriedSchema = { ("candidateId", "s"), ("runId", "s"), ("candidateSha", "s"), ("chunkId", "s"), ("inputDigest", "s") };

        private sealed class Frame
        {
            public char K; // r(oot), o(bject), a(rray)
            public int Step;
            public bool AllowClose = true;
            public (string Name, string Type)[] Schema;
            public string Expect = "";
            public string Element = "";
            public HashSet<string> Fold;
            public HashSet<string> Exact;
        }

        private static Exception Invalid(string what) => new RcLedgerRefusal("ledger-invalid: " + what);

        private static string ScanString(string raw)
        {
            var body = raw.Substring(1, raw.Length - 2);
            if (body.IndexOf('\\') < 0) return body;
            var sb = new StringBuilder();
            var i = 0;
            while (i < body.Length)
            {
                var ch = body[i++];
                if (ch != '\\') { sb.Append(ch); continue; }
                if (i >= body.Length) throw Invalid("json");
                switch (body[i++])
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > body.Length || !int.TryParse(body.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code)) throw Invalid("json");
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: throw Invalid("json");
                }
            }
            return sb.ToString();
        }

        private static Frame ObjectFrame((string Name, string Type)[] schema) => new Frame
        {
            K = 'o', Schema = schema, Fold = new HashSet<string>(StringComparer.OrdinalIgnoreCase), Exact = new HashSet<string>(StringComparer.Ordinal),
        };

        private static void Complete(List<Frame> frames)
        {
            var parent = frames[frames.Count - 1];
            parent.Step = parent.K == 'o' ? 3 : 1;
            if (parent.K != 'r') parent.AllowClose = true;
        }

        private static bool Open(List<Frame> frames, string type, string raw)
        {
            if (frames.Count > 12) throw Invalid("json");
            if (raw == "{")
            {
                var schema = type switch
                {
                    "o:document" => DocumentSchema,
                    "o:contract" => ContractSchema,
                    "o:identity" => IdentitySchema,
                    "o:chunk" => ChunkSchema,
                    "o:carried" or "on:carried" => CarriedSchema,
                    _ => throw Invalid("type"),
                };
                frames.Add(ObjectFrame(schema));
                return true;
            }
            if (raw == "[")
            {
                var element = type switch { "as" => "s", "ai" => "o:identity", "ac" => "o:chunk", _ => throw Invalid("type") };
                frames.Add(new Frame { K = 'a', Element = element });
                return true;
            }
            return false;
        }

        private static void Scalar(string type, string raw)
        {
            var isString = raw.Length >= 2 && raw[0] == '"';
            var isNull = raw == "null";
            var isBool = raw == "true" || raw == "false";
            switch (type)
            {
                case "s":
                    if (!isString) throw Invalid("type");
                    ScanString(raw);
                    return;
                case "sn":
                case "sn?":
                    if (isNull) return;
                    if (!isString) throw Invalid("type");
                    ScanString(raw);
                    return;
                case "n":
                case "nn":
                    if (type == "nn" && isNull) return;
                    if (isString || isNull || isBool) throw Invalid("type");
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsNaN(number) || double.IsInfinity(number))
                        throw Invalid("json");
                    return;
            }
            if (type == "on:carried" && isNull) return;
            throw Invalid("type");
        }

        private static void Close(List<Frame> frames, Frame frame)
        {
            foreach (var (name, type) in frame.Schema)
                if (!type.EndsWith("?", StringComparison.Ordinal) && !frame.Exact.Contains(name)) throw Invalid("missing:" + name);
            frames.RemoveAt(frames.Count - 1);
            Complete(frames);
        }

        private static bool Blank(string text, int start, int length)
        {
            for (var i = start; i < start + length; i++) if (!char.IsWhiteSpace(text[i])) return false;
            return true;
        }

        // The closed-schema token scan: every name known (case-insensitively unique), every value of its type, the
        // nesting as the schema says. Returns the text without a leading U+FEFF.
        private static string CheckJsonText(string text)
        {
            if (string.IsNullOrEmpty(text)) throw Invalid("json");
            var scan = text[0] == '﻿' ? text.Substring(1) : text;
            var frames = new List<Frame> { new Frame { K = 'r', AllowClose = false, Expect = "o:document" } };
            var cursor = 0;
            for (var match = Token.Match(scan); match.Success; match = match.NextMatch())
            {
                if (match.Index > cursor && !Blank(scan, cursor, match.Index - cursor)) throw Invalid("json");
                cursor = match.Index + match.Length;
                var raw = match.Value;
                var frame = frames[frames.Count - 1];
                if (frame.K == 'r')
                {
                    if (frame.Step != 0) throw Invalid("json");
                    if (Open(frames, frame.Expect, raw)) continue;
                    Scalar(frame.Expect, raw);
                    frame.Step = 1;
                    continue;
                }
                if (frame.K == 'o')
                {
                    switch (frame.Step)
                    {
                        case 0:
                            if (raw == "}")
                            {
                                if (!frame.AllowClose) throw Invalid("json");
                                Close(frames, frame);
                                continue;
                            }
                            if (raw.Length < 2 || raw[0] != '"') throw Invalid("json");
                            var name = ScanString(raw);
                            if (!frame.Fold.Add(name)) throw Invalid("duplicate-property:" + name);
                            var found = Array.FindIndex(frame.Schema, f => f.Name == name);
                            if (found < 0) throw Invalid("unknown-property:" + name);
                            frame.Exact.Add(name);
                            frame.Expect = frame.Schema[found].Type;
                            frame.Step = 1;
                            continue;
                        case 1:
                            if (raw != ":") throw Invalid("json");
                            frame.Step = 2;
                            continue;
                        case 2:
                            if (Open(frames, frame.Expect, raw)) continue;
                            Scalar(frame.Expect, raw);
                            frame.Step = 3;
                            frame.AllowClose = true;
                            continue;
                    }
                    if (raw == "}") { Close(frames, frame); continue; }
                    if (raw != ",") throw Invalid("json");
                    frame.Step = 0;
                    frame.AllowClose = false;
                    continue;
                }
                if (frame.Step == 0)
                {
                    if (raw == "]")
                    {
                        if (!frame.AllowClose) throw Invalid("json");
                        frames.RemoveAt(frames.Count - 1);
                        Complete(frames);
                        continue;
                    }
                    if (Open(frames, frame.Element, raw)) continue;
                    Scalar(frame.Element, raw);
                    frame.Step = 1;
                    frame.AllowClose = true;
                    continue;
                }
                if (raw == "]")
                {
                    frames.RemoveAt(frames.Count - 1);
                    Complete(frames);
                    continue;
                }
                if (raw != ",") throw Invalid("json");
                frame.Step = 0;
                frame.AllowClose = false;
            }
            if (cursor < scan.Length && !Blank(scan, cursor, scan.Length - cursor)) throw Invalid("json");
            if (frames.Count != 1 || frames[0].Step != 1) throw Invalid("json");
            return scan;
        }
    }
}
