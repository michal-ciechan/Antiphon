using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// Child processes and directory links for the runner gc witnesses.
/// Linux keeps /bin/sleep, /bin/true, /bin/sh and stat. Windows uses cmd.exe,
/// powershell.exe and a content hash, and a symlink refusal becomes a junction.
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
        if (Windows)
        {
            // ping -n N waits about N-1 seconds and does not need a POSIX sleep.
            var ping = Redirect("cmd.exe");
            ping.ArgumentList.Add("/c");
            ping.ArgumentList.Add("ping -n " + (seconds + 1).ToString(CultureInfo.InvariantCulture) + " 127.0.0.1 >NUL");
            return ping;
        }

        var psi = Redirect("/bin/sleep");
        psi.ArgumentList.Add(seconds.ToString(CultureInfo.InvariantCulture));
        return psi;
    }

    internal static ProcessStartInfo TrueStart()
    {
        if (Windows)
        {
            var psi = Redirect("cmd.exe");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add("exit 0");
            return psi;
        }

        return Redirect("/bin/true");
    }

    internal static ProcessStartInfo HolderStart(string lockPath)
    {
        if (Windows)
        {
            // Share read/write so the test can hash the bytes. FileShare.None from
            // another opener still fails while this handle is open.
            var psi = Redirect("powershell.exe");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(
                "$fs=[System.IO.File]::Open($env:ANTIPHON_LOCK,'OpenOrCreate','ReadWrite','ReadWrite'); Start-Sleep -Seconds 120");
            psi.Environment["ANTIPHON_LOCK"] = lockPath;
            return psi;
        }

        var holder = Redirect("/bin/sh");
        holder.ArgumentList.Add("-c");
        holder.ArgumentList.Add("exec 3>\"$1\"; exec sleep 120");
        holder.ArgumentList.Add("holder");
        holder.ArgumentList.Add(lockPath);
        return holder;
    }

    internal static void LinkDirectory(string link, string target,
        Action<string, string>? symlink = null, Action<string, string>? junction = null)
    {
        if (symlink is null)
            symlink = (linkPath, targetPath) => Directory.CreateSymbolicLink(linkPath, targetPath);
        if (!Windows)
        {
            symlink(link, target);
            return;
        }

        try
        {
            symlink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (junction is not null)
                junction(link, target);
            else
                CreateJunction(link, target);
        }
    }

    internal static string IdentityKind => Windows ? "content" : "stat";

    internal static string Identity(string path)
    {
        if (Windows)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes.Length.ToString(CultureInfo.InvariantCulture) + ":" + Convert.ToHexString(SHA256.HashData(bytes));
        }

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

    internal static bool ProcessGone(int pid)
    {
        if (pid <= 0)
            return false;
        if (!Windows)
            return !Directory.Exists("/proc/" + pid);
        try
        {
            using var found = Process.GetProcessById(pid);
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static void CreateJunction(string link, string target)
    {
        var psi = Redirect("cmd.exe");
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add("mklink /J \"" + link + "\" \"" + target + "\"");
        using var process = Start(psi);
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("mklink /J " + link + " -> " + stderr + stdout);
    }

    private static ProcessStartInfo Redirect(string fileName) => new(fileName)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    private static Process Start(ProcessStartInfo psi) =>
        Process.Start(psi) ?? throw new InvalidOperationException(psi.FileName + " did not start");
}
