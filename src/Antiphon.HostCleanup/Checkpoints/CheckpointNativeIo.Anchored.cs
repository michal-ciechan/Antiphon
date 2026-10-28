using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.HostCleanup.Checkpoints;

public sealed partial class CheckpointNativeIo
{
    public IReadOnlyList<AnchoredChild> ListAnchored(string directory)
    {
        if (OperatingSystem.IsLinux()) return ListAnchoredLinux(directory);
        if (OperatingSystem.IsWindows()) return ListAnchoredWindows(directory);
        throw new IOException("anchored-delete-unavailable");
    }

    public void UnlinkAnchored(string directory, string name, bool directoryEntry)
    {
        if (OperatingSystem.IsLinux()) UnlinkAnchoredLinux(directory, name, directoryEntry);
        else if (OperatingSystem.IsWindows()) UnlinkAnchoredWindows(directory, name, directoryEntry);
        else throw new IOException("anchored-delete-unavailable");
    }

    public void TruncateAnchored(string directory, string name, long length)
    {
        if (length < 0) throw new IOException("anchored-path-unsafe");
        if (OperatingSystem.IsLinux()) TruncateAnchoredLinux(directory, name, length);
        else if (OperatingSystem.IsWindows()) TruncateAnchoredWindows(directory, name, length);
        else throw new IOException("anchored-delete-unavailable");
    }

    // O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC. A symlink component fails the walk (ELOOP)
    // before any unlinkat. Kernel 4.15 has no openat2 RESOLVE_NO_SYMLINKS.
    private const int LinuxDirectoryNoFollow = 0x10000 | 0x20000 | 0x80000;

    private IReadOnlyList<AnchoredChild> ListAnchoredLinux(string directory)
    {
        var fd = OpenNoFollowChain(directory);
        try
        {
            var names = new List<string>();
            foreach (var entry in Directory.EnumerateFileSystemEntries("/proc/self/fd/" + fd))
            {
                var name = Path.GetFileName(entry);
                if (!AnchoredDelete.IncludeListedName(windows: false, name)) continue;
                names.Add(name);
            }
            var children = new List<AnchoredChild>(names.Count);
            foreach (var name in names)
            {
                if (Statx(fd, name, 0x100, 0x3CF, out var stat) != 0 || (stat.Mask & 0x3CF) != 0x3CF)
                    throw new IOException("anchored-entry-stat-failed: errno=" + Marshal.GetLastPInvokeError());
                var kind = KindFromMode(stat.Mode);
                children.Add(new AnchoredChild(name, kind, kind == "file" ? CheckedSize(stat.Size) : 0));
            }
            return children;
        }
        finally { _ = CloseNative(fd); }
    }

    private void UnlinkAnchoredLinux(string directory, string name, bool directoryEntry)
    {
        if (!AnchoredDelete.EntryNameSafe(windows: false, name) || name is "." or "..")
            throw new IOException("anchored-path-unsafe");
        var fd = OpenNoFollowChain(directory);
        try
        {
            // AT_SYMLINK_NOFOLLOW. FIFO, socket and symlink names are not opened.
            if (Statx(fd, name, 0x100, 0x3CF, out var stat) != 0 || (stat.Mask & 0x3CF) != 0x3CF)
                throw new IOException("anchored-entry-stat-failed: errno=" + Marshal.GetLastPInvokeError());
            var isDirectory = (stat.Mode & 0xF000) == 0x4000;
            if (directoryEntry != isDirectory)
                throw new IOException(directoryEntry ? "anchored-not-directory" : "anchored-not-leaf");
            if (UnlinkAt(fd, name, directoryEntry ? 0x200 : 0) != 0)
                throw new IOException("anchored-unlink-failed: errno=" + Marshal.GetLastPInvokeError());
        }
        finally { _ = CloseNative(fd); }
    }

    private void TruncateAnchoredLinux(string directory, string name, long length)
    {
        if (!AnchoredDelete.EntryNameSafe(windows: false, name) || name is "." or "..")
            throw new IOException("anchored-path-unsafe");
        var fd = OpenNoFollowChain(directory);
        try
        {
            if (Statx(fd, name, 0x100, 0x3CF, out var stat) != 0 || (stat.Mask & 0x3CF) != 0x3CF)
                throw new IOException("anchored-entry-stat-failed: errno=" + Marshal.GetLastPInvokeError());
            if ((stat.Mode & 0xF000) != 0x8000 || length > CheckedSize(stat.Size))
                throw new IOException("anchored-not-file");
            // O_WRONLY|O_NOFOLLOW|O_NONBLOCK|O_CLOEXEC. A raced FIFO must not block.
            var file = OpenAt(fd, name, 1 | 0x20000 | 0x800 | 0x80000);
            if (file < 0) throw new IOException("anchored-open-failed: errno=" + Marshal.GetLastPInvokeError());
            try
            {
                if (Statx(file, "", 0x1000, 0x3CF, out var opened) != 0 || (opened.Mode & 0xF000) != 0x8000)
                    throw new IOException("anchored-not-file");
                if (FTruncate(file, length) != 0)
                    throw new IOException("anchored-truncate-failed: errno=" + Marshal.GetLastPInvokeError());
            }
            finally { _ = CloseNative(file); }
        }
        finally { _ = CloseNative(fd); }
    }

    private static int OpenNoFollowChain(string directory)
    {
        if (directory.Length == 0 || directory[0] != '/')
            throw new IOException("anchored-path-unsafe");
        var fd = OpenAt(-100, "/", LinuxDirectoryNoFollow);
        if (fd < 0) throw new IOException("anchored-open-failed: errno=" + Marshal.GetLastPInvokeError());
        try
        {
            foreach (var component in directory.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (component is "." or ".." || !AnchoredDelete.EntryNameSafe(windows: false, component))
                    throw new IOException("anchored-path-unsafe");
                var next = OpenAt(fd, component, LinuxDirectoryNoFollow);
                if (next < 0) throw new IOException("anchored-open-failed: errno=" + Marshal.GetLastPInvokeError());
                if (Statx(next, "", 0x1000, 0x3CF, out var stat) != 0 || (stat.Mode & 0xF000) != 0x4000)
                {
                    _ = CloseNative(next);
                    throw new IOException("anchored-open-failed");
                }
                _ = CloseNative(fd);
                fd = next;
            }
            return fd;
        }
        catch
        {
            _ = CloseNative(fd);
            throw;
        }
    }

    private static string KindFromMode(ushort mode) => (mode & 0xF000) switch
    {
        0x8000 => "file",
        0x4000 => "directory",
        0xA000 => "symlink",
        0x1000 => "fifo",
        0xC000 => "socket",
        _ => "other",
    };

    private static long CheckedSize(ulong size) =>
        size > long.MaxValue ? throw new IOException("anchored-entry-stat-failed") : (long)size;

    private IReadOnlyList<AnchoredChild> ListAnchoredWindows(string directory)
    {
        using var handle = OpenNoFollowChainWindows(directory);
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity)
            throw new IOException("anchored-open-failed: win32=" + Marshal.GetLastPInvokeError());
        var final = buffer.ToString();
        if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) final = @"\\" + final[8..];
        else if (final.StartsWith(@"\\?\", StringComparison.Ordinal)) final = final[4..];
        var children = new List<AnchoredChild>();
        foreach (var info in new DirectoryInfo(final).EnumerateFileSystemInfos())
        {
            if (!AnchoredDelete.IncludeListedName(windows: true, info.Name)) continue;
            var attributes = info.Attributes;
            string kind;
            long size = 0;
            if ((attributes & FileAttributes.ReparsePoint) != 0) kind = "symlink";
            else if ((attributes & FileAttributes.Directory) != 0) kind = "directory";
            else
            {
                kind = "file";
                size = info is FileInfo file ? file.Length : 0;
            }
            children.Add(new AnchoredChild(info.Name, kind, size));
        }
        return children;
    }

    private void UnlinkAnchoredWindows(string directory, string name, bool directoryEntry)
    {
        if (!AnchoredDelete.EntryNameSafe(windows: true, name) || name is "." or "..")
            throw new IOException("anchored-path-unsafe");
        using var parent = OpenNoFollowChainWindows(directory);
        // OPEN_REPARSE_POINT, and FILE_DIRECTORY_FILE only for a real directory.
        // A leaf omits FILE_NON_DIRECTORY_FILE so the reparse entry itself is removed.
        uint options = 0x20 | 0x00200000 | (directoryEntry ? 0x1u : 0u);
        using var child = OpenChild(parent, name, options, 0x00110080);
        if (!GetFileInformationByHandle(child, out var info))
            throw new IOException("anchored-entry-stat-failed: win32=" + Marshal.GetLastPInvokeError());
        var reparse = (info.Attributes & 0x400u) != 0;
        var isDirectory = (info.Attributes & 0x10u) != 0 && !reparse;
        if (directoryEntry)
        {
            if (!isDirectory) throw new IOException("anchored-not-directory");
        }
        else if (isDirectory) throw new IOException("anchored-not-leaf");
        var disposition = new FileDisposition { DeleteFile = true };
        if (!SetFileInformationByHandle(child, 4, ref disposition, Marshal.SizeOf<FileDisposition>()))
            throw new IOException("anchored-unlink-failed: win32=" + Marshal.GetLastPInvokeError());
    }

    private void TruncateAnchoredWindows(string directory, string name, long length)
    {
        if (!AnchoredDelete.EntryNameSafe(windows: true, name) || name is "." or "..")
            throw new IOException("anchored-path-unsafe");
        using var parent = OpenNoFollowChainWindows(directory);
        var child = OpenChild(parent, name, 0x40 | 0x00200000 | 0x20, 0xC0000000);
        FileStream? stream = null;
        try
        {
            if (!GetFileInformationByHandle(child, out var info))
                throw new IOException("anchored-entry-stat-failed: win32=" + Marshal.GetLastPInvokeError());
            var size = ((long)info.SizeHigh << 32) | info.SizeLow;
            if ((info.Attributes & 0x400u) != 0 || (info.Attributes & 0x10u) != 0 || length > size)
                throw new IOException("anchored-not-file");
            stream = new FileStream(child, FileAccess.ReadWrite);
            stream.SetLength(length);
        }
        finally
        {
            if (stream is null) child.Dispose();
            else stream.Dispose();
        }
    }

    private static SafeFileHandle OpenNoFollowChainWindows(string directory)
    {
        var root = Path.GetPathRoot(directory);
        if (string.IsNullOrEmpty(root) || !Path.IsPathRooted(directory))
            throw new IOException("anchored-path-unsafe");
        const uint flags = 0x02000000 | 0x00200000;
        var handle = CreateFile(root, 0x80000000, 7, IntPtr.Zero, 3, flags, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException("anchored-open-failed: win32=" + error);
        }
        try
        {
            if (!DirectoryHandle(handle)) throw new IOException("anchored-open-failed");
            foreach (var component in directory[root.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (component is "." or ".." || !AnchoredDelete.EntryNameSafe(windows: true, component))
                    throw new IOException("anchored-path-unsafe");
                var next = OpenChild(handle, component, 0x1 | 0x00200000 | 0x20, 0x80000000);
                if (!DirectoryHandle(next))
                {
                    next.Dispose();
                    throw new IOException("anchored-open-failed");
                }
                handle.Dispose();
                handle = next;
            }
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static bool DirectoryHandle(SafeFileHandle handle) =>
        GetFileInformationByHandle(handle, out var info)
        && (info.Attributes & 0x400u) == 0
        && (info.Attributes & 0x10u) != 0;

    private static SafeFileHandle OpenChild(SafeFileHandle parent, string name, uint options, uint access)
    {
        var buffer = Marshal.StringToHGlobalUni(name);
        var unicode = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        try
        {
            Marshal.StructureToPtr(new UnicodeString
            {
                Length = (ushort)(name.Length * 2),
                MaximumLength = (ushort)((name.Length + 1) * 2),
                Buffer = buffer,
            }, unicode, false);
            var added = false;
            parent.DangerousAddRef(ref added);
            try
            {
                var attributes = new ObjectAttributes
                {
                    Length = Marshal.SizeOf<ObjectAttributes>(),
                    RootDirectory = parent.DangerousGetHandle(),
                    ObjectName = unicode,
                    Attributes = 0x40,
                };
                var status = NtCreateFile(out var handle, access, ref attributes, out _, IntPtr.Zero, 0, 7, 1, options, IntPtr.Zero, 0);
                if (status < 0) throw new IOException("anchored-entry-open-failed: status=0x" + status.ToString("X8"));
                return new SafeFileHandle(handle, ownsHandle: true);
            }
            finally
            {
                if (added) parent.DangerousRelease();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(unicode);
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(int dirfd, string path, int flags);

    [DllImport("libc", EntryPoint = "ftruncate", SetLastError = true)]
    private static extern int FTruncate(int fd, long length);
}
