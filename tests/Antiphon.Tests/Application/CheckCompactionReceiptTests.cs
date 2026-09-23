using System.Data.Common;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0606 S1: the MODERN caller receipt. A specialist interpretation has no caller session of
/// its own - <c>SpecialistTaskRunner</c> leaves <c>ParentSessionId</c> null on every specialist run
/// - so the recipient owed a reading is reached through the run's producer: the winning
/// <c>SpecialistAttempt</c>, its <c>SpecialistRequest</c>, the checked task that request was taken
/// on, and the exact caller message that request published. Reading the identity off the run
/// instead left <c>AwaitingCheck</c> unreachable for every real modern Check, which also meant the
/// rolling restart allowance never reopened.
///
/// <para>The producer half is seeded (a settled winning request stands in for model execution and
/// qualification) and the delivery half is real: the actual <c>PublishCheckRequestAsync</c>, the
/// actual queue and flush, and the harness adapter that records the exact submitted UserPrompt.
/// Nothing here manufactures the successful caller row or its receipt.</para>
/// </summary>
[Category("Integration")]
public class CheckCompactionReceiptTests
{
    /// <summary>
    /// Short enough that no delivery path spills it to a file, long enough to be identifiable
    /// (<see cref="PromptSubmissionMatch.MinMatchChars"/>) and unique enough that a stray prompt
    /// cannot match it by accident.
    /// </summary>
    private const string Body = "c606 modern check reading for the waiting caller";

    private const string Reading = "c606 useful modern reading";

    // ---- V-1 / G-1 --------------------------------------------------------------------------

    [Test]
    [Arguments("idle")]
    [Arguments("busy")]
    public async Task Modern_receipt_recovers_without_interpretation_parent(string caller)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CallerAsync(schema.ConnectionString);
        var w = await SeedAsync(schema.ConnectionString, h);

        await using (var db = NewDb(schema.ConnectionString))
        {
            var run = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == w.RunId);
            run.ParentSessionId.ShouldBeNull("a specialist run never carries a caller of its own");
            run.Id.ShouldNotBe(w.SubjectId, "the interpretation and the checked task are different tasks");
            (await db.SpecialistRequests.AsNoTracking().SingleAsync(r => r.Id == w.RequestId))
                .WinnerAttemptId.ShouldBe(w.AttemptId);
        }

        await SweepAsync(schema.ConnectionString);
        await AssertOpenAsync(schema.ConnectionString, w, "nothing is recovered before the note is published");

        var busy = caller == "busy";
        if (busy)
            await h.MarkWorkingAsync();
        var messageId = await PublishAsync(h, w);
        if (busy)
        {
            h.Adapter.Inputs.ShouldBeEmpty("a busy caller is never typed into mid-turn");
            await SweepAsync(schema.ConnectionString);
            await AssertOpenAsync(schema.ConnectionString, w, "a queued-but-undelivered note is not a receipt");
            await EndCallerTurnAsync(h);
            await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        }

        await using (var db = NewDb(schema.ConnectionString))
        {
            var note = await db.SessionQueuedMessages.AsNoTracking().SingleAsync();
            note.Id.ShouldBe(messageId);
            note.SourceTaskId.ShouldBe(w.SubjectId, "the queue row names the CHECKED task, not the interpretation");
            note.Origin.ShouldBe(QueuedMessageOrigin.Check);
            note.Status.ShouldBe(QueuedMessageStatus.Sent);
        }

        await SweepAsync(schema.ConnectionString);
        await AssertRecoveredAsync(schema.ConnectionString, w);

        // A repeat sweep must not re-close an already closed episode.
        await SweepAsync(schema.ConnectionString);
        await using var verify = NewDb(schema.ConnectionString);
        (await verify.AgentIncidents.CountAsync(i =>
            i.Kind == AgentIncidentKind.CompactionContinuationRecovered)).ShouldBe(1);
        (await verify.LegacyCheckNotePublications.CountAsync()).ShouldBe(0,
            "the modern path publishes nothing legacy");
        await AssertRecoveredAsync(schema.ConnectionString, w);
    }

    // ---- V-2: the producer and the delivery both survive being cut and rebuilt ----------------

    [Test]
    [Arguments("insert-failure")]
    [Arguments("queued-before-flush")]
    [Arguments("submit-retry")]
    [Arguments("receipt-before-sweep")]
    public async Task Modern_receipt_survives_publication_and_delivery_recreation(string cut)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var failure = new FailPublication();
        await using var h = await CallerAsync(schema.ConnectionString,
            options => options.AddInterceptors(failure));
        var w = await SeedAsync(schema.ConnectionString, h);
        await h.MarkWorkingAsync();

        if (cut == "insert-failure")
        {
            failure.Armed = true;
            var thrown = await Should.ThrowAsync<Exception>(async () => await PublishAsync(h, w));
            thrown.ToString().ShouldContain("c606-publication-write-failed");
            await using var rolled = NewDb(schema.ConnectionString);
            (await rolled.SessionQueuedMessages.CountAsync()).ShouldBe(0);
            (await rolled.AgentTaskEvents.CountAsync(e => e.Id == w.RequestId)).ShouldBe(0);
            (await rolled.SpecialistRequests.AsNoTracking().SingleAsync(r => r.Id == w.RequestId))
                .CallerPublishedAt.ShouldBeNull("the rollback left no publication timestamp");
        }

        var messageId = await PublishAsync(h, w);
        await using (var queued = NewDb(schema.ConnectionString))
        {
            var row = await queued.SessionQueuedMessages.AsNoTracking().SingleAsync();
            row.Id.ShouldBe(messageId);
            row.Status.ShouldBe(QueuedMessageStatus.Pending, "the busy caller has not been typed into yet");
        }

        if (cut == "submit-retry")
            h.Adapter.SwallowSubmits = 1;
        await EndCallerTurnAsync(h);
        var flusher = cut == "queued-before-flush"
            ? ActivatorUtilities.CreateInstance<SessionMessageQueueService>(h.Provider)
            : h.Queue;
        await flusher.FlushSessionAsync(h.SessionId, CancellationToken.None);

        await using (var delivered = NewDb(schema.ConnectionString))
        {
            var row = await delivered.SessionQueuedMessages.AsNoTracking().SingleAsync();
            row.Id.ShouldBe(messageId, "a retried delivery keeps the one durable message identity");
            row.Status.ShouldBe(QueuedMessageStatus.Sent);
        }

        // Every arm finishes on a coordinator that never saw any of it happen.
        await SweepAsync(schema.ConnectionString);
        await AssertRecoveredAsync(schema.ConnectionString, w);
    }

    // ---- G-2: retained legacy history is not a veto ------------------------------------------

    [Test]
    public async Task Historical_legacy_publication_does_not_block_modern_receipt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CallerAsync(schema.ConnectionString);
        var w = await SeedAsync(schema.ConnectionString, h);
        await PublishAsync(h, w);

        // An older episode on this same seat and session left a publication behind for a DIFFERENT
        // interpretation. It is history, not evidence about this run.
        await using (var db = NewDb(schema.ConnectionString))
        {
            var olderEpisode = Guid.NewGuid();
            var olderRun = Guid.NewGuid();
            var olderSubject = Guid.NewGuid();
            var when = w.Generation.AddMinutes(-90);
            db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
            {
                Id = olderEpisode, PhysicalAgentId = w.InterpreterAgentId, SessionId = w.InterpreterSessionId,
                AcceptedStartedAt = when, BoundaryIdentity = "older-boundary", BoundaryCreatedAt = when,
                ContinuationCreatedAt = when, ConfiguredThresholdMinutes = 10, DetectedAt = when,
                State = CheckCompactionRecoveryState.Recovered, Reason = "legacy-receipt",
            });
            db.LegacyCheckNotePublications.Add(HistoricalPublication(w, olderEpisode, olderRun, olderSubject, when));
            await db.SaveChangesAsync();
        }

        await SweepAsync(schema.ConnectionString);
        await AssertRecoveredAsync(schema.ConnectionString, w);
    }

    // ---- G-3: applicable legacy evidence owns its own verdict --------------------------------

    [Test]
    [Arguments("suppressed")]
    [Arguments("wrong-episode")]
    public async Task Invalid_applicable_legacy_publication_cannot_borrow_modern_receipt(string flaw)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CallerAsync(schema.ConnectionString);
        var w = await SeedAsync(schema.ConnectionString, h);
        await PublishAsync(h, w);

        // This publication belongs to the very interpretation the modern proof is about, so it
        // answers for that run - and it is not valid, so the answer is "not yet".
        await using (var db = NewDb(schema.ConnectionString))
        {
            var when = w.Generation.AddMinutes(5);
            var owner = w.EpisodeId;
            if (flaw == "wrong-episode")
            {
                owner = Guid.NewGuid();
                db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
                {
                    Id = owner, PhysicalAgentId = w.InterpreterAgentId, SessionId = w.InterpreterSessionId,
                    AcceptedStartedAt = when, BoundaryIdentity = "other-boundary", BoundaryCreatedAt = when,
                    ContinuationCreatedAt = when, ConfiguredThresholdMinutes = 10, DetectedAt = when,
                    State = CheckCompactionRecoveryState.AwaitingCheck,
                });
            }

            var publication = HistoricalPublication(w, owner, w.RunId, w.SubjectId, when);
            if (flaw == "suppressed")
            {
                publication.State = LegacyCheckNoteState.Suppressed;
                publication.Body = null;
                publication.ContentDigest = null;
                publication.ProducedAt = null;
                publication.SuppressedAt = when;
                publication.SuppressionReason = "captured but never produced";
            }

            db.LegacyCheckNotePublications.Add(publication);
            await db.SaveChangesAsync();
        }

        await SweepAsync(schema.ConnectionString);
        await AssertOpenAsync(schema.ConnectionString, w,
            "invalid evidence that belongs to this run withholds recovery instead of falling through");
    }

    // ---- G-4: only the winning attempt earned the receipt -------------------------------------

    [Test]
    public async Task Nonwinning_interpretation_cannot_claim_request_receipt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CallerAsync(schema.ConnectionString);
        var w = await SeedAsync(schema.ConnectionString, h);
        await PublishAsync(h, w);

        // A second, NEWER succeeded run of the same request, with its own brief and turn end. It is
        // the candidate the receipt sweep picks up, and it never won - the caller was sent the
        // winner's reading, not this one's.
        await using (var db = NewDb(schema.ConnectionString))
        {
            var loser = Guid.NewGuid();
            var at = w.RunCreatedAt.AddMinutes(1);
            db.AgentTasks.Add(Interpretation(w, loser, Reading + " (losing run)", at));
            db.SpecialistAttempts.Add(new SpecialistAttempt
            {
                Id = Guid.NewGuid(), RequestId = w.RequestId, CandidateId = Guid.NewGuid(),
                PhysicalAgentId = w.InterpreterAgentId, TaskId = loser, Ordinal = 2,
                SessionId = w.InterpreterSessionId, SessionStartedAt = w.Generation,
                StartedAt = at, DeadlineAt = at.AddMinutes(5), CompletedAt = at,
                Outcome = SpecialistAttemptOutcome.InvalidReading,
            });
            await db.SaveChangesAsync();
            await AddInterpreterTurnAsync(db, w.InterpreterSessionId, loser, at);
        }

        await SweepAsync(schema.ConnectionString);
        await AssertOpenAsync(schema.ConnectionString, w, "a losing run cannot claim the winner's receipt");
    }

    // ---- G-5: the receipt belongs to the RESUMED generation -----------------------------------

    [Test]
    public async Task Prior_generation_request_cannot_recover_current_episode()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CallerAsync(schema.ConnectionString);
        var w = await SeedAsync(schema.ConnectionString, h);
        await PublishAsync(h, w);

        // Same session id, same everything else: only the accepted generation is the pre-restart
        // one, so this reading was produced by the turn the restart interrupted.
        await using (var db = NewDb(schema.ConnectionString))
        {
            await db.SpecialistAttempts.Where(a => a.Id == w.AttemptId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.SessionStartedAt, w.PriorGeneration));
        }

        await SweepAsync(schema.ConnectionString);
        await AssertOpenAsync(schema.ConnectionString, w,
            "an identical session id is not the resumed generation");
    }

    // ---- G-6: the exact message this request published ----------------------------------------

    [Test]
    public async Task Another_message_for_the_checked_task_is_not_the_receipt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CallerAsync(schema.ConnectionString);
        var w = await SeedAsync(schema.ConnectionString, h);
        await h.MarkWorkingAsync();
        var messageId = await PublishAsync(h, w);

        // The request's own note never reached the caller; a LATER Check note for the same delegate
        // did. Taking "the newest Check row for this subject" would hand this episode a receipt it
        // never earned.
        const string decoy = "c606 a later check note for the very same delegate";
        await using (var db = NewDb(schema.ConnectionString))
        {
            var floor = await MaxSequenceAsync(db, h.SessionId);
            await db.SessionQueuedMessages.Where(m => m.Id == messageId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(m => m.Status, QueuedMessageStatus.Sent)
                    .SetProperty(m => m.LastDeliveryBaselineSequence, floor));
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Body = decoy,
                Sequence = await MaxQueueSequenceAsync(db, h.SessionId) + 1,
                Status = QueuedMessageStatus.Sent, Origin = QueuedMessageOrigin.Check,
                SourceTaskId = w.SubjectId, LastDeliveryBaselineSequence = floor,
                CreatedAt = DateTime.UtcNow, SentAt = DateTime.UtcNow,
                ConversationKey = AgentTaskCheckService.ConversationKey(w.SubjectId),
            });
            await db.SaveChangesAsync();
        }

        await BridgeQueueHarness.InsertEntryAsync(h.SessionId, TranscriptKinds.UserPrompt, decoy,
            timestamp: DateTime.UtcNow, connectionString: schema.ConnectionString);

        await SweepAsync(schema.ConnectionString);
        await AssertOpenAsync(schema.ConnectionString, w,
            "a delivered decoy for the same delegate is not this request's receipt");
    }

    // ---- G-7: the WHOLE note, not its head ----------------------------------------------------

    [Test]
    public async Task Partial_caller_prompt_does_not_recover()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CallerAsync(schema.ConnectionString);
        var w = await SeedAsync(schema.ConnectionString, h);
        await h.MarkWorkingAsync();
        var messageId = await PublishAsync(h, w);

        var clipped = Body[..20];
        clipped.Length.ShouldBeGreaterThanOrEqualTo(PromptSubmissionMatch.MinMatchChars,
            "the fragment still IDENTIFIES the note - only completeness is missing");
        await MarkSentAtFloorAsync(schema.ConnectionString, h, messageId);
        await BridgeQueueHarness.InsertEntryAsync(h.SessionId, TranscriptKinds.UserPrompt, clipped,
            timestamp: DateTime.UtcNow, connectionString: schema.ConnectionString);

        await SweepAsync(schema.ConnectionString);
        await AssertOpenAsync(schema.ConnectionString, w, "a clipped note is not a delivered reading");
    }

    // ---- G-8: the receipt follows the delivery baseline ---------------------------------------

    [Test]
    [Arguments("at-floor")]
    [Arguments("older")]
    public async Task Caller_prompt_at_or_before_baseline_does_not_recover(string placement)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CallerAsync(schema.ConnectionString);
        var w = await SeedAsync(schema.ConnectionString, h);
        await h.MarkWorkingAsync();
        var messageId = await PublishAsync(h, w);

        // The whole body is in the caller's transcript - from BEFORE this delivery started, so it
        // is the previous conversation, not evidence that this note arrived.
        var seq = await BridgeQueueHarness.InsertEntryAsync(h.SessionId, TranscriptKinds.UserPrompt, Body,
            timestamp: DateTime.UtcNow, connectionString: schema.ConnectionString);
        var floor = placement == "at-floor" ? seq : seq + 1;
        await using (var db = NewDb(schema.ConnectionString))
        {
            await db.SessionQueuedMessages.Where(m => m.Id == messageId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(m => m.Status, QueuedMessageStatus.Sent)
                    .SetProperty(m => m.LastDeliveryBaselineSequence, floor));
        }

        await SweepAsync(schema.ConnectionString);
        await AssertOpenAsync(schema.ConnectionString, w,
            "a prompt at or before the delivery baseline predates the note");
    }

    // ---- G-11 / G-12 / G-13 and the rest of the evidence a modern receipt needs ---------------

    [Test]
    [Arguments("absent-correlation")]
    [Arguments("absent-message")]
    [Arguments("absent-baseline")]
    [Arguments("wrong-purpose")]
    [Arguments("wrong-caller")]
    [Arguments("wrong-source")]
    [Arguments("wrong-origin")]
    [Arguments("not-sent")]
    [Arguments("empty-result")]
    [Arguments("missing-interpreter-prompt")]
    [Arguments("missing-interpreter-end")]
    [Arguments("sent-without-user-prompt")]
    public async Task Modern_receipt_requires_complete_evidence(string missing)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CallerAsync(schema.ConnectionString);
        var w = await SeedAsync(schema.ConnectionString, h);
        await PublishAsync(h, w);

        var restore = await BreakAsync(schema.ConnectionString, h, w, missing);
        await SweepAsync(schema.ConnectionString);
        await AssertOpenAsync(schema.ConnectionString, w, $"'{missing}' must withhold recovery");

        // The control: the SAME world, with only that one piece of evidence put back.
        await using (var db = NewDb(schema.ConnectionString))
        {
            await restore(db);
            await db.SaveChangesAsync();
        }

        await SweepAsync(schema.ConnectionString);
        await AssertRecoveredAsync(schema.ConnectionString, w);
    }

    /// <summary>
    /// Removes exactly one piece of durable evidence and hands back the edit that puts it back, so
    /// every negative case carries its own positive control on the same world.
    /// </summary>
    private static async Task<Func<AppDbContext, Task>> BreakAsync(
        string connectionString, BridgeQueueHarness h, Receipt w, string missing)
    {
        await using var db = NewDb(connectionString);
        switch (missing)
        {
            case "absent-correlation":
            {
                var attempt = await db.SpecialistAttempts.SingleAsync(a => a.Id == w.AttemptId);
                var copy = Clone(attempt);
                db.SpecialistAttempts.Remove(attempt);
                await db.SaveChangesAsync();
                return restore => { restore.SpecialistAttempts.Add(copy); return Task.CompletedTask; };
            }
            case "absent-message":
            {
                var note = await db.SessionQueuedMessages.SingleAsync();
                var copy = Clone(note);
                db.SessionQueuedMessages.Remove(note);
                await db.SaveChangesAsync();
                return restore => { restore.SessionQueuedMessages.Add(copy); return Task.CompletedTask; };
            }
            case "absent-baseline":
            {
                var note = await db.SessionQueuedMessages.SingleAsync();
                var floor = note.LastDeliveryBaselineSequence;
                floor.ShouldNotBeNull("the real delivery recorded a baseline");
                note.LastDeliveryBaselineSequence = null;
                await db.SaveChangesAsync();
                return async restore =>
                {
                    (await restore.SessionQueuedMessages.SingleAsync()).LastDeliveryBaselineSequence = floor;
                };
            }
            case "wrong-purpose":
            {
                var request = await db.SpecialistRequests.SingleAsync(r => r.Id == w.RequestId);
                request.Purpose = SpecialistRequestPurpose.Qualification;
                await db.SaveChangesAsync();
                return async restore =>
                {
                    (await restore.SpecialistRequests.SingleAsync(r => r.Id == w.RequestId)).Purpose =
                        SpecialistRequestPurpose.Check;
                };
            }
            case "wrong-caller":
            {
                // The row now belongs to a THIRD session. The intended caller still holds the same
                // whole prompt, so only the ownership equality stands between them.
                var stranger = Guid.NewGuid();
                db.AgentSessions.Add(new AgentSession
                {
                    Id = stranger, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running, Cwd = h.TempRoot, CreatedAt = w.Generation,
                    StartedAt = w.Generation, LastSeenAt = DateTime.UtcNow,
                });
                var note = await db.SessionQueuedMessages.SingleAsync();
                note.AgentSessionId = stranger;
                await db.SaveChangesAsync();
                return async restore =>
                {
                    (await restore.SessionQueuedMessages.SingleAsync()).AgentSessionId = h.SessionId;
                };
            }
            case "wrong-source":
            {
                var note = await db.SessionQueuedMessages.SingleAsync();
                note.SourceTaskId = Guid.NewGuid();
                await db.SaveChangesAsync();
                return async restore =>
                {
                    (await restore.SessionQueuedMessages.SingleAsync()).SourceTaskId = w.SubjectId;
                };
            }
            case "wrong-origin":
            {
                var note = await db.SessionQueuedMessages.SingleAsync();
                note.Origin = QueuedMessageOrigin.Ui;
                await db.SaveChangesAsync();
                return async restore =>
                {
                    (await restore.SessionQueuedMessages.SingleAsync()).Origin = QueuedMessageOrigin.Check;
                };
            }
            case "not-sent":
            {
                var note = await db.SessionQueuedMessages.SingleAsync();
                note.Status = QueuedMessageStatus.Pending;
                await db.SaveChangesAsync();
                return async restore =>
                {
                    (await restore.SessionQueuedMessages.SingleAsync()).Status = QueuedMessageStatus.Sent;
                };
            }
            case "empty-result":
            {
                var run = await db.AgentTasks.SingleAsync(t => t.Id == w.RunId);
                run.Result = "";
                await db.SaveChangesAsync();
                return async restore =>
                {
                    (await restore.AgentTasks.SingleAsync(t => t.Id == w.RunId)).Result = Reading;
                };
            }
            case "missing-interpreter-prompt":
            {
                var marker = DelegationReportFormatter.TaskMarker(w.RunId);
                var entries = await db.TranscriptEntries
                    .Where(t => t.AgentSessionId == w.InterpreterSessionId && t.Kind == TranscriptKinds.UserPrompt)
                    .ToListAsync();
                var prompt = entries.Single(t => t.Text != null && t.Text.Contains(marker, StringComparison.Ordinal));
                var copy = Clone(prompt);
                db.TranscriptEntries.Remove(prompt);
                await db.SaveChangesAsync();
                return restore => { restore.TranscriptEntries.Add(copy); return Task.CompletedTask; };
            }
            case "missing-interpreter-end":
            {
                var end = await db.TranscriptEntries
                    .Where(t => t.AgentSessionId == w.InterpreterSessionId && t.Kind == TranscriptKinds.TurnEnd)
                    .OrderByDescending(t => t.Sequence)
                    .FirstAsync();
                var copy = Clone(end);
                db.TranscriptEntries.Remove(end);
                await db.SaveChangesAsync();
                return restore => { restore.TranscriptEntries.Add(copy); return Task.CompletedTask; };
            }
            case "sent-without-user-prompt":
            {
                var prompts = await db.TranscriptEntries
                    .Where(t => t.AgentSessionId == h.SessionId && t.Kind == TranscriptKinds.UserPrompt)
                    .ToListAsync();
                var receipt = prompts.Single(t => PromptSubmissionMatch.IsCompleteIn(Body, t.Text));
                var copy = Clone(receipt);
                db.TranscriptEntries.Remove(receipt);
                await db.SaveChangesAsync();
                return restore => { restore.TranscriptEntries.Add(copy); return Task.CompletedTask; };
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(missing), missing, "unknown evidence case");
        }
    }

    // ---- world ---------------------------------------------------------------------------------

    private sealed record Receipt(
        Guid InterpreterAgentId,
        Guid InterpreterSessionId,
        Guid EpisodeId,
        Guid SubjectId,
        Guid RunId,
        Guid RequestId,
        Guid AttemptId,
        DateTime PriorGeneration,
        DateTime Generation,
        DateTime StopOutcomeAt,
        DateTime RunCreatedAt);

    private static Task<BridgeQueueHarness> CallerAsync(
        string connectionString, Action<DbContextOptionsBuilder>? configureDbContext = null) =>
        BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = connectionString,
            ConfigureDbContext = configureDbContext,
        });

    /// <summary>
    /// The restarted standing Check seat, its episode waiting on a reading, and the settled winning
    /// specialist request that produced one - everything a real restart leaves behind EXCEPT the
    /// caller's queue row and receipt, which the real publication and the real queue then produce.
    /// </summary>
    private static async Task<Receipt> SeedAsync(string connectionString, BridgeQueueHarness caller)
    {
        var priorGeneration = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-60));
        var generation = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-30));
        var stopOutcomeAt = generation.AddSeconds(-1);
        var runCreatedAt = generation.AddMinutes(2);
        var interpreterAgentId = Guid.NewGuid();
        var interpreterSessionId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var w = new Receipt(interpreterAgentId, interpreterSessionId, episodeId, subjectId, runId,
            requestId, attemptId, priorGeneration, generation, stopOutcomeAt, runCreatedAt);

        await using var db = NewDb(connectionString);
        db.Agents.Add(new Agent
        {
            Id = interpreterAgentId, Name = "check", Slug = "c" + interpreterAgentId.ToString("N")[..8],
            WorkingDirectory = Path.GetTempPath(), Kind = AgentKind.ClaudeCode, AlwaysOn = true,
            Status = AgentStatus.Running, StandingSpecialistRole = AgentTaskRole.Check,
            StandingSpecialistOwnerId = interpreterAgentId, PersistentSessionId = interpreterSessionId.ToString("D"),
            CreatedAt = priorGeneration, UpdatedAt = generation,
        });
        db.AgentSessions.Add(new AgentSession
        {
            Id = interpreterSessionId, StandingAgentId = interpreterAgentId, DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running, Cwd = Path.GetTempPath(),
            CreatedAt = priorGeneration, StartedAt = generation, LastSeenAt = DateTime.UtcNow,
        });
        db.AgentSupervisionStates.Add(new AgentSupervisionState
        {
            AgentId = interpreterAgentId, ActiveCompactionRecoveryId = episodeId,
            LastAutomaticCompactionRestartAt = stopOutcomeAt, CompactionRestartReceiptEligible = false,
            UpdatedAt = stopOutcomeAt,
        });
        db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
        {
            Id = episodeId, PhysicalAgentId = interpreterAgentId, SessionId = interpreterSessionId,
            AcceptedStartedAt = priorGeneration, BoundaryIdentity = "c606-boundary",
            NativeContinuationIdentity = "c606-continuation", BoundaryCreatedAt = priorGeneration.AddMinutes(1),
            ContinuationCreatedAt = priorGeneration.AddMinutes(1), ConfiguredThresholdMinutes = 10,
            DetectedAt = priorGeneration.AddMinutes(11), StopOutcomeAt = stopOutcomeAt,
            State = CheckCompactionRecoveryState.AwaitingCheck, ResumeSessionId = interpreterSessionId,
            ResumeAcceptedStartedAt = generation, LaunchOutcome = "running", Reason = "awaiting-check",
        });
        // The CHECKED delegate: the caller's own task, and the only place a caller identity exists.
        db.AgentTasks.Add(new AgentTask
        {
            Id = subjectId, RootTaskId = subjectId, Title = "subject", Goal = "watch",
            Role = AgentTaskRole.Code, Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Working,
            AgentId = caller.AgentId, ParentSessionId = caller.SessionId, ReplyTo = AgentTaskReplyTo.Session,
            WorkingDirectory = caller.TempRoot, CreatedAt = priorGeneration,
            DispatchedAt = priorGeneration, Attempt = 1, CheckCount = 1,
        });
        db.AgentTasks.Add(Interpretation(w, runId, Reading, runCreatedAt));
        db.SpecialistRequests.Add(new SpecialistRequest
        {
            Id = requestId, AgentId = interpreterAgentId, CheckedTaskId = subjectId, CheckNumber = 1,
            Purpose = SpecialistRequestPurpose.Check, Status = SpecialistRequestStatus.Succeeded,
            Outcome = SpecialistAttemptOutcome.ValidReading, StartedAt = generation,
            DeadlineAt = generation.AddMinutes(10), Title = "check the subject", Facts = "{}",
            WinnerAttemptId = attemptId, Reading = "reading", CompletedAt = runCreatedAt,
            HealthAppliedAt = runCreatedAt,
        });
        db.SpecialistAttempts.Add(new SpecialistAttempt
        {
            Id = attemptId, RequestId = requestId, CandidateId = Guid.NewGuid(),
            PhysicalAgentId = interpreterAgentId, TaskId = runId, Ordinal = 1,
            SessionId = interpreterSessionId, SessionStartedAt = generation, StartedAt = generation,
            DeadlineAt = generation.AddMinutes(10), CompletedAt = runCreatedAt,
            Outcome = SpecialistAttemptOutcome.ValidReading, Reading = "reading",
        });
        await db.SaveChangesAsync();
        await AddInterpreterTurnAsync(db, interpreterSessionId, runId, runCreatedAt);

        // The caller has a history of its own, so the note below is an ordinary delivery rather
        // than a session's unobservable first turn.
        await caller.InsertTurnAsync("c606 caller was already talking", "acknowledged");
        return w;
    }

    private static AgentTask Interpretation(Receipt w, Guid runId, string reading, DateTime at) => new()
    {
        Id = runId, RootTaskId = runId, Title = "read", Goal = "interpret", Role = AgentTaskRole.Check,
        Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Succeeded, Result = reading,
        AgentId = w.InterpreterAgentId, AgentSessionId = w.InterpreterSessionId,
        // Deliberately null: this is the shape SpecialistTaskRunner produces, and the whole point
        // of the producer join.
        ParentSessionId = null,
        WorkingDirectory = Path.GetTempPath(), CreatedAt = at, CompletedAt = at,
    };

    private static async Task AddInterpreterTurnAsync(AppDbContext db, Guid sessionId, Guid runId, DateTime at)
    {
        var seq = await MaxSequenceAsync(db, sessionId);
        db.TranscriptEntries.AddRange(
            new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = seq + 1,
                Kind = TranscriptKinds.UserPrompt,
                Text = DelegationReportFormatter.TaskMarker(runId) + " read the facts",
                Timestamp = at, CreatedAt = at,
            },
            new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = seq + 2,
                Kind = TranscriptKinds.TurnEnd, StopReason = "end_turn", Timestamp = at, CreatedAt = at,
            });
        await db.SaveChangesAsync();
    }

    private static LegacyCheckNotePublication HistoricalPublication(
        Receipt w, Guid recoveryId, Guid runId, Guid subjectId, DateTime at) => new()
    {
        Id = Guid.NewGuid(), CheckedTaskId = subjectId, CheckedTaskAttempt = 1,
        CheckedTaskDispatchedAt = SessionGeneration.Normalize(at), CheckNumber = 1,
        RecoveryId = recoveryId, PhysicalAgentId = w.InterpreterAgentId,
        InterpreterSessionId = w.InterpreterSessionId, InterpreterAcceptedStartedAt = at,
        ParentSessionId = Guid.NewGuid(), CapturedAt = at, FactsSnapshotJson = "{}",
        RenderContextJson = "{}", InterpretationTaskId = runId, InterpretationDeadlineAt = at,
        State = LegacyCheckNoteState.Produced, SourceEventId = Guid.NewGuid(),
        NotificationId = Guid.NewGuid(), ProducedAt = at, Body = "an older legacy note body",
        ContentDigest = DelegationNoteDigest.Compute("an older legacy note body"), NextAttemptAt = at,
    };

    // ---- acting --------------------------------------------------------------------------------

    private static async Task<Guid> PublishAsync(BridgeQueueHarness h, Receipt w)
    {
        await h.Queue.PublishCheckRequestAsync(w.RequestId, h.SessionId, Body,
            "c606 captured timeline", suppress: false, CancellationToken.None);
        await using var db = NewDb(h.ConnectionString);
        var request = await db.SpecialistRequests.AsNoTracking().SingleAsync(r => r.Id == w.RequestId);
        request.CallerMessageId.ShouldNotBeNull("the publication recorded its own durable message id");
        return request.CallerMessageId.Value;
    }

    private static async Task SweepAsync(string connectionString)
    {
        await using var db = NewDb(connectionString);
        await new CheckCompactionContinuationService(
            db, TimeProvider.System, Options.Create(new DelegationSettings()),
            new CheckCompactionContinuationGate(),
            NullLogger<CheckCompactionContinuationService>.Instance).SweepAsync(CancellationToken.None);
    }

    private static Task EndCallerTurnAsync(BridgeQueueHarness h) =>
        h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn",
            timestamp: DateTime.UtcNow);

    private static async Task MarkSentAtFloorAsync(string connectionString, BridgeQueueHarness h, Guid messageId)
    {
        await using var db = NewDb(connectionString);
        var floor = await MaxSequenceAsync(db, h.SessionId);
        await db.SessionQueuedMessages.Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(m => m.Status, QueuedMessageStatus.Sent)
                .SetProperty(m => m.LastDeliveryBaselineSequence, floor));
    }

    // ---- assertions ----------------------------------------------------------------------------

    private static async Task AssertRecoveredAsync(string connectionString, Receipt w)
    {
        await using var db = NewDb(connectionString);
        var episode = await db.CheckCompactionRecoveries.AsNoTracking().SingleAsync(r => r.Id == w.EpisodeId);
        episode.State.ShouldBe(CheckCompactionRecoveryState.Recovered);
        episode.Reason.ShouldBe("receipt");
        episode.UsefulCheckTaskId.ShouldBe(w.RunId);

        var caller = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.Origin == QueuedMessageOrigin.Check).Select(m => m.AgentSessionId).SingleAsync();
        var whole = (await db.TranscriptEntries.AsNoTracking()
                .Where(t => t.AgentSessionId == caller && t.Kind == TranscriptKinds.UserPrompt && t.Text != null)
                .OrderBy(t => t.Sequence).ToListAsync())
            .Where(t => PromptSubmissionMatch.IsCompleteIn(Body, t.Text))
            .ToList();
        whole.Count.ShouldBe(1, "one whole reading reached the caller, once");
        episode.ConfirmingPromptSequence.ShouldBe(whole[0].Sequence);

        var supervision = await db.AgentSupervisionStates.AsNoTracking()
            .SingleAsync(s => s.AgentId == w.InterpreterAgentId);
        supervision.CompactionRestartReceiptEligible.ShouldBeTrue();
        supervision.ActiveCompactionRecoveryId.ShouldBeNull();
        CheckCompactionBudget.Allows(
            supervision.LastAutomaticCompactionRestartAt, supervision.CompactionRestartReceiptEligible,
            w.StopOutcomeAt + CheckCompactionBudget.RollingWindow + TimeSpan.FromMinutes(1))
            .ShouldBeTrue("a delivered reading is what reopens the rolling allowance");
    }

    private static async Task AssertOpenAsync(string connectionString, Receipt w, string because)
    {
        await using var db = NewDb(connectionString);
        var episode = await db.CheckCompactionRecoveries.AsNoTracking().SingleAsync(r => r.Id == w.EpisodeId);
        episode.State.ShouldBe(CheckCompactionRecoveryState.AwaitingCheck, because);
        episode.ConfirmingPromptSequence.ShouldBeNull(because);
        episode.UsefulCheckTaskId.ShouldBeNull(because);

        var supervision = await db.AgentSupervisionStates.AsNoTracking()
            .SingleAsync(s => s.AgentId == w.InterpreterAgentId);
        supervision.CompactionRestartReceiptEligible.ShouldBeFalse(because);
        supervision.ActiveCompactionRecoveryId.ShouldBe(w.EpisodeId, because);
        CheckCompactionBudget.Allows(
            supervision.LastAutomaticCompactionRestartAt, supervision.CompactionRestartReceiptEligible,
            w.StopOutcomeAt + CheckCompactionBudget.RollingWindow + TimeSpan.FromMinutes(1))
            .ShouldBeFalse(because);
    }

    // ---- plumbing ------------------------------------------------------------------------------

    /// <summary>Fails the caller's queue-row insert below the application's own guards.</summary>
    private sealed class FailPublication : DbCommandInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("INSERT INTO \"SessionQueuedMessages\"", StringComparison.Ordinal))
            {
                Armed = false;
                throw new InvalidOperationException("c606-publication-write-failed");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private static async Task<long> MaxSequenceAsync(AppDbContext db, Guid sessionId) =>
        await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;

    private static async Task<long> MaxQueueSequenceAsync(AppDbContext db, Guid sessionId) =>
        await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == sessionId)
            .MaxAsync(m => (long?)m.Sequence) ?? 0;

    private static SpecialistAttempt Clone(SpecialistAttempt a) => new()
    {
        Id = a.Id, RequestId = a.RequestId, CandidateId = a.CandidateId, PhysicalAgentId = a.PhysicalAgentId,
        TaskId = a.TaskId, Ordinal = a.Ordinal, SessionId = a.SessionId, SessionStartedAt = a.SessionStartedAt,
        Fingerprint = a.Fingerprint, CapabilityFingerprint = a.CapabilityFingerprint, StartedAt = a.StartedAt,
        DeadlineAt = a.DeadlineAt, CompletedAt = a.CompletedAt, Outcome = a.Outcome, Reason = a.Reason,
        Reading = a.Reading, PromptSequence = a.PromptSequence, ReportSequence = a.ReportSequence,
        CostUsd = a.CostUsd,
    };

    private static SessionQueuedMessage Clone(SessionQueuedMessage m) => new()
    {
        Id = m.Id, AgentSessionId = m.AgentSessionId, Body = m.Body, Sequence = m.Sequence,
        Status = m.Status, CreatedAt = m.CreatedAt, SentAt = m.SentAt, Origin = m.Origin,
        SourceTaskId = m.SourceTaskId, SourceLandNotificationId = m.SourceLandNotificationId,
        ConversationKey = m.ConversationKey, ContentDigest = m.ContentDigest,
        DeliveryAttempts = m.DeliveryAttempts, LastDeliveryStartedAt = m.LastDeliveryStartedAt,
        LastDeliveryBaselineSequence = m.LastDeliveryBaselineSequence, DeliveryVerdict = m.DeliveryVerdict,
        LastDeliveryGeneration = m.LastDeliveryGeneration,
    };

    private static TranscriptEntry Clone(TranscriptEntry t) => new()
    {
        Id = t.Id, AgentSessionId = t.AgentSessionId, Sequence = t.Sequence, Kind = t.Kind, Text = t.Text,
        StopReason = t.StopReason, Timestamp = t.Timestamp, CreatedAt = t.CreatedAt, Uuid = t.Uuid,
        ToolName = t.ToolName, ToolUseId = t.ToolUseId, IsApiError = t.IsApiError,
        ApiErrorClass = t.ApiErrorClass, ApiErrorStatus = t.ApiErrorStatus,
    };

    private static AppDbContext NewDb(string connectionString) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));
}
