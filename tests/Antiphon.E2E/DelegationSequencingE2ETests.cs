using Antiphon.Agents.Pty;
using Antiphon.E2E.Fixtures;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.E2E;

/// <summary>
/// The headline end-to-end: a REAL Claude acting as an orchestrator, driving TWO real Claude
/// delegates in sequence against a locally running Antiphon, where the second delegate's work
/// depends on the first's.
///
/// Sequencing is the point. Delegate 1 writes a token into a file; delegate 2 reads that file back.
/// If the second report contains the token, then: the delegates genuinely shared a workspace, the
/// first one's work really happened before the second started, and the orchestrator drove both.
/// Nothing about that can pass by accident.
///
/// Substituted seam: spawning a delegate's pty and typing queued messages into a live terminal are
/// the session-runner daemon's jobs, and no daemon exists inside a WebApplicationFactory test.
/// <see cref="TestSessionRunner"/> plays that role — it launches exactly the Claude the dispatcher
/// asked for, in the directory it asked for, feeds it the brief the SERVER composed, ingests the
/// resulting transcript, and types delivered notes into the orchestrator's terminal. Everything
/// either side of that seam is real: the skill, the HTTP callback, task creation, tier resolution,
/// dispatch, marker correlation, settlement, and note delivery.
///
/// Opt-in headed: ANTIPHON_HEADED_TESTS=1 + claude on PATH; self-skips otherwise.
/// </summary>
[Category("Headed")]
[Category("HeadedCanary")]
[NotInParallel("Headed")]
public class DelegationSequencingE2ETests
{
    private const string StepOneToken = "ANTIPHON-STEP-ONE-OK";

    [Test]
    public async Task An_orchestrator_runs_two_delegates_in_sequence_and_the_second_sees_the_firsts_work()
    {
        ClaudeHarness.SkipIfNotEligible();

        var fixture = new AntiphonAppFixture();
        await fixture.InitializeAsync();
        using var repo = new DelegationScratchRepo("antiphon-sequencing-e2e");
        try
        {
            var runner = new TestSessionRunner(fixture);
            runner.AllowRoot(repo.Path);

            var orchestratorSessionId = await runner.SeedSessionAsync(repo.Path);
            var rootTaskId = await runner.SeedOrchestratorTaskAsync(repo.Path, orchestratorSessionId);

            await using var orchestrator = await ClaudeHarness.StartAsync(
                repo.Path,
                runner.EnvFor(orchestratorSessionId, rootTaskId, AgentTaskService.RawTokens[rootTaskId]),
                model: "sonnet");

            // ---- step 1: the orchestrator delegates the first piece --------------------------
            // The invocation is spelled out because what this test is proving is the SEQUENCING and
            // the round trip, not the model's ability to pick a role from prose — that is the
            // separate (and inherently less deterministic) classification canary.
            // ONE LINE on purpose. A multi-line body leaves the composer in a state where the
            // submitting carriage return does not reliably send, and the instruction sits there
            // unsent — the failure looks like the model ignoring you, minutes later.
            await orchestrator.SubmitAsync(
                "You are orchestrating: do not do any work yourself and do not read or write any files. "
                + "Run exactly this command once, then stop and end your turn: "
                + $"pwsh -NoProfile -File scripts/delegate.ps1 -Role Docs -Goal \"Create a file named "
                + $"step1.txt in the current directory whose entire contents are the single line "
                + $"{StepOneToken} then report what you did\"");

            var first = await runner.WaitForNewTaskAsync(rootTaskId, seen: [], timeout: TimeSpan.FromMinutes(5));
            first.ShouldNotBeNull(
                "the orchestrator must reach the API through the skill. "
                + Diagnose(orchestrator));
            Console.WriteLine($"[step 1] kind={first!.Kind} role={first.Role} tier={first.ModelLevel}");

            var firstReport = await runner.RunTaskAsync(first.Id);
            firstReport.ShouldNotBeNullOrWhiteSpace("delegate 1 must produce a report");

            // The delegate really did work in the shared directory — the whole reason Shared is the
            // default, and the precondition for delegate 2 being able to see it.
            var stepOneFile = Path.Combine(repo.Path, "step1.txt");
            File.Exists(stepOneFile).ShouldBeTrue(
                $"delegate 1 must have written step1.txt into the shared workspace. Report:\n{firstReport}");
            (await File.ReadAllTextAsync(stepOneFile)).ShouldContain(StepOneToken);

            // ---- the report comes home, and the orchestrator sees it -------------------------
            var noteToOrchestrator = await runner.TakeNoteForAsync(orchestratorSessionId);
            noteToOrchestrator.ShouldNotBeNull("delegate 1's report must be queued for its caller");
            noteToOrchestrator!.ShouldContain(DelegationReportFormatter.Short(first.Id));

            await orchestrator.SubmitAsync(noteToOrchestrator);

            // ---- step 2: sequenced on the first --------------------------------------------
            await orchestrator.SubmitAsync(
                "Now delegate the follow-up, which depends on what that delegate produced. "
                + "Run exactly this command once, then stop and end your turn: "
                + "pwsh -NoProfile -File scripts/delegate.ps1 -Role Test -Goal \"Read the file "
                + "step1.txt in the current directory and report its exact contents verbatim\"");

            var second = await runner.WaitForNewTaskAsync(
                rootTaskId, seen: [first.Id], timeout: TimeSpan.FromMinutes(5));
            second.ShouldNotBeNull(
                "the orchestrator must delegate the second step. " + Diagnose(orchestrator));
            Console.WriteLine($"[step 2] kind={second!.Kind} role={second.Role} tier={second.ModelLevel}");

            // The tier must be the one OUR policy resolves for whatever role the model picked.
            // Asserting a specific role (and therefore a specific tier) would be asserting the
            // model's word choice, which drifts between runs — the ladder is ours to guarantee,
            // the role is the model's to choose.
            await runner.AssertTierMatchesPolicyAsync(first);
            await runner.AssertTierMatchesPolicyAsync(second);

            var secondReport = await runner.RunTaskAsync(second.Id);

            // ---- THE assertion --------------------------------------------------------------
            // Delegate 2 read what delegate 1 wrote. That single fact proves the sequence really
            // ran in order, in one shared workspace, driven by one orchestrator.
            secondReport.Contains(StepOneToken, StringComparison.Ordinal).ShouldBeTrue(
                "delegate 2 must report the contents delegate 1 wrote — this is the sequencing proof. "
                + $"Report was:\n{secondReport}");

            // ---- final state ----------------------------------------------------------------
            var all = await runner.GetRunAsync(rootTaskId);
            all.Count.ShouldBe(2, "exactly the two delegated tasks, both under one root");
            all.ShouldAllBe(t => t.ParentTaskId == rootTaskId);
            all.ShouldAllBe(t => t.Status == AgentTaskStatus.Succeeded);
            all.ShouldAllBe(t => t.Result != null);
            all.ShouldAllBe(t => t.AgentSessionId != null);
            all.Select(t => t.AgentId).Distinct().Count()
                .ShouldBe(2, "each task gets its own ephemeral agent, not one agent doing everything");
            all.ShouldAllBe(t => t.Workspace == WorkspaceMode.Shared);

            Console.WriteLine(
                $"Run complete: {all.Count} tasks, tiers "
                + string.Join(" -> ", all.OrderBy(t => t.CreatedAt).Select(t => t.ModelLevel)));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    /// <summary>
    /// Say WHY the orchestrator produced nothing. "Task was null" after a five-minute wait is a
    /// mystery; "it is sitting on a permission dialog" is a bug report.
    /// </summary>
    private static string Diagnose(ClaudeHarness session)
    {
        var blocked = session.BlockedOn();
        var reason = blocked is null
            ? "The TUI is not blocked on a dialog — it either never ran the command, or the command failed."
            : $"The TUI is BLOCKED on a [{blocked.Kind}] dialog: {blocked.Title}";
        return $"{reason}\nScreen:\n{session.Screen()}";
    }

    // =========================================================================================
    // A minimal stand-in for the session-runner daemon, scoped to what delegation needs.
    // =========================================================================================

    private sealed class TestSessionRunner(AntiphonAppFixture fixture)
    {
        public void AllowRoot(string root)
        {
            var settings = fixture.Services
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<DelegationSettings>>().Value;
            settings.AllowedRoots.Add(root);
            settings.ApiBaseUrl = fixture.BaseAddress;
        }

        public Dictionary<string, string> EnvFor(Guid sessionId, Guid taskId, string? token) =>
            DelegationScratchRepo.EnvFor(fixture.BaseAddress, sessionId, taskId, token);

        public async Task<Guid> SeedSessionAsync(string cwd)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var session = new AgentSession
            {
                Id = Guid.NewGuid(),
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = cwd,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            };
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            return session.Id;
        }

        /// <summary>The root orchestrator's own row — the source of its create-scoped token.</summary>
        public async Task<Guid> SeedOrchestratorTaskAsync(string cwd, Guid sessionId)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var created = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
                new Server.Application.Dtos.CreateAgentTaskRequest(
                    Goal: "Coordinate this run.",
                    Kind: AgentTaskKind.Orchestrator,
                    Role: AgentTaskRole.Plan),
                new AgentTaskService.Caller(null, null, cwd),
                CancellationToken.None);

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
            row.AgentSessionId = sessionId;
            row.Status = AgentTaskStatus.Working;
            await db.SaveChangesAsync();
            return created.Id;
        }

        public async Task<AgentTask?> WaitForNewTaskAsync(Guid parentTaskId, Guid[] seen, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                await using var scope = fixture.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var task = await db.AgentTasks.AsNoTracking()
                    .Where(t => t.ParentTaskId == parentTaskId && !seen.Contains(t.Id))
                    .OrderBy(t => t.CreatedAt)
                    .FirstOrDefaultAsync();
                if (task is not null)
                    return task;
                await Task.Delay(2_000);
            }
            return null;
        }

        /// <summary>
        /// Dispatch the task, run the real Claude the dispatcher asked for, and feed its report back
        /// through the reply path. Returns the report.
        /// </summary>
        public async Task<string> RunTaskAsync(Guid taskId)
        {
            var task = await TickUntilDispatchedAsync(taskId);
            var sessionId = task.AgentSessionId
                ?? throw new InvalidOperationException("dispatch produced no session");

            var brief = await ReadBriefAsync(sessionId);
            brief.ShouldContain(DelegationReportFormatter.TaskMarker(taskId));

            await using var session = await ClaudeHarness.StartAsync(
                task.WorkingDirectory,
                EnvFor(sessionId, taskId, token: null),
                model: ModelLevelAliases.ForClaude(task.ModelLevel));

            await session.SubmitAsync(brief);

            // The transcript decides whether the turn ended and what was said; the screen only
            // corroborates. Screen-scraping alone cannot do either job: the viewport truncates a
            // long report, and the status line animates forever so "settled" never arrives.
            var turn = await session.WaitForTurnEndAsync();
            turn.Completed.ShouldBeTrue(
                $"delegate for task {DelegationReportFormatter.Short(taskId)} must finish a turn "
                + $"(transcript: {turn.TranscriptPath ?? "not found"}).\nScreen:\n{session.Screen()}");
            if (!turn.ScreenCorroborated)
            {
                Console.WriteLine(
                    $"[warn] task {DelegationReportFormatter.Short(taskId)}: the transcript reported a "
                    + "completed turn but the screen still looked busy — check if this persists.");
            }

            var report = turn.Text;
            await IngestTurnAsync(sessionId, brief, report);
            await fixture.Services.GetRequiredService<AgentTaskReplyService>()
                .OnTurnEndAsync(sessionId, CancellationToken.None);
            return report;
        }

        private async Task<AgentTask> TickUntilDispatchedAsync(Guid taskId)
        {
            // The hosted dispatcher may beat us to it; ticking is idempotent thanks to the
            // transactional claim, so this just removes the timing dependency.
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await using var scope = fixture.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                    .TickAsync(CancellationToken.None);

                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                if (task.AgentSessionId is not null)
                {
                    task.Status.ShouldBe(
                        AgentTaskStatus.Dispatched,
                        $"FailureReason: {task.FailureReason ?? "(none)"}");
                    return task;
                }
                if (task.Status is AgentTaskStatus.Failed or AgentTaskStatus.Blocked)
                    throw new InvalidOperationException($"task did not dispatch: {task.FailureReason}");
                await Task.Delay(1_000);
            }
            throw new InvalidOperationException("task never reached Dispatched");
        }

        private async Task<string> ReadBriefAsync(Guid sessionId)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == sessionId)
                .OrderBy(m => m.Sequence)
                .FirstOrDefaultAsync();
            message.ShouldNotBeNull("the dispatcher queues the brief through the message queue");
            return message!.Body;
        }

        /// <summary>
        /// Take the oldest undelivered note for a session and mark it Sent — the runner's job when
        /// it types a queued message into a live terminal.
        /// </summary>
        public async Task<string?> TakeNoteForAsync(Guid sessionId)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == sessionId
                    && m.Origin == QueuedMessageOrigin.Delegation
                    && m.Status == QueuedMessageStatus.Pending)
                .OrderBy(m => m.Sequence)
                .FirstOrDefaultAsync();
            if (message is null)
                return null;

            message.Status = QueuedMessageStatus.Sent;
            message.SentAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return message.Body;
        }

        /// <summary>
        /// The tier a task was dispatched at must be exactly what the role policy resolves — that
        /// mapping is the product's promise, independent of which role the model chose.
        /// </summary>
        public async Task AssertTierMatchesPolicyAsync(AgentTask task)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var expected = scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .ResolveLevel(task.Kind, task.Role, explicitLevel: null);

            task.ModelLevel.ShouldBe(
                expected,
                $"a {task.Role} task must run at the tier the policy defines for {task.Role}");
        }

        public async Task<List<AgentTask>> GetRunAsync(Guid rootTaskId)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.AgentTasks.AsNoTracking()
                .Where(t => t.ParentTaskId == rootTaskId)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync();
        }

        private async Task IngestTurnAsync(Guid sessionId, string prompt, string report)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seq = await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId)
                .MaxAsync(t => (long?)t.Sequence) ?? 0;

            db.TranscriptEntries.Add(Entry(sessionId, ++seq, TranscriptKinds.UserPrompt, prompt));
            db.TranscriptEntries.Add(Entry(sessionId, ++seq, TranscriptKinds.AssistantText, report));
            var end = Entry(sessionId, ++seq, TranscriptKinds.TurnEnd, null);
            end.StopReason = "end_turn";
            db.TranscriptEntries.Add(end);
            await db.SaveChangesAsync();
        }

        private static TranscriptEntry Entry(Guid sessionId, long sequence, string kind, string? text) => new()
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = sequence,
            Kind = kind,
            Text = text,
            CreatedAt = DateTime.UtcNow,
        };
    }
}
