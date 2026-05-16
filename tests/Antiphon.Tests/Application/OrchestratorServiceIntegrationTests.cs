using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
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
[NotInParallel("Orchestrator")]
public class OrchestratorServiceIntegrationTests
{
    [Test]
    public async Task Orchestrator_dispatches_eligible_card_through_agent_session_service()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot, boardMaxConcurrent: 2);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "ORCH_OK" };
            await using var harness = BuildHarness(tempRoot, [adapter]);

            var result = await harness.Orchestrator.PollTickAsync(CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            result.Dispatched.ShouldBe(1);
            result.Failures.ShouldBe(0);
            adapter.Started.ShouldBeTrue();
            adapter.Killed.ShouldBeTrue();
            adapter.Disposed.ShouldBeTrue();
            adapter.SentPrompt.ShouldContain(graph.Card.Identifier);
            await using var verify = CreateContext();
            var session = await verify.AgentSessions.SingleAsync(s => s.CardId == graph.Card.Id);
            session.Status.ShouldBe(SessionStatus.Stopped);
            var attempt = await verify.RunAttempts.SingleAsync(a => a.CardId == graph.Card.Id);
            attempt.Phase.ShouldBe(RunPhase.Succeeded);
            var retry = await verify.RetrySchedules.SingleAsync(r => r.CardId == graph.Card.Id);
            retry.NextRetryAt.ShouldNotBeNull();
            var card = await verify.Cards.SingleAsync(c => c.Id == graph.Card.Id);
            card.OwnerSessionId.ShouldBeNull();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Orchestrator_skips_card_when_global_concurrency_full()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot, boardMaxConcurrent: 1);
            var occupied = AddCard(graph, "OCCUPIED", graph.ActiveColumn);
            db.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.Add(NewSession(occupied.Id, SessionStatus.Running));
            await db.SaveChangesAsync();

            await using var harness = BuildHarness(tempRoot, []);

            var result = await harness.Orchestrator.PollTickAsync(CancellationToken.None);

            result.Dispatched.ShouldBe(0);
            result.SkippedGlobalConcurrency.ShouldBe(1);
            await using var verify = CreateContext();
            (await verify.AgentSessions.CountAsync(s => s.CardId == graph.Card.Id)).ShouldBe(0);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Orchestrator_respects_max_concurrent_by_column()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot, boardMaxConcurrent: 5, columnMaxConcurrent: 1);
            var occupied = AddCard(graph, "OCCUPIED", graph.ActiveColumn);
            db.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.Add(NewSession(occupied.Id, SessionStatus.Running));
            await db.SaveChangesAsync();

            await using var harness = BuildHarness(tempRoot, []);

            var result = await harness.Orchestrator.PollTickAsync(CancellationToken.None);

            result.Dispatched.ShouldBe(0);
            result.SkippedColumnConcurrency.ShouldBe(1);
            await using var verify = CreateContext();
            (await verify.AgentSessions.CountAsync(s => s.CardId == graph.Card.Id)).ShouldBe(0);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Orchestrator_two_parallel_dispatches_only_one_wins()
    {
        await using var seed = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot);
            seed.Add(graph.Project);
            await seed.SaveChangesAsync();
            var token = graph.Card.ConcurrencyToken;

            await using var harnessA = BuildHarness(tempRoot, []);
            await using var harnessB = BuildHarness(tempRoot, []);

            var results = await Task.WhenAll(
                harnessA.Orchestrator.TryClaimCardAsync(graph.Card.Id, token, "fake", AgentKind.Raw, 120, 30, DateTime.UtcNow, CancellationToken.None),
                harnessB.Orchestrator.TryClaimCardAsync(graph.Card.Id, token, "fake", AgentKind.Raw, 120, 30, DateTime.UtcNow, CancellationToken.None));

            results.Count(result => result is not null).ShouldBe(1);
            await using var verify = CreateContext();
            var card = await verify.Cards.SingleAsync(c => c.Id == graph.Card.Id);
            card.OwnerSessionId.ShouldNotBeNull();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task RetryScheduler_backoff_matches_spec()
    {
        var scheduler = new RetryScheduler(Options.Create(new OrchestratorSettings()));

        scheduler.GetContinuationDelay().ShouldBe(TimeSpan.FromSeconds(1));
        scheduler.GetFailureDelay(1).ShouldBe(TimeSpan.FromSeconds(10));
        scheduler.GetFailureDelay(2).ShouldBe(TimeSpan.FromSeconds(20));
        scheduler.GetFailureDelay(3).ShouldBe(TimeSpan.FromSeconds(40));
        scheduler.GetFailureDelay(10).ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Test]
    public async Task RetryScheduler_survives_restart_and_due_retries_fire()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot);
            graph.Card.RetrySchedule = new RetrySchedule
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                AttemptCount = 1,
                MaxAttempts = 3,
                LastAttemptAt = DateTime.UtcNow.AddMinutes(-1),
                NextRetryAt = DateTime.UtcNow.AddSeconds(-1),
                LastError = "previous failure",
                Card = graph.Card
            };
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            await using var restartedDb = CreateContext();
            restartedDb.RetrySchedules
                .Single(r => r.CardId == graph.Card.Id)
                .LastError.ShouldBe("previous failure");
            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "RETRY_OK" };
            await using var harness = BuildHarness(tempRoot, [adapter]);

            var result = await harness.Orchestrator.PollTickAsync(CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            result.Dispatched.ShouldBe(1);
            adapter.Started.ShouldBeTrue();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Orchestrator_due_continuation_dispatches_again_after_success_releases_claim()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var firstAdapter = new FakeAgentProtocolAdapter { PromptOutput = "FIRST" };
            await using (var firstHarness = BuildHarness(tempRoot, [firstAdapter]))
            {
                var first = await firstHarness.Orchestrator.PollTickAsync(CancellationToken.None);
                first.Dispatched.ShouldBe(1);
                await firstHarness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                firstAdapter.Killed.ShouldBeTrue();
                firstAdapter.Disposed.ShouldBeTrue();
            }

            await using (var markDue = CreateContext())
            {
                var retry = await markDue.RetrySchedules.SingleAsync(r => r.CardId == graph.Card.Id);
                retry.NextRetryAt = DateTime.UtcNow.AddSeconds(-1);
                await markDue.SaveChangesAsync();
            }

            var secondAdapter = new FakeAgentProtocolAdapter { PromptOutput = "SECOND" };
            await using var secondHarness = BuildHarness(tempRoot, [secondAdapter]);
            var second = await secondHarness.Orchestrator.PollTickAsync(CancellationToken.None);
            await secondHarness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            second.Dispatched.ShouldBe(1);
            secondAdapter.Started.ShouldBeTrue();
            secondAdapter.Killed.ShouldBeTrue();
            secondAdapter.Disposed.ShouldBeTrue();
            await using var verify = CreateContext();
            (await verify.RunAttempts.CountAsync(a => a.CardId == graph.Card.Id)).ShouldBe(2);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Reconcile_clears_non_terminal_missing_runtime_session_after_restart()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();
            var session = NewSession(graph.Card.Id, SessionStatus.Running);
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            await db.Cards
                .Where(c => c.Id == graph.Card.Id)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(c => c.OwnerSessionId, session.Id)
                    .SetProperty(c => c.ConcurrencyToken, Guid.NewGuid()));
            db.RunAttempts.Add(new RunAttempt
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                AgentSessionId = session.Id,
                AttemptNumber = 1,
                Phase = RunPhase.StreamingTurn,
                CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow,
                LastEventAt = DateTime.UtcNow,
                PhaseStartedAt = DateTime.UtcNow,
                Prompt = "missing runtime",
                AgentSession = session
            });
            await db.SaveChangesAsync();

            await using var harness = BuildHarness(tempRoot, []);

            var result = await harness.Orchestrator.PollTickAsync(CancellationToken.None);

            result.Reconciled.ShouldBe(1);
            await using var verify = CreateContext();
            var card = await verify.Cards.SingleAsync(c => c.Id == graph.Card.Id);
            card.OwnerSessionId.ShouldBeNull();
            var attempt = await verify.RunAttempts.SingleAsync(a => a.AgentSessionId == session.Id);
            attempt.Phase.ShouldBe(RunPhase.Canceled);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Reconcile_keeps_fresh_starting_preclaim_until_runtime_grace_expires()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();
            var session = NewSession(graph.Card.Id, SessionStatus.Starting);
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            await db.Cards
                .Where(c => c.Id == graph.Card.Id)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(c => c.OwnerSessionId, session.Id)
                    .SetProperty(c => c.ConcurrencyToken, Guid.NewGuid()));

            await using var harness = BuildHarness(tempRoot, []);

            var result = await harness.Orchestrator.PollTickAsync(CancellationToken.None);

            result.Reconciled.ShouldBe(0);
            await using var verify = CreateContext();
            var card = await verify.Cards.SingleAsync(c => c.Id == graph.Card.Id);
            card.OwnerSessionId.ShouldBe(session.Id);
            (await verify.RetrySchedules.CountAsync(r => r.CardId == graph.Card.Id)).ShouldBe(0);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Reconcile_clears_stale_starting_preclaim_after_runtime_grace_expires()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();
            var session = NewSession(graph.Card.Id, SessionStatus.Starting);
            session.LastSeenAt = DateTime.UtcNow.AddMinutes(-10);
            session.CreatedAt = session.LastSeenAt;
            session.StartedAt = session.LastSeenAt;
            db.AgentSessions.Add(session);
            db.RunAttempts.Add(new RunAttempt
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                AgentSessionId = session.Id,
                AttemptNumber = 1,
                Phase = RunPhase.LaunchingAgent,
                CreatedAt = session.LastSeenAt,
                StartedAt = session.LastSeenAt,
                LastEventAt = session.LastSeenAt,
                PhaseStartedAt = session.LastSeenAt,
                Prompt = "stale starting",
                AgentSession = session
            });
            await db.SaveChangesAsync();
            await db.Cards
                .Where(c => c.Id == graph.Card.Id)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(c => c.OwnerSessionId, session.Id)
                    .SetProperty(c => c.ConcurrencyToken, Guid.NewGuid()));

            await using var harness = BuildHarness(tempRoot, []);

            var result = await harness.Orchestrator.PollTickAsync(CancellationToken.None);

            result.Reconciled.ShouldBe(1);
            await using var verify = CreateContext();
            var card = await verify.Cards.SingleAsync(c => c.Id == graph.Card.Id);
            card.OwnerSessionId.ShouldBeNull();
            var attempt = await verify.RunAttempts.SingleAsync(a => a.AgentSessionId == session.Id);
            attempt.Phase.ShouldBe(RunPhase.Canceled);
            var retry = await verify.RetrySchedules.SingleAsync(r => r.CardId == graph.Card.Id);
            retry.NextRetryAt.ShouldNotBeNull();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Orchestrator_launch_failure_releases_preclaim_and_schedules_retry()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            await using var harness = BuildHarness(tempRoot, []);

            var result = await harness.Orchestrator.PollTickAsync(CancellationToken.None);
            await Should.ThrowAsync<InvalidOperationException>(() =>
                harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None));

            result.Dispatched.ShouldBe(1);
            await using var verify = CreateContext();
            var card = await verify.Cards.SingleAsync(c => c.Id == graph.Card.Id);
            card.OwnerSessionId.ShouldBeNull();
            var session = await verify.AgentSessions.SingleAsync(s => s.CardId == graph.Card.Id);
            session.Status.ShouldBe(SessionStatus.Failed);
            var retry = await verify.RetrySchedules.SingleAsync(r => r.CardId == graph.Card.Id);
            retry.NextRetryAt.ShouldNotBeNull();
            retry.LastError.ShouldNotBeNull();
            retry.LastError!.ShouldContain("No fake adapter was queued");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task RetryScheduler_exhausted_max_attempts_is_not_due_or_eligible()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot);
            graph.Card.RetrySchedule = new RetrySchedule
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                AttemptCount = 3,
                MaxAttempts = 3,
                NextRetryAt = DateTime.UtcNow.AddSeconds(-1),
                LastError = "exhausted",
                Card = graph.Card
            };
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            await using var harness = BuildHarness(tempRoot, []);
            var state = await harness.Orchestrator.GetStateAsync(CancellationToken.None);
            var result = await harness.Orchestrator.PollTickAsync(CancellationToken.None);

            state.RetryQueueLength.ShouldBe(0);
            result.Dispatched.ShouldBe(0);
            result.EligibleCards.ShouldBe(0);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Reconcile_ignores_external_tracker_claims()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot);
            graph.Board.TrackerKind = TrackerKind.GitHubIssues;
            db.Add(graph.Project);
            await db.SaveChangesAsync();
            var session = NewSession(graph.Card.Id, SessionStatus.Running);
            graph.Card.OwnerSessionId = session.Id;
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();

            await using var harness = BuildHarness(tempRoot, []);

            var result = await harness.Orchestrator.PollTickAsync(CancellationToken.None);

            result.Reconciled.ShouldBe(0);
            await using var verify = CreateContext();
            var card = await verify.Cards.SingleAsync(c => c.Id == graph.Card.Id);
            card.OwnerSessionId.ShouldBe(session.Id);
            var storedSession = await verify.AgentSessions.SingleAsync(s => s.Id == session.Id);
            storedSession.Status.ShouldBe(SessionStatus.Running);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Reconcile_terminal_missing_runtime_clears_claim_without_retry()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();
            graph.Card.BoardColumnId = graph.DoneColumn.Id;
            graph.Card.BoardColumn = graph.DoneColumn;
            graph.Card.Status = CardStatus.Done;
            var session = NewSession(graph.Card.Id, SessionStatus.Running);
            graph.Card.OwnerSessionId = session.Id;
            db.AgentSessions.Add(session);
            db.RunAttempts.Add(new RunAttempt
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                AgentSessionId = session.Id,
                AttemptNumber = 1,
                Phase = RunPhase.StreamingTurn,
                CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow,
                LastEventAt = DateTime.UtcNow,
                PhaseStartedAt = DateTime.UtcNow,
                Prompt = "terminal missing runtime",
                Card = graph.Card,
                AgentSession = session
            });
            await db.SaveChangesAsync();

            await using var harness = BuildHarness(tempRoot, []);

            var result = await harness.Orchestrator.PollTickAsync(CancellationToken.None);

            result.Reconciled.ShouldBe(1);
            await using var verify = CreateContext();
            var card = await verify.Cards.SingleAsync(c => c.Id == graph.Card.Id);
            card.OwnerSessionId.ShouldBeNull();
            var attempt = await verify.RunAttempts.SingleAsync(a => a.AgentSessionId == session.Id);
            attempt.Phase.ShouldBe(RunPhase.Canceled);
            (await verify.RetrySchedules.CountAsync(r => r.CardId == graph.Card.Id)).ShouldBe(0);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Reconcile_cancels_attempt_when_card_externally_terminal()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();
            graph.Card.BoardColumnId = graph.DoneColumn.Id;
            graph.Card.BoardColumn = graph.DoneColumn;
            graph.Card.Status = CardStatus.Done;
            var session = NewSession(graph.Card.Id, SessionStatus.Running);
            graph.Card.OwnerSessionId = session.Id;
            db.AgentSessions.Add(session);
            db.RunAttempts.Add(new RunAttempt
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                AgentSessionId = session.Id,
                AttemptNumber = 1,
                Phase = RunPhase.StreamingTurn,
                CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow,
                LastEventAt = DateTime.UtcNow,
                PhaseStartedAt = DateTime.UtcNow,
                Prompt = "terminal reconcile",
                Card = graph.Card,
                AgentSession = session
            });
            await db.SaveChangesAsync();

            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [adapter]);
            harness.Runtime.Register(session.Id, adapter);

            var result = await harness.Orchestrator.PollTickAsync(CancellationToken.None);

            result.Reconciled.ShouldBe(1);
            adapter.Killed.ShouldBeTrue();
            adapter.Disposed.ShouldBeTrue();
            await using var verify = CreateContext();
            var card = await verify.Cards.SingleAsync(c => c.Id == graph.Card.Id);
            card.OwnerSessionId.ShouldBeNull();
            var attempt = await verify.RunAttempts.SingleAsync(a => a.AgentSessionId == session.Id);
            attempt.Phase.ShouldBe(RunPhase.Canceled);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Orchestrator_pause_skips_dispatch_until_resume()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "RESUMED" };
            await using var harness = BuildHarness(tempRoot, [adapter]);

            harness.Orchestrator.Pause().Paused.ShouldBeTrue();
            var paused = await harness.Orchestrator.PollTickAsync(CancellationToken.None);
            paused.Paused.ShouldBeTrue();
            adapter.Started.ShouldBeFalse();

            harness.Orchestrator.Resume().Paused.ShouldBeFalse();
            var resumed = await harness.Orchestrator.PollTickAsync(CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            resumed.Dispatched.ShouldBe(1);
            adapter.Started.ShouldBeTrue();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task OrchestratorTick_invokes_dispatch_at_configured_interval()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = CreateGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "HOSTED_TICK" };
            await using var harness = BuildHarness(tempRoot, [adapter], pollIntervalSeconds: 1);
            var hosted = new OrchestratorTickHostedService(
                harness.Provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new OrchestratorSettings { PollIntervalSeconds = 1 }),
                NullLogger<OrchestratorTickHostedService>.Instance);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await hosted.StartAsync(cts.Token);
            await WaitUntilAsync(() => adapter.Started, TimeSpan.FromSeconds(4));
            await hosted.StopAsync(CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentSessions.CountAsync(s => s.CardId == graph.Card.Id)).ShouldBe(1);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Harness BuildHarness(
        string tempRoot,
        IReadOnlyList<IAgentProtocolAdapter> adapters,
        int pollIntervalSeconds = 30)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
        {
            FirstDeltaTimeoutMs = 1_000,
            KillGraceMs = 100,
            SignalRMaxChunkChars = 16 * 1024,
            ReplayBufferMaxChars = 128 * 1024,
            SessionLogPath = Path.Combine(tempRoot, "session-logs")
        }));
        services.AddSingleton<IOptions<OrchestratorSettings>>(Options.Create(new OrchestratorSettings
        {
            PollIntervalSeconds = pollIntervalSeconds,
            MaxDispatchesPerTick = 10,
            DefaultCols = 120,
            DefaultRows = 30,
            InternalTrackerRepositoryPathPrefix = tempRoot
        }));
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
        {
            DefaultDefinition = "fake",
            Definitions = { ["fake"] = new AgentDefinition { Kind = "Raw", Exe = "fake" } }
        }));
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<IWorktreeManager>(new FakeWorktreeManager(Path.Combine(tempRoot, "worktrees")));
        services.AddSingleton<IAgentProtocolAdapterFactory>(new QueueAdapterFactory(adapters));
        services.AddSingleton<IWorkspaceHookRunner>(new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped<RetryScheduler>();
        services.AddSingleton<OrchestratorControlState>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddScoped<OrchestratorService>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<OrchestratorService>();
        var runtime = provider.GetRequiredService<AgentSessionRuntime>();
        var launchQueue = provider.GetRequiredService<AgentSessionLaunchQueue>();

        return new Harness(provider, scope, orchestrator, runtime, launchQueue);
    }

    private static Graph CreateGraph(
        string tempRoot,
        int boardMaxConcurrent = 2,
        int? columnMaxConcurrent = null)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(tempRoot, "repo"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        Directory.CreateDirectory(project.LocalRepositoryPath);

        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Board {Guid.NewGuid():N}",
            TrackerKind = TrackerKind.Internal,
            MaxConcurrentSessions = boardMaxConcurrent,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);

        var active = NewColumn(board, "in-progress", "In Progress", 0, CardStatus.InProgress, isActive: true, isTerminal: false);
        active.MaxConcurrentSessions = columnMaxConcurrent;
        var done = NewColumn(board, "done", "Done", 1, CardStatus.Done, isActive: false, isTerminal: true);
        board.Columns.Add(active);
        board.Columns.Add(done);

        var definition = new BoardWorkflowDefinition
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Version = 1,
            Name = "Default",
            Content = """
                name: E07
                stages:
                  - name: Run
                    executorType: raw
                """,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.WorkflowDefinitions.Add(definition);

        var card = AddCard(new Graph(project, board, active, done, null!), "E07", active);
        return new Graph(project, board, active, done, card);
    }

    private static Card AddCard(Graph graph, string prefix, BoardColumn column)
    {
        var now = DateTime.UtcNow;
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = graph.Board.Id,
            BoardColumnId = column.Id,
            Identifier = $"{prefix}-{Guid.NewGuid():N}"[..20],
            Title = $"Card {prefix}",
            Description = "Run from orchestrator",
            Priority = 1,
            Status = column.CardStatus,
            CreatedAt = now,
            UpdatedAt = now,
            Board = graph.Board,
            BoardColumn = column
        };
        graph.Board.Cards.Add(card);
        column.Cards.Add(card);
        return card;
    }

    private static BoardColumn NewColumn(
        Board board,
        string stateKey,
        string name,
        int order,
        CardStatus status,
        bool isActive,
        bool isTerminal)
    {
        var now = DateTime.UtcNow;
        return new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = stateKey,
            Name = name,
            ColumnOrder = order,
            CardStatus = status,
            IsActive = isActive,
            IsTerminal = isTerminal,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
    }

    private static AgentSession NewSession(Guid cardId, SessionStatus status)
    {
        var now = DateTime.UtcNow;
        return new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            DefinitionName = "fake",
            AgentKind = AgentKind.Raw,
            Status = status,
            Cwd = $"D:/worktrees/card-{cardId:N}",
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now
        };
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-orchestrator-tests-{Guid.NewGuid():N}");

    private static async Task CleanupProjectsByTempRootAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        if (projectIds.Count == 0)
            return;

        var boardIds = await db.Boards
            .Where(b => projectIds.Contains(b.ProjectId))
            .Select(b => b.Id)
            .ToListAsync();
        var cardIds = await db.Cards
            .Where(c => boardIds.Contains(c.BoardId))
            .Select(c => c.Id)
            .ToListAsync();
        var sessionIds = await db.AgentSessions
            .Where(s => cardIds.Contains(s.CardId))
            .Select(s => s.Id)
            .ToListAsync();
        var worktreeIds = await db.Worktrees
            .Where(w => cardIds.Contains(w.CardId))
            .Select(w => w.Id)
            .ToListAsync();
        var attemptIds = await db.RunAttempts
            .Where(a => cardIds.Contains(a.CardId))
            .Select(a => a.Id)
            .ToListAsync();

        await db.Cards
            .Where(c => cardIds.Contains(c.Id))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(c => c.OwnerSessionId, (Guid?)null)
                .SetProperty(c => c.CurrentWorktreeId, (Guid?)null));
        await db.TokenUsages
            .Where(t => attemptIds.Contains(t.RunAttemptId))
            .ExecuteDeleteAsync();
        await db.RetrySchedules
            .Where(r => cardIds.Contains(r.CardId))
            .ExecuteDeleteAsync();
        await db.ExternalIssueRefs
            .Where(r => cardIds.Contains(r.CardId))
            .ExecuteDeleteAsync();
        await db.RunAttempts
            .Where(a => attemptIds.Contains(a.Id))
            .ExecuteDeleteAsync();
        await db.AgentSessions
            .Where(s => sessionIds.Contains(s.Id))
            .ExecuteDeleteAsync();
        await db.Worktrees
            .Where(w => worktreeIds.Contains(w.Id))
            .ExecuteDeleteAsync();
        await db.Cards
            .Where(c => cardIds.Contains(c.Id))
            .ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions
            .Where(d => boardIds.Contains(d.BoardId))
            .ExecuteDeleteAsync();
        await db.BoardColumns
            .Where(c => boardIds.Contains(c.BoardId))
            .ExecuteDeleteAsync();
        await db.Boards
            .Where(b => boardIds.Contains(b.Id))
            .ExecuteDeleteAsync();
        await db.Projects
            .Where(p => projectIds.Contains(p.Id))
            .ExecuteDeleteAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(50);
        }

        predicate().ShouldBeTrue();
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp worktree/session directories.
        }
    }

    private sealed record Graph(
        Project Project,
        Board Board,
        BoardColumn ActiveColumn,
        BoardColumn DoneColumn,
        Card Card);

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(
            ServiceProvider provider,
            IServiceScope scope,
            OrchestratorService orchestrator,
            AgentSessionRuntime runtime,
            AgentSessionLaunchQueue launchQueue)
        {
            Provider = provider;
            Scope = scope;
            Orchestrator = orchestrator;
            Runtime = runtime;
            LaunchQueue = launchQueue;
        }

        public ServiceProvider Provider { get; }
        private IServiceScope Scope { get; }
        public OrchestratorService Orchestrator { get; }
        public AgentSessionRuntime Runtime { get; }
        public AgentSessionLaunchQueue LaunchQueue { get; }

        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    private sealed class QueueAdapterFactory : IAgentProtocolAdapterFactory
    {
        private readonly Queue<IAgentProtocolAdapter> _adapters;

        public QueueAdapterFactory(IEnumerable<IAgentProtocolAdapter> adapters)
        {
            _adapters = new Queue<IAgentProtocolAdapter>(adapters);
        }

        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            if (_adapters.TryDequeue(out var adapter))
                return adapter;

            throw new InvalidOperationException("No fake adapter was queued for dispatch.");
        }
    }

    private sealed class FakeWorktreeManager : IWorktreeManager
    {
        private readonly string _worktreeRoot;
        private readonly List<WorktreeInfo> _worktrees = [];

        public FakeWorktreeManager(string worktreeRoot)
        {
            _worktreeRoot = worktreeRoot;
        }

        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            Directory.CreateDirectory(_worktreeRoot);
            var worktreePath = Path.Combine(_worktreeRoot, $"card-{cardId}");
            Directory.CreateDirectory(worktreePath);
            var now = DateTimeOffset.UtcNow;
            var info = new WorktreeInfo(cardId, repoPath, worktreePath, $"feat/card-{cardId}", baseRef, now, now);
            _worktrees.Add(info);
            return Task.FromResult(info);
        }

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>(_worktrees.ToList());

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) =>
            Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) =>
            Task.FromResult(0);
    }

    private sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
    {
        public OptionsMonitorStub(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
