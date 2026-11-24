using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// A directory link a test owns: a junction on Windows (no privilege needed), a symbolic link
/// elsewhere. <see cref="TryCreate"/> returns null when the host cannot make one, so the caller
/// skips instead of passing vacuously. Dispose removes only the link, never its target.
/// The junction is made in process (CreateDirectoryW, then FSCTL_SET_REPARSE_POINT with a mount
/// point buffer, as mklink /J writes it); until unit 35 of the 2026-11-22 plan it was mklink /J
/// in a pwsh child (tests/Antiphon.Tests/Scripts/Fixtures/directory-link.ps1). No process starts.
/// </summary>
internal sealed class DirectoryLink : IDisposable
{
    public string Path { get; private set; }

    private DirectoryLink(string path) => Path = path;

    public static DirectoryLink? TryCreate(string path, string target)
    {
        if (OperatingSystem.IsWindows())
            return NativeJunction.TryCreate(path, target) && IsLink(path) ? new DirectoryLink(path) : null;
        try
        {
            Directory.CreateSymbolicLink(path, target);
            return IsLink(path) ? new DirectoryLink(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Renames the link itself; the target is not followed.</summary>
    public void MoveTo(string path)
    {
        Directory.Move(Path, path);
        Path = path;
    }

    public void Dispose()
    {
        if (!IsLink(Path)) return;
        try
        {
            Directory.Delete(Path, recursive: false);
        }
        catch (DirectoryNotFoundException) when (!OperatingSystem.IsWindows())
        {
            // rmdir refuses a symbolic link whose target is already gone. The link is still here.
            File.Delete(Path);
        }
    }

    internal static bool IsLink(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    /// <summary>
    /// mklink /J without the shell: the link directory is created new (an existing name, a missing parent or a refused
    /// create is false and touches nothing), then made a mount point to the target's full path. A failed reparse write
    /// removes only the empty directory this call created.
    /// </summary>
    private static class NativeJunction
    {
        private const uint MountPointTag = 0xA0000003;
        private const uint SetReparsePoint = 0x000900A4;
        private const uint GenericWrite = 0x40000000;
        private const uint ShareAll = 0x00000007;
        private const uint OpenExisting = 3;
        private const uint BackupSemantics = 0x02000000;
        private const uint OpenReparsePoint = 0x00200000;

        internal static bool TryCreate(string link, string target)
        {
            string full;
            try { full = System.IO.Path.GetFullPath(target); }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return false; }
            if (!CreateDirectoryW(link, IntPtr.Zero)) return false;
            var made = false;
            try
            {
                using var handle = CreateFileW(link, GenericWrite, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
                if (handle.IsInvalid) return false;
                var substitute = Encoding.Unicode.GetBytes(@"\??\" + full);
                var print = Encoding.Unicode.GetBytes(full);
                // REPARSE_DATA_BUFFER: tag, data length, reserved, then the mount point header (substitute offset and
                // length, print offset and length) and both names, each followed by a UTF-16 NUL.
                var dataLength = 8 + substitute.Length + 2 + print.Length + 2;
                if (dataLength > ushort.MaxValue) return false;
                var buffer = new byte[8 + dataLength];
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), MountPointTag);
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)dataLength);
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), (ushort)substitute.Length);
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12), (ushort)(substitute.Length + 2));
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(14), (ushort)print.Length);
                substitute.CopyTo(buffer, 16);
                print.CopyTo(buffer, 16 + substitute.Length + 2);
                made = DeviceIoControl(handle, SetReparsePoint, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero);
                return made;
            }
            finally
            {
                if (!made) RemoveDirectoryW(link);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateDirectoryW(string path, IntPtr security);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool RemoveDirectoryW(string path);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);
    }
}
