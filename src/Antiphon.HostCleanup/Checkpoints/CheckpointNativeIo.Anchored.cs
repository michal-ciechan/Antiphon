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

    public void UnlinkAnchored(string directory, string name, bool directoryEntry, ulong device = 0, ulong inode = 0)
    {
        if (OperatingSystem.IsLinux()) UnlinkAnchoredLinux(directory, name, directoryEntry, device, inode);
        else if (OperatingSystem.IsWindows()) UnlinkAnchoredWindows(directory, name, directoryEntry, device, inode);
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
                if (Statx(fd, name, 0x100, 0x3CF, out var stat) != 0)
                    throw AnchoredFailure("anchored-entry-stat-failed", Marshal.GetLastPInvokeError());
                if ((stat.Mask & 0x3CF) != 0x3CF)
                    throw new IOException("anchored-entry-stat-failed");
                var kind = KindFromMode(stat.Mode);
                children.Add(new AnchoredChild(name, kind, kind == "file" ? CheckedSize(stat.Size) : 0,
                    LinuxDevice(stat), stat.Inode));
            }
            return children;
        }
        finally { _ = CloseNative(fd); }
    }

    private void UnlinkAnchoredLinux(string directory, string name, bool directoryEntry, ulong device, ulong inode)
    {
        if (!AnchoredDelete.EntryNameSafe(windows: false, name) || name is "." or "..")
            throw new IOException("anchored-path-unsafe");
        var fd = OpenNoFollowChain(directory);
        try
        {
            // AT_SYMLINK_NOFOLLOW. FIFO, socket and symlink names are not opened.
            if (Statx(fd, name, 0x100, 0x3CF, out var stat) != 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                // File.Delete tolerated a name that vanished after it was listed.
                if (!directoryEntry && errno == 2) return;
                throw AnchoredFailure("anchored-entry-stat-failed", errno);
            }
            if ((stat.Mask & 0x3CF) != 0x3CF)
                throw new IOException("anchored-entry-stat-failed");
            if (inode != 0 && (LinuxDevice(stat) != device || stat.Inode != inode))
                throw new IOException("anchored-identity-changed");
            var isDirectory = (stat.Mode & 0xF000) == 0x4000;
            if (directoryEntry != isDirectory)
                throw new IOException(directoryEntry ? "anchored-not-directory" : "anchored-not-leaf");
            if (UnlinkAt(fd, name, directoryEntry ? 0x200 : 0) != 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                if (!directoryEntry && errno == 2) return;
                throw AnchoredFailure("anchored-unlink-failed", errno);
            }
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
            if (Statx(fd, name, 0x100, 0x3CF, out var stat) != 0)
                throw AnchoredFailure("anchored-entry-stat-failed", Marshal.GetLastPInvokeError());
            if ((stat.Mask & 0x3CF) != 0x3CF)
                throw new IOException("anchored-entry-stat-failed");
            if ((stat.Mode & 0xF000) != 0x8000 || length > CheckedSize(stat.Size))
                throw new IOException("anchored-not-file");
            // O_WRONLY|O_NOFOLLOW|O_NONBLOCK|O_CLOEXEC. A raced FIFO must not block.
            var file = OpenAt(fd, name, 1 | 0x20000 | 0x800 | 0x80000);
            if (file < 0) throw AnchoredFailure("anchored-open-failed", Marshal.GetLastPInvokeError());
            try
            {
                if (Statx(file, "", 0x1000, 0x3CF, out var opened) != 0 || (opened.Mode & 0xF000) != 0x8000)
                    throw new IOException("anchored-not-file");
                // STATX_NLINK is 0x4. A second link names the same bytes outside this root.
                if ((opened.Mask & 0x4) == 0 || opened.Links != 1)
                    throw new AnchoredHardLinkException();
                if (FTruncate(file, length) != 0)
                    throw AnchoredFailure("anchored-truncate-failed", Marshal.GetLastPInvokeError());
                // Preserve durable truncation. A crash must not restore the reclaimed bytes.
                if (Fsync(file) != 0)
                    throw AnchoredFailure("anchored-truncate-failed", Marshal.GetLastPInvokeError());
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
        if (fd < 0) throw AnchoredFailure("anchored-open-failed", Marshal.GetLastPInvokeError());
        try
        {
            foreach (var component in directory.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (component is "." or ".." || !AnchoredDelete.EntryNameSafe(windows: false, component))
                    throw new IOException("anchored-path-unsafe");
                var next = OpenAt(fd, component, LinuxDirectoryNoFollow);
                if (next < 0) throw AnchoredFailure("anchored-open-failed", Marshal.GetLastPInvokeError());
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

    private static ulong LinuxDevice(LinuxStatx stat) => ((ulong)stat.DevMajor << 32) | stat.DevMinor;

    // EPERM, EACCES and EROFS are what File.Delete reports as UnauthorizedAccessException.
    private static Exception AnchoredFailure(string what, int errno) =>
        errno is 1 or 13 or 30
            ? new UnauthorizedAccessException(what + ": errno=" + errno)
            : new IOException(what + ": errno=" + errno);

    private static Exception AnchoredWin32(string what, int error) =>
        error == 5
            ? new UnauthorizedAccessException(what + ": win32=" + error)
            : new IOException(what + ": win32=" + error);

    private static Exception AnchoredNtStatus(int status)
    {
        var message = "anchored-entry-open-failed: status=0x" + status.ToString("X8");
        var code = unchecked((uint)status);
        // OBJECT_NAME_NOT_FOUND, OBJECT_PATH_NOT_FOUND, ACCESS_DENIED, CANNOT_DELETE.
        if (code is 0xC0000034u or 0xC000003Au) return new FileNotFoundException(message);
        if (code is 0xC0000022u or 0xC0000121u) return new UnauthorizedAccessException(message);
        return new IOException(message);
    }

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

    private void UnlinkAnchoredWindows(string directory, string name, bool directoryEntry, ulong device, ulong inode)
    {
        if (!AnchoredDelete.EntryNameSafe(windows: true, name) || name is "." or "..")
            throw new IOException("anchored-path-unsafe");
        using var parent = OpenNoFollowChainWindows(directory);
        // OPEN_REPARSE_POINT, and FILE_DIRECTORY_FILE only for a real directory.
        // A leaf omits FILE_NON_DIRECTORY_FILE so the reparse entry itself is removed.
        uint options = 0x20 | 0x00200000 | (directoryEntry ? 0x1u : 0u);
        SafeFileHandle child;
        try { child = OpenChild(parent, name, options, 0x00110080); }
        catch (FileNotFoundException) when (!directoryEntry) { return; }
        using (child)
        {
            if (!GetFileInformationByHandle(child, out var info))
                throw AnchoredWin32("anchored-entry-stat-failed", Marshal.GetLastPInvokeError());
            if (inode != 0)
            {
                var index = ((ulong)info.IndexHigh << 32) | info.IndexLow;
                if (info.Volume != device || index != inode)
                    throw new IOException("anchored-identity-changed");
            }
            var reparse = (info.Attributes & 0x400u) != 0;
            var isDirectory = (info.Attributes & 0x10u) != 0 && !reparse;
            if (directoryEntry)
            {
                if (!isDirectory) throw new IOException("anchored-not-directory");
            }
            else if (isDirectory) throw new IOException("anchored-not-leaf");
            var disposition = new FileDisposition { DeleteFile = true };
            if (!SetFileInformationByHandle(child, 4, ref disposition, Marshal.SizeOf<FileDisposition>()))
            {
                var error = Marshal.GetLastPInvokeError();
                if (!directoryEntry && error is 2 or 3) return;
                throw AnchoredWin32("anchored-unlink-failed", error);
            }
        }
    }

    private void TruncateAnchoredWindows(string directory, string name, long length)
    {
        if (!AnchoredDelete.EntryNameSafe(windows: true, name) || name is "." or "..")
            throw new IOException("anchored-path-unsafe");
        using var parent = OpenNoFollowChainWindows(directory);
        // Share 0 matches the base FileShare.None truncate open.
        // GENERIC_READ|GENERIC_WRITE|SYNCHRONIZE. Base FileAccess.Write went through
        // CreateFile, which maps SYNCHRONIZE in. GENERIC_READ stays so this handle
        // can read size and link count. FILE_SYNCHRONOUS_IO_NONALERT needs SYNCHRONIZE.
        var child = OpenChild(parent, name, 0x40 | 0x00200000 | 0x20, 0xC0000000 | 0x00100000, share: 0);
        FileStream? stream = null;
        try
        {
            if (!GetFileInformationByHandle(child, out var info))
                throw new IOException("anchored-entry-stat-failed: win32=" + Marshal.GetLastPInvokeError());
            var size = ((long)info.SizeHigh << 32) | info.SizeLow;
            if ((info.Attributes & 0x400u) != 0 || (info.Attributes & 0x10u) != 0 || length > size)
                throw new IOException("anchored-not-file");
            if (info.Links != 1)
                throw new AnchoredHardLinkException();
            stream = new FileStream(child, FileAccess.ReadWrite);
            stream.SetLength(length);
            stream.Flush(true);
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
            throw AnchoredWin32("anchored-open-failed", error);
        }
        try
        {
            if (!DirectoryHandle(handle)) throw new IOException("anchored-open-failed");
            foreach (var component in directory[root.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (component is "." or ".." || !AnchoredDelete.EntryNameSafe(windows: true, component))
                    throw new IOException("anchored-path-unsafe");
                // GENERIC_READ | SYNCHRONIZE. Base opened this directory with CreateFile
                // GENERIC_READ, which maps SYNCHRONIZE in before NtCreateFile.
                // FILE_SYNCHRONOUS_IO_NONALERT requires that bit on the raw mask.
                var next = OpenChild(handle, component, 0x1 | 0x00200000 | 0x20, 0x80000000 | 0x00100000);
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

    private static SafeFileHandle OpenChild(SafeFileHandle parent, string name, uint options, uint access, uint share = 7)
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
                var status = NtCreateFile(out var handle, access, ref attributes, out _, IntPtr.Zero, 0, share, 1, options, IntPtr.Zero, 0);
                if (status < 0) throw AnchoredNtStatus(status);
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

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int fd);
}
