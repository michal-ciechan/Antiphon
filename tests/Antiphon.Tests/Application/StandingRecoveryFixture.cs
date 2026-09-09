using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Tests.Application;

internal sealed class StandingRecoveryFixture : IAsyncDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"c466-{Guid.NewGuid():N}");
    public AgentControlServiceIntegrationTests.Harness Harness { get; }
    public Agent Agent { get; private set; } = null!;
    public AgentSession A { get; private set; } = null!;
    public AgentSession B { get; private set; } = null!;
    public StandingRecoveryFixture(params FakeAgentProtocolAdapter[] adapters) : this(null, adapters) { }
    public StandingRecoveryFixture(Action<IServiceCollection>? configureServices, params FakeAgentProtocolAdapter[] adapters)
    {
        Directory.CreateDirectory(Root);
        Harness = AgentControlServiceIntegrationTests.BuildHarness(Root, adapters, defaultKind: "ClaudeCode", configureServices: configureServices);
    }
    public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions());
    public async Task SeedAsync(bool legacy = false, bool held = false)
    {
        await using var db = Db();
        var now = DateTime.UtcNow;
        Agent = new Agent { Id = Guid.NewGuid(), Name = "Standing recovery", Slug = $"c466-{Guid.NewGuid():N}",
            WorkingDirectory = Root, Kind = AgentKind.ClaudeCode, CreatedAt = now, UpdatedAt = now, Status = AgentStatus.Stopped };
        A = Session(now.AddHours(-2), legacy ? null : Agent.Id);
        B = Session(now.AddHours(-1), Agent.Id);
        Agent.PersistentSessionId = B.Id.ToString("D");
        db.Agents.Add(Agent); db.AgentSessions.AddRange(A, B);
        if (legacy) db.AgentIncidents.Add(new AgentIncident { Id = Guid.NewGuid(), AgentId = Agent.Id, SessionId = A.Id,
            Kind = AgentIncidentKind.Recovered, Message = "Historical lifecycle", CreatedAt = now.AddHours(-1), Severity = AlertSeverity.Info });
        await db.SaveChangesAsync();
        if (held) await new StandingContinuityState(db, TimeProvider.System).HoldAsync(Agent.Id, B.Id, StandingContinuityReason.NativeSessionMissing, default);
    }
    private AgentSession Session(DateTime now, Guid? owner) => new() { Id = Guid.NewGuid(), StandingAgentId = owner,
        Cwd = Root, AgentKind = AgentKind.ClaudeCode, DefinitionName = "fake", Status = SessionStatus.Stopped,
        CreatedAt = now, StartedAt = now, LastSeenAt = now, EndedAt = now.AddMinutes(1) };
    public async Task<AgentDetailDto> StartAsync(StartAgentRequest request)
    {
        await using var scope = Harness.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AgentControlService>().StartAsync(Agent.Id, request, default);
    }
    public Task IdleAsync() => Harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
    public async ValueTask DisposeAsync()
    {
        await IdleAsync();
        await Harness.DisposeAsync();
        await AgentSupervisionTests.CleanupAsync(Root);
    }
}
