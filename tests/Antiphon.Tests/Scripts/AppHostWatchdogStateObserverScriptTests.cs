using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0245 S1a — <c>scripts/apphost-watchdog-state-observer.ps1</c>. Drives the real
/// script against a throwaway Scheduled Task (or a missing name) and a temp logs dir.
/// Detection only: the script never Enable/Disable's the live AppHost watchdog.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AppHostWatchdogStateObserverScriptTests
{
    [Test]
    public async Task Missing_task_writes_unhealthy_json_and_a_stable_episode()
    {
        using var dir = new TempRoot();
        var taskName = $"Antiphon-CARD0245-missing-{Guid.NewGuid():N}"[..40];

        var first = await RunObserverAsync(dir.Path, taskName);
        first.ExitCode.ShouldBe(0, first.Output);
        var a = ReadState(dir.StateFile);
        a.GetProperty("state").GetString().ShouldBe("Missing");
        a.GetProperty("healthy").GetBoolean().ShouldBeFalse();
        a.GetProperty("maintenance").GetBoolean().ShouldBeFalse();
        var episode = a.GetProperty("episodeId").GetString();
        episode.ShouldNotBeNullOrWhiteSpace();
        a.GetProperty("disabledSinceUtc").GetString().ShouldNotBeNullOrWhiteSpace();

        var second = await RunObserverAsync(dir.Path, taskName);
        second.ExitCode.ShouldBe(0, second.Output);
        var b = ReadState(dir.StateFile);
        b.GetProperty("state").GetString().ShouldBe("Missing");
        b.GetProperty("episodeId").GetString().ShouldBe(episode);
        b.GetProperty("disabledSinceUtc").GetString().ShouldBe(a.GetProperty("disabledSinceUtc").GetString());
    }

    [Test]
    public async Task Disabled_throwaway_task_is_recorded_as_disabled()
    {
        using var dir = new TempRoot();
        var taskName = $"Antiphon-CARD0245-{Guid.NewGuid():N}"[..40];
        try
        {
            RegisterDisabledTask(taskName, dir.Path);
            var run = await RunObserverAsync(dir.Path, taskName);
            run.ExitCode.ShouldBe(0, run.Output);
            var state = ReadState(dir.StateFile);
            state.GetProperty("state").GetString().ShouldBe("Disabled");
            state.GetProperty("healthy").GetBoolean().ShouldBeFalse();
            state.GetProperty("episodeId").GetString().ShouldNotBeNullOrWhiteSpace();
        }
        finally
        {
            UnregisterTask(taskName);
        }
    }

    [Test]
    public async Task Maintenance_marker_is_recorded_without_changing_task_state()
    {
        using var dir = new TempRoot();
        var taskName = $"Antiphon-CARD0245-missing-{Guid.NewGuid():N}"[..40];
        File.WriteAllText(dir.MarkerFile, "down-on-purpose");

        var run = await RunObserverAsync(dir.Path, taskName);
        run.ExitCode.ShouldBe(0, run.Output);
        var state = ReadState(dir.StateFile);
        state.GetProperty("maintenance").GetBoolean().ShouldBeTrue();
        state.GetProperty("state").GetString().ShouldBe("Missing");
        state.GetProperty("healthy").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task New_condition_gets_a_new_episode()
    {
        using var dir = new TempRoot();
        var missingName = $"Antiphon-CARD0245-missing-{Guid.NewGuid():N}"[..40];
        var disabledName = $"Antiphon-CARD0245-{Guid.NewGuid():N}"[..40];
        try
        {
            var first = await RunObserverAsync(dir.Path, missingName);
            first.ExitCode.ShouldBe(0, first.Output);
            var episode1 = ReadState(dir.StateFile).GetProperty("episodeId").GetString();

            RegisterDisabledTask(disabledName, dir.Path);
            var second = await RunObserverAsync(dir.Path, disabledName);
            second.ExitCode.ShouldBe(0, second.Output);
            var episode2 = ReadState(dir.StateFile).GetProperty("episodeId").GetString();
            episode2.ShouldNotBe(episode1);
            ReadState(dir.StateFile).GetProperty("state").GetString().ShouldBe("Disabled");
        }
        finally
        {
            UnregisterTask(disabledName);
        }
    }

    [Test]
    public void Installer_keeps_observer_when_NoWatchdog_and_skips_it_only_for_NoAppHost()
    {
        var installer = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "install-autostart.ps1"));
        installer.ShouldContain("NoWatchdogStateObserver");
        installer.ShouldContain("Antiphon AppHost Watchdog State Observer");
        installer.ShouldContain("set-apphost-maintenance.ps1");
        installer.ShouldContain("if (-not $NoWatchdogStateObserver -and -not $NoAppHost)");
        installer.ShouldNotContain("Disable-ScheduledTask -TaskName `\"$WatchdogTaskName`\"");
        // -NoWatchdog must not gate the observer registration.
        installer.ShouldNotContain("if (-not $NoWatchdog -and -not $NoWatchdogStateObserver");
    }

    [Test]
    public async Task Maintenance_helper_writes_marker_before_disable_and_clears_in_reverse()
    {
        using var dir = new TempRoot();
        var taskName = $"Antiphon-CARD0245-maint-{Guid.NewGuid():N}"[..40];
        try
        {
            RegisterEnabledTask(taskName, dir.Path);
            var enter = await RunMaintenanceAsync(dir.Path, taskName, clear: false);
            enter.ExitCode.ShouldBe(0, enter.Output);
            File.Exists(dir.MarkerFile).ShouldBeTrue();
            GetTaskState(taskName).ShouldBe("Disabled");
            ReadState(dir.StateFile).GetProperty("maintenance").GetBoolean().ShouldBeTrue();

            var leave = await RunMaintenanceAsync(dir.Path, taskName, clear: true);
            leave.ExitCode.ShouldBe(0, leave.Output);
            File.Exists(dir.MarkerFile).ShouldBeFalse();
            GetTaskState(taskName).ShouldBe("Ready");
        }
        finally
        {
            UnregisterTask(taskName);
        }
    }

    private static JsonElement ReadState(string path)
    {
        File.Exists(path).ShouldBeTrue(path);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    private static Task<(int ExitCode, string Output)> RunObserverAsync(string root, string taskName)
    {
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "apphost-watchdog-state-observer.ps1");
        return RunPwshAsync(script, "-Root", root, "-TaskName", taskName);
    }

    private static Task<(int ExitCode, string Output)> RunMaintenanceAsync(string root, string taskName, bool clear)
    {
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "set-apphost-maintenance.ps1");
        var args = new List<string> { "-Root", root, "-WatchdogTaskName", taskName };
        if (clear) args.Insert(0, "-Clear");
        return RunPwshAsync(script, args.ToArray());
    }

    private static async Task<(int ExitCode, string Output)> RunPwshAsync(string script, params string[] args)
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private static void RegisterDisabledTask(string taskName, string workingDirectory)
    {
        RegisterEnabledTask(taskName, workingDirectory);
        RunSchtasks("Disable-ScheduledTask", taskName);
    }

    private static void RegisterEnabledTask(string taskName, string workingDirectory)
    {
        var ps = $@"
$action = New-ScheduledTaskAction -Execute 'pwsh' -Argument '-NoProfile -Command ""exit 0""' -WorkingDirectory '{workingDirectory.Replace("'", "''")}'
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddYears(1)
$principal = New-ScheduledTaskPrincipal -UserId ""$env:USERDOMAIN\$env:USERNAME"" -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 1)
if (Get-ScheduledTask -TaskName '{taskName}' -ErrorAction SilentlyContinue) {{ Unregister-ScheduledTask -TaskName '{taskName}' -Confirm:$false }}
Register-ScheduledTask -TaskName '{taskName}' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'CARD-0245 throwaway' | Out-Null
";
        RunInline(ps).ExitCode.ShouldBe(0);
    }

    private static void RunSchtasks(string cmdlet, string taskName) =>
        RunInline($"{cmdlet} -TaskName '{taskName}' | Out-Null").ExitCode.ShouldBe(0);

    private static string GetTaskState(string taskName)
    {
        var run = RunInline($"(Get-ScheduledTask -TaskName '{taskName}').State.ToString()");
        run.ExitCode.ShouldBe(0, run.Output);
        return run.Output.Trim();
    }

    private static void UnregisterTask(string taskName) =>
        RunInline($"Unregister-ScheduledTask -TaskName '{taskName}' -Confirm:$false -ErrorAction SilentlyContinue");

    private static (int ExitCode, string Output) RunInline(string script)
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);
        return (process.ExitCode, stdout + stderr);
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = Directory.CreateTempSubdirectory("card0245-wd-").FullName;
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "logs"));
        }

        public string Path { get; }
        public string StateFile => System.IO.Path.Combine(Path, "logs", "apphost-watchdog-state.json");
        public string MarkerFile => System.IO.Path.Combine(Path, "logs", "apphost.down-on-purpose");

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
