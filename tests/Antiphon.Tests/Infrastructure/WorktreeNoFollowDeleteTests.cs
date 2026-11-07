using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Microsoft.Win32.SafeHandles;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-1717 D-5: the Linux no-follow delete must not unlink through a swapped directory entry.</summary>
[Category("Integration")]
public sealed class WorktreeNoFollowDeleteTests
{
    [Test]
    [Timeout(5_000)]
    [NativeLinuxOnly("Linux-only by construction: real statx, no-follow directory descriptors and unlinkat are required; Windows has a separate native witness.")]
    public void C1737_PostHashWriteSurvivesFinalStat(CancellationToken cancellationToken)
    {
        using var timing = TestTiming.BeginPhase(TestPhaseKind.Filesystem, "run");
        var boundary = new PostHashBoundary();
        var root = Path.Combine(Path.GetTempPath(), "c1737-final-stat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "approved.txt");
        byte[] original = "approved original bytes"u8.ToArray();
        byte[] changed = "new bytes written after the completed hash, through the same inode"u8.ToArray();
        try
        {
            File.WriteAllBytes(path, original);
            using var parent = boundary.Open(root);
            var listed = boundary.Stat(parent, "approved.txt");
            var (hash, hashed) = boundary.Hash(parent, "approved.txt", listed);
            hash.ShouldNotBeNullOrEmpty();
            hash.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(original)));
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                writer.SetLength(changed.Length);
                writer.Write(changed);
                writer.Flush(flushToDisk: true);
            }
            var fresh = boundary.Stat(parent, "approved.txt");
            boundary.SameNode(listed, hashed).ShouldBeTrue();
            boundary.SameNode(hashed, fresh).ShouldBeTrue();
            boundary.Size(fresh).ShouldNotBe(boundary.Size(hashed));
            boundary.SameContent(hashed, fresh).ShouldBeFalse();
            Console.WriteLine("reached:W-A23:hashed-then-same-inode-write");

            var error = boundary.Unlink(parent, "approved.txt", listed, hashed);
            File.Exists(path).ShouldBeTrue("preserved:W-A23:post-hash-file");
            File.ReadAllBytes(path).ShouldBe(changed);
            error.ShouldBeOfType<ApprovedTreeRejectedException>(error?.ToString()).Reason.ShouldBe("set_aside_content_changed");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // Bind the real private signatures, including their in/out and nullable stat types.
    // The adapter never fabricates native layouts or replaces the captured hash-ending stat.
    private sealed class PostHashBoundary
    {
        private readonly Type stat = typeof(WorktreeNoFollowDelete).GetNestedType("LinuxStatx", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("LinuxStatx boundary missing");
        private MethodInfo Bind(string name, params Type[] parameters) =>
            typeof(WorktreeNoFollowDelete).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static, parameters)
            ?? throw new InvalidOperationException("native boundary missing: " + name);
        public SafeFileHandle Open(string path) => (SafeFileHandle)Bind("OpenLinuxDirectory", typeof(string)).Invoke(null, [path])!;
        public object Stat(SafeFileHandle parent, string name)
        {
            var fd = Bind("Fd", typeof(SafeFileHandle)).Invoke(null, [parent]);
            object?[] args = [fd, name, 0x100, null];
            ((bool)Bind("TryStat", typeof(int), typeof(string), typeof(int), stat.MakeByRefType()).Invoke(null, args)!)
                .ShouldBeTrue("real statx must succeed");
            return args[3].ShouldNotBeNull();
        }
        public (string Hash, object Ended) Hash(SafeFileHandle parent, string name, object listed)
        {
            object?[] args = [parent, name, listed, null];
            var hash = (string?)Bind("HashAt", typeof(SafeFileHandle), typeof(string), stat.MakeByRefType(),
                typeof(Nullable<>).MakeGenericType(stat).MakeByRefType()).Invoke(null, args);
            return (hash.ShouldNotBeNull(), args[3].ShouldNotBeNull());
        }
        public bool SameNode(object a, object b) => Compare("SameNode", a, b);
        public bool SameContent(object a, object b) => Compare("SameContent", a, b);
        private bool Compare(string name, object a, object b) =>
            (bool)Bind(name, stat.MakeByRefType(), stat.MakeByRefType()).Invoke(null, [a, b])!;
        public ulong Size(object value) => (ulong)stat.GetField("Size")!.GetValue(value)!;
        public Exception? Unlink(SafeFileHandle parent, string name, object listed, object hashed)
        {
            var method = Bind("UnlinkSameNode", typeof(SafeFileHandle), typeof(string), stat, typeof(Nullable<>).MakeGenericType(stat));
            try { method.Invoke(null, [parent, name, listed, hashed]); return null; }
            catch (TargetInvocationException ex) when (ex.InnerException is not null) { return ex.InnerException; }
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int renameat2(int olddirfd, string oldpath, int newdirfd, string newpath, uint flags);

    [Test]
    public void C1717_D5_ExchangedSymlinkDoesNotDeleteOutsideFiles()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("The ancestor exchange uses Linux renameat2.");
            return;
        }

        const int files = 80;
        const int attempts = 24;
        var root = Path.Combine(Path.GetTempPath(), "c1717-d5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        long totalSwaps = 0;
        try
        {
            for (var n = 0; n < attempts; n++)
            {
                var caseRoot = Path.Combine(root, "case-" + n);
                var tree = Path.Combine(caseRoot, "tree");
                var child = Path.Combine(tree, "child");
                var outside = Path.Combine(caseRoot, "outside");
                var exchange = Path.Combine(caseRoot, "exchange");
                Directory.CreateDirectory(child);
                Directory.CreateDirectory(outside);
                for (var i = 0; i < files; i++)
                {
                    File.WriteAllText(Path.Combine(child, "f" + i), "disposable");
                    File.WriteAllText(Path.Combine(outside, "f" + i), "precious-outside");
                }
                Directory.CreateSymbolicLink(exchange, outside);
                using var cts = new CancellationTokenSource();
                long swaps = 0;
                var writer = new Thread(() =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        if (renameat2(-100, child, -100, exchange, 2) == 0) Interlocked.Increment(ref swaps);
                        Thread.SpinWait(8);
                    }
                });
                writer.Start();
                Exception? error = null;
                try { WorktreeNoFollowDelete.Delete(tree, preserveNestedRepositories: true); }
                catch (Exception ex) { error = ex; }
                finally
                {
                    cts.Cancel();
                    writer.Join();
                }
                totalSwaps += swaps;
                Directory.GetFiles(outside).Length.ShouldBe(files,
                    $"attempt {n} swaps {swaps} removed outside files ({error})");
            }
            totalSwaps.ShouldBeGreaterThan(0, "the exchanger never swapped a directory with the outside symlink");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // CARD-1717 B-3: a repository marker that appears after the walk has listed a directory must
    // stop the walk. The Linux deleter used to check markers once, then unlink the rest.
    [Test]
    public void C1717_B3_LateRepositoryMarkerStopsTheWalk()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("The late-marker walk is the Linux no-follow deleter.");
            return;
        }

        AssertLateMarkerStopsTheWalk("child", "child");
    }

    // CARD-1717 B-3: the marker can appear in a directory above the one being walked. That directory's
    // working tree includes the subtree, so the walk below it must stop as well.
    [Test]
    public void C1717_B3_LateRepositoryMarkerAboveTheWalkStopsIt()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("The late-marker walk is the Linux no-follow deleter.");
            return;
        }

        AssertLateMarkerStopsTheWalk("parent/child", "parent");
    }

    private static void AssertLateMarkerStopsTheWalk(string childRelative, string markerRelative)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1717-b3-marker-" + Guid.NewGuid().ToString("N"));
        var tree = Path.Combine(root, "tree");
        var child = Path.Combine(tree, childRelative);
        var marked = Path.Combine(tree, markerRelative);
        Directory.CreateDirectory(child);
        const int count = 20000;
        for (var i = 0; i < count; i++)
            File.WriteAllText(Path.Combine(child, "f" + i), "nested repository working bytes");
        var observed = Directory.EnumerateFiles(child).First();
        var remainingAtMarker = -1;
        var wrote = false;
        Exception? writerError = null;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var writer = new Thread(() =>
        {
            try
            {
                while (File.Exists(observed) && !stop.IsCancellationRequested) Thread.Yield();
                if (stop.IsCancellationRequested) return;
                File.WriteAllText(Path.Combine(marked, ".git"), "gitdir: private-repo\n");
                wrote = true;
                remainingAtMarker = Directory.GetFiles(child, "f*").Length;
            }
            catch (Exception ex) { writerError = ex; }
        });
        writer.Start();
        Exception? error = null;
        try { WorktreeNoFollowDelete.Delete(tree, preserveNestedRepositories: true); }
        catch (Exception ex) { error = ex; }
        finally { stop.Cancel(); writer.Join(); }
        try
        {
            writerError.ShouldBeNull(writerError?.ToString());
            wrote.ShouldBeTrue("the marker writer never observed a deletion");
            remainingAtMarker.ShouldBeGreaterThan(1000);
            var remaining = Directory.Exists(child) ? Directory.GetFiles(child, "f*").Length : 0;
            var lost = remainingAtMarker - remaining;
            lost.ShouldBeLessThanOrEqualTo(8, $"deleted {lost} files after the marker appeared ({error})");
            File.Exists(Path.Combine(marked, ".git")).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // CARD-1717 B-3: under a receipt, a name created in a directory the walk has not listed yet is refused
    // when that directory is listed, before any of its approved entries is unlinked.
    [Test]
    public void C1717_B3_NameAddedToAnUnwalkedDirectoryHolds()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("The mid-walk receipt checks are the Linux no-follow deleter; Windows needs a native witness.");
            return;
        }

        using var world = new WalkWorld();
        var outcome = world.DeleteWhileWalking((walked, other) =>
        {
            var late = Path.Combine(other, "late-only-copy.txt");
            File.WriteAllText(late, "created after the receipt");
            return late;
        });
        outcome.Error.ShouldBeOfType<ApprovedTreeRejectedException>(outcome.Error?.ToString())
            .Reason.ShouldBe("set_aside_content_changed");
        File.ReadAllText(outcome.Late).ShouldBe("created after the receipt");
        Directory.GetFiles(outcome.Other, "f*").Length.ShouldBe(WalkWorld.Files,
            "no approved entry beside the unapproved name is unlinked");
    }

    // CARD-1717 B-3: a name created in the directory being walked is never unlinked; the emptied directory
    // is listed again and the walk stops as a receipt refusal, not as a failed rmdir.
    [Test]
    public void C1717_B3_NameAddedToTheWalkedDirectoryHolds()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("The mid-walk receipt checks are the Linux no-follow deleter; Windows needs a native witness.");
            return;
        }

        using var world = new WalkWorld();
        var outcome = world.DeleteWhileWalking((walked, _) =>
        {
            var late = Path.Combine(walked, "late-only-copy.txt");
            File.WriteAllText(late, "created after the listing");
            return late;
        });
        outcome.Error.ShouldBeOfType<ApprovedTreeRejectedException>(outcome.Error?.ToString())
            .Reason.ShouldBe("set_aside_content_changed");
        File.ReadAllText(outcome.Late).ShouldBe("created after the listing");
    }

    // CARD-1717 B-3: an approved file rewritten during the walk is hashed again just before its unlink
    // and kept with its new bytes.
    [Test]
    public void C1717_B3_BytesChangedDuringTheWalkAreKept()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("The mid-walk receipt checks are the Linux no-follow deleter; Windows needs a native witness.");
            return;
        }

        using var world = new WalkWorld();
        var outcome = world.DeleteWhileWalking((_, other) =>
        {
            var changed = Directory.EnumerateFiles(other, "f*").Last();
            File.WriteAllText(changed, "CHANGED-bytes-mid-walk!!");
            return changed;
        });
        outcome.Error.ShouldBeOfType<ApprovedTreeRejectedException>(outcome.Error?.ToString())
            .Reason.ShouldBe("set_aside_content_changed");
        File.ReadAllText(outcome.Late).ShouldBe("CHANGED-bytes-mid-walk!!");
    }

    // CARD-1717 B-2: Linux reuses a freed directory inode. A directory created later at the same inode must
    // not carry the recorded identity, or a replacement would inherit the receipt.
    [Test]
    public void C1717_B2_ReusedInodeIsANewDirectoryIdentity()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("Inode reuse is the Linux identity; Windows file ids are a separate native witness.");
            return;
        }

        // A private parent a few levels down keeps other writers out of the freed inode's group.
        var root = Path.Combine(Path.GetTempPath(), "c1717-b2-inode-" + Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(root, "a", "b");
        var path = Path.Combine(parent, "tree");
        Directory.CreateDirectory(parent);
        try
        {
            for (var attempt = 0; attempt < Attempts; attempt++)
            {
                Directory.CreateDirectory(path);
                var recorded = WorktreeNoFollowDelete.DirectoryIdentity(path);
                Directory.Delete(path);
                // File-system timestamps come from the coarse real-time clock. Once it has ticked, a new
                // directory's birth time differs from the recorded one; the wait itself touches no file.
                NextCoarseTick();
                Directory.CreateDirectory(path);
                var replacement = WorktreeNoFollowDelete.DirectoryIdentity(path);
                Directory.Delete(path);
                if (InodeOf(recorded) != InodeOf(replacement)) continue;
                replacement.ShouldNotBe(recorded, "a reused inode must not match the recorded directory");
                return;
            }
            throw new InvalidOperationException($"the file system never reused the directory inode in {Attempts} attempts");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        static string InodeOf(string identity) => identity.Split(':')[3];
    }

    private const int Attempts = 400;

    // CARD-1717 A-B-3 (Review ea048200): a writer that opened the file before the walk overwrites bytes the
    // per-unlink hash has already read. The hash still matches the receipt, so only a write-stability check on
    // the hashed descriptor tells the bytes on disk from the bytes hashed. The new bytes must survive.
    [Test]
    public void C1717_B3_WriteThroughAnOpenHandleDuringTheHashIsKept()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("The inotify-timed writer is Linux; the Windows deny-write hash is C1717_B3_WindowsOpenWriterRefusesTheDelete.");
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "c1717-b3-openfd-" + Guid.NewGuid().ToString("N"));
        var tree = Path.Combine(root, "tree");
        var big = Path.Combine(tree, "big.bin");
        Directory.CreateDirectory(tree);
        try
        {
            // Large enough that hashing it outlasts the observer's reaction by a wide margin.
            var block = new byte[1 << 20];
            Array.Fill(block, (byte)'a');
            using (var create = new FileStream(big, FileMode.CreateNew, FileAccess.Write))
                for (var i = 0; i < 64; i++) create.Write(block);
            var listing = WalkWorld.Approve(tree);
            var marker = "CHANGED-through-an-open-handle"u8.ToArray();
            using var writer = new FileStream(big, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var notify = inotify_init1(InNonBlock | InCloExec);
            if (notify < 0) throw new IOException("inotify_init1 failed: " + Marshal.GetLastPInvokeError());
            Exception? writerError = null;
            var wrote = false;
            var opens = 0;
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                if (inotify_add_watch(notify, big, InOpen | InAccess) < 0)
                    throw new IOException("inotify_add_watch failed: " + Marshal.GetLastPInvokeError());
                var observer = new Thread(() =>
                {
                    try
                    {
                        var buffer = new byte[4096];
                        while (!stop.IsCancellationRequested)
                        {
                            var read = (int)read_events(notify, buffer, buffer.Length);
                            if (read <= 0) { Thread.Yield(); continue; }
                            for (var offset = 0; offset + 16 <= read;)
                            {
                                var mask = BitConverter.ToUInt32(buffer, offset + 4);
                                var length = BitConverter.ToInt32(buffer, offset + 12);
                                offset += 16 + length;
                                if ((mask & InOpen) != 0) opens++;
                                // Open 1 is the drift census, open 2 the per-unlink hash. Its first read has happened.
                                if ((mask & InAccess) == 0 || opens < 2) continue;
                                writer.Seek(0, SeekOrigin.Begin);
                                writer.Write(marker);
                                writer.Flush(flushToDisk: true);
                                wrote = true;
                                return;
                            }
                        }
                    }
                    catch (Exception ex) { writerError = ex; }
                });
                observer.Start();
                Exception? error = null;
                var previous = RemovalApproval.Current.Value;
                RemovalApproval.Current.Value = listing;
                try { WorktreeNoFollowDelete.Delete(tree, preserveNestedRepositories: true); }
                catch (Exception ex) { error = ex; }
                finally
                {
                    RemovalApproval.Current.Value = previous;
                    stop.Cancel();
                    observer.Join();
                }
                writerError.ShouldBeNull(writerError?.ToString());
                wrote.ShouldBeTrue($"the writer never saw the per-unlink hash read (opens={opens})");
                File.Exists(big).ShouldBeTrue("a file written while it was hashed is not unlinked");
                error.ShouldBeOfType<ApprovedTreeRejectedException>(error?.ToString()).Reason.ShouldBe("set_aside_content_changed");
                using var check = new FileStream(big, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var head = new byte[marker.Length];
                check.ReadExactly(head);
                head.ShouldBe(marker, "the bytes written through the open handle survive");
            }
            finally { close(notify); }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // CARD-1717 A-B-3 Windows twin (pending native execution, B-6): the per-entry hash shares no write, so a handle
    // already open for writing makes the hash open fail and the delete stops with the file in place.
    [Test]
    public void C1717_B3_WindowsOpenWriterRefusesTheDelete()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Share modes are the Windows no-follow delete; Linux is C1717_B3_WriteThroughAnOpenHandleDuringTheHashIsKept.");
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "c1717-b3-winwriter-" + Guid.NewGuid().ToString("N"));
        var tree = Path.Combine(root, "tree");
        var file = Path.Combine(tree, "held.txt");
        Directory.CreateDirectory(tree);
        try
        {
            File.WriteAllText(file, "approved-bytes");
            var listing = WalkWorld.Approve(tree);
            Exception? error = null;
            using (var writer = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                var previous = RemovalApproval.Current.Value;
                RemovalApproval.Current.Value = listing;
                try { WorktreeNoFollowDelete.Delete(tree, preserveNestedRepositories: true); }
                catch (Exception ex) { error = ex; }
                finally { RemovalApproval.Current.Value = previous; }
                RewriteOpenWriter(writer);
            }
            error.ShouldBeAssignableTo<IOException>(error?.ToString());
            File.Exists(file).ShouldBeTrue("a file open for writing is not deleted");
            File.ReadAllText(file).ShouldBe("later-bytes");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void C1717_B3_OpenWriterRewriteTruncatesEarlierBytes()
    {
        using var timing = TestTiming.BeginPhase(TestPhaseKind.Filesystem, "run");
        var root = Path.Combine(Path.GetTempPath(), "c1717-b3-writer-bytes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "held.txt");
        try
        {
            File.WriteAllText(file, "approved-bytes");
            using (var writer = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                writer.Write("later-bytes"u8);
                writer.Flush();
                ReadWhileOpenWriter(file).ShouldBe("later-bytestes",
                    "overwriting through an open handle preserves the earlier tail until truncated");
                writer.Position = 0;
                RewriteOpenWriter(writer);
            }
            File.ReadAllText(file).ShouldBe("later-bytes");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void C1717_B3_OpenWriterReadShareAdmitsTheWriter()
    {
        OpenWriterReadShare.HasFlag(FileShare.Write).ShouldBeTrue(
            "NTFS refuses FileShare.Read while the open writer still has write access");
    }

    // File.ReadAllText opens FileShare.Read. NTFS refuses that while the writer still has write access.
    private static FileShare OpenWriterReadShare => FileShare.Read;

    private static string ReadWhileOpenWriter(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, OpenWriterReadShare);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void RewriteOpenWriter(FileStream writer)
    {
        // FileMode.Open preserves the old length when the replacement is shorter.
        writer.SetLength(0);
        writer.Write("later-bytes"u8);
    }

    private const int InNonBlock = 0x800;
    private const int InCloExec = 0x80000;
    private const uint InAccess = 0x1;
    private const uint InOpen = 0x20;

    [DllImport("libc", SetLastError = true)]
    private static extern int inotify_init1(int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int inotify_add_watch(int fd, string path, uint mask);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint read_events(int fd, byte[] buffer, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    private static void NextCoarseTick()
    {
        if (clock_gettime(ClockRealtimeCoarse, out var start) != 0) throw new IOException("clock_gettime failed");
        Timespec now;
        do
        {
            Thread.SpinWait(64);
            if (clock_gettime(ClockRealtimeCoarse, out now) != 0) throw new IOException("clock_gettime failed");
        }
        while (now.Seconds == start.Seconds && now.Nanoseconds == start.Nanoseconds);
    }

    private const int ClockRealtimeCoarse = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public long Seconds;
        public long Nanoseconds;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int clock_gettime(int clock, out Timespec time);

    /// <summary>Two approved directories of many files under a receipt, deleted while a writer acts once the walk is inside one.</summary>
    private sealed class WalkWorld : IDisposable
    {
        public const int Files = 4000;
        private readonly string root = Path.Combine(Path.GetTempPath(), "c1717-b3-walk-" + Guid.NewGuid().ToString("N"));
        private readonly string tree;
        private readonly string[] directories;

        public WalkWorld()
        {
            tree = Path.Combine(root, "tree");
            directories = [Path.Combine(tree, "a"), Path.Combine(tree, "b")];
            foreach (var directory in directories)
            {
                Directory.CreateDirectory(directory);
                for (var i = 0; i < Files; i++)
                    File.WriteAllText(Path.Combine(directory, "f" + i), "approved-working-bytes");
            }
        }

        public sealed record Outcome(Exception? Error, string Walked, string Other, string Late);

        /// <summary>
        /// Deletes under a receipt of the current tree. When the first approved file of either directory
        /// goes, <paramref name="act"/> runs once with that directory and the one not listed yet.
        /// </summary>
        public Outcome DeleteWhileWalking(Func<string, string, string> act)
        {
            var probes = directories.Select(d => Directory.EnumerateFiles(d).First()).ToArray();
            string? walked = null, other = null, late = null;
            Exception? writerError = null;
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var writer = new Thread(() =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        var gone = Array.FindIndex(probes, p => !File.Exists(p));
                        if (gone < 0) { Thread.Yield(); continue; }
                        walked = directories[gone];
                        other = directories[1 - gone];
                        late = act(walked, other);
                        return;
                    }
                }
                catch (Exception ex) { writerError = ex; }
            });
            var listing = Approve(tree);
            writer.Start();
            Exception? error = null;
            var previous = RemovalApproval.Current.Value;
            RemovalApproval.Current.Value = listing;
            try { WorktreeNoFollowDelete.Delete(tree, preserveNestedRepositories: true); }
            catch (Exception ex) { error = ex; }
            finally
            {
                RemovalApproval.Current.Value = previous;
                stop.Cancel();
                writer.Join();
            }
            writerError.ShouldBeNull(writerError?.ToString());
            late.ShouldNotBeNull("the writer never observed the walk inside an approved directory");
            return new(error, walked!, other!, late);
        }

        internal static RemovalApproval.Listing Approve(string tree)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in Directory.EnumerateFileSystemEntries(tree, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(tree, entry).Replace(Path.DirectorySeparatorChar, '/');
                names.Add(relative);
                var directory = Directory.Exists(entry);
                kinds[relative] = directory ? "dir" : "file";
                if (!directory) hashes[relative] = WorktreeNoFollowDelete.RegularFileHash(entry)!;
            }
            return new RemovalApproval.Listing { Names = names, Kinds = kinds, Hashes = hashes };
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
