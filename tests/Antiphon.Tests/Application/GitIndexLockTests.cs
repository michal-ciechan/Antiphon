using System.Reflection;
using Antiphon.Server.Application.Dtos;
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
    public void C1436_census_drops_git_that_exits_mid_census()
    {
        // CARD-1436: a git listed by the census that exits before its start time is read is not a holder.
        var gone = new FakeProcess(42, () => throw new InvalidOperationException("No process is associated with this object."),
            () => true);
        var census = GitIndexLock.CensusGitProcesses(new FakeSource(gone));
        census.ShouldBeEmpty();
        gone.Disposed.ShouldBeTrue();
        GitIndexLock.Classify(StaleLock(census), StaleAfter, Now).ShouldBe(GitIndexLock.Kind.Stale);
    }

    [Test]
    public void C1436_census_keeps_live_holders()
    {
        // CARD-1436: a live git started before the lock, and a live git whose start time is unreadable,
        // still hold the lock.
        var mtime = Now - TimeSpan.FromSeconds(600);
        var older = new FakeProcess(7, () => mtime.AddSeconds(-5), () => false);
        var denied = new FakeProcess(8, () => throw new System.ComponentModel.Win32Exception(5),
            () => throw new System.ComponentModel.Win32Exception(5));
        var census = GitIndexLock.CensusGitProcesses(new FakeSource(older, denied));
        census.ShouldBe([(7, mtime.AddSeconds(-5)), (8, (DateTime?)null)]);
        GitIndexLock.Classify(StaleLock([census[0]]), StaleAfter, Now).ShouldBe(GitIndexLock.Kind.Held);
        GitIndexLock.Classify(StaleLock([census[1]]), StaleAfter, Now).ShouldBe(GitIndexLock.Kind.Held);
    }

    private static GitIndexLock.Observation StaleLock(IReadOnlyList<(int Pid, DateTime? StartUtc)> census)
        => new(@"C:\tmp\index.lock", true, Now - TimeSpan.FromSeconds(600), 0, census);

    private sealed class FakeSource(params GitIndexLock.ICensusProcess[] processes) : GitIndexLock.IProcessSource
    {
        public IReadOnlyList<GitIndexLock.ICensusProcess> GitProcesses() => processes;
    }

    private sealed class FakeProcess(int id, Func<DateTime> start, Func<bool> exited) : GitIndexLock.ICensusProcess
    {
        public bool Disposed { get; private set; }
        public int Id => id;
        public DateTime StartTimeUtc => start();
        public bool HasExited => exited();
        public void Dispose() => Disposed = true;
    }

    [Test]
    public void Evaluate_path_error_is_held()
    {
        var observation = new LandingIndexLockObservation(
            @"C:\tmp\.git\index.lock",
            false,
            null,
            null,
            [],
            GitIndexLock.PathErrorReason);
        var (kind, detail) = GitIndexLock.Evaluate(observation, StaleAfter, Now);
        kind.ShouldBe(GitIndexLock.Kind.Held);
        detail.ShouldNotBeNull();
        detail.ShouldContain(GitIndexLock.PathErrorReason);
        detail.ShouldContain(@"C:\tmp\.git\index.lock");
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
