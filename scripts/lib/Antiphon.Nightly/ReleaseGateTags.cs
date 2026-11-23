// Release tags, candidate ids and remote refs of the release gate (moved from release-gate.ps1). D-4 and D-8: release
// tags are vYYYY.MM.DD.N from the UTC cut date and a positive daily sequence, pre-releases add -rc.K. The credit
// verdict and the candidate ref are ReleaseGateCredit; the remote reads that use these are ReleaseGateRemote, the
// publication journal ReleaseGateJournal (unit 12 of 2026-11-22-remaining-ps1-to-csharp.md: the records are typed, no
// PowerShell objects). Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>
    /// A parsed release tag (Test-ReleaseGateTag). A refused tag has Ok false and Reason empty-tag, tag-shape or tag-date
    /// (Prerelease and Rc as far as they were read); an accepted one carries the UTC Date, the Sequence N and, for a
    /// pre-release, Rc K.
    /// </summary>
    public sealed class ReleaseGateTag
    {
        /// <summary>The tag is vYYYY.MM.DD.N or vYYYY.MM.DD.N-rc.K with a real date.</summary>
        public bool Ok { get; init; }

        /// <summary>Why a tag was refused; empty when Ok.</summary>
        public string Reason { get; init; } = "";

        /// <summary>The tag's day, midnight UTC.</summary>
        public DateTime Date { get; init; }

        /// <summary>The daily sequence N.</summary>
        public int Sequence { get; init; }

        /// <summary>The tag text as given.</summary>
        public string Tag { get; init; } = "";

        /// <summary>The tag carries -rc.K.</summary>
        public bool Prerelease { get; init; }

        /// <summary>K of a pre-release tag; 0 otherwise.</summary>
        public int Rc { get; init; }
    }

    /// <summary>One remote ref read (ConvertFrom-ReleaseGateRemoteRef): Status Found, Absent or Failed, and the SHA when Found.</summary>
    public sealed record ReleaseGateRef(string Status, string Sha)
    {
        /// <summary>The ref exists and names exactly one full SHA.</summary>
        public const string Found = "Found";

        /// <summary>The query succeeded and named no such ref.</summary>
        public const string Absent = "Absent";

        /// <summary>The query failed, or its answer was ambiguous or unusable. Never treated as absent.</summary>
        public const string Failed = "Failed";
    }

    /// <summary>A pre-release allocation (Get-ReleaseGatePreReleaseAllocation): the daily sequence N and the rc number K.</summary>
    public sealed record ReleaseGateAllocation(int Sequence, int Rc);

    public static class ReleaseGateTags
    {
        // PowerShell -match: case-insensitive.
        private static readonly Regex TagPattern = new Regex(@"^v(?<y>\d{4})\.(?<m>\d{2})\.(?<d>\d{2})\.(?<n>[1-9]\d*)(?:-rc\.(?<k>[1-9]\d*))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // PowerShell -cmatch: case-sensitive.
        private static readonly Regex LongLivedCandidateIdPattern = new Regex(@"^rc-\d{4}-\d{2}-\d{2}-[0-9a-f]{12}$", RegexOptions.CultureInvariant);
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.CultureInvariant);

        /// <summary>Test-ReleaseGateTag: the tag parsed, or refused (empty-tag, tag-shape, tag-date).</summary>
        public static ReleaseGateTag Tag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return new ReleaseGateTag { Reason = "empty-tag" };
            var m = TagPattern.Match(tag);
            if (!m.Success) return new ReleaseGateTag { Reason = "tag-shape" };
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
            if (mo < 1 || mo > 12 || d < 1 || d > 31) return new ReleaseGateTag { Reason = "tag-date", Prerelease = prerelease, Rc = k };
            DateTime date;
            try { date = new DateTime(y, mo, d, 0, 0, 0, DateTimeKind.Utc); }
            catch (ArgumentOutOfRangeException) { return new ReleaseGateTag { Reason = "tag-date", Prerelease = prerelease, Rc = k }; }
            return new ReleaseGateTag { Ok = true, Date = date, Sequence = n, Tag = tag, Prerelease = prerelease, Rc = k };
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

        /// <summary>The canonical spelling of a parsed tag: NewTag of its date and sequence, plus -rc.K for a pre-release.</summary>
        public static string Canonical(ReleaseGateTag parsed)
        {
            if (parsed == null || !parsed.Ok) return "";
            var canonical = NewTag(parsed.Date, parsed.Sequence);
            return parsed.Prerelease ? canonical + "-rc." + parsed.Rc.ToString(CultureInfo.InvariantCulture) : canonical;
        }

        /// <summary>Test-ReleaseGateLongLivedCandidateId: rc-YYYY-MM-DD- plus 12 lower-case hex digits.</summary>
        public static bool LongLivedCandidateId(string candidateId) =>
            !string.IsNullOrWhiteSpace(candidateId) && LongLivedCandidateIdPattern.IsMatch(candidateId);

        /// <summary>
        /// ConvertFrom-ReleaseGateRemoteRef: Status and SHA from an ls-remote result. Patterns can match suffixes, so only
        /// the complete second field is identity (ordinal). A failed query or an unusable matching record is Failed, never
        /// Absent; two matching records are Failed; a malformed record that names the ref alone is Failed.
        /// </summary>
        public static ReleaseGateRef RemoteRef(NightlyGitResult result, string reference)
        {
            var failed = new ReleaseGateRef(ReleaseGateRef.Failed, "");
            if (result == null || result.ExitCode != 0) return failed;
            var matches = new List<string>();
            foreach (var line in (result.Output ?? "").Split('\n'))
            {
                var trimmed = line.Trim();
                var fields = Whitespace.Split(trimmed);
                if (fields.Length < 2)
                {
                    if (string.Equals(trimmed, reference ?? "", StringComparison.Ordinal)) return failed;
                    continue;
                }
                if (!string.Equals(fields[1], reference ?? "", StringComparison.Ordinal)) continue;
                if (fields.Length != 2 || !ReleaseGateCredit.FullSha(fields[0])) return failed;
                matches.Add(fields[0]);
            }
            if (matches.Count > 1) return failed;
            if (matches.Count == 0) return new ReleaseGateRef(ReleaseGateRef.Absent, "");
            return new ReleaseGateRef(ReleaseGateRef.Found, matches[0]);
        }

        /// <summary>
        /// Get-ReleaseGatePreReleaseAllocation (D-4): one open N per UTC day. The next new SHA takes the next K of the
        /// highest open N. A final tag, or a reservation whose finalTag names that N, closes it; the next pre-release then
        /// takes the next free N at K=1. A new UTC day starts at N=1. occupancy is the journal's (tag, finalTag) pairs and
        /// the remote tags (with no finalTag), in that order.
        /// </summary>
        public static ReleaseGateAllocation Allocation(IEnumerable<(string Tag, string FinalTag)> occupancy, DateTime cutUtc)
        {
            var day = cutUtc.ToUniversalTime().ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
            var closed = new List<int>();
            var used = new List<int>();
            var openN = new List<int>();
            var openK = new List<int>();
            foreach (var (tag, finalTag) in occupancy ?? Array.Empty<(string, string)>())
            {
                var parsed = Tag(tag);
                if (parsed.Ok && SameDay(parsed, day))
                {
                    if (!used.Contains(parsed.Sequence)) used.Add(parsed.Sequence);
                    if (parsed.Prerelease)
                    {
                        openN.Add(parsed.Sequence);
                        openK.Add(parsed.Rc);
                    }
                    else if (!closed.Contains(parsed.Sequence)) closed.Add(parsed.Sequence);
                }
                if (string.IsNullOrWhiteSpace(finalTag)) continue;
                var finalParsed = Tag(finalTag);
                if (finalParsed.Ok && !finalParsed.Prerelease && SameDay(finalParsed, day))
                {
                    if (!closed.Contains(finalParsed.Sequence)) closed.Add(finalParsed.Sequence);
                    if (!used.Contains(finalParsed.Sequence)) used.Add(finalParsed.Sequence);
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
            if (bestN >= 1) return new ReleaseGateAllocation(bestN, bestK + 1);
            var next = 1;
            while (used.Contains(next) || closed.Contains(next)) next++;
            return new ReleaseGateAllocation(next, 1);
        }

        internal static bool SameDay(ReleaseGateTag parsed, string day) =>
            parsed.Date.ToUniversalTime().ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) == day;
    }
}
