using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Antiphon.PtyHost;

/// <summary>
/// Linux detach for the pty-host intermediary: a new process (never a forked managed runtime),
/// then <c>setsid</c> plus stdio onto <c>/dev/null</c> so the intermediary's pipes reach EOF
/// while the host stays alive.
/// </summary>
internal static class PosixProcessSpawner
{
    internal const string DetachFlag = "--detach";
    private const int ORdwr = 2;

    public static int StartDetached(string exePath, IReadOnlyList<string> hostArgs)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("POSIX detach is only available on Unix.");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(DetachFlag);
        foreach (var arg in hostArgs)
            psi.ArgumentList.Add(arg);

        using var child = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start detached pty-host '{exePath}'.");
        return child.Id;
    }

    /// <summary>
    /// Become a session leader and release inherited stdio. Exits the process on failure so the
    /// intermediary never reports a pid that is not actually detached.
    /// </summary>
    public static void BecomeSessionLeaderOrExit()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("POSIX --detach is not supported on this platform.");
            Environment.Exit(1);
        }

        if (Setsid() < 0)
        {
            Console.Error.WriteLine($"setsid failed: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}");
            Environment.Exit(1);
        }

        var nullFd = Open("/dev/null", ORdwr);
        if (nullFd < 0)
        {
            Console.Error.WriteLine("open(/dev/null) failed.");
            Environment.Exit(1);
        }

        if (Dup2(nullFd, 0) < 0 || Dup2(nullFd, 1) < 0 || Dup2(nullFd, 2) < 0)
        {
            Console.Error.WriteLine("dup2 onto /dev/null failed.");
            Environment.Exit(1);
        }

        if (nullFd > 2)
            Close(nullFd);
    }

    [DllImport("libc", EntryPoint = "setsid", SetLastError = true)]
    private static extern int Setsid();

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string path, int flags);

    [DllImport("libc", EntryPoint = "dup2", SetLastError = true)]
    private static extern int Dup2(int oldfd, int newfd);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);
}
