using System.Reflection;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class GitIndexLockTests
{
    private static readonly DateTime Now = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(GitIndexLock.DefaultStaleAfterSeconds);

    [Test]
    [Arguments(false, 0, "none", GitIndexLock.Kind.None)]
    [Arguments(true, 10, "none", GitIndexLock.Kind.Held)]
    [Arguments(true, 600, "before", GitIndexLock.Kind.Held)]
    [Arguments(true, 600, "after", GitIndexLock.Kind.Stale)]
    [Arguments(true, 600, "unreadable", GitIndexLock.Kind.Held)]
    [Arguments(true, 300, "none", GitIndexLock.Kind.Stale)]
    [Arguments(true, 299, "none", GitIndexLock.Kind.Held)]
    public void Classify_matrix(bool present, int ageSeconds, string census, GitIndexLock.Kind expected)
    {
        DateTime? mtime = present ? Now - TimeSpan.FromSeconds(ageSeconds) : null;
        IReadOnlyList<(int Pid, DateTime? StartUtc)> holders = census switch
        {
            "before" => [(42, mtime!.Value.AddSeconds(-5))],
            "after" => [(42, mtime!.Value.AddSeconds(5))],
            "unreadable" => [(42, null)],
            _ => [],
        };
        var observation = new GitIndexLock.Observation(
            present ? @"C:\tmp\index.lock" : "", present, mtime, present ? 0 : null, holders);
        GitIndexLock.Classify(observation, StaleAfter, Now).ShouldBe(expected);
    }

    [Test]
    public void TryReclaimAfterKill_is_gone()
    {
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        typeof(GitIndexLock).GetMethod("TryReclaimAfterKill", any).ShouldBeNull();
        typeof(GitIndexLock).GetMethod("IsLockTakingVerb", any).ShouldBeNull();
        typeof(GitIndexLock).GetMethod("FirstVerb", any).ShouldBeNull();
    }
}

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
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
}
