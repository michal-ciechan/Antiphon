using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public sealed class AgentTaskDecisionQuestionApiTests
{
    private readonly AntiphonWebAppFactory _factory;
    public AgentTaskDecisionQuestionApiTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task Malformed_question_json_is_a_structured_422()
    {
        await _factory.ResetAsync();
        using var workspace = new DecisionTempWorkspace();
        var (taskId, token) = await SeedAsync(workspace.Path);
        using var client = _factory.CreateClient();
        using var body = new StringContent("{invalid-json", Encoding.UTF8, "application/json");
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        var response = await client.PostAsync(
            $"/api/agent-tasks/{taskId}/decision-questions", body);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Test]
    public async Task Own_worker_token_checks_a_grant_without_blocking_the_task()
    {
        await _factory.ResetAsync();
        using var workspace = new DecisionTempWorkspace();
        var (taskId, token) = await SeedAsync(workspace.Path);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        var request = new InternalDecisionQuestionRequest(Guid.NewGuid(), 1,
            "repair", InternalDecisionCategory.ShellTransport, ["scripts/deploy.ps1"],
            null, null, InternalDecisionImpact.None, "May I repair quoting?",
            "Repair quoting only.", "The captured arguments stay the same.");

        var response = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{taskId}/decision-questions", request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
            });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("disposition").GetString().ShouldBe("Continue");
        json.GetProperty("reason").GetString().ShouldBe("dispatch_grant");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AgentTasks.FindAsync(taskId))!.Status.ShouldBe(AgentTaskStatus.Working);
    }

    [Test]
    public async Task Self_identity_and_current_binding_are_required_before_idempotency()
    {
        await _factory.ResetAsync();
        using var workspace = new DecisionTempWorkspace();
        var (taskId, taskToken) = await SeedAsync(workspace.Path);
        var request = Sample();
        Guid sessionId;
        string sessionToken = Guid.NewGuid().ToString("N");
        string otherToken = Guid.NewGuid().ToString("N");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            sessionId = task.AgentSessionId!.Value;
            (await db.AgentSessions.SingleAsync(s => s.Id == sessionId)).DelegationTokenHash = AgentTaskService.HashToken(sessionToken);
            db.AgentSessions.Add(new AgentSession
            {
                Id = Guid.NewGuid(), DefinitionName = "other", AgentKind = AgentKind.Grok,
                Status = SessionStatus.Running, Cwd = workspace.Path,
                DelegationTokenHash = AgentTaskService.HashToken(otherToken),
                CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        (await PostAsync(taskId, taskToken, request)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostAsync(taskId, sessionToken, request with { RequestId = Guid.NewGuid() })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostAsync(taskId, null, request)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PostAsync(taskId, "invalid", request)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PostAsync(taskId, otherToken, request)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            task.Status = AgentTaskStatus.Blocked;
            await db.SaveChangesAsync();
        }
        (await PostAsync(taskId, taskToken, request)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await PostAsync(taskId, sessionToken, request)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AgentTaskDecisionQuestions.CountAsync(q => q.AgentTaskId == taskId)).ShouldBe(2);
            var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            task.Status = AgentTaskStatus.Working;
            task.Attempt = 2;
            task.AgentSessionId = Guid.NewGuid();
            db.AgentSessions.Add(new AgentSession
            {
                Id = task.AgentSessionId.Value, DefinitionName = "replacement", AgentKind = AgentKind.Grok,
                Status = SessionStatus.Running, Cwd = workspace.Path,
                CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        (await PostAsync(taskId, sessionToken, request with { Attempt = 2 })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await PostAsync(taskId, taskToken, request)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task State_attempt_and_live_session_matrix()
    {
        await _factory.ResetAsync();
        using var workspace = new DecisionTempWorkspace();
        var (taskId, token) = await SeedAsync(workspace.Path);
        var request = Sample();
        foreach (var status in new[] { AgentTaskStatus.Queued, AgentTaskStatus.Blocked,
                     AgentTaskStatus.Succeeded, AgentTaskStatus.Failed, AgentTaskStatus.Canceled })
        {
            await SetStateAsync(taskId, status);
            (await PostAsync(taskId, token, request with { RequestId = Guid.NewGuid() })).StatusCode
                .ShouldBe(HttpStatusCode.Conflict);
        }
        await SetStateAsync(taskId, AgentTaskStatus.Dispatched);
        (await PostAsync(taskId, token, request with { RequestId = Guid.NewGuid() })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostAsync(taskId, token, request with { Attempt = 0 })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await PostAsync(taskId, token, request with { Attempt = 2 })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            (await db.AgentSessions.SingleAsync(s => s.Id == task.AgentSessionId)).Status = SessionStatus.Stopped;
            await db.SaveChangesAsync();
        }
        (await PostAsync(taskId, token, request with { RequestId = Guid.NewGuid() })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            (await db.AgentSessions.SingleAsync(s => s.Id == task.AgentSessionId)).Status = SessionStatus.Running;
            task.AgentSessionId = null;
            await db.SaveChangesAsync();
        }
        (await PostAsync(taskId, token, request with { RequestId = Guid.NewGuid() })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Question_schema_refuses_invalid_and_unknown_fields_without_audit_rows()
    {
        await _factory.ResetAsync();
        using var workspace = new DecisionTempWorkspace();
        var (taskId, token) = await SeedAsync(workspace.Path);
        var valid = JsonSerializer.SerializeToNode(Sample(), InternalDecisionPolicy.JsonOptions)!.AsObject();
        var invalid = new List<JsonObject>();
        void Add(Action<JsonObject> change)
        {
            var node = (JsonObject)valid.DeepClone();
            change(node);
            invalid.Add(node);
        }
        Add(j => j.Remove("impact"));
        Add(j => j["impact"] = null);
        Add(j => j["impact"] = "Imaginary");
        Add(j => j["impact"] = 0);
        Add(j => j["category"] = 99);
        Add(j => j["requestId"] = "bad-guid");
        Add(j => j["attempt"] = 0);
        Add(j => j["paths"] = new JsonArray());
        Add(j => j["preservationEvidence"] = " ");
        Add(j => j["unknown"] = true);
        Add(j => j["question"] = new string('x', 501));
        Add(j => j["proposedAction"] = new string('x', 1001));
        Add(j => j["preservationEvidence"] = new string('x', 2001));
        foreach (var node in invalid)
            (await PostRawAsync(taskId, token, node.ToJsonString())).StatusCode
                .ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AgentTaskDecisionQuestions.CountAsync(q => q.AgentTaskId == taskId)).ShouldBe(0);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
            && e.Type == AgentTaskEventType.DecisionQuestion)).ShouldBe(0);
    }

    [Test]
    public async Task Ambiguous_session_binding_is_stale_and_has_no_effect()
    {
        await _factory.ResetAsync();
        using var workspace = new DecisionTempWorkspace();
        var (taskId, token) = await SeedAsync(workspace.Path);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            db.AgentTasks.Add(new AgentTask
            {
                Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "Competing task",
                Goal = "Bind the same session.", Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code, ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared, WorkingDirectory = workspace.Path,
                Status = AgentTaskStatus.Working, AgentSessionId = task.AgentSessionId,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        (await PostAsync(taskId, token, Sample())).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var verifyScope = _factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verify.AgentTaskDecisionQuestions.CountAsync(q => q.AgentTaskId == taskId)).ShouldBe(0);
    }

    private static InternalDecisionQuestionRequest Sample() => new(Guid.NewGuid(), 1,
        "repair", InternalDecisionCategory.ShellTransport, ["scripts/deploy.ps1"], null, null,
        InternalDecisionImpact.None, "May I repair quoting?", "Repair quoting only.",
        "The captured arguments stay the same.");

    private async Task SetStateAsync(Guid taskId, AgentTaskStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AgentTasks.SingleAsync(t => t.Id == taskId)).Status = status;
        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> PostAsync(Guid taskId, string? token, InternalDecisionQuestionRequest request) =>
        await PostRawAsync(taskId, token, JsonSerializer.Serialize(request, InternalDecisionPolicy.JsonOptions));

    private async Task<HttpResponseMessage> PostRawAsync(Guid taskId, string? token, string json)
    {
        using var client = _factory.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        return await client.PostAsync($"/api/agent-tasks/{taskId}/decision-questions",
            new StringContent(json, Encoding.UTF8, "application/json"));
    }

    private async Task<(Guid taskId, string token)> SeedAsync(string directory)
    {
        var taskId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var token = Guid.NewGuid().ToString("N");
        var policy = InternalDecisionPolicy.Normalize(new InternalDecisionPolicyRequest(1,
            [new InternalDecisionGrantRequest("repair", [InternalDecisionCategory.ShellTransport],
                ["scripts/deploy.ps1"], null, "Keep command arguments unchanged.")]),
            AgentTaskRole.Code, WorkspaceMode.Shared,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt)!;
        var policyJson = InternalDecisionPolicy.Serialize(policy);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId, DefinitionName = "test", AgentKind = AgentKind.Grok,
            Status = SessionStatus.Running, Cwd = directory,
            CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "API question fixture", Goal = "Repair quoting.",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code,
            ModelLevel = AgentModelLevel.High, Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory, Status = AgentTaskStatus.Working,
            AgentSessionId = sessionId, TokenHash = AgentTaskService.HashToken(token),
            InternalDecisionPolicyJson = policyJson, InternalDecisionPolicyHash = InternalDecisionPolicy.Hash(policyJson),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return (taskId, token);
    }
}
