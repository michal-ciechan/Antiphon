using Antiphon.Messaging;
using Antiphon.Messaging.Client.Testing;
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

public sealed partial class ChannelOutboundDeadlineTests
{
    [Test]
    public async Task One_converter_and_two_global_seats_hold_pending_work_until_its_original_deadline()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.AddAsync(0, "first", TimeSpan.FromMinutes(3));
        var sameConverter = await fixture.AddAsync(0, "same converter", TimeSpan.FromSeconds(30));
        var second = await fixture.AddAsync(1, "second", TimeSpan.FromMinutes(3));
        var globalOverflow = await fixture.AddAsync(2, "global overflow", TimeSpan.FromSeconds(30));

        await using var db = fixture.Open();
        var pump = fixture.Pump(db);
        (await pump.TickAsync(CancellationToken.None)).ShouldBe(4);
        var rows = await db.ChannelOutboundDeliveries.AsNoTracking()
            .Where(d => new[] { first, sameConverter, second, globalOverflow }.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id);
        rows[first].State.ShouldBe(ChannelOutboundDeliveryState.Converting);
        rows[second].State.ShouldBe(ChannelOutboundDeliveryState.Converting);
        rows[sameConverter].State.ShouldBe(ChannelOutboundDeliveryState.Pending);
        rows[globalOverflow].State.ShouldBe(ChannelOutboundDeliveryState.Pending);
        rows[sameConverter].ConversionTaskId.ShouldBeNull();
        rows[globalOverflow].ConversionTaskId.ShouldBeNull();
        (await db.AgentTasks.AsNoTracking().CountAsync(t => t.OutboundDeliveryId != null)).ShouldBe(2);
        fixture.Producer.SentReplies.ShouldBeEmpty();

        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        (await pump.TickAsync(CancellationToken.None)).ShouldBe(4);
        rows = await db.ChannelOutboundDeliveries.AsNoTracking()
            .Where(d => new[] { first, sameConverter, second, globalOverflow }.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id);
        rows[sameConverter].State.ShouldBe(ChannelOutboundDeliveryState.Ready);
        rows[globalOverflow].State.ShouldBe(ChannelOutboundDeliveryState.Ready);
        rows[sameConverter].FailureReason.ShouldContain("deadline elapsed in queue");
        rows[globalOverflow].FailureReason.ShouldContain("deadline elapsed in queue");
        (await db.AgentTasks.AsNoTracking().CountAsync(t => t.OutboundDeliveryId != null)).ShouldBe(2);
        rows[first].State.ShouldBe(ChannelOutboundDeliveryState.Converting);
        rows[second].State.ShouldBe(ChannelOutboundDeliveryState.Converting);
    }

    [Test]
    public async Task Missing_and_unavailable_converter_fall_back_without_creating_a_task()
    {
        await using var fixture = await Fixture.CreateAsync();
        var missing = await fixture.AddAsync(0, "missing agent", TimeSpan.FromMinutes(1),
            converterId: Guid.NewGuid());
        var unavailable = await fixture.AddAsync(1, "unavailable workspace", TimeSpan.FromMinutes(1));
        await using (var mutate = fixture.Open())
            await mutate.Agents.Where(a => a.Id == fixture.Converters[1]).ExecuteUpdateAsync(
                s => s.SetProperty(a => a.WorkingDirectory, Path.Combine(fixture.Root, "missing")));
        await using var db = fixture.Open();
        var pump = fixture.Pump(db);
        (await pump.TickAsync(CancellationToken.None)).ShouldBe(2);
        var rows = await db.ChannelOutboundDeliveries.AsNoTracking()
            .Where(d => d.Id == missing.Id || d.Id == unavailable.Id).ToListAsync();
        rows.All(d => d.State == ChannelOutboundDeliveryState.Ready
            && d.ConversionTaskId == null && d.ConversionOutcome == "Fallback").ShouldBeTrue();
        rows.Single(d => d.Id == missing.Id).FailureReason.ShouldContain("worker unavailable");
        rows.Single(d => d.Id == unavailable.Id).FailureReason.ShouldContain("workspace or channel role");
        (await db.AgentTasks.AsNoTracking().CountAsync(t => t.OutboundDeliveryId != null)).ShouldBe(0);
        (await pump.TickAsync(CancellationToken.None)).ShouldBe(2);
        fixture.Producer.SentReplies.Select(r => r.Text).ShouldContain(t =>
            t!.Contains("missing agent") && t.Contains("original reply sent"));
        fixture.Producer.SentReplies.Select(r => r.Text).ShouldContain(t =>
            t!.Contains("unavailable workspace") && t.Contains("original reply sent"));
        (await pump.TickAsync(CancellationToken.None)).ShouldBe(0);
        fixture.Producer.SentReplies.Count.ShouldBe(2);
    }

    [Test]
    public async Task Blocked_failed_and_missing_output_keep_original_bytes_and_record_distinct_reasons()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cases = new[]
        {
            (AgentTaskStatus.Blocked, "provider authentication required", "Blocked"),
            (AgentTaskStatus.Failed, "subscription quota refused", "Failed"),
            (AgentTaskStatus.Failed, "model unavailable", "Failed"),
            (AgentTaskStatus.Succeeded, "", "result invalid"),
        };
        var ids = new List<Guid>();
        for (var i = 0; i < cases.Length; i++)
        {
            var delivery = await fixture.AddAsync(i % 3, "original " + i, TimeSpan.FromMinutes(1),
                attachment: i != 0);
            ids.Add(delivery.Id);
            await using var seed = fixture.Open();
            var taskId = Guid.NewGuid();
            await seed.ChannelOutboundDeliveries.Where(d => d.Id == delivery.Id).ExecuteUpdateAsync(s => s
                .SetProperty(d => d.State, ChannelOutboundDeliveryState.Converting)
                .SetProperty(d => d.ConversionTaskId, taskId));
            seed.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, OutboundDeliveryId = delivery.Id,
                Title = "conversion", Goal = "convert", Role = AgentTaskRole.Custom,
                Status = cases[i].Item1, FailureReason = cases[i].Item2,
                AgentId = fixture.Converters[i % 3], ProjectId = fixture.ProjectId,
                WorkingDirectory = fixture.Root, CreatedAt = fixture.Now,
            });
            await seed.SaveChangesAsync();
        }
        await using var db = fixture.Open();
        var pump = fixture.Pump(db);
        (await pump.TickAsync(CancellationToken.None)).ShouldBe(cases.Length);
        var rows = await db.ChannelOutboundDeliveries.AsNoTracking()
            .Where(d => ids.Contains(d.Id)).OrderBy(d => d.CreatedAt).ToListAsync();
        for (var i = 0; i < cases.Length; i++)
        {
            rows[i].State.ShouldBe(ChannelOutboundDeliveryState.Published);
            rows[i].ConversionOutcome.ShouldBe("Fallback");
            rows[i].FailureReason.ShouldContain(cases[i].Item3);
            if (i < 3) rows[i].FailureReason.ShouldContain(cases[i].Item2);
        }
        fixture.Producer.SentReplies.Count.ShouldBe(cases.Length);
        for (var i = 0; i < cases.Length; i++)
        {
            var sent = fixture.Producer.SentReplies[i];
            sent.Text.ShouldContain("original " + i);
            sent.Text.ShouldContain(i == 0 ? "original reply sent" : "original attachments retained");
            sent.Attachments.Count.ShouldBe(i == 0 ? 0 : 1);
            if (i != 0) sent.Attachments.Single().Content.ShouldBe(new byte[] { 0, (byte)i, 255 });
        }
        (await pump.TickAsync(CancellationToken.None)).ShouldBe(0);
        fixture.Producer.SentReplies.Count.ShouldBe(cases.Length);
    }

    [Test]
    public async Task Queued_to_working_race_preserves_the_owner_and_sends_once_at_deadline()
    {
        await using var fixture = await Fixture.CreateAsync();
        var delivery = await fixture.AddAsync(0, "racing original", TimeSpan.Zero, attachment: true);
        var taskId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using (var seed = fixture.Open())
        {
            seed.AgentSessions.Add(new AgentSession { Id = sessionId, DefinitionName = "converter",
                Cwd = fixture.Root, Status = SessionStatus.Running, CreatedAt = fixture.Now,
                StartedAt = fixture.Now, LastSeenAt = fixture.Now });
            await seed.ChannelOutboundDeliveries.Where(d => d.Id == delivery.Id).ExecuteUpdateAsync(s => s
                .SetProperty(d => d.State, ChannelOutboundDeliveryState.Converting)
                .SetProperty(d => d.ConversionTaskId, taskId));
            seed.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId,
                OutboundDeliveryId = delivery.Id, Title = "conversion", Goal = "convert",
                Role = AgentTaskRole.Custom, Status = AgentTaskStatus.Queued,
                AgentId = fixture.Converters[0], ProjectId = fixture.ProjectId,
                WorkingDirectory = fixture.Root, CreatedAt = fixture.Now });
            await seed.SaveChangesAsync();
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var db = fixture.Open();
        var pump = fixture.Pump(db);
        pump.ProbeBarrierAsync = async (name, _, ct) =>
        {
            if (name != "before-conversion-observation") return;
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        var tick = pump.TickAsync(CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using (var winner = fixture.Open())
                (await winner.AgentTasks.Where(t => t.Id == taskId && t.Status == AgentTaskStatus.Queued)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Working)
                        .SetProperty(t => t.AgentSessionId, sessionId))).ShouldBe(1);
            release.TrySetResult();
            (await tick.WaitAsync(TimeSpan.FromSeconds(15))).ShouldBe(1);
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId))
                .Status.ShouldBe(AgentTaskStatus.Working);
            (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId))
                .Status.ShouldBe(SessionStatus.Running);
            (await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == delivery.Id))
                .State.ShouldBe(ChannelOutboundDeliveryState.Published);
            fixture.Producer.SentReplies.ShouldHaveSingleItem().Attachments.Single().Content
                .ShouldBe(new byte[] { 0, 0, 255 });
            await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s =>
                s.SetProperty(t => t.Status, AgentTaskStatus.Succeeded));
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(0);
            fixture.Producer.SentReplies.Count.ShouldBe(1);
        }
        finally
        {
            release.TrySetResult();
            try { await tick.WaitAsync(TimeSpan.FromSeconds(15)); } catch { }
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly ChannelOutboundFileStore _files;
        private readonly Guid _channelId = Guid.NewGuid();
        private int _sequence;

        public string Root { get; }
        public DateTime Now { get; }
        public FakeTimeProvider Clock { get; }
        public Guid ProjectId { get; } = Guid.NewGuid();
        public Guid[] Converters { get; } = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        public FakeAntiphonMessagingClient Producer { get; } = new();

        private Fixture(IsolatedTestSchema schema, string root)
        {
            _schema = schema;
            _options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
            Root = root;
            Now = DateTime.UtcNow;
            Clock = new FakeTimeProvider(new DateTimeOffset(Now));
            _files = new ChannelOutboundFileStore(Path.Combine(root, "outbound"));
        }

        public static async Task<Fixture> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var fixture = new Fixture(schema, Directory.CreateTempSubdirectory("c0418-v11-").FullName);
            var boardId = Guid.NewGuid();
            var inboundId = Guid.NewGuid();
            await using var db = fixture.Open();
            db.Projects.Add(new Project { Id = fixture.ProjectId,
                Name = "v11-" + fixture.ProjectId.ToString("N"),
                CreatedAt = fixture.Now, UpdatedAt = fixture.Now });
            db.Boards.Add(new Board { Id = boardId, ProjectId = fixture.ProjectId,
                Name = "v11", CreatedAt = fixture.Now, UpdatedAt = fixture.Now });
            db.Agents.Add(new Agent { Id = inboundId, BoardId = boardId, Name = "inbound",
                Slug = "inbound-" + inboundId.ToString("N"), WorkingDirectory = fixture.Root });
            foreach (var converter in fixture.Converters)
                db.Agents.Add(new Agent { Id = converter, BoardId = boardId, Name = "converter",
                    Slug = "converter-" + converter.ToString("N"), WorkingDirectory = fixture.Root });
            db.ChatChannels.Add(new ChatChannel { Id = fixture._channelId, Provider = "fake",
                ExternalId = fixture._channelId.ToString("N"), AgentId = inboundId,
                CreatedAt = fixture.Now, UpdatedAt = fixture.Now });
            await db.SaveChangesAsync();
            return fixture;
        }

        public AppDbContext Open() => new(_options);

        public ChannelOutboundDeliveryPump Pump(AppDbContext db)
        {
            var tasks = new AgentTaskService(db,
                new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                Options.Create(new DelegationSettings { AllowedRoots = [Root] }),
                new MockEventBus(), new RecordingSessionStopper(), Clock,
                NullLogger<AgentTaskService>.Instance);
            return new ChannelOutboundDeliveryPump(db, new OutboundConversionTaskRunner(db, tasks),
                _files, Producer, Options.Create(new Antiphon.Messaging.Client.AntiphonMessagingOptions()),
                Clock, NullLogger<ChannelOutboundDeliveryPump>.Instance);
        }

        public async Task<ChannelOutboundDelivery> AddAsync(int converterIndex, string text,
            TimeSpan timeout, bool attachment = false, Guid? converterId = null)
        {
            var id = Guid.NewGuid();
            var sequence = _sequence++;
            var reply = new ChannelReply { Channel = "fake",
                ConversationId = _channelId.ToString("N"), Text = text,
                Attachments = attachment ? [new OutboundAttachment { Kind = AttachmentKind.File,
                    Name = text + ".md", Mime = "text/markdown",
                    Content = [0, (byte)sequence, 255] }] : [] };
            var snapshot = await _files.StageAsync(id, reply, CancellationToken.None);
            var delivery = new ChannelOutboundDelivery
            {
                Id = id, SourceKey = Guid.NewGuid().ToString("N"), ChannelId = _channelId,
                ProjectId = ProjectId, InboundAgentId = await InboundIdAsync(),
                ConverterAgentId = converterId ?? Converters[converterIndex], SourceSessionId = Guid.NewGuid(),
                SendKind = "main", ProfileName = "", PromptRevision = new string('a', 64),
                PromptText = "Convert.", Trigger = "EveryAgentReply",
                InputPath = snapshot.ReplyPath, InputSha256 = snapshot.ReplySha256,
                State = ChannelOutboundDeliveryState.Pending,
                CreatedAt = Now.AddMilliseconds(sequence), DeadlineAt = Now.Add(timeout),
            };
            await using var db = Open();
            db.ChannelOutboundDeliveries.Add(delivery);
            await db.SaveChangesAsync();
            return delivery;
        }

        private async Task<Guid> InboundIdAsync()
        {
            await using var db = Open();
            return (await db.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == _channelId)).AgentId!.Value;
        }

        public async ValueTask DisposeAsync()
        {
            await _schema.DisposeAsync();
            Directory.Delete(Root, recursive: true);
        }
    }
}
