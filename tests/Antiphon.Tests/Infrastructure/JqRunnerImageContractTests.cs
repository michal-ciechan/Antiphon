using System.Text.RegularExpressions;
using Antiphon.Tests.Scripts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class JqRunnerImageContractTests
{
    private const string Version = "1.7.1";
    private const string Sha256 = "5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5";

    [Test]
    public void Pinned_download_is_verified_before_root_owned_install_in_every_runner_target()
    {
        var image = Read("docker/session-runner-grok/Dockerfile");
        var stages = DockerStackDocuments.Stages(image);
        var runtime = stages.Single(stage => stage.Name == "runtime-base").Body;
        runtime.ShouldContain("ARG JQ_VERSION=" + Version + "\n");
        runtime.ShouldContain("ARG JQ_SHA256=" + Sha256 + "\n");
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
        Read("docs/docker-stack.md").ShouldContain("jq " + Version + " (static jq-linux-amd64, SHA-256 `" + Sha256
            + "`, verified before installing root-owned mode 755 at `/usr/local/bin/jq`)");
    }

    [Test]
    public void Qualification_grades_the_jq_row_for_both_targets()
    {
        var wrapper = Read("scripts/verify-card0660-codex-image.ps1");
        wrapper.ShouldContain("'jq-version' = 'unknown'");
        wrapper.ShouldContain("$rows['jq-version'] = Invoke-Probe 'jq-version' '1654:1654' @()");
        Read("docker/session-runner-grok/verify-codex-image.sh").ShouldContain("JQ_VERSION=" + Version + "\n");
    }

    [Test]
    [Arguments("jq-1.7.1", 0, "", "ok")]
    [Arguments("jq-1.7", 0, "", "fail")]
    [Arguments("jq-1.7.0", 0, "", "fail")]
    [Arguments("jq-1.7.10", 0, "", "fail")]
    [Arguments("jq-1.8.0", 0, "", "fail")]
    [Arguments("jq-1.7.1-beta", 0, "", "fail")]
    [Arguments("jq-1x7x1", 0, "", "fail")]
    [Arguments("jq-1.7.1 extra", 0, "", "fail")]
    [Arguments("jq-1.7.1\nextra", 0, "", "fail")]
    [Arguments("", 0, "", "fail")]
    [Arguments("jq-1.7.1", 1, "", "fail")]
    [Arguments("jq-1.7.1", 0, "warning", "fail")]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Version_row_accepts_only_exact_successful_pin_without_stderr(string output, int exitCode, string error, string outcome)
    {
        var probe = Read("docker/session-runner-grok/verify-codex-image.sh");
        var row = Regex.Match(probe, @"(?ms)^  jq-version\)\n(?<body>.*?)^    ;;$");
        row.Success.ShouldBeTrue("the image verifier exposes a jq-version row");
        var script = "JQ_VERSION=" + Version + "\ncandidate='" + output + "'\ncandidate_exit=" + exitCode + "\ncandidate_error='" + error + "'\n" + """
            PROBE_HOME=$(mktemp -d /tmp/c927-version-XXXXXX) || exit 2
            trap 'rm -f "$PROBE_HOME/jq-version.err"; rmdir "$PROBE_HOME"' EXIT
            env() { printf '%s\n' "$candidate"; printf '%s' "$candidate_error" >&2; return "$candidate_exit"; }
            need_uid() { [ "$1" = 1654 ] || exit 2; }
            result() { printf 'C660_ROW jq-version %s %s\n' "$1" "$2"; [ "$1" = ok ] && exit 0; exit 1; }
            """ + "\n" + row.Groups["body"].Value;
        RemoteScriptContractTests.LinuxShell(script).ShouldContain("C660_ROW jq-version " + outcome + " ");
    }

    private static string Read(string path) => DockerStackDocuments.Read(path).Replace("\r\n", "\n");
}
