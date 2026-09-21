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
    public void Testing_runner_has_explicit_socket()
    {
        var sockets = (Runner() + Override()).Split('\n').Count(line => line.Contains("docker.sock", StringComparison.Ordinal));
        sockets.ShouldBe(1);
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

    private static string Override() => DockerStackDocuments.Service(Text("docker-compose.session-testing.yml"), "session-runner");

    private static string Tests() => DockerStackDocuments.Service(Text("docker-compose.test.yml"), "tests");

    private static string Text(string relative) => DockerStackDocuments.Read(relative);
}
