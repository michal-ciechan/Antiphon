using System.Diagnostics;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TimeoutTests
{
    [Test]
    public async Task row_deadline_marks_timeout_without_global_driver_kill()
    {
        var driver = new FakeDriver();
        driver.When(_ => true, (_, token) => Task.Delay(Timeout.Infinite, token).ContinueWith(
            _ => new DriverResult(0, "", ""), TaskScheduler.Default));
        var result = await RowTimeout.RunWithDeadlineAsync(
            driver,
            new DriverRequest("dotnet", ["run"], CheckpointFixtures.TempDir()),
            TimeSpan.FromMilliseconds(50),
            CancellationToken.None);
        result.TimedOut.ShouldBeTrue();
        result.ExitCode.ShouldBe(5);
        driver.KillCount.ShouldBe(0);
    }

    [Test]
    public async Task total_deadline_kills_running_and_skips_queued()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new DriverResult(0, "", "");
        });
        var manifest = new CheckpointManifest();
        manifest.Builds.Add(new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" });
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-1", "bin-a"));
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-2", "bin-a"));
        var scheduler = new RunScheduler(driver, new FakePlatform());
        var result = await scheduler.RunAsync(new SchedulerRequest
        {
            Manifest = manifest,
            Rows = manifest.Checkpoints,
            RunDirectory = CheckpointFixtures.TempDir(),
            WorkingDirectory = CheckpointFixtures.TempDir(),
            State = new RunState { RunId = "t", StartedAt = DateTimeOffset.UtcNow },
            Slots = new FixedSlotClient("unavailable"),
            Commit = new string('b', 40),
            Width = 1,
            TotalTimeout = TimeSpan.FromMilliseconds(200),
        }, CancellationToken.None);
        result.Rows.Select(row => row.State).OrderBy(state => state).ShouldBe(["skipped", "timeout"]);
    }

    [Test]
    [Timeout(180_000)]
    public async Task windows_row_timeout_kills_the_breakaway_grandchild(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Windows Job Object kill covers a grandchild whose parent has exited.");
            return;
        }

        var marker = "C760W2" + Guid.NewGuid().ToString("N");
        try
        {
            var command = "start /b pwsh -NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 600 # " + marker + "\"";
            var result = await RowTimeout.RunWithDeadlineAsync(
                new ProcessDriver(),
                new DriverRequest("cmd.exe", ["/d", "/s", "/c", command], CheckpointFixtures.TempDir()),
                TimeSpan.FromMinutes(1),
                cancellationToken);
            result.TimedOut.ShouldBeTrue();
            PidsWithMarker(marker).ShouldBeEmpty();
        }
        finally
        {
            foreach (var pid in PidsWithMarker(marker))
            {
                try
                {
                    Process.GetProcessById(pid).Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
            }
        }
    }

    private static int[] PidsWithMarker(string marker)
    {
        var split = marker.Length / 2;
        var script = "$m='" + marker[..split] + "'+'" + marker[split..] + "'; "
            + "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like ('*'+$m+'*') -and $_.ProcessId -ne $PID } "
            + "| ForEach-Object { $_.ProcessId }";
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("powershell did not start");
        var text = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(20_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return [];
        }

        return text.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => int.TryParse(line.Trim(), out var pid) ? pid : 0)
            .Where(pid => pid > 0 && pid != Environment.ProcessId)
            .Distinct()
            .ToArray();
    }

    [Test]
    public void row_deadline_is_max_15_or_3x_estimate()
    {
        RowTimeout.DeriveRowMinutes(3, null, 15).ShouldBe(15);
        RowTimeout.DeriveRowMinutes(10, null, 15).ShouldBe(30);
        RowTimeout.DeriveRowMinutes(null, null, 15).ShouldBe(15);
        RowTimeout.DeriveRowMinutes(10, 7, 15).ShouldBe(7);
    }
}
