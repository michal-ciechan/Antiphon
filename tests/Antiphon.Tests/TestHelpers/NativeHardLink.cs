using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Native fixture creation and independent identity observations; never copies bytes.</summary>
internal static class NativeHardLink
{
    internal static void Create(string path, string existing)
    {
        var success = OperatingSystem.IsWindows() ? CreateHardLinkW(path, existing, IntPtr.Zero)
            : OperatingSystem.IsLinux() ? Link(existing, path) == 0
            : throw new PlatformNotSupportedException("Hard-link fixtures require Windows or Linux.");
        if (!success) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Hard-link fixture creation failed.");
    }

    internal sealed record Observation(uint Links, ulong FileId, ulong Volume);

    internal static Observation Observe(string path)
    {
        // These handles close before the reader opens the file, including on Windows.
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (OperatingSystem.IsWindows())
        {
            if (!GetFileInformationByHandle(handle, out var info))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            return new(info.Links, ((ulong)info.IndexHigh << 32) | info.IndexLow, info.VolumeSerial);
        }
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        if (Statx(handle.DangerousGetHandle().ToInt32(), "", 0x1000, 0x104, out var stat) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        if ((stat.Mask & 0x104) != 0x104) throw new IOException("Fixture identity/link metadata unavailable.");
        return new(stat.Links, stat.Inode, ((ulong)stat.DeviceMajor << 32) | stat.DeviceMinor);
    }

    internal static void RequireNtfs(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native Windows qualification required.");
        if (!string.Equals(new DriveInfo(Path.GetPathRoot(path)!).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Native fixture requires local NTFS.");
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existing, string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string path, string existing, IntPtr security);
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Stat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint Links;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int fd, string path, int flags, uint mask, out Stat stat);
    [StructLayout(LayoutKind.Sequential)]
    private struct Information
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh,
            WriteLow, WriteHigh, VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Information info);
}
