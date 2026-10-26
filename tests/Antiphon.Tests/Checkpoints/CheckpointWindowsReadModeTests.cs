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
    public void Windows_reads_do_not_queue_synchronous_io_behind_blocked_workers(string consumer, int size)
    {
        var f = new HostCleanupCheckpointFixture(CheckpointPlatform.Windows);
        var path = Path.Combine(f.Root, "new-content.txt");
        var bytes = Enumerable.Repeat((byte)'x', size).ToArray();
        if (consumer != "write") f.Io.Put(path, bytes);
        ModeledWindowsStream? stream = null;
        f.Io.StreamFactory = () => stream = new(bytes, f.Clock,
            (f.Io.ReadRequests.Last().WindowsFlags & 0x40000000) != 0);

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

        stream.ShouldNotBeNull().Delivered.ShouldBe(size);
        stream.WorkerWaits.ShouldBe(0);
        f.Io.ReadRequests.Count.ShouldBe(1);
        // Keep the existing verified-handle and sequential-read protections.
        (f.Io.ReadRequests.Single().WindowsFlags & 0x08200000).ShouldBe(0x08200000u);
        f.Io.Count("inspect", path).ShouldBe(1);
        f.Io.Count("close-read", path).ShouldBe(1);
        stream.Disposed.ShouldBeTrue();
    }

    // Model the .NET Windows split without changing the process-wide thread pool or
    // acquiring a native lock: ReadAsync on a synchronous handle needs a worker to
    // run ReadFile. With workers occupied by blocking callers, no read starts before
    // the existing deadline. An overlapped handle can submit I/O without that worker.
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
