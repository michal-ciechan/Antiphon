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
        if (OperatingSystem.IsWindows()) DeleteWindows(full, "", protectedRoot);
        else if (OperatingSystem.IsLinux()) DeleteLinux(full, protectedRoot);
        else DeletePortable(full, "", protectedRoot);
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

    private static void DeleteWindows(string path, string relative, string? protectedRoot)
    {
        using var handle = OpenNoFollow(path);
        if (!GetFileInformationByHandle(handle, out var info)) throw Failure("inspect", path);
        const uint directory = 0x10, reparsePoint = 0x400;
        var isLink = (info.FileAttributes & reparsePoint) != 0;
        var isDirectory = !isLink && (info.FileAttributes & directory) != 0;
        // CARD-1717 B-3: the held handle shares no delete, so this name is not replaced between its hash and delete.
        // A-B-3: the hash shares no write, so a handle already open for writing refuses it (sharing violation) and
        // none can write while it reads.
        var approval = RemovalApproval.Current.Value;
        if (approval is not null && relative.Length != 0)
            approval.RejectChanged(relative, isLink ? "link" : isDirectory ? "dir" : "file",
                () => WindowsRegularFileHash(path, denyWrite: true));
        if (isDirectory)
        {
            WorktreeNestedRepositories.ThrowIfRepository(path, protectedRoot);
            // The held handle pins this directory, so the listing below resolves through it.
            var children = Directory.EnumerateFileSystemEntries(path, "*", ListOnly).ToList();
            approval?.RejectUnapproved(relative, children.Select(c => Path.GetFileName(c)));
            foreach (var child in children)
            {
                WorktreeNestedRepositories.ThrowIfRepository(path, protectedRoot);
                var name = Path.GetFileName(child);
                DeleteWindows(child, relative.Length == 0 ? name : relative + "/" + name, protectedRoot);
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
        DeleteLinuxName(parent, Path.GetFileName(path), "", protectedRoot, []);
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

    /// <summary>
    /// Receipt SHA-256 and git blob id from one no-follow read. Null when absent; both strings empty
    /// when the entry exists and is not a regular file. The two digests are of those same bytes.
    /// </summary>
    internal static ReceiptDigest? ReadReceipt(string path)
    {
        if (OperatingSystem.IsLinux()) return LinuxReadReceipt(path);
        if (OperatingSystem.IsWindows()) return WindowsReadReceipt(path);
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        return DigestOf(stream);
    }

    internal readonly record struct ReceiptDigest(string Sha256, string GitBlob);

    internal readonly record struct EntryStamp(string Kind, long Length, long MtimeTicks);

    // CARD-1717 B-3: under a receipt each directory's listing is checked once against it, and each entry
    // again (kind, then the bytes of a content-bound file) just before its unlink. Only listed names are
    // unlinked; a name created after the listing is found when the emptied directory is listed again,
    // and the walk stops there with the remainder still in the set-aside.
    private static void DeleteLinuxName(SafeFileHandle parent, string name, string relative, string? protectedRoot,
        List<(SafeFileHandle Directory, string Relative)> open)
    {
        if (!TryStat(Fd(parent), name, AtSymlinkNoFollow, out var listed)) throw Failure("stat", name);
        var kind = listed.Mode & 0xF000;
        var approval = RemovalApproval.Current.Value;
        LinuxStatx? hashed = null;
        if (approval is not null && relative.Length != 0)
            approval.RejectChanged(relative, KindOf(listed), () => HashAt(parent, name, listed, out hashed));
        if (kind == SIfLnk)
        {
            Unlink(parent, name, directory: false);
            return;
        }
        if (kind != SIfDir)
        {
            UnlinkSameNode(parent, name, listed, hashed);
            return;
        }
        SafeFileHandle dir;
        try { dir = OpenAt(Fd(parent), name, ODirectory | ONoFollow | OCloExec, name); }
        catch (IOException)
        {
            if (TryStat(Fd(parent), name, AtSymlinkNoFollow, out var swapped)
                && (swapped.Mode & 0xF000) == SIfLnk)
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
            var nested = protectedRoot is not null && relative.Length != 0;
            var children = ListDir(dir);
            if (nested && children.Exists(IsRepositoryMarker))
                throw new NestedRepositoryPreservedException([relative + "/"]);
            approval?.RejectUnapproved(relative, children);
            if (nested) open.Add((dir, relative));
            try
            {
                foreach (var child in children)
                {
                    // The marker check above ran once. A repository created after that listing, in this
                    // directory or in any directory above it, must stop the walk before the next name is unlinked.
                    ThrowIfMarked(open);
                    DeleteLinuxName(dir, child, relative.Length == 0 ? child : relative + "/" + child, protectedRoot, open);
                }
                ThrowIfMarked(open);
            }
            finally { if (nested) open.RemoveAt(open.Count - 1); }
            if (approval is not null && ListDir(dir).Count != 0)
                throw new ApprovedTreeRejectedException("set_aside_content_changed");
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

    /// <summary>
    /// CARD-1717 A-B-3: <paramref name="hashed"/> is the stat the approved hash ended on. A write after it, through a
    /// handle opened at any time, moves the size or the change time, so the bytes are kept and the walk stops. What
    /// remains is a write landing between this statx and the unlinkat, or one in the same coarse clock tick as the
    /// file's previous change that also keeps its size and modification time; nothing short of a lease closes those.
    /// </summary>
    private static void UnlinkSameNode(SafeFileHandle parent, string name, LinuxStatx listed, LinuxStatx? hashed)
    {
        if (!TryStat(Fd(parent), name, AtSymlinkNoFollow, out var now)) throw Failure("stat", name);
        if ((now.Mode & 0xF000) == SIfLnk)
        {
            Unlink(parent, name, directory: false);
            return;
        }
        if (!SameNode(listed, now)) throw new IOException("no_follow_identity_changed: " + name);
        if (hashed is { } proof && !SameContent(proof, now))
            throw new ApprovedTreeRejectedException("set_aside_content_changed");
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

    private static void ThrowIfMarked(List<(SafeFileHandle Directory, string Relative)> open)
    {
        foreach (var (directory, relative) in open)
            if (LinuxDirectoryHasMarker(directory)) throw new NestedRepositoryPreservedException([relative + "/"]);
    }

    private static bool LinuxDirectoryHasMarker(SafeFileHandle dir) =>
        TryStat(Fd(dir), ".git", AtSymlinkNoFollow, out _) || TryStat(Fd(dir), "HEAD", AtSymlinkNoFollow, out _);

    /// <summary>SHA-256 of the regular file <paramref name="name"/> names, opened without following a link. Null
    /// when absent; empty when it is not a regular file or not the inode that was listed.</summary>
    /// <remarks>CARD-1717 A-B-3: a writer that already held the file open can change bytes the hash has already
    /// read. The descriptor is stat'ed before and after the read; a size, modification or change time that moved
    /// means the bytes hashed are not the bytes on disk, and the entry reads as changed. <paramref name="after"/> is
    /// the stat the hash ended on, for the re-check at the unlink.</remarks>
    private static string? HashAt(SafeFileHandle parent, string name, in LinuxStatx listed, out LinuxStatx? after)
    {
        after = null;
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
        if ((stat.Mode & 0xF000) != SIfReg || !SameNode(listed, stat) || !SameContent(listed, stat)) return "";
        // The stream owns the descriptor; it stays open for the second stat.
        using var stream = new FileStream(handle, FileAccess.Read);
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (!TryStat(Fd(handle), "", AtEmptyPath, out var ended) || !SameContent(stat, ended)) return "";
        after = ended;
        return hash;
    }

    private static string KindOf(in LinuxStatx stat) =>
        (stat.Mode & 0xF000) switch { SIfLnk => "link", SIfDir => "dir", SIfReg => "file", _ => "other" };

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
        Statx(dirfd, name, flags, StatxBasicStats | StatxBtime, out stat) == 0
        && (stat.Mask & (StatxType | StatxMode | StatxIno)) == (StatxType | StatxMode | StatxIno);

    private static bool SameNode(in LinuxStatx left, in LinuxStatx right) =>
        left.Inode == right.Inode && left.DevMajor == right.DevMajor && left.DevMinor == right.DevMinor;

    // CARD-1717 A-B-3: a write moves the change time (which no caller can set back) and the modification time.
    private static bool SameContent(in LinuxStatx left, in LinuxStatx right) => SameNode(left, right)
        && left.Size == right.Size && left.MtimeSec == right.MtimeSec && left.MtimeNsec == right.MtimeNsec
        && left.CtimeSec == right.CtimeSec && left.CtimeNsec == right.CtimeNsec;

    // CARD-1717 B-2: an inode number is reused once its directory is gone. The birth time, where the file
    // system reports one, tells a directory created later at a reused inode from the recorded one.
    private static string LinuxIdentity(in LinuxStatx stat) => (stat.Mask & StatxBtime) == 0
        ? $"linux:{stat.DevMajor}:{stat.DevMinor}:{stat.Inode}"
        : $"linux:{stat.DevMajor}:{stat.DevMinor}:{stat.Inode}:{stat.BtimeSec}.{stat.BtimeNsec:D9}";

    private static EntryStamp StampOf(in LinuxStatx stat)
    {
        var kind = KindOf(stat);
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

    private static ReceiptDigest? LinuxReadReceipt(string path)
    {
        var raw = openat(AtFdCwd, path, ORdOnly | ONoFollow | OCloExec);
        if (raw < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is ENoEnt or ENotDir) return null;
            if (error == ELoop) return new("", "");
            throw Failure("open", path, error);
        }
        using var handle = new SafeFileHandle((IntPtr)raw, ownsHandle: true);
        if (!TryStat(Fd(handle), "", AtEmptyPath, out var stat)) throw Failure("stat", path);
        if ((stat.Mode & 0xF000) != SIfReg) return new("", "");
        using var stream = new FileStream(handle, FileAccess.Read);
        return DigestOf(stream);
    }

    private static string WindowsDirectoryIdentity(string path)
    {
        using var handle = OpenNoFollow(path);
        if (!GetFileInformationByHandle(handle, out var info)) throw Failure("inspect", path);
        var prefix = (info.FileAttributes & 0x400) != 0 ? "link" : "win";
        return $"{prefix}:{info.VolumeSerialNumber:x8}:{info.FileIndexHigh:x8}:{info.FileIndexLow:x8}";
    }

    private static string? WindowsRegularFileHash(string path, bool denyWrite = false)
    {
        var handle = CreateFileW(path, 0x80000000, denyWrite ? 0x1u | 0x4 : 0x1 | 0x2 | 0x4, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
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

    private static ReceiptDigest? WindowsReadReceipt(string path)
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
            if ((info.FileAttributes & 0x400) != 0 || (info.FileAttributes & 0x10) != 0) return new("", "");
            using var stream = new FileStream(handle, FileAccess.Read);
            return DigestOf(stream);
        }
    }

    internal static string GitBlobOf(Stream stream)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes("blob "
            + stream.Length.ToString(CultureInfo.InvariantCulture) + "\0"));
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) != 0) hash.AppendData(buffer.AsSpan(0, read));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static ReceiptDigest DigestOf(Stream stream)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var git = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        git.AppendData(Encoding.ASCII.GetBytes("blob "
            + stream.Length.ToString(CultureInfo.InvariantCulture) + "\0"));
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) != 0)
        {
            var slice = buffer.AsSpan(0, read);
            sha.AppendData(slice);
            git.AppendData(slice);
        }
        return new(Convert.ToHexStringLower(sha.GetHashAndReset()), Convert.ToHexStringLower(git.GetHashAndReset()));
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
    private const uint StatxBtime = 0x800;
    private const uint StatxType = 0x1;
    private const uint StatxMode = 0x2;
    private const uint StatxIno = 0x100;
    private const int SIfDir = 0x4000;
    private const int SIfReg = 0x8000;
    private const int SIfLnk = 0xA000;
    private const int ENoEnt = 2;
    private const int ENotDir = 20;
    private const int ELoop = 40;

    private static void DeletePortable(string path, string relative, string? protectedRoot)
    {
        // Unix has no handle-pinned walk here; lstat each entry and unlink a link without descending.
        var info = new FileInfo(path);
        var isLink = info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        var isDirectory = !isLink && info.Attributes.HasFlag(FileAttributes.Directory);
        var approval = RemovalApproval.Current.Value;
        if (approval is not null && relative.Length != 0)
            approval.RejectChanged(relative, isLink ? "link" : isDirectory ? "dir" : "file", () => RegularFileHash(path));
        if (!isDirectory) { File.Delete(path); return; }
        WorktreeNestedRepositories.ThrowIfRepository(path, protectedRoot);
        var children = Directory.EnumerateFileSystemEntries(path, "*", ListOnly).ToList();
        approval?.RejectUnapproved(relative, children.Select(c => Path.GetFileName(c)));
        foreach (var child in children)
        {
            WorktreeNestedRepositories.ThrowIfRepository(path, protectedRoot);
            var name = Path.GetFileName(child);
            DeletePortable(child, relative.Length == 0 ? name : relative + "/" + name, protectedRoot);
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
        [FieldOffset(80)] public long BtimeSec;
        [FieldOffset(88)] public uint BtimeNsec;
        [FieldOffset(96)] public long CtimeSec;
        [FieldOffset(104)] public uint CtimeNsec;
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
                    if (!Names.Contains(relative) || !Kinds.TryGetValue(relative, out var kind))
                        throw new ApprovedTreeRejectedException("set_aside_content_changed");
                    if (stamp.Kind == "link") continue;
                    if (kind != stamp.Kind) throw new ApprovedTreeRejectedException("set_aside_content_changed");
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

        /// <summary>A listed name the receipt never approved refuses before anything in its directory goes.</summary>
        public void RejectUnapproved(string relativeDirectory, IEnumerable<string> children)
        {
            foreach (var child in children)
            {
                var relative = relativeDirectory.Length == 0 ? child : relativeDirectory + "/" + child;
                if (!Administrative(relative) && !Names.Contains(relative))
                    throw new ApprovedTreeRejectedException("set_aside_content_changed");
            }
        }

        /// <summary>
        /// The entry about to be unlinked still has its approved kind and, for a content-bound file, the
        /// approved bytes. <paramref name="hash"/> reads the entry the walk will unlink.
        /// </summary>
        public void RejectChanged(string relative, string kind, Func<string?> hash)
        {
            if (Administrative(relative)) return;
            if (!Kinds.TryGetValue(relative, out var approved)) throw new ApprovedTreeRejectedException("set_aside_content_changed");
            // CARD-1017: an approved name that is now a link holds no worktree bytes; it is unlinked, never followed.
            if (kind == "link") return;
            if (approved != kind) throw new ApprovedTreeRejectedException("set_aside_content_changed");
            if (!Hashes.TryGetValue(relative, out var expected)) return;
            var actual = hash();
            if (actual is null || actual.Length == 0 || !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new ApprovedTreeRejectedException("set_aside_content_changed");
        }

        // The worktree's own .git entry is administrative and never in the receipt.
        private static bool Administrative(string relative) => relative.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class ApprovedTreeRejectedException(string reason) : IOException(reason)
{
    public string Reason { get; } = reason;
}
