// CARD-1313 S6-A/S6-B: which full-release baseline B a release selection attempt proposes against. The 2026-11-22 plan
// (docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, unit 8) ported it from Read-ReleaseSelectionBaseline
// (scripts/lib/release-selection.ps1, deleted) and replaced the CLI's copy (Antiphon.Scripts/Baseline.cs, deleted).
//
// A baseline is an archived publication, never today's card revision or remote tip: a receipt
// (antiphon-full-release-baseline-v1) names the published candidate's immutable artifacts under the release root by
// SHA-256, and every one of them must bind to the others - the pinned authority and policy blob, the Full ledger with its
// prepublication receipt, the publication authority, manifest and publish record, the complete green, every required
// suite's planned chunks and rosters with passing TRX at their digests, and the historical selection contract. Without a
// receipt path the archive (<release root>/baselines/*.json, by name) is read: every receipt is validated before one is
// chosen (a corrupt one refuses rather than being skipped for an older green), and the last compatible one naming the
// requested B (or any B) wins. Any mismatch refuses with its reason (baseline-full-only, baseline-digest:<file>, ...).
// The documents are read as written (TestSelectionJson.Parse: keys exactly, strings kept strings). Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The baseline an attempt proposes against: its commit, the receipt's digest, and the reason when there is none usable.</summary>
    public sealed class ReleaseSelectionBaselineChoice
    {
        /// <summary>The baseline commit ("" when none was requested or found).</summary>
        public string Sha { get; init; } = "";

        /// <summary>The SHA-256 of the receipt that names it ("" without a receipt).</summary>
        public string Digest { get; init; } = "";

        /// <summary>The receipt's contract equals the candidate's.</summary>
        public bool Compatible { get; init; }

        /// <summary>baseline-absent, baseline-incompatible, or "".</summary>
        public string Reason { get; init; } = "";
    }

    /// <summary>Read-ReleaseSelectionBaseline.</summary>
    public static class ReleaseSelectionBaseline
    {
        /// <summary>The receipt's format.</summary>
        public const string Format = "antiphon-full-release-baseline-v1";

        private static readonly string[] RequiredFiles =
        {
            "authority/authority.json", "authority/execution-plan.json", "authority/discovery.json", "authority/ledger.json",
            "authority/publication-authority.json", "release-manifest.json", "publish.json", "complete-green.json",
        };

        private static readonly Regex Sha256Hex = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// The baseline of the request against the candidate's contract: the receipt at BaselineReceiptPath, else the archive's
        /// choice, else none (baseline-absent, with the requested BaselineSha). Refuses (NightlyRefusal) a receipt that does not
        /// hold.
        /// </summary>
        public static ReleaseSelectionBaselineChoice Read(ReleaseSelectionRequest request, IDictionary contract)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var requested = request.BaselineSha ?? "";
            var empty = new ReleaseSelectionBaselineChoice { Sha = requested, Reason = "baseline-absent" };
            if (string.IsNullOrEmpty(request.BaselineReceiptPath))
            {
                var archive = ReleaseGateCredit.Join(request.ReleaseRoot, "baselines");
                if (!Directory.Exists(archive)) return empty;
                ReleaseSelectionBaselineChoice chosen = null;
                var files = new DirectoryInfo(archive).GetFiles("*.json")
                    .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    .OrderBy(f => f.Name, StringComparer.Create(CultureInfo.CurrentCulture, true));
                foreach (var file in files)
                {
                    // Each receipt on its own identity (no requested B), then the caller's B as a filter.
                    var candidate = Receipt(request, file.FullName, "", contract);
                    if (candidate != null && candidate.Compatible && (requested.Length == 0 || candidate.Sha == requested)) chosen = candidate;
                }
                return chosen ?? empty;
            }
            var path = ReleaseSelectionAttempt.AssertPath(request.BaselineReceiptPath, request.ReleaseRoot, request.RepositoryRoot);
            return Receipt(request, path, requested, contract) ?? empty;
        }

        // One receipt (null when the path holds no file).
        private static ReleaseSelectionBaselineChoice Receipt(ReleaseSelectionRequest request, string receiptPath, string requested, IDictionary contract)
        {
            var path = ReleaseSelectionAttempt.AssertPath(receiptPath, request.ReleaseRoot, request.RepositoryRoot);
            if (!File.Exists(path)) return null;
            var receipt = Document(path) ?? throw new NightlyRefusal("baseline-unqualified");
            if (!Is(receipt, "format", Format)) throw new NightlyRefusal("baseline-unqualified");
            if (!Is(receipt, "coverageScope", "Full") || !Is(receipt, "profile", "rc")) throw new NightlyRefusal("baseline-full-only");
            if (!Is(receipt, "repository", request.Repository)) throw new NightlyRefusal("baseline-repository");
            if (!Is(receipt, "platform", Text(contract["platform"]))) throw new NightlyRefusal("baseline-platform");
            var sha = Text(Get(receipt, "sha")) ?? "";
            if (requested.Length > 0 && sha != requested) throw new NightlyRefusal("baseline-sha");
            ReleaseSelectionAttempt.AssertCommit(request.RepositoryRoot, sha);
            var root = ReleaseSelectionAttempt.AssertPath(ReleaseGateCredit.Join(request.ReleaseRoot, Text(Get(receipt, "candidateRoot"))), request.ReleaseRoot, request.RepositoryRoot);
            var files = Get(receipt, "files") as IDictionary;
            var documents = new Dictionary<string, IDictionary>(StringComparer.Ordinal);
            var digests = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in RequiredFiles)
            {
                var file = ReleaseSelectionAttempt.AssertPath(ReleaseGateCredit.Join(root, name), root);
                if (!(Get(files, name) is string expected) || !Sha256Hex.IsMatch(expected) || !File.Exists(file) || NightlyPaths.FileSha256(file) != expected)
                    throw new NightlyRefusal("baseline-digest:" + name);
                digests[name] = expected;
                documents[name] = Document(file) ?? throw new NightlyRefusal("baseline-document:" + name);
            }
            var authority = documents["authority/authority.json"];
            var ledger = documents["authority/ledger.json"];
            var publication = documents["authority/publication-authority.json"];
            var manifest = documents["release-manifest.json"];
            var published = documents["publish.json"];
            var green = documents["complete-green.json"];
            if (Text(Get(authority, "schemaVersion")) != "1" || !Is(authority, "kind", "release-authority") || !Is(authority, "profile", "rc")
                || !Is(authority, "repository", request.Repository) || !Is(authority, "sha", sha)
                || !Is(ledger, "selection", "full") || !Is(ledger, "sha", sha)
                || !IsFalse(Get(ledger, "seamed")) || !IsFalse(Get(ledger, "diagnostic")) || !IsFalse(Get(ledger, "noReport"))
                || !(Get(ledger, "teardownSucceeded") is bool teardown && teardown)
                || !ReleaseGateCredit.Verdict(Plain(green)).Ok || !Is(green, "profile", "rc") || !Is(green, "sha", sha)) throw new NightlyRefusal("baseline-full-only");
            var inputs = Get(publication, "inputs") as IDictionary;
            foreach (var (name, file) in new[] { ("authority", "authority"), ("executionPlan", "execution-plan"), ("discovery", "discovery"), ("ledger", "ledger") })
                if (!Is(inputs, name + "Digest", digests["authority/" + file + ".json"])) throw new NightlyRefusal("baseline-publication-binding");
            var digest = TestSelectionJson.Sha256Text(NightlyDocuments.CanonicalJson(inputs));
            if (!Is(publication, "digest", digest) || !Is(manifest, "publicationAuthorityDigest", digest) || !Is(published, "publicationAuthorityDigest", digest)
                || !Is(manifest, "sha", sha) || !Is(published, "sha", sha) || !Is(published, "manifestDigest", ReleaseGatePublication.ManifestDigest(manifest)))
                throw new NightlyRefusal("baseline-publication-binding");
            if (!Items(Get(published, "steps")).Any(s => s is string step && step == "published") || !NonEmpty(Get(published, "releaseId")) || !NonEmpty(Get(published, "releaseUrl")))
                throw new NightlyRefusal("baseline-publication-required");
            var prepublication = Get(ledger, "prepublicationReceipt");
            var recipient = Get(prepublication, "recipient");
            if (!Is(prepublication, "kind", "prepublication") || !Is(Get(prepublication, "correlation"), "sha", sha)
                || !Is(Get(prepublication, "correlation"), "runId", Text(Get(ledger, "runId"))) || !Is(recipient, "cardId", Text(Get(authority, "releaseCardId")))
                || !(Get(recipient, "revision") is long revision) || revision < 1) throw new NightlyRefusal("baseline-receipt");

            var historical = TestSelection.Contract(request.RepositoryRoot, sha, "rc");
            if (NightlyDocuments.CanonicalJson(historical) != NightlyDocuments.CanonicalJson(Get(receipt, "contract"))) throw new NightlyRefusal("baseline-contract");
            if (!Is(authority, "policyHash", Text(historical["policyHash"]))) throw new NightlyRefusal("baseline-policy");
            var blob = ReleaseAuthority.ReadPinnedBlob(request.RepositoryRoot, sha);
            var policy = ReleaseAuthority.PolicyFromBytes(blob.Bytes);
            if (!Is(authority, "policyBlobId", blob.BlobId) || !Is(authority, "policyRawSha256", ReleaseAuthority.Sha256(blob.Bytes))
                || NightlyDocuments.CanonicalJson(Items(Get(authority, "requiredSuites"))) != NightlyDocuments.CanonicalJson(policy.RequiredSuites))
                throw new NightlyRefusal("baseline-policy");

            var plan = documents["authority/execution-plan.json"];
            var discovery = documents["authority/discovery.json"];
            var intentId = Text(Get(authority, "intentId"));
            var candidateId = Text(Get(authority, "candidateId"));
            if (!Is(ledger, "authorityDigest", digests["authority/authority.json"]) || !Is(ledger, "executionPlanDigest", digests["authority/execution-plan.json"])
                || !Is(plan, "authorityDigest", digests["authority/authority.json"]) || !Is(plan, "discoveryDigest", digests["authority/discovery.json"])
                || !Is(plan, "sha", sha) || !Is(plan, "intentId", intentId) || !Is(ledger, "intentId", intentId) || !Is(ledger, "runId", Text(Get(green, "runId")))
                || !Is(ledger, "candidateId", candidateId) || !Is(manifest, "candidateId", candidateId)) throw new NightlyRefusal("baseline-ledger-binding");

            var evidence = Get(inputs, "evidence");
            foreach (var suite in policy.RequiredSuites)
            {
                var planned = Items(Get(plan, "suites")).Where(s => Is(s, "id", suite)).ToList();
                if (planned.Count != 1) throw new NightlyRefusal("baseline-suite-universe");
                if (suite == "client" || suite == "scripts")
                {
                    var rows = Items(Get(ledger, "rosters")).Where(r => Is(r, "suite", suite)).ToList();
                    var plannedEntries = Items(Get(planned[0], "entries"));
                    if (rows.Count != 1 || Text(Get(rows[0], "exitCode")) != "0" || plannedEntries.Length == 0
                        || Items(Get(rows[0], "entries")).Any(e => !Is(e, "outcome", "passed"))
                        || Joined(Items(Get(rows[0], "entries")).Select(e => Text(Get(e, "id")))) != Joined(plannedEntries.Select(Text)))
                        throw new NightlyRefusal("baseline-roster-incomplete");
                    continue;
                }
                var nodes = Items(Get(Get(discovery, "suites"), suite));
                var requiredUids = ReleaseAuthority.RequiredUids(nodes, policy.Exclusions, suite);
                var executed = new List<string>();
                foreach (var chunk in Items(Get(planned[0], "chunks")))
                {
                    var chunkId = Text(Get(chunk, "id"));
                    var rows = Items(Get(ledger, "chunks")).Where(r => Is(r, "chunk", chunkId)).ToList();
                    if (rows.Count != 1 || Text(Get(rows[0], "exitCode")) != "0") throw new NightlyRefusal("baseline-chunk-incomplete");
                    var trx = ReleaseSelectionAttempt.AssertPath(ReleaseGateCredit.Join(root, Text(Get(rows[0], "trx"))), root);
                    var trxSha = Text(Get(rows[0], "trxSha256"));
                    if (!File.Exists(trx) || NightlyPaths.FileSha256(trx) != trxSha || !Is(evidence, chunkId ?? "", trxSha)) throw new NightlyRefusal("baseline-trx-digest");
                    var terminals = NightlyTrx.ReadIdentities(trx);
                    if (terminals.Any(t => t.Outcome != "Passed") || Joined(terminals.Select(t => t.TestId)) != Joined(Items(Get(chunk, "uids")).Select(Text)))
                        throw new NightlyRefusal("baseline-chunk-incomplete");
                    executed.AddRange(terminals.Select(t => t.TestId));
                }
                if (requiredUids.Length == 0 || Joined(executed) != Joined(requiredUids)) throw new NightlyRefusal("baseline-uids-incomplete");
            }
            var compatible = NightlyDocuments.CanonicalJson(historical) == NightlyDocuments.CanonicalJson(contract);
            return new ReleaseSelectionBaselineChoice
            {
                Sha = sha, Digest = NightlyPaths.FileSha256(path), Compatible = compatible, Reason = compatible ? "" : "baseline-incompatible",
            };
        }

        // A JSON object as written (keys exactly), or null for any other document.
        private static IDictionary Document(string path)
        {
            try { return TestSelectionJson.Parse(File.ReadAllText(path, Encoding.UTF8)) as IDictionary; }
            catch (Exception e) when (e is System.Text.Json.JsonException || e is TestSelectionRefusal) { return null; }
        }

        private static object Get(object document, string name) => document is IDictionary map && map.Contains(name) ? map[name] : null;

        // [string] of a value, null kept null.
        private static string Text(object value) => value == null ? null : PsValue.Str(value);

        // $document.name -ceq expected: a missing value equals only a missing one.
        private static bool Is(object document, string name, string expected)
        {
            var value = Get(document, name);
            if (value == null || expected == null) return value == null && expected == null;
            return value is string s ? s == expected : PsValue.Str(value) == expected;
        }

        private static bool IsFalse(object value) => value is bool flag && !flag;

        private static bool NonEmpty(object value) => value is string s ? s.Length > 0 : value != null;

        // @($value): an array's items, nothing for null, otherwise the value itself.
        private static object[] Items(object value) => value == null ? Array.Empty<object>() : value is object[] items ? items : new[] { value };

        // The same multiset of strings reads the same.
        private static string Joined(IEnumerable<string> values) => string.Join(",", values.Select(v => v ?? "").OrderBy(v => v, StringComparer.Ordinal));

        private static IReadOnlyDictionary<string, object> Plain(IDictionary document)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in document) map[PsValue.Str(entry.Key)] = entry.Value;
            return map;
        }
    }
}
