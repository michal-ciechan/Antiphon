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

// The database-backed half of the S-A default setting coverage; the option and validator
// cases stay in the Unit class MatrixDefaultComplexitySettingsTests (CARD-1266 lane split).
[Category("Integration")]
public sealed class MatrixDefaultComplexityRoutingTests
{
    // S-A reverses S-0's seeded-cell expectation: only a resolved cell replaces RolePolicy.
    // Missing/null configuration still normalizes to Medium, with no effect on an empty matrix.
    [Test]
    [Arguments(false, null, false, AgentKind.ClaudeCode, AgentModelLevel.Frontier, null, "(role policy)")]
    [Arguments(true, null, false, AgentKind.ClaudeCode, AgentModelLevel.Frontier, null, "(role policy)")]
    [Arguments(true, "Medium", false, AgentKind.ClaudeCode, AgentModelLevel.Frontier, null, "(role policy)")]
    [Arguments(false, null, true, AgentKind.Codex, AgentModelLevel.Low, TaskComplexity.Medium, "(matrix default Medium)")]
    [Arguments(true, null, true, AgentKind.Codex, AgentModelLevel.Low, TaskComplexity.Medium, "(matrix default Medium)")]
    [Arguments(true, "Medium", true, AgentKind.Codex, AgentModelLevel.Low, TaskComplexity.Medium, "(matrix default Medium)")]
    public async Task The_default_setting_changes_routing_only_when_a_cell_resolves(bool present, string? value,
        bool withCell, AgentKind expectedKind, AgentModelLevel expectedLevel, TaskComplexity? expectedComplexity, string expectedDetail)
    {
        using var services = MatrixDefaultComplexitySettingsTests.Bind(present, value);
        var settings = services.GetRequiredService<IOptions<DelegationSettings>>().Value;
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var workspace = Directory.CreateTempSubdirectory("antiphon-matrix-default-settings");
        try
        {
            settings.AllowedRoots = [workspace.FullName];
            if (withCell) db.ComplexityChains.Add(new ComplexityChain
            {
                Id = Guid.NewGuid(),
                Role = AgentTaskRole.Plan,
                Complexity = TaskComplexity.Medium,
                CandidatesJson = ComplexityChain.SerializeCandidates([new(AgentKind.Codex, AgentModelLevel.Low)]),
                Provenance = RoutingPinProvenance.Human,
                Reason = "S-A uses the default-tier cell when one resolves",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            var options = Options.Create(settings);
            var availability = new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
            var routing = new ComplexityRoutingService(db, options, TimeProvider.System, availability);
            var service = new AgentTaskService(
                db,
                new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                options, new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System,
                NullLogger<AgentTaskService>.Instance,
                modelAvailability: availability,
                routingPins: new RoutingPinService(db, TimeProvider.System, NullLogger<RoutingPinService>.Instance),
                complexityRouting: routing);

            var created = await service.CreateAsync(
                new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Workspace: WorkspaceMode.Shared),
                new AgentTaskService.Caller(null, null, workspace.FullName), CancellationToken.None);

            created.Status.ShouldBe(AgentTaskStatus.Queued);
            created.AgentKind.ShouldBe(expectedKind);
            created.ModelLevel.ShouldBe(expectedLevel);
            created.Complexity.ShouldBe(expectedComplexity);
            if (withCell) created.Routing.ShouldNotBeNull();
            else created.Routing.ShouldBeNull();
            db.ChangeTracker.Clear();
            var saved = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
            saved.Status.ShouldBe(AgentTaskStatus.Queued);
            saved.AgentKind.ShouldBe(expectedKind);
            saved.ModelLevel.ShouldBe(expectedLevel);
            saved.Complexity.ShouldBe(expectedComplexity);
            saved.RoutingPinId.ShouldBeNull();
            var createdEvent = await db.AgentTaskEvents.SingleAsync(
                e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Created);
            createdEvent.Detail.ShouldContain(expectedDetail);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }
}
