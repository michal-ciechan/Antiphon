using System.Net;
using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.IssueTrackers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class IssueTrackerAdapterTests
{
    [Test]
    public async Task TrackerCache_dedupes_same_id_lookup_within_tick()
    {
        var tracker = new FakeIssueTracker();
        var cache = new TrackerCache();
        var config = NewConfig(TrackerKind.GitHubIssues);

        var first = await cache.FetchByIdsAsync(tracker, config, ["A", "B"], CancellationToken.None);
        var second = await cache.FetchByIdsAsync(tracker, config, ["B", "A"], CancellationToken.None);
        var differentAuth = await cache.FetchByIdsAsync(
            tracker,
            config with { ApiKeyEnv = "ANTIPHON_OTHER_TOKEN" },
            ["B", "A"],
            CancellationToken.None);

        tracker.FetchByIdsCalls.ShouldBe(2);
        second.ShouldBeSameAs(first);
        differentAuth.ShouldBe(first);
    }

    [Test]
    public async Task GitHubIssuesTracker_normalises_priority_from_label_convention_and_excludes_pull_requests()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""
                [
                  {
                    "number": 42,
                    "title": "Fix sync",
                    "body": "Details",
                    "state": "open",
                    "html_url": "https://github.test/acme/app/issues/42",
                    "labels": [
                      { "name": "Priority: High" },
                      { "name": "Backend" }
                    ]
                  },
                  {
                    "number": 43,
                    "title": "PR should not become a card",
                    "state": "open",
                    "html_url": "https://github.test/acme/app/pull/43",
                    "pull_request": {},
                    "labels": []
                  }
                ]
                """)
        });
        var tracker = new GitHubIssuesTracker(new HttpClient(handler));
        var config = NewConfig(TrackerKind.GitHubIssues) with
        {
            BaseUrl = "https://github.test/api/v3",
            Repository = "acme/app",
            ActiveStates = ["open"]
        };

        var issues = await tracker.FetchCandidatesAsync(config, CancellationToken.None);

        issues.Count.ShouldBe(1);
        issues[0].ExternalId.ShouldBe("acme/app#42");
        issues[0].ExternalKey.ShouldBe("#42");
        issues[0].Title.ShouldBe("Fix sync");
        issues[0].Priority.ShouldBe(4);
        issues[0].Labels.ShouldBe(["priority: high", "backend"]);
        handler.Requests.Single().RequestUri!.ToString()
            .ShouldBe("https://github.test/api/v3/repos/acme/app/issues?state=open&per_page=100");
    }

    [Test]
    public async Task LinearTracker_blockers_derived_from_inverse_blocks()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""
                {
                  "data": {
                    "issues": {
                      "nodes": [
                        {
                          "id": "lin-1",
                          "identifier": "ANT-123",
                          "title": "Linear card",
                          "description": "Do the work",
                          "priority": 3,
                          "url": "https://linear.test/ANT-123",
                          "state": { "name": "Todo" },
                          "labels": { "nodes": [ { "name": "api" } ] },
                          "inverseRelations": {
                            "nodes": [
                              { "issue": { "id": "lin-blocker", "identifier": "ANT-100" } }
                            ]
                          }
                        }
                      ]
                    }
                  }
                }
                """)
        });
        var tracker = new LinearTracker(new HttpClient(handler));
        var config = NewConfig(TrackerKind.Linear) with
        {
            BaseUrl = "https://linear.test/graphql",
            ProjectKey = "Antiphon",
            ActiveStates = ["Todo"]
        };

        var issues = await tracker.FetchCandidatesAsync(config, CancellationToken.None);

        issues.Single().ExternalId.ShouldBe("lin-1");
        issues.Single().ExternalKey.ShouldBe("ANT-123");
        issues.Single().BlockedByExternalIds.ShouldBe(["lin-blocker"]);
    }

    [Test]
    public async Task JiraTracker_jql_filters_to_active_states()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""
                {
                  "issues": [
                    {
                      "id": "10001",
                      "key": "ANT-7",
                      "self": "https://jira.test/rest/api/3/issue/10001",
                      "fields": {
                        "summary": "Jira card",
                        "description": "Jira body",
                        "status": { "name": "In Progress" },
                        "priority": { "name": "High", "id": "2" },
                        "labels": [ "Backend", "E10" ]
                      }
                    }
                  ]
                }
                """)
        });
        var tracker = new JiraTracker(new HttpClient(handler));
        var config = NewConfig(TrackerKind.Jira) with
        {
            BaseUrl = "https://jira.test",
            ProjectKey = "ANT",
            ActiveStates = ["In Progress"]
        };

        var issues = await tracker.FetchCandidatesAsync(config, CancellationToken.None);

        issues.Single().ExternalKey.ShouldBe("ANT-7");
        issues.Single().ExternalId.ShouldBe("https://jira.test|ANT-7");
        issues.Single().Priority.ShouldBe(4);
        issues.Single().Labels.ShouldBe(["backend", "e10"]);
        var query = Uri.UnescapeDataString(handler.Requests.Single().RequestUri!.Query);
        query.ShouldContain("project = ANT");
        query.ShouldContain("status in (\"In Progress\")");
    }

    private static IssueTrackerConfig NewConfig(TrackerKind kind) =>
        new(
            kind,
            BaseUrl: "https://tracker.test",
            ProjectKey: null,
            Repository: null,
            ActiveStates: [],
            ApiKeyEnv: null,
            Jql: null,
            Options: new Dictionary<string, string>());

    private static StringContent Json(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class FakeIssueTracker : IIssueTracker
    {
        private readonly IReadOnlyList<TrackedIssue> _issues =
        [
            new(
                "A",
                "A",
                "Issue A",
                string.Empty,
                "open",
                0,
                [],
                [],
                string.Empty,
                "{}")
        ];

        public TrackerKind Kind => TrackerKind.GitHubIssues;

        public int FetchByIdsCalls { get; private set; }

        public Task<IReadOnlyList<TrackedIssue>> FetchCandidatesAsync(
            IssueTrackerConfig config,
            CancellationToken ct) =>
            Task.FromResult(_issues);

        public Task<IReadOnlyList<TrackedIssue>> FetchByStatesAsync(
            IssueTrackerConfig config,
            IReadOnlyList<string> states,
            CancellationToken ct) =>
            Task.FromResult(_issues);

        public Task<IReadOnlyList<TrackedIssue>> FetchByIdsAsync(
            IssueTrackerConfig config,
            IReadOnlyList<string> externalIds,
            CancellationToken ct)
        {
            FetchByIdsCalls++;
            return Task.FromResult(_issues);
        }
    }
}
