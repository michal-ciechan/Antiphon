using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointFixtureGitEnvironmentTests : CheckpointTestBase
{
    [Test]
    [Timeout(120_000)]
    public void fixture_commit_matches_hardened_status_under_hostile_config()
    {
        var repo = Path.Combine(TempDir(), "repo");
        Directory.CreateDirectory(repo);
        using var environment = CheckpointFixtureGitEnvironment.Create(TempDir());
        environment.Git(repo, "init", "-b", "card1199");
        var payload = Path.Combine(repo, "payload.txt");
        File.WriteAllBytes(payload, "line\r\n"u8.ToArray());
        environment.Git(repo, "add", "--", "payload.txt");
        environment.Git(repo, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-qm", "fixture", "--no-verify");
        File.SetLastWriteTimeUtc(payload, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var status = HardenedStatus(repo);
        status.Contains("payload.txt", StringComparison.Ordinal).ShouldBeFalse("dirty-baseline payload.txt\n" + status);
    }

    private static string HardenedStatus(string repository)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = repository };
        start.ArgumentList.Add("status");
        start.ArgumentList.Add("--porcelain=v1");
        var result = CheckpointProcessRunner.Run(start, 30_000, git: true);
        result.TimedOut.ShouldBeFalse("hardened status timed out");
        result.Exit.ShouldBe(0, result.Stderr);
        return result.Stdout;
    }
}
