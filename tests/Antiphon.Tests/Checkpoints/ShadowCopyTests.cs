using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class ShadowCopyTests
{
    [Test]
    public void copies_the_dll_set_never_the_source_tree()
    {
        var root = CheckpointFixtures.TempDir();
        var tool = Path.Combine(root, "out");
        Directory.CreateDirectory(tool);
        File.WriteAllText(Path.Combine(tool, "Antiphon.Checkpoints.dll"), "dll");
        File.WriteAllText(Path.Combine(tool, "Antiphon.Checkpoints.deps.json"), "{}");
        var source = Path.Combine(root, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "Program.cs"), "source");
        var destination = Path.Combine(root, "run", "tool");
        ShadowCopy.CopyToolOutput(tool, destination);
        File.Exists(Path.Combine(destination, "Antiphon.Checkpoints.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(destination, "Antiphon.Checkpoints.deps.json")).ShouldBeTrue();
        File.Exists(Path.Combine(destination, "Program.cs")).ShouldBeFalse();
        Directory.Exists(Path.Combine(destination, "src")).ShouldBeFalse();
    }
}
