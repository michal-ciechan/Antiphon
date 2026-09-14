using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0508 S2b. Custody of a dispatch-base warning from the successful claim (capture) to the
/// Warning/notification pair (materialization): what the claim commits, what the projection is
/// allowed to read, and what happens when either half is interrupted or repeated.
///
/// <para>The capture rows drive the REAL dispatcher against a real git repo; the projection rows
/// drive the real <see cref="DispatchBaseWarningIntentService"/> against a real PostgreSQL schema.
/// Nothing here asserts native delivery — that is the DE lane's job.</para>
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Slow")]
public class DispatchBaseNotificationTests
{
    /// <summary>
    /// V-23 / G-66, G-67: one successful claim commits its final dispatch event, the session and
    /// EVERY warning it owes, in one transaction. All three producers fire at once here: a
    /// divergent sibling, a base observed before the default disappeared, and the newly used
    /// unresolved default.
    /// </summary>
    [Test]
    [Timeout(90_000)]
    public async Task C508_ClaimCapturesWarningIntents(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c508-dbn-capture");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "trunk", "master");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0508");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, "divergent sibling work");
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        await db.SaveChangesAsync(ct);

        // Observed under the guard: trunk. Deleted before the claim resolves, so the claim falls
        // to HEAD and owes BOTH a stale-observation warning and an unresolved-default warning.
        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot, "trunk",
            onLeaseAcquired: async () =>
            {
                (await ScratchGitRepo.GitInAsync(repo.Path, "branch", "-D", "trunk")).Ok.ShouldBeTrue();
                await Task.CompletedTask;
            });
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.RepoHead);
        dispatched.AgentSessionId.ShouldNotBeNull();

        var finalDispatch = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Dispatched
                && e.Detail.StartsWith("Dispatched to agent"))
            .SingleAsync(ct);
        var intents = await db.AgentTaskDispatchWarningIntents.AsNoTracking()
            .Where(i => i.TaskId == task.Id).ToListAsync(ct);
        intents.Count.ShouldBe(3);
        intents.ShouldAllBe(i => i.DispatchEventId == finalDispatch.Id);
        intents.ShouldAllBe(i => i.CreatedAt == finalDispatch.At);
        intents.ShouldAllBe(i => i.ParentSessionId == parentSessionId);
        intents.ShouldAllBe(i => i.ReplyTo == AgentTaskReplyTo.Session);
        intents.ShouldContain(i => i.WarningKey == DispatchBaseNotificationPayload.SiblingKey(sibling.Id));
        intents.ShouldContain(i => i.WarningKey == DispatchBaseNotificationPayload.MismatchKey);
        intents.ShouldContain(i => i.WarningKey == DispatchBaseNotificationPayload.DefaultUnresolvedKey);
        intents.Select(i => i.Id).Distinct().Count().ShouldBe(3);
        intents.Select(i => i.NotificationId).Distinct().Count().ShouldBe(3);
        // The dispatcher's post-commit fast path materializes what the claim captured, so each
        // intent already owns its pair — under the ids the claim preallocated, not new ones.
        foreach (var intent in intents)
        {
            var warning = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == intent.Id, ct);
            warning.Type.ShouldBe(AgentTaskEventType.Warning);
            warning.AgentTaskId.ShouldBe(task.Id);
            warning.Detail.ShouldBe(intent.Detail);
            var note = await db.AgentTaskLandNotifications.AsNoTracking()
                .SingleAsync(n => n.Id == intent.NotificationId, ct);
            note.SourceEventId.ShouldBe(intent.Id);
            note.Kind.ShouldBe(LandNotificationKind.DispatchBase);
            note.RequestId.ShouldBeNull();
            note.ParentSessionId.ShouldBe(parentSessionId);
            intent.MaterializedAt.ShouldNotBeNull();
            intent.LastErrorCode.ShouldBeNull();
        }

        var mismatch = intents.Single(i => i.WarningKey == DispatchBaseNotificationPayload.MismatchKey);
        mismatch.Detail.ShouldContain("trunk");
        mismatch.Detail.ShouldContain("HEAD");
        intents.Single(i => i.WarningKey == DispatchBaseNotificationPayload.DefaultUnresolvedKey)
            .Detail.ShouldContain("trunk");
    }

    /// <summary>
    /// V-24 / G-110: capture validates that the dispatch event belongs to the claimed task, and
    /// that it really is the final agent-dispatch event, BEFORE it writes any row.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C508_IntentCaptureBinding(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var owner = await SeedBareTaskAsync(db, Guid.NewGuid());
        var stranger = await SeedBareTaskAsync(db, Guid.NewGuid());
        var foreign = await SeedDispatchEventAsync(db, stranger.Id, DateTime.UtcNow);
        var wrongKind = new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = owner.Id, Type = AgentTaskEventType.Held,
            Detail = "held", At = DateTime.UtcNow,
        };
        db.AgentTaskEvents.Add(wrongKind);
        await db.SaveChangesAsync(ct);

        var service = new DispatchBaseWarningIntentService(db, TimeProvider.System);
        var drafts = new[] { new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, "detail") };

        await Should.ThrowAsync<ValidationException>(
            () => service.CaptureAsync(owner, foreign, drafts, ct));
        await Should.ThrowAsync<ValidationException>(
            () => service.CaptureAsync(owner, wrongKind, drafts, ct));

        db.ChangeTracker.Clear();
        (await db.AgentTaskDispatchWarningIntents.CountAsync(ct)).ShouldBe(0);

        // A blank key or blank detail is skipped, not persisted as an empty obligation.
        var good = await SeedDispatchEventAsync(db, owner.Id, DateTime.UtcNow);
        var ids = await service.CaptureAsync(owner, good,
            [new DispatchWarningDraft("", "detail"), new DispatchWarningDraft("k", "  ")], ct);
        await db.SaveChangesAsync(ct);
        ids.ShouldBeEmpty();
        db.ChangeTracker.Clear();
        (await db.AgentTaskDispatchWarningIntents.CountAsync(ct)).ShouldBe(0);
    }

    /// <summary>
    /// V-24 / G-68, G-69, G-70: identity is the final dispatch EVENT plus the warning key. The
    /// same committed event replays to the same ids whatever order the drafts arrive in; a new
    /// event with the same task, Attempt and text owes brand new ids.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C508_IntentAttemptIdentity(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var task = await SeedBareTaskAsync(db, Guid.NewGuid());
        var at = new DateTime(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc);
        var first = await SeedDispatchEventAsync(db, task.Id, at);
        var service = new DispatchBaseWarningIntentService(db, TimeProvider.System);
        var a = new DispatchWarningDraft(DispatchBaseNotificationPayload.SiblingKey(Guid.NewGuid()), "sibling detail");
        var b = new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, "mismatch detail");

        var original = await service.CaptureAsync(task, first, [a, b], ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var replay = await service.CaptureAsync(task, first, [b, a], ct);
        await db.SaveChangesAsync(ct);
        replay.ShouldBe([original[1], original[0]]);
        db.ChangeTracker.Clear();
        (await db.AgentTaskDispatchWarningIntents.CountAsync(i => i.TaskId == task.Id, ct)).ShouldBe(2);

        // Same task, same Attempt, same text — a different successful claim, so different identity.
        var second = await SeedDispatchEventAsync(db, task.Id, at);
        var fresh = await service.CaptureAsync(task, second, [a, b], ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        fresh.ShouldNotContain(original[0]);
        fresh.ShouldNotContain(original[1]);
        var all = await db.AgentTaskDispatchWarningIntents.AsNoTracking()
            .Where(i => i.TaskId == task.Id).ToListAsync(ct);
        all.Count.ShouldBe(4);
        all.Select(i => i.Id).Distinct().Count().ShouldBe(4);
        all.Select(i => i.NotificationId).Distinct().Count().ShouldBe(4);
    }

    /// <summary>
    /// V-24 / G-108, G-109: the DATABASE, not only the service, refuses a duplicate
    /// (DispatchEventId, WarningKey) pair and a duplicate preallocated NotificationId.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C508_IntentUniqueKeys(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var task = await SeedBareTaskAsync(db, Guid.NewGuid());
        var at = DateTime.UtcNow;
        var dispatchEvent = await SeedDispatchEventAsync(db, task.Id, at);
        var seed = NewIntent(task, dispatchEvent, DispatchBaseNotificationPayload.MismatchKey, "detail", at);
        db.AgentTaskDispatchWarningIntents.Add(seed);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        await using (var duplicateKey = CreateContext(schema))
        {
            var clash = NewIntent(task, dispatchEvent, DispatchBaseNotificationPayload.MismatchKey, "other", at);
            duplicateKey.AgentTaskDispatchWarningIntents.Add(clash);
            var ex = await Should.ThrowAsync<DbUpdateException>(() => duplicateKey.SaveChangesAsync(ct));
            ex.InnerException!.Message.ShouldContain("IX_AgentTaskDispatchWarningIntents_DispatchEventId_WarningKey");
        }

        await using (var duplicateNote = CreateContext(schema))
        {
            var second = await SeedDispatchEventAsync(duplicateNote, task.Id, at);
            var clash = NewIntent(task, second, DispatchBaseNotificationPayload.DefaultUnresolvedKey, "other", at);
            clash.NotificationId = seed.NotificationId;
            duplicateNote.AgentTaskDispatchWarningIntents.Add(clash);
            var ex = await Should.ThrowAsync<DbUpdateException>(() => duplicateNote.SaveChangesAsync(ct));
            ex.InnerException!.Message.ShouldContain("IX_AgentTaskDispatchWarningIntents_NotificationId");
        }

        await using var verify = CreateContext(schema);
        (await verify.AgentTaskDispatchWarningIntents.CountAsync(i => i.TaskId == task.Id, ct)).ShouldBe(1);
    }

    /// <summary>
    /// V-25 / G-72, G-73, G-74, G-75, G-76, G-114, G-115: the projection copies the committed
    /// intent and re-evaluates nothing. The task's route, Attempt and status all move after
    /// capture; the Warning and the note still carry the original ids, body, destination and age.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    [Arguments(AgentTaskStatus.Succeeded)]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    [Arguments(AgentTaskStatus.Queued)]
    public async Task C508_IntentProjectionCustody(AgentTaskStatus laterStatus, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var destinationA = Guid.NewGuid();
        var destinationB = Guid.NewGuid();
        var task = await SeedBareTaskAsync(db, destinationA);
        var at = new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);
        var dispatchEvent = await SeedDispatchEventAsync(db, task.Id, at);
        var service = new DispatchBaseWarningIntentService(db, TimeProvider.System);
        var ids = await service.CaptureAsync(task, dispatchEvent,
            [new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, "frozen mismatch detail")], ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var captured = await db.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == ids[0], ct);

        // Everything the projection might be tempted to re-read moves.
        await db.AgentTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(u => u
            .SetProperty(t => t.ParentSessionId, destinationB)
            .SetProperty(t => t.Attempt, 7)
            .SetProperty(t => t.Status, laterStatus), ct);

        await using var projector = CreateContext(schema);
        await new DispatchBaseWarningIntentService(projector, TimeProvider.System)
            .MaterializeAsync(captured.Id, ct);

        await using var verify = CreateContext(schema);
        var warning = await verify.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == captured.Id, ct);
        warning.Type.ShouldBe(AgentTaskEventType.Warning);
        warning.AgentTaskId.ShouldBe(task.Id);
        warning.Detail.ShouldBe(captured.Detail);
        warning.At.ShouldBe(captured.CreatedAt);

        var note = await verify.AgentTaskLandNotifications.AsNoTracking()
            .SingleAsync(n => n.Id == captured.NotificationId, ct);
        note.SourceEventId.ShouldBe(captured.Id);
        note.Kind.ShouldBe(LandNotificationKind.DispatchBase);
        note.RequestId.ShouldBeNull();
        note.ParentSessionId.ShouldBe(destinationA, "the projection must keep the ORIGINAL destination");
        note.Body.ShouldBe(captured.Body);
        note.ContentDigest.ShouldBe(captured.ContentDigest);
        note.CreatedAt.ShouldBe(captured.CreatedAt, "the note must keep the warning's original age");
        note.State.ShouldBe(captured.InitialState);

        var settled = await verify.AgentTaskDispatchWarningIntents.AsNoTracking()
            .SingleAsync(i => i.Id == captured.Id, ct);
        settled.MaterializedAt.ShouldNotBeNull();
        settled.Attempt.ShouldBe(captured.Attempt);
        settled.LastErrorCode.ShouldBeNull();
    }

    /// <summary>
    /// V-26 / G-79, G-80, G-81, G-82: every integrity refusal leaves the intent pending with an
    /// error recorded and writes NO replacement rows. Each arm breaks exactly one invariant.
    /// </summary>
    [Test]
    [Timeout(90_000)]
    [Arguments("digest")]
    [Arguments("header")]
    [Arguments("binding")]
    [Arguments("existing-warning")]
    [Arguments("existing-note")]
    public async Task C508_IntentProjectionIntegrity(string corruption, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var task = await SeedBareTaskAsync(db, Guid.NewGuid());
        var stranger = await SeedBareTaskAsync(db, Guid.NewGuid());
        var at = new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);
        var dispatchEvent = await SeedDispatchEventAsync(db, task.Id, at);
        var service = new DispatchBaseWarningIntentService(db, TimeProvider.System);
        var ids = await service.CaptureAsync(task, dispatchEvent,
            [new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, "integrity detail")], ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var intent = await db.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == ids[0], ct);
        var expectedError = corruption switch
        {
            "digest" => "intent_digest_mismatch",
            "header" => "intent_header_mismatch",
            "binding" => "intent_dispatch_binding",
            _ => "intent_projection_collision",
        };

        switch (corruption)
        {
            case "digest":
                await db.AgentTaskDispatchWarningIntents.Where(i => i.Id == intent.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(i => i.ContentDigest, "0000"), ct);
                break;
            case "header":
                // A body whose header names the wrong task, with a digest that still matches it —
                // so only the independent header check can catch this.
                var badHeader = DispatchBaseNotificationPayload.Body(
                    intent.NotificationId, stranger.Id, intent.Id, intent.Detail);
                var validDigest = DispatchBaseNotificationPayload.Digest(
                    intent.ReplyTo, intent.ParentSessionId, badHeader);
                await db.AgentTaskDispatchWarningIntents.Where(i => i.Id == intent.Id)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(i => i.Body, badHeader)
                        .SetProperty(i => i.ContentDigest, validDigest), ct);
                break;
            case "binding":
                var strangerEvent = await SeedDispatchEventAsync(db, stranger.Id, at);
                await db.AgentTaskDispatchWarningIntents.Where(i => i.Id == intent.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(i => i.DispatchEventId, strangerEvent.Id), ct);
                break;
            case "existing-warning":
                db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = intent.Id, AgentTaskId = task.Id, Type = AgentTaskEventType.Warning,
                    Detail = "someone already used this id", At = at,
                });
                await db.SaveChangesAsync(ct);
                break;
            default:
                var squatterEvent = await SeedDispatchEventAsync(db, task.Id, at);
                db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
                {
                    Id = intent.NotificationId, TaskId = task.Id, SourceEventId = squatterEvent.Id,
                    Kind = LandNotificationKind.DispatchBase, ReplyTo = AgentTaskReplyTo.None,
                    Body = "squatter", ContentDigest = "x", CreatedAt = at, NextAttemptAt = at,
                    State = LandNotificationState.NotRequired,
                });
                await db.SaveChangesAsync(ct);
                break;
        }

        db.ChangeTracker.Clear();
        await using var projector = CreateContext(schema);
        await new DispatchBaseWarningIntentService(projector, TimeProvider.System)
            .MaterializeAsync(intent.Id, ct);

        await using var verify = CreateContext(schema);
        var after = await verify.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == intent.Id, ct);
        after.MaterializedAt.ShouldBeNull("a refusal must never discharge the obligation");
        after.LastErrorCode.ShouldBe(expectedError);
        after.MaterializationAttempts.ShouldBe(1);
        after.NextAttemptAt.ShouldBeGreaterThan(after.CreatedAt);

        // No replacement rows: the only pre-existing squatter is the one this arm planted.
        var warnings = await verify.AgentTaskEvents.AsNoTracking()
            .CountAsync(e => e.Id == intent.Id, ct);
        warnings.ShouldBe(corruption == "existing-warning" ? 1 : 0);
        var notes = await verify.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.Id == intent.NotificationId).ToListAsync(ct);
        notes.Count.ShouldBe(corruption == "existing-note" ? 1 : 0);
        if (corruption == "existing-note")
            notes[0].Body.ShouldBe("squatter", "the refusal must not overwrite the existing row");
    }

    /// <summary>
    /// V-26 / G-77: two materializers race the same intent through separate scopes. The row lock
    /// serializes them; the loser reloads a materialized row and no-ops. One pair, no errors.
    /// </summary>
    [Test]
    [Timeout(90_000)]
    public async Task C508_IntentProjectionConcurrent(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var task = await SeedBareTaskAsync(db, Guid.NewGuid());
        var at = DateTime.UtcNow;
        var dispatchEvent = await SeedDispatchEventAsync(db, task.Id, at);
        var ids = await new DispatchBaseWarningIntentService(db, TimeProvider.System).CaptureAsync(
            task, dispatchEvent,
            [new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, "concurrent detail")], ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        await using var left = CreateContext(schema);
        await using var right = CreateContext(schema);
        var barrier = new Barrier(2);
        async Task RunAsync(AppDbContext context)
        {
            await Task.Yield();
            barrier.SignalAndWait(ct);
            await new DispatchBaseWarningIntentService(context, TimeProvider.System).MaterializeAsync(ids[0], ct);
        }

        await Task.WhenAll(RunAsync(left), RunAsync(right));

        await using var verify = CreateContext(schema);
        (await verify.AgentTaskEvents.CountAsync(e => e.Id == ids[0], ct)).ShouldBe(1);
        var intent = await verify.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == ids[0], ct);
        (await verify.AgentTaskLandNotifications.CountAsync(n => n.Id == intent.NotificationId, ct)).ShouldBe(1);
        intent.MaterializedAt.ShouldNotBeNull();
        intent.LastErrorCode.ShouldBeNull("a serialized loser is a no-op, not an error");
        intent.MaterializationAttempts.ShouldBe(0);
    }

    /// <summary>
    /// V-26 / G-78: the committed projection wins a lost acknowledgement. Re-running the
    /// materializer after the pair is committed changes nothing — same ids, same marker, same
    /// attempt count — and never produces a second pair.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C508_IntentProjectionLostAcknowledgement(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var task = await SeedBareTaskAsync(db, Guid.NewGuid());
        var at = DateTime.UtcNow;
        var dispatchEvent = await SeedDispatchEventAsync(db, task.Id, at);
        var ids = await new DispatchBaseWarningIntentService(db, TimeProvider.System).CaptureAsync(
            task, dispatchEvent,
            [new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, "lost ack detail")], ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        await using (var first = CreateContext(schema))
            await new DispatchBaseWarningIntentService(first, TimeProvider.System).MaterializeAsync(ids[0], ct);

        await using var read = CreateContext(schema);
        var settled = await read.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == ids[0], ct);
        var marker = settled.MaterializedAt;

        for (var i = 0; i < 2; i++)
        {
            await using var again = CreateContext(schema);
            await new DispatchBaseWarningIntentService(again, TimeProvider.System).MaterializeAsync(ids[0], ct);
        }

        await using var verify = CreateContext(schema);
        var after = await verify.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == ids[0], ct);
        after.MaterializedAt.ShouldBe(marker);
        after.MaterializationAttempts.ShouldBe(0);
        after.LastErrorCode.ShouldBeNull();
        (await verify.AgentTaskEvents.CountAsync(e => e.Id == ids[0], ct)).ShouldBe(1);
        (await verify.AgentTaskLandNotifications.CountAsync(n => n.Id == after.NotificationId, ct)).ShouldBe(1);
    }

    /// <summary>
    /// V-27 / G-83: a failing projection persists a bounded backoff —
    /// 5, 10, 20, 40, 80, 160, 300, 300 seconds — and never discharges the intent.
    /// </summary>
    [Test]
    [Timeout(90_000)]
    public async Task C508_IntentMaterializationRetry(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var task = await SeedBareTaskAsync(db, Guid.NewGuid());
        var at = new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(at);
        var dispatchEvent = await SeedDispatchEventAsync(db, task.Id, at);
        var ids = await new DispatchBaseWarningIntentService(db, clock).CaptureAsync(
            task, dispatchEvent,
            [new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, "retry detail")], ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        // Permanently invalid, so every pass refuses for the same reason.
        await db.AgentTaskDispatchWarningIntents.Where(i => i.Id == ids[0])
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.ContentDigest, "0000"), ct);

        foreach (var expected in new[] { 5, 10, 20, 40, 80, 160, 300, 300 })
        {
            await using var projector = CreateContext(schema);
            await new DispatchBaseWarningIntentService(projector, clock).MaterializeAsync(ids[0], ct);
            await using var read = CreateContext(schema);
            var row = await read.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == ids[0], ct);
            (row.NextAttemptAt - clock.GetUtcNow().UtcDateTime).TotalSeconds.ShouldBe(expected, 0.001);
            row.MaterializedAt.ShouldBeNull();
            row.LastErrorCode.ShouldBe("intent_digest_mismatch");
        }

        await using var verify = CreateContext(schema);
        var final = await verify.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == ids[0], ct);
        final.MaterializationAttempts.ShouldBe(8);
        (await verify.AgentTaskEvents.CountAsync(e => e.Id == ids[0], ct)).ShouldBe(0);
        (await verify.AgentTaskLandNotifications.CountAsync(n => n.Id == final.NotificationId, ct)).ShouldBe(0);
    }

    /// <summary>
    /// V-18 / G-53, G-62: the captured body is the independently formatted header plus the
    /// observed detail, the digest binds route AND complete body, and both survive the task's
    /// route moving twice. A different route or a different body changes the digest.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C508_WarningPayloadAndDestination(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var destinationA = Guid.NewGuid();
        var destinationB = Guid.NewGuid();
        var task = await SeedBareTaskAsync(db, destinationA);
        var at = new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);
        var dispatchEvent = await SeedDispatchEventAsync(db, task.Id, at);
        const string detail = "CARD-0508's kept branch feat/card-task-abc12345 is not contained in master.";
        var ids = await new DispatchBaseWarningIntentService(db, TimeProvider.System).CaptureAsync(
            task, dispatchEvent, [new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, detail)], ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var intent = await db.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == ids[0], ct);

        // Formatted here, independently of the factory under test.
        var expectedHeader = $"[dispatch-base {intent.NotificationId:N} task={task.Id:N} warning={intent.Id:N}]";
        intent.Body.ShouldBe(expectedHeader + "\n" + detail);
        intent.Detail.ShouldBe(detail);
        intent.ParentSessionId.ShouldBe(destinationA);
        intent.InitialState.ShouldBe(LandNotificationState.Queued);

        var digest = intent.ContentDigest;
        digest.ShouldBe(DispatchBaseNotificationPayload.Digest(AgentTaskReplyTo.Session, destinationA, intent.Body));
        digest.ShouldNotBe(DispatchBaseNotificationPayload.Digest(AgentTaskReplyTo.Session, destinationB, intent.Body));
        digest.ShouldNotBe(DispatchBaseNotificationPayload.Digest(AgentTaskReplyTo.Session, destinationA, intent.Body + "!"));
        digest.ShouldNotBe(DispatchBaseNotificationPayload.Digest(AgentTaskReplyTo.None, destinationA, intent.Body));

        // The route moves twice; the projection still carries the captured payload verbatim.
        await db.AgentTasks.Where(t => t.Id == task.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.ParentSessionId, destinationB), ct);
        await using (var projector = CreateContext(schema))
            await new DispatchBaseWarningIntentService(projector, TimeProvider.System).MaterializeAsync(intent.Id, ct);
        await db.AgentTasks.Where(t => t.Id == task.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.ParentSessionId, (Guid?)null), ct);

        await using var verify = CreateContext(schema);
        var note = await verify.AgentTaskLandNotifications.AsNoTracking()
            .SingleAsync(n => n.Id == intent.NotificationId, ct);
        note.Body.ShouldBe(expectedHeader + "\n" + detail);
        note.ContentDigest.ShouldBe(digest);
        note.ParentSessionId.ShouldBe(destinationA);
        note.State.ShouldBe(LandNotificationState.Queued);
    }

    /// <summary>
    /// V-18 / G-54, G-55: the initial state comes from the route captured with the claim.
    /// <see cref="AgentTaskReplyTo.None"/> owes nothing; a missing destination stays owed rather
    /// than being quietly discharged.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    [Arguments(AgentTaskReplyTo.None, false, LandNotificationState.NotRequired)]
    [Arguments(AgentTaskReplyTo.Session, false, LandNotificationState.DestinationUnavailable)]
    [Arguments(AgentTaskReplyTo.Session, true, LandNotificationState.Queued)]
    public async Task C508_WarningStatesAndLease(
        AgentTaskReplyTo replyTo, bool hasDestination, LandNotificationState expected, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var destination = hasDestination ? Guid.NewGuid() : (Guid?)null;
        var task = await SeedBareTaskAsync(db, destination, replyTo);
        var at = DateTime.UtcNow;
        var dispatchEvent = await SeedDispatchEventAsync(db, task.Id, at);
        var ids = await new DispatchBaseWarningIntentService(db, TimeProvider.System).CaptureAsync(
            task, dispatchEvent,
            [new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, "state detail")], ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        await using (var projector = CreateContext(schema))
            await new DispatchBaseWarningIntentService(projector, TimeProvider.System).MaterializeAsync(ids[0], ct);

        await using var verify = CreateContext(schema);
        var intent = await verify.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == ids[0], ct);
        intent.InitialState.ShouldBe(expected);
        intent.MaterializedAt.ShouldNotBeNull();
        var note = await verify.AgentTaskLandNotifications.AsNoTracking()
            .SingleAsync(n => n.Id == intent.NotificationId, ct);
        note.State.ShouldBe(expected);
        note.QueueMessageId.ShouldBeNull("materialization never enqueues");
        note.ConfirmedAt.ShouldBeNull();
        // Only NotRequired is discharged; a missing destination remains an open obligation.
        (note.State == LandNotificationState.NotRequired)
            .ShouldBe(replyTo == AgentTaskReplyTo.None);
    }

    // ---- fixtures ---------------------------------------------------------------------------

    private static AgentTaskDispatchWarningIntent NewIntent(
        AgentTask task, AgentTaskEvent dispatchEvent, string key, string detail, DateTime at)
    {
        var payload = DispatchBaseNotificationPayload.Capture(
            Guid.NewGuid(), Guid.NewGuid(), task.Id, dispatchEvent.Id, task.Attempt, key,
            task.ReplyTo, task.ParentSessionId, detail, at);
        return new AgentTaskDispatchWarningIntent
        {
            Id = payload.WarningEventId,
            DispatchEventId = payload.DispatchEventId,
            TaskId = payload.TaskId,
            Attempt = payload.Attempt,
            WarningKey = payload.WarningKey,
            NotificationId = payload.NotificationId,
            ReplyTo = payload.ReplyTo,
            ParentSessionId = payload.ParentSessionId,
            Detail = payload.Detail,
            Body = payload.Body,
            ContentDigest = payload.ContentDigest,
            CreatedAt = payload.CreatedAt,
            InitialState = payload.InitialState,
            NextAttemptAt = payload.CreatedAt,
        };
    }

    private static async Task<AgentTask> SeedBareTaskAsync(
        AppDbContext db, Guid? parentSessionId, AgentTaskReplyTo replyTo = AgentTaskReplyTo.Session)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "C508 dispatch-base custody", Goal = "custody fixture",
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Dispatched,
            ReplyTo = replyTo, ParentSessionId = parentSessionId, CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return task;
    }

    private static async Task<AgentTaskEvent> SeedDispatchEventAsync(AppDbContext db, Guid taskId, DateTime at)
    {
        var dispatchEvent = new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = taskId, Type = AgentTaskEventType.Dispatched,
            Detail = "Dispatched to agent 'fixture'", At = at,
        };
        db.AgentTaskEvents.Add(dispatchEvent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return dispatchEvent;
    }

    private static async Task<AgentTask> SeedKeptSiblingAsync(
        AppDbContext db, ScratchGitRepo repo, Guid cardId, string commitMessage)
    {
        var id = Guid.NewGuid();
        var branch = $"feat/card-task-{DelegationReportFormatter.Short(id)}";
        await repo.GitAsync("checkout", "-b", branch);
        var file = $"plan-{DelegationReportFormatter.Short(id)}.md";
        await File.WriteAllTextAsync(Path.Combine(repo.Path, file), commitMessage + "\n");
        await repo.GitAsync("add", file);
        await repo.GitAsync("commit", "-m", commitMessage);
        await repo.GitAsync("checkout", "master");

        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "CARD-0508 plan", Goal = "Write the plan.",
            Role = AgentTaskRole.Plan, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Low,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = repo.Path, RepoPath = repo.Path,
            CardId = cardId, WorktreeBranch = branch, Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow.AddHours(-1),
            CompletedAt = DateTime.UtcNow.AddMinutes(-30),
        };
        db.AgentTasks.Add(task);
        return task;
    }

    private static async Task<AgentTask> SeedQueuedWorktreeTaskAsync(
        AppDbContext db, string repoPath, Guid cardId, Guid? parentSessionId)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "CARD-0508 execute", Goal = "Build the plan.",
            Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = repoPath, RepoPath = repoPath,
            CardId = cardId, ParentSessionId = parentSessionId,
            ReplyTo = parentSessionId is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session,
            Status = AgentTaskStatus.Queued, CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(task);
        await Task.CompletedTask;
        return task;
    }

    private static async Task SeedParentSessionAsync(AppDbContext db, Guid parentSessionId)
    {
        db.AgentSessions.Add(new AgentSession
        {
            Id = parentSessionId, DefinitionName = "c508-parent", AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running, Cwd = Path.GetTempPath(), Cols = 120, Rows = 30,
            CreatedAt = DateTime.UtcNow.AddHours(-1), StartedAt = DateTime.UtcNow.AddHours(-1),
            LastSeenAt = DateTime.UtcNow,
        });
        await Task.CompletedTask;
    }

    private static async Task<Card> SeedCardAsync(AppDbContext db, string identifier)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = $"c508-dbn-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/c508.git", CreatedAt = now, UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Name = $"CARD-0508 {Guid.NewGuid():N}",
            MaxConcurrentSessions = 1, CreatedAt = now, UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
            ColumnOrder = 0, CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id, Identifier = identifier,
            Title = $"{identifier} dispatch-base", Description = "CARD-0508.", CreatedAt = now, UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        await db.SaveChangesAsync();
        return card;
    }

    private static ServiceProvider CreateProvider(
        string connectionString, string worktreeBase, string defaultBranch, Func<Task>? onLeaseAcquired = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (onLeaseAcquired is not null)
        {
            services.AddSingleton<IRepositoryMutationLease>(sp =>
                new LeaseHook(new RepositoryMutationLease(sp.GetRequiredService<ILandingGit>()), onLeaseAcquired));
            services.TryAddSingleton<ILandingGit, LandingGit>();
        }

        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings { MaxConcurrentTasks = 512 }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = worktreeBase,
            WorktreeAddTimeoutSeconds = 180,
            DefaultBranch = defaultBranch,
        });
        services.AddSingleton<CompletionNoteFlushQueue>();
        services.AddSingleton<LandDeliveryBoundary>();
        services.AddScoped<AgentTaskLandNotificationService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider();
    }

    private sealed class LeaseHook(IRepositoryMutationLease inner, Func<Task> onAcquired)
        : IRepositoryMutationLease
    {
        private int _fired;

        public async Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
                await onAcquired();
            return await inner.TryAcquireAsync(repository, ct);
        }

        public bool Owns(RepositoryLease lease, string commonDirectory) => inner.Owns(lease, commonDirectory);
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}
