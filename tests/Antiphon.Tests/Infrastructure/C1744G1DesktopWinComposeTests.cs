using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Settings;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1744 S1. W-1..W-3 read the production compose and the deploy script.
/// They do not start a daemon, a container, or a database.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C1744G1DesktopWinComposeTests
{
    private const string ComposePath = "docker-compose.server2-server.yml";
    private const string ScriptPath = "scripts/deploy-server2-server.ps1";
    private const string DesktopWin = "PhoneHomeRunner__Runners__desktop-win__";
    private const string SecretKey = "PHONE_HOME_DESKTOP_WIN_SHARED_SECRET";
    private const string HomeKey = "PHONE_HOME_DESKTOP_WIN_HOME";
    private const string FixtureSecret = "fixture-desktop-win-secret";
    private const string FixtureHome = "C:/Users/fixture";

    [Test]
    public void G1_compose_declares_desktop_win_with_drive_rooted_paths()
    {
        var server = DockerStackDocuments.Service(Read(ComposePath), "server");
        server.ShouldContain(DesktopWin + "Enabled: \"true\"");
        Env(server, "Enabled").ShouldBe("true");
        Env(server, "AllowDelegatedTasks").ShouldBe("true");
        server.ShouldNotContain(DesktopWin + "StandingAgentId");
        Env(server, "DisplayName").ShouldBe("desktop-win");
        Env(server, "SharedSecret").ShouldBe("${" + SecretKey + ":?" + SecretKey + " is required}");
        Env(server, "HostWorkspaceRoot").ShouldBe("${PHONE_HOME_HOST_WORKSPACE_ROOT:?PHONE_HOME_HOST_WORKSPACE_ROOT is required}");
        Env(server, "RunnerWorkspace").ShouldBe("C:/Antiphon/runner/work");
        Env(server, "RunnerRepository").ShouldBe("C:/Antiphon/runner/work/repos/antiphon");
        Env(server, "RawExeAllowList__0").ShouldBe("C:/Program Files/PowerShell/7/pwsh.exe");
        Env(server, "MaxCapacity").ShouldBe("2");
        Env(server, "ChildGrokHome").ShouldBe("${" + HomeKey + ":?" + HomeKey + " is required}/.grok");
        Env(server, "ChildClaudeHome").ShouldBe("${" + HomeKey + ":?" + HomeKey + " is required}/.claude");
        Env(server, "ChildCodexHome").ShouldBe("${" + HomeKey + ":?" + HomeKey + " is required}/.codex");
        Env(server, "CallbackOrigin").ShouldBe("${ANTIPHON_PUBLIC_ORIGIN:?ANTIPHON_PUBLIC_ORIGIN is required}");
        Env(server, "ClaudeAuthProbeEnabled").ShouldBe("true");
        Env(server, "CodexAuthProbeEnabled").ShouldBe("true");
        DockerStackDocuments.Env(server, "HostLoadHistory__DirectRunnerKey").ShouldBe("server2-direct");
        server.Split('\n').Count(line => line.Contains(DesktopWin, StringComparison.Ordinal)).ShouldBe(15);
        server.Contains("C:\\", StringComparison.Ordinal).ShouldBeFalse();
        server.ShouldNotContain("codeperf.net");
        foreach (var path in new[]
                 {
                     Env(server, "RunnerWorkspace"),
                     Env(server, "RunnerRepository"),
                     Env(server, "RawExeAllowList__0"),
                 })
        {
            (path.Length >= 3 && path[1] == ':' && path[2] == '/').ShouldBeTrue(path);
            path.Contains('\\').ShouldBeFalse(path);
        }
    }

    [Test]
    [NativeLinuxOnly("Linux-only by construction: the deploy plan witness uses a Unix file mode and a fake docker on PATH.")]
    public async Task G1_deploy_whatif_refuses_without_desktop_win_keys()
    {
        Read(ScriptPath);
        var marker = Path.Combine(Path.GetTempPath(), "card1744-whatif-" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(Path.GetTempPath(), "card1744-bin-" + Guid.NewGuid().ToString("N"));
        var scratch = Path.Combine(Path.GetTempPath(), "card1744-scratch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(scratch);
        var docker = Path.Combine(bin, "docker");
        var envFile = Path.Combine(scratch, "prod.env");
        var cert = Path.Combine(scratch, "cert.pem");
        var key = Path.Combine(scratch, "key.pem");
        var token = Path.Combine(scratch, "operator-token");
        await File.WriteAllTextAsync(docker, "#!/bin/sh\nprintf '%s\\n' \"$*\" >> \"" + marker + "\"\nexit 97\n");
        File.SetUnixFileMode(docker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            await WriteSecretAsync(cert, "cert");
            await WriteSecretAsync(key, "key");
            await WriteSecretAsync(token, "token");

            await WriteSecretAsync(envFile, EnvBody(cert, key, token, secret: false, home: true));
            var missingSecret = await RunDeployAsync(["-WhatIf", "-EnvFile", envFile], bin);
            missingSecret.Code.ShouldBe(2, missingSecret.Stdout + missingSecret.Stderr);
            ResultLine(missingSecret.Stdout).ShouldBe("SERVER2-SERVER fail exit=2 reason=env-missing");
            File.Exists(marker).ShouldBeFalse();
            missingSecret.Stdout.ShouldNotContain(FixtureSecret);
            missingSecret.Stderr.ShouldNotContain(FixtureSecret);

            await WriteSecretAsync(envFile, EnvBody(cert, key, token, secret: true, home: false));
            var missingHome = await RunDeployAsync(["-WhatIf", "-EnvFile", envFile], bin);
            missingHome.Code.ShouldBe(2, missingHome.Stdout + missingHome.Stderr);
            ResultLine(missingHome.Stdout).ShouldBe("SERVER2-SERVER fail exit=2 reason=env-missing");
            File.Exists(marker).ShouldBeFalse();

            await WriteSecretAsync(envFile, EnvBody(cert, key, token, secret: true, home: true));
            var ready = await RunDeployAsync(["-WhatIf", "-EnvFile", envFile], bin);
            ready.Code.ShouldBe(0, ready.Stdout + ready.Stderr);
            ResultLine(ready.Stdout).ShouldBe("SERVER2-SERVER plan exit=0 reason=ok");
            File.Exists(marker).ShouldBeFalse();
            ready.Stdout.ShouldNotContain(FixtureSecret);
            ready.Stderr.ShouldNotContain(FixtureSecret);
            ready.Stdout.ShouldNotContain(FixtureHome);
        }
        finally
        {
            foreach (var path in new[] { docker, envFile, cert, key, token })
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

    [Test]
    public void G1_compose_binds_valid_phone_home_settings()
    {
        var server = DockerStackDocuments.Service(Read(ComposePath), "server");
        server.ShouldContain(DesktopWin + "Enabled: \"true\"");
        var settings = BindServer(server);
        settings.Enabled.ShouldBeTrue();
        settings.Runners.ShouldContainKey("desktop-win");
        var entry = settings.Runners["desktop-win"];
        var failures = PhoneHomeRunnerSettingsRules.Validate(settings);
        failures.ShouldBeEmpty(string.Join(" | ", failures));
        entry.Enabled.ShouldBeTrue();
        entry.AllowDelegatedTasks.ShouldBeTrue();
        entry.StandingAgentId.ShouldBe(Guid.Empty);
        // The configuration binder concatenates onto an IReadOnlyList. Index 0 from the
        // compose is appended after the property's default Linux paths.
        entry.RawExeAllowList.ShouldBe(new[]
        {
            "/bin/sh",
            "/bin/bash",
            "/usr/local/bin/pwsh",
            "C:/Program Files/PowerShell/7/pwsh.exe",
        });
    }

    private static string Env(string server, string key) =>
        DockerStackDocuments.Env(server, DesktopWin + key);

    private static PhoneHomeRunnerSettings BindServer(string server)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var lines = server.Replace("\r\n", "\n").Split('\n');
        var inEnv = false;
        var envIndent = -1;
        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();
            if (!inEnv)
            {
                if (trimmed == "environment:")
                {
                    inEnv = true;
                    envIndent = raw.Length - raw.TrimStart().Length;
                }

                continue;
            }

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            var indent = raw.Length - raw.TrimStart().Length;
            if (indent <= envIndent)
                break;
            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
                continue;
            var key = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];
            values[key.Replace("__", ":", StringComparison.Ordinal)] = Substitute(value);
        }

        var settings = new PhoneHomeRunnerSettings();
        new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("PhoneHomeRunner").Bind(settings);
        return settings;
    }

    private static string Substitute(string value) =>
        Regex.Replace(value, @"\$\{([A-Za-z_][A-Za-z0-9_]*)(?::([?-])([^}]*))?\}", match =>
        {
            var name = match.Groups[1].Value;
            if (name == "ANTIPHON_PUBLIC_ORIGIN")
                return "https://example.invalid";
            if (name == "PHONE_HOME_SHARED_SECRET")
                return "fixture-secret-1775";
            if (name == "PHONE_HOME_HOST_WORKSPACE_ROOT")
                return "/work";
            if (name == SecretKey)
                return FixtureSecret;
            if (name == HomeKey)
                return FixtureHome;
            if (name == "POSTGRES_PASSWORD")
                return "fixture-password-1744";
            if (match.Groups[2].Value == "-")
                return match.Groups[3].Value;
            throw new InvalidOperationException("unset compose variable " + name);
        });

    private static string EnvBody(string cert, string key, string token, bool secret, bool home)
    {
        var body = new StringBuilder();
        body.Append("POSTGRES_PASSWORD=fixture-password-1744\n");
        body.Append("HANGFIRE_STORAGE=InMemory\n");
        body.Append("ANTIPHON_PUBLIC_ORIGIN=https://example.invalid\n");
        body.Append("PHONE_HOME_SHARED_SECRET=fixture-secret-1775\n");
        body.Append("PHONE_HOME_HOST_WORKSPACE_ROOT=/work\n");
        if (secret)
            body.Append(SecretKey).Append('=').Append(FixtureSecret).Append('\n');
        if (home)
            body.Append(HomeKey).Append('=').Append(FixtureHome).Append('\n');
        body.Append("KEYRING_CERT_FILE=").Append(cert).Append('\n');
        body.Append("KEYRING_KEY_FILE=").Append(key).Append('\n');
        body.Append("OPERATOR_TOKEN_FILE=").Append(token).Append('\n');
        return body.ToString();
    }

    private static async Task WriteSecretAsync(string path, string body)
    {
        await File.WriteAllTextAsync(path, body);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
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

    private static string Read(string relative)
    {
        var path = Path.Combine(DockerStackDocuments.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).ShouldBeTrue(relative);
        return File.ReadAllText(path);
    }
}
