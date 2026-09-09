using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public partial class StandingSessionSelectionTests
{
    [Test]
    [Arguments("Created")] [Arguments("Starting")] [Arguments("Running")] [Arguments("Stopping")]
    [Arguments("kind")] [Arguments("cwd")] [Arguments("pool")]
    [Arguments("worker")] [Arguments("runner-live")] [Arguments("runner-unavailable")]
    [Arguments("current-live")] [Arguments("current-worker")]
    [Arguments("card")] [Arguments("worktree")] [Arguments("queued-card")] [Arguments("current-card")]
    public async Task Invalid_target_matrix_preserves_pointer_generation_hold_and_queue(string shape)
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(adapter); await f.SeedAsync(held: true);
        var expected = "standing_resume_target_active";
        await using (var db = f.Db())
        {
            var a = (await db.AgentSessions.FindAsync(f.A.Id))!;
            var agent = (await db.Agents.FindAsync(f.Agent.Id))!;
            if (Enum.TryParse<SessionStatus>(shape, out var status)) a.Status = status;
            if (shape == "kind") { a.AgentKind = AgentKind.Grok; expected = "standing_resume_incompatible"; }
            if (shape == "cwd") { a.Cwd = Path.Combine(f.Root, "other"); expected = "standing_resume_incompatible"; }
            if (shape == "pool") { agent.IsPoolDelegate = true; expected = "standing_resume_ineligible"; }
            if (shape == "current-live") (await db.AgentSessions.FindAsync(f.B.Id))!.Status = SessionStatus.Running;
            if (shape is "current-live" or "current-worker") expected = "standing_resume_current_active";
            if (shape is "card" or "worktree" or "queued-card" or "current-card")
            {
                var card = SeedCard(db, f.Root);
                if (shape == "card") a.CardId = card.Id;
                if (shape == "worktree")
                {
                    var tree = new Worktree { Id = Guid.NewGuid(), CardId = card.Id, Path = f.Root, RepoPath = f.Root,
                        Branch = "synthetic", CreatedAt = DateTime.UtcNow, LastTouchedAt = DateTime.UtcNow };
                    db.Worktrees.Add(tree); a.WorktreeId = tree.Id;
                }
                if (shape == "queued-card") { card.AssignedAgentId = agent.Id; card.AgentQueuePosition = 1; }
                if (shape == "current-card") agent.CurrentCardId = card.Id;
                expected = shape is "card" or "worktree" ? "standing_resume_ineligible" : "standing_resume_card_work_pending";
            }
            await db.SaveChangesAsync();
        }
        if (shape == "worker") f.Harness.LaunchQueue.TryRegister(f.A.Id).ShouldBeTrue();
        if (shape == "current-worker") f.Harness.LaunchQueue.TryRegister(f.B.Id).ShouldBeTrue();
        if (shape == "runner-live") f.Harness.Runner.ListOverride = _ => Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>(
            [new(f.A.Id, 123, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0)]);
        if (shape == "runner-unavailable") f.Harness.Runner.ListOverride = _ => throw new HttpRequestException("synthetic offline runner");
        try
        {
            if (shape == "runner-unavailable")
                (await Should.ThrowAsync<ServiceUnavailableException>(() => f.StartAsync(new(ResumeSessionId: f.A.Id))))
                    .Code.ShouldBe("standing_resume_runner_unavailable");
            else (await Should.ThrowAsync<ConflictException>(() => f.StartAsync(new(ResumeSessionId: f.A.Id)))).Code.ShouldBe(expected);
            adapter.Started.ShouldBeFalse(); adapter.Killed.ShouldBeFalse(); adapter.Inputs.ShouldBeEmpty();
            await using var verify = f.Db();
            (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(f.B.Id.ToString("D"));
            (await verify.AgentSessions.FindAsync(f.A.Id))!.StartedAt.ShouldBe(f.A.StartedAt, TimeSpan.FromMilliseconds(1));
            (await verify.AgentSessions.FindAsync(f.A.Id))!.StandingAgentId.ShouldBe(f.Agent.Id);
            (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!.ContinuityHeldAt.ShouldNotBeNull();
            (await verify.AgentIncidents.CountAsync(i => i.AgentId == f.Agent.Id && i.Kind == AgentIncidentKind.StandingResumeSelected)).ShouldBe(0);
        }
        finally { f.Harness.LaunchQueue.Unregister(f.A.Id); f.Harness.LaunchQueue.Unregister(f.B.Id); }
    }

    internal static Card SeedCard(Antiphon.Server.Infrastructure.Data.AppDbContext db, string root)
    {
        var project = new Project { Id = Guid.NewGuid(), Name = $"Synthetic recovery {Guid.NewGuid():N}", LocalRepositoryPath = root, CreatedAt = DateTime.UtcNow };
        var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "Synthetic", CreatedAt = DateTime.UtcNow };
        var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Ready", StateKey = "ready", CreatedAt = DateTime.UtcNow };
        var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
            Identifier = "CARD-0001", Title = "Synthetic work", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Projects.Add(project); db.Boards.Add(board); db.BoardColumns.Add(column); db.Cards.Add(card);
        return card;
    }

    [Test]
    public async Task Equivalent_canonical_cwd_is_accepted_without_reusing_historical_arguments()
    {
        var adapter = new FakeAgentProtocolAdapter(); await using var f = new StandingRecoveryFixture(adapter); await f.SeedAsync();
        await using (var db = f.Db())
        {
            (await db.AgentSessions.FindAsync(f.A.Id))!.Cwd = f.Root + Path.DirectorySeparatorChar;
            await db.SaveChangesAsync();
        }
        await f.StartAsync(new(ResumeSessionId: f.A.Id)); await f.IdleAsync();
        adapter.StartedSessionId.ShouldBe(f.A.Id); adapter.StartedArgs.ShouldContain("--resume");
        adapter.StartedArgs.ShouldNotContain("--session-id");
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Unsupported_native_kind_keeps_ordinary_compatibility_but_requires_fresh_after_supported_history(bool priorSupported)
    {
        var adapter = new FakeAgentProtocolAdapter(); await using var f = new StandingRecoveryFixture(adapter); await f.SeedAsync();
        f.Harness.Provider.GetRequiredService<IOptionsMonitor<AgentRegistrySettings>>().CurrentValue.Definitions["fake"].Kind = "Raw";
        await using (var db = f.Db())
        {
            (await db.Agents.FindAsync(f.Agent.Id))!.Kind = AgentKind.Raw;
            if (!priorSupported) (await db.AgentSessions.FindAsync(f.B.Id))!.AgentKind = AgentKind.Raw;
            await db.SaveChangesAsync();
        }
        (await Should.ThrowAsync<ConflictException>(() => f.StartAsync(new(ResumeSessionId: f.B.Id)))).Code.ShouldBe("standing_resume_unsupported");
        if (priorSupported)
            (await Should.ThrowAsync<ConflictException>(() => f.StartAsync(new()))).Code.ShouldBe(StandingContinuityState.HeldCode);
        var accepted = await f.StartAsync(new(Fresh: priorSupported)); await f.IdleAsync();
        accepted.PersistentSessionId.ShouldNotBe(f.B.Id.ToString("D"));
        adapter.StartedArgs.ShouldNotContain("--resume");
        await using var verify = f.Db();
        (await verify.AgentSessions.FindAsync(f.B.Id))!.StandingAgentId.ShouldBe(f.Agent.Id);
        (await verify.AgentIncidents.CountAsync(i => i.AgentId == f.Agent.Id && i.Kind ==
            (priorSupported ? AgentIncidentKind.StandingFreshSelected : AgentIncidentKind.ResumeUnsupported))).ShouldBe(1);
    }

    [Test]
    [Arguments(false, false)] [Arguments(false, true)] [Arguments(true, false)] [Arguments(true, true)]
    public async Task Lost_pointer_preserves_supported_history_but_keeps_raw_only_compatibility(bool priorSupported, bool malformed)
    {
        var adapter = new FakeAgentProtocolAdapter(); await using var f = new StandingRecoveryFixture(adapter); await f.SeedAsync();
        f.Harness.Provider.GetRequiredService<IOptionsMonitor<AgentRegistrySettings>>().CurrentValue.Definitions["fake"].Kind = "Raw";
        await using (var db = f.Db())
        {
            var agent = (await db.Agents.FindAsync(f.Agent.Id))!;
            agent.Kind = AgentKind.Raw; agent.PersistentSessionId = malformed ? "malformed" : null;
            if (!priorSupported) await db.AgentSessions.Where(s => s.StandingAgentId == agent.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AgentKind, AgentKind.Raw));
            await db.SaveChangesAsync();
        }
        if (priorSupported)
        {
            (await Should.ThrowAsync<ConflictException>(() => f.StartAsync(new()))).Code.ShouldBe(StandingContinuityState.HeldCode);
            adapter.Started.ShouldBeFalse();
        }
        var accepted = await f.StartAsync(new(Fresh: priorSupported)); await f.IdleAsync();
        accepted.PersistentSessionId.ShouldNotBe(f.B.Id.ToString("D"));
        adapter.StartedArgs.ShouldNotContain("--resume");
        await using var verify = f.Db();
        (await verify.AgentSessions.CountAsync(s => s.StandingAgentId == f.Agent.Id)).ShouldBe(3);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Legacy_historical_owner_can_resume_after_pointer_moved(bool executionOnly)
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(adapter);
        await f.SeedAsync(legacy: true);
        if (executionOnly)
        {
            await using var db = f.Db();
            await db.AgentIncidents.Where(i => i.SessionId == f.A.Id).ExecuteDeleteAsync();
            var taskId = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId, AgentId = f.Agent.Id,
                AgentSessionId = f.A.Id, Title = "Historical execution", Goal = "Synthetic history",
                WorkingDirectory = f.Root, Status = AgentTaskStatus.Succeeded, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var history = await f.Harness.Control.GetSessionsAsync(f.Agent.Id, 25, null, default);
        history.Items.ShouldContain(s => s.Id == f.A.Id && s.OwnershipEvidence == "Legacy");
        await using (var db = f.Db()) (await db.AgentSessions.FindAsync(f.A.Id))!.StandingAgentId.ShouldBeNull();
        await f.StartAsync(new(ResumeSessionId: f.A.Id));
        await f.IdleAsync();
        adapter.Started.ShouldBeTrue();
        adapter.StartedArgs.ShouldContain("--resume");
        adapter.StartedArgs.ShouldContain(f.A.Id.ToString("D"));
        adapter.StartedArgs.ShouldNotContain("--session-id");
        await using var verify = f.Db();
        (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(f.A.Id.ToString("D"));
        (await verify.AgentSessions.FindAsync(f.A.Id))!.StandingAgentId.ShouldBe(f.Agent.Id);
        (await verify.AgentSessions.FindAsync(f.B.Id)).ShouldNotBeNull();
        (await verify.AgentSessions.FindAsync(f.A.Id))!.InteractiveLaunchCompletedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Foreign_conflicting_or_unproven_ownership_refuses_without_side_effects()
    {
        foreach (var shape in new[] { "foreign", "conflicting", "unproven" })
        {
            var adapter = new FakeAgentProtocolAdapter();
            await using var f = new StandingRecoveryFixture(adapter);
            await f.SeedAsync(legacy: true, held: true);
            await using (var db = f.Db())
            {
                var other = new Agent { Id = Guid.NewGuid(), Name = "Other", Slug = $"c466-{Guid.NewGuid():N}",
                    WorkingDirectory = f.Root, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                db.Agents.Add(other);
                if (shape == "foreign") (await db.AgentSessions.FindAsync(f.A.Id))!.StandingAgentId = other.Id;
                if (shape == "conflicting") db.AgentIncidents.Add(new AgentIncident { Id = Guid.NewGuid(), AgentId = other.Id,
                    SessionId = f.A.Id, Kind = AgentIncidentKind.Crash, Message = "Contradiction", CreatedAt = DateTime.UtcNow });
                if (shape == "unproven") await db.AgentIncidents.Where(i => i.SessionId == f.A.Id).ExecuteDeleteAsync();
                await db.SaveChangesAsync();
            }
            var error = await Should.ThrowAsync<ConflictException>(() => f.StartAsync(new(ResumeSessionId: f.A.Id)));
            error.Code.ShouldBe(shape == "foreign" ? "standing_resume_not_owned" : "standing_resume_owner_unproven");
            adapter.Started.ShouldBeFalse();
            await using var verify = f.Db();
            (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(f.B.Id.ToString("D"));
            (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!.ContinuityHeldAt.ShouldNotBeNull();
            (await verify.AgentSessions.FindAsync(f.A.Id))!.StartedAt.ShouldBe(f.A.StartedAt, TimeSpan.FromMilliseconds(1));
        }
    }

    [Test]
    public async Task Recovery_options_cannot_bypass_existing_start_guards()
    {
        foreach (var option in new[] { "retry", "selection", "fresh" })
        foreach (var guard in new[] { "configuration", "herdr", "specialist", "model", "quota" })
        foreach (var alwaysOn in new[] { false, true })
        {
            var adapter = new FakeAgentProtocolAdapter();
            var modelHoldId = Guid.NewGuid();
            await using var f = new StandingRecoveryFixture(s =>
            {
                s.AddScoped<ModelAvailability>(); s.AddScoped<SubscriptionUsageReader>(); s.AddScoped<SubscriptionQuotaGate>();
                s.AddSingleton(Options.Create(new SubscriptionQuotaGateSettings()));
            }, adapter);
            await f.SeedAsync(held: true);
            await using (var db = f.Db())
            {
                var agent = (await db.Agents.FindAsync(f.Agent.Id))!; agent.AlwaysOn = alwaysOn;
                var state = (await db.AgentSupervisionStates.FindAsync(f.Agent.Id))!;
                state.Suspended = true; state.LivenessLatchedAt = DateTime.UtcNow;
                if (guard == "configuration") agent.WorkingDirectory = Path.Combine(f.Root, "missing");
                if (guard == "herdr") state.HerdrFailureHeldAt = DateTime.UtcNow;
                if (guard == "specialist")
                {
                    agent.StandingSpecialistRole = StandingSpecialistSeatPolicy.Role;
                    f.Harness.Provider.GetRequiredService<IOptions<DelegationSettings>>().Value.CheckEnabled = false;
                }
                if (guard == "model")
                {
                    agent.ModelId = "fable";
                    db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold { Id = modelHoldId, Kind = AgentKind.ClaudeCode,
                        ModelAlias = agent.ModelId, Source = ModelAvailabilitySource.AutoDetected, DisabledUntil = DateTime.UtcNow.AddHours(1),
                        HitAt = DateTime.UtcNow, Reason = "synthetic unavailable model" });
                }
                if (guard == "quota")
                {
                    var profile = new AgentTuiProfile { Id = Guid.NewGuid(), DisplayName = $"Synthetic quota {Guid.NewGuid():N}",
                        Kind = AgentKind.Codex, IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                    db.AgentTuiProfiles.Add(profile); agent.TuiProfileId = profile.Id;
                    db.SubscriptionUsageSamples.Add(new SubscriptionUsageSample { Id = Guid.NewGuid(), Provider = AgentKind.Codex,
                        SubscriptionKey = profile.Id.ToString("D"), RemainingPercent = 3, ResetsAt = DateTime.UtcNow.AddHours(36),
                        ObservedAt = DateTime.UtcNow, AgentSessionId = Guid.NewGuid(), SourceCommand = "/status",
                        ParseStatus = SubscriptionUsageParseStatus.Parsed, RawExcerpt = "synthetic fixture" });
                }
                await db.SaveChangesAsync();
            }
            var request = option == "retry" ? new StartAgentRequest(RetryContinuity: true)
                : option == "selection" ? new(ResumeSessionId: f.A.Id) : new(Fresh: true);
            try
            {
                var error = await Should.ThrowAsync<HttpException>(() => f.StartAsync(request), $"{guard}/{option}/AlwaysOn={alwaysOn}");
                if (guard != "configuration") error.Code.ShouldBe(guard switch { "herdr" => HerdrSupervisionStateService.HeldCode,
                    "specialist" => "specialist_start_refused", "model" => "model_disabled", _ => "subscription_quota_low" });
                adapter.Started.ShouldBeFalse(); adapter.Killed.ShouldBeFalse();
                await using var verify = f.Db();
                var preserved = (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!;
                preserved.ContinuityHeldAt.ShouldNotBeNull(); preserved.Suspended.ShouldBeTrue(); preserved.LivenessLatchedAt.ShouldNotBeNull();
                if (guard == "herdr") preserved.HerdrFailureHeldAt.ShouldNotBeNull();
                (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(f.B.Id.ToString("D"));
                }
            finally
            {
                await using var cleanup = f.Db();
                await cleanup.ModelAvailabilityHolds.Where(h => h.Id == modelHoldId).ExecuteDeleteAsync();
            }
        }
    }

    [Test]
    public async Task Automatic_and_capacity_recovery_cannot_acknowledge_continuity_decisions()
    {
        foreach (var capacity in new[] { false, true })
        foreach (var decision in new[] { "retry", "selection", "fresh" })
        {
            await using var f = new StandingRecoveryFixture(new FakeAgentProtocolAdapter()); await f.SeedAsync(held: true);
            var request = new StartAgentRequest(Fresh: decision == "fresh", ResumeSessionId: decision == "selection" ? f.A.Id : null,
                RetryContinuity: decision == "retry", CapacityRecovery: capacity);
            await using var scope = f.Harness.Provider.CreateAsyncScope();
            (await Should.ThrowAsync<ConflictException>(() => scope.ServiceProvider.GetRequiredService<AgentControlService>()
                .StartAsync(f.Agent.Id, request, default, automatic: !capacity))).Code.ShouldBe("standing_recovery_operator_required");
            await using var verify = f.Db();
            (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!.ContinuityHeldAt.ShouldNotBeNull();
        }
    }

    [Test]
    public async Task Invalid_or_busy_targets_refuse_before_reservation()
    {
        await using var f = new StandingRecoveryFixture(new FakeAgentProtocolAdapter());
        await f.SeedAsync();
        await Should.ThrowAsync<ValidationException>(() => f.StartAsync(new(Fresh: true, ResumeSessionId: f.A.Id)));
        await Should.ThrowAsync<ValidationException>(() => f.StartAsync(new(Fresh: true, RetryContinuity: true)));
        await Should.ThrowAsync<ValidationException>(() => f.StartAsync(new(ResumeSessionId: f.A.Id, RetryContinuity: true)));
        await Should.ThrowAsync<NotFoundException>(() => f.StartAsync(new(ResumeSessionId: Guid.NewGuid())));
        await using var db = f.Db();
        (await db.AgentSessions.FindAsync(f.A.Id))!.Status = SessionStatus.Starting;
        await db.SaveChangesAsync();
        (await Should.ThrowAsync<ConflictException>(() => f.StartAsync(new(ResumeSessionId: f.A.Id))))
            .Code.ShouldBe("standing_resume_target_active");
    }
}
