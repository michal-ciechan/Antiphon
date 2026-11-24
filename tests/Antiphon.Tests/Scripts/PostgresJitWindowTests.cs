using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1886: the JIT window recorder (Antiphon.Nightly.PostgresJitWindow, the postgres-jit-window command that replaced
/// scripts/postgres-jit-window.ps1 in unit 46 of the 2026-11-22 plan) in process: docker is a recording launcher, the files a
/// mock file system, the clock fake. The plan starts nothing; --execute is only ever run here against the fakes.
/// </summary>
[Category("Unit")]
public sealed class PostgresJitWindowTests
{
    private const string Container = "fixture-postgres";
    private const string Snapshot = """{"recordedAt" : "2026-11-24T09:00:00+00:00", "statements" : [{"calls":1}, """ + "\n" + """ {"calls":2}]}""";
    private const string Stats = "fixture-postgres 1.50% 2.00%";
    private static readonly string OutDir = OperatingSystem.IsWindows() ? @"C:\fixture\jit" : "/fixture/jit";

    [Test]
    public void Without_execute_it_prints_the_plan_and_starts_nothing()
    {
        var world = new World();
        var run = world.Run(["--container", Container, "--minutes", "99"]);

        run.Exit.ShouldBe(0);
        run.Lines.ShouldBe(PostgresJitWindow.Plan.ToArray());
        run.Lines[1].ShouldContain("postgres-jit-window --execute --container <name> --out-dir <dir>");
        world.Launcher.Launches.ShouldBeEmpty();
        world.Files.AllPaths.ShouldNotContain(path => path.StartsWith(OutDir, StringComparison.Ordinal));
    }

    [Test]
    [Arguments("", "--out-dir", "--container is required with --execute")]
    [Arguments(Container, "", "--out-dir is required with --execute")]
    [Arguments(Container, "--minutes=0", "--minutes must be from 1 through 30")]
    [Arguments(Container, "--minutes=31", "--minutes must be from 1 through 30")]
    [Arguments(Container, "--interval-seconds=4", "--interval-seconds must be from 5 through 10")]
    [Arguments(Container, "--interval-seconds=11", "--interval-seconds must be from 5 through 10")]
    [Arguments(Container, "--database=antiphon-db", "--database must be a plain identifier")]
    [Arguments(Container, "--user=1postgres", "--user must be a plain identifier")]
    public void Execute_refuses_a_missing_or_out_of_range_value_before_any_docker_call(string container, string extra, string message)
    {
        List<string> arguments = ["--execute"];
        if (container.Length > 0) arguments.AddRange(["--container", container]);
        if (extra != "") arguments.AddRange(["--out-dir", OutDir]);
        if (extra.Contains('=', StringComparison.Ordinal)) arguments.AddRange(extra.Split('=', 2));
        var world = new World();

        Should.Throw<NightlyUsageException>(() => world.Run(arguments)).Message.ShouldBe(message);
        world.Launcher.Launches.ShouldBeEmpty();
    }

    [Test]
    public void Execute_records_both_snapshots_the_stats_samples_and_the_window_without_resetting()
    {
        var world = new World();
        var run = world.Run(["--execute", "--container", Container, "--out-dir", OutDir, "--minutes", "1", "--interval-seconds", "10"]);

        run.Exit.ShouldBe(0, run.Output);
        run.Lines.ShouldBe(["CARD-1886 JIT window written under " + OutDir]);
        var launches = world.Launcher.Launches;
        launches.ShouldAllBe(l => l.FileName == "docker" && l.CaptureOutput && l.TimeoutMilliseconds == 30000 && !l.Arguments.Contains("--context"));
        launches.SelectMany(l => l.Arguments).ShouldAllBe(a => !a.Contains("pg_stat_statements_reset"));
        string[] psql = ["exec", "-e", "PGOPTIONS=-c default_transaction_read_only=on", Container, "psql", "-X", "-A", "-t", "-w", "-v", "ON_ERROR_STOP=1",
            "-U", "postgres", "-d", "antiphon", "-c"];
        launches[0].Arguments.ShouldBe([.. psql, PostgresJitWindow.JitColumnsSql]);
        var snapshot = PostgresJitWindow.SnapshotSql(["jit_functions", "jit_generation_time"]);
        snapshot.ShouldContain("total_exec_time, total_plan_time, shared_blks_hit");
        snapshot.ShouldContain("temp_blks_written, jit_functions, jit_generation_time\n");
        snapshot.ShouldContain("(SELECT row_to_json(i) FROM pg_stat_statements_info i)");
        launches[1].Arguments.ShouldBe([.. psql, snapshot]);
        launches.Skip(2).Take(6).ShouldAllBe(l => l.Arguments.SequenceEqual(new[] { "stats", "--no-stream", "--format", PostgresJitWindow.StatsFormat, Container }));
        launches[8].Arguments.ShouldBe([.. psql, snapshot]);
        launches.Count.ShouldBe(9);

        world.Read("statements-start.json").ShouldBe(Snapshot + "\n");
        world.Read("statements-end.json").ShouldBe(Snapshot + "\n");
        world.Read("docker-stats.txt").ShouldBe(string.Concat(Enumerable.Range(0, 6).Select(i =>
            "2026-11-24T09:00:" + (i * 10).ToString("00") + ".0000000Z " + Stats + "\n")));
        using var note = JsonDocument.Parse(world.Read("window.json"));
        note.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["container", "database", "minutes", "intervalSeconds", "startedAt", "endedAt", "jitColumns"]);
        note.RootElement.GetProperty("container").GetString().ShouldBe(Container);
        note.RootElement.GetProperty("database").GetString().ShouldBe("antiphon");
        note.RootElement.GetProperty("minutes").GetInt32().ShouldBe(1);
        note.RootElement.GetProperty("intervalSeconds").GetInt32().ShouldBe(10);
        note.RootElement.GetProperty("startedAt").GetString().ShouldBe("2026-11-24T09:00:00.0000000Z");
        note.RootElement.GetProperty("endedAt").GetString().ShouldBe("2026-11-24T09:01:00.0000000Z");
        note.RootElement.GetProperty("jitColumns").EnumerateArray().Select(c => c.GetString()).ShouldBe(["jit_functions", "jit_generation_time"]);
    }

    [Test]
    [Arguments("psql", "postgres-jit-window: PostgreSQL query failed (exit 1). Check Docker access and PostgreSQL availability.")]
    [Arguments("column", "postgres-jit-window: unexpected jit column name")]
    [Arguments("stats", "postgres-jit-window: docker stats failed")]
    [Arguments("stats-timeout", "postgres-jit-window: docker stats failed")]
    public void A_failed_query_column_or_sample_exits_1_with_one_line(string failure, string line)
    {
        var world = new World { Failure = failure };
        var run = world.Run(["--execute", "--container", Container, "--out-dir", OutDir, "--minutes", "1"]);

        run.Exit.ShouldBe(1);
        run.Lines.ShouldBe([line]);
        world.Files.FileExists(world.Files.Path.Combine(OutDir, "window.json")).ShouldBeFalse();
    }

    // Docker answers the jit_ column list, the snapshot and the stats sample (or the case's failure); the clock moves only when
    // the window waits.
    private sealed class World
    {
        public World()
        {
            Launcher = new RecordingLauncher(Answer);
        }

        public string Failure { get; init; } = "";

        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-24T09:00:00Z"));

        public MockFileSystem Files { get; } = new();

        public RecordingLauncher Launcher { get; }

        public (int Exit, string Output, string[] Lines) Run(List<string> arguments)
        {
            using var output = new StringWriter();
            var host = new NightlyHost
            {
                Clock = Clock,
                Git = new FakeGit().Run,
                Launcher = Launcher,
                Processes = new FakeProcessTable(),
                Probe = new FakeHostProbe(),
                Out = output,
                Error = output,
                Trace = TextWriter.Null,
                Variables = _ => null,
                Sleep = Clock.Advance,
            };
            var exit = PostgresJitWindow.Run(PostgresJitWindowOptions.Parse(arguments), host, Files);
            var text = output.ToString().ReplaceLineEndings("\n");
            return (exit, text, text.TrimEnd('\n').Split('\n'));
        }

        public string Read(string name) => Files.File.ReadAllText(Files.Path.Combine(OutDir, name));

        private NightlyLaunchResult Answer(NightlyLaunch launch)
        {
            if (launch.Arguments[0] == "stats")
            {
                if (Failure == "stats") return new NightlyLaunchResult { ExitCode = 1, StandardErrorText = "no such container" };
                if (Failure == "stats-timeout") return new NightlyLaunchResult { ExitCode = 1, TimedOut = true };
                return new NightlyLaunchResult { ExitCode = 0, StandardOutputText = Stats + "\n" };
            }
            if (Failure == "psql") return new NightlyLaunchResult { ExitCode = 1, StandardErrorText = "relation does not exist" };
            if (launch.Arguments[^1] == PostgresJitWindow.JitColumnsSql)
                return new NightlyLaunchResult { ExitCode = 0, StandardOutputText = Failure == "column" ? "jit_functions\njit_x;drop\n" : "jit_functions\n jit_generation_time \n\n" };
            return new NightlyLaunchResult { ExitCode = 0, StandardOutputText = Snapshot + "\n" };
        }
    }
}
