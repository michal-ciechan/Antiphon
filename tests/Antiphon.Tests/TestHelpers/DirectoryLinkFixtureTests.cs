using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1061: DirectoryLink makes its link in process (a junction on Windows, a symbolic link elsewhere). Until unit 35 of the
/// 2026-11-22 plan the Windows junction was mklink /J in an owned pwsh child, and this class pinned that child's budgets,
/// timeouts, pipe faults, cancellation and cleanup on a fake owner (C1061_Command_*); with no child those rows have no subject.
/// What stays is the helper's own contract: a link only where nothing was, and Dispose removes the link, never the target.
/// </summary>
[Category("Unit")]
public sealed class DirectoryLinkFixtureTests
{
    [Test]
    public void C1061_Link_is_made_only_where_nothing_was_and_dispose_keeps_the_target()
    {
        var root = Directory.CreateTempSubdirectory("c1061-link-").FullName;
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            File.WriteAllText(Path.Combine(target, "sentinel.txt"), "target survives");

            var linkPath = Path.Combine(root, "link");
            var link = DirectoryLink.TryCreate(linkPath, target);
            link.ShouldNotBeNull("a fresh name under an existing parent");
            DirectoryLink.IsLink(linkPath).ShouldBeTrue();
            File.ReadAllText(Path.Combine(linkPath, "sentinel.txt")).ShouldBe("target survives");
            link.Dispose();
            DirectoryLink.IsLink(linkPath).ShouldBeFalse();
            Directory.Exists(linkPath).ShouldBeFalse();
            File.ReadAllText(Path.Combine(target, "sentinel.txt")).ShouldBe("target survives");

            var ordinary = Directory.CreateDirectory(Path.Combine(root, "ordinary")).FullName;
            File.WriteAllText(Path.Combine(ordinary, "own.txt"), "ordinary survives");
            DirectoryLink.TryCreate(ordinary, target).ShouldBeNull("an existing directory is refused");
            DirectoryLink.IsLink(ordinary).ShouldBeFalse();
            File.ReadAllText(Path.Combine(ordinary, "own.txt")).ShouldBe("ordinary survives");

            var file = Path.Combine(root, "file.txt");
            File.WriteAllText(file, "file survives");
            DirectoryLink.TryCreate(file, target).ShouldBeNull("an existing file is refused");
            File.ReadAllText(file).ShouldBe("file survives");

            var orphan = Path.Combine(root, "missing-parent", "link");
            DirectoryLink.TryCreate(orphan, target).ShouldBeNull("a missing parent is refused");
            Directory.Exists(Path.GetDirectoryName(orphan)).ShouldBeFalse("nothing is created on the way");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
