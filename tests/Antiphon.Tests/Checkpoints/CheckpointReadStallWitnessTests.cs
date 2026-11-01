using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
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
}
