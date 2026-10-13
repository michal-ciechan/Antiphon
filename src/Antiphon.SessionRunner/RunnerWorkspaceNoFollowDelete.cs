using Antiphon.SessionRunner.Contracts;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.SessionRunner;

// Inventory and disposition use the same native type/identity facts. No special
// file is read, and each regular file is hashed through its held no-follow handle.
internal static class RunnerWorkspaceNoFollowDelete
{
    internal sealed record Entry(string Path, bool Directory, string? Hash, string? Identity = null, long Size = -1);
    internal sealed class Refusal(string reason, string path) : IOException(reason + ": " + path)
    {
        internal string Reason { get; } = reason;
    }
    private sealed record Metadata(string Identity, bool Directory, bool Regular, long Size, string WriteStamp);
    private sealed class InventoryBudget
    {
        internal int Entries;
        internal long Bytes;
        internal void Add(Metadata info, string path)
        {
            if (++Entries > 100_000 || (Bytes += info.Size) > 1_073_741_824)
                throw new Refusal("unproven_content", path + " (inventory limit)");
        }
    }

    internal static string Identity(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var ancestors = PinWindowsParents(path);
            using var handle = OpenNoFollow(path);
            return WindowsMetadata(handle, path).Identity;
        }
        using var fd = OpenLinuxPath(path);
        return LinuxMetadata(fd.DangerousGetHandle().ToInt32()).Identity;
    }

    // C1032_TransportRequiredPayload exercises unreadable registration and a Linux link.
    // The read is bounded to 128 bytes and shares S5's pinned path/type metadata.
    internal static Guid ReadStoreGuid(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new IOException("registration path");
        if (OperatingSystem.IsWindows())
        {
            using var parents = PinWindowsParents(path);
            using var handle = OpenNoFollow(path);
            var metadata = WindowsMetadata(handle, path);
            return ReadGuid(handle, metadata, () => WindowsMetadata(handle, path));
        }
        using var parent = OpenLinuxPath(Path.GetDirectoryName(path)!);
        var fd = parent.DangerousGetHandle().ToInt32(); var name = Path.GetFileName(path);
        var listed = LinuxMetadata(fd, name);
        if (!listed.Regular || listed.Size > 128) throw new IOException("registration type or size");
        using var file = OpenLinuxEntry(fd, name);
        var opened = LinuxMetadata(file.DangerousGetHandle().ToInt32());
        if (opened != listed) throw new IOException("registration changed");
        return ReadGuid(file, opened, () => LinuxMetadata(file.DangerousGetHandle().ToInt32()));
    }

    private static Guid ReadGuid(SafeFileHandle file, Metadata before, Func<Metadata> after)
    {
        if (!before.Regular || before.Size is < 1 or > 128) throw new IOException("registration type or size");
        // FileStream owns the supplied handle; callers also dispose it on failure before this point.
        using var stream = new FileStream(file, FileAccess.Read);
        var bytes = new byte[129]; var count = 0;
        while (count < bytes.Length)
        {
            var read = stream.Read(bytes, count, bytes.Length - count);
            if (read == 0) break;
            count += read;
        }
        if (count > 128 || count != before.Size || after() != before
            || !Guid.TryParse(System.Text.Encoding.ASCII.GetString(bytes, 0, count).Trim(), out var store) || store == Guid.Empty)
            throw new IOException("registration invalid or changed");
        return store;
    }

    internal static async Task<Entry[]> InventoryAsync(string root, string? expectedIdentity, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var entries = new List<Entry>(); var budget = new InventoryBudget();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var ancestors = PinWindowsParents(root);
                using var handle = OpenNoFollow(root);
                var info = WindowsMetadata(handle, root);
                if (!info.Directory || expectedIdentity is not null && info.Identity != expectedIdentity) Changed(root);
                await WalkWindowsAsync(root, "", entries, budget, deadline.Token);
            }
            else
            {
                using var handle = OpenLinuxPath(root);
                var info = LinuxMetadata(handle.DangerousGetHandle().ToInt32());
                if (expectedIdentity is not null && info.Identity != expectedIdentity) Changed(root);
                await WalkLinuxAsync(handle.DangerousGetHandle().ToInt32(), "", entries, budget, deadline.Token);
            }
            return entries.OrderBy(e => e.Path, StringComparer.Ordinal).ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new Refusal("unproven_content", root + " (inventory deadline)"); }
    }

    private static async Task WalkLinuxAsync(int parent, string relative, List<Entry> entries,
        InventoryBudget budget, CancellationToken ct)
    {
        if (relative.Count(c => c == Path.DirectorySeparatorChar) >= 128) throw new Refusal("unproven_content", relative);
        foreach (var child in ListChildren("/proc/self/fd/" + parent, ct))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(child); var rel = Path.Combine(relative, name);
            // AT_SYMLINK_NOFOLLOW is lstat-equivalent and never opens a FIFO,
            // socket, device or symlink. Record its type as unproven and refuse.
            var listed = LinuxMetadata(parent, name);
            RequireSupported(listed, rel); budget.Add(listed, rel);
            using var handle = OpenLinuxEntry(parent, name);
            var opened = LinuxMetadata(handle.DangerousGetHandle().ToInt32());
            if (listed != opened) Changed(rel);
            var hash = opened.Directory ? null : await HashAsync(handle, opened, rel, ct);
            entries.Add(new(rel, opened.Directory, hash, opened.Identity, opened.Size));
            if (opened.Directory) await WalkLinuxAsync(handle.DangerousGetHandle().ToInt32(), rel, entries, budget, ct);
        }
    }

    private static async Task WalkWindowsAsync(string root, string relative, List<Entry> entries,
        InventoryBudget budget, CancellationToken ct)
    {
        if (relative.Count(c => c == Path.DirectorySeparatorChar) >= 128) throw new Refusal("unproven_content", relative);
        foreach (var child in ListChildren(Path.Combine(root, relative), ct))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(root, child);
            RequireWindowsAttributes(child); // metadata only, before any data handle
            using var handle = OpenNoFollow(child);
            var info = WindowsMetadata(handle, child); RequireSupported(info, rel); budget.Add(info, rel);
            var hash = info.Directory ? null : await HashAsync(handle, info, rel, ct);
            entries.Add(new(rel, info.Directory, hash, info.Identity, info.Size));
            if (info.Directory) await WalkWindowsAsync(root, rel, entries, budget, ct);
        }
    }

    internal static async Task ValidateRemainingAsync(string root, IReadOnlyList<Entry> expected, CancellationToken ct)
    {
        var allowed = expected.ToDictionary(e => e.Path, StringComparer.Ordinal);
        foreach (var entry in await InventoryAsync(root, null, ct))
            if (!allowed.TryGetValue(entry.Path, out var original) || original != entry) Changed(entry.Path);
    }

    private static void RequireSupported(Metadata info, string path)
    {
        if (!info.Directory && !info.Regular) throw new Refusal("unproven_content", path + " (non-regular)");
    }
    private static void Changed(string path) => throw new Refusal("changed_since_inventory", path);
    private static async Task<string> HashAsync(SafeFileHandle handle, Metadata before, string path, CancellationToken ct)
    {
        RequireSupported(before, path);
        if (!before.Regular || before.Size > 268_435_456) throw new Refusal("unproven_content", path + " (file limit)");
        using var borrowed = new SafeFileHandle(handle.DangerousGetHandle(), ownsHandle: false);
        using var stream = new FileStream(borrowed, FileAccess.Read);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024]; long read = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var count = await stream.ReadAsync(buffer, ct);
            if (count == 0) break;
            read += count;
            if (read > before.Size) Changed(path);
            hash.AppendData(buffer, 0, count);
        }
        var after = OperatingSystem.IsWindows() ? WindowsMetadata(handle, path) : LinuxMetadata(handle.DangerousGetHandle().ToInt32());
        if (before != after || read != before.Size) Changed(path);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static async Task DeleteOwnedAsync(string root, string identity, IReadOnlyList<Entry> entries,
        Action<string>? beforeEntry, Action<string>? afterValidation, CancellationToken ct)
    {
        if (Identity(root) != identity) Changed(root);
        await ValidateRemainingAsync(root, entries, ct);
        var allowed = entries.ToDictionary(e => e.Path, StringComparer.Ordinal);
        if (OperatingSystem.IsWindows())
        {
            using var ancestors = PinWindowsParents(root);
            await DeleteWindowsAsync(root, root, allowed, beforeEntry, identity, afterValidation, ct);
        }
        else
        {
            var full = Path.GetFullPath(root);
            using var parent = OpenLinuxPath(Path.GetDirectoryName(full)!);
            await DeleteLinuxAsync(parent.DangerousGetHandle().ToInt32(), Path.GetFileName(full), "", root,
                allowed, beforeEntry, identity, ct);
        }
    }

    private static async Task DeleteLinuxAsync(int parent, string name, string relative, string root,
        Dictionary<string, Entry> allowed, Action<string>? beforeEntry, string? rootIdentity, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (relative.Length != 0 && !allowed.ContainsKey(relative)) Changed(relative);
        beforeEntry?.Invoke(Path.Combine(root, relative));
        var listed = LinuxMetadata(parent, name);
        // Preserve the existing late-link contract: unlink only the link itself.
        // No other non-regular replacement may be read or deleted.
        if (!listed.Directory && !listed.Regular)
        {
            if (relative.Length != 0 && LinuxIsLink(parent, name))
            {
                if (unlinkat(parent, name, 0) != 0) throw Failure("unlink", name);
                return;
            }
            throw new Refusal("unproven_content", relative + " (non-regular)");
        }
        using var handle = OpenLinuxEntry(parent, name);
        var info = LinuxMetadata(handle.DangerousGetHandle().ToInt32());
        if (listed != info) Changed(relative);
        if (relative.Length == 0)
        {
            if (!info.Directory || info.Identity != rootIdentity) Changed(root);
        }
        else
        {
            var entry = allowed[relative];
            if (entry.Identity != info.Identity || entry.Directory != info.Directory || entry.Size != info.Size) Changed(relative);
            if (info.Regular && await HashAsync(handle, info, relative, ct) != entry.Hash) Changed(relative);
        }
        if (info.Directory)
            foreach (var child in ListChildren("/proc/self/fd/" + handle.DangerousGetHandle().ToInt32(), ct))
            {
                var childName = Path.GetFileName(child);
                await DeleteLinuxAsync(handle.DangerousGetHandle().ToInt32(), childName, Path.Combine(relative, childName),
                    root, allowed, beforeEntry, null, ct);
            }
        ct.ThrowIfCancellationRequested();
        // Linux cannot exclude arbitrary writers with Windows sharing semantics.
        // Recheck the held object and the name immediately before descriptor-relative unlink.
        var current = LinuxMetadata(handle.DangerousGetHandle().ToInt32());
        if ((!info.Directory && current != info) || LinuxMetadata(parent, name).Identity != info.Identity) Changed(relative);
        if (unlinkat(parent, name, info.Directory ? 0x200 : 0) != 0) throw Failure("unlink", name);
    }

    private static SafeFileHandle OpenLinuxEntry(int parent, string name)
    {
        var raw = openat(parent, name, 0x20000 | 0x800 | 0x80000); // NOFOLLOW | NONBLOCK | CLOEXEC
        if (raw < 0) throw Failure("open", name);
        return new SafeFileHandle((IntPtr)raw, ownsHandle: true);
    }

    private static SafeFileHandle OpenLinuxPath(string path)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("S5 descriptor layout is qualified for Linux x64; Windows uses handles");
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

    private static Metadata LinuxMetadata(int fd, string? name = null)
    {
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            if ((name is null ? fstat(fd, buffer) : fstatat(fd, name, buffer, 0x100)) != 0) throw Failure("stat", name ?? "owned descriptor");
            var mode = Marshal.ReadInt32(buffer, 24) & 0xf000;
            var directory = mode == 0x4000; var regular = mode == 0x8000;
            return new($"{Marshal.ReadInt64(buffer, 0):x}:{Marshal.ReadInt64(buffer, 8):x}", directory, regular,
                regular ? Marshal.ReadInt64(buffer, 48) : 0,
                $"{Marshal.ReadInt64(buffer, 88):x}:{Marshal.ReadInt64(buffer, 96):x}:{Marshal.ReadInt64(buffer, 104):x}:{Marshal.ReadInt64(buffer, 112):x}");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static bool LinuxIsLink(int parent, string name)
    {
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            if (fstatat(parent, name, buffer, 0x100) != 0) throw Failure("stat", name);
            return (Marshal.ReadInt32(buffer, 24) & 0xf000) == 0xa000;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [DllImport("libc", SetLastError = true)] private static extern int openat(int fd, string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int unlinkat(int fd, string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int fstat(int fd, IntPtr stat);
    [DllImport("libc", SetLastError = true)] private static extern int fstatat(int fd, string path, IntPtr stat, int flags);

    private static async Task DeleteWindowsAsync(string path, string root, Dictionary<string, Entry> allowed,
        Action<string>? beforeEntry, string? rootIdentity, Action<string>? afterValidation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var relative = Path.GetRelativePath(root, path);
        if (relative != "." && !allowed.ContainsKey(relative)) Changed(relative);
        beforeEntry?.Invoke(path);
        using var handle = OpenNoFollow(path, deleting: true);
        if (!GetFileInformationByHandle(handle, out var raw)) throw Failure("inspect", path);
        if ((raw.FileAttributes & 0x400) != 0)
        {
            if (relative == ".") Changed(path);
            MarkDeleted(handle, path); // disposition of the reparse entry itself
            return;
        }
        var info = WindowsMetadata(handle, path); RequireSupported(info, path);
        if (relative == ".")
        {
            if (!info.Directory || info.Identity != rootIdentity) Changed(path);
        }
        else
        {
            var entry = allowed[relative];
            if (entry.Identity != info.Identity || entry.Directory != info.Directory || entry.Size != info.Size) Changed(relative);
            if (info.Regular && await HashAsync(handle, info, path, ct) != entry.Hash) Changed(relative);
        }
        afterValidation?.Invoke(path);
        if (info.Directory)
            foreach (var child in ListChildren(path, ct))
                await DeleteWindowsAsync(child, root, allowed, beforeEntry, null, afterValidation, ct);
        ct.ThrowIfCancellationRequested();
        // This very handle denies write and delete sharing until disposition;
        // ancestor handles likewise prevent replacement of the traversed chain.
        MarkDeleted(handle, path);
    }

    private static Metadata WindowsMetadata(SafeFileHandle handle, string path)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw Failure("inspect", path);
        if ((info.FileAttributes & (0x400 | 0x40)) != 0 || GetFileType(handle) != 1)
            throw new Refusal("unproven_content", path + " (non-regular/reparse)");
        var directory = (info.FileAttributes & 0x10) != 0;
        return new($"{info.VolumeSerialNumber:x}:{info.FileIndexHigh:x}:{info.FileIndexLow:x}", directory, !directory,
            directory ? 0 : ((long)info.FileSizeHigh << 32) | info.FileSizeLow,
            $"{info.LastWriteTimeHigh:x}:{info.LastWriteTimeLow:x}");
    }
    private static void RequireWindowsAttributes(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            throw new Refusal("unproven_content", path + " (non-regular/reparse)");
    }
    private sealed class WindowsParents : IDisposable
    {
        internal List<SafeFileHandle> Handles { get; } = [];
        public void Dispose() { foreach (var handle in Handles.AsEnumerable().Reverse()) handle.Dispose(); }
    }
    private static WindowsParents PinWindowsParents(string path)
    {
        var parents = new WindowsParents();
        try
        {
            var full = Path.GetFullPath(path); var current = Path.GetPathRoot(full)!;
            var components = full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < components.Length; i++)
            {
                var handle = OpenNoFollow(current, pinOnly: true); parents.Handles.Add(handle);
                if (!WindowsMetadata(handle, current).Directory) Changed(current);
                current = Path.Combine(current, components[i]);
            }
            return parents;
        }
        catch { parents.Dispose(); throw; }
    }

    private static SafeFileHandle OpenNoFollow(string path, bool deleting = false, bool pinOnly = false)
    {
        // GENERIC_READ plus DELETE only for disposition. Share READ only:
        // concurrent write/delete/rename cannot race validation and disposition.
        // Existing conflicting writers cause a bounded refusal, never a bypass.
        for (var attempt = 1; ; attempt++)
        {
            var handle = CreateFileW(path, (pinOnly ? 0x80u : 0x80000000u) | (deleting ? 0x10000u : 0u), pinOnly ? 0x3u : 0x1u,
                IntPtr.Zero, 3, 0x00200000 | 0x02000000, IntPtr.Zero);
            if (!handle.IsInvalid) return handle;
            var error = Marshal.GetLastPInvokeError(); handle.Dispose();
            if (error != 32 || attempt == 5) throw Failure("open", path, error);
            Thread.Sleep(100);
        }
    }

    private static IReadOnlyList<string> ListChildren(string path, CancellationToken ct)
    {
        var children = new List<string>();
        foreach (var child in Directory.EnumerateFileSystemEntries(path, "*", ListOnly))
        {
            ct.ThrowIfCancellationRequested();
            if (children.Count >= 100_000) throw new Refusal("unproven_content", path + " (entry limit)");
            children.Add(child);
        }
        children.Sort(StringComparer.Ordinal); return children;
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
    private static extern uint GetFileType(SafeFileHandle handle);

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
