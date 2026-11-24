using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0420: the startup milestone reader of the runner restart (Antiphon.Nightly.RunnerMilestoneReader, which replaced
/// New-RunnerMilestoneReader and Read-RunnerMilestones of scripts/session-runner-restart-health.ps1 in unit 30 of the
/// 2026-11-22 plan), in process over a real temporary log and RunnerRestartWorld's process table. The rows are the two
/// parser cases of the deleted Antiphon.SessionRunner.Tests RunnerRestartDiagnosticTests, which ran them under pwsh and
/// powershell.exe; the shell argument is gone with the script.
/// </summary>
[Category("Unit")]
public sealed class RunnerMilestoneReaderTests
{
    private static readonly DateTime NotBefore = new(2026, 9, 7, 9, 59, 59, DateTimeKind.Utc);

    private static string Line(string name, string attempt = "current", string start = "2026-09-07T09:58:00.0000000Z", int producer = 900002, int version = 1) =>
        "ANTIPHON_STARTUP " + new JsonObject
        {
            ["version"] = version, ["event"] = name, ["utc"] = "2026-09-07T10:00:00.0000000Z", ["producer"] = "supervisor",
            ["producerPid"] = producer, ["producerStartTimeUtc"] = start, ["attemptId"] = attempt,
        }.ToJsonString();

    [Test]
    public void Only_current_attempt_and_process_records_define_phase()
    {
        using var p = new RunnerRestartWorld();
        var r = new RunnerMilestoneReader(p.LogPath, p.Files);
        void Append(string line)
        {
            File.AppendAllText(p.LogPath, line + "\n");
            r.Read(p, NotBefore);
        }

        Append(Line("build-start", "stale", "2026-09-07T09:57:00.0000000Z"));
        r.Phase.ShouldBeNull("stale process supplied a phase");
        Append(Line("build-start", "foreign", producer: 900003));
        r.Phase.ShouldBeNull("foreign producer supplied a phase");
        Append(Line("build-start"));
        r.Phase!.Event.ShouldBe("build-start", "current phase missing");
        Append(Line("adoption-sweep-start", "stale-attempt"));
        r.Phase!.Event.ShouldBe("build-start", "stale attempt overwrote phase");
        Append(Line("build-start", "stale", "2026-09-07T09:57:00.0000000Z"));
        r.Phase!.Event.ShouldBe("build-start", "stale process overwrote phase");
        Append(Line("retry-scheduled"));
        r.Phase!.Event.ShouldBe("retry-scheduled", "current retry missing");
        Append(Line("build-start", "next"));
        r.Attempt.ShouldBe("next", "new attempt missing");
        Append(Line("build-start", "current"));
        r.Attempt.ShouldBe("next", "retired attempt returned");
        Append(Line("legacy", "next", version: 99));
        r.Phase.ShouldBeNull("unknown schema supplied a phase");
        r.SupervisorIdentity.ShouldBe(RunnerRestartWorld.SupervisorProcess);
        r.RunnerIdentity.ShouldBeNull();
    }

    [Test]
    public void Diagnostic_reader_survives_rotation_gaps_and_partial_records()
    {
        using var p = new RunnerRestartWorld();
        var r = new RunnerMilestoneReader(p.LogPath, p.Files);
        var line = Line("build-start");
        File.WriteAllText(p.LogPath, line[..40]);
        r.Read(p, NotBefore);
        r.Phase.ShouldBeNull("partial record admitted");
        File.AppendAllText(p.LogPath, line[40..] + "\n");
        r.Read(p, NotBefore);
        r.Phase!.Event.ShouldBe("build-start", "completed record lost");
        File.AppendAllText(p.LogPath, "ANTIPHON_STARTUP {bad\n");
        r.Read(p, NotBefore);
        r.Phase.ShouldBeNull("malformed record retained phase");
        File.WriteAllText(p.LogPath, "truncated");
        r.Read(p, NotBefore);
        r.Phase.ShouldBeNull("truncation retained phase");
        File.AppendAllText(p.LogPath, new string('x', 300000) + "\n");
        r.Read(p, NotBefore);
        r.Phase.ShouldBeNull("gap retained phase");
        File.AppendAllText(p.LogPath, line + "\n");
        r.Read(p, NotBefore);
        r.Phase!.Event.ShouldBe("build-start", "did not recover after gap");
        File.Move(p.LogPath, p.LogPath + ".old");
        File.WriteAllText(p.LogPath, line + "\n");
        r.Read(p, NotBefore);
        r.Phase!.Event.ShouldBe("build-start", "replacement did not recover");
        File.Delete(p.LogPath);
        r.Read(p, NotBefore);
        r.Phase.ShouldBeNull("missing log retained phase");
    }

    [Test]
    public void An_overlong_line_and_a_record_before_the_restart_clear_or_skip_the_phase()
    {
        using var p = new RunnerRestartWorld();
        var r = new RunnerMilestoneReader(p.LogPath, p.Files);
        File.WriteAllText(p.LogPath, Line("build-start") + "\n");
        r.Read(p, NotBefore);
        r.Phase!.Event.ShouldBe("build-start");
        File.AppendAllText(p.LogPath, "ANTIPHON_STARTUP " + new string('y', RunnerMilestoneReader.LineBytes + 1) + "\n");
        r.Read(p, NotBefore);
        r.Phase.ShouldBeNull("a line over 8 KiB is an unknown phase");

        // A record older than the restart's start is not this restart's.
        var later = new RunnerMilestoneReader(p.LogPath, p.Files);
        File.WriteAllText(p.LogPath, Line("build-start") + "\n");
        later.Read(p, new DateTime(2026, 9, 7, 10, 0, 1, DateTimeKind.Utc));
        later.Phase.ShouldBeNull();
    }

    // The runner's own records (RunnerStartupDiagnostics, as the runner writes them to its log) decode as this checkout's
    // runner. RunnerStartupReadinessTests decoded a launched runner's log with the PowerShell decoder until unit 30; here the
    // producer is this process, so its identity is real.
    [Test]
    public void The_runners_own_records_decode_as_this_checkouts_runner()
    {
        using var world = new RunnerRestartWorld();
        var me = RunnerProcesses.Identity(Environment.ProcessId).ShouldNotBeNull();
        var diagnostics = new Antiphon.SessionRunner.RunnerStartupDiagnostics(new LineLogger(world.LogPath), Guid.NewGuid().ToString(), 900002, "2026-09-07T09:58:00Z");
        diagnostics.Record("managed-entry");
        using (var sweep = diagnostics.Begin("adoption-sweep")) sweep.Complete(0);
        diagnostics.Record("application-started");
        var reader = new RunnerMilestoneReader(world.LogPath, world.Files);
        reader.Read(new RunnerOnly(world, me), DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));
        reader.Phase!.Event.ShouldBe("application-started");
        reader.Phase!.Producer.ShouldBe("runner");
        reader.RunnerIdentity.ShouldBe(me);
        reader.Attempt.ShouldMatch("^[0-9a-f]{32}$");
    }

    private sealed class LineLogger(string path) : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => File.AppendAllText(path, "10:00:00 INF " + formatter(state, exception) + Environment.NewLine);
    }

    /// <summary>The world's file system and log, with this process as the only live process and this checkout's runner.</summary>
    private sealed class RunnerOnly(RunnerRestartWorld world, RunnerProcessIdentity me) : IRunnerRestartPlatform
    {
        public double? CommandOrigin => null;
        public System.IO.Abstractions.IFileSystem Files => world.Files;
        public string LogPath => world.LogPath;
        public double Now() => 0;
        public string Utc() => RunnerRestartWorld.Origin.ToString("o");
        public void Sleep(double milliseconds) => throw new NotSupportedException();
        public RunnerProbeResult Probe(double budgetMilliseconds) => throw new NotSupportedException();
        public JsonNode? ProbeBuild() => throw new NotSupportedException();
        public RunnerProcessIdentity? Identity() => me;
        public bool Verify(RunnerProcessIdentity? identity) => identity == me;
        public RunnerCensus Census() => new(null, null);
        public RunnerProcessIdentity? Process(int pid) => pid == me.Pid ? me : null;
        public void Control(string operation, RunnerProcessIdentity? identity) => throw new NotSupportedException();
    }
}
