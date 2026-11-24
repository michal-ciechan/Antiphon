using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The always-on installers: the install-autostart and install-cleanup-task commands (Antiphon.Nightly.AutostartInstall; unit 36
/// of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md deleted scripts/install-autostart.ps1 and
/// scripts/install-cleanup-task.ps1). In process on a mock file system with a recording Task Scheduler and docker: the same
/// task names, the actions that run the kept launchers by path (so tasks registered before the port keep working), the
/// triggers and settings the cmdlets built, the -AppHostOnly / -NoWatchdog / -NoAppHost selection, uninstall, the PowerShell
/// host choice and runbook G-1. No real task is registered (ScheduledTaskRegistryTests reads one disabled throwaway task back).
/// </summary>
[Category("Unit")]
public sealed class AutostartInstallTests
{
    private const string Root = @"C:\src\Antiphon";
    private static readonly string Pwsh7 = Path.Combine(@"C:\Program Files", "PowerShell", "7", "pwsh.exe");
    private static readonly string DockerSettings = Path.Combine(@"C:\Users\op\AppData\Roaming", "Docker");

    /// <summary>A recording Task Scheduler: the registered definitions by name and every call in order.</summary>
    internal sealed class RecordingTasks : IScheduledTaskRegistry
    {
        public Dictionary<string, ScheduledTaskDefinition?> Registered { get; } = new(StringComparer.Ordinal);
        public List<string> Calls { get; } = [];

        public bool Exists(string name) => Registered.ContainsKey(name);

        public void Delete(string name)
        {
            Calls.Add("delete " + name);
            Registered.Remove(name);
        }

        public void Register(ScheduledTaskDefinition definition)
        {
            Calls.Add("register " + definition.Name);
            Registered[definition.Name] = definition;
        }
    }

    private sealed class World
    {
        public MockFileSystem Files { get; } = new();
        public RecordingTasks Tasks { get; } = new();
        public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal)
        {
            ["ProgramFiles"] = @"C:\Program Files", ["LOCALAPPDATA"] = @"C:\Users\op\AppData\Local", ["APPDATA"] = @"C:\Users\op\AppData\Roaming",
            ["USERDOMAIN"] = "DESKTOP", ["USERNAME"] = "op",
        };
        public Dictionary<string, string> OnPath { get; } = new(StringComparer.Ordinal) { ["powershell.exe"] = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" };
        public List<(string[] Arguments, bool Capture)> Docker { get; } = [];
        public int DockerInfo { get; set; }
        public int ComposeUp { get; set; }
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 24, 8, 30, 15, TimeSpan.Zero));
        public StringWriter Out { get; } = new();
        public StringWriter Error { get; } = new();

        public World(bool launchers = true)
        {
            Files.AddFile(Path.Combine(Root, "docker-compose.dev.yml"), new MockFileData("services:\n  postgres:\n    image: postgres\n  redpanda:\n    image: redpanda\n"));
            if (launchers)
                foreach (var name in new[] { "autostart-session-runner.ps1", "autostart-apphost.ps1", "watchdog-apphost.ps1", "apphost-watchdog-state-observer.ps1" })
                    Files.AddFile(Path.Combine(Root, "scripts", name), new MockFileData("#requires -Version 5.1"));
            Files.AddFile(Pwsh7, new MockFileData(""));
        }

        public AutostartInstallMachine Machine() => new(Files, Tasks, name => Variables.GetValueOrDefault(name)!, name => OnPath.GetValueOrDefault(name)!,
            (arguments, capture) =>
            {
                Docker.Add((arguments.ToArray(), capture));
                return arguments[0] == "info" ? DockerInfo : ComposeUp;
            });

        private NightlyHost Host() => new()
        {
            Clock = Clock, Git = new FakeGit().Run, Launcher = new RecordingLauncher(), Processes = new FakeProcessTable(), Probe = new FakeHostProbe(), Out = Out,
            Error = Error, Trace = TextWriter.Null,
        };

        public int Install(params string[] arguments) => AutostartInstall.Run(AutostartInstallOptions.Parse(arguments, Root), Host(), Machine());

        public int Cleanup(params string[] arguments) => AutostartInstall.RunCleanupTask(CleanupTaskInstallOptions.Parse(arguments, Root), Host(), Machine());

        public ScheduledTaskDefinition Task(string name) => Tasks.Registered[name].ShouldNotBeNull(name);
    }

    private static string Hidden(string launcher) =>
        "-NonInteractive -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + Path.Combine(Root, "scripts", launcher) + "\"";

    [Test]
    public void Install_registers_the_four_tasks_running_the_kept_launchers_by_path()
    {
        var world = new World();
        world.Install().ShouldBe(0, world.Out.ToString() + world.Error);
        world.Tasks.Calls.ShouldBe(["register Antiphon Session Runner", "register Antiphon AppHost", "register Antiphon AppHost Watchdog",
            "register Antiphon AppHost Watchdog State Observer"]);
        const string user = @"DESKTOP\op";

        var runner = world.Task("Antiphon Session Runner");
        runner.Action.ShouldBe(new ScheduledTaskAction(Pwsh7, Hidden("autostart-session-runner.ps1"), Root));
        runner.Triggers.ShouldBe([new ScheduledTaskTrigger(ScheduledTaskTriggerKind.Logon, UserId: user)]);
        (runner.UserId, runner.Enabled).ShouldBe((user, true));
        runner.Settings.ShouldBe(new ScheduledTaskSettings(true, true, true, "PT0S", 3, "PT1M"));
        runner.Description.ShouldBe("Starts the Antiphon session-runner daemon (port 17204) at logon so agents can run.");

        var appHost = world.Task("Antiphon AppHost");
        appHost.Action.ShouldBe(new ScheduledTaskAction(Pwsh7, Hidden("autostart-apphost.ps1"), Root));
        appHost.Triggers.ShouldBe([new ScheduledTaskTrigger(ScheduledTaskTriggerKind.Logon, UserId: user, Delay: "PT1M")]);
        appHost.Settings.ShouldBe(new ScheduledTaskSettings(true, true, true, "PT1H", 2, "PT5M"));

        // The watchdog and the observer: logon + 15 min repeating every 2 min, and a repeating once trigger 2 min after the install.
        ScheduledTaskTrigger[] repeating =
        [
            new(ScheduledTaskTriggerKind.Logon, UserId: user, Delay: "PT15M", RepetitionInterval: "PT2M", RepetitionDuration: "P3650D"),
            new(ScheduledTaskTriggerKind.Once, StartBoundary: "2026-11-24T08:32:15Z", RepetitionInterval: "PT2M", RepetitionDuration: "P3650D"),
        ];
        var watchdog = world.Task("Antiphon AppHost Watchdog");
        watchdog.Action.ShouldBe(new ScheduledTaskAction(Pwsh7, Hidden("watchdog-apphost.ps1"), Root));
        watchdog.Triggers.ShouldBe(repeating);
        watchdog.Settings.ShouldBe(new ScheduledTaskSettings(true, true, true, "PT30M"));
        watchdog.Description.ShouldContain("restarts via restart-apphost.ps1 if both stay down. Never touches the session-runner on 17204.");
        var observer = world.Task("Antiphon AppHost Watchdog State Observer");
        observer.Action.ShouldBe(new ScheduledTaskAction(Pwsh7, Hidden("apphost-watchdog-state-observer.ps1"), Root));
        observer.Triggers.ShouldBe(repeating);
        observer.Settings.ShouldBe(new ScheduledTaskSettings(true, true, true, "PT5M"));

        world.Docker.Select(call => string.Join(" ", call.Arguments)).ShouldBe(["info", "compose -f " + Path.Combine(Root, "docker-compose.dev.yml") + " up -d"]);
        world.Out.ToString().ShouldContain("  PowerShell host for task: " + Pwsh7);
        world.Out.ToString().ShouldContain("Remove auto-start later:                            scripts\\antiphon.cmd install-autostart --uninstall");
    }

    [Test]
    public void Reinstall_replaces_each_task_and_apphost_only_leaves_the_runner_alone()
    {
        var world = new World();
        world.Install().ShouldBe(0);
        world.Tasks.Calls.Clear();
        world.Install("--apphost-only").ShouldBe(0);
        world.Tasks.Calls.ShouldBe(["delete Antiphon AppHost", "register Antiphon AppHost", "delete Antiphon AppHost Watchdog", "register Antiphon AppHost Watchdog",
            "delete Antiphon AppHost Watchdog State Observer", "register Antiphon AppHost Watchdog State Observer"]);
        world.Out.ToString().ShouldContain("Existing 'Antiphon Session Runner' left untouched.");
    }

    [Test]
    public void Installer_keeps_observer_when_NoWatchdog_and_skips_it_only_for_NoAppHost()
    {
        var noWatchdog = new World();
        noWatchdog.Install("--no-watchdog").ShouldBe(0);
        noWatchdog.Tasks.Registered.Keys.ShouldBe(["Antiphon Session Runner", "Antiphon AppHost", "Antiphon AppHost Watchdog State Observer"], ignoreOrder: true);
        var noAppHost = new World();
        noAppHost.Install("--no-apphost").ShouldBe(0);
        noAppHost.Tasks.Registered.Keys.ShouldBe(["Antiphon Session Runner"]);
        noAppHost.Out.ToString().ShouldContain(@"Start the app by hand with:  .\dev-aspire.ps1");
        var noObserver = new World();
        noObserver.Install("--no-watchdog-state-observer").ShouldBe(0);
        noObserver.Tasks.Registered.Keys.ShouldNotContain("Antiphon AppHost Watchdog State Observer");
        noObserver.Tasks.Registered.Keys.ShouldContain("Antiphon AppHost Watchdog");
        // Detection only: the installer never disables the watchdog (set-apphost-maintenance does, after its marker).
        noWatchdog.Tasks.Calls.ShouldNotContain(call => call.StartsWith("disable", StringComparison.Ordinal));
    }

    [Test]
    public void Uninstall_removes_the_selection_and_leaves_postgres()
    {
        var world = new World();
        world.Install().ShouldBe(0);
        world.Tasks.Calls.Clear();
        world.Docker.Clear();
        world.Install("--uninstall", "--apphost-only").ShouldBe(0);
        world.Tasks.Calls.ShouldBe(["delete Antiphon AppHost", "delete Antiphon AppHost Watchdog", "delete Antiphon AppHost Watchdog State Observer"]);
        world.Tasks.Registered.Keys.ShouldBe(["Antiphon Session Runner"]);
        world.Install("--uninstall").ShouldBe(0);
        world.Tasks.Registered.ShouldBeEmpty();
        world.Out.ToString().ShouldContain("No such task - nothing to do.");
        world.Out.ToString().ShouldContain("  docker compose -f \"" + Path.Combine(Root, "docker-compose.dev.yml") + "\" down");
        world.Docker.ShouldBeEmpty("uninstall never touches docker");
    }

    [Test]
    public void A_missing_launcher_or_a_failed_compose_registers_nothing()
    {
        var bare = new World(launchers: false);
        bare.Install().ShouldBe(1);
        bare.Error.ToString().Trim().ShouldBe("install-autostart: Missing " + Path.Combine(Root, "scripts", "autostart-session-runner.ps1"));
        bare.Tasks.Calls.ShouldBeEmpty();

        var failed = new World { ComposeUp = 1 };
        failed.Install().ShouldBe(1);
        failed.Error.ToString().ShouldContain("docker compose up failed.");
        failed.Tasks.Calls.ShouldBeEmpty();

        // Docker Desktop down: the compose step is skipped and the tasks still registered.
        var down = new World { DockerInfo = 1 };
        down.Install().ShouldBe(0);
        var info = down.Docker.ShouldHaveSingleItem();
        (string.Join(" ", info.Arguments), info.Capture).ShouldBe(("info", true));
        down.Out.ToString().ShouldContain("Docker Desktop is not running - start it, then re-run this command.");
        down.Tasks.Registered.Count.ShouldBe(4);
    }

    [Test]
    public void Runbook_g1_keeps_the_desktop_postgres_down_and_docker_autostart_is_reported()
    {
        var world = new World();
        world.Variables[DevCompose.SkipVariable] = "1";
        world.Files.AddFile(Path.Combine(DockerSettings, "settings-store.json"), new MockFileData("{\"AutoStart\":true}"));
        world.Install().ShouldBe(0);
        world.Docker.Last().Arguments.ShouldBe(["compose", "-f", Path.Combine(Root, "docker-compose.dev.yml"), "up", "-d", "--no-deps", "redpanda"]);
        world.Out.ToString().ShouldContain("ANTIPHON_SKIP_DEV_POSTGRES=1: the desktop Postgres is not brought up");
        world.Out.ToString().ShouldContain("Docker Desktop AutoStart is ON");
        var off = new World();
        off.Files.AddFile(Path.Combine(DockerSettings, "settings-store.json"), new MockFileData("{\"Other\":1}"));
        off.Files.AddFile(Path.Combine(DockerSettings, "settings.json"), new MockFileData("{\"AutoStart\":false}"));
        off.Install().ShouldBe(0);
        off.Out.ToString().ShouldContain("appears OFF");
    }

    [Test]
    public void The_task_host_is_pwsh_7_never_a_pinned_msix_path()
    {
        var world = new World();
        AutostartInstall.PowerShellHost(world.Machine()).ShouldBe(Pwsh7);
        world.Files.RemoveFile(Pwsh7);
        var alias = Path.Combine(@"C:\Users\op\AppData\Local", "Microsoft", "WindowsApps", "pwsh.exe");
        world.Files.AddFile(alias, new MockFileData(""));
        AutostartInstall.PowerShellHost(world.Machine()).ShouldBe(alias, "the per-user alias is version-independent");
        world.Files.RemoveFile(alias);
        var pinned = @"C:\Program Files\WindowsApps\Microsoft.PowerShell_7.6.4.0_x64__8wekyb3d8bbwe\pwsh.exe";
        world.Files.AddFile(pinned, new MockFileData(""));
        world.OnPath["pwsh.exe"] = pinned;
        AutostartInstall.PowerShellHost(world.Machine()).ShouldBe(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");
    }

    [Test]
    public void Cleanup_task_registers_the_weekly_and_logon_build_junk_cleanup()
    {
        var world = new World();
        world.OnPath["pwsh"] = @"C:\Tools\pwsh.exe";
        world.Cleanup().ShouldBe(0);
        var task = world.Task("Antiphon Build Junk Cleanup");
        task.Action.ShouldBe(new ScheduledTaskAction(@"C:\Tools\pwsh.exe", Hidden("cleanup-build-junk.ps1"), null!));
        task.Triggers.ShouldBe([
            new ScheduledTaskTrigger(ScheduledTaskTriggerKind.Weekly, StartBoundary: "2026-11-24T09:00:00Z", DaysOfWeek: 2, WeeksInterval: 1),
            new ScheduledTaskTrigger(ScheduledTaskTriggerKind.Logon, UserId: @"DESKTOP\op"),
        ]);
        task.Settings.ShouldBe(new ScheduledTaskSettings(false, false, true, "PT2H"));
        world.Out.ToString().Trim().ShouldBe("Registered task 'Antiphon Build Junk Cleanup' (weekly Mon 09:00 + at logon) running " + Path.Combine(Root, "scripts", "cleanup-build-junk.ps1"));
        world.Cleanup().ShouldBe(0);
        world.Tasks.Calls.ShouldBe(["register Antiphon Build Junk Cleanup", "delete Antiphon Build Junk Cleanup", "register Antiphon Build Junk Cleanup"]);
        world.Cleanup("--uninstall").ShouldBe(0);
        world.Cleanup("--uninstall").ShouldBe(0, "an absent task is not an error");
        world.Tasks.Registered.ShouldBeEmpty();
        var noPwsh = new World();
        noPwsh.Cleanup().ShouldBe(0);
        noPwsh.Task("Antiphon Build Junk Cleanup").Action.Execute.ShouldBe("powershell.exe");
    }

    [Test]
    public void Start_boundaries_are_utc_to_the_second_as_the_cmdlets_wrote_them()
    {
        AutostartInstall.Boundary(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.FromHours(1))).ShouldBe("2026-07-01T08:00:00Z");
        AutostartInstall.Boundary(new DateTimeOffset(2026, 11, 24, 7, 13, 47, 512, TimeSpan.Zero)).ShouldBe("2026-11-24T07:13:47Z");
        AutostartInstall.Duration(TimeSpan.FromDays(3650)).ShouldBe("P3650D");
        AutostartInstall.Duration(TimeSpan.Zero).ShouldBe("PT0S");
        AutostartInstall.Duration(TimeSpan.FromMinutes(2)).ShouldBe("PT2M");
    }
}
