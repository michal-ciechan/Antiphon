using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Review;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>GitHub thread resolution over GraphQL (design 8.2) against a fake handler: no network.</summary>
[Category("Unit")]
public class GitHubThreadResolutionTests
{
    private static readonly CodeHostTarget Target = new("ghe", ReviewHost.GitHub, "acme/widgets", 42);

    [Test]
    public async Task A_posted_review_records_each_thread_id_and_resolving_it_is_one_mutation()
    {
        var handler = new GraphQlHandler(request => (request.Method.Method, request.RequestUri!.PathAndQuery) switch
        {
            ("POST", "/api/v3/repos/acme/widgets/pulls/42/reviews") => Json("""{ "id": 900, "html_url": "https://ghe.example/acme/widgets/pull/42#review-900" }"""),
            ("GET", "/api/v3/repos/acme/widgets/pulls/42/reviews/900/comments?per_page=100") =>
                Json("""[ { "id": 5001, "path": "src/Lease.cs", "line": 13, "html_url": "https://ghe.example/acme/widgets/pull/42#discussion_r5001" } ]"""),
            var other => throw new InvalidOperationException($"Unexpected request {other}"),
        }, graphQl => graphQl.Query.Contains("reviewThreads")
            ? Threads(("PRRT_other", 4000), ("PRRT_lease", 5001))
            : Json("""{ "data": { "resolveReviewThread": { "thread": { "id": "PRRT_lease", "isResolved": true } } } }"""));
        var host = Host(handler);
        host.Capabilities.CanResolveThreads.ShouldBeTrue();

        var posted = await host.PostReviewAsync(Target, new CodeHostReview(new string('b', 40), "summary",
            [new CodeHostReviewComment("src/Lease.cs", 13, "F1")]), CancellationToken.None);
        var comment = posted.Comments.ShouldHaveSingleItem();
        (comment.CommentId, comment.ThreadId).ShouldBe(("5001", "PRRT_lease"));

        await host.ResolveThreadAsync(Target, comment.ThreadId!, CancellationToken.None);

        var calls = handler.GraphQl;
        calls.Count.ShouldBe(2, "one thread listing at publish, then the mutation alone");
        calls.ShouldAllBe(c => c.Uri == "https://ghe.example/api/graphql" && c.Authorization == "Bearer tok-ghe");
        var listing = calls[0];
        listing.Variables.GetProperty("owner").GetString().ShouldBe("acme");
        listing.Variables.GetProperty("name").GetString().ShouldBe("widgets");
        listing.Variables.GetProperty("number").GetInt32().ShouldBe(42);
        calls[1].Query.ShouldContain("resolveReviewThread");
        calls[1].Variables.GetProperty("threadId").GetString().ShouldBe("PRRT_lease");
    }

    [Test]
    public async Task A_comment_id_is_mapped_to_its_thread_across_pages_and_graphql_errors_map_to_failure_kinds()
    {
        var pages = 0;
        var handler = new GraphQlHandler(_ => throw new InvalidOperationException("no REST call expected"), graphQl =>
        {
            if (graphQl.Query.Contains("resolveReviewThread"))
            {
                return graphQl.Variables.GetProperty("threadId").GetString() == "PRRT_two"
                    ? Json("""{ "data": { "resolveReviewThread": { "thread": { "id": "PRRT_two", "isResolved": true } } } }""")
                    : Json("""{ "data": { "resolveReviewThread": null }, "errors": [ { "type": "FORBIDDEN", "message": "Resource not accessible by personal access token" } ] }""");
            }

            pages++;
            return graphQl.Variables.GetProperty("after").ValueKind == JsonValueKind.Null
                ? Threads(next: "cursor-1", ("PRRT_one", 7001))
                : Threads(("PRRT_two", 7002));
        });
        var host = Host(handler);

        await host.ResolveThreadAsync(Target, "7002", CancellationToken.None);
        pages.ShouldBe(2, "the second page holds the thread");
        handler.GraphQl[1].Variables.GetProperty("after").GetString().ShouldBe("cursor-1");
        handler.GraphQl.Last().Variables.GetProperty("threadId").GetString().ShouldBe("PRRT_two");

        var refused = await Should.ThrowAsync<CodeHostException>(() => host.ResolveThreadAsync(Target, "PRRT_denied", CancellationToken.None));
        refused.Kind.ShouldBe(CodeHostFailureKind.Rejected);
        refused.Message.ShouldContain("Resource not accessible");
        refused.Message.ShouldNotContain("tok-ghe");

        (await Should.ThrowAsync<CodeHostException>(() => host.ResolveThreadAsync(Target, "9999", CancellationToken.None)))
            .Kind.ShouldBe(CodeHostFailureKind.NotFound);
    }

    [Test]
    [Arguments(null, "https://api.github.com/graphql")]
    [Arguments("https://ghe.example.com/api/v3/", "https://ghe.example.com/api/graphql")]
    [Arguments("https://proxy.example/github", "https://proxy.example/github/graphql")]
    public async Task The_graphql_endpoint_follows_the_rest_root(string? baseUrl, string expected)
    {
        GitHubCodeHost.GraphQlEndpoint(new ReviewCodeHost { BaseUrl = baseUrl }).ToString().ShouldBe(expected);
        await Task.CompletedTask;
    }

    private static GitHubCodeHost Host(HttpMessageHandler handler)
    {
        var settings = new ReviewCodeHostSettings();
        settings.Hosts["ghe"] = new ReviewCodeHost { Kind = ReviewHost.GitHub, BaseUrl = "https://ghe.example/api/v3/", Token = "tok-ghe" };
        return new GitHubCodeHost(new HttpClient(handler), Options.Create(settings), new CodeHostResponseCache());
    }

    private static HttpResponseMessage Threads(params (string Id, long CommentId)[] threads) => Threads(null, threads);

    private static HttpResponseMessage Threads(string? next, params (string Id, long CommentId)[] threads)
    {
        var nodes = string.Join(",", threads.Select(t =>
            $$"""{ "id": "{{t.Id}}", "isResolved": false, "comments": { "nodes": [ { "databaseId": {{t.CommentId}} } ] } }"""));
        var pageInfo = next is null ? """{ "hasNextPage": false, "endCursor": null }""" : $$"""{ "hasNextPage": true, "endCursor": "{{next}}" }""";
        return Json($$"""{ "data": { "repository": { "pullRequest": { "reviewThreads": { "pageInfo": {{pageInfo}}, "nodes": [ {{nodes}} ] } } } } }""");
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record GraphQlCall(string Uri, string? Authorization, string Query, JsonElement Variables);

    /// <summary>REST requests go to <c>rest</c>; POSTs to <c>/api/graphql</c> are recorded with their query and variables.</summary>
    private sealed class GraphQlHandler(Func<HttpRequestMessage, HttpResponseMessage> rest, Func<GraphQlCall, HttpResponseMessage> graphQl)
        : HttpMessageHandler
    {
        public List<GraphQlCall> GraphQl { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath != "/api/graphql")
                return rest(request);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var call = new GraphQlCall(request.RequestUri.ToString(), request.Headers.Authorization?.ToString(),
                body.RootElement.GetProperty("query").GetString()!, body.RootElement.GetProperty("variables").Clone());
            GraphQl.Add(call);
            return graphQl(call);
        }
    }
}
