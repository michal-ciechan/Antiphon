using Antiphon.Messaging;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The channel bridge loop, offline: inbound <see cref="ChannelMessage"/>s (via the in-memory messaging
/// fake) discover <see cref="ChatChannel"/> rows; a channel bound to an agent routes messages into the
/// agent's session queue; and on turn end the <see cref="ChannelReplyDispatcher"/> matches the turn back
/// to the channel and produces a typed <see cref="ChannelReply"/> to the outbound topic.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class ChannelBridgeTests
{
    [Test]
    public async Task First_inbound_message_discovers_the_channel_unrouted()
    {
        await using var h = await HarnessAsync();

        var msg = TelegramText(chatId: h.ChatId, "hello there", title: "Family");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        var channels = await h.Channels().GetAllAsync(CancellationToken.None);
        var channel = channels.ShouldHaveSingleItem();
        channel.Provider.ShouldBe("telegram");
        channel.ExternalId.ShouldBe(h.ChatId);
        channel.Title.ShouldBe("Family");
        channel.AgentId.ShouldBeNull();
        channel.MessageCount.ShouldBe(1);
        channel.LastMessagePreview.ShouldBe("hello there");
        h.Adapter.SentInput.ShouldBeEmpty("unbound channels must not route");
        h.EventBus.PublishedEvents.ShouldContain(e => e.EventName == "ChannelChanged");
    }

    [Test]
    public async Task Bound_channel_routes_the_message_into_the_agent_session()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "What's for dinner?", title: "Family", author: "Mike");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        h.Adapter.SentInput.ShouldContain("What's for dinner?");
        h.Adapter.SentInput.ShouldContain("Mike", customMessage: "the prompt must carry the author context");
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(1, "a reply correlation must be tracked");
    }

    [Test]
    public async Task Turn_end_sends_the_agents_answer_down_the_channel()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "What's for dinner?", title: "Family");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);
        var deliveredPrompt = h.Adapter.Inputs[0];

        await h.InsertTurnAsync(deliveredPrompt, "Pasta tonight — Ola already started the sauce.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
        reply.Channel.ShouldBe("telegram");
        reply.ConversationId.ShouldBe(h.ChatId);
        reply.Kind.ShouldBe(ChannelReplyKind.Answer);
        reply.Text.ShouldBe("Pasta tonight — Ola already started the sauce.");
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(0);
    }

    // Live failure 2026-07-24 (Antiphon-Family, Ola's Apple Music question): Claude wrote the
    // turn as UserPrompt, TurnEnd, AssistantText, TurnEnd — the stop marker BEFORE the text. The
    // dispatch on the first (text-less) TurnEnd consumed the correlations, so when the text
    // arrived there was nothing left to match and the reply never reached the chat.
    [Test]
    public async Task A_turn_whose_stop_marker_precedes_the_text_still_replies()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "How do I stop Apple Music autoplaying?", title: "Family");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        await h.InsertEntryAsync(TranscriptKinds.UserPrompt, h.Adapter.Inputs[0]);
        await h.InsertEntryAsync(TranscriptKinds.TurnEnd, null, stopReason: "end_turn");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        h.Dispatcher.PendingCount(h.SessionId)
            .ShouldBe(1, "a text-less TurnEnd must leave the correlation pending, not consume it");

        // The reply text lands after the stop marker; its arrival re-triggers dispatch.
        await h.InsertEntryAsync(TranscriptKinds.AssistantText, "Turn off the car's Bluetooth autoplay setting.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldHaveSingleItem().Text
            .ShouldBe("Turn off the car's Bluetooth autoplay setting.");
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(0);
    }

    [Test]
    public async Task A_response_ending_in_a_question_is_typed_as_question()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "Book the dentist", title: "Family");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        await h.InsertTurnAsync(h.Adapter.Inputs[0], "I can do Tuesday 10:00 or Thursday 15:30. Which works?");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldHaveSingleItem().Kind.ShouldBe(ChannelReplyKind.Question);
    }

    [Test]
    public async Task A_turn_the_bridge_did_not_start_sends_no_reply()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "hello", title: "Family");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        // A human typed directly into the terminal: the turn's prompt matches no pending correlation.
        await h.InsertTurnAsync("run the tests please", "All green.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(1, "the channel's correlation must survive for ITS turn");
    }

    [Test]
    public async Task Redelivered_message_is_not_routed_twice()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();

        var msg = TelegramText(h.ChatId, "ping", title: "Family");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None); // Kafka at-least-once redelivery

        h.Adapter.Inputs.Count(i => i.Contains("ping")).ShouldBe(1);
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(1);
    }

    [Test]
    public async Task Own_bot_messages_are_ignored()
    {
        await using var h = await HarnessAsync();

        var msg = TelegramText(h.ChatId, "echo of our own reply") with
        {
            Author = new Participant { Id = "bot", IsSelf = true },
        };
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        (await h.Channels().GetAllAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    // PR 9: rapid-fire same-sender messages debounce into ONE routed prompt — single truthful
    // envelope header, one line per message, one reply correlation for the whole flush.
    [Test]
    public async Task Rapid_fire_same_sender_inbound_merges_within_window()
    {
        await using var h = await HarnessAsync(debounceWindowMs: 150);
        await h.BindChannelAsync();

        await h.Bridge.HandleInboundAsync(TelegramText(h.ChatId, "line one", title: "Family", author: "Mike"), CancellationToken.None);
        await h.Bridge.HandleInboundAsync(TelegramText(h.ChatId, "line two", title: "Family", author: "Mike"), CancellationToken.None);
        await h.Bridge.HandleInboundAsync(TelegramText(h.ChatId, "line three", title: "Family", author: "Mike"), CancellationToken.None);

        // Real-clock debounce (150ms window): wait for the flush to land.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && h.Adapter.SubmittedBodies.Count == 0)
            await Task.Delay(25);

        var body = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        body.ShouldContain("[Telegram \"Family\" — Mike ");
        body.ShouldContain("] line one\nline two\nline three");
        body.Split("[Telegram").Length.ShouldBe(2, "exactly ONE envelope header for the merged flush");
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(1, "one correlation per flush, not per message");
    }

    // PR 8: channel-routed messages carry the batching metadata (origin + conversation key) and
    // the frozen ChannelPromptFormat envelope; UI enqueues stay Ui-origin with no key.
    [Test]
    public async Task Bridge_enqueues_with_channel_origin_and_conversation_key()
    {
        await using var h = await HarnessAsync();
        await h.BindChannelAsync();
        // Park the session as working so the message stays queued (rows are inspectable).
        await h.MarkSessionWorkingAsync();

        var msg = TelegramText(h.ChatId, "check the origin", title: "Family", author: "Mike");
        await h.Bridge.HandleInboundAsync(msg, CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Origin.ShouldBe(QueuedMessageOrigin.Channel);
        row.ConversationKey.ShouldBe($"telegram:{h.ChatId}");
        row.Body.ShouldContain("[Telegram \"Family\" — Mike ");
        row.Body.ShouldContain("] check the origin");
    }

    [Test]
    public async Task Ui_enqueue_keeps_ui_origin()
    {
        await using var h = await HarnessAsync();
        await h.MarkSessionWorkingAsync();

        var queue = h.Provider.GetRequiredService<SessionMessageQueueService>();
        await queue.EnqueueAsync(h.SessionId, "typed in the web ui", MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Origin.ShouldBe(QueuedMessageOrigin.Ui);
        row.ConversationKey.ShouldBeNull();
    }

    [Test]
    [Arguments("Done. The bill is paid.", ChannelReplyKind.Answer)]
    [Arguments("Which day suits you?", ChannelReplyKind.Question)]
    [Arguments("Two options:\n1. Tuesday\n2. Thursday\nWhich one should I book?", ChannelReplyKind.Question)]
    [Arguments("Is it done? Yes — all sorted, nothing left to do.", ChannelReplyKind.Answer)]
    public async Task ClassifyKind_tells_answers_from_questions(string text, ChannelReplyKind expected)
    {
        ChannelReplyDispatcher.ClassifyKind(text).ShouldBe(expected);
        await Task.CompletedTask;
    }

    // ---------- harness ----------

    private static ChannelMessage TelegramText(
        string chatId, string text, string? title = null, string? author = null) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Channel = "telegram",
        ChannelMessageId = Guid.NewGuid().ToString("n")[..12],
        Conversation = new Conversation { Id = chatId, Kind = ConversationKind.Group, Title = title },
        Author = new Participant { Id = "1001", DisplayName = author ?? "Tester" },
        Timestamp = DateTimeOffset.UtcNow,
        Text = text,
        ReplyHandle = chatId,
        Raw = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
    };

    private static async Task<Harness> HarnessAsync(int debounceWindowMs = 0)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        var messaging = new FakeAntiphonMessagingClient();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings()));
        // DebounceWindowMs 0 = passthrough: these tests assert synchronous routing; the debounce
        // behaviour has its own suites (ChannelInboundDebouncerTests + the rapid-fire bridge tests).
        services.AddSingleton(Options.Create(new ChannelBridgeSettings { Enabled = true, DebounceWindowMs = debounceWindowMs }));
        services.AddSingleton<ChannelInboundDebouncer>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddScoped<ChatChannelService>();
        services.AddSingleton(sp => new ChannelReplyDispatcher(
            sp.GetRequiredService<IServiceScopeFactory>(),
            messaging,
            sp.GetRequiredService<IOptions<ChannelBridgeSettings>>(),
            sp.GetRequiredService<TimeProvider>(),
            NullLogger<ChannelReplyDispatcher>.Instance));
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var chatId = $"-100{Random.Shared.Next(100000, 999999)}";
        var now = DateTime.UtcNow;

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Agents.Add(new Agent
            {
                Id = agentId,
                Name = $"BridgeTestAgent-{agentId:N}"[..30],
                Slug = $"bridge-test-{agentId:N}"[..20],
                WorkingDirectory = Path.GetTempPath(),
                Status = AgentStatus.Working,
                PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                CardId = null,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            await db.SaveChangesAsync();
        }

        var runtime = provider.GetRequiredService<AgentSessionRuntime>();
        var adapter = new FakeAgentProtocolAdapter();
        runtime.Register(sessionId, adapter);

        var dispatcher = provider.GetRequiredService<ChannelReplyDispatcher>();
        var bridge = new ChannelBridgeService(
            messaging,
            dispatcher,
            provider.GetRequiredService<SessionMessageQueueService>(),
            provider.GetRequiredService<ChannelInboundDebouncer>(),
            eventBus,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(),
            provider.GetRequiredService<TimeProvider>(),
            NullLogger<ChannelBridgeService>.Instance);

        return new Harness(provider, bridge, dispatcher, messaging, adapter, eventBus, sessionId, agentId, chatId);
    }

    private sealed record Harness(
        ServiceProvider Provider,
        ChannelBridgeService Bridge,
        ChannelReplyDispatcher Dispatcher,
        FakeAntiphonMessagingClient Messaging,
        FakeAgentProtocolAdapter Adapter,
        MockEventBus EventBus,
        Guid SessionId,
        Guid AgentId,
        string ChatId) : IAsyncDisposable
    {
        public ChatChannelService Channels()
        {
            var scope = Provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ChatChannelService>();
        }

        public async Task BindChannelAsync()
        {
            // Discover the channel with a throwaway message, then bind it to the test agent.
            await Bridge.HandleInboundAsync(
                new ChannelMessage
                {
                    Id = Guid.NewGuid().ToString("n"),
                    Channel = "telegram",
                    ChannelMessageId = "seed",
                    Conversation = new Conversation { Id = ChatId, Kind = ConversationKind.Group, Title = "Family" },
                    Author = new Participant { Id = "1001" },
                    Timestamp = DateTimeOffset.UtcNow,
                    Text = null, // no text → recorded but never routed
                    ReplyHandle = ChatId,
                    Raw = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
                },
                CancellationToken.None);

            await using var scope = Provider.CreateAsyncScope();
            var channels = scope.ServiceProvider.GetRequiredService<ChatChannelService>();
            var channel = (await channels.GetAllAsync(CancellationToken.None))
                .Single(c => c.ExternalId == ChatId);
            await channels.UpdateAsync(
                channel.Id, new UpdateChatChannelRequest(AgentId: AgentId), CancellationToken.None);
        }

        public async Task MarkSessionWorkingAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var baseSeq = (await db.TranscriptEntries
                .Where(t => t.AgentSessionId == SessionId)
                .MaxAsync(t => (long?)t.Sequence)) ?? 0;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = baseSeq + 1,
                Kind = TranscriptKinds.AssistantText, Text = "working", CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public async Task InsertEntryAsync(string kind, string? text, string? stopReason = null)
        {
            await using var scope = Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var baseSeq = (await db.TranscriptEntries
                .Where(t => t.AgentSessionId == SessionId)
                .MaxAsync(t => (long?)t.Sequence)) ?? 0;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = baseSeq + 1,
                Kind = kind, Text = text, StopReason = stopReason, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public async Task InsertTurnAsync(string prompt, string response)
        {
            await using var scope = Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var baseSeq = (await db.TranscriptEntries
                .Where(t => t.AgentSessionId == SessionId)
                .MaxAsync(t => (long?)t.Sequence)) ?? 0;
            var now = DateTime.UtcNow;
            db.TranscriptEntries.AddRange(
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = baseSeq + 1,
                    Kind = TranscriptKinds.UserPrompt, Text = prompt, CreatedAt = now,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = baseSeq + 2,
                    Kind = TranscriptKinds.AssistantText, Text = response, CreatedAt = now,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = baseSeq + 3,
                    Kind = TranscriptKinds.TurnEnd, StopReason = "end_turn", CreatedAt = now,
                });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                await db.ChatChannels.Where(c => c.ExternalId == ChatId).ExecuteDeleteAsync();
                await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
                await db.Agents.Where(a => a.Id == AgentId).ExecuteDeleteAsync();
            }
            await Provider.DisposeAsync();
        }
    }
}
