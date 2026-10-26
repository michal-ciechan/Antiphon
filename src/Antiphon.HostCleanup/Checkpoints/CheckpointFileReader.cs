using System.Security.Cryptography;
using System.Text;
using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

/// <summary>Verify metadata and final path on the exact handle read.</summary>
public static class CheckpointFileReader
{
    public const int DocumentLimit = 4 * 1024 * 1024;
    public const int SourceLimit = 1024 * 1024;
    public const long ContentLimit = 1L << 30; // Content stamps stream; receipt reads keep their 64 MiB bound.
    // CARD-1318 stalled-receipt contract: a read that delivers nothing for this long is refused. CARD-1358: the
    // window bounds the open, the inspection and each chunk, and every delivered chunk re-arms it, so the total is
    // bounded by the chunk count and not by a throughput assumption.
    public static readonly TimeSpan StallDeadline = TimeSpan.FromSeconds(3);
    // Linux handle reads use workers; 256 KiB keeps a 7.5 MiB file at 30 hops.
    public const int ReadChunk = 256 * 1024;

    public static string? Refusal(bool isRegularFile, ulong linkCount, string resolvedPath, string root) =>
        Refusal(CheckpointCleanupDefaults.Context, isRegularFile, linkCount, resolvedPath, root);

    public static string? Refusal(CheckpointCleanupContext context, bool isRegularFile, ulong linkCount, string resolvedPath, string root)
    {
        if (!isRegularFile) return "selected file is not regular";
        if (linkCount != 1) return "selected file has multiple links";
        if (!context.IsStrictlyInside(root, resolvedPath)) return "opened file is outside root";
        return null;
    }

    public static string Read(string root, string path, int limit, Action? afterVerification = null) =>
        Read(root, path, limit, out _, afterVerification);

    public static string Read(string root, string path, int limit, out int byteCount, Action? afterVerification = null)
    {
        var bytes = ReadBytes(root, path, limit, afterVerification);
        byteCount = bytes.Length;
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static byte[] ReadBytes(string root, string path, int limit, Action? afterVerification = null, CancellationToken cancellationToken = default) =>
        ReadBytes(CheckpointCleanupDefaults.Context, root, path, limit, afterVerification, cancellationToken);

    public static byte[] ReadBytes(CheckpointCleanupContext context, string root, string path, int limit,
        Action? afterVerification = null, CancellationToken cancellationToken = default)
    {
        using var content = new MemoryStream();
        Consume(context, root, path, limit, afterVerification, cancellationToken, span => content.Write(span));
        return content.ToArray();
    }

    public static (long Length, string Sha256) Digest(CheckpointCleanupContext context, string root, string path,
        long limit, CancellationToken cancellationToken = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        Consume(context, root, path, limit, null, cancellationToken, span =>
        {
            hash.AppendData(span);
            total += span.Length;
        });
        return (total, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private delegate void ContentSink(ReadOnlySpan<byte> bytes);

    private static void Consume(CheckpointCleanupContext context, string root, string path, long limit,
        Action? afterVerification, CancellationToken cancellationToken, ContentSink sink)
    {
        using var stall = new CancellationTokenSource(StallDeadline, context.Time);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stall.Token);
        deadline.Token.ThrowIfCancellationRequested();
        CheckpointReadOpen request;
        if (context.Io.Platform == CheckpointPlatform.Linux)
        {
            var before = context.Io.Stat(path);
            if (before.Kind == "unknown" || before.Kind == "absent") throw new IOException("opened-handle verification failed");
            if ((before.Mode & 0xF000) != 0x8000) throw new InvalidDataException("selected file is not regular");
            request = new(0x80000 | 0x20000 | 0x800, 0); // CLOEXEC | NOFOLLOW | NONBLOCK
        }
        else if (context.Io.Platform == CheckpointPlatform.Windows)
        {
            if ((context.Io.Attributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidDataException("selected file is not regular");
            // FileStream infers async mode from the retained handle. A synchronous Windows
            // handle makes ReadAsync queue blocking I/O behind the callers waiting for it.
            request = new(0, 0x00200000 | 0x08000000 | 0x40000000); // OPEN_REPARSE_POINT | SEQUENTIAL_SCAN | OVERLAPPED
        }
        else throw new InvalidDataException("opened-handle verification unsupported on this platform");
        using var handle = context.Io.OpenRead(path, request);
        var metadata = handle.Inspect();
        var refusal = Refusal(context, metadata.Regular, metadata.Links, metadata.Path, root);
        if (refusal is not null) throw new InvalidDataException(refusal);
        if (metadata.Size > (ulong)limit) throw new InvalidDataException("selected file exceeds size limit");
        afterVerification?.Invoke();
        using var stream = handle.OpenStream();
        long total = 0;
        var buffer = new byte[ReadChunk];
        try
        {
            int count;
            // One byte past the limit is requested so a file that grows during the read is still refused.
            while ((count = stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - total + 1)), deadline.Token).AsTask().WaitAsync(deadline.Token).GetAwaiter().GetResult()) != 0)
            {
                if (total + count > limit) throw new InvalidDataException("selected file exceeds size limit");
                sink(buffer.AsSpan(0, count));
                total += count;
                stall.CancelAfter(StallDeadline); // progress re-arms the window
            }
        }
        catch (OperationCanceledException) when (stall.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // A stall at the old EOF is complete only if the retained handle still has the inspected size.
            if (total == (long)metadata.Size && handle.Inspect().Size == metadata.Size)
                return;
            throw new IOException($"read-stalled: {total} of {metadata.Size} bytes; no progress within {StallDeadline.TotalSeconds:0}s");
        }
    }
}
