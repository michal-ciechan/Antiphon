using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class ExpectationEscalationTests
{
    [Test]
    public async Task C650_Unanswered_nudge_pages_exact_channel_at_deadline()
    {
        await using var f = await Fixture.CreateAsync();
        await f.MarkDueAsync();
        var sent = await f.PublishAsync();
        sent.ShouldBe(1);
        f.Producer.Sent.Count.ShouldBe(1);
        var reply = f.Producer.Sent.Single();
        reply.Channel.ShouldBe("telegram");
        reply.ConversationId.ShouldBe("c650-operator-" + f.World.Directive.OperatorChannelId.ToString("N"));
        reply.ReplyHandle.ShouldBeNull();
        reply.Text.ShouldContain(f.Nudge.Id.ToString("D"));
        (await f.ReloadAsync()).OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Published);
    }

    [Test]
    public async Task C650_Unavailable_or_unsafe_recipient_pages_immediately()
    {
        await using var f = await Fixture.CreateAsync();
        await f.MarkDueAsync(ExpectationAttemptState.Refused);
        (await f.PublishAsync()).ShouldBe(1);
        f.Producer.Sent.Single().Text.ShouldContain("Prompt=Refused");
    }

    [Test]
    public async Task C650_Broker_failure_retries_same_frozen_page_after_restart()
    {
        await using var f = await Fixture.CreateAsync();
        await f.MarkDueAsync();
        f.Producer.FailNext = true;
        (await f.PublishAsync()).ShouldBe(0);
        var failed = await f.ReloadAsync();
        failed.OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Due);
        failed.OperatorPageBody.ShouldNotBeNull();
        failed.OperatorNextAttemptAt.ShouldBe(f.Clock.GetUtcNow().UtcDateTime.AddMinutes(1));
        (await f.PublishAsync()).ShouldBe(0);
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        f.Producer.FailNext = true;
        (await f.PublishAsync()).ShouldBe(0);
        (await f.ReloadAsync()).OperatorNextAttemptAt.ShouldBe(f.Clock.GetUtcNow().UtcDateTime.AddMinutes(5));
        f.Clock.Advance(TimeSpan.FromMinutes(5));
        f.Producer.FailNext = true;
        (await f.PublishAsync()).ShouldBe(0);
        (await f.ReloadAsync()).OperatorNextAttemptAt.ShouldBe(f.Clock.GetUtcNow().UtcDateTime.AddMinutes(15));
        f.Clock.Advance(TimeSpan.FromMinutes(15));
        (await f.PublishAsync()).ShouldBe(1);
        var accepted = await f.ReloadAsync();
        accepted.OperatorPageBody.ShouldBe(failed.OperatorPageBody);
        accepted.OperatorPageDigest.ShouldBe(failed.OperatorPageDigest);
        accepted.OperatorPublicationOrdinal.ShouldBe(failed.OperatorPublicationOrdinal);
    }

    [Test]
    public async Task C650_Accepted_but_unstamped_page_recovers_with_same_identity()
    {
        await using var f = await Fixture.CreateAsync();
        await f.MarkDueAsync();
        var first = new ChannelReply { Channel = "telegram", ConversationId = "test", Text = "pre-crash" };
        f.Producer.Sent.Add(first);
        var frozen = $"[expectation-operator:{f.Nudge.Id:D}:1] frozen page";
        await using (var db = f.World.Db())
            await db.ExpectationNudges.Where(n => n.Id == f.Nudge.Id).ExecuteUpdateAsync(u => u
                .SetProperty(n => n.OperatorPageProvider, "telegram")
                .SetProperty(n => n.OperatorPageConversationId,
                    "c650-operator-" + f.World.Directive.OperatorChannelId.ToString("N"))
                .SetProperty(n => n.OperatorPageBody, frozen)
                .SetProperty(n => n.OperatorPageDigest, ExpectationDirectiveDigest.HashUtf8(frozen))
                .SetProperty(n => n.OperatorPublicationOrdinal, 1)
                .SetProperty(n => n.OperatorClaimToken, Guid.NewGuid())
                .SetProperty(n => n.OperatorClaimExpiresAt, f.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1)));
        (await f.PublishAsync()).ShouldBe(1);
        f.Producer.Sent.Last().Text.ShouldBe(frozen);
        (await f.ReloadAsync()).OperatorPublicationOrdinal.ShouldBe(1);
    }

    [Test]
    public async Task C650_Concurrent_claims_and_ack_have_one_normal_publication()
    {
        await using var f = await Fixture.CreateAsync();
        await f.MarkDueAsync();
        var results = await Task.WhenAll(f.PublishAsync(), f.PublishAsync());
        results.Sum().ShouldBe(1);
        f.Producer.Sent.Count.ShouldBe(1);
        await using (var db = f.World.Db())
            await db.ExpectationNudges.Where(n => n.Id == f.Nudge.Id).ExecuteUpdateAsync(u => u
                .SetProperty(n => n.AnsweredAt, f.Clock.GetUtcNow().UtcDateTime));
        (await f.PublishAsync()).ShouldBe(0);
        f.Producer.Sent.Count.ShouldBe(1);
    }

    [Test]
    public async Task C650_Disabled_channel_retains_visible_unsent_debt()
    {
        await using var f = await Fixture.CreateAsync();
        await f.MarkDueAsync();
        await using (var db = f.World.Db())
            await db.ChatChannels.Where(c => c.Id == f.World.Directive.OperatorChannelId)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.Enabled, false));
        (await f.PublishAsync()).ShouldBe(0);
        f.Producer.Sent.ShouldBeEmpty();
        (await f.ReloadAsync()).OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Due);
    }

    [Test]
    public async Task C650_Reminders_are_bounded_and_audited()
    {
        await using var f = await Fixture.CreateAsync();
        await f.MarkDueAsync();
        (await f.PublishAsync()).ShouldBe(1);
        f.Clock.Advance(TimeSpan.FromMinutes(29));
        (await f.PublishAsync()).ShouldBe(0);
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        (await f.PublishAsync()).ShouldBe(1);
        f.Producer.Sent.Count.ShouldBe(2);
        f.Producer.Sent[0].Text.ShouldContain(":1]");
        f.Producer.Sent[1].Text.ShouldContain(":2]");
        await using var db = f.World.Db();
        (await db.CardComments.CountAsync(c => c.Body.Contains("Broker accepted page"))).ShouldBe(2);
    }

    [Test]
    public async Task C650_Audit_failure_prevents_publish()
    {
        await using var f = await Fixture.CreateAsync();
        await f.MarkDueAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(f.Schema.ConnectionString).AddInterceptors(new RefuseAudit()).Options;
        await using var refusing = new AppDbContext(options);
        var service = new ExpectationOperatorDeliveryService(refusing, f.Producer, f.Clock,
            new ExpectationTimingSettings());
        await Should.ThrowAsync<InvalidOperationException>(() =>
            service.PublishDueAsync(f.World.Directive, CancellationToken.None));
        f.Producer.Sent.ShouldBeEmpty();
        (await f.ReloadAsync()).OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Due);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(IsolatedTestSchema schema, ExpectationTestWorld world,
            ExpectationNudge nudge, FakeProducer producer, FakeTimeProvider clock)
        {
            Schema = schema; World = world; Nudge = nudge; Producer = producer; Clock = clock;
        }

        public IsolatedTestSchema Schema { get; }
        public ExpectationTestWorld World { get; }
        public ExpectationNudge Nudge { get; }
        public FakeProducer Producer { get; }
        public FakeTimeProvider Clock { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
            var now = world.Now;
            var episode = new ExpectationEpisode
            {
                Id = Guid.NewGuid(), DirectiveId = world.Directive.Id,
                ConfigDigest = world.Digest, Kind = ExpectationEpisodeKind.DispatchFence,
                SubjectKey = "fence:test", Evidence = "three queued tasks held",
                FirstObservedAt = now.AddMinutes(-20), LastObservedAt = now,
            };
            var comment = new CardComment
            {
                Id = Guid.NewGuid(), CardId = world.CardId,
                Body = "watchdog audit", Author = ExpectationLedger.AuditAuthor,
                CreatedAt = now,
            };
            var body = $"[expectation-nudge:{Guid.NewGuid():D}] Inspect queue";
            var nudge = new ExpectationNudge
            {
                Id = Guid.NewGuid(), DirectiveId = world.Directive.Id,
                ConfigDigest = world.Digest, Ordinal = 1,
                EpisodeIdsJson = JsonSerializer.Serialize(new[] { episode.Id }),
                EvidenceSnapshot = "fence three queued tasks", Body = body,
                BodyDigest = ExpectationDirectiveDigest.HashUtf8(body),
                AttemptState = ExpectationAttemptState.Confirmed,
                AnswerDueAt = now, AuditCommentId = comment.Id,
                CreatedAt = now.AddMinutes(-10),
            };
            await using (var db = world.Db())
            {
                db.ExpectationEpisodes.Add(episode);
                db.CardComments.Add(comment);
                db.ExpectationNudges.Add(nudge);
                await db.SaveChangesAsync();
            }
            return new Fixture(schema, world, nudge, new FakeProducer(),
                new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero)));
        }

        public async Task MarkDueAsync(ExpectationAttemptState state = ExpectationAttemptState.Confirmed)
        {
            await using var db = World.Db();
            await db.ExpectationNudges.Where(n => n.Id == Nudge.Id).ExecuteUpdateAsync(u => u
                .SetProperty(n => n.AttemptState, state)
                .SetProperty(n => n.OperatorOutboxState, ExpectationOperatorOutboxState.Due)
                .SetProperty(n => n.OperatorFirstDueAt, Clock.GetUtcNow().UtcDateTime)
                .SetProperty(n => n.OperatorNextAttemptAt, Clock.GetUtcNow().UtcDateTime));
        }

        public async Task<int> PublishAsync()
        {
            await using var db = World.Db();
            return await new ExpectationOperatorDeliveryService(db, Producer, Clock,
                new ExpectationTimingSettings()).PublishDueAsync(World.Directive, CancellationToken.None);
        }

        public async Task<ExpectationNudge> ReloadAsync()
        {
            await using var db = World.Db();
            return await db.ExpectationNudges.AsNoTracking().SingleAsync(n => n.Id == Nudge.Id);
        }

        public ValueTask DisposeAsync() => Schema.DisposeAsync();
    }

    private sealed class FakeProducer : IAntiphonMessagingProducer
    {
        private readonly object _gate = new();
        public List<ChannelReply> Sent { get; } = [];
        public bool FailNext { get; set; }

        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (FailNext) { FailNext = false; throw new IOException("broker unavailable"); }
                Sent.Add(reply);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class RefuseAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<CardComment>()
                    .Any(e => e.State == EntityState.Added) == true)
                throw new InvalidOperationException("audit refused");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
