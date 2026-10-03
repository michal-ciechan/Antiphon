using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Checkpoints.Coverage;

/// <summary>Read-only native boundary. Verify metadata and final path on the exact handle read.</summary>
public static class ConfinedFileReader
{
    public const int DocumentLimit = 4 * 1024 * 1024;
    public const int SourceLimit = 1024 * 1024;

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
    public static string Read(string root, string path, int limit, Action? afterVerification = null)
    {
        using var handle = OperatingSystem.IsLinux() ? OpenLinux(path) : OperatingSystem.IsWindows() ? OpenWindows(path)
            : throw new InvalidDataException("opened-handle verification unsupported on this platform");
        var metadata = OperatingSystem.IsLinux() ? InspectLinux(handle) : InspectWindows(handle);
        var refusal = Refusal(metadata.Regular, metadata.Links, metadata.Path, root);
        if (refusal is not null) throw new InvalidDataException(refusal);
        if (metadata.Size > (ulong)limit) throw new InvalidDataException("selected file exceeds size limit");
        afterVerification?.Invoke();
        using var stream = new FileStream(handle, FileAccess.Read);
        using var content = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = stream.Read(buffer, 0, Math.Min(buffer.Length, limit - (int)content.Length + 1))) != 0)
        {
            if (content.Length + count > limit) throw new InvalidDataException("selected file exceeds size limit");
            content.Write(buffer, 0, count);
        }
        content.Position = 0;
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
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
        var handle = CreateFile(path, 0x80000000, 7, IntPtr.Zero, 3, 0x00200000 | 0x08000000, IntPtr.Zero); // OPEN_REPARSE_POINT | SEQUENTIAL_SCAN
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
