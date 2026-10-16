using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointCoverageMappingNativeTests : CheckpointTestBase
{
    [Test]
    [NotInParallel]
    public void Large_child_output_is_streamed_and_tail_changes_are_detected()
    {
        var root = TempDir();
        var output = Path.Combine(root, "bin-c1310-stream");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        var file = Path.Combine(output, "large.dll");
        const long length = 65L * 1024 * 1024;
        using (var writer = File.Create(file))
        {
            writer.SetLength(length);
            writer.WriteByte(0x43);
            writer.Position = length - 1;
            writer.WriteByte(0x31);
        }
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        SafeWrite.Require(OwnedOutputLedger.Seal(output, DateTimeOffset.UtcNow));
        OwnedOutputLedger.Check(output).ShouldBeNull("outputs above the receipt cap need streaming custody");
        (GC.GetTotalAllocatedBytes(precise: true) - allocated).ShouldBeLessThan(32L * 1024 * 1024,
            "adoption and validation must not materialize the 65 MiB output");
        using (var writer = File.OpenWrite(file))
        {
            writer.Position = length - 1;
            writer.WriteByte(0x32);
        }
        OwnedOutputLedger.Check(output).ShouldNotBeNull("the streaming hash must cover the final byte");
        using (var writer = File.OpenWrite(file))
        {
            writer.Position = length - 1;
            writer.WriteByte(0x31);
        }
        OwnedOutputLedger.Remove(output).ShouldBeNull();
        Directory.Exists(output).ShouldBeFalse();
    }

    [Test]
    public void Windows_copied_old_timestamps_inside_creation_window_are_adopted()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("native Windows creation-time qualification");
        var root = TempDir();
        var old = DateTimeOffset.UtcNow.AddDays(-1);
        var source = Path.Combine(root, "source.dll");
        File.WriteAllText(source, "COPIED BUILD OUTPUT");
        SetWindowsTimes(source, old, old, old);
        var output = Path.Combine(root, "bin-c1310-copy");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        var copied = Path.Combine(output, "copied.dll");
        File.Copy(source, copied);
        // Reproduce MSBuild's preserved source write/change times while retaining the
        // destination's new creation time, independent of the CopyFile implementation.
        SetWindowsTimes(copied, File.GetCreationTimeUtc(copied), old, old);
        var window = OutputCustody.For(output, DateTimeOffset.UtcNow)!;
        File.GetCreationTimeUtc(copied).ShouldBeGreaterThanOrEqualTo(window.RootCreated.UtcDateTime);
        OutputCustody.Stat(copied)!.Value.Changed.ShouldBeLessThan(window.RootCreated);
        OutputCustody.Stat(copied)!.Value.Modified.ShouldBeLessThan(window.RootCreated);
        SafeWrite.Require(OwnedOutputLedger.Seal(output, window.BuildExited));
        OwnedOutputLedger.Check(output).ShouldBeNull("a file created during the build may carry old copied timestamps");
        OwnedOutputLedger.Remove(output).ShouldBeNull();
        Directory.Exists(output).ShouldBeFalse();
        File.ReadAllText(source).ShouldBe("COPIED BUILD OUTPUT");
    }

    [Test]
    public void Windows_file_created_before_root_is_retained()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("native Windows creation-time qualification");
        var root = TempDir();
        var old = DateTimeOffset.UtcNow.AddDays(-1);
        var original = Path.Combine(root, "user.txt");
        File.WriteAllText(original, "USER");
        SetWindowsTimes(original, old, old, old);
        var output = Path.Combine(root, "bin-c1310-preexisting");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        var moved = Path.Combine(output, "user.txt");
        File.Move(original, moved);
        var now = DateTimeOffset.UtcNow;
        SetWindowsTimes(moved, old, now, now);
        var window = OutputCustody.For(output, DateTimeOffset.UtcNow)!;
        File.GetCreationTimeUtc(moved).ShouldBeLessThan(window.RootCreated.UtcDateTime);
        OutputCustody.Stat(moved)!.Value.Changed.ShouldBeGreaterThanOrEqualTo(window.RootCreated);
        SafeWrite.Require(OwnedOutputLedger.Seal(output, window.BuildExited));
        OwnedOutputLedger.Remove(output).ShouldNotBeNull("recent change/write times do not confer creation custody");
        File.ReadAllText(moved).ShouldBe("USER");
    }

    private static void SetWindowsTimes(string path, DateTimeOffset created, DateTimeOffset written, DateTimeOffset changed)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var basic = new WindowsBasicInfo
        {
            Creation = created.ToFileTime(), Write = written.ToFileTime(), Change = changed.ToFileTime()
        };
        SetFileInformationByHandle(handle, 0, ref basic, (uint)Marshal.SizeOf<WindowsBasicInfo>())
            .ShouldBeTrue("native timestamp fixture must be established");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsBasicInfo
    {
        public long Creation, Access, Write, Change;
        public uint Attributes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass,
        ref WindowsBasicInfo info, uint size);

    [Test]
    public void Windows_root_with_other_writers_does_not_adopt_child_output()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("native Windows ACL qualification");
        var root = TempDir();
        var output = Path.Combine(root, "bin-c1310-acl");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        var file = Path.Combine(output, "user.txt");
        File.WriteAllText(file, "USER");
        var directory = new DirectoryInfo(output);
        var acl = directory.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Write, AccessControlType.Allow));
        directory.SetAccessControl(acl);
        SafeWrite.Require(OwnedOutputLedger.Seal(output, DateTimeOffset.UtcNow));
        OwnedOutputLedger.Remove(output).ShouldNotBeNull();
        File.ReadAllText(file).ShouldBe("USER");
    }

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
