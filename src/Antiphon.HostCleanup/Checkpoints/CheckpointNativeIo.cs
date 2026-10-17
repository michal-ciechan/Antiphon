using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.HostCleanup.Checkpoints;

public sealed class CheckpointNativeIo : ICheckpointCleanupIo
{
    public CheckpointPlatform Platform => OperatingSystem.IsLinux() ? CheckpointPlatform.Linux
        : OperatingSystem.IsWindows() ? CheckpointPlatform.Windows : CheckpointPlatform.Other;
    public uint Owner => OperatingSystem.IsLinux() ? GetEuid() : uint.MaxValue;
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
    public void Move(string source, string destination, bool overwrite) => File.Move(source, destination, overwrite);
    public void Unlink(string path) => File.Delete(path);
    public void RemoveDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
    public string? EnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public int CreateExclusiveDirectory(string path)
    {
        var success = OperatingSystem.IsWindows() ? MakeWindows(path, IntPtr.Zero) : MakeUnix(path, 448) == 0;
        return success ? 0 : Marshal.GetLastPInvokeError();
    }

    public CheckpointStat CustodyStat(string path)
    {
        if (!OperatingSystem.IsWindows())
            return Stat(path);
        try
        {
            // BACKUP_SEMANTICS opens a directory. OPEN_REPARSE_POINT keeps a junction from looking like its target.
            const uint flags = 0x02000000 | 0x00200000;
            var handle = CreateFile(path, 0x80000000, 7, IntPtr.Zero, 3, flags, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                return new(File.Exists(path) || Directory.Exists(path) ? "unknown" : "absent", 0, 0, 0, 0, 0);
            }

            using (handle)
            {
                if (!GetFileInformationByHandle(handle, out var info))
                    return new("unknown", 0, 0, 0, 0, 0);
                var reparse = (info.Attributes & 0x400u) != 0;
                var directory = (info.Attributes & 0x10u) != 0;
                var (kind, mode) = reparse ? ("symlink", (ushort)0xA000)
                    : directory ? ("directory", (ushort)0x4000)
                    : GetFileType(handle) == 1 ? ("file", (ushort)0x8000)
                    : ("other", (ushort)0);
                var inode = ((ulong)info.IndexHigh << 32) | info.IndexLow;
                var changed = FileTimeToUnixNs(info.Creation);
                if (inode == 0 || changed == 0)
                    return new("unknown", 0, 0, 0, 0, 0);
                var size = (long)(((ulong)info.SizeHigh << 32) | info.SizeLow);
                return new(kind, 0, inode, kind == "file" ? size : 0, FileTimeToUnixNs(info.Write), changed, mode, info.Links, Owner);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new("unknown", 0, 0, 0, 0, 0);
        }
    }

    private static long FileTimeToUnixNs(System.Runtime.InteropServices.ComTypes.FILETIME time)
    {
        var fileTime = ((long)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;
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
                if (Statx(-100, path, 0x100, mask, out var stat) != 0)
                    return new(Marshal.GetLastPInvokeError() is 2 or 20 ? "absent" : "unknown", 0, 0, 0, 0, 0);
                if ((stat.Mask & mask) != mask) return new("unknown", 0, 0, 0, 0, 0);
                var kind = (stat.Mode & 0xF000) switch { 0x8000 => "file", 0x4000 => "directory", 0xA000 => "symlink", _ => "other" };
                return new(kind, ((ulong)stat.DevMajor << 32) | stat.DevMinor, stat.Inode, (long)stat.Size,
                    stat.MtimeSec * 1_000_000_000 + stat.MtimeNsec, stat.CtimeSec * 1_000_000_000 + stat.CtimeNsec,
                    stat.Mode, stat.Links, stat.Uid);
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

    private sealed class NativeHandle(SafeFileHandle handle) : ICheckpointReadHandle
    {
        public Stream OpenStream() => new FileStream(handle, FileAccess.Read);
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
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int Statx(int fd, string path, int flags, uint mask, out LinuxStatx stat);
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();
    [DllImport("libc", EntryPoint = "mkdir", SetLastError = true)] private static extern int MakeUnix(string path, uint mode);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int OpenNative(string path, int flags);
    [DllImport("libc", EntryPoint = "readlink", SetLastError = true)] private static extern nint ReadLink(string path, byte[] buffer, nuint size);
    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MakeWindows(string path, IntPtr security);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out WindowsInfo info);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(SafeFileHandle handle);
}
