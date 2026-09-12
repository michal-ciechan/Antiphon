using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel]
[ClassDataSource<LandContractWebAppFactory>(Shared = SharedType.PerClass)]
[Category("Integration")]
public sealed class AgentTaskLandContractEndpointTests
{
    private static readonly string ShaB = new('b', 40);
    private static readonly string ShaC = new('c', 40);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private const string VerifyFilter = "/*/*/FreshnessProbeTests/ApprovedFixIsPresent";

    private readonly LandContractWebAppFactory _factory;

    public AgentTaskLandContractEndpointTests(LandContractWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task C495_V2RouteQueuesExactApproval()
    {
        var task = await SeedSucceededAsync();
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{task.Id}/land/v2",
            new { expectedSourceSha = ShaB, verify = VerifyFilter, reviewEvidenceId = (Guid?)null });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location.ShouldNotBeNull();
        response.Headers.Location!.ToString().ShouldContain($"/api/agent-tasks/{task.Id}");
        var body = await ReadLandAsync(response);
        body.Status.ShouldBe("queued");
        body.Notification.ShouldBe("not-required");
        body.RequestId.ShouldNotBe(Guid.Empty);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == body.RequestId);
        row.ExpectedSourceSha.ShouldBe(ShaB);
        row.VerifyFilter.ShouldBe(VerifyFilter);
        var ev = await db.AgentTaskEvents.SingleAsync(e =>
            e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.LandRequested);
        ev.LandRequestId.ShouldBe(body.RequestId);
        Release(task.Id);
    }

    [Test]
    public async Task C495_V2RouteRejectsFreshMissingApproval()
    {
        var task = await SeedSucceededAsync();
        using var client = _factory.CreateClient();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"/api/agent-tasks/{task.Id}/land/v2", content);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadCodeAsync(response)).ShouldBe("expected_source_sha_required");
        await AssertNoRequestsAsync(task.Id);
    }

    [Test]
    public async Task C495_V2RouteRejectsAbbreviatedSha()
    {
        var task = await SeedSucceededAsync();
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{task.Id}/land/v2",
            new { expectedSourceSha = "deadbee" });
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadCodeAsync(response)).ShouldBe("expected_source_sha_invalid");
        await AssertNoRequestsAsync(task.Id);
    }

    [Test]
    public async Task C495_V2RouteRejectsMismatchedEvidence()
    {
        var task = await SeedSucceededAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await LandContractSeeds.SeedReviewAsync(db, task, ShaB);
        }

        using var client = _factory.CreateClient();
        Guid evidenceId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            evidenceId = await db.StageOutcomes.Where(o => o.SubjectTaskId == task.Id).Select(o => o.Id).SingleAsync();
        }

        var response = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{task.Id}/land/v2",
            new { expectedSourceSha = ShaC, reviewEvidenceId = evidenceId });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await AssertNoRequestsAsync(task.Id);
    }

    [Test]
    public async Task C495_V2RouteBodylessResumeInheritsApproval()
    {
        var task = await SeedSucceededAsync();
        using var client = _factory.CreateClient();
        var first = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{task.Id}/land/v2",
            new { expectedSourceSha = ShaB });
        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var queued = await ReadLandAsync(first);
        Release(task.Id);

        using var empty = new StringContent("{}", Encoding.UTF8, "application/json");
        var resume = await client.PostAsync($"/api/agent-tasks/{task.Id}/land/v2", empty);
        resume.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var requeued = await ReadLandAsync(resume);
        requeued.Status.ShouldBe("requeued");
        requeued.RequestId.ShouldBe(queued.RequestId);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId))
                .ExpectedSourceSha.ShouldBe(ShaB);
        }

        Release(task.Id);
        var conflict = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{task.Id}/land/v2",
            new { expectedSourceSha = ShaC });
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(conflict)).ShouldBe("land_request_identity_conflict");
        Release(task.Id);
    }

    [Test]
    public async Task C495_LegacyRouteSharesHandler()
    {
        var task = await SeedSucceededAsync();
        using var client = _factory.CreateClient();
        var legacy = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{task.Id}/land",
            new { expectedSourceSha = ShaB });
        legacy.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var queued = await ReadLandAsync(legacy);
        queued.Status.ShouldBe("queued");
        queued.Notification.ShouldBe("not-required");
        Release(task.Id);

        using var empty = new StringContent("{}", Encoding.UTF8, "application/json");
        var resume = await client.PostAsync($"/api/agent-tasks/{task.Id}/land/v2", empty);
        resume.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var requeued = await ReadLandAsync(resume);
        requeued.Status.ShouldBe("requeued");
        requeued.RequestId.ShouldBe(queued.RequestId);
        Release(task.Id);

        var other = await SeedSucceededAsync();
        using var missing = new StringContent("{}", Encoding.UTF8, "application/json");
        var fresh = await client.PostAsync($"/api/agent-tasks/{other.Id}/land", missing);
        fresh.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadCodeAsync(fresh)).ShouldBe("expected_source_sha_required");
    }

    [Test]
    public async Task C495_V2RouteResolvesShortIdAnd404()
    {
        var task = await SeedSucceededAsync();
        using var client = _factory.CreateClient();
        var shortId = DelegationReportFormatter.Short(task.Id);
        var ok = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{shortId}/land/v2",
            new { expectedSourceSha = ShaB });
        ok.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = await ReadLandAsync(ok);
        body.TaskId.ShouldBe(task.Id);
        Release(task.Id);

        var missing = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{Guid.NewGuid()}/land/v2",
            new { expectedSourceSha = ShaB });
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task C495_V2RouteRefusesUnsucceededTask()
    {
        var task = await SeedWorktreeAsync(AgentTaskStatus.Failed);
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{task.Id}/land/v2",
            new { expectedSourceSha = ShaB });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await AssertNoRequestsAsync(task.Id);
    }

    private async Task<Antiphon.Server.Domain.Entities.AgentTask> SeedSucceededAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await LandContractSeeds.SeedSucceededWorktreeAsync(db);
    }

    private async Task<Antiphon.Server.Domain.Entities.AgentTask> SeedWorktreeAsync(AgentTaskStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await LandContractSeeds.SeedWorktreeAsync(db, status);
    }

    private void Release(Guid taskId) =>
        _factory.Services.GetRequiredService<AgentTaskLandQueue>().Release(taskId);

    private async Task AssertNoRequestsAsync(Guid taskId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == taskId)).ShouldBe(0);
    }

    private static async Task<LandBody> ReadLandAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<LandBody>(Json);
        body.ShouldNotBeNull();
        return body;
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private sealed record LandBody(Guid TaskId, string Status, Guid RequestId, string Notification);
}
