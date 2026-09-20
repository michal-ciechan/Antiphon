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
        compose.ShouldContain("PHONE_HOME_GROK_SESSIONS");
        compose.ShouldContain("SessionRunner__PtyHostDir");
        compose.ShouldContain("/tmp/antiphon-pty-hosts");
        System.Text.RegularExpressions.Regex.IsMatch(
            compose,
            @"PHONE_HOME_GROK_HOME[\s\S]*?read_only:\s*true",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ShouldBeTrue("throwaway GROK_HOME must be a read-only bind");

        var dockerfile = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "docker/session-runner-grok/Dockerfile"));
        System.Text.RegularExpressions.Regex.IsMatch(
            dockerfile, @"^\s*ENV\s+PhoneHome__ServerOrigin\b",
            System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ShouldBeFalse();
        dockerfile.ShouldNotContain("17202");
        dockerfile.ShouldContain("1.0.34");
        dockerfile.ShouldContain("x.ai/cli/install.sh");
        System.Text.RegularExpressions.Regex.IsMatch(
            dockerfile, @"COPY\s+[^\n]*auth\.json",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ShouldBeFalse();
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
        var runnerPort = json.RootElement.GetProperty("runnerPort").GetInt32();
        new[] { 17202, 17203, 17204, 17205 }.ShouldNotContain(runnerPort);
        json.RootElement.GetProperty("localRunnerOrigin").GetString()
            .ShouldContain($"127.0.0.1:{runnerPort}");
        json.RootElement.TryGetProperty("sharedSecret", out _).ShouldBeFalse();
        output.ShouldContain("PhoneHome__ServerOrigin");
    }

    [Test]
    public async Task WriteConfigOnly_copies_primary_auth_into_throwaway_home()
    {
        var primary = Path.Combine(Path.GetTempPath(), "grok-primary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(primary);
        await File.WriteAllTextAsync(Path.Combine(primary, "auth.json"), """{"x":{"key":"not-a-real-token","expires_at":9999999999}}""");
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
        psi.Environment["GROK_HOME"] = primary;
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("-ConfigurationFile");
        psi.ArgumentList.Add(config);
        psi.ArgumentList.Add("-EvidenceRoot");
        psi.ArgumentList.Add(evidence);
        psi.ArgumentList.Add("-WriteConfigOnly");
        using var proc = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("pwsh failed to start");
        var output = await proc.StandardOutput.ReadToEndAsync() + await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        proc.ExitCode.ShouldBe(0, output);
        File.Exists(config).ShouldBeTrue(output);
        var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(config));
        var mount = json.RootElement.GetProperty("oauthMount").GetString();
        mount.ShouldNotBeNull();
        Path.GetFullPath(mount!).ShouldNotBe(Path.GetFullPath(primary));
        File.Exists(Path.Combine(mount!, "auth.json")).ShouldBeTrue(output);
        output.ShouldNotContain("not-a-real-token");
        json.RootElement.GetProperty("grokSessionsMount").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task Server2_placement_uses_desktop_tailscale_not_docker_internal()
    {
        var root = Path.Combine(Path.GetTempPath(), "card0490-live-s2-" + Guid.NewGuid().ToString("N"));
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
        psi.ArgumentList.Add("-Placement");
        psi.ArgumentList.Add("server2");
        psi.ArgumentList.Add("-WriteConfigOnly");
        using var proc = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("pwsh failed to start");
        var output = await proc.StandardOutput.ReadToEndAsync() + await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        File.Exists(config).ShouldBeTrue(output);
        var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(config));
        json.RootElement.GetProperty("placement").GetString().ShouldBe("server2");
        var origin = json.RootElement.GetProperty("phoneHomeServerOrigin").GetString();
        origin.ShouldNotBeNull();
        origin!.ShouldNotContain("host.docker.internal");
        origin.ShouldMatch(@"^http://100\.\d+\.\d+\.\d+:\d+$");
        json.RootElement.GetProperty("serverOrigin").GetString()
            .ShouldContain("127.0.0.1");
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

    [Test]
    public void Script_uses_isolated_local_runner_and_fresh_first_start()
    {
        var script = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "verify-phone-home-grok.ps1"));
        script.ShouldNotContain("http://127.0.0.1:1");
        script.ShouldContain("SessionRunner__BaseUrl");
        script.ShouldContain("localRunnerOrigin");
        script.ShouldContain("dispatchEligible");
        script.ShouldContain("fresh = $true");
        script.ShouldContain("PHONE_HOME_OK_");
        script.ShouldContain("bin-card0490-v7-runner");
    }
}
