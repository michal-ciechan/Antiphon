using Antiphon.HostCleanup.Checkpoints;

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

    public static string ReadText(CheckpointCleanupContext context, string path, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(new MemoryStream(ReadBytes(context, path, cancellationToken)));
        return reader.ReadToEnd();
    }
}
