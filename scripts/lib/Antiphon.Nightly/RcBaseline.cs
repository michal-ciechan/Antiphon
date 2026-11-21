// CARD-1836 S7a (moved from rc-baseline.ps1): read-only baseline proposals, never execution or carry authority.
// Plain C#: no PowerShell types.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>A baseline candidate: a release ref (or a bare sha) pinned to its sealed admission envelope and evidence.</summary>
    public sealed class RcBaselinePointer
    {
        public string RefName { get; set; }
        public string SourceSha { get; set; }
        public string Path { get; set; }
        public string Digest { get; set; }
        public string EvidenceRoot { get; set; }

        internal string[] Fields() => new[] { RefName, SourceSha, Path, Digest, EvidenceRoot };
    }

    /// <summary>antiphon-rc-baseline-proposal-v1. Force-Full unless an admitted baseline, a clean policy and no later failure.</summary>
    public sealed class RcBaselineProposal
    {
        public string Format { get; set; } = "antiphon-rc-baseline-proposal-v1";
        public string CandidateSha { get; set; }
        public RcBaselinePointer Selected { get; set; }
        public string Reason { get; set; } = "baseline-absent";
        public string[] Reasons { get; set; } = Array.Empty<string>();
        public JsonElement FullAnchor { get; set; }
        public JsonElement[] Failures { get; set; } = Array.Empty<JsonElement>();
        public string HistoryDigest { get; set; }
        public bool ForceFull { get; set; } = true;
        public bool ExecutionEvidence { get; set; }
        public bool GateCredit { get; set; }
    }

    public static class RcBaseline
    {
        private static Exception Refuse(string what) => new RcAdmissionRefusal(what);
        private static readonly Regex FullSha = new Regex(@"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant);
        private static readonly Regex ReleaseRef = new Regex(@"\Arefs/heads/release/\d+\.\d+\.\d+\z", RegexOptions.CultureInvariant);
        private static readonly Regex RcLine = new Regex(@"\Arc/\d{4}-\d{2}-\d{2}\z", RegexOptions.CultureInvariant);
        private static readonly string[] FailureOutcomes = { "Failed", "Interrupted", "NotRun", "Unproven" };

        private static string Commit(RcInputInventory.GitReader git, string refName) => git.Run(new[] { 0, 1, 128 }, "rev-parse", "--verify", refName + "^{commit}").Text.Trim();

        private static bool Ancestor(RcInputInventory.GitReader git, string ancestor, string descendant) =>
            git.Run(new[] { 0, 1, 128 }, "merge-base", "--is-ancestor", ancestor, descendant).Exit == 0;

        private static JsonElement F(JsonElement o, string name) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) ? v : default;
        private static string S(JsonElement o, string name) { var v = F(o, name); return v.ValueKind == JsonValueKind.String ? v.GetString() : null; }

        private static void AssertKeys(JsonElement o, params string[] allowed)
        {
            if (o.ValueKind != JsonValueKind.Object) throw Refuse("invalid-catalog-object");
            foreach (var p in o.EnumerateObject()) if (Array.IndexOf(allowed, p.Name) < 0) throw Refuse("invalid-catalog-field:" + p.Name);
        }

        /// <summary>The pointer's sealed envelope and its admission at its own source; refused unless eligible.</summary>
        public static (JsonElement Envelope, RcAdmissionResult Admission) ReadPointer(string repositoryRoot, RcBaselinePointer pointer, RcAdmissionContext currentContext,
            DateTimeOffset utcNow)
        {
            var envelope = RcAdmission.ReadJson(pointer.Path, pointer.Digest);
            if (S(envelope, "platform") != currentContext.Platform) throw Refuse("baseline-platform-mismatch");
            // Admission verifies its own historical inputs. New-candidate input differences belong to later dirty
            // marking, not to baseline discovery.
            var inventory = Antiphon.Rc.HistoricalEvidence.Verify(pointer.EvidenceRoot, S(envelope, "inventorySha256"));
            if (!inventory.valid) throw Refuse("inventory-unproven");
            var proofObject = RcAdmission.EvidenceObject(inventory, pointer.EvidenceRoot, S(envelope, "proofPath"));
            var proof = RcAdmission.ReadJson(proofObject.Path, proofObject.Digest);
            var context = new RcAdmissionContext
            {
                Repository = currentContext.Repository, Platform = currentContext.Platform, RcLine = S(envelope, "rcLine"), Contract = F(envelope, "contract"),
            };
            var admission = RcAdmission.Read(pointer.Path, pointer.Digest, pointer.EvidenceRoot, repositoryRoot, pointer.SourceSha, S(envelope, "runId"), pointer.SourceSha,
                RcAdmission.ReadRecord<RcChunkInputManifest>(F(proof, "manifest")), RcAdmission.ReadRecord<RcBoundaryChunk>(F(proof, "chunk")), context, utcNow);
            if (!admission.Eligible) throw Refuse("baseline-admission-unproven:" + string.Join(",", admission.Reasons));
            return (envelope, admission);
        }

        /// <summary>
        /// The baseline proposal for a candidate: among the pointers that are strict ancestors of it (their refs unmoved),
        /// the recorded release, else the newest release, else the newest candidate of the same rc line (ties by ref
        /// name, ordinal), each admitted at its own source. Force-Full whenever no baseline is proven, the admission or
        /// the forced-Full policy says so, or the candidate's history records any failure after it.
        /// </summary>
        public static RcBaselineProposal Propose(string repositoryRoot, string candidateSha, RcAdmissionContext currentContext, IReadOnlyList<RcBaselinePointer> pointers,
            RcBaselinePointer recordedBaseline, string historyPath, string historyDigest, DateTimeOffset utcNow)
        {
            var result = new RcBaselineProposal { CandidateSha = candidateSha, HistoryDigest = historyDigest };
            using var git = new RcInputInventory.GitReader(repositoryRoot);
            try
            {
                if (candidateSha == null || !FullSha.IsMatch(candidateSha) || Commit(git, candidateSha) != candidateSha) throw Refuse("baseline-candidate-invalid");
                if ((currentContext.Platform != "windows" && currentContext.Platform != "linux") || currentContext.RcLine == null || !RcLine.IsMatch(currentContext.RcLine))
                    throw Refuse("baseline-context-invalid");
                pointers ??= Array.Empty<RcBaselinePointer>();
                var seen = new Dictionary<string, RcBaselinePointer>(StringComparer.Ordinal);
                foreach (var pointer in pointers)
                {
                    if (pointer == null || pointer.Fields().Any(string.IsNullOrEmpty)) throw Refuse("baseline-pointer-invalid");
                    if (!FullSha.IsMatch(pointer.SourceSha) || !RcForcedFull.IsDigest(pointer.Digest) || (!ReleaseRef.IsMatch(pointer.RefName) && pointer.RefName != pointer.SourceSha))
                        throw Refuse("baseline-pointer-invalid");
                    if (seen.ContainsKey(pointer.RefName)) throw Refuse("baseline-pointer-conflict");
                    seen.Add(pointer.RefName, pointer);
                }
                if (recordedBaseline != null)
                {
                    if (recordedBaseline.RefName == null || !ReleaseRef.IsMatch(recordedBaseline.RefName) || !seen.ContainsKey(recordedBaseline.RefName))
                        throw Refuse("baseline-recorded-pointer-missing");
                    if (!recordedBaseline.Fields().SequenceEqual(seen[recordedBaseline.RefName].Fields(), StringComparer.Ordinal)) throw Refuse("baseline-recorded-pointer-conflict");
                }
                if (pointers.Count == 0) throw Refuse("baseline-absent");
                var history = RcAdmission.ReadJson(historyPath, historyDigest);
                AssertKeys(history, "format", "candidateSha", "complete", "failures");
                if (S(history, "format") != "antiphon-rc-baseline-history-v1" || S(history, "candidateSha") != candidateSha || F(history, "complete").ValueKind != JsonValueKind.True
                    || F(history, "failures").ValueKind != JsonValueKind.Array) throw Refuse("baseline-history-unproven");
                var failures = F(history, "failures").EnumerateArray().Select(f => f.Clone()).ToArray();
                foreach (var failure in failures)
                {
                    AssertKeys(failure, "sourceSha", "identity", "outcome");
                    var sha = S(failure, "sourceSha");
                    if (sha == null || !FullSha.IsMatch(sha) || string.IsNullOrEmpty(S(failure, "identity")) || Array.IndexOf(FailureOutcomes, S(failure, "outcome")) < 0
                        || !Ancestor(git, sha, candidateSha)) throw Refuse("baseline-history-unproven");
                }
                var eligible = new List<(RcBaselinePointer Pointer, bool Release, long Timestamp)>();
                foreach (var pointer in pointers)
                {
                    if (Commit(git, pointer.RefName) != pointer.SourceSha) throw Refuse("baseline-ref-moved");
                    if (pointer.SourceSha == candidateSha || !Ancestor(git, pointer.SourceSha, candidateSha)) continue;
                    var read = ReadPointer(repositoryRoot, pointer, currentContext, utcNow);
                    var release = pointer.RefName.StartsWith("refs/heads/release/", StringComparison.Ordinal);
                    if (!release && S(read.Envelope, "rcLine") != currentContext.RcLine) continue;
                    if (!long.TryParse(git.Run("show", "-s", "--format=%ct", pointer.SourceSha).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
                        throw Refuse("baseline-commit-date-invalid");
                    eligible.Add((pointer, release, timestamp));
                }
                // Explicit release, then newest release, then newest same-line candidate; ordinal ties.
                (RcBaselinePointer Pointer, bool Release, long Timestamp)? chosen = null;
                foreach (var entry in eligible)
                {
                    if (recordedBaseline != null && entry.Pointer.RefName == recordedBaseline.RefName) { chosen = entry; break; }
                    if (chosen == null || (entry.Release && !chosen.Value.Release) || (entry.Release == chosen.Value.Release && (entry.Timestamp > chosen.Value.Timestamp
                        || (entry.Timestamp == chosen.Value.Timestamp && string.CompareOrdinal(entry.Pointer.RefName, chosen.Value.Pointer.RefName) < 0)))) chosen = entry;
                }
                if (chosen == null) throw Refuse("baseline-absent");
                // Bind the bytes again, then the ref, immediately before returning; no writes.
                var selected = ReadPointer(repositoryRoot, chosen.Value.Pointer, currentContext, utcNow);
                RcAdmission.ReadJson(historyPath, historyDigest);
                if (Commit(git, chosen.Value.Pointer.RefName) != chosen.Value.Pointer.SourceSha || !Ancestor(git, chosen.Value.Pointer.SourceSha, candidateSha))
                    throw Refuse("baseline-ref-moved");
                var envelope = selected.Envelope;
                var forced = RcForcedFull.Decision(currentContext.Contract, F(envelope, "contract"), F(envelope, "history"), F(envelope, "fullAnchor"), utcNow, false);
                var reasons = new SortedSet<string>(selected.Admission.Reasons.Concat(forced.Reasons), StringComparer.Ordinal);
                if (failures.Length > 0) reasons.Add("post-baseline-failures");
                var c = chosen.Value.Pointer;
                result.Selected = new RcBaselinePointer { RefName = c.RefName, SourceSha = c.SourceSha, Path = c.Path, Digest = c.Digest, EvidenceRoot = c.EvidenceRoot };
                result.FullAnchor = F(envelope, "fullAnchor").Clone();
                result.Failures = failures;
                result.Reason = recordedBaseline != null && c.RefName == recordedBaseline.RefName ? "recorded-release" : chosen.Value.Release ? "ancestor-release" : "same-line-candidate";
                result.Reasons = reasons.ToArray();
                result.ForceFull = selected.Admission.ForceFull || forced.ForceFull || failures.Length > 0;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                result.Selected = null;
                result.FullAnchor = default;
                result.ForceFull = true;
                result.Reason = e.Message;
                result.Reasons = new[] { e.Message };
            }
            return result;
        }
    }
}
