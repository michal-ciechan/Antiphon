using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Infrastructure.Review;

/// <summary>
/// Installation tokens for a GitHub App sign-in (<see cref="ReviewGitHubApp"/>). A token is minted by
/// signing a ten-minute RS256 JWT with the app's private key and posting it to
/// <c>/app/installations/{id}/access_tokens</c>; GitHub returns a token valid for one hour. Tokens are
/// cached per host and installation and renewed five minutes before they expire. Neither the key, the
/// JWT nor the token is ever logged or put in an exception message.
/// </summary>
public sealed class GitHubAppTokens(TimeProvider clock)
{
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresAt)> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _installations = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _mint = new(1, 1);

    public GitHubAppTokens() : this(TimeProvider.System)
    {
    }

    /// <summary>The installation token for <paramref name="repo"/> on this host, minting one when none is fresh.</summary>
    public async Task<string> GetTokenAsync(HttpClient http, string hostId, ReviewCodeHost host, Uri apiRoot, string repo, CancellationToken ct)
    {
        var app = host.App is { IsConfigured: true } a
            ? a
            : throw new CodeHostException(CodeHostFailureKind.NotConfigured, $"GitHub host '{hostId}' has an App section without an app id and private key.");

        if (Fresh(InstallationKey(hostId, app, repo)) is { } cached)
            return cached;

        await _mint.WaitAsync(ct);
        try
        {
            var jwt = CreateJwt(app.AppId!, app.PrivateKey!, clock.GetUtcNow());
            var installation = app.InstallationId ?? await FindInstallationAsync(http, hostId, apiRoot, jwt, repo, ct);
            var key = $"{hostId}|{installation}";
            if (Fresh(key) is { } minted)
                return minted;

            var body = await SendAsync(http, HttpMethod.Post, new Uri(apiRoot, $"app/installations/{installation}/access_tokens"), jwt, ct);
            using var doc = JsonDocument.Parse(body);
            var token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(token))
                throw new CodeHostException(CodeHostFailureKind.Unavailable, "GitHub App token exchange returned no token.");
            var expires = doc.RootElement.TryGetProperty("expires_at", out var e) && e.TryGetDateTimeOffset(out var at)
                ? at
                : clock.GetUtcNow().AddHours(1);
            _tokens[key] = (token, expires);
            return token;
        }
        finally
        {
            _mint.Release();
        }
    }

    /// <summary>A ten-minute app JWT (issued a minute back for clock drift), signed RS256 with the PEM key.</summary>
    public static string CreateJwt(string appId, string privateKeyPem, DateTimeOffset now)
    {
        var header = Base64Url("""{"alg":"RS256","typ":"JWT"}"""u8.ToArray());
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iat = now.AddSeconds(-60).ToUnixTimeSeconds(),
            exp = now.AddMinutes(9).ToUnixTimeSeconds(),
            iss = appId.Trim(),
        }));
        using var rsa = RSA.Create();
        try
        {
            // A key pasted into an environment variable often carries literal \n.
            rsa.ImportFromPem(privateKeyPem.Replace("\\n", "\n"));
        }
        catch (ArgumentException)
        {
            throw new CodeHostException(CodeHostFailureKind.NotConfigured, "The GitHub App private key is not a PEM RSA key.");
        }

        var signature = rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{payload}.{Base64Url(signature)}";
    }

    private string? Fresh(string? key) =>
        key is not null && _tokens.TryGetValue(key, out var entry) && entry.ExpiresAt - clock.GetUtcNow() > RenewBefore ? entry.Token : null;

    private string? InstallationKey(string hostId, ReviewGitHubApp app, string repo) =>
        app.InstallationId is { } id ? $"{hostId}|{id}"
        : _installations.TryGetValue($"{hostId}|{repo}", out var found) ? $"{hostId}|{found}"
        : null;

    private async Task<long> FindInstallationAsync(HttpClient http, string hostId, Uri apiRoot, string jwt, string repo, CancellationToken ct)
    {
        if (_installations.TryGetValue($"{hostId}|{repo}", out var known))
            return known;
        var body = await SendAsync(http, HttpMethod.Get, new Uri(apiRoot, $"repos/{repo}/installation"), jwt, ct);
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("id", out var id) || !id.TryGetInt64(out var installation))
            throw new CodeHostException(CodeHostFailureKind.Unavailable, $"GitHub App installation lookup for {repo} returned no id.");
        _installations[$"{hostId}|{repo}"] = installation;
        return installation;
    }

    private static async Task<string> SendAsync(HttpClient http, HttpMethod method, Uri uri, string jwt, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GitHubCodeHost.JsonMediaType));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Antiphon-Review", "1.0"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", GitHubCodeHost.ApiVersion);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
                return body;
            var status = (int)response.StatusCode;
            var kind = status switch
            {
                404 => CodeHostFailureKind.NotFound,
                401 or 403 => CodeHostFailureKind.Rejected,
                _ => CodeHostFailureKind.Unavailable,
            };
            // The body is GitHub's error message; it never echoes the JWT.
            throw new CodeHostException(kind, $"GitHub App {method.Method} {uri.AbsolutePath}: HTTP {status}: {(body.Length <= 300 ? body : body[..300])}", status);
        }
        catch (HttpRequestException ex)
        {
            throw new CodeHostException(CodeHostFailureKind.Unavailable, $"GitHub App {method.Method} {uri.AbsolutePath}: {ex.Message}");
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
