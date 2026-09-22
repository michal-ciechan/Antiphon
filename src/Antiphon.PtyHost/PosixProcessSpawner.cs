using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Antiphon.PtyHost;

/// <summary>
/// Linux detach for the pty-host intermediary: a new process (never a forked managed runtime),
/// then <c>setsid</c> plus stdio onto <c>/dev/null</c> so the intermediary's pipes reach EOF
/// while the host stays alive.
///
/// <para>CARD-0594: <c>dup2</c> onto <c>/dev/null</c> is not enough on its own. A live detached
/// host was measured holding two further write ends of the launcher's redirect pipe at fds 6 and
/// 7 — duplicates of the inherited stdout/stderr, so <c>dup2</c> on 0/1/2 never touched them.
/// A pipe reaches EOF only when every writer closes it, so <c>LaunchDetachedAsync</c> could not
/// return until the host died, and the runner's connect only began after the host's launch
/// timeout (investigation 2026-09-22, section 3.1). Whoever makes those duplicates — the CLR's
/// PAL <c>dup</c>s stdio during startup before <c>Main</c>, and a non-CLOEXEC copy inherited
/// across <c>exec</c> would look the same — they are recognisable by what they point at, so the
/// release below closes every descriptor above 2 that still aliases the original 0/1/2 by
/// <c>readlink</c> identity. Deliberately not a blind <c>closefrom(3)</c>: that would also close
/// descriptors the running CoreCLR owns (diagnostics IPC socket, image handles, the
/// System.Native signal pipe) with undefined consequences.</para>
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

        // Record what the inherited stdio points at before dup2 rewires 0/1/2, so the surviving
        // aliases can be recognised afterwards by identity rather than by descriptor number.
        var stdioIdentity = RecordStdioIdentity();

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

        CloseStdioAliases(stdioIdentity);
    }

    /// <summary>
    /// The <c>readlink(2)</c> targets of fds 0, 1 and 2 as the kernel reports them:
    /// <c>pipe:[18882905]</c>, <c>socket:[n]</c>, or a path. Empty off Linux, where there is no
    /// <c>/proc</c> to read and the old dup2-only behaviour stays (there is no macOS runner).
    /// </summary>
    private static HashSet<string> RecordStdioIdentity()
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        if (!OperatingSystem.IsLinux())
            return identities;

        for (var fd = 0; fd <= 2; fd++)
        {
            var target = ReadLinkOrNull($"/proc/self/fd/{fd}");
            if (target is not null)
                identities.Add(target);
        }

        return identities;
    }

    /// <summary>
    /// Closes every descriptor above 2 that still points at one of <paramref name="stdioIdentity"/>
    /// — the launcher's redirect pipes, whatever fd number they landed on. Descriptors that
    /// vanish mid-scan (the enumeration's own, most often) read back null and are skipped.
    /// </summary>
    private static void CloseStdioAliases(HashSet<string> stdioIdentity)
    {
        if (!OperatingSystem.IsLinux() || stdioIdentity.Count == 0)
            return;

        string[] entries;
        try
        {
            // Snapshot before closing anything: the enumerator holds a descriptor of its own open
            // while it iterates, and closing descriptors underneath it is asking for trouble.
            entries = Directory.GetFileSystemEntries("/proc/self/fd");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (!int.TryParse(Path.GetFileName(entry), out var fd) || fd <= 2)
                continue;

            var target = ReadLinkOrNull($"/proc/self/fd/{fd}");
            if (target is not null && stdioIdentity.Contains(target))
                Close(fd);
        }
    }

    /// <summary>
    /// The raw <c>readlink(2)</c> target, or null when the link is gone or the target does not
    /// fit the buffer. Raw on purpose: <c>pipe:[n]</c> is not a resolvable path, and it is exactly
    /// the identity two descriptors onto the same pipe share.
    /// </summary>
    private static string? ReadLinkOrNull(string path)
    {
        var buffer = new byte[4096];
        var written = (int)ReadLink(path, buffer, buffer.Length);
        if (written <= 0 || written >= buffer.Length)
            return null;

        return Encoding.UTF8.GetString(buffer, 0, written);
    }

    [DllImport("libc", EntryPoint = "setsid", SetLastError = true)]
    private static extern int Setsid();

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string path, int flags);

    [DllImport("libc", EntryPoint = "dup2", SetLastError = true)]
    private static extern int Dup2(int oldfd, int newfd);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);

    [DllImport("libc", EntryPoint = "readlink", SetLastError = true)]
    private static extern nint ReadLink(string path, byte[] buf, nint bufsiz);
}
