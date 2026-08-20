using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Messaging.Slack;
using Antiphon.Messaging.Tests.FakeSlack;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests;

/// <summary>
/// Integration tests for <see cref="SlackChannelAdapter"/> against the <see cref="FakeSlackServer"/>:
/// the Socket Mode receive loop, event normalization, and the outbound send/upload paths — all
/// entirely offline, against a fake whose wire shape mirrors Slack's.
/// </summary>
public sealed class SlackChannelAdapterTests
{
    private static SlackSettings Settings(FakeSlackServer fake) => new()
    {
        ApiBaseUrl = fake.ApiBaseUrl,
        BotToken = fake.BotToken,
        AppToken = fake.AppToken,
        ErrorBackoffSeconds = 0,
    };

    private static SlackChannelAdapter NewAdapter(FakeSlackServer fake, SlackSettings? settings = null) =>
        new(new HttpClient(), settings ?? Settings(fake), NullLogger<SlackChannelAdapter>.Instance);

    private static async Task<ChannelMessage?> FirstMessageAsync(SlackChannelAdapter adapter, int seconds = 20)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        await foreach (var msg in adapter.ReceiveAsync(cts.Token))
            return msg;
        return null;
    }

    // ---------- capabilities ----------

    [Test]
    public async Task Capabilities_declare_slack_and_threads()
    {
        await using var fake = new FakeSlackServer();
        var caps = NewAdapter(fake).Capabilities;

        caps.Channel.ShouldBe("slack");
        caps.Threads.ShouldBeTrue("a Slack thread is addressable, unlike Telegram's");
        caps.TypingIndicator.ShouldBeFalse("the Web API has no bot typing indicator");
        caps.MarkdownFlavor.ShouldBe("Markdown");
        caps.MaxTextLength.ShouldBe(4000);
    }

    // ---------- inbound normalization ----------

    [Test]
    public async Task Normalizes_an_inbound_channel_message()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.RegisterUser("U0ALICE", "alice", displayName: "Alice A");
        fake.RegisterConversation("C0ENG", "eng-antiphon");
        fake.EnqueueMessage("C0ENG", "U0ALICE", "hello bot");

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg.ShouldNotBeNull();
        msg!.Channel.ShouldBe("slack");
        msg.Text.ShouldBe("hello bot");
        msg.Conversation.Id.ShouldBe("C0ENG");
        msg.Conversation.Kind.ShouldBe(ConversationKind.Channel);
        msg.Conversation.Title.ShouldBe("eng-antiphon");
        msg.Author.Id.ShouldBe("U0ALICE");
        msg.Author.Username.ShouldBe("alice");
        msg.Author.DisplayName.ShouldBe("Alice A");
        msg.Author.IsSelf.ShouldBeFalse();
        msg.ChannelMessageId.ShouldNotBeNullOrEmpty();
        msg.ReplyHandle.ShouldBe("C0ENG", "a message outside a thread addresses the channel itself");
        msg.Raw.GetProperty("event").GetProperty("channel").GetString().ShouldBe("C0ENG");
    }

    [Test]
    public async Task Direct_message_normalizes_as_a_direct_conversation()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueueMessage("D0MIKE", "U0MIKE", "dm please", channelType: "im");

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg!.Conversation.Kind.ShouldBe(ConversationKind.Direct);
        msg.Conversation.Title.ShouldBeNull("a DM has no name");
    }

    [Test]
    public async Task Private_channel_normalizes_as_a_group()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueueMessage("G0PRIV", "U0MIKE", "private", channelType: "group");

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg!.Conversation.Kind.ShouldBe(ConversationKind.Group);
    }

    [Test]
    public async Task Thread_reply_carries_the_thread_ts_in_the_reply_handle()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueueMessage("C0ENG", "U0ALICE", "in a thread", threadTs: "1700000000.000100", ts: "1700000042.000200");

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg!.ReplyHandle.ShouldBe("C0ENG|1700000000.000100", "so the reply lands back in the thread");
        msg.ReplyTo.ShouldNotBeNull();
        msg.ReplyTo!.ChannelMessageId.ShouldBe("1700000000.000100");
        msg.ChannelMessageId.ShouldBe("1700000042.000200");
        msg.Timestamp.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1700000042), TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task Thread_parent_itself_is_not_its_own_reply_target()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        // Slack stamps the PARENT of a thread with thread_ts == ts once it has replies.
        fake.EnqueueMessage("C0ENG", "U0ALICE", "the parent", threadTs: "1700000000.000100", ts: "1700000000.000100");

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg!.ReplyHandle.ShouldBe("C0ENG|1700000000.000100");
        msg.ReplyTo.ShouldBeNull("a message does not reply to itself");
    }

    [Test]
    public async Task Mentions_are_parsed_and_rendered_as_names()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.RegisterUser("U0ALICE", "alice", displayName: "Alice A");
        fake.EnqueueMessage("C0ENG", "U0ALICE", $"<@{fake.BotUserId}> ping <@U0ALICE> &amp; friends");

        var settings = Settings(fake);
        settings.BotUserId = fake.BotUserId;
        var msg = await FirstMessageAsync(NewAdapter(fake, settings));

        msg!.Mentions.Count.ShouldBe(2);
        msg.Mentions[0].Id.ShouldBe(fake.BotUserId);
        msg.Mentions[0].IsMe.ShouldBeTrue("mentions of the bot are how a channel addresses it — we do NOT subscribe to app_mention");
        msg.Mentions[1].Id.ShouldBe("U0ALICE");
        msg.Mentions[1].IsMe.ShouldBeFalse();
        msg.Text.ShouldBe($"@{fake.BotUserId} ping @Alice A & friends",
            "unresolvable ids pass through; entities are unescaped exactly once");
    }

    [Test]
    public async Task Link_and_channel_tokens_are_flattened_to_readable_text()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueueMessage("C0ENG", "U0ALICE", "see <https://example.com|the docs> in <#C0OTHER|general>");

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg!.Text.ShouldBe("see the docs in #general");
    }

    // ---------- the reply-loop guard ----------

    [Test]
    public async Task Our_own_echo_is_dropped_on_bot_id()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        // Slack delivers the bot's OWN chat.postMessage back down this same event stream.
        fake.EnqueueMessage("C0ENG", fake.BotUserId, "the reply we just sent", botId: fake.BotId);
        fake.EnqueueMessage("C0ENG", "U0ALICE", "a real human message");

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg!.Text.ShouldBe("a real human message", "the echo must never round-trip into the agent");
    }

    [Test]
    public async Task Our_own_echo_is_dropped_on_our_user_id()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        // No bot_id on this one — only the configured/auth.test bot user id identifies it.
        fake.EnqueueMessage("C0ENG", fake.BotUserId, "an echo without a bot_id");
        fake.EnqueueMessage("C0ENG", "U0ALICE", "a real human message");

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg!.Text.ShouldBe("a real human message");
    }

    [Test]
    public async Task Bot_user_id_is_resolved_from_auth_test_when_not_configured()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueueMessage("C0ENG", fake.BotUserId, "echo");
        fake.EnqueueMessage("C0ENG", "U0ALICE", "human");

        var adapter = NewAdapter(fake);   // BotUserId left empty on purpose
        var msg = await FirstMessageAsync(adapter);

        adapter.BotUserId.ShouldBe(fake.BotUserId);
        fake.AuthTestCalls.ShouldBe(1, "identity is resolved once per connection, not per message");
        msg!.Text.ShouldBe("human");
    }

    // ---------- filtering ----------

    [Test]
    public async Task Allowlist_drops_conversations_it_does_not_name()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        var settings = Settings(fake);
        settings.AllowedConversationIds = ["C0ALLOWED"];
        fake.EnqueueMessage("C0DENIED", "U0ALICE", "not for you");
        fake.EnqueueMessage("C0ALLOWED", "U0ALICE", "for you");

        var msg = await FirstMessageAsync(NewAdapter(fake, settings));

        msg!.Text.ShouldBe("for you");
        msg.Conversation.Id.ShouldBe("C0ALLOWED");
    }

    [Test]
    public async Task Unhandled_subtypes_are_skipped()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueueMessage("C0ENG", "U0ALICE", "edited wrapper", subtype: "message_changed");
        fake.EnqueueMessage("C0ENG", "U0ALICE", "someone joined", subtype: "channel_join");
        fake.EnqueueMessage("C0ENG", "U0ALICE", "the real one");

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg!.Text.ShouldBe("the real one");
    }

    // ---------- acks ----------

    [Test]
    public async Task Every_envelope_is_acked_including_ones_we_drop()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        var dropped = fake.EnqueueMessage("C0ENG", fake.BotUserId, "our own echo", botId: fake.BotId);
        var kept = fake.EnqueueMessage("C0ENG", "U0ALICE", "human");

        await FirstMessageAsync(NewAdapter(fake));

        // Slack redelivers whatever it has not seen acked — a dropped event must still be acked.
        await WaitUntilAsync(() => fake.Acks.Count >= 2);
        fake.Acks.ShouldContain(dropped);
        fake.Acks.ShouldContain(kept);
    }

    [Test]
    public async Task An_envelope_is_acked_before_its_attachments_are_hydrated()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        var bytes = "%PDF-1.4 slow"u8.ToArray();
        fake.RegisterFile("F0SLOW", bytes);
        fake.PauseDownloads();   // the download cannot complete until the test releases it
        var envelopeId = fake.EnqueueMessage(
            "C0ENG", "U0ALICE", "here", subtype: "file_share",
            files: [fake.FileEntry("F0SLOW", "slow.pdf", "application/pdf", bytes.Length)]);

        var adapter = NewAdapter(fake);
        var receive = FirstMessageAsync(adapter);

        // If the ack came AFTER hydration this would never arrive and the test would time out —
        // which is exactly the redelivery-storm shape the ack-first rule exists to prevent.
        await WaitUntilAsync(() => fake.Acks.Contains(envelopeId));
        fake.ResumeDownloads();

        var msg = await receive;
        msg!.Attachments.ShouldHaveSingleItem().Content.ShouldBe(bytes);
    }

    // ---------- inbound attachments ----------

    [Test]
    public async Task Inbound_file_share_downloads_and_inlines_the_bytes()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };   // PNG magic
        fake.RegisterFile("F0PNG", bytes, "image/png");
        fake.EnqueueMessage(
            "C0ENG", "U0ALICE", "look at this", subtype: "file_share",
            files: [fake.FileEntry("F0PNG", "shot.png", "image/png", bytes.Length)]);

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg!.Text.ShouldBe("look at this");
        var att = msg.Attachments.ShouldHaveSingleItem();
        att.Kind.ShouldBe(AttachmentKind.Image);
        att.Name.ShouldBe("shot.png");
        att.Mime.ShouldBe("image/png");
        att.ChannelRef.ShouldBe("F0PNG");
        att.Content.ShouldBe(bytes);
    }

    [Test]
    public async Task Oversized_inbound_attachment_keeps_metadata_only()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.RegisterFile("F0BIG", [1, 2, 3]);
        var settings = Settings(fake);
        settings.MaxInlineAttachmentBytes = 2;   // tiny cap — the 3-byte file must not download
        fake.EnqueueMessage(
            "C0ENG", "U0ALICE", "big one", subtype: "file_share",
            files: [fake.FileEntry("F0BIG", "big.bin", "application/octet-stream", 3)]);

        var msg = await FirstMessageAsync(NewAdapter(fake, settings));

        var att = msg!.Attachments.ShouldHaveSingleItem();
        att.Content.ShouldBeNull("over-cap files must pass through metadata-only");
        att.ChannelRef.ShouldBe("F0BIG");
    }

    [Test]
    public async Task A_failed_download_never_loses_the_message()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        // No RegisterFile — url_private 404s.
        fake.EnqueueMessage(
            "C0ENG", "U0ALICE", "ghost file", subtype: "file_share",
            files: [fake.FileEntry("F0GHOST", "ghost.bin", "application/octet-stream", 3)]);

        var msg = await FirstMessageAsync(NewAdapter(fake));

        msg.ShouldNotBeNull("a broken download must never drop the inbound message");
        msg!.Text.ShouldBe("ghost file");
        msg.Attachments.ShouldHaveSingleItem().Content.ShouldBeNull();
    }

    [Test]
    public async Task An_html_sign_in_page_is_never_inlined_as_the_file()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        var settings = Settings(fake);
        settings.BotToken = "xoxb-wrong-token";   // url_private answers HTTP 200 + a sign-in page
        fake.EnqueueMessage(
            "C0ENG", "U0ALICE", "auth trouble", subtype: "file_share",
            files: [fake.FileEntry("F0AUTH", "doc.pdf", "application/pdf", 3)]);

        // The bot token is wrong, so auth.test/users.info fail too — the message must still arrive.
        var msg = await FirstMessageAsync(NewAdapter(fake, settings));

        msg!.Attachments.ShouldHaveSingleItem().Content.ShouldBeNull();
    }

    // ---------- outbound ----------

    [Test]
    public async Task Send_posts_to_chat_post_message_and_returns_the_ts()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply { Channel = "slack", ConversationId = "C0ENG", Text = "hi back" },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        result.ChannelMessageId.ShouldNotBeNullOrEmpty();
        var sent = fake.SentMessages.ShouldHaveSingleItem();
        sent.Channel.ShouldBe("C0ENG");
        sent.Text.ShouldBe("hi back");
        sent.ThreadTs.ShouldBeNull();
    }

    [Test]
    public async Task Send_renders_markdown_to_mrkdwn()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply { Channel = "slack", ConversationId = "C0ENG", Text = "**bold** and [docs](https://x.dev)" },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.SentMessages.ShouldHaveSingleItem().Text.ShouldBe("*bold* and <https://x.dev|docs>");
    }

    [Test]
    public async Task Plain_formatting_mode_sends_raw_text()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        var settings = Settings(fake);
        settings.Formatting = "Plain";

        var result = await NewAdapter(fake, settings).SendAsync(
            new ChannelReply { Channel = "slack", ConversationId = "C0ENG", Text = "**not converted**" },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.SentMessages.ShouldHaveSingleItem().Text.ShouldBe("**not converted**");
    }

    [Test]
    public async Task Reply_kinds_are_prefixed()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        var adapter = NewAdapter(fake);

        await adapter.SendAsync(
            new ChannelReply { Channel = "slack", ConversationId = "C0ENG", Text = "still going", Kind = ChannelReplyKind.Progress },
            CancellationToken.None);
        await adapter.SendAsync(
            new ChannelReply { Channel = "slack", ConversationId = "C0ENG", Text = "which one?", Kind = ChannelReplyKind.Question },
            CancellationToken.None);

        fake.SentMessages.Select(m => m.Text).ShouldBe(["⏳ still going", "❓ which one?"]);
    }

    [Test]
    public async Task Reply_handle_targets_the_thread_it_came_from()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "slack",
                // The dispatcher sets BOTH; only the handle carries the thread, so it must win.
                ReplyHandle = "C0ENG|1700000000.000100",
                ConversationId = "C0ENG",
                Text = "in-thread",
            },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        var sent = fake.SentMessages.ShouldHaveSingleItem();
        sent.Channel.ShouldBe("C0ENG");
        sent.ThreadTs.ShouldBe("1700000000.000100");
    }

    [Test]
    public async Task Reply_to_message_id_threads_onto_that_message()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();

        await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "slack", ConversationId = "C0ENG",
                ReplyToMessageId = "1700000099.000500", Text = "answering that",
            },
            CancellationToken.None);

        // In Slack, replying TO a message IS threading onto it — ts values are the thread keys.
        fake.SentMessages.ShouldHaveSingleItem().ThreadTs.ShouldBe("1700000099.000500");
    }

    [Test]
    public async Task Raw_overrides_win_over_the_computed_thread()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        using var overrides = JsonDocument.Parse("""{"thread_ts":"1700000111.000900","unfurl_links":false}""");

        await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "slack", ReplyHandle = "C0ENG|1700000000.000100",
                Text = "override me", RawOverrides = overrides.RootElement.Clone(),
            },
            CancellationToken.None);

        var sent = fake.SentMessages.ShouldHaveSingleItem();
        sent.ThreadTs.ShouldBe("1700000111.000900", "RawOverrides merge last");
        sent.RawBody!["unfurl_links"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Test]
    public async Task Raw_override_text_suppresses_mrkdwn_conversion()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        using var overrides = JsonDocument.Parse("""{"text":"*verbatim*"}""");

        await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "slack", ConversationId = "C0ENG",
                Text = "**would have been converted**", RawOverrides = overrides.RootElement.Clone(),
            },
            CancellationToken.None);

        fake.SentMessages.ShouldHaveSingleItem().Text.ShouldBe("*verbatim*");
    }

    [Test]
    public async Task Send_without_a_target_fails_cleanly()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply { Channel = "slack", Text = "nowhere" }, CancellationToken.None);

        result.Ok.ShouldBeFalse();
        result.Error!.ShouldContain("ConversationId or ReplyHandle");
        fake.PostMessageCalls.ShouldBe(0);
    }

    // ---------- outbound attachments (external upload flow) ----------

    [Test]
    public async Task Inline_attachment_goes_through_the_external_upload_flow()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        var bytes = "%PDF-1.4 tiny invoice"u8.ToArray();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "slack", ReplyHandle = "C0ENG|1700000000.000100", Text = "Here's the invoice",
                Attachments =
                [
                    new OutboundAttachment
                    {
                        Kind = AttachmentKind.File, Content = bytes,
                        Name = "invoice.pdf", Mime = "application/pdf", Caption = "the invoice",
                    },
                ],
            },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.SentMessages.ShouldHaveSingleItem().Text.ShouldBe("Here's the invoice");
        var uploaded = fake.UploadedFiles.ShouldHaveSingleItem();
        uploaded.Title.ShouldBe("invoice.pdf");
        uploaded.Bytes.ShouldBe(bytes);
        uploaded.ChannelId.ShouldBe("C0ENG");
        uploaded.ThreadTs.ShouldBe("1700000000.000100", "a file answering a thread belongs in that thread");
        uploaded.InitialComment.ShouldBe("the invoice");
    }

    [Test]
    public async Task Attachment_only_reply_posts_no_message()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "slack", ConversationId = "C0ENG",
                Attachments = [new OutboundAttachment { Kind = AttachmentKind.File, Content = [1, 2, 3], Name = "a.bin" }],
            },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.SentMessages.ShouldBeEmpty("no text → no chat.postMessage");
        fake.UploadedFiles.ShouldHaveSingleItem().Title.ShouldBe("a.bin");
    }

    [Test]
    public async Task Multiple_attachments_upload_one_each_in_order()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "slack", ConversationId = "C0ENG", Text = "two files",
                Attachments =
                [
                    new OutboundAttachment { Kind = AttachmentKind.File, Content = [1], Name = "one.txt" },
                    new OutboundAttachment { Kind = AttachmentKind.File, Content = [2], Name = "two.txt" },
                ],
            },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.UploadedFiles.Select(f => f.Title).ShouldBe(["one.txt", "two.txt"]);
    }

    [Test]
    public async Task Source_only_attachment_is_posted_as_a_link()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();

        // Slack has no "fetch this URL for me" send, so the reply must not silently lose the file.
        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "slack", ConversationId = "C0ENG",
                Attachments =
                [
                    new OutboundAttachment
                    {
                        Kind = AttachmentKind.File, Source = "https://example.com/report.pdf",
                        Name = "report.pdf", Caption = "the report",
                    },
                ],
            },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.UploadedFiles.ShouldBeEmpty();
        var sent = fake.SentMessages.ShouldHaveSingleItem();
        sent.Text!.ShouldContain("<https://example.com/report.pdf|report.pdf>");
        sent.Text!.ShouldContain("the report");
    }

    [Test]
    public async Task Attachment_with_neither_content_nor_source_fails_cleanly()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();

        var result = await NewAdapter(fake).SendAsync(
            new ChannelReply
            {
                Channel = "slack", ConversationId = "C0ENG",
                Attachments = [new OutboundAttachment { Kind = AttachmentKind.File }],
            },
            CancellationToken.None);

        result.Ok.ShouldBeFalse();
        result.Error!.ShouldContain("neither Content nor Source");
        fake.UploadedFiles.ShouldBeEmpty();
    }

    [Test]
    public async Task A_failing_text_send_stops_the_attachments_behind_it()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        var settings = Settings(fake);
        settings.SendRetryAttempts = 0;
        fake.EnqueuePostMessageError("channel_not_found");

        var result = await NewAdapter(fake, settings).SendAsync(
            new ChannelReply
            {
                Channel = "slack", ConversationId = "C0GONE", Text = "orphan caption",
                Attachments = [new OutboundAttachment { Kind = AttachmentKind.File, Content = [1], Name = "a.bin" }],
            },
            CancellationToken.None);

        result.Ok.ShouldBeFalse();
        fake.UploadedFiles.ShouldBeEmpty("an attachment without its text has no context");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("condition was not met in time");
            await Task.Delay(20);
        }
    }
}
