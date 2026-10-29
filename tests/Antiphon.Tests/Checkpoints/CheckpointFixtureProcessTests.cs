using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Checkpoints;

// CARD-1199 review 6b8368dc F1: a CheckpointFixtureProcess child blocked before exit or holding a pipe after exit
// must fail its fixture step within the stated limit, not hang the test host.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointFixtureProcessTests : CheckpointTestBase
{
    [NativeLinuxOnly("FIFO config proof runs on Linux")]
    [Test]
    [Timeout(60_000)]
    public async Task fixture_git_blocked_opening_its_config_fails_within_the_limit(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("FIFO config proof runs on Linux");
        _ = cancellationToken.CanBeCanceled;

        var root = TempDir();
        // Ordinary control: installed Git through the same fixture path.
        CheckpointFixtureProcess.Git(root, "init", "-q", "-b", "card1199");

        var fifo = Path.Combine(root, "global.gitconfig");
        MkFifo(fifo, 0x180).ShouldBe(0, "mkfifo failed: " + Marshal.GetLastPInvokeError());
        var start = new ProcessStartInfo("git") { WorkingDirectory = root };
        start.Environment["GIT_CONFIG_GLOBAL"] = fifo;
        start.ArgumentList.Add("status");
        var clock = Stopwatch.StartNew();
        var call = Task.Run(() => CheckpointFixtureProcess.Run(start, 2_000));
        try
        {
            var finished = await Task.WhenAny(call, Task.Delay(8_000));
            (finished == call).ShouldBeTrue("fixture step still blocked after 8 s against a 2-s limit");
            var elapsed = clock.ElapsedMilliseconds;
            (await Thrown(call)).ShouldBeOfType<TimeoutException>();
            elapsed.ShouldBeLessThan(4_000);
        }
        finally
        {
            // An unbounded reader reopens the config; keep releasing until it finishes.
            var release = Stopwatch.StartNew();
            while (!call.IsCompleted && release.ElapsedMilliseconds < 5_000)
            {
                ReleaseFifo(fifo);
                await Task.WhenAny(call, Task.Delay(50));
            }
        }
    }

    [NativeLinuxOnly("process-group custody proof runs on Linux")]
    [Test]
    [Timeout(60_000)]
    public async Task fixture_child_whose_descendant_holds_a_pipe_fails_within_the_limit_and_is_killed(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("process-group custody proof runs on Linux");
        _ = cancellationToken.CanBeCanceled;

        var root = TempDir();
        var gate = Path.Combine(root, "gate");
        MkFifo(gate, 0x180).ShouldBe(0, "mkfifo failed: " + Marshal.GetLastPInvokeError());
        var pidfile = Path.Combine(root, "descendant.pid");

        // Healthy control: the same fixture path returns the child's output.
        var healthy = CheckpointFixtureProcess.Run(Shell("printf ok; exit 0"), 2_000);
        healthy.Exit.ShouldBe(0);
        healthy.Stdout.ShouldBe("ok");

        // The direct child exits at once; its descendant keeps stderr open.
        var held = Shell("/bin/cat " + Quote(gate) + " >/dev/null & printf '%s\\n' \"$!\" >> " + Quote(pidfile) + "; exit 0");
        var clock = Stopwatch.StartNew();
        var call = Task.Run(() => CheckpointFixtureProcess.Run(held, 2_000));
        try
        {
            var finished = await Task.WhenAny(call, Task.Delay(8_000));
            (finished == call).ShouldBeTrue("fixture step still blocked after 8 s against a 2-s limit");
            var elapsed = clock.ElapsedMilliseconds;
            (await Thrown(call)).ShouldBeOfType<TimeoutException>();
            elapsed.ShouldBeLessThan(4_000);
            var pids = RecordedPids(pidfile);
            pids.ShouldNotBeEmpty();
            foreach (var pid in pids)
                WaitDead(pid, 2_000).ShouldBeTrue("descendant " + pid + " survived the fixture limit");
        }
        finally
        {
            KillRecorded(pidfile);
            await Task.WhenAny(call, Task.Delay(5_000));
        }
    }

    // Shouldly rethrows its own assertion type, so the failure is recorded rather than asserted with ThrowAsync.
    private static async Task<Exception?> Thrown(Task call)
    {
        try { await call; return null; }
        catch (Exception ex) { return ex; }
    }

    private static ProcessStartInfo Shell(string script)
    {
        var start = new ProcessStartInfo("/bin/sh");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script);
        return start;
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    // Opens the write end only while a reader is blocked on it; never blocks itself.
    private static void ReleaseFifo(string path)
    {
        var fd = Open(path, 0x1 | 0x800);
        if (fd >= 0)
            Close(fd);
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
            var stat = File.ReadAllText("/proc/" + pid + "/stat", Encoding.ASCII);
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

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)] private static extern int MkFifo(string path, uint mode);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "close")] private static extern int Close(int fd);
}
