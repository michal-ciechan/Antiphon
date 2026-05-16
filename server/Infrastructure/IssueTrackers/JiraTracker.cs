using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.IssueTrackers;

public sealed class JiraTracker : IIssueTracker
{
    private readonly HttpClient _httpClient;

    public JiraTracker(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public TrackerKind Kind => TrackerKind.Jira;

    public Task<IReadOnlyList<TrackedIssue>> FetchCandidatesAsync(
        IssueTrackerConfig config,
        CancellationToken ct) =>
        FetchByStatesAsync(config, config.ActiveStates, ct);

    public async Task<IReadOnlyList<TrackedIssue>> FetchByStatesAsync(
        IssueTrackerConfig config,
        IReadOnlyList<string> states,
        CancellationToken ct)
    {
        var jql = string.IsNullOrWhiteSpace(config.Jql)
            ? BuildJql(config.ProjectKey, states.Count == 0 ? config.ActiveStates : states)
            : config.Jql;

        return await SearchAsync(config, jql, ct);
    }

    public async Task<IReadOnlyList<TrackedIssue>> FetchByIdsAsync(
        IssueTrackerConfig config,
        IReadOnlyList<string> externalIds,
        CancellationToken ct)
    {
        var ids = externalIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(ParseIssueKey)
            .ToArray();
        if (ids.Length == 0)
            return [];

        return await SearchAsync(config, $"issuekey in ({string.Join(",", ids)})", ct);
    }

    private async Task<IReadOnlyList<TrackedIssue>> SearchAsync(
        IssueTrackerConfig config,
        string jql,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.BaseUrl))
            throw new ValidationException("tracker.base_url", "Jira tracker requires base_url.");

        var baseUri = new Uri(config.BaseUrl.TrimEnd('/') + "/");
        var path = $"rest/api/3/search?jql={Uri.EscapeDataString(jql)}&maxResults=100";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        ApplyAuth(config, request);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement
            .GetProperty("issues")
            .EnumerateArray()
            .Select(issue => ParseIssue(config, issue))
            .ToList();
    }

    private static TrackedIssue ParseIssue(IssueTrackerConfig config, JsonElement issue)
    {
        var fields = issue.GetProperty("fields");
        var labels = fields.TryGetProperty("labels", out var labelsElement)
            ? labelsElement.EnumerateArray()
                .Select(label => label.GetString() ?? string.Empty)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => label.Trim().ToLowerInvariant())
                .ToList()
            : [];

        var key = issue.GetProperty("key").GetString() ?? string.Empty;
        var self = issue.TryGetProperty("self", out var selfElement)
            ? selfElement.GetString() ?? string.Empty
            : string.Empty;
        var url = string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(key)
            ? self
            : $"{config.BaseUrl.TrimEnd('/')}/browse/{key}";

        return new TrackedIssue(
            ExternalId: $"{NormalizeBaseUrl(config.BaseUrl)}|{key}",
            ExternalKey: key,
            Title: fields.GetProperty("summary").GetString() ?? string.Empty,
            Description: ExtractDescription(fields),
            State: fields.GetProperty("status").GetProperty("name").GetString() ?? string.Empty,
            Priority: ExtractPriority(fields),
            Labels: labels,
            BlockedByExternalIds: [],
            Url: url,
            RawPayloadJson: issue.GetRawText());
    }

    private static string ExtractDescription(JsonElement fields)
    {
        if (!fields.TryGetProperty("description", out var description)
            || description.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        return description.ValueKind == JsonValueKind.String
            ? description.GetString() ?? string.Empty
            : description.GetRawText();
    }

    private static int ExtractPriority(JsonElement fields)
    {
        if (!fields.TryGetProperty("priority", out var priority)
            || priority.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return 0;
        }

        if (priority.TryGetProperty("id", out var id)
            && int.TryParse(id.GetString(), out var numeric))
        {
            return Math.Max(0, 6 - numeric);
        }

        if (priority.TryGetProperty("name", out var name))
        {
            return (name.GetString() ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "highest" => 5,
                "high" => 4,
                "medium" => 2,
                "low" => 1,
                _ => 0
            };
        }

        return 0;
    }

    private static string NormalizeBaseUrl(string baseUrl) =>
        baseUrl.Trim().TrimEnd('/').ToLowerInvariant();

    private static string ParseIssueKey(string externalId)
    {
        var normalized = externalId.Trim();
        var separatorIndex = normalized.LastIndexOf('|');
        return separatorIndex >= 0
            ? normalized[(separatorIndex + 1)..]
            : normalized;
    }

    private static string BuildJql(string? projectKey, IReadOnlyList<string> states)
    {
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(projectKey))
            clauses.Add($"project = {projectKey.Trim()}");

        if (states.Count > 0)
        {
            var quotedStates = states
                .Where(state => !string.IsNullOrWhiteSpace(state))
                .Select(state => $"\"{state.Trim().Replace("\"", "\\\"", StringComparison.Ordinal)}\"");
            clauses.Add($"status in ({string.Join(",", quotedStates)})");
        }

        return clauses.Count == 0
            ? "order by updated DESC"
            : $"{string.Join(" AND ", clauses)} order by updated DESC";
    }

    private static void ApplyAuth(IssueTrackerConfig config, HttpRequestMessage request)
    {
        var token = string.IsNullOrWhiteSpace(config.ApiKeyEnv)
            ? null
            : Environment.GetEnvironmentVariable(config.ApiKeyEnv);
        if (string.IsNullOrWhiteSpace(token))
            return;

        if (config.Options.TryGetValue("email", out var email) && !string.IsNullOrWhiteSpace(email))
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
