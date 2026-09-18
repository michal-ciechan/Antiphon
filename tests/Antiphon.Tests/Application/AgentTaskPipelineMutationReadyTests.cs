using System.Text.Json;
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

/// <summary>
/// CARD-0552 V-552-27..38. The Mutation stage's landing-sourced ready rows, against a real schema.
/// </summary>
public partial class AgentTaskPipelineStatusTests
{
    private const string C552PlanPath = "docs/superpowers/plans/2026-09-17-card-0552-mutation-tracked-stage-plan.md";

    [Test]
    public async Task C552_Q01_ConfirmedPublicationWithOpenCompanionIsAMutationReadyRow()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var world = await SeedDebtAsync(db, workspace.Path);
        var plan = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "plan CARD-0001", cardId: world.Original.Id, completedAt: DateTime.UtcNow.AddHours(-4),
            deliverablePath: C552PlanPath);
        plan.DeliverableRef = "abc123";
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Review, AgentTaskStatus.Succeeded,
            title: "review CARD-0001", cardId: world.Original.Id, completedAt: DateTime.UtcNow.AddHours(-3),
            nextStage: PipelineHandoffKind.Land, nextHandoff: "original Code landing owner");
        await db.SaveChangesAsync();

        var row = MutationReady(await CreateService(db).GetAsync(CancellationToken.None))
            .ShouldHaveSingleItem();

        row.Card.Id.ShouldBe(world.Companion.Id);
        row.Card.Identifier.ShouldBe("CARD-0002");
        row.SourcePlanTaskId.ShouldBe(world.Owner.Id);
        row.SourcePlanShortId.ShouldBe(DelegationReportFormatter.Short(world.Owner.Id));
        row.ReadySince.ShouldBe(world.Operation.RemoteConfirmedAt!.Value);
        row.DeliverablePath.ShouldBe(C552PlanPath);
        row.DeliverableRef.ShouldBe("abc123");
        row.SourceRole.ShouldBe(AgentTaskRole.Code);
        row.Handoff.ShouldBe("original Code landing owner");
        row.SourceLandingOperationId.ShouldBe(world.Operation.Id);
        row.SourceLandingSha.ShouldBe(world.Operation.VerifiedSourceSha);
        row.OriginalCard.ShouldNotBeNull().Id.ShouldBe(world.Original.Id);
        row.OriginalCard!.Identifier.ShouldBe("CARD-0001");
        row.RoutingPin.ShouldBeNull();
    }

    [Test]
    public async Task C552_Q02_NoPlanNoReviewGivesEmptyDeliverableAndNullHandoff()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedDebtAsync(db, workspace.Path);

        var row = MutationReady(await CreateService(db).GetAsync(CancellationToken.None))
            .ShouldHaveSingleItem();

        row.DeliverablePath.ShouldBe("");
        row.DeliverableRef.ShouldBeNull();
        row.Handoff.ShouldBeNull();
    }

    [Test]
    public async Task C552_Q03_UnconfirmedPublicationProducesNoRow()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var world = await SeedDebtAsync(db, workspace.Path, confirmed: false);

        MutationReady(await CreateService(db).GetAsync(CancellationToken.None))
            .Where(r => r.Card.Id == world.Companion.Id).ShouldBeEmpty();
    }

    [Test]
    [Arguments(CardStatus.Done, false)]
    [Arguments(CardStatus.Canceled, false)]
    [Arguments(CardStatus.NeedsDecision, false)]
    [Arguments(CardStatus.Backlog, true)]
    public async Task C552_Q04_TerminalOrArchivedCompanionProducesNoRow(CardStatus status, bool archived)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var world = await SeedDebtAsync(db, workspace.Path, companionStatus: status, companionArchived: archived);

        MutationReady(await CreateService(db).GetAsync(CancellationToken.None))
            .Where(r => r.Card.Id == world.Companion.Id).ShouldBeEmpty();
    }

    [Test]
    [Arguments(AgentTaskStatus.Queued, true)]
    [Arguments(AgentTaskStatus.Dispatched, true)]
    [Arguments(AgentTaskStatus.Working, true)]
    [Arguments(AgentTaskStatus.Blocked, true)]
    [Arguments(AgentTaskStatus.Succeeded, true)]
    [Arguments(AgentTaskStatus.Failed, false)]
    [Arguments(AgentTaskStatus.Canceled, false)]
    public async Task C552_Q05_SourcedAttemptConsumption(AgentTaskStatus status, bool consumed)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var world = await SeedDebtAsync(db, workspace.Path);
        await SeedSourcedTaskAsync(db, workspace.Path, status, world.Companion.Id, world.Operation.Id);

        var rows = MutationReady(await CreateService(db).GetAsync(CancellationToken.None))
            .Where(r => r.Card.Id == world.Companion.Id).ToList();

        if (consumed) rows.ShouldBeEmpty();
        else rows.ShouldHaveSingleItem().SourceLandingOperationId.ShouldBe(world.Operation.Id);
    }

    [Test]
    [Arguments(AgentTaskStatus.Queued, true)]
    [Arguments(AgentTaskStatus.Working, true)]
    [Arguments(AgentTaskStatus.Succeeded, false)]
    public async Task C552_Q06_UnsourcedMutationOnCompanion(AgentTaskStatus status, bool consumed)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var world = await SeedDebtAsync(db, workspace.Path);
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Mutation, status,
            title: "explicit battery", cardId: world.Companion.Id,
            completedAt: status == AgentTaskStatus.Succeeded ? DateTime.UtcNow : null);

        var rows = MutationReady(await CreateService(db).GetAsync(CancellationToken.None))
            .Where(r => r.Card.Id == world.Companion.Id).ToList();

        if (consumed) rows.ShouldBeEmpty();
        else rows.ShouldHaveSingleItem();
    }

    [Test]
    public async Task C552_Q07_NewestOperationIsTheSource()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var older = DateTime.UtcNow.AddHours(-2);
        var world = await SeedDebtAsync(db, workspace.Path, remoteConfirmedAt: older, active: false);
        var newer = await SeedConfirmedLandingAsync(db, world.Owner, world.Companion.Id,
            DateTime.UtcNow.AddHours(-1), verifiedSha: new string('d', 40));
        await SeedSourcedTaskAsync(db, workspace.Path, AgentTaskStatus.Succeeded,
            world.Companion.Id, world.Operation.Id);

        var row = MutationReady(await CreateService(db).GetAsync(CancellationToken.None))
            .Where(r => r.Card.Id == world.Companion.Id).ShouldHaveSingleItem();

        row.SourceLandingOperationId.ShouldBe(newer.Id);
        row.SourceLandingSha.ShouldBe(newer.VerifiedSourceSha);
        row.ReadySince.ShouldBe(newer.RemoteConfirmedAt!.Value);
    }

    [Test]
    public async Task C552_Q08_LandingRowOutranksLegacyRowOnSameCardAndLegacyElsewhereStays()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var world = await SeedDebtAsync(db, workspace.Path);
        // A legacy `next: mutation` handoff on the SAME companion card.
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Succeeded,
            title: "legacy handoff", cardId: world.Companion.Id, completedAt: DateTime.UtcNow.AddMinutes(-5),
            nextStage: PipelineHandoffKind.Mutation);
        var elsewhere = await SeedCardAsync(db, CardStatus.Review, "CARD-0470");
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Succeeded,
            title: "legacy elsewhere", cardId: elsewhere.Id, completedAt: DateTime.UtcNow.AddMinutes(-5),
            nextStage: PipelineHandoffKind.Mutation);

        var ready = MutationReady(await CreateService(db).GetAsync(CancellationToken.None));

        ready.Where(r => r.Card.Id == world.Companion.Id).ShouldHaveSingleItem()
            .SourceLandingOperationId.ShouldBe(world.Operation.Id);
        ready.Where(r => r.Card.Id == elsewhere.Id).ShouldHaveSingleItem()
            .SourceLandingOperationId.ShouldBeNull();
    }

    [Test]
    public async Task C552_Q09_RowsOrderByReadySinceThenIdentifier()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var first = await SeedDebtAsync(db, workspace.Path, remoteConfirmedAt: DateTime.UtcNow.AddHours(-2));
        var secondCompanion = await SeedCardOnBoardAsync(db, first.Original.BoardId, CardStatus.Backlog, "CARD-0003");
        var secondOwner = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Succeeded,
            title: "owner 2", cardId: first.Original.Id, workspace: WorkspaceMode.Worktree,
            repoPath: workspace.Path, worktreeBranch: "feat/second", completedAt: DateTime.UtcNow.AddHours(-1));
        await SeedConfirmedLandingAsync(db, secondOwner, secondCompanion.Id, DateTime.UtcNow.AddHours(-1));

        var ready = MutationReady(await CreateService(db).GetAsync(CancellationToken.None));

        ready.Select(r => r.Card.Identifier).ShouldBe(["CARD-0002", "CARD-0003"]);
    }

    [Test]
    public async Task C552_Q10_CardPinOutranksStagePinOnMutationRow()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var world = await SeedDebtAsync(db, workspace.Path);
        var stagePin = new RoutingPin
        {
            Id = Guid.NewGuid(), CardId = null, Role = AgentTaskRole.Mutation,
            Provenance = RoutingPinProvenance.Human, Strength = RoutingPinStrength.Required,
            AgentKind = AgentKind.Codex, Reason = "operator: mutation stage on Codex",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var cardPin = new RoutingPin
        {
            Id = Guid.NewGuid(), CardId = world.Companion.Id, Role = AgentTaskRole.Mutation,
            Provenance = RoutingPinProvenance.Human, Strength = RoutingPinStrength.Required,
            AgentKind = AgentKind.Grok, Reason = "operator: this battery on Grok",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.RoutingPins.AddRange(stagePin, cardPin);
        await db.SaveChangesAsync();

        MutationReady(await CreateService(db).GetAsync(CancellationToken.None))
            .ShouldHaveSingleItem().RoutingPin.ShouldNotBeNull().Id.ShouldBe(cardPin.Id);

        db.RoutingPins.Remove(cardPin);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        MutationReady(await CreateService(db).GetAsync(CancellationToken.None))
            .ShouldHaveSingleItem().RoutingPin.ShouldNotBeNull().Id.ShouldBe(stagePin.Id);
    }

    [Test]
    public async Task C552_Q11_OriginalDoneCardYieldsNoRowUnderAnyStage()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var world = await SeedDebtAsync(db, workspace.Path);
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Succeeded,
            title: "handoff on the original", cardId: world.Original.Id,
            completedAt: DateTime.UtcNow.AddMinutes(-5), nextStage: PipelineHandoffKind.Mutation);

        var dto = await CreateService(db).GetAsync(CancellationToken.None);

        dto.Stages.SelectMany(s => s.Ready).ShouldNotContain(r => r.Card.Id == world.Original.Id);
        MutationReady(dto).ShouldHaveSingleItem().OriginalCard.ShouldNotBeNull().Id.ShouldBe(world.Original.Id);
        db.ChangeTracker.Clear();
        (await db.Cards.AsNoTracking().SingleAsync(c => c.Id == world.Original.Id)).Status.ShouldBe(CardStatus.Done);
    }

    private static IReadOnlyList<Antiphon.Server.Application.Dtos.AgentTaskPipelineReadyDto> MutationReady(
        Antiphon.Server.Application.Dtos.AgentTaskPipelineDto dto) =>
        dto.Stages.Single(s => s.Role == AgentTaskRole.Mutation).Ready;

    // ---- M-4 seeds -----------------------------------------------------------------------------

    internal sealed record DebtWorld(Card Original, Card Companion, AgentTask Owner, AgentTaskLanding Operation);

    /// <summary>One original, one companion on the SAME board, one Succeeded Code owner, one op.</summary>
    private static async Task<DebtWorld> SeedDebtAsync(
        AppDbContext db,
        string directory,
        bool confirmed = true,
        bool active = true,
        DateTime? remoteConfirmedAt = null,
        CardStatus companionStatus = CardStatus.Backlog,
        bool companionArchived = false)
    {
        var original = await SeedCardAsync(db, CardStatus.Done, "CARD-0001");
        var companion = await SeedCardOnBoardAsync(db, original.BoardId, companionStatus, "CARD-0002", companionArchived);
        var owner = await SeedTaskAsync(db, directory, AgentTaskRole.Code, AgentTaskStatus.Succeeded,
            title: "owner CARD-0001", cardId: original.Id, workspace: WorkspaceMode.Worktree,
            repoPath: directory, worktreeBranch: "feat/card-0001",
            completedAt: DateTime.UtcNow.AddHours(-2));
        var op = await SeedConfirmedLandingAsync(db, owner, companion.Id,
            remoteConfirmedAt ?? DateTime.UtcNow.AddHours(-1), confirmed: confirmed, active: active);
        return new DebtWorld(original, companion, owner, op);
    }

    private static async Task<Card> SeedCardOnBoardAsync(
        AppDbContext db, Guid boardId, CardStatus status, string identifier, bool archived = false)
    {
        var now = DateTime.UtcNow;
        var column = await db.BoardColumns.FirstOrDefaultAsync(c => c.BoardId == boardId && c.CardStatus == status);
        if (column is null)
        {
            var order = await db.BoardColumns.CountAsync(c => c.BoardId == boardId);
            column = new BoardColumn
            {
                Id = Guid.NewGuid(), BoardId = boardId, StateKey = status.ToString().ToLowerInvariant(),
                Name = status.ToString(), ColumnOrder = order, CardStatus = status,
                CreatedAt = now, UpdatedAt = now,
            };
            db.BoardColumns.Add(column);
        }

        var card = new Card
        {
            Id = Guid.NewGuid(), BoardId = boardId, BoardColumnId = column.Id, Identifier = identifier,
            Title = $"{identifier} title", Description = "Companion.", Status = status,
            ArchivedAt = archived ? now : null, CreatedAt = now, UpdatedAt = now,
        };
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        return card;
    }

    /// <summary>
    /// The C448_V33 confirmed-landing shape plus <c>VerificationCardId</c>. <c>confirmed: false</c>
    /// leaves <c>RemoteConfirmedAt</c> set but the confirmation method wrong, which is exactly the
    /// state <see cref="AgentTaskLandingState.HasPublication"/> exists to reject.
    /// </summary>
    private static async Task<AgentTaskLanding> SeedConfirmedLandingAsync(
        AppDbContext db,
        AgentTask owner,
        Guid companionCardId,
        DateTime remoteConfirmedAt,
        string? verifiedSha = null,
        bool confirmed = true,
        bool active = true)
    {
        var sha = verifiedSha ?? new string('b', 40);
        var now = DateTime.UtcNow;
        var op = new AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = owner.Id, Active = active,
            Phase = LandPhase.PublicationConfirmed, Publication = LandPublicationOutcome.Landed,
            Cleanup = LandCleanupStatus.Complete, Mode = LandOperationMode.Fresh,
            OriginalSourceSha = new string('c', 40), RebasedSourceSha = sha, VerifiedSourceSha = sha,
            ObservedRemoteTargetSha = sha, TargetBeforeSha = new string('a', 40),
            TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master",
            SourceFullRef = "refs/heads/source", RepositoryPath = owner.RepoPath ?? owner.WorkingDirectory,
            CommonDirectory = owner.RepoPath ?? owner.WorkingDirectory,
            WorktreePath = owner.RepoPath ?? owner.WorkingDirectory,
            GitDirectory = owner.RepoPath ?? owner.WorkingDirectory,
            SourcePinned = true, TargetPinned = true, PreparedPinned = true, VerificationPassed = true,
            VerifiedAt = now, RemoteFingerprint = new string('a', 64),
            // Postgres keeps microseconds; truncating at the seed means every assertion compares
            // the value the row actually holds rather than a tick the database threw away.
            RemoteConfirmedAt = Truncate(remoteConfirmedAt),
            ConfirmationMethod = confirmed ? "push-endpoint-read-fetch-ancestry" : "none",
            VerificationCardId = companionCardId,
            CreatedAt = now, UpdatedAt = now,
        };
        op.RecoveryRefPrefix = $"refs/antiphon/land/{owner.Id:N}/{op.Id:N}";
        db.AgentTaskLandings.Add(op);
        await db.SaveChangesAsync();
        new AgentTaskLandingState().HasPublication(op).ShouldBe(confirmed);
        return op;
    }

    private static async Task<AgentTask> SeedSourcedTaskAsync(
        AppDbContext db,
        string directory,
        AgentTaskStatus status,
        Guid companionCardId,
        Guid operationId,
        DateTime? createdAt = null,
        DateTime? completedAt = null)
    {
        // SourceLandingOperationId is read-only after save (AppDbContext), so it has to be part of
        // the INSERT — a seed that sets it afterwards throws before it ever reaches the projection.
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "sourced battery", Goal = "sourced battery",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Mutation, Status = status,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = directory, RepoPath = directory,
            CardId = companionCardId, SourceLandingOperationId = operationId,
            SourceLandingSha = new string('b', 40),
            CreatedAt = Truncate(createdAt ?? now)!.Value,
            CompletedAt = Truncate(completedAt),
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }
}

/// <summary>CARD-0552 V-552-38: the route carries the three new ready members.</summary>
public partial class AgentTaskPipelineEndpointTests
{
    [Test]
    public async Task C552_Q12_PipelineJsonCarriesTheLandingMembers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = "c552-json", GitRepositoryUrl = "https://example.test/c552.git",
            CreatedAt = now, UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Name = "C552 json",
            MaxConcurrentSessions = 1, CreatedAt = now, UpdatedAt = now,
        };
        var columns = new[] { CardStatus.Backlog, CardStatus.Done }
            .Select((status, order) => new BoardColumn
            {
                Id = Guid.NewGuid(), BoardId = board.Id, StateKey = status.ToString().ToLowerInvariant(),
                Name = status.ToString(), ColumnOrder = order, CardStatus = status,
                CreatedAt = now, UpdatedAt = now,
            }).ToArray();
        var original = new Card
        {
            Id = Guid.NewGuid(), BoardId = board.Id,
            BoardColumnId = columns.Single(c => c.CardStatus == CardStatus.Done).Id,
            Identifier = "C552-0001", Title = "original", Status = CardStatus.Done,
            CompletedAt = now, CreatedAt = now, UpdatedAt = now,
        };
        var companion = new Card
        {
            Id = Guid.NewGuid(), BoardId = board.Id,
            BoardColumnId = columns.Single(c => c.CardStatus == CardStatus.Backlog).Id,
            Identifier = "C552-0002", Title = "Post-land verification: C552-0001",
            Status = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now,
        };
        var ownerId = Guid.NewGuid();
        var owner = new AgentTask
        {
            Id = ownerId, RootTaskId = ownerId, Title = "owner", Goal = "owner",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Status = AgentTaskStatus.Succeeded,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = @"C:\tmp\c552-json",
            RepoPath = @"C:\tmp\c552-json", WorktreeBranch = "feat/c552", CardId = original.Id,
            ProjectId = project.Id, CreatedAt = now.AddHours(-2), CompletedAt = now.AddHours(-2),
        };
        db.AddRange(project, board, original, companion, owner);
        db.AddRange(columns);
        await db.SaveChangesAsync();

        var sha = new string('b', 40);
        var op = new AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = owner.Id, Active = true,
            Phase = LandPhase.PublicationConfirmed, Publication = LandPublicationOutcome.Landed,
            Cleanup = LandCleanupStatus.Complete, Mode = LandOperationMode.Fresh,
            OriginalSourceSha = new string('c', 40), RebasedSourceSha = sha, VerifiedSourceSha = sha,
            ObservedRemoteTargetSha = sha, TargetBeforeSha = new string('a', 40),
            TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master",
            SourceFullRef = "refs/heads/source", RepositoryPath = owner.RepoPath!,
            CommonDirectory = owner.RepoPath!, WorktreePath = owner.RepoPath!, GitDirectory = owner.RepoPath!,
            SourcePinned = true, TargetPinned = true, PreparedPinned = true, VerificationPassed = true,
            VerifiedAt = now, RemoteFingerprint = new string('a', 64), RemoteConfirmedAt = now.AddHours(-1),
            ConfirmationMethod = "push-endpoint-read-fetch-ancestry", VerificationCardId = companion.Id,
            CreatedAt = now, UpdatedAt = now,
        };
        op.RecoveryRefPrefix = $"refs/antiphon/land/{owner.Id:N}/{op.Id:N}";
        db.AgentTaskLandings.Add(op);
        await db.SaveChangesAsync();

        using var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/api/agent-tasks/pipeline");

        using var document = JsonDocument.Parse(json);
        var row = document.RootElement.GetProperty("stages").EnumerateArray()
            .Single(s => s.GetProperty("role").GetString() == "Mutation")
            .GetProperty("ready").EnumerateArray()
            .Single(r => r.GetProperty("card").GetProperty("id").GetGuid() == companion.Id);
        row.GetProperty("sourceLandingOperationId").GetGuid().ShouldBe(op.Id);
        row.GetProperty("sourceLandingSha").GetString().ShouldBe(sha);
        row.GetProperty("originalCard").GetProperty("id").GetGuid().ShouldBe(original.Id);
        row.GetProperty("originalCard").GetProperty("identifier").GetString().ShouldBe("C552-0001");
    }
}
