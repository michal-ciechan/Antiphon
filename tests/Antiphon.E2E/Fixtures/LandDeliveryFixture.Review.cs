using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Antiphon.E2E.Fixtures;

public sealed partial class LandDeliveryFixture
{
    public Guid ReviewTaskId { get; private set; }
    public string ReviewReportText { get; private set; } = "";

    public sealed record ReviewReceipt(
        AgentTaskLandNotification Note, string WireText, Guid EvidenceId, string ReviewedSha);

    public async Task StartReviewDelegateAsync(string form)
    {
        ReviewTaskId = Guid.NewGuid();
        var marker = DelegationReportFormatter.TaskMarker(ReviewTaskId);
        var finding = DelegationReportFormatter.FindingToken(ReviewTaskId, "clean");
        var pad = string.Equals(form, "spill", StringComparison.Ordinal)
            ? new string('x', 9000) + "\nSPILL-MARKER-C494\n" + new string('y', 9000) + "\n"
            : "";
        ReviewReportText = (pad
            + "Reviewed the owned change.\n"
            + finding + "\n"
            + "--- review evidence ---\n"
            + "subjectTaskId: " + TaskId.ToString("D") + "\n"
            + "reviewedSourceSha: " + SourceSha + "\n"
            + "ordinaryScopeCompleted: Full\n"
            + "--- next stage ---\n"
            + "next: land\n"
            + "handoff: land the reviewed source\n"
            + "[antiphon-report:" + DelegationReportFormatter.Short(ReviewTaskId) + " done]\n")
            .ReplaceLineEndings("\n");
        var reportPath = Path.Combine(Root, "review-assistant.txt");
        await File.WriteAllTextAsync(reportPath, ReviewReportText, new UTF8Encoding(false));

        var reviewDir = Path.Combine(Root, "review-agent");
        Directory.CreateDirectory(reviewDir);
        Guid agentId;
        await using (var db = CreateContext())
        {
            var agent = new Agent
            {
                Id = Guid.NewGuid(), Name = "C494 review provider", Slug = "c494-" + Guid.NewGuid().ToString("N"),
                Kind = AgentKind.Grok, WorkingDirectory = reviewDir, AlwaysOn = false, AutoCompactEnabled = false,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            };
            db.Agents.Add(agent);
            db.AgentTasks.Add(new AgentTask
            {
                Id = ReviewTaskId, RootTaskId = ReviewTaskId, Title = "C494 review", Goal = "Review the owned change.",
                Role = AgentTaskRole.Review, Stage = OrchestrationStage.Review, Kind = AgentTaskKind.Worker,
                AgentKind = AgentKind.Grok, Workspace = WorkspaceMode.ReadOnly, Status = AgentTaskStatus.Working,
                WorkingDirectory = reviewDir, RepoPath = Repository, ReplyTo = AgentTaskReplyTo.Session,
                ParentSessionId = CallerId, FollowUpOfTaskId = TaskId, VerificationProfileVersion = 1,
                VerificationRound = VerificationRound.Final, DispatchedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
                AgentId = agent.Id,
            });
            await db.SaveChangesAsync();
            agentId = agent.Id;
        }

        using var scope = _app.Services.CreateScope();
        var started = await scope.ServiceProvider.GetRequiredService<AgentControlService>().StartAsync(
            agentId, new StartAgentRequest(Prompt: marker + "\nReview the owned change.", IgnoreSubscriptionQuota: true),
            CancellationToken.None);
        var sessionId = Guid.Parse(started.PersistentSessionId!);
        await using (var db = CreateContext())
        {
            await db.AgentTasks.Where(t => t.Id == ReviewTaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AgentSessionId, sessionId));
        }
        await UntilAsync(
            () => Task.FromResult(File.Exists(Path.Combine(Root, "review-report-hold.held"))),
            "review provider held the marked prompt", 180);
    }

    public Task ReleaseReviewReportAsync() =>
        File.WriteAllTextAsync(Path.Combine(Root, "review-report-hold.release"), "release");

    public async Task<ReviewReceipt> WaitForReviewReceiptAsync(string form = "raw", int seconds = 180)
    {
        AgentTaskLandNotification? note = null;
        await UntilAsync(async () =>
        {
            await using var db = CreateContext();
            note = await db.AgentTaskLandNotifications.AsNoTracking().FirstOrDefaultAsync(n =>
                n.TaskId == ReviewTaskId && n.Kind == LandNotificationKind.TaskCompletion && n.ConfirmedAt != null);
            return note is not null;
        }, "complete native Review receipt", seconds);

        await using var observer = CreateContext();
        var stored = await observer.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note!.Id);
        var rendering = TaskCompletionNotification.TryReadDelivery(stored.CompletionDeliveryJson);
        rendering.ShouldNotBeNull();
        var wire = rendering.WireText;
        var row = await observer.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == stored.QueueMessageId);
        var prompt = await observer.TranscriptEntries.AsNoTracking().SingleAsync(p =>
            p.AgentSessionId == CallerId && p.Sequence == stored.ConfirmingPromptSequence);
        prompt.Kind.ShouldBe(TranscriptKinds.UserPrompt);
        PromptSubmissionMatch.IsCompleteIn(wire, prompt.Text!).ShouldBeTrue();
        PromptSubmissionMatch.IsConfirmedBy(wire, prompt.Text!).ShouldBeTrue();
        if (row.LastDeliveryBaselineSequence is long floor)
            prompt.Sequence.ShouldBeGreaterThan(floor);
        else
            (prompt.Timestamp >= row.LastDeliveryStartedAt).ShouldBeTrue();

        var match = Regex.Match(wire, @"review-evidence=([0-9a-f]{32}); subject=([0-9a-f]{32}); reviewed-sha=([0-9a-f]{40})");
        match.Success.ShouldBeTrue(wire);
        var evidenceId = Guid.Parse(match.Groups[1].Value);
        Guid.Parse(match.Groups[2].Value).ShouldBe(TaskId);
        var sha = match.Groups[3].Value;
        sha.ShouldBe(SourceSha);
        wire.ShouldContain("verification=Final");
        wire.ShouldContain("scope=Full");
        var outcome = await observer.StageOutcomes.AsNoTracking().SingleAsync(o => o.StageTaskId == ReviewTaskId);
        outcome.Id.ShouldBe(evidenceId);
        outcome.Outcome.ShouldBe(StageOutcomeKind.Clean);
        outcome.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Full);
        outcome.CommissionedRound.ShouldBe(VerificationRound.Final);
        outcome.ReviewedSourceSha.ShouldBe(SourceSha);
        outcome.SubjectTaskId.ShouldBe(TaskId);
        var snapshot = TaskCompletionNotification.TryReadSnapshot(stored.CompletionSnapshotJson);
        snapshot.ShouldNotBeNull();
        snapshot.RawSha256.ShouldBe(TaskCompletionNotification.Sha256(snapshot.RawResult));
        snapshot.StageOutcomeId.ShouldBe(evidenceId);
        if (form == "Apply")
        {
            snapshot.DistillRequested.ShouldBeTrue();
            snapshot.DistillMode.ShouldBe(OutputDistillerMode.Apply);
            wire.ShouldContain("review-evidence=" + evidenceId.ToString("N"));
        }

        if (ReviewReportText.Contains("SPILL-MARKER-C494", StringComparison.Ordinal))
        {
            snapshot.RawResult.ShouldContain("SPILL-MARKER-C494");
            prompt.Text!.ShouldNotContain("SPILL-MARKER-C494");
            var spilled = rendering.SpillPath ?? (await observer.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == ReviewTaskId)).ResultFilePath;
            spilled.ShouldNotBeNull();
            var bytes = await File.ReadAllBytesAsync(spilled);
            if (rendering.SpillSha256 is not null)
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant().ShouldBe(rendering.SpillSha256);
            else
                (await File.ReadAllTextAsync(spilled)).ShouldContain("SPILL-MARKER-C494");
        }

        var prompts = await observer.TranscriptEntries.AsNoTracking().Where(p =>
            p.AgentSessionId == CallerId && p.Kind == TranscriptKinds.UserPrompt && p.Text != null).ToListAsync();
        prompts.Count(p => p.Text!.Contains("review-evidence=" + evidenceId.ToString("N"))).ShouldBe(1);
        var native = Directory.GetFiles(Path.Combine(Root, "native"), "updates.jsonl", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines).Count(line => line.Contains("user_message_chunk", StringComparison.Ordinal)
                && line.Contains(evidenceId.ToString("N"), StringComparison.Ordinal));
        native.ShouldBe(1);
        return new ReviewReceipt(stored, wire, evidenceId, sha);
    }

    public async Task WaitForUnattemptedReviewQueueAsync()
    {
        await UntilAsync(async () =>
        {
            await using var db = CreateContext();
            var note = await db.AgentTaskLandNotifications.AsNoTracking().FirstOrDefaultAsync(n =>
                n.TaskId == ReviewTaskId && n.Kind == LandNotificationKind.TaskCompletion && n.QueueMessageId != null);
            if (note is null)
                return false;
            var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == note.QueueMessageId);
            row.DeliveryAttempts.ShouldBe(0);
            note.ConfirmedAt.ShouldBeNull();
            return true;
        }, "busy caller has the review completion queued and untyped", 180);
    }

    public async Task AssertReviewHeldAsync(string cut)
    {
        var barrier = cut switch
        {
            "settlement-before-save" => "settlement-before-save",
            "settlement-before-commit" => "settlement-before-commit",
            "queue-before-commit" => "queue-before-commit",
            "queue-inserted-before-outbox-link" => "queue-inserted",
            "queue-committed-before-wakeup" => "queue-committed-before-wakeup",
            "before-typing" => "queue-before-typing",
            "prompt-before-verdict" => "queue-before-verdict",
            "settlement-committed-before-enqueue" => "settlement-saved",
            _ => throw new ArgumentOutOfRangeException(nameof(cut)),
        };
        await UntilAsync(() => Task.FromResult(File.Exists(Path.Combine(Root, barrier + ".barrier.json"))), barrier, 180);
        await using var db = CreateContext();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == ReviewTaskId);
        if (cut is "settlement-before-save" or "settlement-before-commit")
        {
            task.Status.ShouldBe(AgentTaskStatus.Working);
            (await db.StageOutcomes.CountAsync(o => o.StageTaskId == ReviewTaskId)).ShouldBe(0);
            (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == ReviewTaskId)).ShouldBe(0);
            return;
        }

        task.Status.ShouldBe(AgentTaskStatus.Succeeded);
        (await db.StageOutcomes.CountAsync(o => o.StageTaskId == ReviewTaskId && o.Outcome == StageOutcomeKind.Clean)).ShouldBe(1);
        var note = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n =>
            n.TaskId == ReviewTaskId && n.Kind == LandNotificationKind.TaskCompletion);
        if (cut == "settlement-committed-before-enqueue")
        {
            note.QueueMessageId.ShouldBeNull();
            (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == note.Id)).ShouldBe(0);
            return;
        }

        if (cut == "queue-before-commit")
        {
            (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == note.Id)).ShouldBe(0);
            note.QueueMessageId.ShouldBeNull();
            return;
        }

        var queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceLandNotificationId == note.Id);
        if (cut == "queue-inserted-before-outbox-link")
        {
            note.QueueMessageId.ShouldBeNull();
            queued.DeliveryAttempts.ShouldBe(0);
            return;
        }

        note.QueueMessageId.ShouldBe(queued.Id);
        if (cut == "queue-committed-before-wakeup")
        {
            note.ConfirmedAt.ShouldBeNull();
            return;
        }

        if (cut == "before-typing")
        {
            queued.DeliveryAttempts.ShouldBeGreaterThan(0);
            note.ConfirmedAt.ShouldBeNull();
            (await db.TranscriptEntries.AnyAsync(p => p.AgentSessionId == CallerId && p.Kind == TranscriptKinds.UserPrompt
                && p.Text != null && p.Text.Contains("review-evidence="))).ShouldBeFalse();
            return;
        }

        note.ConfirmedAt.ShouldBeNull();
        (await db.TranscriptEntries.CountAsync(p => p.AgentSessionId == CallerId && p.Kind == TranscriptKinds.UserPrompt
            && p.Text != null && p.Text.Contains("review-evidence="))).ShouldBe(1);
    }

    public async Task SeedFalseReviewCandidateAsync(string arm)
    {
        await UntilAsync(() => Task.FromResult(File.Exists(Path.Combine(Root, "queue-before-typing.barrier.json"))),
            "paused before native Review typing", 180);
        Guid noteId;
        await using (var db = CreateContext())
        {
            var note = await db.AgentTaskLandNotifications.SingleAsync(n =>
                n.TaskId == ReviewTaskId && n.Kind == LandNotificationKind.TaskCompletion);
            noteId = note.Id;
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.SourceLandNotificationId == note.Id);
            var rendering = TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson);
            rendering.ShouldNotBeNull();
            row.Status.ShouldBe(QueuedMessageStatus.Sent);
            var wire = rendering.WireText;
            var floor = row.LastDeliveryBaselineSequence ?? 0;
            var seededId = Guid.NewGuid();
            if (arm == "old-time")
            {
                row.LastDeliveryBaselineSequence = null;
                row.LastDeliveryStartedAt = DateTime.UtcNow;
            }
            else if (arm == "old-sequence")
                row.LastDeliveryBaselineSequence = floor + 100;

            if (arm != "Sent-only")
            {
                var evidence = Regex.Match(wire, @"review-evidence=([0-9a-f]{32})").Groups[1].Value;
                var text = arm switch
                {
                    "wrong-id" => wire.Replace(evidence, new string('a', 32), StringComparison.Ordinal),
                    "wrong-sha" => wire.Replace(SourceSha, new string('b', 40), StringComparison.Ordinal),
                    "partial-middle" => wire.Length < 30 ? wire[..1] : string.Concat(wire.AsSpan(0, wire.Length / 3), wire.AsSpan(wire.Length * 2 / 3)),
                    _ => wire,
                };
                var sessionId = CallerId;
                if (arm == "wrong-session")
                    sessionId = (await db.AgentTasks.Where(t => t.Id == ReviewTaskId).Select(t => t.AgentSessionId).SingleAsync())!.Value;
                var timestamp = arm == "old-time" ? DateTime.UtcNow.AddHours(-2) : DateTime.UtcNow;
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = seededId, AgentSessionId = sessionId, Sequence = floor + 40,
                    Kind = arm == "QueuedUserPrompt" ? TranscriptKinds.QueuedUserPrompt : TranscriptKinds.UserPrompt,
                    Text = text, Timestamp = timestamp, CreatedAt = timestamp,
                });
            }

            await db.SaveChangesAsync();
            using (var scope = _app.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>()
                    .ReconcileAsync(noteId, CancellationToken.None);
            }
            (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == noteId)).ConfirmedAt.ShouldBeNull();
            if (arm is "old-time" or "old-sequence")
            {
                row.LastDeliveryBaselineSequence = floor;
                await db.SaveChangesAsync();
            }
            if (arm != "Sent-only")
                await db.TranscriptEntries.Where(t => t.Id == seededId).ExecuteDeleteAsync();
        }

        await using var after = CreateContext();
        var saved = await after.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == noteId);
        saved.ConfirmedAt.ShouldBeNull();
        saved.State.ShouldNotBe(LandNotificationState.Confirmed);
    }

    public async Task LandReviewedAsync(ReviewReceipt receipt)
    {
        var requestId = await RequestAsync(expectedSha: receipt.ReviewedSha, reviewEvidenceId: receipt.EvidenceId);
        await ReleaseExecutionAsync();
        var note = await ReceiptAsync(requestId: requestId, seconds: 180);
        await AssertRemoteAsync();
        await AssertOnePromptAsync(note);
        await using var db = CreateContext();
        var request = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
        request.ExpectedSourceSha.ShouldBe(receipt.ReviewedSha);
        request.ReviewEvidenceId.ShouldBe(receipt.EvidenceId);
        var operation = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.TaskId == TaskId);
        operation.ReviewedSourceSha.ShouldBe(receipt.ReviewedSha);
        operation.ReviewEvidenceId.ShouldBe(receipt.EvidenceId);
        operation.OriginalSourceSha.ShouldBe(receipt.ReviewedSha);
    }

    public async Task RefuseStaleReviewAsync(ReviewReceipt receipt)
    {
        var stale = (await GitAsync(Repository, "rev-parse", "HEAD")).Trim();
        stale.ShouldNotBe(receipt.ReviewedSha);
        var start = new System.Diagnostics.ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-File", Path.Combine(AntiphonAppFixture.FindRepositoryRoot(), "scripts", "delegate.ps1"),
                     "-Land", TaskId.ToString(), "-ExpectedSourceSha", stale, "-ReviewEvidenceId", receipt.EvidenceId.ToString("D"),
                 })
            start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_API"] = _address;
        start.Environment["ANTIPHON_TASK_TOKEN"] = _token;
        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        process.ExitCode.ShouldNotBe(0, stdout);
        var text = stdout + "\n" + stderr;
        text.ShouldContain("review_evidence_sha_mismatch");
        await File.WriteAllTextAsync(Path.Combine(Root, "stale-refusal.txt"),
            "expected=" + stale + " evidence=" + receipt.EvidenceId.ToString("D") + "\n" + text);
        await using var db = CreateContext();
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == TaskId)).ShouldBe(0);
        (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == TaskId)).ShouldBe(0);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == TaskId && n.Kind == LandNotificationKind.Outcome)).ShouldBe(0);
    }
}
