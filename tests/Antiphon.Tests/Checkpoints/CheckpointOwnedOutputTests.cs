using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointOwnedOutputTests : CheckpointTestBase
{
    private string InitRepo()
    {
        var root = TempDir();
        using var process = Process.Start(new ProcessStartInfo("git") { WorkingDirectory = root, ArgumentList = { "init", "-q" } });
        process.ShouldNotBeNull();
        RegisterCheckpointChild(process);
        process.WaitForExit(30_000).ShouldBeTrue();
        process.ExitCode.ShouldBe(0);
        return root;
    }

    // case -> unrecorded addition/change/link -> red if cleanup removes user residue.
    [Test]
    [Arguments("file")]
    [Arguments("changed")]
    [Arguments("directory")]
    [Arguments("link")]
    public void changed_inventory_is_refused(string variant)
    {
        if (variant == "link" && OperatingSystem.IsWindows()) Skip.Test("Linux symlink proof; Windows junction remains R2");
        var root = InitRepo();
        var output = Path.Combine(root, "bin-ledger");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        File.WriteAllText(Path.Combine(output, "compiler.dll"), "COMPILER");
        SafeWrite.Require(OwnedOutputLedger.Seal(output));
        var extra = Path.Combine(output, "user.txt");
        if (variant == "file") File.WriteAllText(extra, "USER");
        if (variant == "changed") File.WriteAllText(Path.Combine(output, "compiler.dll"), "USER");
        if (variant == "directory") Directory.CreateDirectory(extra);
        if (variant == "link") File.CreateSymbolicLink(extra, Path.Combine(root, "outside.txt"));
        try
        {
            var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-ledger"], 0, false, false, createdPaths: [output]);
            result.Deleted.ShouldBeEmpty();
            result.Refusals.ShouldNotBeEmpty();
            Directory.Exists(output).ShouldBeTrue();
            if (variant == "file") File.ReadAllText(extra).ShouldBe("USER");
            if (variant == "changed") File.ReadAllText(Path.Combine(output, "compiler.dll")).ShouldBe("USER");
            if (variant == "directory") Directory.Exists(extra).ShouldBeTrue();
            if (variant == "link") new FileInfo(extra).LinkTarget.ShouldNotBeNull();
        }
        finally { if (variant == "link") new FileInfo(extra).Delete(); }
    }

    // case -> valid ledger -> red if cleanup cannot reclaim its own unchanged payload.
    [Test]
    public void unchanged_owned_inventory_is_removed()
    {
        var root = InitRepo();
        var output = Path.Combine(root, "bin-ledger");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        File.WriteAllText(Path.Combine(output, "compiler.dll"), "COMPILER");
        SafeWrite.Require(OwnedOutputLedger.Seal(output));
        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-ledger"], 0, false, false, createdPaths: [output]);
        result.Refusals.ShouldBeEmpty();
        result.Deleted.ShouldBe([output]);
        Directory.Exists(output).ShouldBeFalse();
    }
}
