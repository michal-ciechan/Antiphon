using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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
    /// Renames the tree to its recorded sibling (<see cref="WorktreeSetAside.SetAsidePath"/>) so the
    /// registration of an absent directory can be dropped while the bytes can still be put back.
    /// Nothing is followed: a rename moves the entry itself, and it refuses a taken destination. A
    /// scanner holding an entry for a moment is waited out as the no-follow open waits; a lasting
    /// hold still refuses.
    /// </summary>
    public static void MoveAside(string path, string aside, WorktreeRenames? renames = null)
    {
        var r = renames ?? WorktreeRenames.System;
        r.Retrying(r.MoveDirectory, Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), aside);
    }

    /// <summary>Puts a set-aside tree back; false when its path has been taken or the move fails.</summary>
    public static bool Restore(string aside, string path)
    {
        try { Directory.Move(aside, Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Deletes <paramref name="path"/> and everything below it without following a link.</summary>
    public static void Delete(string path, bool preserveNestedRepositories = false)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var protectedRoot = preserveNestedRepositories ? full : null;
        if (preserveNestedRepositories)
        {
            var repositories = WorktreeNestedRepositories.Find(full);
            if (repositories.Length != 0) throw new NestedRepositoryPreservedException(repositories);
        }
        // A name created after the receipt was sealed is still here. Refuse before any unlink.
        RemovalApproval.Current.Value?.RejectDrift(full);
        if (OperatingSystem.IsWindows()) DeleteWindows(full, protectedRoot);
        else if (OperatingSystem.IsLinux()) DeleteLinux(full, protectedRoot);
        else DeletePortable(full, protectedRoot);
    }

    /// <summary>
    /// The set-aside tree's delete. On Windows a scanner or indexer holding an entry refuses the walk,
    /// or leaves a deleted name visible while its handle stays open; the whole walk is re-run, which
    /// re-checks every guard, at most <see cref="WorktreeRenames.SharingAttempts"/> times. A preserved
    /// nested repository and any other error are the refusal at once. After the last attempt the
    /// refusal stands: nothing is forced, and the caller keeps the record so a later pass resumes.
    /// </summary>
    public static void DeleteSettled(string path, bool preserveNestedRepositories, WorktreeRenames? renames = null)
    {
        var r = renames ?? WorktreeRenames.System;
        for (var attempt = 1; ; attempt++)
        {
            if (attempt > 1)
            {
                r.Pause(WorktreeRenames.SharingPause);
                if (IsAbsent(path)) return;
            }
            try { r.DeleteTree(path, preserveNestedRepositories); }
            catch (Exception ex) when (attempt < WorktreeRenames.SharingAttempts && r.IsTransientDelete(ex)) { continue; }
            if (!r.Windows || attempt == WorktreeRenames.SharingAttempts || IsAbsent(path)) return;
        }
    }

    private static bool IsAbsent(string path)
    {
        try { _ = File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }

    private static void DeleteWindows(string path, string? protectedRoot)
    {
        using var handle = OpenNoFollow(path);
        if (!GetFileInformationByHandle(handle, out var info)) throw Failure("inspect", path);
        const uint directory = 0x10, reparsePoint = 0x400;
        if ((info.FileAttributes & reparsePoint) == 0 && (info.FileAttributes & directory) != 0)
        {
            WorktreeNestedRepositories.ThrowIfRepository(path, protectedRoot);
            // The held handle pins this directory, so the listing below resolves through it.
            foreach (var child in Directory.EnumerateFileSystemEntries(path, "*", ListOnly).ToList())
            {
                WorktreeNestedRepositories.ThrowIfRepository(path, protectedRoot);
                DeleteWindows(child, protectedRoot);
            }
            WorktreeNestedRepositories.ThrowIfRepository(path, protectedRoot);
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
            if (error != 32 || attempt == WorktreeRenames.SharingAttempts) throw Failure("open", path, error);
            Thread.Sleep(WorktreeRenames.SharingPause);
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

    // CARD-1717 D-5: a path walk can lstat a directory and then enumerate the symlink that replaced it.
    // Linux holds a directory descriptor from a no-follow walk and unlinks names relative to that
    // descriptor. A swapped symlink is unlinked as the link; a different directory inode is refused.
    private static void DeleteLinux(string path, string? protectedRoot)
    {
        var parentPath = Path.GetDirectoryName(path) ?? throw Failure("open", path);
        using var parent = OpenLinuxDirectory(parentPath);
        DeleteLinuxName(parent, Path.GetFileName(path), "", protectedRoot);
    }

    /// <summary>Device and inode of the directory entry itself. A symlink is not followed.</summary>
    internal static string DirectoryIdentity(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (OperatingSystem.IsWindows()) return WindowsDirectoryIdentity(full);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("directory identity");
        var parentPath = Path.GetDirectoryName(full) ?? throw Failure("open", full);
        using var parent = OpenLinuxDirectory(parentPath);
        if (!TryStat(Fd(parent), Path.GetFileName(full), AtSymlinkNoFollow, out var stat)) throw Failure("stat", full);
        return LinuxIdentity(stat);
    }

    /// <summary>No-follow kind, length and UTC write ticks. Null when the entry is already absent.</summary>
    internal static EntryStamp? TryStamp(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            if (Statx(AtFdCwd, path, AtSymlinkNoFollow, StatxBasicStats, out var stat) != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error is ENoEnt or ENotDir) return null;
                throw Failure("stat", path, error);
            }
            return StampOf(stat);
        }
        if (!OperatingSystem.IsWindows())
        {
            var info = new FileInfo(path);
            if (!info.Exists && info.LinkTarget is null) return null;
            var kind = info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint) ? "link"
                : info.Attributes.HasFlag(FileAttributes.Directory) ? "dir" : "file";
            return new EntryStamp(kind, kind == "file" ? info.Length : 0, info.LastWriteTimeUtc.Ticks);
        }
        if (!GetFileAttributesExW(path, 0, out var data))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is 2 or 3) return null;
            throw Failure("stat", path, error);
        }
        var windowsKind = (data.FileAttributes & 0x400) != 0 ? "link" : (data.FileAttributes & 0x10) != 0 ? "dir" : "file";
        var windowsLength = windowsKind == "file" ? ((long)data.FileSizeHigh << 32) | data.FileSizeLow : 0;
        var write = ((long)data.LastWriteHigh << 32) | data.LastWriteLow;
        return new EntryStamp(windowsKind, windowsLength, DateTime.FromFileTimeUtc(write).Ticks);
    }

    /// <summary>
    /// SHA-256 of a regular file opened without following a link. Null when absent; empty when the
    /// entry exists and is not a regular file.
    /// </summary>
    internal static string? RegularFileHash(string path)
    {
        if (OperatingSystem.IsLinux()) return LinuxRegularFileHash(path);
        if (OperatingSystem.IsWindows()) return WindowsRegularFileHash(path);
        if (!File.Exists(path)) return null;
        return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    }

    /// <summary>
    /// Git blob id of a regular file opened without following a link. Null when absent; empty when
    /// the entry exists and is not a regular file.
    /// </summary>
    internal static string? GitBlobSha1(string path)
    {
        if (OperatingSystem.IsLinux()) return LinuxGitBlob(path);
        if (OperatingSystem.IsWindows()) return WindowsGitBlob(path);
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        return GitBlobOf(stream);
    }

    internal readonly record struct EntryStamp(string Kind, long Length, long MtimeTicks);

    private static void DeleteLinuxName(SafeFileHandle parent, string name, string relative, string? protectedRoot)
    {
        if (!TryStat(Fd(parent), name, AtSymlinkNoFollow, out var listed)) throw Failure("stat", name);
        var kind = listed.Mode & 0xF000;
        if (kind == SIfLnk)
        {
            Unlink(parent, name, directory: false);
            return;
        }
        if (kind != SIfDir)
        {
            UnlinkSameNode(parent, name, listed);
            return;
        }
        SafeFileHandle dir;
        try { dir = OpenAt(Fd(parent), name, ODirectory | ONoFollow | OCloExec, name); }
        catch (IOException)
        {
            if (TryStat(Fd(parent), name, AtSymlinkNoFollow, out var swapped) && (swapped.Mode & 0xF000) == SIfLnk)
            {
                Unlink(parent, name, directory: false);
                return;
            }
            throw new IOException("no_follow_identity_changed: " + name);
        }
        using (dir)
        {
            if (!TryStat(Fd(dir), "", AtEmptyPath, out var opened) || !SameNode(listed, opened))
                throw new IOException("no_follow_identity_changed: " + name);
            var children = ListDir(dir);
            if (protectedRoot is not null && relative.Length != 0 && children.Exists(IsRepositoryMarker))
                throw new NestedRepositoryPreservedException([relative + "/"]);
            var known = new HashSet<string>(children, StringComparer.Ordinal);
            foreach (var child in children)
            {
                // The marker check above ran once. A repository created after that listing must stop
                // the walk before the next name is unlinked.
                if (protectedRoot is not null && relative.Length != 0 && LinuxDirectoryHasMarker(dir))
                    throw new NestedRepositoryPreservedException([relative + "/"]);
                var childRelative = relative.Length == 0 ? child : relative + "/" + child;
                var approval = RemovalApproval.Current.Value;
                if (approval is not null)
                {
                    var present = ListDir(dir);
                    foreach (var addedName in present)
                    {
                        if (known.Contains(addedName)) continue;
                        var added = relative.Length == 0 ? addedName : relative + "/" + addedName;
                        if (added.Equals(".git", StringComparison.OrdinalIgnoreCase)
                            || added.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!approval.Names.Contains(added))
                            throw new ApprovedTreeRejectedException("set_aside_content_changed");
                    }
                    if (!present.Contains(child)) continue;
                    if (approval.Hashes.TryGetValue(childRelative, out var expected))
                    {
                        var hash = HashAt(dir, child);
                        if (hash is not null && (hash.Length == 0 || !hash.Equals(expected, StringComparison.OrdinalIgnoreCase)))
                            throw new ApprovedTreeRejectedException("set_aside_content_changed");
                    }
                }
                DeleteLinuxName(dir, child, childRelative, protectedRoot);
            }
            if (!TryStat(Fd(parent), name, AtSymlinkNoFollow, out var now)) throw Failure("stat", name);
            var nowKind = now.Mode & 0xF000;
            if (nowKind == SIfLnk)
            {
                Unlink(parent, name, directory: false);
                return;
            }
            if (nowKind != SIfDir || !SameNode(listed, now))
                throw new IOException("no_follow_identity_changed: " + name);
            Unlink(parent, name, directory: true);
        }
    }

    private static void UnlinkSameNode(SafeFileHandle parent, string name, LinuxStatx listed)
    {
        if (!TryStat(Fd(parent), name, AtSymlinkNoFollow, out var now)) throw Failure("stat", name);
        if ((now.Mode & 0xF000) == SIfLnk)
        {
            Unlink(parent, name, directory: false);
            return;
        }
        if (!SameNode(listed, now)) throw new IOException("no_follow_identity_changed: " + name);
        Unlink(parent, name, directory: false);
    }

    private static void Unlink(SafeFileHandle parent, string name, bool directory)
    {
        if (unlinkat(Fd(parent), name, directory ? AtRemovedIr : 0) != 0) throw Failure("unlink", name);
    }

    private static List<string> ListDir(SafeFileHandle dir)
    {
        var names = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries("/proc/self/fd/" + Fd(dir)))
        {
            var child = Path.GetFileName(entry);
            if (child is "." or "..") continue;
            names.Add(child);
        }
        return names;
    }

    private static bool IsRepositoryMarker(string name) =>
        name.Equals(".git", StringComparison.OrdinalIgnoreCase) || name.Equals("HEAD", StringComparison.OrdinalIgnoreCase);

    private static bool LinuxDirectoryHasMarker(SafeFileHandle dir) =>
        TryStat(Fd(dir), ".git", AtSymlinkNoFollow, out _) || TryStat(Fd(dir), "HEAD", AtSymlinkNoFollow, out _);

    private static string? HashAt(SafeFileHandle parent, string name)
    {
        var raw = openat(Fd(parent), name, ORdOnly | ONoFollow | OCloExec);
        if (raw < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is ENoEnt or ENotDir) return null;
            if (error == ELoop) return "";
            throw Failure("open", name, error);
        }
        using var handle = new SafeFileHandle((IntPtr)raw, ownsHandle: true);
        if (!TryStat(Fd(handle), "", AtEmptyPath, out var stat)) throw Failure("stat", name);
        if ((stat.Mode & 0xF000) != SIfReg) return "";
        using var stream = new FileStream(handle, FileAccess.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static SafeFileHandle OpenLinuxDirectory(string path)
    {
        var current = OpenAt(AtFdCwd, "/", ODirectory | ONoFollow | OCloExec, path);
        try
        {
            foreach (var component in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = OpenAt(Fd(current), component, ODirectory | ONoFollow | OCloExec, path);
                current.Dispose();
                current = next;
            }
            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenAt(int dirfd, string name, int flags, string diagnostic)
    {
        var raw = openat(dirfd, name, flags);
        if (raw < 0) throw Failure("open", diagnostic);
        return new SafeFileHandle((IntPtr)raw, ownsHandle: true);
    }

    private static bool TryStat(int dirfd, string name, int flags, out LinuxStatx stat) =>
        Statx(dirfd, name, flags, StatxBasicStats, out stat) == 0
        && (stat.Mask & (StatxType | StatxMode | StatxIno)) == (StatxType | StatxMode | StatxIno);

    private static bool SameNode(in LinuxStatx left, in LinuxStatx right) =>
        left.Inode == right.Inode && left.DevMajor == right.DevMajor && left.DevMinor == right.DevMinor;

    private static string LinuxIdentity(in LinuxStatx stat) => $"linux:{stat.DevMajor}:{stat.DevMinor}:{stat.Inode}";

    private static EntryStamp StampOf(in LinuxStatx stat)
    {
        var kind = (stat.Mode & 0xF000) switch { SIfLnk => "link", SIfDir => "dir", SIfReg => "file", _ => "other" };
        var ticks = DateTimeOffset.FromUnixTimeSeconds(stat.MtimeSec).Ticks + stat.MtimeNsec / 100;
        return new EntryStamp(kind, kind == "file" ? (long)stat.Size : 0, ticks);
    }

    private static string? LinuxRegularFileHash(string path)
    {
        var raw = openat(AtFdCwd, path, ORdOnly | ONoFollow | OCloExec);
        if (raw < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is ENoEnt or ENotDir) return null;
            if (error == ELoop) return "";
            throw Failure("open", path, error);
        }
        using var handle = new SafeFileHandle((IntPtr)raw, ownsHandle: true);
        if (!TryStat(Fd(handle), "", AtEmptyPath, out var stat)) throw Failure("stat", path);
        if ((stat.Mode & 0xF000) != SIfReg) return "";
        using var stream = new FileStream(handle, FileAccess.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string? LinuxGitBlob(string path)
    {
        var raw = openat(AtFdCwd, path, ORdOnly | ONoFollow | OCloExec);
        if (raw < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is ENoEnt or ENotDir) return null;
            if (error == ELoop) return "";
            throw Failure("open", path, error);
        }
        using var handle = new SafeFileHandle((IntPtr)raw, ownsHandle: true);
        if (!TryStat(Fd(handle), "", AtEmptyPath, out var stat)) throw Failure("stat", path);
        if ((stat.Mode & 0xF000) != SIfReg) return "";
        using var stream = new FileStream(handle, FileAccess.Read);
        return GitBlobOf(stream);
    }

    private static string WindowsDirectoryIdentity(string path)
    {
        using var handle = OpenNoFollow(path);
        if (!GetFileInformationByHandle(handle, out var info)) throw Failure("inspect", path);
        var prefix = (info.FileAttributes & 0x400) != 0 ? "link" : "win";
        return $"{prefix}:{info.VolumeSerialNumber:x8}:{info.FileIndexHigh:x8}:{info.FileIndexLow:x8}";
    }

    private static string? WindowsRegularFileHash(string path)
    {
        var handle = CreateFileW(path, 0x80000000, 0x1 | 0x2 | 0x4, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error is 2 or 3) return null;
            throw Failure("open", path, error);
        }
        using (handle)
        {
            if (!GetFileInformationByHandle(handle, out var info)) throw Failure("inspect", path);
            if ((info.FileAttributes & 0x400) != 0 || (info.FileAttributes & 0x10) != 0) return "";
            using var stream = new FileStream(handle, FileAccess.Read);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
    }

    private static string? WindowsGitBlob(string path)
    {
        var handle = CreateFileW(path, 0x80000000, 0x1 | 0x2 | 0x4, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error is 2 or 3) return null;
            throw Failure("open", path, error);
        }
        using (handle)
        {
            if (!GetFileInformationByHandle(handle, out var info)) throw Failure("inspect", path);
            if ((info.FileAttributes & 0x400) != 0 || (info.FileAttributes & 0x10) != 0) return "";
            using var stream = new FileStream(handle, FileAccess.Read);
            return GitBlobOf(stream);
        }
    }

    private static string GitBlobOf(Stream stream)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes("blob "
            + stream.Length.ToString(CultureInfo.InvariantCulture) + "\0"));
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) != 0) hash.AppendData(buffer.AsSpan(0, read));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static int Fd(SafeFileHandle handle) => handle.DangerousGetHandle().ToInt32();

    private const int AtFdCwd = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const int AtRemovedIr = 0x200;
    private const int AtEmptyPath = 0x1000;
    private const int ORdOnly = 0;
    private const int ODirectory = 0x10000;
    private const int ONoFollow = 0x20000;
    private const int OCloExec = 0x80000;
    private const uint StatxBasicStats = 0x7ff;
    private const uint StatxType = 0x1;
    private const uint StatxMode = 0x2;
    private const uint StatxIno = 0x100;
    private const int SIfDir = 0x4000;
    private const int SIfReg = 0x8000;
    private const int SIfLnk = 0xA000;
    private const int ENoEnt = 2;
    private const int ENotDir = 20;
    private const int ELoop = 40;

    private static void DeletePortable(string path, string? protectedRoot)
    {
        // Unix has no handle-pinned walk here; lstat each entry and unlink a link without descending.
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) { File.Delete(path); return; }
        if (!info.Attributes.HasFlag(FileAttributes.Directory)) { File.Delete(path); return; }
        WorktreeNestedRepositories.ThrowIfRepository(path, protectedRoot);
        foreach (var child in Directory.EnumerateFileSystemEntries(path, "*", ListOnly).ToList())
        {
            WorktreeNestedRepositories.ThrowIfRepository(path, protectedRoot);
            DeletePortable(child, protectedRoot);
        }
        WorktreeNestedRepositories.ThrowIfRepository(path, protectedRoot);
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileAttributesExW(string name, int infoLevel, out Win32FileAttributeData data);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int dirfd, string path, int flags, uint mask, out LinuxStatx stat);

    [DllImport("libc", SetLastError = true)]
    private static extern int openat(int dirfd, string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int unlinkat(int dirfd, string path, int flags);

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(112)] public long MtimeSec;
        [FieldOffset(120)] public uint MtimeNsec;
        [FieldOffset(136)] public uint DevMajor;
        [FieldOffset(140)] public uint DevMinor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32FileAttributeData
    {
        public uint FileAttributes;
        public uint CreationLow, CreationHigh, LastAccessLow, LastAccessHigh, LastWriteLow, LastWriteHigh;
        public uint FileSizeHigh, FileSizeLow;
    }
}

/// <summary>The set-aside manifest the no-follow delete re-checks. A rejection leaves the remainder in place.</summary>
internal static class RemovalApproval
{
    internal static readonly AsyncLocal<Listing?> Current = new();

    internal sealed class Listing
    {
        public required HashSet<string> Names { get; init; }
        public required Dictionary<string, string> Kinds { get; init; }
        public required Dictionary<string, string> Hashes { get; init; }

        public void RejectDrift(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var directory))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    var relative = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
                    if (relative.Equals(".git", StringComparison.OrdinalIgnoreCase)
                        || relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var stamp = WorktreeNoFollowDelete.TryStamp(entry)
                        ?? throw new ApprovedTreeRejectedException("set_aside_content_changed");
                    if (!Names.Contains(relative) || !Kinds.TryGetValue(relative, out var kind) || kind != stamp.Kind)
                        throw new ApprovedTreeRejectedException("set_aside_content_changed");
                    if (Hashes.TryGetValue(relative, out var expected))
                    {
                        var hash = WorktreeNoFollowDelete.RegularFileHash(entry);
                        if (hash is null || hash.Length == 0 || !hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
                            throw new ApprovedTreeRejectedException("set_aside_content_changed");
                    }
                    if (stamp.Kind == "dir") pending.Push(entry);
                }
            }
        }
    }
}

internal sealed class ApprovedTreeRejectedException(string reason) : IOException(reason)
{
    public string Reason { get; } = reason;
}
