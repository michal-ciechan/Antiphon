using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0245 S1a: the watchdog-state observer and the maintenance helper. Since unit 31 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md they are the apphost-watchdog-state-observer and
/// set-apphost-maintenance commands (AppHostWatchdogStateObserver, AppHostMaintenance) and run in process: the document,
/// episode and maintenance rows on AppHostWorld's task table (the scripts ran against throwaway tasks under pwsh), and the
/// Task Scheduler itself (AppHostTasks: a missing name, and a throwaway task created from XML with schtasks.exe, read,
/// disabled and enabled, then deleted; never run). Detection only: nothing touches the live AppHost watchdog.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AppHostWatchdogStateObserverScriptTests
{
    private const string Watchdog = "Antiphon-CARD0245-fixture";

    private static JsonElement ReadState(AppHostWorld world)
    {
        var path = world.LogPath("apphost-watchdog-state.json");
        File.Exists(path).ShouldBeTrue(path);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    private static int Observe(AppHostWorld world) =>
        AppHostWatchdogStateObserver.Run(AppHostObserverOptions.Parse(["--task-name", Watchdog], world.Root), world.Host(), world);

    private static (int Exit, string Text) Maintain(AppHostWorld world, params string[] flags)
    {
        var before = world.Out.ToString().Length;
        var exit = AppHostMaintenance.Run(AppHostMaintenanceOptions.Parse(flags.Concat(["--watchdog-task-name", Watchdog]).ToArray(), world.Root), world.Host(), world);
        return (exit, world.Out.ToString()[before..]);
    }

    [Test]
    public void Missing_task_writes_unhealthy_json_and_a_stable_episode()
    {
        using var world = new AppHostWorld();
        Observe(world).ShouldBe(0);
        var a = ReadState(world);
        a.GetProperty("state").GetString().ShouldBe("Missing");
        a.GetProperty("healthy").GetBoolean().ShouldBeFalse();
        a.GetProperty("maintenance").GetBoolean().ShouldBeFalse();
        a.GetProperty("taskName").GetString().ShouldBe(Watchdog);
        var episode = a.GetProperty("episodeId").GetString();
        Guid.TryParse(episode, out _).ShouldBeTrue(episode);
        a.GetProperty("disabledSinceUtc").GetString().ShouldNotBeNullOrWhiteSpace();
        world.Clock.Advance(TimeSpan.FromMinutes(2));
        Observe(world).ShouldBe(0);
        var b = ReadState(world);
        b.GetProperty("episodeId").GetString().ShouldBe(episode);
        b.GetProperty("disabledSinceUtc").GetString().ShouldBe(a.GetProperty("disabledSinceUtc").GetString());
        b.GetProperty("observedAtUtc").GetString().ShouldNotBe(a.GetProperty("observedAtUtc").GetString());
        world.Out.ToString().ShouldContain("[watchdog-state] [INFO] state=Missing healthy=False maintenance=False episode=" + episode);
        File.ReadAllText(world.LogPath("apphost-watchdog-state-observer.log")).ShouldContain("state=Missing");
        Antiphon.Server.Application.Services.AppHostWatchdogStateDocument.TryParse(File.ReadAllText(world.LogPath("apphost-watchdog-state.json")), out var document).ShouldBeTrue();
        document!.IsUnhealthy.ShouldBeTrue();
    }

    [Test]
    public void Disabled_and_healthy_tasks_are_recorded_and_a_new_condition_gets_a_new_episode()
    {
        using var world = new AppHostWorld();
        Observe(world);
        var missingEpisode = ReadState(world).GetProperty("episodeId").GetString();
        world.Tasks[Watchdog] = new AppHostTaskInfo("Disabled", "State=Disabled");
        Observe(world);
        var disabled = ReadState(world);
        disabled.GetProperty("state").GetString().ShouldBe("Disabled");
        disabled.GetProperty("detail").GetString().ShouldBe("State=Disabled");
        disabled.GetProperty("episodeId").GetString().ShouldNotBe(missingEpisode);
        world.Tasks[Watchdog] = new AppHostTaskInfo("Enabled", "State=Ready");
        Observe(world);
        var healthy = ReadState(world);
        healthy.GetProperty("healthy").GetBoolean().ShouldBeTrue();
        healthy.GetProperty("episodeId").ValueKind.ShouldBe(JsonValueKind.Null);
        healthy.GetProperty("disabledSinceUtc").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public void Maintenance_marker_is_recorded_without_changing_task_state()
    {
        using var world = new AppHostWorld();
        File.WriteAllText(world.LogPath("apphost.down-on-purpose"), "down-on-purpose");
        Observe(world).ShouldBe(0);
        var state = ReadState(world);
        state.GetProperty("maintenance").GetBoolean().ShouldBeTrue();
        state.GetProperty("state").GetString().ShouldBe("Missing");
        world.Trace.ShouldNotContain(row => row.StartsWith("enable", StringComparison.Ordinal) || row.StartsWith("disable", StringComparison.Ordinal));
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
    public void Maintenance_helper_writes_marker_before_disable_and_clears_in_reverse()
    {
        using var world = new AppHostWorld();
        world.Tasks[Watchdog] = new AppHostTaskInfo("Enabled", "State=Ready");
        var enter = Maintain(world);
        enter.Exit.ShouldBe(0, enter.Text);
        File.Exists(world.LogPath("apphost.down-on-purpose")).ShouldBeTrue();
        File.ReadAllText(world.LogPath("apphost.down-on-purpose")).ShouldStartWith("down-on-purpose 2026-11-24T09:00:00");
        world.Trace.ShouldContain("disable " + Watchdog + " marker=True");
        enter.Text.ShouldContain("> Entering AppHost maintenance (create marker, then disable '" + Watchdog + "').");
        enter.Text.ShouldContain("  Watchdog task disabled.");
        enter.Text.ShouldEndWith("Done. This command does not start or stop the AppHost." + Environment.NewLine);
        ReadState(world).GetProperty("maintenance").GetBoolean().ShouldBeTrue();
        ReadState(world).GetProperty("state").GetString().ShouldBe("Disabled");

        var leave = Maintain(world, "--clear");
        leave.Exit.ShouldBe(0, leave.Text);
        world.Trace.ShouldContain("enable " + Watchdog + " marker=True", "the watchdog is re-enabled before the marker goes");
        File.Exists(world.LogPath("apphost.down-on-purpose")).ShouldBeFalse();
        leave.Text.ShouldContain("  Removed " + world.LogPath("apphost.down-on-purpose"));
        ReadState(world).GetProperty("healthy").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public void Enter_if_absent_is_create_new_and_refuses_a_present_marker()
    {
        using var world = new AppHostWorld();
        world.Tasks[Watchdog] = new AppHostTaskInfo("Enabled", "State=Ready");
        var first = Maintain(world, "--enter-if-absent");
        first.Exit.ShouldBe(0, first.Text);
        world.Trace.ShouldContain("disable " + Watchdog + " marker=True");
        world.Tasks[Watchdog] = new AppHostTaskInfo("Enabled", "State=Ready");
        var again = Maintain(world, "--enter-if-absent");
        again.Exit.ShouldBe(2, again.Text);
        again.Text.ShouldContain("  Marker already present. This run did not enter maintenance.");
        world.Trace.Count(row => row.StartsWith("disable", StringComparison.Ordinal)).ShouldBe(1, "a present marker disables nothing");
        File.Exists(world.LogPath("apphost.down-on-purpose")).ShouldBeTrue("and removes nothing");
        var both = Maintain(world, "--clear", "--enter-if-absent");
        both.Exit.ShouldBe(2);
        both.Text.ShouldContain("REFUSED: --clear and --enter-if-absent cannot be combined.");
        using var bare = new AppHostWorld();
        var unregistered = Maintain(bare);
        unregistered.Exit.ShouldBe(0);
        unregistered.Text.ShouldContain("  Watchdog task '" + Watchdog + "' is not registered; marker is in place.");
    }

    // ---- the Task Scheduler (Windows): a missing name, and a throwaway task read, disabled and enabled ----

    [Test]
    public void Task_scheduler_reads_a_missing_task_as_missing()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("CARD-0245: the Task Scheduler is a Windows service");
        var name = $"Antiphon-CARD0245-missing-{Guid.NewGuid():N}"[..40];
        AppHostTasks.Read(name).State.ShouldBe("Missing");
        AppHostTasks.Running(name).ShouldBe(false);
        Should.Throw<InvalidOperationException>(() => AppHostTasks.SetEnabled(name, false));
    }

    [Test]
    public void Task_scheduler_disables_and_enables_a_throwaway_task()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("CARD-0245: the Task Scheduler is a Windows service");
        var name = $"Antiphon-CARD0245-{Guid.NewGuid():N}"[..40];
        var xml = Path.Combine(Path.GetTempPath(), name + ".xml");
        File.WriteAllText(xml, """
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>CARD-0245 throwaway</Description></RegistrationInfo>
              <Triggers><TimeTrigger><StartBoundary>2099-01-01T00:00:00</StartBoundary><Enabled>true</Enabled></TimeTrigger></Triggers>
              <Principals><Principal id="Author"><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings><Enabled>true</Enabled><ExecutionTimeLimit>PT1M</ExecutionTimeLimit></Settings>
              <Actions Context="Author"><Exec><Command>cmd.exe</Command><Arguments>/c exit 0</Arguments></Exec></Actions>
            </Task>
            """, Encoding.Unicode);
        try
        {
            Schtasks("/Create", "/TN", name, "/XML", xml).ShouldBe(0);
            AppHostTasks.Read(name).ShouldBe(new AppHostTaskInfo("Enabled", "State=Ready"));
            AppHostTasks.SetEnabled(name, false);
            AppHostTasks.Read(name).ShouldBe(new AppHostTaskInfo("Disabled", "State=Disabled"));
            AppHostTasks.Running(name).ShouldBe(false);
            AppHostTasks.SetEnabled(name, true);
            AppHostTasks.Read(name).State.ShouldBe("Enabled");
        }
        finally
        {
            Schtasks("/Delete", "/TN", name, "/F");
            File.Delete(xml);
        }
    }

    private static int Schtasks(params string[] arguments)
    {
        var start = new ProcessStartInfo("schtasks.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("schtasks did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit(30_000).ShouldBeTrue("schtasks finished");
        if (process.ExitCode != 0) Console.Error.WriteLine(stdout.Result + stderr.Result);
        return process.ExitCode;
    }
}
