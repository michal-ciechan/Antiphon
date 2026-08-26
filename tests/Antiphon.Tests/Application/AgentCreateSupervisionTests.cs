using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0008 — AlwaysOn / RemoteControlEnabled are part of create, not a follow-up PATCH.
/// </summary>
public class AgentCreateSupervisionTests
{
    [Test]
    public void a_create_request_defaults_supervision_flags_off_and_can_ask_for_them()
    {
        // Older callers omit the properties; the record defaults match the entity defaults.
        var omitted = new CreateAgentRequest("A", "C:\\tmp");
        omitted.AlwaysOn.ShouldBeFalse();
        omitted.RemoteControlEnabled.ShouldBeFalse();

        var asked = new CreateAgentRequest("A", "C:\\tmp", AlwaysOn: true, RemoteControlEnabled: true);
        asked.AlwaysOn.ShouldBeTrue();
        asked.RemoteControlEnabled.ShouldBeTrue();

        // CARD-0210 added BoardId; CARD-0032 added SystemPromptAppend and BundleKeys so a
        // standing orchestrator is born with its contract. They default to null / omitted.
        omitted.BoardId.ShouldBeNull();
        omitted.SystemPromptAppend.ShouldBeNull();
        omitted.BundleKeys.ShouldBeNull();
    }

    [Test]
    [Category("Integration")]
    public async Task CreateAsync_persists_create_time_always_on_and_remote_control()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var agentName = UniqueAgentName("Supervised At Birth");

        var created = await service.CreateAsync(
            new CreateAgentRequest(agentName, "D:/src/app", AlwaysOn: true, RemoteControlEnabled: true),
            CancellationToken.None);

        created.AlwaysOn.ShouldBeTrue();
        created.RemoteControlEnabled.ShouldBeTrue();

        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.AlwaysOn.ShouldBeTrue();
        stored.RemoteControlEnabled.ShouldBeTrue();
    }

    [Test]
    [Category("Integration")]
    public async Task CreateAsync_defaults_always_on_and_remote_control_to_false()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var agentName = UniqueAgentName("Unsupervised At Birth");

        var created = await service.CreateAsync(
            new CreateAgentRequest(agentName, "D:/src/app"),
            CancellationToken.None);

        created.AlwaysOn.ShouldBeFalse();
        created.RemoteControlEnabled.ShouldBeFalse();

        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.AlwaysOn.ShouldBeFalse();
        stored.RemoteControlEnabled.ShouldBeFalse();
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
