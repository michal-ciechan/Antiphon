using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.IssueTrackers;

public sealed class GitHubIssuesTracker : IBidirectionalIssueTracker
{
    private readonly HttpClient _httpClient;

    public GitHubIssuesTracker(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public TrackerKind Kind => TrackerKind.GitHubIssues;

    public Task<IReadOnlyList<TrackedIssue>> FetchCandidatesAsync(
        IssueTrackerConfig config,
        CancellationToken ct) =>
        FetchByStatesAsync(config, config.ActiveStates, ct);

    public async Task<IReadOnlyList<TrackedIssue>> FetchByStatesAsync(
        IssueTrackerConfig config,
        IReadOnlyList<string> states,
        CancellationToken ct)
    {
        var repository = RequireRepository(config);
        var issues = new List<TrackedIssue>();
        foreach (var state in states.Count == 0 ? ["open"] : states)
        {
            var path = $"repos/{repository}/issues?state={Uri.EscapeDataString(state)}&per_page=100";
            using var response = await SendAsync(config, HttpMethod.Get, path, ct);
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            foreach (var issue in doc.RootElement.EnumerateArray())
            {
                if (issue.TryGetProperty("pull_request", out _))
                    continue;

                issues.Add(ParseIssue(repository, issue));
            }
        }

        return issues;
    }

    public async Task<IReadOnlyList<TrackedIssue>> FetchByIdsAsync(
        IssueTrackerConfig config,
        IReadOnlyList<string> externalIds,
        CancellationToken ct)
    {
        var repository = RequireRepository(config);
        var issues = new List<TrackedIssue>();
        foreach (var externalId in externalIds)
        {
            var number = ParseIssueNumber(externalId);
            using var response = await SendAsync(config, HttpMethod.Get, $"repos/{repository}/issues/{number}", ct);
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("pull_request", out _))
                issues.Add(ParseIssue(repository, doc.RootElement));
        }

        return issues;
    }

    public async Task<IReadOnlyList<TrackedIssueComment>> FetchCommentsSinceAsync(
        IssueTrackerConfig config,
        DateTime? since,
        CancellationToken ct)
    {
        var repository = RequireRepository(config);
        var comments = new List<TrackedIssueComment>();
        var page = 1;
        while (true)
        {
            var path = new StringBuilder($"repos/{repository}/issues/comments?per_page=100&sort=created&direction=asc&page={page}");
            if (since is DateTime sinceUtc)
            {
                path.Append("&since=")
                    .Append(Uri.EscapeDataString(sinceUtc.ToUniversalTime().ToString("o")));
            }

            using var response = await SendAsync(config, HttpMethod.Get, path.ToString(), ct);
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var pageCount = 0;
            foreach (var comment in doc.RootElement.EnumerateArray())
            {
                pageCount++;
                if (!TryParseComment(repository, comment, out var parsed) || parsed is null)
                    continue;

                comments.Add(parsed);
            }

            if (pageCount < 100)
                break;

            page++;
        }

        return comments;
    }

    public async Task<TrackedIssueComment> PostCommentAsync(
        IssueTrackerConfig config,
        string externalId,
        string body,
        CancellationToken ct)
    {
        var repository = RequireRepository(config);
        var number = ParseIssueNumber(externalId);
        var payload = JsonSerializer.Serialize(new { body });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await SendAsync(
            config,
            HttpMethod.Post,
            $"repos/{repository}/issues/{number}/comments",
            ct,
            content);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!TryParseComment(repository, doc.RootElement, out var parsed) || parsed is null)
            throw new InvalidOperationException("GitHub comment POST returned an unparseable body.");

        return parsed with { IssueExternalId = NormalizeExternalId(repository, number) };
    }

    public async Task AddLabelsAsync(
        IssueTrackerConfig config,
        string externalId,
        IReadOnlyList<string> labels,
        CancellationToken ct)
    {
        if (labels.Count == 0)
            return;

        var repository = RequireRepository(config);
        var number = ParseIssueNumber(externalId);
        var payload = JsonSerializer.Serialize(new { labels });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await SendAsync(
            config,
            HttpMethod.Post,
            $"repos/{repository}/issues/{number}/labels",
            ct,
            content);
        // Discard body — caller already knows the desired set.
        _ = response;
    }

    public async Task RemoveLabelAsync(
        IssueTrackerConfig config,
        string externalId,
        string label,
        CancellationToken ct)
    {
        var repository = RequireRepository(config);
        var number = ParseIssueNumber(externalId);
        using var response = await SendAsync(
            config,
            HttpMethod.Delete,
            $"repos/{repository}/issues/{number}/labels/{Uri.EscapeDataString(label)}",
            ct);
        _ = response;
    }

    public async Task ReplaceLabelsAsync(
        IssueTrackerConfig config,
        string externalId,
        IReadOnlyList<string> labels,
        CancellationToken ct)
    {
        var repository = RequireRepository(config);
        var number = ParseIssueNumber(externalId);
        var payload = JsonSerializer.Serialize(new { labels });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await SendAsync(
            config,
            HttpMethod.Patch,
            $"repos/{repository}/issues/{number}",
            ct,
            content);
        _ = response;
    }

    public async Task SetStateAsync(
        IssueTrackerConfig config,
        string externalId,
        string state,
        string? stateReason,
        CancellationToken ct)
    {
        var repository = RequireRepository(config);
        var number = ParseIssueNumber(externalId);
        object payload = string.IsNullOrWhiteSpace(stateReason)
            ? new { state }
            : new { state, state_reason = stateReason };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await SendAsync(
            config,
            HttpMethod.Patch,
            $"repos/{repository}/issues/{number}",
            ct,
            content);
        _ = response;
    }

    public async Task<TrackedIssue> CreateIssueAsync(
        IssueTrackerConfig config,
        string title,
        string body,
        IReadOnlyList<string> labels,
        CancellationToken ct)
    {
        var repository = RequireRepository(config);
        var payload = JsonSerializer.Serialize(new { title, body, labels });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await SendAsync(
            config,
            HttpMethod.Post,
            $"repos/{repository}/issues",
            ct,
            content);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseIssue(repository, doc.RootElement);
    }

    private async Task<HttpResponseMessage> SendAsync(
        IssueTrackerConfig config,
        HttpMethod method,
        string path,
        CancellationToken ct,
        HttpContent? content = null)
    {
        var baseUri = new Uri(config.BaseUrl.TrimEnd('/') + "/");
        using var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Antiphon", "1.0"));
        if (ResolveToken(config) is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (content is not null)
            request.Content = content;

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static TrackedIssue ParseIssue(string repository, JsonElement issue)
    {
        var labels = issue.TryGetProperty("labels", out var labelsElement)
            ? labelsElement.EnumerateArray()
                .Select(label => label.GetProperty("name").GetString() ?? string.Empty)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => label.Trim().ToLowerInvariant())
                .ToList()
            : [];

        var number = issue.GetProperty("number").GetInt32();
        return new TrackedIssue(
            ExternalId: NormalizeExternalId(repository, number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ExternalKey: $"#{number}",
            Title: issue.GetProperty("title").GetString() ?? string.Empty,
            Description: issue.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty,
            State: issue.GetProperty("state").GetString() ?? "open",
            Priority: ParsePriority(labels),
            Labels: labels,
            BlockedByExternalIds: [],
            Url: issue.TryGetProperty("html_url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
            RawPayloadJson: issue.GetRawText());
    }

    private static bool TryParseComment(
        string repository,
        JsonElement comment,
        out TrackedIssueComment? parsed)
    {
        parsed = null;
        if (!comment.TryGetProperty("id", out var idElement))
            return false;

        var commentId = idElement.ValueKind == JsonValueKind.Number
            ? idElement.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)
            : idElement.GetString();
        if (string.IsNullOrWhiteSpace(commentId))
            return false;

        var issueExternalId = string.Empty;
        if (comment.TryGetProperty("issue_url", out var issueUrl)
            && issueUrl.GetString() is { Length: > 0 } issueUrlValue
            && TryDeriveIssueExternalId(repository, issueUrlValue, out var derived))
        {
            issueExternalId = derived;
        }

        var author = comment.TryGetProperty("user", out var user)
            && user.TryGetProperty("login", out var login)
                ? login.GetString() ?? string.Empty
                : string.Empty;

        parsed = new TrackedIssueComment(
            ExternalCommentId: commentId,
            IssueExternalId: issueExternalId,
            Author: author,
            Body: comment.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty,
            Url: comment.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() ?? string.Empty : string.Empty,
            CreatedAt: ParseGitHubTimestamp(comment, "created_at"),
            UpdatedAt: ParseGitHubTimestamp(comment, "updated_at"));
        return true;
    }

    private static bool TryDeriveIssueExternalId(string repository, string issueUrl, out string externalId)
    {
        externalId = string.Empty;
        // https://api.github.com/repos/owner/repo/issues/42
        var marker = "/issues/";
        var idx = issueUrl.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return false;

        var number = issueUrl[(idx + marker.Length)..].Trim('/').Split('/')[0];
        if (string.IsNullOrWhiteSpace(number))
            return false;

        externalId = NormalizeExternalId(repository, number);
        return true;
    }

    private static DateTime ParseGitHubTimestamp(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value)
            && value.GetString() is { Length: > 0 } raw
            && DateTimeOffset.TryParse(raw, out var dto))
        {
            return dto.UtcDateTime;
        }

        return DateTime.UtcNow;
    }

    private static int ParsePriority(IReadOnlyList<string> labels)
    {
        foreach (var label in labels)
        {
            var normalized = label.Trim().ToLowerInvariant();
            if (normalized.StartsWith("priority:", StringComparison.Ordinal)
                || normalized.StartsWith("priority/", StringComparison.Ordinal))
            {
                var value = normalized[(normalized.IndexOfAny([':', '/']) + 1)..].Trim();
                if (int.TryParse(value, out var numeric))
                    return numeric;

                return value switch
                {
                    "critical" => 5,
                    "high" => 4,
                    "medium" => 2,
                    "low" => 1,
                    _ => 0
                };
            }

            if (normalized.Length == 2
                && normalized[0] == 'p'
                && int.TryParse(normalized[1..], out var pValue))
            {
                return Math.Max(0, 5 - pValue);
            }
        }

        return 0;
    }

    private static string RequireRepository(IssueTrackerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Repository))
            throw new ValidationException("tracker.repository", "GitHub Issues tracker requires repository in 'owner/repo' format.");

        return config.Repository.Trim();
    }

    private static string NormalizeRepository(string repository) =>
        repository.Trim().Trim('/').ToLowerInvariant();

    private static string NormalizeExternalId(string repository, string number) =>
        $"{NormalizeRepository(repository)}#{number.Trim().TrimStart('#')}";

    private static string ParseIssueNumber(string externalId)
    {
        var normalized = externalId.Trim();
        var hashIndex = normalized.LastIndexOf('#');
        return hashIndex >= 0
            ? normalized[(hashIndex + 1)..]
            : normalized.TrimStart('#');
    }

    /// <summary>
    /// Prefer <see cref="IssueTrackerConfig.ResolvedToken"/> (CARD-0166); fall back to env var
    /// for callers that have not yet run <c>TrackerTokenResolver</c>.
    /// </summary>
    private static string? ResolveToken(IssueTrackerConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.ResolvedToken))
            return config.ResolvedToken;

        return string.IsNullOrWhiteSpace(config.ApiKeyEnv)
            ? null
            : Environment.GetEnvironmentVariable(config.ApiKeyEnv);
    }
}
