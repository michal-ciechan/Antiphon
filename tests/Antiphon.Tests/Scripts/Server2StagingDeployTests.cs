using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Server2ServerDeployWorld;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1742, unit 64 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the deploy-server2-staging-server command
/// (Antiphon.Nightly.Server2StagingDeploy), which replaced scripts/deploy-server2-staging-server.ps1, in process on a mock disk
/// with a recording launcher (Server2ServerDeployWorld). The lines, exits and tool calls are the script's, captured from it before
/// its deletion (pwsh on Windows with .cmd stubs of docker, hostname and curl alone on PATH, the script dot-sourced so that the
/// POSIX mode check could be answered).
/// </summary>
[Category("Unit")]
public sealed class Server2StagingDeployTests
{
    private const string PlanOk = "SERVER2-STAGING plan exit=0 reason=ok";
    private const string Ok = "SERVER2-STAGING ok exit=0 reason=ok";
    private const string Info = "docker info --format {{.Name}}";
    private const string Health = "curl -fsS --max-time 5 http://127.0.0.1:17420/health";
    private const string Version = "curl -fsS --max-time 5 http://127.0.0.1:17420/api/version";

    private static string[] Plan(string envArg, string composePath) =>
    [
        "docker compose --env-file " + envArg + " -f " + composePath + " -p antiphon-server2-staging up -d",
        "acceptance: curl -fsS http://127.0.0.1:17420/health",
        "acceptance: curl -fsS http://127.0.0.1:17420/api/version",
        PlanOk,
    ];

    private static void Planned(Server2DeployRun run, string[] lines)
    {
        run.Exit.ShouldBe(0, run.ToString());
        run.Lines.ShouldBe(lines, run.ToString());
        run.Error.ShouldBeEmpty();
        run.Tools.ShouldBeEmpty("the plan calls no tool");
        run.Hides(StagingPassword).ShouldBeTrue(run.ToString());
    }

    private static void Refused(Server2DeployRun run, string reason, int exit = 2)
    {
        run.Exit.ShouldBe(exit, run.ToString());
        run.Lines.ShouldBe(["SERVER2-STAGING fail exit=" + exit + " reason=" + reason], run.ToString());
        run.Tools.ShouldBeEmpty("a refusal before the lane calls no tool");
        run.Hides(StagingPassword).ShouldBeTrue(run.ToString());
    }

    [Test]
    public void Options_take_the_scripts_parameters_in_kebab_case()
    {
        var options = Server2StagingDeployOptions.Parse([], "checkout");
        (options.WhatIf, options.Execute, options.Project, options.EnvFile, options.ComposeFile, options.RepoRoot, options.Checkout)
            .ShouldBe((false, false, "", "", "", "", "checkout"));
        options = Server2StagingDeployOptions.Parse(["--what-if", "--execute", "--project", " p ", "--env-file", "e", "--compose-file", "c", "--repo-root", "r"], null!);
        (options.WhatIf, options.Execute, options.Project, options.EnvFile, options.ComposeFile, options.RepoRoot, options.Checkout)
            .ShouldBe((true, true, " p ", "e", "c", "r", ""));
        Should.Throw<NightlyUsageException>(() => Server2StagingDeployOptions.Parse(["--expected-server-sha", "x"], "")).Message.ShouldBe("unknown flag --expected-server-sha");
        Server2StagingDeployOptions.Usage.ShouldBe("deploy-server2-staging-server [--what-if | --execute] [--project antiphon-server2-staging] [--env-file <file>]"
            + " [--compose-file <file>] [--repo-root <dir>]");
        File.Exists(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "deploy-server2-staging-server.ps1")).ShouldBeFalse();
    }

    [Test]
    public void Plan_prints_the_host_command_and_the_acceptance_urls()
    {
        var world = new Server2ServerDeployWorld();
        var plan = Plan("<env-file>", world.StagingCompose);
        Planned(world.Staging(), plan);
        Planned(world.Staging("--what-if"), plan);
        Planned(world.Staging("--what-if", "--project", " antiphon-server2-staging "), plan);
        var env = world.StagingEnv();
        Planned(world.Staging("--what-if", "--env-file", env), Plan(env, world.StagingCompose));
        Server2StagingDeploy.HostCommand("", "x.yml").Split('\n').Length.ShouldBe(3);
    }

    [Test]
    public void Mode_project_and_compose_refusals_are_the_scripts()
    {
        var world = new Server2ServerDeployWorld();
        Refused(world.Staging("--what-if", "--execute"), "ambiguous-mode", 3);
        Refused(world.Staging("--what-if", "--project", "antiphon-server2-server"), "staging-compose-wrong-project");
        Refused(world.Staging("--what-if", "--project", "ANTIPHON-SERVER2-STAGING"), "staging-compose-wrong-project");
        Refused(world.Staging("--what-if", "--compose-file", Path.Combine(Base, "nope.yml")), "missing-compose");
        Refused(world.Staging("--what-if", "--compose-file", world.ProductionCompose), "staging-compose-wrong-project");
        Refused(world.Staging("--what-if", "--compose-file", world.Compose("crlf.yml", StagingText.Replace("\n", "\r\n"))), "staging-compose-wrong-project");
        Refused(world.Staging("--what-if", "--compose-file", world.Compose("upper.yml", StagingText.Replace("name: antiphon-server2-staging", "NAME: ANTIPHON-SERVER2-STAGING"))),
            "compose-invalid");

        var directory = Path.Combine(Base, "compose-dir");
        world.Files.AddDirectory(directory);
        var run = world.Staging("--what-if", "--compose-file", directory);
        run.Lines.ShouldBe(["SERVER2-STAGING fail exit=2 reason=staging-compose-wrong-project"]);
        run.Error.Trim().Split('\n').Length.ShouldBe(1, run.Error);

        // The forbidden patterns ignore case (-match); 0.0.0.0 and codeperf.net are regexes with an escaped dot.
        foreach (var (needle, reason) in new[]
        {
            ("antiphon_pgdata", "volume-forbidden"), ("ANTIPHON_PGDATA", "volume-forbidden"), ("dind-data", "volume-forbidden"), ("antiphon-server2-postgres", "volume-forbidden"),
            ("windmill", "volume-forbidden"), ("WINDMILL", "volume-forbidden"), ("0.0.0.0", "bind-any"), ("0x0x0x0", ""), ("5433", "port-forbidden"), ("17280", "port-forbidden"),
            ("17202", "port-forbidden"), (":5432:", "port-forbidden"), ("PhoneHome__ServerOrigin", "phone-home-origin"), ("phonehome__serverorigin", "phone-home-origin"),
            ("codeperf." + "net", "phone-home-origin"), ("codeperfXnet", ""), ("CODEPERF.NET", "phone-home-origin"),
        })
        {
            var path = world.Compose("needle.yml", StagingText + "# " + needle + "\n");
            if (reason.Length == 0) Planned(world.Staging("--what-if", "--compose-file", path), Plan("<env-file>", path));
            else Refused(world.Staging("--what-if", "--compose-file", path), reason);
        }

        var index = 0;
        foreach (var need in Server2StagingDeploy.ComposeNeeds)
        {
            index++;
            var text = StagingText.Replace(need, "X", StringComparison.Ordinal);
            text.ShouldNotBe(StagingText, need);
            Refused(world.Staging("--what-if", "--compose-file", world.Compose("need.yml", text)), index == 1 ? "staging-compose-wrong-project" : "compose-invalid");
        }
        Server2StagingDeploy.ComposeNeeds.Count.ShouldBe(32);
        Server2StagingDeploy.ComposeRefusal(StagingText).ShouldBe("");
    }

    [Test]
    public void Env_refusals_and_image_rules_are_the_scripts()
    {
        var world = new Server2ServerDeployWorld();
        Refused(world.Staging("--what-if", "--env-file", Path.Combine(world.Secrets, "nope.env")), "env-missing");
        var inside = Path.Combine(world.Root, "in.env");
        world.Files.AddFile(inside, new System.IO.Abstractions.TestingHelpers.MockFileData("X=1\n"));
        world.OwnerOnly.Add(inside);
        Refused(world.Staging("--what-if", "--env-file", inside), "env-inside-repo");
        Refused(world.Staging("--what-if", "--env-file", world.StagingEnv("mode.env", ownerOnly: false)), "env-mode");
        Refused(world.Staging("--execute"), "env-missing");
        world.Variables["OS"] = "Windows_NT";
        Refused(world.Staging("--what-if", "--env-file", world.StagingEnv()), "env-mode");
        world.Variables.Remove("OS");

        var directory = Path.Combine(world.Secrets, "dir.env");
        world.Files.AddDirectory(directory);
        world.OwnerOnly.Add(directory);
        var run = world.Staging("--what-if", "--env-file", directory);
        run.Lines.ShouldBe(["SERVER2-STAGING fail exit=2 reason=empty-image"]);
        run.Error.Trim().Split('\n').Length.ShouldBe(1, run.Error);

        var a64 = new string('a', 64);
        var i = 0;
        foreach (var (image, reason) in new[]
        {
            ("", "empty-image"), ("  ", "empty-image"), ("postgres:16", "image-floating"), ("postgres:16-alpine@sha256:" + new string('b', 64), "image-alpine"),
            // -match ignores case for both digest forms.
            ("POSTGRES:16@SHA256:" + a64, ""), ("postgres@sha256:" + new string('c', 64), ""), ("postgres:16@sha256:" + a64, ""), ("postgres:16@sha256:" + new string('A', 64), ""),
            // No patch tag here (the CARD-1406 deploy accepts one; staging never did).
            ("postgres:16.14@sha256:" + new string('9', 64), "image-floating"), (" postgres:16@sha256:" + a64, "image-floating"), ("postgres:16@sha256:" + a64 + "x", "image-floating"),
            ("Alpine", "image-alpine"), ("postgres:16@sha256:" + new string('a', 63), "image-floating"),
        })
        {
            var path = world.StagingEnv("img" + i++ + ".env", new() { ["ANTIPHON_STAGING_POSTGRES_IMAGE"] = image });
            if (reason.Length == 0) Planned(world.Staging("--what-if", "--env-file", path), Plan(path, world.StagingCompose));
            else Refused(world.Staging("--what-if", "--env-file", path), reason);
            Server2StagingDeploy.Image(image).ShouldBe(reason, image);
        }
        Refused(world.Staging("--what-if", "--env-file", world.StagingEnv("noimg.env", drop: ["ANTIPHON_STAGING_POSTGRES_IMAGE"])), "empty-image");
        Refused(world.Staging("--what-if", "--env-file", world.StagingEnv("pw.env", new() { ["POSTGRES_PASSWORD"] = "" })), "empty-password");
        var dev = world.Staging("--what-if", "--env-file", world.StagingEnv("dev.env", new() { ["POSTGRES_PASSWORD"] = DevPassword }));
        Refused(dev, "dev-password");
        dev.Hides(DevPassword).ShouldBeTrue();
    }

    [Test]
    public void Execute_refuses_off_the_server2_host()
    {
        var world = new Server2ServerDeployWorld { Daemon = "other" };
        var env = world.StagingEnv();
        var run = world.Staging("--execute", "--env-file", env);
        run.Lines.ShouldBe(["SERVER2-STAGING fail exit=2 reason=not-server2-host"]);
        run.Tools.ShouldBe([Info, "hostname"]);
        world.Daemon = "server2";
        world.Tools.Remove("docker");
        run = world.Staging("--execute", "--env-file", env);
        run.Lines.ShouldBe(["SERVER2-STAGING fail exit=2 reason=not-server2-host"]);
        run.Tools.ShouldBeEmpty();
    }

    [Test]
    public void Execute_starts_the_project_then_accepts_health_and_version()
    {
        var world = new Server2ServerDeployWorld();
        var env = world.StagingEnv();
        var up = "docker compose --env-file " + env + " -f " + world.StagingCompose + " -p antiphon-server2-staging up -d";
        var run = world.Staging("--execute", "--env-file", env);
        run.Exit.ShouldBe(0, run.ToString());
        run.Lines.ShouldBe([Ok]);
        run.Tools.ShouldBe([Info, "hostname", up, Health, Version]);
        run.Captured.ShouldAllBe(captured => captured);
        run.Hides(StagingPassword).ShouldBeTrue();

        // x-up-fails.
        var failing = new Server2ServerDeployWorld { ComposeExit = 1 };
        run = failing.Staging("--execute", "--env-file", failing.StagingEnv());
        run.Lines.ShouldBe(["SERVER2-STAGING fail exit=2 reason=compose-up-failed"]);
        run.Tools.Length.ShouldBe(3);

        // x-health-then-ok: /health retried every 2 s.
        var slow = new Server2ServerDeployWorld { HealthExits = [22, 22, 0] };
        var start = slow.Clock.GetUtcNow();
        run = slow.Staging("--execute", "--env-file", slow.StagingEnv());
        run.Lines.ShouldBe([Ok]);
        run.Tools.Skip(3).ShouldBe([Health, Health, Health, Version]);
        (slow.Clock.GetUtcNow() - start).ShouldBe(TimeSpan.FromSeconds(4));

        // x-version-fails.
        var version = new Server2ServerDeployWorld { VersionExit = 22 };
        version.Staging("--execute", "--env-file", version.StagingEnv()).Lines.ShouldBe(["SERVER2-STAGING fail exit=2 reason=acceptance-version"]);

        // x-no-curl.
        var nocurl = new Server2ServerDeployWorld();
        nocurl.Tools.Remove("curl");
        run = nocurl.Staging("--execute", "--env-file", nocurl.StagingEnv());
        run.Lines.ShouldBe(["SERVER2-STAGING fail exit=2 reason=acceptance-curl-missing"]);
        run.Tools.Length.ShouldBe(3);

        // x-health-never: 30 calls while under 60 s from the first (the script made 30 too), then acceptance-health; no version call.
        var never = new Server2ServerDeployWorld { HealthExits = [22] };
        run = never.Staging("--execute", "--env-file", never.StagingEnv());
        run.Lines.ShouldBe(["SERVER2-STAGING fail exit=2 reason=acceptance-health"]);
        run.Tools.Skip(3).ShouldAllBe(tool => tool == Health);
        run.Tools.Length.ShouldBe(33);
    }

    [Test]
    public void Contract_literals_live_in_the_command()
    {
        Server2StagingDeploy.Project.ShouldBe("antiphon-server2-staging");
        Server2StagingDeploy.HealthUrl.ShouldBe("http://127.0.0.1:17420/health");
        Server2StagingDeploy.VersionUrl.ShouldBe("http://127.0.0.1:17420/api/version");
        (Server2StagingDeploy.AcceptanceBound, Server2StagingDeploy.AcceptanceRetry).ShouldBe((TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(2)));
    }
}
