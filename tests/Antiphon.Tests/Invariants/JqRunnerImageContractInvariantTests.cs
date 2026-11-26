using System.Text.RegularExpressions;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
[Timeout(600_000)]
public sealed class JqRunnerImageContractInvariantTests
{
    [Test]
    public void Pinned_download_is_verified_before_root_owned_install_in_every_runner_target()
    {
        const string version = JqRunnerImageContractTests.Version;
        const string sha256 = JqRunnerImageContractTests.Sha256;
        var image = JqRunnerImageContractTests.Read("docker/session-runner-grok/Dockerfile");
        var stages = DockerStackDocuments.Stages(image);
        var runtime = stages.Single(stage => stage.Name == "runtime-base").Body;
        runtime.ShouldContain("ARG JQ_VERSION=" + version + "\n");
        runtime.ShouldContain("ARG JQ_SHA256=" + sha256 + "\n");
        var install = Regex.Match(runtime, @"(?ms)^RUN .*?jq-linux-amd64.*?(?=^\S|\z)").Value;
        install.ShouldContain("https://github.com/jqlang/jq/releases/download/jq-${JQ_VERSION}/jq-linux-amd64");
        install.ShouldContain("&& echo \"${JQ_SHA256}  /tmp/jq-download/jq\" | sha256sum -c -");
        install.ShouldContain("&& install -o root -g root -m 0755 /tmp/jq-download/jq /usr/local/bin/jq");
        install.IndexOf("sha256sum -c -", StringComparison.Ordinal).ShouldBeLessThan(
            install.IndexOf("install -o root", StringComparison.Ordinal), "a failed digest must prevent installation");
        install.ShouldContain("&& test \"$(/usr/local/bin/jq --version)\" = \"jq-${JQ_VERSION}\"");
        install.ShouldContain("&& rm -rf /tmp/jq-download");
        foreach (var stage in stages)
            Regex.IsMatch(stage.Body, @"(?s)apt-get install[^\n]*(?:\\\n[^\n]*)*\bjq\b").ShouldBeFalse("no apt jq in " + stage.Name);
        foreach (var target in new[] { "runtime", "receipt-probe", "session-testing" })
            DockerStackDocuments.Closure(stages, target).ShouldContain("/tmp/jq-download/jq /usr/local/bin/jq");
        JqRunnerImageContractTests.Read("docs/docker-stack.md").ShouldContain("jq " + version + " (static jq-linux-amd64, SHA-256 `" + sha256
            + "`, verified before installing root-owned mode 755 at `/usr/local/bin/jq`)");
        JqRunnerImageContractTests.Read("docker/session-runner-grok/verify-codex-image.sh").ShouldContain("JQ_VERSION=" + version + "\n");
    }
}
