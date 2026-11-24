using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DesktopPostgresPowerShellTests
{
    private const string Qps = "pg-qps.ps1";
    private const string Deploy = "deploy-local.ps1";
    private const string Diagnostic = "synthetic PostgreSQL stderr";
    private const string Migration = "20261124000000_Fixture";
    private const string LocalContainer = "fixture-db";
    private static readonly string ContainerId = new('a', 64);
    private static readonly string[] Context = ["--context", "antiphon-server2"];

    public enum Scenario { Match, WrongHost, WrongPort, Stopped, MissingContainer, MissingSecret, QueryFailure, Override }

    [Test]
    [Arguments(Qps, Scenario.Match)]
    [Arguments(Qps, Scenario.WrongHost)]
    [Arguments(Qps, Scenario.WrongPort)]
    [Arguments(Qps, Scenario.Stopped)]
    [Arguments(Qps, Scenario.MissingContainer)]
    [Arguments(Qps, Scenario.MissingSecret)]
    [Arguments(Qps, Scenario.QueryFailure)]
    [Arguments(Qps, Scenario.Override)]
    [Arguments(Deploy, Scenario.Match)]
    [Arguments(Deploy, Scenario.WrongHost)]
    [Arguments(Deploy, Scenario.WrongPort)]
    [Arguments(Deploy, Scenario.Stopped)]
    [Arguments(Deploy, Scenario.MissingContainer)]
    [Arguments(Deploy, Scenario.MissingSecret)]
    [Arguments(Deploy, Scenario.QueryFailure)]
    [Arguments(Deploy, Scenario.Override)]
    public void Scripts_query_only_the_resolved_postgres_target(string script, Scenario scenario)
    {
        var root = Directory.CreateTempSubdirectory("c2065-pwsh-");
        try
        {
            VerifyScript(root.FullName, script, scenario);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static void VerifyScript(string root, string script, Scenario scenario)
    {
        CopyScriptInputs(root, script);
        var appData = Directory.CreateDirectory(Path.Combine(root, "appdata")).FullName;
        var secretFile = Path.Combine(appData, "Microsoft", "UserSecrets", "aspire-antiphon-apphost", "secrets.json");
        var password = $"fixture-{Guid.NewGuid():N}";
        var secretJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Host=192.0.2.1;Port=15432;Username=observer;Database=operations;Password={password}"
        });
        var local = scenario == Scenario.Override;
        var hasSecret = !local && scenario != Scenario.MissingSecret;
        if (hasSecret)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(secretFile)!);
            File.WriteAllText(secretFile, secretJson);
        }

        var trace = Path.Combine(root, "docker.jsonl");
        var signal = Path.Combine(root, "qps-ready");
        var qps = script == Qps;
        var user = local ? "fixture_user" : "observer";
        var database = local ? "fixture_database" : "operations";
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? ScriptHarnessProcessFixture.ResolveInstalledPowerShell() : "pwsh")
        {
            WorkingDirectory = root,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(root, "scripts", script) })
            start.ArgumentList.Add(argument);
        string[] mode = qps ? ["-Seconds", "10", "-SignalFile", signal] : ["-Report"];
        foreach (var argument in mode)
            start.ArgumentList.Add(argument);
        if (local)
        {
            string[] target = qps ? ["-Container", LocalContainer, "-User", user, "-Database", database]
                : ["-PgContainer", LocalContainer, "-PgUser", user, "-PgDatabase", database];
            foreach (var argument in target)
                start.ArgumentList.Add(argument);
        }

        var bin = InstallFakeDocker(root, start);
        // The fake is the only command on the child's PATH, so a missing shim cannot reach real Docker.
        start.Environment["PATH"] = bin;
        start.Environment["APPDATA"] = appData;
        start.Environment["TMPDIR"] = root;
        start.Environment["TEMP"] = root;
        start.Environment["TMP"] = root;
        start.Environment["C2065_DOCKER_TRACE"] = trace;
        start.Environment["C2065_DOCKER_STDERR"] = $"{Diagnostic} Password={password}";
        var host = scenario == Scenario.WrongHost ? "192.0.2.2" : "192.0.2.1";
        var port = scenario == Scenario.WrongPort ? "5432" : "15432";
        var running = scenario == Scenario.Stopped ? "false" : "true";
        start.Environment["C2065_INSPECT"] = $$"""{{ContainerId}}|{{running}}|[{"HostIp":"{{host}}","HostPort":"{{port}}"}]""";
        start.Environment["C2065_INSPECT_EXIT"] = scenario == Scenario.MissingContainer ? "1" : "0";
        var success = scenario is Scenario.Match or Scenario.Override;
        start.Environment["C2065_QUERY"] = qps ? (success ? "160000|16.0|0|2026-01-01||0|0|0" : "") : Migration;
        start.Environment["C2065_QUERY_EXIT"] = scenario == Scenario.QueryFailure ? "9" : "0";

        var result = CheckpointFixtureProcess.Run(start, success && qps ? 25_000 : 10_000);
        AssertPrivateTextAbsent(result.Stdout, password, "stdout");
        AssertPrivateTextAbsent(result.Stderr, password, "stderr");
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            AssertPrivateTextAbsent(file, password, "a fixture file name");
            if (file != secretFile)
                AssertPrivateTextAbsent(File.ReadAllText(file), password, "a fixture file, including captured Docker argv and stdin");
        }
        File.Exists(secretFile).ShouldBe(hasSecret);
        if (hasSecret)
            string.Equals(File.ReadAllText(secretFile), secretJson, StringComparison.Ordinal).ShouldBeTrue("the secret fixture must be unchanged");

        var calls = File.Exists(trace) ? File.ReadLines(trace).Select(line => JsonSerializer.Deserialize<DockerCall>(line)!).ToArray() : [];
        var expectsQuery = success || scenario == Scenario.QueryFailure;
        if (!expectsQuery)
            calls.Any(call => call.Arguments.Contains("psql")).ShouldBeFalse("an unverified endpoint must never start psql");

        var expectsInspect = !local && hasSecret;
        var queryCount = expectsQuery ? (success && qps ? 2 : 1) : 0;
        calls.Length.ShouldBe((expectsInspect ? 1 : 0) + queryCount, result.Stdout + result.Stderr);
        if (expectsInspect)
        {
            calls[0].Arguments.ShouldBe([
                .. Context, "inspect", "--type", "container", "--format",
                """{{.Id}}|{{.State.Running}}|{{json (index .NetworkSettings.Ports "5432/tcp")}}""",
                "antiphon-server2-postgres-postgres-1"]);
            calls[0].Stdin.ShouldBeEmpty();
        }

        string[] prefix = local ? [] : Context;
        string[] socket = local ? [] : ["-h", "/var/run/postgresql", "-p", "5432"];
        string[] query = [.. prefix, "exec", "-e", "PGOPTIONS=-c default_transaction_read_only=on", "-i",
            local ? LocalContainer : ContainerId, "psql", "-X", "-A", "-t", "-w", "-v", "ON_ERROR_STOP=1",
            .. socket, "-U", user, "-d", database, "-f", "-"];
        foreach (var call in calls.Skip(expectsInspect ? 1 : 0))
        {
            call.Arguments.ShouldBe(query);
            var sql = qps
                ? "-- rt.diag.pgss\n" + ReadCounterSql(root)
                : """SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";""";
            call.Stdin.TrimEnd().ReplaceLineEndings("\n").ShouldBe(sql.ReplaceLineEndings("\n"));
        }

        result.Exit.ShouldBe(success ? 0 : qps ? 3 : 1, result.Stdout + result.Stderr);
        var expectedOutput = scenario switch
        {
            Scenario.Match or Scenario.Override => qps ? "windowSeconds=" : "DEPLOY VERDICT: ok",
            Scenario.MissingSecret => "Could not resolve the production PostgreSQL connection",
            Scenario.MissingContainer => "Production PostgreSQL lookup via the antiphon-server2 Docker context failed (exit 1)",
            Scenario.QueryFailure => "PostgreSQL query failed (exit 9)",
            _ => "must publish the endpoint in the AppHost user secret",
        };
        result.Stdout.ShouldContain(expectedOutput);
        if (!success)
            result.Stdout.ShouldContain(qps ? "database unreachable or extension not loaded" : "DEPLOY VERDICT: failed");
        File.Exists(signal).ShouldBe(success && qps);
        if (success && qps)
            File.ReadAllText(signal).ShouldBe("ready");
    }

    private static string ReadCounterSql(string root) => File.ReadAllText(Path.Combine(root,
        "server/Infrastructure/Data/PgStatStatementsSnapshot.sql")).Split("-- @@split@@", 2)[0].Trim();

    private static void AssertPrivateTextAbsent(string text, string password, string channel)
    {
        // Assert a boolean so a regression never puts the captured text into the test report.
        (text.Contains(password, StringComparison.Ordinal) || text.Contains("Password=", StringComparison.OrdinalIgnoreCase)
            || text.Contains(Diagnostic, StringComparison.Ordinal)).ShouldBeFalse($"private PostgreSQL text reached {channel}");
    }

    private static void CopyScriptInputs(string root, string script)
    {
        string[] inputs = [$"scripts/{script}", "scripts/lib/desktop-postgres.ps1", "scripts/lib/antiphon-cli.ps1",
            script == Qps ? "server/Infrastructure/Data/PgStatStatementsSnapshot.sql" : "scripts/apphost-common.ps1"];
        foreach (var input in inputs)
        {
            var destination = Path.Combine(root, input);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, input), destination);
        }
        if (script == Deploy)
        {
            var migrations = Directory.CreateDirectory(Path.Combine(root, "server", "Migrations")).FullName;
            File.WriteAllText(Path.Combine(migrations, Migration + ".cs"), "");
        }
    }

    private static string InstallFakeDocker(string root, ProcessStartInfo start)
    {
        var bin = Directory.CreateDirectory(Path.Combine(root, "bin")).FullName;
        var helper = Path.Combine(AppContext.BaseDirectory, "custody-child", "Antiphon.CustodyTestChild");
        if (OperatingSystem.IsWindows())
        {
            foreach (var extension in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
                File.Copy(helper + extension, Path.Combine(bin, Path.GetFileName(helper) + extension));
            File.Copy(helper + ".exe", Path.Combine(bin, "docker.exe"));
        }
        else
        {
            start.Environment["C2065_DOTNET"] = ScriptHarnessProcessFixture.ResolveDotnet();
            start.Environment["C2065_DOCKER_DLL"] = helper + ".dll";
            FixtureExecutable.Write(Path.Combine(bin, "docker"), """
                #!/bin/sh
                exec "$C2065_DOTNET" "$C2065_DOCKER_DLL" "$@"
                """ + "\n");
        }
        return bin;
    }

    private sealed record DockerCall(string[] Arguments, string Stdin);
}
