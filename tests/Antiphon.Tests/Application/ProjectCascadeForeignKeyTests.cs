using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("Board")]
public class ProjectCascadeForeignKeyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Nudge_audit_comments_are_deleted_only_with_their_board_or_project(bool projectDelete)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var graph = await SeedGraphAsync(db);
        foreach (var scope in graph.Scopes)
            AddNudge(db, scope, graph.Other.SessionId);
        await db.SaveChangesAsync();

        await DeleteAsync(db, graph.Doomed, projectDelete);

        await using var verify = CreateContext(schema);
        (await verify.ExpectationNudges.AnyAsync(n => n.Id == graph.Doomed.NudgeId)).ShouldBeFalse();
        (await verify.CardComments.AnyAsync(c => c.Id == graph.Doomed.CommentId)).ShouldBeFalse();
        await AssertSurvivorsAsync(verify, graph, projectDelete);
        foreach (var survivor in projectDelete ? new[] { graph.Other } : new[] { graph.Sibling, graph.Other })
        {
            var nudge = await verify.ExpectationNudges.SingleAsync(n => n.Id == survivor.NudgeId);
            nudge.AuditCommentId.ShouldBe(survivor.CommentId);
            nudge.Body.ShouldBe("Keep the audit evidence until its card is deleted.");
        }
        if (projectDelete)
            (await verify.ExpectationNudges.AnyAsync(n => n.Id == graph.Sibling.NudgeId)).ShouldBeFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Decision_questions_are_deleted_by_session_even_when_the_task_belongs_elsewhere(bool projectDelete)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var graph = await SeedGraphAsync(db);
        // Deliberately cross task/card ownership: the restrictive parent is the session.
        foreach (var scope in graph.Scopes)
            AddQuestion(db, scope, graph.Other.TaskId);
        await db.SaveChangesAsync();

        await DeleteAsync(db, graph.Doomed, projectDelete);

        await using var verify = CreateContext(schema);
        (await verify.AgentTaskDecisionQuestions.AnyAsync(q => q.Id == graph.Doomed.QuestionId)).ShouldBeFalse();
        (await verify.AgentSessions.AnyAsync(s => s.Id == graph.Doomed.SessionId)).ShouldBeFalse();
        await AssertSurvivorsAsync(verify, graph, projectDelete);
        foreach (var survivor in projectDelete ? new[] { graph.Other } : new[] { graph.Sibling, graph.Other })
        {
            var question = await verify.AgentTaskDecisionQuestions.SingleAsync(q => q.Id == survivor.QuestionId);
            question.AgentSessionId.ShouldBe(survivor.SessionId);
            question.AgentTaskId.ShouldBe(graph.Other.TaskId);
            question.Answer.ShouldBe("Keep this answer.");
        }
        if (projectDelete)
            (await verify.AgentTaskDecisionQuestions.AnyAsync(q => q.Id == graph.Sibling.QuestionId)).ShouldBeFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Later_failure_restores_nudges_or_questions_and_all_parents(bool projectDelete, bool question)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema, new FailBoardDelete());
        var graph = await SeedGraphAsync(db);
        foreach (var scope in graph.Scopes)
        {
            if (question) AddQuestion(db, scope, graph.Other.TaskId);
            else AddNudge(db, scope, graph.Other.SessionId);
        }
        await db.SaveChangesAsync();

        var error = await Should.ThrowAsync<InvalidOperationException>(() => DeleteAsync(db, graph.Doomed, projectDelete));
        error.Message.ShouldBe("CARD-1253: failure after cards and their dependents were deleted");

        await using var verify = CreateContext(schema);
        foreach (var scope in graph.Scopes)
        {
            (await verify.Projects.AnyAsync(p => p.Id == scope.ProjectId)).ShouldBeTrue();
            (await verify.Boards.AnyAsync(b => b.Id == scope.BoardId)).ShouldBeTrue();
            (await verify.CardComments.AnyAsync(c => c.Id == scope.CommentId)).ShouldBeTrue();
            (await verify.AgentSessions.AnyAsync(s => s.Id == scope.SessionId)).ShouldBeTrue();
            (await verify.Cards.SingleAsync(c => c.Id == scope.CardId)).OwnerSessionId.ShouldBe(scope.SessionId);
            (await verify.AgentTasks.SingleAsync(t => t.Id == scope.TaskId)).CardId.ShouldBe(scope.CardId);
            if (question)
                (await verify.AgentTaskDecisionQuestions.SingleAsync(q => q.Id == scope.QuestionId)).Answer.ShouldBe("Keep this answer.");
            else
                (await verify.ExpectationNudges.SingleAsync(n => n.Id == scope.NudgeId)).AuditCommentId.ShouldBe(scope.CommentId);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Restrictive_task_history_survives_while_card_cleanup_is_removed(bool projectDelete)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var graph = await SeedGraphAsync(db);
        var taskId = graph.Doomed.TaskId;
        var child = NewTask(graph.Other.CardId, graph.Other.ProjectId);
        child.ParentTaskId = taskId;
        db.AgentTasks.Add(child);
        var dispatch = new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = taskId, Type = AgentTaskEventType.Dispatched, At = DateTime.UtcNow };
        db.AgentTaskEvents.Add(dispatch);
        var warning = new AgentTaskDispatchWarningIntent { Id = Guid.NewGuid(), TaskId = taskId, DispatchEventId = dispatch.Id, NotificationId = Guid.NewGuid(), WarningKey = "base-observation-stale" };
        db.AgentTaskDispatchWarningIntents.Add(warning);
        var request = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = taskId };
        db.AgentTaskLandRequests.Add(request);
        var landing = new AgentTaskLanding { Id = Guid.NewGuid(), TaskId = taskId };
        db.AgentTaskLandings.Add(landing);
        var binding = new TaskWorktreeMirrorBinding { Id = Guid.NewGuid(), TaskId = taskId, PreparationId = Guid.NewGuid(), BindingId = Guid.NewGuid() };
        db.TaskWorktreeMirrorBindings.Add(binding);
        var retirement = new TaskWorktreeRetirement { Id = Guid.NewGuid(), TaskId = taskId };
        db.TaskWorktreeRetirements.Add(retirement);
        var execution = new VerificationExecution { Id = Guid.NewGuid(), TaskId = taskId, SourceLandingOperationId = landing.Id, SessionId = graph.Doomed.SessionId, BindingJson = "{}" };
        db.VerificationExecutions.Add(execution);
        var attempt = new WorktreeCleanupAttempt { Id = Guid.NewGuid(), TaskId = taskId, RequestId = request.Id, OperationId = landing.Id };
        db.WorktreeCleanupAttempts.Add(attempt);
        var revision = new CardRevision { Id = Guid.NewGuid(), CardId = graph.Doomed.CardId, RevisionNumber = 1, Kind = CardRevisionKind.Move, ToStatus = CardStatus.Done, CreatedAt = DateTime.UtcNow };
        db.CardRevisions.Add(revision);
        var cleanup = new CardWorktreeCleanup { Id = Guid.NewGuid(), CardId = graph.Doomed.CardId, DoneRevisionId = revision.Id };
        db.CardWorktreeCleanups.Add(cleanup);
        var target = new CardWorktreeCleanupTarget { Id = Guid.NewGuid(), CleanupId = cleanup.Id, TaskId = taskId, TaskAttempt = 1, WorkspaceIdentity = "c1253" };
        db.CardWorktreeCleanupTargets.Add(target);
        var endpoint = new CardWorktreeCleanupEndpoint { Id = Guid.NewGuid(), TargetId = target.Id, EndpointIdentity = "local" };
        db.CardWorktreeCleanupEndpoints.Add(endpoint);
        await db.SaveChangesAsync();

        await DeleteAsync(db, graph.Doomed, projectDelete);

        await using var verify = CreateContext(schema);
        (await verify.AgentTasks.SingleAsync(t => t.Id == child.Id)).ParentTaskId.ShouldBe(taskId);
        (await verify.AgentTaskDispatchWarningIntents.SingleAsync(w => w.Id == warning.Id)).TaskId.ShouldBe(taskId);
        (await verify.AgentTaskLandRequests.SingleAsync(r => r.Id == request.Id)).TaskId.ShouldBe(taskId);
        (await verify.AgentTaskLandings.SingleAsync(l => l.Id == landing.Id)).TaskId.ShouldBe(taskId);
        (await verify.TaskWorktreeMirrorBindings.SingleAsync(b => b.Id == binding.Id)).TaskId.ShouldBe(taskId);
        (await verify.TaskWorktreeRetirements.SingleAsync(r => r.Id == retirement.Id)).TaskId.ShouldBe(taskId);
        (await verify.VerificationExecutions.SingleAsync(e => e.Id == execution.Id)).TaskId.ShouldBe(taskId);
        (await verify.WorktreeCleanupAttempts.SingleAsync(a => a.Id == attempt.Id)).TaskId.ShouldBe(taskId);
        (await verify.CardRevisions.AnyAsync(r => r.Id == revision.Id)).ShouldBeFalse();
        (await verify.CardWorktreeCleanups.AnyAsync(c => c.Id == cleanup.Id)).ShouldBeFalse();
        (await verify.CardWorktreeCleanupTargets.AnyAsync(t => t.Id == target.Id)).ShouldBeFalse();
        (await verify.CardWorktreeCleanupEndpoints.AnyAsync(e => e.Id == endpoint.Id)).ShouldBeFalse();
        await AssertSurvivorsAsync(verify, graph, projectDelete);
    }

    [Test]
    public async Task Restrictive_foreign_key_inventory_requires_an_explicit_deletion_probe()
    {
        using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        string[] parents = ["Card", "CardComment", "CardRevision", "AgentSession", "AgentTask", "Board", "Project"];
        var inventory = db.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys())
            .Where(f => parents.Contains(f.PrincipalEntityType.ClrType.Name)
                && f.DeleteBehavior is DeleteBehavior.Restrict or DeleteBehavior.NoAction)
            .Select(f => $"{f.DeclaringEntityType.ClrType.Name}.{string.Join(",", f.Properties.Select(p => p.Name))}->{f.PrincipalEntityType.ClrType.Name}")
            .Order().ToArray();
        inventory.ShouldBe(new[]
        {
            "AgentTask.ParentTaskId->AgentTask",
            "AgentTaskDecisionQuestion.AgentSessionId->AgentSession",
            "AgentTaskDispatchWarningIntent.TaskId->AgentTask",
            "AgentTaskLandRequest.TaskId->AgentTask",
            "AgentTaskLanding.TaskId->AgentTask",
            "Board.ProjectId->Project",
            "CardWorktreeCleanup.CardId->Card",
            "CardWorktreeCleanup.DoneRevisionId->CardRevision",
            "CardWorktreeCleanupTarget.TaskId->AgentTask",
            "ExpectationNudge.AuditCommentId->CardComment",
            "TaskWorktreeMirrorBinding.TaskId->AgentTask",
            "TaskWorktreeRetirement.TaskId->AgentTask",
            "VerificationExecution.TaskId->AgentTask",
            "Workflow.ProjectId->Project",
            "WorktreeCleanupAttempt.TaskId->AgentTask"
        }.Order().ToArray());
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema, params IInterceptor[] interceptors) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString, interceptors));

    private static async Task<Graph> SeedGraphAsync(AppDbContext db)
    {
        var project = NewProject();
        var otherProject = NewProject();
        db.Projects.AddRange(project, otherProject);
        await db.SaveChangesAsync();
        var boards = new BoardService(db, new MockEventBus(), TimeProvider.System);
        var doomed = await SeedScopeAsync(db, await boards.CreateAsync(new CreateBoardRequest(project.Id, "Doomed"), CancellationToken.None));
        var sibling = await SeedScopeAsync(db, await boards.CreateAsync(new CreateBoardRequest(project.Id, "Sibling"), CancellationToken.None));
        var other = await SeedScopeAsync(db, await boards.CreateAsync(new CreateBoardRequest(otherProject.Id, "Other"), CancellationToken.None));
        return new Graph(doomed, sibling, other);
    }

    private static Project NewProject() => new()
    {
        Id = Guid.NewGuid(), Name = $"C1253-{Guid.NewGuid():N}", GitRepositoryUrl = "https://example.test/repo.git",
        BaseBranch = "main", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private static AgentTask NewTask(Guid cardId, Guid projectId)
    {
        var id = Guid.NewGuid();
        return new AgentTask { Id = id, RootTaskId = id, CardId = cardId, ProjectId = projectId,
            Title = "C1253 history", Goal = "Preserve task evidence", Status = AgentTaskStatus.Succeeded, Attempt = 1, CreatedAt = DateTime.UtcNow };
    }

    private static async Task<Scope> SeedScopeAsync(AppDbContext db, BoardDetailDto board)
    {
        var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = board.Columns.First(c => c.StateKey == "backlog").Id,
            Identifier = "CARD-1", Title = "Cascade probe", Status = CardStatus.Done, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Cards.Add(card);
        var session = new AgentSession { Id = Guid.NewGuid(), CardId = card.Id, DefinitionName = "fake", AgentKind = AgentKind.Raw,
            Status = SessionStatus.Stopped, Cwd = Path.GetTempPath(), CreatedAt = DateTime.UtcNow };
        db.AgentSessions.Add(session);
        var comment = new CardComment { Id = Guid.NewGuid(), CardId = card.Id, Body = "Audit comment", CreatedAt = DateTime.UtcNow };
        db.CardComments.Add(comment);
        var task = NewTask(card.Id, board.ProjectId);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        card.OwnerSessionId = session.Id;
        await db.SaveChangesAsync();
        return new Scope(board.ProjectId, board.Id, card.Id, comment.Id, session.Id, task.Id, Guid.NewGuid(), Guid.NewGuid());
    }

    private static void AddNudge(AppDbContext db, Scope scope, Guid destinationSessionId) => db.ExpectationNudges.Add(new ExpectationNudge
    {
        Id = scope.NudgeId, AuditCommentId = scope.CommentId, DirectiveId = scope.NudgeId.ToString("N"), Ordinal = 1,
        Body = "Keep the audit evidence until its card is deleted.", BodyDigest = new string('a', 64),
        DestinationSessionId = destinationSessionId, CreatedAt = DateTime.UtcNow
    });

    private static void AddQuestion(AppDbContext db, Scope scope, Guid taskId) => db.AgentTaskDecisionQuestions.Add(new AgentTaskDecisionQuestion
    {
        Id = scope.QuestionId, AgentSessionId = scope.SessionId, AgentTaskId = taskId, Attempt = 1,
        RequestId = Guid.NewGuid(), CanonicalPayloadJson = "{}", PayloadHash = new string('b', 64),
        Answer = "Keep this answer.", Reason = "Probe", CreatedAt = DateTime.UtcNow
    });

    private static async Task DeleteAsync(AppDbContext db, Scope scope, bool projectDelete)
    {
        if (projectDelete)
            await new ProjectService(db, new StubHttpClientFactory(), Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance)
                .DeleteAsync(scope.ProjectId, force: true, CancellationToken.None);
        else
            (await new BoardService(db, new MockEventBus(), TimeProvider.System).DeleteAsync(scope.BoardId, CancellationToken.None))
                .ProjectDeleted.ShouldBeFalse();
    }

    private static async Task AssertSurvivorsAsync(AppDbContext db, Graph graph, bool projectDelete)
    {
        (await db.Boards.AnyAsync(b => b.Id == graph.Doomed.BoardId)).ShouldBeFalse();
        (await db.Cards.AnyAsync(c => c.Id == graph.Doomed.CardId)).ShouldBeFalse();
        (await db.AgentTasks.SingleAsync(t => t.Id == graph.Doomed.TaskId)).CardId.ShouldBeNull();
        if (projectDelete)
        {
            (await db.Projects.AnyAsync(p => p.Id == graph.Doomed.ProjectId)).ShouldBeFalse();
            (await db.Cards.AnyAsync(c => c.Id == graph.Sibling.CardId)).ShouldBeFalse();
            (await db.AgentTasks.SingleAsync(t => t.Id == graph.Doomed.TaskId)).ProjectId.ShouldBeNull();
        }
        else
        {
            (await db.Projects.AnyAsync(p => p.Id == graph.Doomed.ProjectId)).ShouldBeTrue();
            (await db.Cards.SingleAsync(c => c.Id == graph.Sibling.CardId)).OwnerSessionId.ShouldBe(graph.Sibling.SessionId);
            (await db.AgentTasks.SingleAsync(t => t.Id == graph.Sibling.TaskId)).CardId.ShouldBe(graph.Sibling.CardId);
        }
        (await db.Projects.AnyAsync(p => p.Id == graph.Other.ProjectId)).ShouldBeTrue();
        (await db.Cards.SingleAsync(c => c.Id == graph.Other.CardId)).OwnerSessionId.ShouldBe(graph.Other.SessionId);
        (await db.AgentTasks.SingleAsync(t => t.Id == graph.Other.TaskId)).CardId.ShouldBe(graph.Other.CardId);
    }

    private sealed record Scope(Guid ProjectId, Guid BoardId, Guid CardId, Guid CommentId, Guid SessionId, Guid TaskId, Guid NudgeId, Guid QuestionId);
    private sealed record Graph(Scope Doomed, Scope Sibling, Scope Other)
    {
        public Scope[] Scopes => [Doomed, Sibling, Other];
    }

    private sealed class FailBoardDelete : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            command.CommandText.Contains("DELETE FROM \"Boards\"", StringComparison.Ordinal)
                ? throw new InvalidOperationException("CARD-1253: failure after cards and their dependents were deleted")
                : base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
