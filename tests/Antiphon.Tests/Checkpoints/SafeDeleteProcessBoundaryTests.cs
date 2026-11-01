using System.Diagnostics;
using System.Text;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Checkpoints;

// CARD-1199 F1: the cleanup/write preflight Git reader uses the shared bounded runner.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class SafeDeleteProcessBoundaryTests : CheckpointTestBase
{
    [NativeLinuxOnly("FIFO-held monitor proof runs on Linux")]
    [Test]
    [Timeout(60_000)]
    public void configured_fsmonitor_hook_is_not_run_by_the_preflight_reader(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("FIFO-held monitor proof runs on Linux");
        _ = cancellationToken.CanBeCanceled;

        var root = TempDir();
        Run(root, "git", "init", "-q", "-b", "card1199");
        Run(root, "mkfifo", "gate");
        File.WriteAllText(Path.Combine(root, "tracked.txt"), "control");
        Run(root, "git", "add", "tracked.txt");
        var owned = Path.Combine(root, "bin-owned");
        Directory.CreateDirectory(owned);

        // Healthy installed-Git controls before the monitor exists.
        SafeDelete.PreflightRoots(root, [Path.Combine(root, "tracked.txt")], timeoutMs: 3_000).ShouldBe("tracked");
        SafeDelete.PreflightRoots(root, [owned], timeoutMs: 3_000).ShouldBeNull();

        var marker = Path.Combine(root, "monitor.ran");
        var pidfile = Path.Combine(root, "descendant.pid");
        // The monitor leaves a FIFO-blocked descendant holding Git's inherited stderr.
        WriteScript(Path.Combine(root, "monitor.sh"),
            "#!/bin/sh\n: > " + Quote(marker) + "\n/bin/cat " + Quote(Path.Combine(root, "gate")) + " >/dev/null &\n" +
            "printf '%s\\n' \"$!\" >> " + Quote(pidfile) + "\nprintf 'token\\0'\nexit 0\n");
        Run(root, "git", "config", "core.fsmonitor", Path.Combine(root, "monitor.sh"));
        try
        {
            var started = Stopwatch.StartNew();
            var preflight = SafeDelete.PreflightRoots(root, [owned], timeoutMs: 3_000);
            var preflightMs = started.ElapsedMilliseconds;
            var tracked = SafeDelete.PreflightRoots(root, [Path.Combine(root, "tracked.txt")], timeoutMs: 3_000);

            File.Exists(marker).ShouldBeFalse("the configured filesystem monitor ran");
            File.Exists(pidfile).ShouldBeFalse("the configured filesystem monitor left a descendant");
            preflight.ShouldBeNull();
            tracked.ShouldBe("tracked");
            preflightMs.ShouldBeLessThan(3_000);
        }
        finally
        {
            KillRecorded(pidfile);
        }
    }

    [NativeLinuxOnly("process-group custody proof runs on Linux")]
    [Test]
    [Timeout(60_000)]
    public void exited_git_parent_with_pipe_holding_descendant_meets_one_deadline_and_is_killed(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("process-group custody proof runs on Linux");
        _ = cancellationToken.CanBeCanceled;

        var root = TempDir();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Run(root, "mkfifo", "gate");
        var owned = Path.Combine(root, "bin-owned");
        Directory.CreateDirectory(owned);
        var pidfile = Path.Combine(root, "descendant.pid");
        var healthy = Path.Combine(root, "healthy-git");
        var held = Path.Combine(root, "held-git");
        // Config-inspection calls answer "nothing configured"; only the reader's own call is shaped.
        const string configProbe = "case \"$*\" in *--get-regexp*) exit 1;; esac\n";
        WriteScript(healthy, "#!/bin/sh\n" + configProbe + "exit 0\n");
        // The direct child exits after 1.5 s; its descendant keeps stdout and stderr open.
        WriteScript(held, "#!/bin/sh\n" + configProbe + "/bin/cat " + Quote(Path.Combine(root, "gate")) + " >/dev/null &\n" +
            "printf '%s\\n' \"$!\" >> " + Quote(pidfile) + "\nsleep 1.5\nexit 0\n");
        try
        {
            SafeDelete.PreflightRoots(root, [owned], healthy, 2_500).ShouldBeNull();

            var started = Stopwatch.StartNew();
            var result = SafeDelete.PreflightRoots(root, [owned], held, 2_500);
            var elapsed = started.ElapsedMilliseconds;

            result.ShouldBe("git-timeout");
            // One deadline for exit and drain: the old reader waited 1.5 s plus a fresh 2.5 s drain.
            elapsed.ShouldBeLessThan(3_500);
            var pids = RecordedPids(pidfile);
            pids.ShouldNotBeEmpty();
            foreach (var pid in pids)
                WaitDead(pid, 2_000).ShouldBeTrue("descendant " + pid + " survived the preflight reader");
        }
        finally
        {
            KillRecorded(pidfile);
        }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static void WriteScript(string path, string text)
    {
        // Write and close before any exec: an open writer makes exec fail with ETXTBSY.
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(Encoding.ASCII.GetBytes(text));
            stream.Flush(true);
        }
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    // Every fixture step fails within twelve seconds (ten for exit, two for the drains); the kill reaches the live tree only.
    private static void Run(string root, string file, params string[] args)
    {
        var start = new ProcessStartInfo(file)
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        start.Environment.Remove("GIT_DIR");
        start.Environment.Remove("GIT_WORK_TREE");
        start.Environment.Remove("GIT_INDEX_FILE");
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(2_000);
            throw new TimeoutException(file + " fixture step exceeded ten seconds");
        }
        Task.WhenAll(stdout, stderr).Wait(2_000).ShouldBeTrue(file + " fixture output did not drain");
        process.ExitCode.ShouldBe(0, file + " " + string.Join(' ', args) + ": " + stdout.Result + stderr.Result);
    }

    private static List<int> RecordedPids(string pidfile)
    {
        var pids = new List<int>();
        if (!File.Exists(pidfile))
            return pids;
        foreach (var line in File.ReadAllLines(pidfile))
            if (int.TryParse(line.Trim(), out var pid))
                pids.Add(pid);
        return pids;
    }

    // A zombie awaiting its reaper is dead; any other state is a live descendant.
    private static bool Alive(int pid)
    {
        try
        {
            var stat = File.ReadAllText("/proc/" + pid + "/stat");
            return stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[0] != "Z";
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            return false;
        }
    }

    private static bool WaitDead(int pid, int timeoutMs)
    {
        var clock = Stopwatch.StartNew();
        while (Alive(pid))
        {
            if (clock.ElapsedMilliseconds > timeoutMs)
                return false;
            Thread.Sleep(50);
        }

        return true;
    }

    private static void KillRecorded(string pidfile)
    {
        foreach (var pid in RecordedPids(pidfile))
        {
            if (!Alive(pid))
                continue;
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
                process.WaitForExit(2_000);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }
    }
}
