using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1406, unit 54 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the deploy-server2-postgres command
/// (Antiphon.Nightly.Server2PostgresDeploy), which replaced scripts/deploy-server2-postgres.ps1, in process on a mock disk with a
/// recording launcher (Server2PostgresWorld). The lines, exits and tool calls are the script's, captured from it before its
/// deletion (pwsh on Windows with .cmd stubs of docker, hostname and tailscale alone on PATH; the env-file cases that pass the
/// POSIX mode check through a dot-source that answered Test-Server2PostgresMode0600, because Windows always refuses env-mode);
/// the function tables are the script's functions' answers, captured by dot-sourcing it. The driver's own cases are
/// Server2PostgresComposeTests.
/// </summary>
[Category("Unit")]
public sealed class Server2PostgresDeployTests
{
    private const string PlanOk = "SERVER2-POSTGRES plan exit=0 reason=ok";
    private const string Ok = "SERVER2-POSTGRES ok exit=0 reason=ok";
    private const string Info = "docker info --format {{.Name}}";

    /// <summary>The drill plan as the script printed it (whatif-drill, x-drill), for an env argument and a compose file name.</summary>
    internal static string[] CapturedDrill(string envArg, string composeArg)
    {
        const string p = "drill-command: project=antiphon-server2-postgres-drill volume=antiphon-server2-postgres-drill_pgdata :: ";
        const string scratch = "/var/tmp/antiphon-server2-postgres-drill";
        return
        [
            "drill-project: antiphon-server2-postgres-drill",
            "drill-volume: antiphon-server2-postgres-drill_pgdata",
            "drill-remove: scratch-only",
            "unread: docker-ps",
            "unread: docker-volume-ls",
            "unread: docker-data-root-df",
            "unread: windmill-compose-parent",
            "unread: count-star",
            "unread: dump-elapsed-sec",
            "unread: restore-elapsed-sec",
            "unread: copy-elapsed-sec",
            "unread: dump-bytes",
            "phase3: user-table-count",
            "phase3: sequence-last-value",
            "phase3: extension-version",
            "phase3: ef-migrations-history",
            "phase3: key-table-count-min-max-pk",
            "phase3: key tables are TranscriptEntries, AgentTasks, SessionQueuedMessages, AgentTaskLandNotifications, AgentTaskEvents",
            "phase3: count, minimum primary key, and maximum primary key; no row body",
            p + "pwsh -NoProfile -File scripts/postgres-snapshot.ps1 -Execute -Container antiphon-postgres -RecipientFile \"$AGE_RECIPIENT\"",
            p + "SNAPSHOT=$(ls -1t \"$SNAPSHOT_DIR\"/antiphon-*.dump.age | head -n 1)",
            p + "mkdir -p -m 0700 " + scratch,
            p + "docker volume create antiphon-server2-postgres-drill_pgdata",
            p + "docker compose --env-file " + envArg + " -f " + composeArg + " -p antiphon-server2-postgres-drill up -d",
            p + "copy_start=$(date +%s); scp \"$SNAPSHOT\" mc@server2:" + scratch + "/antiphon-drill.dump.age; copy_end=$(date +%s); echo copy-elapsed-sec=$((copy_end - copy_start))",
            p + "umask 077; age -d -i \"$AGE_IDENTITY\" -o " + scratch + "/antiphon-drill-plain.dump " + scratch + "/antiphon-drill.dump.age; chmod 0600 " + scratch + "/antiphon-drill-plain.dump",
            p + "docker exec -i antiphon-server2-postgres-drill-postgres-1 pg_restore -U antiphon -d antiphon --no-owner --no-acl < " + scratch + "/antiphon-drill-plain.dump",
            p + "docker exec antiphon-server2-postgres-drill-postgres-1 psql -U antiphon -d antiphon -At -c \"select relname, n_live_tup from pg_stat_user_tables order by relname\"",
            p + "docker exec antiphon-server2-postgres-drill-postgres-1 psql -U antiphon -d antiphon -At -c \"select \\\"MigrationId\\\" from \\\"__EFMigrationsHistory\\\" order by \\\"MigrationId\\\"\"",
            p + "docker exec antiphon-server2-postgres-drill-postgres-1 psql -U antiphon -d antiphon -At -c \"select extname, extversion from pg_extension order by extname\"",
            p + "shred -u " + scratch + "/antiphon-drill-plain.dump",
            p + "docker compose --env-file " + envArg + " -f " + composeArg + " -p antiphon-server2-postgres-drill down",
            p + "if case antiphon-server2-postgres-drill_pgdata in *-drill_pgdata) true ;; *) echo 'SERVER2-POSTGRES fail exit=2 reason=volume-forbidden'; exit 2 ;; esac; then docker volume rm antiphon-server2-postgres-drill_pgdata; fi",
            PlanOk,
        ];
    }

    private static string HostLine(string envArg, string composePath) =>
        "docker compose --env-file " + envArg + " -f " + composePath + " -p antiphon-server2-postgres up -d";

    private static void Planned(Server2PostgresRun run, params string[] lines)
    {
        run.Exit.ShouldBe(0, run.ToString());
        run.Lines.ShouldBe(lines, run.ToString());
        run.Error.ShouldBeEmpty();
        run.Tools.ShouldBeEmpty("the plan calls no tool");
        run.Hides(Server2PostgresWorld.Password).ShouldBeTrue(run.ToString());
    }

    private static void Refused(Server2PostgresRun run, string reason, int exit = 2)
    {
        run.Exit.ShouldBe(exit, run.ToString());
        run.Lines.ShouldBe(["SERVER2-POSTGRES fail exit=" + exit + " reason=" + reason], run.ToString());
        run.Tools.ShouldBeEmpty("a refusal before the lane calls no tool");
        run.Hides(Server2PostgresWorld.Password).ShouldBeTrue(run.ToString());
    }

    // ---- the options ----

    [Test]
    public void Options_take_the_scripts_parameters_in_kebab_case()
    {
        var options = Server2PostgresDeployOptions.Parse([], "checkout");
        (options.WhatIf, options.Execute, options.RestoreDrill, options.Project, options.EnvFile, options.ComposeFile, options.RepoRoot, options.Checkout)
            .ShouldBe((false, false, false, "", "", "", "", "checkout"));
        options = Server2PostgresDeployOptions.Parse(["--what-if", "--execute", "--restore-drill", "--project", " p ", "--env-file", "e", "--compose-file", "c", "--repo-root", ""], null!);
        (options.WhatIf, options.Execute, options.RestoreDrill, options.Project, options.EnvFile, options.ComposeFile, options.RepoRoot, options.Checkout)
            .ShouldBe((true, true, true, " p ", "e", "c", "", ""));

        // Parameter binding refused these with exit 1 (and took -What and -Restore as prefixes); the CLI's usage error is exit 2.
        Should.Throw<NightlyUsageException>(() => Server2PostgresDeployOptions.Parse(["--bogus"], "")).Message.ShouldBe("unknown flag --bogus");
        Should.Throw<NightlyUsageException>(() => Server2PostgresDeployOptions.Parse(["-WhatIf"], "")).Message.ShouldBe("unexpected argument '-WhatIf'");
        Should.Throw<NightlyUsageException>(() => Server2PostgresDeployOptions.Parse(["--what"], "")).Message.ShouldBe("unknown flag --what");
        Should.Throw<NightlyUsageException>(() => Server2PostgresDeployOptions.Parse(["--env-file"], "")).Message.ShouldBe("missing value for --env-file");
        Server2PostgresDeployOptions.Usage.ShouldBe("deploy-server2-postgres [--what-if | --execute] [--restore-drill] [--project antiphon-server2-postgres]"
            + " [--env-file <file>] [--compose-file <file>] [--repo-root <dir>]");
    }

    // ---- the plan (the default and --what-if) ----

    [Test]
    public void Plan_prints_the_host_daemon_command_and_calls_no_tool()
    {
        var world = new Server2PostgresWorld();
        var line = HostLine("<env-file>", world.ProductionCompose);
        Planned(world.Run(), line, PlanOk);
        Planned(world.Run("--what-if"), line, PlanOk);
        // The project is trimmed; a blank one is the mode's own.
        Planned(world.Run("--what-if", "--project", "  antiphon-server2-postgres  "), line, PlanOk);
        Planned(world.Run("--what-if", "--project", "   "), line, PlanOk);
        // With a valid env file its path is the --env-file argument; the password is never printed.
        var env = world.Env();
        Planned(world.Run("--what-if", "--env-file", env), HostLine(env, world.ProductionCompose), PlanOk);
        // A blank --repo-root is the checkout; another root holds the compose the plan names.
        Planned(world.Run("--repo-root", " "), line, PlanOk);
        var other = Path.Combine(Server2PostgresWorld.Base, "other");
        world.Files.AddFile(Path.Combine(other, Server2PostgresDeploy.ComposeName), new System.IO.Abstractions.TestingHelpers.MockFileData(Server2PostgresWorld.ProductionText));
        Planned(world.Run("--repo-root", other), HostLine("<env-file>", Path.Combine(other, Server2PostgresDeploy.ComposeName)), PlanOk);
    }

    [Test]
    public void Drill_plan_prints_the_captured_commands_and_calls_no_tool()
    {
        var world = new Server2PostgresWorld();
        var drill = CapturedDrill("<env-file>", "docker-compose.server2-postgres-drill.yml");
        Planned(world.Run("--what-if", "--restore-drill"), drill);
        Planned(world.Run("--restore-drill"), drill);
        var env = world.Env();
        Planned(world.Run("--what-if", "--restore-drill", "--env-file", env), CapturedDrill(env, "docker-compose.server2-postgres-drill.yml"));
        // The drill names its compose by file name; a CRLF drill compose is read as LF (drill-crlf).
        var crlf = world.Compose("drill-crlf.yml", Server2PostgresWorld.DrillText.Replace("\n", "\r\n"));
        Planned(world.Run("--what-if", "--restore-drill", "--compose-file", crlf), CapturedDrill("<env-file>", "drill-crlf.yml"));
    }

    // ---- the refusals before any tool ----

    [Test]
    public void Mode_and_project_refusals_are_the_scripts()
    {
        var world = new Server2PostgresWorld();
        Refused(world.Run("--what-if", "--execute"), "ambiguous-mode", 3);
        Refused(world.Run("--what-if", "--execute", "--restore-drill", "--project", "nope", "--compose-file", Path.Combine(Server2PostgresWorld.Base, "nowhere.yml")), "ambiguous-mode", 3);
        Refused(world.Run("--what-if", "--project", "antiphon-server2-postgres-drill"), "production-compose-wrong-project");
        Refused(world.Run("--what-if", "--restore-drill", "--project", "antiphon-server2-postgres"), "drill-compose-wrong-project");
        // -cne: the project is compared case-sensitively.
        Refused(world.Run("--what-if", "--project", "Antiphon-Server2-Postgres"), "production-compose-wrong-project");
        Refused(world.Run("--what-if", "--restore-drill", "--compose-file", world.ProductionCompose), "production-compose-wrong-project");
        Refused(world.Run("--what-if", "--compose-file", world.DrillCompose), "drill-compose-wrong-project");
        Refused(world.Run("--what-if", "--compose-file", Path.Combine(Server2PostgresWorld.Base, "nope.yml")), "missing-compose");
        Refused(world.Run("--what-if", "--repo-root", world.Secrets), "missing-compose");
    }

    [Test]
    public void Compose_refusals_are_the_scripts()
    {
        var world = new Server2PostgresWorld();
        var prod = Server2PostgresWorld.ProductionText;
        var drill = Server2PostgresWorld.DrillText;
        var network = prod.Replace("    networks:\n      - antiphon-server2\n", "").Replace("networks:\n  antiphon-server2:\n    name: antiphon-server2\n\n", "");
        foreach (var (name, text, reason) in new[]
        {
            ("pgdata.yml", prod.Replace("antiphon-server2-postgres_pgdata", "antiphon_pgdata"), "volume-forbidden"),
            ("alpine.yml", prod + "# alpine\n", "image-alpine"),
            ("any.yml", prod + "# 0.0.0.0\n", "bind-any"),
            ("5433.yml", prod + "# 5433\n", "port-5433"),
            ("net.yml", network, "compose-invalid"),
            ("cpuset.yml", prod.Replace("    cpuset: \"0,12\"\n", ""), "compose-invalid"),
            // The production refusal reads the text as it is: a CRLF checkout (Windows with autocrlf) is compose-invalid.
            ("crlf.yml", prod.Replace("\n", "\r\n"), "compose-invalid"),
        })
            Refused(world.Run("--what-if", "--compose-file", world.Compose(name, text)), reason);
        foreach (var (name, text, reason) in new[]
        {
            ("drill-shares.yml", drill.Replace("    cpu_shares: 2048\n", ""), "compose-invalid"),
            ("drill-port.yml", drill + "# 10.0.0.1:5432:5432\n", "port-production"),
            ("drill-bind.yml", drill + "# ANTIPHON_POSTGRES_BIND_ADDRESS\n", "bind-not-loopback"),
            ("drill-net.yml", drill.Replace("      - antiphon-server2-postgres-drill\n", "      - antiphon-server2-postgres-drill\n      - antiphon-server2\n"), "network-forbidden"),
        })
            Refused(world.Run("--what-if", "--restore-drill", "--compose-file", world.Compose(name, text)), reason);
        // The repo's own CRLF file: the script refused it the same way on a Windows checkout.
        world.Files.AddFile(world.ProductionCompose, new System.IO.Abstractions.TestingHelpers.MockFileData(prod.Replace("\n", "\r\n")));
        Refused(world.Run(), "compose-invalid");
    }

    [Test]
    public void Unreadable_compose_reads_as_empty_with_one_error_line()
    {
        // compose-dir: Test-Path passed, ReadAllText failed with an error record, and the empty text was compose-invalid
        // (production) or not the drill (drill).
        var world = new Server2PostgresWorld();
        var directory = Path.Combine(Server2PostgresWorld.Base, "compose-dir");
        world.Files.AddDirectory(directory);
        var run = world.Run("--what-if", "--compose-file", directory);
        run.Exit.ShouldBe(2, run.ToString());
        run.Lines.ShouldBe(["SERVER2-POSTGRES fail exit=2 reason=compose-invalid"]);
        run.Error.Trim().Split('\n').Length.ShouldBe(1, run.Error);
        run = world.Run("--what-if", "--restore-drill", "--compose-file", directory);
        run.Lines.ShouldBe(["SERVER2-POSTGRES fail exit=2 reason=production-compose-wrong-project"]);
    }

    [Test]
    public void Env_file_refusals_are_the_scripts()
    {
        var world = new Server2PostgresWorld();
        Refused(world.Run("--what-if", "--env-file", Path.Combine(world.Secrets, "nope.env")), "env-missing");
        Refused(world.Run("--what-if", "--restore-drill", "--env-file", "relative.env"), "env-missing");
        var inside = Path.Combine(world.Root, "in.env");
        world.Files.AddFile(inside, new System.IO.Abstractions.TestingHelpers.MockFileData("X=1\n"));
        world.OwnerOnly.Add(inside);
        Refused(world.Run("--what-if", "--env-file", inside), "env-inside-repo");
        Refused(world.Run("--what-if", "--env-file", world.Env(ownerOnly: false)), "env-mode");
        Refused(world.Run("--execute", "--env-file", world.Env(ownerOnly: false)), "env-mode");
        Refused(world.Run("--execute"), "env-missing");
        Refused(world.Run("--execute", "--restore-drill"), "env-missing");

        // $env:OS -eq 'Windows_NT' (ignoring case): always env-mode, whatever the file's mode.
        var good = world.Env();
        foreach (var os in new[] { "Windows_NT", "windows_nt" })
        {
            world.Variables["OS"] = os;
            Refused(world.Run("--what-if", "--env-file", good), "env-mode");
            Refused(world.Run("--execute", "--env-file", good), "env-mode");
        }
        world.Variables["OS"] = "Linux";
        Planned(world.Run("--what-if", "--env-file", good), HostLine(good, world.ProductionCompose), PlanOk);
    }

    [Test]
    public void Env_value_refusals_and_acceptances_are_the_scripts()
    {
        var world = new Server2PostgresWorld();
        var image = Server2PostgresWorld.Image;
        foreach (var (name, path, reason) in new[]
        {
            ("w-bind-any", world.Env("a.env", bind: "0.0.0.0"), "bind-any"),
            ("w-bind-empty", world.Env("b.env", emptyBind: true), "empty-bind"),
            ("w-bind-omitted", world.Env("c.env", omitBind: true), "bind-omitted"),
            ("w-bind-lan", world.Env("d.env", bind: "192.168.4.34"), "bind-not-cgnat"),
            ("w-bind-v6", world.Env("e.env", bind: "fd7a::1"), "bind-not-ipv4"),
            ("w-image-floating", world.Env("f.env", image: "postgres:16"), "image-floating"),
            ("w-image-alpine", world.Env("g.env", image: "postgres:16-alpine@sha256:" + new string('b', 64)), "image-alpine"),
            ("w-image-empty", world.EnvLines("h.env", ["ANTIPHON_POSTGRES_BIND_ADDRESS=100.64.0.2", "POSTGRES_PASSWORD=fixture-password-1406"]), "empty-image"),
            ("w-image-patch-nodigest", world.Env("i.env", image: "postgres:16.14"), "image-floating"),
            ("w-password-empty", world.Env("j.env", password: "   "), "empty-password"),
            ("w-password-dev", world.Env("k.env", password: Server2PostgresWorld.DevPassword), "dev-password"),
        })
        {
            var run = world.Run("--what-if", "--env-file", path);
            Refused(run, reason);
            run.Hides(Server2PostgresWorld.DevPassword).ShouldBeTrue(name);
        }

        foreach (var path in new[]
        {
            world.Env("w-image-repodigest.env", image: "postgres@sha256:" + new string('c', 64)),
            world.Env("w-image-patch.env", image: "postgres:16.14@sha256:" + new string('9', 64)),
            // A PowerShell hashtable: the key's case is ignored.
            world.EnvLines("w-bind-lowerkey.env", ["antiphon_postgres_bind_address=100.64.0.2", "ANTIPHON_POSTGRES_IMAGE=" + image, "POSTGRES_PASSWORD=fixture-password-1406"]),
            world.EnvLines("w-comments.env", ["# comment", "", "  ANTIPHON_POSTGRES_BIND_ADDRESS = 100.64.0.2  ", "=junk", "ANTIPHON_POSTGRES_IMAGE=" + image, "noequals", "POSTGRES_PASSWORD=fixture-password-1406"]),
        })
            Planned(world.Run("--what-if", "--env-file", path), HostLine(path, world.ProductionCompose), PlanOk);

        // docker-compose.dev.yml's first POSTGRES_PASSWORD value is the dev password, compared case-sensitively; without the file
        // there is no dev check.
        world.Files.AddFile(Path.Combine(world.Root, "docker-compose.dev.yml"),
            new System.IO.Abstractions.TestingHelpers.MockFileData("services:\n  postgres:\n    environment:\n      POSTGRES_PASSWORD:   devpw  \n      POSTGRES_PASSWORD: second\n"));
        Refused(world.Run("--what-if", "--env-file", world.Env("l.env", password: "devpw")), "dev-password");
        var second = world.Env("m.env", password: "second");
        Planned(world.Run("--what-if", "--env-file", second), HostLine(second, world.ProductionCompose), PlanOk);
        var upper = world.Env("n.env", password: "DEVPW");
        Planned(world.Run("--what-if", "--env-file", upper), HostLine(upper, world.ProductionCompose), PlanOk);
        world.Files.RemoveFile(Path.Combine(world.Root, "docker-compose.dev.yml"));
        var dev = world.Env("o.env", password: Server2PostgresWorld.DevPassword);
        Planned(world.Run("--what-if", "--env-file", dev), HostLine(dev, world.ProductionCompose), PlanOk);
    }

    [Test]
    public void Unreadable_env_file_reads_as_empty_with_one_error_line()
    {
        // ReadAllLines failed inside Read-Server2PostgresEnv: its loop stopped with an error record and the empty document
        // refused the bind as omitted.
        var world = new Server2PostgresWorld();
        var directory = Path.Combine(world.Secrets, "dir.env");
        world.Files.AddDirectory(directory);
        world.OwnerOnly.Add(directory);
        var run = world.Run("--what-if", "--env-file", directory);
        run.Exit.ShouldBe(2, run.ToString());
        run.Lines.ShouldBe(["SERVER2-POSTGRES fail exit=2 reason=bind-omitted"]);
        run.Error.Trim().Split('\n').Length.ShouldBe(1, run.Error);
        run.Tools.ShouldBeEmpty();
    }

    // ---- --execute ----

    [Test]
    public void Execute_refuses_off_the_server2_host_after_reading_the_daemon_and_hostname()
    {
        var world = new Server2PostgresWorld();
        var env = world.Env();
        // x-off-host: a sibling daemon; x-no-daemon: docker info printed nothing. hostname is read either way.
        foreach (var daemon in new[] { "not-the-host", null })
        {
            world.Daemon = daemon;
            var run = world.Run("--execute", "--env-file", env);
            run.Exit.ShouldBe(2, run.ToString());
            run.Lines.ShouldBe(["SERVER2-POSTGRES fail exit=2 reason=not-server2-host"]);
            run.Tools.ShouldBe([Info, "hostname"]);
            run.Hides(Server2PostgresWorld.Password).ShouldBeTrue();
        }

        // Inside a container (/.dockerenv) the refusal comes before any docker call.
        world.Daemon = world.Hostname = "server2";
        world.Files.AddFile("/.dockerenv", new System.IO.Abstractions.TestingHelpers.MockFileData(""));
        var nested = world.Run("--execute", "--env-file", env);
        nested.Lines.ShouldBe(["SERVER2-POSTGRES fail exit=2 reason=not-server2-host"]);
        nested.Tools.ShouldBeEmpty();
        world.Files.RemoveFile("/.dockerenv");

        // No docker on PATH: lane none, nothing started.
        world.Tools.Remove("docker");
        var none = world.Run("--execute", "--env-file", env);
        none.Lines.ShouldBe(["SERVER2-POSTGRES fail exit=2 reason=not-server2-host"]);
        none.Tools.ShouldBeEmpty();
        world.Tools["docker"] = Path.Combine(Server2PostgresWorld.Base, "bin", "docker");

        // No hostname on PATH: the call operator's error record, an empty name, a sibling.
        world.Tools.Remove("hostname");
        var nameless = world.Run("--execute", "--env-file", env);
        nameless.Lines.ShouldBe(["SERVER2-POSTGRES fail exit=2 reason=not-server2-host"]);
        nameless.Tools.ShouldBe([Info]);
        nameless.Error.ShouldContain("'hostname' is not recognized");
    }

    [Test]
    public void Execute_requires_the_single_tailscale_line_to_equal_the_bind()
    {
        var world = new Server2PostgresWorld { Daemon = "server2", Hostname = "server2" };
        var env = world.Env();
        foreach (var text in new[] { "100.64.0.9\r\n", "100.64.0.2\r\n100.64.0.3\r\n", "" })
        {
            world.TailscaleText = text;
            var run = world.Run("--execute", "--env-file", env);
            run.Exit.ShouldBe(2, run.ToString());
            run.Lines.ShouldBe(["SERVER2-POSTGRES fail exit=2 reason=bind-not-tailnet"]);
            run.Tools.ShouldBe([Info, "hostname", "tailscale ip -4"]);
        }

        // x-bind-space: the bind check trims, the tailnet comparison takes the value as written.
        world.TailscaleText = "100.64.0.2\r\n";
        var spaced = world.EnvLines("space.env", ["ANTIPHON_POSTGRES_BIND_ADDRESS= 100.64.0.2", "ANTIPHON_POSTGRES_IMAGE=" + Server2PostgresWorld.Image, "POSTGRES_PASSWORD=fixture-password-1406"]);
        world.Run("--execute", "--env-file", spaced).Lines.ShouldBe(["SERVER2-POSTGRES fail exit=2 reason=bind-not-tailnet"]);

        // No tailscale on PATH.
        world.Tools.Remove("tailscale");
        var missing = world.Run("--execute", "--env-file", env);
        missing.Lines.ShouldBe(["SERVER2-POSTGRES fail exit=2 reason=bind-not-tailnet"]);
        missing.Tools.ShouldBe([Info, "hostname"]);
    }

    [Test]
    public void Execute_on_the_host_starts_only_the_production_project()
    {
        // x-up-ok and x-host-case (-ne ignores case: Server2 is server2).
        foreach (var daemon in new[] { "server2", "Server2" })
        {
            var world = new Server2PostgresWorld { Daemon = daemon, Hostname = "server2", TailscaleText = "100.64.0.2\r\n" };
            var env = world.Env();
            var run = world.Run("--execute", "--env-file", env);
            run.Exit.ShouldBe(0, run.ToString());
            run.Lines.ShouldBe([Ok]);
            run.Error.ShouldBeEmpty();
            run.Tools.ShouldBe([Info, "hostname", "tailscale ip -4",
                "docker compose --env-file " + env + " -f " + world.ProductionCompose + " -p antiphon-server2-postgres up -d"]);
            run.Hides(Server2PostgresWorld.Password).ShouldBeTrue();
            // compose's output is captured and not printed.
            world.Launcher.Launches[^1].CaptureOutput.ShouldBeTrue();
        }

        // x-up-fail.
        var failing = new Server2PostgresWorld { Daemon = "server2", Hostname = "server2", TailscaleText = "100.64.0.2\n", ComposeExit = 1 };
        var failed = failing.Run("--execute", "--env-file", failing.Env());
        failed.Exit.ShouldBe(2, failed.ToString());
        failed.Lines.ShouldBe(["SERVER2-POSTGRES fail exit=2 reason=compose-up-failed"]);
        failed.Tools.Length.ShouldBe(4);
    }

    [Test]
    public void Execute_restore_drill_prints_the_drill_and_starts_nothing()
    {
        var world = new Server2PostgresWorld { Daemon = "server2", Hostname = "server2", TailscaleText = "100.64.0.2\r\n" };
        var env = world.Env();
        var run = world.Run("--execute", "--restore-drill", "--env-file", env);
        run.Exit.ShouldBe(0, run.ToString());
        run.Lines.ShouldBe(CapturedDrill(env, "docker-compose.server2-postgres-drill.yml"));
        run.Tools.ShouldBe([Info, "hostname", "tailscale ip -4"]);
    }

    // ---- the script's functions (captured by dot-sourcing it) ----

    [Test]
    public void Bind_answers_are_the_scripts()
    {
        foreach (var (address, reason) in new[]
        {
            ("100.64.0.0", ""), ("100.127.255.255", ""), ("100.63.255.255", "bind-not-cgnat"), ("100.128.0.1", "bind-not-cgnat"),
            ("192.168.4.34", "bind-not-cgnat"), ("8.8.8.8", "bind-not-cgnat"), ("0.0.0.0", "bind-any"), (" 0.0.0.0 ", "bind-any"), ("::", "bind-any"),
            ("*", "bind-any"), ("", "empty-bind"), ("   ", "empty-bind"), ("100.64.0.2", ""), (" 100.64.0.2 ", ""), ("100.64.0.256", "bind-not-ipv4"),
            ("100.64.0", "bind-not-ipv4"), ("100.64.0.2.1", "bind-not-ipv4"), ("1000.64.0.2", "bind-not-ipv4"), ("100.064.0.2", ""), ("100.64.00.2", ""),
            ("fd7a::1", "bind-not-ipv4"), ("100.64.0.2/32", "bind-not-ipv4"), ("a.b.c.d", "bind-not-ipv4"), ("100.100.100.100", ""),
        })
            Server2PostgresDeploy.Bind(true, address).ShouldBe(reason, "[" + address + "]");
        Server2PostgresDeploy.Bind(false, "100.64.0.2").ShouldBe("bind-omitted");
    }

    [Test]
    public void Image_answers_are_the_scripts()
    {
        var d64 = new string('9', 64);
        foreach (var (image, reason) in new[]
        {
            ("postgres:16@sha256:" + new string('a', 64), ""), ("postgres@sha256:" + new string('c', 64), ""),
            // -match ignores case for the two digest forms; the patch tag's -cmatch does not.
            ("postgres:16@sha256:" + new string('A', 64), ""), ("POSTGRES:16@SHA256:" + new string('a', 64), ""), ("Postgres@sha256:" + new string('a', 64), ""),
            ("postgres:16.14@sha256:" + d64, ""), ("postgres:16.14-trixie@sha256:" + d64, ""), ("postgres:16.0@sha256:" + d64, ""),
            ("Postgres:16.14@sha256:" + d64, "image-floating"), ("postgres:16.14@sha256:" + new string('A', 64), "image-floating"),
            ("postgres:16.14", "image-floating"), ("postgres:16.14-trixie", "image-floating"), ("postgres:16", "image-floating"), ("postgres:latest", "image-floating"),
            ("postgres:latest@sha256:" + d64, "image-floating"), ("postgres:16.14-bookworm@sha256:" + d64, "image-floating"), ("postgres:17.2@sha256:" + d64, "image-floating"),
            ("postgres:16.14.1@sha256:" + d64, "image-floating"), ("postgres:16.@sha256:" + d64, "image-floating"), ("postgres:16.014@sha256:" + d64, "image-floating"),
            ("postgres:16.14@sha256:" + new string('9', 12), "image-floating"), ("library/postgres:16.14@sha256:" + d64, "image-floating"),
            ("postgres@sha256:" + new string('d', 12), "image-floating"), ("postgres:16-alpine@sha256:" + new string('b', 64), "image-alpine"),
            ("postgres:16-ALPINE@sha256:" + new string('b', 64), "image-alpine"), ("", "empty-image"), ("   ", "empty-image"),
            (" postgres:16@sha256:" + new string('a', 64), "image-floating"), ("postgres:16@sha256:" + new string('a', 64) + " ", "image-floating"),
        })
            Server2PostgresDeploy.Image(image).ShouldBe(reason, "[" + image + "]");
    }

    [Test]
    public void Drill_volume_and_teardown_answers_are_the_scripts()
    {
        foreach (var name in new[] { "antiphon_pgdata", "antiphon-server2-postgres_pgdata", "pgdata", "dind-data", "windmill_db", "Windmill-drill_pgdata",
                     "Antiphon-Server2-Postgres-Drill_pgdata", "ANTIPHON_PGDATA", "PGDATA", "other-drill_pgdata", "" })
        {
            Server2PostgresDeploy.DrillVolumeRefusal(name).ShouldBe("volume-forbidden", name);
            Server2PostgresDeploy.DrillTeardown(name).ShouldBe("volume-forbidden", name);
        }
        Server2PostgresDeploy.DrillVolumeRefusal("antiphon-server2-postgres-drill_pgdata").ShouldBe("");
        Server2PostgresDeploy.DrillTeardown("antiphon-server2-postgres-drill_pgdata").ShouldBe("");
    }

    [Test]
    public void Tailnet_lane_and_host_command_answers_are_the_scripts()
    {
        foreach (var (bind, text, reason) in new[]
        {
            ("100.64.0.2", "100.64.0.2\n", ""), ("100.64.0.2", "100.64.0.2\r\n", ""), ("100.64.0.2", "\n  100.64.0.2  \n\n", ""),
            ("192.168.4.34", "100.64.0.2\n", "bind-not-tailnet"), ("100.64.0.2", "", "bind-not-tailnet"), ("100.64.0.2", "100.64.0.2\n100.64.0.3\n", "bind-not-tailnet"),
            (" 100.64.0.2", "100.64.0.2\n", "bind-not-tailnet"), ("100.64.0.2", "100.64.0.2\r100.64.0.3", "bind-not-tailnet"),
        })
            Server2PostgresDeploy.Tailnet(bind, text).ShouldBe(reason, "[" + bind + "] [" + text + "]");

        foreach (var (daemon, hostname, inContainer, lane) in new[]
        {
            ("d", "drill", false, "sibling"), ("", "x", false, "none"), ("x", "x", false, "host"), ("X", "x", false, "host"), ("x", "y", false, "sibling"),
            ("x", "x", true, "nested"), ("  ", "x", false, "none"),
        })
            Server2PostgresDeploy.Lane(daemon, hostname, inContainer).ShouldBe(lane);

        Server2PostgresDeploy.HostCommand("", @"C:\r\docker-compose.server2-postgres.yml")
            .ShouldBe(@"docker compose --env-file <env-file> -f C:\r\docker-compose.server2-postgres.yml -p antiphon-server2-postgres up -d");
        Server2PostgresDeploy.HostCommand("   ", "/home/mc/x.yml").ShouldBe("docker compose --env-file <env-file> -f /home/mc/x.yml -p antiphon-server2-postgres up -d");
        Server2PostgresDeploy.HostCommand("/home/mc/antiphon-server2/postgres.env", "/home/mc/antiphon-server2/postgres/docker-compose.server2-postgres.yml")
            .ShouldBe("docker compose --env-file /home/mc/antiphon-server2/postgres.env -f /home/mc/antiphon-server2/postgres/docker-compose.server2-postgres.yml -p antiphon-server2-postgres up -d");
    }

    [Test]
    public void Compose_class_and_exact_line_answers_are_the_scripts()
    {
        var prod = Server2PostgresWorld.ProductionText;
        var drill = Server2PostgresWorld.DrillText;
        foreach (var (text, kind) in new[]
        {
            (prod, "production"), (drill, "drill"), (prod.Replace("\n", "\r\n"), "production"), ("", "unknown"), (prod + drill, "unknown"),
            (drill + "    name: antiphon-server2-postgres_pgdata\n", "unknown"), ("name: antiphon-server2-postgres\n", "unknown"),
            ("  name: antiphon-server2-postgres\n    name: antiphon-server2-postgres_pgdata\n", "unknown"),
            ("NAME: antiphon-server2-postgres\n    name: antiphon-server2-postgres_pgdata\n", "unknown"),
        })
            Server2PostgresDeploy.ComposeClass(text).ShouldBe(kind);
        Server2PostgresDeploy.ExactLine("a\r\nname: x\r\n", "name: x").ShouldBeTrue();
        Server2PostgresDeploy.ExactLine("a\rname: x\r", "name: x").ShouldBeFalse();
        Server2PostgresDeploy.ExactLine("NAME: x\n", "name: x").ShouldBeFalse();
        Server2PostgresDeploy.ExactLine("name: x \n", "name: x").ShouldBeFalse();
    }

    /// <summary>Each mutant and the two refusal functions' answers on the production and the drill compose (prod, drill).</summary>
    private static readonly (string Name, string From, string To, string Prod1, string Drill1, string Prod2, string Drill2)[] Mutants =
    [
        ("accept", "", "", "", "volume-forbidden", "compose-invalid", ""),
        ("without the cpuset", "    cpuset: \"0,12\"\n", "", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("with an unpinned 0-23 cpuset", "cpuset: \"0,12\"", "cpuset: \"0-23\"", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("with the old 0-2,12-14 cpuset", "cpuset: \"0,12\"", "cpuset: \"0-2,12-14\"", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("with a 0,12,13 cpuset", "cpuset: \"0,12\"", "cpuset: \"0,12,13\"", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("with a second cpuset line", "    cpu_shares: 2048\n", "    cpu_shares: 2048\n    cpuset: \"1,13\"\n", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("without cpu_shares 2048", "    cpu_shares: 2048\n", "", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("with cpu_shares 1024", "    cpu_shares: 2048\n", "    cpu_shares: 1024\n", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("with cpu_shares 20480", "    cpu_shares: 2048\n", "    cpu_shares: 20480\n", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("with a cpus 6.0 quota", "    cpu_shares: 2048\n", "    cpu_shares: 2048\n    cpus: 6.0\n", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("with a cpus 2.0 quota", "    cpu_shares: 2048\n", "    cpu_shares: 2048\n    cpus: 2.0\n", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("without mem_limit 8g", "    mem_limit: 8g\n", "", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("with mem_limit 4g", "mem_limit: 8g", "mem_limit: 4g", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("without shm_size 1gb", "    shm_size: 1gb\n", "", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("without blkio weight 800", "      weight: 800\n", "", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("quoted shares", "    cpu_shares: 2048\n", "    cpu_shares: \"2048\"\n", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("tab cpuset", "    cpuset: \"0,12\"\n", "\tcpuset: \"0,12\"\n", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("cpus in comment col0", "    cpu_shares: 2048\n", "    cpu_shares: 2048\n# cpus: 1\n", "", "volume-forbidden", "compose-invalid", ""),
        ("cpus indented comment", "    cpu_shares: 2048\n", "    cpu_shares: 2048\n    #cpus: 1\n", "", "volume-forbidden", "compose-invalid", ""),
        ("pgdata", "antiphon-server2-postgres_pgdata", "antiphon_pgdata", "volume-forbidden", "volume-forbidden", "compose-invalid", ""),
        ("alpine", "restart: unless-stopped", "restart: unless-stopped # Alpine", "image-alpine", "volume-forbidden", "image-alpine", "image-alpine"),
        ("any", "restart: unless-stopped", "restart: unless-stopped # 0.0.0.0", "bind-any", "volume-forbidden", "bind-any", "bind-any"),
        ("5433", "restart: unless-stopped", "restart: unless-stopped # 5433", "port-5433", "volume-forbidden", "port-5433", "port-5433"),
        ("no restart", "    restart: unless-stopped\n", "", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("no healthcheck", "pg_isready -U antiphon -d antiphon", "pg_isready", "compose-invalid", "volume-forbidden", "compose-invalid", "compose-invalid"),
        ("no pg_stat track", "pg_stat_statements.track=top", "pg_stat_statements.track=all", "compose-invalid", "volume-forbidden", "compose-invalid", ""),
        ("no network block", "networks:\n  antiphon-server2:\n    name: antiphon-server2\n", "", "compose-invalid", "volume-forbidden", "compose-invalid", ""),
        ("no attachment", "    networks:\n      - antiphon-server2\n", "", "compose-invalid", "volume-forbidden", "compose-invalid", ""),
        ("crlf", "\n", "\r\n", "compose-invalid", "volume-forbidden", "compose-invalid", ""),
    ];

    [Test]
    public void Compose_refusal_answers_on_each_mutant_are_the_scripts()
    {
        foreach (var mutant in Mutants)
        {
            var prod = mutant.From.Length == 0 ? Server2PostgresWorld.ProductionText : Server2PostgresWorld.ProductionText.Replace(mutant.From, mutant.To);
            (Server2PostgresDeploy.ComposeRefusal(prod), Server2PostgresDeploy.DrillComposeRefusal(prod)).ShouldBe((mutant.Prod1, mutant.Drill1), "production " + mutant.Name);
            var drill = mutant.From.Length == 0 ? Server2PostgresWorld.DrillText : Server2PostgresWorld.DrillText.Replace(mutant.From, mutant.To);
            (Server2PostgresDeploy.ComposeRefusal(drill), Server2PostgresDeploy.DrillComposeRefusal(drill)).ShouldBe((mutant.Prod2, mutant.Drill2), "drill " + mutant.Name);
        }

        var text = Server2PostgresWorld.DrillText;
        foreach (var (name, changed, reason) in new[]
        {
            ("prod volume comment", text + "# name: antiphon-server2-postgres_pgdata\n", "volume-forbidden"),
            ("antiphon_pgdata comment", text + "# antiphon_pgdata\n", "volume-forbidden"),
            ("5433", text + "# 5433\n", "port-5433"),
            ("alpine", text + "# ALPINE\n", "image-alpine"),
            ("any", text + "# 0.0.0.0\n", "bind-any"),
            ("prod port", text + "# 1.2.3.4:5432:5432\n", "port-production"),
            ("bind var", text + "# ANTIPHON_POSTGRES_BIND_ADDRESS\n", "bind-not-loopback"),
            ("prod network line", text + "      - antiphon-server2\n", "network-forbidden"),
            ("prod network block", text + "networks:\n  antiphon-server2:\n    name: antiphon-server2\n", "network-forbidden"),
            ("no loopback port", text.Replace("127.0.0.1:5434:5432", "127.0.0.1:5435:5432"), "compose-invalid"),
            ("no drill network block", text.Replace("networks:\n  antiphon-server2-postgres-drill:\n    name: antiphon-server2-postgres-drill", "x"), "compose-invalid"),
            ("crlf", text.Replace("\n", "\r\n"), ""),
        })
            Server2PostgresDeploy.DrillComposeRefusal(changed).ShouldBe(reason, name);
    }

    [Test]
    public void Render_answers_are_the_scripts()
    {
        var prod = Server2PostgresWorld.ProductionText;
        var image = Server2PostgresWorld.Image;
        var rendered = Server2PostgresDeploy.Render(prod, "100.64.0.2", image, Server2PostgresWorld.Password);
        (rendered.Ok, rendered.Reason).ShouldBe((true, "ok"));
        rendered.Redacted.ShouldBe(prod.Replace("${ANTIPHON_POSTGRES_BIND_ADDRESS:?ANTIPHON_POSTGRES_BIND_ADDRESS is required}", "100.64.0.2")
            .Replace("${ANTIPHON_POSTGRES_IMAGE:?ANTIPHON_POSTGRES_IMAGE is required}", image)
            .Replace("${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}", "<redacted>"));
        foreach (var (bind, renderImage, password, reason) in new[]
        {
            ("", image, "p", "empty-bind"), ("0.0.0.0", image, "p", "bind-any"), ("100.64.0.2", "postgres:16", "p", "image-floating"),
            ("100.64.0.2", "postgres:16-alpine@sha256:" + new string('b', 64), "p", "image-alpine"), ("100.64.0.2", image, " ", "empty-password"),
            ("10.0.0.1", image, "p", "bind-not-cgnat"),
        })
            Server2PostgresDeploy.Render(prod, bind, renderImage, password).ShouldBe(new Server2PostgresRender(false, reason, ""));
    }

    [Test]
    public void Env_parse_and_repo_answers_are_the_scripts()
    {
        var env = Server2PostgresEnv.Parse(["# c", "", "  KEY = v a l  ", "=x", "noeq", "DUP=1", "dup=2", "EMPTY=", "SP= lead", "Q=\"quoted\"", "EQ=a=b"]);
        foreach (var (key, present, value) in new[]
        {
            ("KEY", true, " v a l"), ("key", true, " v a l"), ("DUP", true, "2"), ("EMPTY", true, ""), ("SP", true, " lead"), ("Q", true, "\"quoted\""),
            ("EQ", true, "a=b"), ("noeq", false, ""), ("MISSING", false, ""),
        })
            (env.Present(key), env.Value(key)).ShouldBe((present, value), key);
        env.Count.ShouldBe(6);

        var files = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
        var root = Path.Combine(Server2PostgresWorld.Base, "u54-funcs");
        var separator = Path.DirectorySeparatorChar.ToString();
        foreach (var (path, inside) in new[]
        {
            (Path.Combine(root, "a", "b.env"), true), (root, false), (Path.Combine(root, "b.env"), true), (root + "x" + separator + "b.env", false),
            (root.ToUpperInvariant() + separator + "b.env", false),
            (Path.Combine(root, "a", "..", "..", "b.env"), false), (root.Replace('\\', '/') + "/b.env", true),
        })
            Server2PostgresDeploy.InsideRepo(files, path, root).ShouldBe(inside, path);
        Server2PostgresDeploy.InsideRepo(files, Path.Combine(root, "b.env"), root + separator).ShouldBeTrue();
    }

    [Test]
    public void Production_mode_check_reads_the_posix_bits()
    {
        var path = Path.Combine(Path.GetTempPath(), "card1406-mode-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "X=1\n");
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Server2PostgresDeploy.IsOwnerReadWriteOnly(path).ShouldBeFalse("Windows has no POSIX mode");
                return;
            }
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Server2PostgresDeploy.IsOwnerReadWriteOnly(path).ShouldBeTrue();
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            Server2PostgresDeploy.IsOwnerReadWriteOnly(path).ShouldBeFalse();
            File.SetUnixFileMode(path, UnixFileMode.UserRead);
            Server2PostgresDeploy.IsOwnerReadWriteOnly(path).ShouldBeFalse();
            Server2PostgresDeploy.IsOwnerReadWriteOnly(path + "-missing").ShouldBeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
