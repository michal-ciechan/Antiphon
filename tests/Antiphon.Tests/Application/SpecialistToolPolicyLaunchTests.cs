using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class SpecialistToolPolicyLaunchTests
{
    [Test]
    public Task Card0415_V03_launch_refuses_failed_arming() =>
        Card0415_V03_actual_Check_start_requires_effective_provider_policy(
            AgentKind.ClaudeCode, true, "specialist_tool_policy_unavailable");

    [Test]
    [Arguments(AgentKind.ClaudeCode, false, null)]
    [Arguments(AgentKind.ClaudeCode, true, "specialist_tool_policy_unavailable")]
    [Arguments(AgentKind.Codex, false, "specialist_tool_policy_pending_dependency")]
    public async Task Card0415_V03_actual_Check_start_requires_effective_provider_policy(
        AgentKind effectiveKind, bool obstructPolicy, string? expectedCode)
    {
        var settings = new DelegationSettings { CheckInterpreterAgentSlug = $"c415-{Guid.NewGuid():N}" };
        await using var harness = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = true,
            Delegation = settings,
            ConfigureServices = services =>
            {
                services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                    new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new()
                    {
                        DefaultDefinition = "fake",
                        Definitions = { ["fake"] = new AgentDefinition
                        {
                            Kind = effectiveKind.ToString(),
                            Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                        } },
                    }));
                services.AddSingleton<IAgentProtocolAdapterFactory>(sp =>
                    new RecordingFactory(sp.GetRequiredService<AgentSessionRuntime>()));
            },
        });
        string cwd;
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            var agent = await db.Agents.SingleAsync(a => a.Id == harness.AgentId);
            agent.Slug = settings.CheckInterpreterAgentSlug;
            agent.SystemPromptAppend = CheckInterpretation.Contract;
            cwd = agent.WorkingDirectory;
            (await db.AgentSessions.SingleAsync(s => s.Id == harness.SessionId)).Status = SessionStatus.Failed;
            await db.SaveChangesAsync();
        }
        if (obstructPolicy) File.WriteAllText(Path.Combine(cwd, ".claude"), "blocked directory");
        using var scope = harness.Provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentControlService>();
        var factory = (RecordingFactory)harness.Provider.GetRequiredService<IAgentProtocolAdapterFactory>();

        if (expectedCode is not null)
        {
            var failure = await Should.ThrowAsync<ConflictException>(() => service.StartAsync(
                harness.AgentId, new StartAgentRequest(RemoteControl: false, Fresh: true), CancellationToken.None));
            failure.Code.ShouldBe(expectedCode);
            factory.Created.ShouldBeEmpty("refusal must precede launching a provider");
            await using var verify = BridgeQueueHarness.CreateContext();
            (await verify.AgentSessions.CountAsync(s => s.Cwd == cwd)).ShouldBe(1);
            return;
        }

        await service.StartAsync(harness.AgentId, new StartAgentRequest(RemoteControl: false, Fresh: true), CancellationToken.None);
        await harness.Provider.GetRequiredService<AgentSessionLaunchQueue>()
            .WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        File.ReadAllText(Path.Combine(cwd, ".claude", "settings.json"))
            .ShouldBe(CheckInterpretation.DenyAllToolsSettingsJson);
        var adapter = factory.Created.ShouldHaveSingleItem();
        adapter.StartedArgs.ShouldContain("--append-system-prompt");
        adapter.StartedArgs.ShouldContain(a => a.Contains(CheckInterpretation.Contract));
        adapter.SubmittedBodies.ShouldBeEmpty();
    }

    private sealed class RecordingFactory(AgentSessionRuntime runtime) : IAgentProtocolAdapterFactory
    {
        public List<FakeAgentProtocolAdapter> Created { get; } = [];
        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            var adapter = new FakeAgentProtocolAdapter { RegisterOnStart = runtime };
            Created.Add(adapter);
            return adapter;
        }
    }
}
