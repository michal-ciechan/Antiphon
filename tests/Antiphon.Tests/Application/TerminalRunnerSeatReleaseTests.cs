using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Data.Common;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class TerminalRunnerSeatReleaseTests
{
    [Test]
    [Arguments(false, "Working")]
    [Arguments(true, "Working")]
    [Arguments(false, "UnsupportedPeer")]
    [Arguments(true, "UnsupportedPeer")]
    [Arguments(false, "UnsupportedTransport")]
    [Arguments(true, "UnsupportedTransport")]
    public async Task Unpooled_remote_shared_release_never_uses_the_legacy_stopper(bool sweep, string hold)
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync();
        f.Harness.Provider.GetRequiredService<IOptions<DelegationSettings>>().Value.PoolEnabled = false;
        await f.EditAsync((task, _) => task.Workspace = WorkspaceMode.Shared);
        if (hold == "Working")
            f.Wire.Qualified = f.Wire.Qualified with { Status = TerminalSeatQualificationStatus.Working, Token = null };
        else if (hold == "UnsupportedPeer")
            f.Directory.FeaturesOverride = [];
        else
            f.Wire.Unsupported = true;

        if (sweep) await f.SweepAsync();
        else await f.ReleaseFromSettlementAsync();

        f.RecordedStops.Killed.ShouldBeEmpty("D-1: remote Shared retirement must use conditional release");
        f.Wire.ForceCommands.ShouldBe(0);
        f.Wire.ConditionalCommands.ShouldBe(0);
        await using var db = f.Db();
        (await db.RunnerSeatReleases.SingleAsync()).ReasonCode.ShouldBe(hold == "Working" ? "Working" : "Unsupported");
        (await db.Agents.AnyAsync(a => a.Id == f.AgentId)).ShouldBeTrue();
        (await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Running);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Remote_shared_release_keeps_intentional_warm_pooling(bool sweep)
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync();
        await f.EditAsync((task, _) => task.Workspace = WorkspaceMode.Shared);
        if (sweep) await f.SweepAsync();
        else await f.ReleaseFromSettlementAsync();
        f.RecordedStops.Killed.ShouldBeEmpty();
        f.Wire.Calls.ShouldBeEmpty();
        await using var db = f.Db();
        var agent = await db.Agents.SingleAsync(a => a.Id == f.AgentId);
        agent.Status.ShouldBe(AgentStatus.Idle);
        agent.PoolIdleSince.ShouldBe(f.Now);
        agent.PoolReservedForRootTaskId.ShouldBe(f.TaskId);
        (await db.RunnerSeatReleases.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Working_session_keeps_ownership_and_visible_debt()
    {
        foreach (var status in new[] { AgentTaskStatus.Failed, AgentTaskStatus.Succeeded, AgentTaskStatus.Blocked })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(status, parking: status == AgentTaskStatus.Blocked);
            if (status == AgentTaskStatus.Blocked) await f.PreparePublishedBlockedSourceAsync();
            await f.IngestAsync(TranscriptKinds.UserPrompt, "still working", f.Now);
            await f.ReleaseFromSettlementAsync();
            f.RecordedStops.Killed.ShouldBeEmpty("PC-14: the settlement hook cannot use the ordinary stopper");
            f.Wire.ConditionalCommands.ShouldBe(0, "PC-28: server Working independently vetoes runner Idle");
            await using var db = f.Db();
            (await db.Agents.AnyAsync(a => a.Id == f.AgentId)).ShouldBeTrue();
            (await db.RunnerSeatReleases.SingleAsync()).ReasonCode.ShouldBe("Working");
            (await f.AttentionAsync()).Items.ShouldContain(i => i.ConditionKey != null
                && i.ConditionKey.StartsWith("runner-seat-release:"));
        }
    }

    [Test]
    public async Task Janitor_cannot_bypass_a_release_hold()
    {
        foreach (var hold in new[] { TerminalSeatQualificationStatus.Working, TerminalSeatQualificationStatus.Unknown })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Failed);
            f.Wire.Qualified = f.Wire.Qualified with { Status = hold, Token = null };
            await f.EditAsync((_, a) => { a.Status = AgentStatus.Idle; a.PoolIdleSince = f.Now.AddDays(-2); });
            await f.SweepAsync(janitor: true);
            f.RecordedStops.Killed.ShouldBeEmpty("PC-15: neither TTL nor pool-cap retirement can bypass the hold");
            f.Wire.ConditionalCommands.ShouldBe(0);
            await using var db = f.Db();
            (await db.Agents.AnyAsync(a => a.Id == f.AgentId)).ShouldBeTrue();
            (await db.RunnerSeatReleases.SingleAsync()).ReasonCode.ShouldBe(hold.ToString());
        }
    }

    [Test]
    public async Task Cancellation_reconciles_without_second_stop()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Working);
        await f.EditAsync((t, _) => t.CompletedAt = null);
        f.RecordedStops.StopsSessionsIn = f.Schema.ConnectionString;
        using (var scope = f.Harness.Provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CancelAsync(f.TaskId, default);
        f.RecordedStops.Killed.ShouldBe([f.SessionId], "PC-16: exactly the explicit requested stop");
        f.Wire.ForceCommands.ShouldBe(0);
        await using var db = f.Db();
        (await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId)).Status.ShouldBe(AgentTaskStatus.Canceled);
        (await db.RunnerSeatReleases.SingleAsync()).ReasonCode.ShouldBe("SettlementTooYoung");
    }

    [Test]
    public async Task Blocked_report_with_running_runner_requires_a_published_park()
    {
        foreach (var syncBlock in new[] { false, true })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, provider: "Codex");
            await f.PrepareContinuationAsync();
            await f.Live!.SubmitAsync("completed blocked task");
            await f.SettleAsync(syncBlock ? "done" : "blocked");
            (await f.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Blocked);
            if (syncBlock)
            {
                var settled = await f.TaskAsync();
                settled.NextStage.ShouldBe(PipelineHandoffKind.Decide);
                // The fixture has no IRemoteSettlementSync; production records that missing
                // dependency in the Decide handoff and Warning, not in FailureReason.
                settled.NextHandoff.ShouldBe("Runner sync blocked (runner_sync_dependency_unavailable): repair the desktop checkout or the task branch "
                    + "on origin, then reply to this task for a fresh completion report.");
                await using var syncDb = f.Db();
                (await syncDb.AgentTaskEvents.Where(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Warning)
                    .Select(e => e.Detail).ToListAsync()).ShouldContain(
                        "Runner sync unavailable: runner_sync_dependency_unavailable. The report is retained and the desktop checkout was left as found; repair it, then reply "
                        + "to this task for a fresh completion report.");
            }
            f.Live.Child.Kills.ShouldBe(0);
            f.Clock.Advance(TimeSpan.FromSeconds(120));
            await f.JobAsync();
            f.Live.Clock.Advance(TimeSpan.FromSeconds(120));
            await f.JobAsync();
            // CARD-1065 D-3 supersedes the publication-free CARD-0667 expectation.
            // Parking is off in this legacy fixture: a report and idle runner alone
            // cannot authorize releasing a Blocked task's unpublished source.
            f.Live.Child.Kills.ShouldBe(0); f.Live.Runtime.LiveSessionCount.ShouldBe(1);
            f.Live.ConditionalCommands.ShouldBe(0);
            f.RecordedStops.Killed.ShouldBeEmpty();
            await using var db = f.Db();
            var row = await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
            row.Status.ShouldBe(SessionStatus.Running);
            (await db.AgentTaskParks.CountAsync()).ShouldBe(0, "parking remains default-off");
            var task = await f.TaskAsync();
            task.Status.ShouldBe(AgentTaskStatus.Blocked); task.Result.ShouldContain("Complete seat report canary.");
            task.WorktreeBranch.ShouldBe("feat/retained"); Directory.Exists(task.WorktreePath).ShouldBeTrue();
        }
    }

    [Test]
    public async Task Failed_settlement_releases_without_success_branch()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(provider: "Codex");
        await f.Live!.SubmitAsync("real failed task");
        await f.SettleAsync("failed");
        (await f.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Failed);
        await using (var db = f.Db()) (await db.RunnerSeatReleases.CountAsync()).ShouldBe(1, "fast hook records debt");
        f.Clock.Advance(TimeSpan.FromSeconds(120));
        await f.SweepAsync();
        f.Live.Clock.Advance(TimeSpan.FromSeconds(120));
        await f.SweepAsync();
        f.Live.Child.Kills.ShouldBe(1); f.RecordedStops.Killed.ShouldBeEmpty();
        (await f.TaskAsync()).Result.ShouldContain("Complete seat report canary.");
    }

    [Test]
    public async Task Settlement_delivery_precedes_release_and_survives_release_fault()
    {
        foreach (var busy in new[] { false, true })
        {
            var margin = new SettlementCommitMargin();
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(configureDb: b => b.AddInterceptors(margin));
            var parent = await f.AddParentAsync(busy);
            margin.Clock = f.Clock;
            Guid? observedNote = null;
            string? observedBody = null;
            f.Wire.AtCommand = async _ =>
            {
                await using var read = f.Db();
                var terminal = await read.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
                terminal.Status.ShouldBe(AgentTaskStatus.Failed);
                terminal.Result.ShouldContain("Complete seat report canary.");
                var obligation = await read.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == f.TaskId);
                obligation.Body.ShouldContain("Complete seat report canary.");
                obligation.ParentSessionId.ShouldBe(parent);
                obligation.SourceEventId.ShouldNotBe(Guid.Empty);
                observedNote = obligation.Id; observedBody = obligation.Body;
            };
            f.Wire.DropReply = true;
            await f.SettleAsync("failed");
            // The fake clock advances at the durable commit, so this is the first settlement
            // hook's command. A later explicit retry cannot conceal an incorrectly early hook.
            margin.Advanced.ShouldBeTrue();
            f.Wire.ConditionalCommands.ShouldBe(1, "PC-71: the actual post-commit fast hook reaches the wire");
            f.Wire.CallbackFailure.ShouldBeNull(); observedNote.ShouldNotBeNull();
            await using var db = f.Db();
            var owed = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == observedNote);
            owed.Body.ShouldBe(observedBody);
            if (busy)
            {
                await f.RestartAsync();
                await f.AttachRecipientAsync(parent);
            }
            await f.FlushAsync(parent);
            if (busy)
            {
                f.Submitted.ShouldBeEmpty("PC-82: completion waits for committed parent TurnEnd");
                await f.EndTurnAsync(parent); await f.FlushAsync(parent);
            }
            await f.ReconcileParentAsync();
            var confirmed = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == owed.Id);
            confirmed.ConfirmedAt.ShouldNotBeNull();
            f.Submitted.ShouldHaveSingleItem().ShouldContain("Complete seat report canary.");
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == parent
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == f.Submitted[0])).ShouldBe(1);
        }
    }

    private sealed class SettlementCommitMargin : SaveChangesInterceptor
    {
        public Microsoft.Extensions.Time.Testing.FakeTimeProvider? Clock { get; set; }
        public bool Advanced { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Advanced && Clock is not null && eventData.Context is { } context
                && context.ChangeTracker.Entries<AgentTaskLandNotification>().Any()
                && context.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Status == AgentTaskStatus.Failed
                    && e.Entity.CompletedAt != null))
            {
                Clock.Advance(TimeSpan.FromSeconds(120));
                Advanced = true;
            }
            return ValueTask.FromResult(result);
        }
    }

    [Test]
    public async Task Parent_receipt_rejects_ack_stale_or_partial_prompt()
    {
        foreach (var shape in new[] { "ack", "wrong", "stale", "partial", "queued" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            var parent = await f.AddParentAsync(busy: true);
            await f.SettleAsync("failed");
            await using var db = f.Db();
            var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == f.TaskId);
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
            if (shape == "stale") await f.NativePromptAsync(parent, row.Body);
            var submitted = new List<string>();
            f.Recipient!.OnSubmitted = async body =>
            {
                submitted.Add(body);
                if (shape is "ack" or "stale") return;
                var text = shape == "wrong" ? "different report" : shape == "partial" ? body[..40] : body;
                await f.NativePromptAsync(parent, text);
                if (shape == "queued") await db.TranscriptEntries.Where(t => t.AgentSessionId == parent && t.Text == text)
                    .ExecuteUpdateAsync(u => u.SetProperty(t => t.Kind, TranscriptKinds.QueuedUserPrompt));
            };
            await f.EndTurnAsync(parent);
            await f.FlushAsync(parent);
            submitted.ShouldHaveSingleItem("one actual queue attempt, no fabricated attempt stamp");
            var attempted = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
            var rendering = TaskCompletionNotification.TryReadDelivery(attempted.CompletionDeliveryJson).ShouldNotBeNull();
            rendering.WireText.ShouldBe(submitted[0]);
            (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == row.Id)).DeliveryAttempts.ShouldBeGreaterThan(0);
            // Model an acknowledged transport independently of complete native receipt. Keep
            // the real queue's rendering, attempt identity, generation and baseline untouched.
            await db.SessionQueuedMessages.Where(m => m.Id == row.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, QueuedMessageStatus.Sent));
            await f.ReconcileParentAsync();
            (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id))
                .ConfirmedAt.ShouldBeNull("PC-74: " + shape);
            // Late native confirmation of the exact submitted bytes finishes the same
            // attempt; recovery must not type the report a second time.
            await f.NativePromptAsync(parent, submitted[0]); await f.ReconcileParentAsync();
            (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id)).ConfirmedAt.ShouldNotBeNull();
            submitted.Count.ShouldBe(1);
        }
    }

    [Test]
    public async Task Attention_contains_release_identity_and_reason()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync();
        var boardId = Guid.NewGuid(); var columnId = Guid.NewGuid(); var cardId = Guid.NewGuid();
        await using (var db = f.Db())
        {
            var project = new Project { Id = Guid.NewGuid(), Name = "release project" };
            db.Projects.Add(project);
            db.Boards.Add(new Board { Id = boardId, ProjectId = project.Id, Name = "release board" });
            db.BoardColumns.Add(new BoardColumn { Id = columnId, BoardId = boardId, Name = "Done" });
            db.Cards.Add(new Card { Id = cardId, BoardId = boardId, BoardColumnId = columnId, Title = "release card" });
            (await db.AgentTasks.SingleAsync()).CardId = cardId;
            await db.SaveChangesAsync();
        }
        var id = (await f.ReleaseAsync())!.Value;
        var item = (await f.AttentionAsync()).Items.Where(i => i.ConditionKey == $"runner-seat-release:{id:D}").ShouldHaveSingleItem();
        item.Kind.ShouldBe(AttentionKind.SessionDisagreement); item.Severity.ShouldBe(AlertSeverity.Warning);
        item.Headline.ShouldBe("Runner seat released"); item.TaskId.ShouldBe(f.TaskId);
        item.SessionId.ShouldBe(f.SessionId); item.AgentId.ShouldBe(f.AgentId);
        item.CardId.ShouldBe(cardId); item.BoardId.ShouldBe(boardId);
        foreach (var identity in new[] { "fixture", f.Directory.StoreId.ToString("D"), f.SessionId.ToString("D"),
                     f.Observation.ExpectedAcceptedStartedAt.ToString("O"), "attempt=1", "Succeeded", "Released" })
            item.Evidence.ShouldContain(identity);

        // A distinct rowless Working seat is a hold, never a successful release count/headline.
        var heldSession = Guid.NewGuid();
        f.Directory.FeaturesOverride = [RunnerCapabilityFeatures.TerminalSeatReleaseV1, RunnerCapabilityFeatures.TerminalSeatDeliveryEvidenceV1];
        f.Directory.Inventory = () => Task.FromResult<RunnerInventory>(new RunnerInventory.Available(
            [new(heldSession, null, f.Now.AddHours(-1), "Running", null, default, 0, AcceptedStartedAt: f.Now.AddHours(-1))]));
        f.Wire.Qualified = f.Wire.Qualified with { Status = TerminalSeatQualificationStatus.Working,
            Transcript = f.Wire.Qualified.Transcript with { Verdict = TerminalTranscriptVerdict.Working } };
        var deferred = await f.DiscoverAsync();
        deferred.Released.ShouldBe(0, "G-77: deferred-only discovery must report Released=0");
        var heldId = deferred.Candidates.ShouldHaveSingleItem().ReleaseId.ShouldNotBeNull();
        var held = (await f.AttentionAsync()).Items.Single(i => i.ConditionKey == $"runner-seat-release:{heldId:D}");
        held.Headline.ShouldNotBe("Runner seat released"); held.Evidence.ShouldContain("Working");
        held.BoardId.ShouldBeNull(); held.CardId.ShouldBeNull();
        f.Clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromTicks(1));
        (await f.AttentionAsync()).Items.ShouldContain(i => i.ConditionKey == $"runner-seat-release:{id:D}");
        f.Clock.Advance(TimeSpan.FromTicks(2));
        var expired = (await f.AttentionAsync()).Items;
        expired.ShouldNotContain(i => i.ConditionKey == $"runner-seat-release:{id:D}");
        expired.ShouldContain(i => i.ConditionKey == $"runner-seat-release:{heldId:D}", "G-88: unresolved debt never ages out");
        f.Wire.ConditionalCommands.ShouldBe(1); f.Wire.ForceCommands.ShouldBe(0);
    }

    [Test]
    public async Task Long_answer_keeps_complete_content_and_spill_receipt()
    {
        foreach (var length in new[] { 3999, 4000, 4001, 12000 })
        {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
        var answer = length == 12000
            ? string.Concat(Enumerable.Repeat("retain λ 日本語 😀\r\n", 650)) + "FINAL-ANSWER-CANARY"
            : new string('x', length - "FINAL-ANSWER-CANARY".Length) + "FINAL-ANSWER-CANARY";
        await f.SeedLegacyBlockedReleaseAsync();
        (await f.TryAnswerAsync(answer)).ShouldBeNull("the complete Unicode answer must be accepted without a bounded-detail database error");
        var accepted = await f.TaskAsync();
        f.Launches.OnLaunch = id => f.AttachRecipientAsync(id, busy: true).GetAwaiter().GetResult();
        await f.DispatchAsync();
        var queued = (await f.AnswerQueueAsync())!;
        queued.ShouldNotBeNull();
        queued.RemoteSpillBody.ShouldNotBeNull();
        queued.RemoteSpillBody.ShouldContain(answer.ReplaceLineEndings("\n"));
        queued.RemoteSpillBody.Split("FINAL-ANSWER-CANARY").Length.ShouldBe(2);
        queued.RemoteSpillBody.ShouldContain($"Accepted answer: {accepted.ReleasedSeatAnswerId:D}");
        f.Recipient!.Inputs.ShouldBeEmpty();
        await f.RestartAsync();
        await f.AttachRecipientAsync(queued.AgentSessionId);
        await f.EndTurnAsync(queued.AgentSessionId);
        await f.FlushAsync(queued.AgentSessionId);
        await f.DispatchAsync();
        var file = Path.Combine(f.Harness.TempRoot, queued.RemoteSpillRelativePath!);
        (await File.ReadAllTextAsync(file)).ShouldBe(queued.RemoteSpillBody);
        f.Submitted.ShouldBe(new[] { queued.Body });
        f.Recipient!.Inputs.Last().ShouldBe("\r");
        (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBeNull("only the complete pointer receipt finishes answer recovery");
        }
    }

    [Test]
    public async Task Accepted_answer_after_release_is_delivered_once()
    {
        foreach (var boundary in new[] { "accept-before", "accept-after", "attempt-before", "attempt-after",
                     "queue-before", "queue-after", "delivery-stamp", "verdict-before", "complete-before", "release-ambiguous", "idle" })
        {
            var cut = new DeliverySaveCut { Boundary = boundary };
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked,
                configureDb: options => options.AddInterceptors(cut, new DeliveryCommitCut(cut)));
            await f.SeedLegacyBlockedReleaseAsync(unresolved: boundary == "release-ambiguous");
            if (boundary == "release-ambiguous")
                f.Directory.Inventory = () => Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
            f.Wire.AutomaticEnabled = false;
            f.Harness.Provider.GetRequiredService<IOptions<TerminalRunnerSeatReleaseOptions>>().Value.AutomaticEnabled = false;
            cut.Armed = true;
            if (boundary == "accept-after") f.Harness.EventBus.ThrowOnceOnEvent = "AgentTaskChanged";
            await f.TryAnswerAsync("recover exactly once");
            if (boundary == "accept-before")
            {
                (await f.TaskAsync()).ReleasedSeatAnswerId.ShouldBeNull();
                f.Launches.Calls.ShouldBeEmpty();
                await f.AnswerAsync("recover exactly once");
            }
            var answerId = (await f.TaskAsync()).ReleasedSeatAnswerId;
            answerId.ShouldNotBeNull();
            // Restart reconstructs scoped and singleton services over the same migrated clone.
            await f.RestartAsync();
            f.Launches.OnLaunch = id => f.AttachRecipientAsync(id, busy: boundary != "idle").GetAwaiter().GetResult();
            await f.DispatchAsync();
            var target = await f.TaskAsync();
            target.Attempt.ShouldBe(2, boundary);
            target.AgentSessionId.ShouldNotBeNull(boundary);
            if (boundary != "idle") f.Recipient?.Inputs.ShouldBeEmpty("busy recipient must see no input before its committed turn end");
            (await f.TaskAsync()).ReleasedSeatAnswerId.ShouldBe(answerId, boundary);
            await f.RestartAsync();
            await f.AttachRecipientAsync(target.AgentSessionId!.Value);
            await f.DispatchAsync(); // restores a lost enqueue or acknowledgement
            var queued = await f.AnswerQueueAsync();
            queued.ShouldNotBeNull(boundary);
            await f.EndTurnAsync(target.AgentSessionId.Value);
            try { await f.FlushAsync(target.AgentSessionId.Value); }
            catch (InvalidOperationException) when (cut.Hit) { }
            await f.RestartAsync();
            await f.AttachRecipientAsync(target.AgentSessionId.Value);
            if (boundary is "delivery-stamp" or "verdict-before")
            {
                // An interrupted Sent claim is deliberately invisible to retyping until the
                // ordinary confirm + grace + clock-tolerance window expires. Preserve it,
                // then advance the injected clock; never change the production window.
                var interrupted = (await f.AnswerQueueAsync())!;
                interrupted.Status.ShouldBe(QueuedMessageStatus.Sent, boundary);
                interrupted.DeliveryVerdict.ShouldBeNull(boundary);
                var settings = f.Harness.Provider.GetRequiredService<IOptions<SupervisionSettings>>().Value.DeliveryVerification;
                f.Clock.Advance(TimeSpan.FromSeconds(settings.TranscriptConfirmTimeoutSeconds
                    + settings.PostFailureConfirmGraceSeconds
                    + settings.UnobservableBaselineConfirmClockToleranceSeconds).Add(TimeSpan.FromTicks(1)));
            }
            await f.FlushAsync(target.AgentSessionId.Value);
            await f.DispatchAsync();
            if (boundary == "complete-before") await f.DispatchAsync();
            if (boundary is not ("accept-after" or "release-ambiguous" or "idle")) cut.Hit.ShouldBeTrue($"must reach {boundary}");
            f.Submitted.Count.ShouldBe(1, boundary);
            (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBeNull($"{boundary}: matching prompt completes recovery");
            await using var db = f.Db();
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == target.AgentSessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == queued!.Body)).ShouldBe(1, boundary);
            (await db.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == f.TaskId)).ShouldBe(1, boundary);
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.SessionId
                && m.ExecutionTaskId == f.TaskId)).ShouldBe(0, "no delivery to the released seat");
            f.Wire.ConditionalCommands.ShouldBe(0, "historical answer recovery sends no release commands while disabled");
            (await f.TaskAsync()).ReleasedSeatAnswerId.ShouldBeNull();
            (await f.TaskAsync()).ReleasedSeatAnswerTargetAttempt.ShouldBeNull();
        }
    }

    [Test]
    public async Task Answer_receipt_rejects_ack_stale_or_partial_prompt()
    {
        foreach (var shape in new[] { "ack", "wrong", "partial", "stale", "wrong-session", "queued", "assistant", "generation" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
            await f.SeedLegacyBlockedReleaseAsync(); await f.AnswerAsync("receipt canary");
            f.Launches.OnLaunch = id => f.AttachRecipientAsync(id, busy: true).GetAwaiter().GetResult();
            await f.DispatchAsync();
            var row = (await f.AnswerQueueAsync())!;
            var task = await f.TaskAsync();
            await using var db = f.Db();
            var generation = (await db.AgentSessions.SingleAsync(s => s.Id == row.AgentSessionId)).StartedAt;
            var text = shape == "wrong" ? "different prompt" : shape == "partial" ? row.Body[..100] : row.Body;
            if (shape != "ack")
            {
                await f.NativePromptAsync(shape == "wrong-session" ? f.SessionId : row.AgentSessionId, text);
                if (shape is "queued" or "assistant")
                    await db.TranscriptEntries.Where(t => t.AgentSessionId == row.AgentSessionId && t.Text == text)
                        .ExecuteUpdateAsync(u => u.SetProperty(t => t.Kind,
                            shape == "queued" ? TranscriptKinds.QueuedUserPrompt : TranscriptKinds.AssistantText));
            }
            var max = await db.TranscriptEntries.Where(t => t.AgentSessionId == row.AgentSessionId).MaxAsync(t => t.Sequence);
            await db.SessionQueuedMessages.Where(m => m.Id == row.Id).ExecuteUpdateAsync(u => u
                .SetProperty(m => m.Status, QueuedMessageStatus.Sent)
                .SetProperty(m => m.DeliveryAttempts, 1)
                .SetProperty(m => m.DeliveryVerdict, DeliveryVerdict.Delivered)
                .SetProperty(m => m.LastDeliveryStartedAt, f.Now)
                .SetProperty(m => m.LastDeliveryGeneration, shape == "generation" ? generation.AddSeconds(-1) : generation)
                .SetProperty(m => m.LastDeliveryBaselineSequence, shape == "stale" ? max : 1));
            await f.DispatchAsync();
            (await f.TaskAsync()).ReleasedSeatAnswerId.ShouldBe(task.ReleasedSeatAnswerId, shape);
            (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBe("receipt canary", shape);
            // The positive witness comes from actual queue submit bytes, normalizer and ingest.
            await db.SessionQueuedMessages.Where(m => m.Id == row.Id).ExecuteUpdateAsync(u => u
                .SetProperty(m => m.Status, QueuedMessageStatus.Pending)
                .SetProperty(m => m.DeliveryAttempts, 0)
                .SetProperty(m => m.DeliveryVerdict, (DeliveryVerdict?)null)
                .SetProperty(m => m.LastDeliveryStartedAt, (DateTime?)null)
                .SetProperty(m => m.LastDeliveryGeneration, (DateTime?)null)
                .SetProperty(m => m.LastDeliveryBaselineSequence, (long?)null));
            await f.EndTurnAsync(row.AgentSessionId);
            await f.FlushAsync(row.AgentSessionId);
            await f.DispatchAsync();
            (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBeNull($"{shape}: real complete receipt finishes recovery");
        }
    }

    [Test]
    public async Task Legacy_ambiguous_answer_requires_fresh_absence_without_another_release()
    {
        foreach (var hold in new[] { "live", "unknown", "replacement", "unavailable", "working" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
            await f.SeedLegacyBlockedReleaseAsync(unresolved: true);
            await f.AnswerAsync("retain historical answer");
            f.Harness.Provider.GetRequiredService<IOptions<TerminalRunnerSeatReleaseOptions>>().Value.AutomaticEnabled = false;
            var seat = new SessionRunnerSessionDto(f.SessionId, 123, f.Observation.ExpectedAcceptedStartedAt,
                hold == "replacement" ? "Exited" : "Running", null, "", 12,
                AcceptedStartedAt: hold == "replacement" ? f.Now : f.Observation.ExpectedAcceptedStartedAt);
            f.Directory.Inventory = () => Task.FromResult<RunnerInventory>(hold switch
            {
                "unknown" => new RunnerInventory.Unavailable("fixture inventory unavailable"),
                "live" or "replacement" => new RunnerInventory.Available([seat]),
                _ => new RunnerInventory.Available([])
            });
            if (hold == "unavailable") f.Directory.Available = false;
            if (hold == "working") await f.IngestAsync(TranscriptKinds.UserPrompt, "still working", f.Now);
            await f.RecoverAttentionAsync();
            await using var db = f.Db();
            (await db.RunnerSeatReleases.SingleAsync()).State.ShouldBe(RunnerSeatReleaseState.Unresolved, hold);
            (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBe("retain historical answer", hold);
            f.Launches.Calls.ShouldBeEmpty();
            f.Wire.ConditionalCommands.ShouldBe(0, "legacy recovery cannot send a new release");

            f.Directory.Available = true;
            f.Directory.Inventory = () => Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
            if (hold == "working") await f.IngestAsync(TranscriptKinds.TurnEnd, null, f.Now);
            await f.RecoverAttentionAsync();
            var recovered = await db.RunnerSeatReleases.AsNoTracking().SingleAsync();
            recovered.State.ShouldBe(RunnerSeatReleaseState.Confirmed, $"{hold}: fresh absence recovers the historical action");
            recovered.OutcomeCode.ShouldBe(nameof(TerminalSeatReleaseOutcome.AlreadyAbsent));
            f.Wire.ConditionalCommands.ShouldBe(0);
            f.RecordedStops.Killed.ShouldBeEmpty();
            (await db.AgentTaskParks.CountAsync()).ShouldBe(0);
        }
    }

    private sealed class DeliverySaveCut : SaveChangesInterceptor
    {
        public required string Boundary { get; init; }
        public bool Armed { get; set; }
        public bool Hit { get; private set; }
        private bool Matches(DbContext db) => Boundary switch
        {
            "accept-before" => db.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Attempt == 1 && e.Entity.ReleasedSeatAnswerId != null),
            "attempt-before" or "attempt-after" => db.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Attempt == 2 && e.Entity.Status == AgentTaskStatus.Queued),
            "queue-before" or "queue-after" => db.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.Entity.ExecutionTaskId != null),
            "delivery-stamp" => db.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.Entity.DeliveryAttempts > 0 && e.Entity.DeliveryVerdict == null),
            "verdict-before" => db.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.Entity.DeliveryVerdict == DeliveryVerdict.Delivered),
            "complete-before" => db.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Attempt == 2 && e.Entity.ReleasedSeatAnswer == null),
            _ => false
        };
        private void Cut(DbContext db, bool after)
        {
            if (!Armed || Hit || (Boundary.EndsWith("after") || Boundary == "delivery-stamp") != after || !Matches(db)) return;
            if (Boundary == "attempt-after") { CommitPending = true; return; }
            Hit = true; throw new InvalidOperationException($"injected {Boundary}");
        }
        public bool CommitPending { get; private set; }
        public void CutCommit()
        {
            if (!CommitPending || Hit) return;
            Hit = true; throw new InvalidOperationException("injected attempt-after commit");
        }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Cut(data.Context!, false); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result,
            CancellationToken cancellationToken = default)
        { Cut(data.Context!, true); return ValueTask.FromResult(result); }
    }

    private sealed class DeliveryCommitCut(DeliverySaveCut cut) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        { cut.CutCommit(); return Task.CompletedTask; }
    }

    [Test]
    public async Task Answer_racing_release_preserves_one_owner()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await f.PreparePublishedBlockedSourceAsync();
        var recipientGate = f.Harness.Queue.GetLock(f.SessionId);
        await recipientGate.WaitAsync();
        try
        {
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.PendingDelivery,
                "release must observe the queue's actual gate, not an independent semaphore");
            f.Wire.ConditionalCommands.ShouldBe(0);
        }
        finally { recipientGate.Release(); }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = f.ReleaseAsync(async (at, _) =>
        {
            if (at != "BeforeDispatch") return;
            entered.SetResult(); await resume.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var answer = f.TryAnswerAsync("race answer");
        try
        {
            (await f.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Blocked, "answer cannot mutate while release owns the queue gate");
        }
        finally { resume.TrySetResult(); await release; await answer; }
        var duplicate = await Task.WhenAll(f.TryAnswerAsync("race answer"), f.TryAnswerAsync("race answer"));
        var task = await f.TaskAsync();
        task.Attempt.ShouldBe(2, "one accepted answer produces exactly one target attempt");
        task.ReleasedSeatAnswer.ShouldBe("race answer");
        task.ReleasedSeatAnswerTargetAttempt.ShouldBe(2);
        await using var db = f.Db();
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.SessionId)).ShouldBe(0);

        await using var uncertain = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await uncertain.PreparePublishedBlockedSourceAsync();
        uncertain.Wire.DropReply = true;
        await uncertain.ReleaseAsync();
        await uncertain.TryAnswerAsync("persist through an ambiguous release");
        var held = await uncertain.TaskAsync();
        held.ReleasedSeatAnswer.ShouldBe("persist through an ambiguous release");
        held.Status.ShouldBe(AgentTaskStatus.Blocked); held.Attempt.ShouldBe(1);
        AgentTaskService.MatchesReleasedAnswerRevision(held, held.ConcurrencyToken, held.Attempt,
            held.ReleasedSeatAnswerId!.Value, held.ReleasedSeatAnswerReleaseId!.Value).ShouldBeTrue();
        AgentTaskService.MatchesReleasedAnswerRevision(held, Guid.NewGuid(), held.Attempt,
            held.ReleasedSeatAnswerId.Value, held.ReleasedSeatAnswerReleaseId.Value).ShouldBeFalse("revision alone fences an otherwise eligible answer");
        await using var heldDb = uncertain.Db();
        (await heldDb.SessionQueuedMessages.CountAsync()).ShouldBe(0, "no input to an uncertain corpse");

        await using var early = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await early.PreparePublishedBlockedSourceAsync();
        var reserved = await early.RunAsync();
        await early.IngestAsync(TranscriptKinds.UserPrompt, "still busy", early.Now);
        await early.AnswerAsync("answer before dispatch");
        using (var scope = early.Harness.Provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>()
                .AdvanceAsync(reserved.ReleaseId!.Value, early.Observation, default);
        early.Wire.ConditionalCommands.ShouldBe(0);
        (await early.TaskAsync()).Attempt.ShouldBe(1);
    }

    [Test]
    public async Task Answer_recovery_preserves_round_and_admission_guards()
    {
        var cut = new AnswerSaveCut();
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true,
            configureDb: options => options.AddInterceptors(cut));
        await f.PreparePublishedBlockedSourceAsync();
        await f.ReleaseAsync();
        var before = await f.TaskAsync();
        (await f.TryAnswerAsync("stale", round: 2)).ShouldBeOfType<ConflictException>();
        var stale = await f.TaskAsync(); stale.Attempt.ShouldBe(before.Attempt);
        stale.ReleasedSeatAnswerId.ShouldBeNull(); stale.ConcurrencyToken.ShouldBe(before.ConcurrencyToken);
        cut.Armed = true;
        await f.TryAnswerAsync("atomic answer");
        var after = await f.TaskAsync();
        cut.Hit.ShouldBeTrue("the test reaches the new-attempt save boundary");
        after.Attempt.ShouldBe(1, "transaction rolls back the interrupted new attempt");
        after.ReleasedSeatAnswer.ShouldBe("atomic answer", "accepted input survives a failed admission transaction");
        cut.Armed = false;
        await f.AnswerAsync("atomic answer");
        (await f.TaskAsync()).Attempt.ShouldBe(2);
    }

    [Test]
    public async Task Answer_stop_bypass_requires_the_exact_release_receipt()
    {
        foreach (var shape in new[] { "confirmed", "missing-session", "wrong-attempt", "wrong-store", "wrong-generation" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
            await f.PreparePublishedBlockedSourceAsync();
            if (shape != "missing-session") await f.ReleaseAsync();
            await using var db = f.Db();
            if (shape == "missing-session") await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteDeleteAsync();
            if (shape == "wrong-attempt") await f.EditAsync((t, _) => t.Attempt++);
            if (shape is "wrong-store" or "wrong-generation")
            {
                var session = await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
                if (shape == "wrong-store") session.RunnerStoreId = Guid.NewGuid();
                else session.StartedAt = session.StartedAt.AddSeconds(1);
                await db.SaveChangesAsync();
            }
            await f.RetryAsync();
            f.RecordedStops.Killed.Count.ShouldBe(shape == "confirmed" ? 0 : 1, shape);
        }
    }

    [Test]
    public async Task Answer_admission_guards_preserve_the_accepted_reply()
    {
        foreach (var guard in new[] { "quota", "availability", "commit", "capacity", "workspace", "preference" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
            await f.SeedLegacyBlockedReleaseAsync();
            await using var db = f.Db();
            var before = await f.TaskAsync();
            if (guard == "availability")
                db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold { Id = Guid.NewGuid(), Kind = before.AgentKind,
                    ModelAlias = "*", Source = ModelAvailabilitySource.Manual, HitAt = f.Now, Reason = "held preference" });
            if (guard == "quota")
                db.SubscriptionUsageSamples.Add(new SubscriptionUsageSample { Id = Guid.NewGuid(), Provider = before.AgentKind,
                    SubscriptionKey = before.AgentKind.ToString(), AgentSessionId = f.SessionId, RemainingPercent = 1,
                    ParseStatus = SubscriptionUsageParseStatus.Parsed, ObservedAt = f.Now, ResetsAt = f.Now.AddDays(3) });
            if (guard == "commit")
                db.AgentTaskEvents.Add(new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = f.TaskId,
                    Type = AgentTaskEventType.CommitRecoveryStarted, Detail = "settlement-to-preserve", At = f.Now });
            await db.SaveChangesAsync();
            var refusal = await f.TryAnswerAsync("retain admission answer");
            var accepted = await f.TaskAsync();
            accepted.ReleasedSeatAnswer.ShouldBe("retain admission answer", guard);
            f.Launches.Calls.ShouldBeEmpty("the reply handler never launches a provider");
            f.RecordedStops.Killed.ShouldBeEmpty();
            if (guard is "quota" or "availability" or "commit")
            {
                refusal.ShouldNotBeNull(guard);
                accepted.Attempt.ShouldBe(1, guard);
                if (guard == "commit")
                    (await db.AgentTaskEvents.CountAsync(e => e.Type == AgentTaskEventType.CommitRecoveryStarted)).ShouldBe(1);
                // Removing this one guard makes the same persisted answer resumable through Retry.
                await db.ModelAvailabilityHolds.ExecuteDeleteAsync();
                await db.SubscriptionUsageSamples.ExecuteDeleteAsync();
                await db.AgentTaskEvents.Where(e => e.Type == AgentTaskEventType.CommitRecoveryStarted).ExecuteDeleteAsync();
                await f.RetryAsync();
            }
            else
            {
                refusal.ShouldBeNull(); accepted.Attempt.ShouldBe(2);
                if (guard == "capacity") f.Directory.Capacity = 0;
                if (guard == "preference") f.Directory.RefuseNewWork = true;
                if (guard == "workspace")
                {
                    using var scope = f.Harness.Provider.CreateScope();
                    var key = WorkspaceReservationKey.ForTask(before.WorktreePath, before.WorkingDirectory,
                        before.WorktreeBranch, before.RepoPath);
                    var fence = await scope.ServiceProvider.GetRequiredService<IWorkspaceReservationJournal>()
                        .TryAdmitConsumerAsync(new(key, WorkspaceReservationKind.HistoricalFence, Guid.NewGuid()), default);
                    fence.Accepted.ShouldBeTrue();
                }
                await f.DispatchAsync();
                f.Launches.Calls.ShouldBeEmpty(guard);
                var held = await f.TaskAsync();
                held.ReleasedSeatAnswer.ShouldBe("retain admission answer");
                held.AgentKind.ShouldBe(before.AgentKind); held.RunnerId.ShouldBe(before.RunnerId);
                if (guard is "capacity" or "preference") held.Status.ShouldBe(AgentTaskStatus.Queued, guard);
                f.Directory.Capacity = 10; f.Directory.RefuseNewWork = false;
                await db.WorkspaceUseReservations.Where(r => r.Kind == WorkspaceReservationKind.HistoricalFence).ExecuteDeleteAsync();
                // Workspace refusal can settle Failed; put the same accepted target back on its
                // queue for the positive admission witness, without manufacturing a receipt.
                if (held.Status == AgentTaskStatus.Failed)
                    await f.EditAsync((t, _) => t.Status = AgentTaskStatus.Queued);
            }
            await f.DispatchAsync();
            f.Launches.Calls.Count.ShouldBe(1, $"{guard}: otherwise eligible reaches the real launch boundary");
            (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBe("retain admission answer");
        }
    }

    [Test]
    public async Task Answer_fields_do_not_leak_into_a_later_attempt()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await f.PreparePublishedBlockedSourceAsync();
        await f.ReleaseAsync(); await f.TryAnswerAsync("answer-only-canary");
        var accepted = await f.TaskAsync();
        DelegationReportFormatter.BuildBrief(accepted, new DelegationSettings()).ShouldContain("answer-only-canary");
        accepted.Attempt++;
        DelegationReportFormatter.BuildBrief(accepted, new DelegationSettings()).ShouldNotContain("answer-only-canary");
        await f.EditAsync((t, _) => t.Status = AgentTaskStatus.Failed);
        await f.RetryAsync();
        var retried = await f.TaskAsync();
        retried.ReleasedSeatAnswer.ShouldBeNull(); retried.ReleasedSeatAnswerId.ShouldBeNull();
        DelegationReportFormatter.BuildBrief(retried, new DelegationSettings()).ShouldNotContain("answer-only-canary");
    }

    [Test]
    public async Task Answer_keeps_workspace_and_report_context()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await f.EditAsync((t, _) => { t.WorktreePath = f.Harness.TempRoot; t.WorktreeBranch = "feat/retained";
            t.WorktreeBaseRef = "master"; t.Result = "retained report"; t.ResultFilePath = "report.md"; });
        await f.PreparePublishedBlockedSourceAsync();
        var before = await f.TaskAsync();
        await f.ReleaseAsync();
        f.Harness.Provider.GetRequiredService<IOptions<TerminalRunnerSeatReleaseOptions>>().Value.AutomaticEnabled = false;
        await f.TryAnswerAsync("continue retained work");
        var task = await f.TaskAsync();
        task.Attempt.ShouldBe(2); task.Status.ShouldBe(AgentTaskStatus.Queued); task.AgentSessionId.ShouldBeNull();
        task.WorktreePath.ShouldBe(before.WorktreePath); task.WorktreeBranch.ShouldBe(before.WorktreeBranch);
        task.WorktreeBaseRef.ShouldBe(before.WorktreeBaseRef); task.Result.ShouldBe(before.Result);
        task.ResultFilePath.ShouldBe(before.ResultFilePath); task.RootTaskId.ShouldBe(before.RootTaskId);
        task.RunnerId.ShouldBe(before.RunnerId); task.CardId.ShouldBe(before.CardId);
        f.RecordedStops.Killed.ShouldBeEmpty();
        f.Launches.Calls.ShouldBeEmpty();
        await using var db = f.Db();
        (await db.WorkspaceUseReservations.CountAsync(r => r.TaskId == task.Id && r.Active
            && r.Kind == WorkspaceReservationKind.Launch)).ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Reply_on_live_local_or_warm_session_is_unchanged()
    {
        foreach (var shape in new[] { "remote-live", "local", "warm" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
            if (shape != "remote-live")
            {
                await using var db = f.Db();
                var session = await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
                session.RunnerId = null; session.RunnerStoreId = null; session.RunnerCwd = null;
                await db.SaveChangesAsync();
                await f.EditAsync((t, a) => { t.RunnerId = null; t.Workspace = WorkspaceMode.Shared;
                    if (shape == "warm") a.PoolIdleSince = f.Now; });
            }
            await f.IngestAsync(TranscriptKinds.UserPrompt, "busy", f.Now);
            await f.AnswerAsync("same conversation");
            var task = await f.TaskAsync(); task.Attempt.ShouldBe(1); task.AgentSessionId.ShouldBe(f.SessionId);
            task.Status.ShouldBe(AgentTaskStatus.Working); task.ReleasedSeatAnswerId.ShouldBeNull();
            await using var read = f.Db();
            (await read.SessionQueuedMessages.SingleAsync()).Body.ShouldContain("same conversation");
            f.RecordedStops.Killed.ShouldBeEmpty();
        }
    }

    private sealed class AnswerSaveCut : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool Hit { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && data.Context!.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Attempt == 2))
            { Hit = true; throw new InvalidOperationException("answer save cut"); }
            return ValueTask.FromResult(result);
        }
    }

    [Test]
    [Arguments(AgentTaskStatus.Succeeded)]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    [Arguments(AgentTaskStatus.Blocked)]
    public async Task Completed_attempt_registers_release_debt(AgentTaskStatus status)
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(status, parking: status == AgentTaskStatus.Blocked);
        if (status == AgentTaskStatus.Blocked) await f.PreparePublishedBlockedSourceAsync();
        await f.RunAsync();
        await f.RunAsync();
        await using var db = f.Db();
        var rows = await db.RunnerSeatReleases.ToListAsync();
        rows.Count.ShouldBe(1, $"{status} ledger count");
        rows[0].TaskId.ShouldBe(f.TaskId); rows[0].Attempt.ShouldBe(1);
        rows[0].RunnerStoreId.ShouldBe(f.Directory.StoreId); rows[0].SessionId.ShouldBe(f.SessionId);
        rows[0].AcceptedStartedAt.ShouldBe(f.Observation.ExpectedAcceptedStartedAt);
        rows[0].SettlementRevision.ShouldBe((await db.AgentTasks.SingleAsync()).ConcurrencyToken);
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        // The one migration includes all nullable answer fields and permits full Unicode input.
        var task = await db.AgentTasks.SingleAsync();
        task.ReleasedSeatAnswer.ShouldBeNull(); task.ReleasedSeatAnswerId.ShouldBeNull();
        task.ReleasedSeatAnswerRoundId.ShouldBeNull(); task.ReleasedSeatAnswerReleaseId.ShouldBeNull();
        task.ReleasedSeatAnswerTargetAttempt.ShouldBeNull(); task.ReleasedSeatAnswerAcceptedAt.ShouldBeNull();
        var body = new string('界', 5001);
        task.ReleasedSeatAnswer = body; task.ReleasedSeatAnswerId = Guid.NewGuid();
        task.ReleasedSeatAnswerRoundId = Guid.NewGuid(); task.ReleasedSeatAnswerReleaseId = rows[0].Id;
        task.ReleasedSeatAnswerTargetAttempt = 2; task.ReleasedSeatAnswerAcceptedAt = f.Now;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        (await db.AgentTasks.SingleAsync()).ReleasedSeatAnswer.ShouldBe(body);
        // No FK cascade may erase rowless custody.
        await db.AgentTasks.ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteDeleteAsync();
        (await db.RunnerSeatReleases.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task Incomplete_settlements_never_authorize_release()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync();
        await f.EditAsync((task, _) => { task.Status = AgentTaskStatus.Working; task.CompletedAt = null; });
        await using (var writer = f.Db())
        {
            await using var tx = await writer.Database.BeginTransactionAsync();
            var t = await writer.AgentTasks.SingleAsync(); t.Status = AgentTaskStatus.Succeeded; t.CompletedAt = f.Now.AddMinutes(-3);
            await writer.SaveChangesAsync();
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.IncompleteAttempt, "uncommitted settlement");
            await tx.RollbackAsync();
        }
        await f.EditAsync((t, _) => t.Status = AgentTaskStatus.Succeeded);
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.IncompleteAttempt, "null CompletedAt");
        await f.EditAsync((t, _) => { t.Status = AgentTaskStatus.Blocked; t.CompletedAt = f.Now.AddMinutes(-3); t.Result = null; t.ReportEvidence = AgentTaskReportEvidence.Legacy; });
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.IncompleteReport, "routing hold");
        f.Wire.ConditionalCommands.ShouldBe(0);
        await using var db = f.Db(); (await db.RunnerSeatReleases.CountAsync()).ShouldBe(0);
        await f.EditAsync((t, _) => { t.Result = "runner-sync blocked report"; t.ReportEvidence = AgentTaskReportEvidence.Marked; });
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.PublicationRequired,
            "CARD-1065 D-3: a completed marked Blocked report cannot authorize release without a published park, even with parking off");
        f.Wire.ConditionalCommands.ShouldBe(0);
        (await db.RunnerSeatReleases.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Unsettled_blocked_or_queued_owner_is_preserved()
    {
        foreach (var bySession in new[] { true, false })
        foreach (var status in new[] { AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            await using var db = f.Db();
            var owner = new AgentTask { Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Status = status,
                AgentSessionId = bySession ? f.SessionId : Guid.NewGuid(), AgentId = bySession ? Guid.NewGuid() : f.AgentId,
                CreatedAt = f.Now };
            db.AgentTasks.Add(owner); await db.SaveChangesAsync();
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Owned, $"{status} owner by {(bySession ? "session" : "agent")}");
            f.Wire.ConditionalCommands.ShouldBe(0);
            (await db.RunnerSeatReleases.CountAsync(r => r.ActionId != null)).ShouldBe(0);
            db.AgentTasks.Remove(owner); await db.SaveChangesAsync();
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved, "otherwise eligible");
        }
        await using var noAgent = await RunnerSeatReleaseFixture.CreateAsync();
        await noAgent.EditAsync((t, _) => t.AgentId = null);
        (await noAgent.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved, "null agent is not abandonment authority or a veto");
    }

    [Test]
    public async Task Standing_warm_and_verification_owners_are_preserved()
    {
        var variants = new (TerminalRunnerSeatDecision Decision, Action<AgentTask, Agent> Edit)[]
        {
            (TerminalRunnerSeatDecision.StandingOwner, (_, a) => a.IsPoolDelegate = false),
            (TerminalRunnerSeatDecision.AlwaysOnOwner, (_, a) => a.AlwaysOn = true),
            (TerminalRunnerSeatDecision.BoardOwner, (_, a) => a.BoardId = Guid.NewGuid()),
            (TerminalRunnerSeatDecision.SpecialistOwner, (_, a) => a.StandingSpecialistRole = AgentTaskRole.Check),
            (TerminalRunnerSeatDecision.SpecialistOwner, (_, a) => a.StandingSpecialistOwnerId = Guid.NewGuid()),
            (TerminalRunnerSeatDecision.SpecialistOwner, (t, _) => t.Role = AgentTaskRole.Check),
            (TerminalRunnerSeatDecision.WarmPool, (t, a) => { t.Workspace = WorkspaceMode.Shared; a.PoolIdleSince = DateTime.UtcNow; }),
            (TerminalRunnerSeatDecision.VerificationOwner, (_, _) => { }),
        };
        foreach (var variant in variants)
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(sourced: variant.Decision == TerminalRunnerSeatDecision.VerificationOwner);
            // A real board satisfies the FK; other variants each change only their own guard.
            await using var db = f.Db();
            if (variant.Decision == TerminalRunnerSeatDecision.BoardOwner)
            {
                var project = new Project { Id = Guid.NewGuid(), Name = "seat ownership project" };
                db.Projects.Add(project); await db.SaveChangesAsync();
                var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "seat owner" };
                db.Boards.Add(board); await db.SaveChangesAsync();
                await f.EditAsync((_, a) => a.BoardId = board.Id);
            }
            else await f.EditAsync(variant.Edit);
            (await f.RunAsync()).Decision.ShouldBe(variant.Decision);
            f.Wire.ConditionalCommands.ShouldBe(0);
            (await db.RunnerSeatReleases.CountAsync(r => r.ActionId != null)).ShouldBe(0);
            (await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Running);
            (await db.Agents.CountAsync(a => a.Id == f.AgentId)).ShouldBe(1);
        }
        await using var eligible = await RunnerSeatReleaseFixture.CreateAsync();
        (await eligible.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved);
    }

    [Test]
    public async Task Settlement_age_has_its_own_safety_margin()
    {
        foreach (var seconds in new[] { 0d, 119.999, 120, 120.001 })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            await f.EditAsync((t, _) => t.CompletedAt = f.Now.AddSeconds(-seconds));
            (await f.RunAsync()).Decision.ShouldBe(seconds < 120 ? TerminalRunnerSeatDecision.SettlementTooYoung : TerminalRunnerSeatDecision.Reserved,
                $"server settlement age {seconds}; runner already qualified with an independent clock");
            f.Wire.ConditionalCommands.ShouldBe(0); // S3a stops at reservation.
        }
    }

    [Test]
    public async Task Pending_delivery_prevents_release()
    {
        var inputs = new (string Kind, QueuedMessageOrigin Origin)[]
        {
            ("brief", QueuedMessageOrigin.Delegation), ("answer", QueuedMessageOrigin.Delegation),
            ("channel", QueuedMessageOrigin.Channel), ("mention", QueuedMessageOrigin.Mention),
            ("completion", QueuedMessageOrigin.Delegation), ("continuation", QueuedMessageOrigin.System),
            ("recovery", QueuedMessageOrigin.System),
        };
        foreach (var input in inputs)
        foreach (var shape in new[] { "pending", "attempted", "held" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            await using var db = f.Db();
            var message = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.SessionId,
                Body = $"{input.Kind}: complete delivery canary", Origin = input.Origin, CreatedAt = f.Now,
                Status = shape == "attempted" ? QueuedMessageStatus.Sent : QueuedMessageStatus.Pending,
                DeliveryAttempts = shape == "attempted" ? 1 : 0,
                LastDeliveryStartedAt = shape == "attempted" ? f.Now : null,
                HoldUntil = shape == "held" ? f.Now.AddHours(1) : null };
            db.SessionQueuedMessages.Add(message); await db.SaveChangesAsync();
            var before = JsonSerializer.Serialize(message);
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.PendingDelivery, $"{input.Kind}/{shape}");
            f.Wire.ConditionalCommands.ShouldBe(0);
            (await db.RunnerSeatReleases.CountAsync(r => r.ActionId != null)).ShouldBe(0);
            db.ChangeTracker.Clear();
            JsonSerializer.Serialize(await db.SessionQueuedMessages.SingleAsync()).ShouldBe(before, "pending bytes/status/attempt evidence retained");
            await db.SessionQueuedMessages.ExecuteDeleteAsync();
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved, "pending guard is the only veto");
        }
    }

    [Test]
    public async Task Concurrent_reservations_have_one_winner()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync();
        // Registration first, then two independent connections race on the SAME existing row.
        f.Wire.Unsupported = true;
        await f.RunAsync();
        f.Wire.Unsupported = false;
        await using var read = f.Db();
        var release = await read.RunnerSeatReleases.SingleAsync();
        var task = await read.AgentTasks.SingleAsync();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> Race()
        {
            using var scope = f.Harness.Provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>();
            await barrier.Task;
            return await service.TryReserveAsync(release, task, f.Wire.Qualified, CancellationToken.None);
        }
        var first = Race(); var second = Race(); barrier.SetResult();
        var winners = await Task.WhenAll(first, second);
        winners.Sum().ShouldBe(1, "exactly one revision-conditional reservation winner");
        read.ChangeTracker.Clear();
        var saved = await read.RunnerSeatReleases.SingleAsync();
        saved.Revision.ShouldBe(release.Revision + 1); saved.ActionId.ShouldNotBeNull();
    }

    [Test]
    [Arguments(System.Net.HttpStatusCode.NotFound)]
    [Arguments(System.Net.HttpStatusCode.NotImplemented)]
    public async Task Unsupported_server_transport_never_falls_back_to_force(System.Net.HttpStatusCode unsupportedStatus)
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync();
        f.Wire.Unsupported = true;
        f.Wire.UnsupportedStatusCode = unsupportedStatus;
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Unsupported);
        f.Wire.ForceCommands.ShouldBe(0); f.Wire.ConditionalCommands.ShouldBe(0);
        var request = new TerminalSeatReleaseRequest(Guid.NewGuid(), f.Observation, "token");
        var routed = new RoutingSessionRunnerClient(f.Directory);
        (await routed.ReleaseTerminalSeatAsync(f.SessionId, request, default)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Unsupported);
        f.Wire.ForceCommands.ShouldBe(0);

        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: f.Schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync(); host.Directory.MarkRecovered(live);
        var force = 0;
        peer.Reply = frame =>
        {
            if (frame.Operation is PhoneHomeOperation.ReleaseSlot or PhoneHomeOperation.KillGeneration) force++;
            return frame.Operation is PhoneHomeOperation.ObserveTerminalSeat or PhoneHomeOperation.ReleaseTerminalSeat
                ? new PhoneHomeFrame(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                    JsonSerializer.SerializeToElement(new { code = "unsupported_operation" })) : null;
        };
        f.Directory.Client = new RunnerScopedSessionRunnerClient(host.Directory, host.AllowedRunnerId);
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Unsupported);
        (await f.Directory.Client.ReleaseTerminalSeatAsync(f.SessionId, request, default)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Unsupported);
        force.ShouldBe(0, "old phone-home peer never falls back to force/generation kill");
        await using var db = f.Db(); (await db.RunnerSeatReleases.CountAsync(r => r.ActionId != null)).ShouldBe(0);
        f.Harness.Provider.GetRequiredService<IOptions<TerminalRunnerSeatReleaseOptions>>().Value.AutomaticEnabled = false;
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Disabled);
        new TerminalRunnerSeatReleaseOptions().AutomaticEnabled.ShouldBeFalse();
    }
}
