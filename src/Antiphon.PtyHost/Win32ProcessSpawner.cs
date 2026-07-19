using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Antiphon.PtyHost;

/// <summary>
/// Spawns a fully detached process via CreateProcessW so the host can outlive everything above it:
/// DETACHED_PROCESS (no console to share a fate with) + CREATE_BREAKAWAY_FROM_JOB (escape any
/// kill-on-close job object wrapping the caller, e.g. Aspire DCP). .NET's Process.Start cannot
/// set these creation flags, hence the P/Invoke.
/// </summary>
internal static class Win32ProcessSpawner
{
    private const uint DetachedProcess = 0x00000008;
    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CreateBreakawayFromJob = 0x01000000;
    private const int ErrorAccessDenied = 5;

    /// <summary>
    /// Starts <paramref name="exePath"/> detached, preferring job breakaway but falling back to a
    /// plain detached spawn when the surrounding job denies breakaway. Returns the new pid.
    /// </summary>
    public static int StartDetachedWithFallback(string exePath, IReadOnlyList<string> args)
    {
        try
        {
            return Start(exePath, args, DetachedProcess | CreateNewProcessGroup | CreateBreakawayFromJob);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorAccessDenied)
        {
            // Job exists but JOB_OBJECT_LIMIT_BREAKAWAY_OK is not set. The double-spawn still
            // breaks the parent chain; only a kill-on-close job on the whole tree remains a risk.
            return Start(exePath, args, DetachedProcess | CreateNewProcessGroup);
        }
    }

    private static int Start(string exePath, IReadOnlyList<string> args, uint creationFlags)
    {
        var commandLine = new StringBuilder();
        AppendQuoted(commandLine, exePath);
        foreach (var arg in args)
        {
            commandLine.Append(' ');
            AppendQuoted(commandLine, arg);
        }

        var startupInfo = new Startupinfo { cb = Marshal.SizeOf<Startupinfo>() };
        if (!CreateProcessW(
                exePath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                bInheritHandles: false,
                creationFlags,
                IntPtr.Zero,
                lpCurrentDirectory: null,
                ref startupInfo,
                out var processInfo))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateProcessW failed for {exePath}");
        }

        CloseHandle(processInfo.hThread);
        CloseHandle(processInfo.hProcess);
        return processInfo.dwProcessId;
    }

    /// <summary>Standard Windows argv quoting (backslash runs before quotes must be doubled).</summary>
    private static void AppendQuoted(StringBuilder builder, string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
        {
            builder.Append(arg);
            return;
        }

        builder.Append('"');
        var backslashes = 0;
        foreach (var c in arg)
        {
            switch (c)
            {
                case '\\':
                    backslashes++;
                    break;
                case '"':
                    builder.Append('\\', backslashes * 2 + 1).Append('"');
                    backslashes = 0;
                    break;
                default:
                    builder.Append('\\', backslashes);
                    backslashes = 0;
                    builder.Append(c);
                    break;
            }
        }

        builder.Append('\\', backslashes * 2).Append('"');
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Startupinfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref Startupinfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
