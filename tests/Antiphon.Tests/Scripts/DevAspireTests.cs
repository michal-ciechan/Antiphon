using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: dev-aspire (DevAspire.Run), its detached AppHost
/// wrapper run-apphost (DevAspire.RunAppHost) and the CLI run copy the detached children start from (AppHostCli), in process on
/// AppHostWorld. dev-aspire.ps1 had no driver; these rows pin its guard order (CARD-0273, CARD-1744: both refuse before the
/// launch lock), the launch lock (CARD-0011), the preflight order, the stamped build (the SourceRevisionStampContractTests
/// census read dev-aspire.ps1's encoded command; the argv is C# now), the dashboard wait (CARD-1806), the G-1 opt-out and the
/// LIVE_TIMING rows (CARD-1564: the C1564 rows of the deleted scripts/test-live-startup-timing.ps1, minus its value and
/// exception pass-through, which only Invoke-LiveStartupPhase's script block had). Nothing reaches Docker, a process or a port.
/// </summary>
[Category("Unit")]
public sealed class DevAspireTests
{
    private const string LoginLog = "info: Aspire.Hosting.DistributedApplication[0]\n      Login to the dashboard at http://localhost:17205/login?t=0a1b2c\n";

    private static AppHostWorld World(Action<AppHostWorld>? configure = null)
    {
        var world = new AppHostWorld();
        Directory.CreateDirectory(world.PathOf("server"));
        File.WriteAllText(world.PathOf("server", "appsettings.json"), "{}");
        Directory.CreateDirectory(world.PathOf("bin"));
        File.WriteAllText(world.PathOf("bin", "npm.cmd"), "@echo off\n");
        // The wrapper's AppHost prints Aspire's login line once it is up.
        world.OnLaunch = w => File.WriteAllText(w.LogPath("apphost.log"), LoginLog);
        world.Commands = (file, args) => args.Contains("ps") ? new AppHostCommandResult(false, 0, "Up 3 minutes (healthy)\n") : new AppHostCommandResult(false, 0, "");
        configure?.Invoke(world);
        return world;
    }

    private static Func<string, string?> Variables(AppHostWorld world, string? name = null, string? value = null) => variable => variable switch
    {
        "PATH" => world.PathOf("bin"),
        "ComSpec" => "cmd.exe",
        _ => variable == name ? value : null,
    };

    private static (int Exit, string Text) Run(AppHostWorld world, Func<string, string?>? variables = null, params string[] arguments)
    {
        var parsed = DevAspireOptions.Parse(arguments, world.Root);
        var options = new DevAspireOptions
        {
            NoBuild = parsed.NoBuild, NoBrowser = parsed.NoBrowser, AllowWorktree = parsed.AllowWorktree, RepoRoot = parsed.RepoRoot,
            WorktreeRoot = world.PathOf("worktrees"),
        };
        var before = world.Out.ToString().Length;
        var exit = DevAspire.Run(options, world.Host(variables ?? Variables(world)), world, world);
        return (exit, world.Out.ToString()[before..] + world.Error.ToString());
    }

    private static List<(string Operation, string Outcome)> Timing(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("LIVE_TIMING ", StringComparison.Ordinal))
            .Select(line => JsonNode.Parse(line["LIVE_TIMING ".Length..])!.AsObject())
            .Select(row => ((string)row["operation"]!, (string)row["outcome"]!)).ToList();

    private static int At(AppHostWorld world, string prefix) => Array.FindIndex(world.Trace, row => row.StartsWith(prefix, StringComparison.Ordinal));

    [Test]
    public void A_linked_worktree_is_refused_before_the_launch_lock_or_docker()
    {
        using var world = World(w => w.MainWorktree = Path.Combine(Path.GetTempPath(), "dev-aspire-main-elsewhere"));
        File.WriteAllText(world.PhoneHomeConfigPath, "{}");
        var r = Run(world);
        r.Exit.ShouldBe(3, r.Text);
        r.Text.ShouldContain("REFUSED: this AppHost command is rooted in a linked Git worktree.");
        r.Text.ShouldContain("re-run with --allow-worktree.");
        r.Text.ShouldNotContain("phone-home", Case.Insensitive, "the worktree guard answers first");
        world.Trace.ShouldNotContain(row => row.StartsWith("run docker", StringComparison.Ordinal));
        File.Exists(world.LogPath("apphost.launch.lock")).ShouldBeFalse();
        world.CliStarts.ShouldBeEmpty();
    }

    [Test]
    public void Allow_worktree_warns_and_launches()
    {
        using var world = World(w => w.MainWorktree = Path.Combine(Path.GetTempPath(), "dev-aspire-main-elsewhere"));
        var r = Run(world, null, "--allow-worktree", "--no-build", "--no-browser");
        r.Exit.ShouldBe(0, r.Text);
        r.Text.ShouldContain("WARNING: this AppHost command is rooted in a linked Git worktree.");
        r.Text.ShouldContain("--allow-worktree explicitly permits this worktree to affect the shared stack.");
        world.CliStarts.ShouldHaveSingleItem();
    }

    [Test]
    public void A_phone_home_configuration_is_refused_before_the_launch_lock_or_docker()
    {
        using var world = World();
        File.WriteAllText(world.PhoneHomeConfigPath, "{}");
        var r = Run(world);
        r.Exit.ShouldBe(3, r.Text);
        r.Text.ShouldContain(WindowsPhoneHome.LaunchRefusal(true, world.PhoneHomeConfigPath)!);
        world.Git.Calls.ShouldContain(call => call.Arguments.Contains("--show-toplevel"), "the worktree guard ran first");
        world.Trace.ShouldNotContain(row => row.StartsWith("run docker", StringComparison.Ordinal));
        File.Exists(world.LogPath("apphost.launch.lock")).ShouldBeFalse();
        world.CliStarts.ShouldBeEmpty();
    }

    [Test]
    public void The_launch_holds_the_lock_runs_its_preflight_in_order_and_starts_the_stamped_wrapper()
    {
        string? lockText = null;
        using var world = World();
        world.Commands = (file, args) =>
        {
            if (args[0] == "info") lockText = File.ReadAllText(world.LogPath("apphost.launch.lock"));
            return args.Contains("ps") ? new AppHostCommandResult(false, 0, "Up 3 minutes (healthy)\n") : new AppHostCommandResult(false, 0, "");
        };
        var r = Run(world);
        r.Exit.ShouldBe(0, r.Text);

        // CARD-0011: "<pid> <utc round-trip>" while the launch runs, gone after it.
        lockText.ShouldBe("424242 2026-11-24T09:00:00.0000000Z" + Environment.NewLine);
        File.Exists(world.LogPath("apphost.launch.lock")).ShouldBeFalse();

        var appHost = world.PathOf("Antiphon.AppHost");
        var order = new[]
        {
            At(world, "run docker info"), At(world, "run docker network create aspire-preflight-"), At(world, "run docker network rm aspire-preflight-"),
            At(world, "run docker compose -f " + world.PathOf("docker-compose.dev.yml") + " up -d"), At(world, "check-daemon-build"),
            At(world, "slot apphost-restore dotnet restore " + appHost), At(world, "tool "), At(world, "launch run-apphost"), At(world, "run docker ps --filter name=antiphon-postgres"),
        };
        order.ShouldAllBe(index => index >= 0, string.Join("\n", world.Trace));
        order.ShouldBe(order.OrderBy(index => index).ToArray(), string.Join("\n", world.Trace));

        var restore = world.Slots.ShouldHaveSingleItem();
        (restore.Label, restore.WorkingDirectory).ShouldBe(("apphost-restore", world.Root));
        restore.Command.ShouldBe(["dotnet", "restore", appHost]);
        var npm = world.Launches.ShouldHaveSingleItem();
        npm.WorkingDirectory.ShouldBe(world.PathOf("client"));
        npm.FileName.ShouldBe(OperatingSystem.IsWindows() ? "cmd.exe" : "npm");
        npm.Arguments.ShouldBe(OperatingSystem.IsWindows() ? ["/c", world.PathOf("bin", "npm.cmd"), "install"] : ["install"]);

        // The wrapper builds stamped with HEAD; its pid is the AppHost pid.
        world.CliStarts.ShouldHaveSingleItem().ShouldBe(["run-apphost", "--source-revision", AppHostWorld.Head]);
        File.ReadAllText(world.LogPath("apphost.pid")).Trim().ShouldBe("99999");
        File.ReadAllText(world.LogPath("apphost-dashboard-url.txt")).Trim().ShouldBe("http://localhost:17205");
        world.Browsed.ShouldBe(["http://localhost:17205"]);
        r.Text.ShouldContain("  Dashboard : http://localhost:17205");
        r.Text.ShouldContain("  Postgres: Up");
        r.Text.ShouldContain("AppHost running in background (PID 99999).");
        Timing(r.Text).ShouldBe([("docker-info", "completed"), ("docker-network", "completed"), ("container-start", "completed"), ("restore", "completed"),
            ("dependencies", "completed")]);
        foreach (var directory in new[] { world.PathOf("server", "logs", "sessions"), world.PathOf("backups"), world.PathOf("worktrees") })
            Directory.Exists(directory).ShouldBeTrue(directory);
    }

    [Test]
    public void No_build_and_no_browser_skip_the_restore_npm_and_browser()
    {
        using var world = World();
        var r = Run(world, null, "--no-build", "--no-browser");
        r.Exit.ShouldBe(0, r.Text);
        (world.Slots.Count, world.Launches.Count, world.Browsed.Count).ShouldBe((0, 0, 0));
        world.CliStarts.ShouldHaveSingleItem();
        File.Exists(world.LogPath("apphost-dashboard-url.txt")).ShouldBeTrue();
    }

    [Test]
    public void Docker_down_and_missing_settings_fail_before_compose_and_remove_the_lock()
    {
        using var down = World(w => w.Commands = (file, args) => new AppHostCommandResult(false, 1, "error during connect"));
        var r = Run(down);
        r.Exit.ShouldBe(1, r.Text);
        r.Text.ShouldContain("Docker Desktop is not running. Start it, wait for the tray icon, then re-run.");
        Timing(r.Text).ShouldBe([("docker-info", "failed-or-canceled")]);
        down.Trace.ShouldNotContain(row => row.Contains("compose", StringComparison.Ordinal));
        File.Exists(down.LogPath("apphost.launch.lock")).ShouldBeFalse();

        using var unset = World(w => File.Delete(w.PathOf("server", "appsettings.json")));
        var u = Run(unset);
        u.Exit.ShouldBe(1, u.Text);
        u.Text.ShouldContain("server\\appsettings.json not found. Copy appsettings.json.example first.");
        unset.Trace.ShouldNotContain(row => row.StartsWith("run docker", StringComparison.Ordinal));
        File.Exists(unset.LogPath("apphost.launch.lock")).ShouldBeFalse();
    }

    [Test]
    public void A_hung_network_create_is_a_warning_and_the_launch_goes_on()
    {
        using var world = World(w => w.Commands = (file, args) => args.Contains("create")
            ? new AppHostCommandResult(true, -1, "")
            : args.Contains("ps") ? new AppHostCommandResult(false, 0, "Up 1 minute\n") : new AppHostCommandResult(false, 0, ""));
        var r = Run(world, null, "--no-build");
        r.Exit.ShouldBe(0, r.Text);
        r.Text.ShouldContain("  WARNING: Docker network creation is unresponsive (>8s).");
        world.Trace.ShouldNotContain(row => row.StartsWith("run docker network rm", StringComparison.Ordinal));
        Timing(r.Text).ShouldContain(("docker-network", "failed-or-canceled"));
        world.CliStarts.ShouldHaveSingleItem();
    }

    [Test]
    public void The_postgres_opt_out_starts_the_other_services_and_skips_the_container_check()
    {
        using var world = World();
        File.WriteAllText(world.PathOf("docker-compose.dev.yml"), "services:\n  postgres:\n    image: postgres\n  redpanda:\n    image: redpanda\n");
        var r = Run(world, Variables(world, DevCompose.SkipVariable, "1"), "--no-build");
        r.Exit.ShouldBe(0, r.Text);
        r.Text.ShouldContain("> Ensuring dev compose services except Postgres are up...");
        r.Text.ShouldContain("ANTIPHON_SKIP_DEV_POSTGRES=1: desktop Postgres bring-up skipped (database moved off-host); compose up -d --no-deps redpanda only");
        world.Trace.ShouldContain("run docker compose -f " + world.PathOf("docker-compose.dev.yml") + " up -d --no-deps redpanda");
        r.Text.ShouldContain("  Postgres: desktop container not checked (ANTIPHON_SKIP_DEV_POSTGRES=1)");
        world.Trace.ShouldNotContain(row => row.StartsWith("run docker ps", StringComparison.Ordinal));
    }

    [Test]
    public void A_restore_npm_or_head_failure_stops_before_the_wrapper()
    {
        using var restore = World(w => w.Slot = (options, capture) => 1);
        var r = Run(restore);
        r.Exit.ShouldBe(1, r.Text);
        r.Text.ShouldContain("dotnet restore failed.");
        Timing(r.Text).ShouldContain(("restore", "failed-or-canceled"));
        (restore.CliStarts.Count, restore.Launches.Count).ShouldBe((0, 0));
        File.Exists(restore.LogPath("apphost.launch.lock")).ShouldBeFalse();

        using var npm = World(w => w.Tools = (launch, line) => { line("npm ERR! code E404"); return 1; });
        var n = Run(npm);
        n.Exit.ShouldBe(1, n.Text);
        n.Text.ShouldContain("npm ERR! code E404");
        n.Text.ShouldContain("npm install failed.");
        npm.CliStarts.ShouldBeEmpty();

        using var head = World(w => w.HeadExit = 128);
        var h = Run(head, null, "--no-build");
        h.Exit.ShouldBe(1, h.Text);
        h.Text.ShouldContain("git rev-parse HEAD failed in " + head.Root + "; refusing an unstamped AppHost build.");
        head.CliStarts.ShouldBeEmpty();
        File.Exists(head.LogPath("apphost.launch.lock")).ShouldBeFalse();
    }

    [Test]
    public void The_dashboard_falls_back_to_the_dashboard_listener_and_gives_up_after_90_s()
    {
        using var listener = World(w =>
        {
            w.OnLaunch = null;
            w.Named.Add(new AppHostProcessRow(555, "Aspire.Dashboard", null, null));
            w.Listening.AddRange([17205, 17206]);
            w.Owners[17205] = [555];
            w.Owners[17206] = [555];
        });
        File.WriteAllText(listener.LogPath("apphost.log"), "stale Login to the dashboard at http://localhost:1/login\n");
        var r = Run(listener, null, "--no-build", "--no-browser");
        r.Exit.ShouldBe(0, r.Text);
        File.ReadAllText(listener.LogPath("apphost-dashboard-url.txt")).Trim().ShouldBe("http://localhost:17205", "the OTLP port is never the dashboard; a stale log line never counts");

        using var silent = World(w => w.OnLaunch = null);
        var started = silent.UtcNow;
        var s = Run(silent, null, "--no-build");
        s.Exit.ShouldBe(0, s.Text);
        s.Text.ShouldContain("  ...(87 s)");
        s.Text.ShouldContain("  Dashboard URL not found after 90s.");
        s.Text.ShouldContain("  Check log: Get-Content '" + silent.LogPath("apphost.log") + "' -Tail 30");
        File.Exists(silent.LogPath("apphost-dashboard-url.txt")).ShouldBeFalse();
        silent.Browsed.ShouldBeEmpty();
        (silent.UtcNow - started).TotalSeconds.ShouldBeGreaterThanOrEqualTo(90);
    }

    [Test]
    public void Postgres_that_is_not_up_after_45_s_is_a_warning_with_its_state()
    {
        using var world = World(w => w.Commands = (file, args) => args.Contains("-a")
            ? new AppHostCommandResult(false, 0, "antiphon-postgres\tExited (1) 2 minutes ago\n")
            : args.Contains("ps") ? new AppHostCommandResult(false, 0, "Exited (1) 2 minutes ago\n") : new AppHostCommandResult(false, 0, ""));
        var r = Run(world, null, "--no-build", "--no-browser");
        r.Exit.ShouldBe(0, r.Text);
        r.Text.ShouldContain("  WARNING: Postgres ('antiphon-postgres') is not up after 45s.");
        r.Text.ShouldContain("  Container state: antiphon-postgres\tExited (1) 2 minutes ago");
        world.Trace.Count(row => row == "run docker ps --filter name=antiphon-postgres --format {{.Status}}").ShouldBe(9);
    }

    [Test]
    public void Aspire_temp_directories_older_than_two_hours_are_removed()
    {
        using var world = World();
        var temp = world.PathOf("temp");
        foreach (var name in new[] { "aspire.old", "aspire.new", "other.old" }) Directory.CreateDirectory(Path.Combine(temp, name));
        File.WriteAllText(Path.Combine(temp, "aspire.old", "state.json"), "{}");
        File.SetAttributes(Path.Combine(temp, "aspire.old", "state.json"), FileAttributes.ReadOnly);
        Directory.SetLastWriteTimeUtc(Path.Combine(temp, "aspire.old"), world.UtcNow.AddHours(-3));
        Directory.SetLastWriteTimeUtc(Path.Combine(temp, "other.old"), world.UtcNow.AddHours(-3));
        Directory.SetLastWriteTimeUtc(Path.Combine(temp, "aspire.new"), world.UtcNow.AddMinutes(-30));
        var r = Run(world, Variables(world, "TEMP", temp), "--no-build", "--no-browser");
        r.Exit.ShouldBe(0, r.Text);
        r.Text.ShouldContain("  Cleaned 1 old DCP temp dir(s)");
        Directory.Exists(Path.Combine(temp, "aspire.old")).ShouldBeFalse();
        Directory.Exists(Path.Combine(temp, "aspire.new")).ShouldBeTrue();
        Directory.Exists(Path.Combine(temp, "other.old")).ShouldBeTrue();
    }

    // ---- run-apphost: the detached wrapper ----

    [Test]
    public void The_wrapper_builds_stamped_under_a_slot_then_runs_the_apphost_into_the_log()
    {
        using var world = World();
        File.WriteAllText(world.LogPath("apphost.log"), "an earlier launch\n");
        world.Slot = (options, capture) => { capture.WriteLine("Build succeeded."); return 0; };
        world.Tools = (launch, line) => { line("Login to the dashboard at http://localhost:17205/login?t=x"); return 0; };
        var sha = new string('c', 40);
        var exit = DevAspire.RunAppHost(AppHostRunOptions.Parse(["--source-revision", sha], world.Root), world.Host(), world.Files, world);
        exit.ShouldBe(0);
        var appHost = world.PathOf("Antiphon.AppHost");
        var build = world.Slots.ShouldHaveSingleItem();
        (build.Label, build.WorkingDirectory).ShouldBe(("apphost-build", appHost));
        build.Command.ShouldBe(["dotnet", "build", "-p:SourceRevisionId=" + sha]);
        var run = world.Launches.ShouldHaveSingleItem();
        (run.FileName, run.WorkingDirectory).ShouldBe(("dotnet", appHost));
        run.Arguments.ShouldBe(["run", "--no-build"]);
        var log = File.ReadAllText(world.LogPath("apphost.log"));
        log.ShouldNotContain("an earlier launch");
        log.IndexOf("Build succeeded.", StringComparison.Ordinal).ShouldBeLessThan(log.IndexOf("LIVE_TIMING ", StringComparison.Ordinal));
        log.IndexOf("LIVE_TIMING ", StringComparison.Ordinal).ShouldBeLessThan(log.IndexOf("Login to the dashboard", StringComparison.Ordinal));
        Timing(log).ShouldBe([("build", "completed")]);
        world.Out.ToString().ShouldBeEmpty("the wrapper writes its log, not its console");
    }

    [Test]
    public void A_failed_wrapper_build_never_runs_the_apphost()
    {
        using var world = World();
        world.Slot = (options, capture) => { capture.WriteLine("Program.cs(1,1): error CS1002: ; expected"); capture.WriteLine("The build failed. Fix the build errors and run again."); return 1; };
        var exit = DevAspire.RunAppHost(AppHostRunOptions.Parse(["--source-revision", AppHostWorld.Head], world.Root), world.Host(), world.Files, world);
        exit.ShouldBe(1);
        world.Launches.ShouldBeEmpty();
        var log = File.ReadAllText(world.LogPath("apphost.log"));
        AppHostLaunchLog.Verdict(world.Files, world.LogPath("apphost.log")).Kind.ShouldBe("BuildFailed", "restart-apphost reads the build failure in this log");
        Timing(log).ShouldBe([("build", "failed-or-canceled")]);
    }

    [Test]
    [Arguments(new string[0], "missing --source-revision")]
    [Arguments(new[] { "--source-revision", "0123456789ABCDEF0123456789ABCDEF01234567" }, "--source-revision expects 40 lower-case hex characters, not '0123456789ABCDEF0123456789ABCDEF01234567'")]
    [Arguments(new[] { "--source-revision", "unknown" }, "--source-revision expects 40 lower-case hex characters, not 'unknown'")]
    public void Run_apphost_requires_a_full_lower_case_head(string[] arguments, string message)
    {
        Should.Throw<NightlyUsageException>(() => AppHostRunOptions.Parse(arguments, "root")).Message.ShouldBe(message);
    }

    [Test]
    public void Dev_aspire_flags_are_kebab_switches()
    {
        var options = DevAspireOptions.Parse(["--no-build", "--no-browser", "--allow-worktree"], "root");
        (options.NoBuild, options.NoBrowser, options.AllowWorktree, options.RepoRoot, options.WorktreeRoot).ShouldBe((true, true, true, "root", @"C:\Antiphon\worktrees"));
        Should.Throw<NightlyUsageException>(() => DevAspireOptions.Parse(["-NoBuild"], "root")).Message.ShouldBe("unexpected argument '-NoBuild'");
    }

    // ---- the CLI run copy ----

    [Test]
    public void The_cli_run_copy_is_made_once_under_bin_launcher_and_reused()
    {
        var root = Path.Combine(Path.GetTempPath(), "apphost-cli-" + Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(root, "out");
            Directory.CreateDirectory(Path.Combine(output, "runtimes"));
            File.WriteAllText(Path.Combine(output, "Antiphon.Scripts.dll"), "dll");
            File.WriteAllText(Path.Combine(output, "Antiphon.Scripts.runtimeconfig.json"), "{}");
            File.WriteAllText(Path.Combine(output, "runtimes", "native.bin"), "native");
            var checkout = Path.Combine(root, "checkout");
            Directory.CreateDirectory(checkout);

            var hash = AppHostCli.OutputHash(output);
            hash.ShouldBe(ExpectedHash(output), "the PowerShell launcher's run-<hash>: names and bytes in ordinal order");
            var run = Path.Combine(checkout, "bin-launcher", "run-" + hash);
            var argv = AppHostCli.RunCopy(checkout, ["dotnet", "exec", Path.Combine(output, "Antiphon.Scripts.dll")]);
            argv.ShouldBe(["dotnet", "exec", Path.Combine(run, "Antiphon.Scripts.dll")]);
            File.ReadAllText(Path.Combine(run, "runtimes", "native.bin")).ShouldBe("native");
            Directory.GetDirectories(Path.Combine(checkout, "bin-launcher")).Select(Path.GetFileName).ShouldBe(["run-" + hash], "no tmp- copy is left");

            // A start from the copy itself starts it; a second start reuses the copy and never rewrites it (a running wrapper holds it).
            AppHostCli.RunCopy(checkout, ["dotnet", "exec", Path.Combine(run, "Antiphon.Scripts.dll")]).Last().ShouldBe(Path.Combine(run, "Antiphon.Scripts.dll"));
            File.WriteAllText(Path.Combine(run, "runtimes", "native.bin"), "in use");
            AppHostCli.RunCopy(checkout, ["dotnet", "exec", Path.Combine(output, "Antiphon.Scripts.dll")]).ShouldBe(argv);
            File.ReadAllText(Path.Combine(run, "runtimes", "native.bin")).ShouldBe("in use");

            File.WriteAllText(Path.Combine(output, "Antiphon.Scripts.dll"), "rebuilt");
            AppHostCli.RunCopy(checkout, ["dotnet", "exec", Path.Combine(output, "Antiphon.Scripts.dll")]).Last().ShouldNotBe(argv.Last(), "a rebuilt CLI gets its own copy");
        }
        finally
        {
            if (Directory.Exists(root)) ReadOnlyDirectory.Delete(root);
        }
    }

    private static string ExpectedHash(string directory)
    {
        using var buffer = new MemoryStream();
        foreach (var name in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            buffer.Write(Encoding.UTF8.GetBytes(name + "\n"));
            buffer.Write(File.ReadAllBytes(Path.Combine(directory, name)));
        }
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray()))[..24].ToLowerInvariant();
    }

    // ---- CARD-1564: the startup timing rows (scripts/test-live-startup-timing.ps1's C1564 rows) ----

    [Test]
    public void C1564_timing_rows_carry_fixed_labels_the_phase_vocabulary_and_the_outcome_once()
    {
        using var world = World();
        var host = world.Host();
        var restore = new LiveStartupPhase(host, "restore");
        world.Clock.Advance(TimeSpan.FromMilliseconds(250));
        restore.Stop(true);
        restore.Stop(false);
        new LiveStartupPhase(host, "health-wait").Stop(false);
        new LiveStartupPhase(host, "docker-info").Stop(true);
        var rows = world.Out.ToString().Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        rows.Length.ShouldBe(3, "a phase prints once");
        var parsed = rows.Select(row => { row.ShouldStartWith("LIVE_TIMING "); return JsonNode.Parse(row["LIVE_TIMING ".Length..])!.AsObject(); }).ToArray();
        foreach (var row in parsed)
            row.Select(pair => pair.Key).ShouldBe(["version", "runId", "producerPid", "phase", "operation", "elapsedMs", "outcome"], "fixed labels only, no payload");
        parsed.Select(row => ((string)row["phase"]!, (string)row["outcome"]!)).ShouldBe([("process-wait", "completed"), ("network-loopback", "failed-or-canceled"),
            ("fixture-setup", "completed")]);
        ((double)parsed[0]["elapsedMs"]!).ShouldBe(250);
        parsed.Select(row => (string)row["runId"]!).Distinct().ShouldHaveSingleItem().ShouldNotBeNullOrEmpty();
    }
}
