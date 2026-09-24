using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0641 V-6 / D-5: Held caller notes deduplicate per request and stable owner, while the
/// diagnostic Held events, HoldEpisode and progress clocks keep their existing behaviour.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandHeldNotificationTests
{
    private const string Unknown = "unknown";

    [Test]
    public async Task C641_Reason_flips_for_same_owner_emit_one_note_across_restart()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var a = await AddWriterAsync(h, "A");
        DateTime? progress = null;
        for (var flip = 0; flip < 13; flip++)
        {
            if (flip == 6)
                await h.RestartServicesAsync();
            if (flip % 2 == 0)
                await HoldAsync(h, Observation.Writer);
            else
                await HoldAsync(h, Observation.Known, a);
            var request = await RequestAsync(h);
            request.HoldReasonCode.ShouldBe(flip % 2 == 0 ? "repository_or_source_writer" : "repository_mutation_lease_busy");
            request.HoldingTaskId.ShouldBe(a);
            progress ??= request.LastProgressAt;
            request.LastProgressAt.ShouldBe(progress.Value);
        }

        await using var observer = h.CreateContext();
        var saved = await RequestAsync(h);
        saved.HoldEpisode.ShouldBe(13, "each reason flip remains a diagnostic Held episode");
        (await observer.AgentTaskEvents.CountAsync(e => e.LandRequestId == saved.Id && e.Type == AgentTaskEventType.Held))
            .ShouldBe(13);
        (await HeldNotesAsync(h, saved.Id)).Count.ShouldBe(1, "reason flips for the same owner must not mint caller notes");
        saved.HoldNotificationOwnerKey.ShouldBe(Key(a));
    }

    [Test]
    [Arguments("unknown-first")]
    [Arguments("known-first")]
    public async Task C641_Unknown_observations_do_not_change_owner_or_reemit(string variant)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var a = await AddWriterAsync(h, "A");
        var steps = variant == "unknown-first"
            ? new[] { Observation.Untagged, Observation.Writer, Observation.Untagged, Observation.Foreign, Observation.Writer }
            : new[] { Observation.Writer, Observation.Foreign, Observation.Writer, Observation.Untagged, Observation.Writer };
        var anchors = new List<string?>();
        foreach (var step in steps)
        {
            await HoldAsync(h, step);
            anchors.Add((await RequestAsync(h)).HoldNotificationOwnerKey);
        }

        var request = await RequestAsync(h);
        (await HeldNotesAsync(h, request.Id)).Count.ShouldBe(1, "unknown observations never manufacture a holder change");
        var expected = variant == "unknown-first"
            ? new[] { Unknown, Key(a), Key(a), Key(a), Key(a) }
            : new[] { Key(a), Key(a), Key(a), Key(a), Key(a) };
        anchors.ShouldBe(expected);
        request.HoldEpisode.ShouldBe(steps.Length);
    }

    [Test]
    public async Task C641_Different_known_owner_emits_one_additional_note()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var a = await AddWriterAsync(h, "A");
        await HoldAsync(h, Observation.Writer);
        var request = await RequestAsync(h);
        (await HeldNotesAsync(h, request.Id)).Count.ShouldBe(1);

        await SetStatusAsync(h, a, AgentTaskStatus.Succeeded);
        var b = await AddWriterAsync(h, "B");
        await HoldAsync(h, Observation.Writer);
        var notes = await HeldNotesAsync(h, request.Id);
        notes.Count.ShouldBe(2, "a different known holder is one new caller note");
        (await RequestAsync(h)).HoldNotificationOwnerKey.ShouldBe(Key(b));
        notes.OrderBy(n => n.CreatedAt).Last().Body.ShouldContain(b.ToString("N"));

        await HoldAsync(h, Observation.Untagged);
        await HoldAsync(h, Observation.Known, b);
        await HoldAsync(h, Observation.Writer);
        (await HeldNotesAsync(h, request.Id)).Count.ShouldBe(2, "B across reason and identity-unknown flips stays one holder");
        var saved = await RequestAsync(h);
        saved.HoldNotificationOwnerKey.ShouldBe(Key(b));
        saved.HoldEpisode.ShouldBe(5);
    }

    [Test]
    public async Task C641_New_request_gets_its_own_first_note()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var a = await AddWriterAsync(h, "A");
        await HoldAsync(h, Observation.Writer);
        var first = await RequestAsync(h);
        first.HoldNotificationOwnerKey.ShouldBe(Key(a));

        await using (var db = h.CreateContext())
        {
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == first.Id);
            request.IsPending = false;
            request.State = LandRequestState.Canceled;
            request.ReconciliationError = "c641_fixture_superseded";
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.LandRequestedAt = null;
            task.CurrentLandRequestId = null;
            await db.SaveChangesAsync();
        }

        await HoldAsync(h, Observation.Writer);
        var second = await RequestAsync(h);
        second.Id.ShouldNotBe(first.Id);
        second.HoldNotificationOwnerKey.ShouldBe(Key(a));
        (await HeldNotesAsync(h, second.Id)).Count.ShouldBe(1, "a new request starts with no anchor and owes its own first note");
        (await HeldNotesAsync(h, first.Id)).Count.ShouldBe(1);
        await using var observer = h.CreateContext();
        (await observer.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.Id))
            .HoldNotificationOwnerKey.ShouldBe(Key(a), "a finished request keeps its anchor");
    }

    [Test]
    public async Task C641_Concurrent_holds_commit_one_note_and_dedupe_anchor()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var a = await AddWriterAsync(h, "A");
        await h.RequestAsync();
        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(h.RunAsync)));
        runs.ShouldAllBe(r => r == LandRunResult.Held);

        var request = await RequestAsync(h);
        request.HoldNotificationOwnerKey.ShouldBe(Key(a));
        (await HeldNotesAsync(h, request.Id)).Count.ShouldBe(1);
        await using var observer = h.CreateContext();
        (await observer.AgentTaskEvents.CountAsync(e => e.LandRequestId == request.Id && e.Type == AgentTaskEventType.Held))
            .ShouldBe(1);
        request.HoldEpisode.ShouldBe(1);
    }

    [Test]
    public async Task C641_Rollback_does_not_consume_first_note()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var a = await AddWriterAsync(h, "A");
        await h.RequestAsync();
        h.Fault.TerminalCut = "commit";
        h.Fault.EventKind = AgentTaskEventType.Held;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        h.Fault.Triggered.ShouldBeTrue();

        await using (var observer = h.CreateContext())
        {
            var rolledBack = await observer.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.TaskId == h.Fixture.TaskId);
            rolledBack.HoldNotificationOwnerKey.ShouldBeNull("a rolled-back Held transaction must not consume the first note");
            rolledBack.State.ShouldNotBe(LandRequestState.Held);
            (await observer.AgentTaskLandNotifications.CountAsync(n => n.RequestId == rolledBack.Id)).ShouldBe(0);
            (await observer.AgentTaskEvents.CountAsync(e => e.LandRequestId == rolledBack.Id && e.Type == AgentTaskEventType.Held))
                .ShouldBe(0);
        }

        await h.RestartServicesAsync();
        (await h.RunAsync()).ShouldBe(LandRunResult.Held);
        var request = await RequestAsync(h);
        request.HoldNotificationOwnerKey.ShouldBe(Key(a));
        (await HeldNotesAsync(h, request.Id)).Count.ShouldBe(1, "the retry owes and commits the first note");
    }

    [Test]
    public async Task C641_Upgrade_preserves_existing_held_notification_debt()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var a = await AddWriterAsync(h, "A");
        await HoldAsync(h, Observation.Writer);
        var request = await RequestAsync(h);
        var debt = (await HeldNotesAsync(h, request.Id)).ShouldHaveSingleItem();
        debt.State.ShouldBe(LandNotificationState.Queued, "the pre-upgrade note is still undelivered debt");

        await using (var db = h.CreateContext())
        {
            var migrations = db.Database.GetMigrations().ToArray();
            var added = Array.FindIndex(migrations, m => m.EndsWith("_AddLandHoldNotificationOwner", StringComparison.Ordinal));
            added.ShouldBeGreaterThan(0);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(migrations[added - 1]);
            await migrator.MigrateAsync();
        }

        await h.RestartServicesAsync();
        (await RequestAsync(h)).HoldNotificationOwnerKey.ShouldBeNull("the additive column starts null for existing rows");

        await HoldAsync(h, Observation.Untagged);
        var adopted = await RequestAsync(h);
        adopted.HoldNotificationOwnerKey.ShouldBe(Unknown, "adoption seeds from the current defensible owner");
        var notes = await HeldNotesAsync(h, request.Id);
        var kept = notes.ShouldHaveSingleItem("adoption must not replay the existing Held note");
        kept.Id.ShouldBe(debt.Id);
        kept.Body.ShouldBe(debt.Body);
        kept.State.ShouldBe(LandNotificationState.Queued);

        await HoldAsync(h, Observation.Writer);
        (await RequestAsync(h)).HoldNotificationOwnerKey.ShouldBe(Key(a));
        (await HeldNotesAsync(h, request.Id)).Count.ShouldBe(1);

        await SetStatusAsync(h, a, AgentTaskStatus.Succeeded);
        var b = await AddWriterAsync(h, "B");
        await HoldAsync(h, Observation.Writer);
        (await HeldNotesAsync(h, request.Id)).Count.ShouldBe(2, "adoption does not suppress a real later owner change");
        (await RequestAsync(h)).HoldNotificationOwnerKey.ShouldBe(Key(b));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task C641_Held_note_reaches_busy_and_idle_caller(bool busy)
    {
        await using var world = await LandOutcomeDeliveryHarness.CreateAsync();
        var land = world.Land;
        await using (var db = land.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == land.Git.TaskId);
            task.ReplyTo = AgentTaskReplyTo.Session;
            task.ParentSessionId = world.Caller.SessionId;
            await db.SaveChangesAsync();
        }

        await land.AddSourceAsync();
        await using var other = await world.HoldOtherLeaseAsync();
        (await land.RunAsync()).ShouldBe(LandRunResult.Held);
        AgentTaskLandNotification note;
        await using (var observer = land.CreateContext())
        {
            note = await observer.AgentTaskLandNotifications.AsNoTracking()
                .SingleAsync(n => n.TaskId == land.Git.TaskId && n.Kind == LandNotificationKind.Held);
            note.ParentSessionId.ShouldBe(world.Caller.SessionId);
            (await observer.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == note.RequestId))
                .HoldNotificationOwnerKey.ShouldBe(Unknown);
        }

        if (busy)
            await world.MarkBusyAsync();
        await world.ReconcileAsync(note.Id);
        await world.DeliverAsync(busy);
        await world.ReconcileAsync(note.Id);
        await world.AssertReceiptAsync(note);

        (await land.RunAsync()).ShouldBe(LandRunResult.Held);
        await using (var observer = land.CreateContext())
            (await observer.AgentTaskLandNotifications.CountAsync(n => n.TaskId == land.Git.TaskId && n.Kind == LandNotificationKind.Held))
                .ShouldBe(1);
        (await world.Probes.TryAcquireAsync(land.Git.Repository, CancellationToken.None))
            .ShouldBeNull("the delivered note does not change repository lease exclusion");
    }

    private enum Observation
    {
        Writer,
        Known,
        Untagged,
        Foreign,
    }

    private static string Key(Guid taskId) => $"task:{taskId:N}";

    private static async Task HoldAsync(LandingSafetyHarness h, Observation observation, Guid? owner = null)
    {
        var leases = h.Services.GetRequiredService<IRepositoryMutationLease>();
        RepositoryLease? lease = observation switch
        {
            Observation.Known => await leases.TryAcquireAsync(h.Fixture.Repository,
                new RepositoryLeaseOwnerTag(owner!.Value, RepositoryLeasePurposes.Dispatch), CancellationToken.None),
            Observation.Untagged => await leases.TryAcquireAsync(h.Fixture.Repository, CancellationToken.None),
            Observation.Foreign => await new RepositoryMutationLease(h.Fixture.Git).TryAcquireAsync(h.Fixture.Repository, CancellationToken.None),
            _ => null,
        };
        if (observation != Observation.Writer)
            lease.ShouldNotBeNull($"{observation} observation must hold the real repository lease");
        try
        {
            (await h.RunAsync()).ShouldBe(LandRunResult.Held);
        }
        finally
        {
            if (lease is not null)
                await lease.DisposeAsync();
        }
    }

    private static async Task<Guid> AddWriterAsync(LandingSafetyHarness h, string title)
    {
        var id = Guid.NewGuid();
        await using var db = h.CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "C641 writer " + title, Goal = "holds the repository",
            Status = AgentTaskStatus.Working, Role = AgentTaskRole.Code, ReplyTo = AgentTaskReplyTo.None,
            Workspace = WorkspaceMode.Shared, WorkingDirectory = h.Fixture.Repository, RepoPath = h.Fixture.Repository,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SetStatusAsync(LandingSafetyHarness h, Guid taskId, AgentTaskStatus status)
    {
        await using var db = h.CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == taskId)).Status = status;
        await db.SaveChangesAsync();
    }

    private static async Task<AgentTaskLandRequest> RequestAsync(LandingSafetyHarness h)
    {
        await using var db = h.CreateContext();
        return await db.AgentTaskLandRequests.AsNoTracking()
            .Where(r => r.TaskId == h.Fixture.TaskId)
            .OrderByDescending(r => r.RequestedAt)
            .FirstAsync();
    }

    private static async Task<List<AgentTaskLandNotification>> HeldNotesAsync(LandingSafetyHarness h, Guid requestId)
    {
        await using var db = h.CreateContext();
        return await db.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.RequestId == requestId && n.Kind == LandNotificationKind.Held)
            .ToListAsync();
    }
}
