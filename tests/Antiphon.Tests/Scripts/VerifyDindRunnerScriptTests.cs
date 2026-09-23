using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-0604 S5, R-3. The harness talks to a real Docker daemon, so only its argument and naming
// boundaries are guarded here; the observations themselves are CP-4.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class VerifyDindRunnerScriptTests
{
    [Test]
    public async Task Production_port_is_refused()
    {
        var result = await RunAsync("-Image", "antiphon-session-testing:test", "-Port", "17204");
        result.ExitCode.ShouldBe(2);
        result.Output.ShouldContain("refusing production port 17204");
        result.Output.ShouldContain("C604 HARNESS EXIT CODE: 2");
        result.Output.ShouldNotContain("docker build");
    }

    [Test]
    public async Task Image_is_required()
    {
        var result = await RunAsync("-Port", "18298");
        result.ExitCode.ShouldNotBe(0);
        result.Output.ShouldNotContain("C604 HARNESS EXIT CODE: 0");
    }

    [Test]
    public void Compose_and_dockerfile_do_not_hardcode_a_key_path_value()
    {
        var compose = Read("docker-compose.server2-runner.yml");
        compose.ShouldContain("ANTIPHON_DEPLOY_KEY_FILE:?");
        compose.ShouldContain("PHONE_HOME_SECRET_FILE:?");
        // The private halves live only on server2; no compose or image file may name one.
        System.Text.RegularExpressions.Regex.IsMatch(compose, @"(?m)^\s*file:\s*/(?!\$)")
            .ShouldBeFalse("every secret source is interpolated, never a literal host path");

        var dockerfile = Read("docker/session-runner-grok/Dockerfile");
        System.Text.RegularExpressions.Regex.IsMatch(
            dockerfile, @"COPY\s+[^\n]*deploy[_-]?key",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ShouldBeFalse("the private key half is never baked into the image");
        dockerfile.ShouldNotContain("BEGIN OPENSSH PRIVATE KEY");

        var script = Read("scripts/verify-card0604-dind-runner.ps1");
        script.ShouldContain("throwaway_key");
        script.ShouldNotContain("antiphon-server2/secrets");
        script.ShouldNotContain("/home/mc/antiphon-server2");
    }

    [Test]
    public void Harness_names_only_its_own_container()
    {
        var script = Read("scripts/verify-card0604-dind-runner.ps1");
        script.ShouldContain("$containerName = \"c604-$stamp\"");
        script.ShouldContain("$volumeName = \"c604-$stamp-dind\"");
        // Removal acts on this run's own names only: never a project sweep, never a prune.
        foreach (var line in script.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.Contains("'rm'", StringComparison.Ordinal)
                && !trimmed.Contains("'prune'", StringComparison.Ordinal)
                && !trimmed.Contains("'down'", StringComparison.Ordinal))
                continue;
            trimmed.Contains("'prune'", StringComparison.Ordinal).ShouldBeFalse("harness never prunes: " + trimmed);
            (trimmed.Contains("$containerName", StringComparison.Ordinal)
                || trimmed.Contains("$volumeName", StringComparison.Ordinal)
                || trimmed.Contains("$throwaway", StringComparison.Ordinal))
                .ShouldBeTrue("removal names this run's own resource: " + trimmed);
        }

        foreach (var port in new[] { "17202", "17203", "17205" })
            script.ShouldContain(port);
        script.ShouldContain("$forbiddenPorts = 17202, 17203, 17204, 17205");
        script.ShouldContain("[int] $Port = 18298");
    }

    // CARD-0628 G-4: step 7b probes the pinned claude as the app uid against a fresh store it
    // created, grades the signed-out verdict, and hands the CLI no credential of any kind.
    [Test]
    public void Harness_probes_claude_as_the_app_uid_and_expects_logged_out()
    {
        var script = Read("scripts/verify-card0604-dind-runner.ps1");
        script.ShouldContain("'-e', 'CLAUDE_CONFIG_DIR=/tmp/state/claude'");
        script.ShouldContain("'-e', 'PhoneHome__ClaudeHome=/tmp/state/claude'");
        script.ShouldContain("mkdir -p /tmp/state/claude && chown 1654:1654 /tmp/state/claude");
        script.ShouldContain("$claudeEnv = @('-u', '1654:1654', '-e', 'HOME=/home/app', '-e', 'CLAUDE_CONFIG_DIR=/tmp/state/claude'");
        script.ShouldContain("@($containerName, 'claude', '--version')");
        script.ShouldContain("@($containerName, 'claude', 'auth', 'status', '--json')");
        script.ShouldContain("$claudeStatus.ExitCode -eq 1 -and $claudeStatus.Output -match '\"loggedIn\"\\s*:\\s*false'");
        script.ShouldContain("claude   = $claudeVersion -eq '2.1.280'");
        script.ShouldContain("claudeAuth = $claudeAuth -eq 'logged-out'");
        script.ShouldContain("claude={9} claudeAuth={10}");
        foreach (var name in new[] { "CLAUDE_CODE_OAUTH_TOKEN", "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "credentials.json" })
            script.Contains(name, StringComparison.Ordinal).ShouldBeFalse("the harness never passes " + name);
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
    {
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "verify-card0604-dind-runner.ps1");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = DelegateScriptRunner.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout + stderr);
    }
}
