using System.Data.Common;
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
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class ChannelOutboundMetadataRepairTests
{
    [Test]
    public async Task C519_Acceptance_and_all_settlements_commit_together()
    {
        foreach (var kind in new[] { "main", "machine" })
        {
            await using var w = await World.CreateAsync();
            var id = await w.CaptureAsync(kind);
            await w.TickAsync();
            var fault = new WriteFault("SessionQueuedMessages");
            await w.TickAsync(fault);
            fault.Fired.ShouldBeTrue();
            var row = await w.LoadAsync(id);
            row.State.ShouldBe(ChannelOutboundDeliveryState.Publishing);
            row.PublishedAt.ShouldBeNull();
            (await w.MembersAsync(id)).ShouldAllBe(m => m.ChannelReplySettledAt == null);
            w.Clock.Advance(TimeSpan.FromSeconds(301));
            await w.TickAsync();
            (await w.LoadAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
            await w.TickAsync();
            w.Producer.Accepted.ShouldHaveSingleItem();
            (await w.ChannelAsync()).LastReplyAt.ShouldBeNull();
        }
    }

    [Test]
    public async Task C519_Projection_repair_never_reenters_producer()
    {
        foreach (var faultKind in new[] { "ChatChannels", "AgentTasks", "files" })
        {
            await using var w = await World.CreateAsync();
            var task = await w.BundleAsync("complete");
            var id = await w.CaptureAsync("main", [task]);
            await w.TickAsync();
            var fault = new WriteFault(faultKind);
            w.Producer.AfterAcceptance = () => { w.Files.RefuseReads = faultKind == "files"; return Task.CompletedTask; };
            await w.TickAsync(fault);
            (faultKind == "files" ? w.Files.Fired : fault.Fired).ShouldBeTrue(faultKind);
            var accepted = await w.LoadAsync(id);
            accepted.State.ShouldBe(ChannelOutboundDeliveryState.Published);
            accepted.MetadataAppliedAt.ShouldBeNull();
            (await w.MembersAsync(id)).ShouldAllBe(m => m.ChannelReplySettledAt == accepted.PublishedAt);
            (await w.ChannelAsync()).LastReplyAt.ShouldBeNull();
            (await w.TaskAsync(task)).DeliverableDeliveredAt.ShouldBeNull();
            Directory.Delete((await w.TaskAsync(task)).DeliverableBundleDir!, true);
            w.Files.RefuseReads = false;
            await w.TickAsync(); // New context/pump, same durable journal and frozen snapshot.
            await w.TickAsync();
            (await w.LoadAsync(id)).MetadataAppliedAt.ShouldNotBeNull();
            (await w.TaskAsync(task)).DeliverableDeliveredAt.ShouldBe(accepted.PublishedAt);
            (await w.ChannelAsync()).LastReplyPreview.ShouldBe("accepted main");
            w.Producer.Accepted.ShouldHaveSingleItem().Attachments.ShouldHaveSingleItem()
                .Content.ShouldBe("frozen source"u8.ToArray());
        }
    }

    [Test]
    public async Task C519_Projection_repair_cannot_regress_preview()
    {
        await using var w = await World.CreateAsync();
        var old = await w.CaptureAsync("main");
        await w.TickAsync();
        var fault = new WriteFault("ChatChannels");
        await w.TickAsync(fault);
        fault.Fired.ShouldBeTrue();
        w.Clock.Advance(TimeSpan.FromSeconds(1));
        var newer = await w.CaptureAsync("main", text: "newer preview");
        await w.TickAsync(fault); // Keep the old projection pending while materializing the new reply.
        await w.TickAsync(fault);
        await using (var db = w.Db())
            await new ChannelOutboundMetadataRepair(db, w.Files, w.Clock).RepairAsync(newer, default);
        await w.TickAsync();
        (await w.LoadAsync(old)).MetadataAppliedAt.ShouldNotBeNull();
        (await w.ChannelAsync()).LastReplyAt.ShouldBe((await w.LoadAsync(newer)).PublishedAt);
        (await w.ChannelAsync()).LastReplyPreview.ShouldBe("newer preview");
        w.Producer.Accepted.Count.ShouldBe(2);
    }

    [Test]
    public async Task C519_Bundle_stamp_uses_frozen_complete_actual_payload()
    {
        foreach (var shape in new[] { "complete", "missing", "wrong-hash", "over-cap", "partial-zip", "complete-zip" })
        {
            await using var w = await World.CreateAsync();
            var task = await w.BundleAsync(shape);
            var id = await w.CaptureAsync("main", [task], omitted: shape == "missing" ? task : null,
                cap: shape == "over-cap" ? 1 : 1024 * 1024);
            await w.TickAsync();
            // Prove repair never consults current source/manifest after materialization.
            Directory.Delete((await w.TaskAsync(task)).DeliverableBundleDir!, true);
            await w.TickAsync();
            (await w.LoadAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.Published, shape);
            (await w.LoadAsync(id)).MetadataAppliedAt.ShouldNotBeNull(shape);
            (await w.TaskAsync(task)).DeliverableDeliveredAt.HasValue.ShouldBe(shape is "complete" or "complete-zip", shape);
            w.Producer.Accepted.ShouldHaveSingleItem();
        }
    }

    [Test]
    public async Task C519_Projection_repairs_every_implied_task()
    {
        foreach (var kind in new[] { "main", "machine" })
        {
            await using var w = await World.CreateAsync();
            var tasks = new[] { await w.BundleAsync("complete"), await w.BundleAsync("complete"), await w.BundleAsync("complete") };
            var id = await w.CaptureAsync(kind, tasks, omitted: tasks[2]);
            await w.TickAsync();
            foreach (var task in tasks) Directory.Delete((await w.TaskAsync(task)).DeliverableBundleDir!, true);
            await w.TickAsync();
            foreach (var task in tasks.Take(2)) (await w.TaskAsync(task)).DeliverableDeliveredAt.ShouldBe((await w.LoadAsync(id)).PublishedAt);
            (await w.TaskAsync(tasks[2])).DeliverableDeliveredAt.ShouldBeNull();
            (await w.MembersAsync(id)).Count.ShouldBe(3);
            (await w.MembersAsync(id)).ShouldAllBe(m => m.ChannelReplySettledAt != null);
            w.Producer.Accepted.ShouldHaveSingleItem().Attachments.Count.ShouldBe(2);
        }
    }

    [Test]
    public async Task C519_Fair_bounded_send_and_repair_budgets_are_independent()
    {
        await using var w = await World.CreateAsync();
        var id = await w.CaptureAsync("main");
        await w.TickAsync();
        // 321 irreparable accepted rows ahead of the valid projection cannot monopolize repair.
        await using (var db = w.Db())
        {
            for (var i = 0; i < 321; i++) db.ChannelOutboundDeliveries.Add(new()
            {
                Id = Guid.NewGuid(), SourceKey = "old-" + i, ChannelId = w.Channel, ProjectId = w.Project,
                InboundAgentId = w.Agent, SourceSessionId = w.Session, State = ChannelOutboundDeliveryState.Published,
                PublishedAt = w.Now.AddDays(-1), CreatedAt = w.Now.AddDays(-2).AddSeconds(i),
                InputPath = "/missing/" + i, InputSha256 = "missing", SendKind = "main"
            });
            await db.SaveChangesAsync();
        }
        w.Files.Reads = 0;
        await w.TickAsync();
        w.Files.Reads.ShouldBe(321); // One due send + the 320-row repair budget.
        (await w.LoadAsync(id)).State.ShouldBe(ChannelOutboundDeliveryState.Published);
        (await w.LoadAsync(id)).MetadataAppliedAt.ShouldBeNull();
        w.Files.Reads = 0;
        await w.TickAsync();
        w.Files.Reads.ShouldBe(2);
        (await w.LoadAsync(id)).MetadataAppliedAt.ShouldNotBeNull();
        w.Producer.Accepted.ShouldHaveSingleItem();
    }

    private sealed class WriteFault(string table) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }
        private void Check(DbCommand command)
        {
            if (command.CommandText.Contains("UPDATE \"" + table + "\"", StringComparison.Ordinal))
            { Fired = true; throw new IOException("injected " + table + " update fault"); }
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }
    }

    private sealed class Producer : IAntiphonMessagingProducer
    {
        public List<ChannelReply> Accepted { get; } = [];
        public Func<Task>? AfterAcceptance { get; set; }
        public async Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        { Accepted.Add(reply); if (AfterAcceptance is { } callback) await callback(); }
    }

    private sealed class Store(ChannelOutboundFileStore inner) : IChannelOutboundFileStore
    {
        public bool RefuseReads { get; set; }
        public bool Fired { get; private set; }
        public int Reads { get; set; }
        public Task<ChannelReply> ReadReplyAsync(string path, string hash, CancellationToken ct)
        { Reads++; if (RefuseReads) { Fired = true; throw new IOException("injected snapshot read fault"); } return inner.ReadReplyAsync(path, hash, ct); }
        public Task<ChannelOutboundSnapshot> StageAsync(Guid id, ChannelReply reply, CancellationToken ct, string? manifest = null) => inner.StageAsync(id, reply, ct, manifest);
        public Task<ChannelOutboundSealed> ValidateAndSealAsync(Guid id, string path, string hash, int max, CancellationToken ct) => inner.ValidateAndSealAsync(id, path, hash, max, ct);
        public Task<ChannelOutboundMaterialized?> TryAdoptAsync(Guid id, string capture, CancellationToken ct) => inner.TryAdoptAsync(id, capture, ct);
        public Task<ChannelOutboundMaterialized> StageCapturedAsync(Guid id, string capture, ChannelReplyPrepared prepared, CancellationToken ct) => inner.StageCapturedAsync(id, capture, prepared, ct);
    }

    private sealed class World(IsolatedTestSchema schema, string root) : IAsyncDisposable
    {
        public Guid Project { get; } = Guid.NewGuid(); public Guid Board { get; } = Guid.NewGuid();
        public Guid Agent { get; } = Guid.NewGuid(); public Guid Channel { get; } = Guid.NewGuid(); public Guid Session { get; } = Guid.NewGuid();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero));
        public DateTime Now => Clock.GetUtcNow().UtcDateTime;
        public Producer Producer { get; } = new();
        public Store Files { get; } = new(new ChannelOutboundFileStore(Path.Combine(root, "store")));
        public ChannelOutboundSettings Settings { get; } = new() { UnifiedRecoveryEnabled = true };
        private readonly ChannelOutboundWorkCursor _cursor = new();
        private long _sequence;
        private string Root => root;
        public AppDbContext Db(IInterceptor? interceptor = null)
        {
            var builder = new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            return new(builder.Options);
        }
        public static async Task<World> CreateAsync()
        {
            var w = new World(await TestDbFixture.CreateIsolatedSchemaAsync(), Directory.CreateTempSubdirectory("c519-metadata-").FullName);
            await using var db = w.Db();
            db.Projects.Add(new() { Id = w.Project, Name = "metadata" });
            db.Boards.Add(new() { Id = w.Board, ProjectId = w.Project, Name = "metadata" });
            db.Agents.Add(new() { Id = w.Agent, BoardId = w.Board, Name = "inbound", Slug = "inbound", WorkingDirectory = w.Root });
            db.AgentSessions.Add(new() { Id = w.Session, Cwd = w.Root });
            db.ChatChannels.Add(new() { Id = w.Channel, Provider = "fake", ExternalId = w.Channel.ToString("N"), AgentId = w.Agent, Enabled = true });
            await db.SaveChangesAsync(); return w;
        }
        public async Task<Guid> BundleAsync(string shape)
        {
            var id = Guid.NewGuid(); var dir = Path.Combine(root, id.ToString("N")); Directory.CreateDirectory(dir);
            var bytes = "frozen source"u8.ToArray();
            var members = new List<DeliverableBundleService.SourceMember>();
            if (shape.EndsWith("zip", StringComparison.Ordinal))
            {
                using (var zip = new ZipArchive(File.Create(Path.Combine(dir, "bundle-sources.zip")), ZipArchiveMode.Create))
                {
                    using var first = zip.CreateEntry("source.md").Open(); first.Write(bytes);
                }
                members.Add(new("source.md", "bundle-sources.zip", "source.md", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
                if (shape == "partial-zip") members.Add(new("missing.md", "bundle-sources.zip", "missing.md", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
            }
            else
            {
                await File.WriteAllBytesAsync(Path.Combine(dir, "source.md"), bytes);
                members.Add(new("source.md", "source.md", null, bytes.Length, shape == "wrong-hash" ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(bytes))));
            }
            await File.WriteAllTextAsync(Path.Combine(dir, DeliverableBundleService.SourceManifestName), JsonSerializer.Serialize(
                new DeliverableBundleService.SourceManifest(1, true, members, []), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await using var db = Db();
            db.AgentTasks.Add(new() { Id = id, RootTaskId = id, ProjectId = Project, AgentId = Agent,
                Title = "source", Goal = "source", WorkingDirectory = root, RepoPath = root, Status = AgentTaskStatus.Succeeded,
                DeliverableBundleDir = dir, CreatedAt = Now, CompletedAt = Now });
            await db.SaveChangesAsync(); return id;
        }
        public async Task<Guid> CaptureAsync(string kind, Guid[]? tasks = null, Guid? omitted = null, long cap = 1024 * 1024, string? text = null)
        {
            tasks ??= [];
            await using var db = Db();
            var members = Enumerable.Range(0, 3).Select(i => new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = Session, Sequence = ++_sequence,
                Body = "original prompt", Origin = kind == "main" ? QueuedMessageOrigin.Channel : QueuedMessageOrigin.Check,
                Status = QueuedMessageStatus.Sent, CreatedAt = Now, SentAt = Now,
                SourceTaskId = tasks.Length > i ? tasks[i] : null
            }).ToArray();
            db.SessionQueuedMessages.AddRange(members); await db.SaveChangesAsync();
            var source = new ChannelOutboundSource(Session, _sequence * 10, _sequence * 10 + 1, _sequence * 10 + 2, kind, members.Select(m => m.Id).ToArray());
            var body = ChannelReplyPreparation.Describe(text ?? "accepted " + kind) with { BundleTaskIds = tasks.Where(t => t != omitted).ToArray() };
            return (await new ChannelOutboundService(db, Files, Producer, Options.Create(Settings), Clock).CaptureAsync(
                new ChannelReply { Channel = "fake", ConversationId = Channel.ToString("N"), ReplyHandle = "original-thread" },
                source, body, new ChannelBridgeSettings { MaxAttachmentBytes = cap }, default)).Id;
        }
        public async Task TickAsync(IInterceptor? fault = null)
        {
            await using var db = Db(fault);
            await new ChannelOutboundDeliveryPump(db, null!, Files, Producer, Options.Create(new AntiphonMessagingOptions()), Clock,
                NullLogger<ChannelOutboundDeliveryPump>.Instance, Options.Create(Settings), new ChannelReplyPreparation(new ChannelReplyAttachmentReader()),
                cursor: _cursor).TickAsync(default);
        }
        public async Task<ChannelOutboundDelivery> LoadAsync(Guid id) { await using var db = Db(); return await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == id); }
        public async Task<List<SessionQueuedMessage>> MembersAsync(Guid id) { await using var db = Db(); return await db.SessionQueuedMessages.AsNoTracking().Where(m => m.ChannelOutboundDeliveryId == id).ToListAsync(); }
        public async Task<ChatChannel> ChannelAsync() { await using var db = Db(); return await db.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == Channel); }
        public async Task<AgentTask> TaskAsync(Guid id) { await using var db = Db(); return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id); }
        public async ValueTask DisposeAsync() { await schema.DisposeAsync(); Directory.Delete(root, true); }
    }
}
