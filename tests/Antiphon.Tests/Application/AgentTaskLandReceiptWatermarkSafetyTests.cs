using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using Mode = Antiphon.Tests.TestHelpers.LandReceiptScanHarness.ScriptedTranscriptRunner.Mode;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1121 S3: the negative receipt-scan cache under races, faults and failed saves, its statement
/// and row budgets, and recovery of a busy or crashed destination through the real queue and the
/// runner pull. A proof is published only from a stable, exhausted, committed miss; every late or
/// recovered prompt still confirms at its own stored sequence.
/// </summary>
[Category("Integration")]
public sealed class AgentTaskLandReceiptWatermarkSafetyTests
{
    private const long Floor = LandReceiptScanHarness.Floor;

    private sealed record Pass(int Commands, int ReceiptSelects, int ReceiptRows, IReadOnlyList<long?> Floors,
        int TranscriptStatements, int Pulls, IReadOnlyList<string> Sql, string Roster);

    private static async Task<Pass> PassAsync(LandReceiptScanHarness h, Guid noteId, CancellationToken ct = default,
        AgentTaskLandNotificationService? service = null)
    {
        h.ResetCounters();
        await (service ?? h.Service).ReconcileAsync(noteId, ct);
        return Snapshot(h);
    }

    private static Pass Snapshot(LandReceiptScanHarness h)
    {
        var scans = h.Receipts.Scans;
        var transcript = h.Commands.Commands.Count(c => c.Contains("\"TranscriptEntries\"", StringComparison.Ordinal)
            && !c.Contains("session-state.seed", StringComparison.Ordinal));
        return new(h.Commands.Total, scans.Count, scans.Sum(s => s.Rows), scans.Select(s => s.Floor).ToList(),
            transcript, h.Runner.TotalPulls, scans.Select(s => s.Sql).ToList(), h.Commands.Roster());
    }

    private static async Task<IReadOnlyList<Pass>> PassesAsync(LandReceiptScanHarness h, Guid noteId, int count)
    {
        var passes = new List<Pass>();
        for (var i = 0; i < count; i++) passes.Add(await PassAsync(h, noteId));
        return passes;
    }

    /// <summary>The S2 quiet cohort: committed transcript before the enqueue pass, one typed attempt, destination Stopped.</summary>
    private static async Task<(AgentTaskLandNotification Note, SessionQueuedMessage Row, IReadOnlyList<SessionRunnerTranscriptEvent> Events)>
        ArrangeAsync(LandReceiptScanHarness h, int candidates = 48, Func<string, Func<long, (string, string)>?>? candidate = null,
            Action<SessionQueuedMessage>? row = null, SessionStatus destination = SessionStatus.Stopped, string? detail = null)
    {
        IReadOnlyList<SessionRunnerTranscriptEvent> events = [];
        var note = await h.SeedLinkedNoteAsync(detail, async seeded =>
            events = await h.SeedTranscriptAsync(h.SessionId, candidates: candidates, candidate: candidate?.Invoke(seeded.Body)));
        var sent = await h.MarkSentAsync(note, row);
        await h.SetStatusAsync(h.SessionId, destination);
        await h.AssertCoherentAsync(h.SessionId);
        return (note, sent, events);
    }

    private static Func<string, Func<long, (string, string)>?> BodyAt(long sequence, string kind = TranscriptKinds.UserPrompt) =>
        body => seq => seq == sequence ? (kind, body) : (TranscriptKinds.UserPrompt, LandReceiptScanHarness.Filler(seq));

    private static async Task ShouldBeConfirmedAtAsync(LandReceiptScanHarness h, Guid noteId, long expected, long floor = Floor, string? receiptText = null)
    {
        var saved = await h.NoteAsync(noteId);
        var oracle = await h.InMemoryFirstReceiptAsync(saved.ParentSessionId!.Value, floor, saved.IsLegacy, saved.Kind, receiptText ?? saved.Body);
        oracle.ShouldBe(expected, "the in-memory CARD-0641 rule over the committed rows");
        saved.State.ShouldBe(LandNotificationState.Confirmed);
        saved.ConfirmedAt.ShouldNotBeNull();
        saved.ConfirmingPromptSequence.ShouldBe(expected);
    }

    private static async Task ShouldStayOpenAsync(LandReceiptScanHarness h, Guid noteId)
    {
        var saved = await h.NoteAsync(noteId);
        saved.State.ShouldBe(LandNotificationState.AwaitingReceipt);
        saved.ConfirmedAt.ShouldBeNull();
        saved.ConfirmingPromptSequence.ShouldBeNull();
    }

    private static AgentTaskLandNotificationService FreshService(LandReceiptScanHarness h, AppDbContext db,
        LandDeliveryBoundary? boundary = null) =>
        new(db, h.Bridge.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System, boundary, scanCache: h.Cache);

    private static AppDbContext CountedContext(LandReceiptScanHarness h, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] extra) =>
        new(new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(h.ConnectionString))
            .AddInterceptors([h.Commands, h.Receipts, .. extra]).Options);

    // V-7 -------------------------------------------------------------------------------------------

    [Test]
    [Timeout(180_000)]
    [Arguments("commit-between-exhaustion-and-after-stamp")]
    [Arguments("writer-holds-gate-during-reuse-observation")]
    [Arguments("reader-throws-after-one-row")]
    [Arguments("reader-cancelled-after-one-row")]
    [Arguments("commit-before-before-stamp")]
    [Arguments("unique-violation-reload-after-cached-miss")]
    public async Task C1121_RacingCommitCannotPublishOrReuseStaleMiss(string cut, CancellationToken ct)
    {
        var boundary = new LandReceiptScanHarness.CutBoundary();
        await using var h = await LandReceiptScanHarness.CreateAsync(new() { Boundary = boundary });
        var (note, _, _) = await ArrangeAsync(h);
        long? late = null;

        switch (cut)
        {
            case "commit-between-exhaustion-and-after-stamp":
            {
                // The reader ran to exhaustion; a matching prompt commits before the after-stamp read.
                var held = boundary.Hold("receipt-scan-exhausted");
                var pass = PassAsync(h, note.Id, ct);
                await held.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
                try { late = (await h.IngestAsync(h.SessionId, (TranscriptKinds.UserPrompt, note.Body))).Single(); }
                finally { held.Release(); }
                var first = await pass;
                first.ReceiptSelects.ShouldBe(1);
                first.ReceiptRows.ShouldBe(48, "the scan ran to exhaustion before the commit");
                await ShouldStayOpenAsync(h, note.Id);
                var metrics = h.Cache!.GetMetrics();
                metrics.Proofs.ShouldBe(0, "a miss whose committed state moved during the scan certifies nothing");
                metrics.Publishes.ShouldBe(0);
                metrics.Refusals.GetValueOrDefault("certificate:BeforeAfter").ShouldBe(1);
                break;
            }
            case "writer-holds-gate-during-reuse-observation":
            {
                (await PassesAsync(h, note.Id, 2)).Select(p => p.ReceiptSelects).ShouldBe([1, 0], "a cached miss before the race");
                // An ingest of the matching prompt stops inside its commit while holding the session gate.
                h.IngestGate.Arm();
                var ingest = h.IngestAsync(h.SessionId, (TranscriptKinds.UserPrompt, note.Body));
                Task<Pass>? pass = null;
                try
                {
                    await h.IngestGate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
                    pass = PassAsync(h, note.Id, ct);
                    // The reuse observation is a serialized store read: it cannot finish while the
                    // writer holds the gate, so it can never certify the pre-commit revision.
                    (await Task.WhenAny(pass, Task.Delay(TimeSpan.FromSeconds(2), ct))).ShouldNotBe(pass,
                        "the reconcile pass completed while a writer held the session gate");
                }
                finally { h.IngestGate.Release.TrySetResult(); }
                late = (await ingest).Single();
                var raced = await pass!;
                raced.ReceiptSelects.ShouldBe(1, "the pass observed the new revision and scanned in full");
                await ShouldBeConfirmedAtAsync(h, note.Id, late.Value);
                h.Cache!.GetMetrics().Hits.ShouldBe(1, "only the pre-race pass reused");
                h.Cache.GetMetrics().Proofs.ShouldBe(0);
                break;
            }
            case "reader-throws-after-one-row":
            {
                h.Receipts.AfterRow = rows => { if (rows == 1) throw new InvalidOperationException("planned receipt reader fault"); };
                var faulted = await PassAsync(h, note.Id, ct);
                h.Receipts.AfterRow = null;
                faulted.ReceiptSelects.ShouldBe(1);
                faulted.ReceiptRows.ShouldBe(1, "the reader faulted on its first row");
                var saved = await h.NoteAsync(note.Id);
                saved.LastErrorCode.ShouldBe("notification_reconcile_failed:InvalidOperationException");
                saved.EnqueueAttempts.ShouldBe(note.EnqueueAttempts + 1);
                saved.State.ShouldBe(LandNotificationState.AwaitingReceipt);
                h.Cache!.GetMetrics().Publishes.ShouldBe(0, "a partial enumeration is never a negative scan");
                h.Cache.GetMetrics().Proofs.ShouldBe(0);
                var next = await PassAsync(h, note.Id, ct);
                next.ReceiptSelects.ShouldBe(1, "the next pass scans in full");
                next.ReceiptRows.ShouldBe(48);
                late = (await h.IngestAsync(h.SessionId, (TranscriptKinds.UserPrompt, note.Body))).Single();
                break;
            }
            case "reader-cancelled-after-one-row":
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                h.Receipts.AfterRow = rows =>
                {
                    if (rows != 1) return;
                    cts.Cancel();
                    throw new OperationCanceledException(cts.Token);
                };
                var before = await h.NoteAsync(note.Id);
                await Should.ThrowAsync<OperationCanceledException>(() => PassAsync(h, note.Id, cts.Token));
                h.Receipts.AfterRow = null;
                var saved = await h.NoteAsync(note.Id);
                (saved.State, saved.LastErrorCode, saved.EnqueueAttempts, saved.ConcurrencyToken, saved.ConfirmedAt)
                    .ShouldBe((before.State, before.LastErrorCode, before.EnqueueAttempts, before.ConcurrencyToken, before.ConfirmedAt),
                        "cancellation remains cancellation: nothing is recorded");
                h.Cache!.GetMetrics().Publishes.ShouldBe(0);
                h.Cache.GetMetrics().Proofs.ShouldBe(0);
                var next = await PassAsync(h, note.Id, ct);
                next.ReceiptSelects.ShouldBe(1);
                next.ReceiptRows.ShouldBe(48);
                late = (await h.IngestAsync(h.SessionId, (TranscriptKinds.UserPrompt, note.Body))).Single();
                break;
            }
            case "unique-violation-reload-after-cached-miss":
            {
                (await PassesAsync(h, note.Id, 2)).Select(p => p.ReceiptSelects).ShouldBe([1, 0], "a cached miss before the race");
                h.Cache!.GetMetrics().Publishes.ShouldBe(1);
                // The runner holds one new non-matching row; a competing writer commits the same row
                // first, so this pass's persist meets a real 23505, recovers it and reseeds (NeedsReload).
                h.Runner.Transcript = Mode.Entries;
                h.Runner.NextEntries = session => [h.Event(session, 0, TranscriptKinds.AssistantText, "a competing commit",
                    uuid: LandReceiptScanHarness.CompetingTranscriptInsert.Prefix + Guid.NewGuid().ToString("N"))];
                h.Competitor.Arm();
                var reloaded = await PassAsync(h, note.Id, ct);
                h.Runner.Transcript = Mode.NotFound;
                h.Competitor.Injected.ShouldBe(1, "the runtime's save met the competing commit");
                h.Runtime.TryGetTranscriptPersistFailure(h.SessionId, out _).ShouldBeFalse("the reseed succeeded; no failure is retained");
                reloaded.ReceiptSelects.ShouldBe(1, "a NeedsReload catch-up is unknown: the full scan runs");
                reloaded.ReceiptRows.ShouldBe(48);
                await ShouldStayOpenAsync(h, note.Id);
                var metrics = h.Cache.GetMetrics();
                metrics.Refusals.GetValueOrDefault("state:Observation:persist_needs_reload").ShouldBe(1);
                metrics.Publishes.ShouldBe(1, "a pass that needed a reload certifies nothing (A-2/W-5)");
                metrics.Refusals.GetValueOrDefault("certificate:CatchUp").ShouldBe(1);
                // The next pass rescans against the reseeded revision; only then is the miss certified.
                (await PassesAsync(h, note.Id, 2)).Select(p => p.ReceiptSelects).ShouldBe([1, 0], "the next pass rescans, then reuses");
                h.Cache.GetMetrics().Publishes.ShouldBe(2);
                late = (await h.IngestAsync(h.SessionId, (TranscriptKinds.UserPrompt, note.Body))).Single();
                break;
            }
            case "commit-before-before-stamp":
            {
                var held = boundary.Hold("receipt-scan-before-stamp");
                var pass = PassAsync(h, note.Id, ct);
                await held.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
                try { late = (await h.IngestAsync(h.SessionId, (TranscriptKinds.UserPrompt, note.Body))).Single(); }
                finally { held.Release(); }
                (await pass).ReceiptSelects.ShouldBe(1);
                // The scan itself saw the commit that landed before its before-stamp.
                await ShouldBeConfirmedAtAsync(h, note.Id, late.Value);
                h.Cache!.GetMetrics().Publishes.ShouldBe(0);
                h.Cache.GetMetrics().Proofs.ShouldBe(0);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(cut), cut, null);
        }

        if ((await h.NoteAsync(note.Id)).State != LandNotificationState.Confirmed)
        {
            var recovery = await PassAsync(h, note.Id, ct);
            recovery.ReceiptSelects.ShouldBe(1, "a late receipt is found by a full scan, never hidden by a proof");
        }
        late.ShouldNotBeNull().ShouldBeGreaterThan(Floor);
        await ShouldBeConfirmedAtAsync(h, note.Id, late.Value);
        h.Cache!.GetMetrics().Proofs.ShouldBe(0, "confirmation leaves no proof");
        h.Bridge.Adapter.Inputs.ShouldBeEmpty("receipt reconciliation must never type");
        h.Runner.KillCalls.ShouldBe(0);
    }

    // V-8 -------------------------------------------------------------------------------------------

    [Test]
    [Timeout(180_000)]
    [Arguments("receipt-before-save-fails")]
    [Arguments("final-save-fails-after-match")]
    [Arguments("profiled-pointer-hash-mismatch-then-repair")]
    [Arguments("negative-save-fails")]
    public async Task C1121_MatchedButUncommittedReceiptIsNeverCached(string cut, CancellationToken ct)
    {
        const long Match = Floor + 5;
        var boundary = new LandReceiptScanHarness.CutBoundary();
        await using var h = await LandReceiptScanHarness.CreateAsync(new() { Boundary = boundary });
        var spill = Path.Combine(h.Bridge.TempRoot, "c1121-spilled-report.md");
        var wire = $"C1121 completion pointer: the full report is at {spill}; read it before replying.";
        var (note, row, _) = cut switch
        {
            "negative-save-fails" => await ArrangeAsync(h),
            "profiled-pointer-hash-mismatch-then-repair" => await ArrangeAsync(h, candidate: _ => BodyAt(Match)(wire)),
            _ => await ArrangeAsync(h, candidate: BodyAt(Match)),
        };

        switch (cut)
        {
            case "receipt-before-save-fails":
            {
                boundary.Fail("receipt-before-save", new IOException("planned receipt save failure"));
                var failed = await PassAsync(h, note.Id, ct);
                failed.ReceiptSelects.ShouldBe(1);
                boundary.ReachedNames.ShouldContain("receipt-before-save", "the pass matched before failing");
                (await h.NoteAsync(note.Id)).LastErrorCode.ShouldBe("notification_reconcile_failed:IOException");
                break;
            }
            case "final-save-fails-after-match":
            {
                await using var db = CountedContext(h, new ThrowOnceSaveInterceptor());
                var failed = await PassAsync(h, note.Id, ct, FreshService(h, db, boundary));
                failed.ReceiptSelects.ShouldBe(1);
                boundary.ReachedNames.ShouldContain("receipt-before-save");
                (await h.NoteAsync(note.Id)).LastErrorCode.ShouldBe("notification_reconcile_failed:InvalidOperationException");
                break;
            }
            case "profiled-pointer-hash-mismatch-then-repair":
            {
                const string report = "C1121 spilled completion report body";
                await File.WriteAllTextAsync(spill, "not the spilled bytes", ct);
                await h.UpdateNoteAsync(note.Id, n =>
                {
                    n.Kind = LandNotificationKind.TaskCompletion;
                    n.CompletionSnapshotJson = "{}";
                    n.CompletionDeliveryJson = TaskCompletionNotification.SerializeDelivery(new TaskCompletionNotification.Delivery(
                        TaskCompletionNotification.SnapshotVersion, "spilled", n.Body, wire, TaskCompletionNotification.Sha256(wire),
                        [row.Id], spill, TaskCompletionNotification.Sha256(report), DateTime.UtcNow));
                });
                var mismatched = await PassesAsync(h, note.Id, 2);
                mismatched.Select(p => p.ReceiptSelects).ShouldBe([1, 1], "an excluded profiled note scans every pass");
                var saved = await h.NoteAsync(note.Id);
                saved.LastErrorCode.ShouldBe("completion_pointer_content_mismatch");
                saved.State.ShouldBe(LandNotificationState.AwaitingReceipt);
                // Repair the file without appending any prompt: the same transcript row now confirms.
                await File.WriteAllTextAsync(spill, report, ct);
                break;
            }
            case "negative-save-fails":
            {
                await using var db = CountedContext(h, new ThrowOnceSaveInterceptor());
                var failed = await PassAsync(h, note.Id, ct, FreshService(h, db, boundary));
                failed.ReceiptSelects.ShouldBe(1);
                failed.ReceiptRows.ShouldBe(48);
                boundary.ReachedNames.ShouldContain("receipt-scan-exhausted");
                boundary.ReachedNames.ShouldNotContain("receipt-scan-before-certificate", "the certificate waits for the final save");
                (await h.NoteAsync(note.Id)).LastErrorCode.ShouldBe("notification_reconcile_failed:InvalidOperationException");
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(cut), cut, null);
        }

        var metrics = h.Cache!.GetMetrics();
        metrics.Proofs.ShouldBe(0, "a failed or matched pass never leaves a proof");
        metrics.Publishes.ShouldBe(0);
        await ShouldStayOpenAsync(h, note.Id);

        // A fresh scope over the same singleton cache: had any proof been published for the
        // unchanged committed state, this pass would reuse it and never see the receipt.
        await using (var fresh = CountedContext(h))
        {
            var recovery = await PassAsync(h, note.Id, ct, FreshService(h, fresh));
            recovery.ReceiptSelects.ShouldBe(1, "recovery re-reads the transcript");
        }
        if (cut == "negative-save-fails")
        {
            await ShouldStayOpenAsync(h, note.Id);
            h.Cache.GetMetrics().Publishes.ShouldBe(1, "the next committed miss is certified");
            (await PassAsync(h, note.Id, ct)).ReceiptSelects.ShouldBe(0, "and reused");
        }
        else
        {
            await ShouldBeConfirmedAtAsync(h, note.Id, Match, receiptText: cut == "profiled-pointer-hash-mismatch-then-repair" ? wire : null);
            h.Cache.GetMetrics().Proofs.ShouldBe(0);
        }
        h.Bridge.Adapter.Inputs.ShouldBeEmpty("receipt reconciliation must never type");
    }

    // V-9 -------------------------------------------------------------------------------------------

    [Test]
    [Timeout(240_000)]
    [Arguments("two-kind-48")]
    [Arguments("one-kind-852")]
    [Arguments("two-kind-1506")]
    [Arguments("changed-transcript-two-kind-48")]
    [Arguments("live-committed-row-9-8")]
    public async Task C1121_ReconcilePinsStatementAndRowBudgets(string shape, CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync();
        var candidates = shape switch { "one-kind-852" => 852, "two-kind-1506" => 1506, _ => 48 };
        var (note, _, events) = await ArrangeAsync(h, candidates);
        int[] commands = [6, 5, 5];
        int[] transcript = [1, 0, 0];
        switch (shape)
        {
            case "two-kind-48":
                // A restart: the store is first touched by the reconciler's own observation.
                await h.RestartAsync();
                commands = [7, 5, 5];
                break;
            case "one-kind-852":
                await h.UpdateNoteAsync(note.Id, n => n.IsLegacy = true);
                break;
            case "live-committed-row-9-8":
                // The catch-up's own StartedAt, identity and MAX statements, never a cache probe.
                h.Runner.Transcript = Mode.Entries;
                h.Runner.NextEntries = _ => [events[5]];
                commands = [9, 8, 8];
                transcript = [3, 2, 2];
                break;
        }

        var passes = await PassesAsync(h, note.Id, 3);
        if (shape == "changed-transcript-two-kind-48")
        {
            // An unrelated committed change outside the measured window reopens exactly one full scan.
            (await h.IngestAsync(h.SessionId, (TranscriptKinds.AssistantText, "unrelated assistant text"))).Count.ShouldBe(1);
            passes = [.. passes, .. await PassesAsync(h, note.Id, 2)];
            commands = [6, 5, 5, 6, 5];
            transcript = [1, 0, 0, 1, 0];
        }

        var roster = string.Join("\n----\n", passes.Select(p => p.Roster));
        passes.Select(p => p.Commands).ShouldBe(commands, roster);
        passes.Select(p => p.TranscriptStatements).ShouldBe(transcript, "no per-note existence, count or max probe\n" + roster);
        var scans = passes.Select(p => p.ReceiptSelects).ToArray();
        scans.ShouldBe(transcript.Select(t => shape == "live-committed-row-9-8" ? t - 2 : t).ToArray());
        passes.Select(p => p.ReceiptRows).ShouldBe(scans.Select(s => s * candidates).ToArray(), "the full scan reads every candidate");
        passes.SelectMany(p => p.Floors).ShouldAllBe(f => f == Floor, "the floor is the keyed row's baseline on every scan");
        passes.ShouldAllBe(p => p.Pulls == 1, "the catch-up pull runs every pass");
        foreach (var sql in passes.SelectMany(p => p.Sql))
        {
            ReceiptScanRecognizer.IsReceiptScan(sql).ShouldBeTrue(sql);
            sql.ShouldContain(shape == "one-kind-852" ? "t.\"Kind\" = 'UserPrompt'" : "t.\"Kind\" IN ('UserPrompt', 'QueuedUserPrompt')");
            sql.ShouldNotContain("ToolInput");
            sql.ShouldNotContain("ApiErrorTimeZoneId");
            sql.ShouldNotContain("ModelCalls");
        }
        var metrics = h.Cache!.GetMetrics();
        metrics.Proofs.ShouldBe(1);
        metrics.Publishes.ShouldBe(scans.Count(s => s == 1));
        metrics.Hits.ShouldBe(scans.Count(s => s == 0));
        await ShouldStayOpenAsync(h, note.Id);
    }

    // Busy and crashed destinations (D2) ------------------------------------------------------------

    /// <summary>
    /// The runner's transcript for one session: what a pull returns. The live stream never lands a
    /// submission here; only a runtime pull (the queue's post-failure grace, a sync, or the
    /// reconciler's own catch-up) persists it.
    /// </summary>
    private sealed class RunnerTranscript(LandReceiptScanHarness h, IEnumerable<SessionRunnerTranscriptEvent> seeded)
    {
        private readonly List<SessionRunnerTranscriptEvent> _events = [.. seeded];
        public int Submissions;

        public IReadOnlyList<SessionRunnerTranscriptEvent> Events { get { lock (_events) return [.. _events]; } }

        public void Append(string kind, string? text)
        {
            lock (_events) _events.Add(h.Event(h.SessionId, _events.Max(e => e.Sequence) + 1, kind, text));
        }

        /// <summary>The runner holds the session and answers its whole transcript.</summary>
        public void Live()
        {
            h.Runner.Transcript = Mode.Entries;
            h.Runner.NextEntries = _ => Events;
        }

        /// <summary>The submit path: the session takes the prompt and finishes the turn in its own transcript only.</summary>
        public void BindSubmissions(bool crashAfterTaking) => h.Bridge.Adapter.OnSubmitted = submitted =>
        {
            Interlocked.Increment(ref Submissions);
            Append(TranscriptKinds.UserPrompt, submitted);
            Append(TranscriptKinds.TurnEnd, null);
            // A crash right after taking it: the runner no longer holds the session.
            if (crashAfterTaking) h.Runner.Transcript = Mode.NotFound;
            return Task.CompletedTask;
        };
    }

    private static async Task<long> PromptSequenceAsync(LandReceiptScanHarness h, string body)
    {
        await using var db = h.Fixture();
        var prompts = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == h.SessionId && t.Kind == TranscriptKinds.UserPrompt && t.Text == body)
            .Select(t => t.Sequence).ToListAsync();
        return prompts.ShouldHaveSingleItem("the complete prompt is committed exactly once");
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("busy-turn-end-and-prompt-only-via-runner-pull")]
    [Arguments("crashed-after-typing-then-resumed")]
    [Arguments("proof-before-crash-then-resumed")]
    public async Task C1121_BusyOrCrashedDestinationRecoversThroughQueueAndRunnerPull(string arm, CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync();
        IReadOnlyList<SessionRunnerTranscriptEvent> seeded = [];
        var busy = arm.StartsWith("busy", StringComparison.Ordinal);
        // A body that fits one write, so the queue types it whole rather than spilling it.
        var note = await h.SeedLinkedNoteAsync("C1121 recovery outcome", async _ =>
        {
            seeded = await h.SeedTranscriptAsync(h.SessionId);
            // Mid-turn: the destination is Running and busy, so a WhenIdle note waits.
            if (busy) seeded = [.. seeded, h.Event(h.SessionId, seeded.Max(e => e.Sequence) + 1, TranscriptKinds.AssistantText, "still working")];
            if (busy) await h.IngestEventsAsync(h.SessionId, [seeded[^1]]);
        });
        await h.AssertCoherentAsync(h.SessionId);
        var runner = new RunnerTranscript(h, seeded);
        var rowId = note.QueueMessageId!.Value;

        switch (arm)
        {
            case "busy-turn-end-and-prompt-only-via-runner-pull":
            {
                runner.Live();
                runner.BindSubmissions(crashAfterTaking: false);
                await h.Bridge.Queue.FlushIfIdleAsync(h.SessionId, ct);
                runner.Submissions.ShouldBe(0, "a busy destination is not typed into");
                var waiting = await PassAsync(h, note.Id, ct);
                waiting.ReceiptSelects.ShouldBe(0, "an untyped row has no receipt to scan for");
                await ShouldStayOpenAsync(h, note.Id);

                // The turn end reaches only the runner (the stream dropped it). The runtime's pull
                // persists it and flushes the real queue, which types the note; the prompt it takes
                // is again in the runner's transcript only, and the queue's own pull persists it.
                runner.Append(TranscriptKinds.TurnEnd, null);
                await h.Runtime.SyncTranscriptAsync(h.SessionId, ct);
                runner.Submissions.ShouldBe(1, "the real queue typed the note once the turn ended");
                break;
            }
            case "crashed-after-typing-then-resumed":
            {
                runner.Live();
                runner.BindSubmissions(crashAfterTaking: true);
                await h.Bridge.Queue.FlushIfIdleAsync(h.SessionId, ct);
                runner.Submissions.ShouldBeGreaterThanOrEqualTo(1, "the real queue typed the note");
                await h.SetStatusAsync(h.SessionId, SessionStatus.Failed);
                var row = await h.RowAsync(rowId);
                row.DeliveryAttempts.ShouldBeGreaterThan(0);
                // Crashed: the cache certifies the quiet miss and reuses it, with the prompt unrecorded.
                var crashed = await PassesAsync(h, note.Id, 2);
                crashed.Select(p => p.ReceiptSelects).ShouldBe([1, 0], "a crashed (terminal) destination's miss is cached");
                h.Cache!.GetMetrics().Proofs.ShouldBe(1);
                break;
            }
            case "proof-before-crash-then-resumed":
            {
                // An earlier attempt failed while the destination was Stopped; that miss is cached.
                await h.MarkSentAsync(note, r =>
                {
                    r.Status = QueuedMessageStatus.Pending;
                    r.SentAt = null;
                    r.DeliveryVerdict = DeliveryVerdict.NoTranscriptRecord;
                });
                await h.SetStatusAsync(h.SessionId, SessionStatus.Stopped);
                (await PassesAsync(h, note.Id, 2)).Select(p => p.ReceiptSelects).ShouldBe([1, 0]);
                h.Cache!.GetMetrics().Proofs.ShouldBe(1, "a proof exists before the relaunch and crash");

                // Relaunched: the real queue retypes the parked attempt; the session takes it and crashes.
                await h.SetStatusAsync(h.SessionId, SessionStatus.Running);
                runner.Live();
                runner.BindSubmissions(crashAfterTaking: true);
                await h.Bridge.Queue.FlushIfIdleAsync(h.SessionId, ct);
                runner.Submissions.ShouldBeGreaterThanOrEqualTo(1, "the real queue retyped the note");
                await h.SetStatusAsync(h.SessionId, SessionStatus.Failed);
                (await h.RowAsync(rowId)).DeliveryAttempts.ShouldBeGreaterThan(1);
                var crashed = await PassAsync(h, note.Id, ct);
                crashed.ReceiptSelects.ShouldBe(1, "the new attempt is a new context: the old proof is not reused");
                await ShouldStayOpenAsync(h, note.Id);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(arm), arm, null);
        }

        if (!busy)
        {
            // Resumed: the runner holds the session again, with the prompt it took before the crash.
            (await PromptSequenceCountAsync(h, note.Body)).ShouldBe(0, "the crash left the prompt unrecorded");
            await h.SetStatusAsync(h.SessionId, SessionStatus.Running);
            runner.Live();
        }
        var typed = runner.Submissions;
        var recovery = await PassAsync(h, note.Id, ct);
        recovery.Pulls.ShouldBe(1);
        recovery.ReceiptSelects.ShouldBe(1, "a running destination is outside the whitelist: full scan");
        var sequence = await PromptSequenceAsync(h, note.Body);
        var floor = (await h.RowAsync(rowId)).LastDeliveryBaselineSequence.ShouldNotBeNull();
        sequence.ShouldBeGreaterThan(floor);
        await ShouldBeConfirmedAtAsync(h, note.Id, sequence, floor);
        h.Cache!.GetMetrics().Proofs.ShouldBe(0, "confirmation removes any proof");
        runner.Submissions.ShouldBe(typed, "receipt reconciliation never types");
        h.Runner.KillCalls.ShouldBe(0);
    }

    private static async Task<int> PromptSequenceCountAsync(LandReceiptScanHarness h, string body)
    {
        await using var db = h.Fixture();
        return await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
            && t.Kind == TranscriptKinds.UserPrompt && t.Text == body);
    }
}
