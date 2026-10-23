using System.Data.Common;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.Tests.Application;

public partial class DelegationDispatchRecoveryBoundaryTests
{
    /// <summary>
    /// CARD-1161 repair. The ordinary dead-session Failed note (a session reason other than
    /// the absent-launch hold) goes through the real queue, the way the Blocked note does.
    /// eligible: an idle caller gets one complete UserPrompt and that receipt closes the row.
    /// busy: the row stays queued, then one flush after TurnEnd delivers it once.
    /// crash: the Failed commit stands, the note insert throws, and the next dead-session
    /// sweep puts back exactly one note, which then closes with one receipt. CARD-1167.
    /// </summary>
    [Test]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("crash")]
    public async Task C1161_Failed_caller_note_has_one_complete_user_prompt(string mode)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        // The sweep clock jumps the dead-session grace. Delivery confirmation waits on
        // TimeProvider.Delay, and a frozen clock does not release that wait, so the caller
        // harness keeps the system clock. The note is written about two minutes before it.
        var sweepClock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-5));
        var parent = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ClaudeDialogObservation = true,
            AlwaysOn = true,
            PreserveDatabaseOnDispose = true,
            ConnectionString = schema.ConnectionString,
        });
        var decoy = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ClaudeDialogObservation = true,
            AlwaysOn = true,
            PreserveDatabaseOnDispose = true,
            ConnectionString = schema.ConnectionString,
        });
        try
        {
            var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
            {
                SessionReason = "process vanished",
                ExistingParentId = parent.SessionId,
                Brief = false,
            });
            var runner = new CountingRunner();
            var stopper = new RecordingSessionStopper();
            var fault = mode == "crash" ? new FailedNoteInsertFault(seeded.TaskId) : null;
            await using (var host = OpenSweep(
                schema.ConnectionString, runner, stopper, sweepClock, new DeadSessionFirstSeenState(), fault))
                await host.DueAsync();

            if (mode == "crash")
            {
                fault!.Fired.ShouldBeTrue(mode);
                await using (var mid = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
                {
                    var terminal = await mid.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
                    terminal.Status.ShouldBe(AgentTaskStatus.Failed, mode + "-terminal");
                    terminal.CompletedAt.ShouldNotBeNull(mode + "-terminal");
                    (await mid.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == seeded.TaskId))
                        .ShouldBe(0, mode + "-lost");
                }

                await using (var again = OpenSweep(
                    schema.ConnectionString, runner, stopper, sweepClock, new DeadSessionFirstSeenState()))
                    await again.SweepAsync();
                await using (var third = OpenSweep(
                    schema.ConnectionString, runner, stopper, sweepClock, new DeadSessionFirstSeenState()))
                    await third.SweepAsync();
            }

            SessionQueuedMessage note;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var failed = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
                failed.Status.ShouldBe(AgentTaskStatus.Failed, mode);
                failed.CompletedAt.ShouldNotBeNull(mode);
                failed.FailureReason.ShouldNotBeNull(mode);
                failed.FailureReason!.Contains("process vanished", StringComparison.Ordinal).ShouldBeTrue(mode);
                failed.FailureReason.StartsWith("dispatch_launch_absent", StringComparison.Ordinal).ShouldBeFalse(mode);
                (await db.AgentTaskEvents.CountAsync(e =>
                        e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Failed))
                    .ShouldBe(1, mode);
                (await db.AgentTaskEvents.CountAsync(e =>
                        e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Blocked))
                    .ShouldBe(0, mode);
                note = (await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.SourceTaskId == seeded.TaskId).ToListAsync()).ShouldHaveSingleItem(mode);
                note.AgentSessionId.ShouldBe(parent.SessionId, mode);
                note.ExecutionTaskId.ShouldBeNull(mode);
                note.Origin.ShouldBe(QueuedMessageOrigin.Delegation, mode);
                note.Body.ShouldContain("process vanished", customMessage: mode);
            }

            var floor = await PromptFloorAsync(schema.ConnectionString, parent.SessionId);
            if (mode == "busy")
            {
                await parent.MarkWorkingAsync();
                await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
                parent.Adapter.SubmittedBodies.ShouldBeEmpty(mode);
                (await PromptsAfterAsync(schema.ConnectionString, parent.SessionId, floor)).ShouldBeEmpty(mode);
                await parent.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
                await parent.Queue.OnTurnEndAsync(parent.SessionId, CancellationToken.None);
            }
            else
            {
                await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
            }

            await AssertOneReceiptAsync(schema.ConnectionString, note, parent.SessionId, floor, mode);
            parent.Adapter.SubmittedBodies.Count.ShouldBe(1, mode);
            await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
            await AssertOneReceiptAsync(schema.ConnectionString, note, parent.SessionId, floor, mode + "-once");
            parent.Adapter.SubmittedBodies.Count.ShouldBe(1, mode + "-once");
            (await PromptsAfterAsync(schema.ConnectionString, decoy.SessionId, 0)).ShouldBeEmpty(mode);
            decoy.Adapter.SubmittedBodies.ShouldBeEmpty(mode);
            (await CountNotesAsync(schema.ConnectionString, seeded.TaskId)).ShouldBe(1, mode + "-note");
            Quiet(runner, stopper, mode);
        }
        finally
        {
            await parent.DisposeAsync();
            await decoy.DisposeAsync();
        }
    }

    /// <summary>
    /// CARD-1182. A Failed dead-session row whose parent session row is already gone
    /// used to throw on every sweep, log a warning, and leave CompletionNoteQueuedAt
    /// null, so the next 5 s tick selected it again. The sweep stamps the note once
    /// and the following tick does not look the parent up.
    /// </summary>
    [Test]
    public async Task C1182_Gone_parent_stamps_the_lost_note_once()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        var missingParent = Guid.NewGuid();
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
        {
            Status = AgentTaskStatus.Failed,
            Brief = false,
            ExistingParentId = missingParent,
        });
        var decoyParent = Guid.NewGuid();
        var decoy = await SeedAsync(schema.ConnectionString, new AbsentShape
        {
            Status = AgentTaskStatus.Failed,
            Brief = false,
            ExistingParentId = decoyParent,
        });
        var reason = AgentTaskLiveness.DeadSessionFailurePrefix
            + " its session row is gone (no failure reason recorded). No report is coming.";
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
            task.FailureReason = reason;
            task.CompletionNoteQueuedAt = null;
            var other = await db.AgentTasks.SingleAsync(t => t.Id == decoy.TaskId);
            other.FailureReason = "ordinary dispatch failure";
            other.CompletionNoteQueuedAt = null;
            (await db.AgentSessions.AnyAsync(s => s.Id == missingParent || s.Id == decoyParent))
                .ShouldBeFalse();
            await db.SaveChangesAsync();
        }

        var runner = new CountingRunner();
        var stopper = new RecordingSessionStopper();
        var seen = new DeadSessionFirstSeenState();
        var lookups = new ParentSessionLookupCounter(missingParent);
        await using (var host = OpenSweep(schema.ConnectionString, runner, stopper, clock, seen, lookups))
            await host.SweepAsync();
        var afterFirst = lookups.Count;
        DateTime? firstStamp;
        await using (var mid = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var task = await mid.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == seeded.TaskId);
            firstStamp = task.CompletionNoteQueuedAt;
            (await mid.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == seeded.TaskId)).ShouldBe(0);
        }

        clock.Advance(TimeSpan.FromSeconds(5));
        await using (var again = OpenSweep(schema.ConnectionString, runner, stopper, clock, seen, lookups))
            await again.SweepAsync();

        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == seeded.TaskId);
            lookups.Count.ShouldBe(afterFirst,
                $"stamp={task.CompletionNoteQueuedAt:o} first={firstStamp:o} digest={task.CompletionNoteDigest}");
            task.CompletionNoteQueuedAt.ShouldNotBeNull();
            task.CompletionNoteQueuedAt.ShouldBe(firstStamp);
            task.CompletionNoteDigest.ShouldBe(DelegationNoteDigest.Compute(reason));
            task.Status.ShouldBe(AgentTaskStatus.Failed);
            (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == seeded.TaskId)).ShouldBe(0);
            var other = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == decoy.TaskId);
            other.CompletionNoteQueuedAt.ShouldBeNull();
            other.FailureReason.ShouldBe("ordinary dispatch failure");
        }

        Quiet(runner, stopper, "gone-parent");
    }

    /// <summary>
    /// CARD-1186 V-3. Production fails an expired obligation, the note insert throws, and a
    /// later sweep recovers one caller note that still names the proven git recipe. Expected
    /// sentences below are literals; they do not call the production formatter.
    /// </summary>
    [Test]
    [Arguments("one")]
    [Arguments("multiple")]
    [Arguments("busy")]
    [Arguments("historical-decoys")]
    public async Task C1186_Recovered_failure_preserves_obligation_guidance(string mode)
    {
        const string DigestOne = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        const string DigestOlder = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";
        const string DigestNewer = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";
        const string DigestOld = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string DigestHistorical = "4444444444444444444444444444444444444444444444444444444444444444";
        const string DigestForeign = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var oneId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var olderId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var newerId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var historicalId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        var oldId = Guid.Parse("55555555-5555-4555-8555-555555555555");
        var foreignId = Guid.Parse("66666666-6666-4666-8666-666666666666");
        var git = mode switch
        {
            "multiple" => "git=commit-recovery-abandoned:22222222,33333333",
            "historical-decoys" => "git=commit-recovery-abandoned:44444444",
            _ => "git=commit-recovery-abandoned:11111111",
        };

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sweepClock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-5));
        var newerAt = Pg(sweepClock.GetUtcNow().UtcDateTime.AddMinutes(-800));
        var olderAt = Pg(sweepClock.GetUtcNow().UtcDateTime.AddMinutes(-900));
        var ancientAt = Pg(sweepClock.GetUtcNow().UtcDateTime.AddMinutes(-2000));
        var parent = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ClaudeDialogObservation = true,
            AlwaysOn = true,
            PreserveDatabaseOnDispose = true,
            ConnectionString = schema.ConnectionString,
        });
        var decoy = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ClaudeDialogObservation = true,
            AlwaysOn = true,
            PreserveDatabaseOnDispose = true,
            ConnectionString = schema.ConnectionString,
        });
        try
        {
            var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
            {
                SessionReason = "process vanished",
                ExistingParentId = parent.SessionId,
                Brief = false,
            });
            var real = new List<SeededObligation>();
            if (mode == "multiple")
            {
                real.Add(await SeedObligationStartAsync(schema.ConnectionString, seeded.TaskId, olderId, DigestOlder, olderAt));
                real.Add(await SeedObligationStartAsync(schema.ConnectionString, seeded.TaskId, newerId, DigestNewer, newerAt));
            }
            else
            {
                var id = mode == "historical-decoys" ? historicalId : oneId;
                var settlement = mode == "historical-decoys" ? DigestHistorical : DigestOne;
                real.Add(await SeedObligationStartAsync(schema.ConnectionString, seeded.TaskId, id, settlement, newerAt));
            }

            string? oldRecipe = null;
            string? foreignRecipe = null;
            if (mode == "historical-decoys")
            {
                var oldDetail = $"{oldId:D} dead-session reconciler: earlier failure that is not this saved reason";
                await SeedObligationStartAsync(schema.ConnectionString, seeded.TaskId, oldId, DigestOld, ancientAt);
                await SeedAbandonmentAsync(schema.ConnectionString, seeded.TaskId, oldDetail, ancientAt.AddMinutes(1));
                oldRecipe = RecoveryCommand(seeded.TaskId, DigestOld);

                var foreign = await SeedAsync(schema.ConnectionString, new AbsentShape
                {
                    Status = AgentTaskStatus.Succeeded,
                    SessionReason = "process vanished",
                    ExistingParentId = parent.SessionId,
                    Brief = false,
                });
                await SeedObligationStartAsync(schema.ConnectionString, foreign.TaskId, foreignId, DigestForeign, ancientAt);
                await SeedAbandonmentAsync(
                    schema.ConnectionString, foreign.TaskId,
                    $"{foreignId:D} dead-session reconciler: some other task's saved failure",
                    ancientAt.AddMinutes(1));
                foreignRecipe = RecoveryCommand(foreign.TaskId, DigestForeign);
            }

            var runner = new CountingRunner();
            var stopper = new RecordingSessionStopper();
            var fault = new FailedNoteInsertFault(seeded.TaskId);
            await using (var host = OpenSweep(
                schema.ConnectionString, runner, stopper, sweepClock, new DeadSessionFirstSeenState(), fault))
                await host.DueAsync();

            fault.Fired.ShouldBeTrue(mode);
            string reason;
            int failedEvents;
            int abandonments;
            await using (var mid = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var terminal = await mid.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == seeded.TaskId);
                terminal.Status.ShouldBe(AgentTaskStatus.Failed, mode);
                terminal.CompletionNoteQueuedAt.ShouldBeNull(mode);
                terminal.DispatchedAt.ShouldNotBeNull(mode);
                (await mid.AgentTaskLandNotifications.CountAsync(n => n.TaskId == seeded.TaskId
                    && n.Kind == LandNotificationKind.DeliveryFailure)).ShouldBe(0, "ordinary post-dispatch failure keeps its existing path");
                terminal.FailureReason.ShouldNotBeNull(mode);
                reason = terminal.FailureReason!;
                reason.Contains("process vanished", StringComparison.Ordinal).ShouldBeTrue(mode);
                var events = await mid.AgentTaskEvents.AsNoTracking()
                    .Where(e => e.AgentTaskId == seeded.TaskId).ToListAsync();
                CommitRecoveryObligations.Unresolved(events, seeded.TaskId).ShouldBeEmpty(mode + "-unresolved");
                failedEvents = events.Count(e => e.Type == AgentTaskEventType.Failed);
                abandonments = events.Count(e => e.Type == AgentTaskEventType.CommitRecoveryAbandoned);
                failedEvents.ShouldBe(1, mode + "-failed-event");
                foreach (var obligation in real)
                {
                    events.Count(e => e.Type == AgentTaskEventType.CommitRecoveryAbandoned
                            && e.Detail.StartsWith($"{obligation.Id:D} ", StringComparison.Ordinal))
                        .ShouldBe(1, mode + "-abandon");
                    reason.Contains($"{obligation.Id:D} (settlement {obligation.Settlement})", StringComparison.Ordinal)
                        .ShouldBeTrue(mode + "-reason");
                }

                if (mode == "historical-decoys")
                {
                    events.Count(e => e.Type == AgentTaskEventType.CommitRecoveryAbandoned
                            && e.Detail.StartsWith($"{oldId:D} ", StringComparison.Ordinal))
                        .ShouldBe(1, mode + "-old-abandon");
                    reason.Contains(DigestOld, StringComparison.Ordinal).ShouldBeFalse(mode + "-old-reason");
                }

                (await mid.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == seeded.TaskId))
                    .ShouldBe(0, mode + "-lost");
            }

            await using (var again = OpenSweep(
                schema.ConnectionString, runner, stopper, sweepClock, new DeadSessionFirstSeenState()))
                await again.SweepAsync();
            await using (var third = OpenSweep(
                schema.ConnectionString, runner, stopper, sweepClock, new DeadSessionFirstSeenState()))
                await third.SweepAsync();

            SessionQueuedMessage note;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var failed = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == seeded.TaskId);
                failed.FailureReason.ShouldBe(reason, mode + "-reason-unchanged");
                failed.CompletionNoteDigest.ShouldBe(DelegationNoteDigest.Compute(reason), mode);
                var events = await db.AgentTaskEvents.AsNoTracking()
                    .Where(e => e.AgentTaskId == seeded.TaskId).ToListAsync();
                events.Count(e => e.Type == AgentTaskEventType.Failed).ShouldBe(failedEvents, mode + "-failed-once");
                events.Count(e => e.Type == AgentTaskEventType.CommitRecoveryAbandoned)
                    .ShouldBe(abandonments, mode + "-abandon-once");
                note = (await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.SourceTaskId == seeded.TaskId).ToListAsync()).ShouldHaveSingleItem(mode);
                note.AgentSessionId.ShouldBe(parent.SessionId, mode);
                note.ConversationKey.ShouldBe($"task:{seeded.TaskId:N}", mode);
                note.Origin.ShouldBe(QueuedMessageOrigin.Delegation, mode);
                note.ContentDigest.ShouldBe(DelegationNoteDigest.Compute(reason), mode);
                ContainsLiteral(note.Body, git, mode + "-body-git");
                ContainsLiteral(note.NoteHeader, git, mode + "-header-git");
                foreach (var obligation in real)
                {
                    var command = RecoveryCommand(seeded.TaskId, obligation.Settlement);
                    var warning = "Commit recovery obligation " + obligation.Id.ToString("D")
                        + " for task " + seeded.TaskId.ToString("D")
                        + " (settlement " + obligation.Settlement + ") was not resolved. "
                        + "The gated commit, if it exists, is local and unpushed; find it with: "
                        + command + ". Nothing was pushed.";
                    ContainsLiteral(note.Body, obligation.Id.ToString("D"), mode + "-body-id");
                    ContainsLiteral(note.Body, obligation.Settlement, mode + "-body-settlement");
                    ContainsLiteral(note.Body, command, mode + "-body-command");
                    ContainsLiteral(note.Body, "Nothing was pushed.", mode + "-body-unpushed");
                    ContainsLiteral(note.NoteHeader, warning, mode + "-header-warning");
                    ContainsLiteral(note.Body, warning, mode + "-body-warning");
                }

                if (oldRecipe is not null)
                {
                    LacksLiteral(note.Body, oldRecipe, mode + "-old-recipe");
                    LacksLiteral(note.NoteHeader, oldRecipe, mode + "-old-header");
                    LacksLiteral(note.Body, DigestOld, mode + "-old-digest");
                }

                if (foreignRecipe is not null)
                {
                    LacksLiteral(note.Body, foreignRecipe, mode + "-foreign-recipe");
                    LacksLiteral(note.NoteHeader, foreignRecipe, mode + "-foreign-header");
                    LacksLiteral(note.Body, DigestForeign, mode + "-foreign-digest");
                }
            }

            var floor = await PromptFloorAsync(schema.ConnectionString, parent.SessionId);
            if (mode == "busy")
            {
                await parent.MarkWorkingAsync();
                await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
                parent.Adapter.SubmittedBodies.ShouldBeEmpty(mode);
                (await PromptsAfterAsync(schema.ConnectionString, parent.SessionId, floor)).ShouldBeEmpty(mode);
                await parent.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
                await parent.Queue.OnTurnEndAsync(parent.SessionId, CancellationToken.None);
            }
            else
            {
                await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
            }

            // Two obligation paragraphs cross the inbox 1024-byte single write. The typed
            // prompt is then the spill pointer; the file is the note asserted above.
            var typed = await TypedReceiptBodyAsync(schema.ConnectionString, note, mode);
            await AssertOneReceiptAsync(schema.ConnectionString, typed, parent.SessionId, floor, mode);
            parent.Adapter.SubmittedBodies.Count.ShouldBe(1, mode);
            await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
            await AssertOneReceiptAsync(schema.ConnectionString, typed, parent.SessionId, floor, mode + "-once");
            parent.Adapter.SubmittedBodies.Count.ShouldBe(1, mode + "-once");
            (await PromptsAfterAsync(schema.ConnectionString, decoy.SessionId, 0)).ShouldBeEmpty(mode);
            decoy.Adapter.SubmittedBodies.ShouldBeEmpty(mode);
            (await CountNotesAsync(schema.ConnectionString, seeded.TaskId)).ShouldBe(1, mode + "-note");
            if (mode == "one")
            {
                var immediate = await SeedAsync(schema.ConnectionString, new AbsentShape
                {
                    SessionReason = "process vanished",
                    ExistingParentId = parent.SessionId,
                    Brief = false,
                });
                var immediateObligation = Guid.NewGuid();
                await SeedObligationStartAsync(schema.ConnectionString, immediate.TaskId,
                    immediateObligation, DigestOne, newerAt);
                // DueAsync advances three minutes. This independent scenario needs its own
                // clock so the queued note remains older than the caller's real-time flush.
                var immediateClock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-5));
                await using (var host = OpenSweep(
                    schema.ConnectionString, runner, stopper, immediateClock, new DeadSessionFirstSeenState()))
                    await host.DueAsync();
                await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                var direct = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceTaskId == immediate.TaskId);
                ContainsLiteral(direct.Body, "git=commit-recovery-abandoned:" + immediateObligation.ToString("N")[..8], "immediate-git");
                ContainsLiteral(direct.Body, immediateObligation.ToString("D"), "immediate-obligation");
                ContainsLiteral(direct.Body, RecoveryCommand(immediate.TaskId, DigestOne), "immediate-command");
                ContainsLiteral(direct.Body, "Nothing was pushed.", "immediate-warning");
                var immediateFloor = await PromptFloorAsync(schema.ConnectionString, parent.SessionId);
                await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
                var immediateTyped = await TypedReceiptBodyAsync(schema.ConnectionString, direct, "immediate");
                await AssertOneReceiptAsync(schema.ConnectionString, immediateTyped, parent.SessionId, immediateFloor, "immediate");
                (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == immediate.TaskId
                    && n.Kind == LandNotificationKind.DeliveryFailure)).ShouldBe(0);
            }
            Quiet(runner, stopper, mode);
        }
        finally
        {
            await parent.DisposeAsync();
            await decoy.DisposeAsync();
        }
    }

    /// <summary>
    /// The body the queue actually typed. Under the inbox single-write chunk that is the
    /// queued note. Over it, the row becomes the spill pointer and the file keeps the note.
    /// </summary>
    private static async Task<SessionQueuedMessage> TypedReceiptBodyAsync(
        string connection, SessionQueuedMessage queued, string label)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var delivered = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id);
        if (string.Equals(delivered.Body, queued.Body, StringComparison.Ordinal))
            return delivered;

        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == queued.AgentSessionId);
        var relative = TypedBodySpill.InboxRelativePath(queued.Id.ToString("D"));
        (delivered.Body ?? string.Empty).Contains(relative, StringComparison.Ordinal)
            .ShouldBeTrue(label + "-pointer");
        var file = TypedBodySpill.InboxAbsolutePath(session.Cwd, queued.Id.ToString("D"));
        File.ReadAllText(file).ShouldBe(queued.Body, label + "-spill-body");
        return delivered;
    }

    private readonly record struct SeededObligation(Guid Id, string Settlement, DateTime StartedAt);

    private static async Task<SeededObligation> SeedObligationStartAsync(
        string connection, Guid taskId, Guid id, string settlement, DateTime at)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = id,
            AgentTaskId = taskId,
            Type = AgentTaskEventType.CommitRecoveryStarted,
            Detail = settlement,
            At = at,
        });
        await db.SaveChangesAsync();
        return new SeededObligation(id, settlement, at);
    }

    private static async Task SeedAbandonmentAsync(string connection, Guid taskId, string detail, DateTime at)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = taskId,
            Type = AgentTaskEventType.CommitRecoveryAbandoned,
            Detail = detail,
            At = at,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>The recovery command the note must carry. Not <see cref="CommitRecoveryObligations.Recipe"/>.</summary>
    private static string RecoveryCommand(Guid taskId, string settlement) =>
        "git log --all --reflog --fixed-strings --all-match --grep=" + taskId.ToString("D")
        + " --grep=" + settlement + " --format=%H";

    private static void ContainsLiteral(string? text, string literal, string label) =>
        (text ?? string.Empty).IndexOf(literal, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, label);

    private static void LacksLiteral(string? text, string literal, string label) =>
        (text ?? string.Empty).IndexOf(literal, StringComparison.Ordinal).ShouldBe(-1, label);

    /// <summary>Counts commands whose parameters name one session id.</summary>
    private sealed class ParentSessionLookupCounter(Guid sessionId) : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Mentions(command))
                Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private bool Mentions(DbCommand command)
        {
            foreach (DbParameter parameter in command.Parameters)
            {
                if (parameter.Value is Guid id && id == sessionId)
                    return true;
                if (parameter.Value is string text
                    && Guid.TryParse(text, out var parsed)
                    && parsed == sessionId)
                    return true;
            }

            return false;
        }
    }

    private static async Task<int> CountNotesAsync(string connection, Guid taskId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        return await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == taskId);
    }

    /// <summary>
    /// Throws on the first caller-note insert for one task. The Failed status is already
    /// committed on the sweep context; this fault is the later queue insert. CARD-1167.
    /// </summary>
    private sealed class FailedNoteInsertFault(Guid taskId) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && eventData.Context is AppDbContext db)
            {
                var note = db.ChangeTracker.Entries<SessionQueuedMessage>().FirstOrDefault(e =>
                    e.State == EntityState.Added && e.Entity.SourceTaskId == taskId);
                if (note is not null)
                {
                    Fired = true;
                    throw new IOException("injected failure-note enqueue fault after the terminal write");
                }
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
