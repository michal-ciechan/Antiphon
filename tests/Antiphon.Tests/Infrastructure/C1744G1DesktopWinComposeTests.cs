using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.Scripts;
using Microsoft.Extensions.Configuration;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1744 S1. W-1..W-3 read the production compose and run the deploy command in process (unit 64).
/// They do not start a daemon, a container, or a database.
/// </summary>
[Category("Unit")]
public sealed class C1744G1DesktopWinComposeTests
{
    private const string ComposePath = "docker-compose.server2-server.yml";
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
    public void G1_deploy_whatif_refuses_without_desktop_win_keys()
    {
        // Unit 64: the deploy-server2-server command in process (it ran the script under pwsh with a Unix fake docker on Linux only).
        var world = new Server2ServerDeployWorld();
        var cert = world.Cert;
        var key = world.Key;
        var token = world.Token;

        var missingSecret = world.Production("--what-if", "--env-file", world.EnvLines("no-secret.env", EnvBody(cert, key, token, secret: false, home: true)));
        missingSecret.Exit.ShouldBe(2, missingSecret.ToString());
        missingSecret.Lines.Last().ShouldBe("SERVER2-SERVER fail exit=2 reason=env-missing");
        missingSecret.Tools.ShouldBeEmpty();
        missingSecret.Hides(FixtureSecret).ShouldBeTrue();

        var missingHome = world.Production("--what-if", "--env-file", world.EnvLines("no-home.env", EnvBody(cert, key, token, secret: true, home: false)));
        missingHome.Exit.ShouldBe(2, missingHome.ToString());
        missingHome.Lines.Last().ShouldBe("SERVER2-SERVER fail exit=2 reason=env-missing");
        missingHome.Tools.ShouldBeEmpty();

        var ready = world.Production("--what-if", "--env-file", world.EnvLines("ready.env", EnvBody(cert, key, token, secret: true, home: true)));
        ready.Exit.ShouldBe(0, ready.ToString());
        ready.Lines.Last().ShouldBe("SERVER2-SERVER plan exit=0 reason=ok");
        ready.Tools.ShouldBeEmpty();
        ready.Hides(FixtureSecret).ShouldBeTrue();
        ready.Out.ShouldNotContain(FixtureHome);
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

    private static string[] EnvBody(string cert, string key, string token, bool secret, bool home)
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
        return body.ToString().TrimEnd('\n').Split('\n');
    }

    private static string Read(string relative)
    {
        var path = Path.Combine(DockerStackDocuments.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).ShouldBeTrue(relative);
        return File.ReadAllText(path);
    }
}
