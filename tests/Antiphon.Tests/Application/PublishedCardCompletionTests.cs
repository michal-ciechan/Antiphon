using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<GitProcessSpawnLimit>]
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
        await Should.ThrowAsync<ConflictException>(() => w.CloseAsync());
    }

    [Test]
    public async Task C1281_PublicationGate()
    {
        await using var w = await World.CreateAsync();
        await w.PrepareAsync();
        var proposal = await w.ProposeAsync();
        await using (var db = w.Db())
        {
            var op = await db.AgentTaskLandings.SingleAsync(o => o.Id == w.Request.OperationId);
            var confirmed = op.RemoteConfirmedAt; op.RemoteConfirmedAt = null; await db.SaveChangesAsync();
            await w.RefusedAsync(proposal, "publication_unconfirmed", "unconfirmed-zero-done");
            op.RemoteConfirmedAt = confirmed; var ownerId = op.TaskId; op.TaskId = w.ReviewId; await db.SaveChangesAsync();
            await w.RefusedAsync(proposal, "publication_unconfirmed", "foreign-owner-zero-done");
            op.TaskId = ownerId; await db.SaveChangesAsync();
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == ownerId);
            owner.Role = AgentTaskRole.Plan; await db.SaveChangesAsync();
            await w.RefusedAsync(proposal, "implementation_owner_mismatch", "plan-only-zero-done");
            owner.Role = AgentTaskRole.Code; await db.SaveChangesAsync();
            var found = new StageOutcome { Id = Guid.NewGuid(), StageTaskId = w.ReviewId, SubjectTaskId = ownerId,
                Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Found, SupersedesId = w.EvidenceId };
            db.StageOutcomes.Add(found); await db.SaveChangesAsync();
            (await Should.ThrowAsync<ConflictException>(() => w.CompleteAsync(proposal))).Code.ShouldBe("review_evidence_superseded", "superseded-approval-zero-done");
            db.StageOutcomes.Remove(found); await db.SaveChangesAsync();
        }
        (await w.CompleteAsync(proposal)).Disposition.ShouldBe("completed", "confirmed-owner-closes");
    }

    [Test]
    public async Task C1281_Obligations()
    {
        await using var w = await World.CreateAsync();
        (await Should.ThrowAsync<ConflictException>(() => w.ProposeAsync())).Code.ShouldBe("card_completion_acceptance_missing", "acceptance-missing-zero-done");
        await w.PrepareAsync();
        var good = w.Request;
        var digest = new string('a', 64);
        foreach (var (request, reason, label) in new[]
        {
            (good with { EmptyObligationsDecision = null }, "empty_decision_missing", "explicit-empty-decision-required"),
            (good with { AcceptanceItems = [new("manual", CompletionItemState.Pending)] }, "obligations_pending", "acceptance-pending-zero-done"),
            (good with { DeploymentItems = [new("deploy", CompletionItemState.Pending)] }, "obligations_pending", "deployment-pending-zero-done"),
            (good with { DeploymentItems = [new("deploy", CompletionItemState.Satisfied, digest, good.LandedSha, "v1", new string('b',40), "v1")] }, "deployment_mismatch", "deploy-sha-mismatch-zero-done"),
            (good with { DeploymentItems = [new("deploy", CompletionItemState.Satisfied, digest, good.LandedSha, "v1", good.LandedSha, "v2")] }, "deployment_mismatch", "deploy-version-mismatch-zero-done"),
            (good with { Companion = good.Companion with { Required = true, CardId = Guid.NewGuid() } }, "companion_missing", "companion-missing-zero-done"),
            (good with { Companion = good.Companion with { ImplementationTaskId = w.ReviewId } }, "companion_mismatch", "companion-owner-refused"),
        })
        {
            await w.ReviseAsync(request);
            await w.RefusedAsync(await w.ProposeAsync(), reason, label);
        }
        await w.ReviseAsync(good with { AcceptanceItems = [new("manual", CompletionItemState.Satisfied, digest, Actor: "forged")],
            DeploymentItems = [new("deploy", CompletionItemState.Satisfied, digest, good.LandedSha, "v1", good.LandedSha, "v1")] });
        await using (var db = w.Db())
        {
            var latest = await db.CardCompletionAcceptances.Where(a => a.CardId == w.CardId).OrderByDescending(a => a.Revision).FirstAsync();
            JsonSerializer.Deserialize<CompletionItem[]>(latest.AcceptanceItemsJson)![0].Actor.ShouldBe("operator", "authenticated-actor-retained");
            latest.Revision.ShouldBe(9, "acceptance-appends-each-revision");
        }
        (await w.CloseAsync()).Disposition.ShouldBe("completed");
    }

    [Test]
    public async Task C1281_ConcurrentClose()
    {
        await using var w = await World.CreateAsync();
        await w.PrepareAsync();
        var proposal = await w.ProposeAsync();
        await using (var db = w.Db())
        {
            foreach (var status in new[] { AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked })
            {
                var task = new AgentTask { Id = Guid.NewGuid(), CardId = w.CardId, Title = "new work", Goal = "new work", Status = status, Role = AgentTaskRole.Code };
                db.AgentTasks.Add(task); await db.SaveChangesAsync();
                await w.RefusedAsync(proposal, "open_task", "queued-implementation-not-canceled:" + status);
                (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(status);
                db.AgentTasks.Remove(task); await db.SaveChangesAsync();
            }
            var card = await db.Cards.SingleAsync(c => c.Id == w.CardId);
            card.ArchivedAt = DateTime.UtcNow; await db.SaveChangesAsync();
            await w.RefusedAsync(proposal, "card_ineligible", "archived-zero-done");
            card.ArchivedAt = null;
            foreach (var status in new[] { CardStatus.NeedsDecision, CardStatus.Done, CardStatus.Canceled })
            {
                card.Status = status; await db.SaveChangesAsync();
                (await Should.ThrowAsync<ConflictException>(() => w.CompleteAsync(proposal))).Code.ShouldBe("card_completion_card_ineligible", "terminal-or-decision-zero-new-move");
            }
            card.Status = CardStatus.Review;
            var session = new AgentSession { Id = Guid.NewGuid(), DefinitionName = "fixture", Cwd = w.Land.Fixture.Repository };
            db.AgentSessions.Add(session); card.OwnerSessionId = session.Id; await db.SaveChangesAsync();
            await w.RefusedAsync(proposal, "card_ineligible", "owned-zero-done");
            card.OwnerSessionId = null; await db.SaveChangesAsync();
        }
        await w.ReviseAsync(w.Request);
        await w.RefusedAsync(proposal, "acceptance_changed", "changed-acceptance-refused");
        proposal = await w.ProposeAsync();
        w.Settings.PublishedCloseDecisionRevision = "d2";
        await w.RefusedAsync(proposal, "policy_changed", "changed-policy-refused");
        w.Settings.PublishedCloseDecisionRevision = "d1";
        await using (var db = w.Db())
        {
            var card = await db.Cards.SingleAsync(c => c.Id == w.CardId);
            var done = await db.BoardColumns.SingleAsync(c => c.BoardId == card.BoardId && c.CardStatus == CardStatus.Done);
            await w.Cards(db).MoveAsync(card.Id, new(done.Id, card.ConcurrencyToken, "human wins after proposal"), default);
        }
        (await Should.ThrowAsync<ConflictException>(() => w.CompleteAsync(proposal))).Code.ShouldBe("card_completion_card_changed", "human-between-proposal-and-write-wins");
        await using var read = w.Db();
        (await read.CardCompletionReceipts.CountAsync(r => r.CardId == w.CardId)).ShouldBe(0);
        (await read.CardRevisions.CountAsync(r => r.CardId == w.CardId)).ShouldBe(1, "newer-human-zero-done");
    }

    [Test]
    public async Task C1281_ShadowGate()
    {
        await using var w = await World.CreateAsync(); await w.PrepareAsync();
        w.Settings.PublishedClose = PublishedCloseMode.Disabled;
        (await w.CloseAsync()).Disposition.ShouldBe("disabled", "disabled-zero-done");
        w.Settings.PublishedClose = PublishedCloseMode.Shadow;
        (await w.CloseAsync()).Disposition.ShouldBe("shadow", "shadow-zero-done");
        w.Settings.PublishedClose = PublishedCloseMode.Enabled;
        var last = w.Settings.PublishedCloseShadowObservations[^1];
        w.Settings.PublishedCloseShadowObservations.RemoveAt(29);
        await w.RefusedAsync(await w.ProposeAsync(), "shadow_unqualified", "under-30-refused");
        w.Settings.PublishedCloseShadowObservations.Add(last with { ExpectedClose = false });
        await w.RefusedAsync(await w.ProposeAsync(), "shadow_unqualified", "false-proposal-refused");
        w.Settings.PublishedCloseShadowObservations[29] = last with { AcceptanceId = Guid.NewGuid() };
        await w.RefusedAsync(await w.ProposeAsync(), "shadow_evidence_missing", "invented-shadow-evidence-refused");
        w.Settings.PublishedCloseShadowObservations[29] = last;
        (await w.CloseAsync()).Disposition.ShouldBe("completed", "qualified-control-closes");
    }

    [Test]
    public async Task C1281_CloseRecovery()
    {
        await using var w = await World.CreateAsync();
        await using (var seed = w.Db())
        {
            var agent = new Agent { Id = Guid.NewGuid(), Name = "assigned", Slug = "assigned", WorkingDirectory = w.Land.Fixture.Repository };
            seed.Agents.Add(agent);
            var card = await seed.Cards.SingleAsync(c => c.Id == w.CardId);
            card.AssignedAgentId = agent.Id; card.AgentQueuePosition = 1;
            await seed.SaveChangesAsync();
        }
        await w.PrepareAsync();
        var proposal = await w.ProposeAsync();
        foreach (var cut in new[] { "completion-after-move", "completion-after-dequeue", "completion-before-save", "completion-before-commit" })
        {
            w.Boundary.Cut = cut; w.Boundary.Triggered = false;
            await Should.ThrowAsync<InjectedFailure>(() => w.CompleteAsync(proposal));
            w.Boundary.Triggered.ShouldBeTrue(cut + "-reached");
            await using var db = w.Db();
            (await db.CardRevisions.CountAsync(r => r.CardId == w.CardId)).ShouldBe(0, "close-effects-atomic:" + cut);
            (await db.CardCompletionReceipts.CountAsync(r => r.CardId == w.CardId)).ShouldBe(0);
            var retained = await db.Cards.SingleAsync(c => c.Id == w.CardId);
            retained.Status.ShouldBe(CardStatus.Review);
            retained.AssignedAgentId.ShouldNotBeNull("dequeue-rolled-back:" + cut);
            retained.AgentQueuePosition.ShouldBe(1, "queue-position-rolled-back:" + cut);
        }
        w.Boundary.Cut = null;
        w.Events.Callback = async () =>
        {
            await using var db = w.Db();
            (await db.CardCompletionReceipts.CountAsync(r => r.CardId == w.CardId)).ShouldBe(1, "event-sees-committed-close");
            throw new InjectedFailure();
        };
        await Should.ThrowAsync<InjectedFailure>(() => w.CompleteAsync(proposal));
        w.Events.Callback = null;
        (await w.CompleteAsync(proposal)).Disposition.ShouldBe("already-completed");
        (await w.CompleteAsync(proposal)).Disposition.ShouldBe("already-completed");
        await using var read = w.Db();
        var receipt = await read.CardCompletionReceipts.SingleAsync(r => r.CardId == w.CardId);
        (await read.CardRevisions.SingleAsync(r => r.CardId == w.CardId)).Id.ShouldBe(receipt.MoveRevisionId, "same-operation-close-count == 1");
        var completed = await read.Cards.SingleAsync(c => c.Id == w.CardId);
        completed.AssignedAgentId.ShouldBeNull("dequeue-committed-with-receipt");
        completed.AgentQueuePosition.ShouldBeNull();
    }

    [Test]
    public async Task C1281_CleanupDebt()
    {
        await using var w = await World.CreateAsync(retainResidue: true); await w.PrepareAsync();
        var before = (await w.Land.OperationAsync()).ShouldNotBeNull();
        before.Cleanup.ShouldNotBe(LandCleanupStatus.Complete, "fixture-retains-cleanup-debt");
        var pushes = w.Land.Fixture.Git.Trace.Count(a => a[0] == "push");
        (await w.CloseAsync()).Disposition.ShouldBe("completed");
        var after = (await w.Land.OperationAsync()).ShouldNotBeNull();
        after.Cleanup.ShouldBe(before.Cleanup); after.Id.ShouldBe(before.Id);
        w.Land.Fixture.Git.Trace.Count(a => a[0] == "push").ShouldBe(pushes, "no-second-push");
        Directory.Exists(w.Land.Fixture.Source).ShouldBeTrue("cleanup-residue-retained");
    }

    private sealed class InjectedFailure : Exception { }
    private sealed class Boundary : LandDeliveryBoundary
    {
        public string? Cut { get; set; }
        public bool Triggered { get; set; }
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary == Cut) { Triggered = true; throw new InjectedFailure(); }
            return Task.CompletedTask;
        }
    }
    private sealed class Events : IEventBus
    {
        public Func<Task>? Callback { get; set; }
        public Task PublishToAllAsync(string name, object payload, CancellationToken ct = default) => name == "CardChanged" && Callback is not null ? Callback() : Task.CompletedTask;
        public Task PublishToGroupAsync(string group, string name, object payload, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class World : IAsyncDisposable
    {
        public LandingSafetyHarness Land { get; } = new();
        public Guid CardId { get; } = Guid.NewGuid();
        public Guid ReviewId { get; } = Guid.NewGuid();
        public Guid EvidenceId { get; } = Guid.NewGuid();
        public string SourceSha { get; private set; } = "";
        public WriteCardCompletionAcceptanceRequest Request { get; set; } = null!;
        public CardWorkTransitionSettings Settings { get; } = new() { PublishedClose = PublishedCloseMode.Enabled,
            PublishedClosePolicyRevision = "p1", PublishedCloseDecisionRevision = "d1" };
        public Boundary Boundary { get; } = new();
        public Events Events { get; } = new();
        public AppDbContext Db() => Land.CreateContext();
        public CardService Cards(AppDbContext db) => new(db, null!, null!, null!, Events, TimeProvider.System, null!);
        private PublishedCardCompletion Completion(AppDbContext db) => new(db, Options.Create(Settings), Boundary, TimeProvider.System);
        public async Task<PublishedCardCompletionProposal> ProposeAsync()
        { await using var db = Db(); return await Completion(db).ProposeAsync(CardId, default); }
        public async Task<CompletePublishedCardResult> CompleteAsync(PublishedCardCompletionProposal proposal)
        { await using var db = Db(); return await Cards(db).CompletePublishedAsync(proposal, Completion(db), "operator", default); }
        public async Task<CompletePublishedCardResult> CloseAsync() => await CompleteAsync(await ProposeAsync());
        public async Task RefusedAsync(PublishedCardCompletionProposal proposal, string code, string label)
        {
            (await Should.ThrowAsync<ConflictException>(() => CompleteAsync(proposal))).Code.ShouldBe("card_completion_" + code, label);
            await using var db = Db();
            (await db.CardCompletionReceipts.CountAsync(r => r.CardId == CardId)).ShouldBe(0, label);
        }
        public async Task ReviseAsync(WriteCardCompletionAcceptanceRequest request)
        {
            await using var db = Db();
            var revision = await db.CardCompletionAcceptances.Where(a => a.CardId == CardId).MaxAsync(a => a.Revision);
            Request = request with { ExpectedRevision = revision };
            await WriteAsync();
        }
        public async Task PrepareAsync()
        {
            await WriteAsync();
            await using var db = Db();
            var template = await db.CardCompletionAcceptances.AsNoTracking().SingleAsync(a => a.CardId == CardId);
            var operation = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == template.OperationId);
            // Qualification predicate fixture: retained correlated event records, not live Q-4 credit.
            for (var i = 0; i < 30; i++)
            {
                var op = JsonSerializer.Deserialize<AgentTaskLanding>(JsonSerializer.Serialize(operation))!;
                op.Id = Guid.NewGuid(); op.Active = false;
                op.RecoveryRefPrefix = $"refs/antiphon/land/{op.TaskId:N}/{op.Id:N}";
                var a = JsonSerializer.Deserialize<CardCompletionAcceptance>(JsonSerializer.Serialize(template))!;
                a.Id = Guid.NewGuid(); a.CardId = Guid.NewGuid(); a.OperationId = op.Id;
                db.AgentTaskLandings.Add(op); db.CardCompletionAcceptances.Add(a);
                Settings.PublishedCloseShadowObservations.Add(new(a.Id, op.Id, true, true, "independent-operator", DateTime.UtcNow));
            }
            await db.SaveChangesAsync();
        }
        public static async Task<World> CreateAsync(bool publish = true, bool retainResidue = false)
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
                if (retainResidue)
                    w.Land.Fixture.Git.AfterCommand = async (_, args, result) =>
                    {
                        if (args[0] == "push" && result.Succeeded && args.Any(a => a.EndsWith(":" + w.Land.Fixture.TargetRef)))
                            await File.WriteAllTextAsync(Path.Combine(w.Land.Fixture.Source, "retained-after-publication.txt"), "valuable untracked residue\n");
                    };
                await w.Land.RequestAsync(expectedSourceSha: w.SourceSha, reviewEvidenceId: w.EvidenceId);
                await w.Land.RunQueuedAsync();
                w.Land.Fixture.Git.AfterCommand = null;
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
