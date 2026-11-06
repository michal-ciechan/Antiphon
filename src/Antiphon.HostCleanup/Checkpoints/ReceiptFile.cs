using Antiphon.HostCleanup.Checkpoints;
using System.Text;

namespace Antiphon.Checkpoints;

/// <summary>Read receipts only through a bounded, regular-file, verified handle.</summary>
public static class ReceiptFile
{
    public static byte[] ReadBytes(string path, CancellationToken cancellationToken = default) =>
        ReadBytes(CheckpointCleanupDefaults.Context, path, cancellationToken);

    public static byte[] ReadBytes(CheckpointCleanupContext context, string path, CancellationToken cancellationToken = default)
    {
        try
        {
            // Windows metadata lookup refuses pipe/device namespaces before CreateFile.
            // Linux CheckpointFileReader uses statx(AT_SYMLINK_NOFOLLOW), O_NOFOLLOW,
            // O_NONBLOCK and then statx on the retained descriptor before reading.
            if (context.Io.Platform == CheckpointPlatform.Windows &&
                (context.Io.Attributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("receipt-nonregular: " + path);
            return CheckpointFileReader.ReadBytes(context, Path.GetDirectoryName(Path.GetFullPath(path))!, path, 64 * 1024 * 1024, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException)
        { throw new IOException("receipt-read-refused: " + path + ": " + ex.Message, ex); }
    }

    public static (long Length, string Sha256) Digest(CheckpointCleanupContext context, string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (context.Io.Platform == CheckpointPlatform.Windows &&
                (context.Io.Attributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("receipt-nonregular: " + path);
            return CheckpointFileReader.Digest(context, Path.GetDirectoryName(Path.GetFullPath(path))!, path,
                CheckpointFileReader.ContentLimit, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException)
        { throw new IOException("receipt-read-refused: " + path + ": " + ex.Message, ex); }
    }

    public static string ReadText(string path) => ReadText(CheckpointCleanupDefaults.Context, path);

    /// <summary>Qualification input must not hide malformed bytes behind replacement characters.</summary>
    public static string ReadStrictText(string path) => ReadStrictText(CheckpointCleanupDefaults.Context, path);

    public static string ReadStrictText(CheckpointCleanupContext context, string path, CancellationToken cancellationToken = default)
    {
        ReadOnlySpan<byte> bytes = ReadBytes(context, path, cancellationToken);
        Encoding encoding = new UTF8Encoding(false, true);
        var preamble = 0;
        // Keep the BOM formats accepted by the legacy reader, with throwing decoders for each.
        // UTF-32 LE must precede UTF-16 LE because their preambles overlap.
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xff, 0xfe, 0, 0]))
        { encoding = new UTF32Encoding(false, false, true); preamble = 4; }
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0, 0, 0xfe, 0xff]))
        { encoding = new UTF32Encoding(true, false, true); preamble = 4; }
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xff, 0xfe]))
        { encoding = new UnicodeEncoding(false, false, true); preamble = 2; }
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xfe, 0xff]))
        { encoding = new UnicodeEncoding(true, false, true); preamble = 2; }
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xef, 0xbb, 0xbf]))
            preamble = 3;
        try { return encoding.GetString(bytes[preamble..]); }
        catch (DecoderFallbackException ex) { throw new IOException("receipt-invalid-encoding", ex); }
    }

    public static string ReadText(CheckpointCleanupContext context, string path, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(new MemoryStream(ReadBytes(context, path, cancellationToken)));
        return reader.ReadToEnd();
    }
}
