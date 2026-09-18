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
    public void TryReclaimAfterKill_respects_child_start()
    {
        var dir = Directory.CreateTempSubdirectory("c543-reclaim").FullName;
        try
        {
            var path = Path.Combine(dir, "index.lock");
            var childStart = DateTime.UtcNow;
            File.WriteAllBytes(path, []);
            File.SetLastWriteTimeUtc(path, childStart - TimeSpan.FromSeconds(60));
            GitIndexLock.TryReclaimAfterKill(path, childStart, "commit").ShouldBeFalse();
            File.Exists(path).ShouldBeTrue();

            File.SetLastWriteTimeUtc(path, childStart + TimeSpan.FromSeconds(1));
            GitIndexLock.TryReclaimAfterKill(path, childStart, "commit").ShouldBeTrue();
            File.Exists(path).ShouldBeFalse();

            File.WriteAllBytes(path, []);
            File.SetLastWriteTimeUtc(path, childStart + TimeSpan.FromSeconds(1));
            GitIndexLock.TryReclaimAfterKill(path, childStart, "log").ShouldBeFalse();
            File.Exists(path).ShouldBeTrue();
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
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
