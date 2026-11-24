using System.IO.Compression;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the simple-mode dev stack and its database tools
/// (dev-start, dev-stop, dev-fresh, dev-backup, dev-restore, dev-upgrade; DevStack) in process on AppHostWorld. The scripts had
/// no driver; these rows pin their refusals, their order, the stamped server launch and build (the SourceRevisionStampContractTests
/// census read dev-start.ps1 and dev-upgrade.ps1; the argv is C# now), the backup zip and its retention, the restore's input and
/// the confirmation prompts. The worktree root is a fixture directory: nothing touches C:\Antiphon\worktrees, Docker, Postgres,
/// a terminal or a browser.
/// </summary>
[Category("Unit")]
public sealed class DevStackTests
{
    private static AppHostWorld World(Action<AppHostWorld>? configure = null)
    {
        var world = new AppHostWorld();
        Directory.CreateDirectory(world.PathOf("server"));
        File.WriteAllText(world.PathOf("server", "appsettings.json"), "{}");
        Directory.CreateDirectory(world.PathOf("bin"));
        File.WriteAllText(world.PathOf("bin", "npm.cmd"), "@echo off\n");
        configure?.Invoke(world);
        return world;
    }

    private static NightlyHost Host(AppHostWorld world) => world.Host(name => name switch
    {
        "PATH" => world.PathOf("bin"),
        "ComSpec" => "cmd.exe",
        "TEMP" => world.PathOf("temp"),
        _ => null,
    });

    private static string Text(AppHostWorld world) => world.Out.ToString() + world.Error.ToString();

    private static string[] Compose(AppHostWorld world, params string[] rest) => new[] { "compose", "-f", world.PathOf("docker-compose.dev.yml") }.Concat(rest).ToArray();

    private static int Start(AppHostWorld world, params string[] arguments)
    {
        var parsed = DevStartOptions.Parse(arguments, world.Root);
        return DevStack.Start(new DevStartOptions
        {
            NoClient = parsed.NoClient, NoBrowser = parsed.NoBrowser, ServerOnly = parsed.ServerOnly, RepoRoot = world.Root, WorktreeRoot = world.PathOf("worktrees"),
        }, Host(world), world, world);
    }

    // ---- dev-start ----

    [Test]
    public void Dev_start_brings_up_postgres_then_the_stamped_server_and_the_client_then_the_browser()
    {
        using var world = World();
        var ready = 0;
        world.Commands = (file, args) => args.Contains("pg_isready") ? new AppHostCommandResult(false, ++ready >= 3 ? 0 : 2, "") : new AppHostCommandResult(false, 0, "");
        var exit = Start(world);
        exit.ShouldBe(0, Text(world));
        world.Launches.ShouldHaveSingleItem().Arguments.ShouldBe(Compose(world, "up", "-d"));
        Text(world).ShouldContain("  ...(3 s)");
        Text(world).ShouldContain("  Postgres ready.");
        world.Tabs.Select(tab => tab.Title).ShouldBe(["Antiphon Server", "Antiphon Client"]);
        world.Tabs[0].Directory.ShouldBe(world.PathOf("server"));
        world.Tabs[0].Command.ShouldBe(["dotnet", "run", "-p:SourceRevisionId=" + AppHostWorld.Head, "--urls", "http://localhost:17281"]);
        (world.Tabs[1].Directory, string.Join(" ", world.Tabs[1].Command)).ShouldBe((world.PathOf("client"), "npm run dev"));
        world.Browsed.ShouldBe(["http://localhost:17282"]);
        world.IndexOf("browser").ShouldBeGreaterThan(world.IndexOf("sleep 15"));
        foreach (var directory in new[] { world.PathOf("server", "logs", "sessions"), world.PathOf("backups"), world.PathOf("worktrees") })
            Directory.Exists(directory).ShouldBeTrue(directory);
    }

    [Test]
    public void Dev_start_server_only_and_no_client_skip_the_client_and_the_browser()
    {
        using var serverOnly = World();
        Start(serverOnly, "--server-only").ShouldBe(0, Text(serverOnly));
        serverOnly.Tabs.Select(tab => tab.Title).ShouldBe(["Antiphon Server"]);
        serverOnly.Browsed.ShouldBeEmpty();

        using var noClient = World();
        Start(noClient, "--no-client").ShouldBe(0, Text(noClient));
        noClient.Tabs.Select(tab => tab.Title).ShouldBe(["Antiphon Server"]);
        noClient.Browsed.ShouldBe(["http://localhost:17282"]);
    }

    [Test]
    public void Dev_start_refuses_an_unreadable_head_missing_settings_docker_down_and_a_failed_compose()
    {
        using var head = World(w => w.HeadExit = 128);
        Start(head).ShouldBe(1);
        Text(head).ShouldContain("dev-start: git rev-parse HEAD failed; refusing an unstamped server launch.");
        head.Trace.ShouldNotContain(row => row.StartsWith("run docker", StringComparison.Ordinal));

        using var settings = World(w => File.Delete(w.PathOf("server", "appsettings.json")));
        Start(settings).ShouldBe(1);
        Text(settings).ShouldContain("server\\appsettings.json not found. Copy appsettings.json.example and configure it.");

        using var docker = World(w => w.Commands = (file, args) => new AppHostCommandResult(false, 1, ""));
        Start(docker).ShouldBe(1);
        Text(docker).ShouldContain("Docker Desktop is not running. Start it, wait for the tray icon, then re-run.");
        docker.Launches.ShouldBeEmpty();

        using var compose = World(w => w.Tools = (launch, line) => 1);
        Start(compose).ShouldBe(1);
        Text(compose).ShouldContain("docker compose up failed.");
        compose.Tabs.ShouldBeEmpty();
    }

    [Test]
    public void Dev_start_goes_on_when_postgres_is_not_healthy_after_90_s()
    {
        using var world = World(w => w.Commands = (file, args) => new AppHostCommandResult(false, args.Contains("pg_isready") ? 2 : 0, ""));
        Start(world, "--no-browser").ShouldBe(0, Text(world));
        world.Trace.Count(row => row.Contains("pg_isready", StringComparison.Ordinal)).ShouldBe(30);
        Text(world).ShouldContain("WARNING: Postgres not yet healthy - continuing.");
        world.Tabs.Count.ShouldBe(2);
    }

    // ---- dev-stop ----

    [Test]
    public void Dev_stop_stops_the_simple_mode_listeners_and_with_include_postgres_the_compose_stack()
    {
        using var world = World();
        world.Owners[17281] = [5001];
        DevStack.Stop(DevStopOptions.Parse([], world.Root), Host(world), world).ShouldBe(0);
        world.Trace.ShouldContain("stop-id 5001");
        Text(world).ShouldContain("  Stopped .NET server (PID 5001)");
        Text(world).ShouldContain("  React client not listening on :17282");
        Text(world).ShouldContain("  PostgreSQL left running (pass --include-postgres to stop it).");
        world.Trace.ShouldNotContain(row => row.StartsWith("run docker", StringComparison.Ordinal));

        DevStack.Stop(DevStopOptions.Parse(["--include-postgres"], world.Root), Host(world), world).ShouldBe(0);
        world.Trace.ShouldContain("run docker " + string.Join(" ", Compose(world, "down")));
    }

    // ---- dev-fresh ----

    [Test]
    public void Dev_fresh_aborts_unless_yes_is_typed()
    {
        using var world = World();
        Directory.CreateDirectory(world.PathOf("worktrees", "w1"));
        world.Answers.Enqueue("no");
        DevStack.Fresh(new DevFreshOptions { RepoRoot = world.Root, WorktreeRoot = world.PathOf("worktrees") }, Host(world), world, world).ShouldBe(0);
        Text(world).ShouldContain("Aborted.");
        world.Trace.ShouldContain("prompt Type 'yes' to continue, anything else to abort");
        (world.Launches.Count, world.Count("port")).ShouldBe((0, 0));
        Directory.Exists(world.PathOf("worktrees", "w1")).ShouldBeTrue();

        using var closed = World();
        DevStack.Fresh(new DevFreshOptions { RepoRoot = closed.Root, WorktreeRoot = closed.PathOf("worktrees") }, Host(closed), closed, closed).ShouldBe(0);
        Text(closed).ShouldContain("Aborted.", customMessage: "the end of the input is not a yes");
    }

    [Test]
    public void Dev_fresh_stops_drops_the_volume_wipes_the_worktrees_and_starts()
    {
        using var world = World();
        var worktrees = world.PathOf("worktrees");
        Directory.CreateDirectory(Path.Combine(worktrees, "w1", "sub"));
        File.WriteAllText(Path.Combine(worktrees, "w1", "sub", "locked.txt"), "x");
        File.SetAttributes(Path.Combine(worktrees, "w1", "sub", "locked.txt"), FileAttributes.ReadOnly);
        world.Answers.Enqueue("YES");
        var exit = DevStack.Fresh(new DevFreshOptions { RepoRoot = world.Root, WorktreeRoot = worktrees }, Host(world), world, world);
        exit.ShouldBe(0, Text(world));
        world.IndexOf("port 17281").ShouldBeLessThan(world.IndexOf("tool docker " + string.Join(" ", Compose(world, "down", "-v"))));
        Directory.Exists(Path.Combine(worktrees, "w1")).ShouldBeFalse("the read-only tree is gone");
        Directory.Exists(worktrees).ShouldBeTrue("dev-start makes the root again");
        world.Tabs.Count.ShouldBe(2);
        Text(world).ShouldContain("> Wiping worktrees...");
    }

    // ---- dev-backup ----

    private static void Dump(AppHostWorld world, string sql = "CREATE TABLE t (id int);\n", int exit = 0) =>
        world.Tools = (launch, line) =>
        {
            if (launch.OutputFile != null) File.WriteAllText(launch.OutputFile, sql);
            return exit;
        };

    [Test]
    public void Dev_backup_zips_the_dump_prunes_to_retain_and_prints_the_zip_last()
    {
        using var world = World();
        var backups = world.PathOf("backups");
        Directory.CreateDirectory(backups);
        for (var i = 1; i <= 3; i++)
        {
            var old = Path.Combine(backups, "antiphon-2026010" + i + "-000000.sql.zip");
            File.WriteAllText(old, "old");
            File.SetLastWriteTimeUtc(old, new DateTime(2026, 1, i, 0, 0, 0, DateTimeKind.Utc));
        }
        Dump(world);
        var exit = DevStack.Backup(DevBackupOptions.Parse(["--retain", "2"], world.Root), Host(world), world, world);
        exit.ShouldBe(0, Text(world));

        var stamp = world.Clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var zip = Path.Combine(backups, "antiphon-" + stamp + ".sql.zip");
        var dump = world.Launches.ShouldHaveSingleItem();
        dump.Arguments.ShouldBe(Compose(world, "exec", "-T", "postgres", "pg_dump", "-U", "antiphon", "-d", "antiphon", "--no-owner", "--no-acl"));
        dump.OutputFile.ShouldBe(Path.Combine(backups, "antiphon-" + stamp + ".sql"));
        File.Exists(dump.OutputFile).ShouldBeFalse("the plain dump is removed once zipped");
        using (var archive = ZipFile.OpenRead(zip))
        {
            var entry = archive.Entries.ShouldHaveSingleItem();
            entry.FullName.ShouldBe("antiphon-" + stamp + ".sql");
            using var reader = new StreamReader(entry.Open());
            reader.ReadToEnd().ShouldBe("CREATE TABLE t (id int);\n");
        }
        Directory.GetFiles(backups, "*.sql.zip").Select(Path.GetFileName).OrderBy(name => name).ShouldBe(["antiphon-20260103-000000.sql.zip", Path.GetFileName(zip)]);
        Text(world).ShouldContain("  Pruned 2 old backup(s), kept 2.");
        world.Out.ToString().TrimEnd().Split('\n').Last().Trim().ShouldBe(zip);
    }

    [Test]
    public void Dev_backup_refuses_a_stopped_postgres_and_removes_a_failed_dump()
    {
        using var down = World(w => w.Commands = (file, args) => new AppHostCommandResult(false, args.Contains("pg_isready") ? 2 : 0, ""));
        DevStack.Backup(DevBackupOptions.Parse([], down.Root), Host(down), down, down).ShouldBe(1);
        Text(down).ShouldContain("Postgres is not running. Start it first: docker compose -f docker-compose.dev.yml up -d");
        down.Launches.ShouldBeEmpty();

        using var failed = World();
        Dump(failed, "partial", exit: 3);
        DevStack.Backup(DevBackupOptions.Parse(["--backup-dir", failed.PathOf("elsewhere")], failed.Root), Host(failed), failed, failed).ShouldBe(1);
        Text(failed).ShouldContain("pg_dump failed (exit 3).");
        Directory.GetFiles(failed.PathOf("elsewhere")).ShouldBeEmpty();
    }

    // ---- dev-restore ----

    private static string Backup(AppHostWorld world, string name, string sql)
    {
        var backups = world.PathOf("backups");
        Directory.CreateDirectory(backups);
        var file = Path.Combine(backups, name + ".sql");
        File.WriteAllText(file, sql);
        DevStack.Zip(world.Files, file, file + ".zip");
        File.Delete(file);
        return file + ".zip";
    }

    [Test]
    public void Dev_restore_recreates_the_database_and_feeds_the_newest_backup_to_psql()
    {
        using var world = World();
        File.SetLastWriteTimeUtc(Backup(world, "antiphon-old", "OLD"), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newest = Backup(world, "antiphon-new", "INSERT INTO t VALUES (1);\n");
        string? fed = null;
        world.Tools = (launch, line) =>
        {
            if (launch.InputFile != null) fed = File.ReadAllText(launch.InputFile);
            return 0;
        };
        world.Answers.Enqueue("yes");
        var exit = DevStack.Restore(DevRestoreOptions.Parse([], world.Root), Host(world), world, world);
        exit.ShouldBe(0, Text(world));
        Text(world).ShouldContain("Using latest backup: " + newest);
        fed.ShouldBe("INSERT INTO t VALUES (1);\n");
        var psql = world.Launches.Last();
        psql.Arguments.ShouldBe(Compose(world, "exec", "-T", "postgres", "psql", "-U", "antiphon", "-d", "antiphon"));
        var drop = world.IndexOf("run docker " + string.Join(" ", Compose(world, "exec", "-T", "postgres", "psql", "-U", "antiphon", "-d", "postgres", "-c", "DROP DATABASE IF EXISTS antiphon;")));
        var create = world.IndexOf("run docker " + string.Join(" ", Compose(world, "exec", "-T", "postgres", "psql", "-U", "antiphon", "-d", "postgres", "-c", "CREATE DATABASE antiphon OWNER antiphon;")));
        (drop >= 0 && create > drop).ShouldBeTrue(string.Join("\n", world.Trace));
        world.IndexOf("port 17281").ShouldBeLessThan(drop, "the server stops first");
        Directory.GetDirectories(world.PathOf("temp")).ShouldBeEmpty("the extracted copy is removed");
        Text(world).ShouldContain("Restore complete.");
    }

    [Test]
    public void Dev_restore_refuses_without_backups_or_a_missing_file_and_aborts_without_yes()
    {
        using var none = World();
        DevStack.Restore(DevRestoreOptions.Parse([], none.Root), Host(none), none, none).ShouldBe(1);
        Text(none).ShouldContain("No backups found in " + none.PathOf("backups") + ". Run .\\dev-backup.ps1 first.");

        using var missing = World();
        DevStack.Restore(DevRestoreOptions.Parse(["--backup-file", missing.PathOf("nope.sql")], missing.Root), Host(missing), missing, missing).ShouldBe(1);
        Text(missing).ShouldContain("File not found: " + missing.PathOf("nope.sql"));

        using var abort = World();
        Backup(abort, "antiphon-a", "X");
        abort.Answers.Enqueue("y");
        DevStack.Restore(DevRestoreOptions.Parse([], abort.Root), Host(abort), abort, abort).ShouldBe(0);
        Text(abort).ShouldContain("Aborted.");
        (abort.Launches.Count, abort.Count("port")).ShouldBe((0, 0));
    }

    [Test]
    public void Dev_restore_reports_a_failed_psql_and_still_removes_the_extracted_copy()
    {
        using var world = World();
        Backup(world, "antiphon-a", "X");
        world.Tools = (launch, line) => launch.InputFile != null ? 3 : 0;
        DevStack.Restore(DevRestoreOptions.Parse(["--yes"], world.Root), Host(world), world, world).ShouldBe(1);
        Text(world).ShouldContain("psql restore failed (exit 3).");
        world.Trace.ShouldNotContain(row => row.StartsWith("prompt", StringComparison.Ordinal));
        Directory.GetDirectories(world.PathOf("temp")).ShouldBeEmpty();
    }

    // ---- dev-upgrade ----

    [Test]
    public void Dev_upgrade_backs_up_stops_pulls_builds_stamped_installs_and_starts()
    {
        using var world = World();
        Dump(world);
        var exit = DevStack.Upgrade(new DevUpgradeOptions { NoBrowser = true, RepoRoot = world.Root, WorktreeRoot = world.PathOf("worktrees") }, Host(world), world, world);
        exit.ShouldBe(0, Text(world));
        world.Git.Calls.ShouldContain(call => call.Arguments.SequenceEqual(new[] { "pull" }));
        var builds = world.Launches.Where(launch => launch.FileName == "dotnet").ToList();
        builds.ShouldHaveSingleItem().Arguments.ShouldBe(["build", world.PathOf("server"), "-p:SourceRevisionId=" + AppHostWorld.Head, "--configuration", "Release", "--no-restore"]);
        var npm = world.Launches.Single(launch => launch.Arguments.Contains("install"));
        npm.WorkingDirectory.ShouldBe(world.PathOf("client"));
        world.Tabs.Count.ShouldBe(2);
        world.Browsed.ShouldBeEmpty("--no-browser reaches dev-start");
        var order = new[] { world.IndexOf("tool docker " + string.Join(" ", Compose(world, "exec", "-T", "postgres", "pg_dump"))), world.IndexOf("port 17281"),
            world.IndexOf("tool dotnet build"), world.IndexOf("tab Antiphon Server") };
        order.ShouldAllBe(index => index >= 0, string.Join("\n", world.Trace));
        order.ShouldBe(order.OrderBy(index => index).ToArray());
        Text(world).ShouldContain("Upgrade complete.");
    }

    [Test]
    public void Dev_upgrade_retries_the_build_with_a_restore_and_stops_on_a_pull_or_build_failure()
    {
        using var retry = World();
        retry.Tools = (launch, line) =>
        {
            if (launch.OutputFile != null) File.WriteAllText(launch.OutputFile, "x");
            return launch.Arguments.Contains("--no-restore") ? 1 : 0;
        };
        DevStack.Upgrade(new DevUpgradeOptions { RepoRoot = retry.Root, WorktreeRoot = retry.PathOf("worktrees"), NoBrowser = true }, Host(retry), retry, retry).ShouldBe(0, Text(retry));
        retry.Launches.Where(launch => launch.FileName == "dotnet").Select(launch => launch.Arguments.Last()).ShouldBe(["--no-restore", "Release"]);

        using var pull = World();
        Dump(pull);
        pull.Git.Reply("pull", new NightlyGitResult(1, "CONFLICT (content): Merge conflict in a.cs"));
        DevStack.Upgrade(new DevUpgradeOptions { RepoRoot = pull.Root, WorktreeRoot = pull.PathOf("worktrees") }, Host(pull), pull, pull).ShouldBe(1);
        Text(pull).ShouldContain("CONFLICT (content): Merge conflict in a.cs");
        Text(pull).ShouldContain("git pull failed - resolve conflicts and retry.");
        pull.Launches.ShouldNotContain(launch => launch.FileName == "dotnet");

        using var build = World();
        build.Tools = (launch, line) =>
        {
            if (launch.OutputFile != null) File.WriteAllText(launch.OutputFile, "x");
            return launch.FileName == "dotnet" ? 1 : 0;
        };
        DevStack.Upgrade(new DevUpgradeOptions { RepoRoot = build.Root, WorktreeRoot = build.PathOf("worktrees") }, Host(build), build, build).ShouldBe(1);
        Text(build).ShouldContain("dotnet build failed.");
        build.Tabs.ShouldBeEmpty();
    }

    [Test]
    public void The_simple_mode_flags_are_kebab_case()
    {
        (DevStartOptions.Parse(["--no-client", "--no-browser", "--server-only"], "r") is { NoClient: true, NoBrowser: true, ServerOnly: true }).ShouldBeTrue();
        DevStopOptions.Parse(["--include-postgres"], "r").IncludePostgres.ShouldBeTrue();
        DevFreshOptions.Parse(["--yes"], "r").Yes.ShouldBeTrue();
        var backup = DevBackupOptions.Parse(["--backup-dir", "d", "--retain", "3"], "r");
        (backup.BackupDir, backup.Retain).ShouldBe(("d", 3));
        DevBackupOptions.Parse([], "r").Retain.ShouldBe(14);
        var restore = DevRestoreOptions.Parse(["--backup-file", "f.sql.zip", "--backup-dir", "d", "--yes"], "r");
        (restore.BackupFile, restore.BackupDir, restore.Yes).ShouldBe(("f.sql.zip", "d", true));
        DevUpgradeOptions.Parse(["--no-browser"], "r").NoBrowser.ShouldBeTrue();
        Should.Throw<NightlyUsageException>(() => DevBackupOptions.Parse(["--retain", "many"], "r")).Message.ShouldBe("--retain expects an integer, not 'many'");
        Should.Throw<NightlyUsageException>(() => DevStopOptions.Parse(["-IncludePostgres"], "r"));
    }
}
