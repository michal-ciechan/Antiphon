#requires -Version 7.0
# Bounded regular-file reads. Embed the native boundary to avoid a repository-derived source loader.
if (-not ('Antiphon.Checkpoints.Coverage.ConfinedFileReader' -as [type])) {
    Add-Type -TypeDefinition @'
#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Checkpoints.Coverage;

/// <summary>Read-only native boundary. Verify metadata and final path on the exact handle read.</summary>
public static class ConfinedFileReader
{
    public const int DocumentLimit = 4 * 1024 * 1024;
    public const int SourceLimit = 1024 * 1024;
    public const long ContentLimit = 1L << 30;
    // CARD-1318 stalled-receipt contract: a read that delivers nothing for this long is refused. CARD-1363 (the CARD-1358
    // design): the window bounds the open, the inspection and each chunk, and every delivered chunk re-arms it, so the
    // total is bounded by the chunk count and not by a throughput assumption.
    public static readonly TimeSpan StallDeadline = TimeSpan.FromSeconds(3);
    // Linux handle reads use workers; 256 KiB keeps a 7.5 MiB file at 30 hops.
    public const int ReadChunk = 256 * 1024;

    public static string? Refusal(bool isRegularFile, ulong linkCount, string resolvedPath, string root)
    {
        if (!isRegularFile) return "selected file is not regular";
        if (linkCount != 1) return "selected file has multiple links";
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!Path.GetFullPath(resolvedPath).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            return "opened file is outside root";
        return null;
    }

    // The optional hook is a deterministic test seam at the verification/read boundary.
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
        ReadBytes(root, path, limit, afterVerification, cancellationToken, null, null);

    // CARD-1363: the clock and the handle stream are deterministic test seams at the read boundary; production passes neither.
    public static byte[] ReadBytes(string root, string path, int limit, Action? afterVerification, CancellationToken cancellationToken,
        TimeProvider? time, Func<SafeFileHandle, Stream>? openStream)
    {
        using var content = new MemoryStream();
        Consume(root, path, limit, afterVerification, cancellationToken, time, openStream, span => content.Write(span));
        return content.ToArray();
    }

    public static (long Length, string Sha256) Digest(string root, string path, long limit, CancellationToken cancellationToken = default) =>
        Digest(root, path, limit, null, cancellationToken, null, null);

    public static (long Length, string Sha256) Digest(string root, string path, long limit, Action? afterVerification,
        CancellationToken cancellationToken, TimeProvider? time, Func<SafeFileHandle, Stream>? openStream)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        Consume(root, path, limit, afterVerification, cancellationToken, time, openStream, span =>
        {
            hash.AppendData(span);
            total += span.Length;
        });
        return (total, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private delegate void ContentSink(ReadOnlySpan<byte> bytes);

    private static void Consume(string root, string path, long limit, Action? afterVerification, CancellationToken cancellationToken,
        TimeProvider? time, Func<SafeFileHandle, Stream>? openStream, ContentSink sink)
    {
        using var stall = new CancellationTokenSource(StallDeadline, time ?? TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stall.Token);
        deadline.Token.ThrowIfCancellationRequested();
        using var handle = OperatingSystem.IsLinux() ? OpenLinux(path) : OperatingSystem.IsWindows() ? OpenWindows(path)
            : throw new InvalidDataException("opened-handle verification unsupported on this platform");
        var metadata = OperatingSystem.IsLinux() ? InspectLinux(handle) : InspectWindows(handle);
        var refusal = Refusal(metadata.Regular, metadata.Links, metadata.Path, root);
        if (refusal is not null) throw new InvalidDataException(refusal);
        if (metadata.Size > (ulong)limit) throw new InvalidDataException("selected file exceeds size limit");
        afterVerification?.Invoke();
        using var stream = openStream is null ? new FileStream(handle, FileAccess.Read) : openStream(handle);
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
            // Only the retained handle can prove that an EOF stall still represents a complete read.
            if (total == (long)metadata.Size &&
                (OperatingSystem.IsLinux() ? InspectLinux(handle) : InspectWindows(handle)).Size == metadata.Size)
                return;
            throw new IOException($"read-stalled: {total} of {metadata.Size} bytes; no progress within {StallDeadline.TotalSeconds:0}s");
        }
    }

    private static SafeFileHandle OpenLinux(string path)
    {
        // statx is ABI-stable on Linux architectures. Precheck avoids even attempting special files;
        // O_NONBLOCK also prevents a FIFO swapped in after that precheck from blocking open.
        if (Statx(-100, path, 0x100, 0x205, out var before) != 0) throw NativeFailure();
        if ((before.Mode & 0xF000) != 0x8000) throw new InvalidDataException("selected file is not regular");
        var fd = Open(path, 0x80000 | 0x20000 | 0x800); // O_CLOEXEC | O_NOFOLLOW | O_NONBLOCK, O_RDONLY=0
        if (fd < 0) throw NativeFailure();
        return new SafeFileHandle((IntPtr)fd, ownsHandle: true);
    }

    private static (bool Regular, ulong Links, ulong Size, string Path) InspectLinux(SafeFileHandle handle)
    {
        var fd = handle.DangerousGetHandle().ToInt32();
        if (Statx(fd, "", 0x1000, 0x205, out var stat) != 0 || (stat.Mask & 0x205) != 0x205) throw NativeFailure();
        var buffer = new byte[32768];
        var count = ReadLink("/proc/self/fd/" + fd, buffer, (nuint)buffer.Length);
        if (count <= 0 || count >= buffer.Length) throw NativeFailure();
        return ((stat.Mode & 0xF000) == 0x8000, stat.Links, stat.Size, Encoding.UTF8.GetString(buffer, 0, (int)count));
    }

    private static SafeFileHandle OpenWindows(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            throw new InvalidDataException("selected file is not regular");
        // FileStream infers async mode from the retained handle; do not queue synchronous
        // Windows I/O behind the callers blocking on ReadAsync's three-second deadline.
        var handle = CreateFile(path, 0x80000000, 7, IntPtr.Zero, 3, 0x00200000 | 0x08000000 | 0x40000000, IntPtr.Zero); // OPEN_REPARSE_POINT | SEQUENTIAL_SCAN | OVERLAPPED
        if (handle.IsInvalid) { handle.Dispose(); throw NativeFailure(); }
        return handle;
    }

    private static (bool Regular, ulong Links, ulong Size, string Path) InspectWindows(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw NativeFailure();
        var path = new StringBuilder(32768);
        var count = GetFinalPathNameByHandle(handle, path, (uint)path.Capacity, 0);
        if (count == 0 || count >= path.Capacity) throw NativeFailure();
        var final = path.ToString();
        if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) final = @"\\" + final[8..];
        else if (final.StartsWith(@"\\?\", StringComparison.Ordinal)) final = final[4..];
        return (GetFileType(handle) == 1 && (info.Attributes & (0x10u | 0x400u)) == 0, info.Links,
            ((ulong)info.SizeHigh << 32) | info.SizeLow, final);
    }

    private static IOException NativeFailure() => new("opened-handle verification failed: errno=" + Marshal.GetLastPInvokeError());

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint Links;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(40)] public ulong Size;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsInfo
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int Statx(int fd, string path, int flags, uint mask, out LinuxStatx stat);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "readlink", SetLastError = true)] private static extern nint ReadLink(string path, byte[] buffer, nuint size);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out WindowsInfo info);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(SafeFileHandle handle);
}

'@
}
function Read-CheckpointBytes {
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    return ,([Antiphon.Checkpoints.Coverage.ConfinedFileReader]::ReadBytes([IO.Path]::GetDirectoryName($full), $full, 67108864, $null, [Threading.CancellationToken]::None))
}
function Get-CheckpointFileDigest {
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    $digest = [Antiphon.Checkpoints.Coverage.ConfinedFileReader]::Digest([IO.Path]::GetDirectoryName($full), $full, [Antiphon.Checkpoints.Coverage.ConfinedFileReader]::ContentLimit, [Threading.CancellationToken]::None)
    return [pscustomobject]@{ Length = [long]$digest.Item1; Sha256 = [string]$digest.Item2 }
}
function Read-CheckpointText {
    param([string]$Path)
    $bytes = Read-CheckpointBytes $Path
    $reader = [IO.StreamReader]::new([IO.MemoryStream]::new([byte[]]$bytes))
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}
