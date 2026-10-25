using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel, ParallelLimiter<GitProcessSpawnLimit>, Category("Integration")]
public sealed class MatrixChildReceiptTests
{
    [Test]
    public Task A_blocked_commit_child_note_and_the_settled_tasks_completion_each_reach_the_parent_once_in_queue_order() => CommitReceipts(false);

    [Test]
    public Task A_blocked_commit_child_note_reaches_a_busy_parent_once_after_turn_end() => CommitReceipts(true);

    private static async Task CommitReceipts(bool busy)
    {
        await using var factory = new CommitEndpointWebAppFactory();
        using var repo = await SeedRepoAsync();
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, repo, AgentTaskRole.Commit);
        await EntryAsync(factory, seed.Parent, TranscriptKinds.UserPrompt, "Dispatch work.");
        var floor = busy ? 0 : await EntryAsync(factory, seed.Parent, TranscriptKinds.TurnEnd, null);
        var adapter = AdapterFor(factory, seed.Parent);
        var state = factory.Services.GetRequiredService<SessionStateStore>();
        (await state.IsWorkingAsync(seed.Parent, default)).ShouldBe(busy);
        await SettleAsync(factory, seed);
        var blocked = await BlockedNoteAsync(factory, seed, AgentTaskRole.Commit);
        SessionQueuedMessage completion;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            completion = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.AgentSessionId == seed.Parent && m.SourceLandNotificationId != null);
            completion.SourceTaskId.ShouldBe(seed.Task);
            completion.Sequence.ShouldBeGreaterThan(blocked.Sequence);
            completion.Body.ShouldNotBe(blocked.Body);
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == seed.Parent)).ShouldBe(2);
        }
        await PendingAsync(factory, blocked);
        await PendingAsync(factory, completion);
        adapter.SubmittedBodies.ShouldBeEmpty();
        var queue = factory.Services.GetRequiredService<SessionMessageQueueService>();
        if (busy)
        {
            await queue.FlushSessionAsync(seed.Parent, default);
            await PendingAsync(factory, blocked);
            await PendingAsync(factory, completion);
            adapter.SubmittedBodies.ShouldBeEmpty();
            floor = await EntryAsync(factory, seed.Parent, TranscriptKinds.TurnEnd, null);
            (await state.IsWorkingAsync(seed.Parent, default)).ShouldBeFalse();
            await queue.OnTurnEndAsync(seed.Parent, default);
        }
        else await queue.FlushSessionAsync(seed.Parent, default);
        var first = await ReceiptAsync(factory, blocked, floor);
        await PendingAsync(factory, completion);
        adapter.SubmittedBodies.ShouldBe(new[] { blocked.Body });
        var completionFloor = await FloorAsync(factory, seed.Parent);
        if (busy) await queue.OnTurnEndAsync(seed.Parent, default);
        else await queue.FlushSessionAsync(seed.Parent, default);
        var second = await ReceiptAsync(factory, completion, completionFloor);
        second.ShouldBeGreaterThan(first);
        adapter.SubmittedBodies.ShouldBe(new[] { blocked.Body, completion.Body });
        if (busy) await queue.OnTurnEndAsync(seed.Parent, default);
        else await queue.FlushSessionAsync(seed.Parent, default);
        await ReceiptAsync(factory, blocked, floor);
        await ReceiptAsync(factory, completion, completionFloor);
        adapter.SubmittedBodies.ShouldBe(new[] { blocked.Body, completion.Body });
        await NoPendingAsync(factory, seed.Parent);
    }

    [Test]
    public async Task A_committed_blocked_child_note_is_recovered_once_by_the_stranded_sweep()
    {
        await using var factory = new CommitEndpointWebAppFactory();
        using var repo = await SeedRepoAsync();
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, repo, AgentTaskRole.Commit);
        await EntryAsync(factory, seed.Parent, TranscriptKinds.UserPrompt, "Dispatch work.");
        var floor = await EntryAsync(factory, seed.Parent, TranscriptKinds.TurnEnd, null);
        var adapter = AdapterFor(factory, seed.Parent);
        factory.Boundary.InterruptedTask = seed.Task;
        await SettleAsync(factory, seed);
        factory.Boundary.Interrupted.ShouldBeTrue();
        var blocked = await BlockedNoteAsync(factory, seed, AgentTaskRole.Commit);
        await PendingAsync(factory, blocked);
        adapter.SubmittedBodies.ShouldBeEmpty();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AgentTasks.SingleAsync(t => t.Id == seed.Task)).Status.ShouldBe(AgentTaskStatus.Succeeded);
            var obligation = await db.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == seed.Task);
            obligation.QueueMessageId.ShouldBeNull();
            (await db.SessionQueuedMessages.AnyAsync(m => m.SourceLandNotificationId == obligation.Id)).ShouldBeFalse();
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == seed.Parent)).ShouldBe(1);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == blocked.Id);
            var verification = factory.Services.GetRequiredService<IOptions<SupervisionSettings>>().Value.DeliveryVerification;
            row.CreatedAt = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime.AddSeconds(-(verification.StrandedAgeSeconds + 1));
            await db.SaveChangesAsync();
        }
        var recovered = ActivatorUtilities.CreateInstance<SessionMessageQueueService>(factory.Services);
        (await recovered.FlushStrandedQueuesAsync(default)).ShouldBe(1);
        await ReceiptAsync(factory, blocked, floor);
        adapter.SubmittedBodies.ShouldBe(new[] { blocked.Body });
        (await recovered.FlushStrandedQueuesAsync(default)).ShouldBe(0);
        await ReceiptAsync(factory, blocked, floor);
        adapter.SubmittedBodies.ShouldBe(new[] { blocked.Body });
        await using var verify = factory.Services.CreateAsyncScope();
        var finalDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        (await finalDb.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == seed.Parent)).ShouldBe(1);
        (await finalDb.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == seed.Task)).QueueMessageId.ShouldBeNull();
    }

    [Test]
    public async Task A_blocked_merge_child_note_reaches_the_parent_once_as_a_complete_user_prompt()
    {
        await using var factory = new CommitEndpointWebAppFactory();
        using var repo = await SeedRepoAsync();
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, repo, AgentTaskRole.Merge);
        await EntryAsync(factory, seed.Parent, TranscriptKinds.UserPrompt, "Dispatch work.");
        var floor = await EntryAsync(factory, seed.Parent, TranscriptKinds.TurnEnd, null);
        var adapter = AdapterFor(factory, seed.Parent);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var conflicted = await db.AgentTasks.SingleAsync(t => t.Id == seed.Task);
            (await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateMergeTaskAsync(conflicted, ["a.cs"], default)).ShouldNotBeNull();
            await db.SaveChangesAsync();
        }
        var blocked = await BlockedNoteAsync(factory, seed, AgentTaskRole.Merge);
        await PendingAsync(factory, blocked);
        adapter.SubmittedBodies.ShouldBeEmpty();
        var queue = factory.Services.GetRequiredService<SessionMessageQueueService>();
        await queue.FlushSessionAsync(seed.Parent, default);
        await ReceiptAsync(factory, blocked, floor);
        adapter.SubmittedBodies.ShouldBe(new[] { blocked.Body });
        await queue.FlushSessionAsync(seed.Parent, default);
        await ReceiptAsync(factory, blocked, floor);
        adapter.SubmittedBodies.ShouldBe(new[] { blocked.Body });
        await NoPendingAsync(factory, seed.Parent);
        await using var verify = factory.Services.CreateAsyncScope();
        var finalDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var child = await finalDb.AgentTasks.SingleAsync(t => t.ParentTaskId == seed.Task);
        child.Status.ShouldBe(AgentTaskStatus.Blocked);
        child.AgentSessionId.ShouldBeNull();
        (await finalDb.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == seed.Parent)).ShouldBe(1);
        (await finalDb.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == seed.Task)).ShouldBeFalse();
    }

    private sealed record Seed(Guid Task, Guid Parent, Guid Worker);
    private static async Task<Seed> SeedAsync(CommitEndpointWebAppFactory factory, ScratchGitRepo repo, AgentTaskRole role)
    {
        await repo.AddBareOriginAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "assigned work");
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var id in new[] { seed.Parent, seed.Worker })
            db.AgentSessions.Add(new AgentSession { Id = id, Cwd = repo.Path, AgentKind = AgentKind.ClaudeCode,
                DefinitionName = "test-raw", Status = SessionStatus.Running, Cols = 120, Rows = 30,
                CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
        db.AgentTasks.Add(new AgentTask { Id = seed.Task, RootTaskId = seed.Task, ParentSessionId = seed.Parent,
            AgentSessionId = role == AgentTaskRole.Commit ? seed.Worker : null, Title = "matrix worker", Goal = "make the change",
            RepoPath = repo.Path, WorkingDirectory = repo.Path, Workspace = WorkspaceMode.Shared, Role = AgentTaskRole.Custom,
            AgentKind = AgentKind.ClaudeCode, Kind = AgentTaskKind.Worker,
            Status = role == AgentTaskRole.Commit ? AgentTaskStatus.Dispatched : AgentTaskStatus.Blocked,
            ReplyTo = AgentTaskReplyTo.Session, CommitOnSettle = CommitOnSettlePolicy.Agent,
            WorktreePath = role == AgentTaskRole.Merge ? repo.Path : null,
            WorktreeBranch = role == AgentTaskRole.Merge ? "feat/card-task-c097m-merge" : null,
            MergeTargetRef = role == AgentTaskRole.Merge ? "master" : null,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1), DispatchedAt = role == AgentTaskRole.Commit ? DateTime.UtcNow.AddMinutes(-1) : null });
        db.ComplexityChains.Add(new ComplexityChain { Id = Guid.NewGuid(), Role = role, Complexity = TaskComplexity.Medium,
            CandidatesJson = ComplexityChain.SerializeCandidates([new(AgentKind.ClaudeCode, AgentModelLevel.Frontier)]),
            Provenance = RoutingPinProvenance.Human, Reason = "matrix receipt", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<ModelAvailability>().UpsertManualAsync("ClaudeCode", "fable",
            DateTimeOffset.UtcNow.AddHours(4), "hold fable", default);
        return seed;
    }

    private static async Task SettleAsync(CommitEndpointWebAppFactory factory, Seed seed)
    {
        await EntryAsync(factory, seed.Worker, TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(seed.Task));
        await EntryAsync(factory, seed.Worker, TranscriptKinds.AssistantText, "Assigned work complete.\n" + DelegationReportFormatter.ReportToken(seed.Task, "done"));
        await EntryAsync(factory, seed.Worker, TranscriptKinds.TurnEnd, null);
        await factory.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(seed.Worker, default);
    }

    private static async Task<SessionQueuedMessage> BlockedNoteAsync(CommitEndpointWebAppFactory factory, Seed seed, AgentTaskRole role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var child = await db.AgentTasks.SingleAsync(t => t.ParentTaskId == seed.Task);
        child.ParentSessionId.ShouldBe(seed.Parent);
        child.Role.ShouldBe(role);
        child.Status.ShouldBe(AgentTaskStatus.Blocked);
        child.Complexity.ShouldBe(TaskComplexity.Medium);
        child.RoutingPinId.ShouldBeNull();
        child.FailureReason.ShouldStartWith("routing exhausted: ");
        child.FailureReason.ShouldContain($"{role}/Medium chain");
        child.FailureReason.ShouldContain("fable");
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == child.Id && e.Type == AgentTaskEventType.Blocked)).ShouldBe(1);
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceTaskId == child.Id);
        row.AgentSessionId.ShouldBe(seed.Parent);
        row.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        row.ConversationKey.ShouldBeNull();
        row.SourceLandNotificationId.ShouldBeNull();
        row.NoteHeader.ShouldNotBeNull();
        row.ContentDigest.ShouldBe(DelegationNoteDigest.Compute(child.FailureReason));
        row.Body.ShouldContain(row.NoteHeader);
        row.Body.ShouldContain(child.FailureReason);
        return row;
    }

    private static async Task PendingAsync(CommitEndpointWebAppFactory factory, SessionQueuedMessage expected)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == expected.Id);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        row.DeliveryAttempts.ShouldBe(0);
        row.LastDeliveryBaselineSequence.ShouldBeNull();
        row.Body.ShouldBe(expected.Body);
        (await db.TranscriptEntries.CountAsync(p => p.AgentSessionId == expected.AgentSessionId && p.Kind == TranscriptKinds.UserPrompt && p.Text == expected.Body)).ShouldBe(0);
    }

    private static async Task<long> ReceiptAsync(CommitEndpointWebAppFactory factory, SessionQueuedMessage expected, long floor)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == expected.Id);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.Body.ShouldBe(expected.Body);
        row.Sequence.ShouldBe(expected.Sequence);
        row.SourceTaskId.ShouldBe(expected.SourceTaskId);
        row.Origin.ShouldBe(expected.Origin);
        row.ConversationKey.ShouldBe(expected.ConversationKey);
        row.SourceLandNotificationId.ShouldBe(expected.SourceLandNotificationId);
        row.NoteHeader.ShouldBe(expected.NoteHeader);
        row.ContentDigest.ShouldBe(expected.ContentDigest);
        row.DeliveryAttempts.ShouldBe(1);
        row.LastDeliveryBaselineSequence.ShouldBe(floor);
        var receipt = await db.TranscriptEntries.SingleAsync(p => p.AgentSessionId == row.AgentSessionId
            && p.Kind == TranscriptKinds.UserPrompt && p.Sequence > floor && p.Text == expected.Body);
        receipt.Text.ShouldBe(expected.Body);
        (await db.TranscriptEntries.CountAsync(p => p.AgentSessionId == row.AgentSessionId && p.Kind == TranscriptKinds.UserPrompt && p.Text == expected.Body)).ShouldBe(1);
        return receipt.Sequence;
    }

    private static async Task NoPendingAsync(CommitEndpointWebAppFactory factory, Guid session)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().SessionQueuedMessages.CountAsync(m => m.AgentSessionId == session && m.Status == QueuedMessageStatus.Pending)).ShouldBe(0);
    }
    private static async Task<long> FloorAsync(CommitEndpointWebAppFactory factory, Guid session)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().TranscriptEntries.Where(t => t.AgentSessionId == session).MaxAsync(t => t.Sequence);
    }
    private static async Task<ScratchGitRepo> SeedRepoAsync()
    {
        var repo = new ScratchGitRepo("c097m-receipt");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, ".gitignore"), "*.secret\n");
        await repo.CommitFileAsync("tracked.txt", "t\n");
        return repo;
    }
    private static FakeAgentProtocolAdapter AdapterFor(CommitEndpointWebAppFactory factory, Guid session)
    {
        var adapter = new FakeAgentProtocolAdapter { OnSubmitted = async body =>
        {
            await EntryAsync(factory, session, TranscriptKinds.UserPrompt, body);
            await EntryAsync(factory, session, TranscriptKinds.TurnEnd, null);
        }};
        using var scope = factory.Services.CreateScope();
        adapter.EnableClaudeDialogObservation(
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!, session);
        factory.Services.GetRequiredService<AgentSessionRuntime>().Register(session, adapter);
        return adapter;
    }
    private static async Task<long> EntryAsync(CommitEndpointWebAppFactory factory, Guid session, string kind, string? body)
    {
        await using var mutation = await factory.Services.GetRequiredService<SessionStateStore>().BeginMutationAsync([session], default);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sequence = (await db.TranscriptEntries.Where(t => t.AgentSessionId == session).MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
        db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = session, Sequence = sequence,
            Kind = kind, Text = body, Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await mutation.PublishCommittedAsync(default);
        return sequence;
    }
}
