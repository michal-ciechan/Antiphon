using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.TestHelpers.StartRefGit;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-0666. Worktree creation runs inside the dispatch claim, under the repository lease, so it
/// never fetches: a start ref that is not local is refused at once with a message naming it. The
/// fetch of a start SHA only origin has belongs to task create (<c>StartRefAvailabilityTests</c>).
/// </summary>
[Category("GitIntegration")]
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class WorktreeManagerStartRefLocalOnlyTests
{
    [Test]
    public async Task Create_refuses_a_start_sha_only_origin_has_without_fetching_it()
    {
        await SkipIfGitUnavailableAsync();
        var root = NewRoot();
        await using var silent = new SilentOrigin();
        try
        {
            var (repo, worktrees, origin) = await CreateRepoAsync(root);
            var startSha = await PushOriginOnlyCommitAsync(root, origin);
            // origin now never answers: a fetch here would hang, and the listener would see it.
            await GitAsync(repo, "remote", "set-url", "origin", silent.Url);

            var ex = await Should.ThrowAsync<ValidationException>(() => BuildManager(worktrees)
                .CreateAsync(repo, "task-2cd14d4c", startSha, CancellationToken.None));

            ex.Code.ShouldBe("worktree_base_ref_unresolved");
            silent.Accepted.ShouldBe(0, "dispatch-side worktree creation must make no network call");
            (await ResolvesAsync(repo, startSha)).ShouldBeFalse();
            Directory.Exists(Path.Combine(worktrees, "card-task-2cd14d4c")).ShouldBeFalse();
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Test]
    public async Task Create_names_the_unresolved_start_ref_in_the_refusal()
    {
        await SkipIfGitUnavailableAsync();
        var root = NewRoot();
        try
        {
            var (repo, worktrees, _) = await CreateRepoAsync(root);
            const string missing = "0123456789abcdef0123456789abcdef01234567";

            var ex = await Should.ThrowAsync<ValidationException>(() => BuildManager(worktrees)
                .CreateAsync(repo, "task-2cd14d4c", missing, CancellationToken.None));

            // The dispatch report carries only ex.Message; the generic 422 text names nothing.
            ex.Message.ShouldContain(missing);
            ex.Message.ShouldContain("does not resolve to a commit");
            ex.Message.ShouldContain("Dispatch does not fetch");
            ex.Message.ShouldNotContain("One or more validation errors occurred");
            Directory.Exists(Path.Combine(worktrees, "card-task-2cd14d4c")).ShouldBeFalse();
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static WorktreeManager BuildManager(string worktreeRoot) => new(
        Options.Create(new GitSettings { WorktreeBasePath = worktreeRoot }),
        TimeProvider.System,
        NullLogger<WorktreeManager>.Instance);
}
