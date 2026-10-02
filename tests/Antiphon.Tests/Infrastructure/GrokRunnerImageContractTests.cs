using System.Text.RegularExpressions;
using Antiphon.Tests.Scripts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class GrokRunnerImageContractTests
{
    private const string Version = "1.0.41";

    [Test]
    public void Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers()
    {
        var image = Read("docker/session-runner-grok/Dockerfile");
        var runtime = DockerStackDocuments.Stages(image).Single(stage => stage.Name == "runtime-base").Body;
        var installed = Regex.Match(runtime, @"https://x\.ai/cli/install\.sh \| env HOME=/tmp/grok-install GROK_BIN_DIR=/usr/local/bin bash -s (\d+\.\d+\.\d+)");
        installed.Success.ShouldBeTrue();
        installed.Groups[1].Value.ShouldBe(Version, "only the captured 120x30 dashboard version is qualified");
        runtime.ShouldContain("/usr/local/bin/grok --version | grep -F '" + Version + "'");
        runtime.ShouldContain("install -m 0755 /tmp/grok-install/.grok/downloads/grok-linux-x86_64 /usr/local/bin/grok");

        foreach (var path in new[] { "docs/docker-stack.md", "docs/bootstrap.md", "docs/testing-and-build.md", "docs/agent-kinds.md", "docs/ai-agent-tui-configuration.md", "tests/fixtures/card0490-linux/README.md" })
        {
            var doc = Read(path);
            doc.ShouldContain("Grok " + Version, customMessage: path);
            doc.ShouldNotContain("Grok 1.0.40", customMessage: path + " must not advertise the previous image pin");
        }

        Read("docs/agent-kinds.md").ShouldContain("120x30 Grok Build " + Version);
        Read("tests/fixtures/card0490-linux/card0490-live.example.json").ShouldContain("\"grokVersion\": \"" + Version + "\"");
        var phoneHome = Read("scripts/verify-phone-home-grok.ps1");
        phoneHome.ShouldContain("grokVersion = '" + Version + "'");
        phoneHome.ShouldContain("'1\\.0\\.41'");
        phoneHome.ShouldNotContain("1.0.40");

        var probe = Read("docker/session-runner-grok/verify-codex-image.sh");
        probe.ShouldContain("GROK_VERSION=" + Version + "\n");
        var row = GrokRow(probe);
        row.ShouldContain("need_uid 1654");
        row.ShouldContain("env HOME=$PROBE_HOME GROK_HOME=$PROBE_HOME/grok /usr/local/bin/grok --version");
        row.ShouldContain("[ $code -eq 0 ] || result fail");
        row.ShouldContain("[ ! -s \"$PROBE_HOME/grok-version.err\" ] || result fail");
        var wrapper = Read("scripts/verify-card0660-codex-image.ps1");
        wrapper.ShouldContain("'grok-version' = 'unknown'");
        wrapper.ShouldContain("$rows['grok-version'] = Invoke-Probe 'grok-version' '1654:1654' @()");
    }

    [Test]
    [Arguments("grok 1.0.41 (4220f3b224a6)", 0, "ok")]
    [Arguments("grok 1.0.41 (4220f3b224a6) [stable]", 0, "ok")]
    [Arguments("grok 1.0.41 (abcdef123456)", 0, "ok")]
    [Arguments("grok 1.0.41 (abcdef123456) [stable]", 0, "ok")]
    [Arguments("grok 1.0.40 (abcdef123456) [stable]", 0, "fail")]
    [Arguments("grok 1.0.410 (abcdef123456) [stable]", 0, "fail")]
    [Arguments("grok 1.0.42 (abcdef123456) [stable]", 0, "fail")]
    [Arguments("grok 1.0.41-beta (abcdef123456)", 0, "fail")]
    [Arguments("grok 1x0x41 (abcdef123456)", 0, "fail")]
    [Arguments("grok 1.0.41 (abcdef123456) [alpha]", 0, "fail")]
    [Arguments("grok 1.0.41 (abcdef123456) [stable]", 1, "fail")]
    [Arguments("", 0, "fail")]
    [Arguments("grok 1.0.41", 0, "fail")]
    [Arguments("grok 1.0.41 ()", 0, "fail")]
    [Arguments("grok 1.0.41 (abcdef1234) [stable]", 0, "fail")]
    [Arguments("grok 1.0.41 (abcdef12345)", 0, "fail")]
    [Arguments("grok 1.0.41 (abcdef1234567)", 0, "fail")]
    [Arguments("grok 1.0.41 (abcdef12345g)", 0, "fail")]
    [Arguments("grok 1.0.41 (ABCDEF123456)", 0, "fail")]
    [Arguments("grok 1.0.41 abcdef123456", 0, "fail")]
    [Arguments("grok 1.0.41 (abcdef123456) extra", 0, "fail")]
    [Arguments("grok 1.0.41 (abcdef123456) [stable] extra", 0, "fail")]
    [Arguments("extra grok 1.0.41 (abcdef123456)", 0, "fail")]
    [Arguments("grok 1.0.41 (abcdef123456) ", 0, "fail")]
    [Arguments("grok 1.0.41 (abcdef123456)\nextra", 0, "fail")]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Image_version_row_accepts_only_the_successful_qualified_version(string versionOutput, int exitCode, string outcome)
    {
        // Execute the committed row with only the CLI and uid substituted. No image, provider,
        // auth or network: env is a shell stub returning the candidate version and exit code.
        var row = GrokRow(Read("docker/session-runner-grok/verify-codex-image.sh"));
        var script = "GROK_VERSION=" + Version + "\n" + "candidate='" + versionOutput + "'\n" + "candidate_exit=" + exitCode + "\n" + """
            PROBE_HOME=$(mktemp -d /tmp/c986-version-XXXXXX) || exit 2
            printf 'owned root=%s\n' "$PROBE_HOME"
            trap 'rm -f "$PROBE_HOME/grok-version.err"; rmdir "$PROBE_HOME/grok" "$PROBE_HOME"' EXIT
            env() { printf '%s\n' "$candidate"; return "$candidate_exit"; }
            need_uid() { [ "$1" = 1654 ] || exit 2; }
            result() { printf 'C660_ROW grok-version %s %s\n' "$1" "$2"; [ "$1" = ok ] && exit 0; exit 1; }
            """ + "\n" + row;
        RemoteScriptContractTests.LinuxShell(script).ShouldContain("C660_ROW grok-version " + outcome + " ");
    }

    private static string GrokRow(string probe)
    {
        var row = Regex.Match(probe, @"(?ms)^  grok-version\)\n(?<body>.*?)^    ;;$");
        row.Success.ShouldBeTrue("the image verifier exposes a grok-version row");
        return row.Groups["body"].Value;
    }

    private static string Read(string path) => DockerStackDocuments.Read(path).Replace("\r\n", "\n");
}
