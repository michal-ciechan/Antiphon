// Release tags, candidate ids and remote refs of the release gate (moved from release-gate.ps1). D-4 and D-8: release
// tags are vYYYY.MM.DD.N from the UTC cut date and a positive daily sequence, pre-releases add -rc.K. The credit
// verdict and the candidate-ref check stay in release-gate.ps1: nightly-run.ps1 and release-cut.ps1 run them under
// Windows PowerShell 5.1, which cannot load this core.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    public static class ReleaseGateTags
    {
        // PowerShell -match: case-insensitive.
        private static readonly Regex TagPattern = new Regex(@"^v(?<y>\d{4})\.(?<m>\d{2})\.(?<d>\d{2})\.(?<n>[1-9]\d*)(?:-rc\.(?<k>[1-9]\d*))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // PowerShell -cmatch: case-sensitive.
        private static readonly Regex LongLivedCandidateIdPattern = new Regex(@"^rc-\d{4}-\d{2}-\d{2}-[0-9a-f]{12}$", RegexOptions.CultureInvariant);
        private static readonly Regex FullShaPattern = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.CultureInvariant);

        private static PSObject Note(params object[] pairs)
        {
            var o = new PSObject();
            for (var i = 0; i < pairs.Length; i += 2) o.Properties.Add(new PSNoteProperty((string)pairs[i], pairs[i + 1]));
            return o;
        }

        private static bool FullSha(string sha) => !string.IsNullOrWhiteSpace(sha) && FullShaPattern.IsMatch(sha);

        /// <summary>
        /// Test-ReleaseGateTag: { Ok, Reason, Prerelease, Rc } for a refused tag (empty-tag, tag-shape, tag-date), else
        /// { Ok, Reason, Date (UTC), Sequence, Tag, Prerelease, Rc }.
        /// </summary>
        public static PSObject Tag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return Note("Ok", false, "Reason", "empty-tag", "Prerelease", false, "Rc", 0);
            var m = TagPattern.Match(tag);
            if (!m.Success) return Note("Ok", false, "Reason", "tag-shape", "Prerelease", false, "Rc", 0);
            var y = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
            var mo = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
            var d = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
            var n = int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            var k = 0;
            var prerelease = false;
            if (m.Groups["k"].Success && !string.IsNullOrWhiteSpace(m.Groups["k"].Value))
            {
                k = int.Parse(m.Groups["k"].Value, CultureInfo.InvariantCulture);
                prerelease = true;
            }
            if (mo < 1 || mo > 12 || d < 1 || d > 31) return Note("Ok", false, "Reason", "tag-date", "Prerelease", prerelease, "Rc", k);
            DateTime date;
            try { date = new DateTime(y, mo, d, 0, 0, 0, DateTimeKind.Utc); }
            catch (ArgumentOutOfRangeException) { return Note("Ok", false, "Reason", "tag-date", "Prerelease", prerelease, "Rc", k); }
            return Note("Ok", true, "Reason", "", "Date", date, "Sequence", n, "Tag", tag, "Prerelease", prerelease, "Rc", k);
        }

        /// <summary>New-ReleaseGateTag (D-8): vYYYY.MM.DD.N from the UTC cut date and a positive daily sequence.</summary>
        public static string NewTag(DateTime cutUtc, int sequence)
        {
            if (sequence < 1) throw new NightlyRefusal("release sequence must be positive, got " + sequence.ToString(CultureInfo.InvariantCulture));
            var u = cutUtc.ToUniversalTime();
            return string.Format(CultureInfo.InvariantCulture, "v{0:0000}.{1:00}.{2:00}.{3}", u.Year, u.Month, u.Day, sequence);
        }

        /// <summary>New-ReleaseGatePreReleaseTag (D-4): vYYYY.MM.DD.N-rc.K, K a positive integer.</summary>
        public static string NewPreReleaseTag(DateTime cutUtc, int sequence, int rc)
        {
            if (rc < 1) throw new NightlyRefusal("release rc number must be positive, got " + rc.ToString(CultureInfo.InvariantCulture));
            return NewTag(cutUtc, sequence) + "-rc." + rc.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Test-ReleaseGateLongLivedCandidateId: rc-YYYY-MM-DD- plus 12 lower-case hex digits.</summary>
        public static bool LongLivedCandidateId(string candidateId) =>
            !string.IsNullOrWhiteSpace(candidateId) && LongLivedCandidateIdPattern.IsMatch(candidateId);

        /// <summary>
        /// ConvertFrom-ReleaseGateRemoteRef: { Status, Sha } from an ls-remote result. Patterns can match suffixes, so only
        /// the complete second field is identity. A failed query or an unusable matching record is Failed, never Absent.
        /// </summary>
        public static PSObject RemoteRef(object result, string reference)
        {
            var failed = Note("Status", "Failed", "Sha", "");
            if (result == null) return failed;
            // [int]$Result.ExitCode: a missing exit code converts to 0.
            if ((int)LanguagePrimitives.ConvertTo(PsValue.Prop(result, "ExitCode"), typeof(int), CultureInfo.InvariantCulture) != 0) return failed;
            var matches = new List<string>();
            foreach (var line in PsValue.Str(PsValue.Prop(result, "Output")).Split('\n'))
            {
                var trimmed = line.Trim();
                var fields = Whitespace.Split(trimmed);
                if (fields.Length < 2)
                {
                    if (string.Equals(trimmed, reference ?? "", StringComparison.Ordinal)) return failed;
                    continue;
                }
                if (!string.Equals(fields[1], reference ?? "", StringComparison.Ordinal)) continue;
                if (fields.Length != 2 || !FullSha(fields[0])) return failed;
                matches.Add(fields[0]);
            }
            if (matches.Count > 1) return failed;
            if (matches.Count == 0) return Note("Status", "Absent", "Sha", "");
            return Note("Status", "Found", "Sha", matches[0]);
        }
    }
}
