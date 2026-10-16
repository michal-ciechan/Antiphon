using System;
using System.Runtime.InteropServices;

namespace Antiphon.Checkpoints;

// Native mkdir/CreateDirectory fail atomically if the directory already exists.
public static partial class ExclusiveDirectory
{
    [DllImport("libc", EntryPoint = "mkdir", SetLastError = true)]
    private static extern int MakeUnix(string path, uint mode);
    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MakeWindows(string path, IntPtr security);

    public static string? Create(string path)
    {
        var success = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? MakeWindows(path, IntPtr.Zero) : MakeUnix(path, 448) == 0;
        if (success) return null;
        var error = Marshal.GetLastWin32Error();
        return error is 17 or 80 or 183 ? "pre-existing-output" : "output-create-failed:" + error;
    }
}
