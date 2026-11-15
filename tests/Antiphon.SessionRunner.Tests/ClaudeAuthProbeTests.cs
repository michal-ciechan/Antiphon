using System.Diagnostics;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0628 D-7 / G-12. The runner's Claude sign-in probe: what it parses, what it keeps, what it
/// never logs, and the operator's two admitted stores (setup-token env, or the login's
/// <c>.credentials.json</c>) with no API-key fallback.
/// </summary>
public class ClaudeAuthProbeTests
{
    private const string LoggedInJson =
        """{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","email":"someone@example.test","orgId":"org-0000-secret","orgName":"Example Org","subscriptionType":"max"}""";

    private const string LoggedOutJson = """{"loggedIn":false,"authMethod":"none","apiProvider":"firstParty"}""";

    [Test, Category("Unit")]
    public async Task Logged_in_json_is_parsed_to_the_three_fields_only()
    {
        var log = new List<string>();
        var probe = Probe((_, _) => Task.FromResult((0, LoggedInJson, "")), log: log);

        var dto = await probe.ProbeAsync("claude", CancellationToken.None);

        dto.Provider.ShouldBe("claude");
        dto.LoggedIn.ShouldBe(true);
        dto.AuthMethod.ShouldBe("claude.ai");
        dto.SubscriptionType.ShouldBe("max");
        dto.Error.ShouldBeNull();

        var serialized = System.Text.Json.JsonSerializer.Serialize(dto, PhoneHomeFraming.Json);
        var logged = string.Join("\n", log);
        foreach (var secret in new[] { "someone@example.test", "org-0000-secret", "Example Org", "firstParty" })
        {
            serialized.ShouldNotContain(secret);
            logged.ShouldNotContain(secret);
        }

        logged.ShouldContain("loggedIn=True");
        logged.ShouldContain("authMethod=claude.ai");
        logged.ShouldContain("subscriptionType=max");
    }

    [Test, Category("Unit")]
    public async Task Logged_out_json_with_exit_1_is_logged_out()
    {
        var dto = await Probe((_, _) => Task.FromResult((1, LoggedOutJson, ""))).ProbeAsync("claude", CancellationToken.None);

        dto.LoggedIn.ShouldBe(false);
        dto.AuthMethod.ShouldBe("none");
        dto.Error.ShouldBeNull();
    }

    [Test, Category("Unit")]
    public async Task Garbage_nonzero_and_timeout_are_unknown()
    {
        var garbage = await Probe((_, _) => Task.FromResult((0, "not json {", ""))).ProbeAsync("claude", CancellationToken.None);
        garbage.LoggedIn.ShouldBeNull();
        garbage.Error.ShouldBe("unparseable");

        var nonzero = await Probe((_, _) => Task.FromResult((2, "", "boom"))).ProbeAsync("claude", CancellationToken.None);
        nonzero.LoggedIn.ShouldBeNull();
        nonzero.Error.ShouldBe("nonzero_exit");

        // JSON without a boolean loggedIn is not an answer either.
        var shapeless = await Probe((_, _) => Task.FromResult((0, """{"authMethod":"claude.ai"}""", ""))).ProbeAsync("claude", CancellationToken.None);
        shapeless.LoggedIn.ShouldBeNull();

        var timeout = await Probe(
            async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return (0, LoggedInJson, "");
            },
            timeout: TimeSpan.FromMilliseconds(50)).ProbeAsync("claude", CancellationToken.None);
        timeout.LoggedIn.ShouldBeNull();
        timeout.Error.ShouldBe("timeout");

        var missing = await Probe((_, _) => throw new System.ComponentModel.Win32Exception(2)).ProbeAsync("claude", CancellationToken.None);
        missing.LoggedIn.ShouldBeNull();
        missing.Error.ShouldBe("missing_binary");
    }

    // Sets a process-wide sentinel ANTHROPIC_API_KEY so the strip is observable; kept out of parallel runs.
    [Test, Category("Unit"), NotInParallel]
    public async Task Probe_passes_claude_home_as_config_dir()
    {
        ProcessStartInfo? seen = null;
        var previousKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sentinel-api-key-value");
        try
        {
            await Probe((psi, _) =>
            {
                seen = psi;
                return Task.FromResult((1, LoggedOutJson, ""));
            }, claudeHome: "/state/claude-under-test").ProbeAsync("claude", CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previousKey);
        }

        seen.ShouldNotBeNull();
        seen.FileName.ShouldBe("claude");
        seen.ArgumentList.ShouldBe(["auth", "status", "--json"]);
        seen.Environment["CLAUDE_CONFIG_DIR"].ShouldBe("/state/claude-under-test");
        seen.Environment["DISABLE_AUTOUPDATER"].ShouldBe("1");
        // Card decision 1: the CLI never sees an API key, so it can never report one as signed in.
        seen.Environment.ContainsKey("ANTHROPIC_API_KEY").ShouldBeFalse();
        seen.Environment.ContainsKey("ANTHROPIC_AUTH_TOKEN").ShouldBeFalse();
    }

    [Test, Category("Unit")]
    public async Task Setup_token_env_or_credentials_file_is_provisioned_and_the_token_is_never_logged()
    {
        const string token = "sk-ant-oat01-sentinel-token-value";
        var log = new List<string>();

        // The CLI says signed out (or cannot tell), but the operator's setup-token is in the runner env.
        var tokenOnly = await Probe(
            (_, _) => Task.FromResult((1, LoggedOutJson, "")),
            environment: name => name == ClaudeAuthProbe.OAuthTokenVariable ? token : null,
            log: log).ProbeAsync("claude", CancellationToken.None);
        tokenOnly.LoggedIn.ShouldBe(true);
        tokenOnly.AuthMethod.ShouldBe("oauth_token");
        tokenOnly.Error.ShouldBeNull();

        // The interactive login's credential file under ClaudeHome, probe timing out.
        string? checkedPath = null;
        var fileOnly = await Probe(
            (_, _) => Task.FromResult((2, "", "")),
            fileExists: path =>
            {
                checkedPath = path;
                return true;
            },
            log: log).ProbeAsync("claude", CancellationToken.None);
        fileOnly.LoggedIn.ShouldBe(true);
        fileOnly.AuthMethod.ShouldBe("credentials_file");
        checkedPath.ShouldBe("/state/claude/.credentials.json");

        // A whitespace token is not a token, and with no file the CLI's "signed out" stands.
        var blank = await Probe(
            (_, _) => Task.FromResult((1, LoggedOutJson, "")),
            environment: name => name == ClaudeAuthProbe.OAuthTokenVariable ? "  " : null).ProbeAsync("claude", CancellationToken.None);
        blank.LoggedIn.ShouldBe(false);

        var logged = string.Join("\n", log);
        logged.ShouldNotContain(token);
        logged.ShouldNotContain("sentinel");
        logged.ShouldContain("tokenPresent=True");
        logged.ShouldContain("credentialsFilePresent=True");
    }

    [Test, Category("Unit")]
    public async Task Api_key_auth_is_never_provisioned()
    {
        foreach (var method in new[] { "api_key", "apiKey", "API Key" })
        {
            var dto = await Probe((_, _) => Task.FromResult((0,
                $$"""{"loggedIn":true,"authMethod":"{{method}}"}""", ""))).ProbeAsync("claude", CancellationToken.None);
            dto.LoggedIn.ShouldBe(false, method + " must not count as a Claude login");
            dto.Error.ShouldBe("api_key_refused");
        }
    }

    [Test, Category("Unit")]
    public async Task Unknown_provider_is_refused()
    {
        var ex = await Should.ThrowAsync<PhoneHomeAdmissionException>(
            () => Probe((_, _) => Task.FromResult((0, LoggedInJson, ""))).ProbeAsync("codex", CancellationToken.None));
        ex.Code.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
        ex.StatusCode.ShouldBe(400);
    }

    /// <summary>
    /// V-1: the real CLI against an empty store. Production keeps the runner's
    /// <c>CLAUDE_CODE_OAUTH_TOKEN</c> for the probe child; this witness allowlists that child's
    /// environment and points <c>CLAUDE_CONFIG_DIR</c> at an empty temp profile.
    /// </summary>
    [Test, Category("Integration"), ParallelLimiter<ProcessSpawnLimit>]
    public async Task Fresh_config_dir_reports_logged_out_with_the_real_cli()
    {
        if (!OnPath("claude"))
            throw new SkipTestException("claude is not on PATH");
        var home = Path.Combine(Path.GetTempPath(), "c1927-claude-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        var log = new List<string>();
        IReadOnlyList<string>? childNames = null;
        string? isolationFault = null;
        try
        {
            var probe = new ClaudeAuthProbe(
                new PhoneHomeSettings { ClaudeHome = home },
                new ListLogger<ClaudeAuthProbe>(log),
                run: (psi, ct) =>
                {
                    if (!TryIsolateRealCli(psi, home, out var names, out var fault))
                    {
                        isolationFault = fault;
                        throw new InvalidOperationException(fault);
                    }

                    childNames = names;
                    return StartRealCliAsync(psi, ct);
                },
                environment: _ => null,
                timeout: TimeSpan.FromSeconds(60));

            var dto = await probe.ProbeAsync("claude", CancellationToken.None);

            isolationFault.ShouldBeNull();
            dto.Error.ShouldBeNull();
            dto.LoggedIn.ShouldBe(false);
            dto.AuthMethod.ShouldNotBe("oauth_token");
            dto.AuthMethod.ShouldNotBe("credentials_file");
            childNames.ShouldNotBeNull();
            var names = childNames!;
            names.Any(name => string.Equals(name, "PATH", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
            names.Any(name => string.Equals(name, "CLAUDE_CONFIG_DIR", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
            names.Any(name => string.Equals(name, "HOME", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
            names.ShouldNotContain(ClaudeAuthProbe.OAuthTokenVariable);
            foreach (var stripped in ClaudeAuthProbe.StrippedVariables)
                names.ShouldNotContain(stripped);
            names.Count.ShouldBeLessThanOrEqualTo(ChildRuntimeNames.Length + IsolatedChildNames.Length);

            var logged = string.Join("\n", log);
            logged.ShouldContain("loggedIn=False");
            logged.ShouldContain("tokenPresent=False");
            logged.ShouldContain("credentialsFilePresent=False");
            logged.ShouldNotContain(ClaudeAuthProbe.OAuthTokenVariable);
            logged.ShouldNotContain("email");
            logged.ShouldNotContain("orgId");
            logged.ShouldNotContain("orgName");
            logged.ShouldNotContain("@");
        }
        finally
        {
            try { Directory.Delete(home, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Parent variables the real CLI may need to start. Credential names are never copied.</summary>
    private static readonly string[] ChildRuntimeNames =
    [
        "PATH", "PATHEXT", "SystemRoot", "WINDIR", "COMSPEC",
        "LANG", "LC_ALL", "LC_CTYPE", "TERM", "TZ",
        "SSL_CERT_FILE", "SSL_CERT_DIR", "USER", "LOGNAME", "USERNAME",
    ];

    private static readonly string[] IsolatedChildNames =
    [
        "HOME", "USERPROFILE", "XDG_CONFIG_HOME", "XDG_DATA_HOME",
        "TMPDIR", "TEMP", "TMP", "CLAUDE_CONFIG_DIR", "DISABLE_AUTOUPDATER",
    ];

    private static bool TryIsolateRealCli(
        ProcessStartInfo psi, string profile, out string[] names, out string fault)
    {
        names = [];
        fault = "";
        if (psi.FileName != "claude"
            || psi.ArgumentList.Count != 3
            || psi.ArgumentList[0] != "auth"
            || psi.ArgumentList[1] != "status"
            || psi.ArgumentList[2] != "--json")
        {
            fault = "production probe command was not claude auth status --json";
            return false;
        }

        if (!psi.Environment.TryGetValue("CLAUDE_CONFIG_DIR", out var configured)
            || !string.Equals(configured, profile, StringComparison.Ordinal))
        {
            fault = "CLAUDE_CONFIG_DIR was not the isolated profile";
            return false;
        }

        var preserved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in ChildRuntimeNames)
        {
            if (psi.Environment.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
                preserved[name] = value;
        }

        psi.Environment.Clear();
        foreach (var pair in preserved)
            psi.Environment[pair.Key] = pair.Value;

        var tmp = Path.Combine(profile, "tmp");
        Directory.CreateDirectory(tmp);
        psi.WorkingDirectory = profile;
        psi.Environment["HOME"] = profile;
        psi.Environment["USERPROFILE"] = profile;
        psi.Environment["XDG_CONFIG_HOME"] = profile;
        psi.Environment["XDG_DATA_HOME"] = profile;
        psi.Environment["TMPDIR"] = tmp;
        psi.Environment["TEMP"] = tmp;
        psi.Environment["TMP"] = tmp;
        psi.Environment["CLAUDE_CONFIG_DIR"] = profile;
        psi.Environment["DISABLE_AUTOUPDATER"] = "1";

        names = psi.Environment.Keys.ToArray();
        var allowed = new HashSet<string>(ChildRuntimeNames, StringComparer.OrdinalIgnoreCase);
        foreach (var name in IsolatedChildNames)
            allowed.Add(name);
        foreach (var name in names)
        {
            if (!allowed.Contains(name) || NamesCredential(name))
            {
                fault = "isolated child environment has a disallowed name: " + name;
                return false;
            }
        }

        return true;
    }

    private static bool NamesCredential(string name) =>
        name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)
        || name.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
        || name.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
        || name.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase)
        || name.Contains("API_KEY", StringComparison.OrdinalIgnoreCase);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> StartRealCliAsync(
        ProcessStartInfo psi, CancellationToken ct)
    {
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("claude did not start");
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }

        return (process.ExitCode, await stdout, await stderr);
    }

    private static bool OnPath(string name)
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd" } : new[] { "" };
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => extensions.Any(ext => File.Exists(Path.Combine(dir, name + ext))));
    }

    private static ClaudeAuthProbe Probe(
        Func<ProcessStartInfo, CancellationToken, Task<(int, string, string)>> run,
        Func<string, string?>? environment = null,
        Func<string, bool>? fileExists = null,
        List<string>? log = null,
        string claudeHome = "/state/claude",
        TimeSpan? timeout = null) =>
        new(
            new PhoneHomeSettings { ClaudeHome = claudeHome },
            new ListLogger<ClaudeAuthProbe>(log ?? []),
            run: run,
            environment: environment ?? (_ => null),
            fileExists: fileExists ?? (_ => false),
            timeout: timeout);
}
