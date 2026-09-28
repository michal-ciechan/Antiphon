using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Dtos;
using Antiphon.Tests.TestHelpers;
using Antiphon.Messaging;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class ChannelOutboundPublicationTests
{
    [Test]
    public async Task C519_Discovery_pages_past_withheld_rows_without_replaying_pre_chat_attachments()
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var attachment = Path.Combine(f.Harness.TempRoot, "pre-chat.pdf");
        File.WriteAllBytes(attachment, "%PDF-1.4 pre-chat"u8.ToArray());
        const string oldNote = "[task 15ed2644 done] before first chat";
        var preChat = await f.Harness.SeedPendingMessageAsync(oldNote,
            origin: QueuedMessageOrigin.Delegation, status: QueuedMessageStatus.Sent);
        await f.Harness.InsertTurnAsync(oldNote, $"Old attachment\n[[attach: {attachment}]]");

        var chat = await f.Harness.BindChannelAsync();
        const string firstPrompt = "Establish the first answered chat exchange";
        var first = await f.Harness.SeedChannelCorrelationAsync(firstPrompt, $"telegram:{chat}");
        await f.Harness.InsertTurnAsync(firstPrompt, "First chat answer.");
        await f.DispatchAsync();
        (await f.ReadAsync(first)).Publication!.State.ShouldBe("Published");
        var withheld = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var note = $"[task 15ed2644 done] withheld {i}";
            withheld.Add(await f.Harness.SeedPendingMessageAsync(note,
                origin: QueuedMessageOrigin.Delegation, status: QueuedMessageStatus.Sent));
            await f.Harness.InsertTurnAsync(note, "NO_REPLY");
        }
        await using (var db = f.CreateContext())
            await db.SessionQueuedMessages.Where(m => withheld.Contains(m.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.CreatedAt, DateTime.UtcNow.AddHours(-1)));

        const string prompt = "Answer the newer owed channel request";
        var owed = await f.Harness.SeedChannelCorrelationAsync(prompt, $"telegram:{chat}");
        await f.Harness.InsertTurnAsync(prompt, "Newer owed answer.");
        await f.StartWorkerAsync();

        (await f.ReadAsync(owed)).Publication!.State.ShouldBe("Published");
        (await f.ReadAsync(preChat, "machine")).Publication.ShouldBeNull();
        foreach (var id in withheld)
            (await f.ReadAsync(id, "machine")).Publication.ShouldBeNull();
        f.Producer.Accepted.Select(reply => reply.Text).ShouldBe(new[]
        {
            "First chat answer.", "Newer owed answer.",
        });
        // The first pass must stop after ten two-row pages, then resume from its cursor.
        for (var i = 0; i < 21; i++)
            await f.Harness.SeedPendingMessageAsync($"[task 15ed2644 done] unanswered {i}",
                createdAtUtc: DateTime.UtcNow.AddHours(-1),
                origin: QueuedMessageOrigin.Delegation, status: QueuedMessageStatus.Sent);
        const string laterPrompt = "Answer after the machine backlog";
        var laterOwed = await f.Harness.SeedChannelCorrelationAsync(laterPrompt, $"telegram:{chat}");
        await f.Harness.InsertTurnAsync(laterPrompt, "Answer after budget rollover.");

        await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        (await f.ReadAsync(laterOwed)).Publication.ShouldBeNull();
        await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        (await f.ReadAsync(laterOwed)).Publication!.State.ShouldBe("Published");
        f.Producer.Accepted.Select(reply => reply.Text).ShouldBe(new[]
        {
            "First chat answer.", "Newer owed answer.", "Answer after budget rollover.",
        });
    }

    [Test]
    public async Task C519_Trailing_discovery_prioritizes_recent_transcript_over_idle_publications()
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var (sourceId, _, _) = await f.CompleteSourceTurnAsync("trailing");
        await using (var db = f.CreateContext())
        {
            await db.ChannelOutboundPublications.Where(p => p.SessionId == f.SessionId)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.PublishedAt,
                    DateTime.UtcNow.AddDays(-1)));
            for (var i = 0; i < 2; i++)
            {
                var sessionId = Guid.NewGuid();
                var now = DateTime.UtcNow;
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId, DefinitionName = "idle", AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running, Cwd = f.Harness.TempRoot, Cols = 120, Rows = 30,
                    CreatedAt = now, StartedAt = now, LastSeenAt = now,
                });
                db.ChannelOutboundPublications.Add(new ChannelOutboundPublication
                {
                    Id = Guid.NewGuid(), SessionId = sessionId, Path = "main",
                    Provider = "telegram", ConversationId = $"idle-{i}",
                    PromptSequence = 1, FirstTextSequence = 2, LastTextSequence = 2,
                    OriginalResponse = "Old idle answer", EnvelopeJson = "{}",
                    State = "Published", CreatedAt = now, PublishedAt = now,
                });
            }
            await db.SaveChangesAsync();
        }

        await f.StartWorkerAsync();
        (await f.ReadPublicationsAsync(sourceId, "trailing"))
            .ShouldHaveSingleItem().State.ShouldBe("Published");
        f.Producer.Accepted.Count.ShouldBe(2);
        f.Producer.Accepted[1].Text.ShouldContain("Late middle sentinel; Late tail sentinel.");
    }

    [Test]
    [Arguments("main-no-reply")]
    [Arguments("machine-no-reply")]
    [Arguments("system-text")]
    [Arguments("main-api-error")]
    [Arguments("trailing-api-error")]
    public async Task C519_Recovery_preserves_intentional_withholding(string policy)
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        Guid sourceId;
        string path;
        var baseline = 0;
        if (policy.StartsWith("main", StringComparison.Ordinal))
        {
            var chat = await f.Harness.BindChannelAsync();
            const string prompt = "Original channel policy prompt";
            sourceId = await f.Harness.SeedChannelCorrelationAsync(prompt, $"telegram:{chat}");
            path = "main";
            if (policy == "main-no-reply")
                await f.Harness.InsertTurnAsync(prompt, "NO_REPLY");
            else
            {
                await using (var db = f.CreateContext())
                    await db.AgentSessions.Where(s => s.Id == f.SessionId)
                        .ExecuteUpdateAsync(set => set.SetProperty(s => s.AgentKind,
                            Antiphon.Server.Domain.Enums.AgentKind.Grok));
                await f.Harness.InsertTranscriptEntryAsync(
                    Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt, prompt);
                await f.Harness.InsertTranscriptEntryAsync(
                    Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd,
                    "The model is currently at capacity", stopReason: "error",
                    isApiError: true, apiErrorClass: "server_error", apiErrorStatus: 500);
            }
        }
        else if (policy == "trailing-api-error")
        {
            var main = await f.CompleteSourceTurnAsync("main");
            sourceId = main.SourceId;
            path = "trailing";
            await f.DispatchAsync();
            baseline = 1;
            await f.Harness.InsertApiErrorStubAsync("API Error: 429 overloaded");
        }
        else
        {
            var chat = await f.Harness.BindChannelAsync();
            const string channelPrompt = "Initial channel policy turn";
            await f.Harness.SeedChannelCorrelationAsync(channelPrompt, $"telegram:{chat}");
            await f.Harness.InsertTurnAsync(channelPrompt, "Initial answer.");
            await f.DispatchAsync();
            baseline = 1;
            var note = policy == "system-text"
                ? "[System note from Antiphon: ready]"
                : "[task 15ed2644 done] no reply requested";
            sourceId = await f.Harness.SeedPendingMessageAsync(note,
                origin: policy == "system-text"
                    ? Antiphon.Server.Domain.Enums.QueuedMessageOrigin.System
                    : Antiphon.Server.Domain.Enums.QueuedMessageOrigin.Delegation,
                status: Antiphon.Server.Domain.Enums.QueuedMessageStatus.Sent);
            await f.Harness.InsertTurnAsync(note, policy == "system-text" ? "READY" : "NO_REPLY");
            path = "machine";
        }
        await f.RestartAsync();
        await f.StartWorkerAsync();
        await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        f.Producer.Accepted.Count.ShouldBe(baseline);
        (await f.ReadPublicationsAsync(sourceId, path)).ShouldBeEmpty();
        var source = (await f.ReadAsync(sourceId, path)).Source;
        if (policy == "main-no-reply")
            source.ChannelReplySettledAt.ShouldNotBeNull();
        else if (policy != "trailing-api-error")
            source.ChannelReplySettledAt.ShouldBeNull();
        await using (var db = f.CreateContext())
        {
            (await db.AgentIncidents.CountAsync(i => i.AgentId == f.AgentId
                && i.Kind == Antiphon.Server.Domain.Enums.AgentIncidentKind.ChannelReplyLost)).ShouldBe(0);
        }
        if (policy == "main-no-reply")
        {
            var chat = await f.Harness.BindChannelAsync();
            const string prompt = "Explain why NO_REPLY is only a literal opt-out";
            var positive = await f.Harness.SeedChannelCorrelationAsync(prompt, $"telegram:{chat}");
            await f.Harness.InsertTurnAsync(prompt, "The token NO_REPLY appears in prose; answer tail.");
            await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
            (await f.ReadAsync(positive)).Publication!.State.ShouldBe("Published");
            f.Producer.Accepted.Count.ShouldBe(1);
        }
    }

    [Test]
    [Arguments("session")]
    [Arguments("marker")]
    [Arguments("tail")]
    [Arguments("attempt-floor")]
    [Arguments("next-turn")]
    public async Task C519_Recovery_cannot_borrow_another_turn(string mismatch)
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var chat = await f.Harness.BindChannelAsync();
        var body = mismatch == "tail"
            ? "Complete head " + new string('h', 220) + " ORIGINAL TAIL"
            : "Original complete channel prompt with a unique tail";
        if (mismatch == "attempt-floor")
            await f.Harness.InsertTurnAsync(body, "Old answer before the delivery attempt.");
        var sourceId = await f.Harness.SeedChannelCorrelationAsync(body, $"telegram:{chat}");
        var correctPrompt = body;
        if (mismatch == "marker")
        {
            correctPrompt = Antiphon.Server.Application.Services.ChannelPromptCorrelation.Mark(sourceId, body);
            await using var db = f.CreateContext();
            await db.SessionQueuedMessages.Where(m => m.Id == sourceId)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.Body, correctPrompt));
        }
        const string answer = "Correct middle sentinel and correct tail sentinel.";
        switch (mismatch)
        {
            case "session":
                var other = Guid.NewGuid();
                await using (var db = f.CreateContext())
                {
                    db.AgentSessions.Add(new AgentSession
                    {
                        Id = other, DefinitionName = "fake", AgentKind = Antiphon.Server.Domain.Enums.AgentKind.ClaudeCode,
                        Status = Antiphon.Server.Domain.Enums.SessionStatus.Running,
                        Cwd = f.Harness.TempRoot, Cols = 120, Rows = 30,
                        CreatedAt = f.Clock.GetUtcNow().UtcDateTime,
                        StartedAt = f.Clock.GetUtcNow().UtcDateTime,
                        LastSeenAt = f.Clock.GetUtcNow().UtcDateTime,
                    });
                    await db.SaveChangesAsync();
                }
                await f.Harness.InsertTurnAsync(body, answer, other);
                break;
            case "marker":
                await f.Harness.InsertTurnAsync(body, answer);
                break;
            case "tail":
                await f.Harness.InsertTurnAsync(body.Replace("ORIGINAL TAIL", "WRONG TAIL"), answer);
                break;
            case "attempt-floor":
                break;
            case "next-turn":
                await f.Harness.InsertTranscriptEntryAsync(
                    Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt, body);
                await f.Harness.InsertTurnAsync("Different next prompt", answer);
                break;
        }
        await f.StartWorkerAsync();
        var withheld = await f.ReadAsync(sourceId);
        withheld.Source.ChannelReplySettledAt.ShouldBeNull();
        withheld.Publication.ShouldBeNull();
        f.Producer.Accepted.ShouldBeEmpty();
        await f.Harness.InsertTurnAsync(correctPrompt, answer);
        await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        var recovered = await f.ReadAsync(sourceId);
        recovered.Publication!.State.ShouldBe("Published");
        f.Producer.Accepted.ShouldHaveSingleItem().Text.ShouldBe(answer);
    }

    [Test]
    public async Task C519_Upgrade_preserves_old_settled_rows_and_recovers_open_rows()
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var (oldSettled, _) = await f.CompleteMainAsync();
        await using (var db = f.CreateContext())
        {
            await db.SessionQueuedMessages.Where(m => m.Id == oldSettled)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.ChannelReplySettledAt,
                    f.Clock.GetUtcNow().UtcDateTime));
        }
        var openId = await f.CompleteAdditionalMainTurnAsync(77);
        const string note = "[task 15ed2644 done] original machine recovery";
        var machineId = await f.Harness.SeedPendingMessageAsync(note,
            origin: Antiphon.Server.Domain.Enums.QueuedMessageOrigin.Delegation,
            status: Antiphon.Server.Domain.Enums.QueuedMessageStatus.Sent);
        await f.Harness.InsertTurnAsync(note,
            "Machine recovery middle sentinel; machine recovery tail sentinel.");
        var chat = await f.Harness.BindChannelAsync();
        const string silentPrompt = "Please intentionally withhold this old reply";
        var silentId = await f.Harness.SeedChannelCorrelationAsync(silentPrompt, $"telegram:{chat}");
        await f.Harness.InsertTurnAsync(silentPrompt, "NO_REPLY");
        await f.RestartAsync();
        await f.StartWorkerAsync();
        await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        (await f.ReadAsync(oldSettled)).Publication.ShouldBeNull();
        (await f.ReadAsync(openId)).Publication!.State.ShouldBe("Published");
        (await f.ReadAsync(machineId, "machine")).Publication!.State.ShouldBe("Published");
        var silent = await f.ReadAsync(silentId);
        silent.Source.ChannelReplySettledAt.ShouldNotBeNull();
        silent.Publication.ShouldBeNull();
        f.Producer.Accepted.Count.ShouldBe(2);
        f.Producer.Accepted.Any(r => r.Text!.Contains("original tail sentinel")).ShouldBeFalse();
    }

    [Test]
    [Arguments("incident-save")]
    [Arguments("notice-produce")]
    [Arguments("notice-disabled")]
    [Arguments("missing-owner")]
    public async Task C519_Failure_incident_survives_its_own_refusal(string fault)
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var (sourceId, _, _) = await f.CompleteSourceTurnAsync("main");
        f.Producer.Mode = "definite-refuse";
        await f.DispatchAsync();
        var first = await f.ReadAsync(sourceId);
        first.Publication!.State.ShouldBe("Pending");
        first.Publication.IncidentId.ShouldBeNull();
        if (fault == "missing-owner")
        {
            await using var db = f.CreateContext();
            await db.Agents.Where(a => a.Id == f.AgentId)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.PersistentSessionId, (string?)null));
            await db.ChatChannels.Where(c => c.AgentId == f.AgentId)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.AgentId, (Guid?)null));
        }
        await f.StartWorkerAsync();
        await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        (await f.ReadAsync(sourceId)).Publication!.AttemptCount.ShouldBe(2);
        if (fault == "incident-save")
            f.Faults.Arm("incident-save");
        if (fault == "notice-disabled")
        {
            await using var db = f.CreateContext();
            await db.ChatChannels.Where(c => c.AgentId == f.AgentId)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.Enabled, false));
        }
        await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        var afterThird = await f.ReadAsync(sourceId);
        afterThird.Publication!.AttemptCount.ShouldBe(3);
        if (fault == "incident-save")
        {
            f.Faults.WasReached("incident-save").ShouldBeTrue();
            afterThird.Publication.State.ShouldBe("Publishing");
            afterThird.Publication.IncidentId.ShouldBeNull();
            await using var before = f.CreateContext();
            (await before.AgentIncidents.CountAsync(i => i.AgentId == f.AgentId
                && i.Kind == Antiphon.Server.Domain.Enums.AgentIncidentKind.ChannelReplyLost)).ShouldBe(0);
            f.Faults.Disarm();
            await f.RestartAsync();
            f.Clock.Advance(TimeSpan.FromSeconds(90));
            await f.StartWorkerAsync();
        }
        var held = await f.ReadAsync(sourceId);
        held.Publication!.State.ShouldBe("Held");
        held.Publication.IncidentId.ShouldNotBeNull();
        held.Source.ChannelReplySettledAt.ShouldBeNull();
        held.Publication.AgentId.ShouldBe(f.AgentId);
        await using (var db = f.CreateContext())
        {
            var incident = await db.AgentIncidents.AsNoTracking().SingleAsync(i =>
                i.Id == held.Publication.IncidentId);
            incident.Severity.ShouldBe(Antiphon.Server.Domain.Enums.AlertSeverity.Critical);
            incident.AgentId.ShouldBe(f.AgentId);
            incident.Message.ShouldContain(held.Publication.Id.ToString());
            incident.Message.ShouldContain(sourceId.ToString());
            incident.Message.ShouldContain(f.SessionId.ToString());
            (await db.Alerts.CountAsync(a => a.AgentId == f.AgentId
                && a.DedupKey == $"bridge:outbound:{held.Publication.Id}")).ShouldBe(1);
            await db.AgentIncidents.Where(i => i.Id == held.Publication.IncidentId)
                .ExecuteDeleteAsync();
        }
        await f.RestartAsync();
        await f.StartWorkerAsync();
        await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        await using (var db = f.CreateContext())
        {
            (await db.AgentIncidents.CountAsync(i => i.AgentId == f.AgentId
                && i.Kind == Antiphon.Server.Domain.Enums.AgentIncidentKind.ChannelReplyLost)).ShouldBe(0);
            (await db.Alerts.CountAsync(a => a.AgentId == f.AgentId
                && a.DedupKey == $"bridge:outbound:{held.Publication.Id}")).ShouldBe(1);
        }
    }

    [Test]
    [Arguments("outcome-commit")]
    [Arguments("catalog-stamp")]
    [Arguments("bundle-stamp")]
    public async Task C519_Post_acceptance_failure_does_not_lie_or_resend(string cut)
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        Guid sourceId;
        Guid? taskId = null;
        byte[]? pdfBytes = null;
        string path;
        int baseline;
        if (cut == "bundle-stamp")
        {
            var bundle = await f.CompleteMachineBundleAsync();
            sourceId = bundle.SourceId;
            taskId = bundle.TaskId;
            pdfBytes = bundle.PdfBytes;
            baseline = bundle.Baseline;
            path = "machine";
        }
        else
        {
            var main = await f.CompleteSourceTurnAsync("main");
            sourceId = main.SourceId;
            baseline = main.Baseline;
            path = "main";
        }
        f.Faults.Arm(cut);
        await f.DispatchAsync();
        f.Faults.WasReached(cut).ShouldBeTrue();
        f.Producer.Accepted.Count.ShouldBe(baseline + 1);
        var post = await f.ReadAsync(sourceId, path);
        if (cut == "outcome-commit")
        {
            post.Publication!.State.ShouldBe("Unknown");
            post.Publication.PublishedAt.ShouldBeNull();
            post.Source.ChannelReplySettledAt.ShouldBeNull();
            post.Publication.IncidentId.ShouldNotBeNull();
        }
        else
        {
            post.Publication!.State.ShouldBe("Published");
            post.Source.ChannelReplySettledAt.ShouldNotBeNull();
            post.Publication.MetadataStampedAt.ShouldBeNull();
        }
        if (taskId is Guid taskBefore)
        {
            await using var db = f.CreateContext();
            (await db.AgentTasks.Where(t => t.Id == taskBefore)
                .Select(t => t.DeliverableDeliveredAt).SingleAsync()).ShouldBeNull();
        }
        f.Faults.Disarm();
        await f.RestartAsync();
        if (cut == "outcome-commit")
            f.Clock.Advance(TimeSpan.FromSeconds(30));
        await f.StartWorkerAsync();
        var recovered = await f.ReadAsync(sourceId, path);
        recovered.Publication!.State.ShouldBe("Published");
        recovered.Publication.MetadataStampedAt.ShouldNotBeNull();
        f.Producer.Accepted.Count.ShouldBe(baseline + (cut == "outcome-commit" ? 2 : 1));
        if (taskId is Guid taskAfter)
        {
            var attachment = f.Producer.Accepted.Last().Attachments.ShouldHaveSingleItem();
            attachment.Content.ShouldBe(pdfBytes);
            await using var db = f.CreateContext();
            (await db.AgentTasks.Where(t => t.Id == taskAfter)
                .Select(t => t.DeliverableDeliveredAt).SingleAsync()).ShouldNotBeNull();
        }
    }

    [Test]
    [Arguments("second-refuses")]
    [Arguments("unroutable-sibling")]
    public async Task C519_Fanout_keeps_independent_target_outcomes(string caseName)
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var chatA = await f.Harness.BindChannelAsync();
        var chatB = await f.Harness.BindChannelAsync();
        const string bodyA = "Please send target A the shared complete answer, tail A.";
        const string bodyB = "Please send target B the shared complete answer, tail B.";
        const string answer = "Fanout middle sentinel; fanout tail sentinel.";
        var idA = await f.Harness.SeedChannelCorrelationAsync(bodyA, $"telegram:{chatA}");
        var idB = await f.Harness.SeedChannelCorrelationAsync(bodyB,
            caseName == "second-refuses" ? $"telegram:{chatB}" : "malformed-target");
        var markedA = Antiphon.Server.Application.Services.ChannelPromptCorrelation.Mark(idA, bodyA);
        var markedB = Antiphon.Server.Application.Services.ChannelPromptCorrelation.Mark(idB, bodyB);
        await using (var db = f.CreateContext())
        {
            await db.SessionQueuedMessages.Where(m => m.Id == idA)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.Body, markedA));
            await db.SessionQueuedMessages.Where(m => m.Id == idB)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.Body, markedB));
        }
        var prompt = $"{markedA}\n\n{markedB}";
        await f.Harness.InsertTurnAsync(prompt, answer);
        if (caseName == "second-refuses")
            f.Producer.RefuseConversationId = chatB;
        await f.DispatchAsync();
        var a = await f.ReadAsync(idA);
        a.Publication!.State.ShouldBe("Published");
        a.Source.ChannelReplySettledAt.ShouldNotBeNull();
        f.Producer.Accepted.Count(r => r.ConversationId == chatA).ShouldBe(1);
        var b = await f.ReadAsync(idB);
        if (caseName == "second-refuses")
        {
            b.Publication!.State.ShouldBe("Unknown");
            b.Source.ChannelReplySettledAt.ShouldBeNull();
            f.Producer.Accepted.Count(r => r.ConversationId == chatB).ShouldBe(0);
            f.Producer.RefuseConversationId = null;
            await f.RestartAsync();
            f.Clock.Advance(TimeSpan.FromSeconds(30));
            await f.StartWorkerAsync();
            (await f.ReadAsync(idB)).Publication!.State.ShouldBe("Published");
            f.Producer.Accepted.Count(r => r.ConversationId == chatA).ShouldBe(1);
            f.Producer.Accepted.Count(r => r.ConversationId == chatB).ShouldBe(1);
        }
        else
        {
            b.Publication.ShouldBeNull();
            b.Source.ChannelReplySettledAt.ShouldNotBeNull();
            await using var db = f.CreateContext();
            (await db.AgentIncidents.CountAsync(i => i.AgentId == f.AgentId
                && i.Kind == Antiphon.Server.Domain.Enums.AgentIncidentKind.ChannelReplyLost)).ShouldBe(1);
            await f.RestartAsync();
            await f.StartWorkerAsync();
            f.Producer.Accepted.Count(r => r.ConversationId == chatA).ShouldBe(1);
        }
    }

    [Test]
    public async Task C519_Database_rejects_duplicate_publication_ownership()
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var (sourceId, _, _) = await f.CompleteSourceTurnAsync("trailing");
        await f.DispatchAsync();
        var original = (await f.ReadPublicationsAsync(sourceId, "trailing")).ShouldHaveSingleItem();

        static ChannelOutboundPublication Copy(ChannelOutboundPublication source,
            string conversation, long first, long last) => new()
        {
            Id = Guid.NewGuid(), SessionId = source.SessionId, AgentId = source.AgentId,
            PromptSequence = source.PromptSequence, FirstTextSequence = first,
            LastTextSequence = last, Path = source.Path, Provider = source.Provider,
            ConversationId = conversation, ReplyHandle = source.ReplyHandle,
            OriginalResponse = source.OriginalResponse, EnvelopeJson = source.EnvelopeJson,
            CreatedAt = source.CreatedAt, NextAttemptAt = source.NextAttemptAt,
        };

        await using (var db = f.CreateContext())
        {
            db.ChannelOutboundPublications.Add(Copy(original, original.ConversationId,
                original.FirstTextSequence, original.LastTextSequence));
            var ex = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
            ex.InnerException.ShouldBeOfType<PostgresException>()
                .ConstraintName.ShouldBe("IX_ChannelOutboundPublications_IntervalTarget");
        }
        await using (var db = f.CreateContext())
        {
            var second = Copy(original, original.ConversationId + "-other",
                original.FirstTextSequence, original.LastTextSequence);
            db.ChannelOutboundPublications.Add(second);
            db.ChannelOutboundPublicationSources.Add(new ChannelOutboundPublicationSource
            {
                PublicationId = second.Id, QueueMessageId = sourceId, Path = "trailing",
                FirstTextSequence = original.FirstTextSequence,
                LastTextSequence = original.LastTextSequence,
            });
            var ex = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
            ex.InnerException.ShouldBeOfType<PostgresException>()
                .ConstraintName.ShouldBe("IX_ChannelOutboundPublicationSources_SourceInterval");
        }
        await using (var db = f.CreateContext())
        {
            var later = Copy(original, original.ConversationId + "-other",
                original.LastTextSequence + 1, original.LastTextSequence + 1);
            db.ChannelOutboundPublications.Add(later);
            db.ChannelOutboundPublicationSources.Add(new ChannelOutboundPublicationSource
            {
                PublicationId = later.Id, QueueMessageId = sourceId, Path = "trailing",
                FirstTextSequence = later.FirstTextSequence,
                LastTextSequence = later.LastTextSequence,
            });
            await db.SaveChangesAsync();
        }
    }

    [Test]
    public async Task C519_Publication_settings_reject_unbounded_or_inconsistent_values()
    {
        var validator = new ChannelBridgeSettingsValidator();
        var defaults = validator.Validate(null, new ChannelBridgeSettings());
        defaults.Succeeded.ShouldBeTrue();
        var cases = new (string Name, Action<ChannelBridgeSettings> Break)[]
        {
            (nameof(ChannelBridgeSettings.OutboundScanSeconds), s => s.OutboundScanSeconds = 0),
            (nameof(ChannelBridgeSettings.OutboundRetrySeconds), s => s.OutboundRetrySeconds = -1),
            (nameof(ChannelBridgeSettings.OutboundSendTimeoutSeconds), s => s.OutboundSendTimeoutSeconds = 0),
            (nameof(ChannelBridgeSettings.OutboundAttemptLeaseSeconds), s => s.OutboundAttemptLeaseSeconds = 0),
            (nameof(ChannelBridgeSettings.OutboundMaxAttempts), s => s.OutboundMaxAttempts = 0),
            (nameof(ChannelBridgeSettings.OutboundPageSize), s => s.OutboundPageSize = 0),
            (nameof(ChannelBridgeSettings.PendingReplyTtlMinutes), s => s.PendingReplyTtlMinutes = 0),
            (nameof(ChannelBridgeSettings.OutboundSendTimeoutSeconds), s => s.OutboundSendTimeoutSeconds = s.OutboundAttemptLeaseSeconds),
            (nameof(ChannelBridgeSettings.OutboundPageSize), s => s.OutboundPageSize = 101),
        };
        foreach (var (name, breakSetting) in cases)
        {
            var settings = new ChannelBridgeSettings();
            breakSetting(settings);
            var result = validator.Validate(null, settings);
            result.Failed.ShouldBeTrue(name);
            string.Join(" ", result.Failures).ShouldContain(name);
        }
        await Task.CompletedTask;
    }

    [Test]
    public async Task C519_Expired_attempt_is_unknown_and_spends_its_budget()
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var (sourceId, _, _) = await f.CompleteSourceTurnAsync("main");
        f.Producer.Mode = "hold";
        var first = f.DispatchAsync();
        await f.Producer.NextEntryAsync();
        var publishing = await f.ReadAsync(sourceId);
        publishing.Publication!.State.ShouldBe("Publishing");
        publishing.Publication.AttemptCount.ShouldBe(1);
        await f.DispatchAsync();
        f.Producer.Entered.Count.ShouldBe(1);
        f.Clock.Advance(TimeSpan.FromSeconds(30));
        await first.WaitAsync(TimeSpan.FromSeconds(30));
        var unknown = await f.ReadAsync(sourceId);
        unknown.Publication!.State.ShouldBe("Unknown");
        unknown.Publication.IncidentId.ShouldNotBeNull();
        f.Producer.LiveHolds.ShouldBe(0);

        var service = f.Harness.Provider.GetRequiredService<
            Antiphon.Server.Application.Services.ChannelOutboundPublicationService>();
        for (var attempt = 2; attempt <= 3; attempt++)
        {
            f.Producer.HoldGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            f.Clock.Advance(TimeSpan.FromSeconds(30));
            var retry = service.AttemptAsync(unknown.Publication.Id, CancellationToken.None);
            await f.Producer.NextEntryAsync();
            f.Clock.Advance(TimeSpan.FromSeconds(30));
            await retry.WaitAsync(TimeSpan.FromSeconds(30));
            var after = await f.ReadAsync(sourceId);
            after.Publication!.AttemptCount.ShouldBe(attempt);
            after.Publication.State.ShouldBe(attempt == 3 ? "Held" : "Unknown");
            f.Producer.LiveHolds.ShouldBe(0);
        }
        await service.RecoverDueAsync(CancellationToken.None);
        f.Producer.Entered.Count(r => r.Text?.Contains("Original middle sentinel") == true)
            .ShouldBe(3);
    }

    [Test]
    public async Task C519_Concurrent_dispatchers_share_one_fenced_attempt()
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var (sourceId, _, _) = await f.CompleteSourceTurnAsync("main");
        await using var other = await f.ParallelHarnessAsync();
        f.Producer.Mode = "hold";
        f.Faults.PauseOnceAt("before-publication-commit");
        var first = f.DispatchAsync();
        await f.Faults.WaitPausedAsync();
        var second = other.Dispatcher.OnTurnEndAsync(f.SessionId, CancellationToken.None);
        await f.Producer.NextEntryAsync();
        f.Faults.ReleasePause();
        await first.WaitAsync(TimeSpan.FromSeconds(30));
        (await f.ReadAsync(sourceId)).Publication!.State.ShouldBe("Publishing");
        f.Producer.Entered.Count(r => r.Text?.Contains("Original middle sentinel") == true)
            .ShouldBe(1);
        f.Producer.HoldGate.TrySetResult(true);
        await second.WaitAsync(TimeSpan.FromSeconds(30));
        (await f.ReadAsync(sourceId)).Publication!.State.ShouldBe("Published");
        f.Producer.Accepted.Count(r => r.Text?.Contains("Original middle sentinel") == true)
            .ShouldBe(1);

        var lateId = await f.CompleteAdditionalMainTurnAsync(94);
        f.Producer.Mode = "accept";
        f.Faults.PauseOnceAt("producer-accepted");
        var stale = f.DispatchAsync();
        await f.Faults.WaitPausedAsync();
        var claimed = (await f.ReadAsync(lateId)).Publication!;
        claimed.State.ShouldBe("Publishing");
        claimed.AttemptCount.ShouldBe(1);
        f.Clock.Advance(TimeSpan.FromSeconds(90));
        var service = other.Provider.GetRequiredService<
            Antiphon.Server.Application.Services.ChannelOutboundPublicationService>();
        await service.RecoverDueAsync(CancellationToken.None);
        (await f.ReadAsync(lateId)).Publication!.State.ShouldBe("Unknown");
        f.Clock.Advance(TimeSpan.FromSeconds(30));
        (await service.AttemptAsync(claimed.Id, CancellationToken.None)).ShouldBeTrue();
        f.Faults.ReleasePause();
        await stale.WaitAsync(TimeSpan.FromSeconds(30));
        var final = (await f.ReadAsync(lateId)).Publication!;
        final.State.ShouldBe("Published");
        final.AttemptCount.ShouldBe(2);
        f.Producer.Accepted.Count(r => r.Text?.Contains("Independent middle 94") == true)
            .ShouldBe(2);
    }

    [Test]
    [Arguments("idle")]
    [Arguments("busy")]
    public async Task C519_Real_queue_receipt_then_reply_survives_refusal(string recipient)
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var chat = await f.Harness.BindChannelAsync();
        // The shared fake normally inserts a synthetic TurnEnd immediately on submit. This
        // acceptance case keeps the real submitted UserPrompt open for the answer ingested below.
        f.Harness.Adapter.OnSubmitted = submitted =>
            f.Harness.InsertTranscriptEntryAsync(
                Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt,
                submitted, timestamp: f.Harness.Clock.GetUtcNow().UtcDateTime);
        var body = $"Real queued {recipient} prompt with original complete tail sentinel";
        if (recipient == "busy")
            await f.Harness.MarkWorkingAsync();
        Guid sourceId = default;
        await f.Harness.Queue.EnqueueAsync(f.SessionId, body, MessageSendMode.WhenIdle,
            CancellationToken.None, origin: QueuedMessageOrigin.Channel,
            conversationKey: $"telegram:{chat}", onCreated: id => sourceId = id);
        sourceId.ShouldNotBe(Guid.Empty);
        if (recipient == "busy")
        {
            (await f.ReadAsync(sourceId)).Source.Status.ShouldBe(QueuedMessageStatus.Pending);
            f.Harness.Adapter.SentInput.ShouldBeEmpty();
            await f.Harness.InsertTranscriptEntryAsync(
                Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd,
                stopReason: "end_turn");
            await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        }
        var delivered = (await f.ReadAsync(sourceId)).Source;
        delivered.Status.ShouldBe(QueuedMessageStatus.Sent);
        delivered.DeliveryAttempts.ShouldBe(1);
        await using (var db = f.CreateContext())
        {
            var prompts = await db.TranscriptEntries.AsNoTracking()
                .Where(t => t.AgentSessionId == f.SessionId
                    && t.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt
                    && t.Text == delivered.Body).ToListAsync();
            prompts.Count.ShouldBe(1);
            prompts[0].Sequence.ShouldBeGreaterThan(delivered.LastDeliveryBaselineSequence ?? 0);
        }
        var typed = f.Harness.Adapter.SentInput;
        typed.Split(body, StringSplitOptions.None).Length.ShouldBe(2);
        const string answer = "Recovered real queue middle sentinel; recovered real queue tail sentinel.";
        f.Producer.Mode = "definite-refuse";
        long floor;
        await using (var db = f.CreateContext())
            floor = await db.TranscriptEntries.Where(t => t.AgentSessionId == f.SessionId)
                .MaxAsync(t => t.Sequence);
        var events = new[]
        {
            new SessionRunnerTranscriptEvent(f.SessionId, floor + 1,
                Antiphon.SessionRunner.Contracts.TranscriptKinds.AssistantText,
                Guid.NewGuid().ToString("N"), null, f.Harness.Clock.GetUtcNow(), "assistant",
                answer, null, null, null, null, null),
            new SessionRunnerTranscriptEvent(f.SessionId, floor + 2,
                Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd,
                Guid.NewGuid().ToString("N"), null, f.Harness.Clock.GetUtcNow(), "assistant",
                null, null, null, null, null, "end_turn"),
        };
        f.Harness.Runner.SetTranscript(new SessionRunnerTranscriptDto(f.SessionId, events, floor + 2));
        await f.Harness.Runtime.SyncTranscriptAsync(f.SessionId, CancellationToken.None);
        await f.DispatchAsync();
        var refused = await f.ReadAsync(sourceId);
        refused.Publication.ShouldNotBeNull();
        refused.Publication.State.ShouldBe("Pending");
        f.Producer.Accepted.ShouldBeEmpty();
        f.Producer.Mode = "accept";
        await f.RestartAsync();
        f.Clock.Advance(TimeSpan.FromSeconds(30));
        await f.StartWorkerAsync();
        (await f.ReadAsync(sourceId)).Publication!.State.ShouldBe("Published");
        f.Producer.Accepted.ShouldHaveSingleItem().Text.ShouldBe(answer);
        (await f.ReadAsync(sourceId)).Source.DeliveryAttempts.ShouldBe(1);
        await using (var db = f.CreateContext())
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == f.SessionId
                && t.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt
                && t.Text == delivered.Body)).ShouldBe(1);
    }

    [Test]
    public async Task C519_Late_confirm_cannot_hide_a_failed_publication()
    {
        foreach (var failure in new[] { "prepare", "cancel", "produce" })
        {
            await using var f = await ChannelOutboundFixture.CreateAsync();
            var chat = await f.Harness.BindChannelAsync();
            var prompt = $"Late confirmed {failure} complete prompt tail";
            var pdf = Path.Combine(f.Harness.TempRoot, "late-confirm.pdf");
            File.WriteAllBytes(pdf, "%PDF-1.4 late confirm"u8.ToArray());
            var answer = $"Late confirmed original middle and tail.\n[[attach: {pdf}]]";
            var sourceId = await f.Harness.SeedPendingMessageAsync(prompt,
                deliveryAttempts: 1,
                baselineSequence: await f.Harness.CurrentTranscriptMaxSequenceAsync(),
                origin: QueuedMessageOrigin.Channel,
                conversationKey: $"telegram:{chat}");
            if (failure == "prepare") f.Faults.Arm("prepare");
            else f.Producer.Mode = failure == "cancel" ? "cancel" : "definite-refuse";
            var entries = new[]
            {
                new SessionRunnerTranscriptEvent(f.SessionId, 1,
                    Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt,
                    Guid.NewGuid().ToString("N"), null, f.Harness.Clock.GetUtcNow(), "user",
                    prompt, null, null, null, null, null),
                new SessionRunnerTranscriptEvent(f.SessionId, 2,
                    Antiphon.SessionRunner.Contracts.TranscriptKinds.AssistantText,
                    Guid.NewGuid().ToString("N"), null, f.Harness.Clock.GetUtcNow(), "assistant",
                    answer, null, null, null, null, null),
                new SessionRunnerTranscriptEvent(f.SessionId, 3,
                    Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd,
                    Guid.NewGuid().ToString("N"), null, f.Harness.Clock.GetUtcNow(), "assistant",
                    null, null, null, null, null, "end_turn"),
            };
            f.Harness.Runner.SetTranscript(new SessionRunnerTranscriptDto(f.SessionId, entries, 3));
            await f.Harness.Runtime.SyncTranscriptAsync(f.SessionId, CancellationToken.None);
            var before = await f.ReadAsync(sourceId);
            before.Source.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
            before.Source.ChannelReplySettledAt.ShouldBeNull();
            before.Publication?.State.ShouldNotBe("Published");
            f.RuntimeLogs.Messages.ShouldContain(m =>
                m.Contains("Late-confirmed channel reply was not published", StringComparison.Ordinal)
                && m.Contains(sourceId.ToString(), StringComparison.Ordinal)
                && m.Contains("PublicationFailed", StringComparison.Ordinal));
            f.Faults.Disarm();
            f.Producer.Mode = "accept";
            await f.RestartAsync();
            f.Clock.Advance(TimeSpan.FromSeconds(30));
            await f.StartWorkerAsync();
            (await f.ReadAsync(sourceId)).Publication!.State.ShouldBe("Published");
            f.Producer.Accepted.ShouldHaveSingleItem().Text.ShouldContain("Late confirmed original middle");
            (await f.ReadAsync(sourceId)).Source.DeliveryAttempts.ShouldBe(1);
        }
    }

    [Test]
    [Arguments("session")]
    [Arguments("transcript")]
    [Arguments("queue")]
    public async Task C519_Retention_cannot_erase_an_unresolved_reply(string pass)
    {
        foreach (var state in new[] { "preparation", "publishing", "held" })
        {
            await using var f = await ChannelOutboundFixture.CreateAsync();
            var path = state == "preparation" ? "main" : "machine";
            var (sourceId, _, _) = await f.CompleteSourceTurnAsync(path);
            if (state == "preparation") f.Faults.Arm("prepare");
            else if (state == "publishing")
            {
                f.Producer.Mode = "sync-refuse";
                f.Faults.Arm("failure-save");
            }
            else f.Producer.Mode = "definite-refuse";
            await f.DispatchAsync();
            f.Faults.Disarm();
            if (state == "held")
            {
                var service = f.Harness.Provider.GetRequiredService<
                    Antiphon.Server.Application.Services.ChannelOutboundPublicationService>();
                for (var n = 0; n < 2; n++)
                {
                    f.Clock.Advance(TimeSpan.FromSeconds(30));
                    await service.RecoverDueAsync(CancellationToken.None);
                }
            }
            var before = await f.ReadAsync(sourceId, path);
            before.Publication?.State.ShouldBe(state switch
            {
                "preparation" => null,
                "publishing" => "Publishing",
                _ => "Held",
            });
            var old = f.Harness.Now.AddDays(-10);
            var unrelated = Guid.NewGuid();
            var unrelatedQueue = Guid.NewGuid();
            await using (var db = f.CreateContext())
            {
                await db.Agents.Where(a => a.Id == f.AgentId).ExecuteUpdateAsync(set =>
                    set.SetProperty(a => a.PersistentSessionId, (string?)null));
                await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.Status, SessionStatus.Stopped)
                    .SetProperty(s => s.LastSeenAt, old));
                await db.SessionQueuedMessages.Where(m => m.Id == sourceId).ExecuteUpdateAsync(set =>
                    set.SetProperty(m => m.CreatedAt, old));
                await db.TranscriptEntries.Where(t => t.AgentSessionId == f.SessionId)
                    .ExecuteUpdateAsync(set => set.SetProperty(t => t.CreatedAt, old));
                db.AgentSessions.Add(new AgentSession
                {
                    Id = unrelated, DefinitionName = "retention-control", AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Stopped, Cwd = f.Harness.TempRoot, Cols = 120, Rows = 30,
                    CreatedAt = old, StartedAt = old, LastSeenAt = old,
                });
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = unrelatedQueue, AgentSessionId = unrelated, Body = "eligible control",
                    Origin = QueuedMessageOrigin.Ui, Status = QueuedMessageStatus.Sent,
                    Sequence = 1, CreatedAt = old, SentAt = old,
                });
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = unrelated, Sequence = 1,
                    Kind = Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt,
                    Text = "eligible control", CreatedAt = old,
                });
                await db.SaveChangesAsync();
            }
            await using (var db = f.CreateContext())
            {
                var settings = new RetentionSettings
                {
                    SessionRetentionDays = 1, TranscriptRetentionDays = 1,
                    QueuedMessageRetentionDays = 1, TaskRetentionDays = 0,
                };
                var audit = new AuditSettings { RetentionDays = 0 };
                var service = new Antiphon.Server.Application.Services.DataRetentionService(
                    db, Options.Create(settings), Options.Create(audit), f.Harness.Clock,
                    NullLogger<Antiphon.Server.Application.Services.DataRetentionService>.Instance,
                    new Antiphon.Server.Application.Services.AuditService(db, Options.Create(audit)));
                if (pass == "session") await service.PruneSessionsAsync(CancellationToken.None);
                else if (pass == "transcript") await service.PruneTranscriptsAsync(CancellationToken.None);
                else await service.PruneQueuedMessagesAsync(CancellationToken.None);
            }
            await using (var db = f.CreateContext())
            {
                (await db.AgentSessions.AnyAsync(s => s.Id == f.SessionId)).ShouldBeTrue();
                (await db.SessionQueuedMessages.AnyAsync(m => m.Id == sourceId)).ShouldBeTrue();
                var transcript = await db.TranscriptEntries.AsNoTracking()
                    .Where(t => t.AgentSessionId == f.SessionId).ToListAsync();
                transcript.ShouldContain(t => t.Kind ==
                    Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt);
                transcript.ShouldContain(t => t.Kind ==
                    Antiphon.SessionRunner.Contracts.TranscriptKinds.AssistantText);
                if (before.Publication is not null)
                {
                    var retained = await db.ChannelOutboundPublications.AsNoTracking()
                        .SingleAsync(p => p.Id == before.Publication.Id);
                    retained.State.ShouldBe(before.Publication.State);
                    retained.EnvelopeJson.ShouldBe(before.Publication.EnvelopeJson);
                    (await db.ChannelOutboundPublicationSources.AnyAsync(s =>
                        s.PublicationId == retained.Id && s.QueueMessageId == sourceId)).ShouldBeTrue();
                }
                if (pass == "session")
                    (await db.AgentSessions.AnyAsync(s => s.Id == unrelated)).ShouldBeFalse();
                else if (pass == "transcript")
                    (await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == unrelated)).ShouldBeFalse();
                else
                    (await db.SessionQueuedMessages.AnyAsync(m => m.Id == unrelatedQueue)).ShouldBeFalse();
            }
        }
    }

    public static IEnumerable<Func<(string Path, string Trigger)>> RecoveryCases()
    {
        foreach (var path in new[] { "main", "trailing", "machine" })
        foreach (var trigger in new[] { "startup", "periodic" })
        {
            var p = path;
            var t = trigger;
            yield return () => (p, t);
        }
    }

    [Test]
    [MethodDataSource(nameof(RecoveryCases))]
    public async Task C519_Recovery_needs_no_new_turn(string path, string trigger)
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        if (trigger == "periodic")
            await f.StartWorkerAsync();
        var (originalId, answer, baseline) = await f.CompleteSourceTurnAsync(path);
        f.Producer.Mode = "sync-refuse";
        await f.DispatchAsync();
        var secondId = await f.CompleteAdditionalMainTurnAsync(2);
        await f.DispatchAsync();
        var thirdId = await f.CompleteAdditionalMainTurnAsync(3);
        await f.DispatchAsync();
        f.Producer.Accepted.Count.ShouldBe(baseline);
        if (trigger == "startup")
        {
            await f.RestartAsync();
            f.Producer.Mode = "accept";
            f.Clock.Advance(TimeSpan.FromSeconds(30));
            await f.StartWorkerAsync();
        }
        else
        {
            f.Producer.Mode = "accept";
            await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        }
        f.Producer.Accepted.Count.ShouldBe(baseline + 2,
            "the first page must not drain all three owed obligations");
        await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        f.Producer.Accepted.Count.ShouldBe(baseline + 3);
        var original = await f.ReadAsync(originalId, path);
        original.Publication!.State.ShouldBe("Published");
        original.Publication.OriginalResponse.ShouldContain("tail sentinel");
        f.Producer.Accepted.Any(r => r.Text == answer).ShouldBeTrue();
        (await f.ReadAsync(secondId)).Publication!.State.ShouldBe("Published");
        (await f.ReadAsync(thirdId)).Publication!.State.ShouldBe("Published");
        await f.RestartAsync();
        await f.StartWorkerAsync();
        f.Producer.Accepted.Count.ShouldBe(baseline + 3);
    }

    [Test]
    public async Task C519_Trailing_interval_survives_restart_and_a_newer_prompt()
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var (sourceId, firstLate, baseline) = await f.CompleteSourceTurnAsync("trailing");
        f.Producer.Mode = "sync-refuse";
        await f.DispatchAsync();
        f.Producer.Accepted.Count.ShouldBe(baseline);
        await f.Harness.InsertTranscriptEntryAsync(
            Antiphon.SessionRunner.Contracts.TranscriptKinds.AssistantText,
            "Second late middle sentinel; second late tail sentinel.");
        await f.Harness.InsertTurnAsync("Newer independent prompt", "Newer answer must not replace late text.");
        await f.RestartAsync();
        f.Producer.Mode = "accept";
        f.Clock.Advance(TimeSpan.FromSeconds(30));
        await f.StartWorkerAsync();
        var trailing = await f.ReadPublicationsAsync(sourceId, "trailing");
        trailing.Count.ShouldBe(2);
        trailing[0].State.ShouldBe("Published");
        trailing[1].State.ShouldBe("Published");
        trailing[0].LastTextSequence.ShouldBeLessThan(trailing[1].FirstTextSequence);
        trailing[0].OriginalResponse.ShouldContain("Late tail sentinel");
        trailing[1].OriginalResponse.ShouldContain("second late tail sentinel");
        f.Producer.Accepted.Count.ShouldBe(baseline + 2);
        f.Producer.Accepted.Count(r => r.Text == firstLate).ShouldBe(1);
        f.Producer.Accepted.Count(r => r.Text!.Contains("Second late middle sentinel")).ShouldBe(1);
        f.Producer.Accepted.Any(r => r.Text!.Contains("Newer answer")).ShouldBeFalse();
    }

    [Test]
    public async Task C519_Retry_uses_frozen_payload_and_destination()
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var (sourceId, answer, baseline) = await f.CompleteSourceTurnAsync("machine");
        f.Producer.Mode = "sync-refuse";
        await f.DispatchAsync();
        var owed = await f.ReadAsync(sourceId, "machine");
        owed.Publication.ShouldNotBeNull();
        var frozen = JsonSerializer.Deserialize<ChannelReply>(owed.Publication.EnvelopeJson)!;
        var attachment = frozen.Attachments.ShouldHaveSingleItem();
        var originalBytes = attachment.Content!.ToArray();
        await using (var db = f.CreateContext())
        {
            await db.ChatChannels.Where(c => c.Provider == frozen.Channel
                && c.ExternalId == frozen.ConversationId)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.ReplyHandle, "changed-handle"));
        }
        File.WriteAllBytes(attachment.Source!, "changed file bytes"u8.ToArray());
        await f.Harness.InsertTurnAsync("new unrelated prompt", "new text cannot replace machine answer");
        await f.RestartAsync();
        f.Producer.Mode = "accept";
        f.Clock.Advance(TimeSpan.FromSeconds(30));
        await f.StartWorkerAsync();
        var sent = f.Producer.Accepted.Last();
        sent.Channel.ShouldBe(frozen.Channel);
        sent.ConversationId.ShouldBe(frozen.ConversationId);
        sent.ReplyHandle.ShouldBe(frozen.ReplyHandle);
        sent.Kind.ShouldBe(frozen.Kind);
        sent.Text.ShouldBe(answer);
        sent.Attachments.ShouldHaveSingleItem().Content.ShouldBe(originalBytes);
        f.Producer.Accepted.Count.ShouldBe(baseline + 1);
        (await f.ReadAsync(sourceId, "machine")).Publication!.State.ShouldBe("Published");
    }

    public static IEnumerable<Func<(string Path, string Cut)>> CancellationCases()
    {
        string[] paths = ["main", "trailing", "machine"];
        string[] cuts = ["before-attempt", "during-send", "accepted-before-result"];
        foreach (var path in paths)
        foreach (var cut in cuts)
        {
            var capturedPath = path;
            var capturedCut = cut;
            yield return () => (capturedPath, capturedCut);
        }
    }

    [Test]
    [MethodDataSource(nameof(CancellationCases))]
    public async Task C519_Cancellation_never_becomes_publication(string path, string cut)
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var (sourceId, answer, baseline) = await f.CompleteSourceTurnAsync(path);
        if (cut == "before-attempt")
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try { await f.DispatchAsync(canceled.Token); }
            catch (OperationCanceledException) { }
            f.Producer.Entered.Count.ShouldBe(baseline);
        }
        else
        {
            f.Producer.Mode = cut == "during-send" ? "cancel" : "accepted-lost";
            await f.DispatchAsync();
            f.Producer.Entered.Count.ShouldBe(baseline + 1);
        }
        var owed = await f.ReadAsync(sourceId, path);
        owed.Publication?.PublishedAt.ShouldBeNull();
        if (path != "trailing")
            owed.Source.ChannelReplySettledAt.ShouldBeNull();
        if (cut != "before-attempt")
        {
            owed.Publication.ShouldNotBeNull();
            owed.Publication.State.ShouldBe("Unknown");
            owed.Publication.AttemptCount.ShouldBe(1);
            owed.Publication.IncidentId.ShouldNotBeNull();
        }
        f.Producer.Mode = "accept";
        await f.RestartAsync();
        f.Clock.Advance(TimeSpan.FromSeconds(30));
        await f.StartWorkerAsync();
        var recovered = await f.ReadAsync(sourceId, path);
        recovered.Publication!.State.ShouldBe("Published");
        recovered.Publication.PublishedAt.ShouldNotBeNull();
        f.Producer.Accepted.Last().Text.ShouldBe(answer);
        f.Producer.Accepted.Count.ShouldBe(baseline
            + (cut == "accepted-before-result" ? 2 : 1));
        await f.RestartAsync();
        await f.StartWorkerAsync();
        f.Producer.Accepted.Count.ShouldBe(baseline
            + (cut == "accepted-before-result" ? 2 : 1));
    }

    public static IEnumerable<Func<(string Path, string Stage)>> RefusalCases()
    {
        string[] paths = ["main", "trailing", "machine"];
        string[] stages = ["prepare", "publication-commit", "attempt-commit",
            "serialize", "produce-sync", "produce-async", "failure-save"];
        foreach (var path in paths)
        foreach (var stage in stages)
        {
            var capturedPath = path;
            var capturedStage = stage;
            yield return () => (capturedPath, capturedStage);
        }
    }

    [Test]
    [MethodDataSource(nameof(RefusalCases))]
    public async Task C519_Refusal_preserves_the_original_obligation(string path, string stage)
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var (sourceId, answer, baseline) = await f.CompleteSourceTurnAsync(path);
        f.Faults.Arm(stage);
        f.Producer.Mode = stage switch
        {
            "serialize" => "serialize",
            "produce-async" => "async-refuse",
            "produce-sync" or "failure-save" => "sync-refuse",
            _ => "accept",
        };
        await f.DispatchAsync();
        if (stage is "prepare" or "publication-commit" or "attempt-commit" or "failure-save")
            f.Faults.WasReached(stage).ShouldBeTrue();
        f.Producer.Accepted.Count.ShouldBe(baseline);
        var owed = await f.ReadAsync(sourceId, path);
        owed.Publication?.PublishedAt.ShouldBeNull();
        if (path == "trailing")
            owed.Source.ChannelReplySettledAt.ShouldNotBeNull();
        else
            owed.Source.ChannelReplySettledAt.ShouldBeNull();
        if (stage is "publication-commit" or "attempt-commit")
            f.Producer.Entered.Count.ShouldBe(baseline);
        if (stage is "prepare" or "publication-commit")
            owed.Publication.ShouldBeNull();

        f.Faults.Disarm();
        f.Producer.Mode = "accept";
        await f.RestartAsync();
        if (stage == "failure-save")
        {
            f.Clock.Advance(TimeSpan.FromSeconds(90));
            await f.StartWorkerAsync();
            await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        }
        else
        {
            f.Clock.Advance(TimeSpan.FromSeconds(30));
            await f.StartWorkerAsync();
        }
        var recovered = await f.ReadAsync(sourceId, path);
        recovered.Publication.ShouldNotBeNull();
        recovered.Publication.State.ShouldBe("Published");
        recovered.Publication.PublishedAt.ShouldNotBeNull();
        recovered.Publication.OriginalResponse.ShouldContain("tail sentinel");
        if (path != "trailing")
            recovered.Source.ChannelReplySettledAt.ShouldNotBeNull();
        f.Producer.Accepted.Count.ShouldBe(baseline + 1);
        f.Producer.Accepted.Last().Text.ShouldBe(answer);
        await f.RestartAsync();
        await f.StartWorkerAsync();
        f.Producer.Accepted.Count.ShouldBe(baseline + 1);
    }

    [Test]
    public async Task C519_Retry_budget_and_age_survive_restart()
    {
        await using var f = await ChannelOutboundFixture.CreateAsync();
        var (sourceId, answer) = await f.CompleteMainAsync();
        f.Producer.Mode = "sync-refuse";
        await f.DispatchAsync();
        var first = await f.ReadAsync(sourceId);
        first.Source.ChannelReplySettledAt.ShouldBeNull();
        first.Publication.ShouldNotBeNull();
        first.Publication.AttemptCount.ShouldBe(1);
        first.Publication.PublishedAt.ShouldBeNull();
        f.Producer.Accepted.ShouldBeEmpty();

        await f.RestartAsync();
        await f.StartWorkerAsync();
        f.Clock.Advance(TimeSpan.FromSeconds(29));
        f.Producer.Entered.Count.ShouldBe(1);
        await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(1));
        (await f.ReadAsync(sourceId)).Publication!.AttemptCount.ShouldBe(2);
        f.Producer.Entered.Count.ShouldBe(2);

        await f.RestartAsync();
        await f.StartWorkerAsync();
        f.Producer.Entered.Count.ShouldBe(2);
        await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        var held = await f.ReadAsync(sourceId);
        held.Publication!.AttemptCount.ShouldBe(3);
        held.Publication.State.ShouldBe("Held");
        held.Source.ChannelReplySettledAt.ShouldBeNull();
        f.Producer.Entered.Count.ShouldBe(3);
        await f.RestartAsync();
        await f.StartWorkerAsync();
        await f.AdvanceAndScanAsync(TimeSpan.FromSeconds(30));
        f.Producer.Entered.Count.ShouldBe(3);

        await using var age = await ChannelOutboundFixture.CreateAsync();
        var (agedSource, _) = await age.CompleteMainAsync();
        age.Producer.Mode = "sync-refuse";
        await age.DispatchAsync();
        age.Producer.Entered.Count.ShouldBe(1);
        await age.RestartAsync();
        age.Clock.Advance(TimeSpan.FromMinutes(30));
        await age.StartWorkerAsync();
        var expired = await age.ReadAsync(agedSource);
        expired.Publication!.State.ShouldBe("Held");
        expired.Publication.AttemptCount.ShouldBe(1);
        expired.Source.ChannelReplySettledAt.ShouldBeNull();
        age.Producer.Accepted.ShouldBeEmpty();
        answer.ShouldContain("tail sentinel");
    }
}
