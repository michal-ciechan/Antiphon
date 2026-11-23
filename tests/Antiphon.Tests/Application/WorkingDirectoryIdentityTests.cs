using System.Text;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Review 7a972462 round 1: the one host-independent path identity shared by project readiness
/// and setup's reverse project lookup. PostgreSQL witnesses live in
/// <c>ProjectReadinessTests.PathIdentity.cs</c>.
/// </summary>
[Category("Unit")]
public class WorkingDirectoryIdentityTests
{
    [Test]
    [Arguments(@"C:\work", true)]
    [Arguments("C:/work", true)]
    [Arguments("c:", true)]
    [Arguments(@"z:relative", true)]
    [Arguments(@"\\filehost\share", true)]
    [Arguments(@"\/filehost/share", true)]
    [Arguments("/srv/repo", false)]
    [Arguments("//filehost/share", false)]
    [Arguments(@"/\filehost", false)]
    [Arguments(@"alpha\repo", false)]
    [Arguments(@"\single", false)]
    [Arguments("1:/not-a-drive", false)]
    [Arguments("é:/not-ascii", false)]
    [Arguments("C", false)]
    public void Classifies_windows_spellings_by_drive_or_unc_prefix(string path, bool windows) =>
        WorkingDirectoryIdentity.IsWindowsPath(path).ShouldBe(windows);

    [Test]
    [Arguments(@"C:\Work\Repo\", @"c:\work\repo")]
    [Arguments("C:/Work/Repo//", @"c:\work\repo")]
    [Arguments(@"C:\", @"c:\")]
    [Arguments("C:/", @"c:\")]
    [Arguments(@"C:\\\", @"c:\")]
    [Arguments("C:", "c:")]
    [Arguments(@"\\FileHost\Share\Review\", @"\\filehost\share\review")]
    [Arguments(@"\\filehost\share\", @"\\filehost\share")]
    [Arguments(@"\\", @"\\")]
    [Arguments(@"\\\", @"\\")]
    [Arguments("/srv/Repo/", "/srv/Repo")]
    [Arguments("/srv/Repo///", "/srv/Repo")]
    [Arguments("/", "/")]
    [Arguments("///", "/")]
    [Arguments(@"/srv/alpha\repo\", @"/srv/alpha\repo\")]
    [Arguments("relative/dir/", "relative/dir")]
    [Arguments(" /srv/x ", " /srv/x ")]
    public void Key_drops_trailing_separators_above_the_root_only(string path, string key) =>
        WorkingDirectoryIdentity.Key(path).ShouldBe(key);

    [Test]
    [Arguments(@"C:\work\review\", @"C:\work\review\")]
    [Arguments(@"C:\work\review\", @"C:\work\review")]
    [Arguments(@"C:\Work\Review", "c:/work/REVIEW/")]
    [Arguments(@"\\filehost\share\review\", @"\\filehost\share\review\")]
    [Arguments(@"\\FileHost\Share\Review", @"\/filehost/share/review/")]
    [Arguments(@"C:\", "c:/")]
    [Arguments("/repo/", "/repo")]
    [Arguments("/repo", "/repo/")]
    [Arguments("/", "//")]
    [Arguments(@"/srv/alpha\repo", @"/srv/alpha\repo")]
    public void Same_directory_spellings_match(string left, string right)
    {
        WorkingDirectoryIdentity.Same(left, right).ShouldBeTrue();
        WorkingDirectoryIdentity.Same(right, left).ShouldBeTrue();
    }

    [Test]
    [Arguments(@"/srv/alpha\repo", "/srv/alpha/repo")]
    [Arguments("/srv/Team/repo", "/srv/team/repo")]
    [Arguments(@"alpha\repo", "alpha/repo")]
    [Arguments(@"/srv/alpha\repo\", @"/srv/alpha\repo")]
    [Arguments("/c/repo", @"C:\c\repo")]
    [Arguments("//filehost/share", @"\\filehost\share")]
    [Arguments(@"C:\", "C:")]
    [Arguments(@"C:\work", @"C:\work-other")]
    [Arguments(@"C:\work", @"D:\work")]
    [Arguments("/srv/repo", "/srv/repo/child")]
    [Arguments("/srv/repo", "")]
    [Arguments("   ", "   ")]
    public void Distinct_directories_do_not_match(string left, string right)
    {
        WorkingDirectoryIdentity.Same(left, right).ShouldBeFalse();
        WorkingDirectoryIdentity.Same(right, left).ShouldBeFalse();
    }

    [Test]
    public void Missing_paths_never_match()
    {
        WorkingDirectoryIdentity.Same(null, "/srv/repo").ShouldBeFalse();
        WorkingDirectoryIdentity.Same("/srv/repo", null).ShouldBeFalse();
        WorkingDirectoryIdentity.Same(null, null).ShouldBeFalse();
    }

    [Test]
    public void Matching_spellings_are_always_admitted_by_the_prefilter_and_kinds_never_collide()
    {
        var corpus = new List<string>
        {
            @"C:\Work\Repo", @"c:/work/repo/", @"C:\work\repo\\", @"C:\", "C:/", "C:", "c:",
            @"\\FileHost\Share\R", @"\/filehost/share/r/", @"\\", @"\\\",
            "/srv/Repo", "/srv/Repo/", "/srv/repo", @"/srv\Repo", @"/srv/Repo\", "/", "//", "///",
            @"alpha\repo", "alpha/repo", "Alpha/Repo/", "//filehost/share/r", @"/\filehost\share\r",
            "/c/work/repo", @"\c\work\repo", "/srv/100%_done!", "/srv/100%_DONE!/", @"C:\a_b%!", "c:/A_B%!/",
        };
        corpus.AddRange(ProjectReadinessTests.CaseFoldCorpus("0123abcd"));
        var accepted = 0;
        foreach (var left in corpus)
        foreach (var right in corpus)
        {
            if (WorkingDirectoryIdentity.Same(left, right))
            {
                accepted++;
                CLocalePrefilterModel.Admits(left, WorkingDirectoryIdentity.CandidatePattern(right))
                    .ShouldBeTrue($"stored '{left}' probe '{right}'");
            }

            if (WorkingDirectoryIdentity.IsWindowsPath(left) != WorkingDirectoryIdentity.IsWindowsPath(right))
            {
                WorkingDirectoryIdentity.Key(left).ShouldNotBe(
                    WorkingDirectoryIdentity.Key(right), $"'{left}' vs '{right}'");
            }
        }

        accepted.ShouldBeGreaterThan(corpus.Count, "the corpus must contain case-variant pairs, not only self-pairs");
    }

    /// <summary>
    /// Review 0d322ed5 round 2, exhaustively over single code points: every pair of Unicode scalar
    /// values that invariant lower-casing makes one Windows directory is admitted by the prefilter
    /// as PostgreSQL evaluates it under the C collation, in both directions.
    /// </summary>
    [Test]
    public void Every_invariant_case_fold_pair_of_code_points_is_admitted()
    {
        // Group every scalar value that lower-cases to something else under its lower case, then add
        // the lower case itself when it is its own lower case.
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var value in ScalarValues())
        {
            var text = char.ConvertFromUtf32(value);
            var lower = text.ToLowerInvariant();
            if (string.Equals(lower, text, StringComparison.Ordinal))
                continue;
            if (!groups.TryGetValue(lower, out var members))
                groups[lower] = members = [];
            members.Add(text);
        }

        foreach (var (lower, members) in groups)
        {
            if (string.Equals(lower.ToLowerInvariant(), lower, StringComparison.Ordinal))
                members.Add(lower);
        }

        var pairs = 0;
        foreach (var members in groups.Values)
        {
            foreach (var left in members)
            foreach (var right in members)
            {
                var stored = $@"C:\fold\{left}x";
                var probe = $"c:/FOLD/{right}x/";
                WorkingDirectoryIdentity.Same(stored, probe).ShouldBeTrue($"U+{char.ConvertToUtf32(left, 0):X4} vs U+{char.ConvertToUtf32(right, 0):X4}");
                CLocalePrefilterModel.Admits(stored, WorkingDirectoryIdentity.CandidatePattern(probe))
                    .ShouldBeTrue($"U+{char.ConvertToUtf32(left, 0):X4} stored, U+{char.ConvertToUtf32(right, 0):X4} probe");
                pairs++;
            }
        }

        pairs.ShouldBeGreaterThan(2_000, "the sweep must cover the Unicode case pairs, not only ASCII");
    }

    /// <summary>
    /// The lemma the prefilter's wildcard rests on: invariant lower-casing maps every Unicode scalar
    /// value to exactly one scalar value of the same UTF-16 length, and
    /// <see cref="WorkingDirectoryIdentity.CaseAmbiguousCodePoints"/> is exactly the set of members of
    /// the invariant case classes (computed here with <c>string.ToLowerInvariant</c>) whose members
    /// differ after ASCII-only lower-casing. Review f14ac37a round 3: code points without case
    /// variants (CJK, digits) must stay literal.
    /// </summary>
    [Test]
    public void Invariant_lower_case_maps_each_code_point_to_one_code_point()
    {
        var classes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var violations = new List<string>();
        foreach (var value in ScalarValues())
        {
            var text = char.ConvertFromUtf32(value);
            var lower = text.ToLowerInvariant();
            if (lower.Length != text.Length
                || Rune.DecodeFromUtf16(lower, out _, out var consumed) != System.Buffers.OperationStatus.Done
                || consumed != lower.Length)
            {
                violations.Add($"U+{value:X4}");
                continue;
            }

            if (!classes.TryGetValue(lower, out var members))
                classes[lower] = members = [];
            members.Add(text);
        }

        violations.ShouldBeEmpty();
        static string AsciiLower(string text) =>
            text.Length == 1 && text[0] is >= 'A' and <= 'Z' ? ((char)(text[0] + 32)).ToString() : text;
        var expected = classes.Values
            .Where(members => members.Select(AsciiLower).Distinct(StringComparer.Ordinal).Skip(1).Any())
            .SelectMany(members => members)
            .Select(member => char.ConvertToUtf32(member, 0))
            .Order()
            .ToList();
        WorkingDirectoryIdentity.CaseAmbiguousCodePoints.Order().ShouldBe(expected);
        foreach (var cased in new[] { 'k', 'K', '\u212A', '\u00E9', '\u00C9', '\u00E5', '\u212B', '\u03C3', '\u03A3', '\u00DF', '\u1E9E' })
            WorkingDirectoryIdentity.CaseAmbiguousCodePoints.ShouldContain((int)cased, $"U+{(int)cased:X4} has a case variant the C collation cannot fold");
        foreach (var plain in new[] { 0x652F, 0x4ED8, 0x7EC4, 0x20000, '0', '9', '/', '_', 'a', 'A', 'z' })
            WorkingDirectoryIdentity.CaseAmbiguousCodePoints.ShouldNotContain(plain, $"U+{plain:X4} must stay literal");
    }

    [Test]
    [Arguments("/srv/repo/", "/srv/repo")]
    [Arguments(@"C:\Work\Repo\", "c:/wor_/repo")]
    [Arguments("/srv/100%_done!/", "/srv/100!%!_done!!")]
    [Arguments(@"\\host\a_b", "//host/a!_b")]
    [Arguments("C:/CAFÉ/", "c:/caf_")]
    [Arguments("/srv/Über", "/srv/_ber")]
    [Arguments("C:/\U00010400x", "c:/_x")]
    [Arguments("C:/\u212Aelp/kelp", "c:/_elp/_elp")]
    [Arguments("C:/Repos/\u652F\u4ED8\u670D\u52A1\u7EC4/", "c:/repos/\u652F\u4ED8\u670D\u52A1\u7EC4")]
    [Arguments("/srv/\u65E5\u672C_%!", "/srv/\u65E5\u672C!_!%!!")]
    [Arguments("C:/\U00020000x/\u00E9\u7EC4", "c:/\U00020000x/_\u7EC4")]
    [Arguments("C:/stra\u00DFe", "c:/stra_e")]
    public void Candidate_pattern_lowers_ascii_and_wildcards_unfoldable_code_points(string path, string pattern) =>
        WorkingDirectoryIdentity.CandidatePattern(path).ShouldBe(pattern);

    [Test]
    [Arguments("/srv/c-1/Team/repo", "/srv/c-1/team/repo", true)]
    [Arguments("/srv/c-1/team/repo", "/srv/c-1/team/repo-other", false)]
    [Arguments("/srv/c-1/team/repo", "/srv/c-1/team/rep", false)]
    [Arguments("/srv/c-1/a_b", "/srv/c-1/axb", false)]
    [Arguments("C:/caf\u00E9", "c:/cafe", true)]
    [Arguments("C:/caf\u00E9", "c:/cafx", true)]
    [Arguments("C:/caf\u00E9", "c:/cafe-other", false)]
    [Arguments("C:/\U00010400", "c:/\u00E9", true)]
    [Arguments("C:/\U00010400", "c:/ab", false)]
    [Arguments("C:/repos/\u652F\u4ED8\u670D\u52A1\u7EC4", "c:/repos/00000", false)]
    [Arguments("C:/repos/\u652F\u4ED8\u670D\u52A1\u7EC4", "c:/REPOS/\u652F\u4ED8\u670D\u52A1\u7EC4/", true)]
    [Arguments("C:/repos/\u00C9\u652F", "c:/repos/00", false)]
    [Arguments("C:/repos/\u00C9\u652F", "c:/repos/\u00E9\u652F", true)]
    [Arguments("c:/\U00020000x/", "C:\\\U00020000x", true)]
    [Arguments("C:/\U00020000x", "c:/\U00020000X/", true)]
    [Arguments("C:/\U00020000x", "c:/\U00020001x", false)]
    [Arguments("C:/\U00020001x", "c:/\U00020000x", false)]
    [Arguments("C:/\U00020000\U00010400", "c:/\U00020000\U00010428", true)]
    [Arguments("C:/\U00010400\U00020000", "c:/\U00010428\U00020001", false)]
    public void Prefilter_stays_bounded_to_spellings_of_equal_length_and_ascii_text(string probe, string stored, bool admitted) =>
        CLocalePrefilterModel.Admits(stored, WorkingDirectoryIdentity.CandidatePattern(probe)).ShouldBe(admitted);

    private static IEnumerable<int> ScalarValues()
    {
        for (var value = 0; value <= 0x10FFFF; value++)
        {
            if (value is >= 0xD800 and <= 0xDFFF)
                continue;
            yield return value;
        }
    }
}

/// <summary>
/// In-process model of the SQL prefilter as PostgreSQL evaluates it:
/// <c>lower(rtrim(replace(stored, '\', '/'), '/') COLLATE "C") LIKE pattern ESCAPE '!'</c>. Under the
/// C collation <c>lower</c> folds only A-Z and <c>_</c> matches exactly one code point. It covers the
/// patterns <see cref="WorkingDirectoryIdentity.CandidatePattern"/> emits (literals, <c>_</c> and
/// <c>!</c> escapes, never a <c>%</c> wildcard). The SQL also carries the untrimmed prefix guard
/// <c>LIKE pattern || %</c>, which this trimmed match implies, so the model leaves it out; the
/// PostgreSQL witnesses assert that the database returns exactly the rows this model admits, which
/// is also the evidence that the guard drops nothing.
/// </summary>
internal static class CLocalePrefilterModel
{
    public static bool Admits(string stored, string pattern)
    {
        var text = stored.Replace('\\', '/').TrimEnd('/').EnumerateRunes()
            .Select(rune => rune.Value is >= 'A' and <= 'Z' ? rune.Value + 32 : rune.Value)
            .ToArray();
        // Review 5de0f5a5 round 4: walk the pattern by Unicode scalar value, as PostgreSQL does in a
        // UTF-8 database, so a supplementary-plane literal (two UTF-16 units) is one token.
        var tokens = pattern.EnumerateRunes().Select(rune => rune.Value).ToArray();
        var position = 0;
        for (var index = 0; index < tokens.Length; index++)
        {
            if (position >= text.Length)
                return false;
            var token = tokens[index];
            if (token == '%')
                throw new InvalidOperationException("the model covers no % wildcard");
            if (token == '_')
            {
                position++;
                continue;
            }

            if (token == WorkingDirectoryIdentity.CandidatePatternEscape[0])
                token = tokens[++index];
            if (text[position] != token)
                return false;
            position++;
        }

        return position == text.Length;
    }
}
