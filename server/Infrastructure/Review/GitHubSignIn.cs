using System.Net.Http.Headers;
using System.Text.Json;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Review;

/// <summary>
/// GitHub's OAuth web flow for sign-in (<c>Review:Auth</c>): <c>{web}/login/oauth/authorize</c>, then
/// <c>POST {web}/login/oauth/access_token</c> for the code, then <c>GET {api}/user</c>. Works for a GitHub
/// App's user authorization and for an OAuth App. No scope is asked for: the login is all it reads. The
/// user token is used for <c>/user</c> only and then dropped.
/// </summary>
public sealed class GitHubSignInClient(HttpClient http, IOptions<ReviewSettings> settings) : IGitHubSignIn
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(20);

    public Uri AuthorizeUrl(string state, string redirectUri)
    {
        var auth = settings.Value.Auth;
        var query = $"client_id={Uri.EscapeDataString(auth.ClientId ?? "")}&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                    $"&state={Uri.EscapeDataString(state)}&allow_signup=false";
        return new Uri($"{auth.WebBaseUrl.TrimEnd('/')}/login/oauth/authorize?{query}");
    }

    public async Task<string> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct)
    {
        var auth = settings.Value.Auth;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{auth.WebBaseUrl.TrimEnd('/')}/login/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = auth.ClientId ?? "",
                ["client_secret"] = auth.ClientSecret ?? "",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
            }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Antiphon-Review", "1.0"));
        using var doc = await SendAsync(request, "the code exchange", ct);
        var root = doc.RootElement;
        if (root.TryGetProperty("access_token", out var token) && token.GetString() is { Length: > 0 } value)
            return value;
        // GitHub answers a bad or reused code with 200 and an error (bad_verification_code, redirect_uri_mismatch, ...).
        var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
        var detail = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
        throw new GitHubSignInException($"GitHub refused the sign-in code ({error ?? "no token"}{(detail is null ? "" : ": " + detail)}).");
    }

    public async Task<GitHubSignInUser> GetUserAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{settings.Value.Auth.ResolvedApiBaseUrl()}/user");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GitHubCodeHost.JsonMediaType));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Antiphon-Review", "1.0"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", GitHubCodeHost.ApiVersion);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var doc = await SendAsync(request, "GET /user", ct);
        var root = doc.RootElement;
        var login = root.TryGetProperty("login", out var l) ? l.GetString() : null;
        if (string.IsNullOrWhiteSpace(login) || !root.TryGetProperty("id", out var id) || !id.TryGetInt64(out var userId))
            throw new GitHubSignInException("GitHub's GET /user answer has no login.");
        var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        return new GitHubSignInUser(userId, login, name);
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, string what, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Budget);
        try
        {
            using var response = await http.SendAsync(request, budget.Token);
            var body = await response.Content.ReadAsStringAsync(budget.Token);
            if (!response.IsSuccessStatusCode)
                throw new GitHubSignInException($"GitHub answered {what} with {(int)response.StatusCode}.");
            return JsonDocument.Parse(body);
        }
        catch (HttpRequestException ex)
        {
            throw new GitHubSignInException($"GitHub could not be reached for {what}: {ex.Message}");
        }
        catch (JsonException)
        {
            throw new GitHubSignInException($"GitHub's answer to {what} is not JSON.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GitHubSignInException($"GitHub did not answer {what} within {Budget.TotalSeconds:0}s.");
        }
    }
}

/// <summary><see cref="IReviewRepoAccess"/> through the repo's GitHub code host, with its App installation token or its PAT.</summary>
public sealed class GitHubReviewRepoAccess(CodeHostRouter router, GitHubCodeHost gitHub) : IReviewRepoAccess
{
    public Task<string> GetPermissionAsync(string repo, string login, CancellationToken ct) =>
        gitHub.GetRepoPermissionAsync(router.Resolve(ReviewHost.GitHub, repo, null, null, null).Target, login, ct);
}
