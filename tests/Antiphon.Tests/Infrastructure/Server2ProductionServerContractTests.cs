using System.Diagnostics;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1775. The production server compose is a host-daemon file with a direct
/// session-runner sidecar. These witnesses read it and run the deploy script in
/// plan or refusal mode. They do not start a daemon, a container, or a database.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class Server2ProductionServerContractTests
{
    private const string ComposePath = "docker-compose.server2-server.yml";
    private const string ScriptPath = "scripts/deploy-server2-server.ps1";

    [Test]
    public void Production_compose_is_its_own_project()
    {
        var text = Compose();
        text.ShouldContain("name: antiphon-server2-server");
        text.ShouldNotContain("name: antiphon-runner");
        text.ShouldNotContain("name: antiphon-server2-staging");
        text.ShouldNotContain("name: antiphon\n");
        text.ShouldNotContain("privileged:");
        text.ShouldNotContain("docker.sock");
        text.ShouldNotContain("antiphon_pgdata");
        text.ShouldNotContain("dind-data");
        text.ShouldNotContain("windmill");
        text.ShouldNotContain("codex-home");
        text.ShouldContain("name: antiphon-server2-server_work");
        text.ShouldContain("name: antiphon-server2-server_server-state");
        text.ShouldContain("name: antiphon-server2-server_runner-state");
        text.ShouldNotContain("antiphon-server2-staging_");
        text.ShouldNotContain("antiphon-server2-postgres_pgdata");
        Service("server").ShouldNotContain("privileged:");
        Service("server-session-runner").ShouldNotContain("privileged:");
        Service("server-state-init").ShouldNotContain("privileged:");
    }

    [Test]
    public void Production_joins_external_postgres_on_loopback_17400()
    {
        var text = Compose();
        text.ShouldContain("external: true");
        text.ShouldContain("name: antiphon-server2");
        text.ShouldNotContain("  postgres:");
        text.ShouldNotContain("image: postgres");
        text.ShouldNotContain(":5432:");
        text.ShouldNotContain("5433");
        text.ShouldNotContain("17280");
        text.ShouldNotContain("17202");
        text.ShouldNotContain("17420");
        text.ShouldNotContain("0.0.0.0");
        var server = Service("server");
        server.ShouldContain("127.0.0.1:17400:8080");
        server.ShouldContain("Host=postgres;Port=5432;Database=antiphon;Username=antiphon;Password=${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}");
        server.ShouldContain("- antiphon-server2");
        server.ShouldContain("- server-internal");
        server.ShouldNotContain("localhost");
        server.ShouldNotContain("antiphon_dev");
        Service("server-session-runner").ShouldNotContain("ports:");
        Service("server-session-runner").ShouldNotContain("antiphon-server2");
    }

    [Test]
    public void Production_sidecar_shares_work_and_runner_state_with_herdr_off()
    {
        var text = Compose();
        text.ShouldContain("  server-session-runner:");
        var sidecar = Service("server-session-runner");
        sidecar.ShouldContain("dockerfile: docker/session-runner-grok/Dockerfile");
        sidecar.ShouldContain("target: runtime");
        sidecar.ShouldContain("user: \"1654:1654\"");
        Env(sidecar, "SessionRunner__Herdr__Enabled").ShouldBe("false");
        Env(sidecar, "PhoneHome__Enabled").ShouldBe("false");
        Env(sidecar, "SessionRunner__SessionLogPath").ShouldBe("/state/session-runner");
        sidecar.ShouldContain("runner-state:/state");
        sidecar.ShouldContain("work:/work");
        sidecar.ShouldNotContain("ports:");
        var server = Service("server");
        Env(server, "SessionRunner__Enabled").ShouldBe("true");
        Env(server, "SessionRunner__BaseUrl").ShouldBe("http://server-session-runner:8080");
        Env(server, "AgentSessions__SessionLogPath").ShouldBe("/runner-state/session-runner");
        Env(server, "ZombieCensus__SessionLogPath").ShouldBe("/runner-state/session-runner");
        server.ShouldContain("runner-state:/runner-state:ro");
        server.ShouldContain("work:/work");
        server.ShouldContain("server-session-runner:");
        text.ShouldNotContain(".exe");
        text.ShouldNotContain(".cmd");
        text.Contains("C:\\", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("C:/", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Test]
    public void Production_flags_specialists_and_hangfire_default()
    {
        var server = Service("server");
        Env(server, "Hangfire__ServerEnabled").ShouldBe("true");
        Env(server, "Hangfire__Storage").ShouldBe("${HANGFIRE_STORAGE:-InMemory}");
        Env(server, "ZombieCensus__Enabled").ShouldBe("false");
        Env(server, "WorktreeResidue__Enabled").ShouldBe("true");
        Env(server, "WorktreeResidue__Execute").ShouldBe("false");
        Env(server, "WorktreeLegacyRelease__Enabled").ShouldBe("false");
        Env(server, "WorktreeLegacyRelease__Execute").ShouldBe("false");
        Env(server, "RunnerWorktreeRetention__Enabled").ShouldBe("true");
        Env(server, "RunnerWorktreeRetention__Execute").ShouldBe("true");
        Env(server, "Schedules__Enabled").ShouldBe("true");
        Env(server, "ChannelBridge__Enabled").ShouldBe("${CHANNEL_BRIDGE_ENABLED:-false}");
        Env(server, "Digest__Enabled").ShouldBe("false");
        Env(server, "PhoneHomeRunner__Enabled").ShouldBe("true");
        Env(server, "Delegation__CheckInterpreterEnabled").ShouldBe("true");
        Env(server, "Delegation__DiagnoseEnabled").ShouldBe("true");
        Env(server, "Delegation__OutputDistillerEnabled").ShouldBe("true");
        Env(server, "Delegation__CheckInterpreterWorkingDirectory").ShouldBe("/state/check-interpreter");
        Env(server, "Delegation__DiagnoseWorkingDirectory").ShouldBe("/state/diagnose");
        Env(server, "Delegation__OutputDistillerWorkingDirectory").ShouldBe("/state/output-distiller");
        Env(server, "Git__WorktreeBasePath").ShouldBe("/work/worktrees");
        Env(server, "Git__WorkspacePath").ShouldBe("/work/repos");
        var text = Compose();
        text.ShouldContain("CARD-1774");
        text.ShouldContain("InMemory");
        Service("server-state-init").ShouldContain("/state/output-distiller");
        Service("server-state-init").ShouldContain("/state/check-interpreter");
        Service("server-state-init").ShouldContain("/state/diagnose");
    }

    [Test]
    public void Production_key_protection_copies_into_the_state_volume()
    {
        var server = Service("server");
        Env(server, "AgentTui__KeyRingPath").ShouldBe("/state/keyring");
        Env(server, "AgentTui__KeyProtection__Mode").ShouldBe("X509Certificate");
        Env(server, "AgentTui__KeyProtection__CertificatePath").ShouldBe("/state/keyring-cert/cert.pem");
        Env(server, "AgentTui__KeyProtection__CertificatePrivateKeyPath").ShouldBe("/state/keyring-cert/key.pem");
        Env(server, "Operator__TokenPath").ShouldBe("/state/operator-token");
        server.ShouldNotContain("KEYRING_CERT_FILE");
        server.ShouldNotContain("KEYRING_KEY_FILE");
        server.ShouldNotContain("OPERATOR_TOKEN_FILE");
        var init = Service("server-state-init");
        init.ShouldContain("KEYRING_CERT_FILE:?KEYRING_CERT_FILE is required");
        init.ShouldContain("KEYRING_KEY_FILE:?KEYRING_KEY_FILE is required");
        init.ShouldContain("OPERATOR_TOKEN_FILE:?OPERATOR_TOKEN_FILE is required");
        init.ShouldContain("/host-keyring-cert/cert.pem");
        init.ShouldContain("/host-keyring-cert/key.pem");
        init.ShouldContain("/host-operator-token");
        init.ShouldContain("cp /host-keyring-cert/cert.pem /state/keyring-cert/cert.pem");
        init.ShouldContain("cp /host-keyring-cert/key.pem /state/keyring-cert/key.pem");
        init.ShouldContain("cp /host-operator-token /state/operator-token");
        init.ShouldContain("chmod 0600");
        init.ShouldContain("ForeignStateOwner");
        init.ShouldContain("exit 42");
        init.ShouldContain("1654");
        server.ShouldContain("server-state:/state");
        Compose().ShouldContain("name: antiphon-server2-server_server-state");
    }

    [Test]
    public void Production_phone_home_origin_is_not_hard_coded()
    {
        var server = Service("server");
        Env(server, "Delegation__ApiBaseUrl").ShouldBe("${ANTIPHON_PUBLIC_ORIGIN:?ANTIPHON_PUBLIC_ORIGIN is required}");
        Env(server, "PhoneHomeRunner__Runners__server2__Enabled").ShouldBe("true");
        Env(server, "PhoneHomeRunner__Runners__server2__AllowDelegatedTasks").ShouldBe("true");
        Env(server, "PhoneHomeRunner__Runners__server2__CallbackOrigin").ShouldBe("${ANTIPHON_PUBLIC_ORIGIN:?ANTIPHON_PUBLIC_ORIGIN is required}");
        Env(server, "PhoneHomeRunner__Runners__server2__SharedSecret").ShouldBe("${PHONE_HOME_SHARED_SECRET:?PHONE_HOME_SHARED_SECRET is required}");
        Env(server, "PhoneHomeRunner__Runners__server2__HostWorkspaceRoot").ShouldBe("${PHONE_HOME_HOST_WORKSPACE_ROOT:?PHONE_HOME_HOST_WORKSPACE_ROOT is required}");
        Env(server, "PhoneHomeRunner__Runners__server2__RunnerWorkspace").ShouldBe("/work");
        Env(server, "PhoneHomeRunner__Runners__server2__RunnerRepository").ShouldBe("/work/repos/antiphon");
        Env(server, "PhoneHomeRunner__Runners__server2__MaxCapacity").ShouldBe("10");
        var text = Compose();
        text.ShouldNotContain("codeperf.net");
        text.ShouldNotContain("PhoneHome__ServerOrigin");
        Script().ShouldNotContain("codeperf.net");
        text.ShouldNotContain("GH_TOKEN");
        text.ShouldNotContain("RUNNER_GITHUB_TOKEN");
    }

    [Test]
    public void Production_script_names_refusals_and_the_verify_only_gate()
    {
        var script = Script();
        script.Any(ch => ch > 127).ShouldBeFalse();
        Compose().Any(ch => ch > 127).ShouldBeFalse();
        script.ShouldContain("Default is -WhatIf");
        script.ShouldContain("planOnly = -not $Execute");
        script.ShouldContain("not-server2-host");
        script.ShouldContain("ambiguous-mode");
        script.ShouldContain("bind-any");
        script.ShouldContain("volume-forbidden");
        script.ShouldContain("dev-password");
        script.ShouldContain("env-mode");
        script.ShouldContain("env-inside-repo");
        script.ShouldContain("missing-certificate");
        script.ShouldContain("hangfire-storage");
        script.ShouldContain("expected-sha");
        script.ShouldContain("production-compose-wrong-project");
        script.ShouldContain("/.dockerenv");
        script.ShouldContain("antiphon_pgdata");
        script.ShouldContain("dind-data");
        script.ShouldContain("17280");
        script.ShouldContain("17202");
        script.ShouldContain("17420");
        script.ShouldContain("0.0.0.0");
        script.ShouldContain("windmill");
        script.ShouldContain("restart-linux-server.ps1");
        script.ShouldContain("verify-only");
        script.ShouldContain("antiphon-server2-server-server-1");
        script.ShouldContain("http://127.0.0.1:17400");
        script.ShouldNotContain("docker compose up");
        var gateCall = script.IndexOf("restart-linux-server.ps1", StringComparison.Ordinal);
        gateCall.ShouldBeGreaterThanOrEqualTo(0);
        script.Contains("-Execute", StringComparison.Ordinal).ShouldBeTrue();
        var verify = script.IndexOf("verify-only", StringComparison.Ordinal);
        var gateArgs = script.IndexOf("'-Config', $cfg, '-ExpectedServerSha', $Sha", StringComparison.Ordinal);
        gateArgs.ShouldBeGreaterThan(verify);
        script.Substring(gateArgs, Math.Min(80, script.Length - gateArgs)).ShouldNotContain("-Execute");
    }

    [Test]
    public void Production_doc_names_the_compose_and_states_no_cache()
    {
        var doc = DockerStackDocuments.Read("docs/docker-stack.md");
        doc.ShouldContain("docker-compose.server2-server.yml");
        doc.ShouldContain("antiphon-server2-server");
        doc.ShouldContain("127.0.0.1:17400");
        doc.ShouldContain("server-session-runner");
        doc.ShouldContain("scripts/deploy-server2-server.ps1` defaults to `-WhatIf`");
        doc.ShouldContain("verify-only");
        doc.ShouldContain("InMemory");
        doc.ShouldContain("CARD-1774");
        doc.ShouldContain("adds no cache");
        DockerStackDocuments.Read("scripts/deploy-server2.ps1").ShouldNotContain("docker-compose.server2-server.yml");
        DockerStackDocuments.Read("docker-compose.yml").ShouldNotContain("17400");
        DockerStackDocuments.Read("docker-compose.server2-staging-server.yml").ShouldNotContain("17400");
        DockerStackDocuments.Read("scripts/restart-linux-server.ps1").ShouldNotContain("deploy-server2-server");
    }

    [Test]
    [NativeLinuxOnly("Linux-only by construction: the deploy plan witness uses a Unix file mode and a fake docker on PATH.")]
    public async Task WhatIf_prints_the_plan_and_calls_no_docker()
    {
        ReadRequired(ScriptPath);
        var marker = Path.Combine(Path.GetTempPath(), "card1775-whatif-" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(Path.GetTempPath(), "card1775-bin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bin);
        var docker = Path.Combine(bin, "docker");
        await File.WriteAllTextAsync(docker, "#!/bin/sh\nprintf '%s\\n' \"$*\" >> \"" + marker + "\"\nexit 97\n");
        File.SetUnixFileMode(docker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var ran = await RunDeployAsync(new[] { "-WhatIf" }, bin);
            ran.Code.ShouldBe(0, ran.Stdout + ran.Stderr);
            ran.Stdout.ShouldContain("docker-compose.server2-server.yml");
            ran.Stdout.ShouldContain("-p antiphon-server2-server up -d");
            ran.Stdout.ShouldContain("verify-only");
            ran.Stdout.ShouldContain("restart-linux-server.ps1");
            ran.Stdout.ShouldContain("SERVER2-SERVER plan exit=0 reason=ok");
            ran.Stdout.ShouldNotContain("-Execute");
            ran.Stdout.ShouldNotContain("PHONE_HOME_SHARED_SECRET=");
            File.Exists(marker).ShouldBeFalse();
        }
        finally
        {
            if (File.Exists(docker))
                File.Delete(docker);
            if (Directory.Exists(bin))
                Directory.Delete(bin);
            if (File.Exists(marker))
                File.Delete(marker);
        }
    }

    [Test]
    [NativeLinuxOnly("Linux-only by construction: deploy refusals check Unix mode 0600 and the container marker /.dockerenv.")]
    public async Task Execute_refusals_stop_before_docker()
    {
        ReadRequired(ScriptPath);
        ReadRequired(ComposePath);
        var marker = Path.Combine(Path.GetTempPath(), "card1775-refuse-" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(Path.GetTempPath(), "card1775-refuse-bin-" + Guid.NewGuid().ToString("N"));
        var scratch = Path.Combine(Path.GetTempPath(), "card1775-scratch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(scratch);
        var docker = Path.Combine(bin, "docker");
        await File.WriteAllTextAsync(docker, "#!/bin/sh\nprintf '%s\\n' \"$*\" >> \"" + marker + "\"\nexit 97\n");
        File.SetUnixFileMode(docker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var badCompose = Path.Combine(scratch, "bad.yml");
        var envFile = Path.Combine(scratch, "prod.env");
        var cert = Path.Combine(scratch, "cert.pem");
        var key = Path.Combine(scratch, "key.pem");
        var token = Path.Combine(scratch, "operator-token");
        try
        {
            await File.WriteAllTextAsync(badCompose, "name: antiphon-server2-server\nantiphon_pgdata\n");
            await AssertRefusalAsync(bin, ["-WhatIf", "-ComposeFile", badCompose], "volume-forbidden", marker);

            await File.WriteAllTextAsync(badCompose, "name: antiphon-server2-server\n0.0.0.0\n");
            await AssertRefusalAsync(bin, ["-WhatIf", "-ComposeFile", badCompose], "bind-any", marker);

            await File.WriteAllTextAsync(badCompose, "name: antiphon-server2-server\n17420\n");
            await AssertRefusalAsync(bin, ["-WhatIf", "-ComposeFile", badCompose], "port-forbidden", marker);

            await File.WriteAllTextAsync(badCompose, "name: antiphon-server2-server\ncodeperf.net\n");
            await AssertRefusalAsync(bin, ["-WhatIf", "-ComposeFile", badCompose], "phone-home-origin", marker);

            await File.WriteAllTextAsync(badCompose, "name: antiphon-server2-server\nwindmill\n");
            await AssertRefusalAsync(bin, ["-WhatIf", "-ComposeFile", badCompose], "volume-forbidden", marker);

            await AssertRefusalAsync(
                bin,
                ["-WhatIf", "-Project", "antiphon-runner"],
                "production-compose-wrong-project",
                marker);

            var both = await RunDeployAsync(["-WhatIf", "-Execute"], bin);
            both.Code.ShouldBe(3);
            ResultLine(both.Stdout).ShouldBe("SERVER2-SERVER fail exit=3 reason=ambiguous-mode");
            File.Exists(marker).ShouldBeFalse();

            await WriteSecretFileAsync(cert, "cert");
            await WriteSecretFileAsync(key, "key");
            await WriteSecretFileAsync(token, "token");
            var dev = DevPassword();
            await WriteSecretFileAsync(envFile, EnvBody(dev, cert, key, token, "InMemory"));
            var devRun = await RunDeployAsync(["-WhatIf", "-EnvFile", envFile], bin);
            devRun.Code.ShouldBe(2);
            ResultLine(devRun.Stdout).ShouldBe("SERVER2-SERVER fail exit=2 reason=dev-password");
            devRun.Stdout.Contains(dev, StringComparison.Ordinal).ShouldBeFalse();
            File.Exists(marker).ShouldBeFalse();

            await WriteSecretFileAsync(envFile, EnvBody("fixture-password-1775", cert, key, token, "Redis"));
            await AssertRefusalAsync(bin, ["-WhatIf", "-EnvFile", envFile], "hangfire-storage", marker);

            await WriteSecretFileAsync(envFile, EnvBody("fixture-password-1775", Path.Combine(scratch, "missing-cert.pem"), key, token, "InMemory"));
            await AssertRefusalAsync(bin, ["-Execute", "-EnvFile", envFile, "-ExpectedServerSha", new string('a', 40)], "missing-certificate", marker);

            var inside = Path.Combine(DockerStackDocuments.RepoRoot, ".antiphon-card1775-env");
            await WriteSecretFileAsync(inside, EnvBody("fixture-password-1775", cert, key, token, "InMemory"));
            try
            {
                await AssertRefusalAsync(bin, ["-WhatIf", "-EnvFile", inside], "env-inside-repo", marker);
            }
            finally
            {
                if (File.Exists(inside))
                    File.Delete(inside);
            }

            File.SetUnixFileMode(envFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            await WriteSecretFileAsync(envFile, EnvBody("fixture-password-1775", cert, key, token, "InMemory"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            await AssertRefusalAsync(bin, ["-WhatIf", "-EnvFile", envFile], "env-mode", marker);

            await WriteSecretFileAsync(envFile, EnvBody("fixture-password-1775", cert, key, token, "InMemory"));
            await AssertRefusalAsync(
                bin,
                ["-Execute", "-EnvFile", envFile, "-ExpectedServerSha", new string('b', 40)],
                "not-server2-host",
                marker);
        }
        finally
        {
            foreach (var path in new[] { docker, badCompose, envFile, cert, key, token })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }

            if (Directory.Exists(bin))
                Directory.Delete(bin);
            if (Directory.Exists(scratch))
                Directory.Delete(scratch);
            if (File.Exists(marker))
                File.Delete(marker);
        }
    }

    private static async Task AssertRefusalAsync(string bin, string[] args, string reason, string marker)
    {
        var ran = await RunDeployAsync(args, bin);
        ran.Code.ShouldBe(2, ran.Stdout + ran.Stderr);
        ResultLine(ran.Stdout).ShouldBe("SERVER2-SERVER fail exit=2 reason=" + reason);
        File.Exists(marker).ShouldBeFalse();
    }

    private static string ResultLine(string stdout) =>
        stdout.Replace("\r\n", "\n").Split('\n').Last(line => line.StartsWith("SERVER2-SERVER ", StringComparison.Ordinal));

    private static async Task<(int Code, string Stdout, string Stderr)> RunDeployAsync(string[] args, string bin)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(DockerStackDocuments.RepoRoot, ScriptPath));
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        start.Environment["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        start.Environment["TEMP"] = "/tmp";
        start.Environment["TMP"] = "/tmp";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(deadline.Token);
        return (process.ExitCode, await stdout, await stderr);
    }

    private static string DevPassword()
    {
        var text = DockerStackDocuments.Read("docker-compose.dev.yml");
        var marker = "POSTGRES_PASSWORD: ";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var value = text[(start + marker.Length)..];
        var end = value.IndexOfAny(['\r', '\n']);
        if (end >= 0)
            value = value[..end];
        return value.Trim();
    }

    private static string EnvBody(string password, string cert, string key, string token, string storage) =>
        "POSTGRES_PASSWORD=" + password + "\n" +
        "HANGFIRE_STORAGE=" + storage + "\n" +
        "ANTIPHON_PUBLIC_ORIGIN=https://example.invalid\n" +
        "PHONE_HOME_SHARED_SECRET=fixture-secret-1775\n" +
        "PHONE_HOME_HOST_WORKSPACE_ROOT=/work\n" +
        "KEYRING_CERT_FILE=" + cert + "\n" +
        "KEYRING_KEY_FILE=" + key + "\n" +
        "OPERATOR_TOKEN_FILE=" + token + "\n";

    private static async Task WriteSecretFileAsync(string path, string body, UnixFileMode? mode = null)
    {
        await File.WriteAllTextAsync(path, body);
        File.SetUnixFileMode(path, mode ?? (UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    private static string ReadRequired(string relative)
    {
        var path = Path.Combine(DockerStackDocuments.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).ShouldBeTrue(relative);
        return File.ReadAllText(path);
    }

    private static string Compose() => ReadRequired(ComposePath);

    private static string Script() => ReadRequired(ScriptPath);

    private static string Service(string name) => DockerStackDocuments.Service(Compose(), name);

    private static string Env(string block, string key) => DockerStackDocuments.Env(block, key);
}
