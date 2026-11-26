using Antiphon.Nightly;
using Antiphon.Tests.Scripts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1775. The production server compose is a host-daemon file with a direct
/// session-runner sidecar. These witnesses read it and run the deploy command in
/// plan or refusal mode, in process (unit 64 of 2026-11-22-remaining-ps1-to-csharp.md
/// made deploy-server2-server.ps1 the deploy-server2-server command; the plan,
/// refusal and compose drain witnesses ran the script under pwsh with a Unix fake
/// docker and now run on every platform through Server2ServerDeployWorld). They do
/// not start a daemon, a container, or a database.
/// </summary>
[Category("Unit")]
public sealed class Server2ProductionServerContractTests
{
    private const string ComposePath = "docker-compose.server2-server.yml";
    private const string ScriptPath = "scripts/lib/Antiphon.Nightly/Server2ServerDeploy.cs";

    [Test]
    public void Production_compose_is_its_own_project()
    {
        var text = Compose();
        text.ShouldContain("name: antiphon-server2-server");
        text.ShouldNotContain("name: antiphon-runner");
        text.ShouldNotContain("name: antiphon-server2-staging");
        text.ShouldNotContain("name: antiphon\n");
        text.ShouldNotContain("privileged:");
        text.ShouldNotContain("docker.sock");
        text.ShouldNotContain("antiphon_pgdata");
        text.ShouldNotContain("dind-data");
        text.ShouldNotContain("windmill");
        text.ShouldNotContain("codex-home");
        text.ShouldContain("name: antiphon-server2-server_work");
        text.ShouldContain("name: antiphon-server2-server_server-state");
        text.ShouldContain("name: antiphon-server2-server_runner-state");
        text.ShouldNotContain("antiphon-server2-staging_");
        text.ShouldNotContain("antiphon-server2-postgres_pgdata");
        Service("server").ShouldNotContain("privileged:");
        Service("server-session-runner").ShouldNotContain("privileged:");
        Service("server-state-init").ShouldNotContain("privileged:");
    }

    [Test]
    public void Production_joins_external_postgres_on_loopback_17400()
    {
        var text = Compose();
        text.ShouldContain("external: true");
        text.ShouldContain("name: antiphon-server2");
        text.ShouldNotContain("  postgres:");
        text.ShouldNotContain("image: postgres");
        text.ShouldNotContain(":5432:");
        text.ShouldNotContain("5433");
        text.ShouldNotContain("17280");
        text.ShouldNotContain("17202");
        text.ShouldNotContain("17420");
        text.ShouldNotContain("0.0.0.0");
        var server = Service("server");
        server.ShouldContain("127.0.0.1:17400:8080");
        server.ShouldContain("Host=postgres;Port=5432;Database=antiphon;Username=antiphon;Password=${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}");
        server.ShouldContain("- antiphon-server2");
        server.ShouldContain("- server-internal");
        server.ShouldNotContain("localhost");
        server.ShouldNotContain("antiphon_dev");
        Service("server-session-runner").ShouldNotContain("ports:");
        Service("server-session-runner").ShouldNotContain("antiphon-server2");
    }

    [Test]
    public void Production_sidecar_shares_work_and_runner_state_with_herdr_off()
    {
        var text = Compose();
        text.ShouldContain("  server-session-runner:");
        var sidecar = Service("server-session-runner");
        sidecar.ShouldContain("dockerfile: docker/session-runner-grok/Dockerfile");
        sidecar.ShouldContain("target: runtime");
        sidecar.ShouldContain("user: \"1654:1654\"");
        Env(sidecar, "SessionRunner__Herdr__Enabled").ShouldBe("false");
        Env(sidecar, "PhoneHome__Enabled").ShouldBe("false");
        Env(sidecar, "SessionRunner__SessionLogPath").ShouldBe("/state/session-runner");
        sidecar.ShouldContain("runner-state:/state");
        sidecar.ShouldContain("work:/work");
        sidecar.ShouldNotContain("ports:");
        var server = Service("server");
        Env(server, "SessionRunner__Enabled").ShouldBe("true");
        Env(server, "SessionRunner__BaseUrl").ShouldBe("http://server-session-runner:8080");
        Env(server, "AgentSessions__SessionLogPath").ShouldBe("/runner-state/session-runner");
        Env(server, "ZombieCensus__SessionLogPath").ShouldBe("/runner-state/session-runner");
        server.ShouldContain("runner-state:/runner-state:ro");
        server.ShouldContain("work:/work");
        server.ShouldContain("server-session-runner:");
        sidecar.ShouldNotContain(".exe");
        sidecar.ShouldNotContain(".cmd");
        sidecar.Contains("C:\\", StringComparison.Ordinal).ShouldBeFalse();
        sidecar.Contains("C:/", StringComparison.Ordinal).ShouldBeFalse();
        text.ShouldNotContain(".cmd");
        text.Contains("C:\\", StringComparison.Ordinal).ShouldBeFalse();
        text.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains("C:/", StringComparison.Ordinal) || line.Contains(".exe", StringComparison.Ordinal))
            .ShouldBe(new[]
            {
                "PhoneHomeRunner__Runners__desktop-win__RunnerWorkspace: C:/Antiphon/runner/work",
                "PhoneHomeRunner__Runners__desktop-win__RunnerRepository: C:/Antiphon/runner/work/repos/antiphon",
                "PhoneHomeRunner__Runners__desktop-win__RawExeAllowList__0: \"C:/Program Files/PowerShell/7/pwsh.exe\"",
            });
    }

    [Test]
    public void Production_flags_specialists_and_hangfire_default()
    {
        var server = Service("server");
        Env(server, "Hangfire__ServerEnabled").ShouldBe("true");
        Env(server, "Hangfire__Storage").ShouldBe("${HANGFIRE_STORAGE:-InMemory}");
        Env(server, "ZombieCensus__Enabled").ShouldBe("false");
        Env(server, "WorktreeResidue__Enabled").ShouldBe("true");
        Env(server, "WorktreeResidue__Execute").ShouldBe("false");
        Env(server, "WorktreeLegacyRelease__Enabled").ShouldBe("false");
        Env(server, "WorktreeLegacyRelease__Execute").ShouldBe("false");
        Env(server, "RunnerWorktreeRetention__Enabled").ShouldBe("true");
        Env(server, "RunnerWorktreeRetention__Execute").ShouldBe("true");
        Env(server, "Schedules__Enabled").ShouldBe("true");
        Env(server, "ChannelBridge__Enabled").ShouldBe("${CHANNEL_BRIDGE_ENABLED:-false}");
        Env(server, "Digest__Enabled").ShouldBe("false");
        Env(server, "PhoneHomeRunner__Enabled").ShouldBe("true");
        Env(server, "Delegation__CheckInterpreterEnabled").ShouldBe("true");
        Env(server, "Delegation__DiagnoseEnabled").ShouldBe("true");
        Env(server, "Delegation__OutputDistillerEnabled").ShouldBe("true");
        Env(server, "Delegation__CheckInterpreterWorkingDirectory").ShouldBe("/work/specialists/check-interpreter");
        Env(server, "Delegation__DiagnoseWorkingDirectory").ShouldBe("/work/specialists/diagnose");
        Env(server, "Delegation__OutputDistillerWorkingDirectory").ShouldBe("/work/specialists/output-distiller");
        Env(server, "Git__WorktreeBasePath").ShouldBe("/work/worktrees");
        Env(server, "Git__WorkspacePath").ShouldBe("/work/repos");
        var text = Compose();
        text.ShouldContain("CARD-1774");
        text.ShouldContain("InMemory");
        Service("server-state-init").ShouldContain("/work/specialists/output-distiller");
        Service("server-state-init").ShouldContain("/work/specialists/check-interpreter");
        Service("server-state-init").ShouldContain("/work/specialists/diagnose");
        Service("server-state-init").ShouldContain("chmod 0755");
    }

    [Test]
    public void Production_key_protection_copies_into_the_state_volume()
    {
        var server = Service("server");
        Env(server, "AgentTui__KeyRingPath").ShouldBe("/state/keyring");
        Env(server, "AgentTui__KeyProtection__Mode").ShouldBe("X509Certificate");
        Env(server, "AgentTui__KeyProtection__CertificatePath").ShouldBe("/state/keyring-cert/cert.pem");
        Env(server, "AgentTui__KeyProtection__CertificatePrivateKeyPath").ShouldBe("/state/keyring-cert/key.pem");
        Env(server, "Operator__TokenPath").ShouldBe("/state/operator-token");
        server.ShouldNotContain("KEYRING_CERT_FILE");
        server.ShouldNotContain("KEYRING_KEY_FILE");
        server.ShouldNotContain("OPERATOR_TOKEN_FILE");
        var init = Service("server-state-init");
        init.ShouldContain("KEYRING_CERT_FILE:?KEYRING_CERT_FILE is required");
        init.ShouldContain("KEYRING_KEY_FILE:?KEYRING_KEY_FILE is required");
        init.ShouldContain("OPERATOR_TOKEN_FILE:?OPERATOR_TOKEN_FILE is required");
        init.ShouldContain("/host-keyring-cert/cert.pem");
        init.ShouldContain("/host-keyring-cert/key.pem");
        init.ShouldContain("/host-operator-token");
        init.ShouldContain("cp /host-keyring-cert/cert.pem /state/keyring-cert/cert.pem");
        init.ShouldContain("cp /host-keyring-cert/key.pem /state/keyring-cert/key.pem");
        init.ShouldContain("cp /host-operator-token /state/operator-token");
        init.ShouldContain("chmod 0600");
        init.ShouldContain("ForeignStateOwner");
        init.ShouldContain("exit 42");
        init.ShouldContain("1654");
        server.ShouldContain("server-state:/state");
        Compose().ShouldContain("name: antiphon-server2-server_server-state");
    }

    [Test]
    public void Production_phone_home_origin_is_not_hard_coded()
    {
        var server = Service("server");
        Env(server, "Delegation__ApiBaseUrl").ShouldBe("${ANTIPHON_PUBLIC_ORIGIN:?ANTIPHON_PUBLIC_ORIGIN is required}");
        Env(server, "PhoneHomeRunner__Runners__server2__Enabled").ShouldBe("true");
        Env(server, "PhoneHomeRunner__Runners__server2__AllowDelegatedTasks").ShouldBe("true");
        Env(server, "PhoneHomeRunner__Runners__server2__CallbackOrigin").ShouldBe("${ANTIPHON_PUBLIC_ORIGIN:?ANTIPHON_PUBLIC_ORIGIN is required}");
        Env(server, "PhoneHomeRunner__Runners__server2__SharedSecret").ShouldBe("${PHONE_HOME_SHARED_SECRET:?PHONE_HOME_SHARED_SECRET is required}");
        Env(server, "PhoneHomeRunner__Runners__server2__HostWorkspaceRoot").ShouldBe("${PHONE_HOME_HOST_WORKSPACE_ROOT:?PHONE_HOME_HOST_WORKSPACE_ROOT is required}");
        Env(server, "PhoneHomeRunner__Runners__server2__RunnerWorkspace").ShouldBe("/work");
        Env(server, "PhoneHomeRunner__Runners__server2__RunnerRepository").ShouldBe("/work/repos/antiphon");
        Env(server, "PhoneHomeRunner__Runners__server2__MaxCapacity").ShouldBe("10");
        var text = Compose();
        text.ShouldNotContain("codeperf.net");
        text.ShouldNotContain("PhoneHome__ServerOrigin");
        Script().ShouldNotContain("codeperf.net");
        text.ShouldNotContain("GH_TOKEN");
        text.ShouldNotContain("RUNNER_GITHUB_TOKEN");
    }

    [Test]
    public void Production_script_names_refusals_and_the_verify_only_gate()
    {
        var script = Script();
        script.Any(ch => ch > 127).ShouldBeFalse();
        Compose().Any(ch => ch > 127).ShouldBeFalse();
        script.ShouldContain("default is --what-if", Case.Sensitive);
        script.ShouldContain("var planOnly = !options.Execute;", Case.Sensitive);
        foreach (var literal in new[]
                 {
                     "not-server2-host", "ambiguous-mode", "bind-any", "volume-forbidden", "dev-password", "env-mode", "env-inside-repo", "missing-certificate",
                     "hangfire-storage", "expected-sha", "revision-mismatch", "production-compose-wrong-project", "/.dockerenv", "antiphon_pgdata", "dind-data", "17280",
                     "17202", "17420", "0.0.0.0", "windmill", "restart-linux-server", "verify-only", "antiphon-server2-server-server-1", "http://127.0.0.1:17400",
                 })
            script.ShouldContain(literal, Case.Sensitive);
        script.ShouldNotContain("docker compose up");
        // The gate runs verify-only: its options never set Execute, and the plan's gate line has no --execute.
        script.ShouldContain("new LinuxServerRestartOptions { Config = GateConfig, ExpectedServerSha = sha }", Case.Sensitive);
        script.ShouldNotContain("Execute = true");
        var gateLine = Server2ServerDeploy.HostCommand(new System.IO.Abstractions.TestingHelpers.MockFileSystem(), "", "/r/" + ComposePath, "").Split('\n')[2];
        gateLine.ShouldBe("verify-only: scripts/antiphon restart-linux-server --config /var/tmp/antiphon-server2-server.gate.config --expected-server-sha <expected-sha>");
        gateLine.ShouldNotContain("--execute");
    }
    [Test]
    public void Production_doc_names_the_compose_and_states_no_cache()
    {
        var doc = DockerStackDocuments.Read("docs/docker-stack.md");
        doc.ShouldContain("docker-compose.server2-server.yml");
        doc.ShouldContain("antiphon-server2-server");
        doc.ShouldContain("127.0.0.1:17400");
        doc.ShouldContain("server-session-runner");
        doc.ShouldContain("`scripts/antiphon deploy-server2-server` defaults to `--what-if`");
        doc.ShouldContain("verify-only");
        doc.ShouldContain("InMemory");
        doc.ShouldContain("CARD-1774");
        doc.ShouldContain("adds no cache");
        DockerStackDocuments.Read("scripts/deploy-server2.ps1").ShouldNotContain("docker-compose.server2-server.yml");
        DockerStackDocuments.Read("docker-compose.yml").ShouldNotContain("17400");
        DockerStackDocuments.Read("docker-compose.server2-staging-server.yml").ShouldNotContain("17400");
        DockerStackDocuments.Read("scripts/lib/Antiphon.Nightly/LinuxServerRestart.cs").ShouldNotContain("deploy-server2-server");
        doc.ShouldNotContain("deploy-server2-server.ps1");
    }

    [Test]
    public void WhatIf_prints_the_plan_and_calls_no_docker()
    {
        var world = new Server2ServerDeployWorld();
        var ran = world.Production("--what-if");
        ran.Exit.ShouldBe(0, ran.ToString());
        ran.Out.ShouldContain("docker-compose.server2-server.yml");
        ran.Out.ShouldContain("-p antiphon-server2-server up -d");
        ran.Out.ShouldContain("up -d --build");
        // CARD-1788 A-10: before up the command compares the env SOURCE_REVISION and
        // the build-context checkout HEAD. No image label is inspected before up.
        var up = ran.Out.IndexOf(" up -d", StringComparison.Ordinal);
        up.ShouldBeGreaterThan(0);
        var beforeUp = ran.Out[..up];
        beforeUp.ShouldContain("before up: SOURCE_REVISION in the env file and git rev-parse HEAD of ");
        beforeUp.ShouldContain(world.Root + " must equal <expected-sha>");
        beforeUp.ShouldNotContain("image label");
        beforeUp.ShouldNotContain("inspect");
        ran.Out.ShouldContain("verify-only");
        ran.Out.ShouldContain("restart-linux-server");
        ran.Out.ShouldContain("SERVER2-SERVER plan exit=0 reason=ok");
        ran.Out.ShouldNotContain("-Execute");
        ran.Out.ShouldNotContain("PHONE_HOME_SHARED_SECRET=");
        ran.Tools.ShouldBeEmpty();
    }

    [Test]
    public void Execute_refusals_stop_before_docker()
    {
        var world = new Server2ServerDeployWorld();
        AssertRefusal(world, ["--what-if", "--compose-file", world.Compose("bad.yml", "name: antiphon-server2-server\nantiphon_pgdata\n")], "volume-forbidden");
        AssertRefusal(world, ["--what-if", "--compose-file", world.Compose("bad.yml", "name: antiphon-server2-server\n0.0.0.0\n")], "bind-any");
        AssertRefusal(world, ["--what-if", "--compose-file", world.Compose("bad.yml", "name: antiphon-server2-server\n17420\n")], "port-forbidden");
        AssertRefusal(world, ["--what-if", "--compose-file", world.Compose("bad.yml", "name: antiphon-server2-server\ncodeperf." + "net\n")], "phone-home-origin");
        AssertRefusal(world, ["--what-if", "--compose-file", world.Compose("bad.yml", "name: antiphon-server2-server\nwindmill\n")], "volume-forbidden");
        AssertRefusal(world, ["--what-if", "--project", "antiphon-runner"], "production-compose-wrong-project");

        var both = world.Production("--what-if", "--execute");
        both.Exit.ShouldBe(3);
        both.Lines.Last().ShouldBe("SERVER2-SERVER fail exit=3 reason=ambiguous-mode");
        both.Tools.ShouldBeEmpty();

        var devRun = world.Production("--what-if", "--env-file", world.ProductionEnv("dev.env", new() { ["POSTGRES_PASSWORD"] = Server2ServerDeployWorld.DevPassword }));
        devRun.Exit.ShouldBe(2);
        devRun.Lines.Last().ShouldBe("SERVER2-SERVER fail exit=2 reason=dev-password");
        devRun.Hides(Server2ServerDeployWorld.DevPassword).ShouldBeTrue();
        devRun.Tools.ShouldBeEmpty();

        AssertRefusal(world, ["--what-if", "--env-file", world.ProductionEnv("redis.env", new() { ["HANGFIRE_STORAGE"] = "Redis" })], "hangfire-storage");
        AssertRefusal(world, ["--execute", "--env-file", world.ProductionEnv("cert.env", new() { ["KEYRING_CERT_FILE"] = Path.Combine(world.Secrets, "missing-cert.pem") }),
            "--expected-server-sha", new string('a', 40)], "missing-certificate");

        var inside = Path.Combine(world.Root, ".antiphon-card1775-env");
        world.Files.AddFile(inside, new System.IO.Abstractions.TestingHelpers.MockFileData("POSTGRES_PASSWORD=x\n"));
        world.OwnerOnly.Add(inside);
        AssertRefusal(world, ["--what-if", "--env-file", inside], "env-inside-repo");
        AssertRefusal(world, ["--what-if", "--env-file", world.ProductionEnv("mode.env", ownerOnly: false)], "env-mode");

        // CARD-1788 A-10: the host checkout HEAD must equal the expected SHA too. Off the host the lane refuses after the head check.
        const string head = Server2ServerDeployWorld.Head;
        world.Daemon = "not-the-host";
        var env = world.ProductionEnv();
        foreach (var sha in new[] { head, head.ToUpperInvariant() })
        {
            var run = world.Production("--execute", "--env-file", env, "--expected-server-sha", sha);
            run.Lines.Last().ShouldBe("SERVER2-SERVER fail exit=2 reason=not-server2-host");
            run.Tools.ShouldNotContain(tool => tool.StartsWith("docker compose", StringComparison.Ordinal));
        }
        world.Daemon = "server2";
        var other = new string('b', 40);
        var otherEnv = world.ProductionEnv("other.env", new() { ["SOURCE_REVISION"] = other });
        AssertHead(world, ["--execute", "--env-file", otherEnv, "--expected-server-sha", other], "head-mismatch");
        world.GitExit = 128;
        AssertHead(world, ["--execute", "--env-file", otherEnv, "--expected-server-sha", other, "--compose-file", world.Compose("docker-compose.server2-server.yml", Server2ServerDeployWorld.ProductionText)],
            "head-unknown");
        world.GitExit = 0;
        AssertRefusal(world, ["--execute", "--env-file", world.ProductionEnv("rev.env", new() { ["SOURCE_REVISION"] = new string('c', 40) }), "--expected-server-sha", other],
            "revision-mismatch");
        AssertRefusal(world, ["--execute", "--env-file", env, "--expected-server-sha", new string('d', 39) + "e"], "revision-mismatch");
    }
    [Test]
    public void Specialist_working_directories_are_on_a_mount_both_services_share()
    {
        var server = Service("server");
        var sidecar = Service("server-session-runner");
        var serverMounts = VolumeMounts(server);
        var sidecarMounts = VolumeMounts(sidecar);
        var init = Service("server-state-init");
        var script = Script();
        var doc = DockerStackDocuments.Read("docs/docker-stack.md");
        string[] paths =
        [
            Env(server, "Delegation__CheckInterpreterWorkingDirectory"),
            Env(server, "Delegation__DiagnoseWorkingDirectory"),
            Env(server, "Delegation__OutputDistillerWorkingDirectory"),
        ];
        paths.Distinct(StringComparer.Ordinal).Count().ShouldBe(3);
        foreach (var path in paths)
        {
            var shared = serverMounts.Where(mount =>
                PathIsUnder(path, mount.Destination)
                && !mount.ReadOnly
                && sidecarMounts.Any(other =>
                    !other.ReadOnly
                    && string.Equals(other.Source, mount.Source, StringComparison.Ordinal)
                    && string.Equals(other.Destination, mount.Destination, StringComparison.Ordinal))).ToList();
            shared.Count.ShouldBeGreaterThan(0, path);
            init.ShouldContain(path);
            init.ShouldContain("chmod 0755");
            init.ShouldContain("chown -R");
            init.ShouldContain("1654");
            script.ShouldContain(path);
            doc.ShouldContain(path);
        }
    }

    [Test]
    public void State_init_chowns_work_without_walking_repos_and_worktrees()
    {
        // CARD-1788 A-11: a recursive chown of /work walks every repository and
        // worktree on each up. Only the specialist directories are walked; the
        // other /work directories init creates are chowned one level.
        var chowns = InitChowns();
        chowns.Count.ShouldBeGreaterThan(0);
        chowns.ShouldAllBe(chown => chown.Owner == "\"$$uid:$$gid\"");
        var recursive = chowns.Where(chown => chown.Recursive).SelectMany(chown => chown.Targets).ToList();
        var single = chowns.Where(chown => !chown.Recursive).SelectMany(chown => chown.Targets).ToList();
        foreach (var target in recursive.Where(target => PathIsUnder(target, "/work")))
            PathIsUnder(target, "/work/specialists").ShouldBeTrue("recursive chown walks " + target);
        recursive.ShouldContain("/state");
        recursive.ShouldContain("/runner-state");
        var created = InitCreatedPaths();
        created.ShouldContain("/work");
        created.ShouldContain("/work/repos");
        created.ShouldContain("/work/worktrees");
        foreach (var path in created)
        {
            var covered = single.Contains(path) || recursive.Any(root => PathIsUnder(path, root));
            covered.ShouldBeTrue("init creates " + path + " but does not chown it");
        }

        var server = Service("server");
        foreach (var path in new[]
                 {
                     Env(server, "Delegation__CheckInterpreterWorkingDirectory"),
                     Env(server, "Delegation__DiagnoseWorkingDirectory"),
                     Env(server, "Delegation__OutputDistillerWorkingDirectory"),
                 })
        {
            recursive.Any(root => PathIsUnder(path, root)).ShouldBeTrue("specialist " + path + " is not chowned recursively");
        }
    }

    [Test]
    public void Execute_drains_large_compose_output_without_echoing_a_secret()
    {
        // The script started compose with both streams redirected and read to their end before waiting (a first up fills stderr);
        // the command's launcher drains both and the deploy prints neither.
        var world = new Server2ServerDeployWorld();
        var run = world.Production("--execute", "--env-file", world.ProductionEnv(), "--expected-server-sha", Server2ServerDeployWorld.Head);
        run.Exit.ShouldBe(0, run.ToString());
        var compose = world.Launcher.Launches.Single(launch => launch.Arguments.FirstOrDefault() == "compose");
        compose.CaptureOutput.ShouldBeTrue();
        run.Hides(Server2ServerDeployWorld.Secret).ShouldBeTrue(run.ToString());
    }
    [Test]
    public void Production_bootstrap_interpolation_has_no_live_host()
    {
        // CARD-1752 S1. Empty defaults. The env file supplies a value only when the bridge is enabled.
        var server = Service("server");
        server.ShouldContain("AntiphonMessaging__BootstrapServers: \"${ANTIPHON_MESSAGING_BOOTSTRAP_SERVERS:-}\"");
        server.ShouldContain("AntiphonMessaging__ClientId: \"antiphon-server2-server\"");
        server.ShouldContain("AntiphonMessaging__ExpectedClusterId: \"${ANTIPHON_MESSAGING_EXPECTED_CLUSTER_ID:-}\"");
        Env(server, "AntiphonMessaging__BootstrapServers").ShouldBe("${ANTIPHON_MESSAGING_BOOTSTRAP_SERVERS:-}");
        Env(server, "AntiphonMessaging__ClientId").ShouldBe("antiphon-server2-server");
        Env(server, "AntiphonMessaging__ExpectedClusterId").ShouldBe("${ANTIPHON_MESSAGING_EXPECTED_CLUSTER_ID:-}");
        server.ShouldNotContain("localhost");
        server.ShouldNotContain("100.93.77.126");
        server.ShouldNotContain("100.79.51.37");
        var root = DockerStackDocuments.Read("docker-compose.yml");
        root.ShouldContain("ChannelBridge__Enabled: \"false\"");
        root.ShouldNotContain("ANTIPHON_MESSAGING_BOOTSTRAP_SERVERS");
        var needs = Server2ServerDeploy.ComposeNeeds;
        needs.ShouldContain("AntiphonMessaging__BootstrapServers: \"${ANTIPHON_MESSAGING_BOOTSTRAP_SERVERS:-}\"");
        needs.ShouldContain("AntiphonMessaging__ClientId: \"antiphon-server2-server\"");
        needs.ShouldContain("AntiphonMessaging__ExpectedClusterId: \"${ANTIPHON_MESSAGING_EXPECTED_CLUSTER_ID:-}\"");
        needs.ShouldContain("ChannelBridge__Enabled: \"${CHANNEL_BRIDGE_ENABLED:-false}\"");
        needs.ShouldContain("Digest__Enabled: \"false\"");
    }

    [Test]
    public void Production_client_id_is_antiphon_server2_server()
    {
        var server = Service("server");
        server.ShouldContain("AntiphonMessaging__ClientId: \"antiphon-server2-server\"");
        Env(server, "AntiphonMessaging__ClientId").ShouldBe("antiphon-server2-server");
        server.ShouldContain("Digest__Enabled: \"false\"");
        server.ShouldContain("ChannelBridge__Enabled: \"${CHANNEL_BRIDGE_ENABLED:-false}\"");
        Env(server, "Digest__Enabled").ShouldBe("false");
        Env(server, "ChannelBridge__Enabled").ShouldBe("${CHANNEL_BRIDGE_ENABLED:-false}");
    }

    private static void AssertRefusal(Server2ServerDeployWorld world, string[] args, string reason)
    {
        var ran = world.Production(args);
        ran.Exit.ShouldBe(2, ran.ToString());
        ran.Lines.Last().ShouldBe("SERVER2-SERVER fail exit=2 reason=" + reason);
        ran.Tools.ShouldBeEmpty();
    }

    // The head refusals ran git only.
    private static void AssertHead(Server2ServerDeployWorld world, string[] args, string reason)
    {
        var ran = world.Production(args);
        ran.Exit.ShouldBe(2, ran.ToString());
        ran.Lines.Last().ShouldBe("SERVER2-SERVER fail exit=2 reason=" + reason);
        ran.Tools.ShouldHaveSingleItem().ShouldStartWith("git -C ");
    }

    private static string ReadRequired(string relative)
    {
        var path = Path.Combine(DockerStackDocuments.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).ShouldBeTrue(relative);
        return File.ReadAllText(path);
    }
    private readonly record struct ChownStatement(bool Recursive, string Owner, string[] Targets);

    private static string InitCommand()
    {
        var init = Service("server-state-init");
        var line = init.Replace("\r\n", "\n").Split('\n').Single(row => row.Contains("set -eu;", StringComparison.Ordinal)).Trim();
        line.StartsWith("- \"", StringComparison.Ordinal).ShouldBeTrue(line);
        line.EndsWith('"').ShouldBeTrue(line);
        return line[3..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
    }

    private static string[] InitStatements() =>
        InitCommand().Split(';').Select(part => part.Trim()).Where(part => part.Length > 0).ToArray();

    private static List<string> InitCreatedPaths()
    {
        var statements = InitStatements();
        var index = Array.FindIndex(statements, s => s.StartsWith("for d in ", StringComparison.Ordinal));
        index.ShouldBeGreaterThanOrEqualTo(0);
        statements[index + 1].ShouldStartWith("do mkdir -p ");
        return statements[index]["for d in ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static List<ChownStatement> InitChowns()
    {
        var chowns = new List<ChownStatement>();
        foreach (var statement in InitStatements())
        {
            var tokens = statement.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            tokens.Skip(1).ShouldNotContain("chown", statement);
            if (tokens[0] != "chown")
                continue;
            var flags = tokens.Skip(1).Where(token => token.StartsWith('-')).ToArray();
            flags.ShouldAllBe(flag => flag == "-R", statement);
            var owner = tokens.Skip(1).Where(token => !token.StartsWith('-')).First();
            var targets = tokens.Skip(1).Where(token => !token.StartsWith('-')).Skip(1).ToArray();
            targets.ShouldAllBe(target => target.StartsWith('/'), statement);
            chowns.Add(new ChownStatement(flags.Length > 0, owner, targets));
        }

        return chowns;
    }

    private static string Compose() => ReadRequired(ComposePath);

    private static string Script() => ReadRequired(ScriptPath);

    private static string Service(string name) => DockerStackDocuments.Service(Compose(), name);

    private static string Env(string block, string key) => DockerStackDocuments.Env(block, key);

    private readonly record struct VolumeMount(string Source, string Destination, bool ReadOnly);

    private static List<VolumeMount> VolumeMounts(string block)
    {
        var mounts = new List<VolumeMount>();
        foreach (var spec in DockerStackDocuments.List(block, "volumes"))
        {
            var parts = spec.Split(':');
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
                continue;
            var readOnly = parts.Length >= 3 && parts[2].Split(',').Any(flag => flag == "ro");
            mounts.Add(new VolumeMount(parts[0], parts[1], readOnly));
        }

        return mounts;
    }

    private static bool PathIsUnder(string path, string destination)
    {
        var root = destination.TrimEnd('/');
        if (root.Length == 0)
            root = "/";
        if (string.Equals(path, root, StringComparison.Ordinal))
            return true;
        var prefix = root == "/" ? "/" : root + "/";
        return path.StartsWith(prefix, StringComparison.Ordinal);
    }
}
