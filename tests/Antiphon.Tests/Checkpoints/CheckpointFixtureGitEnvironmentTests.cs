using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// CheckpointFixtureGitEnvironment.Git sets fixture repositories up in process for its common shapes (init -b,
/// add -A, add -f, the fixture commit, rev-parse HEAD). Whatever it does in process must leave the repository git
/// itself sees as the git commands would have.
/// </summary>
[Category("Integration")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointFixtureGitEnvironmentTests : CheckpointTestBase
{
    [Test]
    [Timeout(120_000)]
    public void Fixture_setup_in_process_leaves_what_git_would(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = TempDir();
        using var git = CheckpointFixtureGitEnvironment.Create(Path.Combine(scope, "git-isolation"));
        var root = Directory.CreateDirectory(Path.Combine(scope, "repo")).FullName;

        git.Git(root, "init", "-b", "card1199");
        File.WriteAllText(Path.Combine(root, ".gitignore"), "bin-*/\nobj/\n");
        Directory.CreateDirectory(Path.Combine(root, "App", "obj"));
        File.WriteAllText(Path.Combine(root, "App", "App.csproj"), "<Project />\n");
        File.WriteAllText(Path.Combine(root, "App", "Program.cs"), "class P { }\r\n");
        File.WriteAllText(Path.Combine(root, "App", "obj", "project.assets.json"), "{}\n");
        git.Git(root, "add", "-A");
        git.Git(root, "add", "-f", "--", Path.Combine(root, "App", "obj", "project.assets.json"));
        git.Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "fixture", "--no-verify");

        // What real git sees afterwards.
        string Real(params string[] args) => CheckpointFixtureProcess.Git(root, args);
        Real("rev-parse", "--abbrev-ref", "HEAD").Trim().ShouldBe("card1199");
        Real("status", "--porcelain", "--untracked-files=all").ShouldBeEmpty("everything staged is committed, and the rest is ignored");
        Real("ls-files").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)
            .ShouldBe([".gitignore", "App/App.csproj", "App/Program.cs", "App/obj/project.assets.json"]);
        Real("log", "-1", "--format=%an <%ae> %s").Trim().ShouldBe("card-1199 <card-1199@example.invalid> fixture");
        Real("config", "core.autocrlf").Trim().ShouldBe("false");
        Real("cat-file", "-p", "HEAD:App/Program.cs").ShouldBe("class P { }\r\n", "bytes stored as written");
        git.Git(root, "rev-parse", "HEAD").ShouldBe(Real("rev-parse", "HEAD"));
    }
}
