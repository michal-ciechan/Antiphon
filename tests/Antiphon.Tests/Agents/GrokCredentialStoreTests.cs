using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0324 S3: file-only inspection of Grok's OAuth store. The lock file is a permanent
/// artefact and is ignored. Unreadable files are Present so a probe never blocks a launch
/// that would have worked.
/// </summary>
[Category("Unit")]
public class GrokCredentialStoreTests
{
    [Test]
    public void Absent_when_auth_json_is_missing()
    {
        using var home = new TempHome();
        GrokCredentialStore.Inspect(home.Path).ShouldBe(GrokCredentialStore.Finding.Absent);
    }

    [Test]
    public void Lock_only_is_Absent()
    {
        using var home = new TempHome();
        File.WriteAllText(Path.Combine(home.Path, "auth.json.lock"), "46160:1788377353");
        GrokCredentialStore.Inspect(home.Path).ShouldBe(GrokCredentialStore.Finding.Absent);
    }

    [Test]
    public void Empty_scope_is_Empty()
    {
        using var home = new TempHome();
        File.WriteAllText(Path.Combine(home.Path, "auth.json"), """{"https://auth.x.ai::client": {}}""");
        GrokCredentialStore.Inspect(home.Path).ShouldBe(GrokCredentialStore.Finding.Empty);
    }

    [Test]
    public void Present_when_a_scope_has_a_key()
    {
        using var home = new TempHome();
        WriteStore(home.Path, key: "sess-key", refresh: "rt", expiresAtUnix: DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        GrokCredentialStore.Inspect(home.Path).ShouldBe(GrokCredentialStore.Finding.Present);
    }

    [Test]
    public void Expired_with_refresh_token_is_still_Present()
    {
        using var home = new TempHome();
        WriteStore(home.Path, key: "sess-key", refresh: "rt", expiresAtUnix: DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds());
        GrokCredentialStore.Inspect(home.Path).ShouldBe(GrokCredentialStore.Finding.Present);
    }

    [Test]
    public void Expired_without_refresh_token_is_Empty()
    {
        using var home = new TempHome();
        WriteStore(home.Path, key: "sess-key", refresh: null, expiresAtUnix: DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds());
        GrokCredentialStore.Inspect(home.Path).ShouldBe(GrokCredentialStore.Finding.Empty);
    }

    [Test]
    public void GROK_AUTH_PATH_override_is_honoured()
    {
        using var home = new TempHome();
        var alt = Path.Combine(home.Path, "elsewhere.json");
        File.WriteAllText(alt, """{"https://auth.x.ai::client": {"key": "k", "refresh_token": "rt"}}""");
        var env = new Dictionary<string, string> { ["GROK_AUTH_PATH"] = alt };
        GrokCredentialStore.Inspect(home.Path, env).ShouldBe(GrokCredentialStore.Finding.Present);
        GrokCredentialStore.Inspect(home.Path).ShouldBe(GrokCredentialStore.Finding.Absent);
    }

    [Test]
    public void Api_key_in_launch_env_skips_the_store()
    {
        using var home = new TempHome();
        var env = new Dictionary<string, string> { ["XAI_API_KEY"] = "xai-test" };
        GrokCredentialStore.Inspect(home.Path, env).ShouldBe(GrokCredentialStore.Finding.ApiKeyAuth);
    }

    [Test]
    public void Grok_code_api_key_or_provider_command_skips_the_store()
    {
        using var home = new TempHome();
        GrokCredentialStore.Inspect(home.Path, new Dictionary<string, string>
        {
            ["GROK_CODE_XAI_API_KEY"] = "proxy-key",
        }).ShouldBe(GrokCredentialStore.Finding.ApiKeyAuth);
        GrokCredentialStore.Inspect(home.Path, new Dictionary<string, string>
        {
            ["GROK_AUTH_PROVIDER_COMMAND"] = "gkp.ps1",
        }).ShouldBe(GrokCredentialStore.Finding.ApiKeyAuth);
    }

    [Test]
    public void Unreadable_is_Present()
    {
        using var home = new TempHome();
        var path = Path.Combine(home.Path, "auth.json");
        Directory.CreateDirectory(path);
        GrokCredentialStore.Inspect(home.Path).ShouldBe(GrokCredentialStore.Finding.Present);
    }

    [Test]
    public void Malformed_json_is_Present()
    {
        using var home = new TempHome();
        File.WriteAllText(Path.Combine(home.Path, "auth.json"), "{not-json");
        GrokCredentialStore.Inspect(home.Path).ShouldBe(GrokCredentialStore.Finding.Present);
    }

    [Test]
    public void IsLaunchBlocking_only_Absent_and_Empty()
    {
        GrokCredentialStore.IsLaunchBlocking(GrokCredentialStore.Finding.Absent).ShouldBeTrue();
        GrokCredentialStore.IsLaunchBlocking(GrokCredentialStore.Finding.Empty).ShouldBeTrue();
        GrokCredentialStore.IsLaunchBlocking(GrokCredentialStore.Finding.Present).ShouldBeFalse();
        GrokCredentialStore.IsLaunchBlocking(GrokCredentialStore.Finding.ApiKeyAuth).ShouldBeFalse();
    }

    private static void WriteStore(string home, string key, string? refresh, long expiresAtUnix)
    {
        var refreshJson = refresh is null ? "null" : $"\"{refresh}\"";
        File.WriteAllText(
            Path.Combine(home, "auth.json"),
            "{\"https://auth.x.ai::client\": {\"key\": \"" + key
            + "\", \"auth_mode\": \"oidc\", \"refresh_token\": " + refreshJson
            + ", \"expires_at\": " + expiresAtUnix + "}}");
    }

    private sealed class TempHome : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-grok-cred-").FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
