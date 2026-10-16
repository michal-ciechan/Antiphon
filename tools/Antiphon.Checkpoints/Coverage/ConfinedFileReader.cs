using System.Text;
using System.Security.Cryptography;
using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints.Coverage;

/// <summary>Verify metadata and final path on the exact handle read.</summary>
public static class ConfinedFileReader
{
    public const int DocumentLimit = 4 * 1024 * 1024;
    public const int SourceLimit = 1024 * 1024;

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
        ReadContent(context, root, path, limit, 8192, (buffer, count) => content.Write(buffer, 0, count),
            afterVerification, cancellationToken);
        return content.ToArray();
    }

    public static (long Length, string Sha256) Hash(CheckpointCleanupContext context, string root, string path, int limit,
        CancellationToken cancellationToken = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = ReadContent(context, root, path, limit, 64 * 1024,
            (buffer, count) => hash.AppendData(buffer, 0, count), null, cancellationToken);
        return (length, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static long ReadContent(CheckpointCleanupContext context, string root, string path, int limit,
        int bufferSize, Action<byte[], int> consume, Action? afterVerification, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3), context.Time);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
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
            request = new(0, 0x00200000 | 0x08000000); // OPEN_REPARSE_POINT | SEQUENTIAL_SCAN
        }
        else throw new InvalidDataException("opened-handle verification unsupported on this platform");
        using var handle = context.Io.OpenRead(path, request);
        var metadata = handle.Inspect();
        var refusal = Refusal(context, metadata.Regular, metadata.Links, metadata.Path, root);
        if (refusal is not null) throw new InvalidDataException(refusal);
        if (metadata.Size > (ulong)limit) throw new InvalidDataException("selected file exceeds size limit");
        afterVerification?.Invoke();
        using var stream = handle.OpenStream();
        var buffer = new byte[bufferSize];
        long length = 0;
        int count;
        while ((count = stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, (long)limit - length + 1)), deadline.Token).AsTask().WaitAsync(deadline.Token).GetAwaiter().GetResult()) != 0)
        {
            if (length + count > limit) throw new InvalidDataException("selected file exceeds size limit");
            consume(buffer, count);
            length += count;
        }
        return length;
    }
}
