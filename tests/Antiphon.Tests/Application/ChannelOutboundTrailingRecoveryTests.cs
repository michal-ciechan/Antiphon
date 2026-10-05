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
public sealed class ChannelOutboundTrailingRecoveryTests
{
    [Test]
    public async Task C519_Pending_intervals_never_overlap()
    {
        await using var w = await World.CreateAsync();
        var member = await w.MainAsync("original prompt", "root answer");
        await w.DispatchAsync(); await w.DrainAsync();
        var root = await w.RootAsync(member);
        var path = Path.Combine(w.H.TempRoot, "workspace", "late.md");
        var first = await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText,
            $"first tail\n[[attach: {path}]]");
        await w.DispatchAsync();
        var tail = (await w.TailsAsync(root.Id)).ShouldHaveSingleItem();
        tail.LastTextSequence.ShouldBe(first);
        tail.PreparationAttempts.ShouldBe(0);
        w.H.Messaging.SentReplies.Count.ShouldBe(1);
        await w.H.TickOutboundAsync(); // Missing source fails preparation after reservation.
        tail = (await w.TailsAsync(root.Id)).ShouldHaveSingleItem();
        tail.State.ShouldBe(ChannelOutboundDeliveryState.Captured);
        tail.PreparationAttempts.ShouldBe(1);
        tail.NextAttemptAt.ShouldNotBeNull();
        w.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "second tail");
        await w.DispatchAsync();
        var tails = await w.TailsAsync(root.Id);
        tails.Count.ShouldBe(2);
        tails[1].FirstTextSequence.ShouldBe(first + 1);
        tails[1].LastTextSequence.ShouldBe(second);
        tails[0].SourceKey.ShouldNotBe(tails[1].SourceKey);
        (await w.RootAsync(member)).ReservedThroughSequence.ShouldBe(second);
        await File.WriteAllTextAsync(path, "complete first tail bytes");
        w.Clock.Advance(TimeSpan.FromSeconds(30));
        await w.DrainAsync(); await w.DrainAsync();
        w.H.Messaging.SentReplies.Select(r => r.Text).ShouldBe(new[] { "root answer", "first tail", "second tail" });
        w.H.Messaging.SentReplies[1].Attachments.ShouldHaveSingleItem().Content
            .ShouldBe("complete first tail bytes"u8.ToArray());
        var settled = (await w.MemberAsync(member)).ChannelReplySettledAt;
        settled.ShouldNotBeNull();
        await w.Discovery.TickAsync(default); await w.DrainAsync();
        w.H.Messaging.SentReplies.Count.ShouldBe(3);
        (await w.MemberAsync(member)).ChannelReplySettledAt.ShouldBe(settled);
    }

    [Test]
    public async Task C519_Restart_recovers_reserved_tail()
    {
        foreach (var machine in new[] { false, true })
        {
            await using var w = await World.CreateAsync();
            var member = machine ? await w.MachineAsync("root machine") : await w.MainAsync("root prompt", "root main");
            await w.DispatchAsync(); await w.DrainAsync();
            var root = await w.RootAsync(member);
            await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "reserved tail");
            await w.DispatchAsync(); // Captured but not materialized when the owner disappears.
            (await w.TailsAsync(root.Id)).ShouldHaveSingleItem().State.ShouldBe(ChannelOutboundDeliveryState.Captured);
            w.Clock.Advance(TimeSpan.FromSeconds(1));
            await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "undiscovered tail");
            await using var restarted = await w.RestartAsync();
            using var host = new ChannelOutboundHostedService(restarted.Provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ChannelOutboundHostedService>.Instance, w.Clock);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            host.CycleCompletedAsync = _ => { completed.TrySetResult(); return Task.CompletedTask; };
            await host.StartAsync(default);
            try { await completed.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
            finally { await host.StopAsync(default); }
            await restarted.TickOutboundAsync(); await restarted.TickOutboundAsync(); await restarted.TickOutboundAsync();
            restarted.Messaging.SentReplies.Select(r => r.Text).ShouldBe(new[] { "reserved tail", "undiscovered tail" });
            restarted.Messaging.SentReplies.ShouldAllBe(r => r.ConversationId == root.Channel.ExternalId);
            (await w.TailsAsync(root.Id)).Count.ShouldBe(2);
            (await w.MemberAsync(member)).ChannelOutboundDeliveryId.ShouldBe(root.Id);
            (await w.RootAsync(member)).PublicationAttempts.ShouldBe(1);
        }
    }

    [Test]
    public async Task C519_Next_prompt_keeps_an_already_reserved_tail()
    {
        foreach (var promptKind in new[] { TranscriptKinds.UserPrompt, TranscriptKinds.QueuedUserPrompt })
        {
            await using var w = await World.CreateAsync();
            var member = await w.MainAsync("old owning prompt", "old answer");
            await w.DispatchAsync(); await w.DrainAsync();
            var root = await w.RootAsync(member);
            await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "reserved before next prompt");
            await w.DispatchAsync();
            w.Clock.Advance(TimeSpan.FromSeconds(1));
            var final = await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "same batch old tail");
            await w.H.InsertTranscriptEntryAsync(promptKind, "new unrelated prompt");
            await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "new answer must not leak");
            await w.H.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd);
            await using var restarted = await w.RestartAsync();
            await restarted.Provider.GetRequiredService<ChannelOutboundDiscoveryService>().TickAsync(default);
            await restarted.TickOutboundAsync(); await restarted.TickOutboundAsync(); await restarted.TickOutboundAsync();
            restarted.Messaging.SentReplies.Select(r => r.Text)
                .ShouldBe(new[] { "reserved before next prompt", "same batch old tail" });
            root = await w.RootAsync(member);
            root.ReservedThroughSequence.ShouldBe(final);
            root.TailClosedAt.ShouldNotBeNull();
            await restarted.Provider.GetRequiredService<ChannelOutboundDiscoveryService>().TickAsync(default);
            await restarted.TickOutboundAsync();
            restarted.Messaging.SentReplies.Count.ShouldBe(2);
        }
    }

    [Test]
    public async Task C519_Suppressed_tail_advances_cursor_without_publication()
    {
        await using var w = await World.CreateAsync();
        var member = await w.MainAsync("silent root prompt", "NO_REPLY");
        await w.DispatchAsync();
        var root = await w.RootAsync(member);
        root.State.ShouldBe(ChannelOutboundDeliveryState.Suppressed);
        root.PublishedAt.ShouldBeNull();
        (await w.MemberAsync(member)).ChannelReplySettledAt.ShouldNotBeNull();
        var silentEnd = await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "NO_REPLY");
        await using var restarted = await w.RestartAsync();
        var discovery = restarted.Provider.GetRequiredService<ChannelOutboundDiscoveryService>();
        await discovery.TickAsync(default);
        var silent = (await w.TailsAsync(root.Id)).ShouldHaveSingleItem();
        silent.State.ShouldBe(ChannelOutboundDeliveryState.Suppressed);
        silent.LastTextSequence.ShouldBe(silentEnd);
        silent.PublishedAt.ShouldBeNull();
        silent.PreparationAttempts.ShouldBe(0);
        (await w.RootAsync(member)).ReservedThroughSequence.ShouldBe(silentEnd);
        var validEnd = await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "legitimate after silence");
        await discovery.TickAsync(default); await discovery.TickAsync(default);
        await restarted.TickOutboundAsync(); await restarted.TickOutboundAsync();
        restarted.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("legitimate after silence");
        var tails = await w.TailsAsync(root.Id);
        tails.Count.ShouldBe(2);
        tails[1].FirstTextSequence.ShouldBe(silentEnd + 1);
        tails[1].LastTextSequence.ShouldBe(validEnd);
        (await w.RootAsync(member)).PublishedAt.ShouldBeNull();
    }

    [Test]
    public async Task C519_Concurrent_reservation_has_one_winner()
    {
        await using var w = await World.CreateAsync();
        var member = await w.MainAsync("racing root prompt", "root answer");
        await w.DispatchAsync(); await w.DrainAsync();
        var root = await w.RootAsync(member);
        var firstEnd = await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "overlap fragment");
        var secondEnd = await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "suffix fragment");
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAdmitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var firstScope = w.H.Provider.CreateAsyncScope();
        await using var secondScope = w.H.Provider.CreateAsyncScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<ChannelOutboundService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<ChannelOutboundService>();
        firstService.ProbeBarrierAsync = async (name, _, ct) =>
        { if (name == "tail-root-read") { locked.TrySetResult(); await release.Task.WaitAsync(ct); } };
        secondService.ProbeBarrierAsync = (name, _, _) =>
        { if (name == "capture-admission") secondAdmitted.TrySetResult(); return Task.CompletedTask; };
        var route = ChannelReplyPreparation.Deserialize(root.CaptureJson!).Route;
        var first = firstService.CaptureAsync(route, new(w.H.SessionId, root.PromptSequence,
            root.ReservedThroughSequence!.Value + 1, firstEnd, "trailing", []),
            ChannelReplyPreparation.Describe("overlap fragment"), w.Bridge, default, root.Id);
        Task<ChannelOutboundDelivery>? second = null;
        try
        {
            await locked.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = secondService.CaptureAsync(route, new(w.H.SessionId, root.PromptSequence,
                firstEnd, secondEnd, "trailing", []),
                ChannelReplyPreparation.Describe("overlap fragment\n\nsuffix fragment"), w.Bridge, default, root.Id);
            await secondAdmitted.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally { release.TrySetResult(); await first; }
        try { await second!; }
        catch (ConflictException ex) { ex.Code.ShouldBe("channel_outbound_interval_owned"); }
        var tails = await w.TailsAsync(root.Id);
        for (var i = 1; i < tails.Count; i++) tails[i].FirstTextSequence.ShouldBeGreaterThan(tails[i - 1].LastTextSequence);
        w.Clock.Advance(TimeSpan.FromSeconds(1));
        await w.Discovery.TickAsync(default); await w.Discovery.TickAsync(default);
        await w.DrainAsync(); await w.DrainAsync();
        w.H.Messaging.SentReplies.Select(r => r.Text).ShouldBe(new[] { "root answer", "overlap fragment", "suffix fragment" });
    }

    [Test]
    public async Task C519_Fair_root_budget_reaches_tail_behind_idle_roots()
    {
        await using var w = await World.CreateAsync();
        var member = await w.MainAsync("fair target prompt", "target root");
        await w.DispatchAsync(); await w.DrainAsync();
        var target = await w.RootAsync(member);
        await using (var db = w.Db())
        {
            for (var i = 0; i < 321; i++) db.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
            {
                Id = Guid.NewGuid(), ChannelId = target.ChannelId, ProjectId = target.ProjectId,
                InboundAgentId = target.InboundAgentId, SourceSessionId = w.H.SessionId,
                PromptSequence = 1000 + i, FirstTextSequence = 1001 + i, LastTextSequence = 1001 + i,
                ReservedThroughSequence = 1001 + i, CaptureJson = target.CaptureJson,
                SendKind = "main", SourceKey = "idle-root-" + i,
                CreatedAt = target.CreatedAt.AddSeconds(-400 + i), DeadlineAt = target.DeadlineAt,
                State = ChannelOutboundDeliveryState.Suppressed,
            });
            await db.SaveChangesAsync();
        }
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "fair target tail");
        await w.Discovery.TickAsync(default);
        w.Discovery.RootsExaminedLastTick.ShouldBe(320);
        (await w.TailsAsync(target.Id)).ShouldBeEmpty();
        await w.Discovery.TickAsync(default);
        w.Discovery.RootsExaminedLastTick.ShouldBe(2);
        (await w.TailsAsync(target.Id)).ShouldHaveSingleItem();
        await w.DrainAsync();
        w.H.Messaging.SentReplies.Select(r => r.Text).ShouldBe(new[] { "target root", "fair target tail" });
    }

    [Test]
    public async Task C519_Tail_commit_failure_does_not_advance_root()
    {
        await using var w = await World.CreateAsync();
        var member = await w.MainAsync("atomic prompt", "atomic root");
        await w.DispatchAsync(); await w.DrainAsync();
        var root = await w.RootAsync(member);
        var end = await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "atomic tail");
        await using (var scope = w.H.Provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ChannelOutboundService>();
            service.ProbeBarrierAsync = (name, _, _) => name == "capture-before-commit"
                ? Task.FromException(new IOException("capture commit refused")) : Task.CompletedTask;
            await Should.ThrowAsync<IOException>(() => service.CaptureAsync(
                ChannelReplyPreparation.Deserialize(root.CaptureJson!).Route,
                new(w.H.SessionId, root.PromptSequence, root.ReservedThroughSequence!.Value + 1, end, "trailing", []),
                ChannelReplyPreparation.Describe("atomic tail"), w.Bridge, default, root.Id));
        }
        (await w.TailsAsync(root.Id)).ShouldBeEmpty();
        (await w.RootAsync(member)).ReservedThroughSequence.ShouldBe(root.ReservedThroughSequence);
        await w.Discovery.TickAsync(default); await w.DrainAsync();
        w.H.Messaging.SentReplies.Select(r => r.Text).ShouldBe(new[] { "atomic root", "atomic tail" });
    }

    [Test]
    public async Task C519_Machine_tail_policy_and_api_withholding_survive_restart()
    {
        await using var w = await World.CreateAsync();
        var path = Path.Combine(w.H.TempRoot, "workspace", "system.md");
        await File.WriteAllTextAsync(path, "original system bytes");
        var member = await w.MachineAsync($"system attachment\n[[attach: {path}]]", QueuedMessageOrigin.System);
        await w.DispatchAsync(); await w.DrainAsync();
        var root = await w.RootAsync(member);
        var held = await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "ineligible system plain tail");
        await using var restarted = await w.RestartAsync();
        var discovery = restarted.Provider.GetRequiredService<ChannelOutboundDiscoveryService>();
        await discovery.TickAsync(default);
        (await w.TailsAsync(root.Id)).ShouldHaveSingleItem().State.ShouldBe(ChannelOutboundDeliveryState.Suppressed);
        (await w.RootAsync(member)).ReservedThroughSequence.ShouldBe(held);
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, $"eligible attachment tail\n[[attach: {path}]]");
        await discovery.TickAsync(default); await discovery.TickAsync(default);
        await restarted.TickOutboundAsync(); await restarted.TickOutboundAsync();
        restarted.Messaging.SentReplies.ShouldHaveSingleItem().Attachments.ShouldHaveSingleItem().Content
            .ShouldBe("original system bytes"u8.ToArray());
        var beforeError = (await w.RootAsync(member)).ReservedThroughSequence;
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, $"withheld attachment\n[[attach: {path}]]");
        await w.H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "API failure", isApiError: true);
        await discovery.TickAsync(default); await discovery.TickAsync(default);
        await restarted.TickOutboundAsync();
        (await w.RootAsync(member)).ReservedThroughSequence.ShouldBe(beforeError);
        restarted.Messaging.SentReplies.Count.ShouldBe(1);
    }

    private sealed class World : IAsyncDisposable
    {
        public required BridgeQueueHarness H { get; init; }
        public required IsolatedTestSchema Isolated { get; init; }
        public required ControlledTimeProvider Clock { get; init; }
        public required string Key { get; init; }
        public ChannelBridgeSettings Bridge => new() { Enabled = true, MachineTurnTextOrigins = [QueuedMessageOrigin.Check] };
        public ChannelOutboundDiscoveryService Discovery => H.Provider.GetRequiredService<ChannelOutboundDiscoveryService>();
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(H.ConnectionString));
        private static void Configure(IServiceCollection services)
        {
            services.AddSingleton<IOptions<ChannelOutboundSettings>>(Options.Create(
                new ChannelOutboundSettings { UnifiedRecoveryEnabled = true }));
            services.AddSingleton<ChannelOutboundDiscoveryService>();
        }
        public static async Task<World> CreateAsync()
        {
            var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
            var clock = new ControlledTimeProvider(DateTimeOffset.UtcNow);
            var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            { ConnectionString = isolated.ConnectionString, TimeProvider = clock,
                Bridge = new ChannelBridgeSettings { Enabled = true, MachineTurnTextOrigins = [QueuedMessageOrigin.Check] },
                ConfigureServices = Configure });
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
            var projectId = Guid.NewGuid(); var boardId = Guid.NewGuid();
            db.Projects.Add(new Project { Id = projectId, Name = "tails", CreatedAt = h.Now, UpdatedAt = h.Now });
            db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "tails", CreatedAt = h.Now, UpdatedAt = h.Now });
            await db.SaveChangesAsync();
            await db.Agents.Where(a => a.Id == h.AgentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.BoardId, boardId));
            return new World { H = h, Isolated = isolated, Clock = clock, Key = "telegram:" + await h.BindChannelAsync() };
        }
        public Task<BridgeQueueHarness> RestartAsync() => BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        { ConnectionString = H.ConnectionString, TimeProvider = Clock, AttachSessionId = H.SessionId,
            AttachAgentId = H.AgentId, Bridge = Bridge, ConfigureServices = Configure });
        public async Task<Guid> MainAsync(string prompt, string answer)
        { var id = await H.SeedChannelCorrelationAsync(prompt, Key); await H.InsertTurnAsync(prompt, answer); return id; }
        public async Task<Guid> MachineAsync(string answer, QueuedMessageOrigin origin = QueuedMessageOrigin.Check)
        {
            var context = await H.SeedChannelCorrelationAsync("prior context", Key);
            await using (var db = Db()) await db.SessionQueuedMessages.Where(m => m.Id == context)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ChannelReplySettledAt, H.Now));
            var prompt = origin == QueuedMessageOrigin.Check ? "[Check] complete machine prompt" : "[System] complete machine prompt";
            var id = await H.SeedPendingMessageAsync(prompt, status: QueuedMessageStatus.Sent, origin: origin,
                deliveryAttempts: 1, baselineSequence: 0, createdAtUtc: H.Now, lastDeliveryStartedAt: H.Now, legacyNullGeneration: true);
            await H.InsertTurnAsync(prompt, answer); return id;
        }
        public Task<ChannelReplyDispatchResult> DispatchAsync() => H.Dispatcher.OnTurnEndAsync(H.SessionId, default);
        public async Task DrainAsync() { await H.TickOutboundAsync(); await H.TickOutboundAsync(); }
        public async Task<SessionQueuedMessage> MemberAsync(Guid id)
        { await using var db = Db(); return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id); }
        public async Task<ChannelOutboundDelivery> RootAsync(Guid member)
        { var row = await MemberAsync(member); row.ChannelOutboundDeliveryId.ShouldNotBeNull();
            await using var db = Db(); return await db.ChannelOutboundDeliveries.AsNoTracking().Include(d => d.Channel)
                .SingleAsync(d => d.Id == row.ChannelOutboundDeliveryId); }
        public async Task<List<ChannelOutboundDelivery>> TailsAsync(Guid root)
        { await using var db = Db(); return await db.ChannelOutboundDeliveries.AsNoTracking().Where(d => d.RootDeliveryId == root)
                .OrderBy(d => d.FirstTextSequence).ToListAsync(); }
        public async ValueTask DisposeAsync() { await H.DisposeAsync(); await Isolated.DisposeAsync(); }
    }
}
