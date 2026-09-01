using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0021 slice 1 — the sweep that ACTS on a dead session. Detection of the state already
/// existed (the attention projection's DeadSession row); nothing failed the task, so on 2026-08-09
/// three tasks sat Dispatched for hours behind sessions that had been gone the whole time. Two of
/// those shapes are structurally invisible to the delivery watchdog: a <b>Working</b> task is
/// outside its query entirely, and a Dispatched task whose session wrote a transcript before dying
/// passes its "did it start" test forever.
///
/// <para><b>The constraint that outranks the feature</b> is CARD-0056: a row reading Failed was once
/// wrong about a perfectly healthy session — the operator's own. So the sweep never kills anything,
/// and it needs two independent pieces of evidence before it will even write Failed on a task: the
/// runner must ANSWER, and it must not list the session Running. The test that matters most in this
/// file is <c>a_session_the_runner_still_serves_is_left_alone</c>.</para>
///
/// <para><b>Shared-database discipline.</b> This is a FLEET-GLOBAL sweep over one Postgres shared by
/// the whole assembly, so the class takes <c>[NotInParallel]</c> with NO group key (a key would
/// serialise it only against itself) and every assertion is scoped to rows the test itself created.
/// The fake runner goes further and reports every session the database knows about as Running
/// EXCEPT the ones a test declares gone — so the sweep is structurally unable to fail another
/// suite's row, which a bare empty session list would happily have done.</para>
/// </summary>
[Category("Integration")]
[NotInParallel]
public class AgentTaskDeadSessionReconciliationTests
{
    // ---- 1-5: the five ways a session is dead ----------------------------------------------------

    [Test]
    public async Task a_dispatched_task_behind_a_failed_session_is_failed_with_the_sessions_own_reason()
    {
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(
            AgentTaskStatus.Dispatched, SessionStatus.Failed, failureReason: "the pty-host exited (code 1)");
        var harness = scenario.Harness(task.SessionId);

        await scenario.PastGraceAsync(harness);

        var failed = await scenario.ReadTaskAsync(task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldNotBeNull();
        failed.FailureReason.ShouldContain(
            "the pty-host exited (code 1)",
            customMessage: "the session's own reason is the evidence — the sweep must carry it, not restate it");
        failed.FailureReason.ShouldContain(
            task.SessionId.ToString(), customMessage: "say where to read what actually happened");

        harness.Stopper.Killed.ShouldBeEmpty(
            "THE SWEEP NEVER KILLS — the session is dead by evidence, and if that evidence were "
            + "wrong a kill would be the CARD-0056 disaster");
        harness.Runner.Killed.ShouldBeEmpty("and not through the runner client either");

        (await scenario.ParentNoteBodiesAsync())
            .ShouldContain(
                b => b.Contains("the pty-host exited (code 1)"),
                "the caller must HEAR about the death, not discover it");

        await using var verify = CreateContext();
        (await verify.Agents.AnyAsync(a => a.Id == task.AgentId))
            .ShouldBeFalse("the ephemeral delegate is cleaned up, exactly as the watchdog's tail does");
    }

    /// <summary>
    /// The case <c>FailNeverStartedAsync</c> structurally cannot reach: its query is
    /// <c>Status == Dispatched</c>, so a task that got as far as Working and then lost its session
    /// was never a candidate for any automatic settlement at all.
    /// </summary>
    [Test]
    public async Task a_working_task_behind_a_dead_session_is_failed_too()
    {
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(
            AgentTaskStatus.Working, SessionStatus.Failed, failureReason: "process vanished");
        var harness = scenario.Harness(task.SessionId);

        await scenario.PastGraceAsync(harness);

        (await scenario.ReadTaskAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Failed);
    }

    [Test]
    public async Task a_task_whose_session_row_is_gone_is_failed_and_the_reason_says_so()
    {
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(AgentTaskStatus.Dispatched, SessionStatus.Running);
        await scenario.DeleteSessionRowAsync(task.SessionId);
        var harness = scenario.Harness(task.SessionId);

        await scenario.PastGraceAsync(harness);

        var failed = await scenario.ReadTaskAsync(task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldContain("session row is gone");
    }

    [Test]
    public async Task a_session_that_ended_while_still_marked_running_is_dead_for_the_task()
    {
        // The row disagrees with itself. Lockstep with the attention projection, which has always
        // counted this: something closed the session without writing its status.
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(
            AgentTaskStatus.Dispatched, SessionStatus.Running, endedMinutesAgo: 6);
        var harness = scenario.Harness(task.SessionId);

        await scenario.PastGraceAsync(harness);

        var failed = await scenario.ReadTaskAsync(task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldContain("while still marked Running");
    }

    [Test]
    public async Task a_stopped_empty_session_without_an_operator_source_is_StoppedBeforeFirstPrompt()
    {
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(
            AgentTaskStatus.Dispatched, SessionStatus.Stopped,
            agentKind: AgentKind.Grok);
        var harness = scenario.Harness(task.SessionId);

        await scenario.PastGraceAsync(harness);

        var failed = await scenario.ReadTaskAsync(task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureCode.ShouldBe(AgentTaskFailureCode.StoppedBeforeFirstPrompt);
        failed.FailureReason.ShouldNotBeNull();
        failed.FailureReason.ShouldContain("StoppedBeforeFirstPrompt");
        failed.FailureReason.ShouldNotContain("operator");
        failed.FailureReason.ShouldContain(task.SessionId.ToString());
        harness.Stopper.Killed.ShouldBeEmpty("THE SWEEP NEVER KILLS");

        (await scenario.ParentNoteBodiesAsync())
            .ShouldContain(b => b.Contains("StoppedBeforeFirstPrompt"));
    }

    [Test]
    public async Task a_stopped_empty_session_with_OperatorRequest_names_the_operator_source()
    {
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(
            AgentTaskStatus.Dispatched, SessionStatus.Stopped,
            terminationSource: SessionTerminationSource.OperatorRequest);
        var harness = scenario.Harness(task.SessionId);

        await scenario.PastGraceAsync(harness);

        var failed = await scenario.ReadTaskAsync(task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureCode.ShouldBeNull();
        failed.FailureReason.ShouldNotBeNull();
        failed.FailureReason.ShouldContain("operator request");
        failed.FailureReason.ShouldNotContain("StoppedBeforeFirstPrompt");
    }

    [Test]
    public async Task a_clean_process_exit_is_not_promoted_to_an_operator_stop()
    {
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(
            AgentTaskStatus.Dispatched, SessionStatus.Stopped,
            terminationSource: SessionTerminationSource.ProcessExit);
        var harness = scenario.Harness(task.SessionId);

        await scenario.PastGraceAsync(harness);

        var failed = await scenario.ReadTaskAsync(task.Id);
        failed.FailureCode.ShouldBe(AgentTaskFailureCode.StoppedBeforeFirstPrompt);
        failed.FailureReason.ShouldNotContain("operator");
    }

    [Test]
    public async Task a_legacy_unknown_stop_is_not_promoted_to_an_operator_stop()
    {
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(
            AgentTaskStatus.Dispatched, SessionStatus.Stopped,
            terminationSource: SessionTerminationSource.Unknown);
        var harness = scenario.Harness(task.SessionId);

        await scenario.PastGraceAsync(harness);

        var failed = await scenario.ReadTaskAsync(task.Id);
        failed.FailureCode.ShouldBe(AgentTaskFailureCode.StoppedBeforeFirstPrompt);
        failed.FailureReason.ShouldNotContain("operator");
    }

    // ---- 6-8: the evidence gates -----------------------------------------------------------------

    /// <summary>
    /// THE test. Row dead, process alive — the exact false-Failed shape CARD-0056 was written about,
    /// where a healthy session (the operator's own) was marked Failed by a launch-verification false
    /// positive. Reconciliation's third pass re-adopts such a session; this sweep must not settle the
    /// task under it in the meantime.
    /// </summary>
    [Test]
    public async Task a_session_the_runner_still_serves_is_left_alone()
    {
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(AgentTaskStatus.Dispatched, SessionStatus.Failed);
        // Nothing is declared gone, so the fake reports this session Running — as the real runner
        // would about a process it is still serving.
        var harness = scenario.Harness();

        await scenario.PastGraceAsync(harness);

        (await scenario.ReadTaskAsync(task.Id)).Status.ShouldBe(
            AgentTaskStatus.Dispatched, "a live process outranks a dead row, always");
        harness.FirstSeen.IsTracking(task.Id).ShouldBeFalse(
            "and the grace is dropped, so a genuine death later starts its own window");
    }

    [Test]
    public async Task an_unreachable_runner_settles_nothing_and_throws_nothing()
    {
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(AgentTaskStatus.Dispatched, SessionStatus.Failed);
        var harness = scenario.Harness(task.SessionId);
        harness.Runner.ListError = new HttpRequestException("connection refused");

        // An unanswerable runner is no evidence of anything — the same doctrine reconciliation runs
        // on. The task has already waited minutes; another 5 s tick costs nothing.
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        (await harness.Dispatcher.FailDeadSessionTasksAsync(CancellationToken.None)).ShouldBe(0);

        (await scenario.ReadTaskAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
    }

    [Test]
    public async Task the_grace_has_to_elapse_before_anything_is_failed()
    {
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(AgentTaskStatus.Dispatched, SessionStatus.Failed);
        var harness = scenario.Harness(task.SessionId);

        await harness.Dispatcher.FailDeadSessionTasksAsync(CancellationToken.None);
        (await scenario.ReadTaskAsync(task.Id)).Status.ShouldBe(
            AgentTaskStatus.Dispatched, "the first observation only starts the clock");

        harness.Clock.Advance(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(1));
        await harness.Dispatcher.FailDeadSessionTasksAsync(CancellationToken.None);

        (await scenario.ReadTaskAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Failed);
    }

    // ---- 9-10: what the sweep must never touch ---------------------------------------------------

    [Test]
    public async Task a_task_whose_session_recovers_inside_the_grace_never_fails()
    {
        // The whole reason the grace exists: reconciliation re-adopting a wrongly-Failed session
        // flips the row back to Running, and the burned grace must go with it.
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(AgentTaskStatus.Dispatched, SessionStatus.Failed);
        var harness = scenario.Harness(task.SessionId);

        await harness.Dispatcher.FailDeadSessionTasksAsync(CancellationToken.None);
        harness.FirstSeen.IsTracking(task.Id).ShouldBeTrue("first observation recorded");

        await scenario.SetSessionStatusAsync(task.SessionId, SessionStatus.Running);
        harness.Runner.Gone.Clear();
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        await harness.Dispatcher.FailDeadSessionTasksAsync(CancellationToken.None);

        (await scenario.ReadTaskAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        harness.FirstSeen.IsTracking(task.Id).ShouldBeFalse("a recovered task is evicted from the clock");
    }

    [Test]
    public async Task a_check_task_on_a_dead_session_is_failed_with_no_parent_note()
    {
        // CARD-0079: a zombie Dispatched check on a dead previous session occupied the standing
        // interpreter for two days. ReplyTo=None already suppresses a completion note; the sweep
        // still never kills, and RemoveEphemeralAgentAsync only deletes pool delegates.
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(
            AgentTaskStatus.Dispatched, SessionStatus.Failed,
            role: AgentTaskRole.Check,
            replyTo: AgentTaskReplyTo.None,
            isPoolDelegate: false);
        var harness = scenario.Harness(task.SessionId);

        await scenario.PastGraceAsync(harness);

        (await scenario.ReadTaskAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Failed);
        harness.Stopper.Killed.ShouldBeEmpty(
            "THE SWEEP NEVER KILLS — failing the task is enough; a kill would be CARD-0056");
        harness.Runner.Killed.ShouldBeEmpty("and not through the runner client either");
        (await scenario.ParentNoteBodiesAsync())
            .ShouldBeEmpty("ReplyTo=None already suppresses a completion note");

        await using var verify = CreateContext();
        (await verify.Agents.AnyAsync(a => a.Id == task.AgentId))
            .ShouldBeTrue("the standing specialist is not a pool delegate and must stay");
    }

    // ---- CARD-0288 S4: dead-session Fail must not overwrite a sitting report ---------------------

    [Test]
    public async Task a_stopped_session_with_a_marked_done_report_succeeds_instead_of_failing()
    {
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(AgentTaskStatus.Dispatched, SessionStatus.Stopped);
        await scenario.AddMarkedReportAsync(task.SessionId, task.Id);
        var harness = scenario.Harness(task.SessionId);

        await scenario.PastGraceAsync(harness);

        var settled = await scenario.ReadTaskAsync(task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
        harness.Runner.Killed.ShouldBeEmpty(
            "the sweep never kills through the runner; settlement may retire an already-stopped pool delegate");

        var notes = await scenario.ParentNoteBodiesAsync();
        notes.ShouldContain(b => b.Contains("Shipped the work", StringComparison.Ordinal));
        notes.ShouldNotContain(b => b.Contains("StoppedBeforeFirstPrompt", StringComparison.Ordinal));
        notes.ShouldNotContain(b => b.Contains("dead-session", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task a_stopped_session_with_transcript_but_no_report_still_fails()
    {
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(
            AgentTaskStatus.Dispatched, SessionStatus.Stopped, failureReason: "the pty-host exited (code 1)");
        await scenario.AddTranscriptNoiseAsync(task.SessionId);
        var harness = scenario.Harness(task.SessionId);

        await scenario.PastGraceAsync(harness);

        var failed = await scenario.ReadTaskAsync(task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldNotBeNull();
        harness.Stopper.Killed.ShouldBeEmpty("THE SWEEP NEVER KILLS");
    }

    [Test]
    public async Task a_session_the_runner_still_serves_does_not_attempt_settlement()
    {
        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(AgentTaskStatus.Dispatched, SessionStatus.Failed);
        await scenario.AddMarkedReportAsync(task.SessionId, task.Id);
        var harness = scenario.Harness();

        await scenario.PastGraceAsync(harness);

        (await scenario.ReadTaskAsync(task.Id)).Status.ShouldBe(
            AgentTaskStatus.Dispatched, "a live process outranks a dead row, and settlement is not attempted");
    }

    // ---- CARD-0085: same recovery gate on the dead-session sweep ---------------------------------

    [Test]
    public async Task a_dead_session_with_zero_transcript_and_a_worktree_commit_recovers_without_killing()
    {
        using var repo = new ScratchGitRepo("card0085-dead-wt");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");

        var manager = new WorktreeManager(
            Options.Create(new GitSettings
            {
                WorktreeBasePath = repo.WorktreeRoot,
                WorktreeStaleAfterDays = 7,
                WorktreeJanitorIntervalHours = 24,
            }),
            TimeProvider.System,
            NullLogger<WorktreeManager>.Instance);
        var worktrees = new DelegationWorktreeService(
            manager,
            new GitService(NullLogger<GitService>.Instance),
            NullLogger<DelegationWorktreeService>.Instance,
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance));
        var draft = new AgentTask
        {
            Id = Guid.NewGuid(),
            RootTaskId = Guid.NewGuid(),
            Title = "CARD-0083 plan",
            Goal = "plan",
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repo.Path,
            RepoPath = repo.Path,
            MergeTargetRef = "feat/parent",
            CreatedAt = DateTime.UtcNow,
        };
        await worktrees.CreateForTaskAsync(draft, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(draft.WorktreePath!, "plan.md"), "the plan\n");
        (await ScratchGitRepo.GitInAsync(draft.WorktreePath!, "add", ".")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(draft.WorktreePath!, "commit", "-m", "docs: CARD-0083 plan")).Ok
            .ShouldBeTrue();
        var sha = (await ScratchGitRepo.GitInAsync(draft.WorktreePath!, "rev-parse", "--short", "HEAD"))
            .StdOut.Trim();

        await using var scenario = new Scenario();
        var task = await scenario.AddTaskAsync(
            AgentTaskStatus.Dispatched, SessionStatus.Failed, failureReason: "the pty-host exited (code 1)",
            workspace: WorkspaceMode.Worktree,
            workingDirectory: repo.Path,
            title: "CARD-0083 plan",
            worktreePath: draft.WorktreePath,
            worktreeBranch: draft.WorktreeBranch,
            mergeTargetRef: "feat/parent",
            repoPath: repo.Path,
            sessionCwd: draft.WorktreePath);
        var harness = scenario.Harness(task.SessionId);

        await scenario.PastGraceAsync(harness);

        var recovered = await scenario.ReadTaskAsync(task.Id);
        recovered.Status.ShouldBe(AgentTaskStatus.Succeeded);
        recovered.Result.ShouldContain(sha);
        harness.Stopper.Killed.ShouldBeEmpty("recovery never kills — CARD-0056, and this sweep already must not");
        harness.Runner.Killed.ShouldBeEmpty();

        await using var verify = CreateContext();
        (await verify.AgentIncidents.AnyAsync(
            i => i.AgentId == task.AgentId && i.Kind == AgentIncidentKind.DelegateBindRefusalRecovered))
            .ShouldBeTrue();
        (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == task.SessionId))
            .ShouldBe(0);
    }

    // ---- 11: the lockstep ------------------------------------------------------------------------

    /// <summary>
    /// One definition of "dead", two consumers. The attention projection SURFACES the state and the
    /// dispatcher's sweep ACTS on it; a row shown but never failed — or worse, failed but never
    /// shown — would be a defect with no single place to fix it. This repo already carries three
    /// lockstep implementations of the working/idle rule and every drift between them has cost a
    /// real incident, so the shared predicate gets its own pin.
    ///
    /// <para>Read-only: it drives <c>AttentionService.GetAsync</c> and the pure predicate, never the
    /// sweep, so nothing here writes to another suite's rows.</para>
    /// </summary>
    [Test]
    public async Task the_predicate_and_the_attention_projection_agree_on_every_case()
    {
        await using var scenario = new Scenario();

        (SessionStatus Status, int? EndedMinutesAgo, bool Dead, string Case)[] table =
        [
            (SessionStatus.Created, null, false, "Created — it has not started yet, not died"),
            (SessionStatus.Starting, null, false, "Starting"),
            (SessionStatus.Running, null, false, "Running"),
            (SessionStatus.Stopping, null, false, "Stopping — on its way out, but a report may still land"),
            (SessionStatus.Stopped, null, true, "Stopped — terminal, origin unknown unless TerminationSource says otherwise"),
            (SessionStatus.Failed, null, true, "Failed"),
            (SessionStatus.Running, 6, true, "ended while still marked Running"),
        ];

        var seeded = new List<(Guid TaskId, bool Dead, string Case)>();
        foreach (var (status, ended, dead, name) in table)
        {
            var task = await scenario.AddTaskAsync(
                AgentTaskStatus.Dispatched, status, endedMinutesAgo: ended);
            var session = await scenario.ReadSessionSnapshotAsync(task.SessionId);

            AgentTaskLiveness.IsDeadSession(task.SessionId, session).ShouldBe(dead, name);
            seeded.Add((task.Id, dead, name));
        }

        // The two shapes with no session row to read.
        var orphan = await scenario.AddTaskAsync(AgentTaskStatus.Dispatched, SessionStatus.Running);
        await scenario.DeleteSessionRowAsync(orphan.SessionId);
        AgentTaskLiveness.IsDeadSession(orphan.SessionId, null).ShouldBeTrue("the session row is gone");
        seeded.Add((orphan.Id, true, "session row gone"));

        var sessionless = await scenario.AddTaskAsync(
            AgentTaskStatus.Dispatched, SessionStatus.Running, detachSession: true);
        AgentTaskLiveness.IsDeadSession(null, null).ShouldBeTrue("a dispatch always writes a session id");
        seeded.Add((sessionless.Id, true, "no session at all"));

        var items = await scenario.AttentionItemsAsync();
        foreach (var (taskId, dead, name) in seeded)
        {
            items.Any(i => i.TaskId == taskId && i.Kind == AttentionKind.DeadSession)
                .ShouldBe(dead, $"attention and AgentTaskLiveness must agree: {name}");
        }
    }

    // ---- harness ---------------------------------------------------------------------------------

    private sealed record SeededTask(Guid Id, Guid SessionId, Guid AgentId);

    private sealed record Harness(
        AgentTaskDispatcher Dispatcher,
        FakeRunnerClient Runner,
        RecordingSessionStopper Stopper,
        DeadSessionFirstSeenState FirstSeen,
        FakeTimeProvider Clock);

    /// <summary>
    /// Seeds rows, remembers their ids, and deletes exactly those on dispose — the shared-database
    /// rule, mechanised the same way <c>AttentionServiceTests</c> does it.
    /// </summary>
    private sealed class Scenario : IAsyncDisposable
    {
        private readonly List<Guid> _tasks = [];
        private readonly List<Guid> _sessions = [];
        private readonly List<Guid> _agents = [];

        /// <summary>The one parent session every seeded task replies into, so the note has a home.</summary>
        public Guid ParentSessionId { get; } = Guid.NewGuid();

        private bool _parentSeeded;

        public Harness Harness(params Guid[] gone)
        {
            var stopper = new RecordingSessionStopper();
            var runner = new FakeRunnerClient();
            foreach (var id in gone)
                runner.Gone.Add(id);
            var firstSeen = new DeadSessionFirstSeenState();
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            // Default settings on purpose: DeadSessionFailGraceMinutes = 3 is the shipped window.
            services.AddSingleton(Options.Create(new DelegationSettings()));
            services.AddOptions<AgentRegistrySettings>();
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper>(stopper);
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddSingleton(Options.Create(new GitSettings
            {
                WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-deadsession-wt"),
            }));
            services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
            services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
            services.AddScoped<DelegationWorktreeService>();
            services.AddScoped<AgentTaskService>();
            services.AddSingleton<ISessionRunnerClient>(runner);
            services.AddSingleton(firstSeen);
            // CARD-0085: same recovery gate as the delivery watchdog. Empty projects root so Arm B
            // cannot scan the machine's real ~/.claude/projects during these fleet-global sweeps.
            services.AddSingleton<AgentTaskReplyService>();
            services.AddSingleton<GitWorkspaceService>();
            services.AddSingleton(Options.Create(new DelegateBindRefusalRecoverySettings
            {
                ClaudeProjectsRoot = Path.Combine(Path.GetTempPath(), "antiphon-deadsession-no-jsonl"),
            }));
            services.AddSingleton<DelegateBindRefusalRecovery>();
            services.AddScoped<AgentTaskDispatcher>();

            var provider = services.BuildServiceProvider();
            return new Harness(
                provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(),
                runner, stopper, firstSeen, clock);
        }

        /// <summary>Two sweeps with the grace elapsed between them: observe, wait, act.</summary>
        public async Task PastGraceAsync(Harness harness)
        {
            await harness.Dispatcher.FailDeadSessionTasksAsync(CancellationToken.None);
            harness.Clock.Advance(TimeSpan.FromMinutes(5));
            await harness.Dispatcher.FailDeadSessionTasksAsync(CancellationToken.None);
        }

        public async Task<SeededTask> AddTaskAsync(
            AgentTaskStatus status,
            SessionStatus sessionStatus,
            string? failureReason = null,
            int? endedMinutesAgo = null,
            AgentTaskRole role = AgentTaskRole.Code,
            AgentTaskReplyTo replyTo = AgentTaskReplyTo.Session,
            bool isPoolDelegate = true,
            bool detachSession = false,
            WorkspaceMode workspace = WorkspaceMode.Shared,
            string? workingDirectory = null,
            string? title = null,
            string? worktreePath = null,
            string? worktreeBranch = null,
            string? mergeTargetRef = null,
            string? repoPath = null,
            string? sessionCwd = null,
            AgentKind agentKind = AgentKind.ClaudeCode,
            SessionTerminationSource terminationSource = SessionTerminationSource.Unknown)
        {
            await EnsureParentSessionAsync();

            var sessionId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var agentName = $"dead-{agentId:N}"[..16];
            // Recent on purpose: the delivery watchdog's own 10-minute window must not be able to
            // reach these rows, so a failure here can only ever have come from the sweep under test.
            var dispatched = DateTime.UtcNow.AddMinutes(-1);
            var cwd = sessionCwd ?? workingDirectory ?? Path.GetTempPath();

            await using var db = CreateContext();
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "dead-session-test",
                AgentKind = agentKind,
                Status = sessionStatus,
                Cwd = cwd,
                Cols = 120,
                Rows = 30,
                CreatedAt = dispatched,
                StartedAt = dispatched,
                LastSeenAt = dispatched,
                EndedAt = endedMinutesAgo is { } ago ? DateTime.UtcNow.AddMinutes(-ago) : null,
                FailureReason = failureReason,
                TerminationSource = terminationSource,
            });
            db.Agents.Add(new Agent
            {
                Id = agentId,
                Name = agentName,
                Slug = agentName,
                WorkingDirectory = cwd,
                Details = "Dead-session reconciliation test delegate.",
                Status = AgentStatus.Running,
                ModelLevel = AgentModelLevel.High,
                IsPoolDelegate = isPoolDelegate,
                PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = dispatched,
                UpdatedAt = dispatched,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = title ?? "Dead session reconciliation test",
                Goal = "Do the thing.",
                Kind = AgentTaskKind.Worker,
                Role = role,
                AgentKind = agentKind,
                ModelLevel = AgentModelLevel.High,
                Workspace = workspace,
                WorkingDirectory = workingDirectory ?? Path.GetTempPath(),
                RepoPath = repoPath,
                WorktreePath = worktreePath,
                WorktreeBranch = worktreeBranch,
                MergeTargetRef = mergeTargetRef,
                AgentId = agentId,
                AgentName = agentName,
                AgentSessionId = detachSession ? null : sessionId,
                Status = status,
                ReplyTo = replyTo,
                ParentSessionId = ParentSessionId,
                CreatedAt = dispatched,
                DispatchedAt = dispatched,
            });
            await db.SaveChangesAsync();

            _sessions.Add(sessionId);
            _agents.Add(agentId);
            _tasks.Add(taskId);
            return new SeededTask(taskId, sessionId, agentId);
        }

        public async Task AddMarkedReportAsync(Guid sessionId, Guid taskId)
        {
            var at = DateTime.UtcNow.AddMinutes(-1);
            var apiCallId = $"msg_{Guid.NewGuid():N}";
            await using var db = CreateContext();
            db.TranscriptEntries.AddRange(
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 1,
                    Kind = TranscriptKinds.UserPrompt,
                    Uuid = $"dead-marked-{Guid.NewGuid():N}",
                    Role = "user",
                    Text = DelegationReportFormatter.TaskMarker(taskId) + "\n\nDo the thing.",
                    Timestamp = at,
                    CreatedAt = at,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 2,
                    Kind = TranscriptKinds.AssistantText,
                    Uuid = $"dead-marked-{Guid.NewGuid():N}",
                    Role = "assistant",
                    Text = "Shipped the work.\n" + DelegationReportFormatter.ReportToken(taskId, "done"),
                    ApiCallId = apiCallId,
                    Timestamp = at,
                    CreatedAt = at,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 3,
                    Kind = TranscriptKinds.TurnEnd,
                    Uuid = $"dead-marked-{Guid.NewGuid():N}",
                    Role = "assistant",
                    StopReason = TranscriptKinds.StopReasons.EndTurn,
                    ApiCallId = apiCallId,
                    Timestamp = at,
                    CreatedAt = at,
                });
            await db.SaveChangesAsync();
        }

        public async Task AddTranscriptNoiseAsync(Guid sessionId)
        {
            var at = DateTime.UtcNow.AddMinutes(-1);
            await using var db = CreateContext();
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 1,
                Kind = TranscriptKinds.UserPrompt,
                Uuid = $"dead-noise-{Guid.NewGuid():N}",
                Role = "user",
                Text = "a human typed here",
                Timestamp = at,
                CreatedAt = at,
            });
            await db.SaveChangesAsync();
        }

        public async Task DeleteSessionRowAsync(Guid sessionId)
        {
            await using var db = CreateContext();
            await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
        }

        public async Task SetSessionStatusAsync(Guid sessionId, SessionStatus status)
        {
            await using var db = CreateContext();
            await db.AgentSessions.Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status));
        }

        public async Task<AgentTask> ReadTaskAsync(Guid taskId)
        {
            await using var db = CreateContext();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        }

        public async Task<AgentTaskLiveness.SessionSnapshot?> ReadSessionSnapshotAsync(Guid sessionId)
        {
            await using var db = CreateContext();
            var row = await db.AgentSessions.AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => new { s.Status, s.EndedAt, s.FailureReason, s.TerminationSource })
                .SingleOrDefaultAsync();
            return row is null
                ? null
                : new AgentTaskLiveness.SessionSnapshot(
                    row.Status, row.EndedAt, row.FailureReason, row.TerminationSource);
        }

        /// <summary>Everything queued into THIS scenario's parent session, and nothing else.</summary>
        public async Task<List<string>> ParentNoteBodiesAsync()
        {
            await using var db = CreateContext();
            return await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == ParentSessionId)
                .Select(m => m.Body)
                .ToListAsync();
        }

        public async Task<List<AttentionItemDto>> AttentionItemsAsync()
        {
            var service = new AttentionService(
                CreateContext(), new FakeRunnerClient(), Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings()), TimeProvider.System,
                NullLogger<AttentionService>.Instance);
            var result = await service.GetAsync(CancellationToken.None);
            return result.Items.Where(i => i.TaskId is { } t && _tasks.Contains(t)).ToList();
        }

        private async Task EnsureParentSessionAsync()
        {
            if (_parentSeeded)
                return;
            _parentSeeded = true;

            await using var db = CreateContext();
            db.AgentSessions.Add(new AgentSession
            {
                Id = ParentSessionId,
                DefinitionName = "dead-session-test-parent",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                StartedAt = DateTime.UtcNow.AddHours(-1),
                LastSeenAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            _sessions.Add(ParentSessionId);
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.TranscriptEntries.Where(e => _sessions.Contains(e.AgentSessionId)).ExecuteDeleteAsync();
            await db.AgentTaskEvents.Where(e => _tasks.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
            await db.AgentIncidents.Where(i => i.AgentId != null && _agents.Contains(i.AgentId.Value)).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.SessionQueuedMessages
                .Where(m => _sessions.Contains(m.AgentSessionId)).ExecuteDeleteAsync();
            await db.Agents.Where(a => _agents.Contains(a.Id)).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => _sessions.Contains(s.Id)).ExecuteDeleteAsync();
        }
    }

    /// <summary>
    /// The runner's view, faked. It reports every session the database knows about — plus every one
    /// an open task names — as <c>Running</c>, EXCEPT the ids a test puts in <see cref="Gone"/>.
    ///
    /// <para>That is not laziness, it is the shared-database rule applied to a fleet-global sweep: a
    /// fake that returned an empty list would tell the sweep that every other suite's session is
    /// gone too, and it would dutifully fail their tasks. Here the only rows this sweep can reach are
    /// the ones the test itself declared dead.</para>
    /// </summary>
    private sealed class FakeRunnerClient : ISessionRunnerClient
    {
        public HashSet<Guid> Gone { get; } = [];
        public Exception? ListError { get; set; }
        public List<Guid> Killed { get; } = [];

        public async Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
        {
            if (ListError is not null)
                throw ListError;

            await using var db = CreateContext();
            var rows = await db.AgentSessions.AsNoTracking().Select(s => s.Id).ToListAsync(ct);
            var named = await db.AgentTasks.AsNoTracking()
                .Where(t => t.AgentSessionId != null)
                .Select(t => t.AgentSessionId!.Value)
                .Distinct()
                .ToListAsync(ct);

            return rows.Concat(named).Distinct()
                .Where(id => !Gone.Contains(id))
                .Select(id => new SessionRunnerSessionDto(
                    id, Pid: 4242, StartedAt: DateTime.UtcNow.AddHours(-1),
                    Status: "Running", ExitCode: null, ExitReason: AgentExitReason.Unknown,
                    LastSequence: 10, HostPid: 4243))
                .ToList();
        }

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
        {
            Killed.Add(sessionId);
            throw new NotSupportedException("nothing in this sweep may kill a session");
        }

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, "> ", 10));

        // CARD-0228 (auditing CARD-0222's frozen-clock deadlock): this throw is why this
        // harness's frozen FakeTimeProvider(DateTimeOffset.UtcNow) is safe today — it short-circuits
        // SessionMessageQueueService's verify path before any Task.Delay(..., _timeProvider) wait
        // loop is reached. Giving this a real implementation exposes the CARD-0222 class here too;
        // switch the clock to the offset-over-real-clock pattern (HerdrAlwaysOnChannelParityTests /
        // AgentSupervisionTests) in the same change.
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
}
