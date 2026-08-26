using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0036 S1: the away-window projection. Shared-Postgres rules: every assertion is scoped to
/// rows this test created. The class takes <c>[NotInParallel]</c> with no group key because
/// <see cref="AwayDigestProjection.ComputeAsync"/> walks every root task and every card revision
/// in the database.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class AwayDigestProjectionTests
{
    private static readonly DateTime Since = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Until = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void First_sentence_uses_the_first_sentence_or_single_line()
    {
        AwayDigestProjection.FirstSentence("First sentence. Second sentence.").ShouldBe("First sentence.");
        AwayDigestProjection.FirstSentence("Single line").ShouldBe("Single line");
    }

    [Test]
    public void Cost_walk_rolls_children_into_their_root_once()
    {
        var rootId = Guid.NewGuid();
        var root = new AgentTask { Id = rootId, RootTaskId = rootId, CostUsd = 2m };
        var child = new AgentTask { Id = Guid.NewGuid(), RootTaskId = rootId, ParentTaskId = rootId, CostUsd = 3m };

        AgentTaskCostWalk.Calculate([root], [root, child])[rootId].ShouldBe(5m);
    }

    [Test]
    public async Task Window_includes_until_and_excludes_since()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            var atSince = await h.AddRootAsync("at-since", AgentTaskStatus.Succeeded, Since, "Landed on the start.");
            var justAfterSince = await h.AddRootAsync("just-after-since", AgentTaskStatus.Succeeded, Since.AddSeconds(1), "Inside.");
            var atUntil = await h.AddRootAsync("at-until", AgentTaskStatus.Succeeded, Until, "Landed on the end.");
            var afterUntil = await h.AddRootAsync("after-until", AgentTaskStatus.Succeeded, Until.AddSeconds(1), "Too late.");
            var beforeSince = await h.AddRootAsync("before-since", AgentTaskStatus.Succeeded, Since.AddSeconds(-1), "Too early.");

            var digest = await h.ComputeAsync();
            var finished = Ours(digest.Finished, atSince, justAfterSince, atUntil, afterUntil, beforeSince)
                .Select(t => t.TaskId)
                .ToHashSet();

            finished.ShouldNotContain(atSince);
            finished.ShouldContain(justAfterSince);
            finished.ShouldContain(atUntil);
            finished.ShouldNotContain(afterUntil);
            finished.ShouldNotContain(beforeSince);
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task Children_are_not_surfaced_independently_of_their_root()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            var root = await h.AddRootAsync("root-finished", AgentTaskStatus.Succeeded, Until.AddMinutes(-10),
                "The parent report.", cost: 2m);
            var child = await h.AddChildAsync(root, "child-finished", AgentTaskStatus.Succeeded, Until.AddMinutes(-5),
                "The child report.", cost: 3m);

            var digest = await h.ComputeAsync();

            Ours(digest.Finished, root, child).Select(t => t.TaskId).ShouldBe([root]);
            var row = Ours(digest.Finished, root).ShouldHaveSingleItem();
            row.CostUsd.ShouldBe(5m);
            row.Detail.ShouldBe("The parent report.");
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task Check_kind_roots_are_excluded_from_the_window()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            var check = await h.AddRootAsync("check-finished", AgentTaskStatus.Succeeded, Until.AddMinutes(-10),
                "Interpreted a check.", role: AgentTaskRole.Check);
            var work = await h.AddRootAsync("work-finished", AgentTaskStatus.Succeeded, Until.AddMinutes(-10),
                "Did the work.");

            var digest = await h.ComputeAsync();
            var finished = Ours(digest.Finished, check, work).Select(t => t.TaskId).ToHashSet();

            finished.ShouldNotContain(check);
            finished.ShouldContain(work);
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task A_card_moved_back_out_of_review_is_dropped_from_that_section()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            var stillInReview = await h.AddCardAsync("still-in-review", CardStatus.Review);
            await h.AddMoveAsync(stillInReview, CardStatus.InProgress, CardStatus.Review, Until.AddMinutes(-30));

            var movedOut = await h.AddCardAsync("moved-out", CardStatus.Done, completedAt: Until.AddMinutes(-5));
            await h.AddMoveAsync(movedOut, CardStatus.InProgress, CardStatus.Review, Until.AddMinutes(-40));
            await h.AddMoveAsync(movedOut, CardStatus.Review, CardStatus.Done, Until.AddMinutes(-5));

            var beforeWindow = await h.AddCardAsync("review-before-window", CardStatus.Review);
            await h.AddMoveAsync(beforeWindow, CardStatus.InProgress, CardStatus.Review, Since.AddMinutes(-10));

            var digest = await h.ComputeAsync();
            var reviewIds = digest.Review
                .Where(c => c.Identifier == stillInReview.Identifier
                    || c.Identifier == movedOut.Identifier
                    || c.Identifier == beforeWindow.Identifier)
                .Select(c => c.Identifier)
                .ToHashSet();

            reviewIds.ShouldContain(stillInReview.Identifier);
            reviewIds.ShouldNotContain(movedOut.Identifier);
            reviewIds.ShouldNotContain(beforeWindow.Identifier);
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task First_window_stays_false_on_the_projection()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            var digest = await h.ComputeAsync();
            digest.FirstWindow.ShouldBeFalse(
                "ComputeAsync never decides first-vs-subsequent; AwayDigestNotifier overlays the flag");
            digest.SinceUtc.ShouldBe(Since);
            digest.UntilUtc.ShouldBe(Until);
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task A_failed_root_in_the_window_is_failed_not_finished()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            var failed = await h.AddRootAsync("failed-root", AgentTaskStatus.Failed, Until.AddMinutes(-15),
                failure: "Merge conflicted.\nMore detail.");
            var canceled = await h.AddRootAsync("canceled-root", AgentTaskStatus.Canceled, Until.AddMinutes(-15),
                result: "Stopped on request.");

            var digest = await h.ComputeAsync();

            Ours(digest.Failed, failed).ShouldHaveSingleItem().Detail.ShouldBe("Merge conflicted.");
            Ours(digest.Finished, failed).ShouldBeEmpty();
            Ours(digest.Finished, canceled).ShouldHaveSingleItem();
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    private static IReadOnlyList<AwayDigestTaskDto> Ours(IReadOnlyList<AwayDigestTaskDto> rows, params Guid[] ids)
    {
        var set = ids.ToHashSet();
        return rows.Where(r => set.Contains(r.TaskId)).ToList();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly List<Guid> _taskIds = [];
        private readonly List<Guid> _cardIds = [];
        private readonly List<Guid> _columnIds = [];
        private Guid? _projectId;
        private Guid? _boardId;

        public required AppDbContext Db { get; init; }
        public required AwayDigestProjection Projection { get; init; }

        public static Task<Harness> CreateAsync()
        {
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var time = TimeProvider.System;
            var attention = new AttentionService(
                db, new RefusingSessionRunnerClient(), Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings()), time, NullLogger<AttentionService>.Instance);
            var projection = new AwayDigestProjection(
                db, attention, new SubscriptionUsageReader(db, time), Options.Create(new DelegationSettings()));
            return Task.FromResult(new Harness { Db = db, Projection = projection });
        }

        public Task<AwayDigestDto> ComputeAsync() => Projection.ComputeAsync(Since, Until, CancellationToken.None);

        public async Task<Guid> AddRootAsync(
            string title,
            AgentTaskStatus status,
            DateTime completedAt,
            string? result = null,
            string? failure = null,
            AgentTaskRole role = AgentTaskRole.Code,
            decimal cost = 0m)
        {
            var id = Guid.NewGuid();
            var created = completedAt.AddHours(-1);
            Db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = title,
                Goal = "projection window",
                Kind = AgentTaskKind.Worker,
                Role = role,
                ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                Status = status,
                ReplyTo = AgentTaskReplyTo.None,
                Result = result,
                FailureReason = failure,
                CostUsd = cost,
                CreatedAt = created,
                DispatchedAt = created,
                CompletedAt = completedAt,
            });
            await Db.SaveChangesAsync();
            _taskIds.Add(id);
            return id;
        }

        public async Task<Guid> AddChildAsync(
            Guid rootId, string title, AgentTaskStatus status, DateTime completedAt, string? result = null, decimal cost = 0m)
        {
            var id = Guid.NewGuid();
            var created = completedAt.AddHours(-1);
            Db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = rootId,
                ParentTaskId = rootId,
                Depth = 1,
                Title = title,
                Goal = "projection child",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                Status = status,
                ReplyTo = AgentTaskReplyTo.None,
                Result = result,
                CostUsd = cost,
                CreatedAt = created,
                DispatchedAt = created,
                CompletedAt = completedAt,
            });
            await Db.SaveChangesAsync();
            _taskIds.Add(id);
            return id;
        }

        public async Task<Card> AddCardAsync(string title, CardStatus status, DateTime? completedAt = null)
        {
            await EnsureBoardAsync();
            var now = Until.AddMinutes(-60);
            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = _boardId!.Value,
                BoardColumnId = _columnIds[0],
                Identifier = $"DGST-{Guid.NewGuid():N}"[..18],
                Title = title,
                Description = "digest projection card",
                Status = status,
                CreatedAt = now,
                UpdatedAt = now,
                CompletedAt = completedAt,
            };
            Db.Cards.Add(card);
            await Db.SaveChangesAsync();
            _cardIds.Add(card.Id);
            return card;
        }

        public async Task AddMoveAsync(Card card, CardStatus from, CardStatus to, DateTime at)
        {
            Db.CardRevisions.Add(new CardRevision
            {
                Id = Guid.NewGuid(),
                CardId = card.Id,
                RevisionNumber = ++card.RevisionCount,
                Kind = CardRevisionKind.Move,
                FromStatus = from,
                ToStatus = to,
                CreatedAt = at,
            });
            await Db.SaveChangesAsync();
        }

        public async Task CleanupAsync()
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            if (_taskIds.Count > 0)
                await db.AgentTasks.Where(t => _taskIds.Contains(t.Id)).ExecuteDeleteAsync();
            if (_cardIds.Count > 0)
            {
                await db.CardRevisions.Where(r => _cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
                await db.Cards.Where(c => _cardIds.Contains(c.Id)).ExecuteDeleteAsync();
            }
            if (_columnIds.Count > 0)
                await db.BoardColumns.Where(c => _columnIds.Contains(c.Id)).ExecuteDeleteAsync();
            if (_boardId is Guid boardId)
                await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
            if (_projectId is Guid projectId)
                await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();

        private async Task EnsureBoardAsync()
        {
            if (_boardId is not null) return;
            var now = Until.AddHours(-2);
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"digest-proj-{Guid.NewGuid():N}",
                GitRepositoryUrl = "https://example.test/digest.git",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = $"digest-board-{Guid.NewGuid():N}",
                MaxConcurrentSessions = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var column = new BoardColumn
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                StateKey = $"digest-{Guid.NewGuid():N}",
                Name = "Review",
                ColumnOrder = 0,
                CardStatus = CardStatus.Review,
                CreatedAt = now,
                UpdatedAt = now,
            };
            Db.AddRange(project, board, column);
            await Db.SaveChangesAsync();
            _projectId = project.Id;
            _boardId = board.Id;
            _columnIds.Add(column.Id);
        }
    }
}
