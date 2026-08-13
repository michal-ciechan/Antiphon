using System.Diagnostics;
using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0043 — <c>logs/fake-gateway.log</c> reached 57 MB unrolled and <c>logs/session-runner.log</c>
/// had no retention either. Both are cmd.exe <c>&gt;&gt;</c> captures owned by run-daemon.ps1, whose
/// handle stays open for the whole service lifetime — so rotation can only happen at (re)launch, and
/// it has to happen there for every daemon the supervisor starts.
///
/// These run the real script against a throwaway "service" (a .cmd that stops itself) so the
/// rotation is exercised end to end rather than grepped for.
/// </summary>
public class DaemonLogRotationTests
{
    private static readonly TimeSpan SupervisorTimeout = TimeSpan.FromMinutes(2);

    [Test]
    public async Task An_oversized_capture_log_is_rolled_aside_before_the_service_starts()
    {
        using var temp = new TempDir();

        var logFile = Path.Combine(temp.Path, "svc.log");
        await File.WriteAllBytesAsync(logFile, new byte[3 * 1024 * 1024]);

        await RunSupervisorAsync(temp, logFile, logMaxMb: 1);

        var rolls = Rolls(temp);
        rolls.Length.ShouldBe(1, "the oversized log should have been rolled aside exactly once");
        // The supervisor's own "started" line lands before rotation runs, so the roll is >= what we wrote.
        new FileInfo(rolls[0]).Length.ShouldBeGreaterThanOrEqualTo(3 * 1024 * 1024);

        // The fresh log records where the history went, then carries the service's own output.
        var current = await File.ReadAllTextAsync(logFile);
        current.ShouldContain("Rotated log");
        current.ShouldContain("service-ran");
    }

    [Test]
    public async Task An_undersized_capture_log_is_left_alone()
    {
        using var temp = new TempDir();

        var logFile = Path.Combine(temp.Path, "svc.log");
        await File.WriteAllTextAsync(logFile, "keep-me\n");

        await RunSupervisorAsync(temp, logFile, logMaxMb: 20);

        Rolls(temp).ShouldBeEmpty();
        (await File.ReadAllTextAsync(logFile)).ShouldContain("keep-me");
    }

    [Test]
    public async Task Rolls_beyond_the_retained_count_are_pruned()
    {
        using var temp = new TempDir();

        var logFile = Path.Combine(temp.Path, "svc.log");
        await File.WriteAllBytesAsync(logFile, new byte[2 * 1024 * 1024]);

        // Three pre-existing rolls, oldest first; with a retain-count of 2 the rotation adds a
        // fourth and only the two newest may survive.
        var stamps = new[] { "20260810-010101", "20260811-010101", "20260812-010101" };
        for (var i = 0; i < stamps.Length; i++)
        {
            var roll = Path.Combine(temp.Path, $"svc.{stamps[i]}.log");
            await File.WriteAllTextAsync(roll, stamps[i]);
            File.SetLastWriteTime(roll, DateTime.Now.AddMinutes(-30 + i));
        }

        await RunSupervisorAsync(temp, logFile, logMaxMb: 1, logRetainCount: 2);

        var rolls = Rolls(temp).Select(Path.GetFileName).ToArray();
        rolls.Length.ShouldBe(2);
        // The newest of the three pre-existing ones plus the roll just taken; the oldest two go.
        rolls.ShouldContain("svc.20260812-010101.log");
        rolls.ShouldNotContain("svc.20260811-010101.log");
        rolls.ShouldNotContain("svc.20260810-010101.log");
    }

    [Test]
    public async Task Rolls_older_than_the_retention_window_are_pruned()
    {
        using var temp = new TempDir();

        var logFile = Path.Combine(temp.Path, "svc.log");
        await File.WriteAllBytesAsync(logFile, new byte[2 * 1024 * 1024]);

        var stale = Path.Combine(temp.Path, "svc.20260601-010101.log");
        await File.WriteAllTextAsync(stale, "stale");
        File.SetLastWriteTime(stale, DateTime.Now.AddDays(-6));

        var recent = Path.Combine(temp.Path, "svc.20260812-010101.log");
        await File.WriteAllTextAsync(recent, "recent");
        File.SetLastWriteTime(recent, DateTime.Now.AddDays(-1));

        await RunSupervisorAsync(temp, logFile, logMaxMb: 1, logRetainDays: 5);

        File.Exists(stale).ShouldBeFalse("a roll older than LogRetainDays must be pruned");
        File.Exists(recent).ShouldBeTrue("a roll inside the retention window must be kept");
    }

    /// <summary>Rolled captures only — <c>svc.&lt;yyyyMMdd-HHmmss&gt;.log</c>, never the live svc.log.</summary>
    private static string[] Rolls(TempDir temp) =>
        Directory.GetFiles(temp.Path)
            .Where(f => Regex.IsMatch(Path.GetFileName(f), @"^svc\.\d{8}-\d{6}\.log$"))
            .OrderBy(f => f)
            .ToArray();

    /// <summary>
    /// Runs run-daemon.ps1 through exactly one supervise iteration: the fake service writes a marker,
    /// then flips the desired-state file to 'stopped' so the restart loop exits instead of spinning.
    /// </summary>
    private static async Task RunSupervisorAsync(
        TempDir temp,
        string logFile,
        int logMaxMb,
        int logRetainDays = 5,
        int logRetainCount = 10)
    {
        var stateFile = Path.Combine(temp.Path, "svc.state");
        var pidFile = Path.Combine(temp.Path, "svc.service.pid");
        await File.WriteAllTextAsync(stateFile, "running");

        var service = Path.Combine(temp.Path, "svc.cmd");
        await File.WriteAllTextAsync(
            service,
            "@echo off\r\n" +
            "echo service-ran\r\n" +
            $"echo stopped> \"{stateFile}\"\r\n");

        var psi = new ProcessStartInfo
        {
            FileName = "pwsh.exe",
            WorkingDirectory = temp.Path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[]
                 {
                     "-NonInteractive", "-NoProfile", "-File", ScriptPath,
                     "-Name", "svc",
                     "-WorkDir", temp.Path,
                     "-Exe", service,
                     "-LogFile", logFile,
                     "-ServicePidFile", pidFile,
                     "-StateFile", stateFile,
                     "-LogMaxMb", logMaxMb.ToString(),
                     "-LogRetainDays", logRetainDays.ToString(),
                     "-LogRetainCount", logRetainCount.ToString(),
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        using var cts = new CancellationTokenSource(SupervisorTimeout);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start pwsh for run-daemon.ps1");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new TimeoutException(
                $"run-daemon.ps1 did not exit within {SupervisorTimeout}. It should stop after one "
                + "iteration once the fake service writes 'stopped' to the state file.");
        }

        process.ExitCode.ShouldBe(
            0,
            $"run-daemon.ps1 failed.{Environment.NewLine}{await stdout}{Environment.NewLine}{await stderr}");
    }

    private static string ScriptPath => Path.Combine(RepoRoot, "scripts", "run-daemon.ps1");

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;

            return dir?.FullName
                ?? throw new DirectoryNotFoundException(
                    "Could not locate repo root (Antiphon.sln) from test base dir.");
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"antiphon-logrot-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }
}
