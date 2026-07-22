using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Pure unit tests for the same-sender inbound debounce (PR 9): FakeTimeProvider drives the
/// sliding window and hard cap deterministically. The rules: same (conversation, sender) merges;
/// different senders or conversations never do; window 0 is synchronous passthrough; continuous
/// typing cannot defer past the cap; a throwing flush is contained.
/// </summary>
public class ChannelInboundDebouncerTests
{
    private static readonly JsonElement EmptyRaw = JsonDocument.Parse("{}").RootElement.Clone();

    private static ChannelMessage Message(string conversationId, string authorId, string text) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Channel = "telegram",
        ChannelMessageId = Guid.NewGuid().ToString("n")[..10],
        Conversation = new Conversation { Id = conversationId, Kind = ConversationKind.Group, Title = "Chat" },
        Author = new Participant { Id = authorId, DisplayName = authorId },
        Timestamp = DateTimeOffset.UtcNow,
        Text = text,
        ReplyHandle = conversationId,
        Raw = EmptyRaw,
    };

    private static ChannelInboundDebouncer Debouncer(FakeTimeProvider time, int windowMs = 500, int maxMs = 2000) =>
        new(
            Options.Create(new ChannelBridgeSettings { DebounceWindowMs = windowMs, DebounceMaxMs = maxMs }),
            time,
            NullLogger<ChannelInboundDebouncer>.Instance);

    // Timer continuations run asynchronously after FakeTimeProvider.Advance — give them a moment.
    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    [Test]
    public async Task Same_sender_messages_within_window_merge_into_one_flush()
    {
        var time = new FakeTimeProvider();
        var debouncer = Debouncer(time);
        var flushes = new List<IReadOnlyList<ChannelInboundDebouncer.Buffered>>();
        Task Flush(IReadOnlyList<ChannelInboundDebouncer.Buffered> b) { lock (flushes) flushes.Add(b); return Task.CompletedTask; }

        await debouncer.AddAsync(Message("c1", "mike", "first"), Flush, CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(200));
        await debouncer.AddAsync(Message("c1", "mike", "second"), Flush, CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(200));
        await debouncer.AddAsync(Message("c1", "mike", "third"), Flush, CancellationToken.None);

        // Quiet window elapses only now.
        time.Advance(TimeSpan.FromMilliseconds(600));
        (await WaitForAsync(() => { lock (flushes) return flushes.Count == 1; })).ShouldBeTrue();

        lock (flushes)
        {
            var batch = flushes.Single();
            batch.Select(b => b.Message.Text).ShouldBe(["first", "second", "third"]);
        }
        debouncer.PendingLanes.ShouldBe(0);
    }

    [Test]
    public async Task Different_senders_same_conversation_do_not_merge()
    {
        var time = new FakeTimeProvider();
        var debouncer = Debouncer(time);
        var flushes = new List<IReadOnlyList<ChannelInboundDebouncer.Buffered>>();
        Task Flush(IReadOnlyList<ChannelInboundDebouncer.Buffered> b) { lock (flushes) flushes.Add(b); return Task.CompletedTask; }

        await debouncer.AddAsync(Message("c1", "mike", "from mike"), Flush, CancellationToken.None);
        await debouncer.AddAsync(Message("c1", "ola", "from ola"), Flush, CancellationToken.None);

        time.Advance(TimeSpan.FromMilliseconds(600));
        (await WaitForAsync(() => { lock (flushes) return flushes.Count == 2; })).ShouldBeTrue(
            "two senders = two lanes = two flushes (the merged envelope header must stay truthful)");
        lock (flushes)
            flushes.SelectMany(f => f).Count().ShouldBe(2);
    }

    [Test]
    public async Task Different_conversations_never_merge()
    {
        var time = new FakeTimeProvider();
        var debouncer = Debouncer(time);
        var flushes = new List<IReadOnlyList<ChannelInboundDebouncer.Buffered>>();
        Task Flush(IReadOnlyList<ChannelInboundDebouncer.Buffered> b) { lock (flushes) flushes.Add(b); return Task.CompletedTask; }

        await debouncer.AddAsync(Message("c1", "mike", "family chat"), Flush, CancellationToken.None);
        await debouncer.AddAsync(Message("c2", "mike", "ops chat"), Flush, CancellationToken.None);

        time.Advance(TimeSpan.FromMilliseconds(600));
        (await WaitForAsync(() => { lock (flushes) return flushes.Count == 2; })).ShouldBeTrue();
    }

    [Test]
    public async Task Hard_cap_flushes_under_continuous_typing()
    {
        var time = new FakeTimeProvider();
        var debouncer = Debouncer(time, windowMs: 500, maxMs: 2000);
        var flushes = new List<IReadOnlyList<ChannelInboundDebouncer.Buffered>>();
        Task Flush(IReadOnlyList<ChannelInboundDebouncer.Buffered> b) { lock (flushes) flushes.Add(b); return Task.CompletedTask; }

        // A message every 400ms — the sliding window alone would defer forever.
        await debouncer.AddAsync(Message("c1", "mike", "m0"), Flush, CancellationToken.None);
        for (var i = 1; i <= 6; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(400));
            await Task.Delay(20); // let the timer loop observe the still-typing state
            await debouncer.AddAsync(Message("c1", "mike", $"m{i}"), Flush, CancellationToken.None);
        }

        (await WaitForAsync(() => { lock (flushes) return flushes.Count >= 1; })).ShouldBeTrue(
            "the hard cap must have fired mid-stream");
        lock (flushes)
            flushes[0].Count.ShouldBeGreaterThanOrEqualTo(4, "the capped flush carries what was buffered so far");
    }

    [Test]
    public async Task Zero_window_is_synchronous_passthrough()
    {
        var time = new FakeTimeProvider();
        var debouncer = Debouncer(time, windowMs: 0);
        var flushes = new List<IReadOnlyList<ChannelInboundDebouncer.Buffered>>();
        Task Flush(IReadOnlyList<ChannelInboundDebouncer.Buffered> b) { lock (flushes) flushes.Add(b); return Task.CompletedTask; }

        await debouncer.AddAsync(Message("c1", "mike", "inline"), Flush, CancellationToken.None);

        flushes.Count.ShouldBe(1, "no timers, no lanes — flushed before AddAsync returned");
        debouncer.PendingLanes.ShouldBe(0);
    }

    [Test]
    public async Task Flush_all_drains_pending_lanes_through_their_own_callbacks()
    {
        var time = new FakeTimeProvider();
        var debouncer = Debouncer(time);
        var flushed = new List<string>();
        Task Flush(IReadOnlyList<ChannelInboundDebouncer.Buffered> b)
        {
            lock (flushed) flushed.AddRange(b.Select(x => x.Message.Text!));
            return Task.CompletedTask;
        }

        await debouncer.AddAsync(Message("c1", "mike", "held one"), Flush, CancellationToken.None);
        await debouncer.AddAsync(Message("c2", "ola", "held two"), Flush, CancellationToken.None);

        await debouncer.FlushAllAsync();

        flushed.OrderBy(t => t).ShouldBe(["held one", "held two"]);
        debouncer.PendingLanes.ShouldBe(0);
    }

    [Test]
    public async Task Throwing_flush_is_contained()
    {
        var time = new FakeTimeProvider();
        var debouncer = Debouncer(time);

        await debouncer.AddAsync(
            Message("c1", "mike", "doomed"),
            _ => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        time.Advance(TimeSpan.FromMilliseconds(600));
        (await WaitForAsync(() => debouncer.PendingLanes == 0)).ShouldBeTrue(
            "a throwing flush must clear the lane, not wedge or fault the process");
    }
}
