using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.IssueTrackers;

public sealed class LinearTracker : IIssueTracker
{
    private const string IssuesQuery = """
        query AntiphonIssues($project: String, $states: [String!]) {
          issues(filter: {
            project: { name: { eq: $project } }
            state: { name: { in: $states } }
          }) {
            nodes {
              id
              identifier
              title
              description
              priority
              url
              state { name }
              labels { nodes { name } }
              inverseRelations(filter: { type: { eq: "blocks" } }) {
                nodes { issue { id identifier } }
              }
            }
          }
        }
        """;

    private readonly HttpClient _httpClient;

    public LinearTracker(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public TrackerKind Kind => TrackerKind.Linear;

    public Task<IReadOnlyList<TrackedIssue>> FetchCandidatesAsync(
        IssueTrackerConfig config,
        CancellationToken ct) =>
        FetchByStatesAsync(config, config.ActiveStates, ct);

    public async Task<IReadOnlyList<TrackedIssue>> FetchByStatesAsync(
        IssueTrackerConfig config,
        IReadOnlyList<string> states,
        CancellationToken ct)
    {
        var payload = new
        {
            query = IssuesQuery,
            variables = new
            {
                project = config.ProjectKey,
                states = states.Count == 0 ? config.ActiveStates : states
            }
        };

        using var response = await PostGraphQlAsync(config, payload, ct);
        return await ParseIssuesAsync(response, ct);
    }

    public async Task<IReadOnlyList<TrackedIssue>> FetchByIdsAsync(
        IssueTrackerConfig config,
        IReadOnlyList<string> externalIds,
        CancellationToken ct)
    {
        var payload = new
        {
            query = """
                query AntiphonIssuesById($ids: [String!]) {
                  issues(filter: { id: { in: $ids } }) {
                    nodes {
                      id
                      identifier
                      title
                      description
                      priority
                      url
                      state { name }
                      labels { nodes { name } }
                      inverseRelations(filter: { type: { eq: "blocks" } }) {
                        nodes { issue { id identifier } }
                      }
                    }
                  }
                }
                """,
            variables = new { ids = externalIds }
        };

        using var response = await PostGraphQlAsync(config, payload, ct);
        return await ParseIssuesAsync(response, ct);
    }

    private async Task<HttpResponseMessage> PostGraphQlAsync(
        IssueTrackerConfig config,
        object payload,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (ResolveToken(config) is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static async Task<IReadOnlyList<TrackedIssue>> ParseIssuesAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var nodes = doc.RootElement
            .GetProperty("data")
            .GetProperty("issues")
            .GetProperty("nodes");

        return nodes.EnumerateArray().Select(ParseIssue).ToList();
    }

    private static TrackedIssue ParseIssue(JsonElement issue)
    {
        var labels = ReadNestedNames(issue, "labels");
        var blockers = issue.TryGetProperty("inverseRelations", out var inverseRelations)
            && inverseRelations.TryGetProperty("nodes", out var nodes)
                ? nodes.EnumerateArray()
                    .Select(node => node.GetProperty("issue"))
                    .Select(blocker => blocker.GetProperty("id").GetString())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .ToList()
                : [];

        return new TrackedIssue(
            ExternalId: issue.GetProperty("id").GetString() ?? string.Empty,
            ExternalKey: issue.GetProperty("identifier").GetString() ?? string.Empty,
            Title: issue.GetProperty("title").GetString() ?? string.Empty,
            Description: issue.TryGetProperty("description", out var description)
                ? description.GetString() ?? string.Empty
                : string.Empty,
            State: issue.GetProperty("state").GetProperty("name").GetString() ?? string.Empty,
            Priority: issue.TryGetProperty("priority", out var priority) ? priority.GetInt32() : 0,
            Labels: labels,
            BlockedByExternalIds: blockers,
            Url: issue.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
            RawPayloadJson: issue.GetRawText());
    }

    private static IReadOnlyList<string> ReadNestedNames(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var container)
            || !container.TryGetProperty("nodes", out var nodes))
        {
            return [];
        }

        return nodes.EnumerateArray()
            .Select(node => node.GetProperty("name").GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .ToList();
    }

    private static string? ResolveToken(IssueTrackerConfig config) =>
        string.IsNullOrWhiteSpace(config.ApiKeyEnv)
            ? null
            : Environment.GetEnvironmentVariable(config.ApiKeyEnv);
}
