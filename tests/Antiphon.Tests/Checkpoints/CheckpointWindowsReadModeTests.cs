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
    public void Windows_reads_keep_synchronous_handles_content_and_custody(string consumer, int size)
    {
        var f = new HostCleanupCheckpointFixture(CheckpointPlatform.Windows);
        var path = Path.Combine(f.Root, "new-content.txt");
        var bytes = Enumerable.Repeat((byte)'x', size).ToArray();
        if (consumer != "write") f.Io.Put(path, bytes);
        var streams = new List<ModeledWindowsStream>();
        f.Io.StreamFactory = () =>
        {
            var opened = new ModeledWindowsStream(bytes);
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
        // and digest consumers open once. Actual native scheduling is covered by the
        // isolated pool-saturation witness in CheckpointReceiptStallBudgetTests.
        var opens = consumer == "write" ? 2 : 1;
        streams.Count.ShouldBe(opens);
        streams.ShouldAllBe(s => s.Disposed);
        streams[^1].Delivered.ShouldBe(size);
        if (consumer == "write") streams[0].Delivered.ShouldBe(0);
        f.Io.ReadRequests.Count.ShouldBe(opens);
        // OPEN_REPARSE_POINT | SEQUENTIAL_SCAN, without OVERLAPPED. The verification open is the
        // base Matches request and is not this mask.
        const uint digestOpen = 0x00200000u | 0x08000000u;
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
                request.WindowsFlags.ShouldBe(digestOpen);
            }
        }

        f.Io.Count("inspect", path).ShouldBe(1);
        f.Io.Count("close-read", path).ShouldBe(opens);
    }

    // Track bytes and disposal here; a memory stream cannot prove native I/O scheduling.
    private sealed class ModeledWindowsStream(byte[] bytes)
        : MemoryStream(bytes, writable: false)
    {
        public long Delivered { get; private set; }
        public bool Disposed { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
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
