using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointCoverageMappingNativeTests : CheckpointTestBase
{
    [Test]
    public void Native_build_append_and_child_output_are_sealed_and_removed()
    {
        var root = TempDir();
        var output = Path.Combine(root, "bin-c1310-native");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        var mapping = Path.Combine(output, ".msCoverageExtensionSourceRootsMapping_Antiphon.E2E");
        SafeWrite.Require(SafeWrite.Text(mapping, "first-project\n", SafeWrite.Under(output)));
        File.AppendAllText(mapping, "second-project\n");
        var child = Path.Combine(output, "compiler.dll");
        File.WriteAllText(child, "BUILD");
        SafeWrite.Require(OwnedOutputLedger.Seal(output, DateTimeOffset.UtcNow));
        OwnedOutputLedger.Check(output).ShouldBeNull();
        OwnedOutputLedger.Remove(output).ShouldBeNull();
        Directory.Exists(output).ShouldBeFalse();
    }

    [Test]
    public void Native_child_write_after_build_exit_is_retained()
    {
        var root = TempDir();
        var output = Path.Combine(root, "bin-c1310-native");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        var exit = DateTimeOffset.UtcNow;
        // Cross a filesystem timestamp tick, not a production timeout or retry.
        Thread.Sleep(20);
        var late = Path.Combine(output, "late.txt");
        File.WriteAllText(late, "USER");
        SafeWrite.Require(OwnedOutputLedger.Seal(output, exit));
        OwnedOutputLedger.Remove(output).ShouldNotBeNull();
        File.ReadAllText(late).ShouldBe("USER");
    }
}
