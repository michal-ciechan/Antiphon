using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class CardWorktreeMirrorBindingTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public async Task C1032_SchemaUniqueBindings()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        Guid taskId;
        await using (var setup = new AppDbContext(options))
        {
            taskId = Guid.NewGuid();
            setup.AgentTasks.Add(TaskRow(taskId, AgentTaskStatus.Working, "/mirrors/active"));
            await setup.SaveChangesAsync();
        }

        var preparationId = Guid.NewGuid();
        var inserted = 0;
        var rejected = 0;
        var gate = new Barrier(2);
        var workers = Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
        {
            await using var db = new AppDbContext(options);
            db.TaskWorktreeMirrorBindings.Add(Binding(taskId, preparationId, Guid.NewGuid(), "store-" + index));
            gate.SignalAndWait();
            try
            {
                await db.SaveChangesAsync();
                Interlocked.Increment(ref inserted);
            }
            catch (DbUpdateException)
            {
                Interlocked.Increment(ref rejected);
            }
        })).ToArray();
        await Task.WhenAll(workers);
        inserted.ShouldBe(1);
        rejected.ShouldBe(1);

        await using var observer = new AppDbContext(options);
        var preparationRows = await observer.TaskWorktreeMirrorBindings.CountAsync(row => row.PreparationId == preparationId);
        preparationRows.ShouldBe(1);

        var sharedBinding = Guid.NewGuid();
        observer.TaskWorktreeMirrorBindings.Add(Binding(taskId, Guid.NewGuid(), sharedBinding, "same-store"));
        await observer.SaveChangesAsync();
        await using var duplicate = new AppDbContext(options);
        duplicate.TaskWorktreeMirrorBindings.Add(Binding(taskId, Guid.NewGuid(), sharedBinding, "same-store"));
        var duplicateBindingRejected = false;
        try
        {
            await duplicate.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            duplicateBindingRejected = true;
        }

        duplicateBindingRejected.ShouldBeTrue();
        await using var after = new AppDbContext(options);
        (await after.TaskWorktreeMirrorBindings.CountAsync(row => row.BindingId == sharedBinding)).ShouldBe(1);
    }

    [Test]
    public async Task C1032_SchemaLegacyUpgrade()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        await using var db = new AppDbContext(options);
        var activeId = Guid.NewGuid();
        var doneId = Guid.NewGuid();
        var canceledId = Guid.NewGuid();
        var scalarId = Guid.NewGuid();
        db.AgentTasks.Add(TaskRow(activeId, AgentTaskStatus.Working, "/mirrors/active"));
        db.AgentTasks.Add(TaskRow(doneId, AgentTaskStatus.Succeeded, "/mirrors/done"));
        db.AgentTasks.Add(TaskRow(canceledId, AgentTaskStatus.Canceled, "/mirrors/canceled"));
        db.AgentTasks.Add(TaskRow(scalarId, AgentTaskStatus.Queued, "/mirrors/scalar"));
        await db.SaveChangesAsync();
        await SeedDoneCardAsync(db, doneId);

        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var position = Array.FindIndex(migrations, name => name.EndsWith("_AddTaskWorktreeMirrorBinding", StringComparison.Ordinal));
        position.ShouldBeGreaterThan(0);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[position - 1]);
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        await using var observer = new AppDbContext(options);
        var legacyBindingCount = await observer.TaskWorktreeMirrorBindings.CountAsync();
        legacyBindingCount.ShouldBe(0);
        (await observer.AgentTasks.SingleAsync(task => task.Id == activeId)).RemoteWorktreePath.ShouldBe("/mirrors/active");
        (await observer.AgentTasks.SingleAsync(task => task.Id == doneId)).RemoteWorktreePath.ShouldBe("/mirrors/done");
        (await observer.AgentTasks.SingleAsync(task => task.Id == canceledId)).RemoteWorktreePath.ShouldBe("/mirrors/canceled");
        (await observer.AgentTasks.SingleAsync(task => task.Id == scalarId)).RemoteWorktreePath.ShouldBe("/mirrors/scalar");
        (await observer.AgentTasks.SingleAsync(task => task.Id == scalarId)).RunnerId.ShouldBeNull();
        (await observer.Cards.CountAsync(card => card.Status == CardStatus.Done)).ShouldBe(1);
    }

    [Test]
    public async Task C1032_SchemaRequestAndReceiptPersistence()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var taskId = Guid.NewGuid();
        var bindingRowId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var originalRequestBytes = JsonSerializer.SerializeToUtf8Bytes(
            new CompletedCardWorkspaceCommand(
                CompletedCardWorkspaceProtocol.SchemaVersion,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), endpointId, Guid.NewGuid(),
                CompletedCardWorkspaceProtocol.DispositionCardDone,
                new CompletedCardWorkspaceBinding(taskId, 1, Guid.NewGuid(), Guid.NewGuid(), "runner", "store", "repo", "/common", "/git", "/trees/original", "refs/heads/feat", Sha),
                new CompletedCardWorkspaceProof(Sha, "fingerprint", "refs/heads/master", Sha, CompletedCardWorkspaceProtocol.ProofContained, "proof", Sha, null),
                "report", "artifacts", "policy", ["pointer"], ["pattern"]),
            PhoneHomeFraming.Json);
        var originalReceiptBytes = JsonSerializer.SerializeToUtf8Bytes(
            new CompletedCardWorkspaceReceipt(
                CompletedCardWorkspaceProtocol.SchemaVersion,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), endpointId, Guid.NewGuid(),
                taskId, 1, Guid.NewGuid(), Guid.NewGuid(), "runner", "store", "repo", "/common", "/git",
                "/trees/original", "refs/heads/feat", Sha, Sha, "preservation", "artifacts", "policy",
                CompletedCardWorkspaceProtocol.DispositionCardDone, "/aside/owned", "journal-1", true, false),
            PhoneHomeFraming.Json);

        await using (var db = new AppDbContext(options))
        {
            db.AgentTasks.Add(TaskRow(taskId, AgentTaskStatus.Succeeded, "/mirrors/original"));
            await db.SaveChangesAsync();
            var revisionId = await SeedDoneCardAsync(db, taskId);
            var cardId = await db.AgentTasks.Where(task => task.Id == taskId).Select(task => task.CardId).SingleAsync();
            var binding = Binding(taskId, Guid.NewGuid(), Guid.NewGuid(), "store-persist");
            binding.Id = bindingRowId;
            db.TaskWorktreeMirrorBindings.Add(binding);
            var cleanupId = Guid.NewGuid();
            db.CardWorktreeCleanups.Add(new CardWorktreeCleanup
            {
                Id = cleanupId, CardId = cardId!.Value, DoneRevisionId = revisionId,
                DoneAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            });
            var targetId = Guid.NewGuid();
            db.CardWorktreeCleanupTargets.Add(new CardWorktreeCleanupTarget
            {
                Id = targetId, CleanupId = cleanupId, TaskId = taskId, TaskAttempt = 1,
                WorkspaceIdentity = "ordinary", RepositoryPath = "/repo", WorktreePath = "/trees/original",
                SourceFullRef = "refs/heads/feat", CreatedAt = DateTime.UtcNow,
            });
            db.CardWorktreeCleanupEndpoints.Add(new CardWorktreeCleanupEndpoint
            {
                Id = endpointId, TargetId = targetId, EndpointIdentity = "mirror",
                RepositoryPath = "/repo", WorktreePath = "/trees/original", SourceFullRef = "refs/heads/feat",
                State = CardWorktreeCleanupEndpointState.IntentRecorded, UpdatedAt = DateTime.UtcNow,
                MirrorBindingId = bindingRowId, ImmutableRequest = originalRequestBytes, ImportedReceipt = originalReceiptBytes,
            });
            await db.SaveChangesAsync();

            var task = await db.AgentTasks.SingleAsync(row => row.Id == taskId);
            task.Attempt = 7;
            task.RemoteWorktreePath = "/mirrors/moved";
            task.WorktreePath = "/trees/moved";
            task.RunnerId = "other-runner";
            var target = await db.CardWorktreeCleanupTargets.SingleAsync(row => row.Id == targetId);
            target.WorktreePath = "/trees/moved";
            target.TaskAttempt = 7;
            await db.SaveChangesAsync();
        }

        await using var observer = new AppDbContext(options);
        var persisted = await observer.CardWorktreeCleanupEndpoints.SingleAsync(row => row.Id == endpointId);
        var persistedRequestBytes = persisted.ImmutableRequest;
        var persistedReceiptBytes = persisted.ImportedReceipt;
        persistedRequestBytes.ShouldBe(originalRequestBytes);
        persistedReceiptBytes.ShouldBe(originalReceiptBytes);
        persisted.MirrorBindingId.ShouldBe(bindingRowId);
        (await observer.AgentTasks.SingleAsync(task => task.Id == taskId)).RemoteWorktreePath.ShouldBe("/mirrors/moved");
    }

    private static async Task<Guid> SeedDoneCardAsync(AppDbContext db, Guid taskId)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = "c1032 " + Guid.NewGuid().ToString("N"),
            GitRepositoryUrl = "https://example.test/c1032.git", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var board = new Board { Id = Guid.NewGuid(), Project = project, Name = "c1032", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(), Board = board, StateKey = "done", Name = "Done",
            CardStatus = CardStatus.Done, IsTerminal = true, ColumnOrder = 0,
        };
        board.Columns.Add(column);
        var card = new Card
        {
            Id = Guid.NewGuid(), Board = board, BoardColumn = column, Identifier = "CARD-1032",
            Title = "mirror binding", Status = CardStatus.Done, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid(),
        };
        var revision = new CardRevision
        {
            Id = Guid.NewGuid(), Card = card, RevisionNumber = 1, Kind = CardRevisionKind.Move,
            ToStatus = CardStatus.Done, CreatedAt = DateTime.UtcNow,
        };
        db.Cards.Add(card);
        db.CardRevisions.Add(revision);
        var task = await db.AgentTasks.SingleAsync(row => row.Id == taskId);
        task.CardId = card.Id;
        await db.SaveChangesAsync();
        return revision.Id;
    }

    private static AgentTask TaskRow(Guid id, AgentTaskStatus status, string remotePath) => new()
    {
        Id = id, RootTaskId = id, Title = "c1032 " + status, Goal = "fixture",
        Status = status, Workspace = WorkspaceMode.Worktree, Attempt = 1,
        WorkingDirectory = remotePath, WorktreePath = remotePath, RemoteWorktreePath = remotePath,
        CreatedAt = DateTime.UtcNow,
    };

    private static TaskWorktreeMirrorBinding Binding(Guid taskId, Guid preparationId, Guid bindingId, string store) => new()
    {
        Id = Guid.NewGuid(), TaskId = taskId, Attempt = 1, PreparationId = preparationId, BindingId = bindingId,
        RunnerId = "runner", RunnerStoreId = store, RepositoryIdentity = "repo",
        CommonDirectory = "/common", GitDirectory = "/git", WorktreePath = "/trees/" + bindingId.ToString("N"),
        SourceFullRef = "refs/heads/feat", CreationBaselineSha = Sha, CreatedAt = DateTime.UtcNow,
    };
}
