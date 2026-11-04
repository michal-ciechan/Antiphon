using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1704 S1 (SC-1..SC-7, D-6/D-7): a session lives only inside an outer operation scope, a nested scope borrows
/// it, it is disposed before the repository lease is released, and S1 production attaches none.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
[NotInParallel("C1704Sessions")]
public sealed class C1704SessionScopeTests
{
    /// <summary>SC-1 (G-12): with no test hook, no backend value attaches a session in S1.</summary>
    [Test]
    [Timeout(30_000)]
    [Arguments("Cli")]
    [Arguments("LibGit2")]
    [Arguments("Compare")]
    public async Task S1_scopes_carry_no_session(string backend, CancellationToken ct)
    {
        using var settings = C1704Settings.Build($"\"GitBackend\": \"{backend}\"");
        var git = new RecordingGit(settings.Backend);
        using (var scope = git.BeginOperationScope())
        {
            git.CurrentSession.ShouldBeNull(backend);
            git.ScopeBackend.ShouldBe(LandGitBackendNames.Cli, "S1 resolves every value to the CLI");
            (await git.ReadCommitAsync(C1704Reads.Repo, "HEAD", ct)).ShouldNotBeNull();
            git.Calls.Count.ShouldBe(1, "the read ran the CLI");
        }
        git.CurrentSession.ShouldBeNull();
    }

    /// <summary>SC-2 (G-13): a nested scope borrows the session; only the outer scope disposes it.</summary>
    [Test]
    [Timeout(15_000)]
    public async Task Nested_scope_borrows_and_only_the_outer_disposes(CancellationToken ct)
    {
        var log = new List<string>();
        var count = 0;
        var git = new RecordingGit { SessionFactory = () => RecordingSession.Create(log, ref count) };
        var outer = git.BeginOperationScope();
        var session = git.CurrentSession.ShouldBeOfType<RecordingSession>();
        using (git.BeginOperationScope())
        {
            git.CurrentSession.ShouldBeSameAs(session, "the inner scope borrows");
        }
        session.Disposed.ShouldBeFalse("only the outer scope disposes");
        git.CurrentSession.ShouldBeSameAs(session);
        outer.Dispose();
        session.Disposed.ShouldBeTrue();
        git.CurrentSession.ShouldBeNull();
        count.ShouldBe(1);
        await Task.CompletedTask;
    }

    /// <summary>SC-3 (G-14): the lease scope disposes the session before it releases the lease.</summary>
    [Test]
    [Timeout(15_000)]
    public async Task Release_disposes_the_session_before_the_lease(CancellationToken ct)
    {
        var log = new List<string>();
        var count = 0;
        var git = new RecordingGit { SessionFactory = () => RecordingSession.Create(log, ref count) };
        var leases = new OrderedLeases(log);
        var ownership = new LandRepositoryLeaseScope(leases, git, "/c1704/repo", new RepositoryLeaseOwnerTag(null, "c1704"),
            new OrderedLease(log));
        git.CurrentSession.ShouldNotBeNull();
        await ownership.ReleaseAsync();
        log.Where(e => e is "session" or "lease").ShouldBe(["session", "lease"]);
        await ownership.DisposeAsync();
    }

    /// <summary>SC-4 (G-15): a new lease episode opens a fresh session after the first is disposed.</summary>
    [Test]
    [Timeout(15_000)]
    public async Task Reacquire_and_BeginReadScope_open_a_fresh_session(CancellationToken ct)
    {
        var log = new List<string>();
        var count = 0;
        var git = new RecordingGit { SessionFactory = () => RecordingSession.Create(log, ref count) };
        var ownership = new LandRepositoryLeaseScope(new OrderedLeases(log), git, "/c1704/repo",
            new RepositoryLeaseOwnerTag(null, "c1704"), new OrderedLease(log));
        var first = git.CurrentSession.ShouldBeOfType<RecordingSession>();
        await ownership.ReleaseAsync();
        await ownership.ReacquireAsync(ct);
        ownership.BeginReadScope();
        var second = git.CurrentSession.ShouldBeOfType<RecordingSession>();
        second.ShouldNotBeSameAs(first);
        count.ShouldBe(2);
        log.IndexOf("dispose#1").ShouldBeLessThan(log.IndexOf("create#2"), string.Join(",", log));
        await ownership.DisposeAsync();
        second.Disposed.ShouldBeTrue();
    }

    /// <summary>SC-5 (G-16): no session outlives the lease scope, including after a failed reacquire.</summary>
    [Test]
    [Timeout(15_000)]
    public async Task No_session_outlives_the_lease_scope(CancellationToken ct)
    {
        var live = RecordingSession.Live;
        var log = new List<string>();
        var count = 0;
        var git = new RecordingGit { SessionFactory = () => RecordingSession.Create(log, ref count) };
        await using (var ownership = new LandRepositoryLeaseScope(new OrderedLeases(log), git, "/c1704/repo",
            new RepositoryLeaseOwnerTag(null, "c1704"), new OrderedLease(log)))
        {
            await ownership.ReleaseAsync();
            await ownership.ReacquireAsync(ct);
            ownership.BeginReadScope();
            RecordingSession.Live.ShouldBe(live + 1);
        }
        RecordingSession.Live.ShouldBe(live, "a cycle of release, reacquire and read scope leaves no session");

        var failing = new OrderedLeases(log) { Fail = true };
        var thrown = false;
        var broken = new LandRepositoryLeaseScope(failing, git, "/c1704/repo", new RepositoryLeaseOwnerTag(null, "c1704"),
            new OrderedLease(log));
        try
        {
            RecordingSession.Live.ShouldBe(live + 1);
            await broken.ReleaseAsync();
            await broken.ReacquireAsync(ct);
        }
        catch (InvalidOperationException) { thrown = true; }
        finally { await broken.DisposeAsync(); }
        thrown.ShouldBeTrue();
        RecordingSession.Live.ShouldBe(live, "a cycle that ended in an exception leaves no session");

        // A scope whose lease is already gone still disposes its session.
        var leaseless = new LandRepositoryLeaseScope(new OrderedLeases(log), git, "/c1704/repo",
            new RepositoryLeaseOwnerTag(null, "c1704"), null!);
        RecordingSession.Live.ShouldBe(live + 1);
        await leaseless.DisposeAsync();
        RecordingSession.Live.ShouldBe(live, "disposal without a held lease still disposes the session");
        git.CurrentSession.ShouldBeNull();
    }

    /// <summary>SC-6: outside a scope there is no session and typed reads run the CLI.</summary>
    [Test]
    [Timeout(15_000)]
    public async Task Sessions_exist_only_inside_a_scope(CancellationToken ct)
    {
        var log = new List<string>();
        var count = 0;
        var git = new RecordingGit { SessionFactory = () => RecordingSession.Create(log, ref count) };
        git.CurrentSession.ShouldBeNull();
        var trace = await C1704Reads.TraceAllAsync(git, () => git.Calls);
        trace.Count.ShouldBe(C1704Reads.Methods.Length);
        count.ShouldBe(0, "no scope, no session");
    }

    /// <summary>SC-7 (G-17): an attached CLI session answers nothing, so every typed read issues today's argv.</summary>
    [Test]
    [Timeout(15_000)]
    public async Task A_cli_session_answers_nothing(CancellationToken ct)
    {
        var plain = new RecordingGit();
        var expected = await C1704Reads.TraceAllAsync(plain, () => plain.Calls);
        var observer = new RecordingObserver();
        var git = new RecordingGit(observer: observer) { SessionFactory = () => new CliGitSession() };
        using (git.BeginOperationScope())
        {
            git.CurrentSession.ShouldBeOfType<CliGitSession>();
            var actual = await C1704Reads.TraceAllAsync(git, () => git.Calls);
            actual.ShouldBe(expected);
        }
        expected.ShouldBe(C1704Reads.Methods.Select(m => string.Join(' ', C1704Reads.ExpectedArgv(m))).ToList());
        observer.Records.ShouldBeEmpty("a CLI session answers nothing in process");
    }

    private sealed class OrderedLease(List<string> log) : RepositoryLease
    {
        public override string CommonDirectory => "/c1704/repo/.git";
        public override ValueTask DisposeAsync()
        {
            lock (log) log.Add("lease");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderedLeases(List<string> log) : IRepositoryMutationLease
    {
        public bool Fail { get; init; }
        public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) =>
            Fail ? throw new InvalidOperationException("c1704_acquire_failed") : Task.FromResult<RepositoryLease?>(new OrderedLease(log));
        public bool Owns(RepositoryLease lease, string commonDirectory) => true;
    }
}
