using System.Security.Cryptography;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointWindowsReadModeTests
{
    [Test]
    [Arguments("receipt", 32)]
    [Arguments("digest", 32)]
    [Arguments("digest", 467)]
    [Arguments("digest", 808)]
    [Arguments("digest", 524305)]
    [Arguments("write", 32)]
    [Arguments("write", 467)]
    [Arguments("write", 808)]
    [Arguments("write", 262144)]
    [Arguments("write", 524305)]
    public void Windows_reads_do_not_queue_synchronous_io_behind_blocked_workers(string consumer, int size)
    {
        var f = new HostCleanupCheckpointFixture(CheckpointPlatform.Windows);
        var path = Path.Combine(f.Root, "new-content.txt");
        var bytes = Enumerable.Repeat((byte)'x', size).ToArray();
        if (consumer != "write") f.Io.Put(path, bytes);
        var streams = new List<ModeledWindowsStream>();
        f.Io.StreamFactory = () =>
        {
            var opened = new ModeledWindowsStream(bytes, f.Clock,
                (f.Io.ReadRequests.Last().WindowsFlags & 0x40000000) != 0);
            streams.Add(opened);
            return opened;
        };

        switch (consumer)
        {
            case "receipt":
                ReceiptFile.ReadBytes(f.Context, path).ShouldBe(bytes);
                break;
            case "digest":
                var digest = ReceiptFile.Digest(f.Context, path);
                digest.Length.ShouldBe(size);
                digest.Sha256.ShouldBe(Convert.ToHexString(SHA256.HashData(bytes)));
                break;
            case "write":
                f.Write.Text(path, new string('x', size), f.Write.Under(f.Root)).ShouldBeNull();
                f.Context.Registry.Content(path).ShouldNotBeNull();
                f.Io.Nodes[path].Bytes.ShouldBe(bytes);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(consumer));
        }

        // A write opens twice: ReplacementPublication.Matches reads the new bytes back
        // with a synchronous Read, then the custody digest opens the same file. Receipt
        // and digest consumers open once. Linux, review 7ba417d9, every pool worker busy:
        // that synchronous Read returned 808 bytes in 4-5 ms. ReadAsync on the same
        // non-overlapped handle did not finish within 3 s on a capped pool and took 969 ms
        // when the pool could grow. Only that ReadAsync is a worker stall. Digest and
        // receipt opens stay overlapped, no-follow, and sequential.
        var opens = consumer == "write" ? 2 : 1;
        streams.Count.ShouldBe(opens);
        streams.ShouldAllBe(s => s.WorkerWaits == 0 && s.Disposed);
        streams[^1].Delivered.ShouldBe(size);
        if (consumer == "write") streams[0].Delivered.ShouldBe(0);
        f.Io.ReadRequests.Count.ShouldBe(opens);
        // OPEN_REPARSE_POINT | SEQUENTIAL_SCAN | OVERLAPPED. The verification open is the
        // base Matches request and is not this mask.
        const uint digestOpen = 0x00200000u | 0x08000000u | 0x40000000u;
        for (var i = 0; i < f.Io.ReadRequests.Count; i++)
        {
            var request = f.Io.ReadRequests[i];
            if (consumer == "write" && i == 0)
            {
                request.UnixFlags.ShouldBe(0x20000);
                request.WindowsFlags.ShouldBe(0x00200000u);
            }
            else
            {
                (request.WindowsFlags & digestOpen).ShouldBe(digestOpen);
            }
        }

        f.Io.Count("inspect", path).ShouldBe(1);
        f.Io.Count("close-read", path).ShouldBe(opens);
    }

    // Model the .NET Windows split without changing the process-wide thread pool or
    // acquiring a native lock: ReadAsync on a synchronous handle needs a worker to
    // run ReadFile. With workers occupied by blocking callers, no read starts before
    // the existing deadline. An overlapped handle can submit I/O without that worker.
    // A synchronous Read on either handle runs on the caller and is not a stall.
    // This witnesses the scheduling dependency, not the load of the historical host.
    private sealed class ModeledWindowsStream(byte[] bytes, ControlledTimeProvider clock, bool overlapped)
        : MemoryStream(bytes, writable: false)
    {
        public int WorkerWaits { get; private set; }
        public long Delivered { get; private set; }
        public bool Disposed { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!overlapped)
            {
                WorkerWaits++;
                clock.Advance(CheckpointFileReader.StallDeadline);
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("the queued synchronous read must reach its deadline");
            }
            var result = base.ReadAsync(buffer, cancellationToken);
            Delivered += result.Result;
            return result;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
