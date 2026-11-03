using System.Data.Common;
using System.Net;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1656. Review 4b605c04 advisories on the CARD-1649 record-release route: the stamp is conditional on the task
/// still not holding its lease (A-1), the requester refuses invisible and line-separator characters (A-3) and the
/// operator token in any letter case (A-4).
/// </summary>
[Category("Integration")]
[Timeout(60_000)]
public sealed class C1656RecordReleaseHardeningTests
{
    private const string Runner = "runner-a";
    private const string Slot = "ws-01";
    private const int Generation = 3;
    private const string Route = $"/api/session-runners/{Runner}/workspace-slots/{Slot}/record-release";

    [Test]
    public async Task C1656_RequesterRefusesFormatAndSeparatorCharacters()
    {
        // A-3. The requester is written into the audit event and the log line. A control, format (bidi override,
        // zero-width, astral tag) or line/paragraph separator character is refused 400 before any lookup; a plain
        // label passes validation and reaches the task lookup (404 for an unknown task).
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        var token = OperatorTokenFile.ReadOrCreate(host.OperatorTokenPath);

        foreach (var (requester, name) in new[]
                 {
                     ("op\u0007x", "control"), ("op\u2028x", "line separator"), ("op\u2029x", "paragraph separator"),
                     ("op\u202Ex", "right-to-left override"), ("op\u200Bx", "zero width space"), ("op\U000E0041x", "astral tag"),
                 })
        {
            using var refused = await host.PostOperatorAsync(Route, Body(requester), token);
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"a requester with a {name} character is refused");
        }

        using var plain = await host.PostOperatorAsync(Route, Body("op.x@example a-b_c"), token);
        plain.StatusCode.ShouldBe(HttpStatusCode.NotFound, "a plain label passes validation and reaches the task lookup");
    }

    [Test]
    public async Task C1656_RequesterRefusesTheOperatorTokenInAnyCase()
    {
        // A-4. The operator token is lowercase hex; a requester that embeds it in any letter case is refused 400.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        var token = OperatorTokenFile.ReadOrCreate(host.OperatorTokenPath);
        token.ShouldBe(token.ToLowerInvariant());
        var mixed = string.Concat(token.Select((c, i) => i % 2 == 0 ? char.ToUpperInvariant(c) : c));
        token.ShouldNotBe(token.ToUpperInvariant(), "the token has letters, so the upper-case copy differs");

        foreach (var requester in new[] { token, token.ToUpperInvariant(), "by " + token.ToUpperInvariant(), mixed })
        {
            using var refused = await host.PostOperatorAsync(Route, Body(requester), token);
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "a requester holding the operator token in any case is refused");
        }

        using var plain = await host.PostOperatorAsync(Route, Body("operator-x"), token);
        plain.StatusCode.ShouldBe(HttpStatusCode.NotFound, "a requester without the token reaches the task lookup");
    }

    [Test]
    public async Task C1656_ReleaseIsNeverStampedOnATaskThatGoesLiveWhileItIsRecorded()
    {
        // A-1. The checks and the stamp are one conditional update: a task that becomes Working after the checks read
        // it (here, while the stuck record is read) is refused workspace_slot_release_task_live and keeps its lease.
        // The control task, with nothing racing, is stamped and audited by the same call.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = TimeProvider.System;

        var control = await SeedStuckAsync(schema.ConnectionString);
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            await WorkspaceSlotReleaseService.RecordOperatorReleaseAsync(
                db, clock, Runner, Slot, control, Generation, "control", "operator", CancellationToken.None);
        (await ReadAsync(schema.ConnectionString, control)).Task.RemoteWorkspaceSlotReleasedAt.ShouldNotBeNull("the control is stamped");

        var raced = await SeedStuckAsync(schema.ConnectionString);
        var goLive = new GoLiveWhileStuckRecordIsRead(schema.ConnectionString, raced);
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString, goLive)))
        {
            var refused = await Should.ThrowAsync<ConflictException>(() => WorkspaceSlotReleaseService.RecordOperatorReleaseAsync(
                db, clock, Runner, Slot, raced, Generation, "raced", "operator", CancellationToken.None));
            refused.Code.ShouldBe(WorkspaceSlotReleaseService.TaskLiveCode);
        }

        goLive.Fired.ShouldBe(1, "the task went Working after the checks read it");
        var (task, events) = await ReadAsync(schema.ConnectionString, raced);
        task.Status.ShouldBe(AgentTaskStatus.Working);
        task.RemoteWorkspaceSlotReleasedAt.ShouldBeNull("a Working task keeps its lease");
        task.RemoteWorktreePath.ShouldNotBeNull();
        events.ShouldNotContain(text => text.StartsWith(WorkspaceSlotCodes.ReleaseRecorded, StringComparison.Ordinal));
    }

    [Test]
    public async Task C1670_ReleaseOnATaskWhoseAttemptChangesIsRefusedChanged()
    {
        // CARD-1670 (a). A task that changes after the checks without going live (here its attempt moves on while the
        // stuck record is read) fails the conditional update and is refused workspace_slot_release_changed: no stamp,
        // no audit event.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var raced = await SeedStuckAsync(schema.ConnectionString);
        var change = new AfterStuckRecordIsRead(schema.ConnectionString, raced, (other, id, ct) =>
            other.AgentTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Attempt, 2), ct));
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString, change)))
        {
            var refused = await Should.ThrowAsync<ConflictException>(() => WorkspaceSlotReleaseService.RecordOperatorReleaseAsync(
                db, TimeProvider.System, Runner, Slot, raced, Generation, "raced", "operator", CancellationToken.None));
            refused.Code.ShouldBe(WorkspaceSlotReleaseService.ChangedCode);
        }

        change.Fired.ShouldBe(1);
        var (task, events) = await ReadAsync(schema.ConnectionString, raced);
        task.Attempt.ShouldBe(2);
        task.RemoteWorkspaceSlotReleasedAt.ShouldBeNull("a task whose attempt moved on is not stamped");
        task.RemoteWorktreePath.ShouldNotBeNull();
        events.ShouldNotContain(text => text.StartsWith(WorkspaceSlotCodes.ReleaseRecorded, StringComparison.Ordinal));
    }

    [Test]
    public async Task C1670_ReleaseOnATaskDeletedWhileItIsRecordedIsRefusedChanged()
    {
        // CARD-1670 (b). A task deleted after the checks fails the conditional update and is gone when the refusal
        // re-reads it: the same 409 workspace_slot_release_changed, never an unhandled lookup failure (a 500).
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var raced = await SeedStuckAsync(schema.ConnectionString);
        var delete = new AfterStuckRecordIsRead(schema.ConnectionString, raced, async (other, id, ct) =>
        {
            await other.AgentTaskEvents.Where(e => e.AgentTaskId == id).ExecuteDeleteAsync(ct);
            await other.AgentTasks.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
        });
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString, delete)))
        {
            var refused = await Should.ThrowAsync<ConflictException>(() => WorkspaceSlotReleaseService.RecordOperatorReleaseAsync(
                db, TimeProvider.System, Runner, Slot, raced, Generation, "raced", "operator", CancellationToken.None));
            refused.Code.ShouldBe(WorkspaceSlotReleaseService.ChangedCode);
        }

        delete.Fired.ShouldBe(1);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await verify.AgentTasks.AnyAsync(t => t.Id == raced)).ShouldBeFalse();
        (await verify.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == raced)).ShouldBeFalse("no audit event is written for it");
    }

    [Test]
    public async Task C1670_AFailedAuditEventRollsBackTheStamp()
    {
        // CARD-1670 (a). The conditional stamp and the audit event share one transaction: when writing the audit event
        // throws after the stamp ran, neither is kept and the task still owes its release.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var id = await SeedStuckAsync(schema.ConnectionString);
        var fail = new FailAuditEventInsert();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString, fail)))
        {
            Exception? thrown = null;
            try
            {
                await WorkspaceSlotReleaseService.RecordOperatorReleaseAsync(
                    db, TimeProvider.System, Runner, Slot, id, Generation, "rolled back", "operator", CancellationToken.None);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            thrown.ShouldNotBeNull("the injected audit event failure surfaces");
        }

        fail.Fired.ShouldBe(1, "the audit event insert ran after the stamp and threw");
        var (task, events) = await ReadAsync(schema.ConnectionString, id);
        task.RemoteWorkspaceSlotReleasedAt.ShouldBeNull("the stamp rolls back with the audit event");
        task.RemoteWorktreePath.ShouldBe("/work/worktrees/" + Slot);
        events.ShouldNotContain(text => text.StartsWith(WorkspaceSlotCodes.ReleaseRecorded, StringComparison.Ordinal));
    }

    private static WorkspaceSlotRecordReleaseRouteRequest Body(string requester) =>
        new(Guid.NewGuid(), Generation, "runner lost the slot", requester);

    private static async Task<Guid> SeedStuckAsync(string connectionString)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "stuck slot", Goal = "reply",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Custom, AgentKind = AgentKind.Grok,
            ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Worktree, WorkingDirectory = "/work",
            RunnerId = Runner, Status = AgentTaskStatus.Failed, Attempt = 1, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = now, CompletedAt = now, ConcurrencyToken = Guid.NewGuid(),
            RemoteWorktreePath = "/work/worktrees/" + Slot, RemoteWorkspaceSlotId = Slot, RemoteWorkspaceSlotGeneration = Generation,
        });
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = id,
            Type = AgentTaskEventType.Warning,
            Detail = $"{WorkspaceSlotCodes.ReleaseStuck}: slot {Slot} generation {Generation} task attempt 1 is unknown to runner '{Runner}' after 3 attempts.",
            At = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<(AgentTask Task, List<string> Events)> ReadAsync(string connectionString, Guid id)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        var events = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == id).Select(e => e.Detail ?? "").ToListAsync();
        return (task, events);
    }

    /// <summary>
    /// Sets the task Working from another connection once the route has read the stuck record, which comes after the
    /// status check: the window review 4b605c04 A-1 names.
    /// </summary>
    private sealed class GoLiveWhileStuckRecordIsRead(string connectionString, Guid taskId) : DbCommandInterceptor
    {
        private int _fired;

        public int Fired => Volatile.Read(ref _fired);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"AgentTaskEvents\"", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref _fired, 1, 0) == 0)
            {
                await using var other = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
                await other.AgentTasks.Where(t => t.Id == taskId)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Working), cancellationToken);
            }

            return result;
        }
    }

    /// <summary>CARD-1670. Runs <c>change</c> from another connection once the stuck record has been read.</summary>
    private sealed class AfterStuckRecordIsRead(
        string connectionString, Guid taskId, Func<AppDbContext, Guid, CancellationToken, Task> change) : DbCommandInterceptor
    {
        private int _fired;

        public int Fired => Volatile.Read(ref _fired);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"AgentTaskEvents\"", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref _fired, 1, 0) == 0)
            {
                await using var other = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
                await change(other, taskId, cancellationToken);
            }

            return result;
        }
    }

    /// <summary>CARD-1670. Throws in place of the audit event insert, after the conditional stamp has run.</summary>
    private sealed class FailAuditEventInsert : DbCommandInterceptor
    {
        private int _fired;

        public int Fired => Volatile.Read(ref _fired);

        private void Check(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT INTO \"AgentTaskEvents\"", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _fired);
                throw new InvalidOperationException("CARD-1670 injected audit event failure");
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
