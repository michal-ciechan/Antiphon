using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel]
public sealed class ChannelOutboundUnifiedPathTests
{
    [Test]
    public async Task C519_Every_agent_shape_is_captured()
    {
        foreach (var profile in new[] { "none", "MarkdownSources", "EveryAgentReply" })
        foreach (var kind in new[] { "main", "trailing", "machine" })
        {
            await using var w = await World.CreateAsync(profile: profile);
            var member = kind == "machine" ? await w.MachineAsync("original answer")
                : await w.MainAsync(kind == "trailing" ? "NO_REPLY" : "original answer");
            await w.DispatchAsync();
            if (kind == "trailing")
            { await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "original answer"); await w.DispatchAsync(); }
            w.H.Messaging.SentReplies.ShouldBeEmpty();
            await using (var db = w.Db())
            {
                var captured = await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.State == ChannelOutboundDeliveryState.Captured);
                captured.SendKind.ShouldBe(kind);
                captured.InputPath.ShouldBeEmpty();
                captured.PublishedAt.ShouldBeNull();
                ChannelReplyPreparation.Deserialize(captured.CaptureJson!).Body.Text.ShouldBe("original answer");
            }
            await w.H.TickOutboundAsync();
            await using (var db = w.Db())
            {
                var prepared = await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.State != ChannelOutboundDeliveryState.Suppressed);
                prepared.State.ShouldBe(profile == "EveryAgentReply" ? ChannelOutboundDeliveryState.Pending : ChannelOutboundDeliveryState.Ready);
            }
            if (profile != "EveryAgentReply")
            {
                await w.H.TickOutboundAsync();
                w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("original answer");
            }
        }
    }

    [Test]
    public async Task C519_Missing_catalog_preserves_direct_routing()
    {
        // S4's reviewed catalog-less routing amendment explicitly supersedes original PC-20.
        var existing = new ChannelOutboundDispatchIntegrationTests();
        await existing.Missing_catalog_main_publishes_once_and_settles_without_capture(true);
        await existing.Missing_catalog_machine_publishes_once_and_settles_without_capture(true);
        await existing.Missing_catalog_trailing_publishes_once_without_capture(true);
    }

    [Test]
    public async Task C519_Main_silence_keeps_a_suppressed_root()
    {
        await using var w = await World.CreateAsync();
        var member = await w.MainAsync("NO_REPLY"); await w.DispatchAsync();
        await using (var db = w.Db())
        {
            var root = await db.ChannelOutboundDeliveries.SingleAsync();
            root.State.ShouldBe(ChannelOutboundDeliveryState.Suppressed); root.PublishedAt.ShouldBeNull();
            (await db.SessionQueuedMessages.SingleAsync(m => m.Id == member)).ChannelReplySettledAt.ShouldNotBeNull();
        }
        w.H.Messaging.SentReplies.ShouldBeEmpty();
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "NO_REPLY is a token; here is legitimate prose.");
        await w.DispatchAsync(); await w.DrainAsync();
        w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("NO_REPLY is a token; here is legitimate prose.");
    }

    [Test]
    public async Task C519_Machine_silence_leaves_source_unsettled()
    {
        await using var w = await World.CreateAsync();
        var id = await w.MachineAsync("NO_REPLY"); await w.DispatchAsync(); await w.DrainAsync();
        await using var db = w.Db(); var member = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        member.ChannelReplySettledAt.ShouldBeNull(); member.ChannelOutboundDeliveryId.ShouldBeNull();
        member.ChannelReplyDiscoveryClosedAt.ShouldBeNull(); w.H.Messaging.SentReplies.ShouldBeEmpty();
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "legitimate continuation");
        await w.DispatchAsync(); await w.DrainAsync();
        w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("NO_REPLY\n\nlegitimate continuation");
    }

    [Test]
    public async Task C519_Machine_origins_and_attachments_keep_policy()
    {
        foreach (var allowSystem in new[] { false, true })
        foreach (var attachment in new[] { false, true })
        {
            await using var w = await World.CreateAsync(allowSystem);
            var path = Path.Combine(w.H.TempRoot, "workspace", "system.md");
            await File.WriteAllTextAsync(path, "actual system attachment");
            var member = await w.MachineAsync(attachment ? $"NO_REPLY\n[[attach: {path}]]" : "system text", QueuedMessageOrigin.System);
            await w.DispatchAsync(); await w.DrainAsync();
            if (allowSystem || attachment)
            {
                var reply = w.H.Messaging.SentReplies.ShouldHaveSingleItem();
                if (attachment) reply.Attachments.ShouldHaveSingleItem().Content.ShouldBe("actual system attachment"u8.ToArray());
                else reply.Text.ShouldBe("system text");
            }
            else
            {
                w.H.Messaging.SentReplies.ShouldBeEmpty();
                await using var db = w.Db();
                (await db.SessionQueuedMessages.SingleAsync(m => m.Id == member)).ChannelOutboundDeliveryId.ShouldBeNull();
                await w.MachineAsync("allowed Check companion"); await w.DispatchAsync(); await w.DrainAsync();
                w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("allowed Check companion");
            }
        }
    }

    [Test]
    public async Task C519_Api_error_withholds_the_whole_window()
    {
        foreach (var kind in new[] { "main", "trailing", "machine" })
        {
            await using var w = await World.CreateAsync();
            if (kind == "machine") await w.MachineAsync("otherwise valid prose");
            else await w.MainAsync(kind == "trailing" ? "NO_REPLY" : "otherwise valid prose");
            if (kind == "trailing") { await w.DispatchAsync(); await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "valid tail"); }
            await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "API error", isApiError: true);
            await w.DispatchAsync(); await w.DrainAsync();
            w.H.Messaging.SentReplies.ShouldBeEmpty();
            await using var db = w.Db();
            (await db.ChannelOutboundDeliveries.AnyAsync(d => d.PublishedAt != null)).ShouldBeFalse();
        }
    }

    [Test]
    public Task C519_Accepted_target_is_independent_of_failed_sibling() =>
        new ChannelOutboundDeliveryTests().VerifyTargetIndependenceAsync(true);

    [Test]
    public Task C519_Deferred_runtime_releases_without_claiming_publication() =>
        new AgentTaskReplyIntegrationTests().VerifyDeferredRuntimeAsync();

    private sealed class World(BridgeQueueHarness harness, IsolatedTestSchema schema, string key) : IAsyncDisposable
    {
        public BridgeQueueHarness H => harness;
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(harness.ConnectionString));
        public static async Task<World> CreateAsync(bool allowSystem = false, string profile = "none")
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var settings = new ChannelOutboundSettings { UnifiedRecoveryEnabled = true };
            var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                ConnectionString = schema.ConnectionString,
                Bridge = new ChannelBridgeSettings { Enabled = true, MachineTurnTextOrigins = allowSystem
                    ? [QueuedMessageOrigin.Check, QueuedMessageOrigin.System] : [QueuedMessageOrigin.Check] },
                ConfigureServices = s => s.AddSingleton<IOptions<ChannelOutboundSettings>>(Options.Create(settings)),
            });
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
            var project = Guid.NewGuid(); var board = Guid.NewGuid(); var converter = Guid.NewGuid();
            db.Projects.Add(new() { Id = project, Name = "unified" });
            db.Boards.Add(new() { Id = board, ProjectId = project, Name = "unified" });
            db.Agents.Add(new() { Id = converter, BoardId = board, Name = "converter", Slug = "converter", WorkingDirectory = h.TempRoot });
            await db.SaveChangesAsync();
            await db.Agents.Where(a => a.Id == h.AgentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.BoardId, board));
            var conversation = await h.BindChannelAsync();
            if (profile != "none")
            {
                await File.WriteAllTextAsync(Path.Combine(h.TempRoot, "convert.md"), "original conversion prompt");
                settings.Profiles["test"] = new() { ProjectId = project, AgentId = converter, PromptFile = "convert.md",
                    Trigger = Enum.Parse<ChannelOutboundTrigger>(profile) };
                await db.ChatChannels.Where(c => c.ExternalId == conversation).ExecuteUpdateAsync(s => s.SetProperty(c => c.OutboundAgentProfile, "test"));
            }
            return new(h, schema, "telegram:" + conversation);
        }
        public async Task<Guid> MainAsync(string answer)
        { var id = await H.SeedChannelCorrelationAsync("original question", key); await H.InsertTurnAsync("original question", answer); return id; }
        public async Task<Guid> MachineAsync(string answer, QueuedMessageOrigin origin = QueuedMessageOrigin.Check)
        {
            var context = await H.SeedChannelCorrelationAsync("prior context", key);
            await using (var db = Db()) await db.SessionQueuedMessages.Where(m => m.Id == context)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ChannelReplySettledAt, H.Now));
            var prompt = $"[{origin}] {Guid.NewGuid():N}";
            var id = await H.SeedPendingMessageAsync(prompt, status: QueuedMessageStatus.Sent, origin: origin,
                deliveryAttempts: 1, baselineSequence: await H.CurrentTranscriptMaxSequenceAsync(), lastDeliveryStartedAt: H.Now, legacyNullGeneration: true);
            await H.InsertTurnAsync(prompt, answer); return id;
        }
        public Task<ChannelReplyDispatchResult> DispatchAsync() => H.Dispatcher.OnTurnEndAsync(H.SessionId, default);
        public async Task DrainAsync() { await H.TickOutboundAsync(); await H.TickOutboundAsync(); }
        public async ValueTask DisposeAsync() { await H.DisposeAsync(); await schema.DisposeAsync(); }
    }
}
