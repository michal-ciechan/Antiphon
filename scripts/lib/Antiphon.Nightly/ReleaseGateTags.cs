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

        // ---- the publication journal's reservations (D-4, D-8) ---------------------------------------------------

        // $Row.PSObject.Properties.Name -contains name: a note or adapted property, case-insensitive.
        private static bool Has(object row, string name) => row != null && PSObject.AsPSObject(row).Properties[name] != null;

        /// <summary>Get-ReleaseGateOptionalText: the row's [string] field, "" when the row has no such property.</summary>
        public static string OptionalText(object row, string name) => Has(row, name) ? PsValue.Str(PsValue.Prop(row, name)) : "";

        /// <summary>Get-ReleaseGateOptionalInt: the row's field as [int], 0 when absent or blank.</summary>
        public static int OptionalInt(object row, string name)
        {
            if (!Has(row, name)) return 0;
            var text = PsValue.Str(PsValue.Prop(row, name));
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return (int)LanguagePrimitives.ConvertTo(text, typeof(int), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// New-ReleaseGateReservationEntry: a journal row with the historical keys in order; publishedAt, rc and finalTag
        /// are written only when set, so a later publish of the same row neither drops nor invents them.
        /// </summary>
        public static System.Collections.Specialized.OrderedDictionary ReservationEntry(string candidateId, string sha, string tag, int sequence, string reservedAt,
            bool published, string releaseId, string releaseUrl, string publishedAt, int rc, string finalTag)
        {
            var entry = new System.Collections.Specialized.OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "candidateId", candidateId ?? "" }, { "sha", sha ?? "" }, { "tag", tag ?? "" }, { "sequence", sequence },
                { "reservedAt", reservedAt ?? "" }, { "published", published }, { "releaseId", releaseId ?? "" }, { "releaseUrl", releaseUrl ?? "" },
            };
            if (!string.IsNullOrWhiteSpace(publishedAt)) entry["publishedAt"] = publishedAt;
            if (rc >= 1) entry["rc"] = rc;
            if (!string.IsNullOrWhiteSpace(finalTag)) entry["finalTag"] = finalTag;
            return entry;
        }

        /// <summary>
        /// Get-ReleaseGateReservedTag (D-8): the journal row for this candidate (ignoring case), else for this SHA, else
        /// null. A crash after publication recovers the existing release instead of allocating another N.
        /// </summary>
        public static object ReservedTag(object journal, string candidateId, string sha)
        {
            var rows = PsValue.AsArray(PsValue.Prop(journal, "reservations"));
            foreach (var row in rows)
                if (string.Equals(PsValue.Str(PsValue.Prop(row, "candidateId")), candidateId ?? "", StringComparison.OrdinalIgnoreCase)) return row;
            foreach (var row in rows)
                if (string.Equals(PsValue.Str(PsValue.Prop(row, "sha")), sha ?? "", StringComparison.OrdinalIgnoreCase)) return row;
            return null;
        }

        /// <summary>
        /// Get-ReleaseGatePreReleaseAllocation (D-4): { Sequence, Rc }. One open N per UTC day: the next new SHA takes the
        /// next K of the highest open N. A final tag, or a reservation whose finalTag names that N, closes it; the next
        /// pre-release then takes the next free N at K=1. A new UTC day starts at N=1.
        /// </summary>
        public static PSObject Allocation(object journal, DateTime cutUtc, string[] remoteTags)
        {
            var day = cutUtc.ToUniversalTime().ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
            var closed = new List<int>();
            var used = new List<int>();
            var openN = new List<int>();
            var openK = new List<int>();
            var occupancy = new List<object>(PsValue.AsArray(PsValue.Prop(journal, "reservations")));
            foreach (var remote in remoteTags ?? new string[0]) occupancy.Add(Note("tag", remote));
            foreach (var row in occupancy)
            {
                if (row == null) continue;
                var parsed = Tag(PsValue.Str(PsValue.Prop(row, "tag")));
                if (LanguagePrimitives.IsTrue(PsValue.Prop(parsed, "Ok")) && SameDay(parsed, day))
                {
                    var n = (int)PsValue.Prop(parsed, "Sequence");
                    if (!used.Contains(n)) used.Add(n);
                    if ((bool)PsValue.Prop(parsed, "Prerelease"))
                    {
                        openN.Add(n);
                        openK.Add((int)PsValue.Prop(parsed, "Rc"));
                    }
                    else if (!closed.Contains(n)) closed.Add(n);
                }
                var finalText = OptionalText(row, "finalTag");
                if (!string.IsNullOrWhiteSpace(finalText))
                {
                    var finalParsed = Tag(finalText);
                    if (LanguagePrimitives.IsTrue(PsValue.Prop(finalParsed, "Ok")) && !(bool)PsValue.Prop(finalParsed, "Prerelease") && SameDay(finalParsed, day))
                    {
                        var fn = (int)PsValue.Prop(finalParsed, "Sequence");
                        if (!closed.Contains(fn)) closed.Add(fn);
                        if (!used.Contains(fn)) used.Add(fn);
                    }
                }
            }
            var bestN = 0;
            var bestK = 0;
            for (var i = 0; i < openN.Count; i++)
            {
                if (closed.Contains(openN[i])) continue;
                if (openN[i] > bestN || (openN[i] == bestN && openK[i] > bestK))
                {
                    bestN = openN[i];
                    bestK = openK[i];
                }
            }
            if (bestN >= 1) return Note("Sequence", bestN, "Rc", bestK + 1);
            var next = 1;
            while (used.Contains(next) || closed.Contains(next)) next++;
            return Note("Sequence", next, "Rc", 1);
        }

        private static bool SameDay(PSObject parsed, string day) =>
            ((DateTime)PsValue.Prop(parsed, "Date")).ToUniversalTime().ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) == day;
    }
}