using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.HostCleanup.Checkpoints;

public sealed partial class CheckpointNativeIo : ICheckpointCleanupIo
{
    public int CurrentProcessId => Environment.ProcessId;
    public string MachineName => Environment.MachineName;
    public string BootId => ReadBootId();
    public string PidNamespace => ReadPidNamespace();
    public string ImageDirectory => AppContext.BaseDirectory;
    public string CurrentDirectory => Environment.CurrentDirectory;
    public DateTime CreationTimeUtc(string path) => Directory.GetCreationTimeUtc(path);
    public DateTime LastWriteTimeUtc(string path) => Directory.GetLastWriteTimeUtc(path);
    public string StreamPath(Stream stream) => ((FileStream)stream).Name;
    public CheckpointProcessSnapshot Process(int pid)
    {
        using var process = System.Diagnostics.Process.GetProcessById(pid);
        var (start, state) = StartToken(pid, process);
        return new(start, state, process.HasExited);
    }

    private static (long Token, char State) StartToken(int pid, Process process)
    {
        if (!OperatingSystem.IsLinux())
            return (process.StartTime.ToUniversalTime().Ticks, 'R');
        // /proc starttime is a kernel generation in clock ticks since boot. Unlike
        // Process.StartTime's UTC conversion it is byte-stable across observers.
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var endName = stat.LastIndexOf(')');
        if (endName < 0 || endName + 2 >= stat.Length)
            throw new IOException("invalid proc stat");
        var fields = stat[(endName + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length <= 19 || fields[0].Length != 1
            || !long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var start))
            throw new IOException("invalid proc starttime");
        return (start, fields[0][0]);
    }

    private const string BootFormat = "yyyyMMddHHmm";
    private static string ReadBootId()
    {
        if (OperatingSystem.IsLinux())
            return File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
        if (OperatingSystem.IsWindows())
        {
            // The kernel's recorded boot time (CIM's LastBootUpTime). Now minus uptime drifted
            // with every clock slew (29 s after 20 days), so earlier markers read as foreign.
            var info = new byte[48];
            var status = NtQuerySystemInformation(SystemTimeOfDayInformation, info, info.Length, out _);
            if (status != 0)
                throw new InvalidOperationException($"boot time query failed: 0x{status:X8}");
            return DateTime.FromFileTimeUtc(BitConverter.ToInt64(info, 0))
                .ToString(BootFormat, CultureInfo.InvariantCulture);
        }
        // A change or clock correction can only prevent cleanup, never authorize it.
        return (DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64))
            .UtcDateTime.ToString(BootFormat, CultureInfo.InvariantCulture);
    }

    private const int SystemTimeOfDayInformation = 3;

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, byte[] info, int length, out int returned);

    private static string ReadPidNamespace() => OperatingSystem.IsLinux()
        ? new FileInfo("/proc/self/ns/pid").LinkTarget ?? "unknown"
        : "windows-local";
    public CheckpointPlatform Platform => OperatingSystem.IsLinux() ? CheckpointPlatform.Linux
        : OperatingSystem.IsWindows() ? CheckpointPlatform.Windows : CheckpointPlatform.Other;
    public uint Owner => OperatingSystem.IsLinux() ? GetEuid()
        : OperatingSystem.IsWindows() ? WindowsOwner() : uint.MaxValue;
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public FileAttributes Attributes(string path) => File.GetAttributes(path);
    public string? LinkTarget(string path) => new DirectoryInfo(path).LinkTarget ?? new FileInfo(path).LinkTarget;
    public long Length(string path) => new FileInfo(path).Length;
    public IEnumerable<string> Enumerate(string path) => Directory.EnumerateFileSystemEntries(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public Stream Open(string path, FileMode mode, FileAccess access, FileShare share) => new FileStream(path, mode, access, share);
    public void WriteBytes(string path, ReadOnlySpan<byte> bytes) => File.WriteAllBytes(path, bytes.ToArray());
    public void AppendText(string path, string text) => File.AppendAllText(path, text);
    public void Copy(string source, string destination, bool overwrite) => File.Copy(source, destination, overwrite);
    public void Move(string source, string destination, bool overwrite)
    {
        // MoveFileEx replacement can deny a destination even when readers share deletion.
        // ReplaceFile publishes the complete temporary while existing readers retain the old
        // file. Keep first publication on Move, and let the caller retry sharing violations.
        // ReplaceFileW with a backup keeps the previous generation when error 1176 or 1177
        // would otherwise drop the destination. Linux rename stays a single Move.
        if (OperatingSystem.IsWindows() && overwrite && File.Exists(destination))
        {
            var backup = destination + "." + Guid.NewGuid().ToString("N") + ".bak";
            try
            {
                File.Replace(source, destination, backup);
                if (!File.Exists(destination))
                    throw new IOException("replacement-unverified", unchecked((int)0x80070498));
            }
            catch (IOException)
            {
                if (!File.Exists(destination) && File.Exists(backup))
                {
                    try { File.Move(backup, destination, overwrite: false); }
                    catch (IOException) { }
                }
                else if (File.Exists(backup))
                {
                    try { File.Delete(backup); }
                    catch (IOException) { }
                }
                throw;
            }
            if (File.Exists(backup))
            {
                try { File.Delete(backup); }
                catch (IOException) { }
            }
            return;
        }
        File.Move(source, destination, overwrite);
    }
    public void Unlink(string path) => File.Delete(path);
    public void RemoveDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
    public void DeleteAnchored(string path, FilesystemIdentity expectedEntry, bool directory)
    {
        if (OperatingSystem.IsLinux()) DeleteAnchoredLinux(path, expectedEntry, directory);
        else if (OperatingSystem.IsWindows()) DeleteAnchoredWindows(path, expectedEntry, directory);
        else throw new IOException("anchored-delete-unavailable");
    }

    private void DeleteAnchoredLinux(string path, FilesystemIdentity expectedEntry, bool directory)
    {
        var parent = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(parent) || !AnchoredDelete.EntryNameSafe(windows: false, name))
            throw new IOException("anchored-path-unsafe");
        // O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC. A swapped symlink fails here and is not deleted.
        var fd = OpenNative(parent, 0x10000 | 0x20000 | 0x80000);
        if (fd < 0) throw new IOException("anchored-open-failed: errno=" + Marshal.GetLastPInvokeError());
        try
        {
            if (Statx(fd, "", 0x1000, 0x3CF, out var parentStat) != 0 || (parentStat.Mask & 0x3CF) != 0x3CF)
                throw new IOException("anchored-parent-stat-failed: errno=" + Marshal.GetLastPInvokeError());
            if (Statx(fd, name, 0x100, 0x3CF | 0x800, out var entry) != 0 || (entry.Mask & 0x3CF) != 0x3CF)
                throw new IOException("anchored-entry-stat-failed: errno=" + Marshal.GetLastPInvokeError());
            var entryIdentity = IdentityFromLinux(entry);
            var entryKind = entry.Mode & 0xF000;
            if (!AnchoredDelete.Allowed(true, (parentStat.Mode & 0xF000) == 0x4000, false, expectedEntry, entryIdentity, entryKind == 0xA000))
                throw new IOException("anchored-identity-mismatch");
            if (UnlinkAt(fd, name, directory ? 0x200 : 0) != 0)
                throw new IOException("anchored-unlink-failed: errno=" + Marshal.GetLastPInvokeError());
        }
        finally { _ = CloseNative(fd); }
    }

    private void DeleteAnchoredWindows(string path, FilesystemIdentity expectedEntry, bool directory)
    {
        var parent = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(parent) || !AnchoredDelete.EntryNameSafe(windows: true, name))
            throw new IOException("anchored-path-unsafe");
        const uint flags = 0x02000000 | 0x00200000;
        var parentHandle = CreateFile(parent, 0x80000000, 7, IntPtr.Zero, 3, flags, IntPtr.Zero);
        if (parentHandle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            parentHandle.Dispose();
            throw new IOException("anchored-open-failed: win32=" + error);
        }
        using (parentHandle)
        {
            if (!GetFileInformationByHandle(parentHandle, out var parentInfo))
                throw new IOException("anchored-parent-stat-failed");
            using var child = OpenRelative(parentHandle, name, directory);
            if (!GetFileInformationByHandle(child, out var info)
                || !GetFileInformationByHandleEx(child, FileBasicInformation, out var basic, (uint)Marshal.SizeOf<FileBasicInfo>()))
                throw new IOException("anchored-entry-stat-failed");
            var entry = new FilesystemIdentity(info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow, FileTimeToUnixNs(basic.CreationTime));
            if (!AnchoredDelete.Allowed(true, (parentInfo.Attributes & 0x10u) != 0, (parentInfo.Attributes & 0x400u) != 0,
                    expectedEntry, entry, (info.Attributes & 0x400u) != 0))
                throw new IOException("anchored-identity-mismatch");
            var disposition = new FileDisposition { DeleteFile = true };
            if (!SetFileInformationByHandle(child, 4, ref disposition, Marshal.SizeOf<FileDisposition>()))
                throw new IOException("anchored-unlink-failed: win32=" + Marshal.GetLastPInvokeError());
        }
    }

    private static SafeFileHandle OpenRelative(SafeFileHandle parent, string name, bool directory)
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
                uint options = 0x20 | 0x00200000 | (directory ? 0x1u : 0x40u);
                // DELETE | SYNCHRONIZE | FILE_READ_ATTRIBUTES: synchronous opens require
                // SYNCHRONIZE, and identity verification reads attributes through this handle.
                const uint access = 0x00110080;
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

    private static FilesystemIdentity IdentityFromLinux(LinuxStatx stat)
    {
        var created = (stat.Mask & 0x800) != 0 ? stat.BtimeSec * 1_000_000_000 + stat.BtimeNsec : 0;
        return new FilesystemIdentity(((ulong)stat.DevMajor << 32) | stat.DevMinor, stat.Inode, created);
    }
    public string? EnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public int CreateExclusiveDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            var unix = MakeUnix(path, 448);
            return unix == 0 ? 0 : Marshal.GetLastPInvokeError();
        }

        // Owner-only DACL first. A null descriptor inherits ACEs, the mode check then fails,
        // and RecordFreshRoot never runs, so every Windows seal comes back cleanup_refused.
        var bytes = OwnerOnlySecurity();
        if (bytes is null)
            return Marshal.GetLastPInvokeError() is 0 ? 1 : Marshal.GetLastPInvokeError();
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        var attributes = Marshal.AllocHGlobal(Marshal.SizeOf<SecurityAttributes>());
        try
        {
            Marshal.StructureToPtr(new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = pinned.AddrOfPinnedObject(),
                InheritHandle = false,
            }, attributes, false);
            return MakeWindows(path, attributes) ? 0 : Marshal.GetLastPInvokeError();
        }
        finally
        {
            Marshal.FreeHGlobal(attributes);
            pinned.Free();
        }
    }

    // First open plus this many waits. 20 * 50 ms keeps a share-none holder that drops inside
    // the bound adoptable, and one that never drops unknown so cleanup still refuses.
    internal const int CustodySharingViolationAttempts = 21;
    internal const int CustodySharingViolationDelayMilliseconds = 50;
    private const int ErrorSharingViolation = 32;

    public CheckpointStat CustodyStat(string path)
    {
        if (!OperatingSystem.IsWindows())
            return Stat(path);
        for (var attempt = 1; ; attempt++)
        {
            var stat = ReadWindowsCustody(path, out var sharingViolation);
            if (!sharingViolation || attempt >= CustodySharingViolationAttempts)
                return stat;
            Thread.Sleep(CustodySharingViolationDelayMilliseconds);
        }
    }

    private CheckpointStat ReadWindowsCustody(string path, out bool sharingViolation)
    {
        sharingViolation = false;
        try
        {
            // BACKUP_SEMANTICS opens a directory. OPEN_REPARSE_POINT keeps a junction from looking like its target.
            const uint flags = 0x02000000 | 0x00200000;
            var handle = CreateFile(path, 0x80000000, 7, IntPtr.Zero, 3, flags, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                if (error == ErrorSharingViolation)
                {
                    sharingViolation = true;
                    return new("unknown", 0, 0, 0, 0, 0);
                }

                return new(File.Exists(path) || Directory.Exists(path) ? "unknown" : "absent", 0, 0, 0, 0, 0);
            }

            using (handle)
            {
                if (!GetFileInformationByHandle(handle, out var info))
                    return new("unknown", 0, 0, 0, 0, 0);
                if (!GetFileInformationByHandleEx(handle, FileBasicInformation, out var basic, (uint)Marshal.SizeOf<FileBasicInfo>()))
                    return new("unknown", 0, 0, 0, 0, 0);
                var reparse = (info.Attributes & 0x400u) != 0;
                var directory = (info.Attributes & 0x10u) != 0;
                var (kind, mode) = reparse ? ("symlink", (ushort)0xA000)
                    : directory ? ("directory", (ushort)0x4000)
                    : GetFileType(handle) == 1 ? ("file", (ushort)0x8000)
                    : ("other", (ushort)0);
                var inode = ((ulong)info.IndexHigh << 32) | info.IndexLow;
                // NTFS change time moves when creation or last-write is stamped back. CopyFile keeps it.
                var changed = FileTimeToUnixNs(basic.ChangeTime);
                if (inode == 0 || changed == 0 || !TryWindowsOwner(handle, out var owner, out var ownerOnly))
                    return new("unknown", 0, 0, 0, 0, 0);
                if (!ownerOnly)
                    mode = (ushort)(mode | 0x3F);
                var size = (long)(((ulong)info.SizeHigh << 32) | info.SizeLow);
                return new(kind, info.Volume, inode, kind == "file" ? size : 0, FileTimeToUnixNs(info.Write), changed, mode, info.Links, owner,
                    FileTimeToUnixNs(basic.CreationTime));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new("unknown", 0, 0, 0, 0, 0);
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[]? OwnerOnlySecurity()
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var user = identity.User;
        if (user is null)
            return null;
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            user, FileSystemRights.FullControl, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        return security.GetSecurityDescriptorBinaryForm();
    }

    private static readonly object OwnerLock = new();
    private static uint _windowsOwner;
    private static int _windowsOwnerReady;

    [SupportedOSPlatform("windows")]
    private static uint WindowsOwner()
    {
        if (Volatile.Read(ref _windowsOwnerReady) == 1)
            return _windowsOwner;
        lock (OwnerLock)
        {
            if (_windowsOwnerReady == 1)
                return _windowsOwner;
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            var user = identity.User ?? throw new InvalidOperationException("windows owner sid unavailable");
            _windowsOwner = SidHash(user);
            Volatile.Write(ref _windowsOwnerReady, 1);
            return _windowsOwner;
        }
    }

    [SupportedOSPlatform("windows")]
    private static uint SidHash(SecurityIdentifier sid)
    {
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        return SidHash(bytes);
    }

    private static uint SidHash(IntPtr sid)
    {
        var length = GetLengthSid(sid);
        if (length <= 0)
            return 0;
        var bytes = new byte[length];
        Marshal.Copy(sid, bytes, 0, length);
        return SidHash(bytes);
    }

    private static uint SidHash(ReadOnlySpan<byte> sid)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(sid, hash);
        return BitConverter.ToUInt32(hash);
    }

    private static bool TryWindowsOwner(SafeFileHandle handle, out uint owner, out bool ownerOnly)
    {
        owner = 0;
        ownerOnly = false;
        var added = false;
        handle.DangerousAddRef(ref added);
        try
        {
            var error = GetSecurityInfo(handle.DangerousGetHandle(), SeFileObject,
                OwnerSecurityInformation | DaclSecurityInformation,
                out var ownerSid, out _, out var dacl, out _, out var descriptor);
            if (error != 0 || ownerSid == IntPtr.Zero)
                return false;
            try
            {
                var length = GetLengthSid(ownerSid);
                if (length <= 0)
                    return false;
                owner = SidHash(ownerSid);
                ownerOnly = AllowsOnlyOwner(dacl, ownerSid);
                return true;
            }
            finally
            {
                if (descriptor != IntPtr.Zero)
                    LocalFree(descriptor);
            }
        }
        finally
        {
            if (added)
                handle.DangerousRelease();
        }
    }

    // A NULL or unreadable DACL, or any allow ACE for a SID other than the owner, is not owner-only.
    private static bool AllowsOnlyOwner(IntPtr dacl, IntPtr owner)
    {
        if (dacl == IntPtr.Zero)
            return false;
        var info = new AclSizeInformation();
        if (!GetAclInformation(dacl, ref info, Marshal.SizeOf<AclSizeInformation>(), AclSizeInformationClass))
            return false;
        if (info.AceCount == 0)
            return false;
        for (var i = 0; i < info.AceCount; i++)
        {
            if (!GetAce(dacl, i, out var ace))
                return false;
            var type = Marshal.ReadByte(ace);
            if (type == AccessDeniedAceType)
                continue;
            if (type != AccessAllowedAceType)
                return false;
            if (!EqualSid(IntPtr.Add(ace, 8), owner))
                return false;
        }

        return true;
    }

    private static long FileTimeToUnixNs(System.Runtime.InteropServices.ComTypes.FILETIME time) =>
        FileTimeToUnixNs(((long)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime);

    private static long FileTimeToUnixNs(long fileTime)
    {
        if (fileTime <= 0)
            return 0;
        var utc = DateTime.FromFileTimeUtc(fileTime);
        return (utc.Ticks - DateTime.UnixEpoch.Ticks) * 100;
    }

    public CheckpointStat Stat(string path, bool portable = false)
    {
        if (!portable && OperatingSystem.IsLinux())
        {
            try
            {
                // TYPE MODE NLINK UID MTIME CTIME INO SIZE, with no followed symlinks.
                const uint mask = 0x3CF;
                if (Statx(-100, path, 0x100, mask | 0x800, out var stat) != 0)
                    return new(Marshal.GetLastPInvokeError() is 2 or 20 ? "absent" : "unknown", 0, 0, 0, 0, 0);
                if ((stat.Mask & mask) != mask) return new("unknown", 0, 0, 0, 0, 0);
                var kind = (stat.Mode & 0xF000) switch { 0x8000 => "file", 0x4000 => "directory", 0xA000 => "symlink", _ => "other" };
                var created = (stat.Mask & 0x800) != 0 ? stat.BtimeSec * 1_000_000_000 + stat.BtimeNsec : 0;
                return new(kind, ((ulong)stat.DevMajor << 32) | stat.DevMinor, stat.Inode, (long)stat.Size,
                    stat.MtimeSec * 1_000_000_000 + stat.MtimeNsec, stat.CtimeSec * 1_000_000_000 + stat.CtimeNsec,
                    stat.Mode, stat.Links, stat.Uid, created);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
        try
        {
            var file = new FileInfo(path);
            var kind = file.LinkTarget is not null ? "symlink" : file.Exists ? "file" : Directory.Exists(path) ? "directory" : "absent";
            if (kind == "absent") return new(kind, 0, 0, 0, 0, 0);
            var written = kind == "directory" ? Directory.GetLastWriteTimeUtc(path) : file.LastWriteTimeUtc;
            return new(kind, 0, 0, kind == "file" ? file.Length : 0, (written.Ticks - DateTime.UnixEpoch.Ticks) * 100, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return new("unknown", 0, 0, 0, 0, 0); }
    }

    public ICheckpointReadHandle OpenRead(string path, CheckpointReadOpen request)
    {
        SafeFileHandle handle;
        if (OperatingSystem.IsLinux())
        {
            var fd = OpenNative(path, request.UnixFlags);
            if (fd < 0) throw NativeFailure();
            handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
        }
        else if (OperatingSystem.IsWindows())
        {
            handle = CreateFile(path, 0x80000000, 7, IntPtr.Zero, 3, request.WindowsFlags, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw NativeFailure(); }
        }
        else throw new InvalidDataException("opened-handle verification unsupported on this platform");
        return new NativeHandle(handle);
    }

    // The reader owns a dedicated thread and an independent deadline monitor. Execute
    // the syscall there on both platforms, without worker or overlapped-completion queues.
    private sealed class DedicatedReadStream(SafeFileHandle handle) : FileStream(handle, FileAccess.Read)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }
    }

    private sealed class NativeHandle(SafeFileHandle handle) : ICheckpointReadHandle
    {
        public Stream OpenStream() => new DedicatedReadStream(handle);
        public void Dispose() => handle.Dispose();
        public CheckpointHandleStat Inspect()
        {
            if (OperatingSystem.IsLinux())
            {
                var fd = handle.DangerousGetHandle().ToInt32();
                if (Statx(fd, "", 0x1000, 0x205, out var stat) != 0 || (stat.Mask & 0x205) != 0x205) throw NativeFailure();
                var buffer = new byte[32768];
                var count = ReadLink("/proc/self/fd/" + fd, buffer, (nuint)buffer.Length);
                if (count <= 0 || count >= buffer.Length) throw NativeFailure();
                return new((stat.Mode & 0xF000) == 0x8000, stat.Links, stat.Size, Encoding.UTF8.GetString(buffer, 0, (int)count));
            }
            if (!GetFileInformationByHandle(handle, out var info)) throw NativeFailure();
            var path = new StringBuilder(32768);
            var length = GetFinalPathNameByHandle(handle, path, (uint)path.Capacity, 0);
            if (length == 0 || length >= path.Capacity) throw NativeFailure();
            var final = path.ToString();
            if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) final = @"\\" + final[8..];
            else if (final.StartsWith(@"\\?\", StringComparison.Ordinal)) final = final[4..];
            return new(GetFileType(handle) == 1 && (info.Attributes & (0x10u | 0x400u)) == 0, info.Links,
                ((ulong)info.SizeHigh << 32) | info.SizeLow, final);
        }
    }

    private static IOException NativeFailure() => new("opened-handle verification failed: errno=" + Marshal.GetLastPInvokeError());
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint Links;
        [FieldOffset(20)] public uint Uid;
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
    private struct WindowsInfo
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInfo
    {
        public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
        public uint FileAttributes;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileDisposition
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool DeleteFile;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        // Native IO_STATUS_BLOCK begins with an NTSTATUS/PVOID union, followed by
        // ULONG_PTR Information: two pointer-sized fields on both x86 and x64.
        public IntPtr StatusOrPointer;
        public UIntPtr Information;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool InheritHandle;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct AclSizeInformation
    {
        public uint AceCount, AclBytesInUse, AclBytesFree;
    }
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int Statx(int fd, string path, int flags, uint mask, out LinuxStatx stat);
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();
    [DllImport("libc", EntryPoint = "mkdir", SetLastError = true)] private static extern int MakeUnix(string path, uint mode);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int OpenNative(string path, int flags);
    [DllImport("libc", EntryPoint = "close", SetLastError = true)] private static extern int CloseNative(int fd);
    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)] private static extern int UnlinkAt(int dirfd, string pathname, int flags);
    [DllImport("ntdll.dll")] private static extern int NtCreateFile(out IntPtr handle, uint access, ref ObjectAttributes attributes, out IoStatusBlock ioStatus, IntPtr allocation, uint fileAttributes, uint share, uint disposition, uint options, IntPtr ea, uint eaLength);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int fileInformationClass, ref FileDisposition information, int size);
    [DllImport("libc", EntryPoint = "readlink", SetLastError = true)] private static extern nint ReadLink(string path, byte[] buffer, nuint size);
    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MakeWindows(string path, IntPtr security);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out WindowsInfo info);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int fileInformationClass, out FileBasicInfo info, uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityInfo(IntPtr handle, int objectType, uint securityInfo, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
    [DllImport("advapi32.dll")] private static extern int GetLengthSid(IntPtr sid);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetAclInformation(IntPtr acl, ref AclSizeInformation info, int length, int infoClass);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetAce(IntPtr acl, int index, out IntPtr ace);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EqualSid(IntPtr left, IntPtr right);
    private const int FileBasicInformation = 0;
    private const int SeFileObject = 6;
    private const int AclSizeInformationClass = 2;
    private const byte AccessAllowedAceType = 0;
    private const byte AccessDeniedAceType = 1;
    private const uint OwnerSecurityInformation = 0x1;
    private const uint DaclSecurityInformation = 0x4;
}
