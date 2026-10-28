using System.Runtime.InteropServices;
using System.Text;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// CARD-1596: a parent swapped for a symlink during the sweep must not delete
// outside the root. FIFO, socket and symlink entries are removed as themselves.
// Every path stays under one fresh temp fixture.
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

    // Characterization of preserved reclaim. Base already removed a FIFO or socket
    // (fifo_or_socket_root_is_reclaimed). This is not a red witness of the anchored walk.
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

    [Test]
    public void backslash_name_inside_the_root_is_reclaimed_with_the_root()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("a backslash is a legal Linux file name");
        var sandbox = Scratch();
        try
        {
            var root = Candidate(sandbox);
            var plain = Path.Combine(root, "plain.txt");
            File.WriteAllText(plain, "plain");
            // A mishandled Windows path left as one directory entry, not nested components.
            var weirdName = "C:\\Antiphon\\x.txt";
            var weird = Path.Combine(root, weirdName);
            File.WriteAllText(weird, "named");
            Path.GetFileName(weird).ShouldBe(weirdName);
            var receipt = Sweep(sandbox).SweepOnce();
            receipt.CompletedRoots.ShouldBe(1, "skips " + Format(receipt));
            Directory.Exists(root).ShouldBeFalse();
            File.Exists(Path.Combine(root, TestRootGuard.MarkerName)).ShouldBeFalse();
        }
        finally { RemoveScratch(sandbox); }
    }

    [Test]
    public void hard_link_outside_the_root_is_not_truncated()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("the hard-link truncate witness uses Linux link(2)");
        var sandbox = Scratch();
        try
        {
            var outside = Path.Combine(sandbox, "outside");
            Directory.CreateDirectory(outside);
            var foreign = Path.Combine(outside, "foreign.bin");
            var payload = new byte[1000];
            payload[950] = 0x5A;
            File.WriteAllBytes(foreign, payload);
            var root = Candidate(sandbox);
            var linked = Path.Combine(root, "payload.bin");
            Link(foreign, linked).ShouldBe(0, "link errno " + Marshal.GetLastPInvokeError());
            // MaxBytes below the file length is what reaches truncation. A full unlink would not shrink the foreign link.
            var receipt = new CheckpointTempRootSweep(sandbox, options: new CheckpointSweepOptions
            {
                Grace = TimeSpan.Zero,
                Interval = TimeSpan.Zero,
                MaxDuration = TimeSpan.FromSeconds(5),
                MaxBytes = 100,
            }).SweepOnce();
            var after = File.ReadAllBytes(foreign);
            after.Length.ShouldBe(1000, "skips " + Format(receipt) + " reclaimed=" + receipt.ReclaimedBytes);
            after[950].ShouldBe((byte)0x5A);
            receipt.ReclaimedBytes.ShouldBe(0);
            receipt.CompletedRoots.ShouldBe(1, "skips " + Format(receipt));
            Directory.Exists(root).ShouldBeFalse();
        }
        finally { RemoveScratch(sandbox); }
    }

    // A real mount needs root. The listed device is rewritten so the walk stops
    // before MarkDeleting and the marker stays with the root.
    [Test]
    public void mounted_child_keeps_the_root_and_its_marker()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("the mount-boundary witness needs a non-zero Linux device id");
        var sandbox = Scratch();
        try
        {
            var root = Candidate(sandbox);
            var nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "payload.txt"), "still-here");
            var context = new CheckpointCleanupContext(new MountBoundaryIo(), new CheckpointNativeGitRunner());
            var receipt = Sweep(context, sandbox).SweepOnce();
            receipt.CompletedRoots.ShouldBe(0, "skips " + Format(receipt));
            receipt.Skips.GetValueOrDefault("mount-boundary").ShouldBe(1, "skips " + Format(receipt));
            Directory.Exists(root).ShouldBeTrue();
            File.Exists(Path.Combine(root, TestRootGuard.MarkerName)).ShouldBeTrue();
            File.ReadAllText(Path.Combine(nested, "payload.txt")).ShouldBe("still-here");
        }
        finally { RemoveScratch(sandbox); }
    }

    // The leaf is removed after it was listed and before unlinkat. Base File.Delete
    // treated that race as success and still reclaimed the root.
    [Test]
    public void a_leaf_that_vanishes_mid_delete_still_reclaims_the_root()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("the vanish witness uses Linux ENOENT from unlinkat");
        var sandbox = Scratch();
        try
        {
            var root = Candidate(sandbox);
            File.WriteAllText(Path.Combine(root, "payload.txt"), "gone");
            var context = new CheckpointCleanupContext(new VanishingLeafIo(), new CheckpointNativeGitRunner());
            var receipt = Sweep(context, sandbox).SweepOnce();
            receipt.CompletedRoots.ShouldBe(1, "skips " + Format(receipt));
            Directory.Exists(root).ShouldBeFalse();
            File.Exists(Path.Combine(root, "payload.txt")).ShouldBeFalse();
        }
        finally { RemoveScratch(sandbox); }
    }

    // A directory symlink to a mode-000 child must not throw out of SweepOnce.
    // The bad root stays, with its marker, and the other root is still reclaimed.
    [Test]
    public void directory_symlink_to_an_unreadable_child_does_not_abort_other_roots()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("the unreadable directory-symlink witness uses Linux mode bits");
        var sandbox = Scratch();
        string? locked = null;
        try
        {
            var target = Path.Combine(sandbox, "target");
            locked = Path.Combine(target, "locked");
            Directory.CreateDirectory(locked);
            File.WriteAllText(Path.Combine(locked, "hidden.txt"), "secret");
            Chmod(locked, 0).ShouldBe(0, "chmod errno " + Marshal.GetLastPInvokeError());
            var bad = Candidate(sandbox);
            Directory.CreateSymbolicLink(Path.Combine(bad, "out"), target);
            var good = Candidate(sandbox);
            File.WriteAllText(Path.Combine(good, "payload.txt"), "owned");
            var receipt = Sweep(sandbox).SweepOnce();
            receipt.Examined.ShouldBe(2, "skips " + Format(receipt));
            receipt.CompletedRoots.ShouldBe(1, "skips " + Format(receipt));
            receipt.Skips.GetValueOrDefault("inventory-incomplete-or-linked").ShouldBe(1, "skips " + Format(receipt));
            Directory.Exists(good).ShouldBeFalse();
            Directory.Exists(bad).ShouldBeTrue();
            File.Exists(Path.Combine(bad, TestRootGuard.MarkerName)).ShouldBeTrue();
            File.Exists(Path.Combine(sandbox, ".checkpoint-temp-cursor.json")).ShouldBeTrue();
        }
        finally
        {
            if (locked is not null) Chmod(locked, 0x1FF);
            RemoveScratch(sandbox);
        }
    }

    // The outside tree is not the root's content. Listing it would see tool/ and
    // skip as nested-custody-missing, or would change these bytes.
    [Test]
    public void directory_symlink_does_not_read_or_modify_the_outside_tree()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("requires a Linux directory symlink");
        var sandbox = Scratch();
        try
        {
            var outside = Path.Combine(sandbox, "outside");
            var tool = Path.Combine(outside, "tool");
            Directory.CreateDirectory(tool);
            var sentinel = Path.Combine(outside, "kept.txt");
            var toolFile = Path.Combine(tool, "secret.txt");
            File.WriteAllText(sentinel, "foreign-sentinel");
            File.WriteAllText(toolFile, "foreign-tool");
            var bad = Candidate(sandbox);
            var link = Path.Combine(bad, "out");
            Directory.CreateSymbolicLink(link, outside);
            var io = new OutsideReadIo(outside, link);
            var context = new CheckpointCleanupContext(io, new CheckpointNativeGitRunner());
            var receipt = Sweep(context, sandbox).SweepOnce();
            io.OutsideReads.ShouldBe(0, "skips " + Format(receipt));
            File.ReadAllText(sentinel).ShouldBe("foreign-sentinel");
            File.ReadAllText(toolFile).ShouldBe("foreign-tool");
            receipt.Skips.GetValueOrDefault("nested-custody-missing").ShouldBe(0, "skips " + Format(receipt));
            receipt.Skips.GetValueOrDefault("inventory-incomplete-or-linked").ShouldBe(1, "skips " + Format(receipt));
            receipt.CompletedRoots.ShouldBe(0, "skips " + Format(receipt));
            Directory.Exists(bad).ShouldBeTrue();
            File.Exists(Path.Combine(bad, TestRootGuard.MarkerName)).ShouldBeTrue();
            Directory.Exists(link).ShouldBeTrue();
        }
        finally { RemoveScratch(sandbox); }
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string oldPath, string newPath);

    [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
    private static extern int Chmod(string path, int mode);

    private sealed class OutsideReadIo(string outside, string link) : ForwardingNativeIo
    {
        public int OutsideReads { get; private set; }

        public override IReadOnlyList<AnchoredChild> ListAnchored(string directory)
        {
            Note(directory);
            return base.ListAnchored(directory);
        }

        public override IEnumerable<string> Enumerate(string path)
        {
            Note(path);
            return base.Enumerate(path);
        }

        public override bool FileExists(string path)
        {
            Note(path);
            return base.FileExists(path);
        }

        public override bool DirectoryExists(string path)
        {
            Note(path);
            return base.DirectoryExists(path);
        }

        public override Stream Open(string path, FileMode mode, FileAccess access, FileShare share)
        {
            Note(path);
            return base.Open(path, mode, access, share);
        }

        public override ICheckpointReadHandle OpenRead(string path, CheckpointReadOpen request)
        {
            Note(path);
            return base.OpenRead(path, request);
        }

        private void Note(string path)
        {
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return; }
            if (Covers(full, outside) || Covers(full, link)) OutsideReads++;
        }

        private static bool Covers(string full, string root) =>
            full.Equals(root, StringComparison.Ordinal)
            || full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
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

        public override void UnlinkAnchored(string directory, string name, bool directoryEntry, ulong device = 0, ulong inode = 0)
        {
            Swap(Path.Combine(directory, name));
            base.UnlinkAnchored(directory, name, directoryEntry, device, inode);
        }

        public override void TruncateAnchored(string directory, string name, long length)
        {
            Swap(Path.Combine(directory, name));
            base.TruncateAnchored(directory, name, length);
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

    private sealed class MountBoundaryIo : ForwardingNativeIo
    {
        public override IReadOnlyList<AnchoredChild> ListAnchored(string directory)
        {
            var children = base.ListAnchored(directory);
            var rewritten = new AnchoredChild[children.Count];
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child.Kind == "directory")
                {
                    var device = child.Device == 0 ? 2UL : child.Device + 1;
                    child = child with { Device = device };
                }
                rewritten[i] = child;
            }
            return rewritten;
        }
    }

    private sealed class VanishingLeafIo : ForwardingNativeIo
    {
        public override void UnlinkAnchored(string directory, string name, bool directoryEntry, ulong device = 0, ulong inode = 0)
        {
            if (!directoryEntry && name == "payload.txt")
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path)) File.Delete(path);
            }
            base.UnlinkAnchored(directory, name, directoryEntry, device, inode);
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
