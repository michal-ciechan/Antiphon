using System.Text.Json;
using System.Data.Common;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Antiphon.Tests.Agents;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.Redpanda;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration"), Category("Slow")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ChannelOutboundUnifiedTransportTests
{
    [Test, Arguments("main"), Arguments("trailing"), Arguments("machine")]
    public async Task C519_Queue_to_adapter(string kind)
    {
        await using var broker = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:v25.3.4").Build();
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await broker.StartAsync(startup.Token);
        foreach (var busy in new[] { false, true })
        foreach (var lateGateway in new[] { false, true })
        foreach (var cut in new[] { "queue-insert", "queue-committed", "confirm-save", "capture", "definite-refusal", "refusal-save" })
        {
            using var scenario = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var fault = new HandoffFault(cut);
            var producer = new RefusalProducer();
            await using var w = await UnifiedOutboundTransport.CreateAsync(broker, !lateGateway,
                services =>
                {
                    services.AddSingleton<IAntiphonMessagingProducer>(producer);
                    services.AddScoped<ChannelOutboundService>(sp =>
                    {
                        var service = ActivatorUtilities.CreateInstance<ChannelOutboundService>(sp);
                        service.ProbeBarrierAsync = (point, _, _) =>
                        {
                            if (cut == "capture" && point == "capture-before-commit" && fault.Armed)
                            { fault.Fire(); throw new IOException("Injected capture commit refusal"); }
                            return Task.CompletedTask;
                        };
                        return service;
                    });
                }, options => options.AddInterceptors(fault, new CommittedQueueFault(fault)));
            producer.Inner = w.Producer;
            await ReceiveSourceAsync(w, kind, busy, fault, scenario.Token);
            if (cut == "capture") fault.Armed = true;
            await w.H.Dispatcher.OnTurnEndAsync(w.H.SessionId, scenario.Token);
            if (cut == "capture")
            {
                fault.Fired.ShouldBe(1);
                (await w.DeliveryAsync(kind)).ShouldBeNull();
                await w.H.Provider.GetRequiredService<ChannelOutboundDiscoveryService>().TickAsync(scenario.Token);
            }
            (await w.DeliveryAsync(kind))!.State.ShouldBe(ChannelOutboundDeliveryState.Captured);
            if (cut is "definite-refusal" or "refusal-save")
            { producer.RefuseNext = true; fault.Armed = cut == "refusal-save"; }
            await w.H.TickOutboundAsync(); await w.H.TickOutboundAsync();
            if (cut is "definite-refusal" or "refusal-save")
            {
                producer.Refusals.ShouldBe(1);
                var row = (await w.DeliveryAsync(kind))!;
                row.PublicationAttempts.ShouldBe(1);
                row.State.ShouldBe(cut == "refusal-save" ? ChannelOutboundDeliveryState.Publishing : ChannelOutboundDeliveryState.Ready);
                await w.RecoverAsync();
                if (cut == "refusal-save")
                {
                    fault.Fired.ShouldBe(1);
                    (await w.DeliveryAsync(kind))!.State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
                    await using var scope = w.H.Provider.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ChannelOutboundService>().RetryUncertainAsync(row.Id, true, scenario.Token);
                    await w.RecoverAsync();
                }
            }
            if (lateGateway) await w.StartGatewayAsync();
            await w.AssertReceiptAsync();
            var published = (await w.DeliveryAsync(kind))!;
            published.State.ShouldBe(ChannelOutboundDeliveryState.Published);
            published.SourceSessionId.ShouldBe(w.H.SessionId);
            published.RootDeliveryId.ShouldBe(w.RootId);
            if (cut is "queue-insert" or "queue-committed" or "confirm-save") fault.Fired.ShouldBe(1);
            await w.RecoverAsync();
            await w.AssertReceiptAsync();
            scenario.Token.ThrowIfCancellationRequested();
        }
    }

    private static async Task ReceiveSourceAsync(UnifiedOutboundTransport w, string kind, bool busy,
        HandoffFault fault, CancellationToken ct)
    {
        var h = w.H;
        var bridge = new ChannelBridgeService(h.Messaging, h.Queue,
            h.Provider.GetRequiredService<ChannelInboundDebouncer>(), h.EventBus,
            h.Provider.GetRequiredService<IServiceScopeFactory>(), h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(),
            h.Clock, NullLogger<ChannelBridgeService>.Instance, h.Provider.GetRequiredService<ChannelInboundWakeSignal>());
        var body = "whole source HEAD " + Guid.NewGuid().ToString("N") + "\nunique MIDDLE\ncomplete TAIL";
        if (busy) await h.MarkWorkingAsync();
        fault.Armed = fault.Cut is "queue-insert" or "queue-committed" or "confirm-save";
        var inbound = new ChannelMessage
        {
            Id = Guid.NewGuid().ToString("N"), Channel = "slack", ChannelMessageId = Guid.NewGuid().ToString("N"),
            Conversation = new Conversation { Id = w.Conversation, Kind = ConversationKind.Group },
            Author = new Participant { Id = "source", DisplayName = "source" }, Timestamp = DateTimeOffset.UtcNow,
            Text = body, ReplyHandle = w.Conversation + "|" + w.Thread,
            Raw = JsonSerializer.SerializeToElement(new { fixture = "C519", complete = body }),
        };
        try { await bridge.HandleInboundAsync(inbound, ct); }
        catch (IOException) when (fault.Fired == 1) { }
        await using (var db = w.Db())
        {
            var retained = await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == inbound.ChannelMessageId);
            retained.EnvelopeJson.ShouldContain("complete TAIL");
            if (fault.Cut == "queue-insert")
            {
                retained.QueueMessageId.ShouldBeNull();
                (await db.SessionQueuedMessages.CountAsync()).ShouldBe(0);
            }
        }
        await bridge.DrainPendingAsync(ct);
        if (busy)
        {
            h.Adapter.SubmittedBodies.ShouldBeEmpty();
            await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        }
        try { await h.Queue.FlushSessionAsync(h.SessionId, ct); }
        catch (IOException) when (fault.Fired == 1) { }
        await h.Queue.FlushSessionAsync(h.SessionId, ct);
        await using (var db = w.Db())
        {
            var source = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Origin == QueuedMessageOrigin.Channel);
            source.Status.ShouldBe(QueuedMessageStatus.Sent);
            source.DeliveryAttempts.ShouldBe(1);
            var receipt = (await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == h.SessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == source.Body).ToListAsync()).ShouldHaveSingleItem();
            receipt.Sequence.ShouldBeGreaterThan(source.LastDeliveryBaselineSequence ?? 0);
            source.LastDeliveryGeneration.ShouldNotBeNull();
            source.Body.ShouldContain("unique MIDDLE"); source.Body.ShouldContain("complete TAIL");
            w.MemberId = source.Id;
        }
        if (kind == "machine")
        {
            await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "NO_REPLY");
            await h.Dispatcher.OnTurnEndAsync(h.SessionId, ct);
            await h.Queue.EnqueueAsync(h.SessionId, "[Check] machine complete HEAD\nMIDDLE\nTAIL", MessageSendMode.WhenIdle,
                ct, origin: QueuedMessageOrigin.Check);
            await using var db = w.Db();
            var source = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Origin == QueuedMessageOrigin.Check);
            source.Status.ShouldBe(QueuedMessageStatus.Sent);
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == source.Body)).ShouldBe(1);
            w.MemberId = source.Id;
        }
        else if (kind == "trailing")
        {
            await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "initial reply");
            await h.Dispatcher.OnTurnEndAsync(h.SessionId, ct); await h.DrainOutboundAsync();
            await using var db = w.Db();
            w.RootId = (await db.ChannelOutboundDeliveries.SingleAsync()).Id;
            w.BaselineReceipts = 1;
        }
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, w.Answer + "\n[[attach: " + w.SourcePath + "]]");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
    }

    [Test]
    public async Task C519_Size_refusal()
    {
        await using var broker = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:v25.3.4").Build();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await broker.StartAsync(deadline.Token);
        await using var w = await UnifiedOutboundTransport.CreateAsync(broker, maxMessageBytes: 4096);
        await w.SeedCrashSourceAsync("main");
        // Incompressible bytes cross the client cap and reach the broker's smaller topic cap.
        var bytes = new byte[32 * 1024]; Random.Shared.NextBytes(bytes);
        await File.WriteAllBytesAsync(w.SourcePath, bytes);
        await w.H.Dispatcher.OnTurnEndAsync(w.H.SessionId, deadline.Token);
        await w.H.DrainOutboundAsync();
        var failed = (await w.DeliveryAsync("main"))!;
        failed.State.ShouldBe(ChannelOutboundDeliveryState.Failed);
        failed.FailureReason.ShouldContain("Broker size limit definitely refused");
        failed.PublicationAttempts.ShouldBe(1); failed.PublishedAt.ShouldBeNull();
        await using (var db = w.Db())
        {
            (await db.AgentIncidents.CountAsync(i => i.Kind == AgentIncidentKind.ChannelReplyLost)).ShouldBe(1);
            (await db.Alerts.CountAsync()).ShouldBe(1);
        }
        await w.RecoverAsync();
        (await w.DeliveryAsync("main"))!.PublicationAttempts.ShouldBe(1);
        w.Slack.SentMessages.ShouldBeEmpty();
        // A distinct valid obligation on the same topic proves consumer eligibility and lane release.
        await File.WriteAllBytesAsync(w.SourcePath, w.OriginalBytes);
        await w.H.SeedChannelCorrelationAsync("valid companion prompt", w.Key);
        await w.H.InsertTurnAsync("valid companion prompt", w.Answer + "\n[[attach: " + w.SourcePath + "]]");
        await w.H.Dispatcher.OnTurnEndAsync(w.H.SessionId, deadline.Token); await w.H.DrainOutboundAsync();
        await w.AssertReceiptAsync();
    }

    private sealed class HandoffFault(string cut) : SaveChangesInterceptor
    {
        public string Cut => cut;
        public bool Armed { get; set; }
        public int Fired { get; private set; }
        public void Fire() { Armed = false; Fired++; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed) return ValueTask.FromResult(result);
            var queue = eventData.Context!.ChangeTracker.Entries<SessionQueuedMessage>().ToList();
            if (cut == "queue-insert" && queue.Any(e => e.State == EntityState.Added)
                || cut == "confirm-save" && queue.Any(e => e.State == EntityState.Modified && e.Entity.Status == QueuedMessageStatus.Sent)
                || cut == "refusal-save" && eventData.Context.ChangeTracker.Entries<ChannelOutboundDelivery>().Any(e =>
                    e.State == EntityState.Modified && e.Entity.State == ChannelOutboundDeliveryState.Ready && e.Entity.PublicationAttempts == 1))
            { Fire(); throw new IOException("Injected " + cut); }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommittedQueueFault(HandoffFault fault) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (fault.Armed && fault.Cut == "queue-committed" && eventData.Context!.ChangeTracker.Entries<SessionQueuedMessage>().Any())
            { fault.Fire(); throw new IOException("Injected committed queue handoff"); }
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task C519_Converter_handoff()
    {
        await using var broker = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:v25.3.4").Build();
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await broker.StartAsync(startup.Token);
        foreach (var busy in new[] { false, true })
        foreach (var cut in new[] { "conversion-task-committed", "conversion-dispatched", "enqueue-refused", "result-committed" })
        {
            using var scenario = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var worker = new FakeAgentProtocolAdapter();
            var delegation = new DelegationSettings { MaxConcurrentTasks = 16 };
            await using var w = await UnifiedOutboundTransport.CreateAsync(broker, configure: services =>
            {
                services.AddSingleton(Options.Create(delegation));
                services.AddSingleton<IAgentProtocolAdapterFactory>(new WorkerFactory(worker));
                services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                services.AddSingleton<DelegationWorkspaceResolver>();
                services.AddScoped<DelegationWorktreeService>();
                services.AddScoped<AgentTaskService>();
                services.AddScoped<AgentTaskDispatcher>();
                services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                    new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
                    {
                        DefaultDefinition = "claude",
                        Definitions = { ["claude"] = new AgentDefinition
                            { Kind = "ClaudeCode", Exe = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh" } },
                    }));
            });
            delegation.AllowedRoots = [w.H.TempRoot];
            var workerRoot = Directory.CreateDirectory(Path.Combine(w.H.TempRoot, "converter")).FullName;
            await File.WriteAllTextAsync(Path.Combine(workerRoot, "convert.md"), "Preserve every source; emit the exact converted receipt.");
            w.ConverterId = Guid.NewGuid();
            await using (var db = w.Db())
            {
                var agent = await db.Agents.Include(a => a.Board).SingleAsync(a => a.Id == w.H.AgentId);
                w.ProjectId = agent.Board!.ProjectId;
                db.Agents.Add(new Agent { Id = w.ConverterId, BoardId = agent.BoardId, Name = "converter",
                    Slug = "converter-" + w.ConverterId.ToString("N"), Kind = AgentKind.ClaudeCode,
                    WorkingDirectory = workerRoot, AlwaysOn = false });
                await db.SaveChangesAsync();
                await db.ChatChannels.Where(c => c.Id == w.ChannelId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.OutboundAgentProfile, "crash-pdf"));
            }
            w.H.Provider.GetRequiredService<IOptions<ChannelOutboundSettings>>().Value.Profiles["crash-pdf"] = new()
            {
                ProjectId = w.ProjectId, AgentId = w.ConverterId, PromptFile = "convert.md",
                Trigger = ChannelOutboundTrigger.EveryAgentReply, TimeoutSeconds = 900,
            };
            worker.RegisterOnStart = w.H.Runtime;
            worker.OnSubmitted = async submitted =>
            {
                var session = worker.StartedSessionId!.Value;
                await BridgeQueueHarness.InsertEntryAsync(session, TranscriptKinds.UserPrompt, submitted,
                    timestamp: w.H.Now, connectionString: w.Schema.ConnectionString, createdAtUtc: w.H.Now);
                await BridgeQueueHarness.InsertEntryAsync(session, TranscriptKinds.TurnEnd, stopReason: "end_turn",
                    connectionString: w.Schema.ConnectionString, createdAtUtc: w.H.Now);
            };
            await ReceiveSourceAsync(w, "main", busy, new HandoffFault("none"), scenario.Token);
            await w.H.Dispatcher.OnTurnEndAsync(w.H.SessionId, scenario.Token);
            await w.H.TickOutboundAsync();
            var delivery = (await w.DeliveryAsync("main"))!;
            delivery.State.ShouldBe(ChannelOutboundDeliveryState.Pending);
            await using (var child = await w.StartProbeAsync("conversion-task-committed", deliveryId: delivery.Id))
            {
                await child.ReachAsync("conversion-task-committed");
                await child.KillAsync();
            }
            Guid taskId;
            await using (var db = w.Db())
            {
                var task = await db.AgentTasks.SingleAsync(t => t.OutboundDeliveryId == delivery.Id);
                taskId = task.Id; task.Status.ShouldBe(AgentTaskStatus.Queued);
            }
            if (cut is "conversion-dispatched" or "enqueue-refused")
            {
                await using var child = await w.StartProbeAsync(cut == "conversion-dispatched" ? cut : null,
                    mode: "dispatch", deliveryId: delivery.Id);
                if (cut == "conversion-dispatched") { await child.ReachAsync(cut); await child.KillAsync(); }
                else { await child.CompleteAsync(); await child.AssertMarkerAsync("enqueue-refused"); }
                await using var db = w.Db();
                var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                task.Status.ShouldBe(AgentTaskStatus.Dispatched); task.AgentSessionId.ShouldNotBeNull();
                // This is the real interrupted-launch recovery entry used by reconciliation.
                w.H.Provider.GetRequiredService<AgentSessionLaunchQueue>().ResumeInterrupted(task.AgentSessionId.Value, w.ConverterId);
            }
            else
            {
                await using var scope = w.H.Provider.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(scenario.Token);
            }
            await w.H.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(30), scenario.Token);
            Guid workerSession;
            string goal;
            await using (var db = w.Db())
            {
                var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                task.AgentSessionId.ShouldNotBeNull(); workerSession = task.AgentSessionId.Value; goal = task.Goal;
                (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == workerSession)).Status.ShouldBe(SessionStatus.Running);
            }
            await w.H.Queue.FlushSessionAsync(workerSession, scenario.Token);
            await using (var db = w.Db())
            {
                var brief = (await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == workerSession
                    && m.Origin == QueuedMessageOrigin.Delegation && m.SourceTaskId == taskId).ToListAsync()).ShouldHaveSingleItem();
                brief.Status.ShouldBe(QueuedMessageStatus.Sent);
                brief.Body.ShouldContain(goal);
                brief.Body.ShouldContain(delivery.Id.ToString("D"));
                brief.Body.ShouldContain("request.json");
                (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == workerSession
                    && t.Kind == TranscriptKinds.UserPrompt && t.Text == brief.Body
                    && t.Sequence > brief.LastDeliveryBaselineSequence)).ShouldBe(1);
                (await db.AgentTasks.CountAsync(t => t.OutboundDeliveryId == delivery.Id)).ShouldBe(1);
            }
            var prepared = (await w.DeliveryAsync("main"))!;
            var output = Path.Combine(Path.GetDirectoryName(prepared.InputPath)!, "output");
            var convertedText = w.Answer + " converted by the linked worker";
            await File.WriteAllTextAsync(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
                { version = 1, deliveryId = delivery.Id, disposition = "converted", replacementText = convertedText, files = Array.Empty<object>() }));
            await AgentTaskReplyIntegrationTests.SettleExistingConversionTaskAsync(w.Schema.ConnectionString, taskId, workerSession);
            if (cut == "result-committed")
            {
                await using var child = await w.StartProbeAsync("before-conversion-claim", offset: 600, deliveryId: delivery.Id);
                await child.ReachAsync("before-conversion-claim");
                await using var db = w.Db();
                (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId)).Status.ShouldBe(AgentTaskStatus.Succeeded);
                (await w.DeliveryAsync("main"))!.State.ShouldBe(ChannelOutboundDeliveryState.Converting);
                await child.KillAsync();
            }
            await w.RecoverAsync();
            await w.AssertReceiptAsync(expectedText: convertedText);
            var published = (await w.DeliveryAsync("main"))!;
            published.ConversionTaskId.ShouldBe(taskId);
            published.ConversionOutcome.ShouldBe("Converted");
            published.State.ShouldBe(ChannelOutboundDeliveryState.Published);
            published.PublicationAttempts.ShouldBe(1);
            await w.RecoverAsync(); await w.AssertReceiptAsync(expectedText: convertedText);
        }
    }

    private sealed class WorkerFactory(FakeAgentProtocolAdapter worker) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => worker;
    }

    private sealed class RefusalProducer : IAntiphonMessagingProducer
    {
        public IAntiphonMessagingProducer Inner { get; set; } = null!;
        public bool RefuseNext { get; set; }
        public int Refusals { get; private set; }
        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            if (RefuseNext)
            {
                RefuseNext = false; Refusals++;
                throw new ProduceException<string, string>(new Error(ErrorCode.Local_QueueFull), new DeliveryResult<string, string>());
            }
            return Inner.SendAsync(reply, cancellationToken);
        }
    }
}
