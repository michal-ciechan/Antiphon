using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
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
public sealed class ChannelOutboundDispatchIntegrationTests
{
    private static async Task BindProjectAsync(BridgeQueueHarness h)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        db.Projects.Add(new Project { Id = projectId, Name = "activation-" + projectId.ToString("N"),
            CreatedAt = h.Now, UpdatedAt = h.Now });
        db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "activation",
            CreatedAt = h.Now, UpdatedAt = h.Now });
        await db.SaveChangesAsync();
        await db.Agents.Where(a => a.Id == h.AgentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.BoardId, boardId));
    }

    private static Task<BridgeQueueHarness> CreateCataloglessHarnessAsync(string connectionString, bool unifiedRecovery) =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = connectionString,
            Bridge = new ChannelBridgeSettings { Enabled = true, MachineTurnTextOrigins = [QueuedMessageOrigin.Check] },
            ConfigureServices = services => services.AddSingleton<IOptions<ChannelOutboundSettings>>(Options.Create(
                new ChannelOutboundSettings { UnifiedRecoveryEnabled = unifiedRecovery })),
        });

    private static async Task AssertDirectSettlementAsync(BridgeQueueHarness h, Guid memberId)
    {
        await using var observer = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
        var member = await observer.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == memberId);
        member.ChannelReplySettledAt.ShouldNotBeNull();
        member.ChannelOutboundDeliveryId.ShouldBeNull();
        (await observer.ChannelOutboundDeliveries.CountAsync(d => d.SourceSessionId == h.SessionId)).ShouldBe(0);
        (await observer.ChatChannels.CountAsync(c => c.ExternalId == "catalogless")).ShouldBe(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Missing_catalog_main_publishes_once_and_settles_without_capture(bool unifiedRecovery)
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CreateCataloglessHarnessAsync(isolated.ConnectionString, unifiedRecovery);
        const string prompt = "Send the source without a catalog entry";
        var memberId = await h.SeedChannelCorrelationAsync(prompt, "telegram:catalogless");
        var path = Path.Combine(h.TempRoot, "workspace", "source.md");
        var bytes = "# Complete main source\r\nMiddle and tail ✨\r\n"u8.ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        await h.InsertTurnAsync(prompt, $"Complete main answer.\n[[attach: {path}]]");

        var result = await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        result.OutcomeFor(memberId).ShouldBe(ChannelReplyDispatchOutcome.Published);
        var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
        reply.ConversationId.ShouldBe("catalogless");
        reply.ReplyHandle.ShouldBeNull();
        reply.Text.ShouldBe("Complete main answer.");
        reply.Attachments.ShouldHaveSingleItem().Content.ShouldBe(bytes);
        await AssertDirectSettlementAsync(h, memberId);
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        (await h.TickOutboundAsync()).ShouldBe(0);
        h.Messaging.SentReplies.Count.ShouldBe(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Missing_catalog_machine_publishes_once_and_settles_without_capture(bool unifiedRecovery)
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CreateCataloglessHarnessAsync(isolated.ConnectionString, unifiedRecovery);
        var contextId = await h.SeedChannelCorrelationAsync("Original chat", "telegram:catalogless");
        await h.InsertTurnAsync("Original chat", "NO_REPLY");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        const string prompt = "[Check] Send the complete machine source";
        var memberId = await h.SeedPendingMessageAsync(prompt, status: QueuedMessageStatus.Sent,
            origin: QueuedMessageOrigin.Check);
        var path = Path.Combine(h.TempRoot, "workspace", "machine.md");
        var bytes = "# Complete machine source\r\nMiddle and tail ✨\r\n"u8.ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        await h.InsertTurnAsync(prompt, $"Complete machine answer.\n[[attach: {path}]]");

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
        reply.ConversationId.ShouldBe("catalogless");
        reply.ReplyHandle.ShouldBeNull();
        reply.Text.ShouldBe("Complete machine answer.");
        reply.Attachments.ShouldHaveSingleItem().Content.ShouldBe(bytes);
        await AssertDirectSettlementAsync(h, memberId);
        await AssertDirectSettlementAsync(h, contextId);
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        (await h.TickOutboundAsync()).ShouldBe(0);
        h.Messaging.SentReplies.Count.ShouldBe(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Missing_catalog_trailing_publishes_once_without_capture(bool unifiedRecovery)
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CreateCataloglessHarnessAsync(isolated.ConnectionString, unifiedRecovery);
        const string prompt = "An initially silent answer with a later fragment";
        var memberId = await h.SeedChannelCorrelationAsync(prompt, "telegram:catalogless");
        // Silence establishes the routing watermark independently of main publication.
        // Thus this test reaches the trailing send even when the main send has a defect.
        await h.InsertTurnAsync(prompt, "NO_REPLY");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Messaging.SentReplies.ShouldBeEmpty();
        var path = Path.Combine(h.TempRoot, "workspace", "trailing.md");
        var bytes = "# Complete trailing source\r\nMiddle and tail ✨\r\n"u8.ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, $"Complete trailing answer.\n[[attach: {path}]]");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
        reply.ConversationId.ShouldBe("catalogless");
        reply.ReplyHandle.ShouldBeNull();
        reply.Text.ShouldBe("Complete trailing answer.");
        reply.Attachments.ShouldHaveSingleItem().Content.ShouldBe(bytes);
        await AssertDirectSettlementAsync(h, memberId);
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        (await h.TickOutboundAsync()).ShouldBe(0);
        h.Messaging.SentReplies.Count.ShouldBe(1);
    }

    [Test]
    public async Task Activation_captures_before_source_reads_and_publishes_the_staged_bytes()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var reader = new ChannelReplyAttachmentReader();
        var reads = 0;
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = isolated.ConnectionString,
            ConfigureServices = services =>
            {
                services.AddSingleton<IOptions<ChannelOutboundSettings>>(Options.Create(
                    new ChannelOutboundSettings { UnifiedRecoveryEnabled = true }));
                services.AddSingleton<IChannelReplyAttachmentReader>(reader);
            },
        });
        await BindProjectAsync(h);
        var conversation = await h.BindChannelAsync();
        var prompt = "Send the complete source, including its middle and tail.";
        var correlationId = await h.SeedChannelCorrelationAsync(prompt, "telegram:" + conversation);
        var path = Path.Combine(h.TempRoot, "workspace", "late-source.md");
        // The source does not exist during dispatch. A read-before-capture implementation
        // either drops it or cannot commit the durable owner.
        await h.InsertTurnAsync(prompt, $"Complete answer.\n[[attach: {path}]]");
        var dispatched = await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        dispatched.OutcomeFor(correlationId).ShouldBe(ChannelReplyDispatchOutcome.Deferred);
        h.Messaging.SentReplies.ShouldBeEmpty();
        Guid deliveryId;
        await using (var observer = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString)))
        {
            var member = await observer.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlationId);
            member.ChannelOutboundDeliveryId.ShouldNotBeNull();
            deliveryId = member.ChannelOutboundDeliveryId.Value;
            member.ChannelReplySettledAt.ShouldBeNull();
            var capture = await observer.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);
            capture.State.ShouldBe(ChannelOutboundDeliveryState.Captured);
            capture.InputPath.ShouldBeEmpty();
            ChannelReplyPreparation.Deserialize(capture.CaptureJson!).Body.AttachmentPaths.ShouldContain(path);
        }
        reader.BeforeReadAsync = async (source, ct) =>
        {
            reads++;
            await using var observer = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
            var member = await observer.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlationId, ct);
            member.ChannelOutboundDeliveryId.ShouldBe(deliveryId);
            member.ChannelReplySettledAt.ShouldBeNull();
        };
        var bytes = "# Original staged source\r\nComplete middle and tail ✨\r\n"u8.ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        (await h.TickOutboundAsync()).ShouldBe(1);
        reads.ShouldBe(1);
        h.Messaging.SentReplies.ShouldBeEmpty();
        await File.WriteAllTextAsync(path, "replacement that must not be sent");
        (await h.TickOutboundAsync()).ShouldBe(1);
        reads.ShouldBe(1);
        var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
        reply.Text.ShouldBe("Complete answer.");
        reply.Attachments.ShouldHaveSingleItem().Content.ShouldBe(bytes);
        await using var accepted = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
        (await accepted.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId))
            .State.ShouldBe(ChannelOutboundDeliveryState.Published);
        (await accepted.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlationId))
            .ChannelReplySettledAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Activation_preserves_machine_silence_and_origin_policy_before_capture()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = isolated.ConnectionString,
            Bridge = new ChannelBridgeSettings { Enabled = true, MachineTurnTextOrigins = [QueuedMessageOrigin.Check] },
            ConfigureServices = services => services.AddSingleton<IOptions<ChannelOutboundSettings>>(Options.Create(
                new ChannelOutboundSettings { UnifiedRecoveryEnabled = true })),
        });
        await BindProjectAsync(h);
        var conversation = await h.BindChannelAsync();
        // A settled previous channel turn supplies context, without becoming a new obligation.
        var old = await h.SeedChannelCorrelationAsync("Original chat", "telegram:" + conversation);
        await h.InsertTurnAsync("Original chat", "NO_REPLY");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await using (var silence = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString)))
        {
            (await silence.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == old))
                .ChannelReplySettledAt.ShouldNotBeNull();
            (await silence.ChannelOutboundDeliveries.CountAsync(d => d.SourceSessionId == h.SessionId)).ShouldBe(0);
        }
        foreach (var (origin, prompt, response) in new[]
        {
            (QueuedMessageOrigin.System, "[System] Policy holds this plain text", "Held machine answer"),
            (QueuedMessageOrigin.Check, "[Check] Keep this turn silent", "NO_REPLY"),
            (QueuedMessageOrigin.Check, "[Check] Answer this turn", "Allowed complete machine answer"),
        })
        {
            var id = await h.SeedPendingMessageAsync(prompt, status: QueuedMessageStatus.Sent, origin: origin);
            await h.InsertTurnAsync(prompt, response);
            await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
            await using var observer = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
            var member = await observer.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
            member.ChannelReplySettledAt.ShouldBeNull();
            if (response == "Allowed complete machine answer")
            {
                member.ChannelOutboundDeliveryId.ShouldNotBeNull();
                var capture = await observer.ChannelOutboundDeliveries.AsNoTracking()
                    .SingleAsync(d => d.Id == member.ChannelOutboundDeliveryId);
                capture.SendKind.ShouldBe("machine");
                capture.State.ShouldBe(ChannelOutboundDeliveryState.Captured);
            }
            else member.ChannelOutboundDeliveryId.ShouldBeNull();
        }
        h.Messaging.SentReplies.ShouldBeEmpty();
        (await h.TickOutboundAsync()).ShouldBe(1);
        (await h.TickOutboundAsync()).ShouldBe(1);
        h.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("Allowed complete machine answer");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Dispatcher_defers_only_the_bound_conversation_and_preserves_source_bytes(bool unifiedRecovery)
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-dispatch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var profileProject = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var store = new ChannelOutboundFileStore(Path.Combine(root, "outbound"));
        var profileSettings = Options.Create(new ChannelOutboundSettings
        {
            Profiles = new Dictionary<string, ChannelOutboundProfile>
            {
                ["markdown-pdf"] = new()
                {
                    ProjectId = profileProject, AgentId = converterId,
                    PromptFile = "convert.md", Trigger = ChannelOutboundTrigger.MarkdownSources,
                },
            },
        });
        if (unifiedRecovery) profileSettings.Value.UnifiedRecoveryEnabled = true;
        await File.WriteAllTextAsync(Path.Combine(root, "convert.md"), "Convert supplied Markdown.");
        var sourceBytes = "# Synthetic source\r\nPolski tekst i emoji ✨\r\n"u8.ToArray();
        var sourcePath = Path.Combine(root, "source.md");
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            Bridge = new ChannelBridgeSettings { Enabled = true, DebounceWindowMs = 0 },
            ConfigureServices = services =>
            {
                services.AddSingleton<IChannelOutboundFileStore>(store);
                services.AddSingleton<IOptions<ChannelOutboundSettings>>(profileSettings);
                services.AddScoped<ChannelOutboundService>();
            },
        });
        Guid? deliveryId = null;
        Guid? unrelatedZipTaskId = null;
        try
        {
            await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString)))
            {
                var now = DateTime.UtcNow;
                seed.Projects.Add(new Project { Id = profileProject, Name = "dispatch-" + profileProject.ToString("N"),
                    CreatedAt = now, UpdatedAt = now });
                seed.Boards.Add(new Board { Id = boardId, ProjectId = profileProject, Name = "outbound",
                    CreatedAt = now, UpdatedAt = now });
                seed.Agents.Add(new Agent { Id = converterId, Name = "converter",
                    Slug = "converter-" + converterId.ToString("N"), BoardId = boardId,
                    WorkingDirectory = root });
                await seed.SaveChangesAsync();
                await seed.Agents.Where(a => a.Id == h.AgentId)
                    .ExecuteUpdateAsync(u => u.SetProperty(a => a.BoardId, boardId));
                await seed.AgentSessions.Where(s => s.Id == h.SessionId)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Cwd, root));
            }

            var x = await h.BindChannelAsync();
            var y = await h.BindChannelAsync();
            Guid xId;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString)))
            {
                var bound = await db.ChatChannels.SingleAsync(c => c.ExternalId == x);
                xId = bound.Id;
                bound.OutboundAgentProfile = "markdown-pdf";
                bound.ReplyHandle = "T1";
                await db.SaveChangesAsync();
            }
            var xPrompt = "[Telegram X] Send the source";
            var xCorrelation = await h.SeedChannelCorrelationAsync(xPrompt, "telegram:" + x);
            await h.InsertTurnAsync(xPrompt, $"Here is the source.\n[[attach: {sourcePath}]]");
            await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
            h.Messaging.SentReplies.ShouldBeEmpty();
            if (unifiedRecovery)
            {
                await using var observer = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
                var captured = await observer.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.ChannelId == xId);
                captured.State.ShouldBe(ChannelOutboundDeliveryState.Captured);
                captured.InputPath.ShouldBeEmpty();
                captured.PreparationAttempts.ShouldBe(0);
                ChannelReplyPreparation.Deserialize(captured.CaptureJson!).Body.AttachmentPaths
                    .ShouldContain(sourcePath);
                (await h.TickOutboundAsync()).ShouldBe(1);
            }

            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString)))
            {
                var intent = await db.ChannelOutboundDeliveries.SingleAsync(d => d.ChannelId == xId);
                deliveryId = intent.Id;
                intent.State.ShouldBe(ChannelOutboundDeliveryState.Pending);
                intent.ProfileName.ShouldBe("markdown-pdf");
                (await db.SessionQueuedMessages.SingleAsync(m => m.Id == xCorrelation))
                    .ChannelReplySettledAt.ShouldBeNull();
                var frozen = await store.ReadReplyAsync(intent.InputPath, intent.InputSha256,
                    CancellationToken.None);
                frozen.ReplyHandle.ShouldBe("T1");
                frozen.Attachments.ShouldHaveSingleItem().Content.ShouldBe(sourceBytes);
                (await db.ChatChannels.SingleAsync(c => c.Id == xId)).ReplyHandle = "T2";
                await db.SaveChangesAsync();
            }

            var yPrompt = "[Telegram Y] Send the same source";
            var yCorrelation = await h.SeedChannelCorrelationAsync(yPrompt, "telegram:" + y);
            await h.InsertTurnAsync(yPrompt, $"Here is the source.\n[[attach: {sourcePath}]]");
            await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
            if (unifiedRecovery)
            {
                h.Messaging.SentReplies.ShouldBeEmpty();
                await using var observer = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
                var captured = await observer.ChannelOutboundDeliveries.AsNoTracking()
                    .SingleAsync(d => d.ChannelId != xId && d.InboundAgentId == h.AgentId);
                captured.State.ShouldBe(ChannelOutboundDeliveryState.Captured);
                (await observer.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == yCorrelation))
                    .ChannelReplySettledAt.ShouldBeNull();
                // Materialize Y without starting X's converter; the latter is asserted below.
                var tasks = new AgentTaskService(observer,
                    new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                    Options.Create(new DelegationSettings { AllowedRoots = [root] }),
                    new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System,
                    NullLogger<AgentTaskService>.Instance);
                var pump = new ChannelOutboundDeliveryPump(observer, new OutboundConversionTaskRunner(observer, tasks),
                    store, h.Messaging, Options.Create(new AntiphonMessagingOptions()), TimeProvider.System,
                    NullLogger<ChannelOutboundDeliveryPump>.Instance, profileSettings,
                    new ChannelReplyPreparation(new ChannelReplyAttachmentReader()));
                (await pump.TickAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
                (await pump.TickAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
            }
            h.Messaging.SentReplies.ShouldHaveSingleItem().ConversationId.ShouldBe(y);
            h.Messaging.SentReplies[0].Attachments.ShouldHaveSingleItem().Content.ShouldBe(sourceBytes);
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString)))
            {
                (await db.SessionQueuedMessages.SingleAsync(m => m.Id == yCorrelation))
                    .ChannelReplySettledAt.ShouldNotBeNull();
                (await db.SessionQueuedMessages.SingleAsync(m => m.Id == xCorrelation))
                    .ChannelReplySettledAt.ShouldBeNull();
            }

            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString)))
            {
                var tasks = new AgentTaskService(db,
                    new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                    Options.Create(new DelegationSettings { AllowedRoots = [root] }),
                    new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System,
                    NullLogger<AgentTaskService>.Instance);
                var pump = new ChannelOutboundDeliveryPump(db,
                    new OutboundConversionTaskRunner(db, tasks), store, h.Messaging,
                    Options.Create(new AntiphonMessagingOptions()), TimeProvider.System,
                    NullLogger<ChannelOutboundDeliveryPump>.Instance, profileSettings);
                (await pump.TickAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
                db.ChangeTracker.Clear();
                var intent = await db.ChannelOutboundDeliveries.SingleAsync(d => d.Id == deliveryId);
                intent.State.ShouldBe(ChannelOutboundDeliveryState.Converting);
                var task = await db.AgentTasks.SingleAsync(t => t.Id == intent.ConversionTaskId);
                task.OutboundDeliveryId.ShouldBe(deliveryId);
                task.Kind.ShouldBe(AgentTaskKind.Worker);
                task.Role.ShouldBe(AgentTaskRole.Custom);
                task.Workspace.ShouldBe(WorkspaceMode.Shared);
                task.ProjectId.ShouldBe(profileProject);
                task.AgentId.ShouldBe(converterId);
                task.ParentTaskId.ShouldBeNull();
                task.ParentSessionId.ShouldBeNull();
                task.CardId.ShouldBeNull();
                task.ExecutionDeadlineAt.ShouldBe(intent.DeadlineAt);
                task.MaxAttempts.ShouldBe(1);
                task.CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never);
                task.LaunchEnvOverrideJson.ShouldBe("{}");
                task.InheritedLaunchEnvJson.ShouldBe("{}");
                task.Goal.ShouldContain("Do not dispatch child tasks or send to a channel");
                task.Goal.ShouldContain("Convert supplied Markdown.");
                task.Goal.ShouldContain(Path.Combine(Path.GetDirectoryName(intent.InputPath)!, "request.json"));
                task.ReplyTo.ShouldBe(AgentTaskReplyTo.None);
                (await db.AgentTasks.CountAsync(t => t.OutboundDeliveryId == deliveryId)).ShouldBe(1);
                (await db.ChannelOutboundDeliveries.CountAsync(d => d.ChannelId != xId
                    && d.InboundAgentId == h.AgentId)).ShouldBe(unifiedRecovery ? 1 : 0);
                var pdf = "%PDF-1.4 synthetic conversion"u8.ToArray();
                var output = Path.Combine(Path.GetDirectoryName(intent.InputPath)!, "output");
                await File.WriteAllBytesAsync(Path.Combine(output, "combined.pdf"), pdf);
                await File.WriteAllTextAsync(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
                {
                    version = 1, deliveryId = intent.Id, disposition = "converted",
                    files = new[] { new { path = "combined.pdf", name = "combined.pdf",
                        mime = "application/pdf", length = pdf.Length,
                        sha256 = Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant() } },
                }));
                task.Status = AgentTaskStatus.Succeeded;
                task.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                (await pump.TickAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
                db.ChangeTracker.Clear();
                var published = await db.ChannelOutboundDeliveries.SingleAsync(d => d.Id == deliveryId);
                published.State.ShouldBe(ChannelOutboundDeliveryState.Published);
                (await db.SessionQueuedMessages.SingleAsync(m => m.Id == xCorrelation))
                    .ChannelReplySettledAt.ShouldNotBeNull();
                (await db.AgentTasks.CountAsync(t => t.OutboundDeliveryId == deliveryId)).ShouldBe(1);
            }
            h.Messaging.SentReplies.Count.ShouldBe(2);
            var xReply = h.Messaging.SentReplies[1];
            xReply.ConversationId.ShouldBe(x);
            xReply.ReplyHandle.ShouldBe("T1");
            xReply.Attachments.Count.ShouldBe(2);
            xReply.Attachments[0].Content.ShouldBe(sourceBytes);
            xReply.Attachments[1].Content.ShouldBe("%PDF-1.4 synthetic conversion"u8.ToArray());

            // A source task id and manifest do not turn an unrelated zip into a
            // Markdown-source trigger. Activation keeps it in the passthrough pump path.
            unrelatedZipTaskId = Guid.NewGuid();
            var bundleDir = Path.Combine(root, "stray-bundle");
            Directory.CreateDirectory(bundleDir);
            var sourceManifest = new DeliverableBundleService.SourceManifest(1, true,
                [new("docs/source.md", "sources.zip", "docs/source.md", sourceBytes.Length,
                    Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant())], []);
            await File.WriteAllTextAsync(Path.Combine(bundleDir, DeliverableBundleService.SourceManifestName),
                JsonSerializer.Serialize(sourceManifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString)))
            {
                db.AgentTasks.Add(new AgentTask
                {
                    Id = unrelatedZipTaskId.Value, RootTaskId = unrelatedZipTaskId.Value,
                    ProjectId = profileProject, Title = "Source task", Goal = "Produce sources",
                    WorkingDirectory = root, RepoPath = root, Status = AgentTaskStatus.Succeeded,
                    DeliverableBundleDir = bundleDir, CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
                var outbound = new ChannelOutboundService(db, store, h.Messaging,
                    profileSettings, TimeProvider.System);
                var unrelated = new ChannelReply
                {
                    Channel = "telegram", ConversationId = x, Text = "unrelated archive",
                    Attachments = [new OutboundAttachment { Kind = AttachmentKind.File,
                        Name = "unrelated.zip", Mime = "application/zip", Content = [1, 2, 3] }],
                };
                (await outbound.SendAsync(unrelated, ChannelOutboundOrigin.AgentReply,
                    new ChannelOutboundSource(h.SessionId, 700, 701, 702, "main", [],
                        unrelatedZipTaskId), CancellationToken.None))
                    .ShouldBe(unifiedRecovery ? ChannelOutboundSendOutcome.Deferred : ChannelOutboundSendOutcome.Published);
                (await db.ChannelOutboundDeliveries.CountAsync(d => d.ChannelId == xId)).ShouldBe(unifiedRecovery ? 2 : 1);
            }
            if (unifiedRecovery)
            {
                h.Messaging.SentReplies.Count.ShouldBe(2);
                (await h.TickOutboundAsync()).ShouldBe(1);
            }
            h.Messaging.SentReplies.Count.ShouldBe(3);
            h.Messaging.SentReplies[2].Attachments.ShouldHaveSingleItem().Name.ShouldBe("unrelated.zip");
        }
        finally
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
            if (deliveryId is Guid id)
            {
                await db.SessionQueuedMessages.Where(m => m.ChannelOutboundDeliveryId == id)
                    .ExecuteUpdateAsync(u => u.SetProperty(m => m.ChannelOutboundDeliveryId, (Guid?)null));
                await db.AgentTasks.Where(t => t.OutboundDeliveryId == id).ExecuteDeleteAsync();
                await db.ChannelOutboundDeliveries.Where(d => d.Id == id).ExecuteDeleteAsync();
            }
            if (unrelatedZipTaskId is Guid taskId)
                await db.AgentTasks.Where(t => t.Id == taskId).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.BoardId, (Guid?)null));
            await db.Agents.Where(a => a.Id == converterId).ExecuteDeleteAsync();
            await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
            await db.Projects.Where(p => p.Id == profileProject).ExecuteDeleteAsync();
            Directory.Delete(root, recursive: true);
        }
    }
}
