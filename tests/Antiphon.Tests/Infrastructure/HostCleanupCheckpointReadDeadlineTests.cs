using System.Security.Cryptography;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-1358: the verified reader bounds a read by a stall window that every delivered chunk re-arms, as well as a size-proportional overall cap. A short silence window is not one
// clock over the whole file. The clock is the fixture's ControlledTimeProvider and the stream advances it inside
// ReadAsync, so no witness here waits on the wall clock.
[Category("Unit")]
public sealed class HostCleanupCheckpointReadDeadlineTests
{
    private const int PayloadSize = 7_864_320; // 7.5 MiB, the archive size behind CARD-1358
    private const int Served = 64 * 1024;

    [Test]
    public void large_file_read_that_keeps_making_progress_completes_past_three_seconds()
    {
        // red: one three-second deadline over the whole read refuses the fourth chunk (receipt-read-refused) although
        // every read answered within a second.
        var f = new HostCleanupCheckpointFixture();
        var payload = Patterned(PayloadSize);
        f.Io.Put(f.Payload, payload);
        f.Io.StreamFactory = () => new PacedStream(payload, f.Clock, _ => TimeSpan.FromMilliseconds(100));

        var read = ReceiptFile.ReadBytes(f.Context, f.Payload);

        read.Length.ShouldBe(payload.Length);
        read.SequenceEqual(payload).ShouldBeTrue("the bytes delivered over 12 virtual seconds are the file's bytes");
        SHA256.HashData(read).ShouldBe(SHA256.HashData(payload));
        f.Clock.Events.ShouldBeEmpty("the reader must not install a thread-pool timer");
        (f.Clock.GetUtcNow() - DateTimeOffset.Parse("2026-09-25T00:00:00Z")).ShouldBeGreaterThan(TimeSpan.FromSeconds(12));
        f.Io.Count("stream").ShouldBe(1, "one handle stream serves the whole read");
    }

    [Test]
    public void read_with_no_progress_for_the_stall_window_is_refused_as_stalled_and_a_caller_cancel_is_not()
    {
        // red: the refusal of a read whose bytes stopped arriving does not say read-stalled.
        var stalled = new HostCleanupCheckpointFixture();
        var payload = Patterned(PayloadSize);
        stalled.Io.Put(stalled.Payload, payload);
        stalled.Io.StreamFactory = () => new PacedStream(payload, stalled.Clock,
            read => read == 3 ? CheckpointFileReader.StallDeadline : TimeSpan.FromSeconds(1));
        var refused = Should.Throw<IOException>(() => ReceiptFile.ReadBytes(stalled.Context, stalled.Payload));
        refused.Message.ShouldContain("receipt-read-refused", Case.Sensitive);
        refused.Message.ShouldContain("read-stalled: " + 2 * Served + " of " + PayloadSize + " bytes", Case.Sensitive);

        var cancelled = new HostCleanupCheckpointFixture();
        cancelled.Io.Put(cancelled.Payload, payload);
        using var caller = new CancellationTokenSource();
        cancelled.Io.StreamFactory = () => new PacedStream(payload, cancelled.Clock, read =>
        {
            if (read == 2) caller.Cancel();
            return TimeSpan.FromSeconds(1);
        });
        var aborted = Should.Throw<IOException>(() => ReceiptFile.ReadBytes(cancelled.Context, cancelled.Payload, caller.Token));
        aborted.Message.ShouldContain("receipt-read-refused", Case.Sensitive);
        aborted.Message.ShouldNotContain("read-stalled");
        aborted.InnerException.ShouldBeAssignableTo<OperationCanceledException>("a caller cancel stays a cancellation");
    }

    private static byte[] Patterned(int size)
    {
        var bytes = new byte[size];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(i * 31 + (i >> 10));
        return bytes;
    }

    // Serves at most 64 KiB per read and moves the controlled clock before answering, as a slow disk under load
    // would; the read honours the token it is given the way the runtime stream does.
    private sealed class PacedStream(byte[] bytes, ControlledTimeProvider clock, Func<int, TimeSpan> advance) : MemoryStream(bytes, false)
    {
        private int _reads;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = Interlocked.Increment(ref _reads);
            clock.Advance(advance(read));
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, Served)], cancellationToken);
        }
    }
}
