using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0079 PC-13 / G-13. Everything else in this class carries a note that some OTHER producer
/// made. This one carries the mechanism's OWN success path end to end: a silent auto-compaction on
/// a live standing Check seat is detected, the turn is stopped conditionally, the same conversation
/// is resumed as a second accepted generation, the real check service then runs a NEW useful Check
/// on that generation, and its note reaches the caller as one whole correlated prompt — which is
/// what finally closes the episode.
///
/// <para>Without it the suite proves the legacy H-5 recovery path reaches a recipient and proves
/// individual stop/resume cuts survive a crash, but never that the restart it exists to perform
/// actually produces a new reading for the caller who was waiting for one.</para>
///
/// <para>Two boundaries are stated rather than hidden, and neither is the subject: no real Claude
/// reads the brief (the interpretation is settled with a reading, as everywhere else in this
/// class), and no real process is launched — the resume double writes the generation advance a
/// launch produces, the same boundary <c>CheckCompactionCrashTests</c> draws. What is real here is
/// the whole state machine between them, the real producer, and the real delivery.</para>
/// </summary>
public partial class CheckNoteDeliveryHandoffTests
{
    [Test]
    public async Task Automatic_restart_delivers_a_new_check_and_its_whole_caller_note()
    {
        await using var h = await Handoff.CreateAsync(withInterpreter: true, configureServices: services =>
        {
            services.AddScoped<AgentTaskLandNotificationService>();
            services.AddScoped<LegacyCheckNotePublicationService>();
        });
        await h.EnsureInterpreterAsync();
        await h.H.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");

        var stalled = await StallTheInterpreterOnCompactionAsync(h);
        var runner = StalledRunner(h.InterpreterSessionId, stalled.Generation);
        var resume = new GenerationAdvancingResume(h.ConnectionString, h.InterpreterSessionId, stalled.Generation);

        // Sweep 1: detect the overdue silence, confirm it against a fresh observation, stop the
        // turn conditionally and reserve the strict same-session resume.
        await using (var db = h.CreateContext())
            await CompactionService(db, runner, resume).SweepAsync(CancellationToken.None);

        await using (var db = h.CreateContext())
        {
            var episode = await db.CheckCompactionRecoveries.SingleAsync();
            episode.State.ShouldBe(CheckCompactionRecoveryState.ResumeReserved);
            episode.SessionId.ShouldBe(h.InterpreterSessionId);
            episode.ResumeSessionId.ShouldBe(h.InterpreterSessionId,
                "a strict same-conversation resume, never a Fresh start");
            runner.CompactionStops.Count.ShouldBe(1);
            runner.KillCalls.ShouldBe(0, "the conditional stop never falls back to /kill");
            var acceptedGenerations = new[] { episode.AcceptedStartedAt, episode.ResumeAcceptedStartedAt!.Value };
            acceptedGenerations[0].ShouldBe(stalled.Generation);
            acceptedGenerations.Distinct().Count().ShouldBe(2, "G and G2, and nothing in between");
        }

        // Sweep 2: adopt the launch. The resumed generation is running, so the episode starts
        // waiting for a new Check rather than declaring itself recovered.
        await using (var db = h.CreateContext())
            await CompactionService(db, runner, resume).SweepAsync(CancellationToken.None);

        await using (var db = h.CreateContext())
        {
            (await db.CheckCompactionRecoveries.SingleAsync()).State
                .ShouldBe(CheckCompactionRecoveryState.AwaitingCheck);
            (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == h.InterpreterSessionId))
                .TerminationSource.ShouldBe(SessionTerminationSource.CompactionContinuationRecovery,
                    "the exit is attributed to the recovery, never to an operator Stop");
        }

        // The real chain, on the RESUMED generation: a real check run, a real interpretation
        // dispatched to the restarted seat, and the note it produced typed into the caller.
        var taskId = await SeedCheckedDelegateAsync(h);
        await using (var db = h.CreateContext())
        {
            // A delegate a check is being taken on has already been checked once by the time the
            // sweep reaches it; the publication is keyed on that check number, so state it.
            await db.AgentTasks.Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.CheckCount, 1));
        }

        // No swallowed submit on either leg here: the stranded-brief recovery has its own cases in
        // this class, and the subject of this one is whether the RESTART produces a reading at all.
        var run = Task.Run(() => h.Resolve<AgentTaskCheckService>().RunCheckAsync(taskId, CancellationToken.None));
        var interpretation = await WaitForInterpretationAsync(h, stalled.PriorCheckTaskId);
        await h.Resolve<AgentTaskDispatcher>().TickAsync(CancellationToken.None);

        await using (var db = h.CreateContext())
        {
            var placed = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == interpretation.Id);
            placed.Status.ShouldBe(AgentTaskStatus.Dispatched, "the real dispatcher placed it");
            placed.AgentSessionId.ShouldBe(h.InterpreterSessionId,
                "on the RESTARTED seat's session - no second session was launched");
        }

        await AssertRestartedBriefArrivedWholeAsync(h, interpretation.Id);
        await SettleInterpretationAsync(h, interpretation.Id, Reading);
        (await run).ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        Guid notificationId;
        await using (var db = h.CreateContext())
        {
            var publication = await db.LegacyCheckNotePublications.SingleAsync();
            publication.State.ShouldBe(LegacyCheckNoteState.Produced,
                "the restart's own note is published against the episode, not enqueued blind");
            publication.InterpreterAcceptedStartedAt.ShouldBe(resume.Generation2);
            notificationId = publication.NotificationId;
        }

        await h.Resolve<AgentTaskLandNotificationService>().ReconcileAsync(notificationId, CancellationToken.None);
        await h.H.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        await AssertNoteArrivedWholeAsync(h, taskId, carries: Reading);

        // Sweep 3: the new useful Check plus the whole caller receipt is what closes it. Time
        // alone never does, and neither does either half on its own.
        await using (var db = h.CreateContext())
            await CompactionService(db, runner, resume).SweepAsync(CancellationToken.None);

        await using var verify = h.CreateContext();
        var closed = await verify.CheckCompactionRecoveries.SingleAsync();
        closed.State.ShouldBe(CheckCompactionRecoveryState.Recovered);
        closed.UsefulCheckTaskId.ShouldNotBeNull();
        closed.ConfirmingPromptSequence.ShouldNotBeNull();
        var supervision = await verify.AgentSupervisionStates.SingleAsync(s => s.AgentId == h.InterpreterAgentId);
        supervision.CompactionRestartReceiptEligible.ShouldBeTrue(
            "only a delivered reading earns the next automatic restart");
        supervision.ActiveCompactionRecoveryId.ShouldBeNull();
        runner.CompactionStops.Count.ShouldBe(1, "one restart, not a loop");
    }

    /// <summary>
    /// The brief leg, asserted against a seat whose transcript already carries the stalled turn
    /// this episode is about: exactly ONE prompt in the whole session carries the new
    /// interpretation's marker, and it is that brief whole rather than a fragment of it.
    /// </summary>
    private static async Task AssertRestartedBriefArrivedWholeAsync(Handoff h, Guid interpretationId)
    {
        await using var db = h.CreateContext();
        var brief = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == h.InterpreterSessionId)
            .SingleAsync();
        brief.Status.ShouldBe(QueuedMessageStatus.Sent);
        brief.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        brief.Body.ShouldContain(DelegationReportFormatter.TaskMarker(interpretationId));

        var carrying = (await h.PromptsAsync(h.InterpreterSessionId))
            .Where(text => text.Contains(DelegationReportFormatter.TaskMarker(interpretationId), StringComparison.Ordinal))
            .ToList();
        carrying.Count.ShouldBe(1, "one whole brief, once, and nothing riding with it");
        carrying[0].ReplaceLineEndings("\n").Trim()
            .ShouldBe(brief.Body.ReplaceLineEndings("\n").Trim());
    }

    private static CheckCompactionContinuationService CompactionService(
        AppDbContext db, FakeSessionRunnerClient runner, ICompactionContinuationResume resume) =>
        new(db, TimeProvider.System,
            Options.Create(new DelegationSettings { CheckInterpreterEnabled = true }),
            new CheckCompactionContinuationGate(),
            NullLogger<CheckCompactionContinuationService>.Instance,
            runner, resume);

    private static FakeSessionRunnerClient StalledRunner(Guid sessionId, DateTime generation) => new()
    {
        AdvertiseCompactionStop = true,
        GetOverride = (id, _) => Task.FromResult(new SessionRunnerSessionDto(
            id, 1, generation, "Running", null, AgentExitReason.Unknown, 1,
            AcceptedStartedAt: generation)),
        CompactionObservation = new CompactionTailObservation(
            CompactionObservationStatuses.Success, true, 64, "bind-1", 2, 2, "boundary-1", "cont-1"),
        CompactionStopResultFor = request => new CompactionContinuationStopResult(
            sessionId, request.AttemptId, true, CompactionStopOutcomes.Exited, generation),
    };

    /// <summary>
    /// Puts the live interpreter session into the exact CARD-0079 condition: its own correlated
    /// Check prompt, an explicit <c>(auto)</c> boundary, the synthetic continuation, and nothing
    /// afterwards for longer than the ten-minute bound. Written directly because the shape is
    /// entirely about timestamps, and the harness insert stamps CreatedAt as now.
    /// </summary>
    private static async Task<StalledSeat> StallTheInterpreterOnCompactionAsync(Handoff h)
    {
        await using var db = h.CreateContext();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-25));
        var promptAt = generation.AddMinutes(1);
        var priorCheckId = Guid.NewGuid();

        var agent = await db.Agents.SingleAsync(a => a.Id == h.InterpreterAgentId);
        agent.Kind = AgentKind.ClaudeCode;
        agent.AlwaysOn = true;
        agent.Status = AgentStatus.Running;
        agent.StandingSpecialistRole = AgentTaskRole.Check;
        agent.StandingSpecialistOwnerId = agent.Id;

        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.InterpreterSessionId);
        session.StandingAgentId = agent.Id;
        session.AgentKind = AgentKind.ClaudeCode;
        session.Status = SessionStatus.Running;
        session.CreatedAt = generation;
        session.StartedAt = generation;

        // The standing seat's one ended turn predates the generation, so it cannot be mistaken for
        // activity after the boundary.
        foreach (var existing in await db.TranscriptEntries
            .Where(t => t.AgentSessionId == h.InterpreterSessionId).ToListAsync())
        {
            existing.Timestamp = generation;
            existing.CreatedAt = generation;
        }

        db.AgentSupervisionStates.Add(new AgentSupervisionState
        {
            AgentId = agent.Id,
            UpdatedAt = DateTime.UtcNow,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = priorCheckId,
            RootTaskId = priorCheckId,
            Title = "read",
            Goal = "interpret",
            Role = AgentTaskRole.Check,
            Kind = AgentTaskKind.Worker,
            Status = AgentTaskStatus.Succeeded,
            Result = "the reading that never came back",
            AgentId = agent.Id,
            AgentSessionId = h.InterpreterSessionId,
            WorkingDirectory = Path.GetTempPath(),
            CreatedAt = promptAt,
            CompletedAt = promptAt,
        });

        var next = (await db.TranscriptEntries
            .Where(t => t.AgentSessionId == h.InterpreterSessionId)
            .MaxAsync(t => (long?)t.Sequence)) ?? 0;
        db.TranscriptEntries.AddRange(
            Row(h.InterpreterSessionId, ++next, TranscriptKinds.UserPrompt,
                "check " + priorCheckId.ToString("D"), promptAt),
            Row(h.InterpreterSessionId, ++next, TranscriptKinds.CompactBoundary,
                "Context compacted (auto)", promptAt.AddSeconds(3), "boundary-1"),
            Row(h.InterpreterSessionId, ++next, TranscriptKinds.UserPrompt,
                TranscriptKinds.CompactionContinuationPromptPrefix + " summary",
                promptAt.AddSeconds(4), "cont-1"));
        await db.SaveChangesAsync();
        return new StalledSeat(generation, priorCheckId);
    }

    private static TranscriptEntry Row(
        Guid sessionId, long sequence, string kind, string? text, DateTime at, string? uuid = null) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = kind,
        Text = text,
        Timestamp = at,
        CreatedAt = at,
        Uuid = uuid,
    };

    private sealed record StalledSeat(DateTime Generation, Guid PriorCheckTaskId);

    /// <summary>
    /// Stands in for the launch only. It writes what a launch leaves behind — the same session id
    /// running at a second accepted generation — so the episode's own Stopped-to-ResumeReserved
    /// transition and everything downstream of it run for real.
    /// </summary>
    private sealed class GenerationAdvancingResume(string connectionString, Guid sessionId, DateTime generation)
        : ICompactionContinuationResume
    {
        public DateTime Generation2 { get; } = SessionGeneration.Next(generation, DateTime.UtcNow);

        public int Calls { get; private set; }

        public async Task<CompactionResumeResult> ResumeAsync(Guid episodeId, CancellationToken ct)
        {
            Calls++;
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
            var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId, ct);
            session.StartedAt = Generation2;
            session.Status = SessionStatus.Running;
            session.EndedAt = null;
            session.LastSeenAt = DateTime.UtcNow;
            // A relaunch writes the restart boundary, and that boundary is what ends the turn the
            // old process died inside. Without it the resumed seat still reads Working and no new
            // brief could ever be typed into it.
            var next = ((await db.TranscriptEntries
                .Where(t => t.AgentSessionId == sessionId)
                .MaxAsync(t => (long?)t.Sequence, ct)) ?? 0) + 1;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = next,
                Kind = TranscriptKinds.SessionRestartBoundary,
                Timestamp = Generation2,
                CreatedAt = Generation2,
            });
            await db.SaveChangesAsync(ct);
            return new CompactionResumeResult(true, sessionId, Generation2, "reserved");
        }
    }
}
