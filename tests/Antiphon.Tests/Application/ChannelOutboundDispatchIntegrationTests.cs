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
    [Test]
    public async Task Dispatcher_defers_only_the_bound_conversation_and_preserves_source_bytes()
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
                task.ReplyTo.ShouldBe(AgentTaskReplyTo.None);
                (await db.AgentTasks.CountAsync(t => t.OutboundDeliveryId == deliveryId)).ShouldBe(1);
                (await db.ChannelOutboundDeliveries.CountAsync(d => d.ChannelId != xId)).ShouldBe(0);
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
            // Markdown-source trigger. The actual send route must remain direct.
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
                    .ShouldBe(ChannelOutboundSendOutcome.Published);
                (await db.ChannelOutboundDeliveries.CountAsync(d => d.ChannelId == xId)).ShouldBe(1);
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
