using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class StandingRestartEscalationTests
{
    [Test]
    [Arguments("infrastructure")]
    [Arguments("canceled")]
    [Arguments("unavailable")]
    [Arguments("preflight")]
    [Arguments("wrong-id")]
    [Arguments("wrong-generation")]
    [Arguments("recreate")]
    [Arguments("sync-after-commit")]
    public async Task C510_admission_and_outcome_boundaries_preserve_the_streak(string variant)
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var fault = new ReservationFailure();
        var adapter = new FakeAgentProtocolAdapter
        {
            ThrowOnStart = variant switch
            {
                "infrastructure" => new IOException("unavailable transport"),
                "canceled" => new OperationCanceledException("canceled launch"),
                "unavailable" => new AgentSessionService.ResumeTargetMissingException(),
                _ => new InvalidOperationException("confirmed admitted failure")
            }
        };
        try
        {
            Guid agentId;
            Guid id;
            await using (var h = AgentSupervisionTests.BuildHarness(root, [new FakeAgentProtocolAdapter(), adapter],
                definitionKind: "ClaudeCode", configureDb: b => b.AddInterceptors(fault)))
            {
                var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
                agentId = agent.Id;
                id = Guid.Parse((await h.Control.StartAsync(agent.Id, new(), default)).PersistentSessionId!);
                await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                await SessionExitObservation.ObserveMatchingAsync(h.Provider.GetRequiredService<AgentSessionRuntime>(),
                    id, 1, AgentExitReason.ProcessExited, AgentSupervisionTests.CreateContext);
                await using (var db = AgentSupervisionTests.CreateContext())
                {
                    var row = (await db.AgentSessions.FindAsync(id))!;
                    var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                    state.AutomaticResumeFailures = 2;
                    state.LastObservedRestartSessionId = id;
                    state.LastObservedRestartStartedAt = row.StartedAt;
                    state.NextRestartAt = h.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
                    fault.Before = row.StartedAt;
                    fault.SessionId = id;
                    await db.SaveChangesAsync();
                }
                h.Clock.Advance(TimeSpan.FromSeconds(1));
                if (variant == "preflight")
                {
                    h.Runner.ListOverride = _ => throw new TimeoutException("inventory unavailable");
                    await using var actionScope = h.Provider.CreateAsyncScope();
                    var refused = await Should.ThrowAsync<ServiceUnavailableException>(
                        actionScope.ServiceProvider.GetRequiredService<AgentControlService>()
                            .StartAsync(agent.Id, new(), default, automatic: true));
                    refused.Code.ShouldBe("standing_resume_runner_unavailable");
                    var state = await ReadAsync(agent.Id);
                    state.AutomaticResumeFailures.ShouldBe(2);
                    state.LastAutomaticResumeSessionId.ShouldBeNull();
                    adapter.Started.ShouldBeFalse();
                    return;
                }
                fault.Armed = variant == "sync-after-commit";
                await h.Supervisor().TickAsync(default);
                await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                var admitted = await ReadAsync(agent.Id);
                admitted.LastAutomaticResumeSessionId.ShouldBe(id);
                admitted.LastAutomaticResumeStartedAt.ShouldNotBe(fault.Before);
                if (variant == "sync-after-commit")
                {
                    fault.Hits.ShouldBe(1);
                    admitted.AutomaticResumeFailures.ShouldBe(3);
                    adapter.Started.ShouldBeFalse();
                    await SessionExitObservation.ObserveMatchingAsync(h.Provider.GetRequiredService<AgentSessionRuntime>(),
                        id, 1, AgentExitReason.ProcessExited, AgentSupervisionTests.CreateContext);
                }
                await using (var db = AgentSupervisionTests.CreateContext())
                {
                    var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                    state.NextRestartAt = null;
                    if (variant == "wrong-id") state.LastAutomaticResumeSessionId = Guid.NewGuid();
                    if (variant == "wrong-generation") state.LastAutomaticResumeStartedAt = fault.Before;
                    await db.SaveChangesAsync();
                }
                if (variant != "recreate")
                {
                    await h.Supervisor().TickAsync(default);
                    await h.Supervisor().TickAsync(default);
                    var state = await ReadAsync(agent.Id);
                    state.AutomaticResumeFailures.ShouldBe(variant == "sync-after-commit" ? 3 : 2);
                    if (variant == "unavailable") state.ContinuityReason.ShouldBe(StandingContinuityReason.NativeSessionMissing);
                    if (variant is "infrastructure" or "canceled") state.RestartBackoffFailures.ShouldBe(1);
                    return;
                }
            }
            await using var rebuilt = AgentSupervisionTests.BuildHarness(root, [], definitionKind: "ClaudeCode");
            await rebuilt.Supervisor().TickAsync(default);
            await rebuilt.Supervisor().TickAsync(default);
            var persisted = await ReadAsync(agentId);
            persisted.AutomaticResumeFailures.ShouldBe(3);
            persisted.LastAutomaticResumeSessionId.ShouldBe(id);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    [Arguments("healthy-before")]
    [Arguments("healthy-boundary")]
    [Arguments("starting")]
    [Arguments("running-incomplete")]
    [Arguments("manual-start")]
    [Arguments("manual-retry")]
    [Arguments("manual-select")]
    [Arguments("manual-fresh")]
    [Arguments("rejected-human")]
    [Arguments("automatic-accepted")]
    [Arguments("clear-only")]
    public async Task C510_health_and_human_acceptance_reset_the_episode(string variant)
    {
        var root = AgentSupervisionTests.NewTempRoot();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root,
                [new FakeAgentProtocolAdapter(), new FakeAgentProtocolAdapter()],
                new SupervisionSettings { HealthyUptimeResetMinutes = 5 }, definitionKind: "ClaudeCode");
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var id = Guid.Parse((await h.Control.StartAsync(agent.Id, new(), default)).PersistentSessionId!);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            await SessionExitObservation.ObserveMatchingAsync(h.Provider.GetRequiredService<AgentSessionRuntime>(),
                id, 1, AgentExitReason.ProcessExited, AgentSupervisionTests.CreateContext);
            DateTime marker;
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var session = (await db.AgentSessions.FindAsync(id))!;
                marker = session.StartedAt;
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.AutomaticResumeFailures = 3;
                state.LastAutomaticResumeSessionId = id;
                state.LastAutomaticResumeStartedAt = marker;
                state.AutomaticFreshConsumed = true;
                state.AutomaticFreshAlertedHoldAt = marker;
                if (variant.StartsWith("healthy-") || variant is "starting" or "running-incomplete")
                {
                    session.Status = variant == "starting" ? SessionStatus.Starting : SessionStatus.Running;
                    session.InteractiveLaunchCompletedAt = variant == "running-incomplete" ? null : marker;
                }
                await db.SaveChangesAsync();
            }
            var resets = variant == "healthy-boundary" || variant.StartsWith("manual-");
            if (variant.StartsWith("healthy-") || variant is "starting" or "running-incomplete")
                await TickAtAsync(h, marker.AddMinutes(5).AddTicks(variant == "healthy-before" ? -1 : 0));
            else if (variant == "clear-only")
            {
                await using var db = AgentSupervisionTests.CreateContext();
                new StandingContinuityState(db, h.Clock).Clear((await db.AgentSupervisionStates.FindAsync(agent.Id))!);
                await db.SaveChangesAsync();
            }
            else if (variant == "rejected-human")
            {
                h.Runner.ListOverride = _ => throw new TimeoutException("inventory unavailable");
                await using var actionScope = h.Provider.CreateAsyncScope();
                await Should.ThrowAsync<ServiceUnavailableException>(actionScope.ServiceProvider
                    .GetRequiredService<AgentControlService>().StartAsync(agent.Id, new(), default));
            }
            else
            {
                var request = variant switch
                {
                    "manual-retry" => new StartAgentRequest(RetryContinuity: true),
                    "manual-select" => new StartAgentRequest(ResumeSessionId: id),
                    "manual-fresh" => new StartAgentRequest(Fresh: true),
                    _ => new StartAgentRequest()
                };
                h.Clock.Advance(TimeSpan.FromSeconds(1));
                await using var actionScope = h.Provider.CreateAsyncScope();
                await actionScope.ServiceProvider.GetRequiredService<AgentControlService>()
                    .StartAsync(agent.Id, request, default, automatic: variant == "automatic-accepted");
                await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            }
            var after = await ReadAsync(agent.Id);
            after.AutomaticResumeFailures.ShouldBe(resets ? 0 : 3);
            after.AutomaticFreshConsumed.ShouldBe(!resets);
            after.AutomaticFreshAlertedHoldAt.ShouldBe(resets ? null : marker);
            if (resets)
            {
                after.LastAutomaticResumeSessionId.ShouldBeNull();
                after.LastAutomaticResumeStartedAt.ShouldBeNull();
            }
            else
            {
                after.LastAutomaticResumeSessionId.ShouldBe(id);
                if (variant == "automatic-accepted") after.LastAutomaticResumeStartedAt.ShouldNotBe(marker);
                else after.LastAutomaticResumeStartedAt.ShouldBe(marker);
            }
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    [Arguments(0)]
    [Arguments(3)]
    [Arguments(500)]
    public async Task C510_migration_starts_without_invented_history(int legacyCount)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        await using var db = new AppDbContext(options);
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        db.Agents.Add(new Agent { Id = id, Name = "Legacy", Slug = $"legacy-{id:N}",
            WorkingDirectory = Path.GetTempPath(), CreatedAt = now, UpdatedAt = now });
        db.AgentSupervisionStates.Add(new AgentSupervisionState { AgentId = id, UpdatedAt = now });
        await db.SaveChangesAsync();
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, m => m.EndsWith("_AddStandingAutomaticFreshRecovery"));
        index.ShouldBeGreaterThan(0);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[index - 1]);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "AgentSupervisionStates" SET "ContinuityResumeFailures" = {legacyCount}, "RestartBackoffFailures" = {legacyCount} WHERE "AgentId" = {id}""");
        await migrator.MigrateAsync();
        await using var verify = new AppDbContext(options);
        var state = (await verify.AgentSupervisionStates.FindAsync(id))!;
        state.ContinuityResumeFailures.ShouldBe(legacyCount);
        state.RestartBackoffFailures.ShouldBe(legacyCount);
        state.AutomaticResumeFailures.ShouldBe(0);
        state.LastAutomaticResumeSessionId.ShouldBeNull();
        state.LastAutomaticResumeStartedAt.ShouldBeNull();
        state.AutomaticFreshConsumed.ShouldBeFalse();
        state.AutomaticFreshAlertedHoldAt.ShouldBeNull();
        verify.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    // Freeze only the observation. Queue polling retains the harness's advancing clock.
    private async Task TickAtAsync(AgentSupervisionTests.Harness h, DateTime at)
    {
        await using var scope = h.Provider.CreateAsyncScope();
        var supervisor = ActivatorUtilities.CreateInstance<AgentSupervisorService>(
            scope.ServiceProvider, new FakeTimeProvider(new DateTimeOffset(at)));
        await supervisor.TickAsync(default);
    }

    private sealed class ReservationFailure : DbTransactionInterceptor
    {
        public Guid SessionId { get; set; }
        public DateTime Before { get; set; }
        public bool Armed { get; set; }
        public int Hits { get; private set; }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<AgentSession>().Any(e =>
                e.Entity.Id == SessionId && e.Entity.Status == SessionStatus.Starting && e.Entity.StartedAt != Before))
            {
                Armed = false;
                Hits++;
                throw new InvalidOperationException("committed reservation interrupted before enqueue");
            }
            return Task.CompletedTask;
        }
    }
}
