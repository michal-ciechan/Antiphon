using System.IO.Compression;
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

public sealed partial class ChannelOutboundDeliveryTests
{
    [Test]
    [Arguments("main", "explicit-md", "MarkdownSources", true)]
    [Arguments("trailing", "explicit-md", "MarkdownSources", true)]
    [Arguments("machine", "explicit-md", "MarkdownSources", true)]
    [Arguments("machine", "manifest-zip", "MarkdownSources", true)]
    [Arguments("main", "plain-markdown", "EveryAgentReply", true)]
    [Arguments("main", "plain-markdown", "MarkdownSources", false)]
    [Arguments("main", "unrelated-zip", "MarkdownSources", false)]
    [Arguments("main", "plain-markdown", "none", false)]
    public async Task Main_trailing_and_machine_use_the_same_policy(
        string sendKind, string shape, string trigger, bool converts)
    {
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var source = "# Source\r\nUnicode ✨  \r\n"u8.ToArray();
        var root = Directory.CreateTempSubdirectory("c0418-send-shapes-").FullName;
        var store = new ChannelOutboundFileStore(Path.Combine(root, "outbound"));
        var settings = Options.Create(new ChannelOutboundSettings
        {
            Profiles = new Dictionary<string, ChannelOutboundProfile>
            {
                ["conversion"] = new()
                {
                    ProjectId = projectId, AgentId = converterId, PromptFile = "convert.md",
                    Trigger = trigger == "EveryAgentReply"
                        ? ChannelOutboundTrigger.EveryAgentReply : ChannelOutboundTrigger.MarkdownSources,
                },
            },
        });
        await File.WriteAllTextAsync(Path.Combine(root, "convert.md"), "Convert the frozen reply.");
        var md = Path.Combine(root, "source.md");
        await File.WriteAllBytesAsync(md, source);
        var zip = Path.Combine(root, shape == "unrelated-zip" ? "unrelated.zip" : "sources.zip");
        await File.WriteAllBytesAsync(zip, [1, 2, 3]);
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            Bridge = new ChannelBridgeSettings { Enabled = true, DebounceWindowMs = 0 },
            ConfigureServices = services =>
            {
                services.AddSingleton<IChannelOutboundFileStore>(store);
                services.AddSingleton<IOptions<ChannelOutboundSettings>>(settings);
                services.AddScoped<ChannelOutboundService>();
            },
        });
        Guid? channelId = null;
        try
        {
            await using (var db = Db(h))
            {
                var now = DateTime.UtcNow;
                db.Projects.Add(new Project { Id = projectId, Name = "shape-" + projectId.ToString("N"),
                    CreatedAt = now, UpdatedAt = now });
                db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "shapes",
                    CreatedAt = now, UpdatedAt = now });
                db.Agents.Add(new Agent { Id = converterId, BoardId = boardId, Name = "converter",
                    Slug = "converter-" + converterId.ToString("N"), WorkingDirectory = root });
                await db.SaveChangesAsync();
                await db.Agents.Where(a => a.Id == h.AgentId)
                    .ExecuteUpdateAsync(u => u.SetProperty(a => a.BoardId, boardId));
            }
            var conversation = await h.BindChannelAsync();
            await using (var db = Db(h))
            {
                var channel = await db.ChatChannels.SingleAsync(c => c.ExternalId == conversation);
                channelId = channel.Id;
                if (trigger != "none") channel.OutboundAgentProfile = "conversion";
                await db.SaveChangesAsync();
                if (shape is "manifest-zip" or "unrelated-zip")
                {
                    var bundle = Path.Combine(root, "bundle");
                    Directory.CreateDirectory(bundle);
                    var sourceZip = Path.Combine(bundle, "round24-sources.zip");
                    using (var archive = ZipFile.Open(sourceZip, ZipArchiveMode.Create))
                    {
                        var entry = archive.CreateEntry("docs/source.md");
                        await using var stream = entry.Open();
                        await stream.WriteAsync(source);
                    }
                    if (shape == "manifest-zip") zip = sourceZip;
                    var member = new DeliverableBundleService.SourceMember("docs/source.md",
                        Path.GetFileName(sourceZip), "docs/source.md", source.Length,
                        Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant());
                    var manifest = new DeliverableBundleService.SourceManifest(1, true, [member], []);
                    await File.WriteAllTextAsync(Path.Combine(bundle,
                        DeliverableBundleService.SourceManifestName), JsonSerializer.Serialize(manifest,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                    db.AgentTasks.Add(new AgentTask
                    {
                        Id = taskId, RootTaskId = taskId, ProjectId = projectId,
                        Title = "Source bundle", Goal = "Write sources", WorkingDirectory = root,
                        RepoPath = root, Status = AgentTaskStatus.Succeeded,
                        DeliverableBundleDir = bundle, CreatedAt = DateTime.UtcNow,
                    });
                    await db.SaveChangesAsync();
                    if (shape == "manifest-zip")
                        DeliverableBundleService.ListAttachableFiles(await db.AgentTasks
                            .SingleAsync(t => t.Id == taskId)).ShouldContain(zip);
                }
            }

            var marker = shape switch
            {
                "explicit-md" => $"[[attach: {md}]]",
                "manifest-zip" or "unrelated-zip" => $"[[attach: {zip}]]",
                _ => "# Markdown body without an attachment",
            };
            var prompt = "[Telegram X] send sources";
            if (sendKind == "main")
            {
                var correlationId = await h.SeedChannelCorrelationAsync(prompt,
                    "telegram:" + conversation);
                if (shape == "unrelated-zip")
                {
                    await using var db = Db(h);
                    await db.SessionQueuedMessages.Where(m => m.Id == correlationId)
                        .ExecuteUpdateAsync(u => u.SetProperty(m => m.SourceTaskId, taskId));
                }
                await h.InsertTurnAsync(prompt, "Source answer\n" + marker);
                await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
            }
            else
            {
                await h.SeedChannelCorrelationAsync(prompt, "telegram:" + conversation);
                await h.InsertTurnAsync(prompt, "Initial text only.");
                await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
                if (sendKind == "trailing")
                {
                    await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText,
                        "Follow-up\n" + marker);
                    await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
                }
                else
                {
                    var note = "[task " + DelegationReportFormatter.Short(taskId) + " done] Sources ready";
                    await using (var db = Db(h))
                    {
                        var sequence = (await db.SessionQueuedMessages
                            .Where(m => m.AgentSessionId == h.SessionId)
                            .MaxAsync(m => (long?)m.Sequence) ?? 0) + 1;
                        var now = DateTime.UtcNow;
                        db.SessionQueuedMessages.Add(new SessionQueuedMessage
                        {
                            Id = Guid.NewGuid(), AgentSessionId = h.SessionId,
                            Body = note, Sequence = sequence, Status = QueuedMessageStatus.Sent,
                            Origin = QueuedMessageOrigin.Delegation, SourceTaskId = taskId,
                            ConversationKey = "telegram:" + conversation,
                            CreatedAt = now, SentAt = now, DeliveryAttempts = 1,
                        });
                        await db.SaveChangesAsync();
                    }
                    await h.InsertTurnAsync(note, "Machine result\n" + marker);
                    await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
                }
            }

            await using (var db = Db(h))
            {
                var intents = await db.ChannelOutboundDeliveries.AsNoTracking()
                    .Where(d => d.ChannelId == channelId).ToListAsync();
                var directDetails = shape == "manifest-zip" && intents.Count == 0
                    ? $" direct=[{string.Join(", ", h.Messaging.SentReplies.Select(r =>
                        r.Text + ":" + string.Join("/", r.Attachments.Select(a => a.Name))))}]"
                    : "";
                intents.Count.ShouldBe(converts ? 1 : 0, shape + directDetails);
                if (converts)
                {
                    var intent = intents.ShouldHaveSingleItem();
                    intent.SendKind.ShouldBe(sendKind);
                    intent.State.ShouldBe(ChannelOutboundDeliveryState.Pending);
                    intent.Trigger.ShouldBe(trigger);
                    h.Messaging.SentReplies.Count.ShouldBe(sendKind == "main" ? 0 : 1);
                    var frozen = await store.ReadReplyAsync(intent.InputPath, intent.InputSha256,
                        CancellationToken.None);
                    if (shape == "explicit-md")
                        frozen.Attachments.ShouldHaveSingleItem().Content.ShouldBe(source);
                    if (shape is "manifest-zip" or "unrelated-zip")
                        frozen.Attachments.ShouldHaveSingleItem().Name.ShouldBe(Path.GetFileName(zip));
                    var tasks = new AgentTaskService(db,
                        new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                        Options.Create(new DelegationSettings { AllowedRoots = [root] }),
                        new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System,
                        NullLogger<AgentTaskService>.Instance);
                    var pump = new ChannelOutboundDeliveryPump(db,
                        new OutboundConversionTaskRunner(db, tasks), store, h.Messaging,
                        Options.Create(new AntiphonMessagingOptions()), TimeProvider.System,
                        NullLogger<ChannelOutboundDeliveryPump>.Instance, settings);
                    (await pump.TickAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
                    db.ChangeTracker.Clear();
                    var prepared = await db.ChannelOutboundDeliveries.AsNoTracking()
                        .SingleAsync(d => d.Id == intent.Id);
                    prepared.State.ShouldBe(ChannelOutboundDeliveryState.Converting);
                    (await db.AgentTasks.AsNoTracking().CountAsync(t => t.OutboundDeliveryId == intent.Id))
                        .ShouldBe(1);
                }
                else
                {
                    var direct = h.Messaging.SentReplies.Last();
                    direct.Text.ShouldBe(shape == "plain-markdown"
                        ? "Source answer\n# Markdown body without an attachment" : "Source answer");
                    direct.Kind.ShouldBe(ChannelReplyKind.Answer);
                    direct.ConversationId.ShouldBe(conversation);
                    if (shape == "unrelated-zip")
                        direct.Attachments.ShouldHaveSingleItem().Content.ShouldBe([1, 2, 3]);
                    else
                        direct.Attachments.ShouldBeEmpty();
                    (await db.AgentTasks.CountAsync(t => t.ProjectId == projectId
                        && t.OutboundDeliveryId != null)).ShouldBe(0);
                }
            }
        }
        finally
        {
            await using var db = Db(h);
            var deliveryIds = channelId is Guid id
                ? await db.ChannelOutboundDeliveries.Where(d => d.ChannelId == id)
                    .Select(d => d.Id).ToArrayAsync()
                : [];
            await db.SessionQueuedMessages.Where(m => m.AgentSessionId == h.SessionId
                && m.ChannelOutboundDeliveryId != null
                && deliveryIds.Contains(m.ChannelOutboundDeliveryId.Value))
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.ChannelOutboundDeliveryId,
                    (Guid?)null));
            await db.SessionQueuedMessages.Where(m => m.AgentSessionId == h.SessionId
                && m.SourceTaskId == taskId)
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.SourceTaskId, (Guid?)null));
            await db.AgentTasks.Where(t => t.Id == taskId
                || (t.OutboundDeliveryId != null
                    && deliveryIds.Contains(t.OutboundDeliveryId.Value)))
                .ExecuteDeleteAsync();
            await db.ChannelOutboundDeliveries.Where(d => deliveryIds.Contains(d.Id))
                .ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.BoardId, (Guid?)null));
            await db.Agents.Where(a => a.Id == converterId).ExecuteDeleteAsync();
            await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
            await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    private static AppDbContext Db(BridgeQueueHarness h) =>
        new(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
}
