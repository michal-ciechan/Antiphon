using System.Data.Common;
using System.Net.Http;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class CapacityRecoverySupervisionTests
{
    [Test]
    [Timeout(180_000)]
    [Arguments("compaction-block")]
    [Arguments("duplicate-key")]
    public async Task C1593_S3_supervisor_skip_does_not_spend(string mode, CancellationToken ct)
    {
        await using var witness = await Witness.OpenAsync(ct, previous: true, reserve: false);
        if (mode == "duplicate-key")
        {
            await using var db = CapacityRecoveryTestSupport.CreateContext(witness.Schema);
            var session = await db.AgentSessions.SingleAsync(s => s.Id == witness.SessionId, ct);
            session.CapacityRecoveryActionKey = witness.Wait.ActionKey;
            await db.SaveChangesAsync(ct);
        }
        else
        {
            await SeedUnresolvedCompactionAsync(witness, ct);
        }

        var acted = await witness.TickAsync(ct);
        acted.ShouldBeFalse(witness.Log);
        await witness.AssertUntouchedAsync(ct);
        witness.Adapter.Started.ShouldBeFalse(witness.Log);
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("standing_resume_delivery_pending")]
    [Arguments("capacity_recovery_suspended")]
    [Arguments("herdr-hold")]
    public async Task C1593_S3_supervisor_refusal_releases_the_wait(string mode, CancellationToken ct)
    {
        var reserve = mode == "standing_resume_delivery_pending";
        var boundary = new ScriptedBoundary();
        await using var witness = await Witness.OpenAsync(ct, previous: true, reserve: reserve, boundary: boundary);
        if (mode == "standing_resume_delivery_pending")
        {
            await using var db = CapacityRecoveryTestSupport.CreateContext(witness.Schema);
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = witness.SessionId!.Value,
                Body = "attempted",
                Status = QueuedMessageStatus.Pending,
                Sequence = 1,
                Origin = QueuedMessageOrigin.Ui,
                DeliveryAttempts = 1,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
        else
        {
            boundary.Arm = "capacity-supervisor-before-start";
            boundary.OnReached = async (_, token) =>
            {
                await using var db = CapacityRecoveryTestSupport.CreateContext(witness.Schema);
                var state = await db.AgentSupervisionStates.SingleAsync(s => s.AgentId == witness.Agent.Id, token);
                if (mode == "capacity_recovery_suspended")
                    state.Suspended = true;
                else
                    state.HerdrFailureHeldAt = DateTime.UtcNow;
                await db.SaveChangesAsync(token);
            };
        }

        var before = await witness.ReadLadderAsync(ct);
        var acted = await witness.TickAsync(ct);
        acted.ShouldBeFalse(witness.Log);
        await using (var db = CapacityRecoveryTestSupport.CreateContext(witness.Schema))
        {
            var wait = await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == witness.Wait.Id, ct);
            wait.State.ShouldBe(CapacityRecoveryWaitState.Canceled);
            wait.OutcomeReason.ShouldBe(mode == "herdr-hold" ? "start-held" : "start-refused");
            wait.AdmissionCount.ShouldBe(0);
            (await db.CapacityRecoveryProviderStates.AsNoTracking().SingleAsync(s => s.Kind == AgentKind.ClaudeCode, ct))
                .GrantedWaitId.ShouldBeNull();
            if (mode == "standing_resume_delivery_pending")
            {
                var continuity = await db.AgentSupervisionStates.AsNoTracking().SingleAsync(s => s.AgentId == witness.Agent.Id, ct);
                continuity.ContinuityHeldAt.ShouldNotBeNull();
                continuity.ContinuityReason.ShouldBe(StandingContinuityReason.AutomaticFreshRefused);
            }

            if (mode == "herdr-hold")
            {
                (await db.AgentSupervisionStates.AsNoTracking().SingleAsync(s => s.AgentId == witness.Agent.Id, ct))
                    .HerdrFailureHeldAt.ShouldNotBeNull();
            }
        }

        witness.AssertClean();
        var again = await witness.TickAsync(ct);
        again.ShouldBeFalse(witness.Log);
        await using var verify = CapacityRecoveryTestSupport.CreateContext(witness.Schema);
        (await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == witness.Wait.Id, ct))
            .AdmissionCount.ShouldBe(0);
        var ladder = await witness.ReadLadderAsync(ct);
        if (mode != "standing_resume_delivery_pending")
        {
            ladder.Failures.ShouldBe(before.Failures);
            ladder.Next.ShouldBe(before.Next);
        }
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("commit-visibility")]
    [Arguments("consume-revoked")]
    [Arguments("precheck-revoked")]
    public async Task C1593_S3_supervisor_start_and_consume_commit_together(string mode, CancellationToken ct)
    {
        var visibility = new CommitVisibility();
        ScriptedBoundary? boundary = mode == "commit-visibility" ? null : new ScriptedBoundary();
        await using var witness = await Witness.OpenAsync(
            ct,
            previous: mode != "commit-visibility",
            reserve: mode == "precheck-revoked",
            boundary: boundary,
            interceptor: mode == "commit-visibility" ? visibility : null);
        if (mode == "commit-visibility")
            visibility.ConnectionString = witness.Schema.ConnectionString;
        if (boundary is not null)
        {
            boundary.Arm = mode == "precheck-revoked"
                ? "capacity-start-before-precheck"
                : "capacity-start-before-consume";
            boundary.OnReached = (_, token) => RevokeGrantAsync(witness.Schema, token);
        }

        var before = await witness.ReadLadderAsync(ct);
        var acted = await witness.TickAsync(ct);
        if (mode == "commit-visibility")
        {
            visibility.Seen.ShouldNotBeNull("the inserting transaction did not commit");
            visibility.Seen!.State.ShouldBe(CapacityRecoveryWaitState.ActionPending);
            visibility.Seen.AdmissionCount.ShouldBe(0);
            visibility.SeenGrantHeld.ShouldBeTrue();
            await witness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), ct);
            await using var db = CapacityRecoveryTestSupport.CreateContext(witness.Schema);
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.StandingAgentId == witness.Agent.Id, ct);
            var wait = await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == witness.Wait.Id, ct);
            wait.AdmissionCount.ShouldBe(1);
            wait.State.ShouldBe(CapacityRecoveryWaitState.Admitted);
            wait.SessionId.ShouldBe(session.Id);
            witness.Adapter.StartedSessionId.ShouldBe(session.Id);
            return;
        }

        acted.ShouldBeFalse(witness.Log);
        witness.Adapter.Started.ShouldBeFalse(witness.Log);
        await using (var db = CapacityRecoveryTestSupport.CreateContext(witness.Schema))
        {
            (await db.AgentSessions.CountAsync(
                s => s.StandingAgentId == witness.Agent.Id && s.Id != witness.SessionId, ct)).ShouldBe(0);
            var agent = await db.Agents.AsNoTracking().SingleAsync(a => a.Id == witness.Agent.Id, ct);
            agent.PersistentSessionId.ShouldBe(witness.SessionId?.ToString("D"));
            var wait = await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == witness.Wait.Id, ct);
            wait.State.ShouldBe(CapacityRecoveryWaitState.ActionPending);
            wait.ActionKey.ShouldBe(witness.Wait.ActionKey);
            wait.AdmissionCount.ShouldBe(0);
            var provider = await db.CapacityRecoveryProviderStates.AsNoTracking()
                .SingleAsync(s => s.Kind == AgentKind.ClaudeCode, ct);
            provider.GrantedWaitId.ShouldBeNull();
            provider.GrantedActionKey.ShouldBeNull();
            if (mode == "precheck-revoked")
            {
                (await db.AgentSupervisionStates.AsNoTracking().SingleAsync(s => s.AgentId == witness.Agent.Id, ct))
                    .AutomaticFreshConsumed.ShouldBeFalse();
            }
        }

        var ladder = await witness.ReadLadderAsync(ct);
        ladder.Failures.ShouldBe(before.Failures);
        ladder.Next.ShouldBe(before.Next);
        witness.AssertClean();
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("consume-refused-nonreserve")]
    [Arguments("transient-40P01")]
    [Arguments("canceled")]
    public async Task C1593_S3_supervisor_exit_without_attempt_neither_charges_nor_backs_off(string mode, CancellationToken ct)
    {
        var deadlock = new AgentSessionDeadlockOnce();
        var boundary = mode == "transient-40P01" ? null : new ScriptedBoundary();
        await using var witness = await Witness.OpenAsync(
            ct,
            previous: false,
            reserve: false,
            boundary: boundary,
            interceptor: mode == "transient-40P01" ? deadlock : null);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (mode == "canceled")
        {
            boundary!.Arm = "capacity-start-before-consume";
            boundary.OnReached = (_, _) =>
            {
                cancel.Cancel();
                throw new OperationCanceledException(cancel.Token);
            };
        }
        else if (mode == "consume-refused-nonreserve")
        {
            boundary!.Arm = "capacity-start-before-consume";
            boundary.OnReached = (_, token) => RevokeGrantAsync(witness.Schema, token);
        }

        var before = await witness.ReadLadderAsync(ct);
        if (mode == "canceled")
        {
            await Should.ThrowAsync<OperationCanceledException>(() => witness.TickAsync(cancel.Token));
        }
        else
        {
            var acted = await witness.TickAsync(ct);
            acted.ShouldBeFalse(witness.Log);
            witness.AssertClean();
        }

        witness.Adapter.Started.ShouldBeFalse(witness.Log);
        await witness.AssertUntouchedAsync(ct);
        var ladder = await witness.ReadLadderAsync(ct);
        ladder.Failures.ShouldBe(before.Failures);
        ladder.Next.ShouldBe(before.Next);
        (await witness.SessionCountAsync(ct)).ShouldBe(0);

        if (mode != "transient-40P01")
            return;

        deadlock.Spent.ShouldBeTrue();
        var started = await witness.TickAsync(ct);
        started.ShouldBeTrue(witness.Log);
        await witness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), ct);
        witness.Adapter.Started.ShouldBeTrue(witness.Log);
        await using var db = CapacityRecoveryTestSupport.CreateContext(witness.Schema);
        (await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == witness.Wait.Id, ct))
            .AdmissionCount.ShouldBe(1);
    }

    [Test]
    [Timeout(300_000)]
    public async Task C1593_S3_supervisor_start_throw_charges_one_attempt(CancellationToken ct)
    {
        var calls = 0;
        await using var witness = await Witness.OpenAsync(ct, previous: true, reserve: false);
        witness.Harness.Runner.ListOverride = _ =>
        {
            var n = Interlocked.Increment(ref calls);
            if (n is 2 or 4 or 6)
                throw new HttpRequestException("c1593-s3-runner-list");
            return Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        };

        for (var expected = 1; expected <= 3; expected++)
        {
            var acted = await witness.TickAsync(ct);
            acted.ShouldBeTrue(witness.Log);
            await using var db = CapacityRecoveryTestSupport.CreateContext(witness.Schema);
            var wait = await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == witness.Wait.Id, ct);
            wait.AdmissionCount.ShouldBe(expected);
            wait.State.ShouldBe(CapacityRecoveryWaitState.Admitted);
            (await db.CapacityRecoveryProviderStates.AsNoTracking().SingleAsync(s => s.Kind == AgentKind.ClaudeCode, ct))
                .GrantedWaitId.ShouldBeNull();
            if (expected == 3)
                break;
            witness.Harness.Clock.Advance(TimeSpan.FromSeconds(121));
            await witness.Recovery.ReconcileAsync(ct);
        }

        witness.Harness.Clock.Advance(TimeSpan.FromSeconds(121));
        await witness.Recovery.ReconcileAsync(ct);
        await using (var db = CapacityRecoveryTestSupport.CreateContext(witness.Schema))
        {
            (await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == witness.Wait.Id, ct))
                .State.ShouldBe(CapacityRecoveryWaitState.Exhausted);
        }

        var ungated = await witness.TickAsync(ct);
        ungated.ShouldBeTrue(witness.Log);
        await witness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), ct);
        witness.Adapter.Started.ShouldBeTrue(witness.Log);
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("committed")]
    [Arguments("not-committed")]
    [Arguments("in-doubt")]
    public async Task C1593_S3_supervisor_lost_commit_ack_launches_once(string mode, CancellationToken ct)
    {
        DbTransactionInterceptor interceptor = mode == "in-doubt"
            ? new InDoubtStandIn()
            : new ReservationCommitFault(mode == "committed");
        await using var witness = await Witness.OpenAsync(
            ct,
            previous: mode == "in-doubt",
            reserve: false,
            interceptor: interceptor);
        var before = await witness.ReadLadderAsync(ct);
        if (interceptor is InDoubtStandIn doubt)
        {
            doubt.ConnectionString = witness.Schema.ConnectionString;
            doubt.AgentId = witness.Agent.Id;
            doubt.WaitId = witness.Wait.Id;
        }

        var acted = await witness.TickAsync(ct);
        if (interceptor is InDoubtStandIn standIn)
        {
            standIn.TickDone.TrySetResult();
            await standIn.Completed.WaitAsync(TimeSpan.FromSeconds(40), ct);
        }

        await witness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), ct);
        await using var db = CapacityRecoveryTestSupport.CreateContext(witness.Schema);
        var wait = await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == witness.Wait.Id, ct);
        var ladder = await witness.ReadLadderAsync(ct);
        if (mode == "not-committed")
        {
            acted.ShouldBeTrue(witness.Log);
            (await db.AgentSessions.CountAsync(s => s.StandingAgentId == witness.Agent.Id, ct)).ShouldBe(0);
            wait.AdmissionCount.ShouldBe(1);
            ladder.Failures.ShouldBeGreaterThan(before.Failures);
            ladder.Next.ShouldNotBeNull();
            witness.Adapter.Started.ShouldBeFalse(witness.Log);
            return;
        }

        wait.AdmissionCount.ShouldBe(1);
        wait.State.ShouldBe(CapacityRecoveryWaitState.Admitted);
        ladder.Failures.ShouldBe(before.Failures);
        ladder.Next.ShouldBe(before.Next);
        witness.Adapter.Started.ShouldBeTrue(witness.Log);
        witness.Adapter.StartedAcceptedGeneration.ShouldNotBeNull();
        if (mode == "in-doubt")
        {
            var stand = (InDoubtStandIn)interceptor;
            witness.Adapter.StartedSessionId.ShouldBe(witness.SessionId);
            witness.Adapter.StartedAcceptedGeneration.ShouldBe(stand.AcceptedGeneration);
            return;
        }

        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.StandingAgentId == witness.Agent.Id, ct);
        wait.SessionId.ShouldBe(session.Id);
        witness.Adapter.StartedSessionId.ShouldBe(session.Id);
        witness.Adapter.StartedAcceptedGeneration.ShouldBe(session.StartedAt);
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("spends-after-spawn")]
    [Arguments("consume-throws")]
    public async Task C1593_S3_supervisor_card_start_spends_after_spawn(string mode, CancellationToken ct)
    {
        var fault = new ArmedWaitUpdateFault();
        var boundary = new ScriptedBoundary { Arm = "capacity-card-before-consume" };
        if (mode == "consume-throws")
            boundary.OnReached = (_, _) =>
            {
                fault.Arm();
                return Task.CompletedTask;
            };
        await using var witness = await Witness.OpenAsync(
            ct, previous: false, reserve: false, boundary: boundary,
            interceptor: mode == "consume-throws" ? fault : null);
        await SeedSpawnableCardAsync(witness, ct);
        var before = await witness.ReadLadderAsync(ct);
        var acted = await witness.TickAsync(ct);
        acted.ShouldBeTrue(witness.Log);
        await witness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), ct);
        await using var db = CapacityRecoveryTestSupport.CreateContext(witness.Schema);
        var spawn = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.CardId != null, ct);
        var agent = await db.Agents.AsNoTracking().SingleAsync(a => a.Id == witness.Agent.Id, ct);
        agent.PersistentSessionId.ShouldBe(spawn.Id.ToString("D"));
        agent.Status.ShouldBe(AgentStatus.Running);
        var wait = await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == witness.Wait.Id, ct);
        var ladder = await witness.ReadLadderAsync(ct);
        ladder.Failures.ShouldBe(before.Failures);
        ladder.Next.ShouldBe(before.Next);
        if (mode == "spends-after-spawn")
        {
            wait.AdmissionCount.ShouldBe(1);
            wait.State.ShouldBe(CapacityRecoveryWaitState.Admitted);
            wait.SessionId.ShouldBe(spawn.Id);
            (await db.CapacityRecoveryProviderStates.AsNoTracking().SingleAsync(s => s.Kind == AgentKind.ClaudeCode, ct))
                .GrantedWaitId.ShouldBeNull();
            return;
        }

        witness.AssertClean();
        wait.AdmissionCount.ShouldBe(0);
        wait.State.ShouldBe(CapacityRecoveryWaitState.ActionPending);
        (await db.CapacityRecoveryProviderStates.AsNoTracking().SingleAsync(s => s.Kind == AgentKind.ClaudeCode, ct))
            .GrantedWaitId.ShouldBe(witness.Wait.Id);
    }

    private static async Task RevokeGrantAsync(IsolatedTestSchema schema, CancellationToken ct)
    {
        await using var db = CapacityRecoveryTestSupport.CreateContext(schema);
        var state = await db.CapacityRecoveryProviderStates.SingleAsync(s => s.Kind == AgentKind.ClaudeCode, ct);
        state.GrantedWaitId = null;
        state.GrantedActionKey = null;
        state.GrantedAt = null;
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedUnresolvedCompactionAsync(Witness witness, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var episodeId = Guid.NewGuid();
        await using var db = CapacityRecoveryTestSupport.CreateContext(witness.Schema);
        var state = await db.AgentSupervisionStates.SingleOrDefaultAsync(s => s.AgentId == witness.Agent.Id, ct);
        if (state is null)
        {
            state = new AgentSupervisionState { AgentId = witness.Agent.Id, UpdatedAt = now };
            db.AgentSupervisionStates.Add(state);
        }

        state.ActiveCompactionRecoveryId = episodeId;
        state.UpdatedAt = now;
        db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
        {
            Id = episodeId,
            PhysicalAgentId = witness.Agent.Id,
            SessionId = witness.SessionId!.Value,
            AcceptedStartedAt = witness.SessionStartedAt!.Value,
            BoundaryIdentity = "c1593-s3-boundary",
            BoundaryCreatedAt = now,
            ContinuationCreatedAt = now,
            DetectedAt = now,
            State = CheckCompactionRecoveryState.Confirmed,
            ConfiguredThresholdMinutes = 10,
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedSpawnableCardAsync(Witness witness, CancellationToken ct)
    {
        await using var db = CapacityRecoveryTestSupport.CreateContext(witness.Schema);
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "c1593-s3-card",
            LocalRepositoryPath = witness.TempRoot,
            CreatedAt = DateTime.UtcNow,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "c1593-s3",
            CreatedAt = DateTime.UtcNow,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Name = "Doing",
            StateKey = "in-progress",
            CardStatus = CardStatus.InProgress,
            IsActive = true,
            IsTerminal = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = "C1593-" + Guid.NewGuid().ToString("N")[..8],
            Title = "standing card",
            Description = "spawn me",
            Status = CardStatus.InProgress,
            AssignedAgentId = witness.Agent.Id,
            AgentQueuePosition = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Projects.Add(project);
        db.Boards.Add(board);
        db.BoardColumns.Add(column);
        db.Cards.Add(card);
        await db.SaveChangesAsync(ct);
    }

    private sealed class Witness : IAsyncDisposable
    {
        public required string TempRoot { get; init; }
        public required IsolatedTestSchema Schema { get; init; }
        public required AgentSupervisionTests.Harness Harness { get; init; }
        public required FakeAgentProtocolAdapter Adapter { get; init; }
        public required AgentDetailDto Agent { get; init; }
        public required CapacityRecoveryService Recovery { get; init; }
        public required CapacityRecoveryWait Wait { get; init; }
        public Guid? SessionId { get; init; }
        public DateTime? SessionStartedAt { get; init; }
        private IServiceScope? _tickScope;

        public AgentSessionLaunchQueue LaunchQueue => Harness.LaunchQueue;
        public string Log => string.Join(Environment.NewLine, Harness.SupervisorLog);

        public static async Task<Witness> OpenAsync(
            CancellationToken ct,
            bool previous,
            bool reserve,
            ScriptedBoundary? boundary = null,
            IInterceptor? interceptor = null)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var tempRoot = AgentSupervisionTests.NewTempRoot();
            var adapter = new FakeAgentProtocolAdapter();
            try
            {
                var harness = AgentSupervisionTests.BuildHarness(
                    tempRoot,
                    [adapter],
                    definitionKind: "ClaudeCode",
                    connectionString: schema.ConnectionString,
                    configureDb: interceptor is null ? null : options => options.AddInterceptors(interceptor),
                    supervision: new SupervisionSettings
                    {
                        TickSeconds = 1,
                        BackoffBaseSeconds = 5,
                        HealthyUptimeResetMinutes = 10,
                        FreshAfterResumeFailures = 2,
                        AutomaticFreshAfterResumeFailures = reserve ? 2 : 0,
                        CapacityRecovery = new CapacityRecoverySettings { Enabled = true, JitterSeconds = 0 },
                    },
                    configure: boundary is null
                        ? null
                        : services => services.AddSingleton<LandDeliveryBoundary>(boundary));
                var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(harness, tempRoot);
                Guid? sessionId = null;
                DateTime? started = null;
                await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
                {
                    var owner = await db.Agents.SingleAsync(a => a.Id == agent.Id, ct);
                    owner.Kind = AgentKind.ClaudeCode;
                    if (previous)
                    {
                        sessionId = Guid.NewGuid();
                        started = DateTime.UtcNow.AddMinutes(-10);
                        owner.PersistentSessionId = sessionId.Value.ToString("D");
                        db.AgentSessions.Add(new AgentSession
                        {
                            Id = sessionId.Value,
                            AgentKind = AgentKind.ClaudeCode,
                            DefinitionName = "fake",
                            Cwd = owner.WorkingDirectory,
                            Status = SessionStatus.Failed,
                            CreatedAt = started.Value,
                            StartedAt = started.Value,
                        });
                        if (reserve)
                        {
                            db.AgentSupervisionStates.Add(new AgentSupervisionState
                            {
                                AgentId = agent.Id,
                                AutomaticResumeFailures = 3,
                                LastAutomaticResumeSessionId = sessionId,
                                LastAutomaticResumeStartedAt = started,
                                UpdatedAt = DateTime.UtcNow,
                            });
                        }
                    }

                    await db.SaveChangesAsync(ct);
                }

                var recovery = harness.Provider.GetRequiredService<CapacityRecoveryService>();
                var wait = await recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                    $"agent:{agent.Id:N}", CapacityWaitConsumerKind.StandingStart,
                    holdAlreadyCleared: true, agentId: agent.Id, sessionId: sessionId), ct);
                await recovery.ReconcileAsync(ct);
                return new Witness
                {
                    TempRoot = tempRoot,
                    Schema = schema,
                    Harness = harness,
                    Adapter = adapter,
                    Agent = agent,
                    Recovery = recovery,
                    Wait = wait,
                    SessionId = sessionId,
                    SessionStartedAt = started,
                };
            }
            catch
            {
                await schema.DisposeAsync();
                await AgentSupervisionTests.CleanupAsync(tempRoot);
                throw;
            }
        }

        public async Task<bool> TickAsync(CancellationToken ct)
        {
            _tickScope?.Dispose();
            _tickScope = Harness.Provider.CreateScope();
            var supervisor = _tickScope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            var actions = await supervisor.TickAsync(ct);
            return actions > 0;
        }

        public void AssertClean()
        {
            var db = _tickScope!.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ShouldBeFalse(Log);
        }

        public async Task AssertUntouchedAsync(CancellationToken ct)
        {
            await using var db = CapacityRecoveryTestSupport.CreateContext(Schema);
            var wait = await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == Wait.Id, ct);
            wait.AdmissionCount.ShouldBe(0);
            wait.State.ShouldBe(CapacityRecoveryWaitState.ActionPending);
            wait.ActionKey.ShouldBe(Wait.ActionKey);
            (await db.CapacityRecoveryProviderStates.AsNoTracking().SingleAsync(s => s.Kind == AgentKind.ClaudeCode, ct))
                .GrantedWaitId.ShouldBe(Wait.Id);
        }

        public async Task<int> SessionCountAsync(CancellationToken ct)
        {
            await using var db = CapacityRecoveryTestSupport.CreateContext(Schema);
            return await db.AgentSessions.CountAsync(s => s.StandingAgentId == Agent.Id, ct);
        }

        public async Task<Ladder> ReadLadderAsync(CancellationToken ct)
        {
            await using var db = CapacityRecoveryTestSupport.CreateContext(Schema);
            var state = await db.AgentSupervisionStates.AsNoTracking()
                .SingleOrDefaultAsync(s => s.AgentId == Agent.Id, ct);
            return new Ladder(state?.RestartBackoffFailures ?? 0, state?.NextRestartAt);
        }

        public async ValueTask DisposeAsync()
        {
            _tickScope?.Dispose();
            await Harness.DisposeAsync();
            await Schema.DisposeAsync();
            await AgentSupervisionTests.CleanupAsync(TempRoot);
        }
    }

    private readonly record struct Ladder(int Failures, DateTime? Next);

    private sealed class ScriptedBoundary : LandDeliveryBoundary
    {
        public string? Arm { get; set; }
        public Func<string, CancellationToken, Task>? OnReached { get; set; }

        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (Arm is null || !string.Equals(boundary, Arm, StringComparison.Ordinal) || OnReached is null)
                return;
            await OnReached(boundary, ct);
        }
    }

    /// <summary>
    /// One AgentSessions non-query on the supervisor context. The base key stamp is that
    /// write; after the fix it is the reservation's session save.
    /// </summary>
    private sealed class AgentSessionDeadlockOnce : DbCommandInterceptor
    {
        public bool Spent { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Spent
                && eventData.Context?.ChangeTracker.Entries<Agent>().Any() == true
                && command.CommandText.Contains("AgentSessions", StringComparison.Ordinal))
            {
                Spent = true;
                return ValueTask.FromException<InterceptionResult<int>>(
                    new PostgresException("c1593-40P01", "ERROR", "ERROR", "40P01"));
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class ArmedWaitUpdateFault : DbCommandInterceptor
    {
        private int _armed;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) == 1
                && Interlocked.Exchange(ref _armed, 0) == 1
                && command.CommandText.Contains("CapacityRecoveryWaits", StringComparison.Ordinal))
            {
                return ValueTask.FromException<InterceptionResult<int>>(
                    new PostgresException("c1593-card-consume", "ERROR", "ERROR", "XX000"));
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class CommitVisibility : DbTransactionInterceptor
    {
        public string ConnectionString { get; set; } = "";
        public CapacityRecoveryWait? Seen { get; private set; }
        public bool SeenGrantHeld { get; private set; }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AgentSession>()
                    .Any(entry => entry.State == EntityState.Added) == true)
            {
                await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(ConnectionString));
                Seen = await db.CapacityRecoveryWaits.AsNoTracking()
                    .SingleAsync(cancellationToken);
                var grant = await db.CapacityRecoveryProviderStates.AsNoTracking()
                    .SingleAsync(s => s.Kind == AgentKind.ClaudeCode, cancellationToken);
                SeenGrantHeld = grant.GrantedWaitId == Seen.Id;
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class ReservationCommitFault(bool afterCommit) : DbTransactionInterceptor
    {
        private bool _reservation;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!ReservationSession(eventData.Context))
                return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
            if (!afterCommit)
            {
                return ValueTask.FromException<InterceptionResult>(
                    new PostgresException("c1593-not-committed", "ERROR", "ERROR", "XX000"));
            }

            _reservation = true;
            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!_reservation)
                return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
            _reservation = false;
            return Task.FromException(
                new PostgresException("c1593-committed", "ERROR", "ERROR", "XX000"));
        }

        private static bool ReservationSession(DbContext? context) =>
            context?.ChangeTracker.Entries<AgentSession>().Any(entry =>
                entry.State == EntityState.Added
                || entry.State == EntityState.Modified && entry.Entity.Status == SessionStatus.Starting) == true;
    }

    private sealed class InDoubtStandIn : DbTransactionInterceptor
    {
        public string ConnectionString { get; set; } = "";
        public Guid AgentId { get; set; }
        public Guid WaitId { get; set; }
        public DateTime? AcceptedGeneration { get; private set; }
        public TaskCompletionSource TickDone { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Completed { get; private set; } = Task.CompletedTask;

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;
            var session = context?.ChangeTracker.Entries<AgentSession>()
                .FirstOrDefault(entry => entry.State == EntityState.Modified && entry.Entity.Status == SessionStatus.Starting)
                ?.Entity;
            if (session is null)
                return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);

            AcceptedGeneration = session.StartedAt;
            var acceptedId = session.Id;
            var generation = session.StartedAt;
            var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Completed = Task.Run(() => StandInAsync(acceptedId, generation, ready));
            var pid = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < until)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await WaitingOnLockAsync(pid, cancellationToken))
                    break;
                await Task.Delay(25, cancellationToken);
            }

            throw new PostgresException("c1593-in-doubt", "ERROR", "ERROR", "XX000");
        }

        private async Task StandInAsync(Guid sessionId, DateTime generation, TaskCompletionSource<int> ready)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using (var pidCommand = new NpgsqlCommand("SELECT pg_backend_pid()", connection))
                ready.TrySetResult((int)(await pidCommand.ExecuteScalarAsync())!);
            await using var tx = await connection.BeginTransactionAsync();
            await using (var hold = new NpgsqlCommand(
                """SELECT "Id" FROM "Agents" WHERE "Id" = @id FOR UPDATE""", connection, tx))
            {
                hold.Parameters.AddWithValue("id", AgentId);
                await hold.ExecuteScalarAsync();
            }

            await using (var write = new NpgsqlCommand(
                """
                UPDATE "AgentSessions" SET "StartedAt" = @gen, "Status" = 1 WHERE "Id" = @session;
                UPDATE "CapacityRecoveryWaits"
                   SET "State" = 3, "AdmissionCount" = 1, "SessionId" = @session,
                       "SessionStartedAt" = @gen, "NeedsRevalidationGrant" = FALSE,
                       "Outcome" = 'Admitted', "OutcomeReason" = 'Start'
                 WHERE "Id" = @wait;
                UPDATE "CapacityRecoveryProviderStates"
                   SET "GrantedWaitId" = NULL, "GrantedActionKey" = NULL, "GrantedAt" = NULL
                 WHERE "Kind" = 1;
                """, connection, tx))
            {
                write.Parameters.AddWithValue("gen", generation);
                write.Parameters.AddWithValue("session", sessionId);
                write.Parameters.AddWithValue("wait", WaitId);
                await write.ExecuteNonQueryAsync();
            }

            var until = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < until && !TickDone.Task.IsCompleted)
            {
                await using var waiter = new NpgsqlCommand(
                    """
                    SELECT 1
                    FROM pg_locks
                    WHERE relation = '"Agents"'::regclass AND NOT granted AND pid <> pg_backend_pid()
                    LIMIT 1
                    """, connection, tx);
                if (await waiter.ExecuteScalarAsync() is not null)
                    break;
                await Task.Delay(25);
            }

            await tx.CommitAsync();
        }

        private async Task<bool> WaitingOnLockAsync(int pid, CancellationToken ct)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                """SELECT wait_event_type FROM pg_stat_activity WHERE pid = @pid""", connection);
            command.Parameters.AddWithValue("pid", pid);
            var value = await command.ExecuteScalarAsync(ct);
            return value is string kind && kind == "Lock";
        }
    }
}
