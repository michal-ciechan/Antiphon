using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class ShadowCopyTests : CheckpointTestBase
{
    [Test]
    public void copies_the_dll_set_never_the_source_tree()
    {
        var root = TempDir();
        var tool = Path.Combine(root, "out");
        Directory.CreateDirectory(tool);
        File.WriteAllText(Path.Combine(tool, "Antiphon.Checkpoints.dll"), "dll");
        File.WriteAllText(Path.Combine(tool, "Antiphon.Checkpoints.deps.json"), "{}");
        File.WriteAllText(Path.Combine(tool, "YamlDotNet.dll"), "dependency-bytes");
        var resource = Path.Combine(tool, "fr", "resource.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(resource)!);
        File.WriteAllText(resource, "resource-bytes");
        var source = Path.Combine(tool, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "Program.cs"), "source");
        var destination = Path.Combine(root, "run", "tool");
        ShadowCopy.CopyToolOutput(tool, destination);
        File.Exists(Path.Combine(destination, "Antiphon.Checkpoints.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(destination, "Antiphon.Checkpoints.deps.json")).ShouldBeTrue();
        File.ReadAllBytes(Path.Combine(destination, "YamlDotNet.dll"))
            .ShouldBe(File.ReadAllBytes(Path.Combine(tool, "YamlDotNet.dll")));
        File.ReadAllBytes(Path.Combine(destination, "fr", "resource.txt"))
            .ShouldBe(File.ReadAllBytes(resource));
        File.Exists(Path.Combine(destination, "Program.cs")).ShouldBeFalse();
        Directory.Exists(Path.Combine(destination, "src")).ShouldBeFalse();
    }
}
