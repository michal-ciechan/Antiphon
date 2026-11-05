using System.Diagnostics;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// <see cref="InProcessGitWrites"/>: the create-only observation pin is written in process exactly as
/// <c>git update-ref</c> writes it (same loose ref file, no reflog), and every case git would refuse or handle
/// differently (existing ref, missing object, reference-transaction hook, other command shapes) still runs git.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class InProcessObservationPinTests
{
    [Test]
    public async Task Pin_written_in_process_matches_the_ref_git_writes()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var git = new SpawnCountingGit(fixture);
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}";
        var inProcess = prefix + "/remote-observed/" + Guid.NewGuid().ToString("N");
        var byGit = prefix + "/remote-observed/" + Guid.NewGuid().ToString("N");
        var zero = new string('0', 40);

        var written = await git.RunAsync(fixture.Repository,
            ["update-ref", "-m", LandingGit.ObservationMessagePrefix + fixture.TargetRef, inProcess, fixture.SeedSha, zero], default);
        written.ExitCode.ShouldBe(0);
        git.Spawned.ShouldBe(0, "the observation pin is written in process");

        // The same write by git (a message without the observation prefix is not answered in process).
        (await git.RunAsync(fixture.Repository, ["update-ref", "-m", "fixture", byGit, fixture.SeedSha, zero], default)).ExitCode.ShouldBe(0);
        git.Spawned.ShouldBe(1);

        var gitDir = Path.Combine(fixture.Repository, ".git");
        (await File.ReadAllTextAsync(Path.Combine(gitDir, inProcess))).ShouldBe(await File.ReadAllTextAsync(Path.Combine(gitDir, byGit)));
        (await File.ReadAllTextAsync(Path.Combine(gitDir, inProcess))).ShouldBe(fixture.SeedSha + "\n");
        File.Exists(Path.Combine(gitDir, "logs", inProcess)).ShouldBe(File.Exists(Path.Combine(gitDir, "logs", byGit)),
            "the reflog decision is git's (none for refs/antiphon/ by default)");
        (await fixture.RequiredAsync(fixture.Repository, "rev-parse", "--verify", inProcess)).Trim().ShouldBe(fixture.SeedSha);
        (await fixture.RequiredAsync(fixture.Repository, "fsck", "--no-dangling")).ShouldNotContain("error");
    }

    [Test]
    public async Task Refusals_and_unusual_cases_run_git()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var git = new SpawnCountingGit(fixture);
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}";
        var zero = new string('0', 40);
        string[] Pin(string name, string sha) => ["update-ref", "-m", LandingGit.ObservationMessagePrefix + fixture.TargetRef, name, sha, zero];

        // An existing ref: git refuses the create-only update, with its own exit code.
        var existing = prefix + "/remote-observed/" + Guid.NewGuid().ToString("N");
        (await git.RunAsync(fixture.Repository, Pin(existing, fixture.SeedSha), default)).ExitCode.ShouldBe(0);
        var before = git.Spawned;
        (await git.RunAsync(fixture.Repository, Pin(existing, fixture.SeedSha), default)).ExitCode.ShouldNotBe(0);
        git.Spawned.ShouldBe(before + 1, "an existing pin is git's refusal");

        // A missing object: git refuses it.
        before = git.Spawned;
        (await git.RunAsync(fixture.Repository, Pin(prefix + "/remote-observed/" + Guid.NewGuid().ToString("N"), new string('1', 40)), default))
            .ExitCode.ShouldNotBe(0);
        git.Spawned.ShouldBe(before + 1, "a missing object is git's refusal");

        // A name outside the land namespace, or a non-zero old value: not the observation pin shape.
        before = git.Spawned;
        (await git.RunAsync(fixture.Repository, ["update-ref", "-m", LandingGit.ObservationMessagePrefix + "x",
            "refs/antiphon/other/" + Guid.NewGuid().ToString("N"), fixture.SeedSha, zero], default)).ExitCode.ShouldBe(0);
        git.Spawned.ShouldBe(before + 1);

        // A reference-transaction hook (git runs it, libgit2 would not): git writes the pin.
        var hooks = Path.Combine(fixture.Root, "hooks-with-transaction");
        Directory.CreateDirectory(hooks);
        await File.WriteAllTextAsync(Path.Combine(hooks, "reference-transaction"), "#!/bin/sh\nexit 0\n");
        var hooked = new SpawnCountingGit(fixture) { HooksPathOverride = hooks };
        (await hooked.RunAsync(fixture.Repository, Pin(prefix + "/remote-observed/" + Guid.NewGuid().ToString("N"), fixture.SeedSha), default))
            .ExitCode.ShouldBe(0);
        hooked.Spawned.ShouldBe(1, "a reference-transaction hook must run, so git writes the pin");
    }

    /// <summary>The fixture's git, counting the git processes it starts.</summary>
    private sealed class SpawnCountingGit(LandingGitFixture fixture)
        : LandingGitFixture.FixtureGit(Path.Combine(fixture.Root, "home"), fixture.TaskId)
    {
        public int Spawned;

        protected override Process? StartProcess(ProcessStartInfo start)
        {
            Interlocked.Increment(ref Spawned);
            return base.StartProcess(start);
        }
    }
}
