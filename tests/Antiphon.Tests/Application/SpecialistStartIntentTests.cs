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
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class SpecialistStartIntentTests
{
    private sealed class Factory : IAgentProtocolAdapterFactory
    {
        public FakeAgentProtocolAdapter Adapter { get; } = new();
        public int Creates { get; private set; }
        public IAgentProtocolAdapter Create(AgentKind kind) { Creates++; return Adapter; }
    }

    [Test]
    [Arguments("before")]
    [Arguments("during-ready")]
    [Arguments("authorized")]
    public Task Card0415_V22_human_stop_wins_against_queued_and_inflight_Check_launch(string stop) => ExerciseAsync(stop);

    [Test]
    public Task Card0415_V20_missing_native_Check_resume_never_replays_a_fresh_launch() => ExerciseAsync("missing-resume");

    private static async Task ExerciseAsync(string stop)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var factory = new Factory();
        var settings = new DelegationSettings();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString,
            Delegation = settings, ConfigureServices = services =>
            {
                services.AddSingleton<IAgentProtocolAdapterFactory>(factory);
                services.AddSingleton<ISessionRunnerClient>(new FakeSessionRunnerClient());
            } });
        factory.Adapter.RegisterOnStart = h.Runtime;
        var sessionId = Guid.NewGuid();
        string cwd;
        await using (var scope = h.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
            owner.StandingSpecialistOwnerId = owner.Id;
            owner.StandingSpecialistRole = AgentTaskRole.Check;
            owner.Kind = AgentKind.ClaudeCode;
            owner.PersistentSessionId = sessionId.ToString();
            owner.Status = AgentStatus.Running;
            cwd = owner.WorkingDirectory;
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new() { Id = sessionId, AgentKind = AgentKind.ClaudeCode, Cwd = cwd,
                Status = SessionStatus.Starting, CreatedAt = now, StartedAt = now, LastSeenAt = now });
            await db.SaveChangesAsync();
        }
        async Task StopAsync()
        {
            await using var scope = h.Provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentControlService>().StopAsync(h.AgentId, CancellationToken.None);
        }
        if (stop == "before") await StopAsync();
        if (stop == "during-ready") factory.Adapter.ReadyHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (stop == "missing-resume")
        {
            factory.Adapter.ReadyResult = false;
            factory.Adapter.StartupOutput = "No conversation found with session ID: " + sessionId;
        }
        await using var launchScope = h.Provider.CreateAsyncScope();
        var launch = launchScope.ServiceProvider.GetRequiredService<AgentSessionService>().LaunchInteractiveAsync(
            sessionId, h.AgentId, new("owned-test", AgentKind.ClaudeCode, "synthetic-no-process", [],
                new Dictionary<string, string>(), cwd, 120, 30), null, stop == "missing-resume", null, CancellationToken.None);
        if (stop == "during-ready")
        {
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(() => Task.FromResult(factory.Adapter.Started));
            await StopAsync();
            factory.Adapter.ReadyHold!.TrySetResult(true);
        }
        if (stop == "authorized") await launch;
        else if (stop == "missing-resume") await Should.ThrowAsync<AgentSessionService.ResumeTargetMissingException>(async () => await launch);
        else (await Should.ThrowAsync<ConflictException>(async () => await launch)).Code.ShouldBe("specialist_start_intent_revoked");
        await using var verifyScope = h.Provider.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
        session.Status.ShouldBe(stop == "authorized" ? SessionStatus.Running : stop == "missing-resume" ? SessionStatus.Failed : SessionStatus.Stopped);
        factory.Creates.ShouldBe(stop == "before" ? 0 : 1);
        factory.Adapter.Killed.ShouldBe(stop is "during-ready" or "missing-resume");
        factory.Adapter.Inputs.ShouldBeEmpty();
        if (stop is "before" or "during-ready")
        {
            (await verify.AgentSupervisionStates.SingleAsync(s => s.AgentId == h.AgentId)).Suspended.ShouldBeTrue();
            (await verify.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.SuspendedByUser)).ShouldBe(1);
            await using var autoScope = h.Provider.CreateAsyncScope();
            (await Should.ThrowAsync<ConflictException>(() => autoScope.ServiceProvider.GetRequiredService<AgentControlService>()
                .StartAsync(h.AgentId, new(Fresh: true), CancellationToken.None, automatic: true))).Code.ShouldBe("specialist_start_refused");
        }
    }
}
