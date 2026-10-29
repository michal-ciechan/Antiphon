using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Services;
using Antiphon.TestSupport;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Antiphon.E2E.Fixtures;

public sealed partial class LandDeliveryFixture
{
    public sealed record DispatchSibling(Guid Id, string Branch, string Tip, int Covered);
    private readonly List<DispatchSibling> _dispatchSiblings = [];
    public IReadOnlyList<DispatchSibling> ExpectedDispatchSiblings { get; private set; } = [];

    public async Task StartDispatchAsync()
    {
        File.Exists(Path.Combine(Root, "dispatch-task.txt")).ShouldBeTrue("opt-in dispatch setup required");
        Console.WriteLine($"C540 native evidence: {Root}");
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        var project = new Project { Id = Guid.NewGuid(), Name = "C540 owned", GitRepositoryUrl = Remote,
            BaseBranch = "master", CreatedAt = now, UpdatedAt = now };
        var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "C540 owned", CreatedAt = now, UpdatedAt = now };
        var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
            CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now };
        var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
            Identifier = "CARD-0540", Title = "Owned sibling dispatch", CreatedAt = now, UpdatedAt = now };
        db.AddRange(project, board, column, card);
        foreach (var (name, start, alias) in new[] { ("a", "master", false), ("alias", "c540-a", true),
                     ("containing", "c540-a", false), ("divergent", "master", false) })
        {
            var branch = "c540-" + name;
            await GitAsync(Repository, "checkout", "-b", branch, start);
            if (!alias)
            {
                await File.WriteAllTextAsync(Path.Combine(Repository, name + ".txt"), name);
                await GitAsync(Repository, "add", name + ".txt");
                await GitAsync(Repository, "commit", "-m", "owned sibling " + name);
            }
            var tip = (await GitAsync(Repository, "rev-parse", "HEAD")).Trim();
            var id = Guid.NewGuid();
            var sibling = new DispatchSibling(id, branch, tip, name == "containing" ? 2 : 0);
            _dispatchSiblings.Add(sibling);
            db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, CardId = card.Id, ProjectId = project.Id,
                Title = "Owned sibling " + name, Goal = "fixture", Role = AgentTaskRole.Plan,
                Workspace = WorkspaceMode.Worktree, Status = AgentTaskStatus.Succeeded,
                RepoPath = Repository, WorkingDirectory = Repository, WorktreeBranch = branch,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = now.AddMinutes(-10), CompletedAt = now.AddMinutes(-5) });
        }
        await GitAsync(Repository, "checkout", "master");
        ExpectedDispatchSiblings = _dispatchSiblings.Where(s => s.Branch is "c540-containing" or "c540-divergent").ToArray();
        await File.WriteAllTextAsync(Path.Combine(Root, "dispatch-graph.json"), JsonSerializer.Serialize(_dispatchSiblings));
        // CARD-1305: the four same-card branches above are intentionally divergent so the dispatcher
        // observes and reduces them into sibling warnings. CARD-0442 refuses automatic base selection
        // on divergent tips, so the queued task records the -FreshWorktree intent (explicit Target
        // mode, AgentTaskService.cs) exactly as a delegate.ps1 dispatch would.
        db.AgentTasks.Add(new AgentTask { Id = TaskId, RootTaskId = TaskId, CardId = card.Id, ProjectId = project.Id,
            Title = "C540 native queued dispatch", Goal = "Complete this owned fixture turn.", Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code, AgentKind = AgentKind.Grok, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Repository, RepoPath = Repository, Status = AgentTaskStatus.Queued,
            RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Target,
            ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = CallerId, CreatedAt = now });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// V-16: zero ordinary sibling warnings. The project's <c>trunk</c> default is deleted under
    /// the claim lease, after the pre-lease observation. The cut falls back to HEAD and owes the
    /// stale-observation intent plus <c>default-unresolved</c>. A lease edit of the default string
    /// is a different path: the claim is refused and the next tick re-observes, which
    /// <c>AgentTaskDispatchBaseGuardTests.C508_GuardRefMismatchWarnedOnce</c> already covers.
    /// </summary>
    public async Task StartMismatchDispatchAsync()
    {
        File.Exists(Path.Combine(Root, "dispatch-task.txt")).ShouldBeTrue("opt-in dispatch setup required");
        Console.WriteLine($"C508 mismatch native evidence: {Root}");
        await GitAsync(Repository, "branch", "trunk", "master");
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        var project = new Project { Id = Guid.NewGuid(), Name = "C508 mismatch", GitRepositoryUrl = Remote,
            BaseBranch = "trunk", CreatedAt = now, UpdatedAt = now };
        var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "C508 mismatch", CreatedAt = now, UpdatedAt = now };
        var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
            CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now };
        var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
            Identifier = "CARD-0508", Title = "Mismatch dispatch", CreatedAt = now, UpdatedAt = now };
        db.AddRange(project, board, column, card);
        db.AgentTasks.Add(new AgentTask { Id = TaskId, RootTaskId = TaskId, CardId = card.Id, ProjectId = project.Id,
            Title = "C508 mismatch queued dispatch", Goal = "Complete this owned fixture turn.", Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code, AgentKind = AgentKind.Grok, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Repository, RepoPath = Repository, Status = AgentTaskStatus.Queued,
            ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = CallerId, CreatedAt = now });
        await File.WriteAllTextAsync(Path.Combine(Root, "delete-default-on-lease.json"),
            JsonSerializer.Serialize(new { branch = "trunk" }));
        await db.SaveChangesAsync();
    }

    public async Task<List<AgentTaskDispatchWarningIntent>> DispatchIntentsAsync()
    {
        await using var db = CreateContext();
        return await db.AgentTaskDispatchWarningIntents.AsNoTracking().Where(i => i.TaskId == TaskId).OrderBy(i => i.WarningKey).ToListAsync();
    }

    public async Task<List<AgentTaskDispatchWarningIntent>> WaitForDispatchIntentsAsync()
    {
        try
        {
            await UntilProtocolAsync(async () =>
            {
                // CARD-1305: a dispatch refusal (CARD-0442 blocks the task instead of selecting a base)
                // must fail the wait at the first poll with its reason, not spend the quiet deadline.
                await using var db = CreateContext();
                var owned = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == TaskId);
                owned.Status.ShouldNotBe(AgentTaskStatus.Blocked,
                    $"the owned dispatch was refused before any warning intent: {owned.FailureReason}");
                return (await DispatchIntentsAsync()).Count == 2;
            }, "two committed reduced dispatch intents");
        }
        catch (TimeoutException ex)
        {
            throw await DispatchTimeoutAsync(ex);
        }
        var intents = await DispatchIntentsAsync();
        foreach (var expected in ExpectedDispatchSiblings)
        {
            var intent = intents.Single(i => i.WarningKey == DispatchBaseNotificationPayload.SiblingKey(expected.Id));
            intent.Detail.ShouldContain(expected.Branch); intent.Detail.ShouldContain(expected.Tip);
            if (expected.Covered > 0) intent.Detail.ShouldContain($"also covers {expected.Covered} other kept sibling branches");
            else intent.Detail.ShouldNotContain("also covers");
            intent.ParentSessionId.ShouldBe(CallerId);
        }
        return intents;
    }

    public async Task<List<AgentTaskDispatchWarningIntent>> WaitForMismatchIntentAsync()
    {
        // The vanished trunk default is observed, then deleted under the lease. The claim cuts
        // HEAD and commits the mismatch intent plus default-unresolved. The 120 s quiet window
        // renews on dispatch progress (CARD-1359); a stuck host still reports within one quiet
        // period, and the timeout names the rows it actually saw.
        await UntilDispatchAsync(async () =>
        {
            await using var db = CreateContext();
            var owned = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == TaskId);
            owned.Status.ShouldNotBe(AgentTaskStatus.Blocked,
                "the owned dispatch was refused before the vanished-default cut: " + owned.FailureReason);
            owned.Status.ShouldNotBe(AgentTaskStatus.Failed,
                "the owned dispatch failed before the vanished-default cut: " + owned.FailureReason);
            if (owned.Status != AgentTaskStatus.Dispatched) return false;
            var intents = await DispatchIntentsAsync();
            return owned.WorktreeBaseRef == "HEAD"
                && owned.WorktreeBaseSource == WorktreeBaseSource.RepoHead
                && intents.Count == 2
                && intents.Any(i => i.WarningKey == DispatchBaseNotificationPayload.MismatchKey)
                && intents.Any(i => i.WarningKey == DispatchBaseNotificationPayload.DefaultUnresolvedKey);
        }, "vanished-default dispatch at HEAD with the mismatch and default-unresolved intents", 120);
        File.Exists(Path.Combine(Root, "delete-default-on-lease.fired.json"))
            .ShouldBeTrue("the lease hook must delete trunk before the claim cuts HEAD");
        var committed = await DispatchIntentsAsync();
        committed.Count.ShouldBe(2);
        var mismatch = committed.Single(i => i.WarningKey == DispatchBaseNotificationPayload.MismatchKey);
        mismatch.Detail.ShouldContain("trunk");
        mismatch.Detail.ShouldContain("HEAD");
        mismatch.ParentSessionId.ShouldBe(CallerId);
        committed.ShouldContain(i => i.WarningKey == DispatchBaseNotificationPayload.DefaultUnresolvedKey);
        return committed;
    }

    /// <summary>Same <see cref="TimeoutException"/>, plus the owned task's status, intent rows and last events.</summary>
    public async Task<TimeoutException> DispatchTimeoutAsync(TimeoutException ex) =>
        new(DispatchWaitDiagnostic.Append(ex.Message, await CaptureDispatchWaitAsync()), ex);

    private async Task<DispatchWaitDiagnostic.Snapshot> CaptureDispatchWaitAsync()
    {
        try
        {
            await using var db = CreateContext();
            var owned = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == TaskId);
            var rows = await db.AgentTaskDispatchWarningIntents.AsNoTracking()
                .Where(i => i.TaskId == TaskId).OrderBy(i => i.WarningKey)
                .Select(i => new { i.WarningKey, i.Detail }).ToListAsync();
            var events = await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == TaskId).OrderByDescending(e => e.At).Take(6)
                .Select(e => new { e.Type, e.Detail }).ToListAsync();
            return new DispatchWaitDiagnostic.Snapshot(
                owned?.Status.ToString() ?? "missing",
                owned?.FailureReason,
                owned?.WorktreeBaseRef,
                owned?.WorktreeBaseSource.ToString(),
                rows.Count,
                rows.Select(i => i.WarningKey).ToArray(),
                string.Join("; ", rows.Select(i => i.Detail)),
                File.Exists(Path.Combine(Root, "move-default-on-lease.fired.json"))
                    || File.Exists(Path.Combine(Root, "delete-default-on-lease.fired.json")),
                string.Join("; ", events.Select(e => e.Type + ":" + e.Detail)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DispatchWaitDiagnostic.Snapshot(
                "unreadable", ex.GetType().Name + ":" + ex.Message, null, null, 0, [], null, false, null);
        }
    }

    public Task WaitForBoundaryAsync(string boundary) => UntilProtocolAsync(() =>
        Task.FromResult(File.Exists(Path.Combine(Root, boundary + ".barrier.json"))), boundary);

    /// <summary>
    /// CARD-1359 D-2: dispatch-side progress markers for the owned task. Each is a step of the
    /// dispatch chain and non-decreasing, so the sum changes exactly when one of them does.
    /// </summary>
    public sealed record DispatchProgress(long Events, long Intents, long Materialized, long Notifications, long Queued, long Launched)
    {
        public long Total => Events + Intents + Materialized + Notifications + Queued + Launched;
    }

    public async Task<DispatchProgress> DispatchProgressAsync()
    {
        await using var db = CreateContext();
        var events = await db.AgentTaskEvents.AsNoTracking().LongCountAsync(e => e.AgentTaskId == TaskId);
        var intents = await db.AgentTaskDispatchWarningIntents.AsNoTracking().LongCountAsync(i => i.TaskId == TaskId);
        var materialized = await db.AgentTaskDispatchWarningIntents.AsNoTracking().LongCountAsync(i => i.TaskId == TaskId && i.MaterializedAt != null);
        var notifications = await db.AgentTaskLandNotifications.AsNoTracking().LongCountAsync(n => n.TaskId == TaskId);
        var queued = await db.SessionQueuedMessages.AsNoTracking().LongCountAsync(m => m.SourceTaskId == TaskId);
        var sessionId = await db.AgentTasks.AsNoTracking().Where(t => t.Id == TaskId).Select(t => t.AgentSessionId).SingleOrDefaultAsync();
        var launched = sessionId is Guid session
            && await db.AgentSessions.AsNoTracking().AnyAsync(s => s.Id == session && s.InteractiveLaunchCompletedAt != null) ? 1 : 0;
        return new(events, intents, materialized, notifications, queued, launched);
    }

    /// <summary>
    /// CARD-1359: the dispatch-aware twin of <see cref="UntilProtocolAsync"/>. The quiet window (60 s)
    /// and the cap (600 s) are unchanged; the probe is the protocol-git count plus the dispatch-side
    /// markers, refreshed by the predicate wrapper on every poll. The wait record names both so a
    /// future quiet timeout says which source moved last.
    /// </summary>
    public async Task UntilDispatchAsync(Func<Task<bool>> predicate, string evidence, int quietSeconds = 60)
    {
        var started = DateTime.UtcNow;
        var protocolBefore = ProtocolProgress();
        var dispatchBefore = await DispatchProgressAsync();
        var last = dispatchBefore;
        var dispatch = dispatchBefore.Total;
        var outcome = "satisfied";
        try
        {
            await UntilAsync(async () =>
            {
                last = await DispatchProgressAsync();
                dispatch = last.Total;
                return await predicate();
            }, evidence, quietSeconds, () => ProtocolProgress() + dispatch);
        }
        catch (TimeoutException ex)
        {
            outcome = "timeout";
            throw await DispatchTimeoutAsync(ex);
        }
        finally
        {
            var elapsed = (DateTime.UtcNow - started).TotalSeconds;
            DispatchProgress dispatchAfter;
            try { dispatchAfter = await DispatchProgressAsync(); } catch { dispatchAfter = last; }
            await File.WriteAllTextAsync(Path.Combine(Root, $"wait-{Guid.NewGuid():N}.json"), JsonSerializer.Serialize(new {
                evidence, outcome, kind = "dispatch", quietSeconds, elapsedSeconds = elapsed, protocolGitBefore = protocolBefore,
                protocolGitAfter = ProtocolProgress(), dispatchBefore, dispatchAfter, renewed = elapsed > quietSeconds },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
    }

    public Task WaitForDispatchBoundaryAsync(string boundary) => UntilDispatchAsync(() =>
        Task.FromResult(File.Exists(Path.Combine(Root, boundary + ".barrier.json"))), boundary);

    // CARD-1359: the slow-launch seam (FileBoundary.ReachedAsync) holds the owned task's reach of a
    // named boundary for the mapped seconds. Written before the dispatch starts; cleared before the
    // restarted child so recovery never sees it.
    public Task WriteSlowLaunchAsync(IReadOnlyDictionary<string, int> delays) =>
        File.WriteAllTextAsync(Path.Combine(Root, "slow-launch.json"), JsonSerializer.Serialize(delays));

    public void ClearSlowLaunch() => File.Delete(Path.Combine(Root, "slow-launch.json"));

    /// <summary>
    /// CARD-1359 V-1 premise: the seam fired for exactly the owned intents, and the barrier arrived
    /// more than the quiet window after the last protocol-git observation that preceded it, so a
    /// wait renewed only on protocol-git would have timed out. Measured on the child host's clock.
    /// </summary>
    public async Task AssertSlowMaterializationPremiseAsync(string boundary, IReadOnlyList<AgentTaskDispatchWarningIntent> intents, int seconds)
    {
        const string seam = "dispatch-warning-before-materialize";
        var records = Directory.GetFiles(Root, $"slow-launch-{seam}-*.observation.json").Select(path =>
        {
            using var record = JsonDocument.Parse(File.ReadAllText(path));
            return (identity: record.RootElement.GetProperty("identity").GetGuid(),
                seconds: record.RootElement.GetProperty("seconds").GetInt32(),
                at: record.RootElement.GetProperty("at").GetDateTime());
        }).ToList();
        var barrierAt = File.GetLastWriteTimeUtc(Path.Combine(Root, boundary + ".barrier.json"));
        var protocolGit = Directory.GetFiles(Root, "protocol-git-*.json").Select(path =>
        {
            using var record = JsonDocument.Parse(File.ReadAllText(path));
            return record.RootElement.GetProperty("at").GetDateTime();
        }).ToList();
        var lastBeforeBarrier = protocolGit.Where(at => at <= barrierAt).DefaultIfEmpty(DateTime.MinValue).Max();
        var silence = barrierAt - lastBeforeBarrier;
        await File.WriteAllTextAsync(Path.Combine(Root, "slow-launch-premise.json"), JsonSerializer.Serialize(new
        {
            boundary, seam, seconds, barrierAt, lastProtocolGitBeforeBarrier = lastBeforeBarrier, silenceSeconds = silence.TotalSeconds,
            protocolGitCount = protocolGit.Count, protocolGitAfterBarrier = protocolGit.Count(at => at > barrierAt),
            records = records.Count, identities = records.Select(r => r.identity).Distinct().Order().ToArray(),
            intents = intents.Select(i => i.Id).Order().ToArray(),
        }));
        records.Select(r => r.identity).Distinct().Order().ShouldBe(intents.Select(i => i.Id).Order(),
            "the seam must fire for exactly the owned dispatch intents");
        records.ShouldAllBe(r => r.seconds == seconds);
        silence.TotalSeconds.ShouldBeGreaterThan(60d,
            $"the {boundary} barrier must arrive more than the quiet window after the last protocol-git observation");
    }

    public async Task TwoNotificationScansAsync()
    {
        var scans = Directory.GetFiles(Root, "notification-scan-*.observation.json").Length;
        try
        {
            await UntilAsync(() => Task.FromResult(Directory.GetFiles(Root, "notification-scan-*.observation.json").Length >= scans + 2),
                "two completed notification scans");
        }
        catch (TimeoutException ex)
        {
            throw await DispatchTimeoutAsync(ex);
        }
    }

    public async Task AssertDispatchReceiptsAsync(IReadOnlyList<AgentTaskDispatchWarningIntent> original)
    {
        await TwoNotificationScansAsync();
        await using (var db = CreateContext())
        {
            var notes = await db.AgentTaskLandNotifications.Where(n => n.TaskId == TaskId && n.Kind == LandNotificationKind.DispatchBase).ToListAsync();
            notes.Select(n => n.Id).Order().ShouldBe(original.Select(i => i.NotificationId).Order());
            foreach (var intent in original)
            {
                var note = notes.Single(n => n.Id == intent.NotificationId);
                note.Body.ShouldBe(intent.Body); note.SourceEventId.ShouldBe(intent.Id); note.ParentSessionId.ShouldBe(CallerId);
            }
        }
        foreach (var intent in original)
        {
            try
            {
                await ReceiptAsync(LandNotificationKind.DispatchBase, notificationId: intent.NotificationId);
            }
            catch (TimeoutException ex)
            {
                throw await DispatchTimeoutAsync(ex);
            }
        }
        await TwoNotificationScansAsync();
        await using var check = CreateContext();
        var current = await DispatchIntentsAsync();
        current.Select(i => (i.Id, i.NotificationId, i.WarningKey, i.Body)).ShouldBe(
            original.Select(i => (i.Id, i.NotificationId, i.WarningKey, i.Body)));
        var prompts = await check.TranscriptEntries.Where(p => p.AgentSessionId == CallerId && p.Kind == TranscriptKinds.UserPrompt).ToListAsync();
        var nativeDir = Path.Combine(Root, "native");
        var native = (Directory.Exists(nativeDir)
                ? Directory.GetFiles(nativeDir, "updates.jsonl", SearchOption.AllDirectories).SelectMany(File.ReadAllLines)
                : [])
            .Select(line => JsonDocument.Parse(line)).ToList();
        try
        {
            var nativePrompts = native.Where(d => d.RootElement.TryGetProperty("params", out var p)
                    && p.TryGetProperty("update", out var u) && u.GetProperty("sessionUpdate").GetString() == "user_message_chunk")
                .Select(d => d.RootElement.GetProperty("params").GetProperty("update").GetProperty("content").GetProperty("text").GetString()!).ToList();
            bool IsWarning(string? text) => text is not null && (text.Contains("[dispatch-base ", StringComparison.Ordinal)
                || text.Contains("branched from", StringComparison.Ordinal) && text.Contains("kept branch", StringComparison.Ordinal)
                || text.Contains("sibling containment was evaluated against", StringComparison.Ordinal));
            prompts.Count(p => IsWarning(p.Text)).ShouldBe(original.Count);
            nativePrompts.Count(IsWarning).ShouldBe(original.Count, "one native complete prompt per committed dispatch intent");
            foreach (var intent in original)
            {
                prompts.Count(p => PromptSubmissionMatch.IsCompleteIn(intent.Body, p.Text ?? "")).ShouldBe(1);
                nativePrompts.Count(p => PromptSubmissionMatch.IsCompleteIn(intent.Body, p)).ShouldBe(1);
                (await check.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == intent.NotificationId)).ShouldBe(1);
            }
            var siblingCount = original.Count(i => i.WarningKey.StartsWith(
                DispatchBaseNotificationPayload.SiblingKeyPrefix, StringComparison.Ordinal));
            (await check.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == CallerId && m.Body.Contains("kept branch")))
                .ShouldBe(siblingCount);
        }
        finally { foreach (var document in native) document.Dispose(); }
        await SnapshotAsync();
    }

    public async Task MoveDispatchObservationsAsync()
    {
        foreach (var sibling in _dispatchSiblings)
            await GitAsync(Repository, "update-ref", "refs/heads/" + sibling.Branch, "master");
        await using var db = CreateContext();
        await db.AgentTasks.Where(t => t.Id == TaskId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.Status, AgentTaskStatus.Canceled).SetProperty(t => t.ReplyTo, AgentTaskReplyTo.None)
            .SetProperty(t => t.ParentSessionId, (Guid?)null));
        foreach (var sibling in _dispatchSiblings)
            await db.AgentTasks.Where(t => t.Id == sibling.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Canceled));
    }

    public async Task WaitForInterruptedDispatchEligibilityAsync()
    {
        await using var db = CreateContext();
        var attempted = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.SourceTaskId == TaskId
            && m.SourceLandNotificationId != null && m.Status == QueuedMessageStatus.Sent && m.DeliveryVerdict == null).ToListAsync();
        attempted.ShouldNotBeEmpty();
        var eligibleAt = attempted.Max(m => m.LastDeliveryStartedAt!.Value) + InterruptedAttemptAge;
        var waitStarted = DateTime.UtcNow;
        // Arrange the production recovery prerequisite with real time while the child is dead.
        // Do not edit attempt timestamps, shorten the safety guard or extend ReceiptAsync's deadline.
        while (DateTime.UtcNow < eligibleAt)
        {
            var remaining = eligibleAt - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
        }
        await File.WriteAllTextAsync(Path.Combine(Root, "interrupted-attempt-eligibility.json"), JsonSerializer.Serialize(new
        { attempts = attempted.Select(m => new { m.Id, m.LastDeliveryStartedAt }), guardSeconds = InterruptedAttemptAge.TotalSeconds,
            eligibleAt, waitStarted, observedAt = DateTime.UtcNow }));
    }

    public async Task WaitForLateConfirmedInterruptedRowsAsync(IReadOnlyDictionary<Guid, int> attemptsByRow)
    {
        // Queue recovery runs from the restarted host's own flushes or stranded-queue sweep (60 s
        // default period) once the row is eligible: two sweep periods plus host warm-up.
        List<SessionQueuedMessage> rows = [];
        try
        {
            await UntilAsync(async () =>
            {
                await using var db = CreateContext();
                rows = await db.SessionQueuedMessages.AsNoTracking().Where(m => attemptsByRow.Keys.Contains(m.Id)).ToListAsync();
                return rows.Count == attemptsByRow.Count && rows.All(m => m.DeliveryVerdict != null || m.Status != QueuedMessageStatus.Sent
                    || m.DeliveryAttempts != attemptsByRow[m.Id]);
            }, "interrupted post-prompt queue rows recovered after eligibility", 150);
        }
        catch (TimeoutException ex)
        {
            throw await DispatchTimeoutAsync(ex);
        }
        await File.WriteAllTextAsync(Path.Combine(Root, "interrupted-attempt-recovery.json"), JsonSerializer.Serialize(new
        { rows = rows.Select(m => new { m.Id, m.Status, m.DeliveryVerdict, m.DeliveryVerdictAt, m.DeliveryAttempts,
            originalAttempts = attemptsByRow[m.Id], m.LastDeliveryStartedAt }), observedAt = DateTime.UtcNow }));
        foreach (var row in rows)
        {
            row.DeliveryAttempts.ShouldBe(attemptsByRow[row.Id], "transcript evidence must settle the attempt without another one");
            row.Status.ShouldBe(QueuedMessageStatus.Sent);
            row.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed, "the original row owes a transcript late-confirm, not a resubmission");
        }
    }
}
