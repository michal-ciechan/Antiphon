// The release gate's publication pieces (moved from release-gate.ps1). D-8: the SHA-bound release manifest carries
// only allowlisted keys, so raw logs, credentials, hostnames and transcripts never reach the public asset. D-8/D-15:
// publication preconditions are hard refusals; the required suites and the verdict come only from the pinned authority
// and its execution ledger (ReleaseAuthority.Check), never from a report or summary.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Management.Automation;

namespace Antiphon.Nightly
{
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

        private static PSObject Note(params object[] pairs)
        {
            var o = new PSObject();
            for (var i = 0; i < pairs.Length; i += 2) o.Properties.Add(new PSNoteProperty((string)pairs[i], pairs[i + 1]));
            return o;
        }

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

        /// <summary>Test-ReleaseGateManifestAllowlist: { Ok, Extra } - the manifest's keys outside the allowlist.</summary>
        public static PSObject ManifestAllowlistCheck(object manifest)
        {
            var extra = new List<object>();
            foreach (var key in PsValue.NoteMap(manifest).Keys)
                if (!Allowlisted(PsValue.Str(key))) extra.Add(PsValue.Str(key));
            return Note("Ok", extra.Count == 0, "Extra", extra.ToArray());
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
        /// Test-ReleaseGatePublicationGate (D-8/D-15): { Ok, Reasons }. Refuses unless the green earns rc-release credit for
        /// the candidate journal's SHA and id and the remote candidate still points there; a no-report, diagnostic or
        /// seam-driven run; no authority, or the authority's own refusals; and, when given, a policy hash or required suite
        /// set other than the pinned authority's. Reasons keep their first occurrence.
        /// </summary>
        public static PSObject PublicationGate(object green, object candidate, string remoteSha, object authority, string expectedPolicyHash, string[] requiredSuites)
        {
            var reasons = new List<string>();
            var verdict = ReleaseGateCredit.CreditVerdict(green);
            if (!string.Equals(PsValue.Str(PsValue.Prop(verdict, "CreditKind")), "rc-release", StringComparison.OrdinalIgnoreCase)) reasons.Add("not-rc-complete-green");
            if (candidate == null) reasons.Add("missing-candidate-journal");
            else
            {
                var sha = PsValue.Str(PsValue.Prop(green, "sha"));
                if (!string.Equals(PsValue.Str(PsValue.Prop(candidate, "sha")), sha, StringComparison.OrdinalIgnoreCase)) reasons.Add("candidate-sha-mismatch");
                if (!string.Equals(PsValue.Str(PsValue.Prop(candidate, "candidateId")), PsValue.Str(PsValue.Prop(green, "candidateId")), StringComparison.OrdinalIgnoreCase))
                    reasons.Add("candidate-id-mismatch");
                if (!string.IsNullOrWhiteSpace(remoteSha) && !string.Equals(remoteSha, sha, StringComparison.OrdinalIgnoreCase)) reasons.Add("remote-candidate-moved");
                if (string.IsNullOrWhiteSpace(remoteSha)) reasons.Add("remote-candidate-unresolved");
            }
            // [bool]$Green.x: PowerShell's truth (any non-empty string is true).
            if (LanguagePrimitives.IsTrue(PsValue.Prop(green, "noReport"))) reasons.Add("no-report-run");
            if (LanguagePrimitives.IsTrue(PsValue.Prop(green, "diagnostic"))) reasons.Add("diagnostic-run");
            if (LanguagePrimitives.IsTrue(PsValue.Prop(green, "seamed"))) reasons.Add("seam-driven-run");
            if (authority == null) reasons.Add("missing-authority");
            else
            {
                // @($Authority.Reasons): a null list is one empty reason, so it still refuses.
                foreach (var r in PsValue.AsArray(PsValue.Prop(authority, "Reasons"))) reasons.Add(PsValue.Str(r));
                var pinnedHolder = PsValue.Prop(authority, "Pinned");
                var pinned = LanguagePrimitives.IsTrue(pinnedHolder) ? PsValue.Prop(pinnedHolder, "Authority") : null;
                var pinnedHash = "";
                var pinnedSuites = new List<string>();
                if (LanguagePrimitives.IsTrue(pinned))
                {
                    pinnedHash = PsValue.Str(PsValue.Prop(pinned, "policyHash"));
                    var suites = PsValue.Prop(pinned, "requiredSuites");
                    if (suites != null) foreach (var s in PsValue.AsArray(suites)) pinnedSuites.Add(PsValue.Str(s));
                }
                if (!string.IsNullOrWhiteSpace(expectedPolicyHash) && !string.Equals(pinnedHash, expectedPolicyHash, StringComparison.OrdinalIgnoreCase))
                    reasons.Add("policy-hash-mismatch");
                var wanted = (requiredSuites ?? new string[0]).Select(s => s ?? "").ToList();
                if (wanted.Count > 0 && SortedUnique(wanted) != SortedUnique(pinnedSuites)) reasons.Add("required-suites-mismatch");
            }
            var unique = new List<object>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in reasons) if (seen.Add(r)) unique.Add(r);
            return Note("Ok", reasons.Count == 0, "Reasons", unique.ToArray());
        }
    }
}
