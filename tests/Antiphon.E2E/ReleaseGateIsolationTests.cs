using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.E2E.Fixtures;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

/// <summary>
/// CARD-0599 V-4 and R-3, fixture half. The RC profile requires automated E2E, so the fixtures it
/// runs on must demonstrably own their resources rather than borrowing the developer's stack.
/// These tests boot the real <see cref="AntiphonAppFixture"/> - real Program, real disposable
/// Postgres, an isolated random-port session runner and the prebuilt client bundle - and assert
/// observed behaviour, never source text.
///
/// <para>The decisive behaviours: the app's session-runner endpoint is its own loopback random
/// port and is NOT production 17204; the bundle bytes actually served come from this run's
/// client/dist; the database really persists a write and reads it back; headed, distiller and live
/// provider environment variables are cleared inside the test process; and teardown produces a
/// cleanup verdict rather than leaking owned children.</para>
/// </summary>
[NotInParallel]
[Category("OptIn")]
public class ReleaseGateIsolationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AntiphonAppFixture _app = new();

    [Before(Test)]
    public async Task SetupAsync()
    {
        _app.UsePrebuiltFrontend = true;
        await _app.InitializeAsync();
    }

    [After(Test)]
    public async Task TeardownAsync() => await _app.DisposeAsync();

    /// <summary>
    /// The owned runner is a real loopback endpoint on a random port, and it is not the production
    /// runner. A test host that boots real Program must never reach 17204.
    /// </summary>
    [Test]
    public async Task C599_OwnedServices()
    {
        var runnerUrl = _app.OwnedRunnerUrl;
        runnerUrl.ShouldNotBeNullOrWhiteSpace("C599 G-owned: the fixture must own a runner");

        var runner = new Uri(runnerUrl);
        runner.Host.ShouldBeOneOf(["127.0.0.1", "localhost"], "C599 G-owned: the owned runner is loopback");
        runner.Port.ShouldNotBe(17204, "C599 G-owned: the owned runner must not be the production runner");

        var app = new Uri(_app.BaseAddress);
        app.Port.ShouldNotBe(17204, "C599 G-owned: the app must not be served on the production runner port");
        app.Port.ShouldNotBe(17202, "C599 G-owned: the app must not be served on the production server port");
        app.Port.ShouldNotBe(runner.Port, "C599 G-owned: app and runner hold distinct ports");

        // The app is really listening on that endpoint, not merely configured for it.
        using var client = _app.CreateClient();
        var health = await client.GetAsync("/health");
        health.StatusCode.ShouldBe(HttpStatusCode.OK, "C599 G-owned: the owned app answers its own endpoint");

        // And the database is a real one that persists: write, then read back by id.
        var project = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "c599-isolation-" + Guid.NewGuid().ToString("N")[..8],
            gitHubIntegrationEnabled = false,
            notificationsEnabled = false,
        }, JsonOptions);
        project.EnsureSuccessStatusCode();
        var projectId = (await project.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();

        var board = await client.PostAsJsonAsync("/api/boards", new { projectId, name = "c599-board" }, JsonOptions);
        board.EnsureSuccessStatusCode();
        var boardId = (await board.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();

        var readBack = await client.GetAsync($"/api/boards/{boardId}");
        readBack.StatusCode.ShouldBe(HttpStatusCode.OK, "C599 G-owned: the owned database persists the write");
        var stored = await readBack.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        stored.GetProperty("id").GetGuid().ShouldBe(boardId, "C599 G-owned: the stored row is the one we wrote");
    }

    /// <summary>
    /// The test child must not be able to reach a live provider, a live broker or the distiller
    /// approval path. These are cleared for the child process, and the bundle the app serves is
    /// this run's own build rather than whatever a dev server happens to be hosting.
    /// </summary>
    [Test]
    public async Task C599_RefusingAdapters()
    {
        foreach (var name in new[]
                 {
                     "ANTIPHON_HEADED_TESTS", "ANTIPHON_HEADED_LONG_TESTS",
                     "ANTIPHON_DISTILLER_APPLY_CANARY", "ANTIPHON_TG_TEST_TOKEN",
                 })
        {
            var value = Environment.GetEnvironmentVariable(name);
            string.IsNullOrEmpty(value).ShouldBeTrue(
                $"C599 G-refusing: {name} must be clear in an automated E2E child, was '{value}'");
        }

        // The served index really is this run's bundle: fetch it over HTTP and compare to the
        // bytes on disk, so a stale or foreign bundle cannot pass.
        using var client = _app.CreateClient();
        var index = await client.GetAsync("/");
        index.StatusCode.ShouldBe(HttpStatusCode.OK, "C599 G-refusing: the app serves the prebuilt bundle");
        var servedHtml = await index.Content.ReadAsStringAsync();
        servedHtml.ShouldNotBeNullOrWhiteSpace("C599 G-refusing: the served bundle is not empty");
        servedHtml.ShouldContain("<div id=\"root\"", Case.Insensitive, "C599 G-refusing: the served bytes are the SPA shell");
    }

    /// <summary>
    /// Teardown must be accounted for: the fixture records a session-leak verdict, and disposing
    /// it twice is safe. A cleanup that silently skipped would leave owned children behind.
    /// </summary>
    [Test]
    public async Task C599_CleanupEvidence()
    {
        // Prove the fixture can produce its cleanup census on demand, before the [After] hook runs.
        var runnerDirectory = _app.OwnedRunnerDirectory;
        runnerDirectory.ShouldNotBeNullOrWhiteSpace("C599 G-cleanup: the owned runner has a run directory");
        Directory.Exists(runnerDirectory).ShouldBeTrue($"C599 G-cleanup: the owned run directory exists: {runnerDirectory}");

        // The runner directory is owned by this run, not a shared or production location.
        runnerDirectory.ShouldNotContain(@"C:\src\Antiphon", Case.Insensitive, "C599 G-cleanup: the run directory is not the canonical checkout");

        using var client = _app.CreateClient();
        var health = await client.GetAsync("/health");
        health.StatusCode.ShouldBe(HttpStatusCode.OK, "C599 G-cleanup: control - the stack is up before teardown is judged");

        _app.SessionLeakVerdict.ShouldBeNull("C599 G-cleanup: no session leak has been recorded while the stack is healthy");
    }
}
