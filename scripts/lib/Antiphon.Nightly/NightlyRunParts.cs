// The pure parts of the nightly run core (moved from nightly-run-impl.ps1): the nightly-tests hop's argument list, the
// stale line-ending paths git reports, the run directory name, the state paths and the complete-green predicate.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The nightly-tests.ps1 hop's inputs. PowerShell builds it from a hashtable: [TestsHopRequest]@{ ... }.</summary>
    public sealed class TestsHopRequest
    {
        public string TestsScript { get; set; } = "";
        public string CheckoutRoot { get; set; } = "";
        public string RunDir { get; set; } = "";
        public string Sha { get; set; } = "";
        public string GitRefLabel { get; set; } = "";
        public string Trigger { get; set; } = "";
        public string Profile { get; set; } = "";
        public string RunId { get; set; } = "";
        public string Repository { get; set; } = "";
        public string SelectionMode { get; set; } = "";
        public string BaselineSha { get; set; } = "";
        public string BaselineReceiptPath { get; set; } = "";
        public string QualificationPath { get; set; } = "";
        public string TimingRoot { get; set; } = "";
        public string TrainManifestPath { get; set; } = "";
        public string ReleaseIntentPath { get; set; } = "";
        public string SelectionAttemptPath { get; set; } = "";
        public string SelectionAttemptDigest { get; set; } = "";
        public string SelectionPath { get; set; } = "";
        public string SelectionDigest { get; set; } = "";
        public int Attempt { get; set; } = 1;
        public string ReleaseRoot { get; set; } = "";
        public string CandidateId { get; set; } = "";
        public string ExpectedSha { get; set; } = "";
        public string[] Suites { get; set; } = new string[0];
        public string SeamsPath { get; set; } = "";
        public string PriorFailuresPath { get; set; } = "";
        public string PriorFailuresDigest { get; set; } = "";
        public bool AcceptCrossCandidateSeed { get; set; }
        public string SeedSourceCandidateId { get; set; } = "";
        public string RcEvidenceRoot { get; set; } = "";
    }

    public static class NightlyRunParts
    {
        private static readonly string[] CreditKinds = { "master-scheduled", "rc-release" };

        // PowerShell -match: case-insensitive.
        private static readonly Regex EolLine = new Regex(@"^i/(\S*)\s+w/(\S*)\s+attr/([^\t]*)\t(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static PSObject Note(params object[] pairs)
        {
            var o = new PSObject();
            for (var i = 0; i < pairs.Length; i += 2) o.Properties.Add(new PSNoteProperty((string)pairs[i], pairs[i + 1]));
            return o;
        }

        // $list -contains $value: case-insensitive.
        private static bool Contains(List<string> list, string value)
        {
            foreach (var item in list) if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// New-NightlyTestsHopArgumentList: the pwsh arguments of the nightly-tests.ps1 hop. The rc profile always reaches
        /// nightly-tests.ps1 through the selection hop; this is the only builder, so a seed witness and the run core share
        /// one list.
        /// </summary>
        public static string[] TestsHopArguments(TestsHopRequest r)
        {
            r = r ?? new TestsHopRequest();
            string S(string value) => value ?? "";
            var a = new List<string>
            {
                "-NoProfile", "-NonInteractive", "-File", S(r.TestsScript), "-RepoRoot", S(r.CheckoutRoot), "-LogRoot", S(r.RunDir), "-Sha", S(r.Sha),
                "-GitRef", S(r.GitRefLabel), "-Trigger", S(r.Trigger), "-Profile", S(r.Profile), "-RunId", S(r.RunId),
            };
            var attempt = r.Attempt.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(r.SelectionAttemptPath))
            {
                a.AddRange(new[]
                {
                    "-Repository", S(r.Repository), "-SelectionMode", S(r.SelectionMode), "-BaselineSha", S(r.BaselineSha),
                    "-BaselineReceiptPath", S(r.BaselineReceiptPath), "-QualificationPath", S(r.QualificationPath), "-TimingRoot", S(r.TimingRoot),
                    "-TrainManifestPath", S(r.TrainManifestPath), "-ReleaseIntentPath", S(r.ReleaseIntentPath),
                    "-SelectionAttemptPath", S(r.SelectionAttemptPath), "-SelectionAttemptDigest", S(r.SelectionAttemptDigest),
                    "-SelectionPath", S(r.SelectionPath), "-SelectionDigest", S(r.SelectionDigest), "-Attempt", attempt,
                    "-ReleaseRoot", S(r.ReleaseRoot), "-CandidateId", S(r.CandidateId),
                });
            }
            if (!string.IsNullOrWhiteSpace(r.TimingRoot) && !Contains(a, "-TimingRoot")) a.AddRange(new[] { "-TimingRoot", r.TimingRoot });
            if (!string.IsNullOrWhiteSpace(r.ExpectedSha)) a.AddRange(new[] { "-ExpectedSha", r.ExpectedSha });
            // if ($Suites ...): a one-element list is only as true as its element.
            var suites = r.Suites ?? new string[0];
            if (suites.Length > 1 || (suites.Length == 1 && !string.IsNullOrEmpty(suites[0]))) a.AddRange(new[] { "-Suites", string.Join(",", suites) });
            if (!string.IsNullOrWhiteSpace(r.SeamsPath)) a.AddRange(new[] { "-SeamsPath", r.SeamsPath });
            if (!string.IsNullOrWhiteSpace(r.RcEvidenceRoot)) a.AddRange(new[] { "-RcEvidenceRoot", r.RcEvidenceRoot });
            if (!string.IsNullOrEmpty(r.PriorFailuresPath)) a.AddRange(new[] { "-PriorFailuresPath", r.PriorFailuresPath, "-PriorFailuresDigest", S(r.PriorFailuresDigest) });
            if (r.AcceptCrossCandidateSeed) a.Add("-AcceptCrossCandidateSeed");
            if (!string.IsNullOrEmpty(r.SeedSourceCandidateId)) a.AddRange(new[] { "-SeedSourceCandidateId", r.SeedSourceCandidateId });
            if (!Contains(a, "-Attempt")) a.AddRange(new[] { "-Attempt", attempt });
            return a.ToArray();
        }

        /// <summary>
        /// Get-NightlyStaleEolPaths: the tracked paths (git ls-files --eol) whose working copy has the wrong line endings
        /// for a text eol=lf or eol=crlf attribute. A quoted path carries C escapes and is counted in skipped, not guessed.
        /// </summary>
        public static string[] StaleEolPaths(string lsFilesEolOutput, ref int skipped)
        {
            var stale = new List<string>();
            foreach (var raw in (lsFilesEolOutput ?? "").Split('\n'))
            {
                var m = EolLine.Match(raw.TrimEnd('\r'));
                if (!m.Success) continue;
                var index = m.Groups[1].Value;
                var work = m.Groups[2].Value;
                var attr = " " + m.Groups[3].Value.Trim() + " ";
                var path = m.Groups[4].Value;
                if (!string.Equals(index, "lf", StringComparison.OrdinalIgnoreCase) || attr.IndexOf(" -text ", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var wantLf = attr.IndexOf(" eol=lf ", StringComparison.OrdinalIgnoreCase) >= 0;
                var wantCrlf = attr.IndexOf(" eol=crlf ", StringComparison.OrdinalIgnoreCase) >= 0;
                bool Is(string value) => string.Equals(work, value, StringComparison.OrdinalIgnoreCase);
                if ((wantLf && (Is("crlf") || Is("mixed"))) || (wantCrlf && (Is("lf") || Is("mixed"))))
                {
                    if (path.StartsWith("\"", StringComparison.Ordinal)) { skipped++; continue; }
                    stale.Add(path);
                }
            }
            return stale.ToArray();
        }

        /// <summary>New-NightlyRunDirectoryName: yyyy-MM-dd-HHmm (UTC) and the first eight characters of the run id.</summary>
        public static string RunDirectoryName(DateTime utc, string runId)
        {
            var id = runId ?? "";
            return utc.ToUniversalTime().ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture) + "-" + (id.Length > 8 ? id.Substring(0, 8) : id);
        }

        /// <summary>Get-NightlyStatePaths: { StateRoot, LastAttempt, LastGreen, Lock, Logs } under the state root.</summary>
        public static PSObject StatePaths(string stateRoot) =>
            Note("StateRoot", stateRoot ?? "", "LastAttempt", ReleaseGateCredit.Join(stateRoot, "last-run.json"),
                "LastGreen", ReleaseGateCredit.Join(stateRoot, "last-complete-green.json"), "Lock", ReleaseGateCredit.Join(stateRoot, "run.lock"),
                "Logs", ReleaseGateCredit.Join(stateRoot, "logs"));

        /// <summary>
        /// Test-NightlyCompleteGreenPredicate (CARD-0599 D-2): whether the state earns the named credit kind (master by
        /// default, so an RC run never backs into master readiness). An unknown kind earns nothing.
        /// </summary>
        public static bool CompleteGreenPredicate(object state, string creditKind)
        {
            if (!LanguagePrimitives.IsTrue(state)) return false;
            var want = (creditKind ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(want)) want = "master-scheduled";
            if (Array.IndexOf(CreditKinds, want) < 0) return false;
            var verdict = ReleaseGateCredit.CreditVerdict(state);
            if (!LanguagePrimitives.IsTrue(PsValue.Prop(verdict, "Ok"))) return false;
            return string.Equals(PsValue.Str(PsValue.Prop(verdict, "CreditKind")), want, StringComparison.OrdinalIgnoreCase);
        }
    }
}
