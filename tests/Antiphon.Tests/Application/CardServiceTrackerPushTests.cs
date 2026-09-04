using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0347 S2: CardService close/reopen hooks the per-card tracker push after the local write.</summary>
[Category("Integration")]
[NotInParallel]
public class CardServiceTrackerPushTests
{
    [Test]
    public async Task MoveAsync_into_Done_on_a_linked_card_pushes_Closed()
    {
        var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues);
        await using var harness = await CreateHarnessAsync(fake);
        var tempRoot = harness.TempRoot;
        try
        {
            var graph = await SeedLinkedAsync(tempRoot, fake);
            var cards = harness.Scope.ServiceProvider.GetRequiredService<CardService>();
            var result = await cards.MoveAsync(
                graph.CardId,
                new MoveCardRequest(graph.DoneColumnId, graph.Token, "Shipped."),
                CancellationToken.None);

            result.TrackerPush.ShouldNotBeNull();
            result.TrackerPush!.Outcome.ShouldBe(TrackerCardStatePushOutcome.Closed);
            fake.SetStateCalls.Count.ShouldBe(1);
            fake.SetStateCalls[0].State.ShouldBe("closed");
            result.Card.Status.ShouldBe(CardStatus.Done);

            await using var verify = BridgeQueueHarness.CreateContext();
            var row = await verify.Cards.SingleAsync(c => c.Id == graph.CardId);
            row.Status.ShouldBe(CardStatus.Done);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Unlinked_card_move_has_null_TrackerPush_and_zero_writes()
    {
        var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues);
        await using var harness = await CreateHarnessAsync(fake);
        var tempRoot = harness.TempRoot;
        try
        {
            var graph = await SeedUnlinkedAsync(tempRoot);
            var cards = harness.Scope.ServiceProvider.GetRequiredService<CardService>();
            var result = await cards.MoveAsync(
                graph.CardId,
                new MoveCardRequest(graph.DoneColumnId, graph.Token, "Closed without a link."),
                CancellationToken.None);

            result.TrackerPush.ShouldBeNull();
            fake.WriteCallCount.ShouldBe(0);
            result.Card.Status.ShouldBe(CardStatus.Done);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Backlog_to_InProgress_on_a_linked_card_does_not_push()
    {
        var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues);
        await using var harness = await CreateHarnessAsync(fake);
        var tempRoot = harness.TempRoot;
        try
        {
            var graph = await SeedLinkedAsync(tempRoot, fake);
            var cards = harness.Scope.ServiceProvider.GetRequiredService<CardService>();
            var result = await cards.MoveAsync(
                graph.CardId,
                new MoveCardRequest(graph.ActiveColumnId, graph.Token, "Starting."),
                CancellationToken.None);

            result.TrackerPush.ShouldBeNull();
            fake.WriteCallCount.ShouldBe(0);
            result.Card.Status.ShouldBe(CardStatus.InProgress);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task ReopenAsync_on_a_linked_card_pushes_Reopened()
    {
        var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues);
        await using var harness = await CreateHarnessAsync(fake);
        var tempRoot = harness.TempRoot;
        try
        {
            var graph = await SeedLinkedAsync(tempRoot, fake);
            var cards = harness.Scope.ServiceProvider.GetRequiredService<CardService>();
            var closed = await cards.MoveAsync(
                graph.CardId,
                new MoveCardRequest(graph.DoneColumnId, graph.Token, "Closed too soon."),
                CancellationToken.None);
            fake.ClearWriteCounters();
            await Task.Delay(50);

            var reopened = await cards.ReopenAsync(
                graph.CardId,
                new ReopenCardRequest(closed.Card.ConcurrencyToken, "Still open."),
                CancellationToken.None);

            reopened.TrackerPush.ShouldNotBeNull();
            reopened.TrackerPush!.Outcome.ShouldBe(TrackerCardStatePushOutcome.Reopened);
            fake.SetStateCalls.Single().State.ShouldBe("open");
            reopened.Card.Status.ShouldBe(CardStatus.Backlog);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Fake_throwing_still_leaves_the_card_Done_with_Failed_reason()
    {
        var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues) { ThrowOnPostComment = true };
        await using var harness = await CreateHarnessAsync(fake);
        var tempRoot = harness.TempRoot;
        try
        {
            var graph = await SeedLinkedAsync(tempRoot, fake);
            var cards = harness.Scope.ServiceProvider.GetRequiredService<CardService>();
            var result = await cards.MoveAsync(
                graph.CardId,
                new MoveCardRequest(graph.DoneColumnId, graph.Token, "Will fail the push."),
                CancellationToken.None);

            result.Card.Status.ShouldBe(CardStatus.Done);
            result.TrackerPush.ShouldNotBeNull();
            result.TrackerPush!.Outcome.ShouldBe(TrackerCardStatePushOutcome.Failed);
            result.TrackerPush.Reason.ShouldNotBeNullOrWhiteSpace();

            await using var verify = BridgeQueueHarness.CreateContext();
            var row = await verify.Cards.SingleAsync(c => c.Id == graph.CardId);
            row.Status.ShouldBe(CardStatus.Done);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    private static Task<BridgeQueueHarness> CreateHarnessAsync(FakeBidirectionalTracker fake) =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConfigureServices = services =>
            {
                services.AddSingleton(fake);
                services.AddScoped<IIssueTracker>(_ => fake);
            }
        });

    private static async Task<Graph> SeedLinkedAsync(string tempRoot, FakeBidirectionalTracker fake)
    {
        var graph = await SeedBoardAsync(tempRoot, linked: true);
        fake.Candidates =
        [
            new TrackedIssue(
                graph.ExternalId, graph.ExternalKey, "Title", "Body", "open", 0,
                ["status:backlog"], [], graph.Url, "{}")
        ];
        return graph;
    }

    private static Task<Graph> SeedUnlinkedAsync(string tempRoot) => SeedBoardAsync(tempRoot, linked: false);

    private static async Task<Graph> SeedBoardAsync(string tempRoot, bool linked)
    {
        await using var db = BridgeQueueHarness.CreateContext();
        var now = DateTime.UtcNow;
        var n = Random.Shared.Next(10_000, 99_999);
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"PushHook Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(tempRoot, "repo-" + n),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        Directory.CreateDirectory(project.LocalRepositoryPath);

        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"PushHook Board {n}",
            TrackerKind = linked ? TrackerKind.GitHubIssues : TrackerKind.Internal,
            TrackerActivatedAt = linked ? now : null,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);

        var backlog = Col(board, "backlog", "Backlog", 0, CardStatus.Backlog, false, false, now);
        var active = Col(board, "in_progress", "In Progress", 1, CardStatus.InProgress, true, false, now);
        var done = Col(board, "done", "Done", 2, CardStatus.Done, false, true, now);
        board.Columns.Add(backlog);
        board.Columns.Add(active);
        board.Columns.Add(done);

        if (linked)
        {
            board.WorkflowDefinitions.Add(new BoardWorkflowDefinition
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                Version = 1,
                Name = "Tracked",
                Content = string.Join('\n',
                    "---",
                    "tracker:",
                    "  kind: github_issues",
                    "  repository: acme/app",
                    "  active_states: [open]",
                    "---",
                    "Work."),
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
                Board = board
            });
        }

        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = backlog.Id,
            Identifier = $"CARD-{n}",
            Title = $"Hook card {n}",
            Description = "desc",
            Importance = CardImportance.Normal,
            LabelsJson = "[]",
            Status = CardStatus.Backlog,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
            Board = board,
            BoardColumn = backlog
        };
        db.Projects.Add(project);
        db.Cards.Add(card);

        var externalId = $"acme/app#{n}";
        var externalKey = $"#{n}";
        var url = $"https://github.test/acme/app/issues/{n}";
        if (linked)
        {
            var issueRef = new ExternalIssueRef
            {
                Id = Guid.NewGuid(),
                CardId = card.Id,
                TrackerKind = TrackerKind.GitHubIssues,
                ExternalId = externalId,
                ExternalKey = externalKey,
                Url = url,
                RawPayloadJson = "{}",
                LastSyncedAt = now,
                Origin = ExternalIssueOrigin.ExternalImport,
                LastKnownExternalState = "open",
                LastRevisionSynced = 0,
                Card = card
            };
            card.ExternalIssueRef = issueRef;
            db.ExternalIssueRefs.Add(issueRef);
        }

        await db.SaveChangesAsync();
        return new Graph(card.Id, card.ConcurrencyToken, backlog.Id, active.Id, done.Id, externalId, externalKey, url);
    }

    private static BoardColumn Col(
        Board board, string key, string name, int order, CardStatus status,
        bool active, bool terminal, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = key,
            Name = name,
            ColumnOrder = order,
            CardStatus = status,
            IsActive = active,
            IsTerminal = terminal,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };

    private static async Task CleanupAsync(string tempRoot)
    {
        await using var db = BridgeQueueHarness.CreateContext();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        if (projectIds.Count == 0)
            return;

        var boardIds = await db.Boards.Where(b => projectIds.Contains(b.ProjectId)).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
    }

    private sealed record Graph(
        Guid CardId,
        Guid Token,
        Guid BacklogColumnId,
        Guid ActiveColumnId,
        Guid DoneColumnId,
        string ExternalId,
        string ExternalKey,
        string Url);
}
