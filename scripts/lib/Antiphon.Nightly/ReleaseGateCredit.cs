// The release gate's credit decision (moved from release-gate.ps1). D-2: a run earns master-scheduled or rc-release
// credit from its own identity and real green flags, never a guessed kind. D-3: an RC earns credit only against the
// SHA its candidate was pinned to, and a candidate ref is a legacy release/rc-<stamp> or a long-lived rc/YYYY-MM-DD.
// The run core (NightlyRun.cs) and the release cut (ReleaseCut.cs) call it in-process. CreditVerdict and Verdict share one decision:
// the first reads a state as PowerShell does, the second reads plain values, so C# calls it in-process.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>
    /// CreditVerdict's { CreditKind, Ok, Reasons, RequestedKind } in plain values. RequestedKind is "" when the state is
    /// missing or its identity earns no credit kind.
    /// </summary>
    public sealed class ReleaseGateCreditVerdict
    {
        public string CreditKind { get; init; }
        public bool Ok { get; init; }
        public string[] Reasons { get; init; }
        public string RequestedKind { get; init; }
    }

    /// <summary>W2-nightly S19: a candidate ref as ReleaseGateCredit.Candidate reads it (CandidateRef's fields, plain).</summary>
    public sealed class ReleaseGateCandidate
    {
        public bool Ok { get; init; }
        public string Reason { get; init; } = "";
        public string Stamp { get; init; } = "";
        public string Kind { get; init; } = "";
        public string CandidateId { get; init; } = "";
        public string Ref { get; init; } = "";
        public DateTime CutUtc { get; init; }
    }

    /// <summary>W2-nightly S19: a credit kind's store (StatePaths' Root and GreenPath, plain).</summary>
    public sealed record ReleaseGateCreditStore(string CreditKind, string Root, string GreenPath);

    /// <summary>An rc candidate's files (StatePaths' rc-release Root, GreenPath, JournalPath, ManifestPath and PublishJournalPath, plain).</summary>
    public sealed record ReleaseGateCandidatePaths(string Root, string GreenPath, string JournalPath, string ManifestPath, string PublishJournalPath);

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
        internal static string Join(string parent, string child)
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
            var parsed = ParseCandidateRef(reference, sha);
            if (!parsed.Ok) return Note("Ok", false, "Reason", parsed.Reason, "Stamp", parsed.Stamp, "CandidateId", "", "Ref", "", "Kind", parsed.Kind);
            return Note("Ok", true, "Reason", "", "Stamp", parsed.Stamp, "Kind", parsed.Kind, "CandidateId", parsed.CandidateId, "Ref", parsed.Ref,
                "CutUtc", parsed.CutUtc);
        }

        /// <summary>W2-nightly S19: CandidateRef over plain values, for a caller without PowerShell (NightlyRun).</summary>
        public static ReleaseGateCandidate Candidate(string reference, string sha)
        {
            var parsed = ParseCandidateRef(reference, sha);
            return new ReleaseGateCandidate
            {
                Ok = parsed.Ok, Reason = parsed.Reason, Stamp = parsed.Stamp, Kind = parsed.Kind, CandidateId = parsed.Ok ? parsed.CandidateId : "",
                Ref = parsed.Ok ? parsed.Ref : "", CutUtc = parsed.CutUtc,
            };
        }

        private sealed class ParsedCandidateRef
        {
            public bool Ok;
            public string Reason = "";
            public string Stamp = "";
            public string Kind = "";
            public string CandidateId = "";
            public string Ref = "";
            public DateTime CutUtc;
        }

        private static ParsedCandidateRef ParseCandidateRef(string reference, string sha)
        {
            if (string.IsNullOrWhiteSpace(reference)) return new ParsedCandidateRef { Reason = "empty-ref" };
            var normalised = reference.StartsWith("refs/heads/", StringComparison.Ordinal) ? reference.Substring("refs/heads/".Length) : reference;
            const DateTimeStyles styles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;
            var stampMatch = CandidateRefPattern.Match(normalised);
            if (stampMatch.Success)
            {
                var stamp = stampMatch.Groups["stamp"].Value;
                if (!DateTime.TryParseExact(stamp, "yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture, styles, out var cut))
                    return new ParsedCandidateRef { Reason = "ref-timestamp", Stamp = stamp, Kind = "stamp" };
                return new ParsedCandidateRef
                {
                    Ok = true, Stamp = stamp, Kind = "stamp", CandidateId = "rc-" + stamp, Ref = "release/rc-" + stamp, CutUtc = cut.ToUniversalTime()
                };
            }
            var dayMatch = LongLivedRefPattern.Match(normalised);
            if (dayMatch.Success)
            {
                var dateText = dayMatch.Groups["y"].Value + "-" + dayMatch.Groups["m"].Value + "-" + dayMatch.Groups["d"].Value;
                if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, styles, out var cut))
                    return new ParsedCandidateRef { Reason = "ref-date", Kind = "long-lived" };
                var candidateId = FullSha(sha) ? "rc-" + dateText + "-" + sha.Substring(0, 12) : "";
                return new ParsedCandidateRef
                {
                    Ok = true, Kind = "long-lived", CandidateId = candidateId, Ref = "rc/" + dateText, CutUtc = cut.ToUniversalTime()
                };
            }
            return new ParsedCandidateRef { Reason = "ref-shape" };
        }


        /// <summary>
        /// Test-ReleaseGateSameInstant: both blank, or the same instant when both read as dates (a DateTime as itself in
        /// UTC, text as an invariant-culture date assumed UTC), else the same text ignoring case. ConvertFrom-Json turns
        /// an ISO-8601 string into a DateTime, so text written and read back compares as an instant.
        /// </summary>
        public static bool SameInstant(object left, object right)
        {
            var leftEmpty = left == null || string.IsNullOrWhiteSpace(PsValue.Str(left));
            var rightEmpty = right == null || string.IsNullOrWhiteSpace(PsValue.Str(right));
            if (leftEmpty && rightEmpty) return true;
            if (leftEmpty || rightEmpty) return false;
            if (Instant(left, out var l) && Instant(right, out var r)) return l.ToUniversalTime() == r.ToUniversalTime();
            return string.Equals(PsValue.Str(left), PsValue.Str(right), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Instant(object value, out DateTime instant)
        {
            if (PsOps.Base(value) is DateTime dt) { instant = dt.ToUniversalTime(); return true; }
            return DateTime.TryParse(PsValue.Str(value), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out instant);
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
            return ParseCandidateRef(r, "").Ok ? "rc-release" : "";
        }

        /// <summary>
        /// Test-ReleaseGateGreenFlags (D-2): the flags both credit kinds share - real true booleans, a numeric exit code
        /// of 0, a run id and a policy hash. { Ok, Reasons }.
        /// </summary>
        public static PSObject GreenFlags(object state)
        {
            if (state == null) return Note("Ok", false, "Reasons", new object[] { "missing-state" });
            var reasons = GreenReasons(new PsCreditState(state));
            return Note("Ok", reasons.Count == 0, "Reasons", Objects(reasons));
        }

        /// <summary>
        /// Get-ReleaseGateCreditVerdict (D-2, the single decision point): { CreditKind, Ok, Reasons[, RequestedKind] } - the
        /// credit this state earns ("" when none) and the reasons it did not. An RC earns credit only against the full
        /// SHA its candidate was pinned to (D-3); an explicit coverage scope must be Full.
        /// </summary>
        public static PSObject CreditVerdict(object state)
        {
            if (state == null) return Note("CreditKind", "", "Ok", false, "Reasons", new object[] { "missing-state" });
            var verdict = Decide(new PsCreditState(state));
            if (string.IsNullOrWhiteSpace(verdict.RequestedKind)) return Note("CreditKind", "", "Ok", false, "Reasons", Objects(verdict.Reasons));
            return Note("CreditKind", verdict.CreditKind, "Ok", verdict.Ok, "Reasons", Objects(verdict.Reasons), "RequestedKind", verdict.RequestedKind);
        }

        /// <summary>
        /// CreditVerdict over plain values: a key is present when the dictionary contains it, booleans and numbers are
        /// themselves, and text fields read as invariant-culture strings.
        /// </summary>
        public static ReleaseGateCreditVerdict Verdict(IReadOnlyDictionary<string, object> state)
        {
            if (state == null) return new ReleaseGateCreditVerdict { CreditKind = "", Ok = false, Reasons = new[] { "missing-state" }, RequestedKind = "" };
            return Decide(new PlainCreditState(state));
        }

        private static ReleaseGateCreditVerdict Decide(ICreditState state)
        {
            var kind = CreditKind(state.Text("profile"), state.Text("trigger"), state.Text("ref"));
            if (string.IsNullOrWhiteSpace(kind))
                return new ReleaseGateCreditVerdict { CreditKind = "", Ok = false, Reasons = new[] { "unknown-credit-identity" }, RequestedKind = "" };
            var reasons = new List<string>();
            // Absent scope is the historical v1 Full record. Explicit scope must be Full.
            if (state.Has("coverageScope") && !(state.Value("coverageScope") is string scope && scope == "Full")) reasons.Add("full-credit-scope-required");
            reasons.AddRange(GreenReasons(state));
            if (kind == "rc-release")
            {
                var sha = state.Text("sha");
                var expected = state.Text("expectedSha");
                if (!FullSha(sha)) reasons.Add("rc-sha-not-full");
                if (!FullSha(expected)) reasons.Add("rc-expected-sha-missing");
                else if (!string.Equals(sha, expected, StringComparison.OrdinalIgnoreCase)) reasons.Add("rc-sha-mismatch");
                if (string.IsNullOrWhiteSpace(state.Text("candidateId"))) reasons.Add("rc-candidate-missing");
            }
            if (reasons.Count > 0) return new ReleaseGateCreditVerdict { CreditKind = "", Ok = false, Reasons = reasons.ToArray(), RequestedKind = kind };
            return new ReleaseGateCreditVerdict { CreditKind = kind, Ok = true, Reasons = new string[0], RequestedKind = kind };
        }

        private static List<string> GreenReasons(ICreditState state)
        {
            var reasons = new List<string>();
            foreach (var field in new[] { "coverageComplete", "testsPassed", "reportDelivered" })
                if (!(state.Value(field) is bool flag && flag)) reasons.Add("not-true-boolean:" + field);
            var exit = state.Value("exitCode");
            if (exit == null) reasons.Add("missing-exitCode");
            else if (!(exit is int) && !(exit is long) && !(exit is double)) reasons.Add("non-numeric-exitCode");
            else if (state.Int(exit) != 0) reasons.Add("exitCode:" + state.Text("exitCode"));
            if (string.IsNullOrWhiteSpace(state.Text("runId"))) reasons.Add("missing-runId");
            if (string.IsNullOrWhiteSpace(state.Text("policyHash"))) reasons.Add("missing-policyHash");
            return reasons;
        }

        private static object[] Objects(IList<string> values)
        {
            var items = new object[values.Count];
            for (var i = 0; i < items.Length; i++) items[i] = values[i];
            return items;
        }

        /// <summary>How the decision reads one credit state.</summary>
        private interface ICreditState
        {
            /// <summary>The state names the field.</summary>
            bool Has(string name);

            /// <summary>The field's value; null when absent.</summary>
            object Value(string name);

            /// <summary>[string] of the field; "" when absent.</summary>
            string Text(string name);

            /// <summary>[int] of a numeric field value (int, long or double).</summary>
            int Int(object value);
        }

        /// <summary>PowerShell's reading: $state.name, its base object, and [string] / [int] casts.</summary>
        private sealed class PsCreditState : ICreditState
        {
            private readonly object state;

            public PsCreditState(object state) { this.state = state; }

            public bool Has(string name) =>
                PsOps.Base(state) is IDictionary d ? d.Contains(name) : PSObject.AsPSObject(state).Properties[name] != null;

            public object Value(string name) => PsOps.Base(PsValue.Prop(state, name));

            public string Text(string name) => PsValue.Str(PsValue.Prop(state, name));

            public int Int(object value) => (int)LanguagePrimitives.ConvertTo(value, typeof(int), CultureInfo.InvariantCulture);
        }

        /// <summary>Plain values: the dictionary's own key comparison, invariant-culture text, Convert.ToInt32.</summary>
        private sealed class PlainCreditState : ICreditState
        {
            private readonly IReadOnlyDictionary<string, object> state;

            public PlainCreditState(IReadOnlyDictionary<string, object> state) { this.state = state; }

            public bool Has(string name) => state.ContainsKey(name);

            public object Value(string name) => state.TryGetValue(name, out var value) ? value : null;

            public string Text(string name) => Value(name) switch
            {
                null => "",
                string text => text,
                bool flag => flag ? "True" : "False",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
                var other => other.ToString() ?? "",
            };

            public int Int(object value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);
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
        /// W2-nightly S19: StatePaths' Root and GreenPath over plain values, for a caller without PowerShell (NightlyRun).
        /// The same refusals.
        /// </summary>
        public static ReleaseGateCreditStore CreditStore(string creditKind, string stateRoot, string releaseRoot, string candidateId)
        {
            if (string.Equals(creditKind, "rc-release", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(candidateId)) throw new NightlyRefusal("rc credit requires a candidate id");
                var root = CandidateRoot(releaseRoot, candidateId);
                return new ReleaseGateCreditStore(creditKind, root, Join(root, "complete-green.json"));
            }
            if (!string.Equals(creditKind, "master-scheduled", StringComparison.OrdinalIgnoreCase)) throw new NightlyRefusal("unknown credit kind " + creditKind);
            return new ReleaseGateCreditStore(creditKind, stateRoot ?? "", Join(stateRoot, "last-complete-green.json"));
        }

        /// <summary>
        /// Unit 14 of 2026-11-22-remaining-ps1-to-csharp.md: StatePaths' rc-release paths over plain values, for the
        /// publish-release and promote-release commands (no PowerShell). Refuses a blank candidate id as StatePaths does.
        /// </summary>
        public static ReleaseGateCandidatePaths CandidatePaths(string releaseRoot, string candidateId)
        {
            if (string.IsNullOrWhiteSpace(candidateId)) throw new NightlyRefusal("rc credit requires a candidate id");
            var root = CandidateRoot(releaseRoot, candidateId);
            return new ReleaseGateCandidatePaths(root, Join(root, "complete-green.json"), Join(root, "candidate.json"), Join(root, "release-manifest.json"),
                Join(root, "publish.json"));
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
