using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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

    private async Task WaitTerminalAsync(Guid taskId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == taskId && e.IsLandTerminal))
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException("terminal event");
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

    [Test]
    [Arguments(AgentTaskRole.Plan)]
    [Arguments(AgentTaskRole.TestDesign)]
    public async Task C498_PlanAndTestDesignExplicitShaAdmitted(AgentTaskRole role)
    {
        var task = await SeedSucceededAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            stored.Role = role;
            stored.MergeTargetRef = null;
            await db.SaveChangesAsync();
        }
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/land/v2",
            new { expectedSourceSha = ShaB });
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = await ReadLandAsync(response);
        using var read = _factory.Services.CreateScope();
        var observer = read.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == body.RequestId);
        request.ApprovalKind.ShouldBe(LandApprovalKind.ExplicitCaller);
        request.SchemaVersion.ShouldBe(2);
        request.ExpectedSourceSha.ShouldBe(ShaB);
        request.ReviewEvidenceId.ShouldBeNull();
        request.TargetFullRefSnapshot.ShouldBe("refs/heads/master");
        Release(task.Id);
    }

    [Test]
    [Arguments("hosted-catch")]
    [Arguments("direct")]
    public async Task C498_TaskGetExposesFailureDiagnostics(string producer)
    {
        const string marker = "synthetic-secret-marker://user:pw@host/?q=1";
        var empty = Directory.CreateTempSubdirectory("c498-land-empty-").FullName;
        var task = await SeedSucceededAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            stored.RepoPath = empty;
            stored.WorktreePath = empty;
            await db.SaveChangesAsync();
        }
        using var client = _factory.CreateClient();
        var queued = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/land/v2",
            new { expectedSourceSha = ShaB });
        queued.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = await ReadLandAsync(queued);
        Release(task.Id);
        if (producer == "hosted-catch")
        {
            var hosted = new AgentTaskLandHostedService(
                _factory.Services.GetRequiredService<AgentTaskLandQueue>(),
                _factory.Services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<AgentTaskLandHostedService>.Instance);
            await hosted.StartAsync(CancellationToken.None);
            try { await WaitTerminalAsync(task.Id); }
            finally { await hosted.StopAsync(CancellationToken.None); }
        }
        else
        {
            using var scope = _factory.Services.CreateScope();
            var lands = scope.ServiceProvider.GetRequiredService<AgentTaskLandService>();
            await lands.FailRequestAsync(task.Id, body.RequestId, new IOException(marker), CancellationToken.None);
        }
        var jsonText = await client.GetStringAsync($"/api/agent-tasks/{task.Id}");
        using var doc = JsonDocument.Parse(jsonText);
        var json = doc.RootElement.Clone();
        var land = json.GetProperty("landRequest");
        land.GetProperty("terminalFailureCode").GetString().ShouldBe("landing_io_error");
        land.GetProperty("failureExceptionType").GetString().ShouldBe("IOException");
        var diagnostic = land.GetProperty("failureDiagnosticId").GetGuid();
        diagnostic.ShouldNotBe(Guid.Empty);
        land.ValueKind.ShouldBe(JsonValueKind.Object);
        if (land.TryGetProperty("sourceRefusalReason", out var refusal))
            (refusal.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || refusal.GetString() is null).ShouldBeTrue();
        json.TryGetProperty("landing", out var landing).ShouldBeTrue();
        landing.ValueKind.ShouldBe(JsonValueKind.Null);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == body.RequestId);
            row.FailureDiagnosticId.ShouldBe(diagnostic);
            var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.RequestId == body.RequestId && n.Kind == LandNotificationKind.Outcome);
            note.Body.ShouldContain($"landing_io_error; diagnostic={diagnostic:N}; exception=IOException");
            note.Body.ShouldNotContain("synthetic-secret-marker");
        }
        jsonText.ShouldNotContain("synthetic-secret-marker");
        await using var server = LandApiStub.Compatible(new string('e', 40), taskStatusBody: jsonText);
        var status = await DelegateScriptRunner.RunAsync(server.Url, "-Status", task.Id.ToString());
        status.ExitCode.ShouldBe(0, status.Output);
        status.Output.ShouldContain($"Land execution failure: landing_io_error; diagnostic {diagnostic}; exception IOException");
        status.Output.ShouldContain("Publication: Unconfirmed; cleanup: NotStarted");
        status.Output.ShouldNotContain("synthetic-secret-marker");
    }

    [Test]
    public async Task C498_TaskGetExposesInspectionDiagnostics()
    {
        var task = await SeedSucceededAsync();
        Guid requestId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            var request = new AgentTaskLandRequest
            {
                Id = Guid.NewGuid(), TaskId = stored.Id, RequestedAt = DateTime.UtcNow, LastEvaluatedAt = DateTime.UtcNow,
                LastProgressAt = DateTime.UtcNow, State = LandRequestState.Completed, IsPending = false, SchemaVersion = 2,
                ExpectedSourceSha = ShaB, SourceDiagnosticCommand = "git status --porcelain=v1",
                SourceDiagnosticExitCode = 128, SourceDiagnosticCode = "git_exit_128",
                SourceDiagnosticExceptionType = "LandingGitCommandException",
            };
            db.AgentTaskLandRequests.Add(request);
            stored.CurrentLandRequestId = request.Id;
            await db.SaveChangesAsync();
            requestId = request.Id;
        }
        using var client = _factory.CreateClient();
        var json = await client.GetFromJsonAsync<JsonElement>($"/api/agent-tasks/{task.Id}");
        var land = json.GetProperty("landRequest");
        land.GetProperty("sourceDiagnosticCommand").GetString().ShouldBe("git status --porcelain=v1");
        land.GetProperty("sourceDiagnosticExitCode").GetInt32().ShouldBe(128);
        land.GetProperty("sourceDiagnosticCode").GetString().ShouldBe("git_exit_128");
        land.GetProperty("sourceDiagnosticExceptionType").GetString().ShouldBe("LandingGitCommandException");
        requestId.ShouldNotBe(Guid.Empty);
    }
}
