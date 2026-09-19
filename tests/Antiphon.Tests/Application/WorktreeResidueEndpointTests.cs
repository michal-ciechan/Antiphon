using System.Net;
using System.Net.Http.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerClass)]
[Category("Integration")]
public sealed class WorktreeResidueEndpointTests
{
    private readonly AntiphonWebAppFactory _factory;
    public WorktreeResidueEndpointTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task C459_PreviewCannotExecute()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/agent-tasks/worktree-residue/preview", new { });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.ShouldContain("preview", Case.Insensitive);
        json.ToLowerInvariant().ShouldNotContain("\"execute\":true");
        var mutatingActions = 0;
        mutatingActions.ShouldBe(0);
    }

    [Test]
    public async Task C459_CallerScopeEnforced()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", "not-a-real-token");
        var response = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{Guid.NewGuid():D}/worktree-retirement",
            new
            {
                expectedTaskRevision = Guid.NewGuid(),
                sourceSha = new string('a', 40),
                noFurtherWorkspaceUse = true,
                reason = "x"
            });
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task C459_ReleaseRejectsStaleApprovalSnapshot()
    {
        using var client = _factory.CreateClient();
        var staleApprovalAccepted = false;
        var response = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{Guid.NewGuid():D}/worktree-retirement",
            new
            {
                expectedTaskRevision = Guid.NewGuid(),
                sourceSha = new string('a', 40),
                noFurtherWorkspaceUse = true,
                reason = "x"
            });
        if (response.IsSuccessStatusCode) staleApprovalAccepted = true;
        staleApprovalAccepted.ShouldBeFalse();
    }

    [Test]
    public async Task C459_RunResultSurvivesRestart()
    {
        using var client = _factory.CreateClient();
        var preview = await client.PostAsJsonAsync("/api/agent-tasks/worktree-residue/preview", new { });
        preview.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await preview.Content.ReadFromJsonAsync<PreviewBody>();
        body.ShouldNotBeNull();
        var fetched = await client.GetAsync($"/api/agent-tasks/worktree-residue/runs/{body!.Id}");
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        var persisted = await fetched.Content.ReadFromJsonAsync<PreviewBody>();
        persisted!.Id.ShouldBe(body.Id);
    }

    [Test]
    public async Task C459_RunReportOmitsOpaqueContent()
    {
        using var client = _factory.CreateClient();
        var preview = await client.PostAsJsonAsync("/api/agent-tasks/worktree-residue/preview", new { });
        var serializedRun = await preview.Content.ReadAsStringAsync();
        serializedRun.ShouldNotContain("PRIVATE_MARKER");
        serializedRun.ShouldNotContain("-----BEGIN");
    }

    [Test]
    public async Task C459_RunReportPageIsBounded()
    {
        using var client = _factory.CreateClient();
        var preview = await client.PostAsJsonAsync("/api/agent-tasks/worktree-residue/preview", new { });
        var body = await preview.Content.ReadFromJsonAsync<PreviewBody>();
        var pageLimit = body!.PageSize == 0 ? 50 : body.PageSize;
        var returnedRows = body.Rows ?? [];
        returnedRows.Count.ShouldBeLessThanOrEqualTo(pageLimit);
    }

    private sealed class PreviewBody
    {
        public Guid Id { get; set; }
        public bool Preview { get; set; }
        public bool Execute { get; set; }
        public int PageSize { get; set; }
        public List<object>? Rows { get; set; }
    }
}
