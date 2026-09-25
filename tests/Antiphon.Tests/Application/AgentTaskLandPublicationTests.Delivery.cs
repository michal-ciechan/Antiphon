using System.Collections.Immutable;
using System.Security.Cryptography;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class AgentTaskLandPublicationTests
{
    [Test]
    [Arguments("protected", false, "before-enqueue")]
    [Arguments("protected", true, "before-enqueue")]
    [Arguments("protected", false, "queue-inserted")]
    [Arguments("protected", true, "queue-inserted")]
    [Arguments("retained", false, "before-enqueue")]
    [Arguments("retained", true, "before-enqueue")]
    [Arguments("retained", false, "queue-inserted")]
    [Arguments("retained", true, "queue-inserted")]
    public async Task C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt(string detail, bool busy, string cut)
    {
        await using var h = new LandingSafetyHarness();
        // Round A still uses the retention seam. This double actually copies and verifies bytes;
        // production report-root policy is commissioned in Round B.
        var retainedRoot = Path.Combine(h.Fixture.Root, "retained-evidence");
        h.ConfigureServices = services => services.AddSingleton<IWorktreeEvidenceRetention>(new CopyingRetention(retainedRoot));
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fixture.Git.BeforeCommand = (_, args) => Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(
            args[0] == "worktree" && args[1] == "remove" ? new(128, "", "owned drop refusal") : null);
        await h.RunAsync();
        var first = (await h.OperationAsync()).ShouldNotBeNull();
        first.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        await using (var db = h.CreateContext())
            (await db.WorktreeCleanupAttempts.SingleAsync(a => a.OperationId == first.Id)).CaptureJson.ShouldNotBeNull();
        h.Fixture.Git.BeforeCommand = null;
        var relative = detail == "protected" ? ".claude/settings.local.json" : ".antiphon/task-0123abcd.md";
        var path = Path.Combine(h.Fixture.Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "retained caller evidence\n");
        await using var caller = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false, ConnectionString = h.Schema.ConnectionString,
        });
        if (busy) await caller.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "caller is mid-turn");
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.ReplyTo = AgentTaskReplyTo.Session;
            task.ParentSessionId = caller.SessionId;
            await db.SaveChangesAsync();
        }
        await h.RepostAsync();
        await h.RunAsync();
        var expected = detail == "protected" ? "protected: .claude/settings.local.json" : "retained=1 files at " + retainedRoot;
        AgentTaskLandNotification note;
        await using (var db = h.CreateContext())
        {
            note = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == h.Fixture.TaskId
                && n.ParentSessionId == caller.SessionId && n.Kind == LandNotificationKind.Outcome);
            var terminal = await db.AgentTaskEvents.SingleAsync(e => e.Id == note.SourceEventId);
            terminal.LandRequestId.ShouldBe(note.RequestId);
            terminal.LandingOperationId.ShouldBe(first.Id);
            terminal.Detail.ShouldContain(expected);
            note.Body.ShouldContain(expected);
            note.Body.ShouldContain("prior attempt request=");
            note.Body.ShouldContain(note.Id.ToString("N"));
            note.Body.ShouldContain(note.RequestId!.Value.ToString("N"));
            note.State.ShouldBe(LandNotificationState.Queued);
            note.ConfirmedAt.ShouldBeNull();
        }
        if (detail == "retained")
        {
            (await File.ReadAllTextAsync(Path.Combine(retainedRoot, relative))).ShouldBe("retained caller evidence\n");
            Directory.Exists(h.Fixture.Source).ShouldBeFalse();
        }
        else (await File.ReadAllTextAsync(path)).ShouldBe("retained caller evidence\n");

        var boundary = new EnqueueCut(cut);
        var flushes = new CompletionNoteFlushQueue();
        await ReconcileAsync(boundary);
        await using (var failed = h.CreateContext())
        {
            var saved = await failed.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id);
            saved.State.ShouldBe(LandNotificationState.RetryPending);
            saved.QueueMessageId.ShouldBeNull();
            saved.ConfirmedAt.ShouldBeNull();
            (await failed.SessionQueuedMessages.CountAsync(q => q.SourceLandNotificationId == note.Id))
                .ShouldBe(cut == "queue-inserted" ? 1 : 0);
        }
        await h.RestartServicesAsync();
        await ReconcileAsync(null);
        await using (var queued = h.CreateContext())
        {
            var saved = await queued.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id);
            var row = await queued.SessionQueuedMessages.SingleAsync(q => q.SourceLandNotificationId == note.Id);
            saved.QueueMessageId.ShouldBe(row.Id);
            saved.ConfirmedAt.ShouldBeNull();
            row.AgentSessionId.ShouldBe(caller.SessionId);
            row.Body.ShouldBe(note.Body);
            row.ContentDigest.ShouldBe(note.ContentDigest);
            if (cut == "queue-inserted") row.Id.ShouldBe(boundary.InsertedQueueId!.Value);
        }
        await caller.Queue.FlushIfIdleAsync(caller.SessionId, default);
        if (busy)
        {
            caller.Adapter.SubmittedBodies.ShouldBeEmpty();
            await ReconcileAsync(null);
            await using (var waiting = h.CreateContext())
                (await waiting.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id)).ConfirmedAt.ShouldBeNull();
            await caller.Queue.OnTurnEndAsync(caller.SessionId, default);
        }
        await ReconcileAsync(null);
        await using (var delivered = h.CreateContext())
        {
            var saved = await delivered.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id);
            var row = await delivered.SessionQueuedMessages.SingleAsync(q => q.SourceLandNotificationId == note.Id);
            saved.State.ShouldBe(LandNotificationState.Confirmed);
            saved.Body.ShouldBe(note.Body);
            row.DeliveryAttempts.ShouldBeGreaterThan(0);
            var prompts = await delivered.TranscriptEntries.Where(p => p.AgentSessionId == caller.SessionId
                && p.Kind == TranscriptKinds.UserPrompt && p.Text != null).ToListAsync();
            var prompt = prompts.Where(p => PromptSubmissionMatch.IsCompleteIn(note.Body, p.Text!)).ShouldHaveSingleItem();
            prompt.Sequence.ShouldBeGreaterThan(row.LastDeliveryBaselineSequence ?? 0);
            saved.ConfirmingPromptSequence.ShouldBe(prompt.Sequence);
            prompt.Text.ShouldContain(expected);
            PromptSubmissionMatch.IsCompleteIn(note.Body, caller.Adapter.SubmittedBodies.ShouldHaveSingleItem()).ShouldBeTrue();
        }
        await h.Fixture.AssertRemoteSourceAsync();

        async Task ReconcileAsync(LandDeliveryBoundary? fault)
        {
            await using var db = h.CreateContext();
            await new AgentTaskLandNotificationService(db, caller.Queue, flushes, caller.Runtime, TimeProvider.System, fault)
                .ReconcileAsync(note.Id, default);
        }
    }

    private sealed class CopyingRetention(string root) : IWorktreeEvidenceRetention
    {
        public async Task<WorktreeEvidenceRetentionResult> RetainAsync(AgentTask task, string worktreePath,
            ImmutableArray<string> relativePaths, Guid? attemptId, CancellationToken ct)
        {
            var files = ImmutableArray.CreateBuilder<RetainedWorktreeFile>();
            foreach (var relative in relativePaths)
            {
                var bytes = await File.ReadAllBytesAsync(Path.Combine(worktreePath, relative), ct);
                var target = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, bytes, ct);
                var copied = await File.ReadAllBytesAsync(target, ct);
                copied.ShouldBe(bytes);
                files.Add(new(relative, target, copied.Length, Convert.ToHexStringLower(SHA256.HashData(copied))));
            }
            return new(root, files.ToImmutable(), null);
        }
    }
}
