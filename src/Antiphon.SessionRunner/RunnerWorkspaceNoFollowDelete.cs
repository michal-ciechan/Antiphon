using Antiphon.SessionRunner.Contracts;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.SessionRunner;

// S5 local deletion primitive. C1017_RunnerNoFollow exercises a listed child swap.
internal static class RunnerWorkspaceNoFollowDelete
{
    internal sealed record Entry(string Path, bool Directory, string? Hash);

    internal static string Identity(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var handle = OpenNoFollow(path);
            if (!GetFileInformationByHandle(handle, out var info)) throw Failure("identity", path);
            if ((info.FileAttributes & 0x400) != 0) throw new IOException("linked_identity");
            return $"{info.VolumeSerialNumber:x}:{info.FileIndexHigh:x}:{info.FileIndexLow:x}";
        }
        using var fd = OpenLinuxPath(path);
        return LinuxIdentity(fd.DangerousGetHandle().ToInt32());
    }

    internal static Entry[] Inventory(string root)
    {
        var entries = new List<Entry>();
        Walk(root, "", entries);
        return entries.OrderBy(e => e.Path, StringComparer.Ordinal).ToArray();
    }

    private static void Walk(string root, string relative, List<Entry> entries)
    {
        var path = Path.Combine(root, relative);
        if (!RunnerCompletedCardWorkspaceRemoval.UnlinkedPath(path)) throw new IOException("linked_inventory");
        foreach (var child in Directory.EnumerateFileSystemEntries(path))
        {
            var rel = Path.GetRelativePath(root, child);
            var attrs = File.GetAttributes(child);
            if (attrs.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("linked_inventory");
            var directory = attrs.HasFlag(FileAttributes.Directory);
            entries.Add(new(rel, directory, directory ? null : Hash(child)));
            if (directory) Walk(root, rel, entries);
        }
    }

    internal static void ValidateRemaining(string root, IReadOnlyList<Entry> expected)
    {
        var allowed = expected.ToDictionary(e => e.Path, StringComparer.Ordinal);
        foreach (var entry in Inventory(root))
            if (!allowed.TryGetValue(entry.Path, out var original) || original != entry)
                throw new IOException("changed_owned_content");
    }

    private static string Hash(string path) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    internal static void DeleteOwned(string root, string identity, IReadOnlyList<Entry> entries,
        Action<string>? beforeEntry, CancellationToken ct)
    {
        if (Identity(root) != identity) throw new IOException("changed_owned_directory");
        ValidateRemaining(root, entries);
        var allowed = entries.ToDictionary(e => e.Path, StringComparer.Ordinal);
        if (OperatingSystem.IsWindows()) DeleteWindows(root, null, root, allowed, beforeEntry, ct);
        else
        {
            var full = Path.GetFullPath(root);
            using var parent = OpenLinuxPath(Path.GetDirectoryName(full)!);
            DeleteLinux(parent.DangerousGetHandle().ToInt32(), Path.GetFileName(full), "", root,
                allowed, beforeEntry, ct, identity);
        }
    }

    // Linux descendants are opened relative to a pinned parent descriptor with O_NOFOLLOW.
    // Enumeration uses that same descriptor; unlinkat removes the entry under that parent.
    private static void DeleteLinux(int parent, string name, string relative, string root,
        Dictionary<string, Entry> allowed, Action<string>? beforeEntry, CancellationToken ct,
        string? expectedIdentity = null)
    {
        ct.ThrowIfCancellationRequested();
        if (relative.Length != 0 && !allowed.ContainsKey(relative)) throw new IOException("unknown_delete_entry");
        beforeEntry?.Invoke(Path.Combine(root, relative));
        var raw = openat(parent, name, 0x20000 | 0x800 | 0x80000); // NOFOLLOW | NONBLOCK | CLOEXEC
        if (raw < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 2) return;
            if (error == 40 && relative.Length != 0)
            {
                if (unlinkat(parent, name, 0) != 0) throw Failure("unlink", name);
                return;
            }
            throw Failure("open", name, error);
        }
        using var fd = new SafeFileHandle((IntPtr)raw, ownsHandle: true);
        if (expectedIdentity is not null && LinuxIdentity(raw) != expectedIdentity) throw new IOException("changed_owned_directory");
        var mode = LinuxMode(raw);
        if ((mode & 0xf000) == 0x4000)
        {
            var pinnedPath = "/proc/self/fd/" + raw;
            if (relative.Length != 0 && RepositoryDirectoryShape.HasRepositoryMarker(pinnedPath))
                throw new IOException("nested_repository");
            foreach (var child in Directory.EnumerateFileSystemEntries(pinnedPath).ToArray())
            {
                var childName = Path.GetFileName(child);
                DeleteLinux(raw, childName, Path.Combine(relative, childName), root, allowed, beforeEntry, ct);
            }
            if (unlinkat(parent, name, 0x200) != 0) throw Failure("rmdir", name);
        }
        else
        {
            if (relative.Length == 0 || allowed[relative].Directory) throw new IOException("changed_delete_entry");
            using var borrowed = new SafeFileHandle((IntPtr)raw, ownsHandle: false);
            using var stream = new FileStream(borrowed, FileAccess.Read);
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)) != allowed[relative].Hash)
                throw new IOException("changed_owned_content");
            if (unlinkat(parent, name, 0) != 0) throw Failure("unlink", name);
        }
    }

    private static SafeFileHandle OpenLinuxPath(string path)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("S5 requires Linux or Windows");
        var current = new SafeFileHandle((IntPtr)openat(-100, "/", 0x10000 | 0x20000 | 0x80000), true);
        if (current.IsInvalid) throw Failure("open", path);
        try
        {
            foreach (var component in Path.GetFullPath(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = openat(current.DangerousGetHandle().ToInt32(), component, 0x10000 | 0x20000 | 0x80000);
                if (next < 0) throw Failure("open", path);
                current.Dispose(); current = new SafeFileHandle((IntPtr)next, true);
            }
            return current;
        }
        catch { current.Dispose(); throw; }
    }

    private static string LinuxIdentity(int fd)
    {
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            if (fstat(fd, buffer) != 0) throw Failure("stat", "owned descriptor");
            return $"{Marshal.ReadInt64(buffer, 0):x}:{Marshal.ReadInt64(buffer, 8):x}";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static int LinuxMode(int fd)
    {
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            if (fstat(fd, buffer) != 0) throw Failure("stat", "owned descriptor");
            return Marshal.ReadInt32(buffer, 24);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [DllImport("libc", SetLastError = true)] private static extern int openat(int fd, string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int unlinkat(int fd, string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int fstat(int fd, IntPtr stat);

    private static void DeleteWindows(string path, string? protectedRoot, string root, Dictionary<string, Entry> allowed, Action<string>? beforeEntry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var relative = Path.GetRelativePath(root, path);
        if (relative != "." && !allowed.ContainsKey(relative)) throw new IOException("unknown_delete_entry");
        beforeEntry?.Invoke(path);
        using var handle = OpenNoFollow(path);
        if (!GetFileInformationByHandle(handle, out var info)) throw Failure("inspect", path);
        const uint directory = 0x10, reparsePoint = 0x400;
        if ((info.FileAttributes & reparsePoint) == 0 && (info.FileAttributes & directory) != 0)
        {
            if (path != root && RepositoryDirectoryShape.HasRepositoryMarker(path)) throw new IOException("nested_repository");
            // The held handle pins this directory, so the listing below resolves through it.
            foreach (var child in Directory.EnumerateFileSystemEntries(path, "*", ListOnly).ToList())
            {
                if (path != root && RepositoryDirectoryShape.HasRepositoryMarker(path)) throw new IOException("nested_repository");
                DeleteWindows(child, protectedRoot, root, allowed, beforeEntry, ct);
            }
            if (path != root && RepositoryDirectoryShape.HasRepositoryMarker(path)) throw new IOException("nested_repository");
        }
        MarkDeleted(handle, path);
    }

    private static SafeFileHandle OpenNoFollow(string path)
    {
        // DELETE | FILE_READ_ATTRIBUTES; share read and write but not delete, so while this handle is
        // open nobody can rename, delete or replace the entry. FILE_FLAG_OPEN_REPARSE_POINT opens a
        // link itself; FILE_FLAG_BACKUP_SEMANTICS allows a directory handle. A scanner or indexer may
        // hold an entry for a moment; a lasting sharing violation is residue.
        for (var attempt = 1; ; attempt++)
        {
            var handle = CreateFileW(path, 0x00010000 | 0x80, 0x1 | 0x2, IntPtr.Zero, 3, 0x00200000 | 0x02000000, IntPtr.Zero);
            if (!handle.IsInvalid) return handle;
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error != 32 || attempt == 5) throw Failure("open", path, error);
            Thread.Sleep(100);
        }
    }

    private static void MarkDeleted(SafeFileHandle handle, string path)
    {
        // FILE_DISPOSITION_DELETE | POSIX_SEMANTICS | IGNORE_READONLY_ATTRIBUTE: the name goes at once
        // so the parent can follow, and a read-only file goes as Git would remove it.
        var ex = new FileDispositionInfoEx { Flags = 0x1 | 0x2 | 0x10 };
        if (SetFileInformationByHandle(handle, 21, ref ex, (uint)Marshal.SizeOf<FileDispositionInfoEx>())) return;
        var error = Marshal.GetLastPInvokeError();
        // Volumes without the extended disposition (FAT, some network shares) take the classic one.
        if (error is not (87 or 1 or 50)) throw Failure("delete", path, error);
        var classic = new FileDispositionInfo { DeleteFile = 1 };
        if (!SetFileInformationByHandle(handle, 4, ref classic, (uint)Marshal.SizeOf<FileDispositionInfo>()))
            throw Failure("delete", path);
    }

    private static EnumerationOptions ListOnly => new()
    {
        RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false, ReturnSpecialDirectories = false,
    };

    private static IOException Failure(string step, string path, int? error = null)
    {
        var code = error ?? Marshal.GetLastPInvokeError();
        return new IOException($"no_follow_{step}_failed: {path}: {new Win32Exception(code).Message}", code);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfoEx { public uint Flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo { public byte DeleteFile; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint FileAttributes, CreationTimeLow, CreationTimeHigh, LastAccessTimeLow, LastAccessTimeHigh,
            LastWriteTimeLow, LastWriteTimeHigh, VolumeSerialNumber, FileSizeHigh, FileSizeLow,
            NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr securityAttributes, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass,
        ref FileDispositionInfoEx info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass,
        ref FileDispositionInfo info, uint size);
}
