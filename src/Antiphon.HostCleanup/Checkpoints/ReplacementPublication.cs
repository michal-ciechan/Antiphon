using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

// An overwrite must leave either the previous generation or the new bytes recoverable.
// The temporary and the backup stay until the destination matches the new bytes. A clean
// refusal that leaves the destination in place still removes the unused temporary.
internal static class ReplacementPublication
{
    public static void Publish(ICheckpointCleanupIo io, string source, string destination, ReadOnlySpan<byte> expected)
    {
        string? backup = null;
        if (io.FileExists(destination))
        {
            backup = destination + "." + Guid.NewGuid().ToString("N") + ".bak";
            io.Copy(destination, backup, overwrite: false);
        }

        Exception? failure = null;
        try { io.Move(source, destination, overwrite: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failure = ex; }

        if (Matches(io, destination, expected))
        {
            TryUnlink(io, source);
            TryUnlink(io, backup);
            return;
        }

        var missing = !io.FileExists(destination);
        if (missing && backup is not null && io.FileExists(backup))
        {
            try { io.Move(backup, destination, overwrite: false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        // Sharing denial and other closed failures leave the previous file in place.
        // 1175/1176/1177, or any failure that removed the destination, keep the temporary.
        if (failure is not null && !missing && !IsReplaceLoss(failure) && io.FileExists(destination))
        {
            TryUnlink(io, source);
            TryUnlink(io, backup);
            throw failure;
        }

        if (failure is not null) throw failure;
        throw new IOException("replacement-unverified");
    }

    private static bool IsReplaceLoss(Exception failure)
    {
        var code = failure.HResult & 0xFFFF;
        if (code is 1175 or 1176 or 1177) return true;
        return failure.InnerException is { } inner && IsReplaceLoss(inner);
    }

    private static bool Matches(ICheckpointCleanupIo io, string path, ReadOnlySpan<byte> expected)
    {
        try
        {
            if (!io.FileExists(path)) return false;
            using var handle = io.OpenRead(path, new CheckpointReadOpen(0x20000, 0x00200000));
            using var stream = handle.OpenStream();
            var actual = new byte[expected.Length + 1];
            var read = 0;
            while (read < actual.Length)
            {
                var count = stream.Read(actual, read, actual.Length - read);
                if (count == 0) break;
                read += count;
            }

            return read == expected.Length && actual.AsSpan(0, read).SequenceEqual(expected);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryUnlink(ICheckpointCleanupIo io, string? path)
    {
        if (path is null) return;
        try
        {
            if (io.FileExists(path)) io.Unlink(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
