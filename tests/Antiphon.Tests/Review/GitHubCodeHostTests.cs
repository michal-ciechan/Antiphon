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

    [Test]
    public async Task An_app_host_signs_a_jwt_finds_the_installation_and_reuses_the_installation_token()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var tokenPosts = 0;
        string? jwt = null;
        var handler = new FakeHandler(r => r.RequestUri!.PathAndQuery switch
        {
            "/api/v3/repos/acme/widgets/installation" => Json(HttpStatusCode.OK, """{ "id": 7 }"""),
            "/api/v3/app/installations/7/access_tokens" when r.Method == HttpMethod.Post => Token(r),
            "/api/v3/repos/acme/widgets/pulls/42" => Json(HttpStatusCode.OK, PullRequest),
            var other => throw new InvalidOperationException($"Unexpected request {other}"),
        });
        HttpResponseMessage Token(HttpRequestMessage r)
        {
            tokenPosts++;
            jwt = r.Headers.Authorization!.Parameter;
            return Json(HttpStatusCode.Created, $$"""{ "token": "ghs_inst", "expires_at": "{{DateTimeOffset.UtcNow.AddHours(1):O}}" }""");
        }

        var settings = new ReviewCodeHostSettings();
        settings.Hosts["ghe"] = new ReviewCodeHost
        {
            Kind = ReviewHost.GitHub,
            BaseUrl = "https://ghe.example/api/v3/",
            Token = "pat-unused",
            App = new ReviewGitHubApp { AppId = "Iv23app", PrivateKey = rsa.ExportRSAPrivateKeyPem() },
        };
        var host = new GitHubCodeHost(new HttpClient(handler), Options.Create(settings), new CodeHostResponseCache(), new GitHubAppTokens());

        await host.GetPullRequestAsync(Target, CancellationToken.None);
        await host.GetPullRequestAsync(Target, CancellationToken.None);

        tokenPosts.ShouldBe(1, "the installation token is cached until near its expiry");
        handler.Requests.Where(q => q.Uri.EndsWith("/pulls/42")).ShouldAllBe(q => q.Authorization == "Bearer ghs_inst");
        var parts = jwt!.Split('.');
        parts.Length.ShouldBe(3);
        var payload = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(Pad(parts[1]))).RootElement;
        payload.GetProperty("iss").GetString().ShouldBe("Iv23app");
        (payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64()).ShouldBeLessThanOrEqualTo(600, "GitHub refuses an app JWT valid for more than ten minutes");
        rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Convert.FromBase64String(Pad(parts[2])),
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1).ShouldBeTrue();

        static string Pad(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            return s + new string('=', (4 - s.Length % 4) % 4);
        }
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
