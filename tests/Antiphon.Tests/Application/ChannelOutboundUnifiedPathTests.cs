using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
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
    // World teardowns run in the background (World.DisposeAsync); none outlives its test.
    [After(Test)]
    public Task DrainWorldTeardownsAsync() => World.DrainTeardownsAsync();

    // A teardown a test's drain gave up on (hung) is still awaited before the class ends.
    [After(Class)]
    public static Task DrainHungWorldTeardownsAsync() => World.DrainTeardownsAsync();

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
        // An implied Delegation bundle is eligible even with an empty text-origin policy.
        // Exact NO_REPLY still withholds that bundle until the same window gains real prose.
        await using var implied = await World.CreateAsync(attachmentsOnly: true);
        var task = await implied.BundleAsync();
        var source = await implied.MachineAsync("NO_REPLY", QueuedMessageOrigin.Delegation, task);
        await implied.DispatchAsync(); await implied.DrainAsync();
        implied.H.Messaging.SentReplies.ShouldBeEmpty();
        await using (var db = implied.Db())
        {
            (await db.SessionQueuedMessages.SingleAsync(m => m.Id == source)).ChannelOutboundDeliveryId.ShouldBeNull();
            (await db.AgentTasks.SingleAsync(t => t.Id == task)).DeliverableDeliveredAt.ShouldBeNull();
        }
        await implied.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "complete implied source answer");
        await implied.DispatchAsync(); await implied.DrainAsync();
        implied.H.Messaging.SentReplies.ShouldHaveSingleItem().Attachments.ShouldHaveSingleItem()
            .Content.ShouldBe("frozen implied source"u8.ToArray());
        await using (var db = implied.Db())
        {
            (await db.SessionQueuedMessages.SingleAsync(m => m.Id == source)).ChannelReplySettledAt.ShouldNotBeNull();
            (await db.AgentTasks.SingleAsync(t => t.Id == task)).DeliverableDeliveredAt.ShouldNotBeNull();
        }
    }

    [Test]
    public async Task C519_Api_error_withholds_the_whole_window()
    {
        foreach (var kind in new[] { "main", "trailing", "machine" })
        foreach (var position in new[] { "before", "middle", "after" })
        {
            await using var w = await World.CreateAsync();
            if (kind == "machine") await w.MachineAsync("otherwise valid prose");
            else await w.MainAsync(kind == "trailing" ? "NO_REPLY" : "otherwise valid prose");
            if (kind == "trailing") { await w.DispatchAsync(); await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "valid tail"); }
            if (position == "before")
            {
                await using var errors = w.Db();
                var first = await errors.TranscriptEntries.Where(t => t.AgentSessionId == w.H.SessionId
                    && t.Kind == TranscriptKinds.AssistantText).OrderByDescending(t => t.Sequence).FirstAsync();
                first.Text = "API error"; first.IsApiError = true;
                await errors.SaveChangesAsync();
            }
            else await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "API error", isApiError: true);
            if (position != "after")
                await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "otherwise valid suffix prose");
            await w.DispatchAsync(); await w.DrainAsync();
            w.H.Messaging.SentReplies.ShouldBeEmpty();
            await using var db = w.Db();
            (await db.ChannelOutboundDeliveries.AnyAsync(d => d.PublishedAt != null)).ShouldBeFalse();
            await w.MainAsync("valid companion after the withheld window");
            await w.DispatchAsync(); await w.DrainAsync();
            w.H.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("valid companion after the withheld window");
        }
    }

    [Test]
    public Task C519_Accepted_target_is_independent_of_failed_sibling() =>
        new ChannelOutboundDeliveryTests().VerifyTargetIndependenceAsync(true);

    [Test]
    public Task C519_Deferred_runtime_releases_without_claiming_publication() =>
        new AgentTaskReplyIntegrationTests().VerifyDeferredRuntimeAsync();

    // A world's teardown (its harness and its cloned database: about 0.1 s of each ~0.35 s world on the HP laptop on AC)
    // runs in the background while the next world is built and used. A world starts no process and has its own schema
    // and harness, so a teardown still running touches nothing the next one uses. A new world first waits for every
    // teardown but the latest, so at most one overlaps it. After each test every teardown is awaited (the class's
    // After(Test) hook; the class is NotInParallel): no world outlives its test, and every teardown failure (each one,
    // harness and schema both) fails the test that made it, naming the world.
    private sealed class World(BridgeQueueHarness harness, IsolatedTestSchema schema, string key, Guid projectId) : IAsyncDisposable
    {
        // A hang bound, not a budget (a teardown normally takes about 0.1 s): long enough for a loaded host's database
        // drop, short enough to name a stuck world well inside the test's own timeout.
        private static readonly TimeSpan TeardownHung = TimeSpan.FromSeconds(30);
        private static readonly List<(string World, Task Teardown)> Teardowns = [];
        private int _disposed;

        public BridgeQueueHarness H => harness;
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(harness.ConnectionString));
        public static async Task<World> CreateAsync(bool allowSystem = false, string profile = "none", bool attachmentsOnly = false)
        {
            (string World, Task Teardown)[] earlier;
            lock (Teardowns) earlier = [.. Teardowns.Take(Math.Max(0, Teardowns.Count - 1))];
            await AwaitAsync(earlier);
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var settings = new ChannelOutboundSettings { UnifiedRecoveryEnabled = true };
            var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                ConnectionString = schema.ConnectionString,
                Bridge = new ChannelBridgeSettings { Enabled = true, MachineTurnTextOrigins = attachmentsOnly ? [] : allowSystem
                    ? [QueuedMessageOrigin.Delegation, QueuedMessageOrigin.Check, QueuedMessageOrigin.Scheduled, QueuedMessageOrigin.System]
                    : new ChannelBridgeSettings().MachineTurnTextOrigins },
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
            return new(h, schema, "telegram:" + conversation, project);
        }
        public async Task<Guid> MainAsync(string answer)
        {
            var prompt = "original question " + Guid.NewGuid().ToString("N");
            var id = await H.SeedChannelCorrelationAsync(prompt, key); await H.InsertTurnAsync(prompt, answer); return id;
        }
        public async Task<Guid> BundleAsync()
        {
            var id = Guid.NewGuid();
            var dir = Directory.CreateDirectory(Path.Combine(H.TempRoot, "bundle-" + id.ToString("N"))).FullName;
            var bytes = "frozen implied source"u8.ToArray();
            await File.WriteAllBytesAsync(Path.Combine(dir, "source.md"), bytes);
            var manifest = new DeliverableBundleService.SourceManifest(1, true,
                [new("source.md", "source.md", null, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant())], []);
            await File.WriteAllTextAsync(Path.Combine(dir, DeliverableBundleService.SourceManifestName),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await using var db = Db();
            db.AgentTasks.Add(new() { Id = id, RootTaskId = id, ProjectId = projectId,
                AgentId = H.AgentId, Title = "implied source", Goal = "implied source", WorkingDirectory = H.TempRoot,
                RepoPath = H.TempRoot, Status = AgentTaskStatus.Succeeded, DeliverableBundleDir = dir,
                CreatedAt = H.Now, CompletedAt = H.Now });
            await db.SaveChangesAsync();
            return id;
        }
        public async Task<Guid> MachineAsync(string answer, QueuedMessageOrigin origin = QueuedMessageOrigin.Check, Guid? sourceTask = null)
        {
            var context = await H.SeedChannelCorrelationAsync("prior context", key);
            await using (var db = Db()) await db.SessionQueuedMessages.Where(m => m.Id == context)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ChannelReplySettledAt, H.Now));
            var prompt = sourceTask is Guid task ? $"[task {DelegationReportFormatter.Short(task)} done]\nComplete source report"
                : $"[{origin}] {Guid.NewGuid():N}";
            var id = await H.SeedPendingMessageAsync(prompt, status: QueuedMessageStatus.Sent, origin: origin,
                deliveryAttempts: 1, baselineSequence: await H.CurrentTranscriptMaxSequenceAsync(), lastDeliveryStartedAt: H.Now, legacyNullGeneration: true);
            if (sourceTask is not null)
            {
                await using var db = Db();
                await db.SessionQueuedMessages.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.SourceTaskId, sourceTask));
            }
            await H.InsertTurnAsync(prompt, answer); return id;
        }
        public Task<ChannelReplyDispatchResult> DispatchAsync() => H.Dispatcher.OnTurnEndAsync(H.SessionId, default);
        public async Task DrainAsync() { await H.TickOutboundAsync(); await H.TickOutboundAsync(); }
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return ValueTask.CompletedTask;
            var world = schema.ConnectionString.Split(';').FirstOrDefault(p => p.StartsWith("Database=", StringComparison.OrdinalIgnoreCase)) ?? "world";
            var teardown = Task.Run(async () =>
            {
                var failures = new List<Exception>();
                try { await H.DisposeAsync(); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { failures.Add(ex); }
                try { await schema.DisposeAsync(); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { failures.Add(ex); }
                if (failures.Count > 0)
                    throw new InvalidOperationException($"World {world} teardown failed",
                        failures.Count == 1 ? failures[0] : new AggregateException(failures));
            });
            lock (Teardowns) Teardowns.Add((world, teardown));
            return ValueTask.CompletedTask;
        }

        public static async Task DrainTeardownsAsync()
        {
            (string World, Task Teardown)[] all;
            lock (Teardowns) all = [.. Teardowns];
            await AwaitAsync(all);
        }

        // Every failure collected; an entry leaves the list only once it has ended, so a hung one is still awaited later.
        private static async Task AwaitAsync((string World, Task Teardown)[] entries)
        {
            var failures = new List<Exception>();
            foreach (var entry in entries)
            {
                try { await entry.Teardown.WaitAsync(TeardownHung); }
                catch (TimeoutException) { failures.Add(new TimeoutException($"World {entry.World} teardown still running after {TeardownHung.TotalSeconds:0} s")); continue; }
                catch (Exception ex) when (ex is not OutOfMemoryException) { failures.Add(ex); }
                lock (Teardowns) Teardowns.Remove(entry);
            }
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("World teardowns failed", failures);
        }
    }
}
