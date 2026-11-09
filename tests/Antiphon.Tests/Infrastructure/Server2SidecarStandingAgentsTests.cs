using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1778 S2. Production compose pins for the sidecar Claude home, the server
/// executable check, the shared instructions file, and the standing-agent root.
/// Text only: no daemon, container, or database.
/// </summary>
[Category("Unit")]
public sealed class Server2SidecarStandingAgentsTests
{
    private const string ComposePath = "docker-compose.server2-server.yml";
    private const string StagingComposePath = "docker-compose.server2-staging-server.yml";
    private const string ScriptPath = "scripts/deploy-server2-server.ps1";
    private const string DocPath = "docs/docker-stack.md";
    private const string Heading = "### Standing agents and specialists on the sidecar (CARD-1778)";

    [Test]
    public void Sidecar_claude_config_dir_is_on_runner_state()
    {
        var sidecar = Service("server-session-runner");
        var server = Service("server");
        sidecar.ShouldContain("CLAUDE_CONFIG_DIR:");
        DockerStackDocuments.Env(sidecar, "CLAUDE_CONFIG_DIR").ShouldBe("/state/claude");
        server.ShouldNotContain("CLAUDE_CONFIG_DIR");

        var mounts = VolumeMounts(sidecar);
        mounts.Any(mount =>
            mount.Source == "runner-state"
            && mount.Destination == "/state"
            && !mount.ReadOnly).ShouldBeTrue();
        Service("server-state-init").ShouldContain("/runner-state/claude");
    }

    [Test]
    public void Server_leaves_executable_checks_to_the_sidecar()
    {
        var server = Service("server");
        server.ShouldContain("SessionRunner__VerifyExecutablesOnServer:");
        DockerStackDocuments.Env(server, "SessionRunner__VerifyExecutablesOnServer").ShouldBe("false");
        DockerStackDocuments.Read(StagingComposePath).ShouldNotContain("SessionRunner__VerifyExecutablesOnServer");
    }

    [Test]
    public void Orchestrator_instructions_file_is_on_the_shared_work_volume()
    {
        const string path = "/work/.antiphon/orchestrator-instructions.md";
        var server = Service("server");
        var sidecar = Service("server-session-runner");
        server.ShouldContain("Delegation__OrchestratorInstructions__Path:");
        DockerStackDocuments.Env(server, "Delegation__OrchestratorInstructions__Path").ShouldBe(path);

        var serverMounts = VolumeMounts(server);
        var sidecarMounts = VolumeMounts(sidecar);
        var shared = serverMounts.Where(mount =>
            PathIsUnder(path, mount.Destination)
            && !mount.ReadOnly
            && sidecarMounts.Any(other =>
                !other.ReadOnly
                && string.Equals(other.Source, mount.Source, StringComparison.Ordinal)
                && string.Equals(other.Destination, mount.Destination, StringComparison.Ordinal))).ToList();
        shared.Count.ShouldBeGreaterThan(0, path);

        var init = Service("server-state-init");
        init.ShouldContain("/work/.antiphon");
        init.ShouldContain("chmod 0755");
        InitMkdirTargets(init).ShouldContain("/work/.antiphon");
        InitChmodTargets(init).ShouldContain("/work/.antiphon");
    }

    [Test]
    public void Agents_root_is_created_and_documented()
    {
        var init = Service("server-state-init");
        init.ShouldContain("/work/agents");
        InitMkdirTargets(init).ShouldContain("/work/agents");

        var section = StandingAgentsSection();
        section.ShouldContain("/work/agents/<name>");
        section.ShouldContain("workingDirectory");
        section.ShouldContain("sessionBackend");
        section.ShouldContain("PtyHost");
        section.ShouldContain("herdrWorkspaceLabel");
        section.ShouldContain("herdrTabLabel");
        section.ShouldContain("claude auth login");
        section.ShouldContain("CLAUDE_CONFIG_DIR=/state/claude");
        section.ShouldContain("-u 1654:1654");
        section.ShouldNotContain("ANTHROPIC_API_KEY");
        section.ShouldNotContain("CLAUDE_CODE_OAUTH_TOKEN");
        section.ShouldContain("ANTIPHON_ORCHESTRATOR_INSTRUCTIONS_URL");
        section.ShouldContain("fallback");
        section.ShouldContain("CARD-1695");
    }

    [Test]
    public void Deploy_plan_prints_the_new_keys()
    {
        var script = DockerStackDocuments.Read(ScriptPath);
        script.ShouldContain("CLAUDE_CONFIG_DIR: /state/claude");
        script.ShouldContain("SessionRunner__VerifyExecutablesOnServer: \"false\"");
        script.ShouldContain("Delegation__OrchestratorInstructions__Path: /work/.antiphon/orchestrator-instructions.md");
    }

    private static string StandingAgentsSection()
    {
        var doc = DockerStackDocuments.Read(DocPath);
        var start = doc.IndexOf(Heading, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var next = doc.IndexOf("\n### ", start + Heading.Length, StringComparison.Ordinal);
        return next < 0 ? doc[start..] : doc[start..next];
    }

    private static string Service(string name) =>
        DockerStackDocuments.Service(DockerStackDocuments.Read(ComposePath), name);

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

    private static string InitMkdirTargets(string init)
    {
        const string marker = "for d in ";
        var start = init.IndexOf(marker, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = init.IndexOf(';', start);
        return init[start..end];
    }

    private static string InitChmodTargets(string init)
    {
        const string marker = "chmod 0755 ";
        var start = init.IndexOf(marker, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = init.IndexOf(';', start);
        return init[start..end];
    }
}
