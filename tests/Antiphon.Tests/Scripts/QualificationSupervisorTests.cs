using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1667: the rc qualification supervisor set (qualification-observe, -start-supervisor, -supervise, -launch, -preflight;
/// QualificationSupervision, QualificationLaunch), in-process. These cases replaced the C1667 driver
/// scripts/test-qualification-supervisor.ps1 (plan 2026-11-22-remaining-ps1-to-csharp.md, unit 7). Task and state roots are
/// real directories under a temp root; owners are a QualificationProcessWorld; the detached starts, the console and session
/// are a QualificationTestHost, so nothing real is started or stopped. The production detached start is witnessed once
/// against cmd.exe or /bin/sh (DetachedStartWritesItsOwnFilesAndExitCode).
/// Not ported, PowerShell mechanics: the AST scan for FileShare.Delete on every read open (kept as a source check over the C#
/// readers and the File.Replace witness), the 5.1 parse, ASCII and $PSScriptRoot-in-param-defaults check (the ASCII check
/// stays, over the C# sources), the task-directory copies with -CheckoutRoot (nothing is loaded from a checkout), and the
/// Unix kill of the wrapper's process group with a real pwsh supervisor (the detach is pinned by the start plan; its native
/// witness was and stays owed).
/// </summary>
[Category("Unit")]
public sealed class QualificationSupervisorTests
{
    private const string Run = "c1667run";
    private const int Self = 4242;
    private const int Gone = 3100;

    private static readonly DateTime Now = QualificationWorld.Now.UtcDateTime;

    private sealed record Observed(int Code, JsonObject Json, string Text);

    // New-ObservedRun: the owner records name pids whose status the world holds; Self is alive, Gone has exited.
    private sealed class Runs : IDisposable
    {
        public readonly QualificationWorld World = QualificationWorld.Create("c1667");

        public Runs()
        {
            World.Processes.Set(Self, "alive");
            World.Processes.Set(Gone, "absent");
        }

        public (string Task, string State) New(string name, int? runOwner, int? supervisor, string? heartbeatUtc, object? state, string lockRunId = Run,
            string? commissionExit = null, string boundRunId = Run, string? launchStartedAt = null, object? ownCopy = null)
        {
            var task = World.Directory(name + "-task");
            var stateRoot = World.Directory(name + "-state");
            if (launchStartedAt != null) Write(Path.Combine(task, "launch-child.json"), new JsonObject { ["pid"] = 1, ["processStartedAt"] = launchStartedAt, ["runId"] = boundRunId });
            if (ownCopy != null) Write(Path.Combine(task, "own-last-run.json"), ownCopy);
            if (runOwner is { } owner)
                Write(Path.Combine(stateRoot, "run.lock"), new JsonObject { ["pid"] = owner, ["processStartedAt"] = QualificationProcessWorld.StartedText(owner), ["runId"] = lockRunId });
            if (state != null) Write(Path.Combine(stateRoot, "last-run.json"), state);
            if (supervisor is { } pid)
            {
                var identity = new JsonObject { ["pid"] = pid, ["processStartedAt"] = QualificationProcessWorld.StartedText(pid) };
                if (boundRunId.Length > 0) identity["runId"] = boundRunId;
                Write(Path.Combine(task, "supervisor-identity.json"), identity);
            }
            if (heartbeatUtc != null) Write(Path.Combine(task, "supervisor-heartbeat.json"), new JsonObject { ["observedAtUtc"] = heartbeatUtc, ["trx"] = 3, ["hangSuspected"] = false });
            if (commissionExit != null) File.WriteAllText(Path.Combine(task, "commission-exit.txt"), commissionExit);
            return (task, stateRoot);
        }

        public Observed Observe((string Task, string State) run, NightlyHost? host = null)
        {
            World.ClearOutput();
            var code = QualificationObserve.Run(QualificationObserveOptions.Parse(["--task-root", run.Task, "--state-root", run.State]), host ?? World.Host(), World.Files);
            return new Observed(code, World.LastJson(), World.Out);
        }

        public void Dispose() => World.Dispose();
    }

    private static void Write(string path, object value) => QualificationWorld.WriteJson(path, value is JsonNode node ? node.ToJsonString() : value);

    private static JsonObject State(string runId, string phase, DateTime? startedAt = null, DateTime? completedAt = null, int? exitCode = null, bool? testsPassed = null)
    {
        var state = new JsonObject { ["runId"] = runId, ["phase"] = phase };
        if (startedAt != null) state["startedAt"] = startedAt.Value.ToString("o");
        if (completedAt != null) state["completedAt"] = completedAt.Value.ToString("o");
        if (exitCode != null) state["exitCode"] = exitCode;
        if (testsPassed != null) state["testsPassed"] = testsPassed;
        return state;
    }

    private static string? S(JsonObject json, string name) => json[name]?.GetValueKind() == System.Text.Json.JsonValueKind.String ? json[name]!.GetValue<string>() : null;

    [Test]
    public void Observe_classifies_lost_hung_missing_healthy_done_dead_and_unowned_runs()
    {
        using var runs = new Runs();
        var fresh = Now.ToString("o");
        var stale = Now.AddMinutes(-40).ToString("o");
        var running = State(Run, "tests", Now.AddHours(-9));
        var rows = new NightlyCaseRows();

        var o = runs.Observe(runs.New("lost", Self, Gone, stale, running));
        rows.Check(o.Code == 0 && S(o.Json, "classification") == "supervisor-lost-run-alive" && S(o.Json, "runOwner") == "alive" && S(o.Json, "supervisor") == "absent",
            "C1667 observe classifies a gone supervisor with live nightly-run as supervisor-lost-run-alive", o.Text);
        o = runs.Observe(runs.New("hung", Self, Self, stale, running));
        rows.Check(o.Code == 0 && S(o.Json, "classification") == "supervisor-lost-run-alive" && S(o.Json, "reason") == "heartbeat-stale" && S(o.Json, "supervisor") == "alive",
            "C1667 observe treats a stale heartbeat as supervisor lost while the run is alive", o.Text);
        o = runs.Observe(runs.New("missingbeat", Self, Gone, null, running));
        rows.Check(o.Code == 0 && S(o.Json, "classification") == "supervisor-lost-run-alive" && S(o.Json, "heartbeat") == "missing",
            "C1667 observe treats a missing heartbeat as supervisor lost while the run is alive", o.Text);
        o = runs.Observe(runs.New("healthy", Self, Self, fresh, running));
        rows.Check(o.Code == 0 && S(o.Json, "classification") == "running" && S(o.Json, "reason") == "heartbeat-fresh",
            "C1667 observe reports running for a live supervisor with a fresh heartbeat", o.Text);
        o = runs.Observe(runs.New("done", Gone, Gone, stale, State(Run, "final", completedAt: Now.AddMinutes(-2), exitCode: 1, testsPassed: false)));
        rows.Check(o.Code == 0 && S(o.Json, "classification") == "completed" && o.Json["exitCode"]!.GetValue<long>() == 1 && o.Json["commissionExit"] == null
            && S(o.Json, "runOwner") == "absent", "C1667 observe treats last-run completedAt as the end signal without commission-exit", o.Text);
        o = runs.Observe(runs.New("dead", Gone, Gone, stale, running));
        rows.Check(o.Code == 3 && S(o.Json, "classification") == "run-lost" && S(o.Json, "runOwner") == "absent",
            "C1667 observe reports run-lost when nightly-run is gone without completedAt", o.Text);
        o = runs.Observe(runs.New("noowner", null, Self, fresh, running));
        rows.Check(o.Code == 2 && S(o.Json, "classification") == "unknown", "C1667 observe reports unknown without a run owner record", o.Text);
        rows.ShouldAllPass(7);
    }

    // Completion races the probe: nightly-run writes completedAt before releasing run.lock and exiting, so the owner must be
    // probed before last-run.json is read.
    [Test]
    public void Observe_probes_the_run_owner_before_reading_last_run()
    {
        using var runs = new Runs();
        var run = runs.New("race", Self, Gone, Now.AddMinutes(-40).ToString("o"), State(Run, "tests", Now.AddHours(-9)));
        var statePath = Path.Combine(run.State, "last-run.json");
        runs.World.Processes.OnProbe = pid =>
        {
            if (pid != Self) return;
            Write(statePath, State(Run, "final", completedAt: Now, exitCode: 0));
            runs.World.Processes.Set(Self, "absent");
        };
        var o = runs.Observe(run);
        (S(o.Json, "classification") + " exit=" + o.Json["exitCode"]).ShouldBe("completed exit=0", "C1667 observe probes the run owner before reading last-run.json: " + o.Text);
    }

    // D-1 (Review b26811ff): the shared state root keeps the PREVIOUS run's last-run.json, so completed, running and
    // supervisor-lost-run-alive are credited only for the launch's own run id.
    [Test]
    public void Observe_credits_only_the_launch_run_id()
    {
        using var runs = new Runs();
        var fresh = Now.ToString("o");
        var stale = Now.AddMinutes(-40).ToString("o");
        var running = State(Run, "tests", Now.AddHours(-9));
        var previous = State("c1667prev", "final", completedAt: Now.AddHours(-3), exitCode: 0, testsPassed: true);
        var rows = new NightlyCaseRows();
        var o = runs.Observe(runs.New("refused", null, Gone, stale, previous, commissionExit: "2"));
        rows.Check(o.Code == 2 && S(o.Json, "classification") == "launch-failed" && S(o.Json, "reason") == "commission-exit-2" && S(o.Json, "runId") == Run
            && S(o.Json, "stateRunId") == "c1667prev" && o.Json["exitCode"] == null && o.Json["testsPassed"] == null,
            "C1667 observe does not credit a previous run completedAt to a refused launch (commission-exit 2)", o.Text);
        o = runs.Observe(runs.New("foreignlock", Self, Gone, stale, State("c1667other", "tests", Now.AddHours(-1)), lockRunId: "c1667other"));
        rows.Check(o.Code == 2 && S(o.Json, "classification") == "not-started" && S(o.Json, "runLock") == "foreign" && S(o.Json, "lockRunId") == "c1667other"
            && S(o.Json, "runOwner") == "alive", "C1667 observe does not report supervisor-lost-run-alive for another run holding run.lock", o.Text);
        o = runs.Observe(runs.New("replacedlock", Self, Gone, stale, running, lockRunId: "c1667other"));
        rows.Check(o.Code == 2 && S(o.Json, "classification") == "unknown" && S(o.Json, "reason") == "run-lock-foreign" && S(o.Json, "stateRunId") == Run,
            "C1667 observe needs the run.lock run id as well as the last-run.json run id before crediting supervisor-lost-run-alive", o.Text);
        o = runs.Observe(runs.New("slotwait", null, Self, fresh, previous));
        rows.Check(o.Code == 2 && S(o.Json, "classification") == "not-started" && S(o.Json, "reason") == "run-id-not-seen" && S(o.Json, "runLock") == "missing"
            && o.Json["completedAt"] == null, "C1667 observe reports not-started while the launch waits for a build slot (no Started yet)", o.Text);
        o = runs.Observe(runs.New("prestart", Self, Self, fresh, previous));
        rows.Check(o.Code == 2 && S(o.Json, "classification") == "not-started" && S(o.Json, "reason") == "state-not-written" && S(o.Json, "runLock") == "ours",
            "C1667 observe needs the last-run.json run id as well as the run.lock run id before crediting running", o.Text);
        o = runs.Observe(runs.New("unbound", Self, Self, fresh, running, boundRunId: ""));
        rows.Check(o.Code == 2 && S(o.Json, "classification") == "unknown" && S(o.Json, "reason") == "run-id-unbound",
            "C1667 observe refuses to classify without a bound launch run id", o.Text);
        rows.ShouldAllPass(6);
    }

    // D-1 (Review 9f29e134): last-run.json holds only the latest run. Once the next run writes it, our run is credited from the
    // TaskRoot copy, or is unknown/state-superseded; never launch-failed or not-started (which would invite a relaunch).
    // A-2 (Review 230d1f5d): a copy naming another run id is neither credited nor a supersession.
    [Test]
    public void Observe_handles_superseded_state_and_the_task_root_copy()
    {
        using var runs = new Runs();
        var fresh = Now.ToString("o");
        var stale = Now.AddMinutes(-40).ToString("o");
        var launchedAt = Now.AddHours(-9);
        var launched = launchedAt.ToString("o");
        var next = State("c1667next", "tests", Now.AddMinutes(-10));
        var mid = State(Run, "tests", launchedAt);
        var rows = new NightlyCaseRows();
        var o = runs.Observe(runs.New("superseded", Self, Gone, stale, next, lockRunId: "c1667next", commissionExit: "0", launchStartedAt: launched));
        rows.Check(o.Code == 2 && S(o.Json, "classification") == "unknown" && S(o.Json, "reason") == "state-superseded" && S(o.Json, "stateRunId") == "c1667next"
            && o.Json["exitCode"] == null && o.Json["completedAt"] == null,
            "C1667 observe reports a completed run superseded by a later run without a copy as unknown, not launch-failed", o.Text);
        o = runs.Observe(runs.New("lostsuperseded", Self, Gone, stale, next, lockRunId: "c1667next", launchStartedAt: launched));
        var o2 = runs.Observe(runs.New("lostsupercopy", null, Gone, stale, next, launchStartedAt: launched, ownCopy: mid));
        rows.Check(o.Code == 2 && S(o.Json, "classification") == "unknown" && S(o.Json, "reason") == "state-superseded"
            && o2.Code == 2 && S(o2.Json, "classification") == "unknown" && S(o2.Json, "reason") == "state-superseded" && S(o2.Json, "stateCopy") == "ours",
            "C1667 observe reports a lost supervisor whose run was superseded as unknown, not not-started", o.Text + " | " + o2.Text);
        var ownDone = State(Run, "final", launchedAt, Now.AddMinutes(-30), 1, false);
        o = runs.Observe(runs.New("copydone", Self, Gone, stale, next, lockRunId: "c1667next", commissionExit: "0", launchStartedAt: launched, ownCopy: ownDone));
        rows.Check(o.Code == 0 && S(o.Json, "classification") == "completed" && S(o.Json, "reason") == "state-copy-completedAt" && o.Json["exitCode"]!.GetValue<long>() == 1
            && !o.Json["testsPassed"]!.GetValue<bool>() && S(o.Json, "stateRunId") == "c1667next",
            "C1667 observe credits completed from the TaskRoot copy of our last-run.json after it is superseded", o.Text);
        // A refused run writes deferred-busy to last-run.json while ours still holds run.lock.
        var busy = State("c1667busy", "deferred-busy", Now.AddMinutes(-5), exitCode: 3);
        o = runs.Observe(runs.New("busyover", Self, Self, fresh, busy, launchStartedAt: launched, ownCopy: mid));
        rows.Check(o.Code == 0 && S(o.Json, "classification") == "running" && S(o.Json, "stateRunId") == "c1667busy" && S(o.Json, "phase") == "tests",
            "C1667 observe credits running from run.lock and the copy when another run overwrote last-run.json", o.Text);
        // A previous run that started before the launch child is not a supersession: still launch-failed.
        var earlier = State("c1667prev", "final", Now.AddHours(-12), Now.AddHours(-10), 0);
        o = runs.Observe(runs.New("refusedlaunched", null, Gone, stale, earlier, commissionExit: "2", launchStartedAt: launched));
        rows.Check(o.Code == 2 && S(o.Json, "classification") == "launch-failed" && S(o.Json, "reason") == "commission-exit-2",
            "C1667 observe keeps launch-failed when last-run.json names a run that started before the launch child", o.Text);
        var foreignDone = State("c1667prev", "final", Now.AddHours(-12), Now.AddHours(-10), 0, true);
        o = runs.Observe(runs.New("foreigncopy", null, Gone, stale, earlier, commissionExit: "2", launchStartedAt: launched, ownCopy: foreignDone));
        o2 = runs.Observe(runs.New("foreigncopywait", null, Self, fresh, null, launchStartedAt: launched, ownCopy: foreignDone));
        var o3 = runs.Observe(runs.New("foreigncopylock", Self, Self, fresh, null, launchStartedAt: launched, ownCopy: foreignDone));
        rows.Check(o.Code == 2 && S(o.Json, "classification") == "launch-failed" && S(o.Json, "reason") == "commission-exit-2" && S(o.Json, "stateCopy") == "foreign"
            && o.Json["completedAt"] == null && o.Json["exitCode"] == null
            && o2.Code == 2 && S(o2.Json, "classification") == "not-started" && S(o2.Json, "reason") == "run-id-not-seen" && S(o2.Json, "stateCopy") == "foreign"
            && o3.Code == 2 && S(o3.Json, "classification") == "not-started" && S(o3.Json, "reason") == "state-not-written" && S(o3.Json, "stateCopy") == "foreign"
            && o3.Json["phase"] == null,
            "C1667 observe credits neither completion nor supersession to a TaskRoot copy naming another run id", o.Text + " | " + o2.Text + " | " + o3.Text);
        rows.ShouldAllPass(6);
    }

    // supervise keeps the copy: while polling (and does not overwrite it with the next run), and once more at child exit.
    [Test]
    public void Supervise_copies_last_run_for_its_run_id_while_polling_and_at_child_exit()
    {
        using var runs = new Runs();
        var world = runs.World;
        var state = world.Directory("keep-state");
        var logs = world.Directory("keep-logs");
        var statePath = Path.Combine(state, "last-run.json");
        var done = State("c1667keep", "final", Now.AddHours(-9), Now.AddMinutes(-1), 0, true);
        var after = State("c1667after", "Started", Now);
        var rows = new NightlyCaseRows();

        var poll = world.Directory("keep-poll-task");
        world.Seams.OnStart = start => new QualificationFakeChild(5200, Now, world.Clock)
        {
            OnWait = wait =>
            {
                if (wait == 1) { Write(statePath, done); return false; }
                if (wait == 2) { Write(statePath, after); return true; }
                return true;
            },
        };
        var code = QualificationSupervise.Run(Supervise(poll, logs, state, "c1667keep"), world.Host(), world.Files);
        var pollObserved = runs.Observe((poll, state));
        var copy = JsonNode.Parse(File.ReadAllText(Path.Combine(poll, "own-last-run.json")))!.AsObject();
        rows.Check(code == 0 && S(copy, "runId") == "c1667keep" && copy["completedAt"] != null && S(pollObserved.Json, "classification") == "completed"
            && S(pollObserved.Json, "stateRunId") == "c1667after" && pollObserved.Json["testsPassed"]!.GetValue<bool>(),
            "C1667 supervise-rc copies last-run.json for its run id while polling and keeps it after the next run writes", pollObserved.Text);

        var exit = world.Directory("keep-exit-task");
        File.Delete(statePath);
        world.Seams.OnStart = start =>
        {
            Write(statePath, done);
            return new QualificationFakeChild(5300, Now, world.Clock) { OnWait = _ => true };
        };
        code = QualificationSupervise.Run(Supervise(exit, logs, state, "c1667keep"), world.Host(), world.Files);
        Write(statePath, after);
        var exitObserved = runs.Observe((exit, state));
        rows.Check(code == 0 && exitObserved.Code == 0 && S(exitObserved.Json, "classification") == "completed" && S(exitObserved.Json, "reason") == "state-copy-completedAt"
            && S(exitObserved.Json, "stateCopy") == "ours" && File.Exists(Path.Combine(exit, "own-last-run.json")),
            "C1667 supervise-rc copies last-run.json for its run id at child exit", exitObserved.Text);
        rows.ShouldAllPass(2);
    }

    private static QualificationSuperviseOptions Supervise(string task, string logs, string state, string runId, int pollSeconds = 1) =>
        QualificationSuperviseOptions.Parse(["--task-root", task, "--launch-record", Path.Combine(task, "launch-record.json"), "--log-root", logs, "--state-root", state,
            "--run-id", runId, "--poll-seconds", pollSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "--progress-minutes", "1",
            "--powershell-path", "pwsh-fake"]);

    private static string[] StartArguments(string task, string state, string runId, string? record = null) =>
        ["--task-root", task, "--launch-record", record ?? Path.Combine(task, "launch-record.json"), "--log-root", task, "--state-root", state, "--run-id", runId];

    // start-supervisor binds a fresh run id: one the state root (last-run.json, run.lock) or the TaskRoot copy names is refused
    // before anything starts.
    [Test]
    public void Start_supervisor_refuses_a_reused_run_id()
    {
        using var runs = new Runs();
        var world = runs.World;
        var rows = new NightlyCaseRows();
        var task = world.Directory("reuse-task");
        var state = world.Directory("reuse-state");
        File.WriteAllText(Path.Combine(task, "launch-record.json"), "{}");
        Write(Path.Combine(state, "last-run.json"), State("c1667prev", "final", completedAt: Now.AddMinutes(-40)));
        var code = QualificationStartSupervisor.Run(QualificationStartOptions.Parse(StartArguments(task, state, "C1667PREV"), "pwsh-fake"), world.Host(), world.Files);
        rows.Check(code == 1 && world.Out.Contains("already names run id C1667PREV", StringComparison.Ordinal) && !File.Exists(Path.Combine(task, "supervisor-identity.json"))
            && world.Seams.Starts.Count == 0 && world.Seams.Staged.Count == 0, "C1667 start-supervisor refuses a run id the state root already records", world.Out);

        var copyTask = world.Directory("reusecopy-task");
        var copyState = world.Directory("reusecopy-state");
        File.WriteAllText(Path.Combine(copyTask, "launch-record.json"), "{}");
        Write(Path.Combine(copyTask, "own-last-run.json"), State("c1667copy", "final", completedAt: Now.AddMinutes(-40)));
        world.ClearOutput();
        code = QualificationStartSupervisor.Run(QualificationStartOptions.Parse(StartArguments(copyTask, copyState, "c1667copy"), "pwsh-fake"), world.Host(), world.Files);
        rows.Check(code == 1 && world.Out.Contains("already names run id c1667copy", StringComparison.Ordinal) && !File.Exists(Path.Combine(copyTask, "supervisor-identity.json"))
            && world.Seams.Starts.Count == 0, "C1667 start-supervisor refuses a run id the TaskRoot copy own-last-run.json already records", world.Out);

        var lockTask = world.Directory("reuselock-task");
        var lockState = world.Directory("reuselock-state");
        File.WriteAllText(Path.Combine(lockTask, "launch-record.json"), "{}");
        Write(Path.Combine(lockState, "run.lock"), new JsonObject { ["pid"] = Self, ["runId"] = "c1667lock" });
        world.ClearOutput();
        code = QualificationStartSupervisor.Run(QualificationStartOptions.Parse(StartArguments(lockTask, lockState, "c1667lock"), "pwsh-fake"), world.Host(), world.Files);
        rows.Check(code == 1 && world.Out.Contains("already names run id c1667lock", StringComparison.Ordinal) && world.Seams.Starts.Count == 0,
            "C1667 start-supervisor refuses a run id run.lock already records", world.Out);

        // The other refusals: a missing task root or launch record, an existing identity; a bad run id or range is usage.
        world.ClearOutput();
        rows.Check(QualificationStartSupervisor.Run(QualificationStartOptions.Parse(StartArguments(Path.Combine(task, "absent"), state, "c1667new"), "pwsh-fake"), world.Host(), world.Files) == 1
            && world.Out.Contains("TaskRoot does not exist", StringComparison.Ordinal), "C1667 start-supervisor refuses a missing task root", world.Out);
        world.ClearOutput();
        rows.Check(QualificationStartSupervisor.Run(QualificationStartOptions.Parse(StartArguments(task, state, "c1667new", Path.Combine(task, "none.json")), "pwsh-fake"), world.Host(), world.Files) == 1
            && world.Out.Contains("launch record does not exist", StringComparison.Ordinal), "C1667 start-supervisor refuses a missing launch record", world.Out);
        File.WriteAllText(Path.Combine(task, "supervisor-identity.json"), "{}");
        world.ClearOutput();
        rows.Check(QualificationStartSupervisor.Run(QualificationStartOptions.Parse(StartArguments(task, state, "c1667new"), "pwsh-fake"), world.Host(), world.Files) == 1
            && world.Out.Contains("a supervisor identity already exists", StringComparison.Ordinal) && world.Seams.Starts.Count == 0,
            "C1667 start-supervisor refuses a task root that already has a supervisor identity", world.Out);
        rows.Check(Throws(() => QualificationStartOptions.Parse(StartArguments(task, state, "../escape"), "pwsh-fake"))
            && Throws(() => QualificationStartOptions.Parse(StartArguments(task, state, "ok").Concat(["--poll-seconds", "0"]).ToArray(), "pwsh-fake"))
            && Throws(() => QualificationStartOptions.Parse(StartArguments(task, state, "ok"), "")), "C1667 start-supervisor run id, ranges and pwsh are usage errors");
        rows.ShouldAllPass(7);
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (NightlyUsageException) { return true; }
    }

    // A-1 (Review 230d1f5d): nightly-run writes last-run.json with File.Replace, which Windows refuses while a reader's handle
    // lacks FileShare.Delete. The replace runs on every host (Linux never blocks it, so only Windows proves the sharing).
    [Test]
    public void Readers_open_with_FileShare_Delete_so_File_Replace_is_never_blocked()
    {
        using var runs = new Runs();
        var dir = runs.World.Directory("share");
        var path = Path.Combine(dir, "last-run.json");
        var next = Path.Combine(dir, "last-run.json.next");
        Write(path, new JsonObject { ["runId"] = "c1667share" });
        var problems = new List<string>();
        string Replace(string target)
        {
            try
            {
                File.WriteAllText(next, "{\"runId\":\"c1667next\"}");
                File.Replace(next, target, null);
                return "replaced";
            }
            catch (Exception e) { return e.Message; }
        }
        var outcome = "not-run";
        RcQualificationRecords.Read(runs.World.Files, path, target => outcome = Replace(target));
        if (outcome != "replaced") problems.Add("RcQualificationRecords.Read blocked File.Replace: " + outcome);
        using (RcQualificationRecords.OpenSharedRead(runs.World.Files, path)) outcome = Replace(path);
        if (outcome != "replaced") problems.Add("OpenSharedRead blocked File.Replace: " + outcome);
        // The source pin: every read open of the qualification readers is OpenSharedRead's (ReadWrite | Delete).
        foreach (var name in new[] { "RcQualificationRecords.cs", "QualificationSupervisor.cs", "QualificationStop.cs", "QualificationLaunch.cs", "StatusSnapshot.cs" })
        {
            var text = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", name));
            foreach (Match open in Regex.Matches(text, @"FileAccess\.Read\b[^\r\n]*"))
                if (!open.Value.Contains("FileShare.ReadWrite | FileShare.Delete", StringComparison.Ordinal)) problems.Add(name + ": " + open.Value);
        }
        var supervisor = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "QualificationSupervisor.cs"));
        if (!supervisor.Contains("RcQualificationRecords.OpenSharedRead(files, source)", StringComparison.Ordinal)) problems.Add("SaveOwnRunState does not open through OpenSharedRead");
        problems.ShouldBeEmpty("C1667 last-run.json readers open with FileShare.Delete so the nightly-run File.Replace is never blocked");
    }

    private static string LaunchRecord(QualificationWorld world, string name, string checkout, params string[] nightlyArguments)
    {
        var path = Path.Combine(world.Directory(name + "-launch"), "launch-record.json");
        Write(path, new JsonObject
        {
            ["checkoutRoot"] = checkout, ["expectedSha"] = "0123456789abcdef0123456789abcdef01234567",
            ["nightlyArguments"] = new JsonArray(nightlyArguments.Select(a => (JsonNode)a).ToArray()),
        });
        return path;
    }

    private static (int Code, string Text) Launch(QualificationWorld world, string config, string bound)
    {
        world.ClearOutput();
        var code = QualificationLaunch.Run(QualificationLaunchOptions.Parse(["--config", config], bound, "pwsh-fake"), world.Host(), world.Files,
            (_, _) => throw new InvalidOperationException("a refused launch takes no build slot"));
        return (code, world.Out);
    }

    // launch refuses before git or build-slot when its run id is missing or is not the supervisor's bound id; a live run.lock
    // refuses (exit 3) instead of queueing behind it; a dead owner is left to nightly-run (the launch goes on to the HEAD check).
    [Test]
    public void Launch_refuses_a_missing_or_foreign_run_id_and_a_live_run_lock()
    {
        using var runs = new Runs();
        var world = runs.World;
        var rows = new NightlyCaseRows();
        var checkout = world.Directory("checkout");
        var l = Launch(world, LaunchRecord(world, "norunid", checkout, "-Profile", "rc"), Run);
        var m = Launch(world, LaunchRecord(world, "otherrunid", checkout, "-RunId", "c1667other"), Run);
        rows.Check(l.Code == 2 && l.Text.Contains("requires -RunId", StringComparison.Ordinal) && m.Code == 2 && m.Text.Contains("is not the supervisor run id " + Run, StringComparison.Ordinal)
            && world.Git.Calls.Count == 0 && world.Launcher.Launches.Count == 0,
            "C1667 launch-rc refuses a launch whose -RunId is missing or differs from the bound run id", l.Text + " | " + m.Text);
        var liveState = world.Directory("livelock-state");
        Write(Path.Combine(liveState, "run.lock"), new JsonObject { ["pid"] = Self, ["processStartedAt"] = QualificationProcessWorld.StartedText(Self), ["runId"] = "c1667other" });
        l = Launch(world, LaunchRecord(world, "livelock", checkout, "-RunId", Run, "-StateRoot", liveState), Run);
        var goneState = world.Directory("stalelock-state");
        Write(Path.Combine(goneState, "run.lock"), new JsonObject { ["pid"] = Gone, ["processStartedAt"] = QualificationProcessWorld.StartedText(Gone), ["runId"] = "c1667other" });
        var g = Launch(world, LaunchRecord(world, "stalelock", checkout, "-RunId:" + Run, "-StateRoot:" + goneState), Run);
        rows.Check(l.Code == 3 && l.Text.Contains("held by live pid " + Self, StringComparison.Ordinal) && l.Text.Contains("c1667other", StringComparison.Ordinal)
            && g.Code == 2 && g.Text.Contains("checkout HEAD", StringComparison.Ordinal),
            "C1667 launch-rc refuses a live run.lock and leaves a dead owner to nightly-run", l.Text + " | " + g.Text);
        var reusedState = world.Directory("reusedlock-state");
        world.Processes.Set(3300, "reused");
        Write(Path.Combine(reusedState, "run.lock"), new JsonObject { ["pid"] = 3300, ["processStartedAt"] = QualificationProcessWorld.StartedText(3300), ["runId"] = "c1667other" });
        var unreadableState = world.Directory("unreadablelock-state");
        File.WriteAllText(Path.Combine(unreadableState, "run.lock"), "{not json");
        var r = Launch(world, LaunchRecord(world, "reusedlock", checkout, "-RunId", Run, "-StateRoot", reusedState), Run);
        var u = Launch(world, LaunchRecord(world, "unreadablelock", checkout, "-RunId", Run, "-StateRoot", unreadableState), Run);
        rows.Check(r.Code == 2 && r.Text.Contains("checkout HEAD", StringComparison.Ordinal) && u.Code == 3 && u.Text.Contains("held by unreadable owner", StringComparison.Ordinal),
            "C1667 launch-rc leaves a reused owner to nightly-run and refuses an unreadable run.lock", r.Text + " | " + u.Text);
        var bad = Path.Combine(world.Directory("badsha-launch"), "launch-record.json");
        Write(bad, new JsonObject { ["checkoutRoot"] = checkout, ["expectedSha"] = "ABC", ["nightlyArguments"] = new JsonArray("-RunId", Run) });
        var b = Launch(world, bad, Run);
        var missing = Launch(world, Path.Combine(checkout, "no-record.json"), Run);
        rows.Check(b.Code == 2 && b.Text.Contains("expectedSha must be a full lowercase SHA", StringComparison.Ordinal) && missing.Code == 1
            && missing.Text.Contains("cannot be read", StringComparison.Ordinal), "C1667 launch-rc refuses a short SHA and an unreadable record", b.Text + " | " + missing.Text);
        rows.ShouldAllPass(4);
    }

    // The launch with HEAD and core.longpaths in order runs nightly-run with the record's arguments under a build slot this
    // process holds (no build-slot.ps1 child; the slot itself is BuildSlotScriptTests'), on its console, and exits with its code.
    [Test]
    public void Launch_runs_build_slot_then_nightly_run_and_exits_with_its_code()
    {
        using var runs = new Runs();
        var world = runs.World;
        var checkout = world.Directory("checkout");
        var state = world.Directory("state");
        world.Git.Reply("rev-parse", new NightlyGitResult(0, "0123456789abcdef0123456789abcdef01234567" + Environment.NewLine));
        var launcher = new RecordingLauncher(_ => new NightlyLaunchResult { ExitCode = 7 });
        var host = new NightlyHost
        {
            Clock = world.Clock, Git = world.Git.Run, Launcher = launcher, Processes = world.Processes, Probe = world.Probe, Out = world.Fixture.Out,
            Error = world.Fixture.Error, Trace = world.Fixture.Trace, Qualification = world.Seams,
        };
        var config = LaunchRecord(world, "go", checkout, "-NoSync", "-RunId", Run, "-StateRoot", state);
        var slots = new List<(BuildSlotOptions Options, TextWriter? Capture)>();
        var code = QualificationLaunch.Run(QualificationLaunchOptions.Parse(["--config", config], Run, "C:/pwsh/pwsh.exe"), host, world.Files,
            (options, capture) =>
            {
                slots.Add((options, capture));
                return 7;
            });
        code.ShouldBe(7, world.Out);
        launcher.Launches.ShouldBeEmpty("no build-slot.ps1 child");
        var slot = slots.ShouldHaveSingleItem();
        (slot.Options.Label, slot.Options.NoSlot, slot.Options.HolderPid, slot.Capture).ShouldBe(("rc-qualification", false, (int?)null, (TextWriter?)null),
            "the lease is this process's and the run shares its console");
        slot.Options.Command.ShouldBe(new[]
        {
            "C:/pwsh/pwsh.exe", "-NoProfile", "-File", Path.Combine(checkout, "scripts", "nightly-run.ps1"), "-NoSync", "-RunId", Run, "-StateRoot", state,
        });
        world.Out.ShouldContain("build-slot -> nightly-run -NoSync -RunId " + Run);
        world.Out.ShouldContain(" exit 7");
    }

    // Line-flushed log: every line is on disk when the call returns, and no handle stays open.
    [Test]
    public void Supervisor_log_lines_are_on_disk_at_return_and_the_file_is_not_held_open()
    {
        using var runs = new Runs();
        var log = Path.Combine(runs.World.Directory("log"), "supervisor.log");
        QualificationFiles.WriteLine(runs.World.Files, log, "PROGRESS one", Now);
        QualificationFiles.WriteLine(runs.World.Files, log, "PROGRESS two", Now.AddSeconds(1));
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(log));
        Regex.IsMatch(text, @"^\S+Z PROGRESS one\r\n\S+Z PROGRESS two\r\n$").ShouldBeTrue(text);
        text.ShouldStartWith("2026-11-23T12:00:00.000Z PROGRESS one");
        using (new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    [Test]
    public void Detached_start_plans_a_hidden_console_on_Windows_and_setsid_on_Unix()
    {
        var start = new QualificationStart(@"C:\pwsh\pwsh.exe", ["-File", @"C:\a b\supervise-rc.ps1"], @"C:\t\out.log", @"C:\t\err.log");
        var win = QualificationDetach.Plan(start, "Windows");
        win.NewHiddenConsole.ShouldBeTrue("Windows detached start gets its own hidden console, never the starter's");
        win.FileName.ShouldBe(@"C:\pwsh\pwsh.exe");
        win.CommandLine.ShouldBe(@"C:\pwsh\pwsh.exe -File ""C:\a b\supervise-rc.ps1""");
        win.StandardOutput.ShouldBe(@"C:\t\out.log");
        QualificationDetach.Plan(start with { SameSession = true }, "Windows").NewHiddenConsole.ShouldBeTrue("the launch child gets its own console on Windows too");
        var unix = QualificationDetach.Plan(new QualificationStart("/usr/bin/pwsh", ["-File", "/t/supervise-rc.ps1"], "/t/out.log", "/t/err.log"), "Unix");
        unix.FileName.ShouldBe("/bin/sh");
        unix.Setsid.ShouldBeTrue();
        unix.Arguments.ShouldBe(new[] { "-c", QualificationDetach.UnixScript, "sh", "/t/out.log", "/t/err.log", "setsid", "/usr/bin/pwsh", "-File", "/t/supervise-rc.ps1" });
        var same = QualificationDetach.Plan(new QualificationStart("/usr/bin/pwsh", ["x"], "/t/out.log", "/t/err.log", SameSession: true), "Unix");
        same.Setsid.ShouldBeFalse();
        same.Arguments.ShouldNotContain("setsid");
        // CommandLineToArgvW quoting: quotes, trailing backslashes and empty arguments survive.
        QualificationDetach.CommandLine(["plain", "", "a b", "say \"hi\"", @"C:\dir\", @"C:\a b\", @"x\\""y"])
            .ShouldBe(@"plain """" ""a b"" ""say \""hi\"""" C:\dir\ ""C:\a b\\"" ""x\\\\\""y""");
    }

    // The production detached start (QualificationOs): the child's stdout and stderr are its own files, its stdin the null
    // device, the added variable is in its environment, it runs in the directory given (review-standalone, unit 42), and its
    // pid, start and exit code are read back.
    [Test]
    public void DetachedStartWritesItsOwnFilesAndExitCode()
    {
        using var runs = new Runs();
        var dir = runs.World.Directory("detached");
        var stdout = Path.Combine(dir, "child-stdout.log");
        var stderr = Path.Combine(dir, "child-stderr.log");
        var start = OperatingSystem.IsWindows()
            ? new QualificationStart(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", "echo bound run=%ANTIPHON_QUALIFICATION_RUN_ID%& echo fake launch done& echo cwd=%CD%& echo to-stderr 1>&2& exit /b 7"],
                stdout, stderr, SameSession: true, Environment: new Dictionary<string, string> { [QualificationSupervise.RunIdVariable] = "c1667e2e" }, WorkingDirectory: dir)
            : new QualificationStart("/bin/sh", ["-c", "echo bound run=$ANTIPHON_QUALIFICATION_RUN_ID; echo fake launch done; echo cwd=$PWD; echo to-stderr 1>&2; exit 7"],
                stdout, stderr, SameSession: true, Environment: new Dictionary<string, string> { [QualificationSupervise.RunIdVariable] = "c1667e2e" }, WorkingDirectory: dir);
        var before = DateTime.UtcNow.AddSeconds(-5);
        using (var child = new QualificationOs().Start(start))
        {
            child.WaitForExit(TimeSpan.FromSeconds(60)).ShouldBeTrue("the child exits");
            child.HasExited.ShouldBeTrue();
            child.ExitCode.ShouldBe(7);
            child.Pid.ShouldBeGreaterThan(0);
            child.StartTimeUtc.ShouldBeGreaterThan(before);
        }
        var text = File.ReadAllText(stdout);
        text.ShouldContain("bound run=c1667e2e", customMessage: "the run id is in the child's environment");
        text.ShouldContain("fake launch done", customMessage: "launch child output goes to its own file");
        text.ShouldContain("cwd=" + Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar), customMessage: "the child starts in the directory given");
        File.ReadAllText(stderr).ShouldContain("to-stderr");
        new QualificationOs().CommandLine(Environment.ProcessId).ShouldNotBeNullOrWhiteSpace("this process's command line is readable");
    }

    // End to end in process: start-supervisor -> (detached) supervise -> launch child that writes a TRX, prints and exits 7;
    // the identity, launch-child record and launch environment carry the run id; observe reads the finished run.
    [Test]
    public void Start_supervisor_runs_the_supervisor_which_records_the_launch_and_its_exit()
    {
        using var runs = new Runs();
        var world = runs.World;
        var task = world.Directory("e2e task");
        var logs = world.Directory("e2e-logs");
        var state = world.Directory("e2e-state");
        File.WriteAllText(Path.Combine(task, "launch-record.json"), "{}");
        world.Seams.CliArgv = ["dotnet", "exec", Path.Combine(task, "cli", "Antiphon.Scripts.dll")];
        world.Seams.Consoles = [5100];
        const int supervisorPid = 5100, childPid = 5200;
        var supervisorProcesses = new QualificationProcessWorld { CurrentPid = supervisorPid };
        supervisorProcesses.Set(supervisorPid, "alive");
        var supervisorExit = -1;
        world.Seams.OnStart = start =>
        {
            if (start.Arguments.Contains("qualification-supervise"))
            {
                // The detached supervisor, run here to its end on its own process table.
                var arguments = start.Arguments.SkipWhile(a => a != "qualification-supervise").Skip(1).ToArray();
                supervisorExit = QualificationSupervise.Run(QualificationSuperviseOptions.Parse(arguments), world.Host(supervisorProcesses), world.Files);
                world.Processes.Set(supervisorPid, "absent");
                return new QualificationFakeChild(supervisorPid, QualificationProcessWorld.Started(supervisorPid)) { OnWait = _ => true, Exit = supervisorExit };
            }
            File.WriteAllText(start.StandardOutput, "bound run=" + start.Environment![QualificationSupervise.RunIdVariable] + "\nfake launch done\n");
            return new QualificationFakeChild(childPid, Now, world.Clock)
            {
                Exit = 7,
                OnWait = wait =>
                {
                    if (wait == 1) { File.WriteAllText(Path.Combine(logs, "a.trx"), "x"); return false; }
                    return true;
                },
            };
        };
        var code = QualificationStartSupervisor.Run(QualificationStartOptions.Parse(
            ["--task-root", task, "--launch-record", Path.Combine(task, "launch-record.json"), "--log-root", logs, "--state-root", state, "--run-id", "c1667e2e",
             "--poll-seconds", "1", "--progress-minutes", "1"], "C:/pwsh/pwsh.exe"), world.Host(), world.Files);
        var rows = new NightlyCaseRows();
        var identity = JsonNode.Parse(File.ReadAllText(Path.Combine(task, "supervisor-identity.json")))!.AsObject();
        var started = world.LastJson();
        rows.Check(code == 0 && supervisorExit == 0 && identity["pid"]!.GetValue<int>() == supervisorPid && started["supervisorPid"]!.GetValue<int>() == supervisorPid
            && S(started, "observation") == "absent" && File.Exists(Path.Combine(task, "start-supervisor.json")),
            "C1667 start-supervisor returns after the supervisor records its own identity", world.Out);
        var supervisorStart = world.Seams.Starts[0];
        rows.Check(world.Seams.Staged.SequenceEqual([Path.Combine(task, "cli")]) && supervisorStart.FileName == "dotnet" && !supervisorStart.SameSession
            && supervisorStart.StandardOutput == Path.Combine(task, "supervisor-stdout.log") && supervisorStart.StandardError == Path.Combine(task, "supervisor-stderr.log")
            && supervisorStart.Arguments.Take(3).SequenceEqual(["exec", Path.Combine(task, "cli", "Antiphon.Scripts.dll"), "qualification-supervise"])
            && supervisorStart.Arguments.Contains("--detach") && S(identity, "detach") == "new-console" && S(started, "detach") == "new-console",
            "C1667 supervisor runs detached from a staged copy of the CLI with its own files", string.Join(" ", supervisorStart.Arguments));
        var log = File.ReadAllText(Path.Combine(task, "supervisor.log"));
        var exitText = File.ReadAllText(Path.Combine(task, "commission-exit.txt")).Trim();
        rows.Check(exitText == "7", "C1667 supervisor survives teardown of the starting group and records the launch exit code", exitText);
        var beat = JsonNode.Parse(File.ReadAllText(Path.Combine(task, "supervisor-heartbeat.json")))!.AsObject();
        rows.Check(Regex.IsMatch(log, @"(?m)^\S+Z START supervisor pid=5100 run=c1667e2e platform=Windows detach=new-console session= console=\[5100\]\r?$")
            && Regex.IsMatch(log, @"(?m)^\S+Z LAUNCHED pid=5200 ") && Regex.IsMatch(log, @"(?m)^\S+Z EXIT 7\r?$") && log.TrimEnd().EndsWith("SUPERVISOR-END", StringComparison.Ordinal)
            && log.EndsWith("\n", StringComparison.Ordinal) && beat["observedAtUtc"] != null && beat["trx"]!.GetValue<double>() == 1,
            "C1667 supervisor writes START, LAUNCHED and EXIT lines and a heartbeat", log);
        var launchStart = world.Seams.Starts[1];
        var console = File.ReadAllText(Path.Combine(task, "commission-console.log"));
        rows.Check(launchStart.SameSession && launchStart.StandardOutput == Path.Combine(task, "commission-console.log") && console.Contains("fake launch done", StringComparison.Ordinal)
            && launchStart.Arguments.SequenceEqual(["exec", Path.Combine(task, "cli", "Antiphon.Scripts.dll"), "qualification-launch", "--config", Path.Combine(task, "launch-record.json"),
                "--powershell-path", "C:/pwsh/pwsh.exe"]),
            "C1667 launch child output goes to its own commission-console.log", console + " | " + string.Join(" ", launchStart.Arguments));
        var o = runs.Observe((task, state));
        rows.Check(o.Code == 2 && S(o.Json, "supervisor") == "absent" && S(o.Json, "commissionExit") == "7",
            "C1667 observe reads the real supervisor identity and commission-exit of the finished run", o.Text);
        var child = JsonNode.Parse(File.ReadAllText(Path.Combine(task, "launch-child.json")))!.AsObject();
        rows.Check(S(identity, "runId") == "c1667e2e" && S(child, "runId") == "c1667e2e" && child["pid"]!.GetValue<int>() == childPid && console.Contains("bound run=c1667e2e", StringComparison.Ordinal)
            && S(o.Json, "classification") == "launch-failed" && S(o.Json, "runId") == "c1667e2e",
            "C1667 supervisor binds the launch run id in its identity, launch-child record and the launch environment", o.Text + " " + console);
        rows.ShouldAllPass(7);
    }

    // A poll that cannot observe logs HEARTBEAT-ERROR and the next poll retries; a launch that cannot start is SUPERVISOR-ERROR
    // (exit 1); HANG_SUSPECTED is reported once per hang and is observation only.
    [Test]
    public void Supervise_logs_heartbeat_errors_hangs_and_a_failed_launch()
    {
        using var runs = new Runs();
        var world = runs.World;
        var task = world.Directory("errors-task");
        var logs = world.Directory("errors-logs");
        var state = world.Directory("errors-state");
        // The heartbeat path is a directory: every write fails, the supervisor goes on.
        Directory.CreateDirectory(Path.Combine(task, "supervisor-heartbeat.json"));
        world.Seams.OnStart = _ => new QualificationFakeChild(5200, Now, world.Clock) { OnWait = wait => wait > 2 };
        QualificationSupervise.Run(Supervise(task, logs, state, "c1667err"), world.Host(), world.Files).ShouldBe(0);
        var log = File.ReadAllText(Path.Combine(task, "supervisor.log"));
        Regex.Matches(log, "HEARTBEAT-ERROR").Count.ShouldBe(2, log);
        log.ShouldContain(" EXIT 0");

        var failTask = world.Directory("fail-task");
        world.Seams.OnStart = _ => throw new InvalidOperationException("no such launcher");
        QualificationSupervise.Run(Supervise(failTask, logs, state, "c1667fail"), world.Host(), world.Files).ShouldBe(1);
        var failLog = File.ReadAllText(Path.Combine(failTask, "supervisor.log"));
        failLog.ShouldContain("SUPERVISOR-ERROR no such launcher");
        failLog.TrimEnd().ShouldEndWith("SUPERVISOR-END");
        File.Exists(Path.Combine(failTask, "commission-exit.txt")).ShouldBeFalse();

        // A quiet child subtree past the idle budget: one HANG_SUSPECTED line and hang-suspected.txt, nothing stopped.
        var hangTask = world.Directory("hang-task");
        world.Seams.Table.Add(new QualificationProcess(5200, 1, 1.0));
        world.Seams.OnStart = _ => new QualificationFakeChild(5200, Now, world.Clock)
        {
            OnWait = wait =>
            {
                world.Clock.Advance(TimeSpan.FromMinutes(1));
                return wait > 4;
            },
        };
        var hang = QualificationSuperviseOptions.Parse(["--task-root", hangTask, "--launch-record", "r.json", "--log-root", logs, "--state-root", state, "--run-id", "c1667hang",
            "--poll-seconds", "60", "--hang-minutes", "1"]);
        QualificationSupervise.Run(hang, world.Host(), world.Files).ShouldBe(0);
        var hangLog = File.ReadAllText(Path.Combine(hangTask, "supervisor.log"));
        Regex.Matches(hangLog, "HANG_SUSPECTED idleMinutes=").Count.ShouldBe(1, hangLog);
        hangLog.ShouldContain("(observe only; nothing is stopped)");
        File.Exists(Path.Combine(hangTask, "hang-suspected.txt")).ShouldBeTrue();
        world.Seams.Stops.ShouldBeEmpty();
    }

    [Test]
    public void Quiet_test_host_reaches_its_idle_budget_while_the_owner_and_other_logs_are_busy()
    {
        using var runs = new Runs();
        var world = runs.World;
        var task = world.Directory("idle-task");
        var logs = world.Directory("idle-logs");
        var state = world.Directory("idle-state");
        var runState = State(Run, "tests");
        runState["logDir"] = logs;
        Write(Path.Combine(state, "last-run.json"), runState);
        const int ownerPid = 5200, testHostPid = 5201;
        world.Seams.OnStart = _ => new QualificationFakeChild(ownerPid, Now, world.Clock)
        {
            OnWait = wait =>
            {
                if (wait > 21) return true;
                world.Clock.Advance(TimeSpan.FromMinutes(1));
                world.Seams.Table.Clear();
                world.Seams.Table.Add(new QualificationProcess(ownerPid, 1, 10 * wait));
                world.Seams.Table.Add(new QualificationProcess(testHostPid, ownerPid, 1));
                File.AppendAllText(Path.Combine(state, "unrelated.log"), "noise\n");
                QualificationHeartbeat.Write(new QualificationHeartbeatRecord
                {
                    ObservedAtUtc = world.Clock.GetUtcNow().UtcDateTime.ToString("o"),
                    HostCpu = [new QualificationCpuSample(testHostPid, 1)],
                    IdleMinutes = wait - 1,
                    IdleBudgetMinutes = 20,
                    HangSuspected = wait == 21,
                }, Path.Combine(logs, "active-test-heartbeat.json"));
                return false;
            },
        };

        QualificationSupervise.Run(Supervise(task, logs, state, Run), world.Host(), world.Files).ShouldBe(0);

        var beat = QualificationHeartbeat.Read(Path.Combine(task, "supervisor-heartbeat.json"));
        beat.IdleMinutes.ShouldBe(20, "the quiet test host owns the idle clock even while the launcher uses 10 CPU seconds/minute");
        beat.IdleBudgetMinutes.ShouldBe(20);
        beat.HangSuspected.ShouldBeTrue();
        beat.HostCpu.Select(sample => sample.Pid).ShouldBe(new[] { testHostPid });
    }

    [Test]
    public void Preflight_refuses_a_checkout_without_longpaths()
    {
        using var runs = new Runs();
        var world = runs.World;
        world.Git.Reply(arguments => arguments.Contains("core.longpaths"), new NightlyGitResult(1, ""));
        QualificationLaunch.Preflight(QualificationPreflightOptions.Parse(["--checkout-root", @"C:\fixture checkout"]), world.Host()).ShouldBe(2);
        world.Out.ShouldContain("REFUSED: longpaths-not-enabled");
        world.Out.ShouldContain(@"git -C C:\fixture checkout config --local core.longpaths true");
        world.ClearOutput();
        world.Git.Reply(arguments => arguments.Contains("core.longpaths"), new NightlyGitResult(0, "true" + Environment.NewLine));
        QualificationLaunch.Preflight(QualificationPreflightOptions.Parse(["--checkout-root", @"C:\fixture checkout"]), world.Host()).ShouldBe(0);
        world.Out.ShouldNotContain("longpaths-not-enabled");
        world.Git.Calls[^1].Arguments.ShouldBe(new[] { "config", "--local", "--get", "core.longpaths" });
        world.Git.Calls[^1].WorkingDirectory.ShouldBe(@"C:\fixture checkout");
    }

    [Test]
    public void Supervisor_sources_are_ascii()
    {
        var nightly = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly");
        var cli = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Scripts");
        var sources = new[] { "QualificationHost.cs", "QualificationSupervisor.cs", "QualificationLaunch.cs", "QualificationStop.cs", "StatusSnapshot.cs" }.Select(name => Path.Combine(nightly, name))
            .Concat(new[] { "QualificationObserveCommand.cs", "QualificationPreflightCommand.cs", "QualificationLaunchCommand.cs", "QualificationStartSupervisorCommand.cs",
                "QualificationSuperviseCommand.cs", "QualificationStopCommand.cs", "StatusSnapshotCommand.cs" }.Select(name => Path.Combine(cli, name)));
        foreach (var path in sources) File.ReadAllBytes(path).Where(b => b > 127).ShouldBeEmpty(path + " is not ASCII");
    }
}
