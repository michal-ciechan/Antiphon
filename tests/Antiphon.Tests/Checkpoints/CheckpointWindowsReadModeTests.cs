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

        // Publication verifies the destination, then the custody digest reads it again.
        // Receipt and digest consumers open once. Every open is overlapped: a synchronous
        // Read, or ReadAsync on a synchronous handle, queues behind blocked workers.
        var opens = consumer == "write" ? 2 : 1;
        streams.Count.ShouldBe(opens);
        streams.ShouldAllBe(s => s.Delivered == size && s.WorkerWaits == 0 && s.Disposed);
        f.Io.ReadRequests.Count.ShouldBe(opens);
        foreach (var request in f.Io.ReadRequests)
            (request.WindowsFlags & 0x48200000u).ShouldBe(0x48200000u);
        f.Io.Count("inspect", path).ShouldBe(1);
        f.Io.Count("close-read", path).ShouldBe(opens);
    }

    // Model the .NET Windows split without changing the process-wide thread pool or
    // acquiring a native lock: ReadAsync on a synchronous handle needs a worker to
    // run ReadFile. With workers occupied by blocking callers, no read starts before
    // the existing deadline. An overlapped handle can submit I/O without that worker.
    // This witnesses the scheduling dependency, not the load of the historical host.
    private sealed class ModeledWindowsStream(byte[] bytes, ControlledTimeProvider clock, bool overlapped)
        : MemoryStream(bytes, writable: false)
    {
        private bool _async;
        public int WorkerWaits { get; private set; }
        public long Delivered { get; private set; }
        public bool Disposed { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_async) return base.Read(buffer, offset, count);
            return RefuseSynchronous();
        }

        public override int Read(Span<byte> buffer)
        {
            if (_async) return base.Read(buffer);
            return RefuseSynchronous();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!overlapped)
            {
                WorkerWaits++;
                clock.Advance(CheckpointFileReader.StallDeadline);
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("the queued synchronous read must reach its deadline");
            }
            _async = true;
            try
            {
                var result = base.ReadAsync(buffer, cancellationToken);
                Delivered += result.Result;
                return result;
            }
            finally { _async = false; }
        }

        private int RefuseSynchronous()
        {
            WorkerWaits++;
            clock.Advance(CheckpointFileReader.StallDeadline);
            throw new InvalidOperationException("the queued synchronous read must reach its deadline");
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
