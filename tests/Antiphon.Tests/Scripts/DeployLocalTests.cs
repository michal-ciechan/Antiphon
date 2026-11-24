using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 49 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the desktop deploy (Antiphon.Nightly.DeployLocal, the
/// deploy-local command that replaced scripts/deploy-local.ps1) in process on AppHostWorld. Git is the world's FakeGit (the
/// worktree guard, CARD-0273), Docker a recording launcher answering the server2 lookup and psql, the AppHost user secret a fixture
/// file under a fixture APPDATA, the migrations a fixture server/Migrations, and the restart and the verification either recording
/// steps or the real restart-apphost and verify-dev-stack on the world. The CARD-2065 resolution rows were
/// DesktopPostgresPowerShellTests', which started the script in pwsh against a fake docker. Nothing reaches a real database,
/// Docker daemon, user secret, AppHost or port.
/// </summary>
[Category("Unit")]
public sealed class DeployLocalTests
{
    private const string Diagnostic = "synthetic PostgreSQL stderr";
    private const string Migration = "20261124000000_Fixture";
    private const string LocalContainer = "fixture-db";
    private static readonly string ContainerId = new('a', 64);
    private static readonly string[] Context = ["--context", "antiphon-server2"];
    private static readonly string[] Inspect = [.. Context, "inspect", "--type", "container", "--format",
        """{{.Id}}|{{.State.Running}}|{{json (index .NetworkSettings.Ports "5432/tcp")}}""", "antiphon-server2-postgres-postgres-1"];

    public enum Scenario { Match, WrongHost, WrongPort, Stopped, MissingContainer, MissingSecret, QueryFailure, Override }

    /// <summary>The recording restart-apphost and verify-dev-stack steps, and the order of every step and Docker call.</summary>
    private sealed class Steps
    {
        public List<string> Events { get; } = [];

        public AppHostRestartOptions? Restart { get; private set; }

        public DevStackVerifyOptions? Verify { get; private set; }

        public int RestartExit { get; set; }

        public int VerifyExit { get; set; }

        public DeployLocalSteps Bind() => new()
        {
            RestartAppHost = (options, _) => { Events.Add("restart"); Restart = options; return RestartExit; },
            VerifyDevStack = (options, _) => { Events.Add("verify"); Verify = options; return VerifyExit; },
        };
    }

    /// <summary>A docker launcher: the server2 lookup answers inspect, psql answers rows; every call is an event.</summary>
    private static RecordingLauncher Docker(List<string> events, string rows = Migration + "\n", string inspect = "", int inspectExit = 0, int queryExit = 0,
        string stderr = Diagnostic) =>
        new(launch =>
        {
            var isInspect = launch.Arguments.Contains("inspect");
            events.Add(isInspect ? "inspect" : "psql");
            if (isInspect)
                return new NightlyLaunchResult { ExitCode = inspectExit, StandardOutputText = inspectExit == 0 ? inspect : "", StandardErrorText = stderr };
            return new NightlyLaunchResult { ExitCode = queryExit, StandardOutputText = queryExit == 0 ? rows : "", StandardErrorText = stderr };
        });

    private static NightlyHost Host(AppHostWorld world, INightlyLauncher launcher, string? appData = null, FakeGit? git = null) => new()
    {
        Clock = world.Clock,
        Git = (git ?? world.Git).Run,
        Launcher = launcher,
        Processes = new FakeProcessTable(),
        Probe = new FakeHostProbe(),
        Out = world.Out,
        Error = world.Error,
        Trace = TextWriter.Null,
        Sleep = span => { world.Record("sleep " + span.TotalSeconds); world.Clock.Advance(span); },
        // Never this process's environment: APPDATA is the fixture's (or unset), so no real user secret is read.
        Variables = name => name == "APPDATA" ? appData : null,
    };

    private static (int Exit, string Output, string[] Lines) Deploy(AppHostWorld world, NightlyHost host, DeployLocalSteps steps, params string[] arguments)
    {
        var before = world.Out.ToString().Length;
        var exit = DeployLocal.Run(DeployLocalOptions.Parse(arguments, world.Root), host, world.Files, steps);
        var text = (world.Out.ToString()[before..] + world.Error.ToString()).ReplaceLineEndings("\n");
        return (exit, text, text.TrimEnd('\n').Split('\n'));
    }

    private static void WriteMigrations(AppHostWorld world, params string[] names)
    {
        var directory = Directory.CreateDirectory(world.PathOf("server", "Migrations")).FullName;
        foreach (var name in names) File.WriteAllText(Path.Combine(directory, name), "");
    }

    private static string WriteSecret(string appData, string password)
    {
        var secretFile = Path.Combine(appData, "Microsoft", "UserSecrets", "aspire-antiphon-apphost", "secrets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(secretFile)!);
        File.WriteAllText(secretFile, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Host=192.0.2.1;Port=15432;Username=observer;Database=operations;Password={password}",
        }));
        return secretFile;
    }

    private static string Published(string host = "192.0.2.1", string port = "15432", string running = "true") =>
        $$"""{{ContainerId}}|{{running}}|[{"HostIp":"{{host}}","HostPort":"{{port}}"}]""";

    private static string[] Query(bool local, string user, string database)
    {
        string[] prefix = local ? [] : Context;
        string[] socket = local ? [] : ["-h", "/var/run/postgresql", "-p", "5432"];
        return [.. prefix, "exec", "-e", "PGOPTIONS=-c default_transaction_read_only=on", local ? LocalContainer : ContainerId,
            "psql", "-X", "-A", "-t", "-w", "-v", "ON_ERROR_STOP=1", .. socket, "-U", user, "-d", database, "-c", DeployLocal.HistorySql];
    }

    // ---- CARD-2065: the deploy reads only the resolved PostgreSQL target (the rows of DesktopPostgresPowerShellTests, -Report) ----

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
        using var world = new AppHostWorld();
        WriteMigrations(world, Migration + ".cs");
        var appData = Directory.CreateDirectory(world.PathOf("appdata")).FullName;
        var password = $"fixture-{Guid.NewGuid():N}";
        var local = scenario == Scenario.Override;
        var hasSecret = !local && scenario != Scenario.MissingSecret;
        string? secretFile = null;
        if (hasSecret) secretFile = WriteSecret(appData, password);
        var secretJson = secretFile == null ? null : File.ReadAllText(secretFile);

        var events = new List<string>();
        var launcher = Docker(events,
            inspect: Published(scenario == Scenario.WrongHost ? "192.0.2.2" : "192.0.2.1", scenario == Scenario.WrongPort ? "5432" : "15432",
                scenario == Scenario.Stopped ? "false" : "true"),
            inspectExit: scenario == Scenario.MissingContainer ? 1 : 0, queryExit: scenario == Scenario.QueryFailure ? 9 : 0,
            stderr: $"{Diagnostic} Password={password}");
        var user = local ? "fixture_user" : "observer";
        var database = local ? "fixture_database" : "operations";
        List<string> arguments = ["--report"];
        if (local) arguments.AddRange(["--pg-container", LocalContainer, "--pg-user", user, "--pg-database", database]);
        var steps = new Steps();

        var run = Deploy(world, Host(world, launcher, appData), steps.Bind(), [.. arguments]);

        // Assert booleans so a regression never puts the captured text into the test report.
        (run.Output.Contains(password, StringComparison.Ordinal) || run.Output.Contains("Password=", StringComparison.OrdinalIgnoreCase)
            || run.Output.Contains(Diagnostic, StringComparison.Ordinal)).ShouldBeFalse("private PostgreSQL text reached the output");
        launcher.Launches.Any(l => l.Arguments.Any(a => a.Contains(password, StringComparison.Ordinal))).ShouldBeFalse("the secret reached a docker argv");
        foreach (var file in Directory.EnumerateFiles(world.Root, "*", SearchOption.AllDirectories).Where(file => file != secretFile))
            (file.Contains(password, StringComparison.Ordinal) || File.ReadAllText(file).Contains(password, StringComparison.Ordinal))
                .ShouldBeFalse("private PostgreSQL text reached a fixture file");
        if (hasSecret)
            string.Equals(File.ReadAllText(secretFile!), secretJson, StringComparison.Ordinal).ShouldBeTrue("the secret fixture must be unchanged");
        steps.Events.ShouldBeEmpty("--report neither restarts nor verifies");

        var calls = launcher.Launches;
        calls.ShouldAllBe(l => l.FileName == "docker" && l.CaptureOutput && l.TimeoutMilliseconds == 30000);
        var success = scenario is Scenario.Match or Scenario.Override;
        var expectsQuery = success || scenario == Scenario.QueryFailure;
        if (!expectsQuery)
            calls.Any(call => call.Arguments.Contains("psql")).ShouldBeFalse("an unverified endpoint must never start psql");
        var expectsInspect = !local && hasSecret;
        calls.Count.ShouldBe((expectsInspect ? 1 : 0) + (expectsQuery ? 1 : 0), run.Output);
        if (expectsInspect) calls[0].Arguments.ShouldBe(Inspect);
        foreach (var call in calls.Skip(expectsInspect ? 1 : 0))
            call.Arguments.ShouldBe(Query(local, user, database));

        run.Exit.ShouldBe(success ? DeployLocal.Ok : DeployLocal.Failed, run.Output);
        var expectedOutput = scenario switch
        {
            Scenario.Match or Scenario.Override => "DEPLOY VERDICT: ok",
            Scenario.MissingSecret => "Could not resolve the production PostgreSQL connection",
            Scenario.MissingContainer => "Production PostgreSQL lookup via the antiphon-server2 Docker context failed (exit 1)",
            Scenario.QueryFailure => "PostgreSQL query failed (exit 9)",
            _ => "must publish the endpoint in the AppHost user secret",
        };
        run.Output.ShouldContain(expectedOutput);
        if (!success) run.Lines[^1].ShouldStartWith("DEPLOY VERDICT: failed ");
        else run.Lines.ShouldBe(["EF migrations verified: 1 source and database history entries.", "DEPLOY VERDICT: ok"]);
    }

    // ---- CARD-0273: the worktree guard comes before any side effect ----

    [Test]
    public void A_linked_worktree_refuses_before_postgres_the_restart_or_the_verification()
    {
        using var world = new AppHostWorld { MainWorktree = Path.Combine(Path.GetTempPath(), "deploy-local-main-" + Guid.NewGuid().ToString("N")) };
        WriteMigrations(world, Migration + ".cs");
        var events = new List<string>();
        var launcher = Docker(events);
        var steps = new Steps();
        var host = Host(world, launcher, world.PathOf("appdata"));

        var run = Deploy(world, host, steps.Bind(), "--no-build", "--pg-container", LocalContainer);

        run.Exit.ShouldBe(DeployLocal.Refused, run.Output);
        var linked = AppHostWorktree.Classify(host, world.Files, world.Root);
        linked.IsMainWorktree.ShouldBeFalse();
        run.Lines.ShouldBe(["DEPLOY VERDICT: refused " + string.Join(" ", linked.GuardMessage(false))]);
        run.Output.ShouldContain("REFUSED: this AppHost command is rooted in a linked Git worktree.");
        run.Output.ShouldContain("re-run with --allow-worktree.");
        run.Output.ShouldContain("Nothing was killed or started.");
        launcher.Launches.ShouldBeEmpty("no Docker call before the guard");
        steps.Events.ShouldBeEmpty("no restart or verification before the guard");
        world.Trace.ShouldBeEmpty("nothing on the machine");
    }

    [Test]
    public void An_unverifiable_worktree_refuses_before_any_side_effect()
    {
        using var world = new AppHostWorld();
        var git = new FakeGit().Reply("--show-toplevel", new NightlyGitResult(128, "fatal: not a git repository (or any of the parent directories): .git\n"));
        var launcher = Docker([]);
        var steps = new Steps();

        var run = Deploy(world, Host(world, launcher, git: git), steps.Bind(), "--pg-container", LocalContainer);

        run.Exit.ShouldBe(DeployLocal.Refused, run.Output);
        run.Lines.ShouldHaveSingleItem().ShouldStartWith("DEPLOY VERDICT: refused REFUSED: this AppHost command could not verify its Git worktree root.   Failed Git step: git -C \""
            + world.Root + "\" rev-parse --show-toplevel failed (exit 128): fatal: not a git repository");
        run.Output.ShouldContain("No teardown or startup occurred.");
        git.Lines.ShouldBe(["GIT rev-parse --show-toplevel"]);
        launcher.Launches.ShouldBeEmpty();
        steps.Events.ShouldBeEmpty();
    }

    [Test]
    public void Allow_worktree_warns_then_restarts_verifies_and_compares_in_that_order()
    {
        using var world = new AppHostWorld { MainWorktree = Path.Combine(Path.GetTempPath(), "deploy-local-main-" + Guid.NewGuid().ToString("N")) };
        WriteMigrations(world, Migration + ".cs");
        var steps = new Steps();
        var launcher = Docker(steps.Events);
        var host = Host(world, launcher);

        var run = Deploy(world, host, steps.Bind(), "--allow-worktree", "--no-build", "--timeout-sec", "9", "--pg-container", LocalContainer);

        run.Exit.ShouldBe(DeployLocal.Ok, run.Output);
        var warning = AppHostWorktree.Classify(host, world.Files, world.Root).GuardMessage(true);
        run.Lines.ShouldBe([.. warning, "EF migrations verified: 1 source and database history entries.", "DEPLOY VERDICT: ok"]);
        run.Output.ShouldContain("--allow-worktree explicitly permits this worktree to affect the shared stack.");
        steps.Events.ShouldBe(["restart", "verify", "psql"]);

        // restart-apphost.ps1 -TimeoutSec 9 -NoBuild -AllowWorktree, every other parameter its default.
        var restart = steps.Restart.ShouldNotBeNull();
        (restart.NoBuild, restart.TimeoutSec, restart.AllowWorktree, restart.MaxWaitSec, restart.ExpectedServerSha, restart.GracefulStopTimeoutSec, restart.SkipGracefulStop,
            restart.StopOnly, restart.WhatIf, restart.LockMaxAgeMinutes, restart.RepoRoot).ShouldBe((true, 9, true, 600, "", 20, false, false, false, AppHost.LockMaxAgeMinutes, world.Root));
        // verify-dev-stack.ps1 -SkipBrowser -TimeoutSec 30.
        var verify = steps.Verify.ShouldNotBeNull();
        (verify.SkipBrowser, verify.TimeoutSec, verify.SimpleMode, verify.ProbeListeningPorts, verify.RepoRoot).ShouldBe((true, 30, false, false, world.Root));
    }

    [Test]
    public void The_main_worktree_deploys_without_a_warning_and_passes_the_timeout_always()
    {
        using var world = new AppHostWorld();
        WriteMigrations(world, Migration + ".cs");
        var appData = Directory.CreateDirectory(world.PathOf("appdata")).FullName;
        WriteSecret(appData, "fixture-secret");
        var steps = new Steps();
        var launcher = Docker(steps.Events, inspect: Published());

        var run = Deploy(world, Host(world, launcher, appData), steps.Bind());

        run.Exit.ShouldBe(DeployLocal.Ok, run.Output);
        run.Lines.ShouldBe(["EF migrations verified: 1 source and database history entries.", "DEPLOY VERDICT: ok"]);
        // The target is resolved before the restart (Resolve-DesktopPostgresTarget came first in the script's try).
        steps.Events.ShouldBe(["inspect", "restart", "verify", "psql"]);
        DeployLocal.RestartArguments(DeployLocalOptions.Parse([], world.Root)).ShouldBe(["--timeout-sec", "150"]);
        (steps.Restart!.NoBuild, steps.Restart.TimeoutSec, steps.Restart.AllowWorktree).ShouldBe((false, 150, false));
    }

    // ---- the restart, then the verification: a non-zero exit stops the deploy ----

    [Test]
    [Arguments(3, 0, "restart-apphost exited 3")]
    [Arguments(5, 0, "restart-apphost exited 5")]
    [Arguments(1, 0, "restart-apphost exited 1")]
    [Arguments(0, 1, "verify-dev-stack exited 1")]
    public void A_failed_step_stops_the_deploy_before_the_history_is_read(int restartExit, int verifyExit, string detail)
    {
        using var world = new AppHostWorld();
        WriteMigrations(world, Migration + ".cs");
        var appData = Directory.CreateDirectory(world.PathOf("appdata")).FullName;
        WriteSecret(appData, "fixture-secret");
        var steps = new Steps { RestartExit = restartExit, VerifyExit = verifyExit };
        var launcher = Docker(steps.Events, inspect: Published());

        var run = Deploy(world, Host(world, launcher, appData), steps.Bind());

        run.Exit.ShouldBe(DeployLocal.Failed, run.Output);
        run.Lines.ShouldBe(["DEPLOY VERDICT: failed " + detail]);
        steps.Events.ShouldBe(restartExit != 0 ? ["inspect", "restart"] : ["inspect", "restart", "verify"]);
        launcher.Launches.ShouldNotContain(l => l.Arguments.Contains("psql"));
    }

    [Test]
    public void A_target_that_does_not_resolve_fails_before_the_restart()
    {
        using var world = new AppHostWorld();
        WriteMigrations(world, Migration + ".cs");
        var steps = new Steps();
        var launcher = Docker(steps.Events);

        var run = Deploy(world, Host(world, launcher), steps.Bind());

        run.Exit.ShouldBe(DeployLocal.Failed, run.Output);
        run.Lines.ShouldHaveSingleItem().ShouldStartWith("DEPLOY VERDICT: failed Could not resolve the production PostgreSQL connection.");
        steps.Events.ShouldBeEmpty("no restart without a database to compare");
    }

    [Test]
    [Arguments("bad name")]
    [Arguments("-rm")]
    [Arguments(" ")]
    [Arguments("_db")]
    public void A_container_that_is_not_a_plain_name_fails_before_docker(string container)
    {
        using var world = new AppHostWorld();
        WriteMigrations(world, Migration + ".cs");
        var steps = new Steps();
        var launcher = Docker(steps.Events);

        var run = Deploy(world, Host(world, launcher), steps.Bind(), "--pg-container", container);

        run.Exit.ShouldBe(DeployLocal.Failed, run.Output);
        run.Lines.ShouldBe(["DEPLOY VERDICT: failed Specify a PostgreSQL container name or ID."]);
        launcher.Launches.ShouldBeEmpty();
        steps.Events.ShouldBeEmpty();
    }

    // ---- the EF migration history ----

    [Test]
    public void The_history_must_hold_every_source_migration_and_no_other()
    {
        using var world = new AppHostWorld();
        WriteMigrations(world, "20260101000000_A.cs", "20260101000000_A.Designer.cs", "20260102000000_B.cs", "20260102000000_B.Designer.cs",
            "AppDbContextModelSnapshot.cs", "20260103000000_C.cs", "README.md");
        var steps = new Steps();

        var drift = Deploy(world, Host(world, Docker(steps.Events, rows: "20260102000000_B\n 20260104000000_D \n\n20260105000000_E\n")), steps.Bind(), "--report",
            "--pg-container", LocalContainer);
        drift.Exit.ShouldBe(DeployLocal.Failed, drift.Output);
        drift.Lines.ShouldBe(["DEPLOY VERDICT: failed pending migrations: 20260101000000_A, 20260103000000_C; database-only migrations: 20260104000000_D, 20260105000000_E"]);

        var pendingOnly = Deploy(world, Host(world, Docker([], rows: "20260101000000_A\n")), steps.Bind(), "--report", "--pg-container", LocalContainer);
        pendingOnly.Lines.ShouldBe(["DEPLOY VERDICT: failed pending migrations: 20260102000000_B, 20260103000000_C"]);

        // The comparison ignores case, as the script's -notin did; the count is the source's.
        var ok = Deploy(world, Host(world, Docker([], rows: "20260101000000_a\r\n20260102000000_B\r\n20260103000000_C\r\n")), steps.Bind(), "--report", "--pg-container", LocalContainer);
        ok.Exit.ShouldBe(DeployLocal.Ok, ok.Output);
        ok.Lines.ShouldBe(["EF migrations verified: 3 source and database history entries.", "DEPLOY VERDICT: ok"]);
        steps.Events.ShouldBe(["psql"], "the first run's only call; --report never restarts");
    }

    [Test]
    public void Source_migrations_are_sorted_unique_and_skip_hidden_designer_and_snapshot_files()
    {
        using var world = new AppHostWorld();
        WriteMigrations(world, "20260102000000_B.cs", "20260101000000_A.cs", "x.Designer.cs", "Y.DESIGNER.CS", "appdbcontextmodelsnapshot.cs", "notes.txt");
        // Get-ChildItem without -Force skipped hidden files: the attribute on Windows, a leading dot elsewhere.
        var hidden = world.PathOf("server", "Migrations", OperatingSystem.IsWindows() ? "20260109000000_Hidden.cs" : ".20260109000000_Hidden.cs");
        File.WriteAllText(hidden, "");
        if (OperatingSystem.IsWindows()) File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        Directory.CreateDirectory(world.PathOf("server", "Migrations", "20260110000000_Folder.cs"));

        DeployLocal.SourceMigrationIds(world.Files, world.PathOf("server", "Migrations")).ShouldBe(["20260101000000_A", "20260102000000_B"]);
        DeployLocal.RecordedMigrationIds(" a \r\n\r\nb\n").ShouldBe(["a", "b"]);
        DeployLocal.RecordedMigrationIds("").ShouldBeEmpty();
    }

    [Test]
    public void A_missing_migration_directory_fails_after_the_steps()
    {
        using var world = new AppHostWorld();
        var steps = new Steps();

        var run = Deploy(world, Host(world, Docker(steps.Events)), steps.Bind(), "--pg-container", LocalContainer);

        run.Exit.ShouldBe(DeployLocal.Failed, run.Output);
        run.Lines.ShouldBe(["DEPLOY VERDICT: failed Migration directory not found: " + Path.Combine(world.Root, "server", "Migrations")]);
        steps.Events.ShouldBe(["restart", "verify"], "the script listed the source migrations after the verification");
    }

    [Test]
    public void Report_skips_the_guard_the_restart_and_the_verification()
    {
        using var world = new AppHostWorld { MainWorktree = Path.Combine(Path.GetTempPath(), "deploy-local-main-" + Guid.NewGuid().ToString("N")) };
        WriteMigrations(world, Migration + ".cs");
        var steps = new Steps();

        var run = Deploy(world, Host(world, Docker(steps.Events)), steps.Bind(), "--report", "--allow-worktree", "--no-build", "--pg-container", LocalContainer);

        run.Exit.ShouldBe(DeployLocal.Ok, run.Output);
        run.Lines.ShouldBe(["EF migrations verified: 1 source and database history entries.", "DEPLOY VERDICT: ok"]);
        steps.Events.ShouldBe(["psql"]);
        world.Git.Lines.ShouldBeEmpty("a read-only report never classifies the worktree");
    }

    // ---- the verdict line ----

    [Test]
    public void A_failure_is_one_line_and_the_verdict_is_always_last()
    {
        DeployLocal.Detail(new InvalidOperationException("first\r\n  second\tthird ")).ShouldBe("first second third");
        DeployLocal.Detail(new InvalidOperationException(" \n ")).ShouldBe("unknown error");

        using var world = new AppHostWorld();
        WriteMigrations(world, Migration + ".cs");
        var steps = new DeployLocalSteps
        {
            RestartAppHost = (_, host) => { host.Out.WriteLine("restart line"); throw new IOException("lock\nunreadable"); },
            VerifyDevStack = (_, _) => throw new InvalidOperationException("unexpected verification"),
        };
        var run = Deploy(world, Host(world, Docker([])), steps, "--pg-container", LocalContainer);
        run.Exit.ShouldBe(DeployLocal.Failed, run.Output);
        run.Lines.ShouldBe(["restart line", "DEPLOY VERDICT: failed lock unreadable"]);
    }

    [Test]
    public void The_flags_are_the_scripts_parameters_in_kebab_case()
    {
        var defaults = DeployLocalOptions.Parse([], "root");
        (defaults.NoBuild, defaults.TimeoutSec, defaults.AllowWorktree, defaults.Report, defaults.PgContainer, defaults.PgUser, defaults.PgDatabase, defaults.RepoRoot)
            .ShouldBe((false, 150, false, false, "", "antiphon", "antiphon", "root"));
        var all = DeployLocalOptions.Parse(["--no-build", "--timeout-sec", "60", "--allow-worktree", "--report", "--pg-container", "db", "--pg-user", "u", "--pg-database", "d"], "root");
        (all.NoBuild, all.TimeoutSec, all.AllowWorktree, all.Report, all.PgContainer, all.PgUser, all.PgDatabase).ShouldBe((true, 60, true, true, "db", "u", "d"));
        DeployLocal.RestartArguments(all).ShouldBe(["--no-build", "--timeout-sec", "60", "--allow-worktree"]);
        DeployLocal.VerifyArguments.ShouldBe(["--skip-browser", "--timeout-sec", "30"]);
        Should.Throw<NightlyUsageException>(() => DeployLocalOptions.Parse(["-NoBuild"], "root"));
        Should.Throw<NightlyUsageException>(() => DeployLocalOptions.Parse(["--timeout-sec", "soon"], "root"));
        Should.Throw<NightlyUsageException>(() => DeployLocalOptions.Parse(["--pg-container"], "root"));
    }

    // ---- the real restart-apphost and verify-dev-stack in this process, on the fake machine ----

    [Test]
    public void The_real_restart_and_verification_run_in_process_between_the_lookup_and_the_query()
    {
        using var world = new AppHostWorld();
        File.WriteAllText(world.LogPath("apphost.pid"), "111");
        WriteMigrations(world, Migration + ".cs");
        var appData = Directory.CreateDirectory(world.PathOf("appdata")).FullName;
        WriteSecret(appData, "fixture-secret");
        world.Commands = (_, args) => args[0] switch
        {
            "version" => new AppHostCommandResult(false, 0, "Client:\n Version: 27\nServer: Docker Desktop\n"),
            "ps" => new AppHostCommandResult(false, 0, "Up 3 hours (healthy)\n"),
            _ => new AppHostCommandResult(false, 0, ""),
        };
        world.Listening.AddRange([17200, 17202, 17203, 17204, 17205, 17206]);
        world.Http = url => url.EndsWith("/api/version", StringComparison.Ordinal)
            ? new AppHostHttpAnswer(200, "{\"version\":\"" + AppHostWorld.Head + "\",\"informationalVersion\":\"" + AppHostWorld.Head + "\"}", null)
            : new AppHostHttpAnswer(200, url.EndsWith("/health", StringComparison.Ordinal) ? "Healthy" : "[]", null);
        var launcher = new RecordingLauncher(launch =>
        {
            var isInspect = launch.Arguments.Contains("inspect");
            world.Record(isInspect ? "docker inspect" : "docker psql");
            return new NightlyLaunchResult { ExitCode = 0, StandardOutputText = isInspect ? Published() : Migration + "\n" };
        });
        var steps = new DeployLocalSteps
        {
            RestartAppHost = (options, host) => AppHostRestart.Run(options, host, world),
            VerifyDevStack = (options, host) => DevStackVerify.Run(options, host, world),
        };

        var run = Deploy(world, Host(world, launcher, appData), steps, "--no-build");

        run.Exit.ShouldBe(DeployLocal.Ok, run.Output);
        run.Output.ShouldContain("AppHost restarted - dashboard http://127.0.0.1:9/, backend healthy, server " + AppHostWorld.Head + ".");
        run.Output.ShouldContain("Antiphon dev stack verified - all checks passed.");
        run.Lines[^2..].ShouldBe(["EF migrations verified: 1 source and database history entries.", "DEPLOY VERDICT: ok"]);
        var lookup = world.IndexOf("docker inspect");
        var launch = world.IndexOf("launch");
        var verify = world.IndexOf("run docker version");
        var query = world.IndexOf("docker psql");
        (lookup >= 0 && launch > lookup && verify > launch && query > verify).ShouldBeTrue($"lookup={lookup} launch={launch} verify={verify} query={query}");
        world.CliStarts.ShouldHaveSingleItem().ShouldBe(["dev-aspire", "--no-build"]);
        world.Trace.ShouldNotContain(row => row.Contains("playwright", StringComparison.OrdinalIgnoreCase), "the deploy verifies without a browser smoke");
        File.Exists(world.LogPath("apphost.restart.lock")).ShouldBeFalse("a healthy restart removes its lock");
    }
}
