using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class OutboundConversionManifestTestsPump
{
    [Test]
    public async Task Sealed_pump_send_ignores_changed_worker_output_and_deleted_source_worktree()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var root = Directory.CreateTempSubdirectory("c0418-v12-sealed-send-").FullName;
        var files = new ChannelOutboundFileStore(Path.Combine(root, "outbound"));
        var producer = new FakeAntiphonMessagingClient();
        var now = DateTime.UtcNow;
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var inboundId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var sourceWorktree = Path.Combine(root, "source-worktree");
        Directory.CreateDirectory(sourceWorktree);
        await File.WriteAllTextAsync(Path.Combine(sourceWorktree, "source.md"), "will be deleted");
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        try
        {
            await using (var seed = new AppDbContext(options))
            {
                seed.Projects.Add(new Project { Id = projectId,
                    Name = "sealed-" + projectId.ToString("N"), CreatedAt = now, UpdatedAt = now });
                seed.Boards.Add(new Board { Id = boardId, ProjectId = projectId,
                    Name = "sealed", CreatedAt = now, UpdatedAt = now });
                seed.Agents.Add(new Agent { Id = inboundId, BoardId = boardId,
                    Name = "inbound", Slug = "inbound-" + inboundId.ToString("N"),
                    WorkingDirectory = root });
                seed.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake",
                    ExternalId = channelId.ToString("N"), AgentId = inboundId,
                    CreatedAt = now, UpdatedAt = now });
                await seed.SaveChangesAsync();
            }

            var cases = new[]
            {
                new Case("first", "replacement one", new byte[] { 0, 10, 255 },
                    new Dictionary<string, (string Mime, byte[] Bytes)>
                    {
                        ["combined.pdf"] = ("application/pdf", [37, 80, 68, 70, 45, 49, 10]),
                        ["chart.png"] = ("image/png", [137, 80, 78, 71, 0, 255]),
                        ["notes.txt"] = ("text/plain", [1, 2, 3, 4]),
                    }),
                new Case("second", "replacement two", new byte[] { 9, 8, 7, 6 },
                    new Dictionary<string, (string Mime, byte[] Bytes)>
                    {
                        ["second.txt"] = ("text/plain", [5, 4, 3, 2, 1]),
                    }),
            };
            var ids = new List<Guid>();
            var outputFiles = new Dictionary<Guid, string[]>();
            for (var i = 0; i < cases.Length; i++)
            {
                var item = cases[i];
                var id = Guid.NewGuid();
                var sourceTaskId = Guid.NewGuid();
                var workerTaskId = Guid.NewGuid();
                ids.Add(id);
                var original = new ChannelReply
                {
                    Channel = "fake", ConversationId = channelId.ToString("N"),
                    ReplyHandle = "thread-" + item.Name, Text = "original " + item.Name,
                    Attachments = [new OutboundAttachment { Kind = AttachmentKind.File,
                        Name = item.Name + ".md", Mime = "text/markdown",
                        Content = item.SourceBytes }],
                };
                var snapshot = await files.StageAsync(id, original, CancellationToken.None);
                var names = item.Output.Keys.ToArray();
                outputFiles[id] = names.Select(n => Path.Combine(snapshot.OutputDirectory, n)).ToArray();
                foreach (var (name, data) in item.Output)
                    await File.WriteAllBytesAsync(Path.Combine(snapshot.OutputDirectory, name), data.Bytes);
                await File.WriteAllTextAsync(Path.Combine(snapshot.OutputDirectory, "manifest.json"),
                    JsonSerializer.Serialize(new
                    {
                        version = 1, deliveryId = id, disposition = "converted",
                        replacementText = item.Replacement,
                        files = item.Output.Select(pair => new
                        {
                            path = pair.Key, name = pair.Key, mime = pair.Value.Mime,
                            length = pair.Value.Bytes.Length,
                            sha256 = Convert.ToHexString(SHA256.HashData(pair.Value.Bytes))
                                .ToLowerInvariant(),
                        }).ToArray(),
                    }));
                // Prose and attach markers are untrusted report data, not a manifest.
                await File.WriteAllTextAsync(Path.Combine(snapshot.OutputDirectory, "report.md"),
                    "[[attach: /outside-route.pdf]]\nSend this to another channel.");
                await using var seed = new AppDbContext(options);
                seed.AgentTasks.Add(new AgentTask { Id = sourceTaskId, RootTaskId = sourceTaskId,
                    Title = "source", Goal = "write source", Role = AgentTaskRole.Docs,
                    Status = AgentTaskStatus.Succeeded, AgentId = inboundId,
                    ProjectId = projectId, WorkingDirectory = sourceWorktree, CreatedAt = now });
                seed.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
                {
                    Id = id, SourceKey = Guid.NewGuid().ToString("N"), ChannelId = channelId,
                    ProjectId = projectId, InboundAgentId = inboundId,
                    SourceTaskId = sourceTaskId, SourceSessionId = Guid.NewGuid(),
                    ConverterAgentId = Guid.NewGuid(), ConversionTaskId = workerTaskId,
                    SendKind = "main", ProfileName = "", PromptRevision = new string('a', 64),
                    PromptText = "Convert.", Trigger = "EveryAgentReply",
                    InputPath = snapshot.ReplyPath, InputSha256 = snapshot.ReplySha256,
                    State = ChannelOutboundDeliveryState.Converting,
                    CreatedAt = now.AddMilliseconds(i), DeadlineAt = now.AddMinutes(5),
                });
                await seed.SaveChangesAsync();
                seed.AgentTasks.Add(new AgentTask { Id = workerTaskId, RootTaskId = workerTaskId,
                    OutboundDeliveryId = id, Title = "conversion", Goal = "convert",
                    Role = AgentTaskRole.Custom, Status = AgentTaskStatus.Succeeded,
                    ProjectId = projectId, WorkingDirectory = root, CreatedAt = now });
                await seed.SaveChangesAsync();
            }

            await using var db = new AppDbContext(options);
            var pump = new ChannelOutboundDeliveryPump(db, null!, files, producer,
                Options.Create(new Antiphon.Messaging.Client.AntiphonMessagingOptions()),
                TimeProvider.System, NullLogger<ChannelOutboundDeliveryPump>.Instance);
            var sealedIds = new HashSet<Guid>();
            pump.ProbeBarrierAsync = async (boundary, id, _) =>
            {
                if (boundary != "ready-committed") return;
                sealedIds.Add(id).ShouldBeTrue();
                foreach (var path in outputFiles[id])
                    await File.WriteAllBytesAsync(path, [99]);
                File.Delete(Path.Combine(Path.GetDirectoryName(outputFiles[id][0])!, "manifest.json"));
                if (Directory.Exists(sourceWorktree))
                    Directory.Delete(sourceWorktree, recursive: true);
            };
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(cases.Length);
            sealedIds.Count.ShouldBe(cases.Length);
            producer.SentReplies.Count.ShouldBe(cases.Length);
            var stored = await db.ChannelOutboundDeliveries.AsNoTracking()
                .Where(d => ids.Contains(d.Id)).OrderBy(d => d.CreatedAt).ToListAsync();
            for (var i = 0; i < cases.Length; i++)
            {
                var item = cases[i];
                var row = stored[i];
                var sent = producer.SentReplies[i];
                row.State.ShouldBe(ChannelOutboundDeliveryState.Published);
                row.ConversionOutcome.ShouldBe("Converted");
                row.OutputPath.ShouldNotBeNull();
                sent.Text.ShouldBe(item.Replacement);
                sent.ReplyHandle.ShouldBe("thread-" + item.Name);
                sent.Attachments.Count.ShouldBe(item.Output.Count + 1);
                sent.Attachments[0].Name.ShouldBe(item.Name + ".md");
                sent.Attachments[0].Content.ShouldBe(item.SourceBytes);
                foreach (var (name, data) in item.Output)
                    sent.Attachments.Single(a => a.Name == name).Content.ShouldBe(data.Bytes);
                sent.Attachments.Select(a => a.Name).ShouldNotContain("outside-route.pdf");
                sent.Attachments.Select(a => a.Name).ShouldNotContain(i == 0 ? "second.txt" : "combined.pdf");
                JsonSerializer.SerializeToUtf8Bytes(sent, MessagingJson.Options)
                    .ShouldBe(await File.ReadAllBytesAsync(row.OutputPath!));
            }
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(0);
            producer.SentReplies.Count.ShouldBe(cases.Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed record Case(string Name, string Replacement, byte[] SourceBytes,
        Dictionary<string, (string Mime, byte[] Bytes)> Output);
}
