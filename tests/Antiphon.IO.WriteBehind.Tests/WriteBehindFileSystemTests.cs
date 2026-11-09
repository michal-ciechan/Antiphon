using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Antiphon.IO.WriteBehind;
using Shouldly;

namespace Antiphon.IO.WriteBehind.Tests;

/// <summary>
/// The write-behind file system: queued writes are readable at once, land in order with only the newest
/// content of each path, and every other operation sees them; on a real disk the bytes match System.IO.
/// </summary>
[Category("Unit")]
public class WriteBehindFileSystemTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\data" : "/data";

    private static (MockFileSystem Disk, WriteBehindFileSystem Fs) Create()
    {
        var disk = new MockFileSystem();
        disk.Directory.CreateDirectory(Root);
        return (disk, new WriteBehindFileSystem(disk));
    }

    private static string At(string name) => Path.Combine(Root, name);

    /// <summary>Holds the writer thread before the named path lands, until released.</summary>
    private sealed class Hold : IDisposable
    {
        private readonly ManualResetEventSlim release = new(false);
        public readonly ManualResetEventSlim Reached = new(false);

        public Hold(WriteBehindFileSystem fs, string path)
        {
            fs.Queue.BeforeWrite = p =>
            {
                if (!string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) return;
                Reached.Set();
                release.Wait(TimeSpan.FromSeconds(30)).ShouldBeTrue("hold was never released");
            };
            Reached.Wait(TimeSpan.Zero);
        }

        public void WaitReached() => Reached.Wait(TimeSpan.FromSeconds(30)).ShouldBeTrue("writer never reached the held path");

        public void Dispose() => release.Set();
    }

    [Test]
    public async Task QueuedWriteIsReadableBeforeItLands()
    {
        var (disk, fs) = Create();
        using (var hold = new Hold(fs, At("a.json")))
        {
            fs.File.WriteAllText(At("a.json"), "new");
            hold.WaitReached();
            disk.File.Exists(At("a.json")).ShouldBeFalse();
            fs.File.Exists(At("a.json")).ShouldBeTrue();
            fs.File.ReadAllText(At("a.json")).ShouldBe("new");
            fs.File.ReadAllBytes(At("a.json")).ShouldBe(Encoding.UTF8.GetBytes("new"));
            fs.File.ReadAllLines(At("a.json")).ShouldBe(new[] { "new" });
            (await fs.File.ReadAllTextAsync(At("a.json"))).ShouldBe("new");
            fs.PendingWrites.ShouldBe(1);
            fs.PendingLength(At("a.json")).ShouldBe(3);
            fs.PendingLength(At("other.json")).ShouldBeNull();
        }
        fs.Flush();
        disk.File.ReadAllText(At("a.json")).ShouldBe("new");
        fs.PendingWrites.ShouldBe(0);
        fs.PendingLength(At("a.json")).ShouldBeNull();
    }

    [Test]
    public async Task RewritesKeepOnlyTheNewestContentInProgramOrder()
    {
        var (disk, fs) = Create();
        var landed = new List<string>();
        fs.Queue.AfterWrite = p => { lock (landed) landed.Add(Path.GetFileName(p)); };
        using (var hold = new Hold(fs, At("x.json")))
        {
            fs.File.WriteAllText(At("x.json"), "x");
            hold.WaitReached();
            fs.File.WriteAllText(At("a.json"), "a1");
            fs.File.WriteAllText(At("b.json"), "b");
            fs.File.WriteAllText(At("a.json"), "a2");
            fs.File.ReadAllText(At("a.json")).ShouldBe("a2");
        }
        fs.Flush();
        landed.ShouldBe(new[] { "x.json", "b.json", "a.json" });
        disk.File.ReadAllText(At("a.json")).ShouldBe("a2");
        disk.Directory.GetFiles(Root).Select(Path.GetFileName).OrderBy(n => n).ShouldBe(new[] { "a.json", "b.json", "x.json" });
        await Task.CompletedTask;
    }

    [Test]
    public async Task DeleteDropsTheQueuedWrite()
    {
        var (disk, fs) = Create();
        var landed = new List<string>();
        fs.Queue.AfterWrite = p => { lock (landed) landed.Add(Path.GetFileName(p)); };
        using (var hold = new Hold(fs, At("x.json")))
        {
            fs.File.WriteAllText(At("x.json"), "x");
            hold.WaitReached();
            fs.File.WriteAllText(At("gone.json"), "never");
            fs.File.Delete(At("gone.json"));
            fs.File.Exists(At("gone.json")).ShouldBeFalse();
        }
        fs.Flush();
        landed.ShouldBe(new[] { "x.json" });
        disk.File.Exists(At("gone.json")).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task OtherOperationsSeeTheQueuedWrite()
    {
        var (disk, fs) = Create();
        fs.File.WriteAllText(At("list.json"), "listed");
        fs.Directory.GetFiles(Root).Select(Path.GetFileName).ShouldBe(new[] { "list.json" });

        fs.File.WriteAllText(At("log.txt"), "one;");
        fs.File.AppendAllText(At("log.txt"), "two;");
        fs.File.ReadAllText(At("log.txt")).ShouldBe("one;two;");

        fs.File.WriteAllText(At("info.txt"), "12345");
        fs.FileInfo.New(At("info.txt")).Length.ShouldBe(5);

        fs.File.WriteAllText(At("src.txt"), "moved");
        fs.File.Move(At("src.txt"), At("dst.txt"));
        disk.File.ReadAllText(At("dst.txt")).ShouldBe("moved");
        fs.File.Exists(At("src.txt")).ShouldBeFalse();

        fs.File.WriteAllText(At("stream.txt"), "streamed");
        using (var reader = new StreamReader(fs.File.OpenRead(At("stream.txt")))) reader.ReadToEnd().ShouldBe("streamed");
        await Task.CompletedTask;
    }

    [Test]
    public async Task FailedWriteIsRaisedByTheNextFlushOnly()
    {
        var (disk, fs) = Create();
        fs.Queue.BeforeWrite = p => { if (p.EndsWith("bad.json", StringComparison.Ordinal)) throw new IOException("disk said no"); };
        fs.File.WriteAllText(At("bad.json"), "lost");
        fs.File.WriteAllText(At("good.json"), "kept");
        var thrown = Should.Throw<AggregateException>(() => fs.Flush());
        thrown.InnerExceptions.Single().ShouldBeOfType<WriteBehindException>().Path.ShouldBe(At("bad.json"));
        disk.File.Exists(At("bad.json")).ShouldBeFalse();
        disk.File.ReadAllText(At("good.json")).ShouldBe("kept");
        fs.Flush();
        await Task.CompletedTask;
    }

    [Test]
    public async Task WriteIntoAMissingDirectoryIsRefusedAtOnce()
    {
        var (_, fs) = Create();
        Should.Throw<DirectoryNotFoundException>(() => fs.File.WriteAllText(Path.Combine(Root, "missing", "a.json"), "x"));
        Should.Throw<UnauthorizedAccessException>(() => fs.File.WriteAllText(Root, "x"));
        fs.PendingWrites.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task DisposeLandsEverythingAndRefusesLaterWrites()
    {
        var (disk, fs) = Create();
        for (int i = 0; i < 200; i++) fs.File.WriteAllText(At("f" + (i % 20) + ".json"), "v" + i);
        fs.Dispose();
        for (int i = 0; i < 20; i++) disk.File.ReadAllText(At("f" + i + ".json")).ShouldBe("v" + (180 + i));
        Should.Throw<ObjectDisposedException>(() => fs.File.WriteAllText(At("late.json"), "x"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task OnDiskBytesAndReadsMatchSystemIo()
    {
        string dir = Path.Combine(Path.GetTempPath(), "write-behind-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var fs = new WriteBehindFileSystem(new FileSystem());
            var cases = new (string Name, Action<string> Expected, Action<string> Actual)[]
            {
                ("text", p => File.WriteAllText(p, "h\u00e9llo\n"), p => fs.File.WriteAllText(p, "h\u00e9llo\n")),
                ("empty", p => File.WriteAllText(p, ""), p => fs.File.WriteAllText(p, "")),
                ("utf8-bom", p => File.WriteAllText(p, "bom", Encoding.UTF8), p => fs.File.WriteAllText(p, "bom", Encoding.UTF8)),
                ("utf8-bom-empty", p => File.WriteAllText(p, "", Encoding.UTF8), p => fs.File.WriteAllText(p, "", Encoding.UTF8)),
                ("unicode", p => File.WriteAllText(p, "wide", Encoding.Unicode), p => fs.File.WriteAllText(p, "wide", Encoding.Unicode)),
                ("lines", p => File.WriteAllLines(p, new[] { "a", "b" }), p => fs.File.WriteAllLines(p, new[] { "a", "b" })),
                ("lines-bom", p => File.WriteAllLines(p, new[] { "a" }, Encoding.UTF8), p => fs.File.WriteAllLines(p, new[] { "a" }, Encoding.UTF8)),
                ("lines-empty-bom", p => File.WriteAllLines(p, Array.Empty<string>(), Encoding.UTF8), p => fs.File.WriteAllLines(p, Array.Empty<string>(), Encoding.UTF8)),
                ("bytes", p => File.WriteAllBytes(p, new byte[] { 0, 1, 255 }), p => fs.File.WriteAllBytes(p, new byte[] { 0, 1, 255 })),
            };
            foreach (var c in cases)
            {
                string expected = Path.Combine(dir, c.Name + ".expected");
                string actual = Path.Combine(dir, c.Name + ".actual");
                c.Expected(expected);
                File.WriteAllText(actual, "previous content that is longer than the new one");
                c.Actual(actual);
                fs.File.ReadAllText(actual).ShouldBe(File.ReadAllText(expected), c.Name + " read from the queue");
                fs.File.ReadAllLines(actual).ShouldBe(File.ReadAllLines(expected), c.Name + " lines from the queue");
                fs.Flush();
                File.ReadAllBytes(actual).ShouldBe(File.ReadAllBytes(expected), c.Name + " bytes on disk");
            }
            Directory.GetFiles(dir, "*.tmp").ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
        await Task.CompletedTask;
    }
}
