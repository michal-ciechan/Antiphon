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
    public void Matching_spellings_always_share_a_candidate_key_and_kinds_never_collide()
    {
        string[] corpus =
        [
            @"C:\Work\Repo", @"c:/work/repo/", @"C:\work\repo\\", @"C:\", "C:/", "C:", "c:",
            @"\\FileHost\Share\R", @"\/filehost/share/r/", @"\\", @"\\\",
            "/srv/Repo", "/srv/Repo/", "/srv/repo", @"/srv\Repo", @"/srv/Repo\", "/", "//", "///",
            @"alpha\repo", "alpha/repo", "Alpha/Repo/", "//filehost/share/r", @"/\filehost\share\r",
            "/c/work/repo", @"\c\work\repo",
        ];
        foreach (var left in corpus)
        foreach (var right in corpus)
        {
            if (WorkingDirectoryIdentity.Same(left, right))
            {
                WorkingDirectoryIdentity.CandidateKey(left).ShouldBe(
                    WorkingDirectoryIdentity.CandidateKey(right), $"'{left}' vs '{right}'");
            }

            if (WorkingDirectoryIdentity.IsWindowsPath(left) != WorkingDirectoryIdentity.IsWindowsPath(right))
            {
                WorkingDirectoryIdentity.Key(left).ShouldNotBe(
                    WorkingDirectoryIdentity.Key(right), $"'{left}' vs '{right}'");
            }
        }
    }

    [Test]
    public void Candidate_filter_is_a_superset_that_the_exact_rule_refines()
    {
        var stored = new[]
        {
            new Row(@"/srv/alpha\repo"), new Row("/srv/alpha/repo"), new Row("/srv/Alpha/Repo/"),
            new Row(null), new Row("/srv/alpha/repo-other"), new Row(@"\\x\alpha\repo"),
        };

        var filter = WorkingDirectoryIdentity.CandidateFilter<Row>(r => r.Path, "/srv/alpha/repo/").Compile();
        var candidates = stored.Where(filter).Select(r => r.Path).ToList();
        candidates.ShouldBe([@"/srv/alpha\repo", "/srv/alpha/repo", "/srv/Alpha/Repo/"]);
        candidates.Where(p => WorkingDirectoryIdentity.Same(p, "/srv/alpha/repo/"))
            .ShouldBe(["/srv/alpha/repo"]);
    }

    private sealed record Row(string? Path);
}
