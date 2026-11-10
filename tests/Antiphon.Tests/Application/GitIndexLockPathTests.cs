using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class GitIndexLockPathTests
{
    [Test]
    public async Task Path_resolves_main_and_linked_worktree()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var main = await fixture.Git.InspectIndexLockAsync(fixture.Repository, CancellationToken.None);
        main.Reason.ShouldBeNull();
        main.Present.ShouldBeFalse();
        main.Path.Replace('/', '\\').EndsWith(@"\.git\index.lock", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();

        var linked = await fixture.Git.InspectIndexLockAsync(fixture.Source, CancellationToken.None);
        linked.Reason.ShouldBeNull();
        linked.Present.ShouldBeFalse();
        linked.Path.Replace('/', '\\').Contains(@"\worktrees\", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
        linked.Path.Replace('/', '\\').EndsWith(@"\index.lock", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();

        File.WriteAllBytes(main.Path, []);
        File.WriteAllBytes(linked.Path, []);
        (await fixture.Git.InspectIndexLockAsync(fixture.Repository, CancellationToken.None)).Present.ShouldBeTrue();
        (await fixture.Git.InspectIndexLockAsync(fixture.Source, CancellationToken.None)).Present.ShouldBeTrue();
    }

    [Test]
    public async Task Missing_checkout_returns_path_error()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var missing = Path.Combine(fixture.Root, "missing-checkout");
        Directory.Exists(missing).ShouldBeFalse();
        var observation = await fixture.Git.InspectIndexLockAsync(missing, CancellationToken.None);
        observation.Reason.ShouldBe(GitIndexLock.PathErrorReason);
        observation.Present.ShouldBeFalse();
    }
}
