using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1507: the recorded stop (QualificationStop, `antiphon-scripts qualification-stop`), in-process. These cases replaced the
/// C1507 driver scripts/test-qualification-stop.ps1 (plan 2026-11-22-remaining-ps1-to-csharp.md, unit 7). Task and
/// coordination roots are real directories under a temp root; the recorded pids are a QualificationProcessWorld and the tree
/// stop a QualificationTestHost that marks the killed tree absent, so no taskkill runs and no real process is stopped.
/// Not ported, PowerShell mechanics: C1507_TaskDirCopyLoadsCheckoutLibrary and C1507_NonCheckoutRootRefusedBeforeImport
/// (CARD-2045: a task-directory copy of stop-supervisor.ps1 dot-sourcing its helpers from -CheckoutRoot; the stop is now
/// compiled into the CLI that runs it, so no helper is loaded from a checkout and there is no -CheckoutRoot to refuse), and
/// the stop scripts' ASCII check (kept over the C# sources in QualificationSupervisorTests).
/// </summary>
[Category("Unit")]
public sealed class QualificationStopTests
{
    private const string Run = "c1507run";

    private sealed class Stop : IDisposable
    {
        public readonly QualificationWorld World = QualificationWorld.Create("c1507");
        private int _fixtures;

        public sealed record Fixture(string Task, string Coord, string Sentinel)
        {
            public string Lock => Path.Combine(Coord, "native-run.lock");
            public string Log => Path.Combine(Task, "supervisor.log");
        }

        /// <summary>New-C1507Fixture: the recorded supervisor and driver (with the run id), the native lock, sentinel last-run.json files.</summary>
        public Fixture New(int? supervisor, int? driver, int? lockOwner, string lockRunId = Run, string? supervisorCommandLine = null, string runId = Run)
        {
            var name = "f" + (++_fixtures).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var task = World.Directory(name);
            var coord = World.Directory(name + "-coord");
            JsonObject Owner(int pid) => new() { ["pid"] = pid, ["processStartedAt"] = QualificationProcessWorld.StartedText(pid) };
            if (supervisor is { } s)
            {
                var identity = Owner(s);
                identity["runId"] = runId;
                if (supervisorCommandLine != null) identity["commandLine"] = supervisorCommandLine;
                QualificationWorld.WriteJson(Path.Combine(task, "supervisor-identity.json"), identity.ToJsonString());
            }
            if (driver is { } d)
            {
                var child = Owner(d);
                child["runId"] = runId;
                QualificationWorld.WriteJson(Path.Combine(task, "launch-child.json"), child.ToJsonString());
            }
            if (lockOwner is { } l)
            {
                var record = Owner(l);
                record["runId"] = lockRunId;
                record["lane"] = "rc";
                QualificationWorld.WriteJson(Path.Combine(coord, "native-run.lock"), record.ToJsonString());
            }
            const string sentinel = "{\"runId\":\"c1507run\",\"phase\":\"tests\"}";
            File.WriteAllText(Path.Combine(task, "last-run.json"), sentinel);
            File.WriteAllText(Path.Combine(coord, "last-run.json"), sentinel);
            return new Fixture(task, coord, sentinel);
        }

        /// <summary>The tree stop of a world: the pid and its children (children[pid]) become absent.</summary>
        public void TreeStop(Dictionary<int, int[]>? children = null)
        {
            World.Seams.OnStop = pid =>
            {
                var pending = new Queue<int>([pid]);
                var seen = new HashSet<int>();
                while (pending.Count > 0)
                {
                    var current = pending.Dequeue();
                    if (!seen.Add(current)) continue;
                    World.Processes.Set(current, "absent");
                    if (children != null && children.TryGetValue(current, out var kids)) foreach (var kid in kids) pending.Enqueue(kid);
                }
                return new QualificationTreeStop(true, "stopped", QualificationTreeStop.CommandFor(pid));
            };
        }

        public QualificationStopResult Invoke(Fixture fixture, string reason = "operator replacement", string evidenceRoot = "", int census = 0) =>
            QualificationStop.Stop(new QualificationStopOptions
            {
                TaskRoot = fixture.Task, CoordinationRoot = fixture.Coord, RunId = Run, Reason = reason, EvidenceRoot = evidenceRoot, Confirm = true, CensusMilliseconds = census,
            }, World.Host(), World.Files);

        public void Dispose() => World.Dispose();
    }

    private static string Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "missing";

    [Test]
    public void C1507_StopOrder()
    {
        using var stop = new Stop();
        var world = stop.World;
        var rows = new NightlyCaseRows();
        world.Processes.Set(4100, "alive").Set(4200, "alive").Set(4300, "absent");
        stop.TreeStop(new() { [4100] = [4200] });
        var result = stop.Invoke(stop.New(4100, 4200, 4300));
        rows.Check(result.ExitCode == 0 && world.Seams.Stops.SequenceEqual([4100]) && result.Kills.Count == 1 && result.LockAction == "moved",
            "stop kills the supervisor tree once when the driver is its child and never the lock owner", string.Join(",", world.Seams.Stops) + " " + result.Detail);
        rows.Check(result.Kills[0].Phase == "supervisor" && result.Kills[0].Command == "taskkill.exe /PID 4100 /T /F",
            "recorded stop command is taskkill /T /F and the child driver is not killed again", result.Kills[0].Command);

        world.Seams.Stops.Clear();
        world.Processes.Set(4100, "alive").Set(4200, "alive").Set(4300, "absent");
        stop.TreeStop();
        var outlived = stop.Invoke(stop.New(4100, 4200, 4300));
        rows.Check(outlived.ExitCode == 0 && world.Seams.Stops.SequenceEqual([4100, 4200]) && outlived.Kills.Count == 2 && outlived.Kills[1].Phase == "driver"
            && outlived.Kills[1].Command == "taskkill.exe /PID 4200 /T /F" && outlived.LockAction == "moved",
            "a driver that outlived the supervisor tree gets the second taskkill and the lock moves", string.Join(",", world.Seams.Stops) + " " + outlived.Detail);

        world.Seams.Stops.Clear();
        world.Processes.Set(4100, "reused").Set(4200, "alive");
        var reused = stop.New(4100, 4200, 4300);
        var lockBefore = Hash(reused.Lock);
        var refused = stop.Invoke(reused);
        rows.Check(refused.ExitCode == 2 && world.Seams.Stops.Count == 0 && !File.Exists(reused.Log) && Hash(reused.Lock) == lockBefore,
            "a reused supervisor pid is not killed and the lock stays", refused.Detail);

        var hits = 0;
        world.Processes.Set(4100, "alive").Set(4200, "alive");
        world.Processes.OnProbe = pid =>
        {
            if (pid == 4100 && ++hits >= 2) world.Processes.Set(4100, "reused");
        };
        var flipped = stop.Invoke(stop.New(4100, 4200, null));
        world.Processes.OnProbe = null;
        rows.Check(flipped.ExitCode == 2 && flipped.Detail == "supervisor-generation" && world.Seams.Stops.Count == 0,
            "a supervisor generation change immediately before the kill aborts with no kill", flipped.Detail);

        world.Processes.Set(4100, "absent").Set(4200, "alive");
        stop.TreeStop();
        var onlyDriver = stop.Invoke(stop.New(4100, 4200, null));
        rows.Check(onlyDriver.ExitCode == 0 && world.Seams.Stops.SequenceEqual([4200]) && onlyDriver.Kills[0].Phase == "driver",
            "an already-dead supervisor still stops the recorded driver", string.Join(",", world.Seams.Stops) + " " + onlyDriver.Detail);

        world.Seams.Stops.Clear();
        world.Processes.Set(4100, "alive").Set(4200, "alive");
        var foreign = stop.Invoke(stop.New(4100, 4200, null, runId: "other-run"));
        rows.Check(foreign.ExitCode == 2 && world.Seams.Stops.Count == 0 && foreign.Detail == "supervisor-run", "a supervisor identity for another run id authorizes no kill", foreign.Detail);
        rows.ShouldAllPass(7);
    }

    [Test]
    public void C1507_LockClear()
    {
        using var stop = new Stop();
        var world = stop.World;
        var rows = new NightlyCaseRows();
        world.Seams.OnStop = pid => throw new InvalidOperationException("unexpected stop " + pid);

        var cleared = stop.New(3101, 3102, 3103);
        var before = Hash(cleared.Lock);
        var result = stop.Invoke(cleared);
        var moved = Directory.Exists(Path.Combine(cleared.Task, "stop-evidence")) ? Directory.GetFiles(Path.Combine(cleared.Task, "stop-evidence")) : Array.Empty<string>();
        rows.Check(result.ExitCode == 0 && result.LockAction == "moved" && moved.Length == 1 && Hash(moved[0]) == before && !File.Exists(cleared.Lock)
            && result.RetainedLockPath == moved[0], "a dead native-lock owner is moved aside and the original path is absent", result.Detail);

        world.Processes.Set(3200, "alive");
        var alive = stop.New(3201, 3202, 3200);
        var aliveBefore = Hash(alive.Lock);
        var sentinel = Hash(Path.Combine(alive.Task, "last-run.json"));
        result = stop.Invoke(alive);
        rows.Check(result.ExitCode == 4 && result.LockAction == "retained-alive" && Hash(alive.Lock) == aliveBefore && Hash(Path.Combine(alive.Task, "last-run.json")) == sentinel,
            "a live native-lock owner keeps the lock and last-run.json", result.LockAction);

        foreach (var status in new[] { "unknown", "reused" })
        {
            world.Processes.Set(4300, status);
            var held = stop.New(4100, 4200, 4300);
            var heldBefore = Hash(held.Lock);
            result = stop.Invoke(held);
            rows.Check(result.ExitCode == 4 && result.LockAction == "retained-" + status && Hash(held.Lock) == heldBefore,
                status == "unknown" ? "an unknown native-lock owner keeps the lock" : "a reused native-lock pid keeps the lock", result.Detail);
        }

        world.Processes.Set(4300, "absent");
        var foreign = stop.New(4100, 4200, 4300, lockRunId: "other-run");
        var foreignBefore = Hash(foreign.Lock);
        result = stop.Invoke(foreign);
        rows.Check(result.ExitCode == 4 && result.LockAction == "retained-foreign" && Hash(foreign.Lock) == foreignBefore, "a native lock for another run is not cleared", result.Detail);

        world.Processes.Set(4100, "alive").Set(4200, "absent");
        var changed = stop.New(4100, 4200, 4300);
        world.Seams.OnStop = pid =>
        {
            File.WriteAllText(changed.Lock, "{\"pid\":4300,\"processStartedAt\":\"" + QualificationProcessWorld.StartedText(4300) + "\",\"runId\":\"c1507run\",\"lane\":\"replaced\"}");
            world.Processes.Set(pid, "absent");
            return new QualificationTreeStop(true, "stopped", QualificationTreeStop.CommandFor(pid));
        };
        result = stop.Invoke(changed);
        rows.Check(result.ExitCode == 4 && result.LockAction == "retained-changed" && File.Exists(changed.Lock), "a native lock whose bytes change during the stop is not moved", result.Detail);

        world.Processes.Set(4100, "alive").Set(4200, "alive");
        world.Seams.OnStop = pid =>
        {
            if (pid == 4100) world.Processes.Set(4100, "absent");
            return new QualificationTreeStop(true, "stopped", QualificationTreeStop.CommandFor(pid));
        };
        var survivor = stop.New(4100, 4200, 4300);
        var survivorBefore = Hash(survivor.Lock);
        result = stop.Invoke(survivor);
        rows.Check(result.ExitCode == 3 && result.Outcome == "incomplete" && Hash(survivor.Lock) == survivorBefore, "a driver that survives the stop keeps the native lock", result.Detail);

        world.Seams.OnStop = pid => throw new InvalidOperationException("unexpected stop " + pid);
        var missing = stop.New(3301, 3302, null);
        result = stop.Invoke(missing);
        rows.Check(result.ExitCode == 0 && result.LockAction == "absent" && !Directory.Exists(Path.Combine(missing.Task, "stop-evidence")),
            "a missing native lock is already clear and creates no evidence directory", result.Detail);
        rows.ShouldAllPass(8);
    }

    [Test]
    public void C1507_RunLog()
    {
        using var stop = new Stop();
        var rows = new NightlyCaseRows();
        var fixture = stop.New(3101, 3101, null);
        var taskHash = Hash(Path.Combine(fixture.Task, "last-run.json"));
        var coordHash = Hash(Path.Combine(fixture.Coord, "last-run.json"));
        var result = stop.Invoke(fixture);
        var log = File.Exists(fixture.Log) ? File.ReadAllText(fixture.Log) : "";
        rows.Check(result.ExitCode == 0 && log.Contains("STOP reason=\"operator replacement\"", StringComparison.Ordinal) && log.Contains("outcome=stopped", StringComparison.Ordinal)
            && log.EndsWith("\r\n", StringComparison.Ordinal) && result.LogPath == fixture.Log, "the stop reason is appended to the supervisor run log", log);
        rows.Check(Hash(Path.Combine(fixture.Task, "last-run.json")) == taskHash && Hash(Path.Combine(fixture.Coord, "last-run.json")) == coordHash,
            "the stop does not rewrite last-run.json");
        var blank = stop.New(3101, 3101, null);
        result = stop.Invoke(blank, "  ");
        rows.Check(result.ExitCode == 2 && result.Detail == "reason" && !File.Exists(blank.Log), "a blank stop reason is refused before any log write", result.Detail);
        var broken = stop.New(3101, 3101, null);
        result = stop.Invoke(broken, "operator\nreplacement");
        rows.Check(result.ExitCode == 2 && !File.Exists(broken.Log), "a multi-line stop reason is refused before any log write", result.Detail);
        var longReason = stop.New(3101, 3101, null);
        rows.Check(stop.Invoke(longReason, new string('r', 201)).Detail == "reason" && stop.Invoke(longReason, "ok").ExitCode == 0
            && QualificationStop.Stop(new QualificationStopOptions { TaskRoot = longReason.Task, CoordinationRoot = longReason.Coord, RunId = "../x", Reason = "ok", Confirm = true },
                stop.World.Host(), stop.World.Files).Detail == "run-id", "an over-long reason and a malformed run id are refused");
        rows.ShouldAllPass(5);
    }

    // Invoke-QualificationWindowsTreeStop off Windows refuses (windows-only) and records taskkill /T /F; on Windows the refused
    // pids (0 and this process) never reach taskkill. No taskkill runs here.
    [Test]
    public void C1507_WindowsTreeStopRefusals()
    {
        var os = new QualificationOs();
        var zero = os.StopTree(0);
        zero.Ok.ShouldBeFalse();
        zero.Command.ShouldBe("taskkill.exe /PID 0 /T /F");
        var self = os.StopTree(Environment.ProcessId);
        self.Ok.ShouldBeFalse();
        if (OperatingSystem.IsWindows())
        {
            zero.Reason.ShouldBe("refused-pid");
            self.Reason.ShouldBe("refused-pid");
        }
        else
        {
            os.StopTree(12345).ShouldBe(new QualificationTreeStop(false, "windows-only", "taskkill.exe /PID 12345 /T /F"));
        }
    }

    [Test]
    public void C1507_TreeDescendant()
    {
        using var stop = new Stop();
        var world = stop.World;
        var rows = new NightlyCaseRows();
        world.Processes.Set(4100, "alive").Set(4200, "alive").Set(4300, "absent");
        stop.TreeStop(new() { [4100] = [4200] });
        var result = stop.Invoke(stop.New(4100, 4200, 4300));
        rows.Check(result.ExitCode == 0 && world.Seams.Stops.SequenceEqual([4100]) && result.Kills.Count == 1 && result.Kills[0].Phase == "supervisor"
            && result.Kills[0].Command == "taskkill.exe /PID 4100 /T /F" && result.LockAction == "moved",
            "a driver already absent after the supervisor tree kill is one kill and the lock moves", result.Detail);
        foreach (var later in new[] { "reused", "unknown" })
        {
            world.Seams.Stops.Clear();
            world.Processes.Set(4100, "alive").Set(4200, "alive");
            world.Seams.OnStop = pid =>
            {
                if (pid == 4100) { world.Processes.Set(4100, "absent"); world.Processes.Set(4200, later); }
                return new QualificationTreeStop(true, "stopped", QualificationTreeStop.CommandFor(pid));
            };
            var fixture = stop.New(4100, 4200, 4300);
            var lockBefore = Hash(fixture.Lock);
            var after = stop.Invoke(fixture);
            rows.Check(after.ExitCode == 3 && after.Outcome == "incomplete" && after.LockAction == "not-attempted" && world.Seams.Stops.SequenceEqual([4100]) && Hash(fixture.Lock) == lockBefore,
                "a " + later + " driver after a kill stays an incomplete stop and keeps the lock", after.Detail);
        }
        rows.ShouldAllPass(3);
    }

    [Test]
    public void C1507_QuotedReason()
    {
        using var stop = new Stop();
        var fixture = stop.New(3101, 3101, null);
        var result = stop.Invoke(fixture, "probe \"outcome=forged\" lock=forged \\ end");
        var log = File.ReadAllText(fixture.Log);
        result.ExitCode.ShouldBe(0);
        result.Outcome.ShouldBe("stopped");
        log.ShouldContain("reason=\"probe \\\"outcome=forged\\\" lock=forged \\\\ end\" outcome=stopped lock=absent", customMessage: "a quoted stop reason is escaped");
        log.TrimEnd().ShouldEndWith("outcome=stopped lock=absent", customMessage: "it cannot forge the outcome or lock fields");
    }

    [Test]
    public void C1507_LogWriteFailure()
    {
        using var stop = new Stop();
        var fixture = stop.New(3101, 3101, null);
        Directory.CreateDirectory(fixture.Log);
        stop.World.Probe.PlatformName = "windows";
        var exit = QualificationStop.Run(new QualificationStopOptions
        {
            TaskRoot = fixture.Task, CoordinationRoot = fixture.Coord, RunId = Run, Reason = "operator replacement", Confirm = true, CensusMilliseconds = 0,
        }, stop.World.Host(), stop.World.Files);
        var json = stop.World.LastJson();
        exit.ShouldBe(0, "a supervisor.log write failure still returns a JSON stop result: " + stop.World.Out);
        ((string)json["outcome"]!).ShouldBe("stopped");
        ((int)json["exitCode"]!).ShouldBe(0);
    }

    [Test]
    public void C1507_CommandLineIdentity()
    {
        using var stop = new Stop();
        var world = stop.World;
        var rows = new NightlyCaseRows();
        stop.TreeStop();
        world.Processes.Set(4100, "alive");
        world.Seams.CommandLines[4100] = "C:\\live\\supervisor.exe --run";
        var wrong = stop.Invoke(stop.New(4100, null, null, supervisorCommandLine: "not-the-recorded-command"));
        rows.Check(wrong.ExitCode == 2 && world.Seams.Stops.Count == 0 && wrong.Detail == "supervisor-reused",
            "a recorded command line that does not match the live process authorizes no kill", wrong.Detail);
        world.Processes.Set(4100, "alive");
        var matched = stop.Invoke(stop.New(4100, null, null, supervisorCommandLine: "C:\\live\\supervisor.exe --run"));
        rows.Check(matched.ExitCode == 0 && world.Seams.Stops.SequenceEqual([4100]), "a recorded command line that matches the live process still stops that pid", matched.Detail);
        world.Seams.Stops.Clear();
        world.Processes.Set(4100, "alive");
        var plain = stop.Invoke(stop.New(4100, null, null));
        rows.Check(plain.ExitCode == 0 && world.Seams.Stops.SequenceEqual([4100]), "a record without a command line keeps the pid and start-time check", plain.Detail);
        world.Seams.Stops.Clear();
        world.Processes.Set(4100, "alive");
        var blank = stop.Invoke(stop.New(4100, null, null, supervisorCommandLine: ""));
        rows.Check(blank.ExitCode == 0 && world.Seams.Stops.SequenceEqual([4100]), "a blank recorded command line does not change the pid and start-time check", blank.Detail);
        world.Seams.Stops.Clear();
        world.Processes.Set(4100, "alive");
        world.Seams.CommandLines.Remove(4100);
        var unreadable = stop.Invoke(stop.New(4100, null, null, supervisorCommandLine: "C:\\live\\supervisor.exe --run"));
        rows.Check(unreadable.ExitCode == 2 && unreadable.Detail == "supervisor-unknown" && world.Seams.Stops.Count == 0,
            "an unreadable command line of a record that has one authorizes no kill", unreadable.Detail);
        rows.Check(!string.IsNullOrWhiteSpace(new QualificationOs().CommandLine(Environment.ProcessId)), "the live process command line is readable for the identity witness");
        rows.ShouldAllPass(6);
    }

    [Test]
    public void C1507_PostKillReusedSkipsTheCensus()
    {
        foreach (var later in new[] { "reused", "unknown" })
        {
            using var stop = new Stop();
            var world = stop.World;
            world.Processes.Set(4100, "alive").Set(4200, "alive");
            world.Seams.OnStop = pid =>
            {
                if (pid == 4100) { world.Processes.Set(4100, "absent"); world.Processes.Set(4200, later); }
                return new QualificationTreeStop(true, "stopped", QualificationTreeStop.CommandFor(pid));
            };
            var result = stop.Invoke(stop.New(4100, 4200, 4300), census: 4000);
            result.ExitCode.ShouldBe(3, "post-kill " + later + " skips the census: " + result.Detail);
            result.Outcome.ShouldBe("incomplete");
            result.LockAction.ShouldBe("not-attempted");
            result.Kills.Count.ShouldBe(1);
            world.Processes.Probes.Count.ShouldBeLessThanOrEqualTo(6, "post-kill " + later + " observes at most six times");
            world.Sleeps.ShouldBeEmpty("post-kill " + later + " never waits for the census");
        }
    }

    [Test]
    public void C1507_CensusWaitsForTheRecordedPidsThenGivesUp()
    {
        using var stop = new Stop();
        var world = stop.World;
        world.Processes.Set(4100, "alive");
        // The tree stop succeeds but the supervisor stays: the census polls every 200 ms until the window ends.
        world.Seams.OnStop = pid => new QualificationTreeStop(true, "stopped", QualificationTreeStop.CommandFor(pid));
        var result = stop.Invoke(stop.New(4100, null, null), census: 1000);
        result.ExitCode.ShouldBe(3);
        world.Sleeps.Count.ShouldBe(5);
        world.Sleeps.All(span => span.TotalMilliseconds == 200).ShouldBeTrue(string.Join(",", world.Sleeps));
        // A failed taskkill is an incomplete stop with its reason kept.
        world.Processes.Set(4100, "alive");
        world.Seams.OnStop = pid => new QualificationTreeStop(false, "taskkill-exit-128", QualificationTreeStop.CommandFor(pid));
        var failed = stop.Invoke(stop.New(4100, null, null));
        failed.ExitCode.ShouldBe(3);
        failed.Kills.ShouldHaveSingleItem().Reason.ShouldBe("taskkill-exit-128");
    }

    [Test]
    public void C1507_FaultAfterKillKeepsTheKillList()
    {
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "QualificationStop.cs"))
            .ShouldNotContain("log-unwritten", customMessage: "the stop does not call a later fault log-unwritten");
        using var stop = new Stop();
        var world = stop.World;
        world.Processes.Set(4100, "alive");
        stop.TreeStop();
        var fixture = stop.New(4100, null, 4300);
        var evidence = Path.Combine(fixture.Task, "evidence-file");
        File.WriteAllText(evidence, "not-a-directory");
        var lockBefore = Hash(fixture.Lock);
        var result = stop.Invoke(fixture, evidenceRoot: evidence);
        var log = File.ReadAllText(fixture.Log);
        result.ExitCode.ShouldBe(3, result.Detail);
        result.Kills.ShouldHaveSingleItem().Phase.ShouldBe("supervisor");
        result.Detail.ShouldContain("stop-fault");
        result.Detail.ShouldNotContain("log-unwritten");
        result.LockAction.ShouldBe("not-attempted");
        Hash(fixture.Lock).ShouldBe(lockBefore);
        log.ShouldContain("killed=supervisor:4100");
    }

    [Test]
    public void C1507_OwedNativeWindowsStop()
    {
        var text = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "docs", "release-gates.md"));
        (text.Contains("Win32_Process.CommandLine", StringComparison.Ordinal) && text.Contains("taskkill-exit-N", StringComparison.Ordinal)
            && text.Contains("No current producer writes commandLine", StringComparison.Ordinal)).ShouldBeTrue("the owed native Windows stop is named and not claimed");
    }

    [Test]
    public void C1507_EntryRefusesOffWindowsAndTheDryRunChangesNothing()
    {
        using var stop = new Stop();
        var world = stop.World;
        world.Processes.Set(4100, "alive").Set(4200, "alive").Set(4300, "absent");
        stop.TreeStop();
        var fixture = stop.New(4100, 4200, 4300);
        var lockBefore = Hash(fixture.Lock);
        string[] Arguments(bool confirm) =>
            ["--task-root", fixture.Task, "--coordination-root", fixture.Coord, "--run-id", Run, "--reason", "operator replacement", .. (confirm ? new[] { "--confirm" } : Array.Empty<string>())];
        world.Probe.PlatformName = "linux";
        var exit = QualificationStop.Run(QualificationStopOptions.Parse(Arguments(true)), world.Host(), world.Files);
        exit.ShouldBe(5, world.Out);
        world.Out.ShouldContain("windows-only");
        File.Exists(fixture.Log).ShouldBeFalse();
        Hash(fixture.Lock).ShouldBe(lockBefore);
        world.Seams.Stops.ShouldBeEmpty("the command refuses on this host before any kill or lock change");
        world.Probe.PlatformName = "windows";
        world.ClearOutput();
        exit = QualificationStop.Run(QualificationStopOptions.Parse(Arguments(false)), world.Host(), world.Files);
        exit.ShouldBe(2);
        foreach (var expected in new[] { "--confirm", "dry-run", "pid=4100", "pid=4200", QualificationProcessWorld.StartedText(4100), "ownerPid=4300", "move-if-owner-dead", "planned runId=" + Run })
            world.Out.ShouldContain(expected);
        File.Exists(fixture.Log).ShouldBeFalse();
        Hash(fixture.Lock).ShouldBe(lockBefore);
        world.Seams.Stops.ShouldBeEmpty("without --confirm it prints the planned kills and changes nothing");
        world.ClearOutput();
        QualificationStop.Run(QualificationStopOptions.Parse(["--task-root", Path.Combine(fixture.Task, "absent"), "--coordination-root", Path.Combine(fixture.Coord, "absent"),
            "--run-id", Run, "--reason", "x"]), world.Host(), world.Files).ShouldBe(2);
        world.Out.ShouldContain("planned kill phase=supervisor status=task-root");
        world.Out.ShouldContain("planned lock action=coordination-root");
        Should.Throw<NightlyUsageException>(() => QualificationStopOptions.Parse(["--task-root", fixture.Task, "--coordination-root", fixture.Coord, "--checkout-root", fixture.Task]));
    }
}
