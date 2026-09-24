using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("C650ExpectationLedger")]
public sealed class ExpectationLedgerTests
{
    private const string DirectiveId = "tonight";
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Body = "Dispatch is fenced on the repository journal. Inspect ownership before any restart; do not treat HeldAged as the prompt.";
    private static readonly DateTime Scan = new(2026, 9, 24, 2, 6, 23, DateTimeKind.Utc);
    private static readonly DateTime NextNudge = new(2026, 9, 24, 2, 16, 23, DateTimeKind.Utc);
    private static readonly DateTime Generation = new(2026, 9, 24, 2, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task C650_Persists_episode_and_nudge_with_audit()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(isolated.ConnectionString);
        var seed = await SeedAsync(options);
        var sessionId = Guid.NewGuid();
        await using var db = new AppDbContext(options);
        var bus = new RecordingBus();
        var ledger = new ExpectationLedger(db, Clock(), bus);

        await ledger.CommitNudgeAsync(Request(seed, sessionId, ExpectationEpisodeKind.DispatchFence), CancellationToken.None);

        await using var fresh = new AppDbContext(options);
        var nudge = await fresh.ExpectationNudges.SingleOrDefaultAsync();
        nudge.ShouldNotBeNull("durable nudge was not committed with its audit");
        nudge.Body.ShouldBe(Body);
        nudge.BodyDigest.ShouldBe(Sha256(Body));
        nudge.DirectiveId.ShouldBe(DirectiveId);
        nudge.Ordinal.ShouldBe(1);
        nudge.DestinationSessionId.ShouldBe(sessionId);
        nudge.DestinationGeneration.ShouldNotBeNull();
        nudge.DestinationGeneration!.Value.ShouldBe(Generation, TimeSpan.FromMilliseconds(1));
        nudge.BaselineSequence.ShouldBe(42);
        nudge.AttemptState.ShouldBe(ExpectationAttemptState.None);
        nudge.OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.None);
        nudge.OperatorPublishedAt.ShouldBeNull();

        var episode = await fresh.ExpectationEpisodes.SingleAsync();
        episode.Kind.ShouldBe(ExpectationEpisodeKind.DispatchFence);
        episode.SubjectKey.ShouldBe("repo:antiphon");
        episode.ResolvedAt.ShouldBeNull();
        episode.Evidence.ShouldBe("journal fence; queued paths blocked");
        episode.FirstObservedAt.ShouldBe(Scan, TimeSpan.FromMilliseconds(1));
        JsonSerializer.Deserialize<Guid[]>(nudge.EpisodeIdsJson).ShouldBe([episode.Id]);

        var comment = await fresh.CardComments.SingleAsync();
        comment.Id.ShouldBe(nudge.AuditCommentId);
        comment.CardId.ShouldBe(seed.CardId);
        comment.Origin.ShouldBe(CardCommentOrigin.Antiphon);
        comment.Author.ShouldBe(ExpectationLedger.AuditAuthor);
        comment.Body.ShouldContain(nudge.Id.ToString("D"));
        comment.Body.ShouldContain(Body);

        var events = await fresh.AgentTaskEvents.Where(row => row.AgentTaskId == seed.TaskId).ToListAsync();
        events.Count.ShouldBe(1);
        events[0].Type.ShouldBe(AgentTaskEventType.Check);
        events[0].Detail.ShouldContain(nudge.Id.ToString("D"));
        events.ShouldNotContain(row => row.Type is AgentTaskEventType.Held or AgentTaskEventType.HeldAged);
        JsonSerializer.Deserialize<Guid[]>(nudge.CheckEventIdsJson).ShouldBe([events[0].Id]);

        var state = await fresh.ExpectationWatchStates.SingleAsync();
        state.ConfigDigest.ShouldBe(Digest);
        state.LastSuccessfulScanAt.ShouldNotBeNull();
        state.LastSuccessfulScanAt!.Value.ShouldBe(Scan, TimeSpan.FromMilliseconds(1));
        state.NextNudgeAt.ShouldNotBeNull();
        state.NextNudgeAt!.Value.ShouldBe(NextNudge, TimeSpan.FromMilliseconds(1));

        (await fresh.SessionQueuedMessages.CountAsync()).ShouldBe(0);
        bus.Events.Count.ShouldBe(1);
        bus.Events[0].Name.ShouldBe("BoardChanged");
        JsonDocument.Parse(JsonSerializer.Serialize(bus.Events[0].Payload))
            .RootElement.GetProperty("boardId").GetGuid().ShouldBe(seed.BoardId);
    }

    [Test]
    public async Task C650_Concurrent_create_has_one_open_episode()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(isolated.ConnectionString);
        await SeedAsync(options);
        await using var left = new AppDbContext(options);
        await using var right = new AppDbContext(options);
        var leftBus = new RecordingBus();
        var rightBus = new RecordingBus();
        var open = new ExpectationEpisodeOpen(
            DirectiveId,
            Digest,
            ExpectationEpisodeKind.StalledPipeline,
            "repo:antiphon",
            "queued stint aged; HeldAged is not a reset",
            Scan,
            Scan,
            NextNudge);

        var ids = await Task.WhenAll(
            new ExpectationLedger(left, Clock(), leftBus).OpenEpisodeAsync(open, CancellationToken.None),
            new ExpectationLedger(right, Clock(), rightBus).OpenEpisodeAsync(open, CancellationToken.None));

        await using var fresh = new AppDbContext(options);
        var rows = await fresh.ExpectationEpisodes.Where(row => row.ResolvedAt == null).ToListAsync();
        rows.Count.ShouldBe(1);
        rows[0].Kind.ShouldBe(ExpectationEpisodeKind.StalledPipeline);
        ids[0].ShouldBe(rows[0].Id);
        ids[1].ShouldBe(rows[0].Id);
        (await fresh.ExpectationNudges.CountAsync()).ShouldBe(0);
        (await fresh.SessionQueuedMessages.CountAsync()).ShouldBe(0);
        leftBus.Events.ShouldBeEmpty();
        rightBus.Events.ShouldBeEmpty();
    }

    [Test]
    public async Task C650_Audit_failure_rolls_back_nudge()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(isolated.ConnectionString);
        var seed = await SeedAsync(options);
        var missingCard = Guid.NewGuid();
        await using var db = new AppDbContext(options);
        var bus = new RecordingBus();
        var ledger = new ExpectationLedger(db, Clock(), bus);

        await Should.ThrowAsync<Exception>(() => ledger.CommitNudgeAsync(
            Request(seed, Guid.NewGuid(), ExpectationEpisodeKind.DispatchFence) with { AuditCardId = missingCard },
            CancellationToken.None));

        await using var fresh = new AppDbContext(options);
        (await fresh.ExpectationNudges.CountAsync()).ShouldBe(0);
        (await fresh.CardComments.CountAsync()).ShouldBe(0);
        (await fresh.AgentTaskEvents.CountAsync(row => row.Type == AgentTaskEventType.Check)).ShouldBe(0);
        (await fresh.ExpectationEpisodes.CountAsync()).ShouldBe(0);
        (await fresh.SessionQueuedMessages.CountAsync()).ShouldBe(0);
        bus.Events.ShouldBeEmpty();
    }

    [Test]
    public async Task C650_Reload_keeps_clocks_and_attempt_identity()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(isolated.ConnectionString);
        var seed = await SeedAsync(options);
        var sessionId = Guid.NewGuid();
        await using (var db = new AppDbContext(options))
        {
            await new ExpectationLedger(db, Clock(), new RecordingBus())
                .CommitNudgeAsync(Request(seed, sessionId, ExpectationEpisodeKind.SilentInFlight), CancellationToken.None);
        }

        await using var fresh = new AppDbContext(options);
        var state = await fresh.ExpectationWatchStates.SingleAsync();
        state.DirectiveId.ShouldBe(DirectiveId);
        state.ConfigDigest.ShouldBe(Digest);
        state.LastSuccessfulScanAt.ShouldNotBeNull();
        state.LastSuccessfulScanAt!.Value.ShouldBe(Scan, TimeSpan.FromMilliseconds(1));
        state.NextNudgeAt.ShouldNotBeNull();
        state.NextNudgeAt!.Value.ShouldBe(NextNudge, TimeSpan.FromMilliseconds(1));

        var nudge = await fresh.ExpectationNudges.SingleAsync();
        nudge.Ordinal.ShouldBe(1);
        nudge.Body.ShouldBe(Body);
        nudge.BodyDigest.ShouldBe(Sha256(Body));
        nudge.DestinationSessionId.ShouldBe(sessionId);
        nudge.DestinationGeneration.ShouldNotBeNull();
        nudge.DestinationGeneration!.Value.ShouldBe(Generation, TimeSpan.FromMilliseconds(1));
        nudge.BaselineSequence.ShouldBe(42);
        nudge.AttemptState.ShouldBe(ExpectationAttemptState.None);
        var comment = await fresh.CardComments.SingleAsync(row => row.Id == nudge.AuditCommentId);
        comment.Body.ShouldContain(nudge.Id.ToString("D"));
        var episode = await fresh.ExpectationEpisodes.SingleAsync();
        episode.FirstObservedAt.ShouldBe(Scan, TimeSpan.FromMilliseconds(1));
        episode.LastObservedAt.ShouldBe(Scan, TimeSpan.FromMilliseconds(1));
        (await fresh.SessionQueuedMessages.CountAsync()).ShouldBe(0);
    }

    private static ExpectationNudgeRequest Request(Seed seed, Guid sessionId, ExpectationEpisodeKind kind) =>
        new(
            DirectiveId,
            Digest,
            kind,
            "repo:antiphon",
            kind == ExpectationEpisodeKind.DispatchFence
                ? "journal fence; queued paths blocked"
                : "silent after dispatch",
            Body,
            seed.CardId,
            seed.BoardId,
            [seed.TaskId],
            sessionId,
            Generation,
            42,
            Scan,
            NextNudge,
            Scan);

    private static FakeTimeProvider Clock() => new(new DateTimeOffset(Scan, TimeSpan.Zero));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static async Task<Seed> SeedAsync(DbContextOptions<AppDbContext> options)
    {
        var now = new DateTime(2026, 9, 24, 2, 0, 0, DateTimeKind.Utc);
        var seed = new Seed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        await using var db = new AppDbContext(options);
        db.Projects.Add(new Project
        {
            Id = seed.ProjectId,
            Name = "c650",
            GitRepositoryUrl = "",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Boards.Add(new Board
        {
            Id = seed.BoardId,
            ProjectId = seed.ProjectId,
            Name = "c650",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.BoardColumns.Add(new BoardColumn
        {
            Id = seed.ColumnId,
            BoardId = seed.BoardId,
            StateKey = "backlog",
            Name = "Backlog",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Cards.Add(new Card
        {
            Id = seed.CardId,
            BoardId = seed.BoardId,
            BoardColumnId = seed.ColumnId,
            Identifier = "C650" + seed.CardId.ToString("N")[..8],
            Title = "audit",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = seed.AgentId,
            Name = "Standing",
            Slug = "c650-" + seed.AgentId.ToString("N"),
            WorkingDirectory = @"C:\src\Antiphon",
            BoardId = seed.BoardId,
            IsPoolDelegate = false,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ChatChannels.Add(new ChatChannel
        {
            Id = seed.ChannelId,
            Provider = "telegram",
            ExternalId = seed.ChannelId.ToString("N"),
            Kind = ChatChannelKind.Group,
            Enabled = true,
            AgentId = seed.AgentId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = seed.TaskId,
            RootTaskId = seed.TaskId,
            CardId = seed.CardId,
            ProjectId = seed.ProjectId,
            Title = "queued hold",
            Goal = "stay queued",
            Status = AgentTaskStatus.Queued,
            WorkingDirectory = @"C:\src\Antiphon",
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return seed;
    }

    private sealed record Seed(
        Guid ProjectId,
        Guid BoardId,
        Guid ColumnId,
        Guid CardId,
        Guid AgentId,
        Guid ChannelId,
        Guid TaskId);

    private sealed class RecordingBus : IEventBus
    {
        public List<(string Name, object Payload)> Events { get; } = [];

        public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default)
        {
            Events.Add((eventName, payload));
            return Task.CompletedTask;
        }

        public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default)
        {
            Events.Add((eventName, payload));
            return Task.CompletedTask;
        }
    }
}
