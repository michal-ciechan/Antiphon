using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class DockerStackContractTests
{
    [Test]
    public void Server_project_graph_is_available()
    {
        var stage = Stage("Dockerfile", "server-build");
        var missing = new List<string>();
        if (!stage.Body.Contains("COPY src/ src/", StringComparison.Ordinal))
            missing.Add("Antiphon.Agents.Pty.csproj");
        missing.ShouldBeEmpty("missing referenced project list contains " + string.Join(',', missing));
    }

    [Test]
    public void Runtime_contains_built_client()
    {
        var final = Stage("Dockerfile", "runtime");
        final.Body.Contains("client/dist", StringComparison.Ordinal)
            .ShouldBeTrue("final-stage payload lacks wwwroot/index.html");
    }

    [Test]
    public void Runtime_contains_instruction_bundles()
    {
        var included = !DockerStackDocuments.Excluded(Text(".dockerignore"), "server/Bundles/delegate-basics.md");
        included.ShouldBeTrue("required-context list contains the excluded bundle");
    }

    [Test]
    public void Sdk_satisfies_repository_pin()
    {
        var compatible = Stage("Dockerfile", "server-build").From.Contains("sdk:10.", StringComparison.Ordinal);
        compatible.ShouldBeTrue("SDK major compatibility is false");
    }

    [Test]
    public void Runtime_and_rid_match_linux_amd64()
    {
        Stage("Dockerfile", "server-build").Body.Contains("-r linux-x64", StringComparison.Ordinal)
            .ShouldBeTrue("publish RID equals linux-x64");
    }

    [Test]
    public void Runner_contains_linux_host() =>
        RunnerBody().Contains("Antiphon.PtyHost", StringComparison.Ordinal)
            .ShouldBeTrue("native manifest missing Antiphon.PtyHost");

    [Test]
    public void Runner_contains_porta_library() =>
        RunnerBody().Contains("libporta_pty.so", StringComparison.Ordinal)
            .ShouldBeTrue("native manifest missing libporta_pty.so");

    [Test]
    public void Runner_contains_host_runtime_payload() =>
        RunnerBody().Contains("Antiphon.PtyHost.runtimeconfig.json", StringComparison.Ordinal)
            .ShouldBeTrue("native manifest missing Antiphon.PtyHost.runtimeconfig.json");

    [Test]
    public void Runner_requires_executable_host() =>
        RunnerBody().Contains("test -x /app/Antiphon.PtyHost", StringComparison.Ordinal)
            .ShouldBeTrue("required executable validation absent");

    [Test]
    public void Server_publish_has_revision() =>
        Revision("Dockerfile", "server-build").ShouldBe("$SOURCE_REVISION", "publish revision source equals SOURCE_REVISION");

    [Test]
    public void Runner_publish_has_revision() =>
        Revision("docker/session-runner-grok/Dockerfile", "build").ShouldBe("$SOURCE_REVISION", "publish revision source equals SOURCE_REVISION");

    [Test]
    public void Fixture_publish_has_revision() =>
        Revision("docker/tests/Dockerfile", "delivery-fixture").ShouldBe("$SOURCE_REVISION", "publish revision source equals SOURCE_REVISION");

    [Test]
    public void Runner_route_is_internal() =>
        new Uri(Env(Server(), "SessionRunner__BaseUrl")).Host.ShouldBe("session-runner");

    [Test]
    public void Database_route_is_internal()
    {
        var host = System.Text.RegularExpressions.Regex.Match(
            Env(Server(), "ConnectionStrings__DefaultConnection"), @"Host=([^;]+)").Groups[1].Value;
        host.ShouldBe("postgres");
    }

    [Test]
    public void antiphon_phone_home_is_disabled() =>
        False(Server(), "PhoneHomeRunner__Enabled").ShouldBeTrue("effective phone-home enabled is false");

    [Test]
    public void session_runner_phone_home_is_disabled() =>
        False(Runner(), "PhoneHome__Enabled").ShouldBeTrue("effective phone-home enabled is false");

    [Test]
    public void session_runner_port_is_private() =>
        DockerStackDocuments.List(Runner(), "ports").ShouldBeEmpty();

    [Test]
    public void postgres_port_is_private() =>
        DockerStackDocuments.List(Postgres(), "ports").ShouldBeEmpty();

    [Test]
    public void Default_server_bind_is_loopback()
    {
        var spec = DockerStackDocuments.List(Server(), "ports").Single();
        var match = System.Text.RegularExpressions.Regex.Match(spec, @"ANTIPHON_BIND_ADDRESS:-([0-9.]+)");
        var bind = match.Success ? match.Groups[1].Value : spec.Split(':')[0];
        bind.ShouldBe("127.0.0.1");
    }

    [Test]
    public void postgres_must_be_healthy() =>
        Server().Replace("\r\n", "\n").Contains("postgres:\n        condition: service_healthy", StringComparison.Ordinal).ShouldBeTrue();

    [Test]
    public void session_runner_must_be_healthy() =>
        Server().Replace("\r\n", "\n").Contains("session-runner:\n        condition: service_healthy", StringComparison.Ordinal).ShouldBeTrue();

    [Test]
    public void antiphon_health_probe_exists() =>
        HealthInstalled("Dockerfile", Server()).ShouldBeTrue("health executable belongs to stage-installed tool manifest");

    [Test]
    public void session_runner_health_probe_exists() =>
        HealthInstalled("docker/session-runner-grok/Dockerfile", Runner()).ShouldBeTrue("health executable belongs to stage-installed tool manifest");

    [Test]
    public void antiphon_base_has_no_socket() =>
        Server().Contains("docker.sock", StringComparison.Ordinal).ShouldBeFalse();

    [Test]
    public void session_runner_base_has_no_socket() =>
        Runner().Contains("docker.sock", StringComparison.Ordinal).ShouldBeFalse();

    [Test]
    public void Server2_runner_owns_its_daemon()
    {
        var runner = Server2Runner();
        runner.Contains("privileged: true", StringComparison.Ordinal).ShouldBeTrue("server2 runner is privileged");
        runner.Contains("dind-data:/var/lib/docker", StringComparison.Ordinal).ShouldBeTrue("nested store is the dind-data volume");
        var sockets = ComposeFiles()
            .SelectMany(file => Text(file).Replace("\r\n", "\n").Split('\n').Select(line => file + ": " + line))
            .Where(line => line.Contains("docker.sock", StringComparison.Ordinal))
            .ToList();
        sockets.ShouldBeEmpty("no compose file mounts a docker socket");
    }

    [Test]
    public void Only_the_server2_runner_is_privileged()
    {
        var privileged = ComposeFiles()
            .SelectMany(file => Text(file).Replace("\r\n", "\n").Split('\n').Select(line => (file, line: line.Trim())))
            .Where(item => item.line == "privileged: true")
            .Select(item => item.file)
            .ToList();
        privileged.ShouldBe(["docker-compose.server2-runner.yml"]);
    }

    [Test]
    public void Server2_runner_starts_as_root_and_drops()
    {
        Uid(Server2Runner()).ShouldBe(0);
        Stage("docker/session-runner-grok/Dockerfile", "session-testing").Body
            .ShouldContain("antiphon-dind-entrypoint.sh");
        Text("docker/session-runner-grok/dind-entrypoint.sh").ShouldContain("setpriv --reuid=");
    }

    [Test]
    public void Deploy_key_secret_is_required()
    {
        Text("docker-compose.server2-runner.yml").ShouldContain("ANTIPHON_DEPLOY_KEY_FILE:?");
        DockerStackDocuments.List(Server2Runner(), "secrets").ShouldContain("antiphon-deploy-key");
    }

    [Test]
    public void Server2_runner_capacity_is_ten()
    {
        Env(Server2Runner(), "PhoneHome__Capacity").ShouldBe("10");
        // The server refuses a registration above its own bound, so the declared seats must fit it.
        int.Parse(Env(Server2Runner(), "PhoneHome__Capacity"))
            .ShouldBeLessThanOrEqualTo(new Antiphon.Server.Application.Settings.PhoneHomeRunnerSettings().MaxCapacity);
        var stack = DockerStackDocuments.Read("docs/docker-stack.md");
        stack.ShouldContain("PhoneHome__Capacity");
        stack.ShouldContain("\"10\"");
        stack.ShouldContain("scripts/runner-slots.ps1");
    }

    [Test]
    public void Server2_runner_requires_phone_home_origin_and_secret()
    {
        var text = Text("docker-compose.server2-runner.yml");
        text.ShouldContain("PHONE_HOME_SERVER_ORIGIN:?");
        text.ShouldContain("PHONE_HOME_SECRET_FILE:?");
        // CARD-0604 D-1: the runner reads the copy the entrypoint stages onto the app-owned
        // tmpfs, never the compose mount itself -- that arrives owned by the host uid at 0600
        // and uid 1654 cannot open it. DindRunnerContractTests owns the staging contract.
        Env(Server2Runner(), "ANTIPHON_PHONE_HOME_SECRET_SOURCE").ShouldBe("/run/secrets/phone-home");
        Env(Server2Runner(), "PhoneHome__SecretPath").ShouldBe("/run/antiphon/phone-home");
        DockerStackDocuments.List(Server2Runner(), "secrets").ShouldContain("phone-home");
    }

    [Test]
    public void Server2_runner_has_nested_store_volume()
    {
        Destination(Server2Runner(), "dind-data").ShouldBe("/var/lib/docker");
        Text("docker-compose.server2-runner.yml").Replace("\r\n", "\n")
            .Contains("\n  dind-data:", StringComparison.Ordinal).ShouldBeTrue("dind-data is a named volume");
    }

    [Test]
    public void Server2_services_name_their_images()
    {
        // `up --no-build` against a service with only a build block derives a name and tries to
        // PULL it, which fails as a denied access to a repository that does not exist. Every
        // service in the deployment file names the tag the deploy actually builds.
        foreach (var service in new[] { "state-init", "session-runner" })
        {
            var block = DockerStackDocuments.Service(Text("docker-compose.server2-runner.yml"), service);
            block.Contains("image: antiphon-server2/", StringComparison.Ordinal)
                .ShouldBeTrue(service + " names its image explicitly");
            block.ShouldContain("SOURCE_SHA12:?");
        }
    }

    [Test]
    public void Server2_runner_restarts_unless_stopped() =>
        Server2Runner().Contains("restart: unless-stopped", StringComparison.Ordinal).ShouldBeTrue();

    [Test]
    public void Server2_file_defines_only_runner_and_state_init()
    {
        var blocks = Text("docker-compose.server2-runner.yml")
            .Replace("\r\n", "\n").Split('\n')
            .Where(line => line.StartsWith("  ", StringComparison.Ordinal)
                && !line.StartsWith("   ", StringComparison.Ordinal)
                && line.TrimEnd().EndsWith(':'))
            .Select(line => line.Trim().TrimEnd(':'))
            .ToList();
        blocks.ShouldBe(["state-init", "session-runner", "antiphon-deploy-key", "phone-home", "work", "runner-state", "dind-data"]);
    }

    [Test]
    public void Stack_env_example_has_no_socket_gid()
    {
        var text = Text("docker/stack.env.example");
        text.Contains("DOCKER_SOCKET_GID", StringComparison.Ordinal).ShouldBeFalse();
        text.ShouldContain("COMPOSE_PROJECT_NAME=antiphon-runner");
        text.ShouldContain("ANTIPHON_DEPLOY_KEY_FILE=");
        text.ShouldContain("PHONE_HOME_SECRET_FILE=");
    }

    [Test]
    public void Base_compose_keeps_phone_home_disabled()
    {
        False(Server(), "PhoneHomeRunner__Enabled").ShouldBeTrue();
        False(Runner(), "PhoneHome__Enabled").ShouldBeTrue();
    }

    [Test]
    public void Testing_stage_ships_the_engine()
    {
        var body = SessionTesting();
        foreach (var binary in new[]
                 {
                     "docker/docker", "docker/dockerd", "docker/containerd", "docker/containerd-shim-runc-v2",
                     "docker/runc", "docker/docker-init", "docker/docker-proxy", "docker/ctr",
                 })
            body.Contains(binary, StringComparison.Ordinal).ShouldBeTrue("engine tarball path " + binary + " is extracted");
        System.Text.RegularExpressions.Regex
            .Matches(body, @"download\.docker\.com/linux/static/stable/x86_64/docker-[0-9.]+\.tgz")
            .Count.ShouldBe(1, "exactly one engine source");
        body.ShouldContain("docker-27.5.1.tgz");
    }

    [Test]
    public void Testing_stage_pins_legacy_iptables()
    {
        var body = SessionTesting();
        body.ShouldContain("update-alternatives --set iptables /usr/sbin/iptables-legacy");
        body.ShouldContain("update-alternatives --set ip6tables /usr/sbin/ip6tables-legacy");
        body.ShouldContain("iptables --version | grep -F 'legacy'");
    }

    [Test]
    public void Testing_stage_has_build_toolchain()
    {
        var body = SessionTesting();
        body.ShouldContain("COPY --from=build /usr/share/dotnet /usr/share/dotnet");
        body.ShouldContain("COPY --from=node22 /usr/local /usr/local");
        body.ShouldContain("dotnet --list-sdks");
        body.ShouldContain("Microsoft.AspNetCore.App 9.");
        body.ShouldContain("Microsoft.AspNetCore.App 10.");
        body.ShouldContain("node --version");
        body.ShouldContain("openssh-client");
        Stage("docker/session-runner-grok/Dockerfile", "node22").From.ShouldContain("node:22");
    }

    [Test]
    public void Testing_stage_entrypoint_is_the_dind_script()
    {
        var body = SessionTesting();
        body.ShouldContain("COPY docker/session-runner-grok/dind-entrypoint.sh /usr/local/bin/antiphon-dind-entrypoint.sh");
        body.ShouldContain("ENTRYPOINT [\"/usr/local/bin/antiphon-dind-entrypoint.sh\"]");
        body.ShouldContain("CMD [\"dotnet\", \"Antiphon.SessionRunner.dll\"]");
        body.ShouldContain("USER 0:0");
    }

    [Test]
    public void Testing_stage_creates_docker_nested_group()
    {
        SessionTesting().ShouldContain("groupadd -g 1656 docker-nested");
        SessionTesting().ShouldContain("getent group docker-nested");
        Text("docker/session-runner-grok/daemon.json").ShouldContain("\"group\": \"docker-nested\"");
    }

    [Test]
    public void Testing_services_are_unprivileged() =>
        Tests().Contains("privileged: true", StringComparison.Ordinal).ShouldBeFalse();

    [Test]
    public void Applications_share_nonroot_identity()
    {
        var server = Uid(Server());
        var runner = Uid(Runner());
        server.ShouldBeGreaterThan(0);
        runner.ShouldBe(server);
    }

    [Test]
    public void State_paths_are_linux_absolute()
    {
        var invalid = new List<string>();
        var path = Env(Server(), "Git__WorkspacePath");
        if (!path.StartsWith('/') || path.Contains('\\'))
            invalid.Add("Git__WorkspacePath");
        invalid.ShouldBeEmpty();
    }

    [Test]
    public void Workspace_mount_paths_match()
    {
        Destination(Server(), "work").ShouldBe("/work");
        Destination(Runner(), "work").ShouldBe("/work");
    }

    [Test]
    public void Reusable_state_has_named_volumes() =>
        Postgres().Contains("pgdata:", StringComparison.Ordinal).ShouldBeTrue();

    [Test]
    public void Linux_backend_is_inbox()
    {
        Env(Runner(), "SessionRunner__PtyBackend").ShouldBe("inbox");
        Env(Runner(), "ANTIPHON_PTY_BACKEND").ShouldBe("inbox");
    }

    [Test]
    public void Linux_herdr_is_disabled() =>
        False(Runner(), "SessionRunner__Herdr__Enabled").ShouldBeTrue();

    [Test]
    public void Fresh_stack_is_inactive() =>
        EnabledUnattended().ShouldBeEmpty();

    [Test]
    public void Keyring_is_private_external_state() =>
        Env(Server(), "AgentTui__KeyRingPath").StartsWith("/state/", StringComparison.Ordinal).ShouldBeTrue();

    [Test] public void Runtime_context_denies_GitPointer() => Deny(".dockerignore", ".git");
    [Test] public void Runtime_context_denies_AgentState() => Deny(".dockerignore", ".antiphon/case.json");
    [Test] public void Runtime_context_denies_Environment() => Deny(".dockerignore", "client/.env.local");
    [Test] public void Runtime_context_denies_Auth() => Deny(".dockerignore", "scratch/auth.json");
    [Test] public void Runtime_context_denies_ProviderHome() => Deny(".dockerignore", ".grok/config.toml");
    [Test] public void Runtime_context_denies_Certificate() => Deny(".dockerignore", "scratch/key.pfx");
    [Test] public void Runtime_context_denies_Bin() => Deny(".dockerignore", "server/bin/x.dll");
    [Test] public void Runtime_context_denies_AlternateBin() => Deny(".dockerignore", "server/bin-pc/x.dll");
    [Test] public void Runtime_context_denies_Obj() => Deny(".dockerignore", "server/obj/x");
    [Test] public void Runtime_context_denies_NodeModules() => Deny(".dockerignore", "client/node_modules/x");
    [Test] public void Runtime_context_denies_Workspace() => Deny(".dockerignore", "workspace/owned.txt");
    [Test] public void Runtime_context_denies_Logs() => Deny(".dockerignore", "logs/test.log");
    [Test] public void Runtime_context_denies_Pem() => Deny(".dockerignore", "scratch/key.pem");

    [Test] public void Tests_context_denies_GitPointer() => Deny("docker/tests/Dockerfile.dockerignore", ".git");
    [Test] public void Tests_context_denies_AgentState() => Deny("docker/tests/Dockerfile.dockerignore", ".antiphon/case.json");
    [Test] public void Tests_context_denies_Environment() => Deny("docker/tests/Dockerfile.dockerignore", "client/.env.local");
    [Test] public void Tests_context_denies_Auth() => Deny("docker/tests/Dockerfile.dockerignore", "scratch/auth.json");
    [Test] public void Tests_context_denies_ProviderHome() => Deny("docker/tests/Dockerfile.dockerignore", ".grok/config.toml");
    [Test] public void Tests_context_denies_Certificate() => Deny("docker/tests/Dockerfile.dockerignore", "scratch/key.pfx");
    [Test] public void Tests_context_denies_Bin() => Deny("docker/tests/Dockerfile.dockerignore", "server/bin/x.dll");
    [Test] public void Tests_context_denies_AlternateBin() => Deny("docker/tests/Dockerfile.dockerignore", "server/bin-pc/x.dll");
    [Test] public void Tests_context_denies_Obj() => Deny("docker/tests/Dockerfile.dockerignore", "server/obj/x");
    [Test] public void Tests_context_denies_NodeModules() => Deny("docker/tests/Dockerfile.dockerignore", "client/node_modules/x");
    [Test] public void Tests_context_denies_Workspace() => Deny("docker/tests/Dockerfile.dockerignore", "workspace/owned.txt");
    [Test] public void Tests_context_denies_Logs() => Deny("docker/tests/Dockerfile.dockerignore", "logs/test.log");
    [Test] public void Tests_context_denies_Pem() => Deny("docker/tests/Dockerfile.dockerignore", "scratch/key.pem");

    // CARD-0628 G-5: a checkout's Claude settings/skills and any credential file never enter a build context.
    [Test] public void Runtime_context_denies_ClaudeHome() => Deny(".dockerignore", ".claude/settings.json");
    [Test] public void Runtime_context_denies_ClaudeCredentials() => Deny(".dockerignore", "scratch/.credentials.json");
    [Test] public void Tests_context_denies_ClaudeHome() => Deny("docker/tests/Dockerfile.dockerignore", ".claude/settings.json");
    [Test] public void Tests_context_denies_ClaudeCredentials() => Deny("docker/tests/Dockerfile.dockerignore", "scratch/.credentials.json");

    [Test]
    public void Tests_context_retains_claude_skill_docs_only()
    {
        // Source-inspection tests in the Linux lane read .claude/skills; nothing else under .claude,
        // and no credential file even inside skills, may follow them in.
        var ignore = Text("docker/tests/Dockerfile.dockerignore");
        DockerStackDocuments.Excluded(ignore, ".claude/skills/antiphon-delegate/SKILL.md").ShouldBeFalse();
        DockerStackDocuments.Excluded(ignore, ".claude/settings.local.json").ShouldBeTrue();
        DockerStackDocuments.Excluded(ignore, ".claude/skills/x/.credentials.json").ShouldBeTrue();
    }

    // CARD-0628 G-1 (D-2): the native binary, pinned by version and published digest, verified
    // before install, smoke-run with a throwaway HOME/store, and the temp tree gone in the same layer.
    [Test]
    public void Runner_pins_claude_by_version_and_digest()
    {
        var body = RunnerBody();
        body.ShouldContain("ARG CLAUDE_CODE_VERSION=2.1.280");
        System.Text.RegularExpressions.Regex.IsMatch(body, @"ARG CLAUDE_CODE_SHA256=1e08503dbdf3c2cb0d706d32f3408277388d1c76ef108673e8fe42c1b322925b\s")
            .ShouldBeTrue("the published 64-hex digest is pinned");
        body.ShouldContain("https://downloads.claude.ai/claude-code-releases/${CLAUDE_CODE_VERSION}/linux-x64/claude");
        body.ShouldContain("echo \"${CLAUDE_CODE_SHA256}  /tmp/claude-install/claude\" | sha256sum -c -");
        body.ShouldContain("install -m 0755 /tmp/claude-install/claude /usr/local/bin/claude");
        body.ShouldContain("env HOME=/tmp/claude-install CLAUDE_CONFIG_DIR=/tmp/claude-install/cfg DISABLE_AUTOUPDATER=1 /usr/local/bin/claude --version | grep -F \"${CLAUDE_CODE_VERSION}\"");
        body.ShouldContain("rm -rf /tmp/claude-install");
        Order(body, "sha256sum -c -", "install -m 0755 /tmp/claude-install/claude").ShouldBeTrue("verify before install");
        Order(body, "/usr/local/bin/claude --version", "rm -rf /tmp/claude-install").ShouldBeTrue("the temp tree is removed last");
        body.ShouldContain("any GROK_HOME or CLAUDE_CONFIG_DIR contents");
        body.Contains("install.sh | bash -s 2.1.280", StringComparison.Ordinal).ShouldBeFalse("no moving-bootstrap installer");
        Text("docker/session-runner-grok/Dockerfile").Contains("ENV CLAUDE_CODE_OAUTH_TOKEN", StringComparison.Ordinal)
            .ShouldBeFalse("the token is never an image layer");
    }

    // CARD-0628: the --settings file Claude is given on a runner-bound launch is this image
    // path, and its bytes are the server's one-key constant.
    [Test]
    public void Runner_image_ships_the_off_settings_file_at_the_server_constant()
    {
        var path = ClaudeRemoteControlLaunchArgs.RunnerOffSettingsPath;
        path.ShouldBe("/opt/antiphon/claude-remote-control-off.json");
        var body = RunnerBody();
        body.ShouldContain($"COPY server/Runtime/claude-remote-control-off.json {path}");
        Text("server/Runtime/claude-remote-control-off.json").Trim()
            .ShouldBe(ClaudeRemoteControlLaunchArgs.OffSettingsJson);
    }

    // CARD-0628 G-2: the node22 COPY merges /usr/local, so the inventory re-asserts claude after it.
    [Test]
    public void Testing_stage_asserts_claude_after_node_merge()
    {
        var body = SessionTesting();
        const string probe = "claude --version | grep -F '2.1.280'";
        body.ShouldContain(probe);
        Order(body, "COPY --from=node22 /usr/local /usr/local", probe).ShouldBeTrue("asserted after the merge");
        body.ShouldContain("rm -rf /tmp/claude-probe");
    }

    // CARD-0628 G-3 (D-6, amended D-1): one Claude store in three places, and the setup-token
    // arriving as a read-only FILE mount on the /run/antiphon tmpfs, never an environment entry.
    [Test]
    public void Server2_compose_projects_claude_config_dir()
    {
        var runner = Server2Runner();
        Env(runner, "CLAUDE_CONFIG_DIR").ShouldBe("/state/claude");
        Env(runner, "PhoneHome__ClaudeHome").ShouldBe(Env(runner, "CLAUDE_CONFIG_DIR"));
        new global::Antiphon.Server.Application.Settings.PhoneHomeRunnerSettings().ChildClaudeHome
            .ShouldBe(Env(runner, "CLAUDE_CONFIG_DIR"), "the server projects the store the runner process reads");
        new global::Antiphon.SessionRunner.PhoneHomeSettings().ClaudeHome
            .ShouldBe(Env(runner, "CLAUDE_CONFIG_DIR"), "the auth probe targets the same store");
        Destination(runner, "runner-state").ShouldBe("/state");

        DockerStackDocuments.List(runner, "volumes")
            .ShouldContain("${CLAUDE_OAUTH_TOKEN_FILE:?CLAUDE_OAUTH_TOKEN_FILE is required}:/run/antiphon/claude-oauth-token:ro",
                "the deploy's token file, read-only, at the path the entrypoint reads");
        DockerStackDocuments.List(runner, "tmpfs").ShouldContain("/run/antiphon");
        Text("docker/session-runner-grok/dind-entrypoint.sh")
            .ShouldContain("CLAUDE_OAUTH_TOKEN_SOURCE=\"$RUNTIME_DIR/claude-oauth-token\"");
        Text("docker/stack.env.example")
            .ShouldContain("CLAUDE_OAUTH_TOKEN_FILE=/home/mc/antiphon-server2/secrets/claude_oauth_token");
        foreach (var name in new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN" })
            Text("docker-compose.server2-runner.yml").Contains(name, StringComparison.Ordinal)
                .ShouldBeFalse(name + " is never a runner fallback");
        System.Text.RegularExpressions.Regex.IsMatch(Text("docker/stack.env.example"), @"(?m)^\s*CLAUDE_CODE_OAUTH_TOKEN\s*=")
            .ShouldBeFalse("the env example never carries a token assignment");
    }

    // CARD-0628 D-1 (Round D): an environment entry, even a valueless pass-through, is printed by
    // `docker inspect`. No compose file may name the token under any service's environment.
    [Test]
    public void Compose_never_lists_the_claude_token_under_environment()
    {
        foreach (var file in ComposeFiles())
        {
            var lines = Text(file).Replace("\r\n", "\n").Split('\n');
            var envIndent = -1;
            foreach (var raw in lines)
            {
                var trimmed = raw.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;
                var indent = raw.Length - raw.TrimStart().Length;
                if (envIndent >= 0 && indent <= envIndent)
                    envIndent = -1;
                if (trimmed == "environment:")
                {
                    envIndent = indent;
                    continue;
                }

                if (envIndent < 0)
                    continue;
                var entry = trimmed.StartsWith("- ", StringComparison.Ordinal) ? trimmed[2..].TrimStart() : trimmed;
                entry.StartsWith("CLAUDE_CODE_OAUTH_TOKEN", StringComparison.Ordinal)
                    .ShouldBeFalse(file + " lists the Claude token under environment: " + trimmed);
            }
        }
    }

    // CARD-0628 D-1 (Round D): the deploy reads the vault item and streams it over SSH stdin; the
    // host lane only guarantees the file exists at 0600 and records presence, never content.
    [Test]
    public void Server2_deploy_streams_the_claude_token_file()
    {
        var bridge = Text("scripts/c590-real.ps1");
        bridge.ShouldContain("$script:C628ClaudeTokenItem = 'antiphon/server2/claude-oauth-token'");
        bridge.ShouldContain("& bw get password $script:C628ClaudeTokenItem --nointeraction");
        bridge.ShouldContain("$token | & ssh -o BatchMode=yes");
        bridge.ShouldContain("cat > '$target.tmp' && chmod 0600 '$target.tmp'");
        bridge.ShouldContain("if ($Case -eq 'deploy-parent') {");
        bridge.ShouldContain("ClaudeOAuthTokenUnavailable");
        foreach (var line in bridge.Replace("\r\n", "\n").Split('\n').Where(line => line.Contains("$token", StringComparison.Ordinal)))
        {
            line.Contains("Write-", StringComparison.Ordinal).ShouldBeFalse("the token is never written out: " + line.Trim());
            System.Text.RegularExpressions.Regex.IsMatch(line, @"ssh[^|]*\$token")
                .ShouldBeFalse("the token is never an ssh argument: " + line.Trim());
        }

        var remote = Text("scripts/c590-remote.sh").Replace("\r\n", "\n");
        remote.ShouldContain("CLAUDE_OAUTH_TOKEN_PATH=\"$SERVER2_ROOT/secrets/claude_oauth_token\"");
        remote.ShouldContain("CLAUDE_OAUTH_TOKEN_FILE=\"$CLAUDE_OAUTH_TOKEN_PATH\" \\");
        remote.ShouldContain("CLAUDE_OAUTH_TOKEN_FILE=$CLAUDE_OAUTH_TOKEN_PATH\n");
        remote.ShouldContain(": > \"$CLAUDE_OAUTH_TOKEN_PATH\"");
        remote.ShouldContain("chmod 0600 \"$CLAUDE_OAUTH_TOKEN_PATH\"");
        remote.ShouldContain("WARN ClaudeOAuthTokenAbsent");
        Order(remote, "chmod 0600 \"$CLAUDE_OAUTH_TOKEN_PATH\"", "compose_host up -d --no-build")
            .ShouldBeTrue("the file exists before compose binds it");
        foreach (var line in remote.Replace("\r\n", "\n").Split('\n').Where(line => line.Contains("CLAUDE_OAUTH_TOKEN_PATH", StringComparison.Ordinal)))
        {
            var trimmed = line.Trim();
            (trimmed.StartsWith("cat ", StringComparison.Ordinal) || trimmed.Contains("< \"$CLAUDE_OAUTH_TOKEN_PATH\"", StringComparison.Ordinal)
                || trimmed.StartsWith("cp ", StringComparison.Ordinal))
                .ShouldBeFalse("the host lane never reads the token: " + trimmed);
        }
    }

    [Test]
    public void Test_context_retains_linked_sources()
    {
        var included = !DockerStackDocuments.Excluded(Text("docker/tests/Dockerfile.dockerignore"), "tests/Shared/TestClassificationMetadata.cs");
        included.ShouldBeTrue("required-context list includes TestClassificationMetadata.cs");
    }

    [Test]
    public void Test_target_has_supported_tools()
    {
        var text = Text("docker/tests/Dockerfile");
        text.Contains("sdk:10.0", StringComparison.Ordinal).ShouldBeTrue();
        text.Contains("aspnet:9.0", StringComparison.Ordinal).ShouldBeTrue("SDK10/runtime9/Node22/pwsh/Git tool contract lacks runtime9");
        text.Contains("node:22", StringComparison.Ordinal).ShouldBeTrue();
        text.Contains("pwsh", StringComparison.Ordinal).ShouldBeTrue();
        text.Contains("git", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Test]
    public void Http_test_factory_keeps_refusing_client()
    {
        var source = Text("tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs");
        source.ShouldContain("RemoveAll");
        source.ShouldContain("RefusingSessionRunnerClient");
    }

    [Test]
    public void Default_runtime_excludes_test_payload()
    {
        var stages = DockerStackDocuments.Stages(Text("docker/session-runner-grok/Dockerfile"));
        var closure = DockerStackDocuments.Closure(stages, stages[^1].Name);
        closure.Contains("fakegrok", StringComparison.Ordinal).ShouldBeFalse();
        closure.Contains("docker-compose", StringComparison.Ordinal).ShouldBeFalse();
        closure.Contains("dockerd", StringComparison.Ordinal).ShouldBeFalse();
        closure.Contains("antiphon-dind-entrypoint.sh", StringComparison.Ordinal).ShouldBeFalse();
        closure.Contains("sudo", StringComparison.Ordinal).ShouldBeFalse();
    }

    // CARD-0604 G-40 (Cut B). The custody mechanism is a sudo grant plus two root-owned helpers.
    // It belongs to exactly one image -- the privileged persistent server2 runner -- because
    // that is the only place a tracked Mutation execution ever runs. The default runtime and the
    // receipt-probe target boot unprivileged with no dockerd and no custody root, so carrying
    // the helpers there would be a sudo grant with nothing to constrain and everything to lose.
    [Test]
    public void Default_runtime_excludes_custody_helpers()
    {
        var stages = DockerStackDocuments.Stages(Text("docker/session-runner-grok/Dockerfile"));
        foreach (var target in new[] { stages[^1].Name, "receipt-probe" })
        {
            var closure = DockerStackDocuments.Closure(stages, target);
            closure.Contains("antiphon-custody", StringComparison.Ordinal).ShouldBeFalse(target + " carries a custody helper");
            closure.Contains("sudoers", StringComparison.Ordinal).ShouldBeFalse(target + " carries a sudoers drop-in");
            closure.Contains("no-new-privs", StringComparison.Ordinal).ShouldBeFalse(target + " references the custody shim");
        }

        // And they are present exactly where they belong.
        var testing = DockerStackDocuments.Closure(stages, "session-testing");
        testing.Contains("/usr/local/bin/antiphon-custody-enter", StringComparison.Ordinal).ShouldBeTrue();
        testing.Contains("/usr/local/bin/antiphon-custody-kill", StringComparison.Ordinal).ShouldBeTrue();
        testing.Contains("/etc/sudoers.d/antiphon-custody", StringComparison.Ordinal).ShouldBeTrue();
    }

    // CARD-0661. LandingGit and the guarded removals run `git show-ref --exists`, new in Git 2.43;
    // bookworm's apt git is 2.39.5 and bookworm-backports has none. The runner image declares the
    // minimum, builds a SHA-256-pinned release at or above it, checks the installed version against
    // it at build time, and installs no apt git beside it. The docs state the same minimum.
    [Test]
    public void Runner_git_meets_the_declared_minimum()
    {
        var dockerfile = Text("docker/session-runner-grok/Dockerfile");
        var minimum = System.Text.RegularExpressions.Regex.Match(dockerfile, @"ARG GIT_MINIMUM_VERSION=(\d+\.\d+)\r?\n");
        minimum.Success.ShouldBeTrue("the runner Dockerfile declares its minimum Git version");
        var floor = Version.Parse(minimum.Groups[1].Value);
        floor.ShouldBeGreaterThanOrEqualTo(new Version(2, 43), "show-ref --exists is Git 2.43");
        var pinned = System.Text.RegularExpressions.Regex.Match(dockerfile, @"ARG GIT_VERSION=(\d+\.\d+\.\d+)\r?\n");
        pinned.Success.ShouldBeTrue("the runner Git is a pinned release");
        Version.Parse(pinned.Groups[1].Value).ShouldBeGreaterThanOrEqualTo(floor);
        System.Text.RegularExpressions.Regex.IsMatch(dockerfile, @"ARG GIT_SHA256=[0-9a-f]{64}\r?\n")
            .ShouldBeTrue("the Git tarball is pinned by SHA-256");

        var build = Stage("docker/session-runner-grok/Dockerfile", "git-build").Body;
        var verify = build.IndexOf("sha256sum -c -", StringComparison.Ordinal);
        verify.ShouldBeGreaterThan(-1);
        verify.ShouldBeLessThan(build.IndexOf("tar -xzf", StringComparison.Ordinal), "the tarball is verified before it is unpacked");
        build.Contains("sysconfdir=/etc", StringComparison.Ordinal)
            .ShouldBeTrue("the baked /etc/gitconfig must stay the system config the pinned Git reads");

        var runtime = RunnerBody();
        runtime.Contains("COPY --from=git-build /opt/git-root/usr/local/ /usr/local/", StringComparison.Ordinal).ShouldBeTrue();
        runtime.Contains("\"${GIT_MINIMUM_VERSION}\" \"$(git --version | cut -d' ' -f3)\" | sort -V -C", StringComparison.Ordinal)
            .ShouldBeTrue("the installed Git is checked against the minimum at build time");
        foreach (var stage in DockerStackDocuments.Stages(dockerfile).Where(stage => stage.Name != "node22"))
            System.Text.RegularExpressions.Regex.IsMatch(stage.Body, @"apt-get install[^\n]*\sgit(\s|\\|$)",
                    System.Text.RegularExpressions.RegexOptions.Multiline)
                .ShouldBeFalse(stage.Name + " installs Debian's git, which is below the minimum");

        Text("docs/docker-stack.md").Contains("minimum Git " + minimum.Groups[1].Value, StringComparison.Ordinal)
            .ShouldBeTrue("docs/docker-stack.md states the runner's minimum Git");
    }

    [Test]
    public void Stock_server_excludes_fixture()
    {
        var final = Stage("Dockerfile", "runtime").Body;
        final.Contains("DockerStack.Fixture", StringComparison.Ordinal).ShouldBeFalse();
        final.Contains("Mvc.Testing", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Test]
    public void Receipt_stack_is_socket_free()
    {
        var text = Text("docker-compose.delivery-fixture.yml");
        text.Contains("docker.sock", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Test]
    public void Receipt_runner_has_no_docker_tools()
    {
        var stages = DockerStackDocuments.Stages(Text("docker/session-runner-grok/Dockerfile"));
        var closure = DockerStackDocuments.Closure(stages, "receipt-probe");
        closure.Contains("docker-compose", StringComparison.Ordinal).ShouldBeFalse();
        closure.Contains("/usr/local/bin/docker", StringComparison.Ordinal).ShouldBeFalse();
        closure.Contains("dockerd", StringComparison.Ordinal).ShouldBeFalse();
        closure.Contains("sudo", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Test]
    public void Fixture_restart_policy_is_disabled() =>
        DockerStackDocuments.Service(Text("docker-compose.delivery-fixture.yml"), "antiphon")
            .Contains("restart: \"no\"", StringComparison.Ordinal).ShouldBeTrue();

    [Test] public void Fresh_Delegation__DiagnoseEnabled_is_disabled() => False(Server(), "Delegation__DiagnoseEnabled").ShouldBeTrue();
    [Test] public void Fresh_Delegation__OutputDistillerEnabled_is_disabled() => False(Server(), "Delegation__OutputDistillerEnabled").ShouldBeTrue();
    [Test] public void Fresh_Hangfire__ServerEnabled_is_disabled() => False(Server(), "Hangfire__ServerEnabled").ShouldBeTrue();
    [Test] public void Fresh_ZombieCensus__Enabled_is_disabled() => False(Server(), "ZombieCensus__Enabled").ShouldBeTrue();
    [Test] public void Fresh_WorktreeResidue__Enabled_is_disabled() => False(Server(), "WorktreeResidue__Enabled").ShouldBeTrue();
    [Test] public void Fresh_Schedules__Enabled_is_disabled() => False(Server(), "Schedules__Enabled").ShouldBeTrue();
    [Test] public void Fresh_ChannelBridge__Enabled_is_disabled() => False(Server(), "ChannelBridge__Enabled").ShouldBeTrue();
    [Test] public void Fresh_Digest__Enabled_is_disabled() => False(Server(), "Digest__Enabled").ShouldBeTrue();

    [Test]
    public void Receipt_server_has_no_socket() =>
        DockerStackDocuments.Service(Text("docker-compose.delivery-fixture.yml"), "antiphon")
            .Contains("docker.sock", StringComparison.Ordinal).ShouldBeFalse();

    [Test]
    public void Receipt_runner_has_no_socket() =>
        DockerStackDocuments.Service(Text("docker-compose.delivery-fixture.yml"), "session-runner")
            .Contains("docker.sock", StringComparison.Ordinal).ShouldBeFalse();

    private static bool Order(string text, string first, string second)
    {
        var a = text.IndexOf(first, StringComparison.Ordinal);
        var b = text.IndexOf(second, StringComparison.Ordinal);
        return a >= 0 && b >= 0 && a < b;
    }

    private static void Deny(string ignore, string path) =>
        DockerStackDocuments.Excluded(Text(ignore), path).ShouldBeTrue("effective-context sentinel " + path + " is absent");

    private static IReadOnlyList<string> EnabledUnattended()
    {
        string[] keys =
        [
            "Delegation__CheckInterpreterEnabled",
            "Delegation__DiagnoseEnabled",
            "Delegation__OutputDistillerEnabled",
            "Hangfire__ServerEnabled",
            "ZombieCensus__Enabled",
            "WorktreeResidue__Enabled",
            "Schedules__Enabled",
            "ChannelBridge__Enabled",
            "Digest__Enabled",
        ];
        return keys.Where(key => !False(Server(), key)).ToList();
    }

    private static bool HealthInstalled(string dockerfile, string block)
    {
        var command = System.Text.RegularExpressions.Regex.Match(block, "\"CMD\",\\s*\"([^\"]+)\"").Groups[1].Value;
        return Text(dockerfile).Contains(command, StringComparison.Ordinal);
    }

    private static string Revision(string dockerfile, string stage)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            Stage(dockerfile, stage).Body, @"-p:SourceRevisionId=(\$SOURCE_REVISION|unknown)");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static string Destination(string block, string name)
    {
        var spec = DockerStackDocuments.List(block, "volumes").Single(volume => volume.StartsWith(name + ":", StringComparison.Ordinal));
        return DockerStackDocuments.VolumeDestination(spec);
    }

    private static int Uid(string block)
    {
        var line = block.Split('\n').Select(item => item.Trim()).Single(item => item.StartsWith("user:", StringComparison.Ordinal));
        return int.Parse(line["user:".Length..].Trim().Trim('"').Split(':')[0]);
    }

    private static bool False(string block, string key) =>
        string.Equals(Env(block, key), "false", StringComparison.OrdinalIgnoreCase);

    private static string Env(string block, string key) => DockerStackDocuments.Env(block, key);

    private static DockerStage Stage(string file, string name) =>
        DockerStackDocuments.Stages(Text(file)).Single(stage => stage.Name == name);

    private static string RunnerBody() => Stage("docker/session-runner-grok/Dockerfile", "runtime-base").Body;

    private static string Server() => DockerStackDocuments.Service(Text("docker-compose.yml"), "antiphon");

    private static string Runner() => DockerStackDocuments.Service(Text("docker-compose.yml"), "session-runner");

    private static string Postgres() => DockerStackDocuments.Service(Text("docker-compose.yml"), "postgres");

    private static string Server2Runner() => DockerStackDocuments.Service(Text("docker-compose.server2-runner.yml"), "session-runner");

    private static string SessionTesting() => Stage("docker/session-runner-grok/Dockerfile", "session-testing").Body;

    private static IReadOnlyList<string> ComposeFiles() =>
        Directory.GetFiles(DockerStackDocuments.RepoRoot, "docker-compose*.yml")
            .Select(path => Path.GetFileName(path)!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    private static string Tests() => DockerStackDocuments.Service(Text("docker-compose.test.yml"), "tests");

    private static string Text(string relative) => DockerStackDocuments.Read(relative);
}
