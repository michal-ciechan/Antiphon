using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.ValueObjects;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
public sealed class AgentSessionCancelledTurnTests
{
    [Test]
    public async Task C1322_Interrupted_result_preserves_live_session()
    {
        // Independent controls run before the regression witness, including on the red commit.
        await CheckStartAsync(completed: true, stopReason: null, RunPhase.Succeeded);
        await CheckStartAsync(completed: false, stopReason: null, RunPhase.TimedOut);
        await CheckStartAsync(completed: false, TranscriptKinds.StopReasons.Cancelled, RunPhase.AwaitingInput);
    }

    [Test]
    public async Task C1322_Interrupted_result_survives_stall_scans()
    {
        await CheckStartAsync(completed: false, TranscriptKinds.StopReasons.Cancelled,
            RunPhase.AwaitingInput, scanAfterInterruption: true);
    }

    [Test]
    public async Task C1322_Follow_up_prompt_rearms_interrupted_attempt()
    {
        await CheckStartAsync(completed: false, TranscriptKinds.StopReasons.Cancelled,
            RunPhase.AwaitingInput, resumeAfterInterruption: true);
        await CheckStartAsync(completed: false, TranscriptKinds.StopReasons.Cancelled,
            RunPhase.AwaitingInput, resumeAfterInterruption: true, olderCommandWrapperOnly: true);
    }

    private static async Task CheckStartAsync(
        bool completed, string? stopReason, RunPhase expectedPhase, bool scanAfterInterruption = false,
        bool resumeAfterInterruption = false, bool olderCommandWrapperOnly = false)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        await using var db = new AppDbContext(options);
        var scratch = Directory.CreateTempSubdirectory("c1322-session-");
        try
        {
            var card = CreateCard(scratch.FullName);
            db.Add(card.Board.Project);
            await db.SaveChangesAsync();

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(builder => builder.UseNpgsql(schema.ConnectionString));
            await using var provider = services.BuildServiceProvider();
            var scopes = provider.GetRequiredService<IServiceScopeFactory>();
            var events = new MockEventBus();
            var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
            var settings = Options.Create(new AgentSessionSettings
            {
                FirstDeltaTimeoutMs = 1_000,
                KillGraceMs = 100,
                SessionLogPath = Path.Combine(scratch.FullName, "logs")
            });
            var runtime = new AgentSessionRuntime(
                events, settings, scopes, clock, NullLogger<AgentSessionRuntime>.Instance);
            var adapter = new FakeAgentProtocolAdapter
            {
                RegisterOnStart = runtime,
                PromptOutput = "Partial diagnostic response",
                TurnCompleted = completed,
                TurnStopReason = stopReason
            };
            var hooks = new RecordingHooks();
            var service = new AgentSessionService(
                db,
                new FakeWorktrees(Path.Combine(scratch.FullName, "worktree")),
                new WorkspaceHookService(hooks, NullLogger<WorkspaceHookService>.Instance),
                new AdapterFactory(adapter),
                runtime,
                events,
                new SessionMessageQueueService(
                    scopes, runtime, events, clock, NullLogger<SessionMessageQueueService>.Instance),
                scopes,
                settings,
                Options.Create(new SupervisionSettings()),
                clock,
                NullLogger<AgentSessionService>.Instance);

            var result = await service.StartAsync(
                new StartAgentSessionRequest(card.Id, "fake-codex", AgentKind.Codex, "Run the assigned work"),
                new AgentLaunchSpec("fake-codex", AgentKind.Codex, "fake-only", [],
                    new Dictionary<string, string>(), scratch.FullName, 120, 30),
                CancellationToken.None);

            if (scanAfterInterruption)
            {
                // Exercise the exact scanner called by RunAttemptStallHostedService, with
                // fresh DB contexts. A stale streaming control proves the scanner is active.
                var controlSessionId = Guid.NewGuid();
                var control = new FakeAgentProtocolAdapter();
                runtime.Register(controlSessionId, control);
                await using (var seed = new AppDbContext(options))
                {
                    var now = clock.GetUtcNow().UtcDateTime;
                    seed.AgentSessions.Add(new AgentSession
                    {
                        Id = controlSessionId, CardId = card.Id, DefinitionName = "stall-control",
                        AgentKind = AgentKind.Raw, Status = SessionStatus.Running, Cwd = scratch.FullName,
                        CreatedAt = now, StartedAt = now, LastSeenAt = now
                    });
                    seed.RunAttempts.Add(new RunAttempt
                    {
                        Id = Guid.NewGuid(), CardId = card.Id, AgentSessionId = controlSessionId,
                        AttemptNumber = 2, Phase = RunPhase.StreamingTurn, CreatedAt = now,
                        StartedAt = now, LastEventAt = now, PhaseStartedAt = now, Prompt = "stall control"
                    });
                    await seed.SaveChangesAsync();
                }

                clock.Advance(TimeSpan.FromMinutes(10));
                await using (var scanDb = new AppDbContext(options))
                {
                    var scan = new RunAttemptStallDetector(
                        scanDb, runtime, events, settings, clock,
                        NullLogger<RunAttemptStallDetector>.Instance);
                    var stalled = await scan.ScanAsync(CancellationToken.None);
                    adapter.KillCount.ShouldBe(0,
                        "a cancelled boundary must not become an automatic stall kill after five minutes");
                    adapter.Disposed.ShouldBeFalse();
                    stalled.ShouldBe(1, "only the genuinely streaming control is eligible");
                    control.KillCount.ShouldBe(1);
                    control.Disposed.ShouldBeTrue();
                }

                // Refreshing LastEventAt once would merely defer the same kill. Retention must
                // survive another independent scan well beyond every refreshed timestamp.
                clock.Advance(TimeSpan.FromDays(1));
                await using (var scanDb = new AppDbContext(options))
                {
                    var scan = new RunAttemptStallDetector(
                        scanDb, runtime, events, settings, clock,
                        NullLogger<RunAttemptStallDetector>.Instance);
                    (await scan.ScanAsync(CancellationToken.None)).ShouldBe(0);
                    adapter.KillCount.ShouldBe(0);
                    adapter.Disposed.ShouldBeFalse();
                }
            }

            // Read through a new context: tracked in-memory values are not persistence evidence.
            await using var reloaded = new AppDbContext(options);
            var session = await reloaded.AgentSessions.SingleAsync(s => s.Id == result.SessionId);
            var attempt = await reloaded.RunAttempts.SingleAsync(a => a.Id == result.RunAttemptId);
            var storedCard = await reloaded.Cards.SingleAsync(c => c.Id == card.Id);
            var worktree = await reloaded.Worktrees.SingleAsync(w => w.Id == result.WorktreeId);
            result.FirstDeltaReceived.ShouldBeTrue();
            adapter.Started.ShouldBeTrue();
            adapter.SentPrompt.ShouldBe("Run the assigned work");
            adapter.SnapshotRawOutput().ShouldContain("Partial diagnostic response");
            attempt.Phase.ShouldBe(expectedPhase,
                "an interrupted turn must not take the completion-timeout/kill branch");
            attempt.AgentSessionId.ShouldBe(session.Id);
            attempt.WorktreeId.ShouldBe(worktree.Id);
            attempt.ErrorDetails.ShouldBeNull();
            attempt.ExitCode.ShouldBeNull();
            storedCard.OwnerSessionId.ShouldBe(session.Id);
            storedCard.CurrentWorktreeId.ShouldBe(worktree.Id);
            worktree.Status.ShouldBe(WorktreeStatus.Active);
            hooks.Calls.ShouldContain("before_run");

            if (expectedPhase == RunPhase.TimedOut)
            {
                session.Status.ShouldBe(SessionStatus.Failed);
                session.FailureReason.ShouldBe("Timed out waiting for the agent turn to complete.");
                session.EndedAt.ShouldNotBeNull();
                session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
                attempt.CompletedAt.ShouldNotBeNull();
                adapter.KillCount.ShouldBe(1);
                adapter.Disposed.ShouldBeTrue();
                adapter.Lifecycle.ShouldBe(new[] { "Kill", "Dispose" });
                hooks.Calls.ShouldNotContain("after_run");
            }
            else
            {
                session.Status.ShouldBe(SessionStatus.Running);
                session.EndedAt.ShouldBeNull();
                session.FailureReason.ShouldBeNull();
                session.TerminationSource.ShouldBe(SessionTerminationSource.Unknown);
                session.ExitCode.ShouldBeNull();
                adapter.KillCount.ShouldBe(0);
                adapter.Disposed.ShouldBeFalse();
                adapter.Lifecycle.ShouldBeEmpty();
                adapter.Exited.IsCompleted.ShouldBeFalse();
                if (stopReason == TranscriptKinds.StopReasons.Cancelled)
                {
                    attempt.CompletedAt.ShouldBeNull();
                    hooks.Calls.ShouldNotContain("after_run");
                }
                else
                {
                    attempt.CompletedAt.ShouldNotBeNull();
                    hooks.Calls.Count(c => c == "after_run").ShouldBe(1);
                }
            }

            if (resumeAfterInterruption)
            {
                clock.Advance(TimeSpan.FromHours(1));
                var prompt = new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = result.SessionId, Sequence = 10,
                    Kind = TranscriptKinds.UserPrompt, Text = "Continue after interruption",
                    Timestamp = clock.GetUtcNow().UtcDateTime, CreatedAt = clock.GetUtcNow().UtcDateTime
                };
                var action = new SessionRuntimeAction
                {
                    AgentSessionId = result.SessionId, Sequence = prompt.Sequence, Phase = 2
                };

                // Queued input and replay of the old turn are not evidence of a new turn.
                prompt.Kind = TranscriptKinds.QueuedUserPrompt;
                await runtime.ExecuteActionPhaseAsync(action, prompt, CancellationToken.None);
                prompt.Kind = TranscriptKinds.UserPrompt;
                prompt.Timestamp = attempt.PhaseStartedAt.AddSeconds(-1);
                prompt.CreatedAt = prompt.Timestamp.Value;
                await runtime.ExecuteActionPhaseAsync(action, prompt, CancellationToken.None);
                await reloaded.Entry(attempt).ReloadAsync();
                attempt.Phase.ShouldBe(RunPhase.AwaitingInput);

                prompt.Timestamp = clock.GetUtcNow().UtcDateTime;
                prompt.CreatedAt = clock.GetUtcNow().UtcDateTime;
                foreach (var housekeeping in new[]
                         { "[Request interrupted by user]", "<command-name>/status</command-name>" })
                {
                    prompt.Text = housekeeping;
                    await runtime.ExecuteActionPhaseAsync(action, prompt, CancellationToken.None);
                    await reloaded.Entry(attempt).ReloadAsync();
                    attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
                }

                if (olderCommandWrapperOnly)
                {
                    // An earlier turn's command wrapper must not classify a new real
                    // slash-prefixed follow-up as a local-command echo.
                    await using var seed = new AppDbContext(options);
                    seed.TranscriptEntries.Add(new TranscriptEntry
                    {
                        Id = Guid.NewGuid(), AgentSessionId = result.SessionId, Sequence = 1,
                        Kind = TranscriptKinds.UserPrompt, Text = "<command-name>/status</command-name>",
                        Timestamp = attempt.PhaseStartedAt.AddSeconds(-1),
                        CreatedAt = attempt.PhaseStartedAt.AddSeconds(-1)
                    });
                    await seed.SaveChangesAsync();
                }
                else
                {
                    // Phase 2 runs after the transcript batch commits. Claude stores both
                    // the raw slash-command echo and its wrapper, even when the echo is first.
                    var commandEcho = new TranscriptEntry
                    {
                        Id = Guid.NewGuid(), AgentSessionId = result.SessionId, Sequence = 11,
                        Kind = TranscriptKinds.UserPrompt, Text = "/status",
                        Timestamp = clock.GetUtcNow().UtcDateTime, CreatedAt = clock.GetUtcNow().UtcDateTime
                    };
                    await using (var seed = new AppDbContext(options))
                    {
                        seed.TranscriptEntries.AddRange(commandEcho, new TranscriptEntry
                        {
                            Id = Guid.NewGuid(), AgentSessionId = result.SessionId, Sequence = 12,
                            Kind = TranscriptKinds.UserPrompt, Text = "<command-name>/status</command-name>",
                            Timestamp = clock.GetUtcNow().UtcDateTime, CreatedAt = clock.GetUtcNow().UtcDateTime
                        });
                        await seed.SaveChangesAsync();
                    }
                    var interruptedLastEventAt = attempt.LastEventAt;
                    await runtime.ExecuteActionPhaseAsync(new SessionRuntimeAction
                    {
                        AgentSessionId = result.SessionId, Sequence = commandEcho.Sequence, Phase = 2
                    }, commandEcho, CancellationToken.None);
                    clock.Advance(TimeSpan.FromMinutes(10));
                    await using (var commandScanDb = new AppDbContext(options))
                    {
                        var commandScan = new RunAttemptStallDetector(
                            commandScanDb, runtime, events, settings, clock,
                            NullLogger<RunAttemptStallDetector>.Instance);
                        var stalled = await commandScan.ScanAsync(CancellationToken.None);
                        adapter.KillCount.ShouldBe(0,
                            "a raw command echo beside its persisted wrapper must not arm an automatic stall kill");
                        stalled.ShouldBe(0);
                    }
                    await reloaded.Entry(attempt).ReloadAsync();
                    attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
                    attempt.LastEventAt.ShouldBe(interruptedLastEventAt);
                    attempt.CompletedAt.ShouldBeNull();
                    await reloaded.Entry(session).ReloadAsync();
                    session.Status.ShouldBe(SessionStatus.Running);
                    session.EndedAt.ShouldBeNull();
                    adapter.Disposed.ShouldBeFalse();
                }

                prompt.Sequence = 13;
                action.Sequence = prompt.Sequence;
                prompt.Timestamp = clock.GetUtcNow().UtcDateTime;
                prompt.CreatedAt = clock.GetUtcNow().UtcDateTime;
                prompt.Text = olderCommandWrapperOnly
                    ? "/status explain the remaining work"
                    : "Continue after interruption";
                await runtime.ExecuteActionPhaseAsync(action, prompt, CancellationToken.None);
                await reloaded.Entry(attempt).ReloadAsync();
                attempt.Phase.ShouldBe(RunPhase.StreamingTurn,
                    "a confirmed follow-up prompt must re-arm the interrupted attempt's stall scan");
                attempt.LastEventAt.ShouldBe(clock.GetUtcNow().UtcDateTime, TimeSpan.FromMilliseconds(1));
                attempt.CompletedAt.ShouldBeNull();
                await reloaded.Entry(session).ReloadAsync();
                session.Status.ShouldBe(SessionStatus.Running);
                adapter.KillCount.ShouldBe(0);

                // Replaying the accepted prompt cannot extend the new turn's stall deadline.
                var rearmedAt = attempt.LastEventAt;
                clock.Advance(TimeSpan.FromMinutes(1));
                await runtime.ExecuteActionPhaseAsync(action, prompt, CancellationToken.None);
                await reloaded.Entry(attempt).ReloadAsync();
                attempt.LastEventAt.ShouldBe(rearmedAt);
                await using var scanDb = new AppDbContext(options);
                var scan = new RunAttemptStallDetector(scanDb, runtime, events, settings, clock,
                    NullLogger<RunAttemptStallDetector>.Instance);
                (await scan.ScanAsync(CancellationToken.None)).ShouldBe(0);
                clock.Advance(TimeSpan.FromMinutes(10));
                (await scan.ScanAsync(CancellationToken.None)).ShouldBe(1);
                adapter.KillCount.ShouldBe(1, "the existing StreamingTurn scan must be armed again");
            }
        }
        finally
        {
            Directory.Delete(scratch.FullName, recursive: true);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private static Card CreateCard(string repoPath)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = $"C1322-{Guid.NewGuid():N}",
            LocalRepositoryPath = repoPath, BaseBranch = "master", CreatedAt = now, UpdatedAt = now
        };
        var board = new Board
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Project = project,
            Name = $"C1322-{Guid.NewGuid():N}", CreatedAt = now, UpdatedAt = now
        };
        project.Boards.Add(board);
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = board.Id, Board = board,
            StateKey = "backlog", Name = "Backlog", CardStatus = CardStatus.Backlog,
            CreatedAt = now, UpdatedAt = now
        };
        board.Columns.Add(column);
        board.WorkflowDefinitions.Add(new BoardWorkflowDefinition
        {
            Id = Guid.NewGuid(), BoardId = board.Id, Board = board,
            Version = 1, Name = "C1322 hooks", IsActive = true, CreatedAt = now, UpdatedAt = now,
            Content = """
                hooks:
                  before_run: before-run-witness
                  after_run: after-run-witness
                """
        });
        var card = new Card
        {
            Id = Guid.NewGuid(), BoardId = board.Id, Board = board,
            BoardColumnId = column.Id, BoardColumn = column,
            Identifier = $"C1322-{Guid.NewGuid():N}"[..20], Title = "Interrupted first turn",
            Status = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now
        };
        board.Cards.Add(card);
        column.Cards.Add(card);
        return card;
    }

    private sealed class AdapterFactory(FakeAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }

    private sealed class RecordingHooks : IWorkspaceHookRunner
    {
        public List<string> Calls { get; } = [];

        public Task<WorkspaceHookResult?> RunAsync(
            WorkspaceHookContext context, string hookName, WorkspaceHookDefinition? hook, CancellationToken ct)
        {
            Calls.Add(hookName);
            return Task.FromResult<WorkspaceHookResult?>(hook is null ? null :
                new WorkspaceHookResult(hookName, hook.Command, 0, false, "", "", TimeSpan.Zero));
        }

        public Task<WorkspaceHookResult> RunAsync(
            WorkspaceHookContext context, string hookName, string command, TimeSpan timeout, CancellationToken ct)
        {
            Calls.Add(hookName);
            return Task.FromResult(new WorkspaceHookResult(hookName, command, 0, false, "", "", TimeSpan.Zero));
        }
    }

    private sealed class FakeWorktrees(string path) : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            Directory.CreateDirectory(path);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new WorktreeInfo(cardId, repoPath, path, $"feat/card-{cardId}", baseRef, now, now));
        }

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);
        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) =>
            throw new InvalidOperationException("Start must retain its workspace.");
        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;
        public Task<int> PruneStaleAsync(CancellationToken ct) =>
            throw new InvalidOperationException("Start must not prune workspaces.");
    }
}
