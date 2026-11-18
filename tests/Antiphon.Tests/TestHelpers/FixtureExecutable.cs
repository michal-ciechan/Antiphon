using System.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1984 B-1: creates a file that a child will exec without this test process ever holding a
/// write handle on it. Every <see cref="Process.Start()"/> in the process forks a copy of each open
/// descriptor, and the copy lives until that child's exec. Linux refuses to exec a file that is
/// open for writing anywhere (ETXTBSY, "Text file busy"), so an in-process write followed by an
/// exec races every concurrent spawn in the assembly, directly or through a script that runs it.
/// A scratch probe at 8 writers beside 8 spawning threads measured 74 in 32,000 direct execs and
/// 1 in 32,000 through one bash, and 0 in 32,000 through this helper. The content goes to a staging
/// file that is never exec'd, and <c>/usr/bin/install</c> creates the file in its own process,
/// unlinking any existing file first. Linux only. On the wide lane
/// (<see cref="ScriptModelSpawnLimit"/>) every executable is created here;
/// <c>ScriptModelSpawnLaneTests.Wide_lane_methods_write_no_executable_in_process</c> holds the rule.
/// </summary>
internal static class FixtureExecutable
{
    /// <summary>Creates <paramref name="path"/> with <paramref name="content"/> (UTF-8, no BOM) and an octal mode.</summary>
    internal static void Write(string path, string content, string mode = "755")
    {
        var staging = Staging();
        File.WriteAllText(staging, content);
        try { Install(staging, path, mode); }
        finally { File.Delete(staging); }
    }

    internal static async Task WriteAsync(string path, string content, string mode = "755")
    {
        var staging = Staging();
        await File.WriteAllTextAsync(staging, content);
        try { await InstallAsync(staging, path, mode); }
        finally { File.Delete(staging); }
    }

    /// <summary>Creates <paramref name="path"/> as a copy of <paramref name="source"/> with an octal mode.</summary>
    internal static void Copy(string source, string path, string mode = "755") => Install(source, path, mode);

    private static string Staging() => Path.Combine(Path.GetTempPath(), "c1984-exec-" + Guid.NewGuid().ToString("N"));

    private static void Install(string source, string path, string mode)
    {
        using var child = Start(source, path, mode);
        var stderr = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(30_000))
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
            throw new TimeoutException("install did not create " + path + " within 30 s");
        }

        child.WaitForExit();
        Check(child, path, stderr.Result);
    }

    private static async Task InstallAsync(string source, string path, string mode)
    {
        using var child = Start(source, path, mode);
        var stderr = child.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await child.WaitForExitAsync(deadline.Token); }
        catch
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
            throw;
        }

        Check(child, path, await stderr);
    }

    private static Process Start(string source, string path, string mode)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("FixtureExecutable creates Linux executables through /usr/bin/install.");
        var psi = new ProcessStartInfo("/usr/bin/install") { UseShellExecute = false, RedirectStandardError = true };
        foreach (var arg in new[] { "-m", mode, "--", source, path })
            psi.ArgumentList.Add(arg);
        return Process.Start(psi) ?? throw new InvalidOperationException("install did not start");
    }

    private static void Check(Process child, string path, string stderr)
    {
        if (child.ExitCode != 0)
            throw new IOException("install exited " + child.ExitCode + " creating " + path + ": " + stderr.Trim());
    }
}
