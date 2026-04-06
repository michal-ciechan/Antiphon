using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Interfaces;
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
    private readonly ILogger<GitHubService> _logger;
    private readonly GithubSettings _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GitHubService(HttpClient httpClient, IOptions<GithubSettings> settings, ILogger<GitHubService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

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

        // Use git CLI to push — the GitHub API doesn't support push operations
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"push origin {branchName}",
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git push process.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw;
        }

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            _logger.LogError("git push failed (exit {ExitCode}): {StdErr}", process.ExitCode, stderr);
            throw new InvalidOperationException($"git push failed with exit code {process.ExitCode}: {stderr}");
        }

        _logger.LogInformation("Successfully pushed branch {Branch}", branchName);
    }

    public async Task<IReadOnlyList<PullRequestComment>> GetPullRequestCommentsAsync(
        string owner, string repo, int prNumber, CancellationToken ct)
    {
        _logger.LogDebug("Fetching comments for PR #{PrNumber} in {Owner}/{Repo}", prNumber, owner, repo);

        var comments = new List<PullRequestComment>();

        // Get issue comments (general PR comments)
        var issueCommentsJson = await _httpClient.GetStringAsync(
            $"repos/{owner}/{repo}/issues/{prNumber}/comments", ct);
        using var issueDoc = JsonDocument.Parse(issueCommentsJson);
        foreach (var element in issueDoc.RootElement.EnumerateArray())
        {
            comments.Add(ParseComment(element, isReviewComment: false));
        }

        // Get review comments (inline code review comments)
        var reviewCommentsJson = await _httpClient.GetStringAsync(
            $"repos/{owner}/{repo}/pulls/{prNumber}/comments", ct);
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

        // Get the PR to find the head SHA
        var detail = await GetPullRequestDetailAsync(owner, repo, prNumber, ct);

        // Get combined status
        var statusJson = await _httpClient.GetStringAsync(
            $"repos/{owner}/{repo}/commits/{detail.HeadSha}/status", ct);
        using var statusDoc = JsonDocument.Parse(statusJson);
        var state = statusDoc.RootElement.GetProperty("state").GetString() ?? "unknown";

        // Get check runs
        var checksJson = await _httpClient.GetStringAsync(
            $"repos/{owner}/{repo}/commits/{detail.HeadSha}/check-runs", ct);
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

    public async Task<PullRequestDetail> GetPullRequestDetailAsync(
        string owner, string repo, int prNumber, CancellationToken ct)
    {
        var prJson = await _httpClient.GetStringAsync(
            $"repos/{owner}/{repo}/pulls/{prNumber}", ct);
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
            var json = await _httpClient.GetStringAsync("user", ct);
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

        while (true)
        {
            var json = await _httpClient.GetStringAsync(
                $"user/repos?per_page={perPage}&page={page}&type=all", ct);

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

        var json = await _httpClient.GetStringAsync(
            $"repos/{owner}/{repo}/branches?per_page=100", ct);

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
            // Search open PRs first, then closed (to surface active PRs first)
            foreach (var state in new[] { "open", "closed" })
            {
                var encodedBranch = Uri.EscapeDataString(headBranch);
                var json = await _httpClient.GetStringAsync(
                    $"repos/{owner}/{repo}/pulls?state={state}&head={owner}:{encodedBranch}&per_page=1", ct);

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
