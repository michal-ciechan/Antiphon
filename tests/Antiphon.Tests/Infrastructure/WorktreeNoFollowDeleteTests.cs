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
}
