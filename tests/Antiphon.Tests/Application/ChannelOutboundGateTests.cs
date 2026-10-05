using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Tests.TestHelpers;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class ChannelOutboundDeliveryTests
{
    [Test]
    [Arguments("NO_REPLY")]
    [Arguments("operator")]
    [Arguments("api-error")]
    public async Task Gates_precede_admission_and_a_matched_companion_still_converts(string gate)
    {
        await using var world = await OutboundGateWorld.CreateAsync();
        var prompt = "[Telegram] answer this " + Guid.NewGuid().ToString("N");
        var correlation = await world.Harness.SeedChannelCorrelationAsync(prompt, world.ConversationKey);
        if (gate == "operator")
            await world.Harness.InsertTurnAsync("operator terminal command", "operator answer");
        else if (gate == "api-error")
        {
            await world.Harness.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt);
            await world.Harness.InsertApiErrorStubAsync(errorText: "API Error: 529 Overloaded",
                apiErrorClass: "server_error", apiErrorStatus: 529);
        }
        else
            await world.Harness.InsertTurnAsync(prompt, "NO_REPLY");
        await world.Harness.Dispatcher.OnTurnEndAsync(world.Harness.SessionId, CancellationToken.None);

        await world.AssertNoConversionAsync(suppressed: gate == "NO_REPLY");
        world.Harness.Messaging.SentReplies.ShouldBeEmpty();
        await using (var db = world.Db())
        {
            var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlation);
            if (gate == "NO_REPLY")
            {
                row.ChannelOutboundDeliveryId.ShouldNotBeNull();
                row.ChannelReplySettledAt.ShouldNotBeNull();
            }
            else
            {
                row.ChannelOutboundDeliveryId.ShouldBeNull();
                row.ChannelReplySettledAt.ShouldBeNull();
            }
        }

        if (gate == "NO_REPLY")
        {
            prompt = "[Telegram] a fresh answer " + Guid.NewGuid().ToString("N");
            correlation = await world.Harness.SeedChannelCorrelationAsync(prompt, world.ConversationKey);
        }
        await world.Harness.InsertTurnAsync(prompt, "A healthy matched answer.");
        await world.Harness.Dispatcher.OnTurnEndAsync(world.Harness.SessionId, CancellationToken.None);
        await world.AssertOneConversionAsync(correlation);
    }

    [Test]
    public async Task Disallowed_plain_text_machine_turn_stays_out_of_admission()
    {
        await using var world = await OutboundGateWorld.CreateAsync();
        var prior = await world.Harness.SeedChannelCorrelationAsync(
            "[Telegram] earlier accepted request", world.ConversationKey);
        var note = "[system] source.md maintenance note";
        var machineId = Guid.NewGuid();
        await using (var db = world.Db())
        {
            await db.SessionQueuedMessages.Where(m => m.Id == prior)
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.ChannelReplySettledAt, DateTime.UtcNow));
            var sequence = (await db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == world.Harness.SessionId)
                .MaxAsync(m => (long?)m.Sequence) ?? 0) + 1;
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = machineId, AgentSessionId = world.Harness.SessionId,
                Body = note, Sequence = sequence, Origin = QueuedMessageOrigin.System,
                Status = QueuedMessageStatus.Sent, ConversationKey = world.ConversationKey,
                CreatedAt = DateTime.UtcNow, SentAt = DateTime.UtcNow,
                DeliveryAttempts = 1, LastDeliveryStartedAt = DateTime.UtcNow,
                LastDeliveryBaselineSequence = await world.Harness.CurrentTranscriptMaxSequenceAsync(),
            });
            await db.SaveChangesAsync();
        }
        await world.Harness.InsertTurnAsync(note, "Plain system text with source.md in prose.");
        await world.Harness.Dispatcher.OnTurnEndAsync(world.Harness.SessionId, CancellationToken.None);
        await world.AssertNoConversionAsync();
        world.Harness.Messaging.SentReplies.ShouldBeEmpty();
        await using (var db = world.Db())
        {
            var machine = await db.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(m => m.Id == machineId);
            machine.ChannelReplySettledAt.ShouldBeNull();
            machine.ChannelOutboundDeliveryId.ShouldBeNull();
        }

        var prompt = "[Telegram] healthy after machine " + Guid.NewGuid().ToString("N");
        var correlation = await world.Harness.SeedChannelCorrelationAsync(prompt, world.ConversationKey);
        await world.Harness.InsertTurnAsync(prompt, "Healthy matched answer.");
        await world.Harness.Dispatcher.OnTurnEndAsync(world.Harness.SessionId, CancellationToken.None);
        await world.AssertOneConversionAsync(correlation);
    }

    [Test]
    [Arguments("proactive")]
    [Arguments("digest")]
    [Arguments("incident")]
    [Arguments("alert")]
    public async Task Real_control_callers_bypass_the_selected_profile(string caller)
    {
        await using var world = await OutboundGateWorld.CreateAsync();
        await using (var db = world.Db())
        {
            var outbound = world.Outbound(db);
            var channels = new ChatChannelService(db, TimeProvider.System,
                world.Harness.Messaging, world.Settings, outbound);
            if (caller == "proactive")
                await channels.SendAsync(world.ChannelId, "Proactive source.md notice", CancellationToken.None);
            else if (caller == "digest")
            {
                var now = DateTime.UtcNow;
                var id = Guid.NewGuid();
                db.AgentTasks.Add(new AgentTask
                {
                    Id = id, RootTaskId = id, Title = "Digest source.md", Goal = "digest window",
                    Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code,
                    ModelLevel = AgentModelLevel.High, Workspace = WorkspaceMode.Shared,
                    WorkingDirectory = world.Root, Status = AgentTaskStatus.Succeeded,
                    ReplyTo = AgentTaskReplyTo.None, Result = "Done.",
                    CreatedAt = now.AddHours(-2), DispatchedAt = now.AddHours(-2),
                    CompletedAt = now.AddMinutes(-10),
                });
                await db.SaveChangesAsync();
                var attention = new AttentionService(db, new RefusingSessionRunnerClient(),
                    Options.Create(new SupervisionSettings()), Options.Create(new DelegationSettings()),
                    TimeProvider.System, NullLogger<AttentionService>.Instance);
                var projection = new AwayDigestProjection(db, attention,
                    new SubscriptionUsageReader(db, TimeProvider.System),
                    Options.Create(new DelegationSettings()));
                var notifier = new AwayDigestNotifier(db, projection, channels,
                    Options.Create(new DigestSettings { TimeZone = "Europe/London" }),
                    TimeProvider.System, NullLogger<AwayDigestNotifier>.Instance);
                (await notifier.SendDueAsync(world.ChannelId, null, force: true,
                    CancellationToken.None)).ShouldHaveSingleItem().Sent.ShouldBeTrue();
            }
            else if (caller == "incident")
            {
                var incident = new AgentIncident
                {
                    Id = Guid.NewGuid(), AgentId = world.Harness.AgentId,
                    Kind = AgentIncidentKind.ChannelReplyLost, Severity = AlertSeverity.Critical,
                    Message = "Incident source.md", CreatedAt = DateTime.UtcNow,
                };
                db.AgentIncidents.Add(incident);
                await db.SaveChangesAsync();
                var notifier = new IncidentPageNotifier(db, channels,
                    Options.Create(new DigestSettings
                    {
                        WakeOnIncidentKinds = [AgentIncidentKind.ChannelReplyLost],
                        TimeZone = "Europe/London", PublicBaseUrl = "https://antiphon.example",
                    }), TimeProvider.System, NullLogger<IncidentPageNotifier>.Instance);
                await notifier.SweepAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                (await db.AgentIncidents.SingleAsync(i => i.Id == incident.Id))
                    .HumanNotifiedAt.ShouldNotBeNull();
            }
            else
            {
                var alert = new Alert
                {
                    Id = Guid.NewGuid(), Severity = AlertSeverity.Warning,
                    Source = "supervisor", Title = "Alert source.md", Detail = "Control notice",
                    DedupKey = Guid.NewGuid().ToString("N"), CreatedAt = DateTime.UtcNow,
                };
                db.Alerts.Add(alert);
                await db.SaveChangesAsync();
                var throttle = new AlertThrottle();
                var settings = Options.Create(new AlertsSettings { MinMinutesBetweenSends = 5 });
                await new ChannelAlertRouter(db, throttle, settings,
                    NullLogger<ChannelAlertRouter>.Instance).RouteAsync(alert.Id, CancellationToken.None);
                (await new AlertDigestFlusher(db, throttle, world.Harness.Messaging, settings,
                    TimeProvider.System, NullLogger<AlertDigestFlusher>.Instance, outbound)
                    .FlushDueAsync(CancellationToken.None)).ShouldBe(1);
                db.ChangeTracker.Clear();
                (await db.Alerts.SingleAsync(a => a.Id == alert.Id)).RoutedAt.ShouldNotBeNull();
            }
        }

        var sent = world.Harness.Messaging.SentReplies.ShouldHaveSingleItem();
        sent.ConversationId.ShouldBe(world.Conversation);
        sent.Text.ShouldNotBeNullOrWhiteSpace();
        await world.AssertNoConversionAsync();
        await using (var db = world.Db())
        {
            var channel = await db.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == world.ChannelId);
            if (caller == "alert") channel.LastReplyAt.ShouldBeNull();
            else channel.LastReplyAt.ShouldNotBeNull();
        }

        await using (var db = world.Db())
        {
            var control = new ChannelReply
            {
                Channel = "telegram", ConversationId = world.Conversation,
                Text = "Conversion failure notice",
                Attachments = [new OutboundAttachment
                {
                    Kind = AttachmentKind.File, Name = "original.md",
                    Mime = "text/markdown", Content = "# Original source\n"u8.ToArray(),
                }],
            };
            (await world.Outbound(db).SendAsync(control, ChannelOutboundOrigin.Control,
                source: null, CancellationToken.None)).ShouldBe(ChannelOutboundSendOutcome.Published);
            var direct = world.Harness.Messaging.SentReplies.Last();
            direct.Attachments.ShouldHaveSingleItem().Content.ShouldBe("# Original source\n"u8.ToArray());
            direct.Text.ShouldBe("Conversion failure notice");
        }
        await world.AssertNoConversionAsync();

        var prompt = "[Telegram] healthy control companion " + Guid.NewGuid().ToString("N");
        var correlation = await world.Harness.SeedChannelCorrelationAsync(prompt, world.ConversationKey);
        await world.Harness.InsertTurnAsync(prompt, "Healthy companion after " + caller);
        await world.Harness.Dispatcher.OnTurnEndAsync(world.Harness.SessionId, CancellationToken.None);
        await world.AssertOneConversionAsync(correlation, priorControlSends: 2);
    }

    private sealed class OutboundGateWorld : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly ChannelOutboundFileStore _store;
        private readonly string _root;
        private OutboundGateWorld(IsolatedTestSchema schema, BridgeQueueHarness harness,
            IOptions<ChannelOutboundSettings> settings, ChannelOutboundFileStore store,
            Guid channelId, string conversation, string root)
        {
            _schema = schema; Harness = harness; Settings = settings; _store = store;
            ChannelId = channelId; Conversation = conversation; _root = root;
        }

        public BridgeQueueHarness Harness { get; }
        public IOptions<ChannelOutboundSettings> Settings { get; }
        public Guid ChannelId { get; }
        public string Conversation { get; }
        public string ConversationKey => "telegram:" + Conversation;
        public string Root => _root;

        public static async Task<OutboundGateWorld> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var projectId = Guid.NewGuid();
            var boardId = Guid.NewGuid();
            var converterId = Guid.NewGuid();
            var root = Path.Combine(Path.GetTempPath(), "c0418-gate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var store = new ChannelOutboundFileStore(Path.Combine(root, "outbound"));
            var settings = Options.Create(new ChannelOutboundSettings
            {
                UnifiedRecoveryEnabled = true,
                Profiles = new Dictionary<string, ChannelOutboundProfile>
                {
                    ["conversion"] = new()
                    {
                        ProjectId = projectId, AgentId = converterId,
                        PromptFile = "convert.md", Trigger = ChannelOutboundTrigger.EveryAgentReply,
                    },
                },
            });
            var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                ConnectionString = schema.ConnectionString, AlwaysOn = true,
                PreserveDatabaseOnDispose = true,
                Bridge = new ChannelBridgeSettings { Enabled = true, DebounceWindowMs = 0 },
                ConfigureServices = services =>
                {
                    services.AddSingleton<IChannelOutboundFileStore>(store);
                    services.AddSingleton<IOptions<ChannelOutboundSettings>>(settings);
                    services.AddScoped<ChannelOutboundService>();
                },
            });
            await File.WriteAllTextAsync(Path.Combine(root, "convert.md"), "Convert the reply.");
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var now = DateTime.UtcNow;
                db.Projects.Add(new Project { Id = projectId, Name = "gate-" + projectId.ToString("N"),
                    CreatedAt = now, UpdatedAt = now });
                db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "gate",
                    CreatedAt = now, UpdatedAt = now });
                db.Agents.Add(new Agent { Id = converterId, BoardId = boardId, Name = "converter",
                    Slug = "converter-" + converterId.ToString("N"), WorkingDirectory = root });
                await db.SaveChangesAsync();
                await db.Agents.Where(a => a.Id == h.AgentId)
                    .ExecuteUpdateAsync(u => u.SetProperty(a => a.BoardId, boardId));
            }
            var conversation = await h.BindChannelAsync();
            Guid channelId;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var channel = await db.ChatChannels.SingleAsync(c => c.ExternalId == conversation);
                channelId = channel.Id;
                channel.OutboundAgentProfile = "conversion";
                channel.DigestEnabled = true;
                channel.AlertMinSeverity = AlertSeverity.Warning;
                await db.SaveChangesAsync();
            }
            return new OutboundGateWorld(schema, h, settings, store, channelId, conversation, root);
        }

        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
        public ChannelOutboundService Outbound(AppDbContext db) =>
            new(db, _store, Harness.Messaging, Settings, TimeProvider.System);

        public async Task AssertNoConversionAsync(bool suppressed = false)
        {
            await using var db = Db();
            var deliveries = await db.ChannelOutboundDeliveries.Where(d => d.ChannelId == ChannelId).ToListAsync();
            deliveries.Count.ShouldBe(suppressed ? 1 : 0);
            if (suppressed)
            {
                deliveries[0].State.ShouldBe(ChannelOutboundDeliveryState.Suppressed);
                deliveries[0].PublishedAt.ShouldBeNull();
            }
            (await db.AgentTasks.CountAsync(t => t.OutboundDeliveryId != null)).ShouldBe(0);
        }

        public async Task AssertOneConversionAsync(Guid correlation, int priorControlSends = 0)
        {
            await using var db = Db();
            var intent = (await db.ChannelOutboundDeliveries.AsNoTracking()
                .Where(d => d.ChannelId == ChannelId && d.State != ChannelOutboundDeliveryState.Suppressed).ToListAsync()).ShouldHaveSingleItem();
            intent.State.ShouldBe(ChannelOutboundDeliveryState.Captured);
            (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlation))
                .ChannelReplySettledAt.ShouldBeNull();
            intent.Trigger.ShouldBe(nameof(ChannelOutboundTrigger.EveryAgentReply));
            (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlation))
                .ChannelOutboundDeliveryId.ShouldBe(intent.Id);
            Harness.Messaging.SentReplies.Count.ShouldBe(priorControlSends);
            var tasks = new AgentTaskService(db,
                new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                Options.Create(new DelegationSettings { AllowedRoots = [Root] }),
                new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System,
                NullLogger<AgentTaskService>.Instance);
            var pump = new ChannelOutboundDeliveryPump(db,
                new OutboundConversionTaskRunner(db, tasks), _store, Harness.Messaging,
                Options.Create(new AntiphonMessagingOptions()), TimeProvider.System,
                NullLogger<ChannelOutboundDeliveryPump>.Instance, Settings,
                new ChannelReplyPreparation(new ChannelReplyAttachmentReader()));
            (await pump.TickAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
            db.ChangeTracker.Clear();
            (await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == intent.Id))
                .State.ShouldBe(ChannelOutboundDeliveryState.Pending);
            (await pump.TickAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
            db.ChangeTracker.Clear();
            (await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == intent.Id))
                .State.ShouldBe(ChannelOutboundDeliveryState.Converting);
            (await db.AgentTasks.CountAsync(t => t.OutboundDeliveryId == intent.Id)).ShouldBe(1);
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            await _schema.DisposeAsync();
            Directory.Delete(_root, recursive: true);
            Directory.Delete(Harness.TempRoot, recursive: true);
        }
    }
}
