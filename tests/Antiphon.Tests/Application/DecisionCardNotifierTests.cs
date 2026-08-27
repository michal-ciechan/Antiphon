using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>One loud ping per decision parking; an unset stamp is the retry contract.</summary>
[Category("Integration")]
[NotInParallel]
public class DecisionCardNotifierTests
{
    [Test]
    public async Task A_newly_parked_card_is_pinged_exactly_once()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedChannelAsync();
        var card = await h.SeedDecisionAsync("Which release should go first?");

        await h.Notifier.SweepAsync(CancellationToken.None);
        await h.Notifier.SweepAsync(CancellationToken.None);

        h.PingsFor(card).Count.ShouldBe(1);
        (await h.NotifiedAtAsync(card)).ShouldNotBeNull();
    }

    [Test]
    public async Task A_card_parked_again_after_being_decided_is_pinged_again()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedChannelAsync();
        var card = await h.SeedDecisionAsync("First question.");
        await h.Notifier.SweepAsync(CancellationToken.None);

        h.Clock.Advance(TimeSpan.FromMinutes(5));
        await h.ParkAgainAsync(card, "Second question.");
        await h.Notifier.SweepAsync(CancellationToken.None);

        h.PingsFor(card).Count.ShouldBe(2);
    }

    [Test]
    public async Task A_card_decided_before_the_sweep_is_not_pinged()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedChannelAsync();
        var card = await h.SeedDecisionAsync("Can this ship?");
        await h.DecideAsync(card);

        await h.Notifier.SweepAsync(CancellationToken.None);

        h.PingsFor(card).ShouldBeEmpty();
        (await h.NotifiedAtAsync(card)).ShouldBeNull();
    }

    [Test]
    public async Task A_throwing_send_leaves_the_stamp_unset_so_the_next_sweep_retries()
    {
        var throwing = new ThrowingProducer();
        await using var h = await Harness.CreateAsync(throwing);
        await h.SeedChannelAsync();
        var card = await h.SeedDecisionAsync("Still waiting.");

        await h.Notifier.SweepAsync(CancellationToken.None);

        throwing.Attempts.ShouldBe(1);
        (await h.NotifiedAtAsync(card)).ShouldBeNull();

        h.ReplaceProducer(new FakeAntiphonMessagingClient());
        await h.Notifier.SweepAsync(CancellationToken.None);

        h.PingsFor(card).Count.ShouldBe(1);
        (await h.NotifiedAtAsync(card)).ShouldNotBeNull();
    }

    [Test]
    public async Task Wake_on_decision_false_sends_nothing()
    {
        await using var h = await Harness.CreateAsync(wakeOnDecision: false);
        await h.SeedChannelAsync();
        var card = await h.SeedDecisionAsync("Should this wait?");

        await h.Notifier.SweepAsync(CancellationToken.None);

        h.PingsFor(card).ShouldBeEmpty();
        (await h.NotifiedAtAsync(card)).ShouldBeNull();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly DigestSettings _settings;
        private ChatChannelService _channels;
        private Guid? _boardId;
        private Guid? _columnId;

        private Harness(IsolatedTestSchema schema, AppDbContext db, FakeTimeProvider clock,
            IAntiphonMessagingProducer producer, FakeAntiphonMessagingClient messaging, DigestSettings settings)
        {
            _schema = schema;
            Db = db;
            Clock = clock;
            Messaging = messaging;
            _settings = settings;
            _channels = new ChatChannelService(db, clock, producer);
            Notifier = BuildNotifier();
        }

        public AppDbContext Db { get; }
        public FakeTimeProvider Clock { get; }
        public FakeAntiphonMessagingClient Messaging { get; private set; }
        public DecisionCardNotifier Notifier { get; private set; }

        public static async Task<Harness> CreateAsync(IAntiphonMessagingProducer? producer = null, bool wakeOnDecision = true)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
            var messaging = producer as FakeAntiphonMessagingClient ?? new FakeAntiphonMessagingClient();
            return new Harness(schema, db, clock, producer ?? messaging, messaging,
                new DigestSettings { WakeOnDecision = wakeOnDecision, TimeZone = "Europe/London" });
        }

        public void ReplaceProducer(FakeAntiphonMessagingClient producer)
        {
            Messaging = producer;
            _channels = new ChatChannelService(Db, Clock, producer);
            Notifier = BuildNotifier();
        }

        public async Task SeedChannelAsync()
        {
            var now = Clock.GetUtcNow().UtcDateTime;
            Db.ChatChannels.Add(new ChatChannel
            {
                Id = Guid.NewGuid(), Provider = "telegram", ExternalId = $"-decision-{Guid.NewGuid():N}",
                Kind = ChatChannelKind.Direct, Title = "Operator", Enabled = true, DigestEnabled = true,
                CreatedAt = now, UpdatedAt = now,
            });
            await Db.SaveChangesAsync();
        }

        public async Task<Guid> SeedDecisionAsync(string question)
        {
            await EnsureBoardAsync();
            var now = Clock.GetUtcNow().UtcDateTime;
            var cardId = Guid.NewGuid();
            Db.Cards.Add(new Card
            {
                Id = cardId, BoardId = _boardId!.Value, BoardColumnId = _columnId!.Value,
                Identifier = $"CARD-{cardId.ToString("N")[..4]}", Title = "Needs operator input",
                Description = "notifier test", Status = CardStatus.NeedsDecision, CreatedAt = now.AddMinutes(-10),
                UpdatedAt = now.AddMinutes(-5), RevisionCount = 1,
            });
            Db.CardRevisions.Add(new CardRevision
            {
                Id = Guid.NewGuid(), CardId = cardId, RevisionNumber = 1, Kind = CardRevisionKind.Move,
                FromStatus = CardStatus.Backlog, ToStatus = CardStatus.NeedsDecision, ToColumnId = _columnId,
                Reason = question, CreatedAt = now.AddMinutes(-5),
            });
            await Db.SaveChangesAsync();
            return cardId;
        }

        public async Task ParkAgainAsync(Guid cardId, string question)
        {
            var card = await Db.Cards.SingleAsync(c => c.Id == cardId);
            card.Status = CardStatus.NeedsDecision;
            card.UpdatedAt = Clock.GetUtcNow().UtcDateTime;
            Db.CardRevisions.Add(new CardRevision
            {
                Id = Guid.NewGuid(), CardId = cardId, RevisionNumber = ++card.RevisionCount,
                Kind = CardRevisionKind.Move, FromStatus = CardStatus.Backlog, ToStatus = CardStatus.NeedsDecision,
                ToColumnId = card.BoardColumnId, Reason = question, CreatedAt = Clock.GetUtcNow().UtcDateTime,
            });
            await Db.SaveChangesAsync();
        }

        public async Task DecideAsync(Guid cardId)
        {
            var card = await Db.Cards.SingleAsync(c => c.Id == cardId);
            card.Status = CardStatus.Backlog;
            await Db.SaveChangesAsync();
        }

        public IReadOnlyList<ChannelReply> PingsFor(Guid cardId) => Messaging.SentReplies
            .Where(reply => reply.Text?.Contains($"CARD-{cardId.ToString("N")[..4]}") == true).ToList();

        public async Task<DateTime?> NotifiedAtAsync(Guid cardId)
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
            return await db.Cards.Where(c => c.Id == cardId).Select(c => c.DecisionNotifiedAt).SingleAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _schema.DisposeAsync();
        }

        private DecisionCardNotifier BuildNotifier()
        {
            var attention = new AttentionService(Db, new RefusingSessionRunnerClient(),
                Options.Create(new SupervisionSettings()), Options.Create(new DelegationSettings()), Clock,
                NullLogger<AttentionService>.Instance);
            return new DecisionCardNotifier(Db, attention, _channels, Options.Create(_settings), Clock,
                NullLogger<DecisionCardNotifier>.Instance);
        }

        private async Task EnsureBoardAsync()
        {
            if (_boardId is not null) return;
            var now = Clock.GetUtcNow().UtcDateTime;
            var project = new Project { Id = Guid.NewGuid(), Name = "decision-notifier", GitRepositoryUrl = "https://example.test/repo.git", CreatedAt = now, UpdatedAt = now };
            var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "Decision board", CreatedAt = now, UpdatedAt = now };
            var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "needs-decision", Name = "Needs decision", ColumnOrder = 0, CardStatus = CardStatus.NeedsDecision, CreatedAt = now, UpdatedAt = now };
            Db.AddRange(project, board, column);
            await Db.SaveChangesAsync();
            _boardId = board.Id;
            _columnId = column.Id;
        }
    }

    private sealed class ThrowingProducer : IAntiphonMessagingProducer
    {
        public int Attempts { get; private set; }
        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("broker unavailable");
        }
    }
}
