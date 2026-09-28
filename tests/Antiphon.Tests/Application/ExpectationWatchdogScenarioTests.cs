using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class ExpectationWatchdogScenarioTests
{
    [Test]
    public async Task C650_Overnight_fence_nudges_and_pages_without_task_notes()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var clock = new FakeTimeProvider(new DateTimeOffset(world.Now, TimeSpan.Zero));
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        await using (var db = world.Db())
        {
            foreach (var id in ids)
            {
                var task = world.Task(id, AgentTaskStatus.Queued, null,
                    createdAt: world.Now.AddHours(-5));
                task.RepoPath = "/src/antiphon";
                db.AgentTasks.Add(task);
                db.AgentTaskEvents.Add(ExpectationTestWorld.Event(id, AgentTaskEventType.Held,
                    world.Now.AddMinutes(-5), DispatchHoldDetails.LeaseFenced("dead child journal")));
            }
            await db.SaveChangesAsync();
        }
        Guid nudgeId;
        await using (var db = world.Db())
        {
            var scan = await world.Service(db, clock).ScanAsync(world.Directive,
                ExpectationProbeInput.None, CancellationToken.None);
            scan.Evaluation.DispatchFence.ShouldNotBeNull();
            scan.NudgesCommitted.ShouldBe(1);
            nudgeId = scan.NudgeId!.Value;
        }
        await using (var db = world.Db())
        {
            var send = new ExpectationNudgeDeliveryService(db, new RefusePrompt(), clock);
            (await send.DeliverAsync(world.Directive, nudgeId, CancellationToken.None)).Outcome
                .ShouldBe(ExpectationSendOutcome.Refused);
        }
        var producer = new RecordingProducer();
        await using (var db = world.Db())
            (await new ExpectationOperatorDeliveryService(db, producer, clock,
                new ExpectationTimingSettings()).PublishDueAsync(world.Directive, CancellationToken.None)).ShouldBe(1);
        producer.Sent.ShouldHaveSingleItem().ConversationId
            .ShouldBe("c650-operator-" + world.Directive.OperatorChannelId.ToString("N"));
        await using (var db = world.Db())
        {
            (await db.AgentTaskLandNotifications.CountAsync()).ShouldBe(0);
            (await db.SessionQueuedMessages.CountAsync()).ShouldBe(0);
            var checkedIds = await db.AgentTaskEvents.Where(e => e.Type == AgentTaskEventType.Check)
                .Select(e => e.AgentTaskId).ToListAsync();
            checkedIds.Distinct().OrderBy(x => x).ShouldBe(ids.OrderBy(x => x).ToArray());
        }
    }

    [Test]
    public async Task C650_Idle_and_working_callers_receive_complete_prompt()
    {
        foreach (var working in new[] { false, true })
        {
            await using var fixture = await ExpectationDeliveryFixture.CreateAsync();
            if (working)
            {
                await fixture.Harness.MarkWorkingAsync();
                var held = await fixture.Harness.Queue.EnqueueAsync(fixture.SessionId,
                    "older caller note", MessageSendMode.WhenIdle, CancellationToken.None);
                held.Working.ShouldBeTrue();
            }
            var nudge = await fixture.NudgeAsync();
            (await fixture.DeliverAsync(nudge.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Confirmed);
            fixture.Harness.Adapter.Inputs.ShouldBe([nudge.Body, "\r"]);
            var stored = await fixture.ReloadAsync(nudge.Id);
            stored.ReceiptAt.ShouldNotBeNull();
            stored.ReceiptSequence.ShouldNotBeNull();
            await using var db = fixture.Db();
            (await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == fixture.SessionId
                && t.Sequence == stored.ReceiptSequence && t.Kind == TranscriptKinds.UserPrompt
                && t.Text == nudge.Body)).ShouldBeTrue();
            if (working)
                (await db.SessionQueuedMessages.SingleAsync()).Status.ShouldBe(QueuedMessageStatus.Pending);
        }
    }

    [Test]
    public async Task C650_Recovery_and_late_note_receipt_resolve_without_mutation()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var taskId = Guid.NewGuid();
        var note = ExpectationTestWorld.Queued(Guid.NewGuid(), world.OwnedSessionId,
            QueuedMessageStatus.Pending, world.Now.AddMinutes(-20), 1);
        note.SourceTaskId = taskId;
        note.Origin = QueuedMessageOrigin.Delegation;
        note.NoteHeader = "Completion for task " + taskId.ToString("D");
        note.Body = "[task " + taskId.ToString("D") + "] completed";
        note.DeliveryAttempts = 1;
        note.LastDeliveryBaselineSequence = 0;
        await using (var db = world.Db())
        {
            db.AgentTasks.Add(world.Task(taskId, AgentTaskStatus.Succeeded, world.Now.AddMinutes(-30)));
            db.SessionQueuedMessages.Add(note);
            await db.SaveChangesAsync();
        }
        var clock = new FakeTimeProvider(new DateTimeOffset(world.Now, TimeSpan.Zero));
        (await ScanAsync(world, clock)).NudgesCommitted.ShouldBe(1);
        await using (var db = world.Db())
        {
            db.TranscriptEntries.Add(ExpectationTestWorld.Transcript(world.OwnedSessionId, 1,
                TranscriptKinds.UserPrompt, world.Now.AddMinutes(1), note.Body));
            await db.SaveChangesAsync();
        }
        clock.Advance(TimeSpan.FromMinutes(1));
        await ScanAsync(world, clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        await ScanAsync(world, clock);
        await using var read = world.Db();
        (await read.ExpectationEpisodes.SingleAsync()).ResolvedAt.ShouldBe(world.Now.AddMinutes(2));
        var unchanged = await read.SessionQueuedMessages.SingleAsync();
        unchanged.Status.ShouldBe(QueuedMessageStatus.Pending);
        unchanged.DeliveryAttempts.ShouldBe(1);
    }

    [Test]
    public async Task C650_All_five_conditions_have_durable_subject_audits()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var kinds = new[] { ExpectationEpisodeKind.StalledPipeline, ExpectationEpisodeKind.DispatchFence,
            ExpectationEpisodeKind.CapacityDeficit, ExpectationEpisodeKind.SilentInFlight,
            ExpectationEpisodeKind.UndeliveredNote };
        var subjects = kinds.Select((kind, i) => kind + ":" + i).ToArray();
        var taskIds = kinds.Select(_ => Guid.NewGuid()).ToArray();
        var episodeIds = kinds.Select(_ => Guid.NewGuid()).ToArray();
        await using (var db = world.Db())
        {
            for (var i = 0; i < kinds.Length; i++)
            {
                db.AgentTasks.Add(world.Task(taskIds[i], AgentTaskStatus.Queued, null));
                db.ExpectationEpisodes.Add(new ExpectationEpisode
                {
                    Id = episodeIds[i], DirectiveId = world.Directive.Id, ConfigDigest = world.Digest,
                    Kind = kinds[i], SubjectKey = subjects[i], Evidence = kinds[i].ToString(),
                    FirstObservedAt = world.Now, LastObservedAt = world.Now,
                });
            }
            await db.SaveChangesAsync();
        }
        var nudgeId = Guid.NewGuid();
        await using (var db = world.Db())
        {
            var ledger = new ExpectationLedger(db, new FakeTimeProvider(new DateTimeOffset(world.Now, TimeSpan.Zero)),
                new ExpectationTestWorld.QuietBus());
            var commit = await ledger.CommitAggregateNudgeAsync(new ExpectationAggregateNudgeRequest(
                nudgeId, world.Directive.Id, world.Digest, world.BoardId, world.CardId,
                episodeIds, subjects, taskIds, "five conditions",
                "[expectation-nudge:" + nudgeId.ToString("D") + "] inspect all five",
                null, world.Now, world.Now.AddMinutes(10)), CancellationToken.None);
            commit.ShouldNotBeNull();
        }
        await using var read = world.Db();
        var audit = await read.CardComments.SingleAsync(c => c.Author == ExpectationLedger.AuditAuthor);
        foreach (var subject in subjects) audit.Body.ShouldContain(subject);
        var checks = await read.AgentTaskEvents.Where(e => e.Type == AgentTaskEventType.Check)
            .Select(e => e.AgentTaskId).ToListAsync();
        checks.OrderBy(x => x).ShouldBe(taskIds.OrderBy(x => x).ToArray());
        (await read.ExpectationNudges.SingleAsync()).EpisodeIdsJson
            .ShouldBe(JsonSerializer.Serialize(episodeIds));
    }

    private static async Task<ExpectationScanResult> ScanAsync(ExpectationTestWorld world, FakeTimeProvider clock)
    {
        await using var db = world.Db();
        return await world.Service(db, clock).ScanAsync(world.Directive,
            ExpectationProbeInput.None, CancellationToken.None);
    }
    private sealed class RefusePrompt : IExpectationPromptSender
    {
        public Task<ExpectationSendResult> SendAsync(Guid sessionId, DateTime expectedGeneration,
            Guid ownerAgentId, string body, Func<ExpectationSendAttempt, CancellationToken, Task<bool>> commitAttempt,
            CancellationToken ct, Func<ExpectationSendResult, CancellationToken, Task>? recordOutcome = null) =>
            Task.FromResult(ExpectationSendResult.Refuse("test recipient unavailable"));
    }
    private sealed class RecordingProducer : IAntiphonMessagingProducer
    {
        public List<ChannelReply> Sent { get; } = [];
        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        { Sent.Add(reply); return Task.CompletedTask; }
    }
}
