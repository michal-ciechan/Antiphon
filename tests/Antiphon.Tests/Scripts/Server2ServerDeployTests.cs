using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Server2ServerDeployWorld;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1775, unit 64 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the deploy-server2-server command
/// (Antiphon.Nightly.Server2ServerDeploy), which replaced scripts/deploy-server2-server.ps1, in process on a mock disk with a
/// recording launcher (Server2ServerDeployWorld). The lines, exits and tool calls are the script's, captured from it before its
/// deletion (pwsh on Windows with .cmd stubs of docker, hostname, git and pwsh alone on PATH, the script dot-sourced so that the
/// POSIX mode check could be answered, because Windows always refuses env-mode). The verify-only gate is now the restart-linux-server
/// command in this process: where the script started pwsh on scripts/restart-linux-server.ps1, these tests answer the gate's HTTP.
/// </summary>
[Category("Unit")]
public sealed class Server2ServerDeployTests
{
    private const string PlanOk = "SERVER2-SERVER plan exit=0 reason=ok";
    private const string Ok = "SERVER2-SERVER ok exit=0 reason=ok";
    private const string Info = "docker info --format {{.Name}}";

    private static string[] Plan(string context, string envArg, string composePath, string sha = "<expected-sha>") =>
    [
        "before up: SOURCE_REVISION in the env file and git rev-parse HEAD of " + context + " must equal " + sha,
        "docker compose --env-file " + envArg + " -f " + composePath + " -p antiphon-server2-server up -d --build",
        // The script printed pwsh -NoProfile -File scripts/restart-linux-server.ps1 -Config ... -ExpectedServerSha ...; the gate is a command now.
        "verify-only: scripts/antiphon restart-linux-server --config /var/tmp/antiphon-server2-server.gate.config --expected-server-sha " + sha,
        PlanOk,
    ];

    private static void Planned(Server2DeployRun run, string[] lines)
    {
        run.Exit.ShouldBe(0, run.ToString());
        run.Lines.ShouldBe(lines, run.ToString());
        run.Error.ShouldBeEmpty();
        run.Tools.ShouldBeEmpty("the plan calls no tool");
        run.Hides(Password).ShouldBeTrue(run.ToString());
        run.Hides(Secret).ShouldBeTrue(run.ToString());
    }

    private static void Refused(Server2DeployRun run, string reason, int exit = 2)
    {
        run.Exit.ShouldBe(exit, run.ToString());
        run.Lines.ShouldBe(["SERVER2-SERVER fail exit=" + exit + " reason=" + reason], run.ToString());
        run.Tools.ShouldBeEmpty("a refusal before the head check calls no tool");
        run.Hides(Password).ShouldBeTrue(run.ToString());
        run.Hides(Secret).ShouldBeTrue(run.ToString());
    }

    // ---- the options ----

    [Test]
    public void Options_take_the_scripts_parameters_in_kebab_case()
    {
        var options = Server2ServerDeployOptions.Parse([], "checkout");
        (options.WhatIf, options.Execute, options.Project, options.EnvFile, options.ComposeFile, options.RepoRoot, options.ExpectedServerSha, options.Checkout)
            .ShouldBe((false, false, "", "", "", "", "", "checkout"));
        options = Server2ServerDeployOptions.Parse(["--what-if", "--execute", "--project", " p ", "--env-file", "e", "--compose-file", "c", "--repo-root", "", "--expected-server-sha", "s"], null!);
        (options.WhatIf, options.Execute, options.Project, options.EnvFile, options.ComposeFile, options.RepoRoot, options.ExpectedServerSha, options.Checkout)
            .ShouldBe((true, true, " p ", "e", "c", "", "s", ""));
        Should.Throw<NightlyUsageException>(() => Server2ServerDeployOptions.Parse(["--bogus"], "")).Message.ShouldBe("unknown flag --bogus");
        Should.Throw<NightlyUsageException>(() => Server2ServerDeployOptions.Parse(["-WhatIf"], "")).Message.ShouldBe("unexpected argument '-WhatIf'");
        Should.Throw<NightlyUsageException>(() => Server2ServerDeployOptions.Parse(["--env-file"], "")).Message.ShouldBe("missing value for --env-file");
        Server2ServerDeployOptions.Usage.ShouldBe("deploy-server2-server [--what-if | --execute] [--project antiphon-server2-server] [--env-file <file>]"
            + " [--compose-file <file>] [--repo-root <dir>] [--expected-server-sha <sha>]");
        File.Exists(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "deploy-server2-server.ps1")).ShouldBeFalse();
    }

    // ---- the plan ----

    [Test]
    public void Plan_prints_the_revision_rule_the_host_command_and_the_gate()
    {
        var world = new Server2ServerDeployWorld();
        var plan = Plan(world.Root, "<env-file>", world.ProductionCompose);
        Planned(world.Production(), plan);
        Planned(world.Production("--what-if"), plan);
        Planned(world.Production("--what-if", "--project", "  antiphon-server2-server  "), plan);
        Planned(world.Production("--what-if", "--project", "   "), plan);
        Planned(world.Production("--what-if", "--expected-server-sha", "   ", "--env-file", "   "), plan);
        // The SHA is printed as given; the env file's path is the --env-file argument.
        Planned(world.Production("--what-if", "--expected-server-sha", "ABC"), Plan(world.Root, "<env-file>", world.ProductionCompose, "ABC"));
        var env = world.ProductionEnv();
        Planned(world.Production("--what-if", "--env-file", env), Plan(world.Root, env, world.ProductionCompose));
        // The build context is the compose file's directory; another root holds the compose the plan names.
        var other = Path.Combine(Base, "other");
        world.Files.AddFile(Path.Combine(other, Server2ServerDeploy.ComposeName), new System.IO.Abstractions.TestingHelpers.MockFileData(ProductionText));
        Planned(world.Production("--repo-root", other), Plan(other, "<env-file>", Path.Combine(other, Server2ServerDeploy.ComposeName)));
        var outside = world.Compose("prod.yml", ProductionText);
        Planned(world.Production("--compose-file", outside), Plan(Base, "<env-file>", outside));
        // The host command is one Write-Output: three lines joined by "\n".
        Server2ServerDeploy.HostCommand(world.Files, "", "/r/docker-compose.server2-server.yml", "").Split('\n').Length.ShouldBe(3);
    }

    // ---- the refusals before any tool ----

    [Test]
    public void Mode_project_and_compose_file_refusals_are_the_scripts()
    {
        var world = new Server2ServerDeployWorld();
        Refused(world.Production("--what-if", "--execute"), "ambiguous-mode", 3);
        Refused(world.Production("--what-if", "--execute", "--project", "nope", "--compose-file", Path.Combine(Base, "nowhere.yml")), "ambiguous-mode", 3);
        Refused(world.Production("--what-if", "--project", "antiphon-runner"), "production-compose-wrong-project");
        // -cne: the project is compared case-sensitively.
        Refused(world.Production("--what-if", "--project", "Antiphon-Server2-Server"), "production-compose-wrong-project");
        Refused(world.Production("--what-if", "--compose-file", Path.Combine(Base, "nope.yml")), "missing-compose");
        Refused(world.Production("--what-if", "--repo-root", world.Secrets), "missing-compose");
        Refused(world.Production("--what-if", "--compose-file", world.StagingCompose), "production-compose-wrong-project");
        // A CRLF file has no "name: antiphon-server2-server" line under (?m)^...$ (a Windows autocrlf checkout refused the same way).
        Refused(world.Production("--what-if", "--compose-file", world.Compose("crlf.yml", ProductionText.Replace("\n", "\r\n"))), "production-compose-wrong-project");
        // -notmatch ignores case, the needs do not.
        Refused(world.Production("--what-if", "--compose-file", world.Compose("upper.yml", ProductionText.Replace("name: antiphon-server2-server", "NAME: ANTIPHON-SERVER2-SERVER"))),
            "compose-invalid");
        Planned(world.Production("--what-if", "--compose-file", world.Compose("indent.yml", "  " + ProductionText)), Plan(Base, "<env-file>", Path.Combine(Base, "indent.yml")));
    }

    [Test]
    public void Unreadable_compose_reads_as_empty_with_one_error_line()
    {
        // compose-dir: Test-Path passed, ReadAllText failed with an error record, and the empty text had no project line.
        var world = new Server2ServerDeployWorld();
        var directory = Path.Combine(Base, "compose-dir");
        world.Files.AddDirectory(directory);
        var run = world.Production("--what-if", "--compose-file", directory);
        run.Exit.ShouldBe(2, run.ToString());
        run.Lines.ShouldBe(["SERVER2-SERVER fail exit=2 reason=production-compose-wrong-project"]);
        run.Error.Trim().Split('\n').Length.ShouldBe(1, run.Error);
    }

    [Test]
    public void Compose_literal_refusals_are_the_scripts()
    {
        var world = new Server2ServerDeployWorld();
        // The needles in the script's order; .Contains is ordinal, so Windmill and CODEPERF.NET pass.
        foreach (var (needle, reason) in new[]
        {
            ("antiphon_pgdata", "volume-forbidden"), ("dind-data", "volume-forbidden"), ("windmill", "volume-forbidden"), ("Windmill", ""), ("0.0.0.0", "bind-any"),
            ("17420", "port-forbidden"), ("17280", "port-forbidden"), ("17202", "port-forbidden"), ("5433", "port-forbidden"), (":5432:", "port-forbidden"),
            ("codeperf." + "net", "phone-home-origin"), ("CODEPERF.NET", ""), ("privileged:", "privileged"), ("docker.sock", "socket"),
        })
        {
            var path = world.Compose("needle.yml", ProductionText + "# " + needle + "\n");
            if (reason.Length == 0) Planned(world.Production("--what-if", "--compose-file", path), Plan(Base, "<env-file>", path));
            else Refused(world.Production("--what-if", "--compose-file", path), reason);
        }

        // Each need removed: compose-invalid, except the two whose removal also removes the project line (need-1, need-3).
        var index = 0;
        foreach (var need in Server2ServerDeploy.ComposeNeeds)
        {
            index++;
            var text = ProductionText.Replace(need, "X", StringComparison.Ordinal);
            text.ShouldNotBe(ProductionText, need);
            var expected = index == 1 || index == 3 ? "production-compose-wrong-project" : "compose-invalid";
            Refused(world.Production("--what-if", "--compose-file", world.Compose("need.yml", text)), expected);
        }
        Server2ServerDeploy.ComposeNeeds.Count.ShouldBe(54);
        Server2ServerDeploy.ComposeRefusal(ProductionText).ShouldBe("");
    }

    [Test]
    public void Env_file_refusals_are_the_scripts()
    {
        var world = new Server2ServerDeployWorld();
        Refused(world.Production("--what-if", "--env-file", Path.Combine(world.Secrets, "nope.env")), "env-missing");
        Refused(world.Production("--what-if", "--env-file", "relative.env"), "env-missing");
        var inside = Path.Combine(world.Root, "in.env");
        world.Files.AddFile(inside, new System.IO.Abstractions.TestingHelpers.MockFileData("X=1\n"));
        world.OwnerOnly.Add(inside);
        Refused(world.Production("--what-if", "--env-file", inside), "env-inside-repo");
        Refused(world.Production("--what-if", "--env-file", world.ProductionEnv("mode.env", ownerOnly: false)), "env-mode");
        Refused(world.Production("--execute"), "env-missing");
        // $env:OS -eq 'Windows_NT' (ignoring case): always env-mode.
        var good = world.ProductionEnv();
        foreach (var os in new[] { "Windows_NT", "windows_nt" })
        {
            world.Variables["OS"] = os;
            Refused(world.Production("--what-if", "--env-file", good), "env-mode");
        }
        world.Variables["OS"] = "Linux";
        Planned(world.Production("--what-if", "--env-file", good), Plan(world.Root, good, world.ProductionCompose));

        foreach (var (name, path, reason) in new[]
        {
            ("pw-empty", world.ProductionEnv("a.env", new() { ["POSTGRES_PASSWORD"] = "   " }), "empty-password"),
            ("pw-missing", world.ProductionEnv("b.env", drop: ["POSTGRES_PASSWORD"]), "empty-password"),
            ("pw-dev", world.ProductionEnv("c.env", new() { ["POSTGRES_PASSWORD"] = DevPassword }), "dev-password"),
            ("storage-Redis", world.ProductionEnv("d.env", new() { ["HANGFIRE_STORAGE"] = "Redis" }), "hangfire-storage"),
            // -cne: inmemory is not InMemory.
            ("storage-inmemory", world.ProductionEnv("e.env", new() { ["HANGFIRE_STORAGE"] = "inmemory" }), "hangfire-storage"),
            ("no-dw-secret", world.ProductionEnv("f.env", drop: ["PHONE_HOME_DESKTOP_WIN_SHARED_SECRET"]), "env-missing"),
            ("no-dw-home", world.ProductionEnv("g.env", new() { ["PHONE_HOME_DESKTOP_WIN_HOME"] = " " }), "env-missing"),
        })
        {
            var run = world.Production("--what-if", "--env-file", path);
            Refused(run, reason);
            run.Hides(DevPassword).ShouldBeTrue(name);
        }

        // Accepted: the dev password differs by case or by a leading space; the storage is PostgreSql, blank, trimmed or absent;
        // the keys ignore case; comments, blanks and malformed lines are skipped; the --what-if plan needs none of the execute keys.
        foreach (var path in new[]
        {
            world.ProductionEnv("h.env", new() { ["POSTGRES_PASSWORD"] = DevPassword.ToUpperInvariant() }),
            world.ProductionEnv("i.env", new() { ["POSTGRES_PASSWORD"] = " " + DevPassword }),
            world.ProductionEnv("j.env", new() { ["HANGFIRE_STORAGE"] = "PostgreSql" }),
            world.ProductionEnv("k.env", new() { ["HANGFIRE_STORAGE"] = "" }),
            world.ProductionEnv("l.env", new() { ["HANGFIRE_STORAGE"] = "  " }),
            world.ProductionEnv("m.env", new() { ["HANGFIRE_STORAGE"] = "InMemory " }),
            world.ProductionEnv("n.env", drop: ["HANGFIRE_STORAGE"]),
            world.EnvLines("o.env", world.Files.File.ReadAllLines(good).Select(line => line.Split('=', 2)[0].ToLowerInvariant() + "=" + line.Split('=', 2)[1])),
            world.EnvLines("p.env", new[] { "# c", "", "=x", "noeq" }.Concat(world.Files.File.ReadAllLines(good))),
            world.ProductionEnv("q.env", drop: ["ANTIPHON_PUBLIC_ORIGIN", "PHONE_HOME_SHARED_SECRET", "KEYRING_CERT_FILE", "SOURCE_REVISION"]),
        })
            Planned(world.Production("--what-if", "--env-file", path), Plan(world.Root, path, world.ProductionCompose));

        // Without docker-compose.dev.yml there is no dev check.
        world.Files.RemoveFile(Path.Combine(world.Root, "docker-compose.dev.yml"));
        var dev = world.ProductionEnv("r.env", new() { ["POSTGRES_PASSWORD"] = DevPassword });
        Planned(world.Production("--what-if", "--env-file", dev), Plan(world.Root, dev, world.ProductionCompose));
    }

    [Test]
    public void Unreadable_env_file_reads_as_empty_with_one_error_line()
    {
        // env-dir: ReadAllLines failed inside Read-ServerEnv; the empty map refused the password.
        var world = new Server2ServerDeployWorld();
        var directory = Path.Combine(world.Secrets, "dir.env");
        world.Files.AddDirectory(directory);
        world.OwnerOnly.Add(directory);
        var run = world.Production("--what-if", "--env-file", directory);
        run.Lines.ShouldBe(["SERVER2-SERVER fail exit=2 reason=empty-password"]);
        run.Error.Trim().Split('\n').Length.ShouldBe(1, run.Error);
    }

    // ---- --execute ----

    [Test]
    public void Execute_env_and_sha_refusals_are_the_scripts()
    {
        var world = new Server2ServerDeployWorld();
        var keys = Server2ServerDeploy.ExecuteEnvKeys.Concat(Server2ServerDeploy.DesktopWinEnvKeys).ToArray();
        keys.ShouldBe(["ANTIPHON_PUBLIC_ORIGIN", "PHONE_HOME_SHARED_SECRET", "PHONE_HOME_HOST_WORKSPACE_ROOT", "KEYRING_CERT_FILE", "KEYRING_KEY_FILE", "OPERATOR_TOKEN_FILE",
            "PHONE_HOME_DESKTOP_WIN_SHARED_SECRET", "PHONE_HOME_DESKTOP_WIN_HOME"]);
        foreach (var key in Server2ServerDeploy.ExecuteEnvKeys)
            Refused(world.Production("--execute", "--expected-server-sha", Head, "--env-file", world.ProductionEnv("x-" + key + ".env", drop: [key])), "env-missing");
        foreach (var key in new[] { "KEYRING_CERT_FILE", "KEYRING_KEY_FILE", "OPERATOR_TOKEN_FILE" })
            Refused(world.Production("--execute", "--expected-server-sha", Head, "--env-file", world.ProductionEnv("c-" + key + ".env", new() { [key] = Path.Combine(world.Secrets, "absent.pem") })),
                "missing-certificate");
        var good = world.ProductionEnv();
        foreach (var sha in new[] { "", "abc", Head + "g" })
            Refused(world.Production("--execute", "--expected-server-sha", sha, "--env-file", good), "expected-sha");
        // A 64-character SHA is a SHA; the env's revision is the 40-character one.
        Refused(world.Production("--execute", "--expected-server-sha", new string('d', 64), "--env-file", good), "revision-mismatch");
        Refused(world.Production("--execute", "--expected-server-sha", Head, "--env-file", world.ProductionEnv("rev1.env", drop: ["SOURCE_REVISION"])), "revision-mismatch");
        Refused(world.Production("--execute", "--expected-server-sha", Head, "--env-file", world.ProductionEnv("rev2.env", new() { ["SOURCE_REVISION"] = new string('e', 40) })), "revision-mismatch");
    }

    [Test]
    public void Execute_checks_the_build_context_head_before_docker()
    {
        var world = new Server2ServerDeployWorld();
        var good = world.ProductionEnv();
        var git = "git -C " + world.Root + " rev-parse --verify HEAD";
        foreach (var (head, exit, reason) in new[] { (Head, 128, "head-unknown"), ("fatal-not-a-sha", 0, "head-unknown"), ("", 0, "head-unknown"), (new string('e', 40), 0, "head-mismatch") })
        {
            world.GitHead = head;
            world.GitExit = exit;
            var run = world.Production("--execute", "--expected-server-sha", Head, "--env-file", good);
            run.Exit.ShouldBe(2, run.ToString());
            run.Lines.ShouldBe(["SERVER2-SERVER fail exit=2 reason=" + reason]);
            run.Tools.ShouldBe([git]);
            // git's stderr is discarded (2>$null).
            run.Error.ShouldBeEmpty();
        }
        world.GitHead = Head;
        world.GitExit = 0;
        world.Tools.Remove("git");
        var none = world.Production("--execute", "--expected-server-sha", Head, "--env-file", good);
        none.Lines.ShouldBe(["SERVER2-SERVER fail exit=2 reason=head-unknown"]);
        none.Tools.ShouldBeEmpty();
    }

    [Test]
    public void Execute_refuses_off_the_server2_host()
    {
        var world = new Server2ServerDeployWorld();
        var good = world.ProductionEnv();
        var git = "git -C " + world.Root + " rev-parse --verify HEAD";
        foreach (var daemon in new[] { "other-host", null })
        {
            world.Daemon = daemon;
            var run = world.Production("--execute", "--expected-server-sha", Head, "--env-file", good);
            run.Lines.ShouldBe(["SERVER2-SERVER fail exit=2 reason=not-server2-host"]);
            run.Tools.ShouldBe([git, Info, "hostname"]);
        }
        world.Daemon = "server2";
        world.Files.AddFile("/.dockerenv", new System.IO.Abstractions.TestingHelpers.MockFileData(""));
        var nested = world.Production("--execute", "--expected-server-sha", Head, "--env-file", good);
        nested.Lines.ShouldBe(["SERVER2-SERVER fail exit=2 reason=not-server2-host"]);
        nested.Tools.ShouldBe([git]);
        world.Files.RemoveFile("/.dockerenv");
        world.Tools.Remove("docker");
        world.Production("--execute", "--expected-server-sha", Head, "--env-file", good).Tools.ShouldBe([git]);
        world.Tools["docker"] = Path.Combine(Base, "bin", "docker");
        world.Tools.Remove("hostname");
        var nameless = world.Production("--execute", "--expected-server-sha", Head, "--env-file", good);
        nameless.Lines.ShouldBe(["SERVER2-SERVER fail exit=2 reason=not-server2-host"]);
        nameless.Tools.ShouldBe([git, Info]);
        nameless.Error.ShouldContain("'hostname' is not recognized");
    }

    [Test]
    public void Execute_on_the_host_builds_then_runs_the_gate_verify_only()
    {
        var world = new Server2ServerDeployWorld();
        var good = world.ProductionEnv();
        var up = "docker compose --env-file " + good + " -f " + world.ProductionCompose + " -p antiphon-server2-server up -d --build";
        var run = world.Production("--execute", "--expected-server-sha", Head, "--env-file", good);
        run.Exit.ShouldBe(0, run.ToString());
        run.Lines.ShouldBe([Ok]);
        run.Error.ShouldBeEmpty();
        run.Tools.ShouldBe(["git -C " + world.Root + " rev-parse --verify HEAD", Info, "hostname", up]);
        // Compose's output is captured and not printed: it can repeat an interpolated secret.
        run.Captured.ShouldAllBe(captured => captured);
        run.Hides(Secret).ShouldBeTrue(run.ToString());
        run.Hides(Password).ShouldBeTrue(run.ToString());
        // The gate config, mode 0600 off Windows, then one verify-only probe: no docker, no --execute.
        world.Files.File.ReadAllText(Server2ServerDeploy.GateConfig)
            .ShouldBe("baseUrl=http://127.0.0.1:17400\ncontainer=antiphon-server2-server-server-1\nlockPath=/var/tmp/antiphon-server2-server.restart.lock\n");
        world.Chmods.ShouldBe([Server2ServerDeploy.GateConfig]);
        run.Gate.ShouldBe(["http://127.0.0.1:17400/health", "http://127.0.0.1:17400/api/version"]);

        // x-daemon-case (-ne ignores case), x-head-upper (the head is lowered), x-revision-upper-spaced, x-sha-upper.
        world.Daemon = "Server2";
        world.GitHead = Head.ToUpperInvariant();
        var upper = world.ProductionEnv("upper.env", new() { ["SOURCE_REVISION"] = "  " + Head.ToUpperInvariant() + "  " });
        var mixed = world.Production("--execute", "--expected-server-sha", Head.ToUpperInvariant(), "--env-file", upper);
        mixed.Lines.ShouldBe([Ok], mixed.ToString());
        // The gate was asked for the SHA as given (the script passed -ExpectedServerSha unchanged).
        mixed.Gate.Length.ShouldBe(2);

        // x-cert-dir: a certificate path that is a directory passes Test-Path.
        var dirCert = new Server2ServerDeployWorld();
        dirCert.Production("--execute", "--expected-server-sha", Head, "--env-file", dirCert.ProductionEnv("dir-cert.env", new() { ["KEYRING_CERT_FILE"] = dirCert.Secrets }))
            .Lines.ShouldBe([Ok]);
    }

    [Test]
    public void Execute_compose_failure_stops_before_the_gate()
    {
        var world = new Server2ServerDeployWorld { ComposeExit = 1 };
        var run = world.Production("--execute", "--expected-server-sha", Head, "--env-file", world.ProductionEnv());
        run.Lines.ShouldBe(["SERVER2-SERVER fail exit=2 reason=compose-up-failed"]);
        run.Tools.Length.ShouldBe(4);
        run.Gate.ShouldBeEmpty();
        run.Hides(Secret).ShouldBeTrue();
        world.Files.File.Exists(Server2ServerDeploy.GateConfig).ShouldBeFalse();
    }

    [Test]
    public void Gate_retries_health_down_for_sixty_seconds()
    {
        // x-gate-1-then-0: the gate exits 1 (health down) twice, then 0, two seconds apart.
        var world = new Server2ServerDeployWorld { GateHealth = [503, 503, 200] };
        var start = world.Clock.GetUtcNow();
        var run = world.Production("--execute", "--expected-server-sha", Head, "--env-file", world.ProductionEnv());
        run.Lines.ShouldBe([Ok], run.ToString());
        run.Gate.ShouldBe(["http://127.0.0.1:17400/health", "http://127.0.0.1:17400/health", "http://127.0.0.1:17400/health", "http://127.0.0.1:17400/api/version"]);
        (world.Clock.GetUtcNow() - start).ShouldBe(TimeSpan.FromSeconds(4));

        // x-gate-1-always: retried every 2 s until 60 s from the first call (31 calls on a clock that does not move between them;
        // the script's real-time loop made 30), then gate-unverified.
        var down = new Server2ServerDeployWorld { GateHealth = [503] };
        start = down.Clock.GetUtcNow();
        run = down.Production("--execute", "--expected-server-sha", Head, "--env-file", down.ProductionEnv());
        run.Lines.ShouldBe(["SERVER2-SERVER fail exit=2 reason=gate-unverified"]);
        run.Gate.Length.ShouldBe(31);
        run.Gate.ShouldAllBe(url => url == "http://127.0.0.1:17400/health");
        (down.Clock.GetUtcNow() - start).ShouldBe(TimeSpan.FromSeconds(60));
        // The what-if gate never writes the lock for health down.
        down.Files.File.Exists(Server2ServerDeploy.GateLock).ShouldBeFalse();
    }

    [Test]
    public void Gate_unverified_is_any_other_exit()
    {
        // x-gate-5: the server reports another SHA: the gate writes its lock and exits 5; no retry.
        var mismatch = new Server2ServerDeployWorld { GateVersion = "{\"version\":\"" + new string('e', 40) + "\"}" };
        var run = mismatch.Production("--execute", "--expected-server-sha", Head, "--env-file", mismatch.ProductionEnv());
        run.Lines.ShouldBe(["SERVER2-SERVER fail exit=2 reason=gate-unverified"]);
        run.Gate.Length.ShouldBe(2);
        mismatch.Files.File.ReadAllText(Server2ServerDeploy.GateLock).ShouldStartWith("reason=mismatch" + Environment.NewLine + "expected=" + Head + Environment.NewLine);
        // The gate's own lines were discarded, as the script read its child's output and dropped it.
        run.Out.ShouldNotContain("REFUSED");

        // x-gate-3: a fresh lock refuses the gate (exit 3) before any probe.
        var locked = new Server2ServerDeployWorld();
        locked.Files.AddFile(Server2ServerDeploy.GateLock, new System.IO.Abstractions.TestingHelpers.MockFileData("reason=old\n"));
        locked.Files.File.SetLastWriteTimeUtc(Server2ServerDeploy.GateLock, locked.Clock.GetUtcNow().UtcDateTime);
        run = locked.Production("--execute", "--expected-server-sha", Head, "--env-file", locked.ProductionEnv());
        run.Lines.ShouldBe(["SERVER2-SERVER fail exit=2 reason=gate-unverified"]);
        run.Gate.ShouldBeEmpty();
    }

    [Test]
    public void Gate_config_that_cannot_be_written_is_one_error_line_and_the_gate_still_runs()
    {
        // x-gate-config-unwritable: WriteAllText failed with an error record and the script went on; the gate then found no config
        // (exit 3 here: the gate's own refusal) and the deploy refused gate-unverified.
        var world = new Server2ServerDeployWorld();
        world.Files.Directory.Delete("/var/tmp");
        var run = world.Production("--execute", "--expected-server-sha", Head, "--env-file", world.ProductionEnv());
        run.Lines.ShouldBe(["SERVER2-SERVER fail exit=2 reason=gate-unverified"]);
        // The write's error line and the mode call's (the file is not there).
        run.Error.Trim().Split('\n').Length.ShouldBe(2, run.Error);
        run.Gate.ShouldBeEmpty();
    }

    [Test]
    public void Contract_literals_live_in_the_command()
    {
        var source = DockerStackDocuments.Read("scripts/lib/Antiphon.Nightly/Server2ServerDeploy.cs");
        source.Any(ch => ch > 127).ShouldBeFalse();
        source.ShouldNotContain("codeperf." + "net");
        Server2ServerDeploy.Project.ShouldBe("antiphon-server2-server");
        Server2ServerDeploy.Container.ShouldBe("antiphon-server2-server-server-1");
        Server2ServerDeploy.GateBaseUrl.ShouldBe("http://127.0.0.1:17400");
        Server2ServerDeploy.GateConfig.ShouldBe("/var/tmp/antiphon-server2-server.gate.config");
        Server2ServerDeploy.GateLock.ShouldBe("/var/tmp/antiphon-server2-server.restart.lock");
        (Server2ServerDeploy.GateBound, Server2ServerDeploy.GateRetry).ShouldBe((TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(2)));
    }
}
