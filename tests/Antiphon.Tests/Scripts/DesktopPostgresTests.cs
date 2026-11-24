using System.IO.Abstractions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class DesktopPostgresTests
{
    private const string Container = "antiphon-server2-postgres-postgres-1";
    private const string Diagnostic = "uncaptured child diagnostics";
    private static readonly string ContainerId = new('a', 64);

    [Test]
    [Arguments("zombie", "match")]
    [Arguments("pty", "match")]
    [Arguments("measure", "match")]
    [Arguments("zombie", "mismatch")]
    [Arguments("pty", "mismatch")]
    [Arguments("measure", "mismatch")]
    [Arguments("zombie", "missing")]
    [Arguments("pty", "query failure")]
    [Arguments("measure", "stopped")]
    [Arguments("zombie", "override")]
    [Arguments("pty", "override")]
    [Arguments("measure", "override")]
    public void Commands_use_only_a_verified_postgres_target(string command, string scenario)
    {
        var root = Directory.CreateTempSubdirectory("card-2065-");
        try
        {
            VerifyTarget(command, scenario, root.FullName);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static void VerifyTarget(string command, string scenario, string root)
    {
        var secrets = Path.Combine(root, "Microsoft", "UserSecrets", "aspire-antiphon-apphost");
        var context = Path.Combine(root, "context.json");
        File.WriteAllText(context, "{}");
        var local = scenario == "override";
        if (!local && scenario != "missing")
        {
            Directory.CreateDirectory(secrets);
            File.WriteAllText(Path.Combine(secrets, "secrets.json"), command == "pty"
                ? """{"ConnectionStrings":{"DefaultConnection":"Host=192.0.2.1;Port=15432;User ID=observer;Database=operations"}}"""
                : """{"ConnectionStrings:DefaultConnection":"Host=192.0.2.1;Port=15432;Username=observer;Database=operations"}""");
        }

        var launcher = new RecordingLauncher(launch =>
        {
            var args = launch.Arguments;
            if (args.Contains(Container))
            {
                var hostPort = scenario == "mismatch" ? "5432" : "15432";
                var running = scenario == "stopped" ? "false" : "true";
                return new NightlyLaunchResult
                {
                    ExitCode = 0,
                    StandardOutputText = ContainerId + "|" + running + "|[{\"HostIp\":\"192.0.2.1\",\"HostPort\":\"" + hostPort + "\"}]",
                };
            }
            if (args.Contains("psql") && scenario == "query failure")
                return new NightlyLaunchResult { ExitCode = 9, LogText = Diagnostic, StandardErrorText = Diagnostic, StandardOutputText = Diagnostic };
            var stdout = args.Contains("psql") ? "{}" : args.Contains("inspect") ? ContainerId + "|2026-11-24" : "usage_usec 2000000";
            return new NightlyLaunchResult { ExitCode = 0, StandardOutputText = stdout, StandardErrorText = Diagnostic };
        });
        using var output = new StringWriter();
        var host = new NightlyHost
        {
            Clock = TimeProvider.System,
            Launcher = launcher,
            Git = new FakeGit().Run,
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = output,
            Trace = output,
            Variables = key => key == "APPDATA" ? root : null,
        };

        void Run()
        {
            switch (command)
            {
                case "zombie":
                    var zombie = ZombieAgentReaperOptions.Parse(local
                        ? ["--pg-container", "fixture-db", "--pg-user", "fixture-user", "--pg-database", "fixture-database"] : [], "", root);
                    ZombieAgentReaper.Production(host).Query(zombie.PgContainer, zombie.PgUser, zombie.PgDatabase, "SELECT 1;").Output.ShouldBe("{}");
                    break;
                case "pty":
                    var pty = PtyHostReaperOptions.Parse(local
                        ? ["--pg-container", "fixture-db", "--pg-user", "fixture-user", "--pg-database", "fixture-database"] : []);
                    PtyHostReaper.Production(host).Query(pty.PgContainer, pty.PgUser, pty.PgDatabase, "SELECT 1;").Output.ShouldBe("{}");
                    break;
                case "measure":
                    List<string> arguments = ["--evidence-root", root, "--context-path", context];
                    if (local) arguments.AddRange(["--postgres-container", "fixture-db", "--database-user", "fixture-user", "--database", "fixture-database"]);
                    var measure = SessionStateCacheOptions.Parse(arguments, "http://fixture.test", "", root);
                    var snapshot = SessionStateCacheMeasure.Snapshot(measure, "After", host, new FileSystem(), new SessionStateCacheSeams
                    {
                        Http = (_, _, _, _) => new NightlyReportHttpResponse(200, "{}"),
                        Process = _ => throw new InvalidOperationException("unexpected process sample"),
                    });
                    snapshot["postgres"]!["containerCpuSeconds"]!.GetValue<double>().ShouldBe(2);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command));
            }
        }

        var expectedError = scenario switch
        {
            "mismatch" or "stopped" => "must publish the endpoint in the AppHost user secret",
            "missing" => "Could not resolve the production PostgreSQL connection",
            "query failure" => "PostgreSQL query failed (exit 9)",
            _ => null,
        };
        if (expectedError != null)
        {
            var error = Should.Throw<InvalidOperationException>(Run);
            error.Message.ShouldContain(expectedError);
            error.ToString().ShouldNotContain(Diagnostic);
        }
        else
        {
            Run();
        }

        var launches = launcher.Launches;
        if (!local && scenario != "missing")
        {
            var lookup = launches.First();
            lookup.Arguments.Take(5).ShouldBe(["--context", "antiphon-server2", "inspect", "--type", "container"]);
            lookup.Arguments.Last().ShouldBe(Container);
            launches.ShouldAllBe(l => l.Arguments.Take(2).SequenceEqual(new[] { "--context", "antiphon-server2" }));
        }
        else
        {
            launches.ShouldAllBe(l => !l.Arguments.Contains("--context"));
        }
        launches.ShouldAllBe(l => l.FileName == "docker" && l.CaptureOutput && l.TimeoutMilliseconds == 30000);
        var queries = launches.Where(l => l.Arguments.Contains("psql")).ToArray();
        if (expectedError != null && scenario != "query failure")
        {
            queries.ShouldBeEmpty();
            launches.Count.ShouldBe(scenario == "missing" ? 0 : 1);
        }
        else
        {
            var query = queries.ShouldHaveSingleItem().Arguments;
            query.ShouldContain("PGOPTIONS=-c default_transaction_read_only=on");
            query.ShouldContain("ON_ERROR_STOP=1");
            query.ShouldContain("-w");
            query.ShouldContain(local ? "fixture-user" : "observer");
            query.ShouldContain(local ? "fixture-database" : "operations");
            query.Last().ShouldBe(command == "measure" ? SessionStateCacheMeasure.StatisticsSql : "SELECT 1;");
            if (!local) query.ShouldContain("/var/run/postgresql");
            var target = local ? "fixture-db" : ContainerId;
            var operations = local ? launches : launches.Skip(1).ToArray();
            operations.Count.ShouldBe(command == "measure" ? 3 : 1);
            operations.ShouldAllBe(l => l.Arguments.Contains(target));
        }
        output.ToString().ShouldNotContain(Diagnostic);
    }
}
