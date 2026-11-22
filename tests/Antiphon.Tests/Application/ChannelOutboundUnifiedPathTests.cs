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
    // World teardowns run in the background (World.DisposeAsync); only a hung one outlives its test.
    [After(Test)]
    public Task DrainWorldTeardownsAsync() => World.DrainOwnTeardownsAsync();

    // Any teardown still running (one a test's drain reported hung) ends before the class does.
    [After(Class)]
    public static Task DrainEveryWorldTeardownAsync() => World.DrainEveryTeardownAsync();

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
    // and harness, so a teardown still running touches nothing the next one uses. Each teardown belongs to the test
    // that made it (its TUnit test id). A new world first waits for every teardown of its test but the latest, so at
    // most one of its test's teardowns overlaps it; after each test its teardowns are awaited (After(Test); the class
    // is NotInParallel), and every failure (harness and schema both) fails the test that made it, naming the world.
    // Only a teardown that hangs past TeardownHung outlives its test: its test reports it once and hands it to the class
    // drain, so it may run beside later tests' worlds until the class ends (After(Class), which awaits it unbounded but
    // for TUnit's hook timeout, and reports again only how it finally failed). Phase timings a world records may
    // include one earlier world's teardown running beside them.
    private sealed class World(BridgeQueueHarness harness, IsolatedTestSchema schema, string key, Guid projectId) : IAsyncDisposable
    {
        // A hang bound, not a budget, and only ever spent on a hang: a teardown here measured about 0.1 s (25 worlds,
        // 2.5 s in all, HP laptop on AC, 2026-11-22). It is UnifiedOutboundTransport's same 2 min, sized for a loaded
        // qualification host's DROP DATABASE WITH (FORCE), and one deadline covers every teardown a wait takes.
        private static readonly TimeSpan TeardownHung = TimeSpan.FromMinutes(2);
        // The owner of a hung teardown handed to the class drain: never a TUnit test id, nor the "" of no test context.
        private const string HandedOff = "<hung>";
        private static readonly List<(string Owner, string World, Task Teardown)> Teardowns = [];
        private int _disposed;

        private static string CurrentTest => TUnit.Core.TestContext.Current?.Metadata.TestDetails.TestId ?? "";

        public BridgeQueueHarness H => harness;
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(harness.ConnectionString));
        public static async Task<World> CreateAsync(bool allowSystem = false, string profile = "none", bool attachmentsOnly = false)
        {
            var current = CurrentTest;
            (string Owner, string World, Task Teardown)[] earlier;
            lock (Teardowns)
            {
                var own = Teardowns.Where(t => t.Owner == current).ToArray();
                earlier = own.Take(Math.Max(0, own.Length - 1)).ToArray();
            }
            await AwaitAsync(earlier);
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            BridgeQueueHarness? built = null;
            try
            {
                return await BuildAsync(schema, allowSystem, profile, attachmentsOnly, h => built = h);
            }
            catch (Exception buildFailure)
            {
                // A world that failed part-way still drops what it made, its database even when the harness dispose
                // fails; the build failure stays first in what is reported.
                var cleanup = new List<Exception>();
                if (built is not null)
                {
                    try { await built.DisposeAsync(); }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { cleanup.Add(ex); }
                }
                try { await schema.DisposeAsync(); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { cleanup.Add(ex); }
                if (cleanup.Count == 0) throw;
                throw new AggregateException("World build failed, and so did its cleanup", [buildFailure, .. cleanup]);
            }
        }

        private static async Task<World> BuildAsync(IsolatedTestSchema schema, bool allowSystem, string profile,
            bool attachmentsOnly, Action<BridgeQueueHarness> made)
        {
            var settings = new ChannelOutboundSettings { UnifiedRecoveryEnabled = true };
            var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                ConnectionString = schema.ConnectionString,
                Bridge = new ChannelBridgeSettings { Enabled = true, MachineTurnTextOrigins = attachmentsOnly ? [] : allowSystem
                    ? [QueuedMessageOrigin.Delegation, QueuedMessageOrigin.Check, QueuedMessageOrigin.Scheduled, QueuedMessageOrigin.System]
                    : new ChannelBridgeSettings().MachineTurnTextOrigins },
                ConfigureServices = s => s.AddSingleton<IOptions<ChannelOutboundSettings>>(Options.Create(settings)),
            });
            made(h);
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
            var world = new Npgsql.NpgsqlConnectionStringBuilder(schema.ConnectionString).Database ?? "world";
            var teardown = Task.Run(async () =>
            {
                var failures = new List<Exception>();
                try { await H.DisposeAsync(); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { failures.Add(ex); }
                finally
                {
                    try { await schema.DisposeAsync(); }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { failures.Add(ex); }
                }
                if (failures.Count > 0)
                    throw new InvalidOperationException($"World {world} teardown failed",
                        failures.Count == 1 ? failures[0] : new AggregateException(failures));
            });
            lock (Teardowns) Teardowns.Add((CurrentTest, world, teardown));
            return ValueTask.CompletedTask;
        }

        // After each test: its own teardowns, bounded; a hung one is reported here once and left for the class drain.
        public static async Task DrainOwnTeardownsAsync()
        {
            var current = CurrentTest;
            (string Owner, string World, Task Teardown)[] own;
            lock (Teardowns) own = Teardowns.Where(t => t.Owner == current).ToArray();
            await AwaitAsync(own);
        }

        // Before the class ends: every teardown still listed, unbounded (TUnit's hook timeout still applies). A hung one
        // its test already reported as still running is reported again only if it then fails, with its real cause.
        public static async Task DrainEveryTeardownAsync()
        {
            (string Owner, string World, Task Teardown)[] all;
            lock (Teardowns) all = [.. Teardowns];
            var failures = new List<Exception>();
            foreach (var entry in all)
            {
                try { await entry.Teardown; }
                catch (Exception ex) when (ex is not OutOfMemoryException) { failures.Add(ex); }
                lock (Teardowns) Teardowns.Remove(entry);
            }
            Throw(failures);
        }

        // Every entry awaited, under one TeardownHung deadline for them all, and every failure collected. An ended entry
        // leaves the list; one still running at the deadline is reported and handed to the class drain (taken off its
        // test, so no later wait of a test reports it again).
        private static async Task AwaitAsync((string Owner, string World, Task Teardown)[] entries)
        {
            var failures = new List<Exception>();
            var waited = System.Diagnostics.Stopwatch.StartNew();
            foreach (var entry in entries)
            {
                var left = TeardownHung - waited.Elapsed;
                try { await entry.Teardown.WaitAsync(left > TimeSpan.Zero ? left : TimeSpan.Zero); }
                catch (TimeoutException)
                {
                    failures.Add(new TimeoutException($"World {entry.World} teardown still running after the {TeardownHung.TotalMinutes:0} min hang bound"));
                    lock (Teardowns)
                    {
                        Teardowns.Remove(entry);
                        Teardowns.Add((HandedOff, entry.World, entry.Teardown));
                    }
                    continue;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { failures.Add(ex); }
                lock (Teardowns) Teardowns.Remove(entry);
            }
            Throw(failures);
        }

        private static void Throw(List<Exception> failures)
        {
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("World teardowns failed", failures);
        }
    }
}
