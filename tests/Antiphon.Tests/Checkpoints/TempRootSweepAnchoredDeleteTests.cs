using System.Runtime.InteropServices;
using System.Text;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// CARD-1596: the temp-root sweep still unlinks by path. A parent swapped for a
// symlink during that unlink is followed, and a FIFO or socket has to be removed
// as the directory entry itself. Every path stays under one fresh temp fixture.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TempRootSweepAnchoredDeleteTests
{
    [Test]
    public void swapped_parent_symlink_cannot_redirect_temp_root_sweep()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("the sweep parent-swap witness uses a Linux directory symlink");
        var sandbox = Scratch();
        var outside = Path.Combine(sandbox, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "payload.txt");
        File.WriteAllText(sentinel, "foreign-sentinel");
        File.WriteAllText(Path.Combine(outside, "keeper.txt"), "keep");
        var root = Candidate(sandbox);
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "payload.txt"), "owned-payload");
        var io = new SweepSwapIo(nested, outside);
        var context = new CheckpointCleanupContext(io, new CheckpointNativeGitRunner());
        try
        {
            Sweep(context, sandbox).SweepOnce();
            io.Swapped.ShouldBeTrue();
            File.ReadAllText(sentinel).ShouldBe("foreign-sentinel");
            File.ReadAllText(Path.Combine(outside, "keeper.txt")).ShouldBe("keep");
        }
        finally { RemoveScratch(sandbox); }
    }

    [Test]
    public void fifo_and_socket_inside_the_root_are_removed()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("FIFO and Unix socket entries are Linux directory entries");
        var sandbox = Scratch();
        try
        {
            var root = Candidate(sandbox);
            var fifo = Path.Combine(root, "fifo");
            var socketPath = Path.Combine(root, "sock");
            MkFifo(fifo, 0x1B6).ShouldBe(0, "mkfifo errno " + Marshal.GetLastPInvokeError());
            BindSocket(socketPath).ShouldBe(0, "socket bind errno " + Marshal.GetLastPInvokeError());
            Kind(fifo).ShouldBe(0x1000);
            Kind(socketPath).ShouldBe(0xC000);
            var receipt = Sweep(sandbox).SweepOnce();
            receipt.CompletedRoots.ShouldBe(1, "skips " + Format(receipt));
            Directory.Exists(root).ShouldBeFalse();
            Kind(fifo).ShouldBe(-1);
            Kind(socketPath).ShouldBe(-1);
        }
        finally { RemoveScratch(sandbox); }
    }

    [Test]
    public void symlink_inside_the_root_is_unlinked_without_following()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("requires a Linux symlink");
        var sandbox = Scratch();
        try
        {
            var root = Candidate(sandbox);
            var outside = Path.Combine(sandbox, "outside");
            Directory.CreateDirectory(outside);
            var sentinel = Path.Combine(outside, "kept.txt");
            File.WriteAllText(sentinel, "foreign-sentinel");
            File.CreateSymbolicLink(Path.Combine(root, "link"), sentinel);
            var receipt = Sweep(sandbox).SweepOnce();
            File.ReadAllText(sentinel).ShouldBe("foreign-sentinel");
            receipt.CompletedRoots.ShouldBe(1, "skips " + Format(receipt));
            Directory.Exists(root).ShouldBeFalse();
            File.Exists(sentinel).ShouldBeTrue();
        }
        finally { RemoveScratch(sandbox); }
    }

    private sealed class SweepSwapIo(string parent, string foreign) : ForwardingNativeIo
    {
        public bool Swapped { get; private set; }

        public override void Unlink(string path)
        {
            Swap(path);
            base.Unlink(path);
        }

        public override void RemoveDirectory(string path, bool recursive)
        {
            Swap(path);
            base.RemoveDirectory(path, recursive);
        }

        public override void DeleteAnchored(string path, FilesystemIdentity expectedEntry, bool directory)
        {
            Swap(path);
            base.DeleteAnchored(path, expectedEntry, directory);
        }

        private void Swap(string path)
        {
            if (Swapped || !string.Equals(Path.GetFileName(path), "payload.txt", StringComparison.Ordinal))
                return;
            if (!Directory.Exists(parent) || DirectoryLink.IsLink(parent)) return;
            Swapped = true;
            Directory.Move(parent, parent + ".aside");
            Directory.CreateSymbolicLink(parent, foreign);
        }
    }

    private static CheckpointTempRootSweep Sweep(string sandbox) =>
        Sweep(CheckpointCleanupDefaults.Context, sandbox);

    private static CheckpointTempRootSweep Sweep(CheckpointCleanupContext context, string sandbox) =>
        new(context, sandbox, options: new CheckpointSweepOptions
        {
            Grace = TimeSpan.Zero,
            Interval = TimeSpan.Zero,
            MaxDuration = TimeSpan.FromSeconds(5),
        });

    private static string Candidate(string sandbox)
    {
        var root = Path.Combine(sandbox, "c723-" + Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(root);
        var current = new ProcessIdentityProbe().Current();
        TestRootGuard.Write(root, new CheckpointRootMarker
        {
            RootId = Path.GetFileName(root)["c723-".Length..],
            RootPath = Path.GetFullPath(root),
            AttemptId = Guid.NewGuid().ToString("N"),
            AssemblyInvocationId = "sweep-anchor",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Owner = current with { Pid = int.MaxValue, StartUtcTicks = 1 },
        });
        new CheckpointTempRootSweep(sandbox).Register(root);
        return root;
    }

    private static string Format(CheckpointSweepReceipt receipt) =>
        string.Join(",", receipt.Skips.Select(pair => pair.Key + "=" + pair.Value));

    private static string Scratch() =>
        Path.Combine(Path.GetTempPath(), "antiphon-sweep-anchor-" + Guid.NewGuid().ToString("N"));

    private static void RemoveScratch(string sandbox)
    {
        try
        {
            if (!Directory.Exists(sandbox)) return;
            var root = Path.GetFullPath(sandbox);
            UnlinkLinks(root, root);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void UnlinkLinks(string directory, string root)
    {
        var full = Path.GetFullPath(directory);
        if (!full.StartsWith(root, StringComparison.Ordinal)) return;
        FileAttributes attributes;
        try { attributes = File.GetAttributes(full); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(full, recursive: false);
            return;
        }
        foreach (var entry in Directory.EnumerateFileSystemEntries(full))
        {
            FileAttributes child;
            try { child = File.GetAttributes(entry); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if ((child & FileAttributes.ReparsePoint) != 0)
            {
                if ((child & FileAttributes.Directory) != 0) Directory.Delete(entry, recursive: false);
                else File.Delete(entry);
            }
            else if ((child & FileAttributes.Directory) != 0)
                UnlinkLinks(entry, root);
        }
    }

    private static int BindSocket(string path)
    {
        var fd = Socket(1, 1, 0);
        if (fd < 0) return -1;
        try
        {
            var name = Encoding.UTF8.GetBytes(path);
            if (name.Length >= 108) return -1;
            var address = new byte[110];
            address[0] = 1;
            name.CopyTo(address.AsSpan(2));
            return Bind(fd, address, 2 + name.Length + 1);
        }
        finally { _ = Close(fd); }
    }

    private static int Kind(string path)
    {
        var buffer = new byte[256];
        if (LStat(path, buffer) != 0) return -1;
        return BitConverter.ToInt32(buffer, 24) & 0xF000;
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, int mode);
    [DllImport("libc", EntryPoint = "socket", SetLastError = true)]
    private static extern int Socket(int domain, int type, int protocol);
    [DllImport("libc", EntryPoint = "bind", SetLastError = true)]
    private static extern int Bind(int fd, byte[] address, int length);
    [DllImport("libc", EntryPoint = "close")]
    private static extern int Close(int fd);
    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat(string path, byte[] buffer);
}
