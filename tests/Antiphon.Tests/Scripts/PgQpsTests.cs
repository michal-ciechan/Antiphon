using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1987: the pg_stat_statements rate (Antiphon.Nightly.PgQps, the pg-qps command that replaced scripts/pg-qps.ps1 in unit 46
/// of the 2026-11-22 plan) in process. Docker is a recording launcher answering the server2 lookup and psql, the --connection-env
/// psql child is a fake seam, the AppHost user secret is a fixture file under a fixture APPDATA, the clock is fake and the SQL is
/// the checkout's server/Infrastructure/Data/PgStatStatementsSnapshot.sql. The resolution cases moved here from
/// DesktopPostgresPowerShellTests (CARD-2065), which started the script in pwsh against a fake docker. Nothing reaches a real
/// database, Docker daemon, user secret or psql.
/// </summary>
[Category("Unit")]
public sealed class PgQpsTests
{
    private const string Diagnostic = "synthetic PostgreSQL stderr";
    private const string LocalContainer = "fixture-db";
    private const string Meta = "160004|16.4|5|2026-11-01 00:00:00+00";
    private static readonly string ContainerId = new('a', 64);
    private static readonly string[] Context = ["--context", "antiphon-server2"];

    public enum Scenario { Match, WrongHost, WrongPort, Stopped, MissingContainer, MissingSecret, QueryFailure, Override }

    [Test]
    [Arguments(Scenario.Match)]
    [Arguments(Scenario.WrongHost)]
    [Arguments(Scenario.WrongPort)]
    [Arguments(Scenario.Stopped)]
    [Arguments(Scenario.MissingContainer)]
    [Arguments(Scenario.MissingSecret)]
    [Arguments(Scenario.QueryFailure)]
    [Arguments(Scenario.Override)]
    public void Queries_only_the_resolved_postgres_target(Scenario scenario)
    {
        var root = Directory.CreateTempSubdirectory("c2065-qps-");
        try
        {
            VerifyTarget(root.FullName, scenario);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static void VerifyTarget(string root, Scenario scenario)
    {
        var appData = Directory.CreateDirectory(Path.Combine(root, "appdata")).FullName;
        var secretFile = Path.Combine(appData, "Microsoft", "UserSecrets", "aspire-antiphon-apphost", "secrets.json");
        var password = $"fixture-{Guid.NewGuid():N}";
        var secretJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Host=192.0.2.1;Port=15432;Username=observer;Database=operations;Password={password}",
        });
        var local = scenario == Scenario.Override;
        var hasSecret = !local && scenario != Scenario.MissingSecret;
        if (hasSecret)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(secretFile)!);
            File.WriteAllText(secretFile, secretJson);
        }

        var success = scenario is Scenario.Match or Scenario.Override;
        var hostIp = scenario == Scenario.WrongHost ? "192.0.2.2" : "192.0.2.1";
        var hostPort = scenario == Scenario.WrongPort ? "5432" : "15432";
        var running = scenario == Scenario.Stopped ? "false" : "true";
        var launcher = new RecordingLauncher(launch =>
        {
            if (launch.Arguments.Contains("inspect"))
            {
                return scenario == Scenario.MissingContainer
                    ? new NightlyLaunchResult { ExitCode = 1, StandardErrorText = Diagnostic + " Password=" + password }
                    : new NightlyLaunchResult { ExitCode = 0, StandardOutputText = $$"""{{ContainerId}}|{{running}}|[{"HostIp":"{{hostIp}}","HostPort":"{{hostPort}}"}]""" };
            }
            return scenario == Scenario.QueryFailure
                ? new NightlyLaunchResult { ExitCode = 9, StandardErrorText = Diagnostic + " Password=" + password, LogText = Diagnostic }
                : new NightlyLaunchResult { ExitCode = 0, StandardOutputText = "160000|16.0|0|2026-01-01||0|0|0\n", StandardErrorText = Diagnostic };
        });
        var signal = Path.Combine(root, "qps-ready");
        var user = local ? "fixture_user" : "observer";
        var database = local ? "fixture_database" : "operations";
        List<string> arguments = ["--seconds", "10", "--signal-file", signal];
        if (local) arguments.AddRange(["--container", LocalContainer, "--user", user, "--database", database]);

        var run = Run(arguments, launcher, name => name == "APPDATA" ? appData : null);

        (run.Output.Contains(password, StringComparison.Ordinal) || run.Output.Contains("Password=", StringComparison.OrdinalIgnoreCase)
            || run.Output.Contains(Diagnostic, StringComparison.Ordinal)).ShouldBeFalse("private PostgreSQL text reached the output");
        launcher.Launches.Any(l => l.Arguments.Any(a => a.Contains(password, StringComparison.Ordinal))).ShouldBeFalse("the secret reached a docker argv");
        File.Exists(secretFile).ShouldBe(hasSecret);
        if (hasSecret)
            string.Equals(File.ReadAllText(secretFile), secretJson, StringComparison.Ordinal).ShouldBeTrue("the secret fixture must be unchanged");

        var calls = launcher.Launches;
        calls.ShouldAllBe(l => l.FileName == "docker" && l.CaptureOutput && l.TimeoutMilliseconds == 30000);
        var expectsQuery = success || scenario == Scenario.QueryFailure;
        if (!expectsQuery)
            calls.Any(call => call.Arguments.Contains("psql")).ShouldBeFalse("an unverified endpoint must never start psql");
        var expectsInspect = !local && hasSecret;
        calls.Count.ShouldBe((expectsInspect ? 1 : 0) + (expectsQuery ? (success ? 2 : 1) : 0), run.Output);
        if (expectsInspect)
        {
            calls[0].Arguments.ShouldBe([
                .. Context, "inspect", "--type", "container", "--format",
                """{{.Id}}|{{.State.Running}}|{{json (index .NetworkSettings.Ports "5432/tcp")}}""",
                "antiphon-server2-postgres-postgres-1"]);
        }

        string[] prefix = local ? [] : Context;
        string[] socket = local ? [] : ["-h", "/var/run/postgresql", "-p", "5432"];
        string[] query = [.. prefix, "exec", "-e", "PGOPTIONS=-c default_transaction_read_only=on", local ? LocalContainer : ContainerId,
            "psql", "-X", "-A", "-t", "-w", "-v", "ON_ERROR_STOP=1", .. socket, "-U", user, "-d", database, "-c", CounterSql()];
        foreach (var call in calls.Skip(expectsInspect ? 1 : 0))
            call.Arguments.ShouldBe(query);

        run.Exit.ShouldBe(success ? PgQps.Done : PgQps.Unreachable, run.Output);
        var expectedOutput = scenario switch
        {
            Scenario.Match or Scenario.Override => "windowSeconds=10 qps=0 dbMsPerSecond=0 rowsPerSecond=0 partial=False reason=",
            Scenario.MissingSecret => "Could not resolve the production PostgreSQL connection",
            Scenario.MissingContainer => "Production PostgreSQL lookup via the antiphon-server2 Docker context failed (exit 1)",
            Scenario.QueryFailure => "PostgreSQL query failed (exit 9)",
            _ => "must publish the endpoint in the AppHost user secret",
        };
        run.Lines[0].ShouldContain(expectedOutput);
        if (!success) run.Lines.ShouldBe([run.Lines[0], PgQps.UnreachableLine]);
        File.Exists(signal).ShouldBe(success);
        if (success) File.ReadAllText(signal).ShouldBe("ready");
    }

    [Test]
    public void The_window_reports_each_operation_and_the_top_statements_as_json()
    {
        var fake = new Window();
        var run = Run(["--container", LocalContainer, "--json"], fake.Launcher, _ => null, fake.Clock);

        run.Exit.ShouldBe(PgQps.Done, run.Output);
        run.Lines.ShouldHaveSingleItem().ShouldBe("""
            {"windowSeconds":64,"partial":false,"reason":"","total":{"qps":1.640625,"dbMsPerSecond":2.02734375,"rowsPerSecond":1.453125,"calls":105,"meanMs":1.23571429},"operations":[{"operation":"board.cards","calls":60,"qps":0.9375,"totalMs":120,"msPerSecond":1.875,"rows":60,"rowsPerSecond":0.9375,"meanMs":2},{"operation":"redacted-utility","calls":12,"qps":0.1875,"totalMs":6,"msPerSecond":0.09375,"rows":0,"rowsPerSecond":0,"meanMs":0.5},{"operation":"tx.begin","calls":30,"qps":0.46875,"totalMs":3,"msPerSecond":0.046875,"rows":30,"rowsPerSecond":0.46875,"meanMs":0.1},{"operation":"untagged","calls":3,"qps":0.046875,"totalMs":0.75,"msPerSecond":0.01171875,"rows":3,"rowsPerSecond":0.046875,"meanMs":0.25}],"topByCalls":[{"label":"board.cards","calls":60,"qps":0.9375,"totalMs":120,"msPerSecond":1.875,"rows":60,"rowsPerSecond":0.9375,"meanMs":2},{"label":"tx.begin","calls":30,"qps":0.46875,"totalMs":3,"msPerSecond":0.046875,"rows":30,"rowsPerSecond":0.46875,"meanMs":0.1},{"label":"redacted-utility","calls":12,"qps":0.1875,"totalMs":6,"msPerSecond":0.09375,"rows":0,"rowsPerSecond":0,"meanMs":0.5},{"label":"SELECT * FROM items","calls":3,"qps":0.046875,"totalMs":0.75,"msPerSecond":0.01171875,"rows":3,"rowsPerSecond":0.046875,"meanMs":0.25}],"topByTime":[{"label":"board.cards","calls":60,"qps":0.9375,"totalMs":120,"msPerSecond":1.875,"rows":60,"rowsPerSecond":0.9375,"meanMs":2},{"label":"redacted-utility","calls":12,"qps":0.1875,"totalMs":6,"msPerSecond":0.09375,"rows":0,"rowsPerSecond":0,"meanMs":0.5},{"label":"tx.begin","calls":30,"qps":0.46875,"totalMs":3,"msPerSecond":0.046875,"rows":30,"rowsPerSecond":0.46875,"meanMs":0.1},{"label":"SELECT * FROM items","calls":3,"qps":0.046875,"totalMs":0.75,"msPerSecond":0.01171875,"rows":3,"rowsPerSecond":0.046875,"meanMs":0.25}],"snapshotMs":4000,"serverVersionNum":160004,"serverVersion":"16.4"}
            """);
        JsonDocument.Parse(run.Lines[0]).RootElement.GetProperty("total").GetProperty("calls").GetInt64().ShouldBe(105);

        var sql = fake.Launcher.Launches.Select(l => l.Arguments[^1]).ToArray();
        sql.Length.ShouldBe(3);
        sql[0].ShouldBe(CounterSql());
        sql[1].ShouldBe(CounterSql());
        sql[2].ShouldBe(TextSql().Replace("/*IDS*/", "101,103,104,105"));
        sql.ShouldAllBe(text => !text.Contains("pg_stat_statements_reset"));
    }

    [Test]
    public void The_text_summary_lists_operations_and_the_top_calls()
    {
        var fake = new Window();
        var run = Run(["--container", LocalContainer, "--top", "2"], fake.Launcher, _ => null, fake.Clock);

        run.Exit.ShouldBe(PgQps.Done, run.Output);
        run.Lines.ShouldBe([
            "windowSeconds=64 qps=1.640625 dbMsPerSecond=2.02734375 rowsPerSecond=1.453125 partial=False reason=",
            "operation=board.cards calls=60 qps=0.9375",
            "operation=redacted-utility calls=12 qps=0.1875",
            "operation=tx.begin calls=30 qps=0.46875",
            "operation=untagged calls=3 qps=0.046875",
            "topCalls label=board.cards calls=60",
            "topCalls label=tx.begin calls=30",
        ]);
    }

    [Test]
    [Arguments("dealloc", "dealloc")]
    [Arguments("reset", "stats-reset")]
    [Arguments("evicted", "stats-reset")]
    [Arguments("fewer", "stats-reset")]
    public void A_reset_or_deallocation_in_the_window_is_partial_without_statements(string change, string reason)
    {
        var fake = new Window();
        fake.Second = change switch
        {
            "dealloc" => fake.Second.Replace("|5|2026-11-01", "|6|2026-11-01", StringComparison.Ordinal),
            "reset" => fake.Second.Replace("2026-11-01 00:00:00+00", "2026-11-02 00:00:00+00", StringComparison.Ordinal),
            "evicted" => string.Join("\n", fake.Second.Split('\n').Where(line => !line.Contains("|102|", StringComparison.Ordinal))),
            _ => fake.Second.Replace("|101|70|", "|101|9|", StringComparison.Ordinal),
        };
        var run = Run(["--container", LocalContainer], fake.Launcher, _ => null, fake.Clock);

        run.Exit.ShouldBe(PgQps.Partial, run.Output);
        run.Lines.ShouldBe(["windowSeconds=64 qps=0 dbMsPerSecond=0 rowsPerSecond=0 partial=True reason=" + reason]);
        fake.Launcher.Launches.Count.ShouldBe(2, "a partial window fetches no statement text");
    }

    [Test]
    public void Statement_texts_are_fetched_at_most_500_ids_per_query()
    {
        var fake = new Window
        {
            First = Meta + "||0|0|0",
            Second = string.Join("\n", Enumerable.Range(1000, 1001).Select(id => Meta + "|" + id + "|1|1|1")),
        };
        var run = Run(["--container", LocalContainer, "--json"], fake.Launcher, _ => null, fake.Clock);

        run.Exit.ShouldBe(PgQps.Done, run.Output);
        var texts = fake.Launcher.Launches.Skip(2).Select(l => l.Arguments[^1]).ToArray();
        texts.Select(sql => sql[(sql.LastIndexOf('(') + 1)..sql.LastIndexOf(')')].Split(',').Length).ShouldBe([500, 500, 1]);
        JsonDocument.Parse(run.Lines[0]).RootElement.GetProperty("operations")[0].GetProperty("calls").GetInt64().ShouldBe(1001);
    }

    [Test]
    [Arguments("--seconds", "9")]
    [Arguments("--seconds", "3601")]
    [Arguments("--top", "0")]
    [Arguments("--top", "1001")]
    [Arguments("--database", "bad-name")]
    [Arguments("--user", "")]
    [Arguments("--container", "-fixture")]
    [Arguments("--connection-env", "1BAD")]
    [Arguments("--connection-env", "BOTH")]
    public void Bad_arguments_print_one_line_and_exit_2_before_anything_runs(string flag, string value)
    {
        List<string> arguments = [flag, value];
        if (value == "BOTH") arguments.AddRange(["--container", LocalContainer]);
        var launcher = new RecordingLauncher();
        var children = 0;

        var run = Run(arguments, launcher, _ => "Host=db", psql: (_, _) => { children++; return null; });

        run.Exit.ShouldBe(PgQps.BadArguments);
        run.Lines.ShouldBe(["bad arguments"]);
        launcher.Launches.ShouldBeEmpty();
        children.ShouldBe(0);
    }

    [Test]
    public void A_malformed_command_line_is_a_usage_error()
    {
        Should.Throw<NightlyUsageException>(() => PgQpsOptions.Parse(["-Seconds", "60"], "root"));
        Should.Throw<NightlyUsageException>(() => PgQpsOptions.Parse(["--seconds", "sixty"], "root"));
        var options = PgQpsOptions.Parse([], "root");
        (options.Seconds, options.Container, options.ConnectionEnv, options.Database, options.User, options.Top, options.Json, options.SignalFile, options.RepoRoot)
            .ShouldBe((60, "", "", "antiphon", "antiphon", 20, false, "", "root"));
    }

    [Test]
    public void A_connection_variable_reaches_only_a_read_only_psql_child()
    {
        var password = $"fixture-{Guid.NewGuid():N}";
        var fake = new Window();
        var seen = new List<(IReadOnlyDictionary<string, string> Settings, string Sql)>();
        var run = Run(["--connection-env", "PGQPS_FIXTURE", "--json"], fake.Launcher,
            name => name == "PGQPS_FIXTURE" ? $"Host=db.fixture;Port=6543;Username=reader;Database=ops;Password={password}" : null, fake.Clock,
            (settings, sql) =>
            {
                seen.Add((settings, sql));
                return new PgQpsPsqlResult(0, fake.Answer(sql));
            });

        run.Exit.ShouldBe(PgQps.Done, run.Output);
        run.Output.ShouldNotContain(password);
        fake.Launcher.Launches.ShouldBeEmpty();
        seen.Count.ShouldBe(3);
        seen[0].Settings.ShouldBe(new Dictionary<string, string>
        {
            ["PGHOST"] = "db.fixture",
            ["PGPORT"] = "6543",
            ["PGUSER"] = "reader",
            ["PGDATABASE"] = "ops",
            ["PGPASSWORD"] = password,
            ["PGOPTIONS"] = "-c default_transaction_read_only=on",
        });
        seen[0].Sql.ShouldBe(CounterSql());
    }

    [Test]
    [Arguments("", 0)]
    [Arguments("Port=5432;Username=reader", 0)]
    [Arguments("Host=db.fixture", 2)]
    [Arguments("Host=db.fixture", -1)]
    public void A_missing_variable_host_or_failed_psql_is_unreachable(string connection, int psqlExit)
    {
        var calls = 0;
        var run = Run(["--connection-env", "PGQPS_FIXTURE"], new RecordingLauncher(), _ => connection, psql: (_, _) =>
        {
            calls++;
            return psqlExit < 0 ? null : new PgQpsPsqlResult(psqlExit, "ignored");
        });

        run.Exit.ShouldBe(PgQps.Unreachable);
        run.Lines.ShouldBe([PgQps.UnreachableLine]);
        calls.ShouldBe(connection.StartsWith("Host=", StringComparison.Ordinal) ? 1 : 0);
    }

    [Test]
    public void Connection_strings_parse_in_both_forms()
    {
        PgQps.ConnectionSettings("Host = db ; Port=5433;User ID=x; Password='a;b").ShouldBe(new Dictionary<string, string>
        {
            ["host"] = "db",
            ["port"] = "5433",
            ["user id"] = "x",
            ["password"] = "'a",
        });
        PgQps.ConnectionSettings("host=db port=5433 user=r dbname=d password='a b' broken =x").ShouldBe(new Dictionary<string, string>
        {
            ["host"] = "db",
            ["port"] = "5433",
            ["user"] = "r",
            ["dbname"] = "d",
            ["password"] = "a b",
        });
    }

    [Test]
    [Arguments("", "untagged")]
    [Arguments("-- board.cards\nSELECT 1", "board.cards")]
    [Arguments("-- Board.Cards\nSELECT 1", "-- Board.Cards SELECT 1")]
    [Arguments("SELECT token FROM sessions", "redacted-utility")]
    [Arguments("  rollback  ", "tx.rollback")]
    [Arguments("SAVEPOINT s1", "tx.savepoint")]
    [Arguments("\n\t ", "untagged")]
    public void Labels_name_the_tag_the_transaction_or_the_text(string query, string label)
    {
        PgQps.Label(query).ShouldBe(label);
        PgQps.Label("SELECT " + new string('x', 200)).Length.ShouldBe(120);
        PgQps.OperationOf(PgQps.Label(query)).ShouldBe(label is "-- Board.Cards SELECT 1" ? "untagged" : label);
    }

    [Test]
    public void Missing_sql_or_an_unwritable_signal_file_stop_the_run()
    {
        var root = Directory.CreateTempSubdirectory("c1987-qps-");
        try
        {
            var noSql = Run(["--container", LocalContainer], new RecordingLauncher(), _ => null, repoRoot: root.FullName);
            noSql.Exit.ShouldBe(PgQps.Unreachable);
            noSql.Lines.ShouldBe([PgQps.UnreachableLine]);

            var fake = new Window();
            var signal = Path.Combine(root.FullName, "missing", "ready");
            var unwritable = Run(["--container", LocalContainer, "--signal-file", signal], fake.Launcher, _ => null, fake.Clock);
            unwritable.Exit.ShouldBe(PgQps.Failed);
            unwritable.Output.ShouldContain("pg-qps: the signal file could not be written");
            fake.Launcher.Launches.Count.ShouldBe(1, "the window does not continue without its signal");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static (int Exit, string Output, string[] Lines) Run(List<string> arguments, RecordingLauncher launcher, Func<string, string?> variables,
        FakeTimeProvider? clock = null, Func<IReadOnlyDictionary<string, string>, string, PgQpsPsqlResult?>? psql = null, string? repoRoot = null)
    {
        using var output = new StringWriter();
        clock ??= new FakeTimeProvider(DateTimeOffset.Parse("2026-11-24T09:00:00Z"));
        var host = new NightlyHost
        {
            Clock = clock,
            Git = new FakeGit().Run,
            Launcher = launcher,
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = output,
            Trace = TextWriter.Null,
            Variables = variables,
            Sleep = clock.Advance,
        };
        var seams = new PgQpsSeams { Psql = psql ?? ((_, _) => throw new InvalidOperationException("unexpected psql child")) };
        var exit = PgQps.Run(PgQpsOptions.Parse(arguments, repoRoot ?? DelegateScriptRunner.RepoRoot), host, new FileSystem(), seams);
        var text = output.ToString().ReplaceLineEndings("\n");
        return (exit, text, text.TrimEnd('\n').Split('\n'));
    }

    private static string SnapshotSql() =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "server/Infrastructure/Data/PgStatStatementsSnapshot.sql"));

    private static string CounterSql() => PgQps.Tag + "\n" + SnapshotSql().Split("-- @@split@@", 2)[0].Trim();

    private static string TextSql() => PgQps.Tag + "\n" + SnapshotSql().Split("-- @@split@@", 2)[1].Trim();

    // A window of 60 s (the second counter query takes 4 s more): 101 board.cards, 102 idle, 103 BEGIN, 104 a password change and
    // 105 untagged text; the docker launcher answers both counter snapshots and the statement texts.
    private sealed class Window
    {
        private static readonly Dictionary<string, string> Texts = new()
        {
            ["101"] = "-- board.cards\nSELECT * FROM cards WHERE id = $1",
            ["103"] = "BEGIN",
            ["104"] = "ALTER ROLE app PASSWORD 'x'",
            ["105"] = "SELECT  *\n  FROM   items",
        };
        private int _counters;

        public Window()
        {
            Launcher = new RecordingLauncher(launch => new NightlyLaunchResult { ExitCode = 0, StandardOutputText = Answer(launch.Arguments[^1]) });
        }

        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-24T09:00:00Z"));

        public RecordingLauncher Launcher { get; }

        public string First { get; set; } = string.Join("\n", Meta + "|101|10|20.5|10", Meta + "|102|4|8|40", Meta + "|103|7|1|7");

        public string Second { get; set; } = string.Join("\n", Meta + "|101|70|140.5|70", Meta + "|102|4|8|40", Meta + "|103|37|4|37",
            Meta + "|104|12|6|0", Meta + "|105|3|0.75|3");

        public string Answer(string sql)
        {
            if (sql == CounterSql())
            {
                if (++_counters == 1) return First + "\n";
                Clock.Advance(TimeSpan.FromSeconds(4));
                return Second + "\n";
            }
            var ids = sql[(sql.LastIndexOf('(') + 1)..sql.LastIndexOf(')')].Split(',');
            return string.Join("\n", ids.Select(id => id + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(Texts.GetValueOrDefault(id, "-- bulk\nSELECT 1"))))) + "\n";
        }
    }
}
