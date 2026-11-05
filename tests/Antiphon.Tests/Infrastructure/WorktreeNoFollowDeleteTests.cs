using System.Runtime.InteropServices;
using Antiphon.Server.Infrastructure.Git;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-1717 D-5: the Linux no-follow delete must not unlink through a swapped directory entry.</summary>
[Category("Unit")]
public sealed class WorktreeNoFollowDeleteTests
{
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

        var root = Path.Combine(Path.GetTempPath(), "c1717-b3-marker-" + Guid.NewGuid().ToString("N"));
        var tree = Path.Combine(root, "tree");
        var child = Path.Combine(tree, "child");
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
                File.WriteAllText(Path.Combine(child, ".git"), "gitdir: private-repo\n");
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
            File.Exists(Path.Combine(child, ".git")).ShouldBeTrue();
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

    // CARD-1717 B-2: Linux reuses a freed directory inode at once. A directory created later at the same
    // inode must not carry the recorded identity, or a replacement would inherit the receipt.
    [Test]
    public async Task C1717_B2_ReusedInodeIsANewDirectoryIdentity()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("Inode reuse is the Linux identity; Windows file ids are a separate native witness.");
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "c1717-b2-inode-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "tree");
        Directory.CreateDirectory(root);
        try
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                Directory.CreateDirectory(path);
                var recorded = WorktreeNoFollowDelete.DirectoryIdentity(path);
                Directory.Delete(path);
                // A later clock tick: the coarse file-system clock can stamp two creations alike.
                await Task.Delay(30);
                Directory.CreateDirectory(path);
                var replacement = WorktreeNoFollowDelete.DirectoryIdentity(path);
                Directory.Delete(path);
                if (InodeOf(recorded) != InodeOf(replacement)) continue;
                replacement.ShouldNotBe(recorded, "a reused inode must not match the recorded directory");
                return;
            }
            throw new InvalidOperationException("the file system never reused the directory inode in 50 attempts");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        static string InodeOf(string identity) => identity.Split(':')[3];
    }

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

        private static RemovalApproval.Listing Approve(string tree)
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
