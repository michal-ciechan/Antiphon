using Antiphon.Server.Application.Interfaces;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Server.Infrastructure.Files;

public sealed class ChannelReplyAttachmentReader : IChannelReplyAttachmentReader
{
    public async Task<byte[]> ReadAttachmentAsync(string path, IReadOnlyList<string> allowedRoots,
        long maxBytes, CancellationToken ct)
    {
        if (maxBytes < 0 || maxBytes > 20 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        ValidatePath(path, allowedRoots);
        await using var stream = OpenRegularFile(path);
        var length = stream.Length;
        if (length > maxBytes) throw new ChannelReplyFileTooLargeException(length);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (read > maxBytes - output.Length)
                throw new ChannelReplyFileTooLargeException(output.Length + read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return output.ToArray();
    }

    public async Task<string> ReadTextAsync(string path, IReadOnlyList<string> allowedRoots,
        long maxBytes, CancellationToken ct)
    {
        var bytes = await ReadAttachmentAsync(path, allowedRoots, maxBytes, ct);
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }

    internal static void ValidatePath(string path, IReadOnlyList<string> roots)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!Path.IsPathFullyQualified(path) || path.Split('/', '\\').Any(p => p is "." or ".."))
            throw new InvalidDataException("A source path must be absolute and cannot contain traversal.");
        var canonical = Path.GetFullPath(path);
        if (!roots.Any(root => Path.IsPathFullyQualified(root)
            && !root.Split('/', '\\').Any(p => p is "." or "..")
            && canonical.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, comparison)))
            throw new InvalidDataException("A source path is outside the captured allowed roots.");
        for (string? component = canonical; component is not null; component = Path.GetDirectoryName(component))
        {
            if ((File.GetAttributes(component) & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidDataException("Linked paths and devices are not source files.");
        }
        if ((File.GetAttributes(canonical) & FileAttributes.Directory) != 0)
            throw new InvalidDataException("A source must be a regular file.");
    }

    internal static async Task<(long Length, string Sha256)> HashFileAsync(string path,
        IReadOnlyList<string> roots, long maxBytes, CancellationToken ct)
    {
        ValidatePath(path, roots);
        await using var stream = OpenRegularFile(path);
        if (stream.Length > maxBytes) throw new ChannelReplyFileTooLargeException(stream.Length);
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long length = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (read > maxBytes - length) throw new ChannelReplyFileTooLargeException(length + read);
            length += read;
            hash.AppendData(buffer, 0, read);
        }
        return (length, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static FileStream OpenRegularFile(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            // Resolve each component through retained directory handles. O_NOFOLLOW
            // rejects symlink swaps; O_NONBLOCK prevents a substituted FIFO blocking open.
            using var root = OpenAt(-100, "/", 0x200000 | 0x10000 | 0x80000);
            SafeFileHandle? directory = null;
            try
            {
                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < parts.Length - 1; i++)
                {
                    var next = OpenAt((directory ?? root).DangerousGetHandle().ToInt32(), parts[i],
                        0x200000 | 0x10000 | 0x20000 | 0x80000);
                    directory?.Dispose();
                    directory = next;
                }
                var file = OpenAt((directory ?? root).DangerousGetHandle().ToInt32(), parts[^1],
                    0x800 | 0x20000 | 0x80000);
                try
                {
                    if (Statx(file.DangerousGetHandle().ToInt32(), "", 0x1000, 1, out var stat) != 0
                        || (stat.Mode & 0xf000) != 0x8000)
                        throw new InvalidDataException("A source must be a regular file.");
                    return new FileStream(file, FileAccess.Read, 64 * 1024, isAsync: false);
                }
                catch { file.Dispose(); throw; }
            }
            finally { directory?.Dispose(); }
        }
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Safe source reads require Windows or Linux.");
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try { ValidatePath(path, [Path.GetDirectoryName(path)!]); _ = stream.Length; return stream; }
        catch { stream.Dispose(); throw; }
    }

    private static SafeFileHandle OpenAt(int directory, string path, int flags)
    {
        var fd = NativeOpenAt(directory, path, flags);
        if (fd < 0) throw new IOException("The source could not be safely opened.");
        return new SafeFileHandle((IntPtr)fd, ownsHandle: true);
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct FileStat { [FieldOffset(28)] public ushort Mode; }
    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int NativeOpenAt(int directory, string path, int flags);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int directory, string path, int flags, uint mask, out FileStat stat);
}
