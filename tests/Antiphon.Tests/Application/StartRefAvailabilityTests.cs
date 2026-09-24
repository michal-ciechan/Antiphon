using System.Diagnostics;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.TestHelpers.StartRefGit;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0666. A caller start ref is made available at CREATE: a full SHA only origin has is fetched
/// there, through the journaled owned-child path, and every other outcome is a distinct, named
/// refusal. Origins are a local bare repository, a missing path, or a loopback listener that never
/// answers, so no test needs a network or a particular OS.
/// </summary>
[Category("GitIntegration")]
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class StartRefAvailabilityTests
{
    private static StartRefAvailability Build(TimeSpan? fetchTimeout = null, IRepositoryMutationLease? leases = null) =>
        new(new LandingGit(), leases ?? new RepositoryMutationLease(new LandingGit()),
            NullLogger<StartRefAvailability>.Instance, fetchTimeout);

    [Test]
    public async Task A_local_commit_is_accepted_without_contacting_origin()
    {
        await SkipIfGitUnavailableAsync();
        var root = NewRoot();
        await using var silent = new SilentOrigin();
        try
        {
            var (repo, _, _) = await CreateRepoAsync(root, withOrigin: false);
            await GitAsync(repo, "remote", "add", "origin", silent.Url);
            var head = (await GitAsync(repo, "rev-parse", "HEAD")).Trim();

            await Build(TimeSpan.FromSeconds(5)).EnsureAvailableAsync(repo, head, CancellationToken.None);

            silent.Accepted.ShouldBe(0, "a start ref already in the repository must not reach origin");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Test]
    public async Task A_full_sha_only_on_origin_is_fetched_at_create()
    {
        await SkipIfGitUnavailableAsync();
        var root = NewRoot();
        try
        {
            var (repo, _, origin) = await CreateRepoAsync(root);
            var sha = await PushOriginOnlyCommitAsync(root, origin);
            (await ResolvesAsync(repo, sha)).ShouldBeFalse("precondition: the start SHA must be missing locally");

            await Build().EnsureAvailableAsync(repo, sha, CancellationToken.None);

            (await ResolvesAsync(repo, sha)).ShouldBeTrue("create must leave the start commit local for dispatch");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Test]
    [Arguments("short-sha")]
    [Arguments("branch-name")]
    public async Task A_short_sha_or_name_missing_locally_is_refused_without_a_fetch(string shape)
    {
        await SkipIfGitUnavailableAsync();
        var root = NewRoot();
        await using var silent = new SilentOrigin();
        try
        {
            var (repo, _, origin) = await CreateRepoAsync(root);
            var sha = await PushOriginOnlyCommitAsync(root, origin);
            await GitAsync(repo, "remote", "set-url", "origin", silent.Url);
            var selector = shape == "short-sha" ? sha[..12] : "feat/card-task-0badc0de";

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                Build(TimeSpan.FromSeconds(5)).EnsureAvailableAsync(repo, selector, CancellationToken.None));

            ex.Code.ShouldBe(StartRefAvailability.NotFullShaCode);
            ex.StatusCode.ShouldBe(422);
            ex.Message.ShouldContain($"'{selector}'");
            ex.Message.ShouldContain("full 40- or 64-hex");
            silent.Accepted.ShouldBe(0, "only a full SHA is ever fetched");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Test]
    public async Task A_full_sha_naming_a_blob_is_refused_as_not_a_commit()
    {
        await SkipIfGitUnavailableAsync();
        var root = NewRoot();
        try
        {
            var (repo, _, _) = await CreateRepoAsync(root);
            var blob = (await GitAsync(repo, "hash-object", "-w", "README.md")).Trim();

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                Build().EnsureAvailableAsync(repo, blob, CancellationToken.None));

            ex.Code.ShouldBe(StartRefAvailability.NotCommitCode);
            ex.Message.ShouldContain(blob);
            ex.Message.ShouldContain("not a commit");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Test]
    public async Task A_missing_sha_in_a_repository_without_origin_is_refused_as_no_origin()
    {
        await SkipIfGitUnavailableAsync();
        var root = NewRoot();
        try
        {
            var (repo, _, _) = await CreateRepoAsync(root, withOrigin: false);
            const string missing = "0123456789abcdef0123456789abcdef01234567";

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                Build().EnsureAvailableAsync(repo, missing, CancellationToken.None));

            ex.Code.ShouldBe(StartRefAvailability.NoOriginCode);
            ex.Message.ShouldContain(missing);
            ex.Message.ShouldContain("no 'origin' remote");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Test]
    public async Task A_sha_origin_does_not_have_is_refused_as_absent_on_origin()
    {
        await SkipIfGitUnavailableAsync();
        var root = NewRoot();
        try
        {
            var (repo, _, _) = await CreateRepoAsync(root);
            const string missing = "0123456789abcdef0123456789abcdef01234567";

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                Build().EnsureAvailableAsync(repo, missing, CancellationToken.None));

            ex.Code.ShouldBe(StartRefAvailability.NotOnOriginCode);
            ex.StatusCode.ShouldBe(422);
            ex.Message.ShouldContain(missing);
            ex.Message.ShouldContain("origin does not have it");
            ex.Message.ShouldNotContain("One or more validation errors occurred");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Test]
    public async Task An_unreachable_origin_is_refused_as_transport_not_as_absent()
    {
        await SkipIfGitUnavailableAsync();
        var root = NewRoot();
        try
        {
            var (repo, _, _) = await CreateRepoAsync(root, withOrigin: false);
            await GitAsync(repo, "remote", "add", "origin", Path.Combine(root, "no-such-origin.git"));
            const string missing = "0123456789abcdef0123456789abcdef01234567";

            var ex = await Should.ThrowAsync<ServiceUnavailableException>(() =>
                Build().EnsureAvailableAsync(repo, missing, CancellationToken.None));

            ex.Code.ShouldBe(StartRefAvailability.FetchFailedCode);
            ex.StatusCode.ShouldBe(503);
            ex.Message.ShouldContain(missing);
            ex.Message.ShouldContain("could not be reached or refused authentication");
            ex.Message.ShouldNotContain("does not have it");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task A_silent_origin_is_refused_as_timeout_within_the_budget(CancellationToken testCt)
    {
        await SkipIfGitUnavailableAsync();
        var root = NewRoot();
        await using var silent = new SilentOrigin();
        try
        {
            var (repo, _, _) = await CreateRepoAsync(root, withOrigin: false);
            await GitAsync(repo, "remote", "add", "origin", silent.Url);
            const string missing = "0123456789abcdef0123456789abcdef01234567";
            var clock = Stopwatch.StartNew();

            var ex = await Should.ThrowAsync<ServiceUnavailableException>(() =>
                Build(TimeSpan.FromSeconds(2)).EnsureAvailableAsync(repo, missing, testCt));

            ex.Code.ShouldBe(StartRefAvailability.FetchTimeoutCode);
            ex.Message.ShouldContain(missing);
            ex.Message.ShouldContain("did not finish within 2s");
            silent.Accepted.ShouldBeGreaterThan(0, "the fetch must actually have tried origin");
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30), "the fetch budget, not git, bounds create");
            Directory.EnumerateFileSystemEntries(await ChildJournalDirectoryAsync(repo)).ShouldBeEmpty(
                "the killed fetch child's journal is removed once its process has exited");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task The_create_fetch_is_journaled_and_holds_the_repository_lease(CancellationToken testCt)
    {
        await SkipIfGitUnavailableAsync();
        var root = NewRoot();
        await using var silent = new SilentOrigin();
        try
        {
            var (repo, _, _) = await CreateRepoAsync(root, withOrigin: false);
            await GitAsync(repo, "remote", "add", "origin", silent.Url);
            const string missing = "0123456789abcdef0123456789abcdef01234567";
            var leases = new RepositoryMutationLease(new LandingGit());
            var journal = await ChildJournalDirectoryAsync(repo);

            var create = Build(TimeSpan.FromSeconds(8), leases).EnsureAvailableAsync(repo, missing, testCt);
            await silent.WaitForConnectionAsync(TimeSpan.FromSeconds(6));
            silent.Accepted.ShouldBeGreaterThan(0, "precondition: the create fetch is in flight");

            // In flight: the fetch child is journaled like every repository-mutating git child...
            Directory.EnumerateFiles(journal, "*.json").ShouldNotBeEmpty();
            // ...it runs under the repository mutation lease, owned for the start-ref fetch...
            var owner = await leases.FindOwnerAsync(repo, testCt);
            owner.ShouldNotBeNull();
            owner.State.ShouldBe(RepositoryLeaseOwnerState.Known);
            owner.Purpose.ShouldBe(RepositoryLeasePurposes.StartRefFetch);
            // ...and a dispatch acquisition answers at once instead of waiting out the fetch: the
            // lease is held, so the dispatcher holds the task (HeldOnLease) and moves on.
            var clock = Stopwatch.StartNew();
            var during = await leases.TryAcquireAsync(repo,
                new RepositoryLeaseOwnerTag(Guid.NewGuid(), RepositoryLeasePurposes.Dispatch), testCt);
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
            during.ShouldBeNull();
            create.IsCompleted.ShouldBeFalse("precondition: the assertions above ran while the fetch was in flight");

            (await Should.ThrowAsync<ServiceUnavailableException>(() => create)).Code
                .ShouldBe(StartRefAvailability.FetchTimeoutCode);
            Directory.EnumerateFileSystemEntries(journal).ShouldBeEmpty();
            await using var after = await leases.TryAcquireAsync(repo,
                new RepositoryLeaseOwnerTag(Guid.NewGuid(), RepositoryLeasePurposes.Dispatch), testCt);
            after.ShouldNotBeNull("once the create fetch has ended, dispatch admission is open again");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task A_slow_fetch_leaves_the_origin_probe_only_the_remaining_budget(CancellationToken testCt)
    {
        await SkipIfGitUnavailableAsync();
        var root = NewRoot();
        try
        {
            var (repo, _, _) = await CreateRepoAsync(root);
            const string missing = "0123456789abcdef0123456789abcdef01234567";
            var git = new SlowFetchGit(TimeSpan.FromSeconds(5));

            var ex = await Should.ThrowAsync<ServiceUnavailableException>(() =>
                new StartRefAvailability(git, new RepositoryMutationLease(new LandingGit()),
                        NullLogger<StartRefAvailability>.Instance, TimeSpan.FromSeconds(6))
                    .EnsureAvailableAsync(repo, missing, testCt));

            ex.Code.ShouldBe(StartRefAvailability.FetchTimeoutCode);
            git.ProbeAllowed.ShouldNotBeNull("precondition: the failed fetch was followed by the origin probe");
            // One 6s deadline covers all network work: a 5s fetch leaves the probe about 1s, not a fresh 6s.
            git.ProbeAllowed.Value.ShouldBeLessThan(TimeSpan.FromSeconds(3));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task A_repository_lease_held_past_the_budget_refuses_as_busy_without_fetching(CancellationToken testCt)
    {
        await SkipIfGitUnavailableAsync();
        var root = NewRoot();
        await using var silent = new SilentOrigin();
        try
        {
            var (repo, _, _) = await CreateRepoAsync(root, withOrigin: false);
            await GitAsync(repo, "remote", "add", "origin", silent.Url);
            const string missing = "0123456789abcdef0123456789abcdef01234567";
            var leases = new RepositoryMutationLease(new LandingGit());
            await using var held = await leases.TryAcquireAsync(repo,
                new RepositoryLeaseOwnerTag(Guid.NewGuid(), RepositoryLeasePurposes.Land), testCt);
            held.ShouldNotBeNull("precondition: another operation holds the repository lease");
            var clock = Stopwatch.StartNew();

            var ex = await Should.ThrowAsync<ServiceUnavailableException>(() =>
                Build(TimeSpan.FromSeconds(2), leases).EnsureAvailableAsync(repo, missing, testCt));

            ex.Code.ShouldBe(StartRefAvailability.RepositoryBusyCode);
            ex.StatusCode.ShouldBe(503);
            ex.Message.ShouldContain(missing);
            ex.Message.ShouldContain(repo);
            silent.Accepted.ShouldBe(0, "no fetch may run while another operation holds the repository lease");
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20), "the lease wait is bounded by the network budget");
            Directory.Exists(await ChildJournalDirectoryAsync(repo)).ShouldBeFalse("no fetch child was ever journaled");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task A_repository_lease_released_within_the_budget_lets_the_fetch_run(CancellationToken testCt)
    {
        await SkipIfGitUnavailableAsync();
        var root = NewRoot();
        try
        {
            var (repo, _, origin) = await CreateRepoAsync(root);
            var sha = await PushOriginOnlyCommitAsync(root, origin);
            var leases = new RepositoryMutationLease(new LandingGit());
            var held = await leases.TryAcquireAsync(repo,
                new RepositoryLeaseOwnerTag(Guid.NewGuid(), RepositoryLeasePurposes.Land), testCt);
            held.ShouldNotBeNull("precondition: another operation holds the repository lease");

            var create = Build(TimeSpan.FromSeconds(20), leases).EnsureAvailableAsync(repo, sha, testCt);
            await Task.Delay(TimeSpan.FromSeconds(1), testCt);
            create.IsCompleted.ShouldBeFalse("create waits for the lease instead of fetching unfenced");
            (await ResolvesAsync(repo, sha)).ShouldBeFalse("nothing is fetched while the lease is held elsewhere");
            await held.DisposeAsync();

            await create;
            (await ResolvesAsync(repo, sha)).ShouldBeTrue("the fetch runs once the lease is free");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    /// <summary>A fetch that fails after a delay and an ls-remote that answers only its deadline; the rest is real git.</summary>
    private sealed class SlowFetchGit(TimeSpan fetchDelay) : LandingGit
    {
        public TimeSpan? ProbeAllowed { get; private set; }

        public override async Task<Antiphon.Server.Application.Dtos.LandingGitResult> RunAsync(
            string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            if (arguments[0] == "fetch")
            {
                await Task.Delay(fetchDelay, ct);
                return new(128, "", "fixture fetch failed");
            }
            if (arguments[0] == "ls-remote")
            {
                var clock = Stopwatch.StartNew();
                try { await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct); }
                finally { ProbeAllowed = clock.Elapsed; }
            }
            return await base.RunAsync(repository, arguments, ct);
        }
    }

    private static async Task<string> ChildJournalDirectoryAsync(string repo) =>
        Path.Combine(await new LandingGit().CommonDirectoryAsync(repo, CancellationToken.None), "antiphon", "children");
}
