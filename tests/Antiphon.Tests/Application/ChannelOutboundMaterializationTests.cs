using System.Security.Cryptography;
using System.Text;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CP-6 is scheduled after S8: that slice adds the atomic loss/alert oracles to
// the failure cases here. S3 delivers materialization without activating it.
[Category("Integration")]
[NotInParallel]
public sealed class ChannelOutboundMaterializationTests
{
    [Test]
    public async Task C519_Malformed_capture_is_visible_failure()
    {
        await using var w = await World.CreateAsync();
        foreach (var malformed in new[] { "{", "{\"version\":99}", new string('x', ChannelReplyPreparation.MaxCaptureBytes + 1) })
        {
            var d = await w.CaptureAsync();
            await using (var db = w.Db())
            { await db.ChannelOutboundDeliveries.Where(x => x.Id == d.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CaptureJson, malformed)); }
            await w.TickAsync();
            var stored = await w.LoadAsync(d.Id);
            stored.State.ShouldBe(ChannelOutboundDeliveryState.Failed);
            stored.FailureReason.ShouldNotBeNullOrEmpty();
            stored.InputPath.ShouldBeEmpty();
        }
        w.Producer.SentReplies.ShouldBeEmpty();
    }

    [Test]
    public async Task C519_Complete_stage_requires_matching_capture()
    {
        await using var w = await World.CreateAsync();
        var d = await w.CaptureAsync();
        var prepared = await w.Preparation.PrepareAsync(d, default);
        await w.Files.StageCapturedAsync(d.Id, d.CaptureJson!, prepared, default);
        await Should.ThrowAsync<InvalidDataException>(() => w.Files.TryAdoptAsync(d.Id,
            ChannelReplyPreparation.Serialize(ChannelReplyPreparation.Deserialize(d.CaptureJson!) with
            { Body = ChannelReplyPreparation.Describe("different captured reply") }), default));
        var otherId = Guid.NewGuid();
        Directory.Move(Path.Combine(w.StoreRoot, d.Id.ToString("N")), Path.Combine(w.StoreRoot, otherId.ToString("N")));
        await Should.ThrowAsync<InvalidDataException>(() => w.Files.TryAdoptAsync(otherId, d.CaptureJson!, default));
        Directory.Move(Path.Combine(w.StoreRoot, otherId.ToString("N")), Path.Combine(w.StoreRoot, d.Id.ToString("N")));
        (await w.Files.TryAdoptAsync(d.Id, d.CaptureJson!, default)).ShouldNotBeNull();
        w.Producer.SentReplies.ShouldBeEmpty();
    }

    [Test]
    public async Task C519_Complete_stage_requires_valid_file_hashes()
    {
        await using var w = await World.CreateAsync();
        foreach (var file in new[] { "reply.json", "request.json", "input/attachment-001.md" })
        {
            var d = await w.CaptureAsync(attachment: true);
            await w.Files.StageCapturedAsync(d.Id, d.CaptureJson!, await w.Preparation.PrepareAsync(d, default), default);
            var path = Path.Combine(w.StoreRoot, d.Id.ToString("N"), file);
            var original = await File.ReadAllBytesAsync(path);
            await File.WriteAllTextAsync(path, "tampered retained bytes");
            await Should.ThrowAsync<InvalidDataException>(() => w.Files.TryAdoptAsync(d.Id, d.CaptureJson!, default));
            await File.WriteAllBytesAsync(path, original);
            (await w.Files.TryAdoptAsync(d.Id, d.CaptureJson!, default)).ShouldNotBeNull();
        }
        w.Producer.SentReplies.ShouldBeEmpty();
    }

    [Test]
    public async Task C519_Complete_stage_is_adopted_without_reopening_sources()
    {
        foreach (var kind in new[] { "main", "machine", "trailing" })
        {
            await using var w = await World.CreateAsync();
            var d = await w.CaptureAsync(attachment: true, kind: kind);
            await w.Files.StageCapturedAsync(d.Id, d.CaptureJson!, await w.Preparation.PrepareAsync(d, default), default);
            File.Delete(w.SourcePath);
            await w.TickAsync(reader: new RefusingReader());
            var stored = await w.LoadAsync(d.Id);
            stored.State.ShouldBe(ChannelOutboundDeliveryState.Ready);
            var reply = await w.Files.ReadReplyAsync(stored.InputPath, stored.InputSha256, default);
            reply.Attachments.Single().Content.ShouldBe("original attachment"u8.ToArray());
            reply.ReplyHandle.ShouldBe("original-native-thread");
            w.Producer.SentReplies.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task C519_Frozen_prompt_and_policy_survive_restart()
    {
        await using var w = await World.CreateAsync();
        var d = await w.CaptureAsync(profile: true);
        await w.TickAsync();
        var first = await w.LoadAsync(d.Id);
        first.State.ShouldBe(ChannelOutboundDeliveryState.Pending);
        first.PromptText.ShouldBe("original converter prompt");
        await File.WriteAllTextAsync(w.PromptPath, "edited converter prompt");
        w.Settings.Profiles["convert"].TimeoutSeconds = 300;
        w.Settings.Profiles["convert"].MaxPending = 12;
        var next = await w.CaptureAsync(profile: true);
        await w.TickAsync();
        var frozen = await w.LoadAsync(d.Id);
        frozen.PromptText.ShouldBe("original converter prompt");
        frozen.PromptRevision.ShouldBe(Hash("original converter prompt"));
        frozen.DeadlineAt.ShouldBe(first.DeadlineAt);
        frozen.MaxPending.ShouldBe(8);
        (await w.LoadAsync(next.Id)).PromptText.ShouldBe("edited converter prompt");
    }

    [Test]
    public async Task C519_Preparation_budget_survives_restart()
    {
        await using var w = await World.CreateAsync();
        var d = await w.CaptureAsync(attachment: true);
        var reader = new RefusingReader();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await w.TickAsync(reader);
            var row = await w.LoadAsync(d.Id);
            row.PreparationAttempts.ShouldBe(attempt);
            row.PublicationAttempts.ShouldBe(0);
            row.State.ShouldBe(attempt == 3 ? ChannelOutboundDeliveryState.Failed : ChannelOutboundDeliveryState.Captured);
            w.Clock.Advance(TimeSpan.FromSeconds(30));
        }
        await w.TickAsync(reader);
        reader.Calls.ShouldBe(3);
        w.Producer.SentReplies.ShouldBeEmpty();
    }

    [Test]
    public async Task C519_Preparation_deadline_uses_original_obligation()
    {
        await using var w = await World.CreateAsync();
        foreach (var offset in new[] { -1, 0, 1 })
        {
            var d = await w.CaptureAsync();
            await using (var db = w.Db())
                await db.ChannelOutboundDeliveries.Where(x => x.Id == d.Id).ExecuteUpdateAsync(s =>
                    s.SetProperty(x => x.PreparationDeadlineAt, w.Clock.GetUtcNow().UtcDateTime.AddSeconds(-offset)));
            await w.TickAsync();
            (await w.LoadAsync(d.Id)).State.ShouldBe(offset < 0 ? ChannelOutboundDeliveryState.Ready : ChannelOutboundDeliveryState.Failed);
        }
    }

    [Test]
    public async Task C519_Revocation_prevents_converter_creation()
    {
        await using var w = await World.CreateAsync();
        var d = await w.CaptureAsync(profile: true);
        w.Settings.Profiles.Clear();
        File.Delete(w.PromptPath);
        await w.TickAsync();
        var row = await w.LoadAsync(d.Id);
        row.State.ShouldBe(ChannelOutboundDeliveryState.Ready);
        row.ConversionOutcome.ShouldBe("Revoked");
        row.ConversionTaskId.ShouldBeNull();
        await using var db = w.Db();
        (await db.AgentTasks.CountAsync(t => t.OutboundDeliveryId == d.Id)).ShouldBe(0);
        (await w.Files.ReadReplyAsync(row.InputPath, row.InputSha256, default)).Text.ShouldBe("original reply");
    }

    [Test]
    public async Task C519_Concurrent_materialization_respects_max_pending()
    {
        await using var w = await World.CreateAsync();
        w.Settings.Profiles["convert"].MaxPending = 1;
        var first = await w.CaptureAsync(profile: true);
        var second = await w.CaptureAsync(profile: true);
        var staged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothStaged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var db1 = w.Db();
        await using var db2 = w.Db();
        var p1 = w.Pump(db1);
        var p2 = w.Pump(db2);
        p1.ProbeBarrierAsync = async (at, _, ct) =>
        { if (at == "captured-staged") { staged.TrySetResult(); await bothStaged.Task.WaitAsync(TimeSpan.FromSeconds(15), ct); } };
        p2.ProbeBarrierAsync = (at, _, _) => { if (at == "captured-staged") bothStaged.TrySetResult(); return Task.CompletedTask; };
        var run1 = p1.TickAsync(default);
        await staged.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Task.WhenAll(run1, p2.TickAsync(default)).WaitAsync(TimeSpan.FromSeconds(30));
        var rows = new[] { await w.LoadAsync(first.Id), await w.LoadAsync(second.Id) };
        rows.Count(r => r.State == ChannelOutboundDeliveryState.Pending).ShouldBe(1);
        rows.Single(r => r.State == ChannelOutboundDeliveryState.Ready).ConversionOutcome.ShouldBe("QueueOverflow");
    }

    [Test]
    public async Task C519_Converter_capacity_is_serialized()
    {
        await using var w = await World.CreateAsync();
        var active = await w.CaptureAsync(profile: true);
        await w.TickAsync();
        await w.TickAsync();
        (await w.LoadAsync(active.Id)).State.ShouldBe(ChannelOutboundDeliveryState.Converting);
        var next = await w.CaptureAsync(profile: true);
        await w.TickAsync();
        await w.TickAsync();
        (await w.LoadAsync(next.Id)).State.ShouldBe(ChannelOutboundDeliveryState.Pending);
        (await w.LoadAsync(next.Id)).ConversionTaskId.ShouldBeNull();
    }

    [Test]
    public async Task C519_Global_conversion_capacity_is_bounded()
    {
        await using var w = await World.CreateAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            await w.AddConverterAsync(i);
            var d = await w.CaptureAsync(profile: true);
            ids.Add(d.Id);
            await w.TickAsync(); await w.TickAsync();
        }
        (await w.LoadAsync(ids[0])).State.ShouldBe(ChannelOutboundDeliveryState.Converting);
        (await w.LoadAsync(ids[1])).State.ShouldBe(ChannelOutboundDeliveryState.Converting);
        (await w.LoadAsync(ids[2])).State.ShouldBe(ChannelOutboundDeliveryState.Pending);
        (await w.LoadAsync(ids[2])).ConversionTaskId.ShouldBeNull();
    }

    [Test]
    public async Task C519_Conversion_deadline_is_not_retry_time()
    {
        await using var w = await World.CreateAsync();
        var d = await w.CaptureAsync(profile: true);
        await w.TickAsync();
        var original = await w.LoadAsync(d.Id);
        w.Clock.Advance(TimeSpan.FromSeconds(120));
        await w.TickAsync();
        var row = await w.LoadAsync(d.Id);
        row.State.ShouldBe(ChannelOutboundDeliveryState.Ready);
        row.DeadlineAt.ShouldBe(original.DeadlineAt);
        row.ConversionTaskId.ShouldBeNull();
        row.ConversionOutcome.ShouldBe("Fallback");
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private sealed class RefusingReader : IChannelReplyAttachmentReader
    {
        public int Calls { get; private set; }
        public Task<byte[]> ReadAttachmentAsync(string path, IReadOnlyList<string> roots, long max, CancellationToken ct)
        { Calls++; throw new IOException("injected source refusal"); }
        public Task<string> ReadTextAsync(string path, IReadOnlyList<string> roots, long max, CancellationToken ct)
        { Calls++; throw new IOException("injected text refusal"); }
    }

    private sealed class World(IsolatedTestSchema schema, string root) : IAsyncDisposable
    {
        public string StoreRoot => Path.Combine(root, "outbound");
        public string SourcePath => Path.Combine(root, "source.md");
        public string PromptPath => Path.Combine(root, "convert.md");
        public ChannelOutboundFileStore Files { get; } = new(Path.Combine(root, "outbound"));
        public ChannelReplyPreparation Preparation { get; } = new(new ChannelReplyAttachmentReader());
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        public FakeAntiphonMessagingClient Producer { get; } = new();
        public ChannelOutboundSettings Settings { get; } = new();
        private readonly Guid _project = Guid.NewGuid(), _board = Guid.NewGuid(), _inbound = Guid.NewGuid(), _session = Guid.NewGuid();
        private Guid _channel = Guid.NewGuid();
        private string _profileName = "convert";
        private long _prompt = 10;
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        public static async Task<World> CreateAsync()
        {
            var w = new World(await TestDbFixture.CreateIsolatedSchemaAsync(), Directory.CreateTempSubdirectory("c519-materialize-").FullName);
            await File.WriteAllTextAsync(w.PromptPath, "original converter prompt");
            await using var db = w.Db();
            db.Projects.Add(new Project { Id = w._project, Name = "materialization" });
            db.Boards.Add(new Board { Id = w._board, ProjectId = w._project, Name = "materialization" });
            db.Agents.Add(new Agent { Id = w._inbound, BoardId = w._board, Name = "inbound", Slug = "inbound", WorkingDirectory = Path.GetDirectoryName(w.SourcePath)! });
            db.AgentSessions.Add(new AgentSession { Id = w._session, Cwd = Path.GetDirectoryName(w.SourcePath)! });
            db.ChatChannels.Add(new ChatChannel { Id = w._channel, Provider = "slack", ExternalId = w._channel.ToString("N"), AgentId = w._inbound, Enabled = true });
            await db.SaveChangesAsync();
            await w.AddConverterAsync(0);
            return w;
        }
        public async Task AddConverterAsync(int index)
        {
            await using var db = Db();
            var id = Guid.NewGuid();
            if (index > 0)
            {
                _channel = Guid.NewGuid();
                _profileName = "convert-" + index;
                db.ChatChannels.Add(new ChatChannel { Id = _channel, Provider = "slack", ExternalId = _channel.ToString("N"), AgentId = _inbound, Enabled = true });
            }
            db.Agents.Add(new Agent { Id = id, BoardId = _board, Name = "converter " + index, Slug = "converter-" + id.ToString("N"), WorkingDirectory = root });
            await db.SaveChangesAsync();
            Settings.Profiles[_profileName] = new() { ProjectId = _project, AgentId = id, PromptFile = "convert.md", Trigger = ChannelOutboundTrigger.EveryAgentReply };
        }
        public async Task<ChannelOutboundDelivery> CaptureAsync(bool attachment = false, bool profile = false, string kind = "main")
        {
            await File.WriteAllTextAsync(SourcePath, "original attachment");
            await using var db = Db();
            var channel = await db.ChatChannels.SingleAsync(c => c.Id == _channel);
            channel.OutboundAgentProfile = profile ? _profileName : null;
            await db.SaveChangesAsync();
            var service = new ChannelOutboundService(db, Files, Producer, Options.Create(Settings), Clock);
            var prompt = _prompt += 10;
            Guid? rootId = null;
            var route = new ChannelReply { Channel = "slack", ConversationId = _channel.ToString("N"), ReplyHandle = "original-native-thread" };
            if (kind == "trailing")
                rootId = (await service.CaptureAsync(route, new(_session, prompt, prompt + 1, prompt + 2, "main", []), ChannelReplyPreparation.Describe("root"), new(), default)).Id;
            return await service.CaptureAsync(route, new(_session, prompt, prompt + (rootId == null ? 1 : 3), prompt + (rootId == null ? 2 : 4), kind, []),
                ChannelReplyPreparation.Describe("original reply", attachment ? [SourcePath] : []), new(), default, rootId);
        }
        public ChannelOutboundDeliveryPump Pump(AppDbContext db, IChannelReplyAttachmentReader? reader = null)
        {
            var tasks = new AgentTaskService(db, new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                Options.Create(new DelegationSettings { AllowedRoots = [root] }), new MockEventBus(), new RecordingSessionStopper(),
                Clock, NullLogger<AgentTaskService>.Instance);
            return new(db, new OutboundConversionTaskRunner(db, tasks), Files, Producer, Options.Create(new AntiphonMessagingOptions()),
                Clock, NullLogger<ChannelOutboundDeliveryPump>.Instance, Options.Create(Settings), reader == null ? Preparation : new(reader));
        }
        public async Task TickAsync(IChannelReplyAttachmentReader? reader = null)
        { await using var db = Db(); await Pump(db, reader).TickAsync(default); }
        public async Task<ChannelOutboundDelivery> LoadAsync(Guid id)
        { await using var db = Db(); return await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == id); }
        public async ValueTask DisposeAsync()
        { await schema.DisposeAsync(); Directory.Delete(root, true); }
    }
}
