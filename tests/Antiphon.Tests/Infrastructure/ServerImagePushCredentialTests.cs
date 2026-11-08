using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1776. The Linux server image bakes the runner's non-secret credential helper,
/// and the land-credential compose fragment names the read-only mounts. These witnesses
/// read files. They do not start a daemon, a container, or a database, and they do not
/// read a token.
/// </summary>
[Category("Unit")]
public sealed class ServerImagePushCredentialTests
{
    private const string ComposePath = "docker-compose.server2-land-credential.yml";
    private const string ScriptPath = "scripts/server2-repository-mirrors.ps1";
    private const string GitConfigPath = "docker/server/gitconfig";

    [Test]
    public void Server_image_bakes_the_runner_credential_helper()
    {
        var dockerfile = DockerStackDocuments.Read("Dockerfile");
        var runtime = DockerStackDocuments.Stages(dockerfile).Single(stage => stage.Name == "runtime");
        runtime.Body.ShouldContain(
            "COPY docker/session-runner-grok/github-credential.sh /usr/local/bin/antiphon-github-credential");
        runtime.Body.ShouldContain(
            "COPY docker/session-runner-grok/github_known_hosts /etc/antiphon/github_known_hosts");
        runtime.Body.ShouldContain("COPY docker/server/gitconfig /etc/gitconfig");
        runtime.Body.ShouldContain("sh -n /usr/local/bin/antiphon-github-credential");
        runtime.Body.ShouldContain("chmod 0755 /usr/local/bin/antiphon-github-credential");
        dockerfile.Contains("GH_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
        dockerfile.Contains("GITHUB_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
        dockerfile.Contains("ghp_", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Test]
    public void Server_gitconfig_names_the_helper_and_not_an_ssh_push()
    {
        var path = Path.Combine(DockerStackDocuments.RepoRoot, GitConfigPath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).ShouldBeTrue(path);
        var text = File.ReadAllText(path);
        text.ShouldContain("helper = /usr/local/bin/antiphon-github-credential");
        text.ShouldContain("useHttpPath = true");
        text.ShouldContain("username = x-access-token");
        text.ShouldContain("[credential \"https://github.com\"]");
        text.Contains("pushInsteadOf", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("GH_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("GITHUB_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Test]
    public void Land_credential_compose_mounts_the_token_directory_and_allow_list_by_variable()
    {
        var path = Path.Combine(DockerStackDocuments.RepoRoot, ComposePath);
        File.Exists(path).ShouldBeTrue(path);
        var text = DockerStackDocuments.Read(ComposePath);
        text.ShouldContain(
            "${RUNNER_GITHUB_TOKEN_DIR:?RUNNER_GITHUB_TOKEN_DIR is required}:/run/antiphon/github-token:ro");
        text.ShouldContain(
            "${SERVER_PUSH_ALLOW_LIST:?SERVER_PUSH_ALLOW_LIST is required}:/run/antiphon/push-allow-list:ro");
        text.ShouldContain("Git__PushCredentialTokenFile: /run/antiphon/github-token/token");
        text.ShouldContain("Git__PushCredentialPolicyPath: /run/antiphon/push-allow-list");
        text.Contains("GH_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("GITHUB_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("privileged: true", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("docker.sock", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("ghp_", StringComparison.Ordinal).ShouldBeFalse();
        DockerStackDocuments.Service(text, "server").ShouldContain("/run/antiphon/github-token:ro");
    }

    [Test]
    public void Credentials_doc_records_server_container_custody_and_mirror_layout()
    {
        var credentials = DockerStackDocuments.Read("docs/agent-credentials.md");
        credentials.ShouldContain("### Linux server land credential (CARD-1776)");
        credentials.ShouldContain("RUNNER_GITHUB_TOKEN_DIR");
        credentials.ShouldContain("/run/antiphon/github-token:ro");
        credentials.ShouldContain("/usr/local/bin/antiphon-github-credential");
        credentials.ShouldContain("push_credential_absent");
        credentials.ShouldContain("/work/repos/<name>");
        credentials.ShouldContain("gitRepositoryUrl");
        credentials.ShouldContain("before column");
        var sectionStart = credentials.IndexOf("### Linux server land credential (CARD-1776)", StringComparison.Ordinal);
        sectionStart.ShouldBeGreaterThanOrEqualTo(0);
        var section = credentials[sectionStart..];
        var nextHeading = section.IndexOf("\n### ", 4, StringComparison.Ordinal);
        if (nextHeading > 0)
            section = section[..nextHeading];
        section.Contains("GH_TOKEN=", StringComparison.Ordinal).ShouldBeFalse();
        section.Contains("GITHUB_TOKEN=", StringComparison.Ordinal).ShouldBeFalse();
        Regex.IsMatch(credentials, @"ghp_[A-Za-z0-9]{20,}").ShouldBeFalse();
        var stack = DockerStackDocuments.Read("docs/docker-stack.md");
        stack.ShouldContain("/work/repos/<name>");
        stack.ShouldContain("CARD-1776");
        stack.ShouldContain("docker-compose.server2-land-credential.yml");
    }

    [Test]
    public void Mirror_script_defaults_to_whatif_and_refuses_a_missing_credential()
    {
        var path = Path.Combine(DockerStackDocuments.RepoRoot, ScriptPath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).ShouldBeTrue(path);
        var text = File.ReadAllText(path);
        text.ShouldContain("Default is -WhatIf");
        text.ShouldContain("credential-absent");
        text.ShouldContain("remote-unrecorded");
        text.ShouldContain("ambiguous-mode");
        text.ShouldContain("volume-forbidden");
        text.ShouldContain("secret-in-table");
        text.ShouldContain("path-forbidden");
        text.ShouldContain("/work/repos/");
        text.ShouldContain("git clone --");
        text.ShouldContain("GIT_TERMINAL_PROMPT");
        text.ShouldContain("rollback restores the before column");
        text.Contains("GH_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("GITHUB_TOKEN", StringComparison.Ordinal).ShouldBeFalse();
        text.All(character => character <= 127).ShouldBeTrue();
    }
}
