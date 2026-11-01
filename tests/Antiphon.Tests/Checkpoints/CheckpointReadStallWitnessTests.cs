using System.Reflection;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Microsoft.Win32.SafeHandles;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// The silence window measures the read and stays three seconds. Bytes that return after it
// elapsed are still a stall. A zero-byte stall is retried once inside the overall budget. A
// file that stays held is refused with the holder name. A custody failure is not a git timeout.
[Category("Unit")]
public sealed class CheckpointReadStallWitnessTests
{
    [Test]
    public void delivered_read_past_the_silence_window_is_still_refused()
    {
        var f = new HostCleanupCheckpointFixture();
        var payload = Bytes(32);
        f.Io.Put(f.Payload, payload);
        var reads = 0;
        f.Io.StreamFactory = () => new DelayedDelivery(payload, f.Clock, () => reads++);

        var refused = Should.Throw<IOException>(() => ReceiptFile.ReadBytes(f.Context, f.Payload));

        refused.Message.ShouldContain("receipt-read-refused", Case.Sensitive);
        refused.Message.ShouldContain("read-stalled: 0 of 32 bytes; no progress within 3s", Case.Sensitive);
        reads.ShouldBe(1);
    }

    [Test]
    public void zero_byte_stall_is_retried_once_inside_the_overall_budget()
    {
        var f = new HostCleanupCheckpointFixture();
        var payload = Bytes(32);
        f.Io.Put(f.Payload, payload);
        var started = f.Clock.GetUtcNow();
        var reads = 0;
        f.Io.StreamFactory = () => new FirstReadSilent(payload, f.Clock, () => reads++);

        var read = ReceiptFile.ReadBytes(f.Context, f.Payload);

        read.ShouldBe(payload);
        reads.ShouldBe(2);
        (f.Clock.GetUtcNow() - started).ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(31));
    }

    [Test]
    public void held_file_is_refused_with_the_holder_name()
    {
        var f = new HostCleanupCheckpointFixture();
        f.Io.Put(f.Payload, Bytes(32));
        f.Io.ReadHolder = "Defender";
        f.Io.StreamFactory = () => new Silent(f.Clock);

        var refused = Should.Throw<IOException>(() => ReceiptFile.ReadBytes(f.Context, f.Payload));

        refused.Message.ShouldContain("receipt-read-refused", Case.Sensitive);
        refused.Message.ShouldContain("read-stalled: 0 of 32 bytes; no progress within 3s", Case.Sensitive);
        refused.Message.ShouldContain("holder=Defender", Case.Sensitive);
    }

    [Test]
    public void process_custody_failure_is_git_custody()
    {
        var f = new HostCleanupCheckpointFixture();
        f.Io.GitResult = _ => throw new InvalidOperationException("process custody unavailable");

        f.Write.Preflight(f.Root, [f.Root]).ShouldBe("git-custody: process custody unavailable");
    }

    [Test]
    public void exited_child_keeps_its_result_when_job_assignment_loses_the_race()
    {
        CheckpointProcessRunner.CustodyRefusal(assigned: false, exited: true).ShouldBeNull();
        CheckpointProcessRunner.CustodyRefusal(assigned: false, exited: false).ShouldBe("process custody unavailable");
        CheckpointProcessRunner.CustodyRefusal(assigned: true, exited: false).ShouldBeNull();
    }

    // A completed zero at offset 0 is end of file. When the inspected size is 32, that is a
    // truncated file, including the one retry after a stall and a prompt zero with no stall.
    [Test]
    public void stalled_zero_then_prompt_zero_is_refused()
    {
        var f = new HostCleanupCheckpointFixture();
        var payload = Bytes(32);
        f.Io.Put(f.Payload, payload);
        f.Io.StreamFactory = () => new ZeroAfterStall(f.Clock);

        var refused = Should.Throw<IOException>(() => ReceiptFile.Digest(f.Context, f.Payload));

        refused.Message.ShouldContain("receipt-read-refused", Case.Sensitive);
        refused.Message.ShouldContain("read-truncated: 0 of 32 bytes; retained size 32", Case.Sensitive);
        refused.Message.ShouldNotContain(EmptySha, Case.Sensitive);
    }

    [Test]
    public void prompt_zero_read_of_a_nonempty_file_is_refused()
    {
        var f = new HostCleanupCheckpointFixture();
        f.Io.Put(f.Payload, Bytes(32));
        f.Io.StreamFactory = () => new MemoryStream([]);

        var refused = Should.Throw<IOException>(() => ReceiptFile.ReadBytes(f.Context, f.Payload));

        refused.Message.ShouldContain("read-truncated: 0 of 32 bytes; retained size 32", Case.Sensitive);
    }

    [Test]
    public void windows_read_stream_is_not_a_filestream_in_either_copy()
    {
        var native = typeof(CheckpointNativeIo).GetNestedType("WindowsReadStream", BindingFlags.NonPublic);
        native.ShouldNotBeNull();
        native!.BaseType.ShouldBe(typeof(Stream));
        typeof(FileStream).IsAssignableFrom(native).ShouldBeFalse();

        var root = CheckpointFixtures.RepoRoot;
        foreach (var relative in new[]
        {
            "src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs",
            "scripts/lib/checkpoint-file-reader.ps1"
        })
        {
            var source = File.ReadAllText(Path.Combine(root, relative));
            var body = ClassBody(source, "WindowsReadStream");
            body.ShouldContain("class WindowsReadStream : Stream", Case.Sensitive, relative);
            body.ShouldNotContain("class WindowsReadStream : FileStream", Case.Sensitive, relative);
            body.ShouldNotContain("base(handle, FileAccess.Read)", Case.Sensitive, relative);
            var cancel = source.IndexOf("CancelIoEx(", StringComparison.Ordinal);
            var wait = source.IndexOf("GetOverlappedResult(", cancel + 1, StringComparison.Ordinal);
            var blocking = source.IndexOf(", true)", wait, StringComparison.Ordinal);
            var close = source.IndexOf("CloseHandle(", blocking + 1, StringComparison.Ordinal);
            var unpin = source.IndexOf("ops.Unpin()", blocking + 1, StringComparison.Ordinal);
            if (unpin < 0) unpin = source.IndexOf("pin.Free()", blocking + 1, StringComparison.Ordinal);
            (cancel >= 0 && wait > cancel && blocking > wait && close > blocking && unpin > blocking)
                .ShouldBeTrue(relative + " releases the event and buffer only after the completion wait");
            source.ShouldNotContain("WaitForSingleObject(ev, 100)", Case.Sensitive, relative);
        }

        var library = File.ReadAllText(Path.Combine(root, "src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs"));
        var script = File.ReadAllText(Path.Combine(root, "scripts/lib/checkpoint-file-reader.ps1"));
        foreach (var source in new[] { library, script })
        {
            source.ShouldContain("read-truncated: ", Case.Sensitive);
            source.ShouldContain("retained size ", Case.Sensitive);
            source.ShouldContain("TimeSpan.FromMilliseconds(100)", Case.Sensitive);
        }
        CheckpointFileReader.StallDeadline.ShouldBe(TimeSpan.FromSeconds(3));
    }

    [Test]
    public void cancel_keeps_the_buffer_until_the_kernel_wait_returns()
    {
        var ops = new BlockingCancel();
        var buffer = new byte[32];
        long offset = 0;
        var path = Path.GetTempFileName();
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var started = new ManualResetEventSlim(false);
        Exception? error = null;
        var worker = new Thread(() =>
        {
            try
            {
#pragma warning disable CA1416 // The fake seam substitutes the kernel. NativeOverlapped is only a layout here.
                WindowsOverlappedRead.Read(ops, handle, ref offset, buffer, buffer.Length, new CancellationToken(canceled: true));
#pragma warning restore CA1416
            }
            catch (Exception ex) { error = ex; }
            finally { started.Set(); }
        }) { IsBackground = true, Name = "checkpoint-file-read" };
        try
        {
            worker.Start();
            ops.Waiting.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue("the completion wait was not entered");
            ops.Order.ShouldBe(new[] { "read", "cancel", "wait" });
            ops.WaitThread.ShouldBe("checkpoint-file-read");
            ops.ClosedDuringWait.ShouldBeFalse();
            worker.IsAlive.ShouldBeTrue();

            ops.Release(995);
            started.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
            worker.Join(TimeSpan.FromSeconds(5)).ShouldBeTrue();
            error.ShouldBeOfType<OperationCanceledException>();
            ops.Order.ShouldBe(new[] { "read", "cancel", "wait", "close", "unpin" });
            offset.ShouldBe(0);
        }
        finally
        {
            ops.Release(995);
            handle.Dispose();
            File.Delete(path);
        }
    }

    private static string ClassBody(string source, string name)
    {
        var start = source.IndexOf("class " + name, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var next = source.IndexOf("\n    private static", start + 1, StringComparison.Ordinal);
        if (next < 0) next = source.IndexOf("\n    private sealed class NativeHandle", start + 1, StringComparison.Ordinal);
        next.ShouldBeGreaterThan(start);
        return source[start..next];
    }

    private const string EmptySha = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";

    private static byte[] Bytes(int size)
    {
        var bytes = new byte[size];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i + 1);
        return bytes;
    }

    private sealed class DelayedDelivery(byte[] bytes, ControlledTimeProvider clock, Action count) : MemoryStream(bytes, writable: false)
    {
        private int _calls;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // The delivering read is the one that waited. The following EOF probe is immediate.
            if (++_calls == 1)
            {
                count();
                clock.Advance(TimeSpan.FromSeconds(4));
            }
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class FirstReadSilent(byte[] bytes, ControlledTimeProvider clock, Action count) : MemoryStream(bytes, writable: false)
    {
        private int _reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (++_reads == 1)
            {
                count();
                clock.Advance(TimeSpan.FromSeconds(4));
                return ValueTask.FromResult(0);
            }
            if (_reads == 2) count();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class Silent(ControlledTimeProvider clock) : MemoryStream([])
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            clock.Advance(CheckpointFileReader.StallDeadline);
            return ValueTask.FromResult(0);
        }
    }

    private sealed class ZeroAfterStall(ControlledTimeProvider clock) : MemoryStream([])
    {
        private int _reads;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (++_reads == 1) clock.Advance(TimeSpan.FromSeconds(4));
            return ValueTask.FromResult(0);
        }
    }

    private sealed class BlockingCancel : IWindowsOverlappedOps
    {
        private readonly ManualResetEventSlim _release = new(false);
        private int _error = 997;
        public List<string> Order { get; } = [];
        public ManualResetEventSlim Waiting { get; } = new(false);
        public string? WaitThread { get; private set; }
        public bool ClosedDuringWait { get; private set; }

        public void Release(int error)
        {
            _error = error;
            _release.Set();
        }

        public IntPtr CreateEvent() => new IntPtr(64);
        public bool ReadFile(SafeFileHandle handle, byte[] buffer, int count, out int read, ref System.Threading.NativeOverlapped overlapped)
        {
            Order.Add("read");
            read = 0;
            _error = 997;
            return false;
        }

        public bool GetOverlappedResult(SafeFileHandle handle, ref System.Threading.NativeOverlapped overlapped, out int read, bool wait)
        {
            read = 0;
            if (!wait) return false;
            Order.Add("wait");
            WaitThread = Thread.CurrentThread.Name;
            ClosedDuringWait = Order.Contains("close") || Order.Contains("unpin");
            Waiting.Set();
            _release.Wait();
            _error = 995;
            return false;
        }

        public bool CancelIoEx(SafeFileHandle handle, ref System.Threading.NativeOverlapped overlapped)
        {
            Order.Add("cancel");
            return true;
        }

        public uint WaitForSingleObject(IntPtr handle, uint milliseconds) => 258;

        public void CloseHandle(IntPtr handle) => Order.Add("close");
        public void Unpin() => Order.Add("unpin");
        public int LastError() => _error;
    }
}
