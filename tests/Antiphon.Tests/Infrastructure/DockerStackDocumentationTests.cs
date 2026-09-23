using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-0604 S6, R-6. The docs name the new shape AND the old sentences are gone. A superseded
// paragraph left standing is worse than a missing one: the next reader follows it.
[Category("Unit")]
public sealed class DockerStackDocumentationTests
{
    [Test]
    public void Docker_stack_doc_names_the_nested_daemon()
    {
        var text = Read("docs/docker-stack.md");
        text.ShouldContain("nested");
        text.ShouldContain("docker-compose.server2-runner.yml");
        text.ShouldContain("privileged");
        text.ShouldContain("SiblingDaemonRefused");
        text.ShouldContain("dind-entrypoint.sh");

        // The retired sibling lane and its variable must not still be offered as instructions.
        text.Contains("Pass `DOCKER_SOCKET_GID` from the socket's group", StringComparison.Ordinal)
            .ShouldBeFalse("the socket-gid instruction is gone with the sibling lane");
        text.Contains("is the only application override that mounts the socket", StringComparison.Ordinal)
            .ShouldBeFalse("no compose file mounts a socket any more");
        text.Contains("SourceLanding Mutation stays on the Windows local path (CARD-0598)", StringComparison.Ordinal)
            .ShouldBeFalse("that sentence is re-aimed at the two backends, not left as-is");
        text.ShouldContain("windows-job-v1");
        text.ShouldContain("linux-cgroup-v1");
        text.ShouldContain("not a custody receipt");
        // CARD-0628 G-17c.
        text.ShouldContain("Claude Code 2.1.280");
        text.ShouldContain("1e08503d");
        // CARD-0628 Round D.
        text.ShouldContain("CLAUDE_OAUTH_TOKEN_FILE");
        text.ShouldContain("/run/antiphon/claude-oauth-token");
    }

    [Test]
    public void Credentials_doc_names_the_deploy_key_custody()
    {
        var text = Read("docs/agent-credentials.md");
        text.ShouldContain("antiphon-server2-runner");
        text.ShouldContain("/run/antiphon/deploy-key");
        text.ShouldContain("tmpfs");
        text.ShouldContain("/run/secrets/phone-home");
        text.ShouldContain("DeployKeyMissing");
        text.ShouldContain("PhoneHomeSecretMissing");
        text.ShouldContain("ssh.github.com:443");

        // Names and locations only. A real key or secret in a tracked doc is the failure this
        // paragraph exists to prevent.
        text.Contains("BEGIN OPENSSH PRIVATE KEY", StringComparison.Ordinal).ShouldBeFalse();
        System.Text.RegularExpressions.Regex.IsMatch(text, @"\b[0-9a-f]{64}\b")
            .ShouldBeFalse("a 64-hex literal in the credentials doc looks like the phone-home secret");
    }

    [Test]
    public void Card_0590_D6_is_annotated_superseded()
    {
        var text = Read("docs/superpowers/plans/2026-09-21-card-0590-self-contained-docker-stack-plan.md");
        text.ShouldContain("Superseded 2026-09-22 by CARD-0604");
        // Both the re-aimed decisions carry the annotation IN PLACE, and D-14 is cited as standing.
        text.ShouldContain("CARD-0604 D-1/D-5/D-6");
        text.ShouldContain("CARD-0604 D-12/D-17");
        text.ShouldContain("D-14 below is untouched and still binding");
    }

    [Test]
    public void Ops_http_names_runner_status_and_runner_id()
    {
        var text = Read("docs/ops-http.md");
        text.ShouldContain("/api/session-runners/{runnerId}/status");
        text.ShouldContain("dispatchEligible");
        text.ShouldContain("`runnerId`");
        // The distinction that costs a wasted dispatch if it is missed.
        text.ShouldContain("`available` alone is not enough to dispatch");
    }

    [Test]
    public void Testing_doc_names_the_production_enablement_steps()
    {
        var text = Read("docs/testing-and-build.md");
        text.ShouldContain("AllowedRunnerId=server2");
        text.ShouldContain("AllowDelegatedTasks=true");
        text.ShouldContain("restart-apphost.ps1");
        text.ShouldContain("delegate.ps1 -Runner server2");
        // The rule that caused CARD-0358: enablement runs from the main checkout, not a worktree.
        text.ShouldContain("never a worktree");
        text.ShouldContain("--ff-only");
    }

    [Test]
    public void Agent_kinds_doc_names_the_raw_allow_list()
    {
        var text = Read("docs/agent-kinds.md");
        text.ShouldContain("runner-bound");
        text.ShouldContain("/usr/local/bin/pwsh");
        // CARD-0628 G-17a: the sentence changes with the image, and the old one is gone.
        text.ShouldContain("Grok and Claude Code are the agents the image carries");
        text.ShouldContain("Codex is not installed there and stays refused");
        text.ShouldContain("are Grok or Claude Code (CARD-0628)");
        text.Contains("Grok is the only agent the image carries", StringComparison.Ordinal)
            .ShouldBeFalse("the superseded sentence is not left standing");
        text.ShouldContain("phone_home_env_refused");
        text.ShouldContain("phone_home_remote_control_refused");
    }

    // CARD-0628 G-17: the operator's auth decision in names and locations only -- the setup-token
    // is primary, the interactive login the fallback, an API key never -- and no secret-shaped text.
    [Test]
    public void Credentials_doc_names_the_claude_login()
    {
        var text = Read("docs/agent-credentials.md");
        text.ShouldContain("CLAUDE_CODE_OAUTH_TOKEN");
        text.ShouldContain("claude setup-token");
        text.ShouldContain("/state/claude");
        text.ShouldContain("claude auth login");
        text.ShouldContain("provider_sign_in_required");
        text.ShouldContain("phone_home_env_refused");
        text.ShouldContain("`ANTHROPIC_API_KEY` is never a fallback");
        text.IndexOf("**Primary:** `CLAUDE_CODE_OAUTH_TOKEN`", StringComparison.Ordinal)
            .ShouldBeInRange(0, text.IndexOf("**Fallback:** interactive login store", StringComparison.Ordinal));
        // Round D: the file-mount custody, and the superseded environment pass-through is gone.
        text.ShouldContain("antiphon/server2/claude-oauth-token");
        text.ShouldContain("/home/mc/antiphon-server2/secrets/claude_oauth_token");
        text.ShouldContain("/run/antiphon/claude-oauth-token");
        text.Contains("passes `CLAUDE_CODE_OAUTH_TOKEN` through from that environment", StringComparison.Ordinal)
            .ShouldBeFalse("the compose environment pass-through is superseded");
        text.Contains("sk-ant-", StringComparison.Ordinal).ShouldBeFalse("no Anthropic key or token fragment");
        System.Text.RegularExpressions.Regex.IsMatch(text, @"\b[0-9a-f]{64}\b")
            .ShouldBeFalse("the Claude digest lives in the Dockerfile and docker-stack.md, never here");
    }

    // CARD-0628 G-17b.
    [Test]
    public void Ops_http_names_provider_auth()
    {
        var text = Read("docs/ops-http.md");
        text.ShouldContain("/api/session-runners/{runnerId}/provider-auth/{provider}");
        text.ShouldContain("phone_home_unavailable");
        text.ShouldContain("phone_home_unsupported_operation");
        text.ShouldContain("Worktree + Grok or Claude Code");
        text.Contains("Worktree + Grok only", StringComparison.Ordinal).ShouldBeFalse();
    }

    /// <summary>
    /// Prose wraps. Asserting a phrase against the raw bytes makes the guard fail the moment a
    /// paragraph is rewrapped, which says nothing about whether the doc still means what it must,
    /// so every assertion here reads the text with runs of whitespace collapsed to one space.
    /// </summary>
    private static string Read(string relative) =>
        System.Text.RegularExpressions.Regex.Replace(DockerStackDocuments.Read(relative), @"\s+", " ");
}
