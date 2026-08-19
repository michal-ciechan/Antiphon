using System.Reflection;
using Antiphon.Tests.AgentTui;
using Antiphon.Tests.Agents;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

/// <summary>CARD-0050 S5: process-spawning classes share a 1-wide lane.</summary>
[Category("Unit")]
public class ProcessSpawnLimitTests
{
    [Test]
    public void Caps_concurrent_process_spawning_tests_at_one()
    {
        new ProcessSpawnLimit().Limit.ShouldBe(1);
    }

    [Test]
    public void Process_spawning_classes_carry_the_limiter()
    {
        Type[] types =
        [
            typeof(SessionRunnerRuntimeTests),
            typeof(RawPtyAdapterTests),
            typeof(CodexAdapterLocalShellTests),
            typeof(ClaudeAdapterLocalShellTests),
            typeof(CodexAdapterIntegrationTests),
            typeof(ClaudeAdapterIntegrationTests),
            typeof(RunnerProcessProbeTests),
            typeof(AgentSessionServiceIntegrationTests),
            typeof(SessionMessageQueuePtyIntegrationTests),
            typeof(SessionMessageQueueGrokPtyIntegrationTests),
            typeof(DelegationBriefCeilingPtyTests),
            typeof(GrokDelegateEndToEndTests),
            typeof(SlashCommandMenuReconciliationTests),
        ];

        foreach (var type in types)
        {
            Attribute.GetCustomAttribute(type, typeof(ParallelLimiterAttribute<ProcessSpawnLimit>))
                .ShouldNotBeNull($"{type.Name} must carry [ParallelLimiter<ProcessSpawnLimit>]");
        }
    }

    [Test]
    public void Live_runner_send_input_method_carries_the_limiter()
    {
        var method = typeof(AgentSessionRuntimeTests).GetMethod(
            nameof(AgentSessionRuntimeTests.Backend_runtime_can_send_input_to_live_runner_session_after_restart),
            BindingFlags.Instance | BindingFlags.Public);
        method.ShouldNotBeNull();
        Attribute.GetCustomAttribute(method, typeof(ParallelLimiterAttribute<ProcessSpawnLimit>))
            .ShouldNotBeNull("the one method that starts a pty-host must carry the limiter; the rest of the class is fakes");
    }
}
