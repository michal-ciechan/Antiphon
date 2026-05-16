using System.Net.Http.Headers;
using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.IssueTrackers;

public sealed class GitHubIssuesTracker : IIssueTracker
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

    private async Task<HttpResponseMessage> SendAsync(
        IssueTrackerConfig config,
        HttpMethod method,
        string path,
        CancellationToken ct)
    {
        var baseUri = new Uri(config.BaseUrl.TrimEnd('/') + "/");
        using var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Antiphon", "1.0"));
        if (ResolveToken(config) is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

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
            ExternalId: $"{NormalizeRepository(repository)}#{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
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

    private static string ParseIssueNumber(string externalId)
    {
        var normalized = externalId.Trim();
        var hashIndex = normalized.LastIndexOf('#');
        return hashIndex >= 0
            ? normalized[(hashIndex + 1)..]
            : normalized.TrimStart('#');
    }

    private static string? ResolveToken(IssueTrackerConfig config) =>
        string.IsNullOrWhiteSpace(config.ApiKeyEnv)
            ? null
            : Environment.GetEnvironmentVariable(config.ApiKeyEnv);
}
