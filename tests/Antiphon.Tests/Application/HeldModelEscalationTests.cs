using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1490: an explicit escalation must not land a task on a held alias. Create already
/// refuses a held explicit target (ModelAvailabilityCreateTests); escalation used to kill the
/// running session, move the task to the held tier and leave it parked behind the hold.
/// Isolated schema per test.
/// </summary>
[Category("Integration")]
public class HeldModelEscalationTests
{
    [Test]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Working)]
    public async Task Escalating_onto_a_held_alias_is_409_and_leaves_the_task_untouched(AgentTaskStatus status)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var until = new DateTime(2099, 10, 23, 8, 0, 0, DateTimeKind.Utc);
        await SeedHoldAsync(db, until);
        var sessionId = Guid.NewGuid();
        var task = await SeedTaskAsync(db, status, sessionId);
        var stopper = new RecordingSessionStopper();

        var ex = await Should.ThrowAsync<ModelDisabledException>(
            () => CreateService(db, stopper).EscalateAsync(task.Id, null, CancellationToken.None),
            "C1490-escalate-held-refused");

        ex.Code.ShouldBe("model_disabled", "C1490-escalate-code");
        ex.Message.ShouldContain("fable is disabled until 2099-10-23T08:00:00Z", customMessage: "C1490-escalate-names-alias-until");
        ex.Hold.Kind.ShouldBe(AgentKind.ClaudeCode, "C1490-escalate-names-kind");
        ex.Available.ShouldContain(ModelAlias.Opus, "C1490-escalate-available");
        stopper.Killed.ShouldBeEmpty("C1490-escalate-no-kill");
        var after = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        after.ModelLevel.ShouldBe(AgentModelLevel.High, "C1490-escalate-level-unchanged");
        after.AgentKind.ShouldBe(AgentKind.ClaudeCode, "C1490-escalate-no-kind-substitution");
        after.Status.ShouldBe(status, "C1490-escalate-status-unchanged");
        after.EscalatedFrom.ShouldBeNull("C1490-escalate-not-stamped");
        after.AgentSessionId.ShouldBe(sessionId, "C1490-escalate-session-kept");
    }

    [Test]
    public async Task Escalating_after_the_hold_lapsed_is_unchanged()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SeedHoldAsync(db, DateTime.UtcNow.AddMinutes(-5));
        var task = await SeedTaskAsync(db, AgentTaskStatus.Failed, Guid.NewGuid());

        var summary = await CreateService(db, new RecordingSessionStopper())
            .EscalateAsync(task.Id, null, CancellationToken.None);

        summary.ModelLevel.ShouldBe(AgentModelLevel.Frontier, "C1490-lapsed-escalates");
        summary.EscalatedFrom.ShouldBe(AgentModelLevel.High, "C1490-lapsed-stamped");
        summary.Status.ShouldBe(AgentTaskStatus.Queued, "C1490-lapsed-requeued");
    }

    [Test]
    public async Task Escalating_onto_a_non_held_alias_is_unchanged_while_another_alias_is_held()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SeedHoldAsync(db, DateTime.UtcNow.AddHours(1));
        var task = await SeedTaskAsync(db, AgentTaskStatus.Failed, Guid.NewGuid(), AgentModelLevel.Medium);

        var summary = await CreateService(db, new RecordingSessionStopper())
            .EscalateAsync(task.Id, null, CancellationToken.None);

        summary.ModelLevel.ShouldBe(AgentModelLevel.High, "C1490-non-held-escalates");
        summary.Status.ShouldBe(AgentTaskStatus.Queued, "C1490-non-held-requeued");
    }

    private static async Task SeedHoldAsync(AppDbContext db, DateTime until)
    {
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = AgentKind.ClaudeCode,
            ModelAlias = ModelAlias.Fable,
            Source = ModelAvailabilitySource.Manual,
            DisabledUntil = until,
            HitAt = DateTime.UtcNow.AddHours(-1),
            Reason = "fable usage exhausted",
        });
        await db.SaveChangesAsync();
    }

    private static async Task<AgentTask> SeedTaskAsync(
        AppDbContext db, AgentTaskStatus status, Guid sessionId, AgentModelLevel level = AgentModelLevel.High)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "Held escalation",
            Goal = "Seeded goal.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Debug,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = level,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            Status = status,
            AgentSessionId = sessionId,
            DispatchedAt = DateTime.UtcNow.AddMinutes(-30),
            CreatedAt = DateTime.UtcNow.AddMinutes(-31),
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return task;
    }

    private static AgentTaskService CreateService(AppDbContext db, RecordingSessionStopper stopper) =>
        new(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { MaxDepth = 5, MaxTasksPerRoot = 40, AllowedRoots = [] }),
            new MockEventBus(),
            stopper,
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            modelAvailability: new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance));
}
