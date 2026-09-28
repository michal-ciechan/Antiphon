using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class AgentTaskDecisionQuestionIntegrationTests
{
    [Test]
    public async Task Allowed_check_is_durable_idempotent_and_does_not_settle_or_enqueue()
    {
        using var workspace = new DecisionTempWorkspace();
        var (taskId, sessionId) = await SeedAsync(workspace.Path);
        var request = Sample();

        await using (var db = NewDb())
        {
            var service = new AgentTaskDecisionQuestionService(db);
            var caller = new AgentTaskService.Caller(await db.AgentTasks.SingleAsync(t => t.Id == taskId),
                sessionId, workspace.Path);
            var first = await service.CheckAsync(taskId, request, caller, CancellationToken.None);
            first.Disposition.ShouldBe(InternalDecisionDisposition.Continue);
            first.Reason.ShouldBe("dispatch_grant");
            var duplicate = await service.CheckAsync(taskId, request, caller, CancellationToken.None);
            duplicate.ShouldBe(first);
        }

        await using var verify = NewDb();
        (await verify.AgentTaskDecisionQuestions.CountAsync(q => q.AgentTaskId == taskId)).ShouldBe(1);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
            && e.Type == AgentTaskEventType.DecisionQuestion)).ShouldBe(1);
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Working);
        task.CompletedAt.ShouldBeNull();
        task.Result.ShouldBeNull();
        task.AutoContinuedAt.ShouldBeNull();
        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == sessionId)).ShouldBe(0);
    }

    [Test]
    public async Task Same_request_id_with_changed_content_conflicts_and_no_new_event()
    {
        using var workspace = new DecisionTempWorkspace();
        var (taskId, sessionId) = await SeedAsync(workspace.Path);
        var request = Sample();
        await using var db = NewDb();
        var caller = new AgentTaskService.Caller(await db.AgentTasks.SingleAsync(t => t.Id == taskId),
            sessionId, workspace.Path);
        var service = new AgentTaskDecisionQuestionService(db);
        await service.CheckAsync(taskId, request, caller, CancellationToken.None);
        await Should.ThrowAsync<ConflictException>(() => service.CheckAsync(taskId,
            request with { ProposedAction = "Change the backup target." }, caller, CancellationToken.None));
        (await db.AgentTaskDecisionQuestions.CountAsync(q => q.AgentTaskId == taskId)).ShouldBe(1);
    }

    private static InternalDecisionQuestionRequest Sample() => new(
        Guid.NewGuid(), 1, "backup-transport", InternalDecisionCategory.ShellTransport,
        ["scripts/deploy-gym-stat.ps1"], null, null, InternalDecisionImpact.None,
        "May I repair shell quoting?", "Repair only the quoting of the existing command.",
        "Captured arguments remain byte-for-byte the same in an isolated fixture.");

    private static AppDbContext NewDb() => new(TestDbFixture.CreateDbContextOptions());

    private static async Task<(Guid taskId, Guid sessionId)> SeedAsync(string root)
    {
        var id = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var policy = InternalDecisionPolicy.Normalize(InternalDecisionFixtures.Sample(),
            AgentTaskRole.Code, WorkspaceMode.Worktree,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt)!;
        var json = InternalDecisionPolicy.Serialize(policy);
        await using var db = NewDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId, DefinitionName = "test", AgentKind = AgentKind.Grok,
            Status = SessionStatus.Running, Cwd = root,
            CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "Question fixture", Goal = "Repair shell quoting.",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = root, WorktreePath = root,
            Status = AgentTaskStatus.Working, AgentSessionId = sessionId, Attempt = 1,
            InternalDecisionPolicyJson = json, InternalDecisionPolicyHash = InternalDecisionPolicy.Hash(json),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return (id, sessionId);
    }
}
