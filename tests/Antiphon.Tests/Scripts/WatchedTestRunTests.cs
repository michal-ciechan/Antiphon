using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0165/0222: the watched test run (Antiphon.Nightly.WatchedTestRun, which replaced scripts/run-tests-watched.ps1 in unit 38 of
/// the 2026-11-22 plan; the script had no driver) in process. The test executable is the Machine's fake child, which writes its
/// log into a mock file system as the fake clock moves; its CPU samples, the process table, the diagnostic commands (docker,
/// dotnet-stack, dotnet-dump) and the kills are recorded. A run that exits keeps its code; a quiet, idle run is a stall whose
/// evidence is recorded before its tree is killed (exit 124); a busy quiet run is no stall until the hard timeout. Nothing starts.
/// </summary>
[Category("Unit")]
public sealed class WatchedTestRunTests
{
    private static readonly string Bin = OperatingSystem.IsWindows() ? @"C:\fixture\tests\bin-watch" : "/fixture/tests/bin-watch";
    private static readonly string Exe = Path.Combine(Bin, "Antiphon.Tests.exe");
    private static readonly string Out = OperatingSystem.IsWindows() ? @"C:\fixture\logs\watched" : "/fixture/logs/watched";
    private const int Pid = 4242;

    [Test]
    public void A_run_that_exits_keeps_its_code_and_notes_its_progress()
    {
        var machine = new Machine { ExitAfterSeconds = 60, ExitCode = 3, CpuPerSecond = 0.5 };
        machine.Disk.AddFile(Path.Combine(Out, "app.progress"), new MockFileData("an earlier run\n"));
        var run = Watch(machine, "--tag", "app", "--filter", "/*/Antiphon.Tests.Application/*/*", "--detailed", "--env-vars", "ANTIPHON_A=1;ANTIPHON_B=x=y");

        run.Exit.ShouldBe(3, run.Output);
        var start = machine.Started.ShouldNotBeNull();
        start.FileName.ShouldBe(Exe);
        start.WorkingDirectory.ShouldBe(Bin);
        start.Arguments.ShouldBe(["--treenode-filter", "/*/Antiphon.Tests.Application/*/*", "--no-progress", "--no-ansi", "--output", "Detailed"]);
        start.Environment.Select(variable => variable.Key + "=" + variable.Value).ShouldBe(["ANTIPHON_A=1", "ANTIPHON_B=x=y"]);
        (start.StandardOutputPath, start.StandardErrorPath).ShouldBe((Path.Combine(Out, "app.log"), Path.Combine(Out, "app.err")));

        var progress = machine.Disk.File.ReadAllText(Path.Combine(Out, "app.progress")).ReplaceLineEndings("\n");
        progress.ShouldNotContain("an earlier run");
        progress.ShouldStartWith("[env] ANTIPHON_A=1\n[env] ANTIPHON_B=x=y\n[app] started pid=4242 at 08:00:00 exe=" + Exe + " filter=/*/Antiphon.Tests.Application/*/*\n");
        progress.ShouldContain("08:00:20 t=20s log=160 quiet=0s cpu=10 dcpu=10 ws=512MB thr=31 pg=37/100\n");
        progress.ShouldContain("08:00:40 t=40s log=320 quiet=0s cpu=20 dcpu=10 ws=512MB thr=31 pg=37/100\n");
        progress.ShouldContain("[app] EXITED code=3 after 60s\n");
        progress.ShouldContain("--- stdout tail (" + Path.Combine(Out, "app.log") + ", 480 bytes):\n");
        progress.ShouldEndWith("passed Test12 " + new string('.', 25) + "\n--- stderr tail:\nwarning: slow\n");
        progress.Split('\n').Count(line => line.StartsWith("passed ", StringComparison.Ordinal)).ShouldBe(12, "the last 12 log lines");
        run.Output.ReplaceLineEndings("\n").ShouldBe(progress, "every note goes to stdout and the progress file");
        machine.Kills.ShouldBeEmpty();
        machine.Commands.ShouldNotContain(command => command.StartsWith("dotnet-", StringComparison.Ordinal));
    }

    [Test]
    public void A_quiet_idle_run_is_a_stall_whose_evidence_is_recorded_before_its_tree_is_killed()
    {
        var machine = new Machine { GrowsLog = false, CpuPerSecond = 0 };
        var run = Watch(machine);

        run.Exit.ShouldBe(WatchedTestRun.StalledExitCode, run.Output);
        var progress = machine.Disk.File.ReadAllText(Path.Combine(Out, "run.progress")).ReplaceLineEndings("\n");
        progress.ShouldContain("08:02:40 t=160s log=0 quiet=155s cpu=0 dcpu=0 ws=512MB thr=31 pg=37/100\n");
        progress.ShouldContain("[run] STALLED after 160s; stdout last grew 155s ago\n");
        progress.ShouldContain("  proc pid=4242 parent=100 name=Antiphon.Tests.exe cpu=0 threads=31 cmd=" + Exe + " --treenode-filter /*/*/*/*\n");
        progress.ShouldContain("  proc pid=4300 parent=4242 name=dotnet.exe cpu=0 threads=31 cmd=dotnet testhost.dll\n");
        progress.ShouldContain("    child pid=4400 name=conhost.exe cmd=\n");
        progress.ShouldContain("  cpu before 10s: 0 ; after: 0\n");
        progress.ShouldContain("  docker: antiphon-test-pg postgres:16-alpine Up 3 minutes | ryuk testcontainers/ryuk Up 3 minutes\n");
        progress.ShouldContain("  pg_stat_activity -> run-pg-activity.txt\n");
        progress.ShouldContain("  managed thread stacks -> run-stack.txt\n");
        progress.ShouldContain("  Complete\n  dump -> run.dmp  (analyze: dotnet-dump analyze run.dmp -c dumpasync -c exit)\n  killed\n");
        machine.Disk.File.ReadAllText(Path.Combine(Out, "run-pg-activity.txt")).ShouldBe("88|active|Lock|relation|select 1\n");
        machine.Disk.File.ReadAllText(Path.Combine(Out, "run-stack.txt")).ShouldBe("Thread 1: Main\nstack warning\n");
        machine.Commands.ShouldContain("dotnet-dump collect -p 4242 -o " + Path.Combine(Out, "run.dmp"));
        // The dump is taken before anything is killed, and the tree goes highest pid first; the grandchild is not the run's to kill.
        machine.Events.IndexOf("kill 4300").ShouldBeGreaterThan(machine.Events.IndexOf("dotnet-dump collect -p 4242 -o " + Path.Combine(Out, "run.dmp")));
        machine.Kills.ShouldBe([4300, Pid]);
    }

    [Test]
    public void A_busy_quiet_run_is_no_stall_until_the_hard_timeout()
    {
        var machine = new Machine { GrowsLog = false, CpuPerSecond = 0.5 };
        var run = Watch(machine, "--hard-timeout-sec", "300", "--quiet-sec", "60");

        run.Exit.ShouldBe(WatchedTestRun.StalledExitCode, run.Output);
        var progress = machine.Disk.File.ReadAllText(Path.Combine(Out, "run.progress")).ReplaceLineEndings("\n");
        progress.ShouldContain("08:04:00 t=240s log=0 quiet=235s cpu=120 dcpu=10 ws=512MB thr=31 pg=37/100\n");
        progress.ShouldContain("[run] hard timeout\n[run] STALLED after 300s; stdout last grew 295s ago\n");
        machine.Kills.ShouldBe([4300, Pid]);
    }

    [Test]
    public void Bad_input_is_a_usage_error_before_anything_starts()
    {
        Should.Throw<NightlyUsageException>(() => WatchedTestRunOptions.Parse([], Root, Bin)).Message.ShouldContain("--exe");
        Should.Throw<NightlyUsageException>(() => WatchedTestRunOptions.Parse(["--exe", Exe, "--env-vars", "A=1;=2"], Root, Bin)).Message
            .ShouldContain("'=2' is not NAME=VALUE");
        Should.Throw<NightlyUsageException>(() => WatchedTestRunOptions.Parse(["--exe", Exe, "--cpu-floor", "low"], Root, Bin));
        var machine = new Machine();
        machine.Disk.RemoveFile(Exe);
        Should.Throw<NightlyUsageException>(() => Watch(machine)).Message.ShouldContain("--exe names no file");
        machine.Started.ShouldBeNull();
    }

    [Test]
    public void The_options_are_the_scripts_parameters_in_kebab_case_and_the_docs_name_the_command()
    {
        var defaults = WatchedTestRunOptions.Parse(["--exe", "Antiphon.Tests.exe"], Root, Bin);
        (defaults.Exe, defaults.Filter, defaults.Tag, defaults.OutDir, defaults.HardTimeoutSec, defaults.QuietSec, defaults.CpuFloor, defaults.SampleSec, defaults.Detailed)
            .ShouldBe((Exe, "/*/*/*/*", "run", Path.Combine(Root, "logs", "watched"), 3600, 150, 1.0, 20, false));
        defaults.EnvVars.ShouldBeEmpty();
        WatchedTestRunOptions.Parse(["--exe", Exe, "--cpu-floor", "0.25"], Root, Bin).CpuFloor.ShouldBe(0.25);

        var repo = DockerStackDocuments.RepoRoot;
        File.Exists(Path.Combine(repo, "scripts", "run-tests-watched.ps1")).ShouldBeFalse("the watched run is a command");
        File.ReadAllBytes(Path.Combine(repo, "scripts", "lib", "Antiphon.Nightly", "WatchedTestRun.cs")).ShouldAllBe(b => b < 128, "ASCII only");
        File.ReadAllText(Path.Combine(repo, "docs", "testing-and-build.md")).ShouldContain("scripts\\antiphon.cmd run-tests-watched --exe");
    }

    // ---- the machine ----

    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\fixture" : "/fixture";

    private static (int Exit, string Output) Watch(Machine machine, params string[] args)
    {
        var output = new StringWriter();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-24T08:00:00Z"));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        machine.Clock = clock;
        var host = new NightlyHost
        {
            Clock = clock,
            Git = new FakeGit().Run,
            Launcher = new RecordingLauncher(),
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = output,
            Trace = TextWriter.Null,
            Sleep = span =>
            {
                clock.Advance(span);
                machine.Tick();
            },
        };
        var options = WatchedTestRunOptions.Parse(["--exe", Exe, "--out-dir", Out, .. args], Root, Bin);
        var exit = WatchedTestRun.Run(options, host, machine.Disk, machine);
        return (exit, output.ToString());
    }

    /// <summary>
    /// The machine: the test executable (it prints one 40-byte line every five seconds while GrowsLog, burns CpuPerSecond, and exits
    /// with ExitCode once ExitAfterSeconds have passed; 0 never), its child and grandchild, and docker's answers.
    /// </summary>
    private sealed class Machine : IWatchedTestRunSeams
    {
        public MockFileSystem Disk { get; } = new();
        public FakeTimeProvider Clock { get; set; } = new();
        public bool GrowsLog { get; init; } = true;
        public double CpuPerSecond { get; init; }
        public int ExitAfterSeconds { get; init; }
        public int ExitCode { get; init; }
        public WatchedChildStart? Started { get; private set; }
        public List<string> Commands { get; } = [];
        public List<int> Kills { get; } = [];
        public List<string> Events { get; } = [];
        private DateTimeOffset _start;
        private int _lines;
        private bool _killed;

        public Machine() => Disk.AddFile(Exe, new MockFileData("MZ"));

        private double Elapsed => (Clock.GetUtcNow() - _start).TotalSeconds;

        private bool Exited => _killed || (ExitAfterSeconds > 0 && Elapsed >= ExitAfterSeconds);

        public IWatchedChild Start(WatchedChildStart start)
        {
            Started = start;
            _start = Clock.GetUtcNow();
            Disk.AddFile(start.StandardOutputPath, new MockFileData(""));
            Disk.AddFile(start.StandardErrorPath, new MockFileData("warning: slow\n"));
            return new Child(this);
        }

        // Every five seconds of the clock: one more log line while the run prints.
        public void Tick()
        {
            if (Started == null || !GrowsLog || Exited && _lines >= ExitAfterSeconds / 5) return;
            _lines++;
            Disk.File.AppendAllText(Started.StandardOutputPath, $"passed Test{_lines:D2} " + new string('.', 25) + "\n");
        }

        public WatchedSample? Sample(int pid) =>
            pid == Pid || pid == 4300 ? new WatchedSample(Math.Round(Elapsed * CpuPerSecond, 3), 512L * 1024 * 1024, 31) : null;

        public IReadOnlyList<WatchedProcess> Processes() =>
        [
            new(100, 4, "explorer.exe", "explorer.exe"),
            new(Pid, 100, "Antiphon.Tests.exe", Exe + " --treenode-filter /*/*/*/*"),
            new(4300, Pid, "dotnet.exe", "dotnet testhost.dll"),
            new(4400, 4300, "conhost.exe", ""),
        ];

        public WatchedCommand Run(string file, IReadOnlyList<string> arguments)
        {
            var command = file + " " + string.Join(" ", arguments);
            Commands.Add(command);
            Events.Add(command);
            if (command.StartsWith("docker ps --filter ancestor=postgres:16-alpine", StringComparison.Ordinal)) return new(0, "c0ffee\n", "");
            if (command.StartsWith("docker exec c0ffee psql -U test -d antiphon_test -Atc select count(*)", StringComparison.Ordinal)) return new(0, "37/100\n", "");
            if (command.StartsWith("docker exec c0ffee psql -U test -d antiphon_test -Atc select pid, state", StringComparison.Ordinal))
                return new(0, "88|active|Lock|relation|select 1\n", "");
            if (command.StartsWith("docker ps -a --filter label=org.testcontainers", StringComparison.Ordinal))
                return new(0, "antiphon-test-pg postgres:16-alpine Up 3 minutes\nryuk testcontainers/ryuk Up 3 minutes\n", "");
            if (command.StartsWith("dotnet-stack report -p 4242", StringComparison.Ordinal)) return new(0, "Thread 1: Main\n", "stack warning\n");
            if (command.StartsWith("dotnet-dump collect -p 4242", StringComparison.Ordinal)) return new(0, "Writing full to run.dmp\nComplete\n", "");
            return new(-1, "", "unexpected " + command);
        }

        public void Kill(int pid)
        {
            Kills.Add(pid);
            Events.Add("kill " + pid);
            if (pid == Pid) _killed = true;
        }

        private sealed class Child(Machine machine) : IWatchedChild
        {
            public int Id => Pid;
            public bool HasExited => machine.Exited;
            public int ExitCode => machine.ExitCode;
            public void Dispose() { }
        }
    }
}
