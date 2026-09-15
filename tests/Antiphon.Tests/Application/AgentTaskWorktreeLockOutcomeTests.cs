using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskWorktreeLockOutcomeTests
{
    [Test] [Arguments(true, "modern")] [Arguments(false, "modern")]
    [Arguments(true, "inbox")] [Arguments(false, "inbox")]
    public Task C443_BusyRecipientGetsOutcomeWhenIdle(bool owners, string backend) => DeliverAsync(true, owners, backend);
    [Test] [Arguments(true, "modern")] [Arguments(false, "modern")]
    [Arguments(true, "inbox")] [Arguments(false, "inbox")]
    public Task C443_EligibleRecipientGetsOutcome(bool owners, string backend) => DeliverAsync(false, owners, backend);

    [Test] [Arguments(true, "modern")] [Arguments(false, "modern")]
    [Arguments(true, "inbox")] [Arguments(false, "inbox")]
    public Task C443_BusyRecipientGetsUnlandedSiblingWarning(bool owners, string backend) => DeliverAsync(true, owners, backend, sibling: true);
    [Test] [Arguments(true, "modern")] [Arguments(false, "modern")]
    [Arguments(true, "inbox")] [Arguments(false, "inbox")]
    public Task C443_EligibleRecipientGetsUnlandedSiblingWarning(bool owners, string backend) => DeliverAsync(false, owners, backend, sibling: true);

    private static async Task DeliverAsync(bool busy, bool owners, string backend, bool sibling = false)
    {
        BridgeQueueHarness? receiver = null;
        DeliveryWorkers? workers = null;
        WorktreeGuardedCleanupTests.RemovalHarness? producerHarness = null;
        string? siblingWarning = null;
        try
        {
            var h = producerHarness = await WorktreeGuardedCleanupTests.RemovalHarness.CreateAsync(async producer => {
                receiver = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false,
                    ConnectionString = producer.Schema.ConnectionString, ConfigureServices = services => {
                        services.AddSingleton<CompletionNoteFlushQueue>(); services.AddSingleton<SpecialistFailureQueue>();
                        services.AddSingleton(sp => new PtyDeliveryProfile(sp.GetRequiredService<IServiceScopeFactory>(),
                            NullLogger<PtyDeliveryProfile>.Instance, backendOverride: backend));
                        services.AddScoped<AgentTaskLandNotificationService>(); } });
                (await receiver.Provider.GetRequiredService<PtyDeliveryProfile>().RefreshAsync(default))
                    .SingleWriteMaxBytes.ShouldBe(backend == "modern" ? 86400 : 1024);
                producer.Messages = receiver.Queue;
                await using var db = producer.CreateContext();
                var task = await db.AgentTasks.SingleAsync(t => t.Id == producer.Fixture.TaskId);
                task.ParentSessionId = receiver.SessionId; task.ReplyTo = AgentTaskReplyTo.Session;
                await db.SaveChangesAsync();
                workers = new(receiver); await workers.StartAsync();
            });
            var bridge = receiver!;
            if (sibling) siblingWarning = await AddUnlandedSiblingAsync(h.H);
            // Successful admission does not create a notification; this cut precedes the Outcome.
            await using (var db = h.H.CreateContext())
                (await db.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == h.Context.TaskId)).ShouldBeFalse();
            var baselineSubmissions = bridge.Adapter.SubmittedBodies.Count;
            await using (var db = h.H.CreateContext())
                (await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == bridge.SessionId && m.Status == QueuedMessageStatus.Pending)).ShouldBeFalse();
            if (busy) await bridge.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "owned recipient is busy", timestamp: DateTime.UtcNow);
            h.Diagnostics.Owners = owners;
            h.Diagnostics.OwnerName = new string('n', 80); h.Diagnostics.OwnerPath = new string('p', 512);
            h.Probe.Code = null; // Captured failed cleanup, no retry nomination.
            var publication = (await h.H.OperationAsync())!;
            await h.H.RunAsync();
            WorktreeCleanupAttempt capture;
            AgentTaskLandNotification original;
            await using (var db = h.H.CreateContext())
            {
                capture = await db.WorktreeCleanupAttempts.AsNoTracking().SingleAsync(a => a.Id == h.Context.AttemptId);
                original = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.RequestId == h.Context.RequestId && n.Kind == LandNotificationKind.Outcome);
                capture.FinalizedAt.ShouldNotBeNull(); capture.TerminalEventId.ShouldBe(original.SourceEventId);
                var terminal = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == original.SourceEventId);
                if (siblingWarning is not null) terminal.Detail.ShouldContain(siblingWarning);
                System.Text.Encoding.UTF8.GetByteCount(original.Body).ShouldBeLessThanOrEqualTo(1024);
                original.ParentSessionId.ShouldBe(bridge.SessionId); original.Body.ShouldContain(capture.Id.ToString("N"));
                original.Body.ShouldContain(owners ? h.Diagnostics.OwnerName : "InsufficientPrivileges");
                capture.CaptureState.ShouldBe(WorktreeCleanupCaptureState.Captured);
                if (owners) capture.Summary!.Length.ShouldBe(600);
                var after = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == publication.Id);
                after.RemoteConfirmedAt.ShouldBe(publication.RemoteConfirmedAt); after.VerifiedSourceSha.ShouldBe(publication.VerifiedSourceSha);
            }
            await UntilAsync(async () => {
                await using var db = h.H.CreateContext();
                return await db.AgentTaskLandNotifications.AnyAsync(n => n.Id == original.Id && n.QueueMessageId != null);
            });
            if (busy)
            {
                bridge.Adapter.SubmittedBodies.Count.ShouldBe(baselineSubmissions);
                await using (var db = h.H.CreateContext())
                    (await db.TranscriptEntries.CountAsync(p => p.AgentSessionId == bridge.SessionId && p.Kind == TranscriptKinds.UserPrompt
                        && p.Text!.Contains(capture.Id.ToString("N")))).ShouldBe(0);
                await bridge.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn", timestamp: DateTime.UtcNow);
                // The standing completion worker must observe eligibility without a direct flush.
            }
            await UntilAsync(async () => {
                await using var db = h.H.CreateContext();
                return await db.AgentTaskLandNotifications.AnyAsync(n => n.Id == original.Id && n.State == LandNotificationState.Confirmed);
            });
            await using (var db = h.H.CreateContext())
            {
                var confirmed = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == original.Id);
                confirmed.Body.ShouldBe(original.Body); confirmed.ContentDigest.ShouldBe(original.ContentDigest);
                confirmed.ConfirmingPromptSequence.ShouldNotBeNull();
                var prompt = await db.TranscriptEntries.AsNoTracking().SingleAsync(p => p.AgentSessionId == bridge.SessionId
                    && p.Sequence == confirmed.ConfirmingPromptSequence && p.Kind == TranscriptKinds.UserPrompt);
                PromptSubmissionMatch.Normalize(prompt.Text!).ShouldBe(PromptSubmissionMatch.Normalize(original.Body));
                if (siblingWarning is not null) prompt.Text!.ShouldContain(siblingWarning);
                else prompt.Text!.ShouldNotContain("unlanded-sibling=");
                bridge.Adapter.SubmittedBodies.Skip(baselineSubmissions).ShouldBe([original.Body]);
                (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == original.Id)).ShouldBe(1);
                var queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == confirmed.QueueMessageId);
                queued.Body.ShouldBe(original.Body); queued.AgentSessionId.ShouldBe(bridge.SessionId);
            }
        }
        finally
        {
            if (workers is not null) await workers.DisposeAsync();
            if (receiver is not null) await receiver.DisposeAsync();
            if (producerHarness is not null) await producerHarness.DisposeAsync();
        }
    }

    private static async Task<string> AddUnlandedSiblingAsync(LandingSafetyHarness producer)
    {
        var id = Guid.NewGuid();
        var branch = $"feat/card-task-{DelegationReportFormatter.Short(id)}";
        var path = Path.Combine(producer.Fixture.Root, "trees", "sibling");
        await producer.Fixture.RequiredAsync(producer.Fixture.Repository, "worktree", "add", "-b", branch, path, producer.Fixture.SeedSha);
        await File.WriteAllTextAsync(Path.Combine(path, "unlanded-plan.md"), "same-card work remains unlanded\n");
        await producer.Fixture.RequiredAsync(path, "add", "unlanded-plan.md");
        await producer.Fixture.RequiredAsync(path, "commit", "-m", "unlanded sibling");
        await using var db = producer.CreateContext();
        var project = new Project { Id = Guid.NewGuid(), Name = "C443 sibling warning" };
        var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "C443 sibling warning" };
        var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Work" };
        var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
            Identifier = "CARD-0443", Title = "C443 sibling warning" };
        db.Projects.Add(project); db.Boards.Add(board); db.BoardColumns.Add(column); db.Cards.Add(card);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == producer.Fixture.TaskId);
        task.CardId = card.Id;
        db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, CardId = card.Id,
            Title = "Unlanded sibling", Goal = "retain plan", Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree, Status = AgentTaskStatus.Succeeded,
            WorkingDirectory = path, RepoPath = producer.Fixture.Repository, WorktreePath = path,
            WorktreeBranch = branch, ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return $"unlanded-sibling={DelegationReportFormatter.Short(id)}:{branch}";
    }

    private static async Task UntilAsync(Func<Task<bool>> predicate)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (!await predicate()) await Task.Delay(100, budget.Token);
    }

    private sealed class DeliveryWorkers(BridgeQueueHarness receiver) : IAsyncDisposable
    {
        private readonly AgentTaskLandNotificationHostedService _notes = new(receiver.Provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        private readonly CompletionNoteWorkHostedService _completion = new(receiver.Provider.GetRequiredService<IServiceScopeFactory>(),
            receiver.Provider.GetRequiredService<CompletionNoteFlushQueue>(), receiver.Provider.GetRequiredService<SpecialistFailureQueue>(),
            TimeProvider.System, NullLogger<CompletionNoteWorkHostedService>.Instance);
        public async Task StartAsync() { await _notes.StartAsync(default); await _completion.StartAsync(default); }
        public async ValueTask DisposeAsync()
        {
            await _notes.StopAsync(default); await _completion.StopAsync(default);
            _notes.Dispose(); _completion.Dispose();
        }
    }
}
