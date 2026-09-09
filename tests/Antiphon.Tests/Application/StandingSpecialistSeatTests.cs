using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class StandingSpecialistSeatTests
{
    private static async Task<Agent> OwnerAsync(BridgeQueueHarness h, AppDbContext db, DelegationSettings settings)
    {
        var owner = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
        owner.Kind = AgentKind.ClaudeCode;
        owner.ModelLevel = AgentModelLevel.Low;
        owner.Slug = settings.CheckInterpreterAgentSlug;
        owner.StandingSpecialistOwnerId = owner.Id;
        owner.StandingSpecialistRole = AgentTaskRole.Check;
        await db.SaveChangesAsync();
        return owner;
    }

    private static StandingSpecialistSeatService Service(BridgeQueueHarness h, AppDbContext db, DelegationSettings settings) =>
        new(db, Options.Create(settings), TimeProvider.System, h.Runtime,
            h.Scope.ServiceProvider.GetRequiredService<AgentSessionService>());

    [Test]
    public async Task Card0415_V25_compatibility_adopts_typed_primary_without_changing_exact_identity()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var settings = new DelegationSettings { CheckInterpreterAgentSlug = "primary-" + Guid.NewGuid().ToString("N") };
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString, Delegation = settings });
        await using var scope = h.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await OwnerAsync(h, db, settings);
        owner.ModelId = "claude-opus-4-6";
        owner.StandingSpecialistOwnerId = null;
        owner.StandingSpecialistRole = null;
        await db.SaveChangesAsync();
        var cwd = owner.WorkingDirectory;
        var session = owner.PersistentSessionId;
        await Service(h, db, settings).ReconcileOwnerAsync(owner.Id, CancellationToken.None);
        await Service(h, db, settings).ReconcileOwnerAsync(owner.Id, CancellationToken.None);
        owner.StandingSpecialistOwnerId.ShouldBe(owner.Id);
        owner.ModelId.ShouldBe("claude-opus-4-6");
        owner.WorkingDirectory.ShouldBe(cwd);
        owner.PersistentSessionId.ShouldBe(session);
        (await db.StandingSpecialistRoutings.CountAsync()).ShouldBe(0);
        var state = await db.StandingSpecialistCandidateStates.SingleAsync();
        state.PhysicalAgentId.ShouldBe(owner.Id);
        state.QualifiedAt.ShouldBeNull();
        state.Fingerprint.ShouldBeNull();
        state.CapabilityFingerprint.ShouldBeNull();
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task Card0415_V13_declared_alternates_have_stable_separate_seats_without_inherited_profiles_or_qualification()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var settings = new DelegationSettings { CheckInterpreterAgentSlug = "primary-" + Guid.NewGuid().ToString("N") };
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString, Delegation = settings });
        await using var scope = h.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await OwnerAsync(h, db, settings);
        var routing = new StandingSpecialistRoutingService(db, Options.Create(settings), TimeProvider.System, h.EventBus, NullLogger<StandingSpecialistRoutingService>.Instance);
        var saved = await routing.PutAsync(owner.Id, new(null, true,
            [new(AgentKind.ClaudeCode, AgentModelLevel.Low), new(AgentKind.ClaudeCode, AgentModelLevel.Medium), new(AgentKind.Codex, AgentModelLevel.Low)]), CancellationToken.None);
        var service = Service(h, db, settings);
        await service.ReconcileOwnerAsync(owner.Id, CancellationToken.None);
        db.ChangeTracker.Clear();
        var alternate = await db.Agents.SingleAsync(a => a.StandingSpecialistOwnerId == owner.Id && a.Id != owner.Id);
        var candidate = await db.StandingSpecialistCandidateStates.SingleAsync(c => c.PhysicalAgentId == alternate.Id);
        alternate.AlwaysOn.ShouldBeTrue();
        alternate.IsPoolDelegate.ShouldBeFalse();
        alternate.TuiProfileId.ShouldBeNull();
        alternate.LaunchEnvJson.ShouldBeNull();
        alternate.BoardId.ShouldBeNull();
        alternate.RemoteControlEnabled.ShouldBeFalse();
        alternate.WorkingDirectory.ShouldNotBe(owner.WorkingDirectory);
        candidate.Status.ShouldBe(StandingSpecialistCandidateStatus.DeclaredButUnprovisioned);
        candidate.UnprovisionedAt.ShouldNotBeNull();
        candidate.QualifiedAt.ShouldBeNull();
        (await StandingSpecialistSeatPolicy.StartRefusalAsync(db, alternate, settings, true, CancellationToken.None))!.ShouldContain("no certified");
        var codex = await db.StandingSpecialistCandidateStates.SingleAsync(c => c.AgentKind == AgentKind.Codex);
        codex.Status.ShouldBe(StandingSpecialistCandidateStatus.PendingDependency);
        codex.PhysicalAgentId.ShouldBeNull();
        await routing.PutAsync(owner.Id, new(saved.ConcurrencyToken, true,
            [new(AgentKind.ClaudeCode, AgentModelLevel.Low), new(AgentKind.Codex, AgentModelLevel.Low), new(AgentKind.ClaudeCode, AgentModelLevel.Medium)]), CancellationToken.None);
        await service.ReconcileOwnerAsync(owner.Id, CancellationToken.None);
        (await db.Agents.CountAsync(a => a.StandingSpecialistOwnerId == owner.Id)).ShouldBe(2);
        (await db.StandingSpecialistCandidateStates.AsNoTracking().SingleAsync(c => c.Id == candidate.Id)).PhysicalAgentId.ShouldBe(alternate.Id);
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    [Arguments("feature")]
    [Arguments("routing")]
    [Arguments("suspended")]
    [Arguments("liveness")]
    public async Task Card0415_V22_start_intent_respects_current_owner_barriers(string barrier)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var settings = new DelegationSettings { CheckInterpreterAgentSlug = "primary-" + Guid.NewGuid().ToString("N") };
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString, Delegation = settings });
        await using var scope = h.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await OwnerAsync(h, db, settings);
        (await StandingSpecialistSeatPolicy.StartRefusalAsync(db, owner, settings, true, CancellationToken.None)).ShouldBeNull();
        if (barrier == "feature") settings.CheckInterpreterEnabled = false;
        else if (barrier == "routing") db.StandingSpecialistRoutings.Add(new() { Id = Guid.NewGuid(), AgentId = owner.Id, Enabled = false });
        else db.AgentSupervisionStates.Add(new() { AgentId = owner.Id,
            Suspended = barrier == "suspended", LivenessLatchedAt = barrier == "liveness" ? DateTime.UtcNow : null });
        await db.SaveChangesAsync();
        (await StandingSpecialistSeatPolicy.StartRefusalAsync(db, owner, settings, true, CancellationToken.None)).ShouldNotBeNull();
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public void Card0415_V02_typed_Check_floor_contains_no_workspace_reading_ritual()
    {
        var owner = new Agent { Id = Guid.NewGuid(), Name = "renamed interpreter", StandingSpecialistRole = AgentTaskRole.Check };
        owner.StandingSpecialistOwnerId = owner.Id;
        var floor = AgentWorkspaceProvisioner.Render(owner, Path.GetTempPath());
        floor.ShouldContain(CheckInterpretation.Contract);
        floor.ShouldNotContain("AGENTS.md");
        floor.ShouldNotContain("Read before");
    }
}
