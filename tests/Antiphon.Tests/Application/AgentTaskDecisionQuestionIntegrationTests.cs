using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        var denied = request with { RequestId = Guid.NewGuid(), Impact = InternalDecisionImpact.Data };
        (await service.CheckAsync(taskId, denied, caller, CancellationToken.None))
            .Disposition.ShouldBe(InternalDecisionDisposition.NeedsHuman);
        var changedDenial = await Should.ThrowAsync<ConflictException>(() => service.CheckAsync(taskId,
            denied with { ProposedAction = "Change retention too." }, caller, CancellationToken.None));
        changedDenial.Code.ShouldBe("decision_question_changed");
        (await db.AgentTaskDecisionQuestions.CountAsync(q => q.AgentTaskId == taskId)).ShouldBe(2);
    }

    [Test]
    public async Task Idempotency_survives_independent_scopes_and_restart()
    {
        using var workspace = new DecisionTempWorkspace();
        var (taskId, sessionId) = await SeedAsync(workspace.Path);
        var request = Sample();
        async Task<InternalDecisionQuestionResponse> CheckAsync(InternalDecisionQuestionRequest input)
        {
            await using var db = NewDb();
            var caller = new AgentTaskService.Caller(await db.AgentTasks.SingleAsync(t => t.Id == taskId),
                sessionId, workspace.Path);
            return await new AgentTaskDecisionQuestionService(db).CheckAsync(taskId, input, caller, CancellationToken.None);
        }

        var parallel = await Task.WhenAll(CheckAsync(request), CheckAsync(request));
        parallel[0].ShouldBe(parallel[1]);
        var afterRestart = await CheckAsync(request with { Paths = [@"scripts\deploy-gym-stat.ps1"] });
        afterRestart.ShouldBe(parallel[0]);
        var changed = await Should.ThrowAsync<ConflictException>(() => CheckAsync(request with
        {
            ProposedAction = "Change the backup target.",
        }));
        changed.Code.ShouldBe("decision_question_changed");

        await using var verify = NewDb();
        (await verify.AgentTaskDecisionQuestions.CountAsync(q => q.AgentTaskId == taskId)).ShouldBe(1);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
            && e.Type == AgentTaskEventType.DecisionQuestion)).ShouldBe(1);

        var original = await verify.AgentTaskDecisionQuestions.SingleAsync(q => q.AgentTaskId == taskId);
        verify.AgentTaskDecisionQuestions.Add(new AgentTaskDecisionQuestion
        {
            Id = Guid.NewGuid(), AgentTaskId = taskId, Attempt = original.Attempt,
            AgentSessionId = sessionId, RequestId = original.RequestId,
            CanonicalPayloadJson = original.CanonicalPayloadJson, PayloadHash = original.PayloadHash,
            GrantId = original.GrantId, Disposition = original.Disposition,
            Reason = original.Reason, CreatedAt = DateTime.UtcNow,
        });
        var duplicate = await Should.ThrowAsync<DbUpdateException>(() => verify.SaveChangesAsync());
        (duplicate.InnerException as PostgresException)?.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Test]
    public async Task Later_attempt_rechecks_grants_and_never_replays_an_old_answer()
    {
        using var workspace = new DecisionTempWorkspace();
        var (taskId, sessionId) = await SeedAsync(workspace.Path);
        var request = Sample();
        await using (var db = NewDb())
        {
            var caller = new AgentTaskService.Caller(await db.AgentTasks.SingleAsync(t => t.Id == taskId),
                sessionId, workspace.Path);
            (await new AgentTaskDecisionQuestionService(db).CheckAsync(taskId, request, caller,
                CancellationToken.None)).Disposition.ShouldBe(InternalDecisionDisposition.Continue);
        }
        var newSession = Guid.NewGuid();
        await using (var db = NewDb())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            task.Attempt = 2;
            task.AgentSessionId = newSession;
            task.InternalDecisionPolicyJson = null;
            task.InternalDecisionPolicyHash = null;
            db.AgentSessions.Add(new AgentSession
            {
                Id = newSession, DefinitionName = "retry", AgentKind = AgentKind.Grok,
                Status = SessionStatus.Running, Cwd = workspace.Path,
                CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        await using (var db = NewDb())
        {
            var caller = new AgentTaskService.Caller(await db.AgentTasks.SingleAsync(t => t.Id == taskId),
                newSession, workspace.Path);
            await Should.ThrowAsync<ConflictException>(() => new AgentTaskDecisionQuestionService(db)
                .CheckAsync(taskId, request, caller, CancellationToken.None));
            var later = await new AgentTaskDecisionQuestionService(db).CheckAsync(taskId,
                request with { Attempt = 2 }, caller, CancellationToken.None);
            later.Disposition.ShouldBe(InternalDecisionDisposition.NeedsHuman);
            later.Reason.ShouldBe("no_grant");
            later.QuestionId.ShouldNotBe(Guid.Empty);
        }
        await using var verify = NewDb();
        (await verify.AgentTaskDecisionQuestions.CountAsync(q => q.AgentTaskId == taskId)).ShouldBe(2);
    }

    [Test]
    public async Task Decision_is_not_a_reply_or_report_and_publication_reads_committed_rows()
    {
        using var workspace = new DecisionTempWorkspace();
        var (taskId, sessionId) = await SeedAsync(workspace.Path);
        await using (var setup = NewDb())
        {
            var task = await setup.AgentTasks.SingleAsync(t => t.Id == taskId);
            task.StandingAuthority = "May continue after an explicit reply.";
            task.AutoContinueOnWait = true;
            await setup.SaveChangesAsync();
        }
        await using var beforeDb = NewDb();
        var before = await beforeDb.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        var bus = new CommittedQuestionBus(taskId);
        await using (var db = NewDb())
        {
            var caller = new AgentTaskService.Caller(await db.AgentTasks.SingleAsync(t => t.Id == taskId),
                sessionId, workspace.Path);
            var service = new AgentTaskDecisionQuestionService(db, bus);
            (await service.CheckAsync(taskId, Sample(), caller, CancellationToken.None))
                .Disposition.ShouldBe(InternalDecisionDisposition.Continue);
            (await service.CheckAsync(taskId, Sample() with { Impact = InternalDecisionImpact.Data },
                caller, CancellationToken.None)).Disposition.ShouldBe(InternalDecisionDisposition.NeedsHuman);
            bus.Publications.ShouldBe(2);
        }
        await using var verify = NewDb();
        var after = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        after.Status.ShouldBe(before.Status);
        after.Attempt.ShouldBe(before.Attempt);
        after.CompletedAt.ShouldBe(before.CompletedAt);
        after.Result.ShouldBe(before.Result);
        after.NextStage.ShouldBe(before.NextStage);
        after.NextHandoff.ShouldBe(before.NextHandoff);
        after.CompletionNoteDigest.ShouldBe(before.CompletionNoteDigest);
        after.ReportEvidence.ShouldBe(before.ReportEvidence);
        after.ReportNudgedAt.ShouldBe(before.ReportNudgedAt);
        after.ReportNudgedSequence.ShouldBe(before.ReportNudgedSequence);
        after.ReportNudgeMessageId.ShouldBe(before.ReportNudgeMessageId);
        after.AutoContinuedAt.ShouldBe(before.AutoContinuedAt);
        after.AutoContinueOnWait.ShouldBe(before.AutoContinueOnWait);
        after.StandingAuthority.ShouldBe(before.StandingAuthority);
        after.ConcurrencyToken.ShouldBe(before.ConcurrencyToken);
        (await verify.AgentTaskDecisionQuestions.CountAsync(q => q.AgentTaskId == taskId)).ShouldBe(2);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
            && e.Type == AgentTaskEventType.DecisionQuestion)).ShouldBe(2);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
            && (e.Type == AgentTaskEventType.Blocked || e.Type == AgentTaskEventType.Completed
                || e.Type == AgentTaskEventType.Replied))).ShouldBe(0);
        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == sessionId)).ShouldBe(0);
    }

    [Test]
    public async Task State_transition_committed_under_the_task_lock_precedes_the_check()
    {
        using var workspace = new DecisionTempWorkspace();
        var (taskId, sessionId) = await SeedAsync(workspace.Path);
        await using var writer = NewDb();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.AgentTasks.FromSqlInterpolated(
            $"SELECT * FROM \"AgentTasks\" WHERE \"Id\" = {taskId} FOR UPDATE").SingleAsync();
        var pending = Task.Run(async () =>
        {
            await using var db = NewDb();
            var caller = new AgentTaskService.Caller(await db.AgentTasks.AsNoTracking()
                .SingleAsync(t => t.Id == taskId), sessionId, workspace.Path);
            return await new AgentTaskDecisionQuestionService(db).CheckAsync(taskId, Sample(), caller,
                CancellationToken.None);
        });
        var task = await writer.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status = AgentTaskStatus.Canceled;
        await writer.SaveChangesAsync();
        await transaction.CommitAsync();
        await Should.ThrowAsync<ConflictException>(() => pending);
        await using var verify = NewDb();
        (await verify.AgentTaskDecisionQuestions.CountAsync(q => q.AgentTaskId == taskId)).ShouldBe(0);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
            && e.Type == AgentTaskEventType.DecisionQuestion)).ShouldBe(0);
    }

    [Test]
    public async Task Question_time_uses_the_stored_snapshot_not_a_repository_policy_file()
    {
        using var workspace = new DecisionTempWorkspace();
        var (taskId, sessionId) = await SeedAsync(workspace.Path);
        var expanded = InternalDecisionPolicy.Normalize(InternalDecisionFixtures.Sample(
                categories: [InternalDecisionCategory.ShellTransport], paths: ["tests/new-helper.cs"]),
            AgentTaskRole.Code, WorkspaceMode.Worktree,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt)!;
        File.WriteAllText(Path.Combine(workspace.Path, "internal-decision-policy.json"),
            InternalDecisionPolicy.Serialize(expanded));
        await using var db = NewDb();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        var originalHash = task.InternalDecisionPolicyHash;
        var caller = new AgentTaskService.Caller(task, sessionId, workspace.Path);
        var result = await new AgentTaskDecisionQuestionService(db).CheckAsync(taskId,
            Sample() with { Paths = ["tests/new-helper.cs"] }, caller, CancellationToken.None);
        result.Disposition.ShouldBe(InternalDecisionDisposition.NeedsHuman);
        result.Reason.ShouldBe("path_not_granted");
        await using var verify = NewDb();
        (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId))
            .InternalDecisionPolicyHash.ShouldBe(originalHash);
    }

    [Test]
    public async Task Decision_and_event_commit_together_after_a_database_failure()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new DecisionTempWorkspace();
        var (taskId, sessionId) = await SeedAsync(workspace.Path, schema.ConnectionString);
        await using (var setup = NewDb(schema.ConnectionString))
        {
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION card0407_fail_event() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."AgentTaskId"::text = TG_ARGV[0] THEN
                        RAISE EXCEPTION 'injected decision event failure';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                """);
            await setup.Database.ExecuteSqlRawAsync($"""
                CREATE TRIGGER card0407_fail_event_before_insert
                BEFORE INSERT ON "AgentTaskEvents"
                FOR EACH ROW EXECUTE FUNCTION card0407_fail_event('{taskId:D}');
                """);
        }

        var bus = new CommittedQuestionBus(taskId, schema.ConnectionString);
        var request = Sample();
        await using (var db = NewDb(schema.ConnectionString))
        {
            var caller = new AgentTaskService.Caller(await db.AgentTasks.SingleAsync(t => t.Id == taskId),
                sessionId, workspace.Path);
            await Should.ThrowAsync<DbUpdateException>(() => new AgentTaskDecisionQuestionService(db, bus)
                .CheckAsync(taskId, request, caller, CancellationToken.None));
        }
        bus.Publications.ShouldBe(0);
        await using (var verify = NewDb(schema.ConnectionString))
        {
            (await verify.AgentTaskDecisionQuestions.CountAsync(q => q.AgentTaskId == taskId)).ShouldBe(0);
            (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
                && e.Type == AgentTaskEventType.DecisionQuestion)).ShouldBe(0);
            await verify.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER card0407_fail_event_before_insert ON \"AgentTaskEvents\"");
            await verify.Database.ExecuteSqlRawAsync("DROP FUNCTION card0407_fail_event()");
        }
        await using (var db = NewDb(schema.ConnectionString))
        {
            var caller = new AgentTaskService.Caller(await db.AgentTasks.SingleAsync(t => t.Id == taskId),
                sessionId, workspace.Path);
            (await new AgentTaskDecisionQuestionService(db, bus).CheckAsync(taskId, request, caller,
                CancellationToken.None)).Disposition.ShouldBe(InternalDecisionDisposition.Continue);
        }
        bus.Publications.ShouldBe(1);
    }

    [Test]
    public async Task Competing_binding_cannot_commit_between_admission_and_decision()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new DecisionTempWorkspace();
        var (taskId, sessionId) = await SeedAsync(workspace.Path, schema.ConnectionString);
        var competitorId = Guid.NewGuid();
        await using (var setup = NewDb(schema.ConnectionString))
        {
            setup.AgentTasks.Add(new AgentTask
            {
                Id = competitorId, RootTaskId = competitorId, Title = "Queued competitor",
                Goal = "Wait for session.", Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.High, Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workspace.Path, Status = AgentTaskStatus.Queued,
                AgentSessionId = sessionId, CreatedAt = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();
        }
        var gate = new PauseDecisionInsertInterceptor();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var check = Task.Run(async () =>
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(schema.ConnectionString).AddInterceptors(gate).Options;
            await using var db = new AppDbContext(options);
            var caller = new AgentTaskService.Caller(await db.AgentTasks.AsNoTracking()
                .SingleAsync(t => t.Id == taskId), sessionId, workspace.Path);
            return await new AgentTaskDecisionQuestionService(db).CheckAsync(taskId, Sample(), caller,
                timeout.Token);
        });
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var writer = NewDb(schema.ConnectionString);
            // SET is session-local; keep EF's connection open across the following UPDATE.
            await writer.Database.OpenConnectionAsync();
            await writer.Database.ExecuteSqlRawAsync("SET lock_timeout = '500ms'");
            var blocked = await Should.ThrowAsync<PostgresException>(() =>
                writer.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE \"AgentTasks\" SET \"Status\" = {(int)AgentTaskStatus.Working} WHERE \"Id\" = {competitorId}"));
            blocked.SqlState.ShouldBe(PostgresErrorCodes.LockNotAvailable);
        }
        finally
        {
            gate.Resume.TrySetResult();
            await check.WaitAsync(TimeSpan.FromSeconds(15));
        }
        (await check).Disposition.ShouldBe(InternalDecisionDisposition.Continue);
        await using var verify = NewDb(schema.ConnectionString);
        (await verify.AgentTaskDecisionQuestions.CountAsync(q => q.AgentTaskId == taskId)).ShouldBe(1);
        (await verify.AgentTasks.SingleAsync(t => t.Id == competitorId)).Status.ShouldBe(AgentTaskStatus.Queued);
    }

    private sealed class PauseDecisionInsertInterceptor : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"AgentTaskDecisionQuestions\"", StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class CommittedQuestionBus(Guid taskId, string? connectionString = null) : IEventBus
    {
        public int Publications { get; private set; }

        public Task PublishToGroupAsync(string group, string eventName, object payload,
            CancellationToken ct = default) => throw new InvalidOperationException("Unexpected group publication.");

        public async Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default)
        {
            eventName.ShouldBe("AgentTaskChanged");
            await using var db = NewDb(connectionString);
            var count = await db.AgentTaskDecisionQuestions.CountAsync(q => q.AgentTaskId == taskId, ct);
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
                && e.Type == AgentTaskEventType.DecisionQuestion, ct)).ShouldBe(count);
            count.ShouldBe(Publications + 1);
            Publications++;
        }
    }

    private static InternalDecisionQuestionRequest Sample() => new(
        Guid.NewGuid(), 1, "backup-transport", InternalDecisionCategory.ShellTransport,
        ["scripts/deploy-gym-stat.ps1"], null, null, InternalDecisionImpact.None,
        "May I repair shell quoting?", "Repair only the quoting of the existing command.",
        "Captured arguments remain byte-for-byte the same in an isolated fixture.");

    private static AppDbContext NewDb(string? connectionString = null) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    private static async Task<(Guid taskId, Guid sessionId)> SeedAsync(string root, string? connectionString = null)
    {
        var id = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var policy = InternalDecisionPolicy.Normalize(InternalDecisionFixtures.Sample(),
            AgentTaskRole.Code, WorkspaceMode.Worktree,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt)!;
        var json = InternalDecisionPolicy.Serialize(policy);
        await using var db = NewDb(connectionString);
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
