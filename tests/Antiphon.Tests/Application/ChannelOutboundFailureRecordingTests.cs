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
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class ChannelOutboundFailureRecordingTests
{
    [Test]
    public Task C519_Loss_and_source_outcome_are_atomic() => FaultMatrixAsync("AgentIncidents");

    [Test]
    public Task C519_Loss_requires_its_alert() => FaultMatrixAsync("Alerts");

    private static async Task FaultMatrixAsync(string table)
    {
        foreach (var route in new[] { "Failed", "Preparation", "Uncertain", "TTL", "Unroutable", "Provider" })
        {
            await using var w = await World.CreateAsync();
            await w.SeedAsync(route);
            await w.FaultAsync(table, enable: true);
            await w.RunAsync(route);
            await using (var db = w.Db())
            {
                (await db.AgentIncidents.CountAsync()).ShouldBe(0, route + "/" + table);
                (await db.Alerts.CountAsync()).ShouldBe(0, route + "/" + table);
                (await db.SessionQueuedMessages.SingleAsync()).ChannelReplySettledAt.ShouldBeNull();
                if (w.Delivery is Guid id)
                {
                    var row = await db.ChannelOutboundDeliveries.SingleAsync(d => d.Id == id);
                    row.State.ShouldBe(route == "Uncertain" ? ChannelOutboundDeliveryState.Publishing
                        : route == "Preparation" ? ChannelOutboundDeliveryState.Captured : ChannelOutboundDeliveryState.Ready);
                    row.FailureReportedEpisode.ShouldBe(0);
                }
            }
            w.Producer.Notices.ShouldBe(0);
            var entries = w.Producer.Entries;
            var reads = w.Reader.Calls;
            // A fresh pump/context after lease expiry repairs only recording at a spent budget.
            w.Clock.Advance(TimeSpan.FromSeconds(301));
            await w.RunAsync(route);
            w.Producer.Entries.ShouldBe(entries);
            w.Reader.Calls.ShouldBe(reads);
            await w.FaultAsync(table, enable: false);
            w.Clock.Advance(TimeSpan.FromSeconds(301));
            await w.RunAsync(route);
            await w.AssertLossAsync(route == "Uncertain");
            w.Producer.Entries.ShouldBe(entries);
            await w.RunAsync(route);
            await w.AssertLossAsync(route == "Uncertain");
        }
    }

    [Test]
    public async Task C519_Loss_is_critical_and_identifiable()
    {
        await using var w = await World.CreateAsync();
        await w.SeedAsync("Failed"); await w.RunAsync("Failed");
        await w.AssertLossAsync();
        await using var db = w.Db();
        var incident = await db.AgentIncidents.SingleAsync();
        incident.Kind.ShouldBe(AgentIncidentKind.ChannelReplyLost);
        incident.Severity.ShouldBe(AlertSeverity.Critical);
        incident.SessionId.ShouldBe(w.Session);
        incident.AgentId.ShouldBe(w.Owner);
        incident.Message.ShouldContain(w.Source.ToString());
        incident.Message.ShouldContain(w.Delivery!.Value.ToString());
        var alert = await db.Alerts.SingleAsync();
        alert.Severity.ShouldBe(AlertSeverity.Critical);
        alert.Detail.ShouldContain(w.Source.ToString());
        alert.Detail.ShouldContain(w.Delivery.Value.ToString());
    }

    [Test]
    public async Task C519_Failure_episode_survives_history_pruning()
    {
        foreach (var route in new[] { "Failed", "Uncertain", "TTL" })
        {
            await using var w = await World.CreateAsync();
            await w.SeedAsync(route); await w.RunAsync(route);
            await w.AssertLossAsync(route == "Uncertain");
            await using (var db = w.Db()) await db.AgentIncidents.ExecuteDeleteAsync();
            await w.RunAsync(route); w.Clock.Advance(TimeSpan.FromHours(1)); await w.RunAsync(route);
            await using var check = w.Db();
            (await check.AgentIncidents.CountAsync()).ShouldBe(0);
            (await check.Alerts.CountAsync()).ShouldBe(1);
            if (w.Delivery is Guid id)
                (await check.ChannelOutboundDeliveries.SingleAsync(d => d.Id == id)).FailureReportedEpisode.ShouldBe(1);
        }
    }

    [Test]
    public async Task C519_Missing_owner_still_records_loss()
    {
        foreach (var owner in new[] { "captured", "current", "deleted", "none" })
        {
            await using var w = await World.CreateAsync();
            var route = owner == "current" || owner == "none" ? "TTL" : "Failed";
            await w.SeedAsync(route);
            await using (var db = w.Db())
            {
                if (owner == "captured")
                    await db.ChatChannels.ExecuteUpdateAsync(s => s.SetProperty(c => c.AgentId, (Guid?)null));
                if (owner is "deleted" or "none") await db.Agents.ExecuteDeleteAsync();
            }
            await w.RunAsync(route); await w.AssertLossAsync();
            await using var check = w.Db();
            var expected = owner is "deleted" or "none" ? (Guid?)null : w.Owner;
            (await check.AgentIncidents.SingleAsync()).AgentId.ShouldBe(expected);
            (await check.Alerts.SingleAsync()).AgentId.ShouldBe(expected);
        }
    }

    [Test]
    public async Task C519_Notice_failure_cannot_erase_loss()
    {
        foreach (var mode in new[] { "enabled", "disabled", "refused" })
        {
            await using var w = await World.CreateAsync();
            await w.SeedAsync("Failed");
            w.SendNotices = true; w.Producer.RefuseNotice = mode == "refused";
            w.Producer.BeforeNotice = async () => await w.AssertLossAsync();
            if (mode == "disabled")
            { await using var db = w.Db(); await db.ChatChannels.ExecuteUpdateAsync(s => s.SetProperty(c => c.Enabled, false)); }
            await w.RunAsync("Failed"); await w.AssertLossAsync();
            w.Producer.Notices.ShouldBe(mode == "disabled" ? 0 : 1);
            await using var check = w.Db();
            (await check.ChannelOutboundDeliveries.CountAsync()).ShouldBe(1);
        }
    }

    [Test]
    public async Task C519_Uncertainty_message_does_not_claim_nondelivery()
    {
        await using var w = await World.CreateAsync();
        await w.SeedAsync("Uncertain"); await w.RunAsync("Uncertain");
        await w.AssertLossAsync(uncertain: true);
        w.Producer.Accepted.ShouldBe(1);
        await using var db = w.Db();
        foreach (var message in new[] { (await db.AgentIncidents.SingleAsync()).Message, (await db.Alerts.SingleAsync()).Detail! })
        {
            message.ShouldContain("acceptance is unknown"); message.ShouldContain("may have published");
            message.ShouldNotContain("never sent"); message.ShouldNotContain("no turn");
        }
        await w.RunAsync("Uncertain"); w.Producer.Entries.ShouldBe(1);
    }

    [Test]
    public async Task C519_Acknowledged_retry_starts_a_new_failure_episode()
    {
        await using var w = await World.CreateAsync();
        await w.SeedAsync("Uncertain"); await w.RunAsync("Uncertain");
        await using (var db = w.Db()) await w.Service(db).RetryUncertainAsync(w.Delivery!.Value, true, default);
        await w.RunAsync("Uncertain"); await w.RunAsync("Uncertain");
        await using var check = w.Db();
        (await check.AgentIncidents.CountAsync()).ShouldBe(2);
        (await check.Alerts.CountAsync()).ShouldBe(2);
        var row = await check.ChannelOutboundDeliveries.SingleAsync();
        row.FailureEpisode.ShouldBe(2); row.FailureReportedEpisode.ShouldBe(2);
        row.PublicationAttempts.ShouldBe(2); w.Producer.Entries.ShouldBe(2);
    }

    private sealed class RefusingReader : IChannelReplyAttachmentReader
    {
        public int Calls;
        public Task<byte[]> ReadAttachmentAsync(string path, IReadOnlyList<string> roots, long max, CancellationToken ct)
        { Calls++; throw new IOException("injected preparation failure"); }
        public Task<string> ReadTextAsync(string path, IReadOnlyList<string> roots, long max, CancellationToken ct)
        { Calls++; throw new IOException("injected preparation failure"); }
    }

    private sealed class Producer : IAntiphonMessagingProducer
    {
        public int Entries, Accepted, Notices;
        public bool RefuseNotice;
        public Func<Task>? BeforeNotice;
        public async Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            if (reply.Text?.StartsWith("[Antiphon]", StringComparison.Ordinal) == true)
            {
                Notices++;
                if (BeforeNotice is not null) await BeforeNotice();
                if (RefuseNotice) throw new IOException("injected notice refusal");
                return;
            }
            Entries++; Accepted++;
            throw new IOException("accepted before outcome was returned");
        }
    }

    private sealed class World(IsolatedTestSchema schema, string root) : IAsyncDisposable
    {
        public Guid Project = Guid.NewGuid(), Board = Guid.NewGuid(), Owner = Guid.NewGuid(), Session = Guid.NewGuid(), Channel = Guid.NewGuid(), Source = Guid.NewGuid();
        public Guid? Delivery;
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        public DateTime Now => Clock.GetUtcNow().UtcDateTime;
        public Producer Producer { get; } = new();
        public ChannelOutboundSettings Settings { get; } = new() { UnifiedRecoveryEnabled = true };
        public ChannelOutboundFileStore Files { get; } = new(Path.Combine(root, "store"));
        public bool SendNotices;
        public RefusingReader Reader { get; } = new();
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        public ChannelOutboundService Service(AppDbContext db) => new(db, Files, Producer, Options.Create(Settings), Clock);
        public static async Task<World> CreateAsync()
        {
            var w = new World(await TestDbFixture.CreateIsolatedSchemaAsync(), Directory.CreateTempSubdirectory("c519-loss-").FullName);
            await using var db = w.Db();
            db.Projects.Add(new() { Id = w.Project, Name = "loss" });
            db.Boards.Add(new() { Id = w.Board, ProjectId = w.Project, Name = "loss" });
            db.Agents.Add(new() { Id = w.Owner, BoardId = w.Board, Name = "owner", Slug = "owner", PersistentSessionId = w.Session.ToString("D") });
            db.AgentSessions.Add(new() { Id = w.Session, Cwd = root });
            db.ChatChannels.Add(new() { Id = w.Channel, Provider = "fake", ExternalId = "chat", AgentId = w.Owner, Enabled = true });
            await db.SaveChangesAsync(); return w;
        }
        public async Task SeedAsync(string route)
        {
            await using var db = Db();
            if (route is "Failed" or "Uncertain")
            {
                Delivery = Guid.NewGuid();
                var snapshot = await Files.StageAsync(Delivery.Value, new ChannelReply { Channel = "fake", ConversationId = "chat", Text = "frozen reply" }, default);
                db.ChannelOutboundDeliveries.Add(new() { Id = Delivery.Value, SourceKey = Delivery.ToString()!, ChannelId = Channel,
                    ProjectId = Project, InboundAgentId = Owner, SourceSessionId = Session, SendKind = "main", PromptSequence = 1,
                    InputPath = snapshot.ReplyPath, InputSha256 = snapshot.ReplySha256, State = ChannelOutboundDeliveryState.Ready,
                    FailureEpisode = 1, CreatedAt = Now, DeadlineAt = Now.AddMinutes(2),
                    PublicationAttempts = route == "Failed" ? 3 : 0 });
                if (route == "Failed") File.Delete(snapshot.ReplyPath); // spent budget must not reopen even the frozen payload
            }
            db.SessionQueuedMessages.Add(new() { Id = Source, AgentSessionId = Session, Sequence = 1, Body = "original prompt",
                Origin = QueuedMessageOrigin.Channel, Status = QueuedMessageStatus.Sent, ConversationKey = route == "Unroutable" ? "fake:missing" : "fake:chat",
                CreatedAt = Now.AddDays(-1), SentAt = route == "TTL" ? Now.AddMinutes(-31) : Now, ChannelOutboundDeliveryId = Delivery });
            if (route is "Unroutable" or "Provider")
            {
                db.TranscriptEntries.Add(new() { Id = Guid.NewGuid(), AgentSessionId = Session, Sequence = 1, Kind = TranscriptKinds.UserPrompt, Text = "original prompt", Timestamp = Now });
                db.TranscriptEntries.Add(new() { Id = Guid.NewGuid(), AgentSessionId = Session, Sequence = 2, Kind = TranscriptKinds.AssistantText,
                    Text = route == "Provider" ? "connection error" : "actual answer", Timestamp = Now, IsApiError = route == "Provider" });
                db.TranscriptEntries.Add(new() { Id = Guid.NewGuid(), AgentSessionId = Session, Sequence = 3, Kind = TranscriptKinds.TurnEnd,
                    StopReason = TranscriptKinds.StopReasons.EndTurn, Timestamp = Now, IsApiError = route == "Provider",
                    ApiErrorClass = route == "Provider" ? TranscriptKinds.ApiErrorClasses.Transport : null });
            }
            await db.SaveChangesAsync();
            if (route == "Preparation")
            {
                var capture = await Service(db).CaptureAsync(new ChannelReply { Channel = "fake", ConversationId = "chat" },
                    new(Session, 1, 2, 2, "main", [Source]), ChannelReplyPreparation.Describe("reply", [Path.Combine(root, "missing.md")]), new(), default);
                Delivery = capture.Id;
                await db.ChannelOutboundDeliveries.Where(d => d.Id == Delivery).ExecuteUpdateAsync(s => s.SetProperty(d => d.PreparationAttempts, 2));
            }
        }
        public async Task RunAsync(string route)
        {
            if (Delivery is not null)
            {
                await using var db = Db();
                var recorder = new ChannelOutboundFailureRecorder(db, Clock,
                    channels: SendNotices ? new ChatChannelService(db, Clock, Producer, Options.Create(Settings), Service(db)) : null);
                var pump = new ChannelOutboundDeliveryPump(db, null!, Files, Producer, Options.Create(new AntiphonMessagingOptions()),
                    Clock, NullLogger<ChannelOutboundDeliveryPump>.Instance, Options.Create(Settings), new ChannelReplyPreparation(Reader), recorder);
                try { await pump.TickAsync(default); }
                catch (ChannelOutboundFailureRecordingException) { /* Assert the database rollback independently. */ }
                return;
            }
            var services = new ServiceCollection();
            services.AddScoped(_ => Db()); services.AddScoped(sp => Service(sp.GetRequiredService<AppDbContext>()));
            await using var provider = services.BuildServiceProvider();
            var dispatcher = new ChannelReplyDispatcher(provider.GetRequiredService<IServiceScopeFactory>(), Producer,
                Options.Create(new ChannelBridgeSettings()), Clock, NullLogger<ChannelReplyDispatcher>.Instance);
            if (route == "TTL") await dispatcher.SweepStaleCorrelationsAsync(default);
            else await dispatcher.OnTurnEndAsync(Session, default);
        }
        public async Task FaultAsync(string table, bool enable)
        {
            (table is "AgentIncidents" or "Alerts").ShouldBeTrue();
            await using var db = Db();
            if (enable)
            {
                await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION c519_reject_loss() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected loss insert failure'; END $$");
                await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER c519_loss BEFORE INSERT ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION c519_reject_loss()");
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync($"DROP TRIGGER c519_loss ON \"{table}\"");
                await db.Database.ExecuteSqlRawAsync("DROP FUNCTION c519_reject_loss()");
            }
        }
        public async Task AssertLossAsync(bool uncertain = false)
        {
            await using var db = Db();
            (await db.AgentIncidents.CountAsync()).ShouldBe(1);
            (await db.Alerts.CountAsync()).ShouldBe(1);
            var source = await db.SessionQueuedMessages.SingleAsync();
            if (uncertain) source.ChannelReplySettledAt.ShouldBeNull(); else source.ChannelReplySettledAt.ShouldNotBeNull();
            if (Delivery is Guid id)
            {
                var delivery = await db.ChannelOutboundDeliveries.SingleAsync(d => d.Id == id);
                delivery.State.ShouldBe(uncertain ? ChannelOutboundDeliveryState.PublishUncertain : ChannelOutboundDeliveryState.Failed);
                delivery.FailureReportedEpisode.ShouldBe(1);
                delivery.PublishedAt.ShouldBeNull();
            }
        }
        public async ValueTask DisposeAsync() { await schema.DisposeAsync(); Directory.Delete(root, true); }
    }
}
