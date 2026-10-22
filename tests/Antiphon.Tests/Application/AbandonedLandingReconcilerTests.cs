using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1446 S1 (D-1, V-1, V-2): the daily reconciler refuses an unpublished landing that stopped in a
/// resumable phase, whose task is terminal, with no pending request and no update for
/// <c>WorktreeResidue:AbandonedLandingDays</c>. Rows are seeded directly; no Git runs.
/// </summary>
[Category("Integration")]
public sealed class AbandonedLandingReconcilerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// V-1. An 8-day-old schema-3 <c>push_rejected</c> operation at <c>PushStarted</c> with
    /// <c>PushExitCode=1</c>, task Succeeded and request Completed becomes <c>Refused</c>/<c>Refused</c>,
    /// its <c>LastReason</c> is prefixed <c>landing_abandoned:</c>, and exactly one Warning event is written.
    /// A second run is a no-op.
    /// </summary>
    [Test]
    public async Task C1446_abandoned_push_rejected_landing_becomes_refused_after_seven_days()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var taskId = Guid.NewGuid();
        Guid opId;
        await using (var db = new AppDbContext(options))
        {
            var request = Request(taskId, LandRequestState.Completed);
            SeedTask(db, taskId, AgentTaskStatus.Succeeded, request.Id);
            db.AgentTaskLandRequests.Add(request);
            var op = Landing(taskId, LandPhase.PushStarted, Now.UtcDateTime.AddDays(-8), "push_rejected", pushExitCode: 1);
            opId = op.Id;
            db.AgentTaskLandings.Add(op);
            await db.SaveChangesAsync();
        }

        var logger = new CapturingLogger();
        await using (var db = new AppDbContext(options))
            (await Reconciler(db, logger, days: 7).RunAsync(CancellationToken.None)).ShouldBe(1);

        await using (var db = new AppDbContext(options))
        {
            var op = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == opId);
            op.Phase.ShouldBe(LandPhase.Refused);
            op.Publication.ShouldBe(LandPublicationOutcome.Refused);
            op.Active.ShouldBeTrue("a refused operation stays the active row until -Land replaces it");
            op.LastReason.ShouldBe("landing_abandoned:push_rejected");
            op.UpdatedAt.ShouldBe(Now.UtcDateTime);
            var events = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == taskId).ToListAsync();
            events.Count.ShouldBe(1);
            events[0].Type.ShouldBe(AgentTaskEventType.Warning);
            events[0].LandingOperationId.ShouldBe(opId);
            events[0].Detail.ShouldStartWith("landing_abandoned: operation " + opId.ToString("N") + " refused push_rejected at PushStarted on ");
            events[0].Detail.ShouldContain("no resume within 7 days; the next -Land starts a fresh operation");
        }

        await using (var db = new AppDbContext(options))
            (await Reconciler(db, logger, days: 7).RunAsync(CancellationToken.None)).ShouldBe(0, "already refused");
        await using (var db = new AppDbContext(options))
            (await db.AgentTaskEvents.AsNoTracking().CountAsync(e => e.AgentTaskId == taskId)).ShouldBe(1, "no second event");
    }

    /// <summary>
    /// V-2. Guard rows: a push that returned 0 is kept and logged at Warning; six days is not seven;
    /// a pending request keeps the operation; a published operation is untouched; a schema-2
    /// <c>TargetAdvanceStarted source_changed</c> row is refused by the same rule; days=0 disables the reconciler.
    /// </summary>
    [Test]
    [Arguments("push-exit-zero")]
    [Arguments("six-days")]
    [Arguments("pending-request")]
    [Arguments("published")]
    [Arguments("schema2-target-advance")]
    [Arguments("days-zero")]
    public async Task C1446_guard_rows(string row)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var taskId = Guid.NewGuid();
        var days = row == "days-zero" ? 0 : 7;
        var age = row == "six-days" ? TimeSpan.FromDays(6) : TimeSpan.FromDays(30);
        var pending = row == "pending-request";
        var expectRefused = row == "schema2-target-advance";
        Guid opId;
        await using (var db = new AppDbContext(options))
        {
            var request = Request(taskId, pending ? LandRequestState.Queued : LandRequestState.Completed);
            SeedTask(db, taskId, AgentTaskStatus.Succeeded, request.Id);
            db.AgentTaskLandRequests.Add(request);
            var op = row switch
            {
                "push-exit-zero" => Landing(taskId, LandPhase.PushStarted, Now.UtcDateTime - age, "push_unconfirmed", pushExitCode: 0),
                "published" => Landing(taskId, LandPhase.PushStarted, Now.UtcDateTime - age, "push_rejected", pushExitCode: 1,
                    publication: LandPublicationOutcome.Landed, remoteConfirmedAt: Now.UtcDateTime - age),
                "schema2-target-advance" => Landing(taskId, LandPhase.TargetAdvanceStarted, Now.UtcDateTime - age, "source_changed",
                    schema: 2),
                _ => Landing(taskId, LandPhase.PushStarted, Now.UtcDateTime - age, "push_rejected", pushExitCode: 1),
            };
            opId = op.Id;
            db.AgentTaskLandings.Add(op);
            await db.SaveChangesAsync();
        }

        var logger = new CapturingLogger();
        await using (var db = new AppDbContext(options))
            (await Reconciler(db, logger, days).RunAsync(CancellationToken.None)).ShouldBe(expectRefused ? 1 : 0, row);

        await using (var db = new AppDbContext(options))
        {
            var op = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == opId);
            var events = await db.AgentTaskEvents.AsNoTracking().CountAsync(e => e.AgentTaskId == taskId);
            if (expectRefused)
            {
                op.Phase.ShouldBe(LandPhase.Refused, row);
                op.Publication.ShouldBe(LandPublicationOutcome.Refused, row);
                op.LastReason.ShouldBe("landing_abandoned:source_changed", row);
                events.ShouldBe(1, row);
                return;
            }

            op.Phase.ShouldBe(row == "schema2-target-advance" ? LandPhase.TargetAdvanceStarted : LandPhase.PushStarted, row);
            op.Publication.ShouldBe(row == "published" ? LandPublicationOutcome.Landed : LandPublicationOutcome.Unconfirmed, row);
            (op.LastReason ?? string.Empty).StartsWith("landing_abandoned:", StringComparison.Ordinal).ShouldBeFalse(row);
            op.UpdatedAt.ShouldBe(Now.UtcDateTime - age, row);
            events.ShouldBe(0, row);
        }

        if (row == "push-exit-zero")
            logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("push_unconfirmed", StringComparison.Ordinal),
                "a zero push exit that was never confirmed is a human decision");
        else
            logger.Entries.ShouldNotContain(e => e.Level == LogLevel.Warning, row);
    }

    private static AbandonedLandingReconciler Reconciler(AppDbContext db, CapturingLogger logger, int days) => new(
        db,
        Options.Create(new WorktreeResidueSettings { AbandonedLandingDays = days }),
        new FakeTimeProvider(Now),
        logger);

    private static void SeedTask(AppDbContext db, Guid id, AgentTaskStatus status, Guid currentLandRequestId)
    {
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "C1446", Goal = "C1446",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = "/repo", RepoPath = "/repo", Status = status,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = Now.UtcDateTime.AddDays(-40),
            CompletedAt = Now.UtcDateTime.AddDays(-31),
            CurrentLandRequestId = currentLandRequestId,
            LandRequestedAt = Now.UtcDateTime.AddDays(-31),
        });
    }

    private static AgentTaskLandRequest Request(Guid taskId, LandRequestState state) => new()
    {
        Id = Guid.NewGuid(), TaskId = taskId, RequestedAt = Now.UtcDateTime.AddDays(-31),
        LastEvaluatedAt = Now.UtcDateTime.AddDays(-31), LastProgressAt = Now.UtcDateTime.AddDays(-31), State = state,
        IsPending = state is LandRequestState.Queued or LandRequestState.Held or LandRequestState.Running or LandRequestState.NeedsResolution,
        ExpectedSourceSha = new string('a', 40),
    };

    private static AgentTaskLanding Landing(Guid taskId, LandPhase phase, DateTime updatedAt, string lastReason,
        int? pushExitCode = null, int schema = 3, LandPublicationOutcome publication = LandPublicationOutcome.Unconfirmed,
        DateTime? remoteConfirmedAt = null) => new()
    {
        Id = Guid.NewGuid(), TaskId = taskId, SchemaVersion = schema, Active = true, Phase = phase,
        Publication = publication, RemoteConfirmedAt = remoteConfirmedAt, LastReason = lastReason,
        // CK_AgentTaskLandings_V2Approval: a schema-2/3 row carries its approval and reviewed source.
        ApprovalLandRequestId = Guid.NewGuid(), ReviewedSourceSha = new string('a', 40), ApprovalKind = LandApprovalKind.ExplicitCaller,
        PushExitCode = pushExitCode, PushStartedAt = pushExitCode is null ? null : updatedAt,
        RepositoryPath = "/repo", WorktreePath = "/trees/x", LandWorktreePath = "/trees/land/x",
        CommonDirectory = "/repo/.git", GitDirectory = "/repo/.git", SourceFullRef = "refs/heads/feat/x",
        TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master",
        OriginalSourceSha = new string('a', 40), TargetBeforeSha = new string('b', 40),
        SourceLocalSha = new string('a', 40), LocalTargetBeforeSha = new string('b', 40),
        CreatedAt = updatedAt.AddHours(-1), UpdatedAt = updatedAt,
    };

    private sealed class CapturingLogger : ILogger<AbandonedLandingReconciler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
