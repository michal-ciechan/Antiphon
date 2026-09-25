using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Resilience;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Resilience;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.GitHub;

/// <summary>
/// Implements GitHub operations via the GitHub REST API using HttpClient (FR59-FR64).
/// Uses personal access token from GithubSettings for authentication.
/// </summary>
public class GitHubService : IGitHubService
{
    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<GitHubService> _logger;
    private readonly GithubSettings _settings;
    private readonly TimeProvider _time;
    private readonly IOptionsMonitor<ResilienceSettings>? _resilience;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GitHubService(
        HttpClient httpClient,
        IOptions<GithubSettings> settings,
        ILogger<GitHubService> logger,
        IHttpClientFactory? httpClientFactory = null,
        TimeProvider? time = null,
        IOptionsMonitor<ResilienceSettings>? resilience = null)
    {
        _httpClient = httpClient;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _resilience = resilience;

        _httpClient.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Antiphon", "1.0"));

        if (!string.IsNullOrEmpty(_settings.PersonalAccessToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _settings.PersonalAccessToken);
        }
    }

    public async Task<int> CreatePullRequestAsync(
        string owner, string repo, string sourceBranch, string targetBranch,
        string title, string body, CancellationToken ct)
    {
        _logger.LogInformation(
            "Creating PR from {Source} to {Target} in {Owner}/{Repo}",
            sourceBranch, targetBranch, owner, repo);

        var payload = new
        {
            title,
            body,
            head = sourceBranch,
            @base = targetBranch
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"repos/{owner}/{repo}/pulls", content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var prNumber = doc.RootElement.GetProperty("number").GetInt32();

        _logger.LogInformation("Created PR #{PrNumber} in {Owner}/{Repo}", prNumber, owner, repo);
        return prNumber;
    }

    public async Task PushBranchAsync(string repoPath, string branchName, CancellationToken ct)
    {
        _logger.LogInformation("Pushing branch {Branch} in {RepoPath}", branchName, repoPath);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(60));
        var result = await new Git.LandingGit().RunAsync(repoPath, ["push", "origin", branchName], cts.Token);
        if (!result.Succeeded) throw new InvalidOperationException(result.Diagnostic);

        _logger.LogInformation("Successfully pushed branch {Branch}", branchName);
    }

    public async Task<IReadOnlyList<PullRequestComment>> GetPullRequestCommentsAsync(
        string owner, string repo, int prNumber, CancellationToken ct)
    {
        _logger.LogDebug("Fetching comments for PR #{PrNumber} in {Owner}/{Repo}", prNumber, owner, repo);

        var comments = new List<PullRequestComment>();
        var budget = ReadBudget();

        // Get issue comments (general PR comments)
        var issueCommentsJson = await ReadStringAsync(
            $"repos/{owner}/{repo}/issues/{prNumber}/comments",
            ResilienceOperations.GitHubPullRequestComments,
            budget,
            ct);
        using var issueDoc = JsonDocument.Parse(issueCommentsJson);
        foreach (var element in issueDoc.RootElement.EnumerateArray())
        {
            comments.Add(ParseComment(element, isReviewComment: false));
        }

        // Get review comments (inline code review comments)
        var reviewCommentsJson = await ReadStringAsync(
            $"repos/{owner}/{repo}/pulls/{prNumber}/comments",
            ResilienceOperations.GitHubPullRequestComments,
            budget,
            ct);
        using var reviewDoc = JsonDocument.Parse(reviewCommentsJson);
        foreach (var element in reviewDoc.RootElement.EnumerateArray())
        {
            comments.Add(ParseComment(element, isReviewComment: true));
        }

        return comments;
    }

    public async Task<PullRequestStatus> GetPullRequestStatusAsync(
        string owner, string repo, int prNumber, CancellationToken ct)
    {
        _logger.LogDebug("Fetching status for PR #{PrNumber} in {Owner}/{Repo}", prNumber, owner, repo);
        var budget = ReadBudget();

        // Get the PR to find the head SHA
        var detail = await GetPullRequestDetailAsync(owner, repo, prNumber, budget, ct);

        // Get combined status
        var statusJson = await ReadStringAsync(
            $"repos/{owner}/{repo}/commits/{detail.HeadSha}/status",
            ResilienceOperations.GitHubCombinedStatus,
            budget,
            ct);
        using var statusDoc = JsonDocument.Parse(statusJson);
        var state = statusDoc.RootElement.GetProperty("state").GetString() ?? "unknown";

        // Get check runs
        var checksJson = await ReadStringAsync(
            $"repos/{owner}/{repo}/commits/{detail.HeadSha}/check-runs",
            ResilienceOperations.GitHubCheckRuns,
            budget,
            ct);
        using var checksDoc = JsonDocument.Parse(checksJson);
        var checkRuns = new List<CheckRunInfo>();
        foreach (var element in checksDoc.RootElement.GetProperty("check_runs").EnumerateArray())
        {
            checkRuns.Add(new CheckRunInfo(
                element.GetProperty("name").GetString() ?? "unknown",
                element.GetProperty("status").GetString() ?? "unknown",
                element.TryGetProperty("conclusion", out var conclusion) ? conclusion.GetString() : null));
        }

        return new PullRequestStatus(state, checkRuns);
    }

    public Task<PullRequestDetail> GetPullRequestDetailAsync(
        string owner, string repo, int prNumber, CancellationToken ct) =>
        GetPullRequestDetailAsync(owner, repo, prNumber, ReadBudget(), ct);

    private async Task<PullRequestDetail> GetPullRequestDetailAsync(
        string owner, string repo, int prNumber, ResilienceBudget budget, CancellationToken ct)
    {
        var prJson = await ReadStringAsync(
            $"repos/{owner}/{repo}/pulls/{prNumber}",
            ResilienceOperations.GitHubPullRequest,
            budget,
            ct);
        using var prDoc = JsonDocument.Parse(prJson);
        var root = prDoc.RootElement;

        return new PullRequestDetail(
            Number: root.GetProperty("number").GetInt32(),
            State: root.GetProperty("state").GetString() ?? "unknown",
            IsMerged: root.TryGetProperty("merged", out var merged) && merged.GetBoolean(),
            HeadSha: root.GetProperty("head").GetProperty("sha").GetString() ?? string.Empty,
            BaseBranch: root.GetProperty("base").GetProperty("ref").GetString() ?? string.Empty,
            HeadBranch: root.GetProperty("head").GetProperty("ref").GetString() ?? string.Empty);
    }

    public async Task<(bool Success, string? Login, string? Error)> CheckConnectivityAsync(CancellationToken ct)
    {
        try
        {
            var json = await ReadStringAsync("user", ResilienceOperations.GitHubUser, ReadBudget(), ct);
            using var doc = JsonDocument.Parse(json);
            var login = doc.RootElement.TryGetProperty("login", out var loginProp)
                ? loginProp.GetString()
                : null;
            return (true, login, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GitHub connectivity check failed");
            return (false, null, ex.Message);
        }
    }

    public async Task<IReadOnlyList<GitHubRepoDto>> GetRepositoriesAsync(CancellationToken ct)
    {
        _logger.LogInformation("Fetching all repositories from GitHub");

        var repos = new List<GitHubRepoDto>();
        var page = 1;
        const int perPage = 100;
        var budget = ReadBudget();

        while (true)
        {
            var json = await ReadStringAsync(
                $"user/repos?per_page={perPage}&page={page}&type=all",
                ResilienceOperations.GitHubRepositories,
                budget,
                ct);

            using var doc = JsonDocument.Parse(json);
            var array = doc.RootElement;

            if (array.GetArrayLength() == 0)
                break;

            foreach (var element in array.EnumerateArray())
            {
                repos.Add(new GitHubRepoDto(
                    FullName: element.GetProperty("full_name").GetString() ?? string.Empty,
                    CloneUrl: element.GetProperty("clone_url").GetString() ?? string.Empty,
                    HtmlUrl: element.GetProperty("html_url").GetString() ?? string.Empty,
                    IsPrivate: element.GetProperty("private").GetBoolean()));
            }

            if (array.GetArrayLength() < perPage)
                break;

            page++;
        }

        _logger.LogInformation("Fetched {Count} repositories", repos.Count);
        return repos;
    }

    public async Task<IReadOnlyList<GitHubBranchDto>> GetBranchesAsync(
        string owner, string repo, CancellationToken ct)
    {
        _logger.LogDebug("Fetching branches for {Owner}/{Repo}", owner, repo);

        var json = await ReadStringAsync(
            $"repos/{owner}/{repo}/branches?per_page=100",
            ResilienceOperations.GitHubBranches,
            ReadBudget(),
            ct);

        using var doc = JsonDocument.Parse(json);
        var branches = new List<GitHubBranchDto>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            branches.Add(new GitHubBranchDto(
                Name: element.GetProperty("name").GetString() ?? string.Empty,
                Sha: element.GetProperty("commit").GetProperty("sha").GetString() ?? string.Empty,
                IsProtected: element.GetProperty("protected").GetBoolean()
            ));
        }

        return branches;
    }

    public async Task<PullRequestInfo?> FindPullRequestForBranchAsync(
        string owner, string repo, string headBranch, CancellationToken ct)
    {
        _logger.LogDebug(
            "Looking for PR with head branch {Branch} in {Owner}/{Repo}",
            headBranch, owner, repo);

        try
        {
            var budget = ReadBudget();
            // Search open PRs first, then closed (to surface active PRs first)
            foreach (var state in new[] { "open", "closed" })
            {
                var encodedBranch = Uri.EscapeDataString(headBranch);
                var json = await ReadStringAsync(
                    $"repos/{owner}/{repo}/pulls?state={state}&head={owner}:{encodedBranch}&per_page=1",
                    ResilienceOperations.GitHubPullRequestByBranch,
                    budget,
                    ct);

                using var doc = JsonDocument.Parse(json);
                var array = doc.RootElement;

                if (array.GetArrayLength() == 0)
                    continue;

                var pr = array[0];
                return new PullRequestInfo(
                    Number: pr.GetProperty("number").GetInt32(),
                    Title: pr.GetProperty("title").GetString() ?? string.Empty,
                    State: pr.GetProperty("state").GetString() ?? "unknown",
                    BaseBranch: pr.GetProperty("base").GetProperty("ref").GetString() ?? string.Empty,
                    HtmlUrl: pr.GetProperty("html_url").GetString() ?? string.Empty);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to look up PR for branch {Branch} in {Owner}/{Repo}", headBranch, owner, repo);
            return null;
        }
    }

    private ResilienceBudget ReadBudget() =>
        ResilienceBudget.Start(_time, _resilience?.CurrentValue ?? new ResilienceSettings(), profile: null);

    private async Task<string> ReadStringAsync(
        string path,
        string operation,
        ResilienceBudget budget,
        CancellationToken ct)
    {
        var client = ResilienceReadClients.Select(
            _httpClientFactory, _httpClient, ResilienceClientNames.GitHubRead);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        ResilienceRequest.Stamp(request, operation, budget);
        ResilienceRequest.CopyDefaultHeaders(_httpClient, request);
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static PullRequestComment ParseComment(JsonElement element, bool isReviewComment)
    {
        return new PullRequestComment(
            Id: element.GetProperty("id").GetInt64(),
            Author: element.GetProperty("user").GetProperty("login").GetString() ?? "unknown",
            Body: element.GetProperty("body").GetString() ?? string.Empty,
            CreatedAt: element.GetProperty("created_at").GetDateTime(),
            IsReviewComment: isReviewComment);
    }
}
