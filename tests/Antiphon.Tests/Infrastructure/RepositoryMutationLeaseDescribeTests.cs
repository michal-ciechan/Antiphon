using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-0535 V-2b: DescribeUnavailableAsync is journal-only and never opens landing.lock.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RepositoryMutationLeaseDescribeTests
{
    [Test]
    [Timeout(30_000)]
    public async Task describe_is_null_on_empty_children_and_names_a_non_json_journal_file(
        CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c535-describe");
        await repo.CommitFileAsync("keep.txt", "seed\n");
        var git = new LandingGit();
        var leases = new RepositoryMutationLease(git);
        var common = await git.CommonDirectoryAsync(repo.Path, ct);
        var children = Path.Combine(common, "antiphon", "children");
        Directory.CreateDirectory(children);

        (await leases.DescribeUnavailableAsync(repo.Path, ct)).ShouldBeNull();
        await using (var acquired = await leases.TryAcquireAsync(repo.Path, ct))
            acquired.ShouldNotBeNull("the probe must not leave landing.lock held");

        await File.WriteAllTextAsync(Path.Combine(children, "not-a-journal.txt"), "residue", ct);
        var described = await leases.DescribeUnavailableAsync(repo.Path, ct);
        described.ShouldNotBeNull();
        described.ShouldContain("children");
        described.ShouldContain("recover-repository-children.ps1");

        File.Delete(Path.Combine(children, "not-a-journal.txt"));
        await using var after = await leases.TryAcquireAsync(repo.Path, ct);
        after.ShouldNotBeNull();
    }
}
