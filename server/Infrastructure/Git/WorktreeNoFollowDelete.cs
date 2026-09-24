using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// CARD-0665 (review 5b79328d item 1): the one recursive delete guarded removal uses. Git for
/// Windows deletes through a directory junction, so a link that replaces a checked directory after
/// the last reading would carry deletion outside the tree. Here a reparse point (junction, symbolic
/// link, mount point) is removed as the link itself and never enumerated. On Windows each entry is
/// opened without following links, inspected and deleted through that same handle, and a directory
/// is held open without delete sharing while its children go, so it cannot be renamed or replaced
/// by a link underneath the walk.
/// </summary>
internal static class WorktreeNoFollowDelete
{
    /// <summary>
    /// Renames the tree to a fresh sibling so Git can drop the registration of an absent directory
    /// while the bytes can still be put back. Nothing is followed: a rename moves the entry itself.
    /// </summary>
    public static string MoveAside(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var parent = Path.GetDirectoryName(full) ?? throw new IOException("worktree_root_has_no_parent");
        var aside = Path.Combine(parent, $".{Path.GetFileName(full)}.removing-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.Move(full, aside);
        return aside;
    }

    /// <summary>Puts a set-aside tree back; false when its path has been taken or the move fails.</summary>
    public static bool Restore(string aside, string path)
    {
        try { Directory.Move(aside, Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Deletes <paramref name="path"/> and everything below it without following a link.</summary>
    public static void Delete(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (OperatingSystem.IsWindows()) DeleteWindows(full);
        else DeletePortable(full);
    }

    private static void DeleteWindows(string path)
    {
        using var handle = OpenNoFollow(path);
        if (!GetFileInformationByHandle(handle, out var info)) throw Failure("inspect", path);
        const uint directory = 0x10, reparsePoint = 0x400;
        if ((info.FileAttributes & reparsePoint) == 0 && (info.FileAttributes & directory) != 0)
        {
            // The held handle pins this directory, so the listing below resolves through it.
            foreach (var child in Directory.EnumerateFileSystemEntries(path, "*", ListOnly).ToList())
                DeleteWindows(child);
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

    private static void DeletePortable(string path)
    {
        // Unix has no handle-pinned walk here; lstat each entry and unlink a link without descending.
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) { File.Delete(path); return; }
        if (!info.Attributes.HasFlag(FileAttributes.Directory)) { File.Delete(path); return; }
        foreach (var child in Directory.EnumerateFileSystemEntries(path, "*", ListOnly).ToList())
            DeletePortable(child);
        Directory.Delete(path, recursive: false);
    }

    private static readonly EnumerationOptions ListOnly = new()
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
