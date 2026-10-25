using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1033 V-8. Upgrade from the mirror-binding predecessor keeps old rows and leaves the new
/// admission columns null. A second (RunId, ActionKey) is rejected by that unique index.
/// The generated migration does not delete rows, run SQL, or invent a cutoff.
/// </summary>
[Category("Integration")]
public sealed class CardCleanupSchedulingSchemaTests
{
    [Test]
    public async Task C1033_AdditiveUpgradePreservesHistory()
    {
        new WorktreeResidueSettings().Execute.ShouldBeFalse();
        new WorktreeResidueSettings().CardDoneAfterUtc.ShouldBeNull();
        var shipped = (await File.ReadAllTextAsync(Path.Combine(CheckoutRoot(), "server", "appsettings.json")))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        shipped.Contains("\"Execute\": false,\n    \"CardDoneAfterUtc\": null", StringComparison.Ordinal)
            .ShouldBeTrue();

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        await using var db = new AppDbContext(options);
        var migrations = db.Database.GetMigrations().ToArray();
        var position = Array.FindIndex(migrations, static name =>
            name.EndsWith("_AddCardCleanupScheduling", StringComparison.Ordinal));
        position.ShouldBeGreaterThan(0);
        migrations[position - 1].ShouldEndWith("_AddTaskWorktreeMirrorBinding");
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        db.Database.HasPendingModelChanges().ShouldBeFalse();

        var now = new DateTime(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc);
        var world = await SeedPredecessorAsync(db, now);
        db.ChangeTracker.Clear();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[position - 1]);
        (await ColumnCountAsync(db, "WorktreeResidueRunCandidates", "ActionKey")).ShouldBe(0);
        (await ColumnCountAsync(db, "WorktreeResidueRunCandidates", "AcceptedAt")).ShouldBe(0);
        (await ColumnCountAsync(db, "CardWorktreeCleanupEndpoints", "EvidenceDigest")).ShouldBe(0);
        (await ColumnCountAsync(db, "WorktreeResidueCandidateCursors", "EvidenceDigest")).ShouldBe(0);
        (await RowCountAsync(db, "Cards")).ShouldBe(4);
        (await RowCountAsync(db, "WorktreeResidueRunCandidates")).ShouldBe(1);

        await migrator.MigrateAsync(migrations[position]);
        db.ChangeTracker.Clear();
        (await db.Database.GetPendingMigrationsAsync()).ShouldBe(migrations.Skip(position + 1));
        // Exercise the named upgrade first, then restore the current schema for current-model reads.
        (await ColumnCountAsync(db, "WorktreeResidueRunCandidates", "ActionKey")).ShouldBe(1);
        (await ColumnCountAsync(db, "WorktreeResidueRunCandidates", "AcceptedAt")).ShouldBe(1);
        (await ColumnCountAsync(db, "CardWorktreeCleanupEndpoints", "EvidenceDigest")).ShouldBe(1);
        (await ColumnCountAsync(db, "WorktreeResidueCandidateCursors", "EvidenceDigest")).ShouldBe(1);
        await migrator.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        db.Database.HasPendingModelChanges().ShouldBeFalse();

        var active = await db.Cards.SingleAsync(c => c.Id == world.ActiveCardId);
        active.Status.ShouldBe(CardStatus.InProgress);
        active.CompletedAt.ShouldBeNull();
        active.ArchivedAt.ShouldBeNull();
        var historical = await db.Cards.SingleAsync(c => c.Id == world.HistoricalDoneCardId);
        historical.Status.ShouldBe(CardStatus.Done);
        historical.CompletedAt.ShouldBe(now.AddDays(-2));
        historical.ArchivedAt.ShouldBeNull();
        var archived = await db.Cards.SingleAsync(c => c.Id == world.ArchivedDoneCardId);
        archived.Status.ShouldBe(CardStatus.Done);
        archived.CompletedAt.ShouldBe(now.AddDays(-3));
        archived.ArchivedAt.ShouldBe(now.AddDays(-1));
        var canceled = await db.Cards.SingleAsync(c => c.Id == world.CanceledCardId);
        canceled.Status.ShouldBe(CardStatus.Canceled);
        canceled.TerminalReason.ShouldBe("fixture canceled");
        (await db.CardRevisions.CountAsync(r => r.CardId == historical.Id && r.Id == world.HistoricalRevisionId))
            .ShouldBe(1);

        var run = await db.WorktreeResidueRuns.SingleAsync(r => r.Id == world.RunId);
        run.Execute.ShouldBeFalse();
        run.Preview.ShouldBeTrue();
        run.ActionsAccepted.ShouldBe(0);
        run.ActionBudget.ShouldBe(25);
        var storedCandidate = await db.WorktreeResidueRunCandidates.SingleAsync(c => c.Id == world.CandidateId);
        storedCandidate.Lane.ShouldBe("legacy");
        storedCandidate.Outcome.ShouldBe("Held");
        storedCandidate.ReasonCode.ShouldBe("historical_inventory");
        storedCandidate.TaskId.ShouldBe(world.TaskId);
        storedCandidate.RetirementId.ShouldBe(world.RetirementId);
        storedCandidate.LandingOperationId.ShouldBe(world.LandingOperationId);
        storedCandidate.LandRequestId.ShouldBe(world.LandRequestId);
        storedCandidate.Path.ShouldBe("/tmp/c1033-historical");
        storedCandidate.Branch.ShouldBe("feat/card-task-historical");
        storedCandidate.DirectoryRemoved.ShouldBe(false);
        storedCandidate.RegistrationRemoved.ShouldBe(false);
        storedCandidate.BranchRemoved.ShouldBe(false);
        storedCandidate.EvaluatedAt.ShouldBe(now.AddHours(-4));
        var historicalAcceptedRows = await db.WorktreeResidueRunCandidates.AsNoTracking()
            .Where(c => c.Id == world.CandidateId && (c.ActionKey != null || c.AcceptedAt != null))
            .Select(c => c.Id)
            .ToListAsync();
        historicalAcceptedRows.ShouldBeEmpty();
        var schedulingMigration = await File.ReadAllTextAsync(Path.Combine(
            CheckoutRoot(),
            "server",
            "Migrations",
            "20261012112137_AddCardCleanupScheduling.cs"));
        schedulingMigration.Contains("DELETE FROM", StringComparison.Ordinal).ShouldBeFalse();
        schedulingMigration.Contains("CardDoneAfterUtc", StringComparison.Ordinal).ShouldBeFalse();
        schedulingMigration.Contains(".Sql(", StringComparison.Ordinal).ShouldBeFalse();
        storedCandidate.ActionKey.ShouldBeNull();
        storedCandidate.AcceptedAt.ShouldBeNull();
        storedCandidate.CardId.ShouldBeNull();
        storedCandidate.DoneRevisionId.ShouldBeNull();
        storedCandidate.TaskAttempt.ShouldBeNull();
        storedCandidate.TargetId.ShouldBeNull();
        storedCandidate.EndpointId.ShouldBeNull();
        storedCandidate.EndpointIdentity.ShouldBeNull();
        storedCandidate.RunnerId.ShouldBeNull();
        storedCandidate.RunnerStoreId.ShouldBeNull();
        storedCandidate.OperationId.ShouldBeNull();
        storedCandidate.RequestDigest.ShouldBeNull();
        storedCandidate.EndpointState.ShouldBeNull();
        storedCandidate.Retryable.ShouldBeNull();
        storedCandidate.NextAttemptAt.ShouldBeNull();

        var endpoint = await db.CardWorktreeCleanupEndpoints.SingleAsync(e => e.Id == world.EndpointId);
        endpoint.OperationId.ShouldBe(world.OperationId);
        endpoint.RequestDigest.ShouldBe(world.RequestDigest);
        endpoint.ImmutableRequest.ShouldNotBeNull();
        endpoint.ImmutableRequest!.SequenceEqual(world.CommandBytes).ShouldBeTrue();
        endpoint.ImportedReceipt.ShouldNotBeNull();
        endpoint.ImportedReceipt!.SequenceEqual(world.ReceiptBytes).ShouldBeTrue();
        endpoint.MirrorBindingId.ShouldBe(world.BindingId);
        endpoint.ConcurrencyToken.ShouldBe(world.EndpointToken);
        endpoint.State.ShouldBe(CardWorktreeCleanupEndpointState.IntentRecorded);
        endpoint.Attempts.ShouldBe(2);
        endpoint.Retryable.ShouldBeTrue();
        endpoint.NextAttemptAt.ShouldBe(now.AddHours(24));
        endpoint.EvidenceDigest.ShouldBeNull();
        endpoint.EndpointIdentity.ShouldBe("local");
        var binding = await db.TaskWorktreeMirrorBindings.SingleAsync(b => b.Id == world.BindingId);
        binding.TaskId.ShouldBe(world.TaskId);
        binding.Attempt.ShouldBe(1);
        binding.PreparationId.ShouldBe(world.PreparationId);
        binding.RunnerId.ShouldBe("runner-a");
        binding.RunnerStoreId.ShouldBe("store-a");
        binding.BindingId.ShouldBe(world.BindingKey);
        binding.RepositoryIdentity.ShouldBe("/tmp/c1033-repo");
        binding.CreationBaselineSha.ShouldBe(new string('a', 40));
        binding.WorktreePath.ShouldBe("/tmp/c1033-mirror");
        var cursor = await db.WorktreeResidueCandidateCursors.SingleAsync(c => c.Id == world.CursorId);
        cursor.CandidateKey.ShouldBe("legacy-cursor");
        cursor.LastEvaluatedAt.ShouldBe(now.AddHours(-4));
        cursor.NotBefore.ShouldBe(now.AddHours(20));
        cursor.EvidenceDigest.ShouldBeNull();
        (await db.Cards.CountAsync()).ShouldBe(4);
        (await db.WorktreeResidueRuns.CountAsync()).ShouldBe(1);
        (await db.WorktreeResidueRunCandidates.CountAsync()).ShouldBe(1);
        (await db.CardWorktreeCleanupEndpoints.CountAsync()).ShouldBe(1);
        (await db.TaskWorktreeMirrorBindings.CountAsync()).ShouldBe(1);
        (await db.WorktreeResidueCandidateCursors.CountAsync()).ShouldBe(1);

        var admittedAt = now.AddMinutes(5);
        var roundTrip = new WorktreeResidueRunCandidate
        {
            Id = Guid.NewGuid(),
            RunId = world.RunId,
            Lane = "card-done",
            Outcome = "Deferred",
            ReasonCode = "roundtrip",
            EvaluatedAt = admittedAt,
            ActionKey = "roundtrip-key",
            AcceptedAt = admittedAt,
            CardId = world.HistoricalDoneCardId,
            DoneRevisionId = world.HistoricalRevisionId,
            TaskAttempt = 4,
            TargetId = world.TargetId,
            EndpointId = world.EndpointId,
            EndpointIdentity = "mirror-a",
            RunnerId = "runner-a",
            RunnerStoreId = "store-a",
            OperationId = world.OperationId,
            RequestDigest = world.RequestDigest,
            EndpointState = CardWorktreeCleanupEndpointState.Pending,
            Retryable = false,
            NextAttemptAt = admittedAt.AddHours(24),
        };
        db.WorktreeResidueRunCandidates.Add(roundTrip);
        endpoint.EvidenceDigest = new string('e', 64);
        cursor.EvidenceDigest = new string('c', 64);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var reloaded = await db.WorktreeResidueRunCandidates.SingleAsync(c => c.Id == roundTrip.Id);
        reloaded.ActionKey.ShouldBe("roundtrip-key");
        reloaded.AcceptedAt.ShouldBe(admittedAt);
        reloaded.CardId.ShouldBe(world.HistoricalDoneCardId);
        reloaded.DoneRevisionId.ShouldBe(world.HistoricalRevisionId);
        reloaded.TaskAttempt.ShouldBe(4);
        reloaded.TargetId.ShouldBe(world.TargetId);
        reloaded.EndpointId.ShouldBe(world.EndpointId);
        reloaded.EndpointIdentity.ShouldBe("mirror-a");
        reloaded.RunnerId.ShouldBe("runner-a");
        reloaded.RunnerStoreId.ShouldBe("store-a");
        reloaded.OperationId.ShouldBe(world.OperationId);
        reloaded.RequestDigest.ShouldBe(world.RequestDigest);
        reloaded.EndpointState.ShouldBe(CardWorktreeCleanupEndpointState.Pending);
        reloaded.Retryable.ShouldBe(false);
        reloaded.NextAttemptAt.ShouldBe(admittedAt.AddHours(24));
        (await db.CardWorktreeCleanupEndpoints.SingleAsync(e => e.Id == world.EndpointId))
            .EvidenceDigest.ShouldBe(new string('e', 64));
        (await db.WorktreeResidueCandidateCursors.SingleAsync(c => c.Id == world.CursorId))
            .EvidenceDigest.ShouldBe(new string('c', 64));
        (await db.WorktreeResidueRunCandidates.SingleAsync(c => c.Id == world.CandidateId))
            .ActionKey.ShouldBeNull();

        var otherRun = new WorktreeResidueRun
        {
            Id = Guid.NewGuid(),
            StartedAt = admittedAt,
            Execute = false,
            Preview = true,
            ActionBudget = 25,
            ActionsAccepted = 0,
        };
        db.WorktreeResidueRuns.Add(otherRun);
        db.WorktreeResidueRunCandidates.Add(Candidate(world.RunId, "shared-action", admittedAt));
        db.WorktreeResidueRunCandidates.Add(Candidate(otherRun.Id, "shared-action", admittedAt));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.WorktreeResidueRunCandidates.Add(Candidate(world.RunId, "shared-action", admittedAt));
        var duplicateAccepted = true;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException postgres
            && postgres.SqlState == PostgresErrorCodes.UniqueViolation
            && postgres.ConstraintName == "IX_WorktreeResidueRunCandidates_RunId_ActionKey")
        {
            duplicateAccepted = false;
        }
        duplicateAccepted.ShouldBeFalse();
    }

    private static WorktreeResidueRunCandidate Candidate(Guid runId, string actionKey, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        RunId = runId,
        Lane = "card-done",
        Outcome = "Accepted",
        ReasonCode = "admitted",
        EvaluatedAt = at,
        ActionKey = actionKey,
        AcceptedAt = at,
    };

    private static Task<int> RowCountAsync(AppDbContext db, string table)
    {
        var sql = table switch
        {
            "Cards" => """SELECT COUNT(*)::int AS "Value" FROM "Cards" """,
            "WorktreeResidueRunCandidates" => """SELECT COUNT(*)::int AS "Value" FROM "WorktreeResidueRunCandidates" """,
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        return db.Database.SqlQueryRaw<int>(sql).SingleAsync();
    }

    private static async Task<int> ColumnCountAsync(AppDbContext db, string table, string column) =>
        await db.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*)::int AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = {0} AND column_name = {1}
            """, table, column).SingleAsync();

    private static async Task<Seed> SeedPredecessorAsync(AppDbContext db, DateTime now)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "c1033 " + Guid.NewGuid().ToString("N"),
            GitRepositoryUrl = "https://example.test/c1033.git",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "c1033",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var activeColumn = Column(board.Id, "in-progress", "In progress", CardStatus.InProgress, 0, now);
        var doneColumn = Column(board.Id, "done", "Done", CardStatus.Done, 1, now);
        doneColumn.IsTerminal = true;
        var canceledColumn = Column(board.Id, "canceled", "Canceled", CardStatus.Canceled, 2, now);
        canceledColumn.IsTerminal = true;
        var active = Card(board.Id, activeColumn.Id, "CARD-1033A", "active", CardStatus.InProgress, now);
        var historical = Card(board.Id, doneColumn.Id, "CARD-1033H", "historical done", CardStatus.Done, now);
        historical.CompletedAt = now.AddDays(-2);
        var archived = Card(board.Id, doneColumn.Id, "CARD-1033R", "archived done", CardStatus.Done, now);
        archived.CompletedAt = now.AddDays(-3);
        archived.ArchivedAt = now.AddDays(-1);
        archived.ArchivedReason = "fixture archive";
        var canceled = Card(board.Id, canceledColumn.Id, "CARD-1033C", "canceled", CardStatus.Canceled, now);
        canceled.TerminalReason = "fixture canceled";
        var revision = new CardRevision
        {
            Id = Guid.NewGuid(),
            CardId = historical.Id,
            RevisionNumber = 1,
            Kind = CardRevisionKind.Move,
            FromStatus = CardStatus.Review,
            ToStatus = CardStatus.Done,
            ToColumnId = doneColumn.Id,
            CreatedAt = now.AddDays(-2),
        };
        historical.RevisionCount = 1;
        var taskId = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            CardId = historical.Id,
            Title = "historical",
            Goal = "historical",
            Role = AgentTaskRole.Code,
            Kind = AgentTaskKind.Worker,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = "/tmp/c1033-historical",
            RepoPath = "/tmp/c1033-repo",
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            Attempt = 1,
            CreatedAt = now.AddDays(-3),
            CompletedAt = now.AddDays(-2),
        };
        var cleanup = new CardWorktreeCleanup
        {
            Id = Guid.NewGuid(),
            CardId = historical.Id,
            DoneRevisionId = revision.Id,
            DoneAt = now.AddDays(-2),
            CreatedAt = now.AddDays(-2),
        };
        var target = new CardWorktreeCleanupTarget
        {
            Id = Guid.NewGuid(),
            CleanupId = cleanup.Id,
            TaskId = taskId,
            TaskAttempt = 1,
            WorkspaceIdentity = "workspace-a",
            RepositoryPath = "/tmp/c1033-repo",
            WorktreePath = "/tmp/c1033-historical",
            SourceFullRef = "refs/heads/feat/card-task-historical",
            CreatedAt = now.AddDays(-2),
        };
        var preparationId = Guid.NewGuid();
        var bindingKey = Guid.NewGuid();
        var binding = new TaskWorktreeMirrorBinding
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            Attempt = 1,
            PreparationId = preparationId,
            RunnerId = "runner-a",
            RunnerStoreId = "store-a",
            BindingId = bindingKey,
            RepositoryIdentity = "/tmp/c1033-repo",
            CommonDirectory = "/tmp/c1033-repo/.git",
            GitDirectory = "/tmp/c1033-repo/.git/worktrees/historical",
            WorktreePath = "/tmp/c1033-mirror",
            SourceFullRef = "refs/heads/feat/card-task-historical",
            CreationBaselineSha = new string('a', 40),
            CreatedAt = now.AddDays(-3),
        };
        var operationId = Guid.NewGuid();
        var endpointToken = Guid.NewGuid();
        var command = new byte[] { 0xC1, 0x03, 0x33 };
        var receipt = new byte[] { 0x10, 0x32, 0x01 };
        var endpoint = new CardWorktreeCleanupEndpoint
        {
            Id = Guid.NewGuid(),
            TargetId = target.Id,
            EndpointIdentity = "local",
            RunnerId = "runner-a",
            RunnerStoreId = "store-a",
            RepositoryPath = "/tmp/c1033-repo",
            WorktreePath = "/tmp/c1033-historical",
            SourceFullRef = "refs/heads/feat/card-task-historical",
            SourceSha = new string('b', 40),
            OperationId = operationId,
            RequestDigest = new string('d', 64),
            ImmutableRequest = command,
            ImportedReceipt = receipt,
            MirrorBindingId = binding.Id,
            State = CardWorktreeCleanupEndpointState.IntentRecorded,
            Attempts = 2,
            Retryable = true,
            NextAttemptAt = now.AddHours(24),
            IntentAt = now.AddHours(-5),
            UpdatedAt = now.AddHours(-4),
            ConcurrencyToken = endpointToken,
        };
        var retirementId = Guid.NewGuid();
        var landingOperationId = Guid.NewGuid();
        var landRequestId = Guid.NewGuid();
        var run = new WorktreeResidueRun
        {
            Id = Guid.NewGuid(),
            StartedAt = now.AddHours(-4),
            Execute = false,
            Preview = true,
            ActionBudget = 25,
            ActionsAccepted = 0,
            Candidates = 1,
            Held = 1,
        };
        var candidate = new WorktreeResidueRunCandidate
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            Lane = "legacy",
            Outcome = "Held",
            ReasonCode = "historical_inventory",
            TaskId = taskId,
            RetirementId = retirementId,
            LandingOperationId = landingOperationId,
            LandRequestId = landRequestId,
            Path = "/tmp/c1033-historical",
            Branch = "feat/card-task-historical",
            DirectoryRemoved = false,
            RegistrationRemoved = false,
            BranchRemoved = false,
            EvaluatedAt = now.AddHours(-4),
        };
        var cursor = new WorktreeResidueCandidateCursor
        {
            Id = Guid.NewGuid(),
            CandidateKey = "legacy-cursor",
            LastEvaluatedAt = now.AddHours(-4),
            NotBefore = now.AddHours(20),
        };
        db.AddRange(project, board, activeColumn, doneColumn, canceledColumn, active, historical, archived, canceled,
            revision, task, cleanup, target, binding, endpoint, run, candidate, cursor);
        await db.SaveChangesAsync();
        return new Seed(active.Id, historical.Id, archived.Id, canceled.Id, revision.Id, taskId, run.Id,
            candidate.Id, endpoint.Id, target.Id, binding.Id, cursor.Id, operationId, preparationId, bindingKey,
            endpointToken, retirementId, landingOperationId, landRequestId, new string('d', 64), command, receipt);
    }

    private static BoardColumn Column(Guid boardId, string key, string name, CardStatus status, int order, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            StateKey = key,
            Name = name,
            CardStatus = status,
            ColumnOrder = order,
            CreatedAt = now,
            UpdatedAt = now,
        };

    private static Card Card(Guid boardId, Guid columnId, string identifier, string title, CardStatus status, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            BoardColumnId = columnId,
            Identifier = identifier,
            Title = title,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyToken = Guid.NewGuid(),
        };

    private static string CheckoutRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Antiphon.sln"))) root = root.Parent;
        root.ShouldNotBeNull();
        return root!.FullName;
    }

    private sealed record Seed(
        Guid ActiveCardId,
        Guid HistoricalDoneCardId,
        Guid ArchivedDoneCardId,
        Guid CanceledCardId,
        Guid HistoricalRevisionId,
        Guid TaskId,
        Guid RunId,
        Guid CandidateId,
        Guid EndpointId,
        Guid TargetId,
        Guid BindingId,
        Guid CursorId,
        Guid OperationId,
        Guid PreparationId,
        Guid BindingKey,
        Guid EndpointToken,
        Guid RetirementId,
        Guid LandingOperationId,
        Guid LandRequestId,
        string RequestDigest,
        byte[] CommandBytes,
        byte[] ReceiptBytes);
}
