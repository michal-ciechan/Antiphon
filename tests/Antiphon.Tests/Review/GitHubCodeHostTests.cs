using System.Net;
using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Review;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>GitHub REST request and response mapping (design 8.3) over a fake handler: no network.</summary>
[Category("Unit")]
public class GitHubCodeHostTests
{
    private static readonly CodeHostTarget Target = new("ghe", ReviewHost.GitHub, "acme/widgets", 42);

    private const string PullRequest = """
        { "number": 42, "state": "closed", "merged": true, "draft": false,
          "title": "Release the lock after the save", "body": "Fixes the stuck lease.",
          "html_url": "https://ghe.example/acme/widgets/pull/42",
          "user": { "login": "dana" },
          "base": { "ref": "main", "sha": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
          "head": { "ref": "fix/lock", "sha": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } }
        """;

    [Test]
    public async Task Pull_request_and_paged_files_map_from_rest_with_the_token_and_enterprise_base_url()
    {
        var firstPage = "[" + string.Join(",", Enumerable.Range(0, 100).Select(i =>
            $$"""{ "filename": "src/f{{i}}.cs", "status": "modified", "additions": 1, "deletions": 0 }""")) + "]";
        const string secondPage = """
            [ { "filename": "src/New.cs", "status": "renamed", "previous_filename": "src/Old.cs", "additions": 3, "deletions": 2 },
              { "filename": "docs/gone.md", "status": "removed", "additions": 0, "deletions": 9 } ]
            """;
        var handler = new FakeHandler(r => r.RequestUri!.PathAndQuery switch
        {
            "/api/v3/repos/acme/widgets/pulls/42" => Json(HttpStatusCode.OK, PullRequest),
            "/api/v3/repos/acme/widgets/pulls/42/files?per_page=100&page=1" => Json(HttpStatusCode.OK, firstPage),
            "/api/v3/repos/acme/widgets/pulls/42/files?per_page=100&page=2" => Json(HttpStatusCode.OK, secondPage),
            var other => throw new InvalidOperationException($"Unexpected request {other}"),
        });
        var host = Host(handler);

        var pr = await host.GetPullRequestAsync(Target, CancellationToken.None);
        var files = await host.ListChangedFilesAsync(Target, new CodeHostRange(pr.BaseSha, pr.HeadSha), CancellationToken.None);

        var first = handler.Requests[0];
        first.Uri.ShouldBe("https://ghe.example/api/v3/repos/acme/widgets/pulls/42");
        first.Authorization.ShouldBe("Bearer tok-ghe");
        first.Accept.ShouldBe(GitHubCodeHost.JsonMediaType);
        first.ApiVersion.ShouldBe(GitHubCodeHost.ApiVersion);
        handler.Requests.Count.ShouldBe(3, "one PR read and two file pages; the short second page ends the listing");

        pr.Number.ShouldBe(42);
        pr.Title.ShouldBe("Release the lock after the save");
        pr.Description.ShouldBe("Fixes the stuck lease.");
        pr.AuthorLogin.ShouldBe("dana");
        pr.BaseRef.ShouldBe("main");
        pr.HeadRef.ShouldBe("fix/lock");
        pr.BaseSha.ShouldBe(new string('a', 40));
        pr.HeadSha.ShouldBe(new string('b', 40));
        pr.Url.ShouldBe("https://ghe.example/acme/widgets/pull/42");
        pr.State.ShouldBe(CodeHostPullRequestState.Merged);

        files.Count.ShouldBe(102);
        var renamed = files.Single(f => f.Path == "src/New.cs");
        renamed.Status.ShouldBe(CodeHostFileStatus.Renamed);
        renamed.PreviousPath.ShouldBe("src/Old.cs");
        (renamed.Additions, renamed.Deletions).ShouldBe((3, 2));
        files.Single(f => f.Path == "docs/gone.md").Status.ShouldBe(CodeHostFileStatus.Deleted);
    }

    [Test]
    public async Task A_304_reuses_the_cached_body_a_recheck_diffs_by_compare_and_errors_map_to_failure_kinds()
    {
        var calls = 0;
        var handler = new FakeHandler(r =>
        {
            var path = r.RequestUri!.PathAndQuery;
            if (path == "/api/v3/repos/acme/widgets/pulls/42")
            {
                calls++;
                if (r.Headers.TryGetValues("If-None-Match", out var tags) && tags.Single() == "\"pr-v1\"")
                    return new HttpResponseMessage(HttpStatusCode.NotModified);
                var ok = Json(HttpStatusCode.OK, PullRequest);
                ok.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"pr-v1\"");
                return ok;
            }

            return path switch
            {
                "/api/v3/repos/acme/widgets/compare/cccc...dddd" => new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent("diff --git a/x b/x\n", Encoding.UTF8, "text/plain") },
                "/api/v3/repos/acme/widgets/pulls/404" => Json(HttpStatusCode.NotFound, """{"message":"Not Found"}"""),
                "/api/v3/repos/acme/widgets/pulls/401" => Json(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}"""),
                "/api/v3/repos/acme/widgets/pulls/403" => Limited(),
                var other => throw new InvalidOperationException($"Unexpected request {other}"),
            };
        });
        var host = Host(handler);

        var fresh = await host.GetPullRequestAsync(Target, CancellationToken.None);
        var revalidated = await host.GetPullRequestAsync(Target, CancellationToken.None);
        calls.ShouldBe(2);
        revalidated.ShouldBe(fresh, "the 304 answers from the cached body");

        var diff = await host.GetDiffAsync(Target, new CodeHostRange("aaaa", "dddd", SinceSha: "cccc"), CancellationToken.None);
        diff.ShouldStartWith("diff --git");
        handler.Requests.Last().Accept.ShouldBe(GitHubCodeHost.DiffMediaType);

        (await Should.ThrowAsync<CodeHostException>(() => host.GetPullRequestAsync(Target with { PrNumber = 404 }, CancellationToken.None)))
            .Kind.ShouldBe(CodeHostFailureKind.NotFound);
        var rejected = await Should.ThrowAsync<CodeHostException>(() => host.GetPullRequestAsync(Target with { PrNumber = 401 }, CancellationToken.None));
        rejected.Kind.ShouldBe(CodeHostFailureKind.Rejected);
        rejected.Message.ShouldContain("Bad credentials");
        rejected.Message.ShouldNotContain("tok-ghe");
        (await Should.ThrowAsync<CodeHostException>(() => host.GetPullRequestAsync(Target with { PrNumber = 403 }, CancellationToken.None)))
            .Kind.ShouldBe(CodeHostFailureKind.RateLimited);
    }

    private static GitHubCodeHost Host(FakeHandler handler)
    {
        var settings = new ReviewCodeHostSettings();
        settings.Hosts["ghe"] = new ReviewCodeHost { Kind = ReviewHost.GitHub, BaseUrl = "https://ghe.example/api/v3/", Token = "tok-ghe" };
        return new GitHubCodeHost(new HttpClient(handler), Options.Create(settings), new CodeHostResponseCache());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Limited()
    {
        var response = Json(HttpStatusCode.Forbidden, """{"message":"API rate limit exceeded"}""");
        response.Headers.Add("x-ratelimit-remaining", "0");
        return response;
    }

    private sealed record SentRequest(string Uri, string? Authorization, string? Accept, string? ApiVersion);

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.Accept.SingleOrDefault()?.MediaType,
                request.Headers.TryGetValues("X-GitHub-Api-Version", out var version) ? version.Single() : null));
            return Task.FromResult(respond(request));
        }
    }
}
