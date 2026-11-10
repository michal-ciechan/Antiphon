using System.Diagnostics;
using System.Globalization;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// Child processes and directory links for the runner gc witnesses. The host OS is visible
/// and still ignored: every child is a POSIX tool, and a refused symlink is not replaced.
/// </summary>
internal static class MaintenanceFixture
{
    /// <summary>Null follows the host. A test sets this to force the other platform's tools.</summary>
    internal static bool? ForceWindows { get; set; }

    private static bool Windows => ForceWindows ?? OperatingSystem.IsWindows();

    internal static Process StartSleep(int seconds) => Start(SleepStart(seconds));

    internal static Process StartTrue() => Start(TrueStart());

    internal static Process StartHolder(string lockPath) => Start(HolderStart(lockPath));

    internal static ProcessStartInfo SleepStart(int seconds)
    {
        _ = Windows;
        var psi = Redirect("/bin/sleep");
        psi.ArgumentList.Add(seconds.ToString(CultureInfo.InvariantCulture));
        return psi;
    }

    internal static ProcessStartInfo TrueStart()
    {
        _ = Windows;
        return Redirect("/bin/true");
    }

    internal static ProcessStartInfo HolderStart(string lockPath)
    {
        _ = Windows;
        var psi = Redirect("/bin/sh");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("exec 3>\"$1\"; exec sleep 120");
        psi.ArgumentList.Add("holder");
        psi.ArgumentList.Add(lockPath);
        return psi;
    }

    internal static void LinkDirectory(string link, string target,
        Action<string, string>? symlink = null, Action<string, string>? junction = null)
    {
        _ = Windows;
        _ = junction;
        if (symlink is null)
            Directory.CreateSymbolicLink(link, target);
        else
            symlink(link, target);
    }

    internal static string IdentityKind
    {
        get
        {
            _ = Windows;
            return "stat";
        }
    }

    internal static string Identity(string path)
    {
        var psi = Redirect("stat");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("%d:%i:%s");
        psi.ArgumentList.Add(path);
        using var process = Start(psi);
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("stat " + path + " -> " + stderr);
        return stdout.Trim();
    }

    internal static bool ProcessGone(int pid) => pid > 0 && !Directory.Exists("/proc/" + pid);

    private static ProcessStartInfo Redirect(string fileName) => new(fileName)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    private static Process Start(ProcessStartInfo psi) =>
        Process.Start(psi) ?? throw new InvalidOperationException(psi.FileName + " did not start");
}
