using System.Security;
using System.Security.Cryptography;
using Antiphon.Checkpoints;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointEofStallTests
{
    private const int OriginalSize = 65_536;
    private const string StalledReason = "read-stalled: 65536 of 65536 bytes; no progress within 3s";

    [Test]
    [Arguments("receipt")]
    [Arguments("digest")]
    [Arguments("stamp")]
    public void append_during_eof_stall_is_refused(string consumer)
    {
        using var f = new EofFixture();
        f.Arm(() =>
        {
            f.Node.Bytes = [.. f.Node.Bytes, 0x7f];
            f.Node.Stat = f.Node.Stat with { Size = f.Node.Bytes.Length };
        });

        // Assert the outcome first: the old reader succeeds on all three paths.
        AssertRefused(f, consumer, StalledReason);

        f.AssertStall();
        f.Node.Bytes.Length.ShouldBe(OriginalSize + 1);
        f.Node.Stat.Size.ShouldBe(OriginalSize + 1);
    }

    [Test]
    public void stamp_catch_formats_the_innermost_reason()
    {
        using var f = new EofFixture();
        var cases = new (Exception Error, string Expected)[]
        {
            (new IOException("outer", new IOException("  inner\r\nreason  ")), "unreadable|inner  reason"),
            (new IOException("outer", new IOException(" \r\n ")), "unreadable|IOException"),
            (new IOException("outer", new IOException(new string('x', 205))), "unreadable|" + new string('x', 200)),
            (new UnauthorizedAccessException("  access\r\ndenied  "), "unreadable|access  denied"),
            (new SecurityException("  security\r\nrefused  "), "unreadable|security  refused")
        };
        foreach (var (error, expected) in cases)
        {
            var calls = 0;
            var stamp = BuildConfinement.Stamp(f.Path, path =>
            {
                path.ShouldBe(f.Path);
                calls++;
                throw error;
            });
            stamp.ShouldBe(expected);
            calls.ShouldBe(1, "the real regular-file shape must enter Stamp's digest try/catch");
        }
    }

    [Test]
    [Arguments("receipt")]
    [Arguments("digest")]
    [Arguments("stamp")]
    public void eof_stall_with_unprovable_final_size_is_refused(string consumer)
    {
        foreach (var inspectFailure in new[] { false, true })
        {
            using var f = new EofFixture();
            f.Arm(() =>
            {
                if (inspectFailure)
                    f.Files.Io.InspectFails = true;
                else
                {
                    f.Node.Bytes = f.Node.Bytes[..^1];
                    f.Node.Stat = f.Node.Stat with { Size = f.Node.Bytes.Length };
                }
            });

            AssertRefused(f, consumer, inspectFailure ? "inspect failed" : StalledReason);

            f.AssertStall();
            f.Node.Stat.Size.ShouldBe(inspectFailure ? OriginalSize : OriginalSize - 1);
        }
    }

    [Test]
    [Arguments("receipt")]
    [Arguments("digest")]
    [Arguments("stamp")]
    public void eof_stall_without_growth_still_completes(string consumer)
    {
        using var f = new EofFixture();
        f.Arm(() => { });

        switch (consumer)
        {
            case "receipt":
                ReceiptFile.ReadBytes(f.Files.Context, f.Path).ShouldBe(f.Original);
                break;
            case "digest":
                var digest = ReceiptFile.Digest(f.Files.Context, f.Path);
                digest.Length.ShouldBe(OriginalSize);
                digest.Sha256.ShouldBe(Convert.ToHexString(SHA256.HashData(f.Original)));
                break;
            case "stamp":
                f.Stamp().ShouldBe(f.OriginalStamp);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(consumer));
        }

        f.AssertStall();
        f.Node.Bytes.ShouldBe(f.Original);
        f.Node.Stat.Size.ShouldBe(OriginalSize);
    }

    private static void AssertRefused(EofFixture f, string consumer, string reason)
    {
        if (consumer == "stamp")
        {
            var stamp = f.Stamp();
            stamp.ShouldBe("unreadable|" + reason);
            stamp.ShouldNotBe(f.OriginalStamp);
            return;
        }
        var refusal = Should.Throw<IOException>(() =>
        {
            switch (consumer)
            {
                case "receipt":
                    ReceiptFile.ReadBytes(f.Files.Context, f.Path);
                    break;
                case "digest":
                    ReceiptFile.Digest(f.Files.Context, f.Path);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(consumer));
            }
        });
        refusal.Message.ShouldStartWith("receipt-read-refused: ");
        refusal.InnerException.ShouldBeOfType<IOException>().Message.ShouldBe(reason);
    }

    private sealed class EofFixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c1369-" + Guid.NewGuid().ToString("N"));
        private readonly List<DateTimeOffset> _inspectionTimes = [];
        private readonly (ulong Device, ulong Inode) _identity;
        private EofStallStream? _stream;
        public HostCleanupCheckpointFixture Files { get; } = new();
        public VirtualCheckpointIo.Node Node { get; }
        public byte[] Original { get; } = Enumerable.Range(0, OriginalSize).Select(i => (byte)(i * 31 + (i >> 10))).ToArray();
        public string Path { get; }
        public string OriginalStamp { get; }

        public EofFixture()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "payload.bin");
            try
            {
                File.WriteAllBytes(Path, Original);
                Node = Files.Io.Put(Path, Original);
                _identity = (Node.Stat.Device, Node.Stat.Inode);
                OriginalStamp = Stamp();
                OriginalStamp.ShouldBe("file|65536|" + Convert.ToHexString(SHA256.HashData(Original)).ToLowerInvariant());
            }
            catch
            {
                Directory.Delete(_directory, recursive: true);
                throw;
            }
        }

        public string Stamp() => BuildConfinement.Stamp(Path, path => ReceiptFile.Digest(Files.Context, path));

        public void Arm(Action duringStall)
        {
            Files.Io.Events.Clear();
            Files.Io.ReadRequests.Clear();
            Files.Io.Before = (operation, path) =>
            {
                if (operation == "inspect" && path == Path)
                    _inspectionTimes.Add(Files.Clock.GetUtcNow());
            };
            Files.Io.StreamFactory = () => _stream = new EofStallStream(Original, Files.Clock, duringStall);
        }

        public void AssertStall()
        {
            var stream = _stream.ShouldNotBeNull();
            stream.Stalls.ShouldBe(1);
            stream.StalledOffset.ShouldBe(OriginalSize);
            stream.ElapsedInsideRead.ShouldBe(CheckpointFileReader.StallDeadline);
            stream.TimerFiredInsideRead.ShouldBeTrue();
            stream.TokenCancelledInsideRead.ShouldBeTrue();
            ReferenceEquals(Files.Io.Nodes[Path], Node).ShouldBeTrue("the append/shrink changes the retained node, not the path");
            (Node.Stat.Device, Node.Stat.Inode).ShouldBe(_identity);
            Files.Io.Count("open-read", Path).ShouldBe(1);
            Files.Io.Count("stream", Path).ShouldBe(1);
            Files.Io.Count("close-read", Path).ShouldBe(1);
            Files.Io.Count("inspect", Path).ShouldBe(2);
            _inspectionTimes.Count.ShouldBe(2);
            _inspectionTimes[0].ShouldBe(stream.StallStarted);
            _inspectionTimes[1].ShouldBe(stream.StallStarted + CheckpointFileReader.StallDeadline);
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    private sealed class EofStallStream(byte[] original, ControlledTimeProvider clock, Action duringStall) : MemoryStream(original, writable: false)
    {
        public int Stalls { get; private set; }
        public long StalledOffset { get; private set; }
        public DateTimeOffset StallStarted { get; private set; }
        public TimeSpan ElapsedInsideRead { get; private set; }
        public bool TimerFiredInsideRead { get; private set; }
        public bool TokenCancelledInsideRead { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position < original.Length)
                return base.ReadAsync(buffer, cancellationToken);

            Stalls++;
            StalledOffset = Position;
            StallStarted = clock.GetUtcNow();
            var fires = clock.Events.Count(e => e.Action == "fire");
            duringStall();
            clock.Advance(CheckpointFileReader.StallDeadline);
            ElapsedInsideRead = clock.GetUtcNow() - StallStarted;
            TimerFiredInsideRead = clock.Events.Count(e => e.Action == "fire") == fires + 1;
            TokenCancelledInsideRead = cancellationToken.IsCancellationRequested;
            // Complete the operation synchronously, with cancellation and no bytes or pending task.
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("the EOF stall did not cancel its read");
        }
    }
}
