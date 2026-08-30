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
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0020 S2/S3 — <c>AgentTaskDispatcher.FailOverdueTasksAsync</c>, the deadline on work that
/// STARTED and never stops. The three sweeps beside it all ask a question about DELIVERY or
/// LIVENESS, and a task whose brief landed, whose session is alive and which is simply never going
/// to finish answered every one of them for as long as it ran.
///
/// <para><b>It fails and reports; it never escalates, never kills and never retries.</b> Every test
/// here asserts the session survived — that is the CARD-0056 constraint, not a detail: the thing
/// being failed is a task that may contain real work, and unlike a never-started session there is
/// no evidence here that anything is wrong with the process.</para>
///
/// <para><b>Hermetic limits.</b> This is a fleet-global sweep against the shared test Postgres, so
/// it also walks every OTHER suite's open tasks. With the shipped 240-minute ceiling it would fail
/// rows seeded by <c>AttentionServiceTests</c> (one is dispatched exactly 240 minutes ago) as a side
/// effect of running. The harness therefore arms limits far beyond anything any other suite seeds
/// (the oldest is 400 minutes) and back-dates its own rows past them, so no other test's data can
/// reach even the 80% preview gate. <c>NotInParallel</c> with NO group key on top, per the
/// shared-database rule in CLAUDE.md.</para>
/// </summary>
[Category("Integration")]
[NotInParallel]
public class AgentTaskOverdueDeadlineTests
{
    /// <summary>Minutes. Far past anything any other suite seeds; the preview gate alone is 80 000.</summary>
    private const int Ceiling = 100_000;
    private const int ModelWait = 50_000;
    private const int LocalExecution = 60_000;

    [Test]
    public async Task a_task_past_its_role_ceiling_is_failed_with_the_clock_and_the_phase_named()
    {
        var (harness, stopper) = CreateHarness();
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 150_000);
        // Idle tail: the ceiling is the only clock that can apply.
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 149_000),
            (TranscriptKinds.TurnEnd, null, 148_000));

        await harness.FailOverdueTasksAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldNotBeNull();
        failed.FailureReason.ShouldContain($"{Ceiling}-minute ceiling for role Code", customMessage:
            "the reason must name the clock that fired");
        failed.FailureReason.ShouldContain("Last transcript entry: TurnEnd", customMessage:
            "and the phase, so the failure is diagnosable without opening the session");
        failed.FailureReason.ShouldContain("not escalated and not retried");
        stopper.Killed.ShouldBeEmpty("a deadline is not evidence that the session is broken");
        (await verify.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == task && e.Type == AgentTaskEventType.Failed)).ShouldBeTrue();
    }

    [Test]
    public async Task a_task_under_its_ceiling_is_left_completely_alone()
    {
        var (harness, stopper) = CreateHarness();
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 50_000);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 49_000),
            (TranscriptKinds.TurnEnd, null, 48_000));

        await harness.FailOverdueTasksAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task))
            .Status.ShouldBe(AgentTaskStatus.Working, "half way through a ceiling is not overdue");
        stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task a_deferred_dispatched_task_still_fails_on_the_model_wait_deadline()
    {
        // CARD-0117 D8: the delivery watchdog hands a Pending-brief + working session to
        // TaskDeadlinePolicy. The bound is ModelWaitDeadlineMinutes, and this sweep already
        // covers Dispatched rows, already pulls, already does not kill.
        var (harness, stopper) = CreateHarness();
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(
            dispatchedMinutesAgo: 70_000, status: AgentTaskStatus.Dispatched);
        await scenario.SeedEntriesAsync((TranscriptKinds.UserPrompt, "the brief", 69_000));
        await scenario.SeedPendingBriefAsync(task);

        await harness.FailOverdueTasksAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task);
        failed.Status.ShouldBe(AgentTaskStatus.Failed, "the deferred case is not open-ended");
        failed.FailureReason.ShouldContain("waiting on the model");
        failed.FailureReason.ShouldContain($"{ModelWait}-minute deadline");
        stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task a_mid_turn_session_past_the_model_wait_deadline_is_failed_naming_the_phase()
    {
        // The S3 tightening: the ceiling has not been reached, and the task is failed anyway
        // because the session has been mid-turn with the model owing the next token for longer
        // than a model wait is ever allowed to take.
        var (harness, stopper) = CreateHarness();
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 70_000);
        await scenario.SeedEntriesAsync((TranscriptKinds.UserPrompt, "the brief", 69_000));

        await harness.FailOverdueTasksAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task);
        failed.Status.ShouldBe(AgentTaskStatus.Failed, "under the ceiling, but the phase clock fired");
        failed.FailureReason.ShouldNotBeNull();
        failed.FailureReason.ShouldContain("waiting on the model", customMessage:
            "the phase, not just the elapsed time");
        failed.FailureReason.ShouldContain($"{ModelWait}-minute deadline");
        stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task an_idle_session_never_takes_the_phase_deadline_however_long_it_has_sat()
    {
        // The partition with PastExpectedIdle, enforced end to end: the phase clock owns
        // working == true and nothing else, so an idle tail this old is still only the ceiling's
        // business — and the ceiling is nowhere near.
        var (harness, _) = CreateHarness();
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 70_000);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 69_500),
            (TranscriptKinds.TurnEnd, null, 69_000));

        await harness.FailOverdueTasksAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task)).Status.ShouldBe(AgentTaskStatus.Working);
    }

    [Test]
    public async Task a_compaction_tail_does_not_read_as_a_stalled_model_call()
    {
        // The measured defect (plan section 3.1): every UserPrompt-headed gap over 10 minutes in
        // the live corpus — up to 45.5 hours — is a /compact housekeeping record. Keyed on the raw
        // Kind this task would be failed; through IsWorkingAsync it is correctly left alone.
        var (harness, _) = CreateHarness();
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 70_000);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 69_500),
            (TranscriptKinds.TurnEnd, null, 69_400),
            (TranscriptKinds.UserPrompt,
                $"{TranscriptKinds.LocalCommandStdoutPrefix}Compacted (ctrl+o to see full summary)</local-command-stdout>",
                69_000));

        await harness.FailOverdueTasksAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task)).Status.ShouldBe(AgentTaskStatus.Working);
    }

    [Test]
    public async Task an_unresolved_api_error_recovery_stands_the_sweep_down()
    {
        // CARD-0072's ladder is the more specific mechanism: it schedules its own resumes and
        // escalates to Critical on its own caps. Failing the task underneath it would settle work
        // the ladder is still reviving.
        var (harness, stopper) = CreateHarness();
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 150_000);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 149_000),
            (TranscriptKinds.TurnEnd, null, 148_000));
        await scenario.SeedApiErrorRecoveryAsync(resolved: false);

        await harness.FailOverdueTasksAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task))
            .Status.ShouldBe(AgentTaskStatus.Working, "the retry ladder owns this session");
    }

    [Test]
    public async Task a_resolved_api_error_recovery_does_not_stand_the_sweep_down()
    {
        // The gate is "the ladder is still working on it", not "the ladder ever touched it".
        var (harness, _) = CreateHarness();
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 150_000);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 149_000),
            (TranscriptKinds.TurnEnd, null, 148_000));
        await scenario.SeedApiErrorRecoveryAsync(resolved: true);

        await harness.FailOverdueTasksAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task)).Status.ShouldBe(AgentTaskStatus.Failed);
    }

    [Test]
    public async Task bind_refusal_recovery_still_wins_over_the_deadline()
    {
        // CARD-0085: an unbound session is not evidence the work did not happen. The deadline is
        // the last gate before Failed, and the evidence in the working directory outranks it.
        using var repo = new ScratchGitRepo("card0020-overdue-recovery");
        await repo.CommitFileAsync("README.md", "base\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "plan.md"), "the plan\n");
        await repo.GitAsync("add", ".");
        await repo.GitAsync("commit", "-m", "docs(providers): CARD-0083 plan - the contract");
        var sha = (await repo.GitReadAsync("rev-parse", "--short", "HEAD")).Trim();

        var (harness, stopper) = CreateHarness();
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(
            dispatchedMinutesAgo: 150_000,
            workingDirectory: repo.Path,
            title: "CARD-0083 plan the provider contract",
            withAgent: true);
        // No transcript at all — the unbound shape. The ceiling is breached on the wall clock.

        await harness.FailOverdueTasksAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var recovered = await verify.AgentTasks.SingleAsync(t => t.Id == task);
        recovered.Status.ShouldBe(AgentTaskStatus.Succeeded, "the work is in the repo; the row was wrong");
        recovered.Result.ShouldContain(sha);
        stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task the_caller_is_told_rather_than_left_to_discover_it()
    {
        var (harness, _) = CreateHarness();
        await using var scenario = new Scenario();
        var parent = await scenario.SeedSessionAsync();
        var task = await scenario.SeedTaskAsync(
            dispatchedMinutesAgo: 150_000, replyToSession: parent);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 149_000),
            (TranscriptKinds.TurnEnd, null, 148_000));

        await harness.FailOverdueTasksAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task)).Status.ShouldBe(AgentTaskStatus.Failed);
        var note = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == parent && m.Origin == QueuedMessageOrigin.Delegation)
            .SingleAsync();
        note.Body.ShouldContain("ceiling for role Code", customMessage:
            "the completion note carries the same reason the board shows");
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static (AgentTaskDispatcher Dispatcher, RecordingSessionStopper Stopper) CreateHarness()
    {
        var stopper = new RecordingSessionStopper();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            DefaultTimeoutMinutes = Ceiling,
            ModelWaitDeadlineMinutes = ModelWait,
            LocalExecutionDeadlineMinutes = LocalExecution,
            RolePolicy = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Code"] = new DelegationSettings.RolePolicyEntry { TimeoutMinutes = Ceiling },
            },
        }));
        services.AddOptions<AgentRegistrySettings>();
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-overdue-wt"),
        }));
        services.AddSingleton<IWorktreeManager, WorktreeManager>();
        services.AddSingleton<IGitService, GitService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        services.AddSingleton<AgentTaskReplyService>();
        // CARD-0085's recovery gate, armed exactly as the delivery watchdog's suite arms it: with
        // no repo and no CARD-NNNN in the title it finds nothing and the Failed stands.
        services.AddSingleton<GitWorkspaceService>();
        services.AddSingleton(Options.Create(new DelegateBindRefusalRecoverySettings()));
        services.AddSingleton<DelegateBindRefusalRecovery>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), stopper);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>Seeds rows, remembers their ids, deletes exactly those — the shared-database rule.</summary>
    private sealed class Scenario : IAsyncDisposable
    {
        private readonly Guid _sessionId = Guid.NewGuid();
        private readonly List<Guid> _sessions = [];
        private readonly List<Guid> _tasks = [];
        private readonly List<Guid> _agents = [];
        private long _seq;

        public Scenario() => _sessions.Add(_sessionId);

        public async Task<Guid> SeedSessionAsync()
        {
            var id = Guid.NewGuid();
            await using var db = CreateContext();
            db.AgentSessions.Add(NewSession(id, DateTime.UtcNow.AddMinutes(-5)));
            await db.SaveChangesAsync();
            _sessions.Add(id);
            return id;
        }

        public async Task<Guid> SeedTaskAsync(
            int dispatchedMinutesAgo,
            string? workingDirectory = null,
            string? title = null,
            bool withAgent = false,
            Guid? replyToSession = null,
            AgentTaskStatus status = AgentTaskStatus.Working)
        {
            var dispatched = DateTime.UtcNow.AddMinutes(-dispatchedMinutesAgo);
            var cwd = workingDirectory ?? Path.GetTempPath();
            await using var db = CreateContext();
            if (!await db.AgentSessions.AnyAsync(s => s.Id == _sessionId))
                db.AgentSessions.Add(NewSession(_sessionId, dispatched, cwd));

            Guid? agentId = null;
            if (withAgent)
            {
                agentId = Guid.NewGuid();
                var name = $"ovd-{agentId:N}"[..16];
                db.Agents.Add(new Agent
                {
                    Id = agentId.Value,
                    Name = name,
                    Slug = name,
                    WorkingDirectory = cwd,
                    Details = "CARD-0020 overdue-deadline test delegate.",
                    Status = AgentStatus.Running,
                    ModelLevel = AgentModelLevel.Frontier,
                    IsPoolDelegate = true,
                    PersistentSessionId = _sessionId.ToString("D"),
                    CreatedAt = dispatched,
                    UpdatedAt = dispatched,
                });
                _agents.Add(agentId.Value);
            }

            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = title ?? "Overdue deadline test",
                Goal = "Run forever.",
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = cwd,
                AgentId = agentId,
                AgentSessionId = _sessionId,
                Status = status,
                ReplyTo = replyToSession is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session,
                ParentSessionId = replyToSession,
                CreatedAt = dispatched,
                DispatchedAt = dispatched,
            });
            await db.SaveChangesAsync();
            _tasks.Add(id);
            return id;
        }

        /// <param name="entries">Kind, text, and how many minutes ago the record is stamped.</param>
        public async Task SeedEntriesAsync(params (string Kind, string? Text, int MinutesAgo)[] entries)
        {
            await using var db = CreateContext();
            foreach (var (kind, text, minutesAgo) in entries)
            {
                var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = _sessionId,
                    Sequence = ++_seq,
                    Kind = kind,
                    Uuid = $"overdue-{Guid.NewGuid():N}",
                    Role = kind == TranscriptKinds.UserPrompt ? "user" : "assistant",
                    Text = text,
                    Timestamp = at,
                    CreatedAt = at,
                    StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null,
                });
            }

            await db.SaveChangesAsync();
        }

        public async Task SeedPendingBriefAsync(Guid taskId)
        {
            await using var db = CreateContext();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = _sessionId,
                Body = DelegationReportFormatter.TaskMarker(taskId) + "\n\nRun forever.",
                Status = QueuedMessageStatus.Pending,
                Sequence = 1,
                Origin = QueuedMessageOrigin.Delegation,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            });
            await db.SaveChangesAsync();
        }

        public async Task SeedApiErrorRecoveryAsync(bool resolved)
        {
            await using var db = CreateContext();
            db.ApiErrorRecoveries.Add(new ApiErrorRecovery
            {
                Id = Guid.NewGuid(),
                AgentSessionId = _sessionId,
                StubSequence = 1,
                Classification = ApiErrorClassification.Transient,
                DetectedAt = DateTime.UtcNow.AddMinutes(-30),
                AttemptCount = 1,
                NextAttemptAt = resolved ? null : DateTime.UtcNow.AddMinutes(30),
                ResolvedAt = resolved ? DateTime.UtcNow.AddMinutes(-1) : null,
                ResolvedReason = resolved ? ApiErrorRecoveryReasons.Superseded : null,
            });
            await db.SaveChangesAsync();
        }

        private static AgentSession NewSession(Guid id, DateTime at, string? cwd = null) => new()
        {
            Id = id,
            DefinitionName = "overdue-test",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = cwd ?? Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = at,
            StartedAt = at,
            LastSeenAt = DateTime.UtcNow,
        };

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.ApiErrorRecoveries.Where(r => _sessions.Contains(r.AgentSessionId)).ExecuteDeleteAsync();
            await db.TranscriptEntries.Where(e => _sessions.Contains(e.AgentSessionId)).ExecuteDeleteAsync();
            await db.SessionQueuedMessages.Where(m => _sessions.Contains(m.AgentSessionId)).ExecuteDeleteAsync();
            await db.AgentTaskEvents.Where(e => _tasks.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
            await db.AgentIncidents.Where(i => i.AgentId != null && _agents.Contains(i.AgentId.Value)).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => _sessions.Contains(s.Id)).ExecuteDeleteAsync();
            await db.Agents.Where(a => _agents.Contains(a.Id)).ExecuteDeleteAsync();
        }
    }
}

/// <summary>
/// The one thing the sweep tests above cannot catch: the WIRING. <c>AgentSessionRuntime</c> is an
/// OPTIONAL constructor dependency — every harness that predates it still constructs the dispatcher
/// — so a missing registration in the real container would not throw. It would leave the two sweeps
/// that make irreversible decisions judging whatever happened to stream, forever, with no symptom
/// anywhere. That is exactly the CARD-0055 failure the pull exists to prevent.
///
/// <c>[NotInParallel]</c> is REQUIRED here, not stylistic: the session-shared
/// <see cref="AntiphonWebAppFactory"/> is a <c>WebApplicationFactory</c>, whose first-touch
/// <c>EnsureServer()</c> is not thread-safe. Every other consumer of the same shared instance
/// carries <c>[NotInParallel]</c>, so this class was the only one that could touch it
/// concurrently — and when it won that race the shared factory was left holding a TestServer
/// that had never been started, failing all 32 tests across the other six classes with
/// "The server has not been started or no web application was configured." while this one
/// passed. Any new class taking the shared factory must carry this attribute too.
/// </summary>
[Category("Integration")]
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class AgentTaskDispatcherWiringTests
{
    private readonly AntiphonWebAppFactory _factory;

    public AgentTaskDispatcherWiringTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public void the_real_container_gives_the_dispatcher_a_live_transcript_pull()
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
            .TranscriptPullArmed
            .ShouldBeTrue("AgentSessionRuntime must reach the dispatcher, not default to null");
    }
}
