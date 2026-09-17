using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0552 V-552-10..19. The writer against a real schema, called exactly as the land terminal
/// calls it: inside a transaction, under the owner's row lock.
/// </summary>
[Category("Integration")]
public sealed class PostLandVerificationCompanionsTests
{
    private static readonly DateTime Now = new(2100, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task C552_W01_FreshOperationCreatesCompanion()
    {
        await using var world = await World.CreateAsync();
        var op = await world.AddOperationAsync();

        var result = await world.EnsureAsync(op);

        result.Created.ShouldBeTrue();
        result.Linked.ShouldBeTrue();
        await using var db = world.Context();
        var companion = await db.Cards.SingleAsync(c => c.Id == result.CardId);
        companion.Identifier.ShouldBe("CARD-0002");
        companion.Title.ShouldBe("Post-land verification: CARD-0001");
        companion.Status.ShouldBe(CardStatus.Backlog);
        companion.BoardColumnId.ShouldBe(world.BacklogColumnId);
        BoardService.ParseLabels(companion.LabelsJson).ShouldBe(["post-land-verification"]);
        companion.Importance.ShouldBe(CardImportance.Normal);
        companion.ImportanceProvenance.ShouldBe(CardImportanceProvenance.Auto);
        companion.Description.ShouldStartWith(
            "post-land-verification:" + world.OwnerId.ToString("D") + "\n");
        companion.Description.ShouldContain("O: " + op.Id.ToString("D"));
        (await db.AgentTaskLandings.SingleAsync(o => o.Id == op.Id)).VerificationCardId.ShouldBe(companion.Id);

        var companionRevision = (await db.CardRevisions.Where(r => r.CardId == companion.Id).ToListAsync())
            .ShouldHaveSingleItem();
        companionRevision.Kind.ShouldBe(CardRevisionKind.ContentEdit);
        companionRevision.Reason.ShouldBe("Confirmed publication " + op.Id.ToString("N"));
        companionRevision.EditedBy.ShouldBe("land");

        var originalRevision = (await db.CardRevisions.Where(r => r.CardId == world.OriginalCardId).ToListAsync())
            .ShouldHaveSingleItem();
        originalRevision.Reason.ShouldBe("Post-land verification companion");
        originalRevision.EditedBy.ShouldBe("land");
        (await db.Cards.SingleAsync(c => c.Id == world.OriginalCardId)).Description
            .ShouldEndWith($"\nPost-land verification: CARD-0002 ({companion.Id:D})");
    }

    [Test]
    public async Task C552_W02_LinkByOwnerFkOutranksDescriptionKey()
    {
        await using var world = await World.CreateAsync();
        var first = await world.AddOperationAsync();
        var initial = await world.EnsureAsync(first);
        await using (var edit = world.Context())
        {
            var op1 = await edit.AgentTaskLandings.SingleAsync(o => o.Id == first.Id);
            op1.Active = false;
            var card = await edit.Cards.SingleAsync(c => c.Id == initial.CardId);
            card.Description = "edited by a human";
            await edit.SaveChangesAsync();
        }

        var second = await world.AddOperationAsync(active: true);
        var result = await world.EnsureAsync(second);

        result.Created.ShouldBeFalse();
        result.Linked.ShouldBeTrue();
        result.CardId.ShouldBe(initial.CardId);
        await using var db = world.Context();
        (await db.AgentTaskLandings.SingleAsync(o => o.Id == second.Id)).VerificationCardId.ShouldBe(initial.CardId);
        var revisions = await db.CardRevisions.Where(r => r.CardId == initial.CardId)
            .OrderBy(r => r.RevisionNumber).ToListAsync();
        revisions.Count.ShouldBe(2);
        revisions[^1].Reason.ShouldBe("Confirmed publication " + second.Id.ToString("N"));
        (await db.Cards.SingleAsync(c => c.Id == initial.CardId)).Description
            .ShouldContain("O: " + second.Id.ToString("D"));
    }

    [Test]
    public async Task C552_W03_CallerCreatedCompanionIsLinkedAndAppended()
    {
        await using var world = await World.CreateAsync();
        var key = PostLandVerificationCompanions.StableKey(world.OwnerId);
        var callerText = key + "\npublication pending\ncaller notes";
        var callerCard = await world.AddCardAsync("CARD-0002", CardStatus.Backlog, callerText);
        var op = await world.AddOperationAsync();

        var result = await world.EnsureAsync(op);

        result.Created.ShouldBeFalse();
        result.Linked.ShouldBeTrue();
        result.CardId.ShouldBe(callerCard);
        await using var db = world.Context();
        var card = await db.Cards.SingleAsync(c => c.Id == callerCard);
        card.Description.ShouldStartWith(callerText);
        card.Description.ShouldContain("O: " + op.Id.ToString("D"));
        (await db.CardRevisions.Where(r => r.CardId == callerCard).ToListAsync())
            .ShouldHaveSingleItem().Description.ShouldBe(callerText);
        (await db.Cards.CountAsync(c => c.BoardId == world.BoardId)).ShouldBe(2);
    }

    [Test]
    public async Task C552_W04_KeyOnAnotherBoardIsNotLinked()
    {
        await using var world = await World.CreateAsync();
        var key = PostLandVerificationCompanions.StableKey(world.OwnerId);
        var (otherBoard, otherCard) = await world.AddCardOnAnotherBoardAsync(key + "\npublication pending");
        var op = await world.AddOperationAsync();

        var result = await world.EnsureAsync(op);

        result.Created.ShouldBeTrue();
        await using var db = world.Context();
        (await db.Cards.SingleAsync(c => c.Id == otherCard)).Description
            .ShouldBe(key + "\npublication pending");
        (await db.Cards.CountAsync(c => c.BoardId == otherBoard)).ShouldBe(1);
    }

    [Test]
    [Arguments("done")]
    [Arguments("canceled")]
    [Arguments("archived")]
    public async Task C552_W05_ClosedCompanionIsSuperseded(string variant)
    {
        await using var world = await World.CreateAsync();
        var first = await world.AddOperationAsync();
        var initial = await world.EnsureAsync(first);
        await using (var close = world.Context())
        {
            var card = await close.Cards.SingleAsync(c => c.Id == initial.CardId);
            if (variant == "archived") card.ArchivedAt = Now;
            else card.Status = variant == "done" ? CardStatus.Done : CardStatus.Canceled;
            var op1 = await close.AgentTaskLandings.SingleAsync(o => o.Id == first.Id);
            op1.Active = false;
            await close.SaveChangesAsync();
        }

        var second = await world.AddOperationAsync(active: true);
        var result = await world.EnsureAsync(second);

        result.Created.ShouldBeTrue();
        await using var db = world.Context();
        var created = await db.Cards.SingleAsync(c => c.Id == result.CardId);
        created.Identifier.ShouldBe("CARD-0003");
        var lines = created.Description.Split('\n');
        lines[0].ShouldBe(PostLandVerificationCompanions.StableKey(world.OwnerId));
        lines[1].ShouldBe($"Supersedes: CARD-0002 ({initial.CardId:D})");
        (await db.AgentTaskLandings.SingleAsync(o => o.Id == second.Id)).VerificationCardId.ShouldBe(created.Id);

        var old = await db.Cards.SingleAsync(c => c.Id == initial.CardId);
        old.ArchivedAt.ShouldBe(variant == "archived" ? Now : null);
        old.Status.ShouldBe(variant switch
        {
            "done" => CardStatus.Done,
            "canceled" => CardStatus.Canceled,
            _ => CardStatus.Backlog,
        });
        old.Description.ShouldContain("O: " + first.Id.ToString("D"));
        old.Description.ShouldNotContain("O: " + second.Id.ToString("D"));
    }

    [Test]
    public async Task C552_W06_AlreadyLinkedWritesNothing()
    {
        await using var world = await World.CreateAsync();
        var op = await world.AddOperationAsync();
        var first = await world.EnsureAsync(op);
        var revisionsBefore = await world.RevisionCountAsync();

        var result = await world.EnsureAsync(op, assertNoChanges: true);

        result.ShouldBe(new PostLandVerificationCompanions.Result(first.CardId, "CARD-0002", false, false));
        (await world.RevisionCountAsync()).ShouldBe(revisionsBefore);
    }

    [Test]
    public async Task C552_W07_UnboundOwnerThrows()
    {
        await using var world = await World.CreateAsync(bindCard: false);
        var op = await world.AddOperationAsync();

        var error = await Should.ThrowAsync<InvalidOperationException>(() => world.EnsureAsync(op));

        error.Message.ShouldBe("post_land_companion_requires_card");
        await using var db = world.Context();
        (await db.Cards.CountAsync(c => c.BoardId == world.BoardId)).ShouldBe(1);
    }

    [Test]
    public async Task C552_W08_LinkedCompanionCardCannotBeDeleted()
    {
        await using var world = await World.CreateAsync();
        var op = await world.AddOperationAsync();
        var result = await world.EnsureAsync(op);

        await using var db = world.Context();
        db.Cards.Remove(await db.Cards.SingleAsync(c => c.Id == result.CardId));
        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        ((PostgresException)error.InnerException!).SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Test]
    public async Task C552_W09_MigrationAddsVerificationCardColumnAndIndex()
    {
        var name = "test_c552_upgrade_" + Guid.NewGuid().ToString("N");
        await using (var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString))
        {
            await maintenance.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", maintenance);
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(TestDbFixture.ConnectionString) { Database = name }.ConnectionString;
        await using var owned = new IsolatedTestSchema(name, connection);
        var options = TestDbFixture.CreateDbContextOptions(connection);
        await using var db = new AppDbContext(options);
        var migrations = db.Database.GetMigrations().ToArray();
        migrations.Count(m => m.EndsWith("_AddLandingVerificationCard", StringComparison.Ordinal)).ShouldBe(1);

        await db.GetService<IMigrator>().MigrateAsync();

        var columns = await db.Database.SqlQuery<string>(
            $"""SELECT "column_name" AS "Value" FROM information_schema.columns WHERE "table_name" = 'AgentTaskLandings'""")
            .ToListAsync();
        columns.ShouldContain("VerificationCardId");
        var indexes = await db.Database.SqlQuery<string>(
            $"""SELECT "indexname" AS "Value" FROM pg_indexes WHERE "tablename" = 'AgentTaskLandings'""")
            .ToListAsync();
        indexes.ShouldContain("IX_AgentTaskLandings_VerificationCardId");

        var taskId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId, CreatedAt = Now });
        await db.SaveChangesAsync();
        db.AgentTaskLandings.Add(new AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = taskId, CreatedAt = Now, UpdatedAt = Now,
            VerificationCardId = Guid.NewGuid(),
        });
        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        ((PostgresException)error.InnerException!).SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Test]
    public async Task C552_W10_IdentifierIsAllocatedFromTheBoardsHighest()
    {
        await using var world = await World.CreateAsync();
        await world.AddCardAsync("CARD-0007", CardStatus.Backlog, "unrelated");
        var op = await world.AddOperationAsync();

        var result = await world.EnsureAsync(op);

        result.Identifier.ShouldBe("CARD-0008");
    }

    /// <summary>
    /// A project, a board with Backlog(0)/InProgress(1)/Review(2)/Done(3)/Canceled(4) columns, a
    /// Done original `CARD-0001` and a Succeeded Code owner bound to it.
    /// </summary>
    private sealed class World : IAsyncDisposable
    {
        private IsolatedTestSchema _schema = null!;
        private DbContextOptions<AppDbContext> _options = null!;

        public Guid BoardId { get; private set; }
        public Guid OriginalCardId { get; private set; }
        public Guid OwnerId { get; private set; }
        public Guid BacklogColumnId { get; private set; }
        public string RepoPath { get; } = Path.Combine(Path.GetTempPath(), "c552-" + Guid.NewGuid().ToString("N"));

        public static async Task<World> CreateAsync(bool bindCard = true)
        {
            var world = new World();
            world._schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            world._options = TestDbFixture.CreateDbContextOptions(world._schema.ConnectionString);
            await using var db = world.Context();
            var project = new Project
            {
                Id = Guid.NewGuid(), Name = "c552", GitRepositoryUrl = "https://example.test/c552.git",
                CreatedAt = Now, UpdatedAt = Now,
            };
            var board = new Board
            {
                Id = Guid.NewGuid(), ProjectId = project.Id, Name = "C552",
                MaxConcurrentSessions = 1, CreatedAt = Now, UpdatedAt = Now,
            };
            var columns = new[] { CardStatus.Backlog, CardStatus.Done }
                .Select((status, order) => new BoardColumn
                {
                    Id = Guid.NewGuid(), BoardId = board.Id, StateKey = status.ToString().ToLowerInvariant(),
                    Name = status.ToString(), ColumnOrder = order, CardStatus = status,
                    CreatedAt = Now, UpdatedAt = Now,
                }).ToArray();
            var original = new Card
            {
                Id = Guid.NewGuid(), BoardId = board.Id,
                BoardColumnId = columns.Single(c => c.CardStatus == CardStatus.Done).Id,
                Identifier = "CARD-0001", Title = "CARD-0001 title", Description = "The original.",
                Status = CardStatus.Done, CompletedAt = Now, TerminalReason = "closed by fixture",
                CreatedAt = Now, UpdatedAt = Now,
            };
            var owner = new AgentTask
            {
                Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "owner", Goal = "fixture",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
                Status = AgentTaskStatus.Succeeded, CardId = bindCard ? original.Id : null,
                ProjectId = project.Id, WorkingDirectory = world.RepoPath, RepoPath = world.RepoPath,
                CreatedAt = Now, CompletedAt = Now,
            };
            owner.RootTaskId = owner.Id;
            db.AddRange(project, board, original, owner);
            db.AddRange(columns);
            await db.SaveChangesAsync();

            world.BoardId = board.Id;
            world.OriginalCardId = original.Id;
            world.OwnerId = owner.Id;
            world.BacklogColumnId = columns.Single(c => c.CardStatus == CardStatus.Backlog).Id;
            return world;
        }

        public AppDbContext Context() => new(_options);

        public async Task<AgentTaskLanding> AddOperationAsync(bool active = true)
        {
            await using var db = Context();
            var op = new AgentTaskLanding
            {
                Id = Guid.NewGuid(), TaskId = OwnerId, Active = active,
                Phase = LandPhase.PublicationConfirmed, Publication = LandPublicationOutcome.Landed,
                Cleanup = LandCleanupStatus.Complete, Mode = LandOperationMode.Fresh,
                // C (reviewed) differs from L (verified) because the source was rebased, which is
                // the shape the description's "Reviewed C" / "L=" lines are there to distinguish.
                OriginalSourceSha = new string('c', 40), RebasedSourceSha = new string('b', 40),
                VerifiedSourceSha = new string('b', 40),
                ObservedRemoteTargetSha = new string('b', 40), TargetBeforeSha = new string('a', 40),
                TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master",
                SourceFullRef = "refs/heads/source", RepositoryPath = RepoPath, CommonDirectory = RepoPath,
                WorktreePath = RepoPath, GitDirectory = RepoPath, SourcePinned = true, TargetPinned = true,
                PreparedPinned = true, VerificationPassed = true, VerifiedAt = Now,
                RemoteFingerprint = new string('a', 64), RemoteConfirmedAt = Now,
                ConfirmationMethod = "push-endpoint-read-fetch-ancestry",
                CreatedAt = Now, UpdatedAt = Now,
            };
            op.RecoveryRefPrefix = $"refs/antiphon/land/{OwnerId:N}/{op.Id:N}";
            db.AgentTaskLandings.Add(op);
            await db.SaveChangesAsync();
            new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
            return op;
        }

        public async Task<Guid> AddCardAsync(string identifier, CardStatus status, string description)
        {
            await using var db = Context();
            var column = await db.BoardColumns.FirstAsync(c => c.BoardId == BoardId && c.CardStatus == status);
            var card = new Card
            {
                Id = Guid.NewGuid(), BoardId = BoardId, BoardColumnId = column.Id, Identifier = identifier,
                Title = identifier, Description = description, Status = status,
                CreatedAt = Now.AddMinutes(-5), UpdatedAt = Now.AddMinutes(-5),
            };
            db.Cards.Add(card);
            await db.SaveChangesAsync();
            return card.Id;
        }

        public async Task<(Guid BoardId, Guid CardId)> AddCardOnAnotherBoardAsync(string description)
        {
            await using var db = Context();
            var project = await db.Projects.FirstAsync();
            var board = new Board
            {
                Id = Guid.NewGuid(), ProjectId = project.Id, Name = "Other",
                MaxConcurrentSessions = 1, CreatedAt = Now, UpdatedAt = Now,
            };
            var column = new BoardColumn
            {
                Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
                ColumnOrder = 0, CardStatus = CardStatus.Backlog, CreatedAt = Now, UpdatedAt = Now,
            };
            var card = new Card
            {
                Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
                Identifier = "OTHER-0001", Title = "elsewhere", Description = description,
                Status = CardStatus.Backlog, CreatedAt = Now, UpdatedAt = Now,
            };
            db.AddRange(board, column, card);
            await db.SaveChangesAsync();
            return (board.Id, card.Id);
        }

        /// <summary>Exactly the land terminal's shape: one transaction, the owner's row lock.</summary>
        public async Task<PostLandVerificationCompanions.Result> EnsureAsync(
            AgentTaskLanding operation, bool assertNoChanges = false)
        {
            await using var db = Context();
            await using var tx = await db.Database.BeginTransactionAsync();
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == OwnerId);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {OwnerId} FOR UPDATE");
            var op = await db.AgentTaskLandings.SingleAsync(o => o.Id == operation.Id);
            var writer = new PostLandVerificationCompanions(
                db, new FakeTimeProvider(new DateTimeOffset(Now)),
                NullLogger<PostLandVerificationCompanions>.Instance);

            var result = await writer.EnsureAsync(owner, op, Now, CancellationToken.None);

            if (assertNoChanges) db.ChangeTracker.HasChanges().ShouldBeFalse();
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return result;
        }

        public async Task<int> RevisionCountAsync()
        {
            await using var db = Context();
            return await db.CardRevisions.CountAsync();
        }

        public async ValueTask DisposeAsync() => await _schema.DisposeAsync();
    }
}
