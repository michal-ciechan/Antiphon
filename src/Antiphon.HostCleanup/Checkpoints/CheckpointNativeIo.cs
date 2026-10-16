using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Antiphon.HostCleanup.Checkpoints;

public sealed class CheckpointNativeIo : ICheckpointCleanupIo
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
    public uint Owner => OperatingSystem.IsLinux() ? GetEuid() : OperatingSystem.IsWindows() ? 1u : uint.MaxValue;
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
        if (OperatingSystem.IsWindows()) return CreatePrivateWindowsDirectory(path);
        var success = MakeUnix(path, 448) == 0;
        return success ? 0 : Marshal.GetLastPInvokeError();
    }

    private static int CreatePrivateWindowsDirectory(string path)
    {
        using var current = WindowsIdentity.GetCurrent();
        var sid = current.User?.Value ?? throw new IOException("current owner unavailable");
        // Protected DACL; child files/directories inherit only this owner's full control.
        if (!ConvertSecurityDescriptor($"O:{sid}D:P(A;OICI;FA;;;{sid})", 1, out var descriptor, out _))
            return Marshal.GetLastPInvokeError();
        try
        {
            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            var success = MakePrivateWindows(path, ref security);
            return success ? 0 : Marshal.GetLastPInvokeError();
        }
        finally { LocalFree(descriptor); }
    }

    public CheckpointStat Stat(string path, bool portable = false)
    {
        if (!portable && OperatingSystem.IsWindows()) return WindowsStat(path);
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

    private static CheckpointStat WindowsStat(string path)
    {
        try
        {
            // READ_ATTRIBUTES, OPEN_REPARSE_POINT and BACKUP_SEMANTICS: inspect the entry,
            // including directories, without following a reparse target.
            using var handle = CreateFile(path, 0x80, 7, IntPtr.Zero, 3, 0x00200000 | 0x02000000, IntPtr.Zero);
            if (handle.IsInvalid) return new(Marshal.GetLastPInvokeError() is 2 or 3 ? "absent" : "unknown", 0, 0, 0, 0, 0);
            if (!GetFileInformationByHandle(handle, out var info)
                || !GetFileInformationByHandleEx(handle, 0, out var basic, (uint)Marshal.SizeOf<WindowsBasicInfo>()))
                return new("unknown", 0, 0, 0, 0, 0);
            if ((info.Attributes & 0x400) != 0) return new("symlink", 0, 0, 0, 0, 0);
            if (GetFileType(handle) != 1) return new("other", 0, 0, 0, 0, 0);
            var directory = (info.Attributes & 0x10) != 0;
            FileSystemSecurity security = directory
                ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
                : new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            using var current = WindowsIdentity.GetCurrent();
            var owner = security.GetOwner(typeof(SecurityIdentifier));
            var owned = current.User is not null && current.User.Equals(owner);
            var privateAcl = !security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
                .Any(rule => rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(current.User));
            var mode = (ushort)((directory ? 0x41C0 : 0x8180) | (privateAcl ? 0 : 0x3F));
            return new(directory ? "directory" : "file", info.Volume,
                ((ulong)info.IndexHigh << 32) | info.IndexLow, directory ? 0 : (long)(((ulong)info.SizeHigh << 32) | info.SizeLow),
                UnixNanoseconds(basic.Write), UnixNanoseconds(basic.Change), mode, info.Links, owned ? 1u : 0u);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return new("unknown", 0, 0, 0, 0, 0); }
    }

    private static long UnixNanoseconds(long fileTime) => (DateTime.FromFileTimeUtc(fileTime).Ticks - DateTime.UnixEpoch.Ticks) * 100;

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
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsBasicInfo
    {
        public long Creation, Access, Write, Change;
        public uint Attributes;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr Descriptor;
        public int InheritHandle;
    }
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int Statx(int fd, string path, int flags, uint mask, out LinuxStatx stat);
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();
    [DllImport("libc", EntryPoint = "mkdir", SetLastError = true)] private static extern int MakeUnix(string path, uint mode);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int OpenNative(string path, int flags);
    [DllImport("libc", EntryPoint = "readlink", SetLastError = true)] private static extern nint ReadLink(string path, byte[] buffer, nuint size);
    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MakeWindows(string path, IntPtr security);
    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MakePrivateWindows(string path, ref SecurityAttributes security);
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertSecurityDescriptor(string descriptor, uint revision, out IntPtr security, out uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out WindowsBasicInfo info, uint size);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out WindowsInfo info);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(SafeFileHandle handle);
}
