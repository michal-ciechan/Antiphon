using System.Net;
using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.IssueTrackers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0166 S2: GitHub write + comment-pull request shapes.</summary>
[Category("Integration")]
public class GitHubIssuesTrackerWriteTests
{
    [Test]
    public async Task PostCommentAsync_posts_body_to_issue_comments_path_with_bearer()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = Json("""
                {
                  "id": 99,
                  "body": "hello",
                  "html_url": "https://github.test/acme/app/issues/7#issuecomment-99",
                  "user": { "login": "sync-bot" },
                  "created_at": "2026-08-24T12:00:00Z",
                  "updated_at": "2026-08-24T12:00:00Z",
                  "issue_url": "https://api.github.test/repos/acme/app/issues/7"
                }
                """)
        });
        var tracker = new GitHubIssuesTracker(new HttpClient(handler));
        var config = NewConfig() with { ResolvedToken = "ghs_test_token" };

        var comment = await tracker.PostCommentAsync(config, "acme/app#7", "hello", CancellationToken.None);

        comment.ExternalCommentId.ShouldBe("99");
        comment.IssueExternalId.ShouldBe("acme/app#7");
        comment.Author.ShouldBe("sync-bot");
        comment.Body.ShouldBe("hello");
        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.ToString().ShouldBe("https://github.test/api/v3/repos/acme/app/issues/7/comments");
        request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization.Parameter.ShouldBe("ghs_test_token");
        handler.Bodies.Single().ShouldContain("\"body\":\"hello\"");
    }

    [Test]
    public async Task FetchCommentsSinceAsync_carries_since_sort_and_derives_issue_external_id()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""
                [
                  {
                    "id": 11,
                    "body": "from GH",
                    "html_url": "https://github.test/acme/app/issues/3#issuecomment-11",
                    "user": { "login": "alice" },
                    "created_at": "2026-08-24T10:00:00Z",
                    "updated_at": "2026-08-24T10:00:00Z",
                    "issue_url": "https://api.github.test/repos/acme/app/issues/3"
                  }
                ]
                """)
        });
        var tracker = new GitHubIssuesTracker(new HttpClient(handler));
        var since = new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc);

        var comments = await tracker.FetchCommentsSinceAsync(NewConfig(), since, CancellationToken.None);

        comments.Single().IssueExternalId.ShouldBe("acme/app#3");
        comments.Single().ExternalCommentId.ShouldBe("11");
        comments.Single().Author.ShouldBe("alice");
        var uri = handler.Requests.Single().RequestUri!;
        uri.AbsolutePath.ShouldBe("/api/v3/repos/acme/app/issues/comments");
        var query = Uri.UnescapeDataString(uri.Query);
        query.ShouldContain("since=");
        query.ShouldContain("sort=created");
        query.ShouldContain("direction=asc");
        query.ShouldContain("per_page=100");
    }

    [Test]
    public async Task FetchCommentsSinceAsync_paginates_until_short_page()
    {
        var page = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            page++;
            if (page == 1)
            {
                var items = string.Join(",", Enumerable.Range(1, 100).Select(i => $$"""
                    {
                      "id": {{i}},
                      "body": "c{{i}}",
                      "html_url": "https://github.test/acme/app/issues/1#issuecomment-{{i}}",
                      "user": { "login": "alice" },
                      "created_at": "2026-08-24T10:00:00Z",
                      "updated_at": "2026-08-24T10:00:00Z",
                      "issue_url": "https://api.github.test/repos/acme/app/issues/1"
                    }
                    """));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = Json($"[{items}]") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json("""
                    [
                      {
                        "id": 101,
                        "body": "last",
                        "html_url": "https://github.test/acme/app/issues/1#issuecomment-101",
                        "user": { "login": "alice" },
                        "created_at": "2026-08-24T11:00:00Z",
                        "updated_at": "2026-08-24T11:00:00Z",
                        "issue_url": "https://api.github.test/repos/acme/app/issues/1"
                      }
                    ]
                    """)
            };
        });
        var tracker = new GitHubIssuesTracker(new HttpClient(handler));

        var comments = await tracker.FetchCommentsSinceAsync(NewConfig(), since: null, CancellationToken.None);

        comments.Count.ShouldBe(101);
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[0].RequestUri!.Query.ShouldContain("page=1");
        handler.Requests[1].RequestUri!.Query.ShouldContain("page=2");
    }

    [Test]
    public async Task AddLabelsAsync_and_RemoveLabelAsync_hit_labels_sub_resource()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("[]")
        });
        var tracker = new GitHubIssuesTracker(new HttpClient(handler));
        var config = NewConfig();

        await tracker.AddLabelsAsync(config, "acme/app#5", ["status:in-progress"], CancellationToken.None);
        await tracker.RemoveLabelAsync(config, "acme/app#5", "status:backlog", CancellationToken.None);

        handler.Requests[0].Method.ShouldBe(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString()
            .ShouldBe("https://github.test/api/v3/repos/acme/app/issues/5/labels");
        handler.Requests[1].Method.ShouldBe(HttpMethod.Delete);
        handler.Requests[1].RequestUri!.ToString()
            .ShouldBe("https://github.test/api/v3/repos/acme/app/issues/5/labels/status%3Abacklog");
    }

    [Test]
    public async Task SetStateAsync_patches_state_and_state_reason()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""{"number":9,"title":"x","state":"closed","labels":[]}""")
        });
        var tracker = new GitHubIssuesTracker(new HttpClient(handler));

        await tracker.SetStateAsync(NewConfig(), "acme/app#9", "closed", "completed", CancellationToken.None);

        handler.Bodies.Single().ShouldContain("\"state\":\"closed\"");
        handler.Bodies.Single().ShouldContain("\"state_reason\":\"completed\"");
        handler.Requests.Single().Method.ShouldBe(HttpMethod.Patch);
    }

    [Test]
    public async Task CreateIssueAsync_posts_title_body_and_labels()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = Json("""
                {
                  "number": 12,
                  "title": "New card",
                  "body": "desc",
                  "state": "open",
                  "html_url": "https://github.test/acme/app/issues/12",
                  "labels": [{ "name": "status:backlog" }]
                }
                """)
        });
        var tracker = new GitHubIssuesTracker(new HttpClient(handler));

        var issue = await tracker.CreateIssueAsync(
            NewConfig(),
            "New card",
            "desc",
            ["status:backlog"],
            CancellationToken.None);

        issue.ExternalId.ShouldBe("acme/app#12");
        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.ToString().ShouldBe("https://github.test/api/v3/repos/acme/app/issues");
        handler.Bodies.Single().ShouldContain("\"title\":\"New card\"");
        handler.Bodies.Single().ShouldContain("\"body\":\"desc\"");
        handler.Bodies.Single().ShouldContain("status:backlog");
    }

    [Test]
    public async Task ReplaceLabelsAsync_patches_issue_labels_array()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""{"number":4,"title":"x","state":"open","labels":[]}""")
        });
        var tracker = new GitHubIssuesTracker(new HttpClient(handler));

        await tracker.ReplaceLabelsAsync(NewConfig(), "acme/app#4", ["backend", "status:review"], CancellationToken.None);

        handler.Requests.Single().Method.ShouldBe(HttpMethod.Patch);
        handler.Bodies.Single().ShouldContain("\"labels\"");
        handler.Bodies.Single().ShouldContain("backend");
        handler.Bodies.Single().ShouldContain("status:review");
    }

    private static IssueTrackerConfig NewConfig() =>
        new(
            TrackerKind.GitHubIssues,
            BaseUrl: "https://github.test/api/v3",
            ProjectKey: null,
            Repository: "acme/app",
            ActiveStates: ["open"],
            ApiKeyEnv: null,
            Jql: null,
            Options: new Dictionary<string, string>());

    private static StringContent Json(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return handler(request);
        }
    }
}
