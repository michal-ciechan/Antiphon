using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.ApiKeys;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0347 S1: per-card GitHub state push, shared with the bidirectional run.</summary>
[Category("Integration")]
[NotInParallel]
public class TrackerCardStatePushServiceTests
{
    [Test]
    public async Task Linked_Done_open_cursor_closes_with_completed_and_status_label()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            graph.Card.BoardColumnId = graph.DoneColumn.Id;
            graph.Card.BoardColumn = graph.DoneColumn;
            graph.Card.Status = CardStatus.Done;
            graph.Card.CompletedAt = clock.GetUtcNow().UtcDateTime;
            graph.Card.TerminalReason = "Shipped the close push.";
            graph.Card.ExternalIssueRef!.LastKnownExternalState = "open";
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", ["status:backlog"])]
            };
            var sut = NewSut(db, fake, clock);
            var result = await sut.PushForCardAsync(graph.Card.Id, CancellationToken.None);

            result.ShouldNotBeNull();
            result.Outcome.ShouldBe(TrackerCardStatePushOutcome.Closed);
            result.ExternalKey.ShouldBe("#1");
            fake.PostCommentCalls.Count.ShouldBe(1);
            fake.PostCommentCalls[0].Body.ShouldStartWith(
                $"Card {graph.Card.Identifier} closed as **Done** on Antiphon.");
            fake.PostCommentCalls[0].Body.ShouldEndWith(
                $"<!-- antiphon:system-comment={graph.Card.Id:N} -->");
            fake.SetStateCalls.ShouldHaveSingleItem();
            fake.SetStateCalls[0].State.ShouldBe("closed");
            fake.SetStateCalls[0].StateReason.ShouldBe("completed");
            fake.FetchByIdsCalls.ShouldBe(1);
            fake.Candidates.Single().Labels.ShouldContain("status:done");

            await using var verify = CreateContext();
            var issueRef = await verify.ExternalIssueRefs.SingleAsync(r => r.CardId == graph.Card.Id);
            issueRef.LastKnownExternalState.ShouldBe("closed");
            issueRef.LastOutboundSyncedAt.ShouldBe(clock.GetUtcNow().UtcDateTime);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Canceled_closes_as_not_planned()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            graph.Card.BoardColumnId = graph.DoneColumn.Id;
            graph.Card.BoardColumn = graph.DoneColumn;
            graph.Card.Status = CardStatus.Canceled;
            graph.Card.CompletedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", [])]
            };
            var result = await NewSut(db, fake, clock).PushForCardAsync(graph.Card.Id, CancellationToken.None);

            result!.Outcome.ShouldBe(TrackerCardStatePushOutcome.Closed);
            fake.SetStateCalls.Single().StateReason.ShouldBe("not_planned");
            fake.PostCommentCalls.Single().Body.ShouldStartWith(
                $"Card {graph.Card.Identifier} closed as **Canceled** on Antiphon.");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Cursor_already_closed_is_InSync_with_zero_writes()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            graph.Card.BoardColumnId = graph.DoneColumn.Id;
            graph.Card.BoardColumn = graph.DoneColumn;
            graph.Card.Status = CardStatus.Done;
            graph.Card.ExternalIssueRef!.LastKnownExternalState = "closed";
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "closed", "Title", "Body", ["status:done"])]
            };
            var result = await NewSut(db, fake, clock).PushForCardAsync(graph.Card.Id, CancellationToken.None);

            result!.Outcome.ShouldBe(TrackerCardStatePushOutcome.InSync);
            fake.WriteCallCount.ShouldBe(0);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Fresh_reopen_revision_reopens_then_second_call_is_InSync()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            graph.Card.ExternalIssueRef!.LastKnownExternalState = "closed";
            db.CardRevisions.Add(new CardRevision
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                RevisionNumber = ++graph.Card.RevisionCount,
                Kind = CardRevisionKind.Reopen,
                Reason = "continue work",
                EditedBy = "operator",
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                Card = graph.Card
            });
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "closed", "Title", "Body", ["status:done"])]
            };
            var sut = NewSut(db, fake, clock);
            var first = await sut.PushForCardAsync(graph.Card.Id, CancellationToken.None);

            first!.Outcome.ShouldBe(TrackerCardStatePushOutcome.Reopened);
            fake.PostCommentCalls.Single().Body.ShouldStartWith(
                $"Card {graph.Card.Identifier} reopened on Antiphon.");
            fake.SetStateCalls.Single().State.ShouldBe("open");
            fake.SetStateCalls.Single().StateReason.ShouldBe("reopened");

            fake.ClearWriteCounters();
            var second = await sut.PushForCardAsync(graph.Card.Id, CancellationToken.None);
            second!.Outcome.ShouldBe(TrackerCardStatePushOutcome.InSync);
            fake.WriteCallCount.ShouldBe(0);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task ThrowOnPostComment_is_Failed_without_SetState_and_cursor_stays_open()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            graph.Card.BoardColumnId = graph.DoneColumn.Id;
            graph.Card.BoardColumn = graph.DoneColumn;
            graph.Card.Status = CardStatus.Done;
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", [])],
                ThrowOnPostComment = true
            };
            var result = await NewSut(db, fake, clock).PushForCardAsync(graph.Card.Id, CancellationToken.None);

            result!.Outcome.ShouldBe(TrackerCardStatePushOutcome.Failed);
            result.Reason.ShouldNotBeNullOrWhiteSpace();
            fake.SetStateCalls.ShouldBeEmpty();

            await using var verify = CreateContext();
            var issueRef = await verify.ExternalIssueRefs.SingleAsync(r => r.CardId == graph.Card.Id);
            issueRef.LastKnownExternalState.ShouldBe("open");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task HangOnSetState_times_out_and_leaves_the_card_row_untouched()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            graph.Card.BoardColumnId = graph.DoneColumn.Id;
            graph.Card.BoardColumn = graph.DoneColumn;
            graph.Card.Status = CardStatus.Done;
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", [])],
                HangOnSetState = true
            };
            var sut = NewSut(db, fake, clock, new TrackerSettings { CardStatePushTimeoutSeconds = 1 });
            var sw = Stopwatch.StartNew();
            var result = await sut.PushForCardAsync(graph.Card.Id, CancellationToken.None);
            sw.Stop();

            result!.Outcome.ShouldBe(TrackerCardStatePushOutcome.Failed);
            result.Reason.ShouldBe("timeout");
            sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
            fake.SetStateCalls.ShouldBeEmpty();

            await using var verify = CreateContext();
            var issueRef = await verify.ExternalIssueRefs.SingleAsync(r => r.CardId == graph.Card.Id);
            issueRef.LastKnownExternalState.ShouldBe("open");
            issueRef.LastOutboundSyncedAt.ShouldBeNull();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Guards_unlinked_read_only_inactive_and_unresolved_token()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var unlinkedGraph = await SeedBoardAsync(db, tempRoot, clock, syncOutCreate: false);
            var unlinked = NewCard(unlinkedGraph, clock.GetUtcNow().UtcDateTime);
            db.Cards.Add(unlinked);
            await db.SaveChangesAsync();
            var github = new FakeBidirectionalTracker(TrackerKind.GitHubIssues);
            (await NewSut(db, github, clock).PushForCardAsync(unlinked.Id, CancellationToken.None))
                .ShouldBeNull();
            github.WriteCallCount.ShouldBe(0);

            var jiraGraph = await SeedLinkedBoardAsync(db, tempRoot, clock, externalId: "acme/app#2");
            jiraGraph.Board.TrackerKind = TrackerKind.Jira;
            jiraGraph.Board.WorkflowDefinitions.Single().Content = """
                ---
                tracker:
                  kind: jira
                  project: TEST
                ---
                Work.
                """;
            jiraGraph.Card.BoardColumnId = jiraGraph.DoneColumn.Id;
            jiraGraph.Card.BoardColumn = jiraGraph.DoneColumn;
            jiraGraph.Card.Status = CardStatus.Done;
            await db.SaveChangesAsync();
            var jira = new ReadOnlyTracker(TrackerKind.Jira);
            var jiraResult = await NewSut(db, jira, clock).PushForCardAsync(jiraGraph.Card.Id, CancellationToken.None);
            jiraResult!.Outcome.ShouldBe(TrackerCardStatePushOutcome.Skipped);
            jiraResult.Reason.ShouldBe("tracker_read_only");

            var internalGraph = await SeedLinkedBoardAsync(db, tempRoot, clock, externalId: "acme/app#3");
            internalGraph.Board.TrackerKind = TrackerKind.Internal;
            internalGraph.Card.BoardColumnId = internalGraph.DoneColumn.Id;
            internalGraph.Card.BoardColumn = internalGraph.DoneColumn;
            internalGraph.Card.Status = CardStatus.Done;
            await db.SaveChangesAsync();
            var internalFake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues);
            var internalResult = await NewSut(db, internalFake, clock)
                .PushForCardAsync(internalGraph.Card.Id, CancellationToken.None);
            internalResult!.Outcome.ShouldBe(TrackerCardStatePushOutcome.Skipped);
            internalResult.Reason.ShouldBe("tracker_inactive");
            internalFake.WriteCallCount.ShouldBe(0);

            var tokenGraph = await SeedLinkedBoardAsync(db, tempRoot, clock, externalId: "acme/app#4");
            tokenGraph.Board.WorkflowDefinitions.Single().Content = """
                ---
                tracker:
                  kind: github_issues
                  repository: acme/app
                  token_key: missing-c0347-key
                  active_states: [open]
                ---
                Work.
                """;
            tokenGraph.Card.BoardColumnId = tokenGraph.DoneColumn.Id;
            tokenGraph.Card.BoardColumn = tokenGraph.DoneColumn;
            tokenGraph.Card.Status = CardStatus.Done;
            await db.SaveChangesAsync();
            var tokenFake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", [])]
            };
            var tokenResult = await NewSut(db, tokenFake, clock)
                .PushForCardAsync(tokenGraph.Card.Id, CancellationToken.None);
            tokenResult!.Outcome.ShouldBe(TrackerCardStatePushOutcome.Skipped);
            tokenResult.Reason.ShouldBe("token_unresolved");
            tokenFake.WriteCallCount.ShouldBe(0);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Switch_off_is_disabled_with_zero_writes()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            graph.Card.BoardColumnId = graph.DoneColumn.Id;
            graph.Card.BoardColumn = graph.DoneColumn;
            graph.Card.Status = CardStatus.Done;
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", [])]
            };
            var result = await NewSut(
                    db, fake, clock, new TrackerSettings { PushStateOnCardTransition = false })
                .PushForCardAsync(graph.Card.Id, CancellationToken.None);

            result!.Outcome.ShouldBe(TrackerCardStatePushOutcome.Skipped);
            result.Reason.ShouldBe("disabled");
            fake.WriteCallCount.ShouldBe(0);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Running_board_is_sync_running_with_zero_writes()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            graph.Card.BoardColumnId = graph.DoneColumn.Id;
            graph.Card.BoardColumn = graph.DoneColumn;
            graph.Card.Status = CardStatus.Done;
            await db.SaveChangesAsync();

            TrackerBidirectionalSyncService.TryHoldRunning(graph.Board.Id).ShouldBeTrue();
            try
            {
                var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
                {
                    Candidates = [Issue("acme/app#1", "open", "Title", "Body", [])]
                };
                var result = await NewSut(db, fake, clock)
                    .PushForCardAsync(graph.Card.Id, CancellationToken.None);

                result!.Outcome.ShouldBe(TrackerCardStatePushOutcome.Skipped);
                result.Reason.ShouldBe("sync_running");
                fake.WriteCallCount.ShouldBe(0);
            }
            finally
            {
                TrackerBidirectionalSyncService.ReleaseRunning(graph.Board.Id);
            }
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    private static TrackerCardStatePushService NewSut(
        AppDbContext db,
        IIssueTracker tracker,
        TimeProvider clock,
        TrackerSettings? settings = null)
    {
        var tokens = new TrackerTokenResolver(
            db, new ApiKeyStoreTests.FakeApiKeyProtector(), NullLogger<TrackerTokenResolver>.Instance);
        return new TrackerCardStatePushService(
            db,
            tokens,
            [tracker],
            Options.Create(settings ?? new TrackerSettings()),
            NullLogger<TrackerCardStatePushService>.Instance,
            clock);
    }

    private static async Task<Graph> SeedLinkedBoardAsync(
        AppDbContext db,
        string tempRoot,
        FakeTimeProvider clock,
        ExternalIssueOrigin origin = ExternalIssueOrigin.ExternalImport,
        string externalId = "acme/app#1")
    {
        var graph = await SeedBoardAsync(db, tempRoot, clock, syncOutCreate: false);
        var card = NewCard(graph, clock.GetUtcNow().UtcDateTime);
        var issueRef = new ExternalIssueRef
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            TrackerKind = TrackerKind.GitHubIssues,
            ExternalId = externalId,
            ExternalKey = "#" + externalId.Split('#')[^1],
            Url = $"https://github.test/{externalId.Replace('#', '/')}",
            RawPayloadJson = "{}",
            LastSyncedAt = clock.GetUtcNow().UtcDateTime,
            Origin = origin,
            LastKnownExternalState = "open",
            LastRevisionSynced = 0,
            Card = card
        };
        card.ExternalIssueRef = issueRef;
        db.Cards.Add(card);
        db.ExternalIssueRefs.Add(issueRef);
        await db.SaveChangesAsync();
        return graph with { Card = card };
    }

    private static async Task<Graph> SeedBoardAsync(
        AppDbContext db,
        string tempRoot,
        FakeTimeProvider clock,
        bool syncOutCreate)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Push Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(tempRoot, "repo-" + Guid.NewGuid().ToString("N")[..8]),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        Directory.CreateDirectory(project.LocalRepositoryPath!);

        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Push Board {Guid.NewGuid():N}",
            TrackerKind = TrackerKind.GitHubIssues,
            TrackerActivatedAt = now,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);

        var backlog = Col(board, "backlog", "Backlog", 0, CardStatus.Backlog, active: false, terminal: false, now);
        var active = Col(board, "in_progress", "In Progress", 1, CardStatus.InProgress, active: true, terminal: false, now);
        var done = Col(board, "done", "Done", 2, CardStatus.Done, active: false, terminal: true, now);
        board.Columns.Add(backlog);
        board.Columns.Add(active);
        board.Columns.Add(done);

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
                $"  sync_out_create: {syncOutCreate.ToString().ToLowerInvariant()}",
                "---",
                "Work on {{ issue.identifier }}."),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        });

        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return new Graph(project, board, backlog, active, done, Card: null!);
    }

    private static Card NewCard(Graph graph, DateTime createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            BoardId = graph.Board.Id,
            BoardColumnId = graph.BacklogColumn.Id,
            Identifier = $"CARD-{Random.Shared.Next(1000, 9999)}",
            Title = $"Card {Guid.NewGuid():N}"[..20],
            Description = "desc",
            Importance = CardImportance.Normal,
            LabelsJson = "[]",
            Status = CardStatus.Backlog,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            Board = graph.Board,
            BoardColumn = graph.BacklogColumn
        };

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

    private static TrackedIssue Issue(
        string externalId, string state, string title, string body, IReadOnlyList<string> labels) =>
        new(externalId, "#" + externalId.Split('#')[^1], title, body, state, 0, labels, [],
            $"https://github.test/{externalId.Replace('#', '/')}", "{}");

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-card-push-{Guid.NewGuid():N}");

    private static async Task CleanupAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        if (projectIds.Count == 0)
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { /* best effort */ }
            return;
        }

        var boardIds = await db.Boards.Where(b => projectIds.Contains(b.ProjectId)).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        await db.CardComments.Where(c => cardIds.Contains(c.CardId)).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
        try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { /* best effort */ }
    }

    private sealed record Graph(
        Project Project,
        Board Board,
        BoardColumn BacklogColumn,
        BoardColumn ActiveColumn,
        BoardColumn DoneColumn,
        Card Card);

    private sealed class ReadOnlyTracker(TrackerKind kind) : IIssueTracker
    {
        public TrackerKind Kind { get; } = kind;

        public Task<IReadOnlyList<TrackedIssue>> FetchCandidatesAsync(
            IssueTrackerConfig config, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TrackedIssue>>([]);

        public Task<IReadOnlyList<TrackedIssue>> FetchByStatesAsync(
            IssueTrackerConfig config, IReadOnlyList<string> states, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TrackedIssue>>([]);

        public Task<IReadOnlyList<TrackedIssue>> FetchByIdsAsync(
            IssueTrackerConfig config, IReadOnlyList<string> externalIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TrackedIssue>>([]);
    }
}
