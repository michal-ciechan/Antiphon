using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1742. The staging server compose is a host-daemon file. These witnesses read it.
/// They do not start a daemon, a container, or a database.
/// </summary>
[Category("Unit")]
public sealed class Server2StagingServerContractTests
{
    private const string ComposePath = "docker-compose.server2-staging-server.yml";
    private const string ScriptPath = "scripts/deploy-server2-staging-server.ps1";

    [Test]
    public void Staging_file_is_its_own_compose_project()
    {
        var text = Compose();
        text.ShouldContain("name: antiphon-server2-staging");
        text.ShouldNotContain("name: antiphon-runner");
        text.ShouldNotContain("privileged: true");
        text.ShouldNotContain("docker.sock");
        text.ShouldNotContain("external: true");
        text.ShouldNotContain("  session-runner:");
        text.ShouldNotContain("runner-state");
        text.ShouldNotContain("codex-home");
        Service("staging-server").ShouldNotContain("privileged:");
        Service("staging-postgres").ShouldNotContain("privileged:");
        Service("staging-state-init").ShouldNotContain("privileged:");
    }

    [Test]
    public void Staging_postgres_is_private_scratch_not_production()
    {
        var text = Compose();
        var postgres = Service("staging-postgres");
        postgres.ShouldContain("${ANTIPHON_STAGING_POSTGRES_IMAGE:?ANTIPHON_STAGING_POSTGRES_IMAGE is required}");
        postgres.ShouldContain("shared_preload_libraries=pg_stat_statements");
        postgres.ShouldContain("pg_stat_statements.track=top");
        postgres.ShouldContain("pg_stat_statements.max=5000");
        postgres.ShouldContain("pg_isready -U antiphon -d antiphon");
        postgres.ShouldNotContain("ports:");
        postgres.ShouldNotContain("alpine");
        text.ShouldContain("name: antiphon-server2-staging_pgdata");
        text.ShouldContain("name: antiphon-server2-staging");
        text.ShouldNotContain("antiphon_pgdata");
        text.ShouldNotContain("antiphon-server2-postgres");
        text.ShouldNotContain("dind-data");
        text.ShouldNotContain("windmill");
        text.ShouldNotContain(":5432:");
        text.ShouldNotContain("5433");
        text.ShouldNotContain("17280");
        text.ShouldNotContain("0.0.0.0");
        var server = Service("staging-server");
        server.ShouldContain("Host=staging-postgres;Port=5432;Database=antiphon;Username=antiphon;Password=${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}");
        server.ShouldNotContain("localhost");
        server.ShouldNotContain("antiphon_dev");
    }

    [Test]
    public void Staging_hangfire_is_on_and_census_legacy_bridge_and_digest_are_off()
    {
        var server = Service("staging-server");
        Env(server, "Hangfire__ServerEnabled").ShouldBe("true");
        Env(server, "ZombieCensus__Enabled").ShouldBe("false");
        Env(server, "WorktreeLegacyRelease__Enabled").ShouldBe("false");
        Env(server, "WorktreeLegacyRelease__Execute").ShouldBe("false");
        Env(server, "RunnerWorktreeRetention__Enabled").ShouldBe("false");
        Env(server, "RunnerWorktreeRetention__Execute").ShouldBe("false");
        Env(server, "ChannelBridge__Enabled").ShouldBe("false");
        Env(server, "Digest__Enabled").ShouldBe("false");
        Env(server, "SessionRunner__Enabled").ShouldBe("false");
        Env(server, "PhoneHomeRunner__Enabled").ShouldBe("false");
    }

    [Test]
    public void Staging_schedules_interpreter_diagnose_and_distiller_use_linux_paths()
    {
        var server = Service("staging-server");
        Env(server, "Schedules__Enabled").ShouldBe("true");
        Env(server, "Delegation__CheckInterpreterEnabled").ShouldBe("true");
        Env(server, "Delegation__DiagnoseEnabled").ShouldBe("true");
        Env(server, "Delegation__OutputDistillerEnabled").ShouldBe("true");
        Env(server, "Delegation__CheckInterpreterWorkingDirectory").ShouldBe("/state/check-interpreter");
        Env(server, "Delegation__DiagnoseWorkingDirectory").ShouldBe("/state/diagnose");
        Env(server, "Delegation__OutputDistillerWorkingDirectory").ShouldBe("/state/output-distiller");
        var text = Compose();
        text.ShouldNotContain(".exe");
        text.ShouldNotContain(".cmd");
        text.Contains("C:\\", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("C:/", StringComparison.Ordinal).ShouldBeFalse();
        text.ShouldNotContain("LOCALAPPDATA");
        Env(server, "Agents__Definitions__grok__Exe").ShouldBe("/usr/local/bin/grok");
        Env(server, "Agents__Definitions__claude__Exe").ShouldBe("/usr/local/bin/claude");
        Env(server, "Agents__Definitions__codex__Exe").ShouldBe("/usr/local/bin/codex");
        Env(server, "Agents__Definitions__raw-pwsh__Exe").ShouldBe("/usr/local/bin/pwsh");
        Env(server, "Agents__Definitions__raw-sh__Exe").ShouldBe("/bin/sh");
    }

    [Test]
    public void Staging_residue_is_on_for_this_projects_worktrees_only()
    {
        var server = Service("staging-server");
        Env(server, "WorktreeResidue__Enabled").ShouldBe("true");
        Env(server, "WorktreeResidue__Execute").ShouldBe("false");
        Env(server, "Git__WorktreeBasePath").ShouldBe("/work/worktrees");
        Env(server, "Git__WorkspacePath").ShouldBe("/work/repos");
        Compose().ShouldContain("name: antiphon-server2-staging_work");
        server.ShouldContain("work:/work");
        server.ShouldNotContain("C:\\Antiphon\\worktrees");
        Service("staging-state-init").ShouldContain("/work/worktrees");
    }

    [Test]
    public void Staging_key_ring_is_on_this_projects_state_volume()
    {
        var server = Service("staging-server");
        Env(server, "AgentTui__KeyRingPath").ShouldBe("/state/keyring");
        server.ShouldContain("server-state:/state");
        Compose().ShouldContain("name: antiphon-server2-staging_server-state");
        var init = Service("staging-state-init");
        init.ShouldContain("/state/keyring");
        init.ShouldContain("/state/check-interpreter");
        init.ShouldContain("/state/diagnose");
        init.ShouldContain("/state/output-distiller");
        init.ShouldContain("ForeignStateOwner");
        init.ShouldContain("exit 42");
        init.ShouldContain("1654");
    }

    [Test]
    public void Staging_acceptance_is_loopback_health_and_version()
    {
        var server = Service("staging-server");
        server.ShouldContain("127.0.0.1:17420:8080");
        server.ShouldContain("http://127.0.0.1:8080/health");
        var script = Script();
        script.ShouldContain("http://127.0.0.1:17420/health");
        script.ShouldContain("http://127.0.0.1:17420/api/version");
        script.ShouldContain("acceptance-health");
        script.ShouldContain("acceptance-version");
    }

    [Test]
    public void Staging_phone_home_origin_vhost_apphost_and_runner_stay()
    {
        var text = Compose();
        text.ShouldNotContain("PhoneHome__ServerOrigin");
        text.ShouldNotContain("codeperf.net");
        text.ShouldNotContain("deploy-server2.ps1");
        var runner = DockerStackDocuments.Service(
            DockerStackDocuments.Read("docker-compose.server2-runner.yml"), "session-runner");
        DockerStackDocuments.Env(runner, "SessionRunner__Herdr__Enabled").ShouldBe("false");
        var deploy = DockerStackDocuments.Read("scripts/deploy-server2.ps1");
        deploy.ShouldNotContain("server2-staging");
        deploy.ShouldNotContain("antiphon-server2-staging");
        var root = DockerStackDocuments.Service(DockerStackDocuments.Read("docker-compose.yml"), "antiphon");
        DockerStackDocuments.Env(root, "Hangfire__ServerEnabled").ShouldBe("false");
        DockerStackDocuments.Env(root, "ZombieCensus__Enabled").ShouldBe("false");
    }

    [Test]
    public void Staging_script_plans_without_docker_and_refuses_a_foreign_daemon()
    {
        var script = Script();
        script.ShouldContain("Default is -WhatIf");
        script.ShouldContain("planOnly = -not $Execute");
        script.ShouldContain("not-server2-host");
        script.ShouldContain("ambiguous-mode");
        script.ShouldContain("bind-any");
        script.ShouldContain("volume-forbidden");
        script.ShouldContain("dev-password");
        script.ShouldContain("env-mode");
        script.ShouldContain("env-inside-repo");
        script.ShouldContain("/.dockerenv");
        script.ShouldContain("antiphon-server2-staging");
        script.ShouldNotContain("deploy-server2.ps1");
        DockerStackDocuments.Read("docs/docker-stack.md").ShouldContain(
            "scripts/deploy-server2-staging-server.ps1` defaults to `-WhatIf`");
        DockerStackDocuments.Read("docs/testing-and-build.md").ShouldContain(
            "docker-compose.server2-staging-server.yml");
    }

    private static string Compose() => DockerStackDocuments.Read(ComposePath);

    private static string Script() => DockerStackDocuments.Read(ScriptPath);

    private static string Service(string name) => DockerStackDocuments.Service(Compose(), name);

    private static string Env(string block, string key) => DockerStackDocuments.Env(block, key);
}
