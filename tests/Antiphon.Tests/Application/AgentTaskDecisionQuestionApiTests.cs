using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
