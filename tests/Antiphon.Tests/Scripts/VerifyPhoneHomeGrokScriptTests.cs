using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class VerifyPhoneHomeGrokScriptTests
{
    [Test]
    public void Compose_and_dockerfile_do_not_hardcode_server_origin()
    {
        var compose = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "docker-compose.runner-grok.yml"));
        compose.ShouldContain("PHONE_HOME_SERVER_ORIGIN");
        compose.ShouldNotContain("PhoneHome__ServerOrigin: http");
        compose.ShouldNotContain("host.docker.internal:17202");
        compose.ShouldNotContain("host.docker.internal:17203");
        compose.ShouldNotContain("host.docker.internal:17204");
        compose.ShouldNotContain("host.docker.internal:17205");
        compose.ShouldContain("PHONE_HOME_GROK_HOME");

        var dockerfile = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "docker/session-runner-grok/Dockerfile"));
        System.Text.RegularExpressions.Regex.IsMatch(
            dockerfile, @"^\s*ENV\s+PhoneHome__ServerOrigin\b",
            System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ShouldBeFalse();
        dockerfile.ShouldNotContain("17202");
    }

    [Test]
    public async Task WriteConfigOnly_allocates_isolated_ports_and_refuses_production()
    {
        var root = Path.Combine(Path.GetTempPath(), "card0490-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = Path.Combine(root, "card0490-live.json");
        var evidence = Path.Combine(root, "evidence");
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "verify-phone-home-grok.ps1");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = DelegateScriptRunner.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("-ConfigurationFile");
        psi.ArgumentList.Add(config);
        psi.ArgumentList.Add("-EvidenceRoot");
        psi.ArgumentList.Add(evidence);
        psi.ArgumentList.Add("-WriteConfigOnly");
        using var proc = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("pwsh failed to start");
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        var output = stdout + stderr;
        proc.ExitCode.ShouldBeOneOf(0, 2);
        File.Exists(config).ShouldBeTrue(output);
        var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(config));
        var serverPort = json.RootElement.GetProperty("serverPort").GetInt32();
        var postgresPort = json.RootElement.GetProperty("postgresPort").GetInt32();
        new[] { 17202, 17203, 17204, 17205 }.ShouldNotContain(serverPort);
        new[] { 17202, 17203, 17204, 17205 }.ShouldNotContain(postgresPort);
        json.RootElement.GetProperty("phoneHomeServerOrigin").GetString()
            .ShouldContain($"host.docker.internal:{serverPort}");
        json.RootElement.GetProperty("serverOrigin").GetString()
            .ShouldContain($"127.0.0.1:{serverPort}");
        json.RootElement.TryGetProperty("sharedSecret", out _).ShouldBeFalse();
        output.ShouldContain("PhoneHome__ServerOrigin");
    }

    [Test]
    public async Task Configuration_naming_production_port_is_refused()
    {
        var root = Path.Combine(Path.GetTempPath(), "card0490-live-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = Path.Combine(root, "card0490-live.json");
        await File.WriteAllTextAsync(config, """{"serverPort":17202}""");
        var evidence = Path.Combine(root, "evidence");
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "verify-phone-home-grok.ps1");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = DelegateScriptRunner.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("-ConfigurationFile");
        psi.ArgumentList.Add(config);
        psi.ArgumentList.Add("-EvidenceRoot");
        psi.ArgumentList.Add(evidence);
        psi.ArgumentList.Add("-WriteConfigOnly");
        using var proc = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("pwsh failed to start");
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        proc.ExitCode.ShouldNotBe(0);
        (stdout + stderr).ShouldContain("17202");
    }
}
