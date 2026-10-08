using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Antiphon.Tests.Application;

public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    public async Task C1150_Concurrent_producers_ensure_one_brief()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(schema.ConnectionString, "Keep the single brief.");
        try
        {
            await using var normal = await OpenQueueAsync(schema.ConnectionString, seeded);
            await using var recoveryA = await OpenQueueAsync(schema.ConnectionString);
            await using var recoveryB = await OpenQueueAsync(schema.ConnectionString);
            var request = RequestFor(seeded);
            // A blocking Barrier on the test thread never reaches the other two
            // producers. This gate releases all three only after each is awaiting it.
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task Produce(SessionMessageQueueService queue)
            {
                await release.Task.ConfigureAwait(false);
                await queue.EnsureDispatchBriefAsync(request, CancellationToken.None).ConfigureAwait(false);
            }

            var producers = new[]
            {
                Produce(normal.Queue),
                Produce(recoveryA.Queue),
                Produce(recoveryB.Queue),
            };
            release.SetResult();
            await Task.WhenAll(producers);

            var first = await SingleBriefAsync(schema.ConnectionString, seeded.TaskId, "V-6");
            var marker = DelegationReportFormatter.TaskMarker(seeded.TaskId);
            first.Body.Contains(marker, StringComparison.Ordinal).ShouldBeTrue("V-6");
            var payload = AuthoritativeBrief(first);
            payload.Contains(marker, StringComparison.Ordinal).ShouldBeTrue("V-6");
            payload.Contains(seeded.Goal, StringComparison.Ordinal).ShouldBeTrue("V-6");
            var spillPath = DispatchBriefEvidence.AbsoluteSpillPath(first.Body);
            var spillBefore = spillPath is not null && File.Exists(spillPath)
                ? await File.ReadAllBytesAsync(spillPath)
                : null;
            if (await UserPromptCountAsync(schema.ConnectionString, seeded.SessionId) == 0)
                await normal.Queue.FlushSessionAsync(seeded.SessionId, CancellationToken.None);
            var prompts = await UserPromptsAsync(schema.ConnectionString, seeded.SessionId);
            prompts.Count.ShouldBe(1, "V-6");
            prompts[0].Contains(marker, StringComparison.Ordinal).ShouldBeTrue("V-6");
            PromptSubmissionMatch.IsCompleteIn(first.Body, prompts[0]).ShouldBeTrue("V-6");

            await using (var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                (await verify.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).Status
                    .ShouldBe(SessionStatus.Running, "V-6");
                var task = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
                task.Status.ShouldBe(AgentTaskStatus.Dispatched, "V-6");
                task.Attempt.ShouldBe(1, "V-6");
            }

            await normal.DisposeAsync();
            await recoveryA.DisposeAsync();
            await recoveryB.DisposeAsync();
            await using var again = await OpenQueueAsync(schema.ConnectionString, seeded);
            var repeat = await again.Queue.EnsureDispatchBriefAsync(request, CancellationToken.None);
            repeat.Inserted.ShouldBeFalse("V-6");
            var retained = await SingleBriefAsync(schema.ConnectionString, seeded.TaskId, "V-6");
            retained.Id.ShouldBe(first.Id, "V-6");
            Encoding.UTF8.GetBytes(retained.Body).ShouldBe(Encoding.UTF8.GetBytes(first.Body), "V-6");
            if (spillBefore is not null && spillPath is not null)
                (await File.ReadAllBytesAsync(spillPath)).ShouldBe(spillBefore, "V-6");
            (await UserPromptCountAsync(schema.ConnectionString, seeded.SessionId)).ShouldBe(1, "V-6");
        }
        finally
        {
            if (Directory.Exists(seeded.Directory))
                Directory.Delete(seeded.Directory, true);
        }
    }

    [Test]
    [Arguments("inline")]
    [Arguments("local-spill")]
    [Arguments("remote-spill")]
    public async Task C1150_Existing_brief_and_spill_are_byte_identical(string shape)
    {
        const string goal = "Keep this goal exact.\nLine two café \u2603.";
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(schema.ConnectionString, goal);
        try
        {
            var captured = await SeedRetainedBriefAsync(schema.ConnectionString, seeded, shape);
            await using var queue = await OpenQueueAsync(schema.ConnectionString);
            var result = await queue.Queue.EnsureDispatchBriefAsync(RequestFor(seeded), CancellationToken.None);
            result.Inserted.ShouldBeFalse(shape);
            result.Kind.ShouldBe(DispatchBriefKind.Reuse, shape);
            await AssertRetainedAsync(schema.ConnectionString, seeded, captured, shape);
            (await UserPromptCountAsync(schema.ConnectionString, seeded.SessionId)).ShouldBe(0, shape);
        }
        finally
        {
            if (Directory.Exists(seeded.Directory))
                Directory.Delete(seeded.Directory, true);
        }
    }

    [Test]
    [Arguments("canceled")]
    [Arguments("attempted")]
    [Arguments("marker-only")]
    [Arguments("damaged-spill")]
    public async Task C1150_Uncertain_evidence_cannot_create_a_replacement(string shape)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(schema.ConnectionString, "Do not replace this goal.");
        try
        {
            await using var queue = await OpenQueueAsync(schema.ConnectionString);
            await AssertReceiptCompanionAsync(schema.ConnectionString, queue.Queue);
            byte[]? beforeBody = null;
            byte[]? beforeSpill = null;
            Guid? existingId = null;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                if (shape == "marker-only")
                {
                    db.TranscriptEntries.Add(Prompt(seeded, DelegationReportFormatter.TaskMarker(seeded.TaskId), 1));
                }
                else
                {
                    var row = HealthyRow(seeded, seeded.Goal);
                    if (shape == "canceled")
                    {
                        row.Status = QueuedMessageStatus.Canceled;
                        row.CanceledAt = seeded.DispatchedAt;
                    }
                    else if (shape == "attempted")
                    {
                        row.DeliveryAttempts = 2;
                        row.LastDeliveryStartedAt = seeded.DispatchedAt;
                    }
                    else
                    {
                        row.RemoteSpillRelativePath = ".antiphon/inbox/damaged.md";
                        row.RemoteSpillBody = DelegationReportFormatter.TaskMarker(seeded.TaskId) + "\nretained-canary";
                        row.Body = DelegationReportFormatter.TaskMarker(seeded.TaskId) + "\n.antiphon/inbox/damaged.md";
                    }

                    db.SessionQueuedMessages.Add(row);
                    existingId = row.Id;
                    beforeBody = Encoding.UTF8.GetBytes(row.Body);
                    beforeSpill = row.RemoteSpillBody is null ? null : Encoding.UTF8.GetBytes(row.RemoteSpillBody);
                }

                await db.SaveChangesAsync();
            }

            var result = await queue.Queue.EnsureDispatchBriefAsync(RequestFor(seeded), CancellationToken.None);
            result.Inserted.ShouldBeFalse(shape);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var task = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
            var rows = await verify.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId
                || m.AgentSessionId == seeded.SessionId).ToListAsync();
            (await UserPromptCountAsync(schema.ConnectionString, seeded.SessionId))
                .ShouldBe(shape == "marker-only" ? 1 : 0, shape);
            if (shape == "attempted")
            {
                result.Kind.ShouldBe(DispatchBriefKind.AttemptOwned, shape);
                task.Status.ShouldBe(AgentTaskStatus.Dispatched, shape);
                task.FailureReason.ShouldBeNull(shape);
                rows.Count.ShouldBe(1, shape);
                rows[0].DeliveryAttempts.ShouldBe(2, shape);
                (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId
                    && e.Type == AgentTaskEventType.Blocked)).ShouldBe(0, shape);
            }
            else if (shape == "marker-only")
            {
                result.Kind.ShouldBe(DispatchBriefKind.Uncertain, shape);
                task.Status.ShouldBe(AgentTaskStatus.Blocked, shape);
                task.FailureReason.ShouldBe(DispatchBriefEvidence.EvidenceUncertainReason, shape);
                task.FailureReason!.Contains("delivered", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(shape);
                rows.Count.ShouldBe(0, shape);
            }
            else if (shape == "canceled")
            {
                result.Kind.ShouldBe(DispatchBriefKind.Uncertain, shape);
                task.Status.ShouldBe(AgentTaskStatus.Blocked, shape);
                task.FailureReason.ShouldBe(DispatchBriefEvidence.EvidenceUncertainReason, shape);
                task.FailureReason!.Contains("delivered", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(shape);
                rows.Count.ShouldBe(1, shape);
            }
            else
            {
                result.Kind.ShouldBe(DispatchBriefKind.Unavailable, shape);
                task.Status.ShouldBe(AgentTaskStatus.Blocked, shape);
                task.FailureReason!.StartsWith("dispatch_brief_input_unavailable", StringComparison.Ordinal)
                    .ShouldBeTrue(shape);
                task.FailureReason.Contains("delivered", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(shape);
                rows.Count.ShouldBe(1, shape);
            }

            if (existingId is Guid id)
            {
                var retained = rows.Single(m => m.Id == id);
                Encoding.UTF8.GetBytes(retained.Body).ShouldBe(beforeBody, shape);
                (retained.RemoteSpillBody is null ? null : Encoding.UTF8.GetBytes(retained.RemoteSpillBody))
                    .ShouldBe(beforeSpill, shape);
            }
        }
        finally
        {
            if (Directory.Exists(seeded.Directory))
                Directory.Delete(seeded.Directory, true);
        }
    }

    [Test]
    public async Task C1150_Old_attempt_evidence_does_not_suppress_current_brief()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(schema.ConnectionString, "Current attempt still needs its brief.");
        try
        {
            Guid oldSessionId;
            Guid oldRowId;
            Guid staleSameSessionId;
            byte[] oldBody;
            byte[] staleBody;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                oldSessionId = Guid.NewGuid();
                db.AgentSessions.Add(SessionRow(oldSessionId, seeded.Directory, seeded.StartedAt.AddHours(-1)));
                var oldRow = HealthyRow(seeded, seeded.Goal);
                oldRow.AgentSessionId = oldSessionId;
                oldRow.CreatedAt = seeded.DispatchedAt.AddSeconds(1);
                oldRowId = oldRow.Id;
                oldBody = Encoding.UTF8.GetBytes(oldRow.Body);
                var stale = HealthyRow(seeded, seeded.Goal);
                stale.CreatedAt = seeded.DispatchedAt.AddMinutes(-30);
                staleSameSessionId = stale.Id;
                staleBody = Encoding.UTF8.GetBytes(stale.Body);
                db.SessionQueuedMessages.AddRange(oldRow, stale);
                db.TranscriptEntries.Add(Prompt(seeded, oldRow.Body, 1, seeded.SessionId, seeded.DispatchedAt.AddMinutes(-30)));
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = oldSessionId,
                    Kind = TranscriptKinds.UserPrompt,
                    Sequence = 1,
                    Text = oldRow.Body,
                    Timestamp = seeded.DispatchedAt,
                    CreatedAt = seeded.DispatchedAt,
                });
                await db.SaveChangesAsync();
            }

            await using var queue = await OpenQueueAsync(schema.ConnectionString);
            var first = await queue.Queue.EnsureDispatchBriefAsync(RequestFor(seeded), CancellationToken.None);
            first.Inserted.ShouldBeTrue("V-9");
            first.Kind.ShouldBe(DispatchBriefKind.Absent, "V-9");
            SessionQueuedMessage current;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var rows = await db.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId).ToListAsync();
                rows.Count.ShouldBe(3, "V-9");
                current = rows.Single(m => m.Id != oldRowId && m.Id != staleSameSessionId);
                current.AgentSessionId.ShouldBe(seeded.SessionId, "V-9");
                current.CreatedAt.ShouldBeGreaterThanOrEqualTo(seeded.DispatchedAt, "V-9");
                current.Body += "retained-canary";
                await db.SaveChangesAsync();
            }

            byte[] canary;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
                canary = Encoding.UTF8.GetBytes((await db.SessionQueuedMessages.SingleAsync(m => m.Id == current.Id)).Body);

            queue.Queue.BeforeDispatchBriefRowLock = async ct =>
            {
                await using var other = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                var task = await other.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId, ct);
                task.Attempt = 2;
                await other.SaveChangesAsync(ct);
            };
            try
            {
                var second = await queue.Queue.EnsureDispatchBriefAsync(RequestFor(seeded), CancellationToken.None);
                second.Kind.ShouldBe(DispatchBriefKind.Superseded, "V-9");
                second.Inserted.ShouldBeFalse("V-9");
            }
            finally
            {
                queue.Queue.BeforeDispatchBriefRowLock = null;
            }

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var after = await verify.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId).ToListAsync();
            after.Count.ShouldBe(3, "V-9");
            Encoding.UTF8.GetBytes(after.Single(m => m.Id == current.Id).Body).ShouldBe(canary, "V-9");
            Encoding.UTF8.GetBytes(after.Single(m => m.Id == oldRowId).Body).ShouldBe(oldBody, "V-9");
            Encoding.UTF8.GetBytes(after.Single(m => m.Id == staleSameSessionId).Body).ShouldBe(staleBody, "V-9");
            (await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId)).Attempt.ShouldBe(2, "V-9");
        }
        finally
        {
            if (Directory.Exists(seeded.Directory))
                Directory.Delete(seeded.Directory, true);
        }
    }

    [Test]
    [Arguments("healthy", "Reuse", false)]
    [Arguments("delivery-attempts", "AttemptOwned", false)]
    [Arguments("status-sent", "AttemptOwned", false)]
    [Arguments("canceled", "Uncertain", true)]
    [Arguments("delivery-verdict", "AttemptOwned", false)]
    [Arguments("delivery-started", "AttemptOwned", false)]
    [Arguments("marker-removed", "Uncertain", true)]
    [Arguments("marker-without-identity", "Uncertain", true)]
    [Arguments("rules-source-task", "Reuse", false)]
    [Arguments("other-session", "Absent", false)]
    [Arguments("before-dispatch", "Absent", false)]
    [Arguments("complete-user-prompt", "Received", false)]
    [Arguments("marker-only-prompt", "Uncertain", true)]
    [Arguments("old-session-prompt", "Absent", false)]
    [Arguments("remote-spill-missing", "Unavailable", true)]
    [Arguments("remote-spill-conflict", "Unavailable", true)]
    [Arguments("attempt-mismatch", "Superseded", false)]
    [Arguments("session-mismatch", "Superseded", false)]
    [Arguments("dispatched-at-mismatch", "Superseded", false)]
    [Arguments("started-at-mismatch", "Superseded", false)]
    [Arguments("custody-only", "Uncertain", true)]
    [Arguments("correlation-only", "Uncertain", true)]
    [Arguments("origin-ui", "Uncertain", true)]
    [Arguments("working-healthy", "Reuse", false)]
    [Arguments("failed-status", "Uncertain", false)]
    [Arguments("queued-user-prompt", "Reuse", false)]
    [Arguments("two-unattempted", "Uncertain", true)]
    [Arguments("remote-released-received", "Received", false)]
    [Arguments("remote-released-unreceived", "Unavailable", true)]
    [Arguments("remote-released-clipped-prompt", "Unavailable", true)]
    [Arguments("local-spill-deleted-received", "Received", false)]
    [Arguments("canceled-received", "Received", false)]
    [Arguments("windows-spill-missing", "Unavailable", true)]
    [Arguments("windows-spill-intact", "Reuse", false)]
    [Arguments("retried-intact-spill", "AttemptOwned", false)]
    [Arguments("inline-relative-mentions", "Reuse", false)]
    [Arguments("inline-absolute-posix-mention", "Reuse", false)]
    [Arguments("inline-absolute-windows-mention", "Reuse", false)]
    [Arguments("inline-quoted-path", "Reuse", false)]
    [Arguments("inline-mention-received", "Received", false)]
    [Arguments("inline-mention-attempted", "AttemptOwned", false)]
    [Arguments("lookalike-bare-path-lines", "Reuse", false)]
    [Arguments("lookalike-other-task-pointer", "Reuse", false)]
    [Arguments("lookalike-pointer-without-length", "Reuse", false)]
    [Arguments("lookalike-message-pointer-midbody", "Reuse", false)]
    [Arguments("delivery-time-spill-released", "AttemptOwned", false)]
    [Arguments("api-fallback-pointer", "Reuse", false)]
    [Arguments("genuine-relative-pointer-missing", "Unavailable", true)]
    [Arguments("genuine-compact-pointer-missing", "Unavailable", true)]
    [Arguments("genuine-joined-pointer-missing", "Unavailable", true)]
    [Arguments("genuine-message-pointer-missing", "Unavailable", true)]
    // Repair 3 F6: the bare-headline pointer every producer wrote before 563568e60.
    [Arguments("legacy-pointer-missing", "Unavailable", true)]
    [Arguments("legacy-pointer-attempted-missing", "Unavailable", true)]
    [Arguments("legacy-windows-pointer-missing", "Unavailable", true)]
    [Arguments("legacy-unc-pointer-missing", "Unavailable", true)]
    [Arguments("legacy-joined-pointer-missing", "Unavailable", true)]
    [Arguments("legacy-pointer-corrupt", "Unavailable", true)]
    [Arguments("legacy-pointer-intact", "Reuse", false)]
    [Arguments("lookalike-other-marker-headline", "Reuse", false)]
    [Arguments("lookalike-pointer-without-report-tail", "Reuse", false)]
    // Repair 3 F7: only the outer envelope is read, never a quoted previous report or goal.
    [Arguments("retry-handoff-own-pointer", "Reuse", false)]
    [Arguments("retry-handoff-own-pointer-attempted", "AttemptOwned", false)]
    [Arguments("retry-handoff-nested-quotes", "Reuse", false)]
    [Arguments("retry-handoff-fenced-headline", "Reuse", false)]
    [Arguments("goal-quotes-own-pointer", "Reuse", false)]
    [Arguments("retry-outer-pointer-missing", "Unavailable", true)]
    public Task C1150_Brief_evidence_whitelist_flips_one_condition(string condition, string expected, bool hold)
    {
        var taskId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var dispatched = Pg(DateTime.UtcNow.AddMinutes(-5));
        var started = dispatched;
        var goal = "Keep this goal exact.";
        var marker = DelegationReportFormatter.TaskMarker(taskId);
        var body = marker + "\n\n" + goal + "\nretained-canary for the whitelist row.";
        var request = new DispatchBriefEnsureRequest(taskId, 1, sessionId, dispatched, started);
        var snapshot = new DispatchBriefTaskSnapshot(
            AgentTaskStatus.Dispatched, 1, sessionId, dispatched, started, goal);
        var row = new DispatchBriefRowEvidence(
            Guid.NewGuid(), sessionId, QueuedMessageOrigin.Delegation, QueuedMessageStatus.Pending,
            dispatched, taskId, null, null, null, null, body, null, null, null,
            0, null, null, null, null, null);
        var rows = new List<DispatchBriefRowEvidence> { row };
        var prompts = new List<DispatchBriefPromptEvidence>();
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var pointerTask = new AgentTask
        {
            Id = taskId,
            Title = "Whitelist pointer",
            Goal = goal,
            Role = AgentTaskRole.Custom,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Shared,
        };
        string Pointer(string? spillPath, AgentKind kind = AgentKind.ClaudeCode) =>
            DelegationReportFormatter.BuildBriefPointer(pointerTask, new DelegationSettings(), spillPath, 5000, kind);
        var inbox = ".antiphon/inbox/" + row.Id.ToString("D") + ".md";
        var localPath = "/srv/antiphon/whitelist/.antiphon/task-" + DelegationReportFormatter.Short(taskId) + "-brief.md";
        var windowsPath = @"C:\Antiphon\worktrees\whitelist\.antiphon\task-" + DelegationReportFormatter.Short(taskId) + "-brief.md";
        var briefSpill = ".antiphon/task-" + DelegationReportFormatter.Short(taskId) + "-brief.md";
        string Inline(string mention) =>
            marker + "\n\n" + mention + " starts the goal.\n" + goal + "\nThe middle names " + mention
            + " too.\nretained-canary ends with " + mention;
        var delivered = row with
        {
            Status = QueuedMessageStatus.Sent,
            SentAt = dispatched,
            DeliveryAttempts = 1,
            DeliveryVerdict = DeliveryVerdict.Delivered,
            LastDeliveryStartedAt = dispatched,
        };
        var uncPath = @"\\host\share\whitelist\.antiphon\task-" + DelegationReportFormatter.Short(taskId) + "-brief.md";
        string Legacy(string pointer) => pointer.Replace(
            marker + " YOUR BRIEF IS NOT IN THIS MESSAGE.", "YOUR BRIEF IS NOT IN THIS MESSAGE.", StringComparison.Ordinal);
        var ownPointer = Pointer(localPath);
        var fencedOwn = "I was given this pointer and could not read it:\n```text\n" + ownPointer + "\n```";
        string Retry(string result, string? inlineGoal = null)
        {
            // Attempt 2 of the same task: BuildBrief renders the previous report as its handoff.
            request = request with { Attempt = 2 };
            snapshot = snapshot with { Attempt = 2 };
            return DelegationReportFormatter.BuildBrief(new AgentTask
            {
                Id = taskId,
                Title = "Whitelist pointer",
                Goal = inlineGoal ?? goal,
                Role = AgentTaskRole.Custom,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                Attempt = 2,
                Result = result,
            }, new DelegationSettings());
        }

        switch (condition)
        {
            case "healthy":
                break;
            case "delivery-attempts":
                rows[0] = row with { DeliveryAttempts = 1 };
                break;
            case "status-sent":
                rows[0] = row with { Status = QueuedMessageStatus.Sent, SentAt = dispatched };
                break;
            case "canceled":
                rows[0] = row with { Status = QueuedMessageStatus.Canceled, CanceledAt = dispatched };
                break;
            case "delivery-verdict":
                rows[0] = row with { DeliveryVerdict = DeliveryVerdict.Delivered };
                break;
            case "delivery-started":
                rows[0] = row with { LastDeliveryStartedAt = dispatched };
                break;
            case "marker-removed":
                rows[0] = row with { Body = "no marker here at all, only an execution id" };
                break;
            case "marker-without-identity":
                rows[0] = row with { ExecutionTaskId = null, SourceTaskId = null };
                break;
            case "rules-source-task":
                rows[0] = row with { ExecutionTaskId = null, SourceTaskId = taskId };
                break;
            case "other-session":
                rows[0] = row with { AgentSessionId = Guid.NewGuid() };
                break;
            case "before-dispatch":
                rows[0] = row with { CreatedAt = dispatched.AddMinutes(-30) };
                break;
            case "complete-user-prompt":
                prompts.Add(new DispatchBriefPromptEvidence(sessionId, TranscriptKinds.UserPrompt, body, dispatched));
                break;
            case "marker-only-prompt":
                rows.Clear();
                prompts.Add(new DispatchBriefPromptEvidence(sessionId, TranscriptKinds.UserPrompt, marker, dispatched));
                break;
            case "old-session-prompt":
                rows.Clear();
                prompts.Add(new DispatchBriefPromptEvidence(Guid.NewGuid(), TranscriptKinds.UserPrompt, body, dispatched));
                break;
            case "remote-spill-missing":
                rows[0] = row with
                {
                    RemoteSpillRelativePath = ".antiphon/inbox/missing.md",
                    RemoteSpillBody = null,
                    Body = marker + "\n.antiphon/inbox/missing.md",
                };
                break;
            case "remote-spill-conflict":
                var path = Path.Combine(
                    Path.GetTempPath(), "c1150-whitelist-" + Guid.NewGuid().ToString("N"), ".antiphon", "conflict.md");
                files[path] = body + "\nfile-bytes";
                // F4: only a producer pointer names a spill file; a bare path line is inline text.
                rows[0] = row with
                {
                    Body = Pointer(path),
                    RemoteSpillBody = body + "\nremote-bytes",
                    RemoteSpillRelativePath = ".antiphon/inbox/conflict.md",
                };
                break;
            case "attempt-mismatch":
                snapshot = snapshot with { Attempt = 2 };
                break;
            case "session-mismatch":
                snapshot = snapshot with { AgentSessionId = Guid.NewGuid() };
                break;
            case "dispatched-at-mismatch":
                snapshot = snapshot with { DispatchedAt = dispatched.AddMinutes(10) };
                break;
            case "started-at-mismatch":
                snapshot = snapshot with { SessionStartedAt = started.AddMinutes(10) };
                break;
            case "custody-only":
                rows[0] = row with
                {
                    ExecutionTaskId = null,
                    Body = "custody only",
                    RulesCoveredByMessageId = Guid.NewGuid(),
                };
                break;
            case "correlation-only":
                rows[0] = row with
                {
                    ExecutionTaskId = null,
                    Body = "correlation only",
                    ConversationKey = "task-input:" + taskId.ToString("D") + ":note",
                };
                break;
            case "origin-ui":
                rows[0] = row with { Origin = QueuedMessageOrigin.Ui };
                break;
            case "working-healthy":
                snapshot = snapshot with { Status = AgentTaskStatus.Working };
                break;
            case "failed-status":
                snapshot = snapshot with { Status = AgentTaskStatus.Failed };
                break;
            case "queued-user-prompt":
                prompts.Add(new DispatchBriefPromptEvidence(sessionId, TranscriptKinds.QueuedUserPrompt, body, dispatched));
                break;
            case "two-unattempted":
                rows.Add(row with { Id = Guid.NewGuid() });
                break;
            case "remote-released-received":
            case "remote-released-unreceived":
            case "remote-released-clipped-prompt":
                // F5: the queue typed the bound pointer, then released its retained bytes.
                rows[0] = delivered with { Body = Pointer(inbox), RemoteSpillRelativePath = inbox, RemoteSpillBody = null };
                if (condition == "remote-released-received")
                    prompts.Add(new DispatchBriefPromptEvidence(sessionId, TranscriptKinds.UserPrompt, rows[0].Body, dispatched));
                else if (condition == "remote-released-clipped-prompt")
                    prompts.Add(new DispatchBriefPromptEvidence(
                        sessionId, TranscriptKinds.UserPrompt, rows[0].Body[..(rows[0].Body.Length / 2)], dispatched));
                break;
            case "local-spill-deleted-received":
                rows[0] = delivered with { Body = Pointer(localPath) };
                prompts.Add(new DispatchBriefPromptEvidence(sessionId, TranscriptKinds.UserPrompt, rows[0].Body, dispatched));
                break;
            case "canceled-received":
                rows[0] = row with { Status = QueuedMessageStatus.Canceled, CanceledAt = dispatched, DeliveryAttempts = 1 };
                prompts.Add(new DispatchBriefPromptEvidence(sessionId, TranscriptKinds.UserPrompt, body, dispatched));
                break;
            case "windows-spill-missing":
                rows[0] = row with { Body = Pointer(windowsPath) };
                break;
            case "windows-spill-intact":
                rows[0] = row with { Body = Pointer(windowsPath) };
                files[windowsPath] = body;
                break;
            case "retried-intact-spill":
                rows[0] = row with { Body = Pointer(localPath), DeliveryAttempts = 2, LastDeliveryStartedAt = dispatched };
                files[localPath] = body;
                break;
            case "inline-relative-mentions":
                rows[0] = row with { Body = Inline(".antiphon/task-report.md and .antiphon\\inbox\\notes.md") };
                break;
            case "inline-absolute-posix-mention":
                // A readable unrelated file at the mentioned path is still not this brief's payload.
                files["/srv/antiphon/whitelist/.antiphon/inbox/notes.md"] = "unrelated notes";
                rows[0] = row with { Body = Inline("/srv/antiphon/whitelist/.antiphon/inbox/notes.md") };
                break;
            case "inline-absolute-windows-mention":
                rows[0] = row with { Body = Inline(windowsPath) };
                break;
            case "inline-quoted-path":
                rows[0] = row with { Body = Inline(@"'C:\Antiphon\work tree\whitelist\.antiphon\inbox\notes.md'") };
                break;
            case "inline-mention-received":
                rows[0] = delivered with { Body = Inline(briefSpill) };
                prompts.Add(new DispatchBriefPromptEvidence(sessionId, TranscriptKinds.UserPrompt, rows[0].Body, dispatched));
                break;
            case "inline-mention-attempted":
                rows[0] = delivered with { Body = Inline(briefSpill) };
                break;
            case "lookalike-bare-path-lines":
                rows[0] = row with { Body = marker + "\n" + briefSpill + "\n" + marker };
                break;
            case "lookalike-other-task-pointer":
                var other = new AgentTask
                {
                    Id = Guid.NewGuid(),
                    Title = "Another task",
                    Goal = "Another goal",
                    Role = AgentTaskRole.Custom,
                    ModelLevel = AgentModelLevel.Frontier,
                    Workspace = WorkspaceMode.Shared,
                };
                rows[0] = row with
                {
                    Body = body + "\nQuoted from another task:\n"
                        + DelegationReportFormatter.BuildBriefPointer(other, new DelegationSettings(), briefSpill, 5000),
                };
                break;
            case "lookalike-pointer-without-length":
                rows[0] = row with
                {
                    Body = marker + " role=Custom tier=Frontier workspace=Shared\n\nWhitelist pointer\n\n" + marker
                        + " YOUR BRIEF IS NOT IN THIS MESSAGE. Read it in full before you do anything else:\n\n    "
                        + briefSpill + "\n\nEverything you need is there. Do not start from this summary.\n\n" + marker,
                };
                break;
            case "lookalike-message-pointer-midbody":
                var quoted = TypedBodySpill.Fit(new TypedBodySpill.Request(
                    "quoted channel message " + new string('q', 200), 64, null,
                    RelativeSpillPath: inbox, ApiFallback: inbox)).ToType;
                rows[0] = row with { Body = body + "\n" + quoted };
                break;
            case "delivery-time-spill-released":
                // The queue spilled this inline body at delivery and released those bytes; the body
                // is still the whole brief.
                rows[0] = delivered with { RemoteSpillRelativePath = inbox, RemoteSpillBody = null };
                break;
            case "api-fallback-pointer":
                rows[0] = row with { Body = Pointer(null) };
                break;
            case "genuine-relative-pointer-missing":
                rows[0] = row with { Body = Pointer(briefSpill) };
                break;
            case "genuine-compact-pointer-missing":
                rows[0] = row with
                {
                    Body = DelegationReportFormatter.BuildBriefPointer(
                        pointerTask, new DelegationSettings(), briefSpill, 5000, maxWireBytes: 400),
                };
                rows[0].Body.ShouldContain("Read the complete task brief at", Case.Sensitive, condition);
                break;
            case "genuine-joined-pointer-missing":
                rows[0] = row with { Body = Pointer(briefSpill, AgentKind.Codex) };
                rows[0].Body.ShouldNotContain("\n", Case.Sensitive, condition);
                break;
            case "genuine-message-pointer-missing":
                rows[0] = row with
                {
                    Body = TypedBodySpill.Fit(new TypedBodySpill.Request(
                        body + new string('x', 400), 64, null, RelativeSpillPath: inbox, ApiFallback: inbox)).ToType,
                };
                rows[0].Body.ShouldStartWith(marker + " " + TypedBodySpill.PointerHeadline, Case.Sensitive, condition);
                break;
            case "legacy-pointer-missing":
                rows[0] = row with { Body = Legacy(ownPointer) };
                rows[0].Body.ShouldNotContain(marker + " YOUR BRIEF", Case.Sensitive, condition);
                break;
            case "legacy-pointer-attempted-missing":
                rows[0] = delivered with { Body = Legacy(ownPointer) };
                break;
            case "legacy-windows-pointer-missing":
                rows[0] = row with { Body = Legacy(Pointer(windowsPath)) };
                break;
            case "legacy-unc-pointer-missing":
                rows[0] = row with { Body = Legacy(Pointer(uncPath)) };
                break;
            case "legacy-joined-pointer-missing":
                rows[0] = row with { Body = Legacy(Pointer(localPath, AgentKind.Codex)) };
                rows[0].Body.ShouldNotContain("\n", Case.Sensitive, condition);
                break;
            case "legacy-pointer-corrupt":
                rows[0] = row with { Body = Legacy(ownPointer) };
                files[localPath] = marker + "\nthe goal was cut away";
                break;
            case "legacy-pointer-intact":
                rows[0] = row with { Body = Legacy(ownPointer) };
                files[localPath] = body;
                break;
            case "lookalike-other-marker-headline":
                // No producer writes another task's marker on this task's headline (F4 stays).
                rows[0] = row with
                {
                    Body = ownPointer.Replace(marker + " YOUR BRIEF",
                        DelegationReportFormatter.TaskMarker(Guid.NewGuid()) + " YOUR BRIEF", StringComparison.Ordinal),
                };
                break;
            case "lookalike-pointer-without-report-tail":
                rows[0] = row with
                {
                    Body = ownPointer[..ownPointer.IndexOf("--- how to report back ---", StringComparison.Ordinal)]
                        + "A paragraph no producer writes.\n\n" + marker,
                };
                break;
            case "retry-handoff-own-pointer":
                rows[0] = row with { Body = Retry(fencedOwn) };
                rows[0].Body.ShouldContain(marker + " YOUR BRIEF", Case.Sensitive, condition);
                break;
            case "retry-handoff-own-pointer-attempted":
                rows[0] = delivered with { Body = Retry(fencedOwn) };
                break;
            case "retry-handoff-nested-quotes":
                rows[0] = row with
                {
                    Body = Retry("--- previous attempt ---\nAttempt 1 ran at frontier and did not settle this. "
                        + "Do not start cold — this is\nwhat it reported:\n\n" + ownPointer + "\n\n> "
                        + fencedOwn.Replace("\n", "\n> ", StringComparison.Ordinal)),
                };
                break;
            case "retry-handoff-fenced-headline":
                var headline = ownPointer[ownPointer.IndexOf(marker + " YOUR BRIEF", 1, StringComparison.Ordinal)
                    ..ownPointer.IndexOf("--- how to report back ---", StringComparison.Ordinal)].Trim();
                rows[0] = row with { Body = Retry("```\n" + headline + "\n```") };
                break;
            case "goal-quotes-own-pointer":
                rows[0] = row with { Body = Retry("Attempt 1 ended.", "Analyze this pointer:\n```text\n" + ownPointer + "\n```\n" + goal) };
                break;
            case "retry-outer-pointer-missing":
                Retry(fencedOwn);
                rows[0] = row with { Body = ownPointer };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(condition));
        }

        var decision = DispatchBriefEvidence.Classify(
            request, snapshot, rows, prompts,
            path => files.TryGetValue(path, out var text) ? text : null);
        decision.Kind.ToString().ShouldBe(expected, condition);
        decision.Hold.ShouldBe(hold, condition);
        if (decision.Reason is not null)
            decision.Reason.Contains("delivered", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(condition);
        return Task.CompletedTask;
    }

    private sealed record CurrentBrief(
        Guid TaskId,
        Guid SessionId,
        Guid AgentId,
        DateTime DispatchedAt,
        DateTime StartedAt,
        string Goal,
        string Directory);

    private sealed record RetainedBrief(
        Guid Id,
        byte[] Body,
        byte[]? Spill,
        string? SpillPath,
        byte[]? FileBytes,
        int DeliveryAttempts,
        QueuedMessageStatus Status,
        DateTime? SentAt,
        DateTime? CanceledAt,
        DateTime? LastDeliveryStartedAt,
        DeliveryVerdict? Verdict);

    private static DispatchBriefEnsureRequest RequestFor(CurrentBrief seeded) =>
        new(seeded.TaskId, 1, seeded.SessionId, seeded.DispatchedAt, seeded.StartedAt);

    private static async Task<CurrentBrief> SeedCurrentAsync(string connection, string goal)
    {
        var taskId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var directory = Path.Combine(Path.GetTempPath(), "antiphon-c1150-" + taskId.ToString("N"));
        Directory.CreateDirectory(directory);
        var when = Pg(DateTime.UtcNow.AddMinutes(-2));
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var slug = "b" + agentId.ToString("N")[..12];
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = slug,
            Slug = slug,
            Kind = AgentKind.ClaudeCode,
            WorkingDirectory = directory,
            CreatedAt = when,
            UpdatedAt = when,
        });
        db.AgentSessions.Add(SessionRow(sessionId, directory, when, SessionStatus.Running));
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "Queue-owned brief",
            Goal = goal,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            Role = AgentTaskRole.Custom,
            Workspace = WorkspaceMode.Shared,
            ReplyTo = AgentTaskReplyTo.None,
            WorkingDirectory = directory,
            AgentId = agentId,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Dispatched,
            Attempt = 1,
            DispatchedAt = when,
            CreatedAt = when.AddMinutes(-1),
            ExecutionDeadlineAt = when.AddHours(1),
        });
        await db.SaveChangesAsync();
        return new CurrentBrief(taskId, sessionId, agentId, when, when, goal, directory);
    }

    private static AgentSession SessionRow(Guid id, string directory, DateTime when, SessionStatus status = SessionStatus.Stopped) =>
        new()
        {
            Id = id,
            DefinitionName = "brief",
            AgentKind = AgentKind.ClaudeCode,
            Status = status,
            Cwd = directory,
            CreatedAt = when,
            StartedAt = when,
            LastSeenAt = when,
        };

    private static SessionQueuedMessage HealthyRow(CurrentBrief seeded, string goal)
    {
        var marker = DelegationReportFormatter.TaskMarker(seeded.TaskId);
        return new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = seeded.SessionId,
            Origin = QueuedMessageOrigin.Delegation,
            Status = QueuedMessageStatus.Pending,
            Sequence = 1,
            CreatedAt = seeded.DispatchedAt,
            ExecutionTaskId = seeded.TaskId,
            Body = marker + "\n\n" + goal + "\nretained-canary",
        };
    }

    private static TranscriptEntry Prompt(
        CurrentBrief seeded, string text, long sequence, Guid? sessionId = null, DateTime? timestamp = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId ?? seeded.SessionId,
            Kind = TranscriptKinds.UserPrompt,
            Sequence = sequence,
            Text = text,
            Timestamp = timestamp ?? seeded.DispatchedAt,
            CreatedAt = timestamp ?? seeded.DispatchedAt,
        };

    private static async Task<RetainedBrief> SeedRetainedBriefAsync(string connection, CurrentBrief seeded, string shape)
    {
        var marker = DelegationReportFormatter.TaskMarker(seeded.TaskId);
        var payload = marker + "\n" + seeded.Goal + "\nretained-canary";
        var row = HealthyRow(seeded, seeded.Goal);
        string? path = null;
        byte[]? fileBytes = null;
        if (shape == "local-spill")
        {
            path = Path.Combine(seeded.Directory, ".antiphon", "task-" + DelegationReportFormatter.Short(seeded.TaskId) + "-brief.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, payload);
            fileBytes = await File.ReadAllBytesAsync(path);
            // The producer's pointer, so this shape is a real local spill and not inline text (F4).
            row.Body = DelegationReportFormatter.BuildBriefPointer(new AgentTask
            {
                Id = seeded.TaskId,
                Title = "Queue-owned brief",
                Goal = seeded.Goal,
                Role = AgentTaskRole.Custom,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
            }, new DelegationSettings(), path, payload.Length);
        }
        else if (shape == "remote-spill")
        {
            var relative = ".antiphon/inbox/" + row.Id.ToString("D") + ".md";
            row.RemoteSpillBody = payload;
            row.RemoteSpillRelativePath = relative;
            row.Body = marker + "\n" + relative + "\n" + marker;
        }

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        db.SessionQueuedMessages.Add(row);
        await db.SaveChangesAsync();
        return new RetainedBrief(
            row.Id,
            Encoding.UTF8.GetBytes(row.Body),
            row.RemoteSpillBody is null ? null : Encoding.UTF8.GetBytes(row.RemoteSpillBody),
            path,
            fileBytes,
            row.DeliveryAttempts,
            row.Status,
            row.SentAt,
            row.CanceledAt,
            row.LastDeliveryStartedAt,
            row.DeliveryVerdict);
    }

    private static async Task AssertRetainedAsync(string connection, CurrentBrief seeded, RetainedBrief captured, string shape)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var rows = await db.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId).ToListAsync();
        rows.Count.ShouldBe(1, shape);
        var row = rows[0];
        row.Id.ShouldBe(captured.Id, shape);
        Encoding.UTF8.GetBytes(row.Body).ShouldBe(captured.Body, shape);
        (row.RemoteSpillBody is null ? null : Encoding.UTF8.GetBytes(row.RemoteSpillBody)).ShouldBe(captured.Spill, shape);
        row.RemoteSpillRelativePath.ShouldBe(
            captured.Spill is null ? null : ".antiphon/inbox/" + captured.Id.ToString("D") + ".md", shape);
        row.DeliveryAttempts.ShouldBe(captured.DeliveryAttempts, shape);
        row.Status.ShouldBe(captured.Status, shape);
        row.SentAt.ShouldBe(captured.SentAt, shape);
        row.CanceledAt.ShouldBe(captured.CanceledAt, shape);
        row.LastDeliveryStartedAt.ShouldBe(captured.LastDeliveryStartedAt, shape);
        row.DeliveryVerdict.ShouldBe(captured.Verdict, shape);
        if (captured.FileBytes is not null && captured.SpillPath is not null)
            (await File.ReadAllBytesAsync(captured.SpillPath)).ShouldBe(captured.FileBytes, shape);
        var authoritative = row.RemoteSpillBody ?? (captured.SpillPath is null ? row.Body : await File.ReadAllTextAsync(captured.SpillPath));
        Encoding.UTF8.GetBytes(authoritative).ShouldBe(
            captured.Spill ?? captured.FileBytes ?? captured.Body, shape);
        (await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId)).Status
            .ShouldBe(AgentTaskStatus.Dispatched, shape);
    }

    private static async Task AssertReceiptCompanionAsync(string connection, SessionMessageQueueService queue)
    {
        var seeded = await SeedCurrentAsync(connection, "Receipt companion goal stays.");
        var marker = DelegationReportFormatter.TaskMarker(seeded.TaskId);
        var body = marker + "\n\n" + seeded.Goal + "\ncomplete receipt companion body for the original attempt.";
        var rowId = Guid.NewGuid();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = rowId,
                AgentSessionId = seeded.SessionId,
                Origin = QueuedMessageOrigin.Delegation,
                Status = QueuedMessageStatus.Sent,
                SentAt = seeded.DispatchedAt,
                DeliveryAttempts = 1,
                DeliveryVerdict = DeliveryVerdict.Delivered,
                Sequence = 1,
                CreatedAt = seeded.DispatchedAt,
                ExecutionTaskId = seeded.TaskId,
                Body = body,
            });
            db.TranscriptEntries.Add(Prompt(seeded, body, 1));
            await db.SaveChangesAsync();
        }

        var result = await queue.EnsureDispatchBriefAsync(RequestFor(seeded), CancellationToken.None);
        result.Kind.ShouldBe(DispatchBriefKind.Received, "V-8 receipt");
        result.Inserted.ShouldBeFalse("V-8 receipt");
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, "V-8 receipt");
        var row = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == rowId);
        row.Body.ShouldBe(body, "V-8 receipt");
        row.Status.ShouldBe(QueuedMessageStatus.Sent, "V-8 receipt");
        (await verify.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == seeded.TaskId)).ShouldBe(1, "V-8 receipt");
        if (Directory.Exists(seeded.Directory))
            Directory.Delete(seeded.Directory, true);
    }

    private static async Task<SessionQueuedMessage> SingleBriefAsync(string connection, Guid taskId, string label)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var rows = await db.SessionQueuedMessages.Where(m => m.ExecutionTaskId == taskId).ToListAsync();
        rows.Count.ShouldBe(1, label);
        return rows[0];
    }

    private static async Task<int> UserPromptCountAsync(string connection, Guid sessionId) =>
        (await UserPromptsAsync(connection, sessionId)).Count;

    private static async Task<List<string>> UserPromptsAsync(string connection, Guid sessionId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        return await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt)
            .Select(t => t.Text ?? "")
            .ToListAsync();
    }

    private static string AuthoritativeBrief(SessionQueuedMessage row)
    {
        if (!string.IsNullOrEmpty(row.RemoteSpillBody))
            return row.RemoteSpillBody;
        var path = DispatchBriefEvidence.AbsoluteSpillPath(row.Body);
        if (path is not null && File.Exists(path))
            return File.ReadAllText(path);
        return row.Body;
    }

    private static Task<BridgeQueueHarness> OpenQueueAsync(string connection, CurrentBrief? attached = null) =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = connection,
            PreserveDatabaseOnDispose = true,
            AttachSessionId = attached?.SessionId,
            AttachAgentId = attached?.AgentId,
        });
}
