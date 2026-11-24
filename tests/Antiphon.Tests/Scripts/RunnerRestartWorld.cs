using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts;

/// <summary>One control or wait call the fake restart platform saw: the operation, its value and the fake clock then.</summary>
internal sealed record RunnerRestartTraceRow(string Op, object? Value, double Ms);

/// <summary>One restart run: the exit, the final result line's JSON, the whole output and the trace so far (every run of the world).</summary>
internal sealed record RunnerRestartRun(int Exit, JsonElement Json, string Output, RunnerRestartTraceRow[] Trace)
{
    public string Outcome => Json.GetProperty("outcome").GetString()!;

    public double Wait => Json.GetProperty("waitElapsedMs").GetDouble();

    public string[] Mutations => Trace.Select(t => t.Op).Where(op => op is not ("probe" or "sleep")).ToArray();
}

/// <summary>
/// Unit 30 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the C# twin of the deleted fake platform
/// (tests/Antiphon.SessionRunner.Tests/Fixtures/RunnerRestart/platform.ps1) that RestartFixture ran restart-session-runner.ps1
/// against. A fake monotonic clock (milliseconds from StartMs) that only Sleep, the first Probe, a stop-service and Verify
/// advance; a wall clock 2026-09-07T10:00:00Z plus the fake milliseconds (plus UtcJump from the first second on); the
/// runner 900001 and supervisor 900002; startup records appended to the startup log as the clock passes them; controls
/// recorded (and, with Forbid or FailOperation, refused). Files live under a temporary root: the startup log, and the
/// "state" and "pid" sentinels write-state and delete-pids change. The trace and the files outlive a run, as the trace
/// file did; each Run starts the clock at StartMs again.
/// </summary>
internal sealed class RunnerRestartWorld : IRunnerRestartPlatform, IDisposable
{
    public static readonly DateTime Origin = new(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc);
    public static readonly RunnerProcessIdentity Runner = new(900001, "2026-09-07T09:59:00.0000000Z", "synthetic-runner.exe");
    public static readonly RunnerProcessIdentity SupervisorProcess = new(900002, "2026-09-07T09:58:00.0000000Z", "synthetic-supervisor.exe");

    private static readonly string[] Configured = ["write-state", "stop-service", "start-task", "stop-supervisor", "kill-sessions", "delete-pids"];

    private readonly NightlyFixture _fixture = NightlyFixture.Create("runner-restart");
    private readonly List<RunnerRestartTraceRow> _trace = [];
    private readonly HashSet<double> _emitted = [];
    private double _ms;
    private int _probes;
    private int _calls;

    public RunnerRestartWorld()
    {
        File.WriteAllText(StatePath, "stopped-sentinel");
        File.WriteAllText(PidPath, "pid-sentinel");
    }

    public string Root => _fixture.Root;

    public string StatePath => _fixture.PathOf("state");

    public string PidPath => _fixture.PathOf("pid");

    public double HealthyAt { get; set; }

    public int Status { get; set; } = 200;

    public double StartMs { get; set; }

    public List<(double AtMs, JsonObject Record)> Records { get; } = [];

    public double UtcJump { get; set; }

    public double ActionMs { get; set; }

    public double FirstProbeMs { get; set; }

    public double VerifyMs { get; set; }

    public bool Foreign { get; set; }

    public bool Reused { get; set; }

    public bool NoSupervisor { get; set; }

    public bool Forbid { get; set; }

    public string? FailOperation { get; set; }

    public string? FailWaitOperation { get; set; }

    public string? CapabilitiesJson { get; set; }

    /// <summary>The toolchain versions the command's floor check reads (git and the SDK at or above their floors).</summary>
    public FakeHostProbe Tools { get; } = new() { Versions = { ["git"] = "git version 2.50.0" } };

    /// <summary>An ANTIPHON_STARTUP record of the supervisor, emitted once the clock reaches atMs.</summary>
    public static (double AtMs, JsonObject Record) Record(string name, int at, string attempt = "attempt-one", int code = 0) =>
        (at, new JsonObject
        {
            ["version"] = 1, ["event"] = name, ["utc"] = Origin.AddMilliseconds(at).ToString("O"),
            ["producer"] = "supervisor", ["producerPid"] = SupervisorProcess.Pid, ["producerStartTimeUtc"] = SupervisorProcess.StartTimeUtc,
            ["attemptId"] = attempt, ["exitCode"] = code,
        });

    /// <summary>
    /// Runs the restart-session-runner command on this platform with the CLI arguments (RunnerRestart.Run: the toolchain floor
    /// read on a fake probe, the restart, the result line), and checks the result line is the one last line and its exit code is
    /// the command's.
    /// </summary>
    public RunnerRestartRun Run(params string[] arguments)
    {
        _ms = StartMs;
        _probes = 0;
        _calls = 0;
        _emitted.Clear();
        using var output = new StringWriter();
        var host = new NightlyHost
        {
            Clock = new FakeTimeProvider(Origin), Git = new FakeGit().Run, Launcher = new RecordingLauncher(), Processes = new FakeProcessTable(),
            Probe = Tools, Out = output, Error = TextWriter.Null, Trace = TextWriter.Null,
        };
        var exit = RunnerRestart.Run(RunnerRestartOptions.Parse(arguments, NightlyPlanCases.Repo), host, this);
        var text = output.ToString();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var final = lines.Where(line => line.StartsWith(RunnerRestart.ResultPrefix, StringComparison.Ordinal)).ToArray();
        if (final.Length != 1 || lines[^1] != final[0]) throw new InvalidOperationException("one result line, last: " + text);
        var json = JsonDocument.Parse(final[0][RunnerRestart.ResultPrefix.Length..]).RootElement.Clone();
        if (json.GetProperty("exitCode").GetInt32() != exit) throw new InvalidOperationException("the result's exit code is the command's: " + text);
        return new RunnerRestartRun(exit, json, text, _trace.ToArray());
    }

    private void Trace(string op, object? value)
    {
        if (++_calls > 1000) throw new InvalidOperationException("fixture operation fuse");
        _trace.Add(new RunnerRestartTraceRow(op, value, _ms));
    }

    public double? CommandOrigin => null;

    public IFileSystem Files { get; } = new FileSystem();

    public string LogPath => _fixture.PathOf("startup.log");

    public double Now()
    {
        if (FailWaitOperation == "clock" && _probes > 0) throw new InvalidOperationException("injected wait failure: clock");
        return _ms;
    }

    public string Utc()
    {
        if (FailWaitOperation == "utc" && _probes > 0) throw new InvalidOperationException("injected wait failure: utc");
        return Origin.AddMilliseconds(_ms + (_ms >= 1000 ? UtcJump : 0)).ToString("o");
    }

    public void Sleep(double milliseconds)
    {
        Trace("sleep", milliseconds);
        _ms += milliseconds;
    }

    public RunnerProbeResult Probe(double budgetMilliseconds)
    {
        Trace("probe", budgetMilliseconds);
        _probes++;
        if (_probes == 1) _ms += FirstProbeMs;
        if (FailWaitOperation == "probe") throw new InvalidOperationException("injected wait failure: probe");
        foreach (var (at, record) in Records)
        {
            if (_ms >= at && _emitted.Add(at))
                File.AppendAllText(LogPath, "ANTIPHON_STARTUP " + record.ToJsonString() + "\n");
        }
        var status = _ms >= HealthyAt ? Status : 0;
        return new RunnerProbeResult(status, "fixture-" + status);
    }

    public JsonNode? ProbeBuild() => CapabilitiesJson == null ? null : JsonNode.Parse(CapabilitiesJson);

    public RunnerProcessIdentity Identity()
    {
        if (Foreign) return new RunnerProcessIdentity(900003, Runner.StartTimeUtc, @"C:\fixture\other-build\Antiphon.SessionRunner.exe");
        if (Reused) return Runner with { StartTimeUtc = "2026-09-07T09:57:00.0000000Z" };
        return Runner;
    }

    public bool Verify(RunnerProcessIdentity identity)
    {
        _ms += VerifyMs;
        return identity == Runner;
    }

    public RunnerProcessIdentity? Process(int pid) => pid == Runner.Pid ? Runner : pid == SupervisorProcess.Pid ? SupervisorProcess : null;

    public RunnerCensus Census() => new(NoSupervisor ? null : SupervisorProcess, null);

    public void Control(string operation, RunnerProcessIdentity? identity)
    {
        Trace(operation, identity);
        if (Forbid || FailOperation == operation) throw new InvalidOperationException("injected operation failure: " + operation);
        if (operation == RunnerRestartOperations.StopService) _ms += ActionMs;
        if (operation == RunnerRestartOperations.WriteState) File.WriteAllText(StatePath, "running");
        else if (operation == RunnerRestartOperations.DeletePids) File.WriteAllText(PidPath, "");
        else if (!Configured.Contains(operation)) throw new InvalidOperationException("unconfigured operation: " + operation);
    }

    public void Dispose() => _fixture.Dispose();
}
