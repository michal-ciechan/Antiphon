using System.IO.Abstractions;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0043, CARD-0420: the run-daemon supervisor (Antiphon.Nightly.DaemonSupervisor, the body of scripts/run-daemon.ps1
/// since unit 30 of the 2026-11-22 plan; the script is the AppHost's thin launcher). In process on a temporary root, with
/// the build answered by a RecordingLauncher, HEAD by a FakeGit, the clock and the waits by a FakeTimeProvider, and the
/// service by a fake starter that writes what the cmd.exe service did. The rows are those of the deleted
/// Antiphon.SessionRunner.Tests classes DaemonStartupDiagnosticsTests (which ran run-daemon.ps1 under pwsh and
/// powershell.exe with c589 build-slot shims) and DaemonLogRotationTests (which ran it with a .cmd service); one Windows row
/// still runs a real .cmd service under cmd.exe through the production starter. Dropped:
/// Build_stub_survives_the_build_slot_process_boundary (it proved the PowerShell fixture's build stub reached the build-slot
/// child; the build is now one launch the fake answers).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DaemonSupervisorTests
{
    private const int Supervisor = 900002;
    private static readonly DateTime SupervisorStart = new(2026, 9, 7, 9, 58, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset RecordClock = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeService(int pid, DateTime? started, Func<int> run) : IDaemonService
    {
        public int Pid => pid;
        public DateTime? StartTimeUtc => started;
        public int WaitForExit() => run();
        public void Dispose() { }
    }

    /// <summary>One supervisor's world: the root, its files, the fakes, and the service's behaviour per launch (1-based).</summary>
    private sealed class World : IDisposable
    {
        private int _builds;

        public World(DateTimeOffset? now = null)
        {
            Clock = new FakeTimeProvider(now ?? RecordClock);
            Processes.Start(Supervisor, SupervisorStart);
            Launcher = new RecordingLauncher(_ =>
            {
                _builds++;
                return new NightlyLaunchResult { ExitCode = BuildExit(_builds), LogText = "synthetic build output\n" };
            });
        }

        public NightlyFixture Fixture { get; } = NightlyFixture.Create("run-daemon");
        public FakeTimeProvider Clock { get; }
        public FakeProcessTable Processes { get; } = new();
        public FakeGit Git { get; set; } = new();
        public RecordingLauncher Launcher { get; }
        public Func<int, int> BuildExit { get; set; } = _ => 0;
        public List<DaemonServiceStart> Starts { get; } = [];
        public Func<DaemonServiceStart, int, int> Service { get; set; } = (_, _) => 0;
        public DaemonServiceStarter? Starter { get; set; }
        public string Log => Fixture.PathOf("svc.log");
        public string State => Fixture.PathOf("svc.state");
        public string Pid => Fixture.PathOf("svc.pid");

        public int Run(string name, bool build, params string[] extra)
        {
            var arguments = new List<string> { "--name", name, "--work-dir", Fixture.Root, "--exe", Fixture.PathOf("svc.cmd"), "--log-file", Log,
                "--service-pid-file", Pid, "--state-file", State, "--supervisor-pid", Supervisor.ToString() };
            if (build) arguments.AddRange(["--build-project-dir", "fixture-only"]);
            arguments.AddRange(extra);
            var host = new NightlyHost
            {
                Clock = Clock, Git = Git.Run, Launcher = Launcher, Processes = Processes, Probe = new FakeHostProbe(), Out = Fixture.Out, Error = Fixture.Error,
                Trace = TextWriter.Null, Sleep = Clock.Advance,
            };
            return DaemonSupervisor.Run(DaemonSupervisorOptions.Parse(arguments, Fixture.PathOf("build-slot.ps1")), host, new FileSystem(), Starter ?? new DaemonServiceStarter(Start));
        }

        private IDaemonService Start(DaemonServiceStart start)
        {
            Starts.Add(start);
            var index = Starts.Count;
            return new FakeService(7000 + index, RecordClock.UtcDateTime, () =>
            {
                File.ReadAllText(Pid).Trim().ShouldBe((7000 + index).ToString(), "the wrapper's PID is the service PID file while it runs");
                return Service(start, index);
            });
        }

        public string[] Lines => File.ReadAllLines(Log);

        public JsonElement[] Records => Lines.Where(l => l.Contains("ANTIPHON_STARTUP ", StringComparison.Ordinal))
            .Select(l => JsonDocument.Parse(l[(l.IndexOf("ANTIPHON_STARTUP ", StringComparison.Ordinal) + 17)..]).RootElement.Clone()).ToArray();

        public void Dispose() => Fixture.Dispose();
    }

    private static string Event(JsonElement record) => record.GetProperty("event").GetString()!;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Build_failure_retries_without_launching_failed_output(bool failFirst) => Exercise(failFirst, 1, true);

    [Test]
    public void Every_relaunch_builds_and_gets_a_fresh_attempt() => Exercise(false, 2, true);

    [Test]
    public void Nonrunner_without_build_keeps_its_launch_behavior() => Exercise(false, 1, false);

    private static void Exercise(bool failFirst, int runs, bool build)
    {
        using var w = new World();
        if (failFirst) w.BuildExit = n => n == 1 ? 17 : 0;
        w.Service = (_, index) =>
        {
            if (index == runs) File.WriteAllText(w.State, "stopped");
            return 0;
        };
        File.WriteAllText(w.State, "running");
        w.Run(build ? "session-runner" : "generic", build).ShouldBe(0);

        var records = w.Records;
        w.Launcher.Launches.Count.ShouldBe(build ? runs + (failFirst ? 1 : 0) : 0, "every relaunch must build");
        w.Starts.Count.ShouldBe(runs);
        var launches = records.Where(r => Event(r) == "wrapper-started").ToArray();
        launches.Length.ShouldBe(runs);
        launches.Select(r => r.GetProperty("attemptId").GetString()).Distinct().Count().ShouldBe(runs);
        foreach (var (launch, start) in launches.Zip(w.Starts))
        {
            launch.GetProperty("version").GetInt32().ShouldBe(1);
            launch.GetProperty("producer").GetString().ShouldBe("supervisor");
            launch.GetProperty("producerPid").GetInt32().ShouldBe(Supervisor);
            launch.GetProperty("producerStartTimeUtc").GetString().ShouldBe("2026-09-07T09:58:00.0000000Z");
            launch.GetProperty("wrapperPid").GetInt32().ShouldBeGreaterThan(0);
            launch.GetProperty("wrapperPid").GetInt32().ShouldNotBe(launch.GetProperty("producerPid").GetInt32());
            if (build)
            {
                start.Environment["ANTIPHON_STARTUP_ATTEMPT"].ShouldBe(launch.GetProperty("attemptId").GetString());
                start.Environment["ANTIPHON_STARTUP_SUPERVISOR_PID"].ShouldBe(Supervisor.ToString());
                start.Environment["ANTIPHON_STARTUP_SUPERVISOR_START"].ShouldBe("2026-09-07T09:58:00.0000000Z");
            }
            else
            {
                start.Environment.ShouldBeEmpty("only the session runner gets the startup correlation");
            }
        }
        if (failFirst)
        {
            records.First(r => Event(r) == "build-complete").GetProperty("exitCode").GetInt32().ShouldBe(17);
            records.First(r => Event(r) == "retry-scheduled").GetProperty("elapsedMs").GetDouble().ShouldBe(5000);
            w.Lines.ShouldContain(l => l.EndsWith("[session-runner] [ERR] Build failed (exit 17). Retrying in 5 s...", StringComparison.Ordinal));
        }
        if (runs == 2) records.Single(r => Event(r) == "retry-scheduled").GetProperty("elapsedMs").GetDouble().ShouldBe(3000);
        if (build) w.Lines.Count(l => l == "synthetic build output").ShouldBe(w.Launcher.Launches.Count, "each build's output is appended to the log");
        Event(records[^1]).ShouldBe("supervisor-stopping");
        File.Exists(w.Pid).ShouldBeFalse("the service PID file is removed once the service exits");

        // The restart's reader accepts the records the supervisor wrote (the recorded supervisor is their producer).
        using var platform = new RunnerRestartWorld();
        var reader = new RunnerMilestoneReader(w.Log, new FileSystem());
        reader.Read(platform, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        reader.Phase!.Event.ShouldBe("supervisor-stopping", "the production decoder did not accept the supervisor's records");
        reader.Attempt.ShouldBe(launches[^1].GetProperty("attemptId").GetString());
    }

    [Test]
    public void The_build_is_stamped_with_a_full_HEAD_through_the_build_slot_and_unstamped_otherwise()
    {
        using var stamped = new World { Git = new FakeGit("ABCDEF0123456789ABCDEF0123456789ABCDEF01\n") };
        stamped.Service = (_, _) => { File.WriteAllText(stamped.State, "stopped"); return 0; };
        stamped.Run("session-runner", true).ShouldBe(0);
        var launch = stamped.Launcher.Launches.ShouldHaveSingleItem();
        (launch.FileName, launch.WorkingDirectory, launch.CaptureOutput, launch.Custody).ShouldBe(("pwsh", "", true, NightlyCustody.Watched));
        launch.Arguments.ShouldBe(["-NoProfile", "-File", stamped.Fixture.PathOf("build-slot.ps1"), "-Label", "daemon-build", "--", "dotnet", "build", "fixture-only",
            "-c", "Debug", "--nologo", "-p:SourceRevisionId=abcdef0123456789abcdef0123456789abcdef01"]);
        var head = stamped.Git.Calls.ShouldHaveSingleItem();
        (head.WorkingDirectory, string.Join(" ", head.Arguments)).ShouldBe(("fixture-only", "rev-parse HEAD"));

        using var unstamped = new World { Git = new FakeGit("fatal: not a git repository", 128) };
        unstamped.Service = (_, _) => { File.WriteAllText(unstamped.State, "stopped"); return 0; };
        unstamped.Run("session-runner", true).ShouldBe(0);
        unstamped.Launcher.Launches.ShouldHaveSingleItem().Arguments[^1].ShouldBe("--nologo");
        unstamped.Lines.ShouldContain(l => l.EndsWith("WARNING: git rev-parse HEAD failed in fixture-only; building unstamped.", StringComparison.Ordinal));
    }

    [Test]
    public void The_built_executable_runs_under_cmd_with_its_output_appended_to_the_log()
    {
        using var w = new World();
        w.Service = (_, _) => { File.WriteAllText(w.State, "stopped"); return 0; };
        w.Run("fake-gateway", true, "--exe-args", " --urls  http://localhost:17208").ShouldBe(0);
        var start = w.Starts.ShouldHaveSingleItem();
        start.FileName.ShouldBe("cmd.exe");
        start.WorkingDirectory.ShouldBe(w.Fixture.Root);
        start.Arguments.ShouldBe("/s /c \"" + w.Fixture.PathOf("svc.cmd") + " --urls http://localhost:17208 >> \"" + w.Log + "\" 2>&1\"");
        w.Lines.ShouldContain(l => l.EndsWith("[fake-gateway] Starting " + w.Fixture.PathOf("svc.cmd") + " --urls http://localhost:17208", StringComparison.Ordinal));
        w.Lines.ShouldContain(l => l.EndsWith("[fake-gateway] Service PID 7001", StringComparison.Ordinal));
        w.Lines.ShouldContain(l => l.EndsWith("[fake-gateway] Exited (code 0)", StringComparison.Ordinal));
        w.Lines.ShouldContain(l => l.EndsWith("[fake-gateway] Desired state is 'stopped' - stopping.", StringComparison.Ordinal));
        Regex.IsMatch(w.Lines[0], @"^\d\d:\d\d:\d\d \[fake-gateway\] Supervisor started \(PID 900002, run-daemon PID 4242\)$").ShouldBeTrue(w.Lines[0]);
        w.Records.Select(Event).ToArray().ShouldBe(["build-start", "build-complete", "launch-requested", "wrapper-started", "service-exit", "supervisor-stopping"]);
    }

    [Test]
    public void Desired_state_stops_before_a_launch_or_after_the_service_and_is_running_when_absent()
    {
        using (var stopped = new World())
        {
            File.WriteAllText(stopped.State, "  STOPPED\r\n");
            File.WriteAllText(stopped.Pid, "1234");
            stopped.Run("generic", false).ShouldBe(0);
            stopped.Starts.ShouldBeEmpty();
            stopped.Lines[^1].ShouldEndWith("[generic] Desired state is stopped - exiting.");
            File.Exists(stopped.Pid).ShouldBeFalse();
        }

        using (var absent = new World())
        {
            absent.Service = (_, _) => { File.WriteAllText(absent.State, " "); return 3; };
            absent.Run("generic", false).ShouldBe(0);
            absent.Starts.Count.ShouldBe(1, "an absent state file is running");
            absent.Lines.ShouldContain(l => l.EndsWith("[generic] Exited (code 3)", StringComparison.Ordinal));
            absent.Lines[^1].ShouldEndWith("[generic] Desired state is '' - stopping.", customMessage: "a blank state is not running");
        }

        using (var empty = new World())
        {
            File.WriteAllText(empty.State, "");
            empty.Service = (_, index) => { if (index == 2) File.WriteAllText(empty.State, "stopped"); return 1; };
            empty.Run("generic", false).ShouldBe(0);
            empty.Starts.Count.ShouldBe(2, "an empty state file is running");
            empty.Lines.ShouldContain(l => l.EndsWith("[generic] Restarting in 3 s...", StringComparison.Ordinal));
        }
    }

    [Test]
    public void A_launch_error_is_logged_and_supervision_goes_on()
    {
        using var w = new World();
        w.Starter = _ =>
        {
            File.WriteAllText(w.State, "stopped");
            throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified");
        };
        w.Run("generic", false).ShouldBe(0);
        w.Records.Select(Event).ToArray().ShouldBe(["launch-requested", "launch-error", "supervisor-stopping"]);
        w.Records[1].GetProperty("exitCode").GetInt32().ShouldBe(1);
        w.Lines.ShouldContain(l => l.EndsWith("[generic] [ERR] Failed to start: The system cannot find the file specified", StringComparison.Ordinal));
    }

    [Test]
    public void A_gone_supervisor_ends_supervision_once_the_service_exits()
    {
        using var w = new World();
        File.WriteAllText(w.State, "running");
        w.Service = (_, _) =>
        {
            w.Processes.Exit(Supervisor);
            return 0;
        };
        w.Run("session-runner", false).ShouldBe(0);
        w.Starts.Count.ShouldBe(1, "a supervisor that is gone is never relaunched for");
        w.Lines[^1].ShouldEndWith("[session-runner] Supervisor PID 900002 is gone - stopping.");
        Event(w.Records[^1]).ShouldBe("supervisor-stopping");

        // A reused id (another start time) is gone too.
        using var reused = new World();
        reused.Service = (_, index) =>
        {
            reused.Processes.Start(Supervisor, SupervisorStart.AddMinutes(1));
            if (index == 2) File.WriteAllText(reused.State, "stopped");
            return 0;
        };
        reused.Run("generic", false).ShouldBe(0);
        reused.Starts.Count.ShouldBe(1);
    }

    [Test]
    public void The_options_require_the_service_files_and_split_the_arguments()
    {
        string[] every = ["--name", "n", "--work-dir", "w", "--exe", "e", "--log-file", "l", "--service-pid-file", "p", "--state-file", "s"];
        var options = DaemonSupervisorOptions.Parse(every, "slot.ps1");
        (options.LogMaxMb, options.LogRetainDays, options.LogRetainCount, options.SupervisorPid, options.BuildProjectDir, options.BuildSlotScript)
            .ShouldBe((20, 5, 10, (int?)null, "", "slot.ps1"));
        options.ExeArgList.ShouldBeEmpty();
        DaemonSupervisorOptions.Parse([.. every, "--exe-args", " --urls\thttp://localhost:17204 "], "slot.ps1").ExeArgList.ShouldBe(["--urls", "http://localhost:17204"]);
        foreach (var flag in new[] { "--name", "--work-dir", "--exe", "--log-file", "--service-pid-file", "--state-file" })
        {
            var without = every.ToList();
            without.RemoveRange(without.IndexOf(flag), 2);
            Should.Throw<NightlyUsageException>(() => DaemonSupervisorOptions.Parse(without, "slot.ps1")).Message.ShouldBe("missing " + flag);
        }
        Should.Throw<NightlyUsageException>(() => DaemonSupervisorOptions.Parse([.. every, "--supervisor-pid", "0"], "slot.ps1"))
            .Message.ShouldBe("--supervisor-pid expects a process id above 0");
        Should.Throw<NightlyUsageException>(() => DaemonSupervisorOptions.Parse([.. every, "--log-max-mb", "big"], "slot.ps1"))
            .Message.ShouldBe("--log-max-mb expects an integer, not 'big'");
    }

    // ---- CARD-0043 rotation at (re)launch (the DaemonLogRotationTests rows; a service that writes and stops itself) ----

    private static World RotationWorld()
    {
        var w = new World(DateTimeOffset.UtcNow);
        File.WriteAllText(w.State, "running");
        w.Service = (_, _) =>
        {
            File.AppendAllText(w.Log, "service-ran" + Environment.NewLine);
            File.WriteAllText(w.State, "stopped");
            return 0;
        };
        return w;
    }

    private static string[] Rolls(World w) => Directory.GetFiles(w.Fixture.Root)
        .Where(f => Regex.IsMatch(Path.GetFileName(f), @"^svc\.\d{8}-\d{6}\.log$")).OrderBy(f => f, StringComparer.Ordinal).ToArray();

    [Test]
    public void An_oversized_capture_log_is_rolled_aside_before_the_service_starts()
    {
        using var w = RotationWorld();
        File.WriteAllBytes(w.Log, new byte[3 * 1024 * 1024]);
        w.Run("svc", false, "--log-max-mb", "1").ShouldBe(0);
        var roll = Rolls(w).ShouldHaveSingleItem("the oversized log should have been rolled aside exactly once");
        new FileInfo(roll).Length.ShouldBeGreaterThanOrEqualTo(3 * 1024 * 1024);
        var current = File.ReadAllText(w.Log);
        current.ShouldContain("Rotated log at 3.0 MB -> " + Path.GetFileName(roll));
        current.ShouldContain("service-ran");
    }

    [Test]
    public void An_undersized_capture_log_is_left_alone()
    {
        using var w = RotationWorld();
        File.WriteAllText(w.Log, "keep-me\n");
        w.Run("svc", false, "--log-max-mb", "20").ShouldBe(0);
        Rolls(w).ShouldBeEmpty();
        File.ReadAllText(w.Log).ShouldContain("keep-me");
    }

    [Test]
    public void Rolls_beyond_the_retained_count_are_pruned()
    {
        using var w = RotationWorld();
        File.WriteAllBytes(w.Log, new byte[2 * 1024 * 1024]);
        // Three older rolls, oldest first; with a retain count of 2 the rotation adds a fourth and only the two newest stay.
        var stamps = new[] { "20260810-010101", "20260811-010101", "20260812-010101" };
        for (var i = 0; i < stamps.Length; i++)
        {
            var roll = w.Fixture.PathOf($"svc.{stamps[i]}.log");
            File.WriteAllText(roll, stamps[i]);
            File.SetLastWriteTimeUtc(roll, DateTime.UtcNow.AddMinutes(-30 + i));
        }
        w.Run("svc", false, "--log-max-mb", "1", "--log-retain-count", "2").ShouldBe(0);
        var rolls = Rolls(w).Select(Path.GetFileName).ToArray();
        rolls.Length.ShouldBe(2);
        rolls.ShouldContain("svc.20260812-010101.log");
        rolls.ShouldNotContain("svc.20260811-010101.log");
        rolls.ShouldNotContain("svc.20260810-010101.log");
    }

    [Test]
    public void Rolls_older_than_the_retention_window_are_pruned()
    {
        using var w = RotationWorld();
        File.WriteAllBytes(w.Log, new byte[2 * 1024 * 1024]);
        var stale = w.Fixture.PathOf("svc.20260601-010101.log");
        File.WriteAllText(stale, "stale");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-6));
        var recent = w.Fixture.PathOf("svc.20260812-010101.log");
        File.WriteAllText(recent, "recent");
        File.SetLastWriteTimeUtc(recent, DateTime.UtcNow.AddDays(-1));
        w.Run("svc", false, "--log-max-mb", "1", "--log-retain-days", "5").ShouldBe(0);
        File.Exists(stale).ShouldBeFalse("a roll older than --log-retain-days must be pruned");
        File.Exists(recent).ShouldBeTrue("a roll inside the retention window must be kept");
    }

    [Test]
    public void A_zero_cap_neither_rotates_nor_prunes()
    {
        using var w = RotationWorld();
        File.WriteAllBytes(w.Log, new byte[2 * 1024 * 1024]);
        var stale = w.Fixture.PathOf("svc.20260601-010101.log");
        File.WriteAllText(stale, "stale");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-30));
        w.Run("svc", false, "--log-max-mb", "0").ShouldBe(0);
        Rolls(w).ShouldBe([stale]);
    }

    // ---- the production starter: a real .cmd service under cmd.exe in the temporary root ----

    [Test]
    public void A_real_cmd_service_appends_to_the_log_and_receives_the_startup_correlation()
    {
        if (!OperatingSystem.IsWindows())
            Skip.Test("CARD-1669: the service runs under cmd.exe with a >> capture, which exists only on Windows");
        using var w = new World();
        File.WriteAllText(w.State, "running");
        File.WriteAllText(w.Fixture.PathOf("svc.cmd"), "@echo off\r\n"
            + "echo %ANTIPHON_STARTUP_ATTEMPT%,%ANTIPHON_STARTUP_SUPERVISOR_PID%,%ANTIPHON_STARTUP_SUPERVISOR_START%>> \"" + w.Fixture.PathOf("children.txt") + "\"\r\n"
            + "echo service-ran\r\n"
            + "echo stopped> \"" + w.State + "\"\r\n");
        var before = Environment.GetEnvironmentVariable("ANTIPHON_STARTUP_ATTEMPT");
        w.Starter = DaemonSupervisor.StartService;
        w.Run("session-runner", true).ShouldBe(0);

        Environment.GetEnvironmentVariable("ANTIPHON_STARTUP_ATTEMPT").ShouldBe(before, "the parent environment is unchanged");
        var launch = w.Records.Single(r => Event(r) == "wrapper-started");
        launch.GetProperty("wrapperPid").GetInt32().ShouldBeGreaterThan(0);
        launch.GetProperty("wrapperPid").GetInt32().ShouldNotBe(Supervisor);
        launch.GetProperty("wrapperStartTimeUtc").GetString()!.ShouldEndWith("Z");
        File.ReadAllLines(w.Fixture.PathOf("children.txt")).ShouldBe([launch.GetProperty("attemptId").GetString() + "," + Supervisor + ",2026-09-07T09:58:00.0000000Z"]);
        w.Lines.ShouldContain("service-ran", "the service's stdout is appended to the log by the >> capture");
        w.Records.Single(r => Event(r) == "service-exit").GetProperty("exitCode").GetInt32().ShouldBe(0);
        File.Exists(w.Pid).ShouldBeFalse();
    }
}
