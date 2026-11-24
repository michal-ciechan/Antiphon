using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>
/// Sign-in with GitHub (<c>Review:Auth:Mode</c> GitHub) against the real host and Postgres, with GitHub's
/// OAuth answers and each person's repo access faked: the API needs a session, a sign-in round trip sets
/// one for the GitHub login, a person decides gates only on repos they can write to, someone with no
/// access gets no session, a forged state is refused, and signing out ends the session.
/// </summary>
[Category("Integration")]
[NotInParallel("ReviewEndToEnd")]
[ClassDataSource<ReviewSignInApiTests.Factory>(Shared = SharedType.PerClass)]
public class ReviewSignInApiTests(ReviewSignInApiTests.Factory factory)
{
    private const string WritableRepo = "acme/billing";

    [Before(Test)]
    public Task ResetAsync() => factory.ResetAsync();

    [Test]
    public async Task The_api_needs_a_session_but_the_sign_in_routes_and_version_stay_open()
    {
        using var client = NewClient();

        var runs = await client.GetAsync("/api/review-runs");
        runs.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await runs.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().ShouldBe("sign_in_required");
        (await client.GetAsync("/api/review-me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var status = await client.GetFromJsonAsync<JsonElement>("/api/review-auth/status");
        status.GetProperty("mode").GetString().ShouldBe("GitHub");
        status.GetProperty("signedIn").GetBoolean().ShouldBeFalse();
        (await client.GetAsync("/api/version")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task A_signed_in_person_acts_as_their_login_and_decides_only_where_they_can_write()
    {
        // The demo runs give the reviewed repos open gates; an allowed login seeds them.
        using (var boss = NewClient())
        {
            await SignInAsync(boss, "boss-code", "/reviews");
            (await boss.PostAsync("/api/review-runs/demo", null)).EnsureSuccessStatusCode();
        }

        using var client = NewClient();
        var landed = await SignInAsync(client, "dev-code", "/reviews/queue?scope=All");
        landed.ShouldBe("/reviews/queue?scope=All");

        var me = await client.GetFromJsonAsync<JsonElement>("/api/review-me");
        me.GetProperty("login").GetString().ShouldBe("dev");
        me.GetProperty("displayName").GetString().ShouldBe("Dev Eloper");
        var status = await client.GetFromJsonAsync<JsonElement>("/api/review-auth/status");
        status.GetProperty("signedIn").GetBoolean().ShouldBeTrue();
        status.GetProperty("login").GetString().ShouldBe("dev");

        var open = (await client.GetFromJsonAsync<JsonElement>("/api/review-queue?scope=All")).GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("gate").GetProperty("status").GetString() != "Done")
            .Select(i => (Repo: i.GetProperty("run").GetProperty("subject").GetProperty("repo").GetString()!, Gate: i.GetProperty("gate")))
            .ToList();
        var writable = open.Where(i => i.Repo == WritableRepo).ToList();
        var other = open.Where(i => i.Repo != WritableRepo).ToList();
        writable.ShouldNotBeEmpty();
        other.ShouldNotBeEmpty();
        writable.ShouldAllBe(i => i.Gate.GetProperty("viewerCanDecide").GetBoolean());
        foreach (var (repo, gate) in other)
        {
            gate.GetProperty("viewerCanDecide").GetBoolean().ShouldBeFalse();
            gate.GetProperty("viewerCannotDecideReason").GetString().ShouldBe(
                $"You need write access to {repo} on GitHub to decide its gates (checked when you signed in: sign out and in again after your access changes)");
        }

        var refused = await client.PostAsJsonAsync($"/api/review-gates/{other[0].Gate.GetProperty("id").GetString()}/decision", new { decision = "Approve" });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Someone_with_no_access_to_a_reviewed_repo_gets_no_session()
    {
        using var client = NewClient();
        var state = await StartAsync(client, "/");

        var callback = await client.GetAsync($"/api/review-auth/callback?code=stranger-code&state={Uri.EscapeDataString(state)}");

        callback.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await callback.Content.ReadAsStringAsync()).ShouldContain("@stranger needs write access to one of the repos this Antiphon reviews");
        (await client.GetAsync("/api/review-me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task A_callback_whose_state_does_not_match_is_refused()
    {
        using var client = NewClient();
        await StartAsync(client, "/");

        var callback = await client.GetAsync("/api/review-auth/callback?code=forged-code&state=forged");

        callback.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.GitHub.Exchanged.ShouldNotContain("forged-code");
        (await client.GetAsync("/api/review-me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Signing_out_ends_the_session()
    {
        using var client = NewClient();
        await SignInAsync(client, "boss-code", "/");
        (await client.GetAsync("/api/review-me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.PostAsync("/api/review-auth/logout", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.GetAsync("/api/review-me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // The cookies were protected with sign-in's own key ring (Review:Auth:KeyRingPath), not the server's.
        Directory.EnumerateFiles(factory.SignInKeys, "key-*.xml").ShouldNotBeEmpty();
    }

    [Test]
    [Arguments("/reviews/1?x=2", "/reviews/1?x=2")]
    [Arguments("//evil.example/x", "/")]
    [Arguments("/\\evil.example", "/")]
    [Arguments("https://evil.example/", "/")]
    [Arguments(null, "/")]
    public void A_return_url_stays_on_this_site(string? returnUrl, string expected) =>
        ReviewAuthEndpoints.LocalReturnUrl(returnUrl).ShouldBe(expected);

    private HttpClient NewClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    /// <summary>Starts sign-in and returns the state GitHub would hand back.</summary>
    private static async Task<string> StartAsync(HttpClient client, string returnUrl)
    {
        var login = await client.GetAsync($"/api/review-auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = login.Headers.Location!.ToString();
        location.ShouldStartWith(FakeGitHubSignIn.AuthorizePrefix);
        return location[FakeGitHubSignIn.AuthorizePrefix.Length..];
    }

    /// <summary>The whole round trip; returns where the callback sends the browser.</summary>
    private static async Task<string> SignInAsync(HttpClient client, string code, string returnUrl)
    {
        var state = await StartAsync(client, returnUrl);
        var callback = await client.GetAsync($"/api/review-auth/callback?code={code}&state={Uri.EscapeDataString(state)}");
        callback.StatusCode.ShouldBe(HttpStatusCode.Redirect, await callback.Content.ReadAsStringAsync());
        return callback.Headers.Location!.ToString();
    }

    public sealed class FakeGitHubSignIn : IGitHubSignIn
    {
        public const string AuthorizePrefix = "https://github.test/authorize?state=";

        private static readonly Dictionary<string, GitHubSignInUser> Users = new()
        {
            ["dev-code"] = new GitHubSignInUser(1, "dev", "Dev Eloper"),
            ["boss-code"] = new GitHubSignInUser(2, "boss", null),
            ["stranger-code"] = new GitHubSignInUser(3, "stranger", null),
        };

        public List<string> Exchanged { get; } = [];

        public Uri AuthorizeUrl(string state, string redirectUri)
        {
            redirectUri.ShouldEndWith(ReviewAuthEndpoints.CallbackPath);
            return new Uri(AuthorizePrefix + state);
        }

        public Task<string> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct)
        {
            lock (Exchanged)
                Exchanged.Add(code);
            return Users.ContainsKey(code) ? Task.FromResult("token-" + code) : throw new GitHubSignInException("bad_verification_code");
        }

        public Task<GitHubSignInUser> GetUserAsync(string accessToken, CancellationToken ct) =>
            Task.FromResult(Users[accessToken["token-".Length..]]);
    }

    /// <summary>dev can write to acme/billing and only read the rest; nobody else has access; boss is an allowed login.</summary>
    public sealed class FakeRepoAccess : IReviewRepoAccess
    {
        public Task<string> GetPermissionAsync(string repo, string login, CancellationToken ct) =>
            Task.FromResult(login == "dev" ? repo == WritableRepo ? "write" : "read" : "none");
    }

    public sealed class Factory : AntiphonWebAppFactory
    {
        public FakeGitHubSignIn GitHub { get; } = new();

        /// <summary>Sign-in's own key ring, as docker-compose.review.yml sets it.</summary>
        public string SignInKeys { get; } = Path.Combine(Path.GetTempPath(), "antiphon-signin-keys-" + Guid.NewGuid().ToString("N"));

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            try
            {
                Directory.Delete(SignInKeys, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            services.RemoveAll<IGitHubSignIn>();
            services.AddSingleton<IGitHubSignIn>(GitHub);
            services.RemoveAll<IReviewRepoAccess>();
            services.AddSingleton<IReviewRepoAccess, FakeRepoAccess>();
            services.PostConfigure<ReviewSettings>(s =>
            {
                s.DemoSeedEnabled = true;
                s.AllowViewerHeader = true; // ignored while sign-in is on
                s.DefaultViewerLogin = "ops";
                s.PublicBaseUrl = null;
                s.Auth.Mode = ReviewAuthModes.GitHub;
                s.Auth.ClientId = "Iv23test";
                s.Auth.ClientSecret = "test-secret";
                s.Auth.AllowedLogins = ["boss"];
                s.Auth.KeyRingPath = SignInKeys;
            });
            services.PostConfigure<ReviewPollingSettings>(s =>
            {
                s.Enabled = false;
                s.Repos = [new ReviewPollingRepo { Repo = WritableRepo }];
            });
        }
    }
}
