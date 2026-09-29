using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public sealed class RepositoryCloneSourceTests
{
    [Test]
    [Arguments("git@GitHub.COM:Owner/Repo.git")]
    [Arguments("git@github.com:owner/repo")]
    [Arguments("ssh://git@GitHub.com:22/OWNER/REPO.git")]
    [Arguments("https://github.com/owner/repo")]
    [Arguments("https://GITHUB.com/OWNER/REPO.git/")]
    [Arguments(" https://github.com/owner/repo.git ")]
    public void Normalizes_every_admitted_spelling_to_one_https_identity(string source)
    {
        RepositoryCloneSource.TryNormalize(source, out var identity).ShouldBeTrue();
        identity.ShouldBe("https://github.com/owner/repo.git");
    }

    [Test]
    [Arguments("")]
    [Arguments("owner/repo")]
    [Arguments("http://github.com/owner/repo")]
    [Arguments("https://github.com/owner/../repo")]
    [Arguments("git@github.com:repo")]
    [Arguments("https://x-access-token:secret-https@example.com/owner/repo.git")]
    [Arguments("ssh://git:secret-ssh@example.com/owner/repo.git")]
    public void Refuses_spellings_that_are_not_a_repository(string source) =>
        RepositoryCloneSource.TryNormalize(source, out _).ShouldBeFalse();

    [Test]
    [Arguments("https://github.com/owner/repo.git", "repo")]
    [Arguments("https://github.com/owner/ABC.git", "abc")]
    [Arguments("https://github.com/owner/a-b_c.git", "a-b_c")]
    [Arguments("https://github.com/owner/.git", "")]
    [Arguments("https://github.com/owner/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.git", "")]
    [Arguments("not-a-repository", "")]
    public void Derives_a_directory_name_or_refuses(string source, string expected)
    {
        var derived = RepositoryCloneSource.TryDeriveName(source, out var name);
        derived.ShouldBe(expected.Length > 0);
        if (derived) name.ShouldBe(expected);
    }

    [Test]
    public void Derives_the_last_segment_of_a_local_path()
    {
        var source = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "parent", "secondary"));
        RepositoryCloneSource.TryDeriveName(source, out var name).ShouldBeTrue();
        name.ShouldBe("secondary");
    }

    [Test]
    public void Allow_list_prefix_matching_is_ordinal_and_slash_bounded()
    {
        const string primary = "https://github.com/other/primary.git";
        var prefixes = new[] { "https://github.com/owner/" };
        RepositoryCloneSource.IsAdmitted("https://github.com/owner/repo.git", prefixes, primary).ShouldBeTrue();
        RepositoryCloneSource.IsAdmitted(primary, prefixes, primary).ShouldBeTrue();
        RepositoryCloneSource.IsAdmitted("https://github.com/ownerish/repo.git", prefixes, primary).ShouldBeFalse();
        RepositoryCloneSource.IsAdmitted("https://GITHUB.com/owner/repo.git", prefixes, primary).ShouldBeFalse();
    }
}
