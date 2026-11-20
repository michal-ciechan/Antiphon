// The release gate's credit decision (moved from release-gate.ps1). D-2: a run earns master-scheduled or rc-release
// credit from its own identity and real green flags, never a guessed kind. D-3: an RC earns credit only against the
// SHA its candidate was pinned to, and a candidate ref is a legacy release/rc-<stamp> or a long-lived rc/YYYY-MM-DD.
// nightly-run.ps1 and release-cut.ps1 run under pwsh and load this core.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    public static class ReleaseGateCredit
    {
        public const string DefaultReleaseRoot = @"C:\Antiphon\releases";

        private static readonly string[] Profiles = { "nightly", "rc" };

        // PowerShell -match: case-insensitive.
        private static readonly Regex CandidateRefPattern = new Regex(@"^release/rc-(?<stamp>\d{8}T\d{6}Z)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex LongLivedRefPattern = new Regex(@"^rc/(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // PowerShell -cmatch: case-sensitive.
        private static readonly Regex FullShaPattern = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

        private static PSObject Note(params object[] pairs)
        {
            var o = new PSObject();
            for (var i = 0; i < pairs.Length; i += 2) o.Properties.Add(new PSNoteProperty((string)pairs[i], pairs[i + 1]));
            return o;
        }

        // Join-Path: on Windows '/' becomes '\'; at most one separator is dropped from each side of the join.
        private static string Join(string parent, string child)
        {
            var sep = Path.DirectorySeparatorChar;
            string Normal(string s) => sep == '\\' ? (s ?? "").Replace('/', '\\') : (s ?? "");
            var left = Normal(parent);
            var right = Normal(child);
            if (left.Length > 0 && left[left.Length - 1] == sep) left = left.Substring(0, left.Length - 1);
            if (right.Length > 0 && right[0] == sep) right = right.Substring(1);
            return left + sep + right;
        }

        /// <summary>Test-ReleaseGateRealBoolean (D-2): only a real boolean counts; a JSON string "true" does not.</summary>
        public static bool RealBoolean(object value) => PsOps.Base(value) is bool b && b;

        /// <summary>Test-ReleaseGateFullSha: 40 lower-case hex digits.</summary>
        public static bool FullSha(string sha) => !string.IsNullOrWhiteSpace(sha) && FullShaPattern.IsMatch(sha);

        /// <summary>
        /// Test-ReleaseGateCandidateRef (D-3): a legacy release/rc-yyyyMMddTHHmmssZ stamp, or a long-lived rc/YYYY-MM-DD
        /// (refs/heads/ stripped). The long-lived candidate id is rc-YYYY-MM-DD- plus the first 12 hex digits of a full
        /// sha. Refused: { Ok, Reason (empty-ref, ref-timestamp, ref-date, ref-shape), Stamp, CandidateId, Ref, Kind };
        /// accepted: { Ok, Reason, Stamp, Kind, CandidateId, Ref, CutUtc }.
        /// </summary>
        public static PSObject CandidateRef(string reference, string sha)
        {
            if (string.IsNullOrWhiteSpace(reference)) return Note("Ok", false, "Reason", "empty-ref", "Stamp", "", "CandidateId", "", "Ref", "", "Kind", "");
            var normalised = reference.StartsWith("refs/heads/", StringComparison.Ordinal) ? reference.Substring("refs/heads/".Length) : reference;
            const DateTimeStyles styles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;
            var stampMatch = CandidateRefPattern.Match(normalised);
            if (stampMatch.Success)
            {
                var stamp = stampMatch.Groups["stamp"].Value;
                if (!DateTime.TryParseExact(stamp, "yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture, styles, out var cut))
                    return Note("Ok", false, "Reason", "ref-timestamp", "Stamp", stamp, "CandidateId", "", "Ref", "", "Kind", "stamp");
                return Note("Ok", true, "Reason", "", "Stamp", stamp, "Kind", "stamp", "CandidateId", "rc-" + stamp, "Ref", "release/rc-" + stamp,
                    "CutUtc", cut.ToUniversalTime());
            }
            var dayMatch = LongLivedRefPattern.Match(normalised);
            if (dayMatch.Success)
            {
                var dateText = dayMatch.Groups["y"].Value + "-" + dayMatch.Groups["m"].Value + "-" + dayMatch.Groups["d"].Value;
                if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, styles, out var cut))
                    return Note("Ok", false, "Reason", "ref-date", "Stamp", "", "CandidateId", "", "Ref", "", "Kind", "long-lived");
                var candidateId = FullSha(sha) ? "rc-" + dateText + "-" + sha.Substring(0, 12) : "";
                return Note("Ok", true, "Reason", "", "Stamp", "", "Kind", "long-lived", "CandidateId", candidateId, "Ref", "rc/" + dateText,
                    "CutUtc", cut.ToUniversalTime());
            }
            return Note("Ok", false, "Reason", "ref-shape", "Stamp", "", "CandidateId", "", "Ref", "", "Kind", "");
        }

        /// <summary>
        /// Get-ReleaseGateCreditKind (D-2): the credit a run may earn from its identity alone, before its green flags;
        /// "" for an unknown combination, which is no credit (never the master default). An absent profile is the
        /// pre-CARD-0599 master lane.
        /// </summary>
        public static string CreditKind(string profile, string trigger, string reference)
        {
            var p = (profile ?? "").Trim().ToLowerInvariant();
            var t = (trigger ?? "").Trim().ToLowerInvariant();
            var r = (reference ?? "").Trim();
            if (string.IsNullOrWhiteSpace(p)) p = "nightly";
            if (Array.IndexOf(Profiles, p) < 0) return "";
            if (p == "nightly")
            {
                if (t != "scheduled") return "";
                // -ne: case-insensitive.
                if (!string.Equals(r, "master", StringComparison.OrdinalIgnoreCase) && !string.Equals(r, "origin/master", StringComparison.OrdinalIgnoreCase)) return "";
                return "master-scheduled";
            }
            if (t != "rc") return "";
            return LanguagePrimitives.IsTrue(PsValue.Prop(CandidateRef(r, ""), "Ok")) ? "rc-release" : "";
        }

        /// <summary>
        /// Test-ReleaseGateGreenFlags (D-2): the flags both credit kinds share - real true booleans, a numeric exit code
        /// of 0, a run id and a policy hash. { Ok, Reasons }.
        /// </summary>
        public static PSObject GreenFlags(object state)
        {
            if (state == null) return Note("Ok", false, "Reasons", new object[] { "missing-state" });
            var reasons = new List<object>();
            foreach (var field in new[] { "coverageComplete", "testsPassed", "reportDelivered" })
                if (!RealBoolean(PsValue.Prop(state, field))) reasons.Add("not-true-boolean:" + field);
            var exit = PsValue.Prop(state, "exitCode");
            var exitBase = PsOps.Base(exit);
            if (exit == null) reasons.Add("missing-exitCode");
            else if (!(exitBase is int) && !(exitBase is long) && !(exitBase is double)) reasons.Add("non-numeric-exitCode");
            else if ((int)LanguagePrimitives.ConvertTo(exitBase, typeof(int), CultureInfo.InvariantCulture) != 0) reasons.Add("exitCode:" + PsValue.Str(exit));
            if (string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(state, "runId")))) reasons.Add("missing-runId");
            if (string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(state, "policyHash")))) reasons.Add("missing-policyHash");
            return Note("Ok", reasons.Count == 0, "Reasons", reasons.ToArray());
        }

        /// <summary>
        /// Get-ReleaseGateCreditVerdict (D-2, the single decision point): { CreditKind, Ok, Reasons[, RequestedKind] } - the
        /// credit this state earns ("" when none) and the reasons it did not. An RC earns credit only against the full
        /// SHA its candidate was pinned to (D-3); an explicit coverage scope must be Full.
        /// </summary>
        public static PSObject CreditVerdict(object state)
        {
            if (state == null) return Note("CreditKind", "", "Ok", false, "Reasons", new object[] { "missing-state" });
            var kind = CreditKind(PsValue.Str(PsValue.Prop(state, "profile")), PsValue.Str(PsValue.Prop(state, "trigger")), PsValue.Str(PsValue.Prop(state, "ref")));
            if (string.IsNullOrWhiteSpace(kind)) return Note("CreditKind", "", "Ok", false, "Reasons", new object[] { "unknown-credit-identity" });
            var reasons = new List<object>();
            // Absent scope is the historical v1 Full record. Explicit scope must be Full.
            var stateBase = PsOps.Base(state);
            var scopePresent = stateBase is IDictionary d ? d.Contains("coverageScope") : PSObject.AsPSObject(state).Properties["coverageScope"] != null;
            if (scopePresent && !(PsOps.Base(PsValue.Prop(state, "coverageScope")) is string scope && scope == "Full")) reasons.Add("full-credit-scope-required");
            var flags = GreenFlags(state);
            if (!LanguagePrimitives.IsTrue(PsValue.Prop(flags, "Ok"))) reasons.AddRange(PsValue.AsArray(PsValue.Prop(flags, "Reasons")));
            if (kind == "rc-release")
            {
                var sha = PsValue.Str(PsValue.Prop(state, "sha"));
                var expected = PsValue.Str(PsValue.Prop(state, "expectedSha"));
                if (!FullSha(sha)) reasons.Add("rc-sha-not-full");
                if (!FullSha(expected)) reasons.Add("rc-expected-sha-missing");
                else if (!string.Equals(sha, expected, StringComparison.OrdinalIgnoreCase)) reasons.Add("rc-sha-mismatch");
                if (string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(state, "candidateId")))) reasons.Add("rc-candidate-missing");
            }
            if (reasons.Count > 0) return Note("CreditKind", "", "Ok", false, "Reasons", reasons.ToArray(), "RequestedKind", kind);
            return Note("CreditKind", kind, "Ok", true, "Reasons", new object[0], "RequestedKind", kind);
        }

        /// <summary>Get-ReleaseGateCandidateRoot: releaseRoot (default C:\Antiphon\releases)\candidates\candidateId.</summary>
        public static string CandidateRoot(string releaseRoot, string candidateId) =>
            Join(Join(string.IsNullOrWhiteSpace(releaseRoot) ? DefaultReleaseRoot : releaseRoot, "candidates"), candidateId);

        /// <summary>
        /// Get-ReleaseGateStatePaths (D-2): the credit store. Master green is the only thing that reaches
        /// stateRoot\last-complete-green.json; RC green lands in the candidate root. { CreditKind, Root, GreenPath,
        /// JournalPath, ManifestPath, PublishJournalPath }.
        /// </summary>
        public static PSObject StatePaths(string creditKind, string stateRoot, string releaseRoot, string candidateId)
        {
            // -eq / -ne: case-insensitive.
            if (string.Equals(creditKind, "rc-release", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(candidateId)) throw new NightlyRefusal("rc credit requires a candidate id");
                var root = CandidateRoot(releaseRoot, candidateId);
                return Note("CreditKind", creditKind, "Root", root, "GreenPath", Join(root, "complete-green.json"), "JournalPath", Join(root, "candidate.json"),
                    "ManifestPath", Join(root, "release-manifest.json"), "PublishJournalPath", Join(root, "publish.json"));
            }
            if (!string.Equals(creditKind, "master-scheduled", StringComparison.OrdinalIgnoreCase)) throw new NightlyRefusal("unknown credit kind " + creditKind);
            return Note("CreditKind", creditKind, "Root", stateRoot ?? "", "GreenPath", Join(stateRoot, "last-complete-green.json"), "JournalPath", "",
                "ManifestPath", "", "PublishJournalPath", "");
        }

        /// <summary>
        /// ConvertTo-ReleaseGateUtc: a DateTime as itself in UTC (a timestamp read back from JSON may already be one, and
        /// its string form is in the current culture), else the text parsed invariant-culture, then current-culture,
        /// assumed UTC. Null for blank text; refuses unparseable text.
        /// </summary>
        public static DateTime? Utc(object value)
        {
            if (PsOps.Base(value) is DateTime dt) return dt.ToUniversalTime();
            var text = PsValue.Str(value);
            if (string.IsNullOrWhiteSpace(text)) return null;
            const DateTimeStyles styles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, styles, out var parsed)) return parsed.ToUniversalTime();
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, styles, out parsed)) return parsed.ToUniversalTime();
            throw new NightlyRefusal("unparseable timestamp " + text);
        }

        /// <summary>ConvertTo-ReleaseGateUtc without -AllowEmpty: Utc, refusing blank text (empty timestamp).</summary>
        public static DateTime RequiredUtc(object value) => Utc(value) ?? throw new NightlyRefusal("empty timestamp");
    }
}
