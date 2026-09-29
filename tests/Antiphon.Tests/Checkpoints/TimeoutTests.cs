using System.Diagnostics;
using System.Text;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TimeoutTests : CheckpointTestBase
{
    [Test]
    public async Task total_deadline_skips_a_queued_build_without_starting_it()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new DriverResult(0, "", "");
        });
        var manifest = new CheckpointManifest();
        manifest.Builds.Add(new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" });
        manifest.Builds.Add(new BuildSpec { Id = "bin-b", Project = "tests/Antiphon.Tests", OutputPath = "bin-b/" });
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-1", "bin-a"));
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-2", "bin-b"));
        var result = await new RunScheduler(driver, new FakePlatform()).RunAsync(new SchedulerRequest
        {
            Manifest = manifest,
            Rows = manifest.Checkpoints,
            RunDirectory = CheckpointFixtures.TempDir(),
            WorkingDirectory = CheckpointFixtures.TempDir(),
            State = new RunState { RunId = "t", StartedAt = DateTimeOffset.UtcNow },
            Slots = new FixedSlotClient("unavailable"),
            Width = 2,
            TotalTimeout = TimeSpan.FromMilliseconds(200),
        }, CancellationToken.None);
        driver.Count(CheckpointFixtures.IsBuild).ShouldBe(1);
        result.State.Builds.Single(build => build.Id == "bin-a").State.ShouldBe("failed");
        result.State.Builds.Single(build => build.Id == "bin-b").State.ShouldBe("skipped");
        result.Rows.All(row => row.State == "skipped").ShouldBeTrue();
        result.ExitCode.ShouldBe(5);
    }

    [Test]
    public async Task row_deadline_marks_timeout_without_global_driver_kill()
    {
        var driver = new FakeDriver();
        driver.When(_ => true, (_, token) => Task.Delay(Timeout.Infinite, token).ContinueWith(
            _ => new DriverResult(0, "", ""), TaskScheduler.Default));
        var result = await RowTimeout.RunWithDeadlineAsync(
            driver,
            new DriverRequest("dotnet", ["run"], TempDir()),
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
            RunDirectory = TempDir(),
            WorkingDirectory = TempDir(),
            State = new RunState { RunId = "t", StartedAt = DateTimeOffset.UtcNow },
            Slots = new FixedSlotClient("unavailable"),
            Commit = new string('b', 40),
            Width = 1,
            TotalTimeout = TimeSpan.FromMilliseconds(200),
        }, CancellationToken.None);
        result.Rows.Select(row => row.State).OrderBy(state => state).ShouldBe(["skipped", "timeout"]);
    }

    [Test]
    [Timeout(60_000)]
    public async Task windows_quick_row_finishes_beside_a_slow_row(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Concurrent Windows row processes must not inherit each other's stdout pipes.");
            return;
        }

        var driver = new ProcessDriver();
        var directory = TempDir();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var slow = Task.Run(() => driver.RunAsync(new DriverRequest("ping.exe", ["-n", "20", "127.0.0.1"], directory), stop.Token), CancellationToken.None);
        var quick = Enumerable.Range(0, 8).Select(_ => Task.Run(
            () => driver.RunAsync(new DriverRequest("cmd.exe", ["/d", "/c", "echo", "quick-ok"], directory), stop.Token),
            CancellationToken.None)).ToArray();
        try
        {
            var finished = await Task.WhenAll(quick).WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
            foreach (var result in finished)
            {
                result.ExitCode.ShouldBe(0, result.Stderr);
                result.Stdout.ShouldContain("quick-ok");
            }
        }
        finally
        {
            stop.Cancel();
            try { await Task.WhenAll(quick.Cast<Task>().Append(slow)).WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None); }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) { }
        }
    }

    [Test]
    [Timeout(90_000)]
    public async Task windows_row_arguments_round_trip_intact(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Windows row arguments are delivered by ProcessStartInfo.ArgumentList.");
            return;
        }

        var directory = TempDir();
        var exe = CompileEchoExe(directory);
        string[] expected = [@"C:\a b\", "second", "q\"x", "a\\\"b c", ""];
        var result = await new ProcessDriver().RunAsync(
            new DriverRequest(exe, expected, directory), cancellationToken);
        result.ExitCode.ShouldBe(0, result.Stderr);
        var lines = result.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldBe("count=5");
        for (var i = 0; i < expected.Length; i++)
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(expected[i]));
            lines[i + 1].ShouldBe(i + "=" + encoded, expected[i]);
        }
    }

    [Test]
    [Timeout(90_000)]
    public async Task windows_chatty_row_drains_interleaved_stdout_and_stderr(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Windows row pipes must drain a large interleaved stdout and stderr.");
            return;
        }

        var directory = TempDir();
        var exe = CompileEchoExe(directory);
        var result = await new ProcessDriver().RunAsync(
            new DriverRequest(exe, ["--chatter"], directory), cancellationToken);
        result.ExitCode.ShouldBe(0, result.Stderr);
        result.Stdout.Length.ShouldBeGreaterThan(9_000_000);
        result.Stderr.Length.ShouldBeGreaterThan(2_000_000);
    }

    [Test]
    [Timeout(180_000)]
    public async Task windows_row_timeout_kills_the_start_b_grandchild(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The descendant sweep kills a start /b grandchild on Windows.");
            return;
        }

        var marker = "C760W2" + Guid.NewGuid().ToString("N");
        try
        {
            var command = "start /b pwsh -NoProfile -NonInteractive -Command Start-Sleep -Seconds 600 # " + marker;
            var result = await RowTimeout.RunWithDeadlineAsync(
                new ProcessDriver(),
                new DriverRequest("cmd.exe", ["/d", "/c", command], TempDir()),
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

    private static string CompileEchoExe(string directory)
    {
        var source = Path.Combine(directory, "echo-args.cs");
        var exe = Path.Combine(directory, "echo-args.exe");
        // The lane census reads this file as text. Keep the helper's type keyword off a
        // "public class" line so it is not counted as an untagged test class.
        File.WriteAllText(source, """
            using System;
            public 
            """ + "class" + """
             EchoArgs {
              public static void Main(string[] args) {
                if (args.Length == 1 && args[0] == "--chatter") {
                  byte[] line = new byte[1024];
                  for (int i = 0; i < 1023; i++) line[i] = (byte)'x';
                  line[1023] = (byte)'\n';
                  byte[] err = new byte[1024];
                  for (int i = 0; i < 1023; i++) err[i] = (byte)'e';
                  err[1023] = (byte)'\n';
                  var stdout = Console.OpenStandardOutput();
                  var stderr = Console.OpenStandardError();
                  int outLeft = 10240;
                  int errLeft = 2560;
                  while (outLeft > 0 || errLeft > 0) {
                    if (outLeft > 0) { stdout.Write(line, 0, line.Length); outLeft--; }
                    if (errLeft > 0) { stderr.Write(err, 0, err.Length); errLeft--; }
                  }
                  stdout.Flush();
                  stderr.Flush();
                  return;
                }
                Console.WriteLine("count=" + args.Length);
                for (int i = 0; i < args.Length; i++) {
                  byte[] bytes = System.Text.Encoding.Unicode.GetBytes(args[i]);
                  Console.WriteLine(i.ToString() + "=" + Convert.ToBase64String(bytes));
                }
              }
            }
            """);
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = directory,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("Add-Type -TypeDefinition (Get-Content -Raw -LiteralPath '.\\echo-args.cs') -OutputAssembly '.\\echo-args.exe' -OutputType ConsoleApplication");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("powershell did not start");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("echo helper compile timed out");
        }

        var stderr = stderrTask.GetAwaiter().GetResult();
        stdoutTask.GetAwaiter().GetResult();
        process.ExitCode.ShouldBe(0, stderr);
        File.Exists(exe).ShouldBeTrue(stderr);
        return exe;
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
