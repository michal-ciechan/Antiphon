using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Porta.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0308: Kill must reap MCP-style grandchildren that hold the session cwd, not only the TUI.
/// Without terminating the spawn job (modern) / the process tree (inbox), a leftover child keeps
/// <c>.worktrees/*</c> open and <c>git worktree remove</c> leaves a dir whose gitdir later goes stale.
/// </summary>
[Category("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class PtyKillProcessTreeTests
{
    private static readonly Regex ChildPidPattern = new(@"CHILD_PID=(\d+)", RegexOptions.CultureInvariant);
    private static readonly TimeSpan KillBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SpawnBudget = TimeSpan.FromSeconds(15);

    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static void SkipIfNotWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
    }

    private static string RequireShippedDll()
    {
        SkipIfNotWindows();
        if (!ConPtyRedistributable.TryLocate(out var dll, out var why))
            throw new SkipTestException("no shipped conpty.dll: " + why);
        return dll!;
    }

    [Test]
    public async Task Modern_Kill_reaps_grandchild_and_releases_held_directory()
    {
        var dll = RequireShippedDll();
        await using var held = new HeldDirectory();
        using var connection = ModernConPtyConnection.Spawn(dll, PtyOptionsFor(held.Path));
        using var pump = Pump(connection);

        var childPid = await WaitForChildPidAsync(() => pump.Text, SpawnBudget);
        held.Track(connection.Pid, childPid);

        IsAlive(connection.Pid).ShouldBeTrue("pty child must still be running");
        IsAlive(childPid).ShouldBeTrue("grandchild must still be running");
        await WaitUntilAsync(
            () => held.LockIsHeld(),
            SpawnBudget,
            "grandchild did not take an exclusive lock on the held directory");
        held.TryDelete().ShouldBeFalse("grandchild still holds the directory; delete must fail before Kill");

        connection.Kill();

        await WaitUntilAsync(() => !IsAlive(connection.Pid), KillBudget, $"pty pid {connection.Pid} still alive after Kill");
        await WaitUntilAsync(() => !IsAlive(childPid), KillBudget, $"grandchild pid {childPid} still alive after Kill");
        await WaitUntilAsync(() => held.TryDelete(), KillBudget, "held directory still locked after Kill");
    }

    [Test]
    public async Task Modern_runner_KillAsync_reaps_grandchild_and_releases_held_directory()
    {
        RequireShippedDll();
        await using var held = new HeldDirectory();
        await using var runner = new PtyAgentRunner("modern");
        // memoryLimitMb > 0 so KillAsync's nested-job TryTerminate is not a dead line.
        await runner.StartAsync(
            "pwsh.exe",
            ["-NoProfile", "-Command", GrandchildHoldScript(held.Path)],
            cwd: AppContext.BaseDirectory,
            memoryLimitMb: 256);

        runner.Backend!.Backend.ShouldBe(PtyBackend.ModernConPty, "a silent fallback would make this test prove nothing");
        runner.Pid.ShouldNotBeNull();
        var childPid = await WaitForChildPidAsync(runner.SnapshotText, SpawnBudget);
        held.Track(runner.Pid.Value, childPid);

        IsAlive(runner.Pid.Value).ShouldBeTrue();
        IsAlive(childPid).ShouldBeTrue();
        await WaitUntilAsync(() => held.LockIsHeld(), SpawnBudget, "grandchild did not take an exclusive lock");
        held.TryDelete().ShouldBeFalse();

        var killed = await runner.KillAsync(KillBudget);
        killed.ShouldBeTrue("KillAsync must observe the top-level exit within 2 s");

        await WaitUntilAsync(() => !IsAlive(childPid), KillBudget, $"grandchild pid {childPid} still alive after KillAsync");
        await WaitUntilAsync(() => held.TryDelete(), KillBudget, "held directory still locked after KillAsync");
    }

    [Test]
    public async Task Inbox_runner_KillAsync_reaps_grandchild_and_releases_held_directory()
    {
        SkipIfNotWindows();
        await using var held = new HeldDirectory();
        await using var runner = new PtyAgentRunner("inbox");
        await runner.StartAsync(
            "pwsh.exe",
            ["-NoProfile", "-Command", GrandchildHoldScript(held.Path)],
            cwd: AppContext.BaseDirectory);

        runner.Backend!.Backend.ShouldBe(PtyBackend.InboxConhost);
        runner.Pid.ShouldNotBeNull();
        var childPid = await WaitForChildPidAsync(runner.SnapshotText, SpawnBudget);
        held.Track(runner.Pid.Value, childPid);

        IsAlive(childPid).ShouldBeTrue();
        await WaitUntilAsync(() => held.LockIsHeld(), SpawnBudget, "grandchild did not take an exclusive lock");
        held.TryDelete().ShouldBeFalse();

        var killed = await runner.KillAsync(KillBudget);
        killed.ShouldBeTrue("inbox KillAsync must observe the top-level exit within 2 s");

        await WaitUntilAsync(() => !IsAlive(childPid), KillBudget, $"grandchild pid {childPid} still alive after inbox KillAsync");
        await WaitUntilAsync(() => held.TryDelete(), KillBudget, "held directory still locked after inbox KillAsync");
    }

    [Test]
    public async Task Modern_Kill_on_already_exited_child_does_not_throw()
    {
        var dll = RequireShippedDll();
        using var connection = ModernConPtyConnection.Spawn(dll, new PtyOptions
        {
            Name = "antiphon-pty",
            Cols = 80,
            Rows = 24,
            Cwd = AppContext.BaseDirectory,
            App = Cmd,
            CommandLine = ["/d", "/c", "exit 0"],
            Environment = new Dictionary<string, string>(),
        });
        using var pump = Pump(connection);

        await WaitUntilAsync(() => !IsAlive(connection.Pid), SpawnBudget, "cmd /c exit 0 did not exit");
        Should.NotThrow(() => connection.Kill());
        await Task.CompletedTask;
    }

    [Test]
    public async Task Inbox_KillAsync_on_already_exited_child_does_not_throw()
    {
        SkipIfNotWindows();
        await using var runner = new PtyAgentRunner("inbox");
        await runner.StartAsync(Cmd, ["/d", "/c", "exit 0"]);
        await runner.Exited.WaitAsync(SpawnBudget);
        await Should.NotThrowAsync(() => runner.KillAsync(KillBudget));
    }

    private static PtyOptions PtyOptionsFor(string held) => new()
    {
        Name = "antiphon-pty",
        Cols = 80,
        Rows = 24,
        Cwd = AppContext.BaseDirectory,
        App = "pwsh.exe",
        CommandLine = ["-NoProfile", "-Command", GrandchildHoldScript(held)],
        Environment = new Dictionary<string, string>(),
    };

    /// <summary>
    /// Pty child starts a 5.1 powershell grandchild (never MSIX, so it stays in the job) that
    /// opens <c>lock.bin</c> exclusive and sleeps. UseShellExecute is false via -NoNewWindow.
    /// </summary>
    private static string GrandchildHoldScript(string held)
    {
        var heldLiteral = held.Replace("'", "''", StringComparison.Ordinal);
        return $$"""
            $ErrorActionPreference = 'Stop'
            $held = '{{heldLiteral}}'
            $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
            $inner = "`$fs = [IO.File]::Open((Join-Path '{{heldLiteral}}' 'lock.bin'), 'OpenOrCreate', 'ReadWrite', 'None'); Start-Sleep -Seconds 600"
            $p = Start-Process -FilePath $ps -ArgumentList @('-NoProfile','-NonInteractive','-Command', $inner) -WorkingDirectory $held -PassThru -NoNewWindow
            if ($null -eq $p) { throw 'Start-Process returned null' }
            Write-Output ("CHILD_PID=" + $p.Id)
            Wait-Process -Id $p.Id
            """;
    }

    private static async Task<int> WaitForChildPidAsync(Func<string> text, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Match? match = null;
        string last = "";
        while (DateTime.UtcNow < deadline)
        {
            last = text();
            match = ChildPidPattern.Match(last);
            if (match.Success)
                return int.Parse(match.Groups[1].Value);
            await Task.Delay(50);
        }

        throw new ShouldAssertException(
            "expected CHILD_PID=<n> from the pty child within " + timeout + ". Output: " + Truncate(last));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string because)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }

        throw new ShouldAssertException(because + " (waited " + timeout + ")");
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TryKill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static string Truncate(string text) =>
        text.Length <= 800 ? text : text[..800] + "…";

    private static PumpSession Pump(ModernConPtyConnection connection) => new(connection);

    private sealed class PumpSession : IDisposable
    {
        private readonly StringBuilder _output = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;

        public PumpSession(ModernConPtyConnection connection)
        {
            _pump = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                try
                {
                    int read;
                    while ((read = await connection.ReaderStream.ReadAsync(buffer, _cts.Token)) > 0)
                    {
                        lock (_output)
                            _output.Append(Encoding.UTF8.GetString(buffer, 0, read));
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            });
        }

        public string Text
        {
            get
            {
                lock (_output)
                    return _output.ToString();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _ = Task.WhenAny(_pump, Task.Delay(TimeSpan.FromSeconds(1)));
            _cts.Dispose();
        }
    }

    private sealed class HeldDirectory : IAsyncDisposable
    {
        public string Root { get; }
        public string Path { get; }
        public string LockFile { get; }
        private int _ptyPid;
        private int _childPid;

        public HeldDirectory()
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "card0308-" + Guid.NewGuid().ToString("N"));
            Path = System.IO.Path.Combine(Root, "held");
            LockFile = System.IO.Path.Combine(Path, "lock.bin");
            Directory.CreateDirectory(Path);
        }

        public void Track(int ptyPid, int childPid)
        {
            _ptyPid = ptyPid;
            _childPid = childPid;
        }

        public bool LockIsHeld()
        {
            if (!File.Exists(LockFile))
                return false;
            try
            {
                using var stream = new FileStream(LockFile, FileMode.Open, FileAccess.Read, FileShare.None);
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        public bool TryDelete()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
                return !Directory.Exists(Path);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_childPid > 0) TryKill(_childPid);
            if (_ptyPid > 0) TryKill(_ptyPid);
            await Task.Delay(50);
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
