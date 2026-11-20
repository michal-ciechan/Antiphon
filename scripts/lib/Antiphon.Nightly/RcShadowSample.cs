// CARD-1836 S8b compiled shadow sample. Shadow only: effectiveMode stays Full and gateCredit stays false.
// Behaviour matches scripts/lib/rc-shadow-sample.ps1 at the port base. No trust record is written.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    public sealed class RcShadowFailure
    {
        public string Kind { get; set; }
        public string ChunkId { get; set; }
    }

    public sealed class RcShadowProposal
    {
        public string Format { get; set; }
        public string EffectiveMode { get; set; }
        public bool GateCredit { get; set; }
        public string BindingState { get; set; }
        public string IncrementalValidation { get; set; }
        public bool TrustGranted { get; set; }
        public bool TrustRecordWritten { get; set; }
        public bool PilotClaim { get; set; }
        public bool Launched { get; set; }
        public bool ExpandedToFull { get; set; }
        public bool CarriesInvalidated { get; set; }
        public string Sample { get; set; }
        public bool ShadowPilot { get; set; }
        public string Seed { get; set; }
        public int N { get; set; }
        public int Cchunk { get; set; }
        public int StratumCount { get; set; }
        public string[] Selected { get; set; }
        public string[] ExpectedSkipOnly { get; set; }
    }

    public sealed class RcShadowTrust
    {
        public bool TrustGranted { get; set; }
        public string IncrementalValidation { get; set; }
        public string EffectiveMode { get; set; }
        public bool GateCredit { get; set; }
        public string Reason { get; set; }
        public string[] Reasons { get; set; }
        public int ObservedPilots { get; set; }
        public int RequiredPilots { get; set; }
        public bool CarriesInvalidated { get; set; }
        public string[] InvalidatedCarries { get; set; }
        public object PreservedSampleFailure { get; set; }
        public bool ExpandedToFull { get; set; }
        public bool TrustRecordWritten { get; set; }
        public bool ShadowPilot { get; set; }
    }

    public static class RcShadowSample
    {
        static readonly Regex SeedPattern = new Regex(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant);
        static readonly Regex DigestPattern = new Regex(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant);
        static readonly Regex MapVersionPattern = new Regex(@"\A[1-9][0-9]*\z", RegexOptions.CultureInvariant);
        static readonly Regex VersionPattern = new Regex(@"\A[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?\z", RegexOptions.CultureInvariant);
        static readonly string[] ContractFields =
        {
            "format", "inputMapVersion", "sdkVersion", "runtimeVersion", "platform", "executionImplementation",
            "policyHash", "catalogHash", "selectorHash", "skipResolverHash", "discoveryHash", "parserHash",
            "coverageHash", "launcherHash", "custodyHash", "buildHash", "harnessHash", "inputMapHash", "sdkHash", "runtimeHash"
        };
        static readonly HashSet<string> ContractFieldSet = new HashSet<string>(ContractFields, StringComparer.Ordinal);
        static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static object Field(object source, string name) => RcPort.Field(source, name);

        public static bool PilotEligible(object pilot, string contractDigest)
        {
            if (pilot == null) return false;
            var cut = RcPort.Field(pilot, "cutId");
            var platform = RcPort.Field(pilot, "platform");
            var sample = RcPort.Field(pilot, "sample");
            var shadow = RcPort.Field(pilot, "shadowPilot");
            var discrepancy = RcPort.Field(pilot, "discrepancy");
            var open = RcPort.Field(pilot, "openFailure");
            var receipts = RcPort.Field(pilot, "nativeReceipts");
            var overdue = RcPort.Field(pilot, "fullOverdue");
            var digest = RcPort.Field(pilot, "contractDigest");
            var executed = RcPort.Field(pilot, "nExecuted");
            if (cut is not string cutId || cutId.Length == 0) return false;
            if (!OrdinalEquals(platform, "windows")) return false;
            if (!OrdinalEquals(sample, "executed")) return false;
            if (shadow is not bool shadowBit || !shadowBit) return false;
            if (discrepancy is not bool discrepancyBit || discrepancyBit) return false;
            if (open is not bool openBit || openBit) return false;
            if (!OrdinalEquals(receipts, "current")) return false;
            if (overdue is not bool overdueBit || overdueBit) return false;
            if (digest is not string digestText || !DigestPattern.IsMatch(digestText)) return false;
            if (contractDigest == null || !DigestPattern.IsMatch(contractDigest)) return false;
            if (!string.Equals(digestText, contractDigest, StringComparison.Ordinal)) return false;
            var planned = RcPort.Field(pilot, "n");
            var population = RcPort.Field(pilot, "cchunk");
            var strata = RcPort.Field(pilot, "stratumCount");
            if (!Whole(planned) || !Whole(executed)) return false;
            var plannedN = ToInt(planned);
            var executedN = ToInt(executed);
            var minimum = 30;
            if (population != null || strata != null)
            {
                if (!Whole(population) || !Whole(strata)) return false;
                minimum = SampleCount(ToInt(population), ToInt(strata));
            }
            if (minimum < 1 || plannedN < minimum || executedN < plannedN || executedN < minimum) return false;
            return true;
        }

        public static RcShadowProposal Proposal(object chunks, string seed, string proposalPath, string checkoutRoot)
        {
            var parsed = new List<Chunk>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in RcPort.Unwrap(chunks))
            {
                if (raw == null) continue;
                var chunk = ParseChunk(raw);
                if (!seen.Add(chunk.Id)) RcPort.Throw("shadow-chunk-duplicate");
                parsed.Add(chunk);
            }
            var population = new List<Chunk>();
            var skips = new List<Chunk>();
            foreach (var chunk in parsed)
            {
                if (chunk.ExpectedSkipOnly) skips.Add(chunk);
                else if (chunk.CompletelyCarryable) population.Add(chunk);
            }
            var resolvedSeed = ResolveSeed(seed);
            var strata = new SortedDictionary<string, List<Chunk>>(StringComparer.Ordinal);
            foreach (var chunk in population)
            {
                var key = chunk.Suite + "\n" + chunk.Boundary;
                if (!strata.TryGetValue(key, out var list))
                {
                    list = new List<Chunk>();
                    strata[key] = list;
                }
                list.Add(chunk);
            }
            var cchunk = population.Count;
            var stratumCount = strata.Count;
            var sampleCount = SampleCount(cchunk, stratumCount);
            var selected = new List<Chunk>();
            if (cchunk > 0)
            {
                var picked = new HashSet<string>(StringComparer.Ordinal);
                foreach (var pair in strata)
                {
                    var best = SortRank(pair.Value, resolvedSeed, "stratum")[0];
                    selected.Add(best);
                    picked.Add(best.Id);
                }
                if (selected.Count < sampleCount)
                {
                    var rest = new List<Chunk>();
                    foreach (var chunk in population)
                    {
                        if (!picked.Contains(chunk.Id)) rest.Add(chunk);
                    }
                    foreach (var chunk in SortRank(rest, resolvedSeed, "fill"))
                    {
                        if (selected.Count >= sampleCount) break;
                        selected.Add(chunk);
                    }
                }
            }
            var applicable = cchunk > 0;
            var proposal = new RcShadowProposal
            {
                Format = "antiphon-rc-shadow-proposal-v1",
                EffectiveMode = "Full",
                GateCredit = false,
                BindingState = "proposal-only",
                IncrementalValidation = "Shadow",
                TrustGranted = false,
                TrustRecordWritten = false,
                PilotClaim = false,
                Launched = false,
                ExpandedToFull = false,
                CarriesInvalidated = false,
                Sample = applicable ? "roster" : "not-applicable",
                ShadowPilot = applicable,
                Seed = resolvedSeed,
                N = sampleCount,
                Cchunk = cchunk,
                StratumCount = stratumCount,
                Selected = Ids(SortIdentity(selected)),
                ExpectedSkipOnly = Ids(SortIdentity(skips))
            };
            if (!string.IsNullOrWhiteSpace(proposalPath)) WriteProposal(proposalPath, checkoutRoot, proposal);
            return proposal;
        }

        public static RcShadowTrust Trust(
            object currentContract,
            object baselineContract,
            object evidence,
            object fullAnchor,
            DateTimeOffset utcNow,
            object pilots,
            object carries,
            object sampleFailure,
            bool rosterChanged,
            object operatorTrustRecord,
            string currentSample,
            string contractDigest,
            string trustRecordPath)
        {
            _ = fullAnchor;
            _ = utcNow;
            _ = trustRecordPath;
            var reasons = new List<string>();
            if (currentContract != null || baselineContract != null)
                reasons.AddRange(ContractReasons(currentContract, baselineContract));
            var derived = EvidenceFailure(evidence, carries);
            if (sampleFailure != null || derived != null) reasons.Add("sample-failed");
            if (rosterChanged) reasons.Add("sample-roster-changed");
            var preserved = sampleFailure ?? derived;
            reasons.Sort(StringComparer.Ordinal);
            var invalidate = reasons.Count > 0;
            var invalidated = Array.Empty<string>();
            if (invalidate) invalidated = CarryIds(carries);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var observed = 0;
            foreach (var pilot in RcPort.Unwrap(pilots))
            {
                if (!PilotEligible(pilot, contractDigest)) continue;
                var cut = (string)RcPort.Field(pilot, "cutId");
                if (seen.Add(cut)) observed++;
            }
            string reason;
            if (invalidate)
            {
                reason = string.Join("|", reasons);
            }
            else if (observed == 0 && (string.Equals(currentSample, "not-applicable", StringComparison.Ordinal) || string.IsNullOrEmpty(currentSample)))
            {
                reason = "not-a-shadow-pilot";
                reasons.Add(reason);
            }
            else if (observed < 2)
            {
                reason = "pilot-count";
                reasons.Add(reason);
            }
            else if (operatorTrustRecord == null)
            {
                reason = "operator-trust-required";
                reasons.Add(reason);
            }
            else
            {
                reason = "trust-transition-disabled";
                reasons.Add(reason);
            }
            return new RcShadowTrust
            {
                TrustGranted = false,
                IncrementalValidation = "Shadow",
                EffectiveMode = "Full",
                GateCredit = false,
                Reason = reason,
                Reasons = reasons.ToArray(),
                ObservedPilots = observed,
                RequiredPilots = 2,
                CarriesInvalidated = invalidate,
                InvalidatedCarries = invalidated,
                PreservedSampleFailure = preserved,
                ExpandedToFull = false,
                TrustRecordWritten = false,
                ShadowPilot = false
            };
        }

        static IEnumerable<string> ContractReasons(object current, object baseline)
        {
            var reasons = new List<string>();
            foreach (var field in ContractFields)
            {
                var currentValue = ContractField(current, field);
                var baselineValue = ContractField(baseline, field);
                var currentKnown = ContractValue(field, currentValue);
                var baselineKnown = ContractValue(field, baselineValue);
                if (!currentKnown) reasons.Add("contract-current-unknown:" + field);
                if (!baselineKnown) reasons.Add("contract-baseline-unknown:" + field);
                if (currentKnown && baselineKnown && !string.Equals((string)currentValue, (string)baselineValue, StringComparison.Ordinal))
                    reasons.Add("contract-changed:" + field);
            }
            Extra(reasons, "current", current);
            Extra(reasons, "baseline", baseline);
            return reasons;
        }

        static void Extra(List<string> reasons, string side, object contract)
        {
            foreach (var field in ContractNames(contract))
            {
                if (!ContractFieldSet.Contains(field)) reasons.Add("contract-" + side + "-unknown:" + field);
            }
        }

        // Get-RcFullField reads $Object.Keys, which a key named Keys shadows.
        // A shadowed string enumerates as characters, so no contract field matches.
        // The real indexer is used only when that enumeration yields the field name.
        static object ContractField(object source, string name)
        {
            source = RcPort.See(source);
            if (source is IDictionary dictionary && TryShadowedKeys(dictionary, out var shadowed))
            {
                foreach (var key in EnumerateShadow(shadowed, true))
                {
                    if (key is string text && string.Equals(text, name, StringComparison.Ordinal))
                        return RcPort.See(dictionary[text]);
                }
                return null;
            }
            return RcPort.Field(source, name);
        }

        // Get-RcFullFieldNames is @($Object.Keys). @() does not unroll a string.
        static List<string> ContractNames(object source)
        {
            source = RcPort.See(source);
            if (source is IDictionary dictionary && TryShadowedKeys(dictionary, out var shadowed))
            {
                var names = new List<string>();
                foreach (var key in EnumerateShadow(shadowed, false))
                {
                    if (key is string text) names.Add(text);
                }
                return names;
            }
            return RcPort.Names(source);
        }

        static bool TryShadowedKeys(IDictionary dictionary, out object shadowed)
        {
            shadowed = null;
            if (!dictionary.Contains("Keys")) return false;
            shadowed = dictionary["Keys"];
            return true;
        }

        static IEnumerable EnumerateShadow(object shadowed, bool unrollString)
        {
            if (shadowed is string text)
            {
                if (!unrollString)
                {
                    yield return text;
                    yield break;
                }
                foreach (var character in text) yield return character.ToString();
                yield break;
            }
            if (shadowed is IEnumerable list)
            {
                foreach (var item in list) yield return item;
                yield break;
            }
            yield return shadowed;
        }

        static bool ContractValue(string field, object value)
        {
            if (value is not string text) return false;
            if (field.EndsWith("Hash", StringComparison.Ordinal)) return DigestPattern.IsMatch(text);
            switch (field)
            {
                case "format": return text == "antiphon-rc-input-contract-v1";
                case "inputMapVersion": return MapVersionPattern.IsMatch(text);
                case "sdkVersion":
                case "runtimeVersion": return VersionPattern.IsMatch(text);
                case "platform": return text == "linux" || text == "windows";
                case "executionImplementation": return text == "compiled" || text == "fallback";
                default: return false;
            }
        }

        static object EvidenceFailure(object evidence, object carries)
        {
            var carried = new HashSet<string>(StringComparer.Ordinal);
            foreach (var carry in RcPort.Unwrap(carries))
            {
                if (carry == null) continue;
                if (carry is string text)
                {
                    if (text.Length > 0) carried.Add(text);
                    continue;
                }
                throw new InvalidOperationException("shadow-carry-id");
            }
            var sawSampled = RcPort.HasKey(evidence, "sampled");
            var sawResults = RcPort.HasKey(evidence, "results");
            if (!sawSampled && !sawResults) return null;
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (sawSampled)
            {
                var sampled = RcPort.Field(evidence, "sampled");
                if (RcPort.Empty(sampled)) return Fail("empty-results", "");
                foreach (var id in RcPort.Unwrap(sampled))
                {
                    if (id is not string text || text.Length == 0) return Fail("missing-outcome", "");
                    if (!seen.Add(text)) return Fail("provenance-failure", text);
                    ids.Add(text);
                }
                if (ids.Count == 0) return Fail("empty-results", "");
            }
            var rows = new List<object>();
            if (sawResults)
            {
                var results = RcPort.Field(evidence, "results");
                if (RcPort.Empty(results)) return Fail("empty-results", ids.Count > 0 ? ids[0] : "");
                foreach (var row in RcPort.Unwrap(results))
                {
                    if (row == null) return Fail("missing-outcome", "");
                    var rowId = RcPort.Field(row, "chunkId");
                    if (!Readable(rowId)) return Fail("unreadable-chunk-id", "");
                    rows.Add(row);
                }
                if (rows.Count == 0) return Fail("empty-results", ids.Count > 0 ? ids[0] : "");
            }
            var launched = new HashSet<string>(ids, StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var rowId = RcPort.Field(row, "chunkId");
                if (!Readable(rowId)) return Fail("unreadable-chunk-id", "");
                var label = (string)rowId;
                if (!carried.Contains(label)) continue;
                if (sawSampled && launched.Contains(label)) continue;
                return Fail("carried-result", label);
            }
            if (ids.Count > 0 && rows.Count == 0) return Fail("empty-results", ids[0]);
            if (ids.Count == 0)
            {
                if (rows.Count == 0) return Fail("empty-results", "");
                foreach (var row in rows)
                {
                    var rowId = RcPort.Field(row, "chunkId");
                    var failure = ResultRow(row, rowId is string text ? text : "");
                    if (failure != null) return failure;
                }
                return null;
            }
            foreach (var id in ids)
            {
                var matches = new List<object>();
                foreach (var row in rows)
                {
                    var rowId = RcPort.Field(row, "chunkId");
                    if (rowId is string text && string.Equals(text, id, StringComparison.Ordinal)) matches.Add(row);
                }
                if (matches.Count == 0) return Fail("missing-outcome", id);
                if (matches.Count > 1) return Fail("provenance-failure", id);
                var failure = ResultRow(matches[0], id);
                if (failure != null) return failure;
            }
            return null;
        }

        static RcShadowFailure ResultRow(object row, string chunkId)
        {
            var outcome = RcPort.Field(row, "outcome");
            if (Closed(row, "unexpectedSkip") || (outcome is string skipped && string.Equals(skipped, "skipped", StringComparison.Ordinal)))
                return Fail("unexpected-skip", chunkId);
            if (outcome is not string outcomeText || outcomeText.Length == 0) return Fail("missing-outcome", chunkId);
            var provenance = RcPort.Field(row, "provenance");
            if (Closed(row, "provenanceFailure") || provenance is not string provenanceText || !string.Equals(provenanceText, "verified", StringComparison.Ordinal))
                return Fail("provenance-failure", chunkId);
            if (!string.Equals(outcomeText, "passed", StringComparison.Ordinal)) return Fail("sample-outcome", chunkId);
            return null;
        }

        static bool Readable(object value) => value is string text && !string.IsNullOrWhiteSpace(text);

        static bool Closed(object row, string name)
        {
            var exact = false;
            var variant = false;
            foreach (var key in RcPort.Names(row))
            {
                if (string.Equals(key, name, StringComparison.Ordinal)) exact = true;
                else if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) variant = true;
            }
            if (variant) return true;
            if (!exact) return false;
            var value = RcPort.Field(row, name);
            return !(value is bool bit && !bit);
        }

        static string[] CarryIds(object carries)
        {
            var ids = new List<string>();
            foreach (var carry in RcPort.Unwrap(carries))
            {
                if (carry == null) continue;
                if (carry is not string text || text.Length == 0) throw new InvalidOperationException("shadow-carry-id");
                ids.Add(text);
            }
            ids.Sort(StringComparer.Ordinal);
            return ids.ToArray();
        }

        static Chunk ParseChunk(object raw)
        {
            var id = RequireText(RcPort.Field(raw, "id"), "shadow-chunk-id");
            var suite = RequireText(RcPort.Field(raw, "suite"), "shadow-chunk-suite");
            var boundary = RequireText(RcPort.Field(raw, "boundary"), "shadow-chunk-boundary");
            var skip = RcPort.Field(raw, "expectedSkipOnly");
            var carry = RcPort.Field(raw, "completelyCarryable");
            if (skip != null && skip is not bool) RcPort.Throw("shadow-chunk-skip");
            if (carry != null && carry is not bool) RcPort.Throw("shadow-chunk-carryable");
            return new Chunk
            {
                Id = id,
                Suite = suite,
                Boundary = boundary,
                ExpectedSkipOnly = skip is bool skipBit && skipBit,
                CompletelyCarryable = carry is bool carryBit && carryBit
            };
        }

        static string RequireText(object value, string message)
        {
            var text = value as string;
            if (text == null || text.Length == 0 || text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0)
                throw new InvalidOperationException(message);
            return text;
        }

        static string ResolveSeed(string seed)
        {
            if (string.IsNullOrEmpty(seed))
            {
                var bytes = new byte[32];
                RandomNumberGenerator.Fill(bytes);
                return Convert.ToHexString(bytes).ToLowerInvariant();
            }
            if (!SeedPattern.IsMatch(seed)) RcPort.Throw("shadow-seed");
            return seed;
        }

        static int SampleCount(int cchunk, int stratumCount)
        {
            if (cchunk <= 0) return 0;
            var fraction = (int)(((long)cchunk * 5 + 99) / 100);
            var baseCount = Math.Min(cchunk, Math.Max(30, fraction));
            return Math.Min(cchunk, Math.Max(baseCount, stratumCount));
        }

        static string Rank(string seed, string purpose, string suite, string boundary, string id)
        {
            var text = seed + "\n" + purpose + "\n" + suite + "\n" + boundary + "\n" + id;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        static List<Chunk> SortRank(List<Chunk> items, string seed, string purpose)
        {
            return items
                .OrderBy(item => Rank(seed, purpose, item.Suite, item.Boundary, item.Id), StringComparer.Ordinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToList();
        }

        static List<Chunk> SortIdentity(List<Chunk> items)
        {
            return items
                .OrderBy(item => item.Suite, StringComparer.Ordinal)
                .ThenBy(item => item.Boundary, StringComparer.Ordinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToList();
        }

        static string[] Ids(List<Chunk> items)
        {
            var ids = new string[items.Count];
            for (var i = 0; i < items.Count; i++) ids[i] = items[i].Id;
            return ids;
        }

        static void WriteProposal(string path, string checkoutRoot, RcShadowProposal proposal)
        {
            var full = ExternalPath(path, checkoutRoot);
            if (File.Exists(full)) RcPort.Throw("write-once");
            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var json = ProposalJson(proposal);
            var bytes = new UTF8Encoding(false).GetBytes(json);
            using (var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var read = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))).ToLowerInvariant();
            if (!string.Equals(read, digest, StringComparison.Ordinal)) RcPort.Throw("proposal-readback");
        }

        static string ProposalJson(RcShadowProposal proposal)
        {
            var selected = new JsonArray();
            foreach (var id in proposal.Selected) selected.Add(id);
            var skips = new JsonArray();
            foreach (var id in proposal.ExpectedSkipOnly) skips.Add(id);
            var node = new JsonObject
            {
                ["format"] = proposal.Format,
                ["effectiveMode"] = proposal.EffectiveMode,
                ["gateCredit"] = proposal.GateCredit,
                ["bindingState"] = proposal.BindingState,
                ["incrementalValidation"] = proposal.IncrementalValidation,
                ["trustGranted"] = proposal.TrustGranted,
                ["trustRecordWritten"] = proposal.TrustRecordWritten,
                ["pilotClaim"] = proposal.PilotClaim,
                ["launched"] = proposal.Launched,
                ["expandedToFull"] = proposal.ExpandedToFull,
                ["carriesInvalidated"] = proposal.CarriesInvalidated,
                ["sample"] = proposal.Sample,
                ["shadowPilot"] = proposal.ShadowPilot,
                ["seed"] = proposal.Seed,
                ["n"] = proposal.N,
                ["cchunk"] = proposal.Cchunk,
                ["stratumCount"] = proposal.StratumCount,
                ["selected"] = selected,
                ["expectedSkipOnly"] = skips
            };
            return node.ToJsonString(JsonOptions);
        }

        static string ExternalPath(string path, string checkoutRoot)
        {
            var full = Path.GetFullPath(path);
            var checkout = Path.GetFullPath(checkoutRoot ?? "").TrimEnd(Path.DirectorySeparatorChar);
            var prefix = checkout + Path.DirectorySeparatorChar;
            if (full.Equals(checkout, StringComparison.OrdinalIgnoreCase) || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                RcPort.Throw("proposal-output-inside-checkout");
            var current = full;
            for (var depth = 0; !string.IsNullOrEmpty(current); depth++)
            {
                if (depth >= 128) RcPort.Throw("proposal-path-depth");
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    RcPort.Throw("proposal-linked-path");
                var parent = Path.GetDirectoryName(current);
                if (string.Equals(parent, current, StringComparison.Ordinal)) break;
                current = parent;
            }
            return full;
        }

        static RcShadowFailure Fail(string kind, string chunkId) => new RcShadowFailure { Kind = kind, ChunkId = chunkId };

        static bool OrdinalEquals(object value, string expected) =>
            value is string text && string.Equals(text, expected, StringComparison.Ordinal);

        static bool Whole(object value)
        {
            if (value is not int && value is not long) return false;
            var number = value is int whole ? whole : (long)value;
            return number >= 0 && number <= int.MaxValue;
        }

        static int ToInt(object value) => value is int whole ? whole : (int)(long)value;

        sealed class Chunk
        {
            public string Id;
            public string Suite;
            public string Boundary;
            public bool ExpectedSkipOnly;
            public bool CompletelyCarryable;
        }
    }
}
