using System.Diagnostics;
using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.AgentTui;

[NotInParallel("RunnerProcessProbe")]
public sealed class RunnerProcessProbeTests
{
    [Test]
    public async Task Probe_preserves_argument_boundaries_without_shell_interpretation()
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var canaryPath = Path.Combine(scratch, "shell-canary.txt");
            var arguments = new[]
            {
                "plain value",
                $"; [IO.File]::WriteAllText('{canaryPath}', 'pwned')",
                "$(Write-Output should-not-run)",
                "& whoami"
            };
            var probe = CreateProbe(maxOutputBytes: 4096);

            var result = await probe.RunAsync(
                Request(script, ["arguments", canaryPath, .. arguments]),
                CancellationToken.None);

            result.ExitCode.ShouldBe(0);
            result.TimedOut.ShouldBeFalse();
            File.Exists(canaryPath).ShouldBeFalse();
            result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => Encoding.UTF8.GetString(Convert.FromBase64String(line)))
                .ShouldBe(arguments);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Probe_caps_combined_stdout_and_stderr_and_signals_truncation()
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var probe = CreateProbe(maxOutputBytes: 1024);

            var result = await probe.RunAsync(
                Request(script, ["oversized"]),
                CancellationToken.None);

            result.OutputTruncated.ShouldBeTrue();
            (Encoding.UTF8.GetByteCount(result.StandardOutput)
             + Encoding.UTF8.GetByteCount(result.StandardError)).ShouldBeLessThanOrEqualTo(1024);
            result.ExitCode.ShouldBe(0);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Probe_timeout_kills_the_entire_process_tree()
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "timeout-pids.txt");
            var reaper = new RunnerProcessReaper();
            var probe = CreateProbe(reaper, timeoutSeconds: 1);

            var result = await probe.RunAsync(
                Request(script, ["tree", pidPath]),
                CancellationToken.None);

            result.TimedOut.ShouldBeTrue();
            result.Cancelled.ShouldBeFalse();
            result.CleanupConfirmed.ShouldBeTrue();
            var pids = await ReadPidsAsync(pidPath);
            foreach (var pid in pids)
                await AssertProcessExitedAsync(pid);
            await reaper.WaitForEmptyAsync(TimeSpan.FromSeconds(5));
            reaper.TrackedProcessCount.ShouldBe(0);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Probe_cancellation_kills_the_tree_and_redacts_secret_canaries()
    {
        const string secretCanary = "synthetic-probe-secret-canary";
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "cancel-pids.txt");
            var reaper = new RunnerProcessReaper();
            var probe = CreateProbe(reaper, timeoutSeconds: 10);
            using var cancellation = new CancellationTokenSource();
            var request = Request(script, ["tree-with-secret", pidPath, secretCanary]) with
            {
                SecretValues = [secretCanary]
            };

            var operation = probe.RunAsync(request, cancellation.Token);
            await WaitForFileAsync(pidPath);
            cancellation.Cancel();
            var result = await operation;

            result.Cancelled.ShouldBeTrue();
            result.TimedOut.ShouldBeFalse();
            result.CleanupConfirmed.ShouldBeTrue();
            result.StandardOutput.ShouldNotContain(secretCanary);
            result.StandardError.ShouldNotContain(secretCanary);
            result.SensitiveOutputDetected.ShouldBeTrue();
            var pids = await ReadPidsAsync(pidPath);
            foreach (var pid in pids)
                await AssertProcessExitedAsync(pid);
            await reaper.WaitForEmptyAsync(TimeSpan.FromSeconds(5));
            reaper.TrackedProcessCount.ShouldBe(0);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Unconfirmed_primary_cleanup_is_owned_and_completed_by_the_reaper()
    {
        var scratch = CreateScratch();
        var reaperEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReaper = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "reaper-pids.txt");
            var reaper = new RunnerProcessReaper(async (process, cancellationToken) =>
            {
                reaperEntered.TrySetResult();
                await releaseReaper.Task.WaitAsync(cancellationToken);
                return await RunnerProcessCleanup.StopTreeAsync(process, cancellationToken);
            });
            var probe = CreateProbe(
                reaper,
                timeoutSeconds: 1,
                seams: new RunnerProcessProbeSeams
                {
                    StopTreeAsync = (_, _) => Task.FromResult(false)
                });

            var result = await probe.RunAsync(
                Request(script, ["tree", pidPath]),
                CancellationToken.None);

            result.TimedOut.ShouldBeTrue();
            result.CleanupConfirmed.ShouldBeFalse();
            result.Error.ShouldBe(
                "The probe process cleanup could not be confirmed; background cleanup is continuing.");
            await reaperEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            reaper.TrackedProcessCount.ShouldBe(1);

            releaseReaper.TrySetResult();
            await reaper.WaitForEmptyAsync(TimeSpan.FromSeconds(5));
            reaper.TrackedProcessCount.ShouldBe(0);
            var pids = await ReadPidsAsync(pidPath);
            foreach (var pid in pids)
                await AssertProcessExitedAsync(pid);
        }
        finally
        {
            releaseReaper.TrySetResult();
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Reaper_host_stop_kills_every_already_started_tracked_process()
    {
        var scratch = CreateScratch();
        var reaperEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReaper = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "host-stop-pids.txt");
            var reaper = new RunnerProcessReaper(async (_, cancellationToken) =>
            {
                reaperEntered.TrySetResult();
                await releaseReaper.Task.WaitAsync(cancellationToken);
                return false;
            });
            var probe = CreateProbe(
                reaper,
                timeoutSeconds: 1,
                seams: new RunnerProcessProbeSeams
                {
                    StopTreeAsync = (_, _) => Task.FromResult(false)
                });

            var result = await probe.RunAsync(
                Request(script, ["tree", pidPath]),
                CancellationToken.None);
            result.CleanupConfirmed.ShouldBeFalse();
            await reaperEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var stopping = reaper.StopAsync(shutdown.Token);
            var pids = await ReadPidsAsync(pidPath);
            foreach (var pid in pids)
                await AssertProcessExitedAsync(pid);
            await Task.Delay(100);
            stopping.IsCompleted.ShouldBeFalse();

            releaseReaper.TrySetResult();
            await stopping;
            await reaper.WaitForEmptyAsync(TimeSpan.FromSeconds(5));
            reaper.TrackedProcessCount.ShouldBe(0);
        }
        finally
        {
            releaseReaper.TrySetResult();
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Reaper_shutdown_waits_for_a_starting_guard_then_kills_and_rejects_future_starts()
    {
        var scratch = CreateScratch();
        var startEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startCalls = 0;
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "shutdown-late-start-pids.txt");
            var reaper = new RunnerProcessReaper();
            var probe = CreateProbe(
                reaper,
                timeoutSeconds: 1,
                seams: new RunnerProcessProbeSeams
                {
                    StartProcessAsync = async (guard, _) => await Task.Run(() =>
                    {
                        var started = guard.TryStart();
                        if (started)
                            startedPid.TrySetResult(guard.Process.Id);
                        return started;
                    }),
                    StartCommitted = () =>
                    {
                        Interlocked.Increment(ref startCalls);
                        startEntered.TrySetResult();
                        releaseStart.Task.GetAwaiter().GetResult();
                    }
                });

            var operation = probe.RunAsync(
                Request(script, ["tree", pidPath]),
                CancellationToken.None);
            await startEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var stopping = reaper.StopAsync(shutdown.Token);
            stopping.IsCompleted.ShouldBeFalse();

            releaseStart.TrySetResult();
            var startedProcessId = await startedPid.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await AssertProcessExitedAsync(startedProcessId);
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            await stopping;
            reaper.TrackedProcessCount.ShouldBe(0);
            if (File.Exists(pidPath))
            {
                foreach (var pid in await ReadPidsAsync(pidPath))
                    await AssertProcessExitedAsync(pid);
            }

            var rejected = await probe.RunAsync(
                Request(script, ["credential", "unused"]),
                CancellationToken.None);
            rejected.Started.ShouldBeFalse();
            rejected.Error.ShouldBe(
                "The runner process probe is unavailable because the host is shutting down.");
            startCalls.ShouldBe(1);
        }
        finally
        {
            releaseStart.TrySetResult();
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Blocking_path_inspection_observes_the_one_second_deadline()
    {
        var probe = CreateProbe(
            new RunnerProcessReaper(),
            timeoutSeconds: 1,
            seams: new RunnerProcessProbeSeams
            {
                CheckExecutableAsync = async (_, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return new RunnerPathCheck(true, "Unexpected completion.");
                }
            });

        var elapsed = Stopwatch.StartNew();
        var result = await probe.CheckExecutableAsync("synthetic-runner", CancellationToken.None);
        elapsed.Stop();

        result.IsAvailable.ShouldBeFalse();
        result.Message.ShouldContain("deadline", Case.Insensitive);
        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Late_process_start_after_the_one_second_deadline_is_reaped()
    {
        var scratch = CreateScratch();
        var startEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "late-start-pids.txt");
            var reaper = new RunnerProcessReaper();
            var probe = CreateProbe(
                reaper,
                timeoutSeconds: 1,
                seams: new RunnerProcessProbeSeams
                {
                    StartProcessAsync = async (process, _) =>
                    {
                        startEntered.TrySetResult();
                        await releaseStart.Task;
                        var started = process.TryStart();
                        await WaitForFileAsync(pidPath);
                        return started;
                    }
                });

            var operation = probe.RunAsync(
                Request(script, ["tree", pidPath]),
                CancellationToken.None);
            await startEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var elapsed = Stopwatch.StartNew();
            var result = await operation;
            elapsed.Stop();

            result.TimedOut.ShouldBeTrue();
            result.Started.ShouldBeFalse();
            result.CleanupConfirmed.ShouldBeFalse();
            elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
            reaper.TrackedProcessCount.ShouldBe(1);

            releaseStart.TrySetResult();
            await reaper.WaitForEmptyAsync(TimeSpan.FromSeconds(5));
            var pids = await ReadPidsAsync(pidPath);
            foreach (var pid in pids)
                await AssertProcessExitedAsync(pid);
            reaper.TrackedProcessCount.ShouldBe(0);
        }
        finally
        {
            releaseStart.TrySetResult();
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Bounded_startup_that_exits_on_stdin_close_is_clean()
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "clean-stop-pid.txt");
            var probe = CreateProbe(timeoutSeconds: 5);
            var request = Request(script, ["stdin-close", pidPath]) with
            {
                StopAfter = TimeSpan.FromMilliseconds(250)
            };

            var result = await probe.RunAsync(request, CancellationToken.None);

            result.TimedOut.ShouldBeFalse();
            result.Cancelled.ShouldBeFalse();
            result.CleanlyStopped.ShouldBeTrue();
            result.CleanupConfirmed.ShouldBeTrue();
            result.Error.ShouldBeNull();
            result.ExitCode.ShouldBe(0);
            await AssertProcessExitedAsync((await ReadPidsAsync(pidPath)).Single());
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Probe_redacts_credential_shaped_output_without_persisting_raw_values()
    {
        const string canary = "credential-shaped-canary";
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var probe = CreateProbe(maxOutputBytes: 4096);

            var result = await probe.RunAsync(
                Request(script, ["credential", canary]),
                CancellationToken.None);

            result.SensitiveOutputDetected.ShouldBeTrue();
            result.StandardOutput.ShouldNotContain(canary);
            result.StandardError.ShouldNotContain(canary);
            result.StandardOutput.ShouldNotContain("API_TOKEN", Case.Insensitive);
            result.StandardError.ShouldNotContain("password", Case.Insensitive);
            result.StandardOutput.ShouldNotContain("OPENAI_API_KEY", Case.Insensitive);
            result.StandardError.ShouldNotContain("SERVICE_TOKEN", Case.Insensitive);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Bounded_startup_reports_forced_tree_termination_as_not_clean()
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "forced-stop-pids.txt");
            var probe = CreateProbe(timeoutSeconds: 5);
            var request = Request(script, ["tree", pidPath]) with
            {
                StopAfter = TimeSpan.FromSeconds(1)
            };

            var result = await probe.RunAsync(request, CancellationToken.None);

            result.TimedOut.ShouldBeFalse();
            result.Cancelled.ShouldBeFalse();
            result.CleanlyStopped.ShouldBeFalse();
            result.CleanupConfirmed.ShouldBeTrue();
            result.Error.ShouldBe("The probe process required forced cleanup.");
            var pids = await ReadPidsAsync(pidPath);
            foreach (var pid in pids)
                await AssertProcessExitedAsync(pid);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static RunnerProcessProbe CreateProbe(
        int timeoutSeconds = 5,
        int maxOutputBytes = 64 * 1024) =>
        new(Options.Create(new AgentTuiSettings
        {
            ProbeTimeoutSeconds = timeoutSeconds,
            MaxProbeOutputBytes = maxOutputBytes
        }));

    private static RunnerProcessProbe CreateProbe(
        RunnerProcessReaper reaper,
        int timeoutSeconds = 5,
        int maxOutputBytes = 64 * 1024,
        RunnerProcessProbeSeams? seams = null) =>
        new(
            Options.Create(new AgentTuiSettings
            {
                ProbeTimeoutSeconds = timeoutSeconds,
                MaxProbeOutputBytes = maxOutputBytes
            }),
            reaper,
            seams);

    private static RunnerProcessRequest Request(string script, IReadOnlyList<string> helperArguments) =>
        new(
            PowerShellExecutable(),
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, .. helperArguments],
            Path.GetDirectoryName(script),
            new Dictionary<string, string> { ["PROBE_ORDINARY"] = "ordinary" },
            SecretValues: []);

    private static string PowerShellExecutable()
    {
        var alias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            "pwsh.exe");
        return File.Exists(alias) ? alias : "pwsh";
    }

    private static string CreateScratch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"antiphon-runner-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteHelper(string scratch)
    {
        var path = Path.Combine(scratch, "probe-helper.ps1");
        File.WriteAllText(path, """
            $ErrorActionPreference = 'Stop'
            $mode = $args[0]
            if ($mode -eq 'arguments') {
                for ($i = 2; $i -lt $args.Count; $i++) {
                    $bytes = [Text.Encoding]::UTF8.GetBytes([string]$args[$i])
                    [Console]::Out.WriteLine([Convert]::ToBase64String($bytes))
                }
                exit 0
            }
            if ($mode -eq 'oversized') {
                [Console]::Out.Write(('O' * 8192))
                [Console]::Error.Write(('E' * 8192))
                exit 0
            }
            if ($mode -eq 'credential') {
                [Console]::Out.WriteLine(('API_TOKEN=' + $args[1]))
                [Console]::Out.WriteLine(('OPENAI_API_KEY=' + $args[1]))
                [Console]::Out.WriteLine(('{"OPENAI_API_KEY":"' + $args[1] + '"}'))
                [Console]::Error.WriteLine(('password: ' + $args[1]))
                [Console]::Error.WriteLine(('SERVICE_TOKEN=' + $args[1]))
                [Console]::Error.WriteLine(('{"SERVICE_TOKEN":"' + $args[1] + '"}'))
                exit 0
            }
            if ($mode -eq 'tree' -or $mode -eq 'tree-with-secret') {
                $pwsh = Join-Path $PSHOME 'pwsh.exe'
                $child = Start-Process -FilePath $pwsh -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Seconds 60') -PassThru
                [IO.File]::WriteAllText($args[1], ($PID.ToString() + ',' + $child.Id.ToString()))
                if ($mode -eq 'tree-with-secret') {
                    [Console]::Out.WriteLine($args[2])
                    [Console]::Error.WriteLine(('token=' + $args[2]))
                }
                Start-Sleep -Seconds 60
                exit 0
            }
            if ($mode -eq 'stdin-close') {
                [IO.File]::WriteAllText($args[1], $PID.ToString())
                [void][Console]::In.ReadLine()
                exit 0
            }
            throw 'Unknown helper mode.'
            """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static async Task WaitForFileAsync(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(path) && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        File.Exists(path).ShouldBeTrue();
    }

    private static async Task<int[]> ReadPidsAsync(string path)
    {
        await WaitForFileAsync(path);
        return (await File.ReadAllTextAsync(path))
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();
    }

    private static async Task AssertProcessExitedAsync(int pid)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited)
                    return;
            }
            catch (ArgumentException)
            {
                return;
            }

            await Task.Delay(50);
        }

        using var survivor = Process.GetProcessById(pid);
        survivor.HasExited.ShouldBeTrue($"Probe process {pid} survived cancellation or timeout.");
    }
}
