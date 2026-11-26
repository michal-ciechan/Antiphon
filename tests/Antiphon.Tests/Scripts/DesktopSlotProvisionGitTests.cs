using System.IO.Abstractions;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0824 S2: desktop-slot-provision (unit 51 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md) against real git
/// through the production tool seam (OperatorToolOs), in fixture repositories under a temporary root: core.longpaths is set on the
/// slot clone only, and a clone that git resolves to an enclosing repository (an empty .git directory, or a .git file pointing
/// elsewhere) is refused without touching that repository. These were the deleted driver's execute and clone-ceiling cases.
/// </summary>
[Category("Unit")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class DesktopSlotProvisionGitTests
{
    private static readonly OperatorToolOs Git = new(Environment.GetEnvironmentVariable);

    [Test]
    public void Execute_sets_longpaths_on_the_slot_clone_and_leaves_a_decoy_alone()
    {
        using var world = new OperatorToolWorld("c0824s2-git");
        var root = world.PathOf("execute");
        var clone = Path.Combine(root, "repos", "antiphon");
        var decoy = Path.Combine(root, "decoy");
        Repository(clone);
        Repository(decoy);

        var first = Provision(world, root);
        first.ExitCode.ShouldBe(0, string.Join("\n", first.Lines));
        first.Lines[^1].ShouldBe("longpaths=true");
        Longpaths(clone).ShouldBe("true");
        Longpaths(decoy).ShouldBe("false");

        var second = Provision(world, root);
        second.ExitCode.ShouldBe(0, string.Join("\n", second.Lines));
        second.Lines[^1].ShouldBe("longpaths=true");
        Longpaths(clone).ShouldBe("true");
        Directory.EnumerateFileSystemEntries(root).Count().ShouldBe(5, "the four directories and the decoy");
    }

    [Test]
    public void A_clone_that_resolves_to_an_enclosing_repository_is_refused()
    {
        using var world = new OperatorToolWorld("c0824s2-git");
        var outer = world.PathOf("enclosing");
        Repository(outer);
        Directory.CreateDirectory(Path.Combine(outer, "repos", "antiphon", ".git"));
        var result = Provision(world, outer);
        result.ExitCode.ShouldBe(2, string.Join("\n", result.Lines));
        result.Lines[^1].ShouldStartWith("refused: clone-unresolved ");
        Longpaths(outer).ShouldBe("false", "the enclosing repository is untouched");

        var gitFileOuter = world.PathOf("enclosing-gitfile");
        Repository(gitFileOuter);
        var gitFileClone = Directory.CreateDirectory(Path.Combine(gitFileOuter, "repos", "antiphon")).FullName;
        File.WriteAllText(Path.Combine(gitFileClone, ".git"), "gitdir: " + Path.Combine(gitFileOuter, ".git") + "\n");
        var pointed = Provision(world, gitFileOuter);
        pointed.ExitCode.ShouldBe(2, string.Join("\n", pointed.Lines));
        pointed.Lines[^1].ShouldStartWith("refused: clone-");
        pointed.Lines.ShouldNotContain("longpaths=true");
        Longpaths(gitFileOuter).ShouldBe("false", "the repository the .git file names is untouched");
    }

    private static DesktopSlotResult Provision(OperatorToolWorld world, string root) =>
        DesktopSlotProvision.Provision(new DesktopSlotProvisionOptions { Root = root, Execute = true }, world.Host(), new FileSystem(),
            new DesktopSlotSeams { Tools = Git, FreeBytes = _ => 64L * 1024 * 1024 * 1024 });

    // A fixture repository with core.longpaths false.
    private static void Repository(string path)
    {
        Directory.CreateDirectory(path);
        Run("init", "-q", "--", path).ExitCode.ShouldBe(0, "git init " + path);
        Run("-C", path, "config", "--local", "core.longpaths", "false").ExitCode.ShouldBe(0);
    }

    private static string Longpaths(string path) => string.Join("\n", Run("-C", path, "config", "--local", "--get", "core.longpaths").OutputLines).Trim();

    private static OperatorToolResult Run(params string[] arguments) => Git.Run(new OperatorToolCall("git", arguments));
}
