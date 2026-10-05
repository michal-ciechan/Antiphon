using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class BlockedTaskParkStateTests
{
    [Test]
    public async Task C1065_EpisodeIdentitySurvivesRestart()
    {
        await using var f = await BlockedTaskParkFixture.CreateAsync();
        await using (var model = f.Db())
        {
            (await model.Database.GetPendingMigrationsAsync()).ShouldBeEmpty("real migrations applied");
            model.Database.HasPendingModelChanges().ShouldBeFalse("additive migration matches model snapshot");
        }
        f.Options.Enabled.ShouldBeFalse();
        f.Options.ReclaimExisting.ShouldBeFalse();
        (await f.RegisterAsync()).ShouldBeNull("shipping defaults create no episode");
        f.Options.Enabled = true;

        // Pause a real transaction immediately before commit. Another connection cannot see
        // Requested (or any release debt) before that commit; a fault rolls it all back.
        f.Cut.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Cut.Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Cut.Fail = true;
        var registering = f.RegisterAsync();
        try
        {
            await f.Cut.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var reader = f.Db();
            (await reader.AgentTaskParks.CountAsync()).ShouldBe(0, "uncommitted intent is invisible");
            (await reader.RunnerSeatReleases.CountAsync()).ShouldBe(0, "registration creates no release debt");
        }
        finally { f.Cut.Continue.TrySetResult(); }
        await Should.ThrowAsync<InvalidOperationException>(() => registering);
        f.Cut.Fail = false; f.Cut.Entered = null; f.Cut.Continue = null;
        await using (var reader = f.Db()) (await reader.AgentTaskParks.CountAsync()).ShouldBe(0, "rollback");

        Guid?[] ids = [];
        Exception? concurrentError = null;
        try { ids = await Task.WhenAll(f.RegisterAsync(), f.RegisterAsync()); }
        catch (Exception ex) { concurrentError = ex; }
        concurrentError.ShouldBeNull("G-2: concurrent registrations succeed through the reuse decision");
        ids[0].ShouldNotBeNull("G-2");
        ids[1].ShouldBe(ids[0], "G-2: concurrent registration reuses identity");
        await f.RestartAsync();
        Guid? restarted = null;
        Exception? registrationError = null;
        try { restarted = await f.RegisterAsync(); }
        catch (Exception ex) { registrationError = ex; }
        registrationError.ShouldBeNull("G-2: restart registration must succeed without a duplicate insert");
        restarted.ShouldBe(ids[0], "G-2");

        // A direct writer bypasses the service lock and its reuse predicate. Only the actual
        // migrated PostgreSQL unique index can reject this separate-context insert (PC-1).
        await using (var direct = f.Db())
        {
            var duplicate = await direct.AgentTaskParks.AsNoTracking().SingleAsync();
            duplicate.Id = Guid.NewGuid();
            direct.AgentTaskParks.Add(duplicate);
            var error = await Should.ThrowAsync<DbUpdateException>(() => direct.SaveChangesAsync(), "G-1");
            error.InnerException.ShouldBeOfType<PostgresException>("G-1").SqlState
                .ShouldBe(PostgresErrorCodes.UniqueViolation, "G-1");
        }
        await f.NextEpisodeAsync(attempt: true);
        var nextAttempt = await f.RegisterAsync();
        nextAttempt.ShouldNotBeNull("G-3");
        nextAttempt.ShouldNotBe(ids[0], "G-3");
        await f.NextEpisodeAsync(attempt: false);
        var nextEvent = await f.RegisterAsync();
        nextEvent.ShouldNotBeNull("G-4");
        nextEvent.ShouldNotBe(nextAttempt, "G-4");
        await using var db = f.Db();
        (await db.AgentTaskParks.CountAsync()).ShouldBe(3, "distinct attempt/event identities");
        var current = await db.AgentTaskParks.SingleAsync(p => p.Id == nextEvent);
        f.Cut.Fail = true;
        await Should.ThrowAsync<InvalidOperationException>(() => f.AdvanceAsync(current.Id, AgentTaskParkState.Published));
        f.Cut.Fail = false;
        await using (var rolledBack = f.Db())
            (await rolledBack.AgentTaskParks.SingleAsync(p => p.Id == current.Id)).State
                .ShouldBe(AgentTaskParkState.Requested, "uncommitted publication state rolls back");
        (await f.AdvanceAsync(current.Id, AgentTaskParkState.Held)).ShouldBeTrue();
        (await f.RunAsync((s, _) => s.PersistStateAsync(current.Id, current.Revision, current.State,
            AgentTaskParkState.Published, "park_stale", default))).ShouldBeFalse("stale episode revision");
        await db.AgentTasks.Where(t => t.Id == f.TaskId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()));
        (await f.AdvanceAsync(current.Id, AgentTaskParkState.Requested)).ShouldBeFalse("task CAS veto");
        (await f.AdvanceAsync(ids[0]!.Value, AgentTaskParkState.Published)).ShouldBeFalse("old attempt cannot advance");
    }

    [Test]
    public async Task C1065_ParkingDoesNotSettleOrDiscardHistory()
    {
        // This slice exercises storage transitions, not publication/release qualification.
        // No successful publication or release receipts are seeded; S2-S5 own those operations.
        foreach (var shape in new[] { (Report: true, Session: true), (Report: false, Session: true), (Report: false, Session: false) })
        {
            await using var f = await BlockedTaskParkFixture.CreateAsync(shape.Report, shape.Session);
            f.Options.Enabled = true;
            var before = await RetainedAsync(f);
            var id = (await f.RegisterAsync()).ShouldNotBeNull("G-7");
            var captured = await CapturedAsync(f, id);
            foreach (var state in new[] { AgentTaskParkState.Requested, AgentTaskParkState.Held,
                AgentTaskParkState.Requested, AgentTaskParkState.Published, AgentTaskParkState.ReleasePending, AgentTaskParkState.Parked })
            {
                await using var read = f.Db();
                var current = await read.AgentTaskParks.SingleAsync(p => p.Id == id);
                if (current.State != state) (await f.AdvanceAsync(id, state)).ShouldBeTrue($"persist {state}");
                await f.RestartAsync();
                (await f.ScanCardsAsync()).ShouldBe(0, "G-5: parking never advances the card");
                await using var fresh = f.Db();
                var task = await fresh.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
                task.Status.ShouldBe(AgentTaskStatus.Blocked, "G-5");
                AgentTaskService.IsSettled(task.Status).ShouldBeFalse("G-5");
                (await fresh.Cards.SingleAsync(c => c.Id == f.CardId)).Status.ShouldBe(CardStatus.InProgress, "G-5");
                (await RetainedAsync(f)).ShouldBe(before, "G-6: full retained task/artifact/transcript/history snapshot");
                (await CapturedAsync(f, id)).ShouldBe(captured, "G-6: episode coordinates remain immutable");
                if (!shape.Report) task.CompletedAt.ShouldBeNull("G-7");
                var saved = await fresh.AgentTaskParks.SingleAsync(p => p.Id == id);
                saved.State.ShouldBe(state);
                if (!shape.Report) saved.CompletedAt.ShouldBeNull("G-7");
                saved.PublicationReceiptId.ShouldBeNull("state storage is not publication authority");
                saved.RunnerSeatReleaseId.ShouldBeNull("state storage is not exit authority");
                (await fresh.RunnerSeatReleases.CountAsync()).ShouldBe(0);
            }
        }
    }

    private static async Task<string> CapturedAsync(BlockedTaskParkFixture f, Guid id)
    {
        await using var db = f.Db();
        var p = await db.AgentTaskParks.AsNoTracking().SingleAsync(p => p.Id == id);
        return JsonSerializer.Serialize(new { p.Id, p.TaskId, p.Attempt, p.BlockEventId,
            p.TaskConcurrencyToken, p.AgentId, p.SessionId, p.RunnerId, p.RunnerStoreId,
            p.AcceptedStartedAt, p.Workspace, p.WorktreeId, p.WorktreePath, p.RemoteWorktreePath,
            p.FullRef, p.BaselineSha, p.ReportReference, p.ReportDigest, p.TranscriptSequence,
            p.BlockedAt, p.CompletedAt, p.CreatedAt });
    }

    private static async Task<string> RetainedAsync(BlockedTaskParkFixture f)
    {
        await using var db = f.Db();
        var t = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == f.TaskId);
        var transcript = await db.TranscriptEntries.AsNoTracking().Where(e => e.AgentSessionId == f.SessionId)
            .OrderBy(e => e.Sequence).Select(e => new { e.Id, e.Sequence, e.Kind, e.Text }).ToArrayAsync();
        var history = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == f.TaskId)
            .OrderBy(e => e.At).Select(e => new { e.Id, e.Type, e.Detail, e.At }).ToArrayAsync();
        return JsonSerializer.Serialize(new { t.Result, t.ResultFilePath, t.FailureReason, t.CompletedAt,
            t.AgentId, t.AgentSessionId, t.WorktreeId, t.WorktreePath, t.RemoteWorktreePath, t.WorktreeBranch,
            t.WorktreeBaseSha, t.ConcurrencyToken, t.Attempt, t.RepliedAtSequence, transcript, history,
            AgentRetained = await db.Agents.AnyAsync(a => a.Id == f.AgentId),
            WorkspaceRetained = Directory.Exists(t.WorktreePath),
            ReportBytes = t.ResultFilePath is null ? null : await File.ReadAllTextAsync(t.ResultFilePath) });
    }
}
