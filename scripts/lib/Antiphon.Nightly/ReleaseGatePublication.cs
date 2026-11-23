// The release gate's publication pieces (moved from release-gate.ps1). D-8: the SHA-bound release manifest carries
// only allowlisted keys, so raw logs, credentials, hostnames and transcripts never reach the public asset. D-8/D-15:
// publication preconditions are hard refusals; the required suites and the verdict come only from the pinned authority
// and its execution ledger (ReleaseAuthority.Check), never from a report or summary. Unit 14 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the gate and the allowlist check answer typed records
// (their PSObject forms had no caller left once publish-release.ps1 and promote-release.ps1 became C#), so the
// publish-release and promote-release commands run them without PowerShell. ASCII only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;

namespace Antiphon.Nightly
{
    /// <summary>Test-ReleaseGatePublicationGate's answer: Ok, or the refusal reasons (each kept at its first occurrence).</summary>
    public sealed class ReleaseGatePublicationGate
    {
        /// <summary>Every precondition holds.</summary>
        public bool Ok { get; init; }

        /// <summary>The refusal reasons, in order, without repeats.</summary>
        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    }

    /// <summary>Test-ReleaseGateManifestAllowlist's answer: Ok, or the manifest keys outside the allowlist.</summary>
    public sealed class ReleaseGateAllowlistCheck
    {
        /// <summary>Every key is allowlisted.</summary>
        public bool Ok { get; init; }

        /// <summary>The keys outside the allowlist, in manifest order.</summary>
        public IReadOnlyList<string> Extra { get; init; } = Array.Empty<string>();
    }

    public static class ReleaseGatePublication
    {
        public const int ManifestSchema = 1;
        public const string DefaultCoordinationRoot = @"C:\Antiphon\verification";

        public static readonly string[] ManifestAllowlist =
        {
            "schemaVersion", "repository", "tag", "candidateRef", "candidateId", "sha",
            "nativeRunId", "windmillJobId", "scheduleSlot", "startedAt", "completedAt",
            "policyHash", "profile", "scriptHashes", "buildHash", "bundleHash",
            "suites", "exclusions", "summaryDigest", "capabilities", "publicationAuthorityDigest",
        };

        private static bool Allowlisted(string key) => ManifestAllowlist.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// New-ReleaseGateManifest (D-8): schemaVersion, then every allowlisted key in order (null when not given). A field
        /// outside the allowlist is refused.
        /// </summary>
        public static OrderedDictionary NewManifest(IDictionary fields)
        {
            var manifest = new OrderedDictionary(StringComparer.OrdinalIgnoreCase) { { "schemaVersion", ManifestSchema } };
            foreach (var key in ManifestAllowlist)
            {
                if (key == "schemaVersion") continue;
                manifest[key] = fields != null && fields.Contains(key) ? fields[key] : null;
            }
            if (fields != null)
                foreach (var key in fields.Keys)
                    if (!Allowlisted(PsValue.Str(key))) throw new NightlyRefusal("manifest field " + PsValue.Str(key) + " is not allowlisted");
            return manifest;
        }

        /// <summary>Test-ReleaseGateManifestAllowlist: Ok, or the manifest's keys (a dictionary's or a record's) outside the allowlist.</summary>
        public static ReleaseGateAllowlistCheck ManifestAllowlistCheck(object manifest)
        {
            var extra = new List<string>();
            foreach (var key in PsValue.NoteMap(manifest).Keys)
                if (!Allowlisted(PsValue.Str(key))) extra.Add(PsValue.Str(key));
            return new ReleaseGateAllowlistCheck { Ok = extra.Count == 0, Extra = extra.ToArray() };
        }

        /// <summary>Get-ReleaseGateManifestDigest: SHA-256 of the manifest's canonical JSON.</summary>
        public static string ManifestDigest(object manifest) => TestPhaseRecords.Sha256Text(NightlyDocuments.CanonicalJson(manifest));

        /// <summary>Get-ReleaseGateMasterStateFiles (V-2): the four files an RC lane must leave byte-identical.</summary>
        public static string[] MasterStateFiles(string stateRoot) => new[]
        {
            ReleaseGateCredit.Join(stateRoot, "last-run.json"), ReleaseGateCredit.Join(stateRoot, "last-complete-green.json"),
            ReleaseGateCredit.Join(stateRoot, "last-monitor.json"), ReleaseGateCredit.Join(stateRoot, "interim-qualification-receipt.json"),
        };

        /// <summary>Get-ReleaseGateNativeLockPath (D-4): native-run.lock under the coordination root (default C:\Antiphon\verification).</summary>
        public static string NativeLockPath(string coordinationRoot) =>
            ReleaseGateCredit.Join(string.IsNullOrWhiteSpace(coordinationRoot) ? DefaultCoordinationRoot : coordinationRoot, "native-run.lock");

        // Sort-Object -Unique over strings: current-culture order ignoring case, one of each case-insensitive group.
        private static string SortedUnique(IEnumerable<string> values)
        {
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            var list = values.ToList();
            list.Sort((a, b) => compare.Compare(a, b, CompareOptions.IgnoreCase));
            var unique = new List<string>();
            foreach (var value in list)
                if (unique.Count == 0 || compare.Compare(unique[unique.Count - 1], value, CompareOptions.IgnoreCase) != 0) unique.Add(value);
            return string.Join(",", unique);
        }

        /// <summary>
        /// Test-ReleaseGatePublicationGate (D-8/D-15). Refuses unless the green earns rc-release credit (ReleaseGateCredit.Verdict)
        /// for the candidate journal's SHA and id and the remote candidate still points there; a no-report, diagnostic or
        /// seam-driven run; no authority, or the authority's own refusals; and, when given, a policy hash or required suite
        /// set other than the pinned authority's. green and candidate are the documents as plain values (a null document is
        /// absent); a marker is true as PowerShell's [bool] reads it (any non-empty string is true). Reasons keep their first
        /// occurrence.
        /// </summary>
        public static ReleaseGatePublicationGate Gate(IReadOnlyDictionary<string, object> green, IReadOnlyDictionary<string, object> candidate,
            string remoteSha, AuthorityCheck authority, string expectedPolicyHash, IReadOnlyList<string> requiredSuites)
        {
            string Field(IReadOnlyDictionary<string, object> document, string name) =>
                document != null && document.TryGetValue(name, out var value) ? PsValue.Str(value) : "";
            bool Marker(string name) => green != null && green.TryGetValue(name, out var value) && PsValue.IsTrue(value);
            var reasons = new List<string>();
            var verdict = ReleaseGateCredit.Verdict(green);
            if (!string.Equals(verdict.CreditKind, "rc-release", StringComparison.OrdinalIgnoreCase)) reasons.Add("not-rc-complete-green");
            if (candidate == null) reasons.Add("missing-candidate-journal");
            else
            {
                var sha = Field(green, "sha");
                if (!string.Equals(Field(candidate, "sha"), sha, StringComparison.OrdinalIgnoreCase)) reasons.Add("candidate-sha-mismatch");
                if (!string.Equals(Field(candidate, "candidateId"), Field(green, "candidateId"), StringComparison.OrdinalIgnoreCase)) reasons.Add("candidate-id-mismatch");
                if (!string.IsNullOrWhiteSpace(remoteSha) && !string.Equals(remoteSha, sha, StringComparison.OrdinalIgnoreCase)) reasons.Add("remote-candidate-moved");
                if (string.IsNullOrWhiteSpace(remoteSha)) reasons.Add("remote-candidate-unresolved");
            }
            if (Marker("noReport")) reasons.Add("no-report-run");
            if (Marker("diagnostic")) reasons.Add("diagnostic-run");
            if (Marker("seamed")) reasons.Add("seam-driven-run");
            if (authority == null) reasons.Add("missing-authority");
            else
            {
                reasons.AddRange(authority.Reasons ?? new[] { "" });
                var pinned = authority.Pinned?.Authority;
                var pinnedHash = pinned?.PolicyHash ?? "";
                var pinnedSuites = (pinned?.RequiredSuites ?? new string[0]).Select(s => s ?? "").ToList();
                if (!string.IsNullOrWhiteSpace(expectedPolicyHash) && !string.Equals(pinnedHash, expectedPolicyHash, StringComparison.OrdinalIgnoreCase))
                    reasons.Add("policy-hash-mismatch");
                var wanted = (requiredSuites ?? new string[0]).Select(s => s ?? "").ToList();
                if (wanted.Count > 0 && SortedUnique(wanted) != SortedUnique(pinnedSuites)) reasons.Add("required-suites-mismatch");
            }
            var unique = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in reasons) if (seen.Add(r)) unique.Add(r);
            return new ReleaseGatePublicationGate { Ok = reasons.Count == 0, Reasons = unique.ToArray() };
        }
    }
}
