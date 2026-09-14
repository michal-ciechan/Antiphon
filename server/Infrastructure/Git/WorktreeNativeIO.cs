using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>Metadata/open-only boundary. No content, disposition or deletion API.</summary>
public class WorktreeNativeIO
{
    public virtual bool Supported => OperatingSystem.IsWindows();
    public virtual bool Exists(string path) => Directory.Exists(path) || File.Exists(path);
    public virtual FileAttributes Attributes(string path) => File.GetAttributes(path);
    public virtual IEnumerator<string> Entries(string path) => Directory.EnumerateFileSystemEntries(path).GetEnumerator();

    public virtual string? Identity(string path)
    {
        if (!Supported) return null;
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var parts = new Stack<string>();
        for (string? at = path; at is not null; at = Path.GetDirectoryName(at)) parts.Push(at);
        var identities = new StringBuilder();
        foreach (var part in parts)
        {
            using var handle = Open(part, 0x80, 7, 3, 0x02200000, false);
            if (!handle.Succeeded || !Same(handle.FinalPath, part)
                || handle.ReparsePoint || handle.FileIdentity is null) return null;
            identities.Append(handle.FileIdentity).Append(';');
        }
        return identities.ToString();
    }

    public virtual WorktreeNativeHandle Open(string path, uint desiredAccess, uint shareMode,
        uint disposition, uint flags, bool inherit)
    {
        if (!Supported) throw new PlatformNotSupportedException();
        if (inherit) throw new ArgumentException("inheritable_probe_handle_forbidden");
        var handle = CreateFileW(path, desiredAccess, shareMode, IntPtr.Zero, disposition, flags, IntPtr.Zero);
        // Must precede every other native/managed metadata operation.
        var error = handle.IsInvalid ? Marshal.GetLastPInvokeError() : (int?)null;
        if (handle.IsInvalid) { handle.Dispose(); return new(false, error, null, null, false, null); }
        try
        {
            var name = new StringBuilder(32768);
            var size = GetFinalPathNameByHandleW(handle, name, (uint)name.Capacity, 0);
            var final = size is > 0 and < 32768 ? Normalize(name.ToString()) : null;
            var hasInfo = GetFileInformationByHandle(handle, out var info);
            var id = hasInfo ? $"{info.VolumeSerialNumber:x8}:{info.FileIndexHigh:x8}{info.FileIndexLow:x8}:{info.CreationTimeHigh:x8}{info.CreationTimeLow:x8}" : null;
            return new(true, null, final, id, hasInfo && (info.FileAttributes & 0x400) != 0, handle);
        }
        catch { handle.Dispose(); throw; }
    }

    internal static string Normalize(string path) => path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
        ? @"\\" + path[8..] : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    internal static bool Same(string? a, string b) => a is not null && string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    internal static bool Within(string path, string root) => Same(path, root)
        || Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr securityAttributes, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint FileAttributes, CreationTimeLow, CreationTimeHigh, LastAccessTimeLow, LastAccessTimeHigh,
            LastWriteTimeLow, LastWriteTimeHigh, VolumeSerialNumber, FileSizeHigh, FileSizeLow,
            NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
}

public sealed class WorktreeNativeHandle(bool succeeded, int? error, string? finalPath,
    string? fileIdentity, bool reparsePoint, IDisposable? resource) : IDisposable
{
    public bool Succeeded { get; } = succeeded;
    public int? Error { get; } = error;
    public string? FinalPath { get; } = finalPath;
    public string? FileIdentity { get; } = fileIdentity;
    public bool ReparsePoint { get; } = reparsePoint;
    public void Dispose() => resource?.Dispose();
}
