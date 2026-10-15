using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PublishedCardCompletionTests
{
    [Test]
    public async Task C1281_ReadyForRelease()
    {
        await using var w = await World.CreateAsync(publish: false);
        await w.WriteAsync();
        await using var db = w.Db();
        var view = await new CardCompletionAcceptanceService(db, TimeProvider.System).GetAsync(w.CardId, default);
        view.Readiness.ShouldBe("ready for release", "ready-metadata-survives-restart");
        view.Acceptance!.ReviewedSourceSha.ShouldBe(w.SourceSha);
        (await db.Cards.SingleAsync(c => c.Id == w.CardId)).Status.ShouldBe(CardStatus.Review, "ready-status-review");
        (await db.BoardColumns.CountAsync()).ShouldBe(2, "no-new-board-column");
        (await db.AgentSessions.CountAsync()).ShouldBe(0, "no-waiting-session");
    }

    private sealed class World : IAsyncDisposable
    {
        public LandingSafetyHarness Land { get; } = new();
        public Guid CardId { get; } = Guid.NewGuid();
        public Guid ReviewId { get; } = Guid.NewGuid();
        public Guid EvidenceId { get; } = Guid.NewGuid();
        public string SourceSha { get; private set; } = "";
        public WriteCardCompletionAcceptanceRequest Request { get; set; } = null!;
        public AppDbContext Db() => Land.CreateContext();
        public static async Task<World> CreateAsync(bool publish = true)
        {
            var w = new World();
            await w.Land.InitializeAsync();
            w.SourceSha = await w.Land.AddSourceAsync();
            await using (var db = w.Db())
            {
                var project = new Project { Id = Guid.NewGuid(), Name = "completion", LocalRepositoryPath = w.Land.Fixture.Repository };
                var board = new Board { Id = Guid.NewGuid(), Name = "completion", ProjectId = project.Id };
                var review = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Review", StateKey = "review", CardStatus = CardStatus.Review };
                var done = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Done", StateKey = "done", CardStatus = CardStatus.Done, IsTerminal = true, ColumnOrder = 1 };
                var card = new Card { Id = w.CardId, BoardId = board.Id, BoardColumnId = review.Id, Identifier = "CARD-0001", Title = "completion", Status = CardStatus.Review };
                db.AddRange(project, board, review, done, card);
                var owner = await db.AgentTasks.SingleAsync(t => t.Id == w.Land.Fixture.TaskId);
                owner.CardId = w.CardId; owner.ProjectId = project.Id;
                db.AgentTasks.Add(new AgentTask { Id = w.ReviewId, RootTaskId = w.ReviewId, Title = "Review", Goal = "Review", Role = AgentTaskRole.Review,
                    Stage = OrchestrationStage.Review, Status = AgentTaskStatus.Succeeded, CardId = w.CardId, ProjectId = project.Id,
                    RepoPath = w.Land.Fixture.Repository, WorkingDirectory = w.Land.Fixture.Repository });
                db.StageOutcomes.Add(new StageOutcome { Id = w.EvidenceId, Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
                    Source = StageOutcomeSource.Delegate, StageTaskId = w.ReviewId, SubjectTaskId = owner.Id, CardId = w.CardId,
                    ReviewedSourceSha = w.SourceSha, ReviewedSourceRef = w.Land.Fixture.SourceRef, ReviewedRepositoryPath = w.Land.Fixture.Repository,
                    ReviewedSourceClean = true, VerificationProfileVersion = 1, CommissionedRound = VerificationRound.Final, OrdinaryScopeCompleted = VerificationScope.Full });
                await db.SaveChangesAsync();
                w.Request = new(0, card.ConcurrencyToken, null, null, null, null, owner.Id, w.Land.Fixture.Repository,
                    w.Land.Fixture.SourceRef, w.SourceSha, w.EvidenceId, null, null, "p1", "d1", [], [],
                    new("source-only", "No deployment or manual acceptance required"), new(false, null, owner.Id, null), true);
            }
            if (publish)
            {
                await w.Land.RequestAsync(expectedSourceSha: w.SourceSha, reviewEvidenceId: w.EvidenceId);
                await w.Land.RunQueuedAsync();
                var op = (await w.Land.OperationAsync()).ShouldNotBeNull();
                new AgentTaskLandingState().HasPublication(op).ShouldBeTrue("real-private-publication");
                var observed = await w.Land.Fixture.RequiredAsync(w.Land.Fixture.Observer, "fetch", "origin", w.Land.Fixture.TargetRef);
                (await w.Land.Fixture.RequiredAsync(w.Land.Fixture.Observer, "rev-parse", "FETCH_HEAD")).Trim().ShouldBe(op.VerifiedSourceSha);
                w.Request = w.Request with { OperationId = op.Id, LandedSha = op.VerifiedSourceSha,
                    Companion = w.Request.Companion with { OperationId = op.Id } };
            }
            return w;
        }
        public async Task WriteAsync()
        {
            await using var db = Db();
            await new CardCompletionAcceptanceService(db, TimeProvider.System).WriteAsync(CardId, Request, "operator", default);
        }
        public ValueTask DisposeAsync() => Land.DisposeAsync();
    }
}
