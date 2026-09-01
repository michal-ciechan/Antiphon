using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

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
            typeof(PtyAgentRunnerTests),
            typeof(FakeClaudeContractTests),
            typeof(FakeGrokContractTests),
            typeof(ClaudeSubmitContractTests),
            typeof(PtyInputChunkingTests),
            typeof(PtyLargeWriteTests),
            typeof(PtyBackendContractTests),
            typeof(PtyBracketedPasteContractTests),
            typeof(ModernPtyDa1Tests),
            typeof(ClaudeVerifiedDeliveryTests),
            typeof(ClaudeInteractionTests),
            typeof(ClaudeHeadedTests),
            typeof(ClaudeHeadedLongTests),
            typeof(ClaudeDangerousTests),
            typeof(ClaudeComposerRenderCanaryTests),
            typeof(ClaudeComposerCaptureProbeTests),
            typeof(ClaudeCompactionCanaryTests),
            typeof(ClaudeAppendSystemPromptCanaryTests),
            typeof(PtyPasteMarkerExperiments),
            typeof(PtyInputLossExperiments),
            typeof(ClaudeSignalCanaryTests),
            typeof(GrokCanaryTests),
            typeof(GrokSubmitWhileWorkingCanaryTests),
            typeof(GrokQuestionPopupCanaryTests),
            typeof(ClaudePasteLossCanaryTests),
            typeof(FakeVsRealClipParityTests),
            typeof(ClaudeLocalCommandCanaryTests),
            typeof(ClaudeInterruptCanaryTests),
            typeof(ClaudeTrustPromptCanaryTests),
            typeof(ClaudeRemoteControlMenuCanaryTests),
            typeof(ClaudeSubmitConfirmCanaryTests),
            typeof(ClaudeOverlayCanaryTests),
            typeof(CodexOverlayCanaryTests),
            typeof(GrokUsageOverlayCanaryTests),
            typeof(ClaudeTuiModeTests),
            typeof(PtyStressTests),
            typeof(ClaudeDetectorsTests),
        ];

        foreach (var type in types)
        {
            Attribute.GetCustomAttribute(type, typeof(ParallelLimiterAttribute<ProcessSpawnLimit>))
                .ShouldNotBeNull($"{type.Name} must carry [ParallelLimiter<ProcessSpawnLimit>]");
        }
    }
}
