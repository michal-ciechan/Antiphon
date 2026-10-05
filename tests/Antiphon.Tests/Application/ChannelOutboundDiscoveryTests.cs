using System.Threading.Channels;
using Antiphon.Messaging;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Supervision;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class ChannelOutboundDiscoveryTests
{
    [Test]
    public async Task C519_Startup_recovers_without_signal()
    {
        await using var w = await World.CreateAsync();
        var id = await w.MainAsync("original complete prompt", "original complete answer");
        await w.H.InsertTurnAsync("newer unrelated prompt", "newer unrelated answer");
        using var host = w.Host();
        await host.StartAsync(default);
        try
        {
            await w.CycleAsync(); // Clock is held: only the startup cycle can have run.
            var delivery = await w.DeliveryAsync(id);
            delivery.PromptSequence.ShouldBe(1);
            delivery.State.ShouldBe(ChannelOutboundDeliveryState.Ready);
            w.H.Messaging.SentReplies.ShouldBeEmpty();
            w.Clock.Advance(TimeSpan.FromSeconds(5));
            await w.CycleAsync();
            w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("original complete answer");
            (await w.MemberAsync(id)).ChannelReplySettledAt.ShouldNotBeNull();
        }
        finally { await host.StopAsync(default); }
    }

    [Test]
    public async Task C519_Timer_recovers_without_signal()
    {
        await using var w = await World.CreateAsync();
        using var host = w.Host();
        await host.StartAsync(default);
        try
        {
            await w.CycleAsync();
            var id = await w.MainAsync("inserted after empty startup", "timer complete answer");
            w.H.Messaging.SentReplies.ShouldBeEmpty();
            w.Clock.Advance(TimeSpan.FromSeconds(5));
            await w.CycleAsync();
            (await w.DeliveryAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.Ready);
            w.Clock.Advance(TimeSpan.FromSeconds(5));
            await w.CycleAsync();
            w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("timer complete answer");
        }
        finally { await host.StopAsync(default); }
    }

    [Test]
    public async Task C519_Default_off_does_not_discover()
    {
        await using var w = await World.CreateAsync(false);
        var id = await w.MainAsync("undispatched prompt", "undispatched answer");
        (await w.Discovery.TickAsync(default)).ShouldBe(0);
        (await w.MemberAsync(id)).ChannelOutboundDeliveryId.ShouldBeNull();
        w.H.Messaging.SentReplies.ShouldBeEmpty();
        await w.H.Dispatcher.OnTurnEndAsync(w.H.SessionId, default);
        w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("undispatched answer");
    }

    [Test]
    public async Task C519_Historical_match_requires_complete_prompt()
    {
        await using var w = await World.CreateAsync();
        var body = new string('a', 150) + " complete middle and distinct tail";
        var id = await w.H.SeedChannelCorrelationAsync(body, w.Key);
        await w.H.InsertTurnAsync(new string('a', 150) + " wrong tail", "wrong answer");
        (await w.Discovery.TickAsync(default)).ShouldBe(1);
        (await w.MemberAsync(id)).ChannelOutboundDeliveryId.ShouldBeNull();
        await w.H.InsertTurnAsync(body, "valid complete answer");
        await w.Discovery.TickAsync(default);
        (await w.DeliveryAsync(id)).PromptSequence.ShouldBe(4);
        await w.DrainAsync();
        w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("valid complete answer");
    }

    [Test]
    public async Task C519_Historical_match_requires_marker()
    {
        await using var w = await World.CreateAsync();
        var marked = $"[antiphon-channel:{Guid.NewGuid():N}] full middle and tail";
        var id = await w.H.SeedChannelCorrelationAsync(marked, w.Key);
        await w.H.InsertTurnAsync($"[antiphon-channel:{Guid.NewGuid():N}] full middle and tail", "wrong marker answer");
        await w.Discovery.TickAsync(default);
        (await w.MemberAsync(id)).ChannelOutboundDeliveryId.ShouldBeNull();
        await w.H.InsertTurnAsync(marked, "valid marker answer");
        await w.Discovery.TickAsync(default);
        await w.DrainAsync();
        w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("valid marker answer");
    }

    [Test]
    public async Task C519_Historical_match_requires_source_session()
    {
        await using var w = await World.CreateAsync();
        var foreignSession = Guid.NewGuid();
        var id = await w.H.SeedChannelCorrelationAsync("complete session-specific prompt", w.Key);
        // The other session must be in the same database for a missing session predicate to fail.
        await using (var db = w.Db())
        {
            db.AgentSessions.Add(new AgentSession { Id = foreignSession, CreatedAt = w.H.Now,
                StartedAt = w.H.Now, LastSeenAt = w.H.Now });
            await db.SaveChangesAsync();
        }
        await w.H.InsertTurnAsync("complete session-specific prompt", "foreign answer", foreignSession);
        await w.Discovery.TickAsync(default);
        (await w.MemberAsync(id)).ChannelOutboundDeliveryId.ShouldBeNull();
        await w.H.InsertTurnAsync("complete session-specific prompt", "own complete answer");
        await w.Discovery.TickAsync(default);
        await w.DrainAsync();
        w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("own complete answer");
    }

    [Test]
    public async Task C519_Historical_match_obeys_attempt_floors()
    {
        await using var w = await World.CreateAsync();
        var id = await w.MainAsync("complete floor-sensitive prompt", "stale answer");
        await using (var db = w.Db())
            await db.SessionQueuedMessages.Where(m => m.Id == id).ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.LastDeliveryBaselineSequence, 1L));
        await w.Discovery.TickAsync(default);
        (await w.MemberAsync(id)).ChannelOutboundDeliveryId.ShouldBeNull();
        await w.H.InsertTurnAsync("complete floor-sensitive prompt", "above-floor answer");
        await w.Discovery.TickAsync(default);
        await w.DrainAsync();
        w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("above-floor answer");
    }

    [Test]
    public async Task C519_Historical_native_time_obeys_original_attempt()
    {
        await using var w = await World.CreateAsync();
        var id = await w.H.SeedChannelCorrelationAsync("native time sensitive prompt", w.Key);
        await using (var db = w.Db())
            await db.SessionQueuedMessages.Where(m => m.Id == id).ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.LastDeliveryBaselineSequence, (long?)null));
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "native time sensitive prompt",
            timestamp: w.H.Now.AddMinutes(-1));
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "stale native answer");
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd);
        await w.Discovery.TickAsync(default);
        (await w.MemberAsync(id)).ChannelOutboundDeliveryId.ShouldBeNull();
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "native time sensitive prompt",
            timestamp: w.H.Now);
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "valid native answer");
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd);
        await w.Discovery.TickAsync(default);
        await w.DrainAsync();
        w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("valid native answer");
    }

    [Test]
    public async Task C519_Historical_withholding_does_not_hide_a_later_receipt()
    {
        await using var w = await World.CreateAsync();
        var id = await w.H.SeedChannelCorrelationAsync("complete resumed prompt", w.Key);
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "complete resumed prompt");
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "partial answer");
        await w.H.InsertApiErrorStubAsync();
        await w.Discovery.TickAsync(default);
        (await w.MemberAsync(id)).ChannelOutboundDeliveryId.ShouldBeNull();
        w.H.Messaging.SentReplies.ShouldBeEmpty();
        await w.H.InsertTurnAsync("complete resumed prompt", "complete resumed answer");
        await w.Discovery.TickAsync(default);
        await w.DrainAsync();
        w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("complete resumed answer");
    }

    [Test]
    public async Task C519_Historical_answer_stops_at_next_prompt()
    {
        await using var w = await World.CreateAsync();
        var id = await w.MainAsync("old prompt", "old answer");
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "old late fragment");
        await w.H.InsertTurnAsync("new unrelated prompt", "new answer must not leak");
        await w.Discovery.TickAsync(default);
        (await w.DeliveryAsync(id)).LastTextSequence.ShouldBe(4);
        await w.DrainAsync();
        w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("old answer\n\nold late fragment");
    }

    [Test]
    public async Task C519_Machine_context_must_predate_injection()
    {
        foreach (var prior in new[] { false, true })
        {
            await using var w = await World.CreateAsync();
            if (prior) await w.ContextAsync();
            var id = await w.MachineAsync("[Check] complete injected prompt", "complete machine answer");
            if (!prior) await w.ContextAsync();
            await w.Discovery.TickAsync(default);
            if (prior)
            {
                (await w.DeliveryAsync(id)).SendKind.ShouldBe("machine");
                await w.DrainAsync();
                w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("complete machine answer");
            }
            else
            {
                (await w.MemberAsync(id)).ChannelOutboundDeliveryId.ShouldBeNull();
                w.H.Messaging.SentReplies.ShouldBeEmpty();
            }
        }
    }

    [Test]
    public async Task C519_Discovery_closure_waits_for_complete_window()
    {
        await using var w = await World.CreateAsync();
        await w.ContextAsync();
        var system = await w.MachineAsync("[System] complete system prompt", "held system answer", QueuedMessageOrigin.System);
        var silent = await w.MachineAsync("[Check] complete check prompt", "NO_REPLY");
        await w.Discovery.TickAsync(default);
        foreach (var id in new[] { system, silent })
        {
            var member = await w.MemberAsync(id);
            member.ChannelOutboundDeliveryId.ShouldBeNull();
            member.ChannelReplySettledAt.ShouldBeNull();
            if (id == system) member.ChannelReplyDiscoveryClosedAt.ShouldNotBeNull();
            else member.ChannelReplyDiscoveryClosedAt.ShouldBeNull();
        }
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "legitimate late machine text");
        await w.Discovery.TickAsync(default);
        (await w.DeliveryAsync(silent)).SendKind.ShouldBe("machine");
        await w.DrainAsync();
        w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("NO_REPLY\n\nlegitimate late machine text");
        var path = Path.Combine(w.H.TempRoot, "workspace", "closed-source.md");
        await File.WriteAllTextAsync(path, "complete closure companion bytes");
        // Repeating a closed source's complete prompt in a later turn must not resurrect it,
        // even when that later turn has attachments that would otherwise be eligible.
        await w.H.InsertTurnAsync("[System] complete system prompt", $"do not resurrect\n[[attach: {path}]]");
        await w.H.Dispatcher.OnTurnEndAsync(w.H.SessionId, default);
        (await w.MemberAsync(system)).ChannelOutboundDeliveryId.ShouldBeNull();
        w.H.Messaging.SentReplies.Count.ShouldBe(1);
        var fresh = await w.MachineAsync("[System] fresh system attachment prompt", $"fresh attachment\n[[attach: {path}]]",
            QueuedMessageOrigin.System);
        await w.H.Dispatcher.OnTurnEndAsync(w.H.SessionId, default);
        (await w.DeliveryAsync(fresh)).State.ShouldBe(ChannelOutboundDeliveryState.Captured);
        await w.DrainAsync();
        w.H.Messaging.SentReplies.Count.ShouldBe(2);
        w.H.Messaging.SentReplies[^1].Attachments.ShouldHaveSingleItem().Content
            .ShouldBe("complete closure companion bytes"u8.ToArray());
    }

    [Test]
    public async Task C519_Closed_machine_source_cannot_be_captured()
    {
        await using var w = await World.CreateAsync();
        await w.ContextAsync();
        var closed = await w.MachineAsync("[System] closed source prompt", "policy held text", QueuedMessageOrigin.System);
        await w.H.InsertTurnAsync("next unrelated prompt", "unrelated answer");
        await w.Discovery.TickAsync(default);
        (await w.MemberAsync(closed)).ChannelReplyDiscoveryClosedAt.ShouldNotBeNull();
        await using (var scope = w.H.Provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ChannelOutboundService>();
            var error = await Should.ThrowAsync<ConflictException>(() => service.CaptureAsync(
                new ChannelReply { Channel = "telegram", ConversationId = w.Key[9..], Text = "closed source" },
                new ChannelOutboundSource(w.H.SessionId, 1, 2, 2, "machine", [closed]),
                ChannelReplyPreparation.Describe("closed source"), new ChannelBridgeSettings(), default));
            error.Code.ShouldBe("channel_outbound_source_closed");
        }
        (await w.MemberAsync(closed)).ChannelOutboundDeliveryId.ShouldBeNull();
        await using (var db = w.Db()) (await db.ChannelOutboundDeliveries.CountAsync()).ShouldBe(0);
        var fresh = await w.MachineAsync("[Check] fresh eligible companion", "fresh complete machine answer");
        await w.Discovery.TickAsync(default);
        (await w.DeliveryAsync(fresh)).State.ShouldBe(ChannelOutboundDeliveryState.Captured);
        await w.DrainAsync();
        w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("fresh complete machine answer");
    }

    [Test]
    public async Task C519_Complete_machine_batch_has_one_owner_for_every_member()
    {
        await using var w = await World.CreateAsync();
        await w.ContextAsync();
        var bodies = Enumerable.Range(0, 3).Select(i => $"[task {Guid.NewGuid():N} done]\nComplete report middle and tail {i}").ToArray();
        var members = new List<Guid>();
        foreach (var body in bodies)
            members.Add(await w.H.SeedPendingMessageAsync(body, status: QueuedMessageStatus.Sent,
                origin: QueuedMessageOrigin.Delegation, conversationKey: "task:shared-batch",
                deliveryAttempts: 1, baselineSequence: 0, createdAtUtc: w.H.Now,
                lastDeliveryStartedAt: w.H.Now, legacyNullGeneration: true));
        var path = Path.Combine(w.H.TempRoot, "workspace", "batch.md");
        await File.WriteAllTextAsync(path, "complete batch attachment bytes");
        await w.H.InsertTurnAsync(ChannelPromptFormat.FormatBatch(bodies[..^1], bodies[^1]),
            $"complete batch answer\n[[attach: {path}]]");
        await w.Discovery.TickAsync(default);
        var root = await w.DeliveryAsync(members[0]);
        foreach (var member in members)
        {
            (await w.MemberAsync(member)).ChannelOutboundDeliveryId.ShouldBe(root.Id);
            (await w.MemberAsync(member)).ChannelReplySettledAt.ShouldBeNull();
        }
        await w.DrainAsync();
        var reply = w.H.Messaging.SentReplies.ShouldHaveSingleItem();
        reply.Text.ShouldBe("complete batch answer");
        reply.Attachments.ShouldHaveSingleItem().Content.ShouldBe("complete batch attachment bytes"u8.ToArray());
        foreach (var member in members) (await w.MemberAsync(member)).ChannelReplySettledAt.ShouldNotBeNull();
        await using var db = w.Db();
        (await db.ChannelOutboundDeliveries.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task C519_Legacy_settled_sources_are_not_replayed()
    {
        await using var w = await World.CreateAsync();
        var settled = await w.MainAsync("settled original", "do not replay");
        await using (var db = w.Db())
            await db.SessionQueuedMessages.Where(m => m.Id == settled).ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.ChannelReplySettledAt, w.H.Now));
        var open = await w.MainAsync("open original", "recover this answer");
        await w.Discovery.TickAsync(default);
        (await w.MemberAsync(settled)).ChannelOutboundDeliveryId.ShouldBeNull();
        (await w.DeliveryAsync(open)).State.ShouldBe(ChannelOutboundDeliveryState.Captured);
        await w.DrainAsync();
        w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("recover this answer");
        // Reconstruct the scheduling owner: durable member linkage prevents another root/send.
        var restarted = new ChannelOutboundDiscoveryService(w.H.Provider.GetRequiredService<IServiceScopeFactory>(),
            w.H.Dispatcher, Options.Create(new ChannelOutboundSettings { UnifiedRecoveryEnabled = true }),
            NullLogger<ChannelOutboundDiscoveryService>.Instance);
        (await restarted.TickAsync(default)).ShouldBe(0);
        w.H.Messaging.SentReplies.Count.ShouldBe(1);
    }

    [Test]
    public async Task C519_Fair_cursors_and_finite_source_budget()
    {
        await using var w = await World.CreateAsync();
        await using (var db = w.Db())
        {
            for (var i = 0; i < 321; i++) db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = w.H.SessionId, Sequence = i + 1,
                Origin = QueuedMessageOrigin.System, Status = QueuedMessageStatus.Sent,
                Body = "withheld idle source " + i, CreatedAt = w.H.Now.AddSeconds(-400 + i),
                SentAt = w.H.Now.AddSeconds(-400 + i), DeliveryAttempts = 1, LastDeliveryBaselineSequence = 0,
            });
            await db.SaveChangesAsync();
        }
        var target = await w.MainAsync("eligible behind withheld prefix", "fairly recovered answer");
        (await w.Discovery.TickAsync(default)).ShouldBe(320);
        (await w.MemberAsync(target)).ChannelOutboundDeliveryId.ShouldBeNull();
        (await w.Discovery.TickAsync(default)).ShouldBe(2);
        (await w.DeliveryAsync(target)).State.ShouldBe(ChannelOutboundDeliveryState.Captured);
        await w.DrainAsync();
        w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("fairly recovered answer");
    }

    private sealed class World : IAsyncDisposable
    {
        public required BridgeQueueHarness H { get; init; }
        public required IsolatedTestSchema Isolated { get; init; }
        public required ControlledTimeProvider Clock { get; init; }
        public required string Key { get; init; }
        private readonly Channel<bool> _cycles = Channel.CreateUnbounded<bool>();
        public ChannelOutboundDiscoveryService Discovery => H.Provider.GetRequiredService<ChannelOutboundDiscoveryService>();
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(H.ConnectionString));
        public static async Task<World> CreateAsync(bool enabled = true)
        {
            var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
            var clock = new ControlledTimeProvider(DateTimeOffset.UtcNow);
            var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                ConnectionString = isolated.ConnectionString, TimeProvider = clock,
                Bridge = new ChannelBridgeSettings { Enabled = true, MachineTurnTextOrigins = [QueuedMessageOrigin.Check] },
                ConfigureServices = services =>
                {
                    services.AddSingleton<IOptions<ChannelOutboundSettings>>(Options.Create(
                        new ChannelOutboundSettings { UnifiedRecoveryEnabled = enabled }));
                    services.AddSingleton<ChannelOutboundDiscoveryService>();
                },
            });
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
            var projectId = Guid.NewGuid(); var boardId = Guid.NewGuid();
            db.Projects.Add(new Project { Id = projectId, Name = "discovery", CreatedAt = h.Now, UpdatedAt = h.Now });
            db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "discovery", CreatedAt = h.Now, UpdatedAt = h.Now });
            await db.SaveChangesAsync();
            await db.Agents.Where(a => a.Id == h.AgentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.BoardId, boardId));
            var key = "telegram:" + await h.BindChannelAsync();
            return new World { H = h, Isolated = isolated, Clock = clock, Key = key };
        }
        public ChannelOutboundHostedService Host() => new(H.Provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChannelOutboundHostedService>.Instance, Clock)
            { CycleCompletedAsync = _ => { _cycles.Writer.TryWrite(true); return Task.CompletedTask; } };
        public async Task CycleAsync() => await _cycles.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        public async Task<Guid> MainAsync(string prompt, string answer)
        {
            var id = await H.SeedChannelCorrelationAsync(prompt, Key);
            await H.InsertTurnAsync(prompt, answer);
            return id;
        }
        public async Task ContextAsync()
        {
            var id = await H.SeedChannelCorrelationAsync("prior chat context", Key);
            await using var db = Db();
            await db.SessionQueuedMessages.Where(m => m.Id == id).ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.ChannelReplySettledAt, H.Now));
        }
        public async Task<Guid> MachineAsync(string prompt, string answer, QueuedMessageOrigin origin = QueuedMessageOrigin.Check)
        {
            var id = await H.SeedPendingMessageAsync(prompt, status: QueuedMessageStatus.Sent, origin: origin,
                deliveryAttempts: 1, baselineSequence: await H.CurrentTranscriptMaxSequenceAsync(),
                createdAtUtc: H.Now, lastDeliveryStartedAt: H.Now, legacyNullGeneration: true);
            await H.InsertTurnAsync(prompt, answer);
            return id;
        }
        public async Task<SessionQueuedMessage> MemberAsync(Guid id)
        { await using var db = Db(); return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id); }
        public async Task<ChannelOutboundDelivery> DeliveryAsync(Guid id)
        {
            var member = await MemberAsync(id); member.ChannelOutboundDeliveryId.ShouldNotBeNull();
            await using var db = Db();
            return await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == member.ChannelOutboundDeliveryId);
        }
        public async Task DrainAsync() { await H.TickOutboundAsync(); await H.TickOutboundAsync(); }
        public async ValueTask DisposeAsync() { await H.DisposeAsync(); await Isolated.DisposeAsync(); }
    }
}
