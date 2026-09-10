using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Application.Services;
using Npgsql;
using Shouldly;
using TUnit.Core;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandNotificationPersistenceTests
{
    [Test]
    public async Task C467_V07_ConcurrentSettlementAndExplicitCleanup()
    {
        await using var h = new LandingSafetyHarness(); await h.InitializeAsync(); await h.AddSourceAsync(); await h.RunAsync();
        h.Fixture.Git.Trace.Clear();
        await Task.WhenAll(h.FailAsync(new IOException("lost acknowledgement A")), h.FailAsync(new IOException("lost acknowledgement B")));
        await using var db = h.CreateContext();
        var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == h.Fixture.TaskId);
        (await db.AgentTaskEvents.CountAsync(e => e.LandRequestId == note.RequestId && e.IsLandTerminal)).ShouldBe(1);
        h.Fixture.Git.Trace.ShouldBeEmpty();
        db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification { Id = Guid.NewGuid(), TaskId = note.TaskId,
            RequestId = note.RequestId, SourceEventId = note.SourceEventId, CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow });
        var duplicate = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        ((PostgresException)duplicate.InnerException!).SqlState.ShouldBe("23505"); db.ChangeTracker.Clear();
        db.AgentTaskEvents.Add(new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = note.TaskId, LandRequestId = note.RequestId,
            IsLandTerminal = true, Type = AgentTaskEventType.LandRefused, At = DateTime.UtcNow });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        db.AgentTaskLandRequests.Add(new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = note.TaskId,
            RequestedAt = DateTime.UtcNow, LastProgressAt = DateTime.UtcNow, LastEvaluatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        db.AgentTaskLandRequests.Add(new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = note.TaskId,
            RequestedAt = DateTime.UtcNow, LastProgressAt = DateTime.UtcNow, LastEvaluatedAt = DateTime.UtcNow });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        db.AgentTaskEvents.AddRange(new[] { AgentTaskEventType.Held, AgentTaskEventType.LandAged }.Select(type => new AgentTaskEvent {
            Id = Guid.NewGuid(), AgentTaskId = note.TaskId, LandRequestId = note.RequestId, Type = type, At = DateTime.UtcNow }));
        await db.SaveChangesAsync();
        (await db.AgentTaskEvents.CountAsync(e => e.LandRequestId == note.RequestId && !e.IsLandTerminal)).ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task C467_V17_UpgradeAndLegacyEvidence()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var bridge = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        await using var db = new AppDbContext(options);
        var migrations = db.Database.GetMigrations().ToArray();
        var cut = Array.FindIndex(migrations, m => m.EndsWith("_AddDurableLandDelivery", StringComparison.Ordinal));
        cut.ShouldBeGreaterThan(0);
        await db.GetService<IMigrator>().MigrateAsync(migrations[cut - 1]);
        var pending = Guid.NewGuid(); var legacy = Guid.NewGuid(); var none = Guid.NewGuid();
        var unique = Guid.NewGuid(); var ambiguous = Guid.NewGuid();
        var old = DateTime.UtcNow.AddDays(-5);
        foreach (var id in new[] { pending, legacy, none, unique, ambiguous })
        {
            DateTime? requested = id == pending ? old : null;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AgentTasks" ("Id", "RootTaskId", "Depth", "Title", "Goal", "Kind", "Role", "ModelLevel", "Attempt", "MaxAttempts",
                    "WorkingDirectory", "Ephemeral", "Status", "ReplyTo", "ConcurrencyToken", "CreatedAt", "TokensIn", "TokensOut", "CostUsd",
                    "Workspace", "WorktreeBranch", "WorktreePath", "LandRequestedAt", "LandAttempt", "LandVerifyFilter")
                VALUES ({id}, {id}, 0, 'upgrade', 'upgrade fixture', 0, 0, 0, 0, 1, 'fixture', false, {(int)AgentTaskStatus.Succeeded},
                    {(int)(id == none ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session)}, {Guid.NewGuid()}, {old}, 0, 0, 0,
                    {(int)WorkspaceMode.Worktree}, 'feat/legacy', 'fixture', {requested}, 2, 'preserved-filter')
                """);
            if (id == unique || id == ambiguous)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "AgentTasks" SET "ParentSessionId" = {bridge.SessionId} WHERE "Id" = {id}
                    """);
                for (var i = 0; i < (id == unique ? 1 : 2); i++)
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO "SessionQueuedMessages" ("Id", "AgentSessionId", "Body", "Status", "Sequence", "Origin", "ConversationKey",
                            "CreatedAt", "DeliveryAttempts", "LastDeliveryBaselineSequence", "LastDeliveryStartedAt", "RulesFollowOnCount")
                        VALUES ({Guid.NewGuid()}, {bridge.SessionId}, 'historical claim, no receipt', 1, {i + 1}, 3, {$"land:{id:N}"},
                            {old.AddSeconds(1)}, 1, 10, {old}, 0)
                        """);
            }
            if (id != pending) await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AgentTaskEvents" ("Id", "AgentTaskId", "Type", "Detail", "At")
                VALUES ({Guid.NewGuid()}, {id}, {(int)AgentTaskEventType.Landed}, 'historical claim, no receipt', {old})
                """);
        }
        await db.GetService<IMigrator>().MigrateAsync(); await db.GetService<IMigrator>().MigrateAsync();
        await using var observer = new AppDbContext(options);
        var backfilled = await observer.AgentTaskLandRequests.SingleAsync(r => r.IsPending);
        backfilled.TaskId.ShouldBe(pending); backfilled.RequestedAt.ShouldBe(old, TimeSpan.FromMilliseconds(1));
        backfilled.LastProgressAt.ShouldBe(old, TimeSpan.FromMilliseconds(1)); backfilled.Attempt.ShouldBe(2);
        backfilled.VerifyFilter.ShouldBe("preserved-filter"); backfilled.State.ShouldBe(LandRequestState.Queued);
        (await observer.AgentTasks.SingleAsync(t => t.Id == pending)).CurrentLandRequestId.ShouldBe(backfilled.Id);
        var attached = await observer.AgentTaskLandNotifications.SingleAsync();
        attached.TaskId.ShouldBe(unique); attached.IsLegacy.ShouldBeTrue(); attached.ConfirmedAt.ShouldBeNull();
        attached.EnqueueAttempts.ShouldBe(0); attached.QueueMessageId.ShouldNotBeNull();
        (await observer.SessionQueuedMessages.CountAsync(q => q.AgentSessionId == bridge.SessionId)).ShouldBe(3);
        var notifier = new AgentTaskLandNotificationService(observer, bridge.Queue, new CompletionNoteFlushQueue(), bridge.Runtime, TimeProvider.System);
        await notifier.ReconcileAsync(attached.Id, CancellationToken.None);
        attached.ConfirmedAt.ShouldBeNull("a historical Sent row is not receipt evidence");
        bridge.Runner.SetTranscript(new(bridge.SessionId, [new SessionRunnerTranscriptEvent(bridge.SessionId, 11, TranscriptKinds.UserPrompt,
            "c467-legacy-evidence", null, DateTimeOffset.UtcNow, "user", attached.Body, null, null, null, null, null)], 11));
        await notifier.ReconcileAsync(attached.Id, CancellationToken.None);
        attached.State.ShouldBe(LandNotificationState.Confirmed); attached.ConfirmingPromptSequence.ShouldNotBeNull();
        attached.EnqueueAttempts.ShouldBe(0); bridge.Adapter.Inputs.ShouldBeEmpty();
        (await observer.SessionQueuedMessages.CountAsync(q => q.AgentSessionId == bridge.SessionId)).ShouldBe(3);
        (await observer.AgentTasks.Where(t => t.Id == legacy || t.Id == none).Select(t => t.CurrentLandRequestId).ToListAsync()).ShouldAllBe(id => id == null);
        await using var connection = new NpgsqlConnection(schema.ConnectionString); await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_indexes WHERE indexname IN ('IX_AgentTaskLandRequests_TaskId','IX_AgentTaskEvents_LandRequestId','IX_AgentTaskLandNotifications_SourceEventId','IX_SessionQueuedMessages_SourceLandNotificationId') AND indexdef LIKE '%UNIQUE%'", connection);
        Convert.ToInt32(await command.ExecuteScalarAsync()).ShouldBe(4);
    }
    [Test]
    [Arguments("landed")]
    [Arguments("already-present")]
    [Arguments("residue-cleanup")]
    [Arguments("preoperation-refusal")]
    public async Task C467_V05_OutcomeObligationMatrix(string outcome)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        if (outcome != "already-present") await h.AddSourceAsync();
        var residue = Path.Combine(h.Fixture.Source, ".antiphon", "valuable.txt");
        if (outcome == "residue-cleanup")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(residue)!);
            await File.WriteAllTextAsync(residue, "owned residue");
        }
        if (outcome == "preoperation-refusal")
        {
            await using var db = h.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.WorktreeBranch = null;
            await db.SaveChangesAsync();
        }
        await h.RunAsync();
        await using var observer = h.CreateContext();
        var request = await observer.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.TaskId == h.Fixture.TaskId);
        request.IsPending.ShouldBeFalse();
        request.State.ShouldBe(LandRequestState.Completed);
        var note = await observer.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.RequestId == request.Id);
        note.SourceEventId.ShouldBe(request.TerminalEventId!.Value);
        note.State.ShouldBe(LandNotificationState.NotRequired);
        note.Body.ShouldContain(request.Id.ToString("N"));
        note.Body.ShouldContain(note.Id.ToString("N"));
        note.Body.ShouldContain("publication=");
        note.Body.ShouldContain("cleanup=");
        note.ConfirmedAt.ShouldBeNull();
        (await observer.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Fixture.TaskId)).LandRequestedAt.ShouldBeNull();
        var terminal = await observer.AgentTaskEvents.SingleAsync(e => e.Id == note.SourceEventId);
        terminal.IsLandTerminal.ShouldBeTrue();
        terminal.Type.ShouldBe(outcome switch {
            "already-present" => AgentTaskEventType.AlreadyPresent,
            "residue-cleanup" => AgentTaskEventType.LandedWithResidue,
            "preoperation-refusal" => AgentTaskEventType.LandRefused,
            _ => AgentTaskEventType.Landed,
        });
        if (outcome != "preoperation-refusal") await h.Fixture.AssertRemoteSourceAsync();
        await h.FailAsync(new IOException("lost acknowledgement"));
        (await observer.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id)).ShouldBe(1);
        if (outcome == "residue-cleanup")
        {
            File.Delete(residue);
            await h.RepostAsync();
            await h.RunAsync();
            var notes = await observer.AgentTaskLandNotifications.AsNoTracking().Where(n => n.TaskId == h.Fixture.TaskId).ToListAsync();
            notes.Count.ShouldBe(2);
            notes.Select(n => n.RequestId).Distinct().Count().ShouldBe(2);
            notes.Select(n => n.ContentDigest).Distinct().Count().ShouldBe(2);
            notes.Select(n => n.LandingOperationId).Distinct().Count().ShouldBe(1);
            (await observer.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.LandingCleanup)).ShouldBe(1);
        }
    }
}
