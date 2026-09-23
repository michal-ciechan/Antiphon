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

    /// <summary>V-1: the real desktop binary against an empty store, through the production runner.</summary>
    [Test, Category("Integration"), ParallelLimiter<ProcessSpawnLimit>]
    public async Task Fresh_config_dir_reports_logged_out_with_the_real_cli()
    {
        if (!OnPath("claude"))
            throw new SkipTestException("claude is not on PATH");
        var home = Path.Combine(Path.GetTempPath(), "c628-claude-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        try
        {
            var probe = new ClaudeAuthProbe(
                new PhoneHomeSettings { ClaudeHome = home },
                environment: _ => null,
                timeout: TimeSpan.FromSeconds(60));

            var dto = await probe.ProbeAsync("claude", CancellationToken.None);

            dto.Error.ShouldBeNull();
            dto.LoggedIn.ShouldBe(false);
        }
        finally
        {
            try { Directory.Delete(home, recursive: true); } catch { /* best effort */ }
        }
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
