using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// The reader scripts/lib/checkpoint-file-reader.ps1 embeds (its last loader, scripts/lib/checkpoint-process-runner.ps1,
/// went in unit 62 with the C487 harness that loaded it), compiled from that file's source by <see cref="EmbeddedCheckpointReader"/> and driven in process.
/// Unit 20 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md moved these cases here from the deleted pwsh
/// drivers: CARD-1532's digest bounds, stalls and end-of-file checks (test-checkpoint-large-stamp.ps1 -Mode reader),
/// CARD-1363's re-armed stall window, overall deadline and Windows open (test-checkpoint-reader-deadline.ps1), CARD-1573's
/// content bound and growth refusal (test-checkpoint-output-inventory.ps1) and the compiled facade's member parity
/// (test-checkpoint-compiled-facade.ps1). Every clock is manual: a stream the test injects moves it as it answers.
/// </summary>
[Category("Unit")]
public sealed class EmbeddedCheckpointReaderTests : CheckpointTestBase
{
    private const long Bound = 1L << 30;
    private const int ReceiptLimit = 64 * 1024 * 1024;
    private static readonly DateTimeOffset Epoch = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void C1573_content_bound_is_one_gibibyte()
    {
        EmbeddedCheckpointReader.Reader.GetField("ContentLimit")!.GetValue(null).ShouldBe(Bound);
        EmbeddedCheckpointReader.Reader.Assembly.Location.ShouldBeEmpty("the embedded reader, not a loaded tool assembly");
    }

    // C1532 (reader mode of the deleted large-stamp driver): the digest streams up to the inclusive 1 GiB bound and the
    // receipt read to its 64 MiB cap in bounded chunks with no timer, refuses an initial oversize or growth past the bound
    // after exactly one extra byte, re-arms its silence window on progress, keeps a caller cancel a cancellation, and
    // refuses an end of file whose size changed or whose handle cannot be re-inspected.
    [Test]
    [Arguments("bound")]
    [Arguments("oversize")]
    [Arguments("growth")]
    [Arguments("receipt")]
    [Arguments("receipt-over")]
    [Arguments("progress")]
    [Arguments("stall")]
    [Arguments("cancel-before")]
    [Arguments("cancel")]
    [Arguments("eof")]
    [Arguments("grow")]
    [Arguments("shrink")]
    [Arguments("inspect")]
    public void C1532_digest_limits_stalls_and_eof(string mode)
    {
        var root = TempDir();
        var path = Path.Combine(root, "reader.bin");
        long size = mode is "bound" or "oversize" or "growth" ? Bound : mode is "receipt" or "receipt-over" ? ReceiptLimit : 1024 * 1024;
        if (mode is "oversize" or "receipt-over") size++;
        using (var file = File.Create(path)) file.SetLength(size);
        var clock = new PacedClock();
        using var caller = new CancellationTokenSource();
        if (mode == "cancel-before") caller.Cancel();
        var delivered = mode == "growth" ? Bound + 1 : size;
        var stream = new LargeZeroStream(delivered, path, mode, clock, caller);
        Exception? failure = null;
        object? result = null;
        try
        {
            result = mode is "receipt" or "receipt-over"
                ? ReadBytes(root, path, ReceiptLimit, caller.Token, clock, stream.Factory())
                : Digest(root, path, Bound, caller.Token, clock, stream.Factory());
        }
        catch (Exception ex) { failure = Innermost(ex); }

        if (mode is "bound" or "receipt" or "progress" or "eof")
        {
            failure.ShouldBeNull();
            if (mode == "receipt") ((byte[])result!).LongLength.ShouldBe(size);
            else
            {
                var (length, sha256) = ((long, string))result!;
                length.ShouldBe(size);
                using var native = File.OpenRead(path);
                sha256.ShouldBe(Convert.ToHexString(SHA256.HashData(native)), "a full independent hash of the file");
            }
            stream.Delivered.ShouldBe(size);
            stream.Reads.ShouldBeGreaterThanOrEqualTo(5, "several bounded chunks");
            clock.Creates.ShouldBe(0, "the reader installs no pool timer");
        }
        else
        {
            failure.ShouldNotBeNull(mode + " was accepted");
            if (mode is "oversize" or "growth" or "receipt-over") failure.Message.ShouldContain("selected file exceeds size limit");
            else if (mode is "cancel-before" or "cancel") failure.ShouldBeAssignableTo<OperationCanceledException>();
            else if (mode != "inspect") failure.Message.ShouldContain("read-stalled");
        }
        stream.Opens.ShouldBe(mode is "oversize" or "receipt-over" or "cancel-before" ? 0 : 1, "retained stream");
        stream.MaxRead.ShouldBeLessThanOrEqualTo(262_144, "chunk bound");
        if (mode == "growth") stream.Delivered.ShouldBe(Bound + 1, "one extra byte");
    }

    // C1532: native admission refuses a second link and a path outside the root before any read, and on Linux a FIFO
    // without opening it for reading.
    [Test]
    public void C1532_native_admission_refuses_links_outside_paths_and_fifos()
    {
        var root = TempDir();
        var path = Path.Combine(root, "native.bin");
        File.WriteAllText(path, "native bytes");
        var link = Path.Combine(root, "hardlink");
        HardLink(link, path);
        Should.Throw<Exception>(() => Digest(root, path, Bound, CancellationToken.None, null, null))
            .Message.ShouldContain("selected file has multiple links");
        File.Delete(link);
        Digest(root, path, Bound, CancellationToken.None, null, null).Length.ShouldBe(12, "the single-link control reads");
        Should.Throw<Exception>(() => Digest(Path.Combine(root, "other"), path, Bound, CancellationToken.None, null, null))
            .ToString().ShouldContain("outside root");
        if (!OperatingSystem.IsLinux()) return;
        var fifo = Path.Combine(root, "fifo");
        MakeFifo(fifo, 0x180).ShouldBe(0, "mkfifo errno " + Marshal.GetLastPInvokeError());
        try
        {
            Should.Throw<Exception>(() => Digest(root, fifo, Bound, CancellationToken.None, null, null))
                .Message.ShouldContain("selected file is not regular");
        }
        finally { File.Delete(fifo); }
    }

    // C1573 (the deleted output-inventory driver): growth past a small bound is refused after one extra byte, in chunks
    // of at most 256 KiB.
    [Test]
    public void C1573_growth_past_the_bound_is_refused_after_one_extra_byte()
    {
        var root = TempDir();
        var path = Path.Combine(root, "growing.bin");
        File.WriteAllBytes(path, [4, 5, 6, 7]);
        const long limit = 262_144;
        var growth = new GrowthStream(limit);
        Should.Throw<Exception>(() => Digest(root, path, limit, CancellationToken.None, null, growth.Factory()))
            .Message.ShouldContain("selected file exceeds size limit");
        growth.Delivered.ShouldBe(limit + 1);
        growth.MaxRead.ShouldBeLessThanOrEqualTo(262_144);
    }

    // C1363 (the deleted reader-deadline driver): every delivered chunk re-arms the three-second window, so 7.5 MiB
    // delivered over twelve virtual seconds is read whole; a read that stops delivering for the window is refused with
    // read-stalled and the bytes it had; a caller cancel stays a cancellation; and a trickle that always progresses is
    // still bounded by the overall cap.
    [Test]
    public void C1363_progress_re_arms_the_stall_window()
    {
        var (root, path, payload) = Payload();
        var clock = new PacedClock();
        var stream = new PacedStream(payload, clock, TimeSpan.FromMilliseconds(100), 0, TimeSpan.Zero, null);
        var read = (byte[])ReadBytes(root, path, ReceiptLimit, CancellationToken.None, clock, stream.Factory());
        SHA256.HashData(read).ShouldBe(SHA256.HashData(payload), "the bytes delivered over 12 virtual seconds are the file bytes");
        stream.Reads.ShouldBeGreaterThanOrEqualTo(120);
        clock.Creates.ShouldBe(0, "the reader installs no timer");
        clock.GetUtcNow().ShouldBeGreaterThanOrEqualTo(Epoch.AddSeconds(12), "progress crossed the silence window");
    }

    [Test]
    public void C1363_a_stall_is_refused_and_a_cancel_stays_a_cancellation()
    {
        var (root, path, payload) = Payload();
        var stalled = new PacedStream(payload, new PacedClock(), TimeSpan.FromSeconds(1), 3, TimeSpan.FromSeconds(3), null);
        var refused = Innermost(Should.Throw<Exception>(() =>
            ReadBytes(root, path, ReceiptLimit, CancellationToken.None, stalled.Clock, stalled.Factory())));
        refused.ShouldBeOfType<IOException>();
        refused.Message.ShouldStartWith("read-stalled: " + 2 * PacedStream.Served + " of " + payload.Length + " bytes");

        using var caller = new CancellationTokenSource();
        var cancelled = new PacedStream(payload, new PacedClock(), TimeSpan.FromSeconds(1), 2, TimeSpan.FromSeconds(1), caller);
        var aborted = Innermost(Should.Throw<Exception>(() =>
            ReadBytes(root, path, ReceiptLimit, caller.Token, cancelled.Clock, cancelled.Factory())));
        aborted.ShouldBeAssignableTo<OperationCanceledException>();
        aborted.Message.ShouldNotContain("read-stalled");
    }

    [Test]
    public void C1363_a_trickle_is_bounded_by_the_overall_deadline()
    {
        var root = TempDir();
        var path = Path.Combine(root, "trickle.bin");
        var bytes = new byte[256];
        File.WriteAllBytes(path, bytes);
        var clock = new PacedClock();
        var trickle = new TrickleStream(bytes, clock);
        Innermost(Should.Throw<Exception>(() => ReadBytes(root, path, ReceiptLimit, CancellationToken.None, clock, trickle.Factory())))
            .Message.ShouldContain("read-deadline-exceeded");
        clock.GetUtcNow().ShouldBeLessThanOrEqualTo(Epoch.AddSeconds(31), "the 31 s cap of a tiny read");
    }

    // C1363 windows-open: the embedded reader's own Windows open, run on every OS with only its kernel call substituted.
    // The production flags and FileStream's handle-mode detection stay real: the retained stream is asynchronous and keeps
    // the no-follow and sequential flags. This does not qualify native Windows IO.
    [Test]
    public void C1363_windows_open_keeps_overlapped_no_follow_sequential_flags()
    {
        var assembly = EmbeddedCheckpointReader.Load(source =>
        {
            var substituted = source.Replace("var handle = CreateFile(", "var handle = PortableWindowsOpen.CreateFile(", StringComparison.Ordinal);
            substituted.ShouldNotBe(source, "the Windows factory call is in the embedded source");
            return substituted + PortableWindowsOpen;
        });
        var root = TempDir();
        var path = Path.Combine(root, "windows-owner.txt");
        File.WriteAllText(path, "0123456789abcdef0123456789abcdef");
        var reader = assembly.GetType(EmbeddedCheckpointReader.TypeName, throwOnError: true)!;
        using var handle = (SafeFileHandle)Call(reader.GetMethod("OpenWindows", BindingFlags.Static | BindingFlags.NonPublic)!, [path])!;
        using var stream = new FileStream(handle, FileAccess.Read);
        stream.IsAsync.ShouldBeTrue("the Windows verified reader must not queue synchronous IO");
        var flags = (uint)assembly.GetType("Antiphon.Checkpoints.Coverage.PortableWindowsOpen", throwOnError: true)!.GetField("Flags")!.GetValue(null)!;
        (flags & 0x08200000u).ShouldBe(0x08200000u, "no-follow and sequential flags");
        Convert.ToHexString(SHA256.HashData(stream)).ShouldBe(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    // The kept library calls these members; a process that loaded the tool first gets the compiled facade under the same
    // name, so each must exist on both (the deleted compiled-facade driver's member check).
    [Test]
    public void C1573_library_members_exist_on_the_embedded_reader_and_the_compiled_facade()
    {
        var library = File.ReadAllText(EmbeddedCheckpointReader.LibraryPath);
        var names = Regex.Matches(library, @"ConfinedFileReader\]::([A-Za-z]+)").Select(match => match.Groups[1].Value).Distinct().ToList();
        names.ShouldNotBeEmpty("the library calls the reader");
        foreach (var type in new[] { EmbeddedCheckpointReader.Reader, typeof(Antiphon.Checkpoints.Coverage.ConfinedFileReader) })
            foreach (var name in names)
                type.GetMember(name, BindingFlags.Public | BindingFlags.Static).ShouldNotBeEmpty(type.Assembly.GetName().Name + " lacks " + name);
    }

    // The launcher and the kept reader library stay ASCII (C585 AsciiOnly and C1532 of the deleted drivers).
    [Test]
    [Arguments("scripts/run-checkpoint.ps1")]
    [Arguments("scripts/lib/checkpoint-file-reader.ps1")]
    public void checkpoint_scripts_are_ascii(string relative)
    {
        var bytes = File.ReadAllBytes(Path.Combine(CheckpointFixtures.RepoRoot, relative));
        Array.FindIndex(bytes, static b => b > 127).ShouldBe(-1, relative + " has a non-ASCII byte");
    }

    private (string Root, string Path, byte[] Payload) Payload()
    {
        var root = TempDir();
        var payload = new byte[7_864_320]; // 7.5 MiB, the archive size behind CARD-1358
        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)((i * 31 + (i >> 10)) & 0xFF);
        var path = Path.Combine(root, "payload.bin");
        File.WriteAllBytes(path, payload);
        return (root, path, payload);
    }

    private static (long Length, string Sha256) Digest(string root, string path, long limit, CancellationToken token,
        TimeProvider? clock, Func<SafeFileHandle, Stream>? open) =>
        ((long, string))Call(EmbeddedCheckpointReader.Reader.GetMethod("Digest",
            [typeof(string), typeof(string), typeof(long), typeof(Action), typeof(CancellationToken), typeof(TimeProvider), typeof(Func<SafeFileHandle, Stream>)])!,
            [root, path, limit, null, token, clock, open])!;

    private static object ReadBytes(string root, string path, int limit, CancellationToken token, TimeProvider? clock, Func<SafeFileHandle, Stream>? open) =>
        Call(EmbeddedCheckpointReader.Reader.GetMethod("ReadBytes",
            [typeof(string), typeof(string), typeof(int), typeof(Action), typeof(CancellationToken), typeof(TimeProvider), typeof(Func<SafeFileHandle, Stream>)])!,
            [root, path, limit, null, token, clock, open])!;

    private static object? Call(MethodInfo method, object?[] arguments)
    {
        try { return method.Invoke(null, arguments); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static Exception Innermost(Exception exception)
    {
        while (exception.InnerException is not null) exception = exception.InnerException;
        return exception;
    }

    private static void HardLink(string link, string existing)
    {
        if (OperatingSystem.IsWindows()) CreateHardLink(link, existing, IntPtr.Zero).ShouldBeTrue("CreateHardLink " + Marshal.GetLastPInvokeError());
        else Link(existing, link).ShouldBe(0, "link errno " + Marshal.GetLastPInvokeError());
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string link, string existing, IntPtr security);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existing, string link);

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo(string path, uint mode);

    private const string PortableWindowsOpen = """

        public static class PortableWindowsOpen
        {
            public static uint Flags;
            public static SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template)
            {
                if (access != 0x80000000 || share != 7 || creation != 3) throw new InvalidOperationException("unexpected read access/share/disposition");
                Flags = flags;
                return File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                    (FileOptions)(flags & 0x40000000));
            }
        }
        """;

    /// <summary>A manual clock whose timers fire inside Advance, on the advancing thread.</summary>
    private sealed class PacedClock : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<PacedTimer> _timers = [];
        private DateTimeOffset _now = Epoch;
        public int Creates;

        public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
        public override long GetTimestamp() => GetUtcNow().UtcTicks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new PacedTimer(this, callback, state);
            lock (_gate) { _timers.Add(timer); Creates++; timer.Arm(_now, dueTime); }
            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            var due = new List<PacedTimer>();
            lock (_gate)
            {
                _now += delta;
                foreach (var timer in _timers)
                    if (timer.Due is { } at && at <= _now) { timer.Due = null; due.Add(timer); }
            }
            foreach (var timer in due) timer.Fire();
        }

        private sealed class PacedTimer(PacedClock owner, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset? Due;
            public void Arm(DateTimeOffset now, TimeSpan dueTime) => Due = dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;
            public void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate) Arm(owner._now, dueTime);
                return true;
            }
            public void Dispose() { lock (owner._gate) Due = null; }
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }

    /// <summary>Delivers zeros up to its length, moving the clock per read, and plays one end-of-file mode.</summary>
    private sealed class LargeZeroStream(long length, string path, string mode, PacedClock clock, CancellationTokenSource caller) : Stream
    {
        private SafeFileHandle? _handle;
        public long Delivered;
        public int MaxRead, Reads, Opens;

        public Func<SafeFileHandle, Stream> Factory() => handle => { Opens++; _handle = handle; return this; };

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++;
            MaxRead = Math.Max(MaxRead, buffer.Length);
            if (mode == "cancel" && Reads == 2) caller.Cancel();
            if (Delivered == length && mode is "eof" or "grow" or "shrink" or "inspect")
            {
                if (mode == "inspect") _handle!.Dispose();
                else if (mode != "eof")
                {
                    using var file = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    file.SetLength(length + (mode == "grow" ? 1 : -1));
                }
                clock.Advance(TimeSpan.FromSeconds(3));
            }
            else clock.Advance(TimeSpan.FromSeconds(mode == "stall" && Reads == 2 ? 3 : length >= 67_108_864 ? 0.1 : 1));
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(buffer.Length, length - Delivered);
            buffer.Span[..count].Clear();
            Delivered += count;
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => Delivered; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Serves at most 64 KiB per read and moves the clock before answering, as a slow disk under load would.</summary>
    private sealed class PacedStream(byte[] bytes, PacedClock clock, TimeSpan ordinary, int specialRead, TimeSpan special,
        CancellationTokenSource? cancelAtSpecial) : MemoryStream(bytes, false)
    {
        public const int Served = 64 * 1024;
        private int _reads;
        public int Reads => _reads;
        public PacedClock Clock => clock;
        public Func<SafeFileHandle, Stream> Factory() => _ => this;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = Interlocked.Increment(ref _reads);
            if (read == specialRead) { cancelAtSpecial?.Cancel(); clock.Advance(special); }
            else clock.Advance(ordinary);
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, Served)], cancellationToken);
        }
    }

    private sealed class TrickleStream(byte[] bytes, PacedClock clock) : MemoryStream(bytes, false)
    {
        public Func<SafeFileHandle, Stream> Factory() => _ => this;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            return base.ReadAsync(buffer[..1], cancellationToken);
        }
    }

    private sealed class GrowthStream(long limit) : Stream
    {
        public long Delivered;
        public int MaxRead;
        public Func<SafeFileHandle, Stream> Factory() => _ => this;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            MaxRead = Math.Max(MaxRead, buffer.Length);
            var count = (int)Math.Min(buffer.Length, limit - Delivered + 1);
            if (count > 0) buffer.Span[..count].Clear();
            Delivered += count;
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => Delivered;
        public override long Position { get => Delivered; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
