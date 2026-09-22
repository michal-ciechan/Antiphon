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
        text.ShouldContain("Grok is the only agent the image carries");
    }

    private static string Read(string relative) => DockerStackDocuments.Read(relative);
}
