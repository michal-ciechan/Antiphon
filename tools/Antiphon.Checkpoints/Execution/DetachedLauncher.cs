using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Antiphon.Checkpoints;

public sealed record LaunchRequest(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

public sealed class DetachedLauncher
{
    private readonly IPlatform _platform;

    public DetachedLauncher(IPlatform platform) => _platform = platform;

    public int Start(LaunchRequest request)
    {
        if (_platform.IsWindows)
            return StartWindows(request);
        return StartLinux(request);
    }

    private static int StartLinux(LaunchRequest request)
    {
        var psi = new ProcessStartInfo("setsid")
        {
            UseShellExecute = false,
            WorkingDirectory = request.WorkingDirectory,
        };
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(request.FileName);
        foreach (var argument in request.Arguments)
            psi.ArgumentList.Add(argument);
        var process = Process.Start(psi) ?? throw new InvalidOperationException("setsid did not start");
        return process.Id;
    }

    private static int StartWindows(LaunchRequest request)
    {
        var command = new StringBuilder();
        command.Append(Quote(request.FileName));
        foreach (var argument in request.Arguments)
        {
            command.Append(' ');
            command.Append(Quote(argument));
        }

        var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        // DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP. CREATE_BREAKAWAY_FROM_JOB is intentionally absent
        // so the session kill-on-close job still owns the executor.
        const uint flags = 0x00000008 | 0x00000200;
        if (!CreateProcessW(null, command.ToString(), IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero,
                request.WorkingDirectory, ref startup, out var info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW failed");
        }

        CloseHandle(info.hThread);
        CloseHandle(info.hProcess);
        return info.dwProcessId;
    }

    private static string Quote(string value)
    {
        if (value.Length == 0)
            return "\"\"";
        if (value.IndexOfAny([' ', '\t', '"']) < 0)
            return value;
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
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

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
