using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0082 S2 — the three Agent columns round-trip through create/update DTOs.</summary>
[Category("Integration")]
public class ContextCompactionAgentTests
{
    [Test]
    public void a_create_request_defaults_the_overrides_to_null()
    {
        var omitted = new CreateAgentRequest("A", "C:\\tmp");
        omitted.AutoCompactEnabled.ShouldBeNull();
        omitted.AutoCompactIdleMinutes.ShouldBeNull();
        omitted.AutoCompactContextPercent.ShouldBeNull();
    }

    [Test]
    [Category("Integration")]
    public async Task CreateAsync_persists_null_overrides_so_the_agent_uses_the_global_default()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var agentName = UniqueAgentName("Default Compact");

        var created = await service.CreateAsync(
            new CreateAgentRequest(agentName, "D:/src/app"),
            CancellationToken.None);

        created.AutoCompactEnabled.ShouldBeNull();
        created.AutoCompactIdleMinutes.ShouldBeNull();
        created.AutoCompactContextPercent.ShouldBeNull();

        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.AutoCompactEnabled.ShouldBeNull();
        stored.AutoCompactIdleMinutes.ShouldBeNull();
        stored.AutoCompactContextPercent.ShouldBeNull();
    }

    [Test]
    [Category("Integration")]
    public async Task UpdateAsync_persists_overrides_and_null_clears_them_back_to_the_global_default()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var created = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Override Compact"), "D:/src/app"),
            CancellationToken.None);

        var withOverrides = await service.UpdateAsync(
            created.Id,
            new UpdateAgentRequest(
                created.Name,
                created.WorkingDirectory,
                created.Details,
                created.DefaultWorkflowTemplateId,
                created.AssignmentPolicy,
                created.BoardId,
                AutoCompactEnabled: false,
                AutoCompactIdleMinutes: 60,
                AutoCompactContextPercent: 80),
            CancellationToken.None);

        withOverrides.AutoCompactEnabled.ShouldBe(false);
        withOverrides.AutoCompactIdleMinutes.ShouldBe(60);
        withOverrides.AutoCompactContextPercent.ShouldBe(80);

        var cleared = await service.UpdateAsync(
            created.Id,
            new UpdateAgentRequest(
                created.Name,
                created.WorkingDirectory,
                created.Details,
                created.DefaultWorkflowTemplateId,
                created.AssignmentPolicy,
                created.BoardId,
                AutoCompactEnabled: null,
                AutoCompactIdleMinutes: null,
                AutoCompactContextPercent: null),
            CancellationToken.None);

        cleared.AutoCompactEnabled.ShouldBeNull();
        cleared.AutoCompactIdleMinutes.ShouldBeNull();
        cleared.AutoCompactContextPercent.ShouldBeNull();

        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.AutoCompactEnabled.ShouldBeNull();
        stored.AutoCompactIdleMinutes.ShouldBeNull();
        stored.AutoCompactContextPercent.ShouldBeNull();
    }

    private static AgentService CreateService(AppDbContext db, IEventBus eventBus) =>
        new(
            db,
            new CardWorkflowRunFactory(db, TimeProvider.System),
            eventBus,
            TimeProvider.System,
            new NoOpDirectoryWriter(),
            NullLogger<AgentService>.Instance);

    private sealed class NoOpDirectoryWriter : IDirectoryWriter
    {
        public void CreateDirectory(string path) { }
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string UniqueAgentName(string prefix) => $"{prefix} {Guid.NewGuid():N}";
}
