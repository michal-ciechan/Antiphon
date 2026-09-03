using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0338 S1: a machine-triggered turn's plain text is a follow-up ChannelReply for
/// Delegation, Check and Scheduled origins. NO_REPLY still suppresses; System stays marker-only.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class ChannelMachineTurnTextTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    private static Task<BridgeQueueHarness> CreateHarnessAsync(
        Action<IServiceCollection>? configure = null,
        ChannelBridgeSettings? bridge = null) =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            Bridge = bridge ?? new ChannelBridgeSettings { Enabled = true, DebounceWindowMs = 0 },
            ConfigureServices = configure,
        });

    private static ChannelReplyDispatcher Restarted(BridgeQueueHarness h) => new(
        h.Provider.GetRequiredService<IServiceScopeFactory>(),
        h.Messaging,
        h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(),
        h.Provider.GetRequiredService<TimeProvider>(),
        NullLogger<ChannelReplyDispatcher>.Instance);

    private static async Task<SessionQueuedMessage> RowAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    [Test]
    public async Task The_incident_shape_delivers_plain_text_as_a_follow_up()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 23:31] CARD-0003";
        var channelRowId = await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "On it.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1, "the ack turn is the main-path reply");
        (await RowAsync(channelRowId)).ChannelReplySettledAt.ShouldNotBeNull();

        var note = "[task 15ed2644 done] CARD-0003 implemented";
        var injectionId = await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTurnAsync(note, "CARD-0003 implemented, 665 tests pass; review dispatched.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2, "the [task done] turn must follow-up the same conversation");
        var followUp = h.Messaging.SentReplies[1];
        followUp.Channel.ShouldBe("telegram");
        followUp.ConversationId.ShouldBe(chatId);
        followUp.ReplyHandle.ShouldBe(chatId);
        followUp.Text.ShouldBe("CARD-0003 implemented, 665 tests pass; review dispatched.");
        followUp.Attachments.ShouldBeEmpty();
        followUp.Kind.ShouldBe(ChannelReplyKind.Answer);
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldNotBeNull(
            "the Delegation row's ChannelReplySettledAt is the claim-before-produce marker");
        (await RowAsync(channelRowId)).ChannelReplySettledAt.ShouldNotBeNull(
            "the follow-up must not touch the already-settled Channel-origin row");
    }

    [Test]
    [Arguments(QueuedMessageOrigin.Check, "[check 27b19b2f #1] still looping?", "review looping on claude-fable-5, canceling")]
    [Arguments(QueuedMessageOrigin.Scheduled, "scheduled status please", "still waiting on the review")]
    public async Task Check_and_Scheduled_origins_deliver_plain_text(
        QueuedMessageOrigin origin, string note, string reply)
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = $"[Telegram \"Family\" — Mike 00:18] {origin}";
        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var injectionId = await SeedMachineInjectionAsync(h, note, origin);
        await h.InsertTurnAsync(note, reply);
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2, $"{origin} plain text must follow-up");
        h.Messaging.SentReplies[1].Text.ShouldBe(reply);
        h.Messaging.SentReplies[1].Attachments.ShouldBeEmpty();
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldNotBeNull();
    }

    [Test]
    public async Task System_plain_text_is_not_delivered_and_does_not_claim()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 00:00] boot";
        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var note = ChannelPreamble.BootstrapBody;
        var injectionId = await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.System);
        await h.InsertTurnAsync(note, "READY");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1, "System origin stays marker-only");
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldBeNull();
    }

    [Test]
    public async Task System_origin_with_attach_still_sends()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 00:01] file from system";
        var pdf = WriteFile(h, "system.pdf", "%PDF-1.4 system"u8.ToArray());
        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var note = "[System note from Antiphon: file ready]";
        await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.System);
        await h.InsertTurnAsync(note, $"[[attach: {pdf}]]");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2);
        h.Messaging.SentReplies[1].Attachments.ShouldHaveSingleItem().Name.ShouldBe("system.pdf");
    }

    [Test]
    public async Task Exact_NO_REPLY_is_silence_and_does_not_claim()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 00:02] quiet check";
        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var note = "[check abcd0001 #1] anything new?";
        var injectionId = await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Check);
        await h.InsertTurnAsync(note, "NO_REPLY");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1);
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldBeNull();
    }

    [Test]
    public async Task Prose_around_NO_REPLY_is_delivered()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 00:03] noted";
        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var note = "[check abcd0002 #1] still going";
        await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Check);
        await h.InsertTurnAsync(note, "Noted — NO_REPLY");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2);
        h.Messaging.SentReplies[1].Text.ShouldBe("Noted — NO_REPLY");
    }

    [Test]
    public async Task Re_running_the_same_turn_end_and_a_restart_do_not_double_send()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 00:04] once";
        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var note = "[task cd34ef56 done] shipped";
        var injectionId = await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTurnAsync(note, "Shipped.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Messaging.SentReplies.Count.ShouldBe(2);

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await Restarted(h).OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2, "claim-before-produce makes re-triggers a no-op");
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Trailing_text_follows_via_the_dispatched_watermark()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 00:05] trailing";
        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var note = "[task trail001 done] first";
        await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTurnAsync(note, "CARD-0003 implemented.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Messaging.SentReplies.Count.ShouldBe(2);

        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "review dispatched.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(3, "trailing AssistantText of the same turn follows");
        h.Messaging.SentReplies[2].Text.ShouldBe("review dispatched.");
        h.Messaging.SentReplies[2].ConversationId.ShouldBe(chatId);
    }

    [Test]
    public async Task An_api_error_stub_in_the_trailing_window_sends_nothing()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 00:06] stub trail";
        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var note = "[task stub0002 done] first";
        await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTurnAsync(note, "CARD-0003 implemented.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Messaging.SentReplies.Count.ShouldBe(2);

        await h.InsertApiErrorStubAsync();
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2, "DispatchFollowUpAsync withholds an API-error stub");
    }

    [Test]
    public async Task Stop_marker_before_text_does_not_claim_then_the_text_sends_once()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 00:07] stop first";
        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var note = "[task stop0001 done] later";
        var injectionId = await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, note);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1, "empty window must not claim");
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldBeNull();

        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "choose another kind?");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2);
        h.Messaging.SentReplies[1].Text.ShouldBe("choose another kind?");
        h.Messaging.SentReplies[1].Kind.ShouldBe(ChannelReplyKind.Question);
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldNotBeNull();
    }

    [Test]
    public async Task A_produce_failure_un_claims_so_the_next_trigger_sends_once()
    {
        await using var h = await CreateHarnessAsync(services =>
        {
            services.AddSingleton(sp =>
                new ToggleFailProducer(sp.GetRequiredService<FakeAntiphonMessagingClient>()));
            services.AddSingleton<IAntiphonMessagingProducer>(sp =>
                sp.GetRequiredService<ToggleFailProducer>());
        });
        var fail = h.Provider.GetRequiredService<ToggleFailProducer>();

        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 00:08] retry";
        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Messaging.SentReplies.Count.ShouldBe(1);

        var note = "[task fail0001 done] retry";
        var injectionId = await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTurnAsync(note, "Grok review died on the sign-in screen.");

        fail.FailRemaining = 1;
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1, "the failed produce must not leave a reply recorded");
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldBeNull(
            "un-claim on produce failure, or the status is lost forever");

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2);
        h.Messaging.SentReplies[1].Text.ShouldBe("Grok review died on the sign-in screen.");
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Empty_origins_dial_sends_nothing_for_plain_text_but_still_sends_markers()
    {
        var bridge = new ChannelBridgeSettings
        {
            Enabled = true,
            DebounceWindowMs = 0,
            MachineTurnTextOrigins = [],
        };
        await using var h = await CreateHarnessAsync(bridge: bridge);
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 00:09] dial";
        var pdf = WriteFile(h, "dial.pdf", "%PDF-1.4 dial"u8.ToArray());
        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var textNote = "[task dial0001 done] status";
        var textId = await SeedMachineInjectionAsync(h, textNote, QueuedMessageOrigin.Delegation);
        await h.InsertTurnAsync(textNote, "CARD-0003 implemented.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1, "empty origins is attachments-only");
        (await RowAsync(textId)).ChannelReplySettledAt.ShouldBeNull();

        var fileNote = "[task dial0002 done] file";
        await SeedMachineInjectionAsync(h, fileNote, QueuedMessageOrigin.Delegation);
        await h.InsertTurnAsync(fileNote, $"[[attach: {pdf}]]");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2);
        h.Messaging.SentReplies[1].Attachments.ShouldHaveSingleItem().Name.ShouldBe("dial.pdf");
    }

    [Test]
    public void MachineTurnTextOrigins_rejects_Channel_Ui_and_Supervision()
    {
        var validator = new ChannelBridgeSettingsValidator();
        validator.Validate(null, new ChannelBridgeSettings
        {
            MachineTurnTextOrigins = [QueuedMessageOrigin.Channel],
        }).Failed.ShouldBeTrue();
        validator.Validate(null, new ChannelBridgeSettings
        {
            MachineTurnTextOrigins = [QueuedMessageOrigin.Ui],
        }).Failed.ShouldBeTrue();
        validator.Validate(null, new ChannelBridgeSettings
        {
            MachineTurnTextOrigins = [QueuedMessageOrigin.Supervision],
        }).Failed.ShouldBeTrue();
        validator.Validate(null, new ChannelBridgeSettings()).Succeeded.ShouldBeTrue();
        validator.Validate(null, new ChannelBridgeSettings
        {
            MachineTurnTextOrigins = [QueuedMessageOrigin.System],
        }).Succeeded.ShouldBeTrue();
        validator.Validate(null, new ChannelBridgeSettings
        {
            MachineTurnTextOrigins = [],
        }).Succeeded.ShouldBeTrue();
    }

    [Test]
    public async Task An_operator_typed_plain_text_turn_sends_nothing_and_raises_no_incident()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 00:10] still waiting";
        var channelRowId = await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync("run the tests please", "All green.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty("an operator-typed turn must never follow-up a chat");
        (await RowAsync(channelRowId)).ChannelReplySettledAt.ShouldBeNull();

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(i => i.AgentId == h.AgentId)).ShouldBeFalse(
            "the Warning incident stays marker-only");
    }

    [Test]
    public async Task Implied_bundle_plus_text_sends_both_and_stamps()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 23:31] CARD-0002 cleanup";
        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "On it.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var (taskId, files) = await SeedBundleTaskAsync(h, mdCount: 2);
        var note = "[task 3f4a6029 done] CARD-0002 is Done";
        await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation, taskId);
        await h.InsertTurnAsync(note, "CARD-0002 cleanup landed.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2);
        var followUp = h.Messaging.SentReplies[1];
        followUp.Text.ShouldBe("CARD-0002 cleanup landed.");
        followUp.Attachments.Count.ShouldBe(2);
        followUp.Attachments.Select(a => a.Name).ShouldBe(files.Select(Path.GetFileName), ignoreOrder: true);
        (await TaskRowAsync(taskId)).DeliverableDeliveredAt.ShouldNotBeNull();
    }

    private static string WriteFile(BridgeQueueHarness h, string name, byte[] bytes)
    {
        var path = Path.Combine(h.TempRoot, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static async Task<Guid> SeedMachineInjectionAsync(
        BridgeQueueHarness h, string body, QueuedMessageOrigin origin, Guid? sourceTaskId = null)
    {
        await using var db = CreateContext();
        var seq = ((await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == h.SessionId)
            .MaxAsync(m => (long?)m.Sequence)) ?? 0) + 1;
        var id = Guid.NewGuid();
        var sent = DateTime.UtcNow;
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = id,
            AgentSessionId = h.SessionId,
            Body = body,
            Status = QueuedMessageStatus.Sent,
            Sequence = seq,
            Origin = origin,
            SourceTaskId = sourceTaskId,
            CreatedAt = sent,
            SentAt = sent,
            DeliveryAttempts = 1,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<AgentTask> TaskRowAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    private static async Task<(Guid TaskId, string[] Files)> SeedBundleTaskAsync(
        BridgeQueueHarness h, int mdCount)
    {
        var taskId = Guid.NewGuid();
        var shortId = DelegationReportFormatter.Short(taskId);
        var bundleDir = Path.Combine(h.TempRoot, ".antiphon", "deliverables", shortId);
        Directory.CreateDirectory(bundleDir);
        var files = new List<string>();
        for (var i = 1; i <= mdCount; i++)
        {
            var path = Path.Combine(bundleDir, $"0{i}-doc.md");
            File.WriteAllText(path, $"# doc {i}\n");
            files.Add(path);
        }

        File.WriteAllText(Path.Combine(bundleDir, "render.log"), "ok\n");

        await using var db = CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "Docs bundle",
            Goal = "Write the spec.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = h.TempRoot,
            RepoPath = h.TempRoot,
            Status = AgentTaskStatus.Succeeded,
            DeliverableBundleDir = bundleDir,
            DeliverableFileCount = mdCount,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return (taskId, files.ToArray());
    }

    private sealed class ToggleFailProducer : IAntiphonMessagingProducer
    {
        private readonly IAntiphonMessagingProducer _inner;

        public int FailRemaining;

        public ToggleFailProducer(IAntiphonMessagingProducer inner) => _inner = inner;

        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            if (FailRemaining > 0)
            {
                FailRemaining--;
                throw new InvalidOperationException("broker down (CARD-0338 produce-failure test)");
            }

            return _inner.SendAsync(reply, cancellationToken);
        }
    }
}
